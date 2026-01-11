module Mibo3D.Program

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Input

// ─────────────────────────────────────────────────────────────
// Input
// ─────────────────────────────────────────────────────────────

type GameAction =
  | MoveForward
  | MoveBackward
  | MoveLeft
  | MoveRight
  | MoveUp
  | MoveDown

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
  |> InputMap.key MoveUp Keys.Space
  |> InputMap.key MoveDown Keys.LeftShift

// ─────────────────────────────────────────────────────────────
// Model
// ─────────────────────────────────────────────────────────────

type Model = {
  Position: Vector3
  Velocity: Vector3
  Input: ActionState<GameAction>
}

// ─────────────────────────────────────────────────────────────
// Messages
// ─────────────────────────────────────────────────────────────

type Msg =
  | Tick of GameTime
  | InputChanged of ActionState<GameAction>

// ─────────────────────────────────────────────────────────────
// Init
// ─────────────────────────────────────────────────────────────

let init(ctx: GameContext) : struct (Model * Cmd<Msg>) =
  let model = {
    Position = Vector3.Zero
    Velocity = Vector3(2.f, 1.5f, 2.f)
    Input = ActionState.empty
  }

  model, Cmd.none

// ─────────────────────────────────────────────────────────────
// Update
// ─────────────────────────────────────────────────────────────

let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
  match msg with
  | InputChanged input -> { model with Input = input }, Cmd.none

  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    // Manual movement
    let speed = 5.0f
    let mutable manualVelocity = Vector3.Zero

    if model.Input.Held.Contains MoveForward then
      manualVelocity.Z <- manualVelocity.Z - speed

    if model.Input.Held.Contains MoveBackward then
      manualVelocity.Z <- manualVelocity.Z + speed

    if model.Input.Held.Contains MoveLeft then
      manualVelocity.X <- manualVelocity.X - speed

    if model.Input.Held.Contains MoveRight then
      manualVelocity.X <- manualVelocity.X + speed

    if model.Input.Held.Contains MoveUp then
      manualVelocity.Y <- manualVelocity.Y + speed

    if model.Input.Held.Contains MoveDown then
      manualVelocity.Y <- manualVelocity.Y - speed

    // Bouncing logic
    let mutable velocity = model.Velocity

    let mutable position =
      model.Position + (velocity * dt) + (manualVelocity * dt)

    let bounds = 5.f

    if position.X < -bounds || position.X > bounds then
      velocity.X <- -velocity.X

    if position.Z < -bounds || position.Z > bounds then
      velocity.Z <- -velocity.Z

    if position.Y < -bounds || position.Y > bounds then
      velocity.Y <- -velocity.Y

    {
      model with
          Position = position
          Velocity = velocity
    },
    Cmd.none

// ─────────────────────────────────────────────────────────────
// View
// ─────────────────────────────────────────────────────────────

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd3D>) =
  // Static Camera to see the whole bounds
  let camera =
    Camera3D.lookAt
      (Vector3(12.f, 12.f, 12.f))
      Vector3.Zero
      Vector3.Up
      (MathHelper.ToRadians 45.f)
      (800.f / 600.f)
      0.1f
      100.f

  Draw3D.camera camera buffer

  // Load the cube model
  let cube = Assets.model "cube" ctx

  // Draw cube player
  Draw3D.mesh cube (Matrix.CreateTranslation model.Position)
  |> Draw3D.withColor Color.Red
  |> Draw3D.withBasicEffect
  |> Draw3D.submit buffer

// ─────────────────────────────────────────────────────────────
// Program
// ─────────────────────────────────────────────────────────────

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init update
    |> Program.withAssets
    |> Program.withRenderer(
      Batch3DRenderer.createWithConfig
        {
          Batch3DConfig.defaults with
              ClearColor = ValueSome Color.CornflowerBlue
        }
        view
    )
    |> Program.withInput
    |> Program.withSubscription(fun ctx _ ->
      InputMapper.subscribeStatic inputMap InputChanged ctx)
    |> Program.withTick Tick
    |> Program.withConfig(fun (game, graphics) ->
      game.Content.RootDirectory <- "Content"
      game.Window.Title <- "Mibo 3D Game"
      game.IsMouseVisible <- true
      graphics.PreferredBackBufferWidth <- 800
      graphics.PreferredBackBufferHeight <- 600)

  use game = new ElmishGame<Model, Msg>(program)
  game.Run()
  0
