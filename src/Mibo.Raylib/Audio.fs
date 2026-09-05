namespace Mibo.Elmish

open System
open System.Collections.Generic
open System.IO
open Raylib_cs
open Mibo.Audio

// ─────────────────────────────────────────────────────────────────────────────
// Raylib audio service.
//
// raylib loads files, period — a plain path is the honest source type here (the
// MonoGame shell instead distinguishes pipeline assets from loose files).
// Sounds load once; each key owns the shared sample data plus 8 aliases
// (LoadSoundAlias shares the sample buffer, so the pool costs no extra audio
// memory) and plays round-robin through them, so a key can overlap itself up
// to 8 times before the oldest playback is stolen. Music is a single channel:
// one loaded Music per key, one playing at a time, streamed by the per-frame
// Tick the host calls.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-key playback entry: the loaded <c>Sound</c> plus the alias pool played
/// round-robin. The cursor always advances, so the 9th playback of a key
/// steals the slot of the oldest one.
/// </summary>
type private SoundBankEntry = {
  Source: Sound
  Aliases: Sound[]
  mutable Cursor: int
}

/// <summary>
/// The raylib-backed <see cref="T:Mibo.Audio.IAudio"/>: file-path sound/music
/// registration, an 8-alias ring per sound, a single music channel, and the
/// fade state machine driven by <see cref="M:Mibo.Audio.IAudio.Tick"/>.
/// </summary>
/// <remarks>
/// <para>
/// Register entries right after the host registers the service (the
/// <c>RaylibProgram.withBank</c> builder does this for MVU programs) — sounds
/// load into memory at that point, before user <c>init</c> runs.
/// </para>
/// <para>
/// Paths resolve against the game's asset base path when the program set one
/// with <c>withAssetsBasePath</c>, the same rule as the asset service. File
/// formats: WAV, OGG, MP3, FLAC, QOA (whatever the raylib build decodes).
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
type AudioService(baseAssetPath: string voption) as this =

  let resolvePath(path: string) =
    match baseAssetPath with
    | ValueSome bp -> Path.Combine(bp, path)
    | ValueNone -> path

  let sounds = Dictionary<string, SoundBankEntry>()
  let musics = Dictionary<string, Music>()

  let mutable disposed = false

  // The single music channel: the track that plays, and the volume state.
  let mutable hasMusic = false
  let mutable currentMusic = Unchecked.defaultof<Music>
  // The last explicitly-set music volume (the slider); new tracks start here.
  let mutable musicVolume = 1.0f
  // The volume applied to the channel right now (the fade may be en route).
  let mutable appliedVolume = 1.0f

  // Fade state machine, advanced by Tick.
  let mutable fadeActive = false
  let mutable fadeFrom = 1.0f
  let mutable fadeTo = 1.0f
  let mutable fadeElapsed = 0.0f
  let mutable fadeDuration = 0.0f
  let mutable fadeStopOnComplete = false

  let clamp01 v =
    if v < 0.0f then 0.0f
    elif v > 1.0f then 1.0f
    else v

  member private _.playKey(key: string, voice: Voice) =
    match sounds.TryGetValue(key) with
    | true, entry when not disposed ->
      entry.Cursor <- (entry.Cursor + 1) % entry.Aliases.Length

      let alias = entry.Aliases[entry.Cursor]

      Raylib.SetSoundVolume(alias, voice.Volume)
      Raylib.SetSoundPan(alias, voice.Pan)
      Raylib.SetSoundPitch(alias, voice.Pitch)
      Raylib.PlaySound(alias)
    | _ -> ()

  member private _.playMusicKey(key: string, looping: bool) =
    match musics.TryGetValue(key) with
    | true, m when not disposed ->
      if hasMusic then
        Raylib.StopMusicStream(currentMusic)

      // A new track starts at the slider volume, not wherever a fade was.
      fadeActive <- false
      appliedVolume <- musicVolume

      currentMusic <- m
      hasMusic <- true
      currentMusic.Looping <- CBool looping
      Raylib.SetMusicVolume(currentMusic, appliedVolume)
      Raylib.PlayMusicStream(currentMusic)
    | _ -> ()

  member private _.stopMusicNow() =
    if hasMusic && not disposed then
      Raylib.StopMusicStream(currentMusic)

    fadeActive <- false
    appliedVolume <- musicVolume

  member private _.setMusicVolumeNow(volume: float32) =
    let v = clamp01 volume
    musicVolume <- v
    appliedVolume <- v
    fadeActive <- false

    if hasMusic && not disposed then
      Raylib.SetMusicVolume(currentMusic, v)

  /// <summary>Registers a sound effect from a file path. Loading a key twice keeps the first registration.</summary>
  /// <param name="key">The game-vocabulary key to play the sound with (e.g. "jump").</param>
  /// <param name="path">File path (relative to the program's asset base path when one is set).</param>
  member _.RegisterSound(key: string, path: string) =
    if not(sounds.ContainsKey key) then
      let sound = Raylib.LoadSound(resolvePath path)

      let aliases = Array.init 8 (fun _ -> Raylib.LoadSoundAlias(sound))

      sounds.Add(
        key,
        {
          Source = sound
          Aliases = aliases
          Cursor = 0
        }
      )

  /// <summary>Registers a music track from a file path. Loading a key twice keeps the first registration.</summary>
  /// <param name="key">The game-vocabulary key to start the track with (e.g. "overworld").</param>
  /// <param name="path">File path (relative to the program's asset base path when one is set).</param>
  member _.RegisterMusic(key: string, path: string) =
    if not(musics.ContainsKey key) then
      musics.Add(key, Raylib.LoadMusicStream(resolvePath path))

  /// <summary>Advances music streaming and fade interpolation by one frame. The host calls this every frame; calling it yourself double-steps the music.</summary>
  /// <param name="dt">Elapsed seconds since the last frame.</param>
  member _.Tick(dt: float32) : unit =
    if not disposed then
      if hasMusic then
        Raylib.UpdateMusicStream(currentMusic)

      if fadeActive then
        fadeElapsed <- fadeElapsed + dt

        let v = Fade.volume fadeFrom fadeTo fadeElapsed fadeDuration

        appliedVolume <- v

        if hasMusic then
          Raylib.SetMusicVolume(currentMusic, v)

        if fadeElapsed >= fadeDuration then
          fadeActive <- false
          appliedVolume <- fadeTo

          if fadeStopOnComplete then
            Raylib.StopMusicStream(currentMusic)
            appliedVolume <- musicVolume

  /// <summary>Unloads every registered sound, alias, and music stream.</summary>
  member _.Dispose() : unit =
    if not disposed then
      disposed <- true

      for KeyValue(_, entry) in sounds do
        for alias in entry.Aliases do
          Raylib.UnloadSoundAlias(alias)

        Raylib.UnloadSound(entry.Source)

      sounds.Clear()

      if hasMusic then
        Raylib.StopMusicStream(currentMusic)
        hasMusic <- false

      for KeyValue(_, m) in musics do
        Raylib.UnloadMusicStream(m)

      musics.Clear()

  interface IAudio with
    member this.Play(key: string) : unit = this.playKey(key, Voice.center)

    member this.Play(key: string, voice: Voice) : unit =
      this.playKey(key, voice)

    member _.StopAllSounds() : unit =
      if not disposed then
        for KeyValue(_, entry) in sounds do
          for alias in entry.Aliases do
            Raylib.StopSound(alias)

    member this.PlayMusic(key: string) : unit = this.playMusicKey(key, true)

    member this.PlayMusicOnce(key: string) : unit =
      this.playMusicKey(key, false)

    member this.StopMusic() : unit = this.stopMusicNow()

    member _.PauseMusic() : unit =
      if hasMusic && not disposed then
        Raylib.PauseMusicStream(currentMusic)

    member _.ResumeMusic() : unit =
      if hasMusic && not disposed then
        Raylib.ResumeMusicStream(currentMusic)

    member _.SeekMusic(seconds: float32) : unit =
      if hasMusic && not disposed then
        // SeekMusicStream takes seconds; clamp to the track length.
        let length = Raylib.GetMusicTimeLength(currentMusic)

        let position =
          if length <= 0.0f then
            0.0f
          else
            min (max seconds 0.0f) length

        Raylib.SeekMusicStream(currentMusic, position)

    member _.MusicPosition() : float32 =
      if hasMusic && not disposed then
        Raylib.GetMusicTimePlayed(currentMusic)
      else
        0.0f

    member _.SetMusicVolume(volume: float32) : unit =
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
      Raylib.SetMasterVolume(clamp01 volume)

    member this.Tick(dt: float32) : unit = this.Tick(dt)

    member this.Dispose() : unit = this.Dispose()
