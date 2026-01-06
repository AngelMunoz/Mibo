module Gamino.Core

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open Gamino.Elmish

// --- Domain ---

type Model = {
    Player: Gamino.Player.Model
    Particles: Gamino.Particles.Model
}

type Msg =
    | Tick of GameTime
    | PlayerMsg of Gamino.Player.Msg

// --- App Logic ---

let init () =
    let struct(pModel, _) = Gamino.Player.init (Vector2(100.f, 100.f)) Color.Red
    let struct(partModel, _) = Gamino.Particles.init()
    
    let initialModel = {
        Player = pModel
        Particles = partModel
    }
    struct (initialModel, Cmd.none)

let update (msg: Msg) (model: Model) =
    match msg with
    | Tick gt ->
        let dt = float32 gt.ElapsedGameTime.TotalSeconds
        
        // 1. Update Player (Time Tick)
        let struct(newPlayer, _) = 
            Gamino.Player.update (Gamino.Player.Tick dt) model.Player

        // 2. Update Particles (Time Tick)
        let struct(newParticles, _) = 
            Gamino.Particles.update (Gamino.Particles.Update dt) model.Particles
        
        // 3. Orchestration: Check if Player is "Firing"
        let struct(finalParticles, _) = 
            if newPlayer.IsFiring then 
                let spawnPos = newPlayer.Player.Position
                Gamino.Particles.update (Gamino.Particles.Emit (spawnPos, 5)) newParticles
            else
                struct(newParticles, Cmd.none)

        struct ({ Player = newPlayer; Particles = finalParticles }, Cmd.none)

    | PlayerMsg pMsg ->
        let struct(newPlayer, _) = Gamino.Player.update pMsg model.Player
        struct ({ model with Player = newPlayer }, Cmd.none)

// --- Subscriptions ---

let subscribe (model: Model) =
    // Compose subscriptions
    Sub.batch [
        // Map Player subs to Main Msg
        Gamino.Player.subscribe model.Player |> Sub.map "Player" PlayerMsg
    ]

// --- Composition Root ---

let view (model: Model) (buffer: RenderBuffer) =
    Gamino.Player.view model.Player buffer
    Gamino.Particles.view model.Particles buffer

// --- Entry Point ---

type MiboGame() as this =
    // We register the subscription function here
    inherit ElmishGame<Model, Msg>(
        Program.mkProgram init update view
        |> Program.withSubscription subscribe
    )
    
    // Instantiate services
    let inputService = Gamino.Input.InputService()

    do
        // Register services
        this.RegisterService(inputService)
    
    override this.Update(gameTime) =
        // The Engine drives the Time Tick
        base.Dispatch (Tick gameTime)
        base.Update(gameTime)

    override this.LoadContent() =
        base.LoadContent()
        // Initialize subsystem resources
        Gamino.Player.Resources.loadContent this.GraphicsDevice
        Gamino.Particles.Resources.loadContent this.GraphicsDevice

[<EntryPoint>]
let main argv =
    use game = new MiboGame()
    game.Run()
    0