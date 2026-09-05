---
title: Audio
category: Amenities
categoryindex: 12
index: 6
---

# Audio (sound effects + music)

Mibo ships a backend-agnostic audio module: a game built on either backend plays sound effects, loops music, fades, pans, and (on MonoGame) uses 3D positioning — without touching `Raylib_cs` or `Microsoft.Xna.Framework.Audio` types in game code.

The portable contract is the `IAudio` interface (`Mibo.Audio`, in `Mibo.Core`). Both backends register a service that implements it, so the same game code runs on raylib and MonoGame:

```fsharp
let audio = GameContext.getService<IAudio> ctx
audio.Play "jump"
audio.PlayMusic "overworld"      // loops
audio.FadeMusic(0.0f, 1.5f)      // fade out over 1.5 s, then stop
```

> _**NOTE**_: You rarely resolve `IAudio` by hand. MVU programs have the [command helpers](#mvu-commands) (`Audio.play ctx "jump"`), adaptive programs have the [intent helpers](#adaptive-intents) (`ctx.Intents.play(ctx.Context, "jump")`).

## How it works

- **Every windowed host registers `IAudio` before your `init` runs** (like `IAssets`), advances it once per frame, and disposes it on shutdown. You never call `Tick` — the host does.
- **Keys are game vocabulary** ("jump", "overworld"). You register what each key loads in your program's bank; after that, every helper and the service itself speak only keys. Playing an unregistered key is a silent no-op, which keeps headless runs and tests safe.
- **Sound effects overlap.** Each key owns a pool of 8 playback slots played round-robin, so a machine-gun key can layer itself up to 8 times before the oldest playback is stolen.
- **Music is a single channel.** Starting a track replaces the one playing (`MediaPlayer` is a singleton on MonoGame, so the portable contract promises one track at a time).
- **No mix groups, by design.** A "group volume" is your model state: multiply it into the voice at the play site (see [the slider pattern](#the-slider-pattern-sfx-groups-are-model-state)).

## Registering a bank

Sounds load when the program starts — before your `init` runs. Declare the whole bank in one call on the program builder:

```fsharp
// raylib: a plain file path is the source
mkProgram
|> RaylibProgram.withBank [
    Sound ("jump", "assets/jump.wav")
    Music ("overworld", "assets/overworld.ogg")
  ]
```

```fsharp
// MonoGame: each entry names its source explicitly — no guessing
mkProgram
|> MonoGameProgram.ofProgram
|> MonoGameProgram.withBank [
    Sound ("jump", Pipeline "audio/jump")        // MGCB asset
    Music ("overworld", File "music/overworld.ogg")
  ]
```

The bank is an ordinary F# value, so large banks stay one call — generate the list:

```fsharp
let sfxBank =
  [ for file in Directory.GetFiles("assets/sfx", "*.wav") ->
      Sound(Path.GetFileNameWithoutExtension file, file) ]

mkProgram |> RaylibProgram.withBank sfxBank
```

Adaptive programs bind the bank themselves through `AdaptiveProgram.withServiceRegistration`, calling `RegisterSound`/`RegisterMusic` on the backend's audio service (`AudioService` in `Mibo.Elmish`).

### Path rules

| | raylib | MonoGame |
|---|---|---|
| Sound source | file path (relative to `withAssetsBasePath` when set) | `Pipeline name` (MGCB asset, no extension) or `File path` (loose **WAV**) |
| Music source | file path (same base-path rule) | `Pipeline name` or `File path` (loaded through `Song.FromUri`, platform decoders) |
| Formats | WAV, OGG, MP3, FLAC, QOA | Pipeline: every format the pipeline imports. Loose: WAV for sound effects; music depends on the platform decoder |

> _**NOTE**_: On MonoGame, the pipeline is the guaranteed path. Loose files must ship with the game (copy them to the output directory yourself), and loose sound effects are WAV-only. A missing pipeline asset throws at startup, where a configuration mistake belongs.

## Voices: per-play knobs

`Voice` is a struct (zero allocation) with volume, pan, and pitch. Start from `Voice.center` and update fields:

```fsharp
Audio.play ctx "jump"                                    // default voice
Audio.playWith ctx "land" (Voice.ofVolume 0.7f)          // quieter
Audio.playWith ctx "whoosh" { Voice.center with Pan = -1.0f }  // full left
Audio.playWith ctx "hit" (Voice.at 0.5f 0.3f)            // volume + pan
```

### 2D positional audio (portable)

`Attenuation2D.compute` turns a listener facing plus positions into a `Voice`: volume falls off linearly to zero at `maxDistance`, and pan follows the direction to the source. Works on both backends.

```fsharp
let voice =
  Attenuation2D.compute(facingRad, player.X, player.Y) (enemy.X, enemy.Y) 640.0f

Audio.playWith ctx "enemy-step" voice
```

## Music channel

| Helper (MVU) | Intent (adaptive) | Service call | Effect |
|---|---|---|---|
| `Audio.playMusic ctx key` | `intents.playMusic(ctx, key)` | `PlayMusic key` | start looping (the background-music case) |
| `Audio.playMusicOnce ctx key` | `intents.playMusicOnce(ctx, key)` | `PlayMusicOnce key` | play through, then stop |
| `Audio.stopMusic ctx` | `intents.stopMusic(ctx)` | `StopMusic()` | stop and reset to the start |
| `Audio.pauseMusic ctx` / `resumeMusic ctx` | `intents.pauseMusic(ctx)` / `resumeMusic(ctx)` | `PauseMusic()` / `ResumeMusic()` | park / continue |
| `Audio.seekMusic ctx 12.0f` | `intents.seekMusic(ctx, 12.0f)` | `SeekMusic 12.0f` | jump to an absolute time |
| — (read, not a command) | — | `MusicPosition()` | playback position in seconds |
| `Audio.setMusicVolume ctx v` | `intents.setMusicVolume(ctx, v)` | `SetMusicVolume v` | the music slider, live |
| `Audio.fadeMusicIn ctx 2.0f` | `intents.fadeMusicIn(ctx, 2.0f)` | `FadeMusic(last, 2.0f)` | fade in toward the last `setMusicVolume` value |
| `Audio.fadeMusicOut ctx 1.5f` | `intents.fadeMusicOut(ctx, 1.5f)` | `FadeMusic(0.0f, 1.5f)` | fade out; the music stops at the end |

\* `MusicPosition` reads a value, so it is only on the service (call it from `update`/`Subscribe` directly), not a command.

A fade to (or below) zero stops the music when it completes. A fade to a positive volume leaves it playing. A newly started track cancels any running fade and starts at the slider volume — a fade-out ends the track that was playing; it is not a sticky volume.

`Audio.setMasterVolume ctx v` scales every sound effect (music volume is the separate `setMusicVolume` knob).

## MVU commands

Every helper resolves the service from the `GameContext` and yields `Cmd.none` when audio is absent (headless runs), with the same cost profile as `Cmd.ofEffect`:

```fsharp
| Jump       -> { model with Vy = jumpSpeed }, Audio.play ctx "jump"
| Land x     -> { model with ... }, Audio.playWith ctx "land" (Voice.at 0.7f (x / halfScreenWidth))
| LevelStart -> model, Cmd.batch [ Audio.playMusic ctx "overworld"; Audio.fadeMusicIn ctx 2.0f ]
| GameOver   -> model, Audio.fadeMusicOut ctx 1.5f
```

## Adaptive intents

The adaptive set mirrors the MVU set as members on `IntentQueue` — they hang off the `ctx.Intents` value you already hold, post one closure, and resolve the service at drain time (Headless-safe):

```fsharp
let update ctx state msg =
  match msg with
  | Jump ->
      ctx.Intents.play(ctx.Context, "jump")
      { state with Vy = jumpSpeed }
```

## The slider pattern: sfx "groups" are model state

The framework has no mix groups or audio categories. The composition is ordinary F# in your model:

```fsharp
// options menu: two model fields, two knobs
let sfx key = Audio.playWith ctx key { Voice.center with Volume = model.SfxVolume }

| Slider v ->
    { model with SfxVolume = v },
    Audio.setMusicVolume ctx v      // music slider, live
```

## MonoGame 3D audio

MonoGame extends the portable contract with a listener model (raylib has no listener, so 3D stays backend-only). Resolve the extended interface — the same service, more members:

```fsharp
let audio = GameContext.getService<MonoGameAudio> ctx

// once per frame (or when the camera moves): the camera is the listener
audio.SetListener(cameraPos, cameraForward, cameraUp, Vector3.Zero)

audio.Play3D("enemy-step", enemyPos)                    // position only
audio.Play3D("car", carPos, carVelocity)                // + Doppler velocity
audio.Play3D("shot", shotPos, ?voice = Voice.ofVolume 0.8f)

audio.DistanceScale <- 40.0f    // world-units-to-meters scaling
audio.DopplerScale <- 1.0f      // 0 turns Doppler off
```

Distance attenuation, panning, and Doppler shift come from the listener/emitter geometry — pan from the voice is ignored on 3D plays. Moving the listener re-attenuates live plays automatically on the next frame; a play's own position is fixed for its lifetime.

## Format notes

- **raylib**: whatever the raylib build decodes — WAV, OGG, MP3, FLAC, QOA. Sounds load fully into memory (shared per key through the alias pool); music streams from disk.
- **MonoGame**: pipeline assets are the guaranteed path for both sounds and music. Loose sound effects are WAV-only (`SoundEffect.FromFile`); loose music goes through `Song.FromUri` with the platform decoders.

## See also

- [Assets](assets.html) — loading textures/fonts/models per backend (audio banks follow the same base-path rules on raylib).
- [Input](input.html) — the other amenity service every host registers.
