module SpaceBattle.Program

open System
open System.Numerics
open Mibo.Animation
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Input
open Mibo.Layout
open AnimState
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

  member val Turn: Phase.Turn = Unchecked.defaultof<_> with get, set
  member val TurnOrder: Phase.TurnOrder = Unchecked.defaultof<_> with get, set
  member val Anim: AnimationState = Unchecked.defaultof<_> with get, set
  member val GameAssets: GameAssets = Unchecked.defaultof<_> with get, set
  member val Skybox: Shaders.SkyboxModel = Unchecked.defaultof<_> with get, set

// ─────────────────────────────────────────────────────────────
// Messages
// ─────────────────────────────────────────────────────────────

[<Struct>]
type Msg =
  | InputMsg of input: InputMsg
  | MapMsg of mapMsg: MapMsg
  | UnitsMsg of unitsMsg: UnitsMsg
  | Tick of tick: GameTime
  | PhaseMsg of phase: Phase.PhaseMsg
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
        id = 1<UnitId>
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
        id = 2<UnitId>
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
        id = 3<UnitId>
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
        id = 4<UnitId>
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
        id = 5<UnitId>
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
        id = 6<UnitId>
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

  let turnOrder = Phase.createTurnOrder [| Pirates; Federation |]

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
  | InputMsg inputMsg ->
    match inputMsg with
    | MouseAction(Zoom z) ->
      let cam = Camera.applyZoom z model.Cam
      let mutable c = cam.Camera
      Camera.clampToMapBounds model.Map.Grid &c
      model.Cam <- { Camera = c }
    | _ -> ()

    let struct (input, cmd) =
      Input.update inputMsg model.Input model.Units model.Turn.CurrentFaction


    let cmd =
      cmd
      |> Cmd.map(fun msg ->
        match msg with
        | CalculateRange ->
          MapMsg(
            RecalculateRange(
              input.Selection,
              input.HoveredOver,
              model.Units,
              model.Turn.CurrentFaction
            )
          )
        | other -> InputMsg other)

    model.Input <- input

    let cmd =
      match inputMsg with
      | CellClicked cell ->
        Cmd.batch [ cmd; Cmd.ofMsg(PhaseMsg(Phase.PhaseMsg.CellClicked cell)) ]
      | _ -> cmd

    model, cmd

  | MapMsg mapMsg ->
    model.Map <- Map.update mapMsg model.Map
    model, Cmd.none

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
      | ValueSome AnimationEvent.MoveComplete ->
        Cmd.ofMsg(PhaseMsg Phase.PhaseMsg.Resolution)
      | _ -> Cmd.none

    model.Cam <- { Camera = c }
    model.Decorations <- decorations
    model.Anim <- anim

    model, cmd

  | PhaseMsg phaseMsg ->
    let query: Phase.PhaseQuery = {
      Selection = model.Input.Selection
      UnitAt =
        fun cell ->
          match model.Units |> Map.tryFind cell with
          | Some u -> ValueSome u
          | None -> ValueNone
      IsReachable = fun cell -> model.Map.Reachable.Contains cell
      CurrentFaction = model.Turn.CurrentFaction
    }

    let struct (result, phaseCmd) =
      Phase.System.update(
        {
          Msg = phaseMsg
          Query = query
          Turn = model.Turn
          TurnOrder = model.TurnOrder
        }
      )

    model.Turn <- result.Turn
    model.TurnOrder <- result.TurnOrder

    let intentCmd =
      match result.Intent with
      | Phase.Intent.PerformMove move ->
        let struct (fc, fr) = move.From
        let struct (dc, dr) = move.Dest
        let fromPos = model.Map.Grid |> HexGrid.getWorldPos fc fr
        let toPos = model.Map.Grid |> HexGrid.getWorldPos dc dr

        Cmd.batch [
          Cmd.ofMsg(
            AnimationMsg(
              AnimationMsg.StartMove(move.From, move.Dest, fromPos, toPos)
            )
          )
          Cmd.ofMsg(InputMsg ClearSelection)
        ]
      | Phase.Intent.SwitchSelection cell ->
        Cmd.ofMsg(InputMsg(SelectCell cell))
      | Phase.Intent.ClearSelection -> Cmd.ofMsg(InputMsg ClearSelection)
      | Phase.Intent.MoveResolved resolved ->
        Cmd.ofMsg(UnitsMsg(MoveUnit(resolved.Source, resolved.Dest)))
      | Phase.Intent.AttackResolved _resolved -> Cmd.none // future: apply damage via UnitsMsg
      | Phase.Intent.PerformAttack _ -> Cmd.none // future: start attack animation
      | Phase.Intent.NoIntent -> Cmd.none

    model, Cmd.batch [ phaseCmd |> Cmd.map PhaseMsg; intentCmd ]

  | AnimationMsg msg ->
    match msg with
    | AnimationMsg.StartMove(from, dest, fromPos, toPos) ->
      model.Anim <- AnimState.startMove from dest fromPos toPos model.Anim
      model, Cmd.none
    | AnimationMsg.ShowBanner(message, duration) ->
      model.Anim <- AnimState.showBanner message duration model.Anim
      model, Cmd.none
    | AnimationMsg.Tick _ -> model, Cmd.none

  | UnitsMsg unitsMsg ->
    model.Units <- Units.update unitsMsg model.Units

    let cmd =
      match unitsMsg with
      | MoveUnit _ ->
        Cmd.ofMsg(
          MapMsg(
            RecalculateRange(
              model.Input.Selection,
              model.Input.HoveredOver,
              model.Units,
              model.Turn.CurrentFaction
            )
          )
        )
      | _ -> Cmd.none

    model, cmd


module ModelDebugoverlay =

  open DebugUtils

  [<Literal>]
  let ShowDebug = true

  [<Literal>]
  let PanelWidth = 320

  [<Literal>]
  let PanelMargin = 10

  let view (model: Model) (buffer: RenderBuffer2D) : RenderBuffer2D =
    if not ShowDebug then
      buffer
    else
      let font = model.GameAssets.MonoFont
      let style = DebugUtils.defaultStyle
      let x = PanelMargin
      let startY = PanelMargin

      let struct (y, buffer) =
        Phase.Debug.view font style model.Turn model.TurnOrder x startY buffer

      let struct (y, buffer) =
        Input.Debug.view font style model.Input x y buffer

      let struct (y, buffer) =
        Units.Debug.view font style model.Units x y buffer

      let struct (y, buffer) = Camera.Debug.view font style model.Cam x y buffer

      let struct (y, buffer) =
        AnimState.Debug.view font style model.Anim x y buffer

      let totalHeight = y - startY + PanelMargin

      buffer
      |> DebugUtils.background
        (x - PanelMargin)
        (startY - PanelMargin)
        (PanelWidth + PanelMargin * 2)
        totalHeight
        style

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
    model.Map
    model.Input.HoveredOver
  |> Draw.drop

  let movingUnit =
    match model.Anim with
    | AnimState.Moving tween ->
      let struct (tc, tr) = tween.From

      ValueSome
        struct (tc, tr, Vector2.Lerp(tween.FromPos, tween.ToPos, tween.Progress))
    | _ -> ValueNone

  buffer
  |> Units.view
    ctx
    model.Units
    model.UnitSprites
    model.Map.Grid
    movingUnit
    model.Cam.Camera
  |> Draw.drop

  Camera.endView buffer |> Draw.drop

  ModelDebugoverlay.view model buffer |> Draw.drop

// ─────────────────────────────────────────────────────────────
// Subscriptions
// ─────────────────────────────────────────────────────────────

let subscriptions (ctx: GameContext) (model: Model) : Sub<Msg> =
  let zoomSub =
    Mouse.onScroll (fun scroll -> InputMsg(MouseAction(Zoom scroll))) ctx

  let clickSub =
    Mouse.onRightClick
      (fun pos ->
        let cell = Input.cellFromMouse pos model.Cam.Camera model.Map.Grid
        InputMsg(MouseAction(GetInfo cell)))
      ctx

  let infoSub =
    Mouse.onLeftClick
      (fun pos ->
        let cell = Input.cellFromMouse pos model.Cam.Camera model.Map.Grid
        InputMsg(MouseAction(Select cell)))
      ctx

  let posSub =
    Mouse.onMove
      (fun pos ->
        let cell = Input.cellFromMouse pos model.Cam.Camera model.Map.Grid
        InputMsg(MouseAction(Hover cell)))
      ctx

  let inputSub =
    InputMapper.subscribeStatic Input.inputMap (InputChanged >> InputMsg) ctx

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
