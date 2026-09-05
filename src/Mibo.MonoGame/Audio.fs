namespace Mibo.Elmish

open System
open System.Collections.Generic
open System.IO
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Audio
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Media
open Mibo.Audio

// ─────────────────────────────────────────────────────────────────────────────
// MonoGame audio service.
//
// Mirrors the raylib audio service's shape, with one difference the platform
// forces: MonoGame can load sounds and music either through the content
// pipeline (MGCB assets) or as loose files, so the source is explicit and
// typed — the Source union — instead of a plain path. No fallback guessing.
//
// Why the service owns SoundEffectInstance handles instead of calling
// SoundEffect.Play(): Play() returns no handle, so per-play volume changes
// (the live music-slider pattern applied to SFX) and per-frame Apply3D for 3D
// audio would be impossible. The service creates 8 instances per sound and
// plays them round-robin, so a key can overlap itself up to 8 times before
// the oldest playback is stolen.
//
// Music is the single MediaPlayer channel (a MonoGame singleton): one track
// at a time, streaming handled by the platform, fades interpolated by Tick.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Where a registered sound or music track loads from. The choice is explicit
/// and typed — no fallback guessing from the file extension.
/// </summary>
/// <remarks>
/// <para>
/// <c>Pipeline</c> loads an MGCB-compiled asset through the game's
/// <c>ContentManager</c> — the guaranteed path: every format the pipeline
/// imports works, on every DesktopGL/WindowsDX backend.
/// </para>
/// <para>
/// <c>File</c> loads a loose file from disk at runtime. Sound effects support
/// WAV (RIFF) files only; music goes through <c>Song.FromUri</c>, which uses
/// the platform decoders (the file the path points to must ship with the
/// game — copy it to the output directory yourself).
/// </para>
/// </remarks>
type Source =
  /// <summary>An MGCB asset name, loaded with <c>content.Load</c> (no extension — e.g. <c>"audio/jump"</c>).</summary>
  | Pipeline of string
  /// <summary>A loose file path, relative to the working directory (e.g. <c>"music/overworld.ogg"</c>).</summary>
  | File of string

/// <summary>
/// Per-key playback entry: the loaded <see cref="T:Microsoft.Xna.Framework.Audio.SoundEffect"/>
/// plus its 8-instance ring played round-robin. The cursor always advances,
/// so the 9th playback of a key steals the slot of the oldest one.
/// </summary>
/// <remarks>
/// <para>
/// The instance slots are created lazily — slot k on the k-th play of the
/// key — because every created slot is a live XAudio2 <c>SourceVoice</c> on
/// WindowsDX (device limit: 512) and banks can hold many keys. A key that
/// never plays costs nothing; the cursor cycles in order, so the created
/// slots are always <c>0 .. Cursor</c>.
/// </para>
/// <para>
/// Each slot carries its own pooled <c>AudioEmitter</c> and a 3D flag,
/// allocated once at registration — <c>Play3D</c> never allocates. Arming a
/// slot always sets the flag (2D plays clear it, 3D plays set it), so the
/// per-frame re-attenuation sweep never applies a stale emitter over a fresh
/// playback, and a slot can never be tracked twice. The per-entry
/// <c>Has3D</c> flag turns the per-frame sweep into one bool read for keys
/// (and whole banks) that never play in 3D.
/// </para>
/// </remarks>
type private SoundBankEntry = {
  Effect: SoundEffect
  Instances: SoundEffectInstance[]
  Emitters: AudioEmitter[]
  SlotIs3D: bool[]
  mutable Cursor: int
  mutable Has3D: bool
}

/// <summary>The state of the single music channel, tracked alongside <c>MediaPlayer.State</c> so both backends apply the same seek rule.</summary>
type private MusicChannelState =
  /// <summary>No track selected, or stopped by StopMusic or a fade-out.</summary>
  | Stopped
  /// <summary>A track is playing.</summary>
  | Playing
  /// <summary>A track is parked at its position.</summary>
  | Paused

/// <summary>
/// The MonoGame-only 3D audio surface, extending <see cref="T:Mibo.Audio.IAudio"/>
/// the way the backend's <c>IAssets</c> extends <c>IAssetCache</c>. Registered
/// alongside <c>IAudio</c> by the MonoGame hosts; resolve it with
/// <c>GameContext.getService&lt;MonoGameAudio&gt; ctx</c>. The raylib backend
/// has no listener model, so 3D positioning is not part of the portable
/// contract — the Core <c>Attenuation2D</c> helper covers the portable case.
/// </summary>
type MonoGameAudio =
  inherit IAudio

  /// <summary>Sets where the listener hears from: the camera is the usual choice. Call again as it moves; playing 3D sounds re-attenuate on the next tick.</summary>
  /// <param name="position">Listener position in world units.</param>
  /// <param name="forward">Listener facing direction (unit length).</param>
  /// <param name="up">Listener up direction (unit length).</param>
  /// <param name="velocity">Listener velocity in world units per second (drives Doppler shift).</param>
  abstract SetListener:
    position: Vector3 * forward: Vector3 * up: Vector3 * velocity: Vector3 ->
      unit

  /// <summary>Plays a registered sound positioned at an emitter in 3D: distance attenuation, panning, and Doppler shift come from the listener/emitter geometry. Pan from the voice is ignored — geometry decides it.</summary>
  /// <param name="key">The game-vocabulary key the sound was registered under.</param>
  /// <param name="emitterPosition">Emitter position in world units.</param>
  /// <param name="emitterVelocity">Emitter velocity in world units per second (drives Doppler shift). Defaults to zero.</param>
  /// <param name="voice">Volume and pitch knobs; Volume scales the distance-attenuated result. Defaults to <c>Voice.center</c>.</param>
  abstract Play3D:
    key: string *
    emitterPosition: Vector3 *
    ?emitterVelocity: Vector3 *
    ?voice: Voice ->
      unit

  /// <summary>Scales world units to the distances at which 3D sounds attenuate. Wraps <c>SoundEffect.DistanceScale</c>.</summary>
  abstract DistanceScale: float32 with get, set

  /// <summary>Scales the Doppler effect strength for 3D sounds (0 = off). Wraps <c>SoundEffect.DopplerScale</c>.</summary>
  abstract DopplerScale: float32 with get, set

/// <summary>
/// The MonoGame-backed <see cref="T:Mibo.Audio.IAudio"/>: pipeline/loose-file
/// sound and music registration, an 8-instance ring per sound, the single
/// MediaPlayer music channel, 3D audio, and the fade state machine driven by
/// <see cref="M:Mibo.Audio.IAudio.Tick"/>.
/// </summary>
/// <remarks>
/// <para>
/// Register entries right after the host registers the service (the
/// <c>MonoGameProgram.withBank</c> builder does this for MVU programs) — the
/// assets load at that point, before user <c>init</c> runs. A missing
/// pipeline asset throws there, at startup, where a configuration mistake
/// belongs.
/// </para>
/// <para>
/// Fades interpolate the music channel volume through the Core
/// <see cref="M:Mibo.Audio.Fade.volume"/> helper. A fade to (or below) zero
/// stops the music when it completes. The volume a fade-in returns to is the
/// last one passed to <see cref="M:Mibo.Elmish.AudioService.SetMusicVolume(System.Single)"/>
/// (1.0 if never set). A newly started track cancels any running fade and
/// starts at that same volume — a fade-out ends the track that was playing,
/// it is not a sticky volume.
/// </para>
/// </remarks>
type AudioService(content: ContentManager) =

  let sounds = Dictionary<string, SoundBankEntry>()
  // Keys whose SoundEffect/Song came from a loose File — the service owns
  // those and disposes them; pipeline assets stay owned by the ContentManager.
  let fileOwned = HashSet<string>()

  let musics = Dictionary<string, Song>()

  let mutable disposed = false

  // The single music channel: the track that plays, and the volume state.
  let mutable hasMusic = false
  let mutable currentSong = Unchecked.defaultof<Song>
  // The channel state, tracked so seek and pause/resume honor
  // stopped-vs-paused identically to the raylib backend.
  let mutable musicState = MusicChannelState.Stopped
  // The last explicitly-set music volume (the slider); new tracks start here.
  let mutable musicVolume = 1.0f
  // The volume applied to the channel right now, before the master scale
  // (the fade may be en route).
  let mutable appliedVolume = 1.0f
  // Master volume scales the whole mix (the portable rule). MonoGame's
  // SoundEffect.MasterVolume covers the sound effects; the music channel is
  // scaled by hand on every application.
  let mutable masterVolume = 1.0f

  // Fade state machine, advanced by Tick.
  let mutable fadeActive = false
  let mutable fadeFrom = 1.0f
  let mutable fadeTo = 1.0f
  let mutable fadeElapsed = 0.0f
  let mutable fadeDuration = 0.0f
  let mutable fadeStopOnComplete = false

  // 3D audio: where the listener hears from. Live 3D plays are found through
  // the per-slot emitters and flags on each bank entry (see SoundBankEntry).
  let listener = AudioListener()

  let clamp01 v =
    if v < 0.0f then 0.0f
    elif v > 1.0f then 1.0f
    else v

  // Portable Voice.Pitch is a speed multiplier (1.0 = normal, clamped
  // 0.5..2 by Voice.clamp); MonoGame stores the pitch as a base-2 exponent
  // (0 = normal, +1 = one octave up) on both XAudio2 and OpenAL.
  let toMonoGamePitch(multiplier: float32) : float32 =
    System.MathF.Log2 multiplier

  // The music channel volume is the applied (pre-master) volume scaled by
  // the master — the single place the master rule touches music.
  let applyMusicVolume() =
    if not disposed then
      MediaPlayer.Volume <- appliedVolume * masterVolume

  let loadEffect(source: Source) : SoundEffect =
    match source with
    | Pipeline name -> content.Load<SoundEffect> name
    | File path -> SoundEffect.FromFile path

  let loadSong(source: Source) : Song =
    match source with
    | Pipeline name -> content.Load<Song> name
    | File path ->
      // Song.FromUri requires an absolute URI.
      Song.FromUri(Path.GetFileName path, Uri(Path.GetFullPath path))

  // Advances the ring cursor and returns the slot index, creating the slot's
  // instance on its first use (see SoundBankEntry on why lazily).
  member private _.nextSlot(entry: SoundBankEntry) : int =
    entry.Cursor <- (entry.Cursor + 1) % entry.Instances.Length
    let slot = entry.Cursor

    if isNull entry.Instances[slot] then
      entry.Instances[slot] <- entry.Effect.CreateInstance()

    slot

  member private this.playKey(key: string, voice: Voice) =
    match sounds.TryGetValue(key) with
    | true, entry when not disposed ->
      let slot = this.nextSlot entry

      // A 2D play re-arms the slot: retire its 3D flag so the Tick sweep
      // cannot apply the slot's old emitter over this playback's pan.
      entry.SlotIs3D[slot] <- false

      let instance = entry.Instances[slot]
      let v = Voice.clamp voice

      // Stop first: Play() early-returns on a Playing slot (and resumes a
      // Paused one), so re-arming a live slot must reset it or the steal
      // would drop the new playback and keep the old one.
      instance.Stop()

      instance.Volume <- v.Volume
      instance.Pan <- v.Pan
      instance.Pitch <- toMonoGamePitch v.Pitch
      instance.Play()
    | _ -> ()

  member private _.playMusicKey(key: string, looping: bool) =
    match musics.TryGetValue(key) with
    | true, song when not disposed ->
      // A new track starts at the slider volume, not wherever a fade was.
      fadeActive <- false
      appliedVolume <- musicVolume

      hasMusic <- true
      musicState <- MusicChannelState.Playing
      currentSong <- song
      MediaPlayer.IsRepeating <- looping
      applyMusicVolume()
      MediaPlayer.Play(song)
    | _ -> ()

  member private _.stopMusicNow() =
    if not disposed then
      MediaPlayer.Stop()

    fadeActive <- false
    appliedVolume <- musicVolume
    musicState <- MusicChannelState.Stopped

  member private _.setMusicVolumeNow(volume: float32) =
    let v = clamp01 volume
    musicVolume <- v
    appliedVolume <- v
    fadeActive <- false
    applyMusicVolume()

  member private this.play3DKey
    (
      key: string,
      emitterPosition: Vector3,
      emitterVelocity: Vector3 option,
      voice: Voice option
    ) =
    match sounds.TryGetValue(key) with
    | true, entry when not disposed ->
      let slot = this.nextSlot entry
      entry.Has3D <- true

      let v = Voice.clamp(defaultArg voice Voice.center)

      // The slot's pooled emitter: written in place, never allocated. The
      // flag stays on through the playback so the Tick sweep re-attenuates
      // against the moving listener.
      let emitter = entry.Emitters[slot]
      emitter.Position <- emitterPosition
      emitter.Velocity <- defaultArg emitterVelocity Vector3.Zero
      entry.SlotIs3D[slot] <- true

      let instance = entry.Instances[slot]

      // Apply3D reads the instance volume, so set the knobs before it, and
      // apply before Play (the platform contract). Geometry decides pan.
      instance.Volume <- v.Volume
      instance.Pitch <- toMonoGamePitch v.Pitch

      // Stop first, like the 2D path: a live slot must reset before Play(),
      // which early-returns on Playing (and resumes Paused).
      instance.Stop()

      instance.Apply3D(listener, emitter)
      instance.Play()
    | _ -> ()

  /// <summary>Registers a sound effect from a pipeline asset or a loose WAV file. Loading a key twice keeps the first registration.</summary>
  /// <param name="key">The game-vocabulary key to play the sound with (e.g. "jump").</param>
  /// <param name="source">The <see cref="T:Mibo.Elmish.Source"/> to load from.</param>
  member _.RegisterSound(key: string, source: Source) =
    if not(sounds.ContainsKey key) then
      let effect = loadEffect source

      let entry = {
        Effect = effect
        // Slots are created lazily on the k-th play of the key (see
        // SoundBankEntry): a bank of many keys costs no platform voices
        // until the keys actually play.
        Instances = Array.create 8 (null: SoundEffectInstance)
        Emitters = Array.init 8 (fun _ -> AudioEmitter())
        SlotIs3D = Array.zeroCreate 8
        Cursor = 0
        Has3D = false
      }

      sounds.Add(key, entry)

      match source with
      | File _ -> fileOwned.Add key |> ignore
      | Pipeline _ -> ()

  /// <summary>Registers a music track from a pipeline asset or a loose file. Loading a key twice keeps the first registration.</summary>
  /// <param name="key">The game-vocabulary key to start the track with (e.g. "overworld").</param>
  /// <param name="source">The <see cref="T:Mibo.Elmish.Source"/> to load from.</param>
  member _.RegisterMusic(key: string, source: Source) =
    if not(musics.ContainsKey key) then
      musics.Add(key, loadSong source)

      match source with
      | File _ -> fileOwned.Add key |> ignore
      | Pipeline _ -> ()

  /// <summary>Advances music fades and 3D re-attenuation by one frame. The host calls this every frame; calling it yourself double-steps the fades.</summary>
  /// <param name="dt">Elapsed seconds since the last frame.</param>
  member _.Tick(dt: float32) : unit =
    if not disposed then
      if fadeActive then
        fadeElapsed <- fadeElapsed + dt

        let v = Fade.volume fadeFrom fadeTo fadeElapsed fadeDuration

        appliedVolume <- v
        MediaPlayer.Volume <- v * masterVolume

        if fadeElapsed >= fadeDuration then
          fadeActive <- false
          appliedVolume <- fadeTo

          if fadeStopOnComplete then
            MediaPlayer.Stop()
            appliedVolume <- musicVolume
            musicState <- MusicChannelState.Stopped

      // Re-attenuate live 3D plays against the current listener. The entry
      // flag keeps the sweep at one bool read for keys that never play in
      // 3D; within a 3D key, a slot is in the sweep only while its flag is
      // on and its instance still plays (re-arming flips the flag first).
      for KeyValue(_, entry) in sounds do
        if entry.Has3D then
          for slot in 0 .. entry.Instances.Length - 1 do
            if entry.SlotIs3D[slot] then
              let instance = entry.Instances[slot]

              if instance.State = SoundState.Playing then
                instance.Apply3D(listener, entry.Emitters[slot])

  /// <summary>Disposes the instance ring and the loose-file sounds and songs. Pipeline assets stay owned by the ContentManager.</summary>
  member _.Dispose() : unit =
    if not disposed then
      disposed <- true

      if hasMusic then
        MediaPlayer.Stop()
        hasMusic <- false

      // The instance ring is always service-owned; slots may not exist yet
      // (lazy creation).
      for KeyValue(_, entry) in sounds do
        for instance in entry.Instances do
          if not(isNull instance) then
            instance.Dispose()

      // Loose files were loaded and are disposed here; pipeline assets are
      // owned (and disposed) by the ContentManager.
      for KeyValue(key, entry) in sounds do
        if fileOwned.Contains key then
          entry.Effect.Dispose()

      for KeyValue(key, song) in musics do
        if fileOwned.Contains key then
          song.Dispose()

      sounds.Clear()
      musics.Clear()
      fileOwned.Clear()

  interface IAudio with
    member this.Play(key: string) : unit = this.playKey(key, Voice.center)

    member this.Play(key: string, voice: Voice) : unit =
      this.playKey(key, voice)

    member _.StopAllSounds() : unit =
      if not disposed then
        for KeyValue(_, entry) in sounds do
          for instance in entry.Instances do
            if not(isNull instance) && instance.State <> SoundState.Stopped then
              instance.Stop()

    member this.PlayMusic(key: string) : unit = this.playMusicKey(key, true)

    member this.PlayMusicOnce(key: string) : unit =
      this.playMusicKey(key, false)

    member this.StopMusic() : unit = this.stopMusicNow()

    // Guarded by the tracked channel state, like the raylib backend:
    // pausing, resuming, or seeking with no running track is a no-op, and a
    // seek never starts a stopped track.
    member _.PauseMusic() : unit =
      if hasMusic && musicState = MusicChannelState.Playing && not disposed then
        MediaPlayer.Pause()
        musicState <- MusicChannelState.Paused

    member _.ResumeMusic() : unit =
      if hasMusic && musicState = MusicChannelState.Paused && not disposed then
        MediaPlayer.Resume()
        musicState <- MusicChannelState.Playing

    member _.SeekMusic(seconds: float32) : unit =
      if
        hasMusic && musicState <> MusicChannelState.Stopped && not disposed
      then
        // Clamp to the track length, like the raylib backend.
        let length = float32 currentSong.Duration.TotalSeconds

        let position =
          if length <= 0.0f then
            0.0f
          else
            min (max seconds 0.0f) length

        // Play(song, position) is the only seek MonoGame offers, and it
        // starts playback — restore a paused state so seeking a paused track
        // stays paused, matching the ray backend.
        let wasPaused = musicState = MusicChannelState.Paused

        MediaPlayer.Play(currentSong, TimeSpan.FromSeconds(float position))

        if wasPaused then
          MediaPlayer.Pause()

    member _.MusicPosition() : float32 =
      if hasMusic && not disposed then
        float32 MediaPlayer.PlayPosition.TotalSeconds
      else
        0.0f

    member this.SetMusicVolume(volume: float32) : unit =
      this.setMusicVolumeNow volume

    member this.FadeMusic(targetVolume: float32, seconds: float32) : unit =
      let target = clamp01 targetVolume

      if seconds <= 0.0f then
        if target <= 0.0f then
          this.stopMusicNow()
        else
          this.setMusicVolumeNow target
      else
        fadeActive <- true
        fadeFrom <- appliedVolume
        fadeTo <- target
        fadeElapsed <- 0.0f
        fadeDuration <- seconds
        fadeStopOnComplete <- target <= 0.0f

    member _.SetMasterVolume(volume: float32) : unit =
      // Master volume scales the whole mix (the portable rule): the sound
      // effects through SoundEffect.MasterVolume (live — the setter
      // re-applies to playing instances), the music channel through our own
      // scaling, re-applied right here.
      let v = clamp01 volume
      masterVolume <- v
      SoundEffect.MasterVolume <- v
      applyMusicVolume()

    member _.MusicVolume() : float32 = musicVolume

    member this.Tick(dt: float32) : unit = this.Tick(dt)

    member this.Dispose() : unit = this.Dispose()

  interface MonoGameAudio with
    member _.SetListener
      (position: Vector3, forward: Vector3, up: Vector3, velocity: Vector3)
      : unit =
      listener.Position <- position
      listener.Forward <- forward
      listener.Up <- up
      listener.Velocity <- velocity

    member this.Play3D
      (
        key: string,
        emitterPosition: Vector3,
        ?emitterVelocity: Vector3,
        ?voice: Voice
      ) : unit =
      this.play3DKey(key, emitterPosition, emitterVelocity, voice)

    member _.DistanceScale
      with get (): float32 = SoundEffect.DistanceScale
      and set v = SoundEffect.DistanceScale <- v

    member _.DopplerScale
      with get (): float32 = SoundEffect.DopplerScale
      and set v = SoundEffect.DopplerScale <- v
