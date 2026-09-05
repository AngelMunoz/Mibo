namespace Mibo.Adaptive

open Mibo.Elmish
open Mibo.Audio

// ─────────────────────────────────────────────────────────────────────────────
// Audio intents: the adaptive helpers over the portable IAudio service.
//
// Type extensions on IntentQueue, so the helpers hang off the queue the user
// already holds (ctx.Intents) instead of a module. Each helper posts a
// closure that captures the GameContext and resolves IAudio at drain time —
// Headless-safe, one allocation per posted intent, same as every other
// intent, and a silent no-op when no service is registered.
//
// The mirror of the MVU set (module Mibo.Elmish.Audio): the same keys, the
// same knobs, the same music-channel rules. The fade-in target (the last
// setMusicVolume value) is read from the service (IAudio.MusicVolume), so
// there is no shared helper state.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Hosts the <see cref="T:Mibo.Adaptive.IntentQueue"/> audio extensions.
/// Auto-opened with the <c>Mibo.Adaptive</c> namespace, so the helpers are in
/// scope wherever the adaptive runtime is.
/// </summary>
[<AutoOpen>]
module AudioIntents =

  /// <summary>
  /// Audio intents on <see cref="T:Mibo.Adaptive.IntentQueue"/>: play sound
  /// effects with per-play knobs, drive the single music channel (play,
  /// pause, seek, fades), and set volumes. The work runs at the post drain,
  /// like every intent — posted from <c>Update</c> it lands after the step's
  /// <c>Update</c>, before the frame is forced.
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
  /// There are no mix groups: a sound-effect "bus" is model state the game
  /// multiplies into the voice at the play site, and music is a single
  /// channel with one live knob (<c>setMusicVolume</c>).
  /// </para>
  /// </remarks>
  /// <example>
  /// <code>
  /// let update ctx state msg =
  ///   match msg with
  ///   | Jump -&gt;
  ///       ctx.Intents.play(ctx.Context, "jump")
  ///       { state with Vy = jumpSpeed }
  /// </code>
  /// </example>
  type IntentQueue with

    /// <summary>Plays the sound registered under <paramref name="key"/> with the default voice.</summary>
    /// <param name="ctx">The game context; the posted work resolves the audio service from it at drain time.</param>
    /// <param name="key">The key the sound was registered under in the bank.</param>
    member this.play(ctx: GameContext, key: string) : unit =
      this.post(fun () ->
        match GameContext.tryGetService<IAudio> ctx with
        | ValueSome audio -> audio.Play key
        | ValueNone -> ())

    /// <summary>Plays the sound registered under <paramref name="key"/> with per-play knobs.</summary>
    /// <param name="ctx">The game context.</param>
    /// <param name="key">The key the sound was registered under in the bank.</param>
    /// <param name="voice">Volume, pan, and pitch for this playback (start from <see cref="M:Mibo.Audio.Voice.center"/> and update fields).</param>
    member this.playWith(ctx: GameContext, key: string, voice: Voice) : unit =
      this.post(fun () ->
        match GameContext.tryGetService<IAudio> ctx with
        | ValueSome audio -> audio.Play(key, voice)
        | ValueNone -> ())

    /// <summary>Stops every playing sound effect. Music keeps playing.</summary>
    /// <param name="ctx">The game context.</param>
    member this.stopAll(ctx: GameContext) : unit =
      this.post(fun () ->
        match GameContext.tryGetService<IAudio> ctx with
        | ValueSome audio -> audio.StopAllSounds()
        | ValueNone -> ())

    /// <summary>Starts the music registered under <paramref name="key"/> as the background track, looping. Replaces the track that is playing.</summary>
    /// <param name="ctx">The game context.</param>
    /// <param name="key">The key the track was registered under in the bank.</param>
    member this.playMusic(ctx: GameContext, key: string) : unit =
      this.post(fun () ->
        match GameContext.tryGetService<IAudio> ctx with
        | ValueSome audio -> audio.PlayMusic key
        | ValueNone -> ())

    /// <summary>Starts the music registered under <paramref name="key"/> once through, then stops. Replaces the track that is playing.</summary>
    /// <param name="ctx">The game context.</param>
    /// <param name="key">The key the track was registered under in the bank.</param>
    member this.playMusicOnce(ctx: GameContext, key: string) : unit =
      this.post(fun () ->
        match GameContext.tryGetService<IAudio> ctx with
        | ValueSome audio -> audio.PlayMusicOnce key
        | ValueNone -> ())

    /// <summary>Stops the music and resets it to its start.</summary>
    /// <param name="ctx">The game context.</param>
    member this.stopMusic(ctx: GameContext) : unit =
      this.post(fun () ->
        match GameContext.tryGetService<IAudio> ctx with
        | ValueSome audio -> audio.StopMusic()
        | ValueNone -> ())

    /// <summary>Pauses the music at the current position.</summary>
    /// <param name="ctx">The game context.</param>
    member this.pauseMusic(ctx: GameContext) : unit =
      this.post(fun () ->
        match GameContext.tryGetService<IAudio> ctx with
        | ValueSome audio -> audio.PauseMusic()
        | ValueNone -> ())

    /// <summary>Resumes the music from where it was paused.</summary>
    /// <param name="ctx">The game context.</param>
    member this.resumeMusic(ctx: GameContext) : unit =
      this.post(fun () ->
        match GameContext.tryGetService<IAudio> ctx with
        | ValueSome audio -> audio.ResumeMusic()
        | ValueNone -> ())

    /// <summary>Seeks the music to an absolute time in seconds.</summary>
    /// <param name="ctx">The game context.</param>
    /// <param name="seconds">The position to jump to, counted from the track start.</param>
    member this.seekMusic(ctx: GameContext, seconds: float32) : unit =
      this.post(fun () ->
        match GameContext.tryGetService<IAudio> ctx with
        | ValueSome audio -> audio.SeekMusic seconds
        | ValueNone -> ())

    /// <summary>Sets the music volume live — the music slider. This is also the volume a later fade-in fades to.</summary>
    /// <param name="ctx">The game context.</param>
    /// <param name="volume">Music volume (1.0 = full, 0.0 = silent).</param>
    member this.setMusicVolume(ctx: GameContext, volume: float32) : unit =
      this.post(fun () ->
        match GameContext.tryGetService<IAudio> ctx with
        | ValueSome audio -> audio.SetMusicVolume volume
        | ValueNone -> ())

    /// <summary>Fades the music in over <paramref name="seconds"/>, toward the current music slider value — the last volume passed to <c>setMusicVolume</c> (1.0 if never set), read from the service at drain time.</summary>
    /// <param name="ctx">The game context.</param>
    /// <param name="seconds">Fade duration in seconds.</param>
    member this.fadeMusicIn(ctx: GameContext, seconds: float32) : unit =
      this.post(fun () ->
        match GameContext.tryGetService<IAudio> ctx with
        | ValueSome audio -> audio.FadeMusic(audio.MusicVolume(), seconds)
        | ValueNone -> ())

    /// <summary>Fades the music out over <paramref name="seconds"/>; the music stops when the fade completes.</summary>
    /// <param name="ctx">The game context.</param>
    /// <param name="seconds">Fade duration in seconds.</param>
    member this.fadeMusicOut(ctx: GameContext, seconds: float32) : unit =
      this.post(fun () ->
        match GameContext.tryGetService<IAudio> ctx with
        | ValueSome audio -> audio.FadeMusic(0.0f, seconds)
        | ValueNone -> ())

    /// <summary>Sets the master volume that scales the whole mix — every sound effect and the music channel, live (see <see cref="M:Mibo.Audio.IAudio.SetMasterVolume"/>).</summary>
    /// <param name="ctx">The game context.</param>
    /// <param name="volume">Master volume (1.0 = full).</param>
    member this.setMasterVolume(ctx: GameContext, volume: float32) : unit =
      this.post(fun () ->
        match GameContext.tryGetService<IAudio> ctx with
        | ValueSome audio -> audio.SetMasterVolume volume
        | ValueNone -> ())
