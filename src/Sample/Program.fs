open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open FSharp.UMX

open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Input
open MiboSample
open MiboSample.Domain
open MiboSample.DemoComponents

// ─────────────────────────────────────────────────────────────
// Model: The World
//
// This is an Elmish model structured as a "world" holding entity
// components. The pattern uses different storage strategies:
//
// • Mutable Dictionary: For unstable entity components that change
//   every frame.
//
// • Mutable ResizeArray/Array: For burst, short-lived objects
//   (particles, projectiles) to avoid perf penalties from Map churn.
//
// • Immutable Map: For stable data that doesn't change every frame
//   or does so rarely.
//   Safe by default; Dictionary should be preferred when there's
//   high frequency updates and GC pressure is a concern.
//
// ─────────────────────────────────────────────────────────────

type Model = {
  // Entity components: keyed by EntityId, mutated every frame
  Positions: Dictionary<Guid<EntityId>, Vector2>
  Inputs: Dictionary<Guid<EntityId>, InputState>

  // Non-entity collections: particles are visual effects, not entities
  Particles: ResizeArray<Particles.Particle>

  // Stable data: changes infrequently via immutable updates
  Speeds: Map<Guid<EntityId>, float32>
  Hues: Map<Guid<EntityId>, float32> // Current hue (lerps toward target)
  TargetHues: Map<Guid<EntityId>, float32> // Target hue (set on fire)
  Sizes: Map<Guid<EntityId>, Vector2>

  // Well-known entity IDs
  PlayerId: Guid<EntityId>

  // Control-plane state
  BoxBounces: int
}

// ─────────────────────────────────────────────────────────────
// Messages: Control Plane Only
//
// Messages are for events and control flow, NOT for per-entity
// position updates. Hot data changes happen via direct mutation
// in system tick functions, bypassing the message queue entirely.
// ─────────────────────────────────────────────────────────────

[<Struct>]
type Msg =
  | Tick of gt: GameTime
  | KeyDown of key: Keys
  | KeyUp of key: Keys
  | PlayerFired of id: Guid<EntityId> * position: Vector2
  | DemoBoxBounced of count: int

// ─────────────────────────────────────────────────────────────
// Update.tick: The Frame Loop
//
// This is where systems run. The parent orchestrates order:
// 1. Player system computes new position based on input
// 2. Particle system ages and moves particles
// 3. Any events (like firing) are dispatched as Elmish messages
//
// Systems return computed values; the parent applies mutations.
// This keeps system logic pure and testable.
// ─────────────────────────────────────────────────────────────

module Update =

  let tick
    (boxRef: ComponentRef<InteractiveBoxOverlay>)
    (gt: GameTime)
    (model: Model)
    : struct (Model * Cmd<Msg>) =
    let dt = float32 gt.ElapsedGameTime.TotalSeconds
    let playerId = model.PlayerId

    // Read current state
    let position = model.Positions[playerId]
    let speed = Map.find playerId model.Speeds
    let input = model.Inputs[playerId]

    // Run player system (pure - returns new values)
    let result = Player.World.tick dt playerId position speed input

    // Apply position mutation at the call site
    model.Positions[playerId] <- result.NewPosition

    // Run particle system
    Particles.World.tick dt model.Particles

    // Run hue color system (lerps toward target)
    let hueResult =
      HueColor.World.tick dt playerId model.Hues model.TargetHues 5.f

    let newModel =
      if hueResult.Changed then
        {
          model with
              Hues = Map.add playerId hueResult.NewHue model.Hues
        }
      else
        model

    // Fire events are dispatched as messages (rare, control-plane)
    let fireCmd =
      result.FireEvent
      |> ValueOption.map (function
        | Player.Fired(id, pos) ->
          Cmd.ofEffect(Effect(fun dispatch -> dispatch(PlayerFired(id, pos)))))
      |> ValueOption.defaultValue Cmd.none

    // Interop with MonoGame components
    let interopCmd =
      Cmd.ofEffect(
        Effect(fun _ ->
          boxRef.TryGet()
          |> ValueOption.iter(fun box ->
            box.SpeedScale <- if input.IsFiring then 2.5f else 1.0f

            box.Tint <-
              if input.IsFiring then Color.HotPink else Color.DeepSkyBlue

            box.SetVisible(true)))
      )

    newModel, Cmd.batch2(fireCmd, interopCmd)

// ─────────────────────────────────────────────────────────────
// Init: Create the World
// ─────────────────────────────────────────────────────────────

let init(_ctx: GameContext) : struct (Model * Cmd<Msg>) =
  let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

  // Initialize mutable containers for hot data
  let positions = Dictionary<Guid<EntityId>, Vector2>()
  positions[playerId] <- Vector2(100.f, 100.f)

  let inputs = Dictionary<Guid<EntityId>, InputState>()
  inputs[playerId] <- InputState.empty

  // Initialize immutable maps for stable data
  let speeds = Map.ofList [ playerId, 200.f ]
  let hues = Map.ofList [ playerId, 0.f ]
  let targetHues = Map.ofList [ playerId, 0.f ]
  let sizes = Map.ofList [ playerId, Vector2(32.f, 32.f) ]

  {
    Positions = positions
    Inputs = inputs
    Particles = ResizeArray()
    Speeds = speeds
    Hues = hues
    TargetHues = targetHues
    Sizes = sizes
    PlayerId = playerId
    BoxBounces = 0
  },
  Cmd.none

// ─────────────────────────────────────────────────────────────
// Update: Message Dispatcher
//
// Systems return computed values; the parent applies mutations.
// Only BoxBounced actually modifies the model (control-plane state).
// ─────────────────────────────────────────────────────────────

let update
  (boxRef: ComponentRef<InteractiveBoxOverlay>)
  (msg: Msg)
  (model: Model)
  : struct (Model * Cmd<Msg>) =
  match msg with
  | Tick gt -> Update.tick boxRef gt model

  | KeyDown key ->
    // Pure: compute new input, then apply mutation
    let currentInput = model.Inputs[model.PlayerId]
    model.Inputs[model.PlayerId] <- Player.World.keyDown key currentInput
    model, Cmd.none

  | KeyUp key ->
    let currentInput = model.Inputs[model.PlayerId]
    model.Inputs[model.PlayerId] <- Player.World.keyUp key currentInput
    model, Cmd.none

  | PlayerFired(id, pos) ->
    // Emit particles at fired position
    Particles.World.emit pos 100 model.Particles

    // Shift hue target for smooth color transition
    let newTargets = HueColor.World.shiftTarget id 15.f model.TargetHues
    { model with TargetHues = newTargets }, Cmd.none

  | DemoBoxBounced count ->
    let position = model.Positions[model.PlayerId]
    Particles.World.emit position 20 model.Particles
    { model with BoxBounces = count }, Cmd.none

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
  let playerId = model.PlayerId

  // Pull component data for player
  let pos = model.Positions[playerId]
  let hue = Map.find playerId model.Hues
  let color = HueColor.hueToColor hue
  let size = Map.find playerId model.Sizes
  Player.view ctx pos color size buffer

  // Particles render directly from their list
  Particles.view ctx model.Particles buffer

// ─────────────────────────────────────────────────────────────
// Subscribe: Reactive Input Bindings
// ─────────────────────────────────────────────────────────────

let subscribe
  (boxRef: ComponentRef<InteractiveBoxOverlay>)
  (ctx: GameContext)
  (_model: Model)
  =
  Sub.batch [
    Keyboard.listen KeyDown KeyUp ctx
    InteractiveBoxOverlayBridge.subscribeBounced boxRef DemoBoxBounced
  ]

// ─────────────────────────────────────────────────────────────
// Entry Point: Wire Up the Elmish Program
// ─────────────────────────────────────────────────────────────

[<EntryPoint>]
let main argv =
  let interactiveBoxRef = ComponentRef<InteractiveBoxOverlay>()

  let program =
    Program.mkProgram init (update interactiveBoxRef)
    |> Program.withAssets
    |> Program.withRenderer(Batch2DRenderer.create view)
    |> Program.withInput
    |> Program.withTick Tick
    |> Program.withSubscription(subscribe interactiveBoxRef)
    |> Program.withComponent BouncingBoxOverlay.create
    |> Program.withComponentRef
      interactiveBoxRef
      InteractiveBoxOverlayBridge.create

  use game = new ElmishGame<Model, Msg>(program)
  game.Run()
  0
