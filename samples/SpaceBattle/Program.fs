module SpaceBattle.Program

open System
open System.Numerics
open Mibo.Animation
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Input
open Mibo.Layout
open Raylib_cs
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
  member val Effects: Effects.EffectState = Unchecked.defaultof<_> with get, set
  member val Fog: FogOfWar.FogState = Unchecked.defaultof<_> with get, set
  member val GameOver: Faction voption = ValueNone with get, set
  member val VPWidth: float32 = Unchecked.defaultof<_> with get, set
  member val VPHeight: float32 = Unchecked.defaultof<_> with get, set

// ─────────────────────────────────────────────────────────────
// Messages
// ─────────────────────────────────────────────────────────────

[<Struct>]
type Msg =
  | InputMsg of input: InputMsg
  | MapMsg of mapMsg: MapMsg
  | UnitsMsg of unitsMsg: UnitsMsg
  | CameraMsg of cameraMsg: CameraMsg
  | Tick of tick: GameTime
  | PhaseMsg of phase: Phase.PhaseMsg
  | AnimationMsg of animation: AnimationMsg
  | RestartGame

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
        VisualRange = 3
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
        VisualRange = 5
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
        VisualRange = 6
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
        VisualRange = 3
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
        VisualRange = 5
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
        VisualRange = 6
      }
    ]

  let turnOrder = Phase.createTurnOrder [| Pirates; Federation |]

  let vpW = float32 ctx.WindowWidth
  let vpH = float32 ctx.WindowHeight

  let model =
    Model(
      Time = {
        ElapsedGameTime = TimeSpan.Zero
        TotalTime = TimeSpan.Zero
      },
      Input = Input.init,
      Cam = Camera.init(vpW, vpH),
      Map = map,
      Units = units,
      UnitSprites = SBAssets.initUnitSprites assets,
      Decorations = AnimatedDecorations.init map.Grid assets,
      Turn = Phase.newTurn turnOrder,
      TurnOrder = turnOrder,
      Anim = Idle,
      GameAssets = assets,
      Skybox = Shaders.Skybox.init(vpW, vpH),
      Effects = Effects.init(),
      Fog = FogOfWar.init(),
      VPWidth = vpW,
      VPHeight = vpH
    )

  model.Map <- {
    model.Map with
        Visible =
          Map.computeVisibleUnits
            model.Units
            model.Turn.CurrentFaction
            model.Map.Grid
  }

  model, Cmd.none

let private resetGameState(model: Model) =
  let map = Map.init(Random.Shared.Next 10001)

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
        VisualRange = 3
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
        VisualRange = 5
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
        VisualRange = 6
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
        VisualRange = 3
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
        VisualRange = 5
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
        VisualRange = 6
      }
    ]

  let turnOrder = Phase.createTurnOrder [| Pirates; Federation |]

  model.Input <- Input.init
  model.Map <- map
  model.Units <- units
  model.UnitSprites <- SBAssets.initUnitSprites model.GameAssets
  model.Decorations <- AnimatedDecorations.init map.Grid model.GameAssets
  model.Turn <- Phase.newTurn turnOrder
  model.TurnOrder <- turnOrder
  model.Anim <- Idle
  model.Effects <- Effects.init()
  model.Fog <- FogOfWar.init()
  model.GameOver <- ValueNone

  model.Map <- {
    model.Map with
        Visible =
          Map.computeVisibleUnits
            model.Units
            model.Turn.CurrentFaction
            model.Map.Grid
  }

  model

// ─────────────────────────────────────────────────────────────
// Update
// ─────────────────────────────────────────────────────────────

let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
  match msg with
  | InputMsg inputMsg ->
    match model.GameOver with
    | ValueSome _ ->
      match inputMsg with
      | InputChanged inputs when inputs.Started.Contains Restart ->
        resetGameState model, Cmd.none
      | _ -> model, Cmd.none
    | ValueNone ->

    let camCmd =
      match inputMsg with
      | MouseAction(Zoom z) -> Cmd.ofMsg(CameraMsg(CameraMsg.ApplyZoom z))
      | _ -> Cmd.none

    let struct (input, inputCmd) =
      Input.update inputMsg model.Input model.Units model.Turn.CurrentFaction

    let inputCmd =
      inputCmd
      |> Cmd.map(fun msg ->
        match msg with
        | CalculateRange ->
          let canMove, canAct =
            match input.Selection with
            | Selected cell ->
              match model.Units |> Map.tryFind cell with
              | Some unit when unit.Faction = model.Turn.CurrentFaction ->
                Phase.canMove unit.id model.Turn,
                Phase.canPerformAction unit.id model.Turn
              | _ -> false, false
            | NoSelection -> false, false

          MapMsg(
            RecalculateRange {
              Selection = input.Selection
              Hovered = input.HoveredOver
              Units = model.Units
              CurrentFaction = model.Turn.CurrentFaction
              CanMove = canMove
              CanAct = canAct
            }
          )
        | other -> InputMsg other)

    model.Input <- input

    match inputMsg with
    | InputChanged inputs when inputs.Started.Contains ToggleFullScreen ->
      model.VPWidth <- float32(Raylib.GetScreenWidth())
      model.VPHeight <- float32(Raylib.GetScreenHeight())
    | _ -> ()

    let inputCmd =
      match inputMsg with
      | CellClicked cell ->
        Cmd.batch [
          inputCmd
          Cmd.ofMsg(PhaseMsg(Phase.PhaseMsg.CellClicked cell))
        ]
      | InputChanged inputs when
        inputs.Started.Contains EndTurn && model.Turn.Phase = Phase.Active
        ->
        Cmd.batch [ inputCmd; Cmd.ofMsg(PhaseMsg Phase.PhaseMsg.EndTurn) ]
      | _ -> inputCmd

    model, Cmd.batch [ camCmd; inputCmd ]

  | MapMsg mapMsg ->
    model.Map <- Map.update mapMsg model.Map
    model, Cmd.none

  | Tick gt ->
    let mutable model = model
    model.Time <- gt

    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    let cam =
      Camera.update
        (CameraMsg.ApplyMovement(model.Input.State.Held, dt))
        model.Cam

    model.Cam <-
      Camera.update
        (CameraMsg.ClampToMap(model.Map.Grid, model.VPWidth, model.VPHeight))
        cam

    let decorations =
      AnimatedDecorations.update
        dt
        model.Map.Grid
        model.Cam.Camera
        model.VPWidth
        model.VPHeight
        model.Decorations

    let attackTarget =
      match model.Anim with
      | AnimState.Attacking tween -> ValueSome tween.To
      | _ -> ValueNone

    let isLaser1 =
      match model.Anim with
      | AnimState.Attacking tween ->
        let struct (ac, ar) = tween.AttackerCell

        match model.Units |> Map.tryFind tween.AttackerCell with
        | Some u ->
          match u.Class with
          | Fighter -> false
          | Battleship -> true
          | Cruiser -> (ac + ar) % 2 = 0
        | None -> true
      | _ -> true

    let struct (anim, event) = AnimState.update dt model.Anim

    Effects.update model.Effects dt

    match anim with
    | AnimState.Attacking tween ->
      let laserPos = Vector2.Lerp(tween.From, tween.To, tween.Progress)

      let laserDir = Vector2.Normalize(tween.To - tween.From)
      Effects.spawnTrail model.Effects laserPos laserDir isLaser1
    | _ -> ()

    match event with
    | ValueSome AnimationEvent.AttackComplete ->
      match attackTarget with
      | ValueSome target -> Effects.spawnImpact model.Effects target isLaser1
      | ValueNone -> ()
    | _ -> ()

    let cmd =
      match event with
      | ValueSome AnimationEvent.MoveComplete ->
        Cmd.ofMsg(PhaseMsg Phase.PhaseMsg.Resolution)
      | ValueSome AnimationEvent.AttackComplete ->
        Cmd.ofMsg(PhaseMsg Phase.PhaseMsg.Resolution)
      | ValueSome(AnimationEvent.SegmentChanged dir) ->
        match model.Anim with
        | AnimState.Moving tween ->
          Cmd.ofMsg(UnitsMsg(UpdateDirection(tween.From, dir)))
        | _ -> Cmd.none
      | ValueSome(AnimationEvent.TransitionComplete _newFaction) ->
        Cmd.ofMsg(PhaseMsg Phase.PhaseMsg.TransitionDone)
      | ValueSome AnimationEvent.BannerComplete -> Cmd.none
      | ValueNone -> Cmd.none

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
      IsReachable =
        fun cell ->
          model.Map.Reachable.Contains cell
          || model.Map.AttackTargets.Contains cell
      IsVisible = fun cell -> model.Map.Visible.Contains cell

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

    match phaseMsg with
    | Phase.TransitionDone ->
      model.Map <- {
        model.Map with
            Visible =
              Map.computeVisibleUnits
                model.Units
                model.Turn.CurrentFaction
                model.Map.Grid
      }

    | _ -> ()

    let intentCmd =
      match result.Intent with
      | Phase.Intent.PerformMove move ->
        let path = model.Map.Path
        let unit = model.Units |> Map.find move.From

        let simplified = Selection.simplifyPath path model.Map.Grid

        let waypoints =
          simplified
          |> Array.map(fun struct (c, r) ->
            model.Map.Grid |> HexGrid.getWorldPos c r)

        let segDists = Array.zeroCreate simplified.Length
        segDists[0] <- 0f

        for i in 1 .. simplified.Length - 1 do
          let struct (c1, r1) = simplified[i - 1]
          let struct (c2, r2) = simplified[i]

          segDists[i] <-
            segDists[i - 1]
            + float32(Hex2DSpatial.distance c1 r1 c2 r2 model.Map.Grid)

        let totalDist = segDists[simplified.Length - 1]

        if totalDist > 0f then
          for i in 1 .. simplified.Length - 2 do
            segDists[i] <- segDists[i] / totalDist

        segDists[simplified.Length - 1] <- 1f

        let directions =
          simplified
          |> Array.pairwise
          |> Array.choose(fun (a, b) -> Units.directionFromCells a b)

        let duration = AnimState.moveDuration unit.MoveRange (path.Length - 1)

        let firstDir = directions |> Array.tryHead

        let dirCmd =
          match firstDir with
          | Some dir -> Cmd.ofMsg(UnitsMsg(UpdateDirection(move.From, dir)))
          | None -> Cmd.none

        Cmd.batch [
          dirCmd
          Cmd.ofMsg(
            AnimationMsg(
              AnimationMsg.StartMove(
                move.From,
                move.Dest,
                waypoints,
                segDists,
                directions,
                duration
              )
            )
          )
          Cmd.ofMsg(InputMsg ClearSelection)
        ]
      | Phase.Intent.SwitchSelection cell ->
        Cmd.ofMsg(InputMsg(SelectCell cell))
      | Phase.Intent.ClearSelection -> Cmd.ofMsg(InputMsg ClearSelection)
      | Phase.Intent.MoveResolved resolved ->
        Cmd.ofMsg(UnitsMsg(MoveUnit(resolved.Source, resolved.Dest)))
      | Phase.Intent.AttackResolved resolved ->
        Cmd.ofMsg(UnitsMsg(AttackUnit(resolved.Attacker, resolved.Target)))
      | Phase.Intent.PerformAttack attack ->
        let struct (ac, ar) = attack.AttackerCell
        let struct (tc, tr) = attack.Target
        let attackerPos = model.Map.Grid |> HexGrid.getWorldPos ac ar
        let targetPos = model.Map.Grid |> HexGrid.getWorldPos tc tr

        let shipDir = Units.directionFromCells attack.AttackerCell attack.Target

        let dirCmd =
          match shipDir with
          | Some dir ->
            Cmd.ofMsg(UnitsMsg(UpdateDirection(attack.AttackerCell, dir)))
          | None -> Cmd.none

        let laserDir = Units.directionFromWorldPositions attackerPos targetPos

        Cmd.batch [
          dirCmd
          Cmd.ofMsg(
            AnimationMsg(
              AnimationMsg.StartAttack(
                attackerPos,
                targetPos,
                laserDir,
                attack.AttackerCell,
                attack.Target,
                0.4f
              )
            )
          )
          Cmd.ofMsg(InputMsg ClearSelection)
        ]
      | Phase.Intent.StartTransition newFaction ->
        model.Anim <- AnimState.startTransition newFaction 2.0f model.Anim

        Cmd.none
      | Phase.Intent.NoIntent -> Cmd.none

    model, Cmd.batch [ phaseCmd |> Cmd.map PhaseMsg; intentCmd ]

  | AnimationMsg msg ->
    match msg with
    | AnimationMsg.StartMove(from,
                             dest,
                             waypoints,
                             segDists,
                             directions,
                             duration) ->
      model.Anim <-
        AnimState.startMove
          from
          dest
          waypoints
          segDists
          directions
          duration
          model.Anim

      model, Cmd.none
    | AnimationMsg.ShowBanner(message, duration) ->
      model.Anim <- AnimState.showBanner message duration model.Anim
      model, Cmd.none
    | AnimationMsg.StartAttack(from,
                               target,
                               dir,
                               attackerCell,
                               targetCell,
                               duration) ->
      model.Anim <-
        AnimState.startAttack
          from
          target
          dir
          attackerCell
          targetCell
          duration
          model.Anim

      model, Cmd.none
    | AnimationMsg.Tick _ -> model, Cmd.none

  | UnitsMsg unitsMsg ->
    model.Units <- Units.update unitsMsg model.Units

    model.Map <- {
      model.Map with
          Visible =
            Map.computeVisibleUnits
              model.Units
              model.Turn.CurrentFaction
              model.Map.Grid
    }

    match unitsMsg with
    | AttackUnit _ ->
      match Units.checkGameOver model.Units model.TurnOrder.Factions with
      | ValueSome winner ->
        model.GameOver <- ValueSome winner

        model.Anim <- AnimState.showBanner $"{winner} Wins!" 0.0f model.Anim
      | ValueNone -> ()
    | _ -> ()

    let cmd =
      match unitsMsg with
      | MoveUnit _ ->
        let canMove, canAct =
          match model.Input.Selection with
          | Selected cell ->
            match model.Units |> Map.tryFind cell with
            | Some unit when unit.Faction = model.Turn.CurrentFaction ->
              Phase.canMove unit.id model.Turn,
              Phase.canPerformAction unit.id model.Turn
            | _ -> false, false
          | NoSelection -> false, false

        Cmd.ofMsg(
          MapMsg(
            RecalculateRange {
              Selection = model.Input.Selection
              Hovered = model.Input.HoveredOver
              Units = model.Units
              CurrentFaction = model.Turn.CurrentFaction
              CanMove = canMove
              CanAct = canAct
            }
          )
        )
      | _ -> Cmd.none

    model, cmd

  | CameraMsg cameraMsg ->
    model.Cam <- Camera.update cameraMsg model.Cam
    model, Cmd.none
  | RestartGame -> failwith "Not Implemented"


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
  model.Effects.Lighting.Reset()

  buffer
  |> Shaders.Skybox.render
    (model.Cam.Camera.Target, model.VPWidth, model.VPHeight)
    model.Skybox
  |> Draw.drop

  Camera.beginView model.Cam buffer
  |> LightDraw.setAmbient
    model.Effects.Lighting
    (0<RenderLayer>, { Color = Effects.ambientColor })
  |> Draw.drop

  match model.Anim with
  | AnimState.Attacking tween ->
    let laserPos = Vector2.Lerp(tween.From, tween.To, tween.Progress)

    let struct (ac, ar) = tween.AttackerCell

    let isLaser1 =
      match model.Units |> Map.tryFind tween.AttackerCell with
      | Some u ->
        match u.Class with
        | Fighter -> false
        | Battleship -> true
        | Cruiser -> (ac + ar) % 2 = 0
      | None -> true

    let lightColor =
      if isLaser1 then
        Color(255uy, 100uy, 60uy)
      else
        Color(80uy, 255uy, 100uy)

    buffer
    |> LightDraw.addPointLight model.Effects.Lighting 0<RenderLayer> {
      Position = laserPos
      Color = lightColor
      Intensity = 2.5f
      Radius = 150.0f
      Falloff = 1.5f
      CastsShadows = false
    }
    |> Draw.drop
  | _ -> ()

  for flash in model.Effects.ImpactFlashes do
    buffer
    |> LightDraw.addPointLight model.Effects.Lighting 0<RenderLayer> {
      Position = flash.Position
      Color = Color(255uy, 255uy, 200uy)
      Intensity = flash.Intensity
      Radius = flash.Radius
      Falloff = 2.0f
      CastsShadows = false
    }
    |> Draw.drop

  buffer
  |> Map.viewTiles
    model.VPWidth
    model.VPHeight
    model.Decorations
    model.Cam.Camera
    model.Map
    model.Effects.Lighting
  |> Draw.drop

  FogOfWar.render
    model.Fog
    model.Map.Visible
    model.Map.Grid
    model.Cam.Camera
    model.VPWidth
    model.VPHeight
    buffer
  |> Draw.drop

  let movingUnit =
    match model.Anim with
    | AnimState.Moving tween ->
      let struct (tc, tr) = tween.From

      let pos =
        AnimState.interpolatePosition
          tween.Waypoints
          tween.SegmentDists
          tween.Progress

      ValueSome struct (tc, tr, pos)
    | _ -> ValueNone

  buffer
  |> Units.view
    model.VPWidth
    model.VPHeight
    model.Units
    model.UnitSprites
    model.Map.Grid
    movingUnit
    model.Map.Visible
    model.Effects.Lighting
    model.Cam.Camera
  |> Draw.drop

  match model.Anim with
  | AnimState.Attacking tween ->
    let pos = Vector2.Lerp(tween.From, tween.To, tween.Progress)

    let struct (ac, ar) = tween.AttackerCell
    let attackerUnit = model.Units |> Map.tryFind tween.AttackerCell

    let laser =
      match attackerUnit with
      | Some u ->
        match u.Class with
        | Fighter -> model.GameAssets.Laser2
        | Battleship -> model.GameAssets.Laser1
        | Cruiser ->
          if (ac + ar) % 2 = 0 then
            model.GameAssets.Laser1
          else
            model.GameAssets.Laser2
      | None -> model.GameAssets.Laser1

    let fw = float32 laser.FrameSize.X
    let fh = float32 laser.FrameSize.Y

    let source = Rectangle(0f, 0f, fw, fh)

    let targetRect = Rectangle(pos.X, pos.Y, fw, fh)

    let dx = tween.To.X - tween.From.X
    let dy = tween.To.Y - tween.From.Y
    let angle = atan2 dy dx * 180f / float32 System.Math.PI + 90f
    let origin = Vector2(fw / 2f, fh / 2f)

    buffer
    |> LightDraw.litSprite
      model.Effects.Lighting
      (SpriteState.create(laser.Texture, targetRect, source)
       |> SpriteState.withRotation angle
       |> SpriteState.withOrigin origin)
    |> Draw.drop
  | _ -> ()

  buffer
  |> LightDraw.endLighting model.Effects.Lighting 0<RenderLayer>
  |> Draw.drop

  if model.Effects.ParticleCount > 0 then
    buffer
    |> Draw.setBlend 0<RenderLayer> BlendMode.Additive
    |> ParticleDraw.particles
      model.Effects.ParticleTexture
      model.Effects.Particles
      model.Effects.ParticleCount
      0<RenderLayer>
    |> Draw.setBlend 0<RenderLayer> BlendMode.Alpha
    |> Draw.drop

  buffer
  |> UI.drawHpBars
    model.VPWidth
    model.VPHeight
    model.Units
    model.Map.Grid
    model.Map.Visible
    movingUnit
    model.Turn
    model.Cam.Camera
  |> Draw.drop

  let infoMode = model.Input.State.Held.Contains InfoMode

  if infoMode then
    buffer
    |> UI.drawInfoOverlays
      model.VPWidth
      model.VPHeight
      model.Units
      model.Map.Grid
      model.Map.Visible
      model.Input.HoveredOver
      model.Cam.Camera
    |> Draw.drop
  else
    buffer
    |> Map.viewOverlays
      model.VPWidth
      model.VPHeight
      model.Cam.Camera
      model.Map
      model.Input.HoveredOver
    |> Draw.drop

  match model.Anim with
  | AnimState.Transitioning transition ->
    let alpha = byte(int(transition.Timer / transition.Duration * 180.0f))

    let factionColor =
      match transition.NewFaction with
      | Federation -> Color(60uy, 120uy, 255uy, alpha)
      | Empire -> Color(255uy, 60uy, 60uy, alpha)
      | Pirates -> Color(60uy, 255uy, 120uy, alpha)

    let cx = model.VPWidth / 2.0f
    let cy = model.VPHeight / 2.0f

    buffer
    |> Draw.fillRect
      (0<RenderLayer>, Color(0uy, 0uy, 0uy, alpha))
      (Rectangle(0f, 0f, model.VPWidth, model.VPHeight))
    |> Draw.text(
      TextState.create(
        model.GameAssets.MonoFont,
        $"{transition.NewFaction}'s Turn",
        Vector2(cx - 120.0f, cy - 30.0f)
      )
      |> TextState.withFontSize 48.0f
      |> TextState.withSpacing 2.0f
      |> TextState.withColor factionColor
    )
    |> Draw.drop
  | _ -> ()

  Camera.endView buffer |> Draw.drop

  buffer
  |> UI.drawTurnIndicator
    model.Turn
    model.TurnOrder
    model.GameAssets.MonoFont
    model.VPWidth
  |> Draw.drop

  ModelDebugoverlay.view model buffer |> Draw.drop

  match model.GameOver with
  | ValueSome winner ->
    let cx = model.VPWidth / 2.0f
    let cy = model.VPHeight / 2.0f

    buffer
    |> Draw.fillRect
      (0<RenderLayer>, Color(0uy, 0uy, 0uy, 180uy))
      (Rectangle(0f, 0f, model.VPWidth, model.VPHeight))
    |> Draw.text(
      TextState.create(
        model.GameAssets.MonoFont,
        $"{winner} Wins!",
        Vector2(cx - 140.0f, cy - 40.0f)
      )
      |> TextState.withFontSize 56.0f
      |> TextState.withSpacing 2.0f
      |> TextState.withColor Color.White
    )
    |> Draw.text(
      TextState.create(
        model.GameAssets.MonoFont,
        "Press R to restart",
        Vector2(cx - 100.0f, cy + 30.0f)
      )
      |> TextState.withFontSize 24.0f
      |> TextState.withSpacing 1.0f
      |> TextState.withColor Color.Gray
    )
    |> Draw.drop
  | ValueNone -> ()

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
          Width = int Constants.VPWidth
          Height = int Constants.VPHeight
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
