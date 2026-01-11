module Mibo2D.Program

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Input

// ─────────────────────────────────────────────────────────────
// Input
// ─────────────────────────────────────────────────────────────

type GameAction =
  | MoveLeft
  | MoveRight
  | MoveUp
  | MoveDown

let inputMap =
  InputMap.empty
  |> InputMap.key MoveLeft Keys.Left
  |> InputMap.key MoveLeft Keys.A
  |> InputMap.key MoveRight Keys.Right
  |> InputMap.key MoveRight Keys.D
  |> InputMap.key MoveUp Keys.Up
  |> InputMap.key MoveUp Keys.W
  |> InputMap.key MoveDown Keys.Down
  |> InputMap.key MoveDown Keys.S

// ─────────────────────────────────────────────────────────────
// Model
// ─────────────────────────────────────────────────────────────

type Model = {
  Position: Vector2
  Velocity: Vector2
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
    Position = Vector2(400.f, 300.f)
    Velocity = Vector2(200.f, 150.f)
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
    let speed = 200.f
    let mutable manualVelocity = Vector2.Zero

    if model.Input.Held.Contains MoveLeft then
      manualVelocity.X <- manualVelocity.X - speed

    if model.Input.Held.Contains MoveRight then
      manualVelocity.X <- manualVelocity.X + speed

    if model.Input.Held.Contains MoveUp then
      manualVelocity.Y <- manualVelocity.Y - speed

    if model.Input.Held.Contains MoveDown then
      manualVelocity.Y <- manualVelocity.Y + speed

    // Bouncing logic
    let mutable velocity = model.Velocity

    let mutable position =
      model.Position + (velocity * dt) + (manualVelocity * dt)

    if position.X < 0.f || position.X > 800.f - 32.f then
      velocity.X <- -velocity.X

    if position.Y < 0.f || position.Y > 600.f - 32.f then
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

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
  // Draw player (using a 1x1 pixel texture if no asset loaded, or load one)
  let pixel =
    Assets.getOrCreate
      "pixel"
      (fun gd ->
        let t = new Texture2D(gd, 1, 1)
        t.SetData([| Color.White |])
        t)
      ctx

  Draw2D.sprite
    pixel
    (Rectangle(int model.Position.X, int model.Position.Y, 32, 32))
  |> Draw2D.withColor Color.Red
  |> Draw2D.submit buffer

// ─────────────────────────────────────────────────────────────
// Program
// ─────────────────────────────────────────────────────────────

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init update
    |> Program.withAssets
    |> Program.withRenderer(Batch2DRenderer.create view)
    |> Program.withInput
    |> Program.withSubscription(fun ctx _ ->
      InputMapper.subscribeStatic inputMap InputChanged ctx)
    |> Program.withTick Tick
    |> Program.withConfig(fun (game, graphics) ->
      game.Content.RootDirectory <- "Content"
      game.Window.Title <- "Mibo 2D Game"
      game.IsMouseVisible <- true
      graphics.PreferredBackBufferWidth <- 800
      graphics.PreferredBackBufferHeight <- 600)

  use game = new ElmishGame<Model, Msg>(program)
  game.Run()
  0
