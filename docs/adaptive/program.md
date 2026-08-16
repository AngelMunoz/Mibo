---
title: Adaptive Programs
category: Adaptive
categoryindex: 3
index: 2
---

# Adaptive Programs & Hosts

The adaptive counterpart of `Program<'Msg,'Model>` is `AdaptiveProgram<'Frame>` — with the message/command machinery removed. There is no `Msg` and no `Cmd`: handlers write roots and run effects directly, and reactions are deferred through an **intent queue**.

```fsharp
open Mibo.Adaptive
open Mibo.Elmish

let program =
    AdaptiveProgram.mkProgram
        (fun (ctx: AdaptiveFrameContext) ->
            // init: build the state, return the frame builder + subscriptions
            boot ctx
            AdaptiveInit.ofFrameBuilder (Frame.force ctx getState)
            |> AdaptiveInit.withSubscriptions (Input.subscriptions ctx))
        (fun (ctx: AdaptiveContext) (gameTime: GameTime) ->
            // update: tick the sim, post reactions
            Application.update getState ctx gameTime)
    |> AdaptiveProgram.withConfig (fun _ -> config)
    |> AdaptiveProgram.withRenderer (fun () -> Renderer2D.create view)
```

## The contexts

Every phase sees exactly what it may use — the context split *is* the enforcement:

| Context | Members | Seen by |
|---|---|---|
| `AdaptiveFrameContext` | `Time` (the framework's time root), `ExitRequested`, `Context` (`GameContext`: window size + registered services) | init, frame builder, subscriptions, view |
| `AdaptiveContext` | everything above **plus** `Intents` (the intent queue) | update only |

The update phase reacts to what it read and defers work; the frame builder and projection construction see only the queue-less context, so **the force phase cannot enqueue work — the design makes it impossible**.

`ctx.Time` is a `cval<GameTime>` written by the runner at the start of every step. The frame reads it at force time, so the draw side animates on the sim's clock, never on a backend-specific one. Set `ExitRequested` to stop the runner — the counterpart of `Cmd.signalExit`.

## The intent queue (the `Cmd` replacement)

`ctx.Intents` is a queue of `unit -> unit` work items, each drained at a framework-owned moment:

| Member | Runs |
|---|---|
| `post` | after `Update`, before the frame is forced (the default — the old `Cmd` batch order) |
| `postNextFrame` | at the top of the next step |
| `postTask` / `postAsync` | background work; completion returns via `post` |

The one-way rule that keeps event translation acyclic: systems emit events as data, update translates them by posting handlers, and only the handlers write other systems' roots.

## The step order

One step, in order (same on every host):

1. Pump cross-thread posts (`Posting.pump` — only if you post from other threads).
2. Drain the next-frame and pre-step lanes.
3. Diff subscriptions (attach/detach `AdaptiveSub`s).
4. Write the time root (`ctx.Time`).
5. Run `Update`.
6. Drain the intent queue.
7. **Force the frame** — your frame builder resolves every output projection once.

Fixed-step programs run `Update` + drain per sub-step and force once at the end (`AdaptiveProgram.withFixedStep`).

## The frame builder

`AdaptiveInit.ofFrameBuilder` takes a `unit -> 'Frame`. Build `'Frame` as a **struct** with everything the renderer needs: transient views (`AMap.getValue` snapshots), scalars, and by-reference payloads for the non-adaptive partners (particle pools, the camera, the map). Resolve each projection exactly once — after this, drawing is plain struct reads with no graph access.

The builder closes over whatever it needs (a state cell, the context) — it is re-invoked every step, so a restart can swap the state under it without rebuilding the program.

## Hosts

| Host | Backend | Loop |
|---|---|---|
| `AdaptiveRaylibGame<'Frame>` | raylib | poll input → step → renderers draw the forced frame |
| `AdaptiveMonoGameGame<'Frame>` | MonoGame (all clients) | `Update(gameTime)` steps, `Draw` renders |
| `AdaptiveHeadless<'Frame>` | none (tests, servers) | `Step`, `StepN`, `StepUntil`, `Run`, `RunAsync` |

MonoGame programs wrap with `AdaptiveMonoGameProgram.ofProgram` for device-level configuration. The headless runner exposes `Frame`, `GameTime` and `Post` for test assertions — the same step order, no window.

Renderers are registered in draw order with `AdaptiveProgram.withRenderer` and receive `(ctx, frame, buffer)` — the same renderer surface MVU uses, reading the frame instead of a model.

## Composition checklist

* `mkProgram init update` — the two-phase program.
* `withConfig` — window title/size, target FPS.
* `withRenderer` — one per pass, in draw order.
* `withObserver` / `withInput` — per-step observer callbacks (diagnostics), the input service.
* `withServiceRegistration` — register custom services into `GameContext` (see [Adaptive Services](services.html)).
* `withAssetsBasePath`, `withFixedStep` — asset root, fixed timestep.

For the graph itself — `cval`, `aval`, `cmap`, `amap`, transactions, cross-thread posting — see the [Mibo.Adaptive](../mibo-adaptive/overview.html) section.
