namespace Mibo.Elmish

open Mibo.Audio

// ─────────────────────────────────────────────────────────────────────────────
// Audio commands: the MVU helpers over the portable IAudio service.
//
// Every helper resolves IAudio with GameContext.tryGetService and yields
// Cmd.none when the service is absent — headless programs and test runs play
// no sound and need no audio device. One effect closure per command, same
// cost profile as Cmd.ofEffect.
//
// Keys are game vocabulary ("jump", "overworld") — whatever the program
// registered in its bank via withBank. There are no mix groups: a sound-effect
// "bus" is model state the game multiplies into the voice at the play site,
// and music is a single channel with one live knob (setMusicVolume).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Audio commands for MVU programs: play sound effects with per-play knobs,
/// drive the single music channel (play, pause, seek, fades), and set
/// volumes. Resolve nothing yourself — the helpers find the host-registered
/// <see cref="T:Mibo.Audio.IAudio"/> service through the
/// <see cref="T:Mibo.Elmish.GameContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// The sounds and tracks are registered in the program's bank
/// (<c>RaylibProgram.withBank</c> / <c>MonoGameProgram.withBank</c>) before
/// <c>init</c> runs. Playing an unregistered key is a silent no-op, as is
/// every helper when no audio service is registered (headless runs).
/// </para>
/// <para>
/// <c>fadeMusicIn</c> fades toward the last volume passed to
/// <c>setMusicVolume</c> (1.0 if never set), so the music slider and fades
/// compose: <c>setMusicVolume ctx 0.8f</c> once, then
/// <c>fadeMusicIn ctx 2.0f</c> after every track start.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// | Jump -&gt; { model with Vy = jumpSpeed }, Audio.play ctx "jump"
/// | LevelStart -&gt;
///     model,
///     Cmd.batch
///       [ Audio.playMusic ctx "overworld"
///         Audio.fadeMusicIn ctx 2.0f ]
/// </code>
/// </example>
[<RequireQualifiedAccess>]
module Audio =

  /// <summary>Plays the sound registered under <paramref name="key"/> with the default voice.</summary>
  /// <param name="ctx">The game context (the helpers resolve the audio service from it).</param>
  /// <param name="key">The key the sound was registered under in the bank.</param>
  let play (ctx: GameContext) (key: string) : Cmd<'Msg> =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> Cmd.ofEffect(Effect(fun _ -> audio.Play key))
    | ValueNone -> Cmd.none

  /// <summary>Plays the sound registered under <paramref name="key"/> with per-play knobs.</summary>
  /// <param name="ctx">The game context.</param>
  /// <param name="key">The key the sound was registered under in the bank.</param>
  /// <param name="voice">Volume, pan, and pitch for this playback (start from <see cref="M:Mibo.Audio.Voice.center"/> and update fields).</param>
  let playWith (ctx: GameContext) (key: string) (voice: Voice) : Cmd<'Msg> =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> Cmd.ofEffect(Effect(fun _ -> audio.Play(key, voice)))
    | ValueNone -> Cmd.none

  /// <summary>Stops every playing sound effect. Music keeps playing.</summary>
  /// <param name="ctx">The game context.</param>
  let stopAll(ctx: GameContext) : Cmd<'Msg> =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> Cmd.ofEffect(Effect(fun _ -> audio.StopAllSounds()))
    | ValueNone -> Cmd.none

  /// <summary>Starts the music registered under <paramref name="key"/> as the background track, looping. Replaces the track that is playing.</summary>
  /// <param name="ctx">The game context.</param>
  /// <param name="key">The key the track was registered under in the bank.</param>
  let playMusic (ctx: GameContext) (key: string) : Cmd<'Msg> =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> Cmd.ofEffect(Effect(fun _ -> audio.PlayMusic key))
    | ValueNone -> Cmd.none

  /// <summary>Starts the music registered under <paramref name="key"/> once through, then stops. Replaces the track that is playing.</summary>
  /// <param name="ctx">The game context.</param>
  /// <param name="key">The key the track was registered under in the bank.</param>
  let playMusicOnce (ctx: GameContext) (key: string) : Cmd<'Msg> =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> Cmd.ofEffect(Effect(fun _ -> audio.PlayMusicOnce key))
    | ValueNone -> Cmd.none

  /// <summary>Stops the music and resets it to its start.</summary>
  /// <param name="ctx">The game context.</param>
  let stopMusic(ctx: GameContext) : Cmd<'Msg> =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> Cmd.ofEffect(Effect(fun _ -> audio.StopMusic()))
    | ValueNone -> Cmd.none

  /// <summary>Pauses the music at the current position.</summary>
  /// <param name="ctx">The game context.</param>
  let pauseMusic(ctx: GameContext) : Cmd<'Msg> =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> Cmd.ofEffect(Effect(fun _ -> audio.PauseMusic()))
    | ValueNone -> Cmd.none

  /// <summary>Resumes the music from where it was paused.</summary>
  /// <param name="ctx">The game context.</param>
  let resumeMusic(ctx: GameContext) : Cmd<'Msg> =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> Cmd.ofEffect(Effect(fun _ -> audio.ResumeMusic()))
    | ValueNone -> Cmd.none

  /// <summary>Seeks the music to an absolute time in seconds.</summary>
  /// <param name="ctx">The game context.</param>
  /// <param name="seconds">The position to jump to, counted from the track start.</param>
  let seekMusic (ctx: GameContext) (seconds: float32) : Cmd<'Msg> =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio -> Cmd.ofEffect(Effect(fun _ -> audio.SeekMusic seconds))
    | ValueNone -> Cmd.none

  /// <summary>Sets the music volume live — the music slider. This is also the volume a later <see cref="M:Mibo.Elmish.Audio.fadeMusicIn"/> fades to.</summary>
  /// <param name="ctx">The game context.</param>
  /// <param name="volume">Music volume (1.0 = full, 0.0 = silent).</param>
  let setMusicVolume (ctx: GameContext) (volume: float32) : Cmd<'Msg> =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio ->
      Cmd.ofEffect(Effect(fun _ -> audio.SetMusicVolume volume))
    | ValueNone -> Cmd.none

  /// <summary>Fades the music in over <paramref name="seconds"/>, toward the current music slider value — the last volume passed to <see cref="M:Mibo.Elmish.Audio.setMusicVolume"/> (1.0 if never set), read from the service.</summary>
  /// <param name="ctx">The game context.</param>
  /// <param name="seconds">Fade duration in seconds.</param>
  let fadeMusicIn (ctx: GameContext) (seconds: float32) : Cmd<'Msg> =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio ->
      let target = audio.MusicVolume()
      Cmd.ofEffect(Effect(fun _ -> audio.FadeMusic(target, seconds)))
    | ValueNone -> Cmd.none

  /// <summary>Fades the music out over <paramref name="seconds"/>; the music stops when the fade completes.</summary>
  /// <param name="ctx">The game context.</param>
  /// <param name="seconds">Fade duration in seconds.</param>
  let fadeMusicOut (ctx: GameContext) (seconds: float32) : Cmd<'Msg> =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio ->
      Cmd.ofEffect(Effect(fun _ -> audio.FadeMusic(0.0f, seconds)))
    | ValueNone -> Cmd.none

  /// <summary>Sets the master volume that scales the whole mix — every sound effect and the music channel, live (see <see cref="M:Mibo.Audio.IAudio.SetMasterVolume"/>).</summary>
  /// <param name="ctx">The game context.</param>
  /// <param name="volume">Master volume (1.0 = full).</param>
  let setMasterVolume (ctx: GameContext) (volume: float32) : Cmd<'Msg> =
    match GameContext.tryGetService<IAudio> ctx with
    | ValueSome audio ->
      Cmd.ofEffect(Effect(fun _ -> audio.SetMasterVolume volume))
    | ValueNone -> Cmd.none
