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
// Messages
// ─────────────────────────────────────────────────────────────

[<Struct>]
type Msg =
  | Tick of gt: GameTime
  | KeyDown of key: Keys
  | KeyUp of key: Keys
  | PlayerFired of id: Guid<EntityId> * position: Vector2
  | DemoBoxBounced of count: int

// ─────────────────────────────────────────────────────────────
// System Pipeline (Generic Delegates - Framework-Ready Design)
//
// These types and combinators are generic and could live in
// Mibo.Elmish. The type system enforces the snapshot boundary
// through the 'Model/'Snapshot type difference.
// ─────────────────────────────────────────────────────────────

module System =
  /// Start pipeline with mutable model
  let inline start (model: 'Model) : 'Model * Cmd<'Msg> list = (model, [])

  /// Pipe a mutable system (pre-snapshot)
  let inline pipeMutable (system: 'Model -> struct ('Model * Cmd<'Msg> list)) (model: 'Model, cmds: Cmd<'Msg> list) : 'Model * Cmd<'Msg> list =
    let struct (newModel, newCmds) = system model
    (newModel, cmds @ newCmds)

  /// SNAPSHOT: Transition from mutable Model to readonly Snapshot
  let inline snapshot (toSnapshot: 'Model -> 'Snapshot) (model: 'Model, cmds: Cmd<'Msg> list) : 'Snapshot * Cmd<'Msg> list =
    (toSnapshot model, cmds)

  /// Pipe a readonly system (post-snapshot)
  let inline pipe (system: 'Snapshot -> struct ('Snapshot * Cmd<'Msg> list)) (snap: 'Snapshot, cmds: Cmd<'Msg> list) : 'Snapshot * Cmd<'Msg> list =
    let struct (newSnap, newCmds) = system snap
    (newSnap, cmds @ newCmds)

  /// Finish pipeline: convert snapshot back to model, batch commands
  let inline finish (fromSnapshot: 'Snapshot -> 'Model) (snap: 'Snapshot, cmds: Cmd<'Msg> list) : struct ('Model * Cmd<'Msg>) =
    struct (fromSnapshot snap, Cmd.batch cmds)

// ─────────────────────────────────────────────────────────────
// Init
// ─────────────────────────────────────────────────────────────

let init (_ctx: GameContext) : struct (Model * Cmd<Msg>) =
  let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

  let positions = Dictionary<Guid<EntityId>, Vector2>()
  positions[playerId] <- Vector2(100.f, 100.f)

  let inputs = Dictionary<Guid<EntityId>, InputState>()
  inputs[playerId] <- InputState.empty

  struct ({
    Positions = positions
    Inputs = inputs
    Particles = ResizeArray()
    Speeds = Map.ofList [ playerId, 200.f ]
    Hues = Map.ofList [ playerId, 0.f ]
    TargetHues = Map.ofList [ playerId, 0.f ]
    Sizes = Map.ofList [ playerId, Vector2(32.f, 32.f) ]
    PlayerId = playerId
    BoxBounces = 0
  }, Cmd.none)

// ─────────────────────────────────────────────────────────────
// Update: Composable Pipeline
// ─────────────────────────────────────────────────────────────

let update (boxRef: ComponentRef<InteractiveBoxOverlay>) (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
  match msg with
  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    // Type-enforced pipeline with snapshot boundary
    let struct (finalModel, allCmds) =
      System.start model
      // Phase 1: Mutable systems (can mutate positions, particles)
      |> System.pipeMutable (Physics.update dt)
      |> System.pipeMutable (Particles.update dt)
      // SNAPSHOT: transition to readonly
      |> System.snapshot Model.toSnapshot
      // Phase 2: Readonly systems (work with immutable snapshot)
      |> System.pipe (HueColor.update dt 5.f)
      |> System.pipe (Player.processActions (fun id pos -> PlayerFired(id, pos)))
      // Finish: convert back to Model
      |> System.finish Model.fromSnapshot

    // MonoGame interop (reads from model)
    let interopCmd =
      Cmd.ofEffect(Effect<Msg>(fun _ ->
        match finalModel.Inputs.TryGetValue(finalModel.PlayerId) with
        | true, input ->
          boxRef.TryGet()
          |> ValueOption.iter(fun box ->
            box.SpeedScale <- if input.IsFiring then 2.5f else 1.0f
            box.Tint <- if input.IsFiring then Color.HotPink else Color.DeepSkyBlue
            box.SetVisible(true))
        | _ -> ()))

    struct (finalModel, Cmd.batch2(allCmds, interopCmd))

  | KeyDown key ->
    let currentInput = model.Inputs[model.PlayerId]
    model.Inputs[model.PlayerId] <- Player.updateInput (Player.KeyPressed key) currentInput
    struct (model, Cmd.none)

  | KeyUp key ->
    let currentInput = model.Inputs[model.PlayerId]
    model.Inputs[model.PlayerId] <- Player.updateInput (Player.KeyReleased key) currentInput
    struct (model, Cmd.none)

  | PlayerFired(id, pos) ->
    Particles.emit pos 100 model
    let newModel = HueColor.shiftTarget id 15.f model
    struct (newModel, Cmd.none)

  | DemoBoxBounced count ->
    Particles.emit model.Positions[model.PlayerId] 20 model
    struct ({ model with BoxBounces = count }, Cmd.none)

// ─────────────────────────────────────────────────────────────
// View
// ─────────────────────────────────────────────────────────────

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
  let playerId = model.PlayerId
  let pos = model.Positions[playerId]
  let hue = model.Hues |> Map.tryFind playerId |> Option.defaultValue 0f
  let color = HueColor.hueToColor hue
  let size = model.Sizes |> Map.tryFind playerId |> Option.defaultValue (Vector2(32f, 32f))

  Player.view ctx pos color size buffer
  Particles.view ctx model.Particles buffer

// ─────────────────────────────────────────────────────────────
// Subscribe
// ─────────────────────────────────────────────────────────────

let subscribe (boxRef: ComponentRef<InteractiveBoxOverlay>) (ctx: GameContext) (_model: Model) =
  Sub.batch [
    Keyboard.listen KeyDown KeyUp ctx
    InteractiveBoxOverlayBridge.subscribeBounced boxRef DemoBoxBounced
  ]

// ─────────────────────────────────────────────────────────────
// Entry Point
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
    |> Program.withComponentRef interactiveBoxRef InteractiveBoxOverlayBridge.create

  use game = new ElmishGame<Model, Msg>(program)
  game.Run()
  0
