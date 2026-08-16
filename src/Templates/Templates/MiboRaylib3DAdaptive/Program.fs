module MiboRaylib3DAdaptive.Program

open System.Numerics
open Raylib_cs
open Mibo
open Mibo.Adaptive
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
// State: roots hold what you write; derived values follow them
// ─────────────────────────────────────────────────────────────

type World = {
  Position: cval<Vector3>
  Velocity: cval<Vector3>
  Actions: cval<ActionState<GameAction>>
  ManualVelocity: aval<Vector3>
}

// ─────────────────────────────────────────────────────────────
// Frame: everything the renderer needs, packed once per frame
// ─────────────────────────────────────────────────────────────

type Frame = { Position: Vector3 }

// ─────────────────────────────────────────────────────────────
// Update: advances the game each frame, writes the roots
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

let update (world: World) (_ctx: AdaptiveContext) (gameTime: GameTime) =
  let dt = float32 gameTime.ElapsedGameTime.TotalSeconds

  // Read the action state once at the top of update
  let actions = world.Actions |> AVal.getValue

  // ManualVelocity is derived: it updated itself when the actions
  // changed, so read it instead of recalculating it here
  let manual = world.ManualVelocity |> AVal.getValue

  let position =
    (world.Position |> AVal.getValue)
    + ((world.Velocity |> AVal.getValue) * dt)
    + (manual * dt)

  let velocity = bounce 5.f position (world.Velocity |> AVal.getValue)

  world.Position.Set position
  world.Velocity.Set velocity

// Started and Released are edge events. The runtime clears them
// after update, so they are fresh every step. No manual clear is needed.

// ─────────────────────────────────────────────────────────────
// Frame builder: reads the state, builds the Frame
// ─────────────────────────────────────────────────────────────

let frame (world: World) () : Frame = {
  Position = world.Position |> AVal.getValue
}

// ─────────────────────────────────────────────────────────────
// Init: runs once at startup, registers the input subscription
// ─────────────────────────────────────────────────────────────

let init (world: World) (ctx: AdaptiveFrameContext) : AdaptiveInit<Frame> =
  // The input mapper turns key presses into GameAction values and
  // writes them into the Actions root (needs withInput below)
  let inputSub =
    InputMapper.subscribeStaticAdaptive inputMap world.Actions ctx.Context

  let subMap = [ inputSub.Id, inputSub ] |> AMap.ofList

  AdaptiveInit.ofFrameBuilder(frame world)
  |> AdaptiveInit.withSubscriptions(fun _ -> subMap)

// ─────────────────────────────────────────────────────────────
// View: draws the Frame, never touches the state
// ─────────────────────────────────────────────────────────────

let view (_ctx: GameContext) (frame: Frame) (buffer: RenderBuffer3D) =
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
      frame.Position.X,
      frame.Position.Y,
      frame.Position.Z
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

// Build the world once. The derived values must be created a
// single time; creating them inside update would rebuild them
// every frame and waste the benefit
let mkWorld() =
  let actions = CVal.create ActionState.empty

  {
    Position = CVal.create Vector3.Zero
    Velocity = CVal.create(Vector3(2.f, 1.5f, 2.f))
    Actions = actions
    ManualVelocity = actions |> AVal.map computeManualVelocity
  }

let world = mkWorld()

[<EntryPoint>]
let main _ =
  let program =
    AdaptiveProgram.mkProgram (init world) (update world)
    |> AdaptiveProgram.withConfig(
      GameConfig.withWidth 800
      >> GameConfig.withHeight 600
      >> GameConfig.withTitle "Mibo Raylib 3D Adaptive Game"
    )
    |> AdaptiveProgram.withInput
    |> AdaptiveProgram.withRenderer(fun () ->
      Renderer3D.create (ForwardPbrPipeline()) view)

  let game = new AdaptiveRaylibGame<Frame>(program)
  game.Run()
  0
