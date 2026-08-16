---
title: Adaptive Services
category: Adaptive
categoryindex: 3
index: 5
---

# Service Composition in Adaptive Programs

The [environment pattern](../services.html) applies to adaptive programs unchanged: build services **before** the program, hold them in an `Env` record, and thread them by partial application. What changes is where context-dependent initialization goes — and it is cleaner than in MVU: `boot ctx` receives the `GameContext` before anything else runs, so the classic circular dependency (a service that needs `GameContext`/`IAssets`) disappears.

```fsharp
type Env = {
    Network: INetworkService
    Leaderboard: ILeaderboardService
}

let main _ =
    // 1. Create the environment independent of the program
    let env = { Network = Network.create "https://api.example.com"; Leaderboard = Leaderboard.create () }

    // 2. boot runs FIRST with the frame context — the natural home
    //    for Init(ctx)-style work (asset-dependent caches, listeners)
    let boot (ctx: AdaptiveFrameContext) =
        env.Network.Connect()

    let getState = // ... your state cell
    let program =
        AdaptiveProgram.mkProgram
            (fun ctx -> boot ctx; AdaptiveInit.ofFrameBuilder (Frame.force ctx getState))
            (fun ctx gameTime -> Application.update env getState ctx gameTime)
        |> AdaptiveProgram.withRenderer (fun () -> Renderer2D.create (view env))
```

Async work follows the queue, not `Cmd.ofAsync`: start it with `ctx.Intents.postTask`/`postAsync`, and the completion lands back through `post` — drained on the owner thread, in order, without blocking the step.

## Two ways to reach services

| Route | When |
|---|---|
| `Env` record, closed over by `update`/`view` | your own services, known at composition time (the default) |
| `ctx.Context |> GameContext.getService<'T>` | services registered by the host or the framework (`IAssets`, `IInput`) |

Registration happens in two places: hosts register what they own (the input service, the asset cache), and your program can add custom services with `AdaptiveProgram.withServiceRegistration`, which hands you the `GameContext` to register into before init runs.

## Pitfalls specific to adaptive programs

* **Do not write roots from background threads.** Services that complete on foreign threads must hand results back through the post ring (`ctx.Intents.postTask` wraps this for you). Owner-thread confinement is a library invariant — debug builds throw on violations.
* **Do not force projections from services.** Reads of adaptive state belong to the phases that own them: `update` may `AVal.getValue` cold paths, the frame builder resolves outputs once per step. A service that forces mid-flight sees a half-stepped world.
* **Keep diagnostics as owned state.** Counters and cost samplers live on your state (or the frame), written by `update` or the frame builder only — never module-level globals mutated from inside projection lambdas. Projection functions must stay pure; hidden writes there are invisible, untestable, and reset only by side effect.
