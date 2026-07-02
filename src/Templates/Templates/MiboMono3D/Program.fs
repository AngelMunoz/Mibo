module MiboMono3D

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo
open Mibo.Elmish
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
  | InputChanged input -> { model with Input = input }, Cmd.none
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

// Primitive meshes need a GraphicsDevice to upload their GPU buffers,
// so we lazily build the set on the first frame and reuse it after.
let mutable private primitives: Primitive3D.PrimitiveSet voption = ValueNone

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer3D) =
  let primSet =
    match primitives with
    | ValueSome s -> s
    | ValueNone ->
      let gd = MonoGameGameContext.getGraphicsDevice ctx
      let s = Primitive3D.create gd
      primitives <- ValueSome s
      s

  let camera: Camera3D = {
    Position = Vector3(12.f, 12.f, 12.f)
    Target = Vector3.Zero
    Up = Vector3.Up
    FovY = MathHelper.ToRadians(55.0f)
    NearPlane = 0.1f
    FarPlane = 1000.f
    Projection = CameraProjection.Perspective
  }

  let transform = Matrix.CreateTranslation(model.Position)
  let material = Material3D.colored Microsoft.Xna.Framework.Color.Red

  buffer
  |> Draw3D.beginCameraWith(
    Camera3D.render camera
    |> Camera3D.withClear Microsoft.Xna.Framework.Color.White
  )
  |> Draw3D.setAmbientLight {
    Color = Mibo.Color.White
    Intensity = 0.5f
  }
  |> Draw3D.addDirectionalLight {
    Direction = System.Numerics.Vector3(1.f, -1.f, 1.f)
    Color = Mibo.Color.White
    Intensity = 1.f
    CastsShadows = false
  }
  |> Draw3D.drawPrimitive primSet.Cube transform material
  |> Draw3D.endCamera
  |> Draw3D.drop

// ─────────────────────────────────────────────────────────────
// Program
// ─────────────────────────────────────────────────────────────

/// Builds the full Mibo program. The thin client projects
/// (DesktopGL, WindowsDX) call this and pass the result to MiboGame.
let create() : Program<Model, Msg> =
  Program.mkProgram init update
  |> Program.withConfig(fun cfg -> {
    cfg with
        Width = 800
        Height = 600
        Title = "Mibo MonoGame 3D Game"
        TargetFPS = 60
  })
  |> Program.withInput
  |> Program.withSubscription(fun ctx _model ->
    InputMapper.subscribeStatic inputMap InputChanged ctx)
  |> Program.withTick Tick
  |> Program.withRenderer(fun () -> Renderer3D.create (ForwardPipeline()) view)
