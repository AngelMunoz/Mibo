module MiboMono2DAdaptive

open Microsoft.Xna.Framework
open Mibo
open Mibo.Adaptive
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
// State: roots hold what you write; derived values follow them
// ─────────────────────────────────────────────────────────────

type World = {
  Position: cval<Vector2>
  Velocity: cval<Vector2>
  Actions: cval<ActionState<GameAction>>
  ManualVelocity: aval<Vector2>
}

// ─────────────────────────────────────────────────────────────
// Frame: everything the renderer needs, packed once per frame
// ─────────────────────────────────────────────────────────────

type Frame = { Position: Vector2 }

// ─────────────────────────────────────────────────────────────
// Update: advances the game each frame, writes the roots
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

  let velocity =
    bounce
      Vector2.Zero
      (Vector2(768.f, 568.f))
      position
      (world.Velocity |> AVal.getValue)

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

let view (_ctx: GameContext) (frame: Frame) (buffer: RenderBuffer2D) =
  buffer
    .fillRect(
      float32 frame.Position.X,
      float32 frame.Position.Y,
      32f,
      32f,
      Color.Red,
      layer = 0<RenderLayer>
    )
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
    Position = CVal.create(Vector2(400.f, 300.f))
    Velocity = CVal.create(Vector2(200.f, 150.f))
    Actions = actions
    ManualVelocity = actions |> AVal.map computeManualVelocity
  }

let world = mkWorld()

/// Builds the full adaptive MonoGame program with the content root configured
/// for the MonoGame content pipeline. The thin client projects (DesktopGL,
/// DesktopVK, WindowsDX12) pass this directly to AdaptiveMonoGameGame.
let create() : AdaptiveMonoGameProgram<Frame> =
  AdaptiveProgram.mkProgram (init world) (update world)
  |> AdaptiveProgram.withConfig(
    GameConfig.withWidth 800
    >> GameConfig.withHeight 600
    >> GameConfig.withTitle "Mibo MonoGame 2D Adaptive Game"
  )
  |> AdaptiveProgram.withInput
  |> AdaptiveProgram.withRenderer(fun () -> Renderer2D.create view)
  |> AdaptiveMonoGameProgram.ofProgram
  |> AdaptiveMonoGameProgram.withConfig(fun (game, _deviceManager) ->
    game.Content.RootDirectory <- "Content")
