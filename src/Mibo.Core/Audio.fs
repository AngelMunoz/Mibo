namespace Mibo.Audio

open System

// ─────────────────────────────────────────────────────────────────────────────
// The backend-neutral audio contract.
//
// IAudio is the full portable surface: both backends (raylib, MonoGame)
// register a service that implements it, so game code plays sounds and drives
// music without touching Raylib_cs or Microsoft.Xna.Framework.Audio types.
// The contract holds no source types and no registration members — how a key
// is loaded is backend shell surface (file paths on raylib, pipeline/loose
// files on MonoGame), configured through the per-backend program builders.
//
// Keys are game vocabulary ("jump", "overworld") — the currency of the Cmd
// helpers, the intent helpers, and direct service use. No backend information
// lives in a key.
//
// There are no mix groups and no audio categories by design: a named-group
// volume is user model state. SFX volumes multiply at the play site (voice =
// group volume × per-play volume), and music is a single channel with one
// live knob (SetMusicVolume). Framework machinery would only duplicate a
// dictionary the game already owns.
//
// Music is a single channel on purpose: the MonoGame MediaPlayer is a
// singleton, so a symmetric contract can only promise one track at a time.
//
// The host owns the lifecycle: every windowed host registers IAudio before
// user init runs, calls Tick(dt) once per frame, and disposes the service on
// shutdown. Users never call Tick.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-play knobs for a sound effect: volume, pan, and pitch.
/// </summary>
/// <remarks>
/// A struct passed by copy — building one and passing it to
/// <see cref="M:Mibo.Audio.IAudio.Play(System.String,Mibo.Audio.Voice)"/>
/// does not allocate. Start from <see cref="M:Mibo.Audio.Voice.center"/> and
/// change fields with a record update.
/// </remarks>
/// <example>
/// <code>
/// let voice = { Voice.center with Volume = 0.7f; Pan = -0.5f }
/// </code>
/// </example>
[<Struct>]
type Voice = {
  /// <summary>Playback volume. 1.0 = full volume, 0.0 = silent. Values outside 0.0..1.0 clamp at the service entry.</summary>
  Volume: float32

  /// <summary>Stereo pan. -1.0 = fully left, 0.0 = center, 1.0 = fully right. Values outside -1.0..1.0 clamp at the service entry.</summary>
  Pan: float32

  /// <summary>Playback pitch as a speed multiplier: 1.0 = normal, 0.5 = half speed, 2.0 = double speed. This is the raylib convention, translated per backend (MonoGame stores pitches as octave offsets from normal). Values outside 0.5..2.0 (one octave down or up) clamp at the service entry — the range every backend expresses.</summary>
  Pitch: float32
}

/// <summary>Constructors for <see cref="T:Mibo.Audio.Voice"/> values.</summary>
module Voice =
  /// <summary>The default voice: full volume, centered, normal pitch.</summary>
  let center: Voice = {
    Volume = 1.0f
    Pan = 0.0f
    Pitch = 1.0f
  }

  /// <summary>A voice with the given volume, centered, normal pitch.</summary>
  /// <param name="volume">Playback volume (1.0 = full).</param>
  let inline ofVolume(volume: float32) : Voice = { center with Volume = volume }

  /// <summary>A voice with the given volume and pan, normal pitch.</summary>
  /// <param name="volume">Playback volume (1.0 = full).</param>
  /// <param name="pan">Stereo pan (-1 left .. 1 right).</param>
  let inline at (volume: float32) (pan: float32) : Voice = {
    center with
        Volume = volume
        Pan = pan
  }

  /// <summary>Clamps a voice to the portable ranges — Volume 0.0..1.0, Pan -1.0..1.0, Pitch 0.5..2.0. The services call this at the entry of every play, so an out-of-range knob can never crash one backend while playing fine on another.</summary>
  /// <param name="voice">The voice to clamp.</param>
  /// <returns>The same voice with every knob inside its portable range.</returns>
  let inline clamp(voice: Voice) : Voice = {
    Volume = min (max voice.Volume 0.0f) 1.0f
    Pan = min (max voice.Pan -1.0f) 1.0f
    Pitch = min (max voice.Pitch 0.5f) 2.0f
  }

/// <summary>
/// The backend-neutral audio service. Every windowed host registers one under
/// this interface before user <c>init</c> runs; resolve it with
/// <c>GameContext.getService&lt;IAudio&gt; ctx</c> (the MVU Cmd helpers and the
/// adaptive intent helpers do this for you and no-op when it is absent).
/// </summary>
/// <remarks>
/// <para>
/// Sound effects overlap: each key owns a small pool of playback slots, so
/// playing the same key several times per frame layers the copies. Music is a
/// single channel — starting a track replaces the one playing.
/// </para>
/// <para>
/// Unknown keys are silent no-ops, so headless programs and test runs never
/// need an audio device.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// let audio = GameContext.getService&lt;IAudio&gt; ctx
/// audio.Play "jump"
/// audio.PlayMusic "overworld"        // loops
/// audio.FadeMusic(0.0f, 1.5f)        // fade out over 1.5 s, then stop
/// </code>
/// </example>
type IAudio =
  inherit IDisposable

  // ── sfx ──────────────────────────────────────────────────────────────────

  /// <summary>Plays the sound registered under <paramref name="key"/> with the default voice.</summary>
  /// <param name="key">The game-vocabulary key the sound was registered under (e.g. "jump").</param>
  abstract Play: key: string -> unit

  /// <summary>Plays the sound registered under <paramref name="key"/> with per-play knobs.</summary>
  /// <param name="key">The game-vocabulary key the sound was registered under (e.g. "jump").</param>
  /// <param name="voice">Volume, pan, and pitch for this playback only.</param>
  abstract Play: key: string * voice: Voice -> unit

  /// <summary>Stops every playing sound effect. Music keeps playing.</summary>
  abstract StopAllSounds: unit -> unit

  // ── music (single channel) ───────────────────────────────────────────────

  /// <summary>Plays the music registered under <paramref name="key"/>, looping — the background-music case. Replaces the track that is playing.</summary>
  abstract PlayMusic: key: string -> unit

  /// <summary>Plays the music registered under <paramref name="key"/> once through, then stops. Replaces the track that is playing.</summary>
  abstract PlayMusicOnce: key: string -> unit

  /// <summary>Stops the music and resets it to its start.</summary>
  abstract StopMusic: unit -> unit

  /// <summary>Pauses the music at the current position.</summary>
  abstract PauseMusic: unit -> unit

  /// <summary>Resumes the music from where it was paused.</summary>
  abstract ResumeMusic: unit -> unit

  /// <summary>Seeks the music to an absolute time in seconds.</summary>
  /// <param name="seconds">The position to jump to, counted from the track start.</param>
  abstract SeekMusic: seconds: float32 -> unit

  /// <summary>The music playback position in seconds.</summary>
  abstract MusicPosition: unit -> float32

  /// <summary>Sets the music volume live — the music slider. This is also the volume a later fade-in returns to.</summary>
  /// <param name="volume">Music volume (1.0 = full, 0.0 = silent).</param>
  abstract SetMusicVolume: volume: float32 -> unit

  /// <summary>Fades the music volume to <paramref name="targetVolume"/> over <paramref name="seconds"/>. A fade to (or below) zero stops the music when it completes; a fade to a positive volume leaves it playing.</summary>
  /// <param name="targetVolume">The volume to reach (0.0 = fade out and stop).</param>
  /// <param name="seconds">Fade duration in seconds; zero or less jumps straight to the target.</param>
  abstract FadeMusic: targetVolume: float32 * seconds: float32 -> unit

  // ── global ───────────────────────────────────────────────────────────────

  /// <summary>Sets the master volume that scales the whole mix — every sound effect and the music channel — live, on sounds that are already playing too. Rule on both backends: master volume is the one knob above everything; per-backend engines apply it differently, the services make them behave the same. Music volume is set separately with <see cref="M:Mibo.Audio.IAudio.SetMusicVolume(System.Single)"/>.</summary>
  /// <param name="volume">Master volume (1.0 = full). Values outside 0.0..1.0 clamp.</param>
  abstract SetMasterVolume: volume: float32 -> unit

  /// <summary>The music volume — the current value of the music slider, as set through <see cref="M:Mibo.Audio.IAudio.SetMusicVolume(System.Single)"/> (1.0 if never set). This is the volume a fade with no explicit target returns to, so the MVU <c>fadeMusicIn</c> and adaptive <c>fadeMusicIn</c> helpers read it from here.</summary>
  /// <returns>The music slider value, 0.0..1.0.</returns>
  abstract MusicVolume: unit -> float32

  // ── per-frame tick (hosts call this; users never do) ────────────────────

  /// <summary>Advances music streaming and fade interpolation by one frame. The windowed hosts call this every frame; calling it yourself double-steps the music.</summary>
  /// <param name="dt">Elapsed seconds since the last frame.</param>
  abstract Tick: dt: float32 -> unit

/// <summary>
/// 2D positional attenuation: turns a listener facing plus listener/source
/// positions into a <see cref="T:Mibo.Audio.Voice"/> (Volume and Pan set,
/// Pitch 1). This is the portable alternative to the MonoGame-only 3D audio
/// surface — raylib has no listener model, so the Core contract only promises
/// flat 2D attenuation.
/// </summary>
/// <remarks>
/// Volume falls off linearly from 1.0 at the listener's position to 0.0 at
/// <paramref name="maxDistance"/>; sources beyond that distance are silent.
/// Pan is the signed direction from the listener to the source along the
/// listener's right-hand perpendicular, in the same coordinate system as the
/// positions: with the usual screen coordinates (X right, Y down), a source
/// below the listener plays on the right when the listener faces +X.
/// </remarks>
/// <example>
/// <code>
/// let voice =
///   Attenuation2D.compute(facingRad, player.X, player.Y) (enemy.X, enemy.Y) 640.0f
///
/// audio.Play("enemy-step", voice)
/// </code>
/// </example>
module Attenuation2D =

  /// <summary>Computes the <see cref="T:Mibo.Audio.Voice"/> for a sound source heard by a 2D listener.</summary>
  /// <param name="facingTuple">The listener's facing angle in radians, plus the listener position (X, Y).</param>
  /// <param name="sourceTuple">The sound source position (X, Y), in the same coordinates.</param>
  /// <param name="maxDistance">Distance in the same units at which the source falls silent.</param>
  /// <returns>A voice with linear distance volume and directional pan (Pitch 1).</returns>
  let compute
    (facingTuple: float32 * float32 * float32)
    (sourceTuple: float32 * float32)
    (maxDistance: float32)
    : Voice =
    let facingRad, listenerX, listenerY = facingTuple
    let sourceX, sourceY = sourceTuple
    let dx = sourceX - listenerX
    let dy = sourceY - listenerY
    let distance = sqrt(dx * dx + dy * dy)

    let safeMax = if maxDistance <= 0.0f then 1.0f else maxDistance

    let volume =
      let v = 1.0f - distance / safeMax

      if v < 0.0f then 0.0f
      elif v > 1.0f then 1.0f
      else v

    // Right-hand perpendicular of the facing direction (cos f, sin f).
    let rightX = -sin facingRad
    let rightY = cos facingRad

    let pan =
      if distance < 1e-6f then
        0.0f
      else
        let p = (dx * rightX + dy * rightY) / distance

        if p < -1.0f then -1.0f
        elif p > 1.0f then 1.0f
        else p

    {
      Volume = volume
      Pan = pan
      Pitch = 1.0f
    }

/// <summary>
/// Linear fade interpolation shared by both backend services — the single
/// definition of how a volume moves from a start value to a target over a
/// duration, so raylib and MonoGame fades behave identically.
/// </summary>
module Fade =

  /// <summary>Interpolates the volume at <paramref name="elapsed"/> seconds into a fade from <paramref name="startVolume"/> to <paramref name="targetVolume"/> lasting <paramref name="duration"/> seconds.</summary>
  /// <param name="startVolume">Volume when the fade started.</param>
  /// <param name="targetVolume">Volume when the fade completes.</param>
  /// <param name="elapsed">Seconds since the fade started.</param>
  /// <param name="duration">Total fade duration in seconds.</param>
  /// <returns>The interpolated volume: the start value before the fade begins, the target value at and past the end.</returns>
  let inline volume
    (startVolume: float32)
    (targetVolume: float32)
    (elapsed: float32)
    (duration: float32)
    : float32 =
    if duration <= 0.0f then
      targetVolume
    elif elapsed <= 0.0f then
      startVolume
    elif elapsed >= duration then
      targetVolume
    else
      startVolume + (targetVolume - startVolume) * (elapsed / duration)
