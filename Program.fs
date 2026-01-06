module Gamino.Core

open Microsoft.Xna.Framework
open Gamino.Elmish

[<Struct>]
type Model =
    { Player: Player.Model
      Particles: Particles.Model }

[<Struct>]
type Msg =
    | Tick of gt: GameTime
    | PlayerMsg of pm: Player.Msg

let init () =
    let struct (pModel, _) = Player.init (Vector2(100.f, 100.f)) Color.Red
    let struct (partModel, _) = Particles.init ()

    let initialModel =
        { Player = pModel
          Particles = partModel }

    struct (initialModel, Cmd.none)

let update (msg: Msg) (model: Model) =
    match msg with
    | Tick gt ->
        let dt = float32 gt.ElapsedGameTime.TotalSeconds

        // 1. Update Player (Time Tick)
        let struct (newPlayer, _) = Player.update (Player.Tick dt) model.Player

        // 2. Update Particles (Time Tick)
        let struct (newParticles, _) =
            Particles.update (Particles.Update dt) model.Particles

        // 3. Orchestration: Check if Player is "Firing"
        let struct (finalParticles, _) =
            if newPlayer.IsFiring then
                let spawnPos = newPlayer.Player.Position
                Particles.update (Particles.Emit(spawnPos, 5)) newParticles
            else
                struct (newParticles, Cmd.none)

        struct ({ Player = newPlayer
                  Particles = finalParticles },
                Cmd.none)

    | PlayerMsg pMsg ->
        let struct (newPlayer, _) = Gamino.Player.update pMsg model.Player
        struct ({ model with Player = newPlayer }, Cmd.none)

// --- Subscriptions ---

let subscribe (model: Model) =
    // Compose subscriptions
    Sub.batch [ Player.subscribe model.Player |> Sub.map "Player" PlayerMsg ]

// --- Composition Root ---

let view (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
    Gamino.Player.view model.Player buffer
    Gamino.Particles.view model.Particles buffer

[<EntryPoint>]
let main argv =
    let program =
        Program.mkProgram init update
        |> Program.withSubscription subscribe
        |> Program.withTick Tick
        |> Program.withLoadContent Player.Resources.loadContent
        |> Program.withLoadContent Particles.Resources.loadContent
        |> Program.withRenderer (Batch2DRenderer.create view)
        |> Program.withService (fun _ -> Input.InputService() :> IEngineService)

    use game = new ElmishGame<Model, Msg>(program)
    game.Run()
    0
