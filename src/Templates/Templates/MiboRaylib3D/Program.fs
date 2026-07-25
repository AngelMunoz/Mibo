module MiboRaylib3D.Program

open System
open System.Numerics
open Raylib_cs
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo.Input

// ─────────────────────────────────────────────────────────────
// Input
// ─────────────────────────────────────────────────────────────

[<Struct>]
type GameAction =
  | MoveForward
  | MoveBackward
  | MoveLeft
  | MoveRight
  | MoveUp
  | MoveDown

let inputMap =
  InputMap.empty
  |> InputMap.key MoveForward KeyCode.W
  |> InputMap.key MoveForward KeyCode.Up
  |> InputMap.key MoveBackward KeyCode.S
  |> InputMap.key MoveBackward KeyCode.Down
  |> InputMap.key MoveLeft KeyCode.A
  |> InputMap.key MoveLeft KeyCode.Left
  |> InputMap.key MoveRight KeyCode.D
  |> InputMap.key MoveRight KeyCode.Right
  |> InputMap.key MoveUp KeyCode.Space
  |> InputMap.key MoveDown KeyCode.LeftShift

// ─────────────────────────────────────────────────────────────
// Model
// ─────────────────────────────────────────────────────────────
[<Struct>]
type Model = {
  Position: Vector3
  Velocity: Vector3
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
    Position = Vector3.Zero
    Velocity = Vector3(2.f, 1.5f, 2.f)
    Input = ActionState.empty
  }

  model, Cmd.none

// ─────────────────────────────────────────────────────────────
// Update
// ─────────────────────────────────────────────────────────────

let moveSpeed = 5.f

let computeManualVelocity(input: ActionState<GameAction>) =
  let dx =
    if input.Held.Contains MoveLeft then -moveSpeed
    elif input.Held.Contains MoveRight then moveSpeed
    else 0.f

  let dy =
    if input.Held.Contains MoveUp then moveSpeed
    elif input.Held.Contains MoveDown then -moveSpeed
    else 0.f

  let dz =
    if input.Held.Contains MoveForward then -moveSpeed
    elif input.Held.Contains MoveBackward then moveSpeed
    else 0.f

  Vector3(dx, dy, dz)

let bounce (bounds: float32) (position: Vector3) (velocity: Vector3) =

  let x =
    if position.X < -bounds || position.X > bounds then
      -velocity.X
    else
      velocity.X

  let y =
    if position.Y < -bounds || position.Y > bounds then
      -velocity.Y
    else
      velocity.Y

  let z =
    if position.Z < -bounds || position.Z > bounds then
      -velocity.Z
    else
      velocity.Z

  Vector3(x, y, z)

let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
  match msg with
  | InputChanged input -> struct ({ model with Input = input }, Cmd.none)

  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds
    let manual = computeManualVelocity model.Input
    let position = model.Position + (model.Velocity * dt) + (manual * dt)
    let velocity = bounce 5.f position model.Velocity

    {
      model with
          Position = position
          Velocity = velocity
    },
    Cmd.none

// ─────────────────────────────────────────────────────────────
// View
// ─────────────────────────────────────────────────────────────

let view (_ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
  let camera =
    Camera3D(
      Vector3(12.f, 12.f, 12.f),
      Vector3.Zero,
      Vector3.UnitY,
      55.0f,
      CameraProjection.Perspective
    )

  let transform =
    Raymath.MatrixTranslate(
      model.Position.X,
      model.Position.Y,
      model.Position.Z
    )

  let material = Material3D.colored Raylib_cs.Color.Red

  buffer
    .beginCameraWith(
      Camera3D.render camera |> Camera3D.withClear Raylib_cs.Color.RayWhite
    )
    .setAmbientLight(
      {
        Color = Mibo.Color.White
        Intensity = 0.5f
      }
    )
    .addDirectionalLight(
      {
        Direction = Vector3(1.f, -1.f, 1.f)
        Color = Mibo.Color.White
        Intensity = 1.f
        CastsShadows = false
      }
    )
    .mesh(Primitive3D.cube, transform, material)
    .endCamera()
    .drop()

// ─────────────────────────────────────────────────────────────
// Program
// ─────────────────────────────────────────────────────────────

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init update
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 800
          Height = 600
          Title = "Mibo Raylib 3D Game"
          TargetFPS = 60
    })
    |> Program.withInput
    |> Program.withSubscription(fun ctx _model ->
      InputMapper.subscribeStatic inputMap InputChanged ctx)
    |> Program.withTick Tick
    |> Program.withRenderer(fun () ->
      Renderer3D.create (ForwardPbrPipeline()) view)

  let game = new RaylibGame<Model, Msg>(program)
  game.Run()
  0
