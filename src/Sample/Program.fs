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
open MiboSample.Crates
open MiboSample.DemoComponents
// Shared ref used by the subscription to support dynamic remapping without requiring
// subscription replacement. The user can ignore this if they never remap.
let private inputMapRef: InputMap<GameAction> ref = ref InputMap.empty

// ─────────────────────────────────────────────────────────────
// Messages
// ─────────────────────────────────────────────────────────────

[<Struct>]
type Msg =
  | Tick of gt: GameTime
  | InputMapped of actions: ActionState<GameAction>
  | PlayerFired of id: Guid<EntityId> * position: Vector2
  | EmitParticles of position: Vector2 * count: int
  | DemoBoxBounced of count: int
  | SpawnCrate
  | CrateHit of crateId: Guid<EntityId>

let crateRetryMode = RetryMode.Immediate

// System pipeline is now provided by Mibo.Elmish.System

// ─────────────────────────────────────────────────────────────
// Init
// ─────────────────────────────────────────────────────────────

let init(_ctx: GameContext) : struct (Model * Cmd<Msg>) =
  let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

  let positions = Dictionary<Guid<EntityId>, Vector2>()
  positions[playerId] <- Vector2(100.f, 100.f)

  // Configure Input Map
  let inputMap =
    InputMap.empty
    |> InputMap.key MoveLeft Keys.Left
    |> InputMap.key MoveRight Keys.Right
    |> InputMap.key MoveUp Keys.Up
    |> InputMap.key MoveDown Keys.Down
    |> InputMap.key Fire Keys.Space

  // Make the current mapping available to the subscription.
  inputMapRef.Value <- inputMap

  struct ({
            Positions = positions
            Actions = ActionState.empty
            InputMap = inputMap
            Particles = ResizeArray()
            Crates = ResizeArray()
            Speeds = Map.ofList [ playerId, 200.f ]
            Hues = Map.ofList [ playerId, 0.f ]
            TargetHues = Map.ofList [ playerId, 0.f ]
            Sizes = Map.ofList [ playerId, Vector2(32.f, 32.f) ]
            PlayerId = playerId
            BoxBounces = 0
            CrateHits = 0
          },
          Cmd.none)

// ─────────────────────────────────────────────────────────────
// Update: Composable Pipeline
// ─────────────────────────────────────────────────────────────

let update
  (boxRef: ComponentRef<InteractiveBoxOverlay>)
  (msg: Msg)
  (model: Model)
  : struct (Model * Cmd<Msg>) =
  match msg with
  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    // Type-enforced pipeline with snapshot boundary
    let struct (finalModel, allCmds) =
      System.start model
      // Phase 1: Mutable systems (can mutate positions, particles)
      |> System.pipeMutable(Physics.update dt)
      |> System.pipeMutable(Particles.update dt)
      // SNAPSHOT: transition to readonly
      |> System.snapshot Model.toSnapshot
      // Phase 2: Readonly systems (work with immutable snapshot)
      |> System.pipe(HueColor.update dt 5.f)
      |> System.pipe(Crates.ensureTarget crateRetryMode (fun () -> SpawnCrate))
      |> System.pipe(Crates.detectFirstOverlap(fun id -> CrateHit id))
      |> System.pipe(Player.processActions(fun id pos -> PlayerFired(id, pos)))
      // Finish: convert back to Model
      |> System.finish Model.fromSnapshot

    // MonoGame interop (reads from model)
    let interopCmd =
      Cmd.ofEffect(
        Effect<Msg>(fun _ ->
          // Direct Actions check instead of dictionary lookup
          boxRef.TryGet()
          |> ValueOption.iter(fun box ->
            let isFiring = finalModel.Actions.Held.Contains Fire
            box.SpeedScale <- if isFiring then 2.5f else 1.0f
            box.Tint <- if isFiring then Color.HotPink else Color.DeepSkyBlue
            box.SetVisible(true)))
      )

    struct (finalModel, Cmd.batch2(allCmds, interopCmd))


  | InputMapped actions ->
    // User handles their own model strategy: here we store the mapped ActionState.
    struct ({ model with Actions = actions }, Cmd.none)

  | PlayerFired(id, pos) ->
    let newModel = HueColor.shiftTarget id 15.f model
    struct (newModel, Cmd.ofMsg(EmitParticles(pos, 100)) |> Cmd.deferNextFrame)

  | EmitParticles(pos, count) ->
    Particles.emit pos count model
    struct (model, Cmd.none)

  | DemoBoxBounced count ->
    struct ({ model with BoxBounces = count },
            Cmd.ofMsg(EmitParticles(model.Positions[model.PlayerId], 20))
            |> Cmd.deferNextFrame)

  | SpawnCrate ->
    // Update doesn't have GameContext by design; use current default window size.
    // This is just a sample; a real game would model viewport changes explicitly.
    let struct (m, cmds) =
      Crates.spawnOne crateRetryMode (fun () -> SpawnCrate) 800 600 model

    struct (m, Cmd.batch cmds)

  | CrateHit crateId ->
    let m = Crates.removeCrate crateId model

    let spawnCmd =
      match crateRetryMode with
      | Deferred -> Cmd.ofMsg SpawnCrate |> Cmd.deferNextFrame
      | Immediate -> Cmd.ofMsg SpawnCrate

    struct (m,
            Cmd.batch [
              Cmd.ofMsg(EmitParticles(model.Positions[model.PlayerId], 40))
              |> Cmd.deferNextFrame
              spawnCmd
            ])

// ─────────────────────────────────────────────────────────────
// View
// ─────────────────────────────────────────────────────────────

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
  let snapshot = Model.toSnapshot model

  ctx.Game.Window.Title <-
    $"MiboSample | Crates={snapshot.Crates.Count}/{Crates.targetCount} | Hits={snapshot.CrateHits} | Mode={crateRetryMode}"

  let playerId = model.PlayerId
  let pos = model.Positions[playerId]
  let hue = model.Hues |> Map.tryFind playerId |> Option.defaultValue 0f
  let color = HueColor.hueToColor hue

  let size =
    model.Sizes
    |> Map.tryFind playerId
    |> Option.defaultValue(Vector2(32f, 32f))

  Player.view ctx pos color size buffer
  Crates.view ctx snapshot buffer
  Particles.view ctx model.Particles buffer

// ─────────────────────────────────────────────────────────────
// Subscribe
// ─────────────────────────────────────────────────────────────

let subscribe
  (boxRef: ComponentRef<InteractiveBoxOverlay>)
  (ctx: GameContext)
  (_model: Model)
  =
  Sub.batch [
    // Subscription-based input mapping: the framework maps raw input -> ActionState.
    // The user only needs to handle a single message.
    InputMapper.subscribe (fun () -> inputMapRef.Value) InputMapped ctx
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
    |> Program.withInputMapper inputMapRef.Value
    |> Program.withTick Tick
    |> Program.withSubscription(subscribe interactiveBoxRef)
    |> Program.withComponent BouncingBoxOverlay.create
    |> Program.withComponentRef
      interactiveBoxRef
      InteractiveBoxOverlayBridge.create

  use game = new ElmishGame<Model, Msg>(program)
  game.Run()
  0
