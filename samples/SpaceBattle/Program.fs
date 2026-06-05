module SpaceBattle.Program

open System
open System.Numerics
open Mibo.Animation
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Input
open Mibo.Layout
open Phase
open AnimState
open SpaceBattle.Map
open SpaceBattle.Types
open SpaceBattle.Units

// ─────────────────────────────────────────────────────────────
// Model
// ─────────────────────────────────────────────────────────────

type Model() =

  member val Time: GameTime = Unchecked.defaultof<_> with get, set
  member val Input: InputModel = Unchecked.defaultof<_> with get, set
  member val Cam: CameraModel = Unchecked.defaultof<_> with get, set
  member val Map: MapModel = Unchecked.defaultof<_> with get, set

  member val Units: Map<struct (int * int), SBUnit> =
    Unchecked.defaultof<_> with get, set

  member val UnitSprites: Map<struct (Faction * UnitClass), SpriteSheet> =
    Unchecked.defaultof<_> with get, set

  member val Decorations: Map<struct (int * int), AnimatedSprite> =
    Unchecked.defaultof<_> with get, set

  member val Turn: Turn = Unchecked.defaultof<_> with get, set
  member val TurnOrder: TurnOrder = Unchecked.defaultof<_> with get, set
  member val Anim: AnimationState = Unchecked.defaultof<_> with get, set
  member val GameAssets: GameAssets = Unchecked.defaultof<_> with get, set
  member val Skybox: Shaders.SkyboxModel = Unchecked.defaultof<_> with get, set

// ─────────────────────────────────────────────────────────────
// Messages
// ─────────────────────────────────────────────────────────────

[<Struct>]
type Msg =
  | Tick of tick: GameTime
  | InputChanged of inputs: ActionState<GameAction>
  | MouseAction of mouse: MouseAction
  | PhaseMsg of phase: PhaseMsg
  | AnimationMsg of animation: AnimationMsg

// ─────────────────────────────────────────────────────────────
// Init
// ─────────────────────────────────────────────────────────────

let init(ctx: GameContext) : struct (Model * Cmd<Msg>) =
  let map = Map.init(Random.Shared.Next 10001)
  let assets = SBAssets.loadSpriteSheets ctx

  let units =
    Map.ofList [
      struct (0, 0),
      {
        Faction = Pirates
        Class = Fighter
        Direction = SE
        HP = 100
        MaxHP = 100
        Defense = 10
        MoveRange = 7
        AttackRange = 4
      }
      struct (1, 0),
      {
        Faction = Pirates
        Class = Cruiser
        Direction = SE
        HP = 150
        MaxHP = 150
        Defense = 15
        MoveRange = 5
        AttackRange = 6
      }
      struct (0, 1),
      {
        Faction = Pirates
        Class = Battleship
        Direction = SE
        HP = 200
        MaxHP = 200
        Defense = 30
        MoveRange = 3
        AttackRange = 2
      }
      struct (map.Grid.Width - 1, map.Grid.Height - 1),
      {
        Faction = Federation
        Class = Fighter
        Direction = NW
        HP = 100
        MaxHP = 100
        Defense = 10
        MoveRange = 7
        AttackRange = 4
      }
      struct (map.Grid.Width - 1, map.Grid.Height - 2),
      {
        Faction = Federation
        Class = Cruiser
        Direction = NW
        HP = 150
        MaxHP = 150
        Defense = 15
        MoveRange = 5
        AttackRange = 6
      }
      struct (map.Grid.Width - 2, map.Grid.Height - 1),
      {
        Faction = Federation
        Class = Battleship
        Direction = NW
        HP = 200
        MaxHP = 200
        Defense = 30
        MoveRange = 3
        AttackRange = 2
      }
    ]

  let turnOrder = Phase.createTurnOrder [| Federation; Pirates |]

  let model =
    Model(
      Time = {
        ElapsedGameTime = TimeSpan.Zero
        TotalTime = TimeSpan.Zero
      },
      Input = Input.init,
      Cam = Camera.init(),
      Map = map,
      Units = units,
      UnitSprites = SBAssets.initUnitSprites assets,
      Decorations = AnimatedDecorations.init map.Grid assets,
      Turn = Phase.newTurn turnOrder,
      TurnOrder = turnOrder,
      Anim = Idle,
      GameAssets = assets,
      Skybox = Shaders.Skybox.init(Constants.VPWidth, Constants.VPHeight)
    )

  model, Cmd.none

// ─────────────────────────────────────────────────────────────
// Update
// ─────────────────────────────────────────────────────────────

let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
  match msg with
  | InputChanged input ->
    model.Input <- { model.Input with State = input }
    model, Cmd.none

  | MouseAction action ->
    match action with
    | MouseAction.Zoom z ->
      let cam = Camera.applyZoom z model.Cam
      let mutable c = cam.Camera
      Camera.clampToMapBounds model.Map.Grid &c

      model.Cam <- { Camera = c }
      model, Cmd.none
    | MouseAction.MovedTo pos ->
      model.Input <-
        Input.updateHover pos model.Cam.Camera model.Map.Grid model.Input

      model, Cmd.none
    | MouseAction.Select _
    | MouseAction.GetInfo _ -> model, Cmd.none

  | Tick gt ->
    let mutable model = model
    model.Time <- gt

    let cam = Camera.applyMovement model.Input.State gt model.Cam
    let mutable c = cam.Camera
    Camera.clampToMapBounds model.Map.Grid &c

    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    let decorations =
      AnimatedDecorations.update dt model.Map.Grid c model.Decorations

    let struct (anim, event) = AnimState.update dt model.Anim

    let cmd =
      match event with
      | ValueSome AnimationEvent.MoveComplete -> Cmd.ofMsg(PhaseMsg Resolution)
      | _ -> Cmd.none

    model.Cam <- { Camera = c }
    model.Decorations <- decorations
    model.Anim <- anim

    model, cmd

  | PhaseMsg phase ->
    let struct (turn, turnOrder) =
      Phase.System.update phase model.Turn model.TurnOrder

    let anim =
      match turn.Phase with
      | Resolving ->
        AnimState.startMove
          struct (0, 0)
          struct (1, 0)
          Vector2.Zero
          Vector2.UnitX
          model.Anim
      | Active -> model.Anim

    model.Turn <- turn
    model.TurnOrder <- turnOrder
    model.Anim <- anim

    model, Cmd.none

  | AnimationMsg msg ->
    match msg with
    | AnimationMsg.StartMove(from, dest, fromPos, toPos) ->
      model.Anim <- AnimState.startMove from dest fromPos toPos model.Anim
      model, Cmd.none
    | AnimationMsg.ShowBanner(message, duration) ->
      model.Anim <- AnimState.showBanner message duration model.Anim
      model, Cmd.none
    | AnimationMsg.Tick _ -> model, Cmd.none

// ─────────────────────────────────────────────────────────────
// View
// ─────────────────────────────────────────────────────────────

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  buffer
  |> Shaders.Skybox.render
    (model.Cam.Camera.Target, Constants.VPWidth, Constants.VPHeight)
    model.Skybox
  |> Draw.drop

  Camera.beginView model.Cam buffer
  |> Map.view
    ctx
    model.Decorations
    model.Cam.Camera
    model.Map.Grid
    model.Input.HoveredOver
  |> Draw.drop

  Units.view
    ctx
    model.Units
    model.UnitSprites
    model.Map.Grid
    model.Cam.Camera
    buffer
  |> Draw.drop

  Camera.endView buffer |> Draw.drop

// ─────────────────────────────────────────────────────────────
// Subscriptions
// ─────────────────────────────────────────────────────────────

let subscriptions (ctx: GameContext) (model: Model) : Sub<Msg> =
  let zoomSub = Mouse.onScroll (fun scroll -> MouseAction(Zoom scroll)) ctx
  let clickSub = Mouse.onRightClick (fun pos -> MouseAction(Select pos)) ctx
  let infoSub = Mouse.onLeftClick (fun pos -> MouseAction(GetInfo pos)) ctx
  let posSub = Mouse.onMove (fun pos -> MouseAction(MovedTo pos)) ctx
  let inputSub = InputMapper.subscribeStatic Input.inputMap InputChanged ctx

  Sub.batch [ zoomSub; clickSub; infoSub; posSub; inputSub ]

// ─────────────────────────────────────────────────────────────
// Program
// ─────────────────────────────────────────────────────────────

[<EntryPoint>]
let main _ =
  let program =
    Program.mkProgram init update
    |> Program.withConfig(fun cfg -> {
      cfg with
          Width = 1280
          Height = 720
          Title = "Mibo Raylib 2D Game"
          TargetFPS = 60
    })
    |> Program.withAssetsBasePath AppContext.BaseDirectory
    |> Program.withInput
    |> Program.withSubscription subscriptions
    |> Program.withTick Tick
    |> Program.withRenderer(fun () -> Renderer2D.create view)

  let game = new RaylibGame<Model, Msg>(program)
  game.Run()
  0
