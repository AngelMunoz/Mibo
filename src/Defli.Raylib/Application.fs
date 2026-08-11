namespace Defli.Raylib

open System
open System.Numerics
open AdaptiveSlop.Core
open Mibo
open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Input
open Raylib_cs
open Defli
open Defli.World
open Defli.World.Systems
open Defli.World.Systems.Camera

// ─────────────────────────────────────────────────────────────
// Application — the windowed composition root (the MVU shell's
// adaptive counterpart). Input is translated through subscriptions
// created at Init (the IInput service delivers per-frame deltas);
// the handlers write roots and call the world handlers directly — no
// Msg, no Cmd, no Sub. Services reach the world through
// AdaptiveContext.Context — no registration ceremony.
//
// Ordering per frame (host): input.Poll() → the subscriptions run
// (roots written, handlers called) → Step → shell phase (keyboard
// pan with the current dt) → Router.step → frame forced → draw.
// ─────────────────────────────────────────────────────────────

[<Struct>]
type GameAction =
  | StartNextWave
  | ToggleDiagnostics
  | SelectArrow
  | SelectFrost
  | SelectCannon
  | Restart
  | ResetCamera
  | PanLeft
  | PanRight
  | PanUp
  | PanDown

module Inputs =

  /// The key bindings — the original InputMap as a plain match (the
  /// mapper's ActionState tracking is replaced by the shell's PanDir
  /// accumulation).
  let actionOfKey(key: KeyCode) : GameAction voption =
    match key with
    | KeyCode.Space
    | KeyCode.Enter -> ValueSome GameAction.StartNextWave
    | KeyCode.F3 -> ValueSome GameAction.ToggleDiagnostics
    | KeyCode.D1 -> ValueSome GameAction.SelectArrow
    | KeyCode.D2 -> ValueSome GameAction.SelectFrost
    | KeyCode.D3 -> ValueSome GameAction.SelectCannon
    | KeyCode.R -> ValueSome GameAction.Restart
    | KeyCode.Home -> ValueSome GameAction.ResetCamera
    | KeyCode.A
    | KeyCode.Left -> ValueSome GameAction.PanLeft
    | KeyCode.D
    | KeyCode.Right -> ValueSome GameAction.PanRight
    | KeyCode.W
    | KeyCode.Up -> ValueSome GameAction.PanUp
    | KeyCode.S
    | KeyCode.Down -> ValueSome GameAction.PanDown
    | _ -> ValueNone

  /// The pan direction a pan action contributes. The pressed key moves
  /// the CAMERA (Up → the view pans north); Camera.Pan subtracts its
  /// input (drag semantics: the world follows the cursor), so keyboard
  /// deltas carry the OPPOSITE sign of the drag they mirror.
  let panStep(action: GameAction) : Vector2 =
    match action with
    | GameAction.PanLeft -> Vector2(1f, 0f)
    | GameAction.PanRight -> Vector2(-1f, 0f)
    | GameAction.PanUp -> Vector2(0f, 1f)
    | GameAction.PanDown -> Vector2(0f, -1f)
    | _ -> Vector2.Zero

module Application =

  /// Keyboard pan speed in screen pixels per second (the Camera
  /// subsystem converts by its zoom — panning feels constant on
  /// screen).
  let panSpeed = 500f

  /// Screen → world → the CONTAINING cell (the sim's cellAt — the
  /// floor-based pick, not the nearest-center one).
  let private hoverCell
    (world: World)
    (viewport: Vector2)
    (screenPos: Vector2)
    : struct (int * int) voption =
    let worldPos =
      CameraView.screenToWorld world.Camera.State viewport screenPos

    Defli.Application.cellAt worldPos (MapModel.terrain world.Map)

  let private handleKeyboard
    (ctx: AdaptiveContext)
    (cell: WorldCell)
    (shell: Shell)
    (delta: KeyboardDelta)
    : unit =
    for code in delta.Pressed do
      match Inputs.actionOfKey code with
      | ValueSome action ->
        let world = cell.Value

        match action with
        | GameAction.StartNextWave -> Router.startNextWave world
        | GameAction.ToggleDiagnostics ->
          shell.Diag.Visible <- not shell.Diag.Visible
        | GameAction.SelectArrow -> Router.selectTower world TowerDefs.arrow
        | GameAction.SelectFrost -> Router.selectTower world TowerDefs.frost
        | GameAction.SelectCannon -> Router.selectTower world TowerDefs.cannon
        | GameAction.ResetCamera ->
          Camera.Camera.update CameraMsg.Reset world.Camera
        | GameAction.Restart ->
          // Only from game over (misclicks must not wipe a run): swap
          // the world; the host re-runs Init after Step (fresh graph,
          // fresh subscriptions, fresh clock).
          if AVal.getValue world.Economy.GameOver then
            cell.Value <- World.init world.Config
            shell.PanDir <- Vector2.Zero
            shell.MiddleDown <- false
            ctx.RestartRequested.Set(true)
        | GameAction.PanLeft
        | GameAction.PanRight
        | GameAction.PanUp
        | GameAction.PanDown ->
          shell.PanDir <- shell.PanDir + Inputs.panStep action
      | ValueNone -> ()

    for code in delta.Released do
      match Inputs.actionOfKey code with
      | ValueSome action -> shell.PanDir <- shell.PanDir - Inputs.panStep action
      | ValueNone -> ()

  let private handleMouse
    (ctx: AdaptiveContext)
    (cell: WorldCell)
    (shell: Shell)
    (delta: MouseDelta)
    : unit =
    let world = cell.Value
    shell.MousePos <- delta.Position

    if delta.Buttons.Pressed |> Array.contains MouseButtonCode.Middle then
      shell.MiddleDown <- true

    if delta.Buttons.Released |> Array.contains MouseButtonCode.Middle then
      shell.MiddleDown <- false

    let viewport = Vector2(float32 ctx.WindowWidth, float32 ctx.WindowHeight)

    // Hover cell — the CVal the world projections join on.
    world.HoverCell |> CVal.set(hoverCell world viewport delta.Position)

    // Wheel zoom / middle-drag pan: direct camera messages (no dt).
    if delta.ScrollDelta <> 0f then
      // Multiplicative steps toward the camera target.
      let factor = float32(1.1 ** float delta.ScrollDelta)
      Camera.Camera.update (CameraMsg.ZoomBy factor) world.Camera
    elif shell.MiddleDown && delta.PositionDelta <> Vector2.Zero then
      // Middle-drag pan: world moves opposite the drag (screen px).
      Camera.Camera.update (CameraMsg.Pan delta.PositionDelta) world.Camera

    // Clicks → place / upgrade (the router validates everything).
    if delta.Buttons.Pressed |> Array.contains MouseButtonCode.Left then
      hoverCell world viewport delta.Position
      |> ValueOption.iter(fun c -> Router.placeTower world c |> ignore)

    if delta.Buttons.Pressed |> Array.contains MouseButtonCode.Right then
      hoverCell world viewport delta.Position
      |> ValueOption.iter(fun c -> Router.upgradeTower world c |> ignore)

  /// The windowed program: boot (texture filtering), input wiring and
  /// the per-frame shell phase over the same sim (Router.step).
  let windowedProgram
    (cell: WorldCell)
    (shell: Shell)
    : AdaptiveProgram<Frame.RenderFrame> =
    AdaptiveProgram.mkProgram
      (fun ctx ->
        // The raylib loader forces TRILINEAR filtering on every texture
        // (docs/assets.md): a gutterless spritesheet sampled bilinearly
        // at tile borders bleeds adjacent (black) texels in — the seam
        // lines between tiles. Point filtering stops it. Applied ONCE at
        // boot (mutates the cached texture's sampler — not per frame).
        let assets = ctx.Context |> GameContext.getService<IAssets>

        assets.Texture Tiles.SheetPath
        |> Texture.filter TextureFilter.Point
        |> ignore

        // Input → world. The subscriptions run on the game thread (the
        // host polls right before Step), so the graph owner-thread rule
        // holds. Disposed with the runner — or re-created on restart.
        let input = ctx.Context |> GameContext.getService<IInput>
        let d1 = input.KeyboardDelta.Subscribe(handleKeyboard ctx cell shell)
        let d2 = input.MouseDelta.Subscribe(handleMouse ctx cell shell)

        AdaptiveInit.ofFrameBuilder(Frame.buildFrame cell.Value)
        |> AdaptiveInit.withDisposables [ d1; d2 ])
      (fun _ctx gameTime ->
        let world = cell.Value

        Diagnostics.update shell.Diag

        // Keyboard pan with the current dt (the shell accumulated the
        // held keys during the input poll).
        if shell.PanDir <> Vector2.Zero then
          let dt = float32 gameTime.ElapsedGameTime.TotalSeconds

          Camera.Camera.update
            (CameraMsg.Pan(shell.PanDir * panSpeed * dt))
            world.Camera

        Router.step world gameTime)
