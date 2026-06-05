module SpaceBattle.Program

open System
open System.Numerics
open System.Threading
open Mibo.Animation
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Input
open Mibo.Layout
open Mibo.Input
open Phase
open AnimState
open SpaceBattle.Map
open SpaceBattle.Types
open SpaceBattle.Units

// ─────────────────────────────────────────────────────────────
// Input
// ─────────────────────────────────────────────────────────────

[<Struct>]
type GameAction =
  | MoveLeft
  | MoveRight
  | MoveUp
  | MoveDown
  | GetInfo
  | Select

[<Struct>]
type MouseAction =
  | Zoom of zoom: float32
  | Select of Vector2
  | GetInfo of Vector2
  | MovedTo of Vector2

let inputMap =
  InputMap.empty
  |> InputMap.key MoveLeft KeyboardKey.Left
  |> InputMap.key MoveLeft KeyboardKey.A
  |> InputMap.key MoveRight KeyboardKey.Right
  |> InputMap.key MoveRight KeyboardKey.D
  |> InputMap.key MoveUp KeyboardKey.Up
  |> InputMap.key MoveUp KeyboardKey.W
  |> InputMap.key MoveDown KeyboardKey.Down
  |> InputMap.key MoveDown KeyboardKey.S

// ─────────────────────────────────────────────────────────────
// Model
// ─────────────────────────────────────────────────────────────

type Model = {
  mutable Time: GameTime
  Input: ActionState<GameAction>
  MouseActions: MouseAction voption
  Seed: int
  Camera: Camera2D
  Map: HexGrid<Tile>
  Units: Map<struct (int * int), SBUnit>
  AnimatedSprites: Map<struct (int * int), AnimatedSprite>
  UnitSprites: Map<struct (Faction * UnitClass), SpriteSheet>
  Turn: Turn
  TurnOrder: TurnOrder
  Anim: AnimationState
  GameAssets: GameAssets
  Skybox: Shaders.SkyboxModel
  HoveredOver: struct (int * int) voption
}

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
  let seed = Random.Shared.Next 10001
  let turnOrder = Phase.createTurnOrder [| Units.Federation; Units.Pirates |]
  let map = Map.createMap Vector2.Zero 12 12 |> Map.fillMap(Random seed)
  let assets = SBAssets.loadSpriteSheets ctx
  let unitAssets = assets.FactionAssets
  let mutable unitSprites = Map.empty

  for faction, unitClass in
    [
      Federation, Fighter
      Federation, Cruiser
      Federation, Battleship
      Pirates, Fighter
      Pirates, Cruiser
      Pirates, Battleship
    ] do
    let sprite =
      match unitClass with
      | Fighter -> unitAssets[faction].Fighter
      | Cruiser -> unitAssets[faction].Cruiser
      | Battleship -> unitAssets[faction].BattleShip

    unitSprites <- Map.add struct (faction, unitClass) (sprite) unitSprites

  let mutable animatedSprites = Map.empty

  map
  |> HexGrid.iter(fun col row tile ->
    match tile with
    | Asteroid1 ->
      let asteroidSH =
        assets.Decorations
        |> Map.tryFind Asteroid
        |> Option.map(Array.tryHead)
        |> Option.flatten
        |> Option.defaultWith(fun () -> failwith "no asteroid")

      let animated = AnimatedSprite.create asteroidSH "spin"
      animatedSprites <- Map.add struct (col, row) animated animatedSprites
    | Asteroid2 ->
      let asteroidSH =
        assets.Decorations
        |> Map.tryFind Asteroid
        |> Option.map(Array.tryLast)
        |> Option.flatten
        |> Option.defaultWith(fun () -> failwith "no asteroid")

      let animated = AnimatedSprite.create asteroidSH "spin"
      animatedSprites <- Map.add struct (col, row) animated animatedSprites
    | Crate1 ->
      let crateSH =
        assets.Decorations
        |> Map.tryFind Crate
        |> Option.map(Array.tryHead)
        |> Option.flatten
        |> Option.defaultWith(fun () -> failwith "no crate")

      let animated = AnimatedSprite.create crateSH "spin"
      animatedSprites <- Map.add struct (col, row) animated animatedSprites
    | Crate2 ->
      let crateSH =
        assets.Decorations
        |> Map.tryFind Crate
        |> Option.map(Array.tryLast)
        |> Option.flatten
        |> Option.defaultWith(fun () -> failwith "no crate")

      let animated = AnimatedSprite.create crateSH "spin"
      animatedSprites <- Map.add struct (col, row) animated animatedSprites
    | Station ->
      let animated = AnimatedSprite.create assets.Station "spin"
      animatedSprites <- Map.add struct (col, row) animated animatedSprites
    | DeepSpace -> ())

  let skybox = Shaders.Skybox.init(Constants.VPWidth, Constants.VPHeight)

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

      struct (map.Width - 1, map.Height - 1),
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
      struct (map.Width - 1, map.Height - 2),
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
      struct (map.Width - 2, map.Height - 1),
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

  let model = {
    Time = {
      ElapsedGameTime = TimeSpan.Zero
      TotalTime = TimeSpan.Zero
    }
    Input = ActionState.empty
    MouseActions = ValueNone
    Camera =
      Camera2D.create
        Vector2.Zero
        1f
        (Vector2(Constants.VPWidth, Constants.VPHeight))
    Map = map
    Seed = seed
    Units = units
    TurnOrder = turnOrder
    Turn = Phase.newTurn turnOrder
    Anim = Idle
    AnimatedSprites = animatedSprites
    UnitSprites = unitSprites
    GameAssets = assets
    Skybox = skybox
    HoveredOver = ValueNone
  }

  model, Cmd.none

// ─────────────────────────────────────────────────────────────
// Update
// ─────────────────────────────────────────────────────────────


let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
  match msg with
  | InputChanged input -> { model with Input = input }, Cmd.none
  | MouseAction(mouseAction) ->
    match mouseAction with
    | Zoom zoom ->
      let mutable camera = model.Camera
      camera.Zoom <- max 0.1f (camera.Zoom + zoom * 0.1f)
      { model with Camera = camera }, Cmd.none
    | Select pos
    | GetInfo pos ->
      let worldPos = Raylib.GetScreenToWorld2D(pos, model.Camera)
      let cell = model.Map |> Mibo.Layout.Hex2DSpatial.worldToCell worldPos
      model, Cmd.none
    | MovedTo pos ->
      let worldPos = Raylib.GetScreenToWorld2D(pos, model.Camera)
      let cell = model.Map |> Mibo.Layout.Hex2DSpatial.worldToCell worldPos

      { model with HoveredOver = cell }, Cmd.none

  | Tick gt ->
    let mutable model = model
    model.Time <- gt

    let speed = 300f * float32 gt.ElapsedGameTime.TotalSeconds
    let mutable cam = model.Camera

    if model.Input.Held.Contains MoveLeft then
      cam.Target <- cam.Target + Vector2(-speed, 0f)

    if model.Input.Held.Contains MoveRight then
      cam.Target <- cam.Target + Vector2(speed, 0f)

    if model.Input.Held.Contains MoveUp then
      cam.Target <- cam.Target + Vector2(0f, -speed)

    if model.Input.Held.Contains MoveDown then
      cam.Target <- cam.Target + Vector2(0f, speed)

    let dt = float32 gt.ElapsedGameTime.TotalSeconds
    let topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, model.Camera)

    let bottomRight =
      Raylib.GetScreenToWorld2D(
        Vector2(Constants.VPWidth, Constants.VPHeight),
        model.Camera
      )

    let mutable animatedSprites = model.AnimatedSprites

    model.Map
    |> HexGrid.iterVisible
      topLeft.X
      topLeft.Y
      bottomRight.X
      bottomRight.Y
      (fun col row tile ->
        match model.AnimatedSprites |> Map.tryFind struct (col, row) with
        | Some animated ->
          animatedSprites <-
            Map.add
              struct (col, row)
              (AnimatedSprite.update dt animated)
              animatedSprites
        | None -> ())

    let struct (anim, event) = AnimState.update dt model.Anim

    let cmd =
      match event with
      | ValueSome AnimationEvent.MoveComplete -> Cmd.ofMsg(PhaseMsg Resolution)
      | _ -> Cmd.none

    {
      model with
          Camera = cam
          Anim = anim
          AnimatedSprites = animatedSprites
    },
    cmd
  | PhaseMsg phase ->
    let struct (turn, turnOrder) =
      Phase.System.update phase model.Turn model.TurnOrder

    let anim =
      match turn.Phase with
      | Resolving ->
        // Placeholder: start a move animation. Real positions come from unit data.
        AnimState.startMove
          struct (0, 0)
          struct (1, 0)
          Vector2.Zero
          Vector2.UnitX
          model.Anim
      | Active -> model.Anim

    {
      model with
          Turn = turn
          TurnOrder = turnOrder
          Anim = anim
    },
    Cmd.none

  | AnimationMsg msg ->
    match msg with
    | AnimationMsg.StartMove(from, dest, fromPos, toPos) ->
      {
        model with
            Anim = AnimState.startMove from dest fromPos toPos model.Anim
      },
      Cmd.none
    | AnimationMsg.ShowBanner(message, duration) ->
      {
        model with
            Anim = AnimState.showBanner message duration model.Anim
      },
      Cmd.none
    | AnimationMsg.Tick _ -> model, Cmd.none



// ─────────────────────────────────────────────────────────────
// View
// ─────────────────────────────────────────────────────────────

module Layer0 =
  let camera = Draw.beginCamera 0<RenderLayer>
  let endCamera = Draw.endCamera 0<RenderLayer>

  let brownCircle = Draw.fillCircle(0<RenderLayer>, Color.Brown)

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer2D) =
  buffer
  |> Shaders.Skybox.render
    (model.Camera.Target, Constants.VPWidth, Constants.VPHeight)
    model.Skybox
  |> Draw.drop

  Layer0.camera model.Camera buffer
  |> Layer0.brownCircle(Vector2.Zero, 64f)
  |> Draw.drop

  Map.view
    ctx
    model.AnimatedSprites
    model.Camera
    model.Map
    model.HoveredOver
    buffer
  |> Draw.drop

  Units.view ctx model.Units model.UnitSprites model.Map model.Camera buffer
  |> Draw.drop

  Layer0.endCamera buffer |> Draw.drop


let subscriptions (ctx: GameContext) (model: Model) : Sub<Msg> =
  let zoomSub = Mouse.onScroll (fun scroll -> MouseAction(Zoom scroll)) ctx

  let clickSub = Mouse.onRightClick (fun pos -> MouseAction(Select pos)) ctx
  let infoSub = Mouse.onLeftClick (fun pos -> MouseAction(GetInfo pos)) ctx
  let posSub = Mouse.onMove (fun pos -> MouseAction(MovedTo pos)) ctx
  let inputSub = InputMapper.subscribeStatic inputMap InputChanged ctx

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
