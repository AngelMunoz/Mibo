namespace Mibo.Adaptive

open Mibo.Elmish
open Mibo.Audio

// ─────────────────────────────────────────────────────────────────────────────
// Audio surface: the adaptive helpers over the portable IAudio service.
//
// The helpers hang off the adaptive contexts as a zero-allocation surface —
// ctx.Audio — bound to the context's intent queue and GameContext, so no call
// passes what the context already holds. Each helper resolves IAudio from the
// bound context when it runs and posts one closure that captures only the
// resolved service — Headless-safe, no allocation at all when no service is
// registered, one closure per posted intent otherwise.
//
// The mirror of the MVU set (module Mibo.Elmish.Audio): the same keys, the
// same knobs, the same music-channel rules. The fade-in target (the last
// setMusicVolume value) is read from the service (IAudio.MusicVolume), so
// there is no shared helper state.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The audio helpers bound to one adaptive context, reachable as
/// <c>ctx.Audio</c> on both
/// <see cref="T:Mibo.Adaptive.AdaptiveFrameContext"/> (Init) and
/// <see cref="T:Mibo.Adaptive.AdaptiveContext"/> (Update): play sound effects
/// with per-play knobs, drive the single music channel (play, pause, seek,
/// fades), and set volumes.
/// </summary>
/// <remarks>
/// <para>
/// The sounds and tracks are registered before <c>init</c> runs — the hosts
/// register the audio service unconditionally, and the backend program
/// builders (<c>AdaptiveRaylibProgram.withBank</c> /
/// <c>AdaptiveMonoGameProgram.withBank</c>) load the bank. Playing an
/// unregistered key is a silent no-op, as is every helper when no audio
/// service is registered (headless runs).
/// </para>
/// <para>
/// Each helper posts one <c>unit -&gt; unit</c> closure, like every intent:
/// posted from <c>Update</c> the work runs at the post drain (after the
/// step's <c>Update</c>, before the frame is forced); posted from
/// <c>Init</c> it runs at the startup drain, before the first frame is
/// forced. The audio service resolves from the bound context before the
/// closure is posted.
/// </para>
/// <para>
/// There are no mix groups: a sound-effect "bus" is model state the game
/// multiplies into the voice at the play site, and music is a single channel
/// with one live knob (<c>setMusicVolume</c>).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// let update ctx state msg =
///   match msg with
///   | Jump -&gt;
///       ctx.Audio.play("jump")
///       { state with Vy = jumpSpeed }
/// </code>
/// </example>
[<Struct>]
type AudioSurface internal (intents: IntentQueue, ctx: GameContext) =

  /// <summary>Plays the sound registered under <paramref name="key"/> with the default voice.</summary>
  /// <param name="key">The key the sound was registered under in the bank.</param>
  member inline _.play(key: string) : unit =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> intents.post(fun () -> audio.Play key)
    | ValueNone -> ()

  /// <summary>Plays the sound registered under <paramref name="key"/> with per-play knobs.</summary>
  /// <param name="key">The key the sound was registered under in the bank.</param>
  /// <param name="voice">Volume, pan, and pitch for this playback (start from <see cref="M:Mibo.Audio.Voice.center"/> and update fields).</param>
  member inline _.playWith(key: string, voice: Voice) : unit =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> intents.post(fun () -> audio.Play(key, voice))
    | ValueNone -> ()

  /// <summary>Stops every playing sound effect. Music keeps playing.</summary>
  member inline _.stopAll() : unit =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> intents.post(fun () -> audio.StopAllSounds())
    | ValueNone -> ()

  /// <summary>Starts the music registered under <paramref name="key"/> as the background track, looping. Replaces the track that is playing.</summary>
  /// <param name="key">The key the track was registered under in the bank.</param>
  member inline _.playMusic(key: string) : unit =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> intents.post(fun () -> audio.PlayMusic key)
    | ValueNone -> ()

  /// <summary>Starts the music registered under <paramref name="key"/> once through, then stops. Replaces the track that is playing.</summary>
  /// <param name="key">The key the track was registered under in the bank.</param>
  member inline _.playMusicOnce(key: string) : unit =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> intents.post(fun () -> audio.PlayMusicOnce key)
    | ValueNone -> ()

  /// <summary>Stops the music and resets it to its start.</summary>
  member inline _.stopMusic() : unit =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> intents.post(fun () -> audio.StopMusic())
    | ValueNone -> ()

  /// <summary>Pauses the music at the current position.</summary>
  member inline _.pauseMusic() : unit =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> intents.post(fun () -> audio.PauseMusic())
    | ValueNone -> ()

  /// <summary>Resumes the music from where it was paused.</summary>
  member inline _.resumeMusic() : unit =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> intents.post(fun () -> audio.ResumeMusic())
    | ValueNone -> ()

  /// <summary>Seeks the music to an absolute time in seconds.</summary>
  /// <param name="seconds">The position to jump to, counted from the track start.</param>
  member inline _.seekMusic(seconds: float32) : unit =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> intents.post(fun () -> audio.SeekMusic seconds)
    | ValueNone -> ()

  /// <summary>Sets the music volume live — the music slider. This is also the volume a later fade-in fades to.</summary>
  /// <param name="volume">Music volume (1.0 = full, 0.0 = silent).</param>
  member inline _.setMusicVolume(volume: float32) : unit =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> intents.post(fun () -> audio.SetMusicVolume volume)
    | ValueNone -> ()

  /// <summary>Fades the music in over <paramref name="seconds"/>, toward the current music slider value — the last volume passed to <c>setMusicVolume</c> (1.0 if never set), read from the service at post time.</summary>
  /// <param name="seconds">Fade duration in seconds.</param>
  member inline _.fadeMusicIn(seconds: float32) : unit =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio ->
      intents.post(fun () -> audio.FadeMusic(audio.MusicVolume(), seconds))
    | ValueNone -> ()

  /// <summary>Fades the music out over <paramref name="seconds"/>; the music stops when the fade completes.</summary>
  /// <param name="seconds">Fade duration in seconds.</param>
  member inline _.fadeMusicOut(seconds: float32) : unit =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> intents.post(fun () -> audio.FadeMusic(0.0f, seconds))
    | ValueNone -> ()

  /// <summary>Sets the master volume that scales the whole mix — every sound effect and the music channel, live (see <see cref="M:Mibo.Audio.IAudio.SetMasterVolume"/>).</summary>
  /// <param name="volume">Master volume (1.0 = full).</param>
  member inline _.setMasterVolume(volume: float32) : unit =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> intents.post(fun () -> audio.SetMasterVolume volume)
    | ValueNone -> ()

/// <summary>
/// Hosts the audio-surface extensions. Auto-opened with the
/// <c>Mibo.Adaptive</c> namespace, so <c>ctx.Audio</c> is in scope wherever
/// the adaptive runtime is.
/// </summary>
[<AutoOpen>]
module AudioSurfaceExtensions =

  type AdaptiveFrameContext with

    /// <summary>The audio helpers bound to this context. From <c>Init</c>, posted work runs at the startup drain, before the first frame is forced.</summary>
    member this.Audio: AudioSurface = AudioSurface(this.Intents, this.Context)

  type AdaptiveContext with

    /// <summary>The audio helpers bound to this context. From <c>Update</c>, posted work runs at the post drain, after this step's <c>Update</c>, before the frame is forced.</summary>
    member this.Audio: AudioSurface = AudioSurface(this.Intents, this.Context)
