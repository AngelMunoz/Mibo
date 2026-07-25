module MiboMono2D

open Microsoft.Xna.Framework
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Input

// ─────────────────────────────────────────────────────────────
// Input
// ─────────────────────────────────────────────────────────────

[<Struct>]
type GameAction =
  | MoveLeft
  | MoveRight
  | MoveUp
  | MoveDown

let inputMap =
  InputMap.empty
  |> InputMap.key MoveLeft KeyCode.Left
  |> InputMap.key MoveLeft KeyCode.A
  |> InputMap.key MoveRight KeyCode.Right
  |> InputMap.key MoveRight KeyCode.D
  |> InputMap.key MoveUp KeyCode.Up
  |> InputMap.key MoveUp KeyCode.W
  |> InputMap.key MoveDown KeyCode.Down
  |> InputMap.key MoveDown KeyCode.S

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

[<Struct>]
type Msg =
  | Tick of tick: GameTime
  | InputChanged of inputs: ActionState<GameAction>

// ─────────────────────────────────────────────────────────────
// Init
// ─────────────────────────────────────────────────────────────

let init(_ctx: GameContext) : struct (Model * Cmd<Msg>) =
  let model = {
    Position = Vector2(400.f, 300.f)
    Velocity = Vector2(200.f, 150.f)
    Input = ActionState.empty
  }

  model, Cmd.none

// ─────────────────────────────────────────────────────────────
// Update
// ─────────────────────────────────────────────────────────────

let speed = 200.f

let computeManualVelocity(input: ActionState<GameAction>) =
  let x =
    if input.Held.Contains MoveLeft then -speed
    elif input.Held.Contains MoveRight then speed
    else 0.f

  let y =
    if input.Held.Contains MoveUp then -speed
    elif input.Held.Contains MoveDown then speed
    else 0.f

  Vector2(x, y)

let bounce
  (min: Vector2)
  (max: Vector2)
  (position: Vector2)
  (velocity: Vector2)
  =
  let x =
    if position.X < min.X || position.X > max.X then
      -velocity.X
    else
      velocity.X

  let y =
    if position.Y < min.Y || position.Y > max.Y then
      -velocity.Y
    else
      velocity.Y

  Vector2(x, y)

let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
  match msg with
  | InputChanged input -> { model with Input = input }, Cmd.none
  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds
    let manual = computeManualVelocity model.Input
    let position = model.Position + (model.Velocity * dt) + (manual * dt)

    let velocity =
      bounce Vector2.Zero (Vector2(768.f, 568.f)) position model.Velocity

    {
      model with
          Position = position
          Velocity = velocity
    },
    Cmd.none

// ─────────────────────────────────────────────────────────────
// View
// ─────────────────────────────────────────────────────────────

let view (_ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  buffer
    .fillRect(
      float32 model.Position.X,
      float32 model.Position.Y,
      32f,
      32f,
      Color.Red,
      layer = 0<RenderLayer>
    )
    .drop()

// ─────────────────────────────────────────────────────────────
// Program
// ─────────────────────────────────────────────────────────────

/// Builds the full Mibo MonoGame program with the content root configured for the
/// MonoGame content pipeline. The thin client projects (DesktopGL, DesktopVK,
/// WindowsDX12) pass this directly to MiboGame.
let create() : MonoGameProgram<Model, Msg> =
  Program.mkProgram init update
  |> Program.withConfig(fun cfg -> {
    cfg with
        Width = 800
        Height = 600
        Title = "Mibo MonoGame 2D Game"
  })
  |> Program.withInput
  |> Program.withSubscription(fun ctx _model ->
    InputMapper.subscribeStatic inputMap InputChanged ctx)
  |> Program.withTick Tick
  |> Program.withRenderer(fun () -> Renderer2D.create view)
  |> MonoGameProgram.ofProgram
  |> MonoGameProgram.withConfig(fun (game, deviceManager) ->
    game.Content.RootDirectory <- "Content")
