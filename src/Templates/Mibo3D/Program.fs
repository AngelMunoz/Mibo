module Mibo3D.Program

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Input

// ─────────────────────────────────────────────────────────────
// Model
// ─────────────────────────────────────────────────────────────

type Model = {
    Position: Vector3
    Input: ActionState<GameAction>
}

// ─────────────────────────────────────────────────────────────
// Input
// ─────────────────────────────────────────────────────────────

type GameAction =
    | MoveForward
    | MoveBackward
    | MoveLeft
    | MoveRight

let inputMap =
    InputMap.empty
    |> InputMap.key MoveForward Keys.W
    |> InputMap.key MoveForward Keys.Up
    |> InputMap.key MoveBackward Keys.S
    |> InputMap.key MoveBackward Keys.Down
    |> InputMap.key MoveLeft Keys.A
    |> InputMap.key MoveLeft Keys.Left
    |> InputMap.key MoveRight Keys.D
    |> InputMap.key MoveRight Keys.Right

// ─────────────────────────────────────────────────────────────
// Messages
// ─────────────────────────────────────────────────────────────

type Msg =
    | Tick of GameTime
    | InputChanged of ActionState<GameAction>

// ─────────────────────────────────────────────────────────────
// Init
// ─────────────────────────────────────────────────────────────

let init (ctx: GameContext) : struct (Model * Cmd<Msg>) =
    struct ({ Position = Vector3.Zero; Input = ActionState.empty }, Cmd.none)

// ─────────────────────────────────────────────────────────────
// Update
// ─────────────────────────────────────────────────────────────

let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
    match msg with
    | InputChanged input ->
        struct ({ model with Input = input }, Cmd.none)

    | Tick gt ->
        let dt = float32 gt.ElapsedGameTime.TotalSeconds
        let speed = 5.0f
        let mutable velocity = Vector3.Zero

        if model.Input.Held.Contains MoveForward then velocity.Z <- velocity.Z - speed
        if model.Input.Held.Contains MoveBackward then velocity.Z <- velocity.Z + speed
        if model.Input.Held.Contains MoveLeft then velocity.X <- velocity.X - speed
        if model.Input.Held.Contains MoveRight then velocity.X <- velocity.X + speed

        struct ({ model with Position = model.Position + (velocity * dt) }, Cmd.none)

// ─────────────────────────────────────────────────────────────
// View
// ─────────────────────────────────────────────────────────────

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd3D>) =
    // Camera
    let cameraPos = model.Position + Vector3(0.f, 5.f, 10.f)
    let camera = Camera3D.lookAt cameraPos model.Position Vector3.Up (MathHelper.ToRadians 45.f) (800.f / 600.f) 0.1f 100.f
    Draw3D.camera camera buffer

    // Draw a simple quad for the player if no model
    // We'll create a simple texture on the fly for the template
    let texture = Assets.getOrCreate "texture" (fun gd ->
        let t = new Texture2D(gd, 1, 1)
        t.SetData([| Color.Red |])
        t
    ) ctx

    let quad = Draw3D.quadOnXZ model.Position (Vector2(1.f, 1.f))
    Draw3D.quad texture quad buffer

// ─────────────────────────────────────────────────────────────
// Program
// ─────────────────────────────────────────────────────────────

[<EntryPoint>]
let main _ =
    let program =
        Program.mkProgram init update
        |> Program.withAssets
        |> Program.withRenderer (Batch3DRenderer.create view)
        |> Program.withInput
        |> Program.withSubscription (InputMapper.subscribeStatic inputMap InputChanged)
        |> Program.withTick Tick
        |> Program.withConfig (fun (game, graphics) ->
            game.Window.Title <- "Mibo 3D Game"
            game.IsMouseVisible <- true
            graphics.PreferredBackBufferWidth <- 800
            graphics.PreferredBackBufferHeight <- 600
        )

    use game = new ElmishGame<Model, Msg>(program)
    game.Run()
    0
