namespace Defli.Raylib

open System.Numerics
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Layout
open Raylib_cs
open Defli
open Defli.World
open Defli.World.Frame
open Defli.World.Systems
open Defli.World.Systems.Camera

// ─────────────────────────────────────────────────────────────
// WorldView — the world pass and the HUD pass, reading ONLY the
// forced RenderFrame (the draw contract: no graph access at draw
// time). The shell supplies the FrameDiag object; the VfxView owns
// its conversion buffer.
// ─────────────────────────────────────────────────────────────

module WorldView =

  let inline drawOutline
    size
    (color: Mibo.Color)
    (frame: RenderFrame)
    (buffer: RenderBuffer2D)
    =
    frame.HoverCell
    |> ValueOption.iter(fun c ->
      let struct (hx, hy) = c
      let p = CellGrid2D.getWorldPos hx hy (MapModel.terrain frame.Map)

      buffer
        .rectOutline(
          p.X,
          p.Y,
          size,
          size,
          color,
          thickness = 2f,
          layer = Layers.Effects
        )
        .drop())

  let private hoverOverlays (frame: RenderFrame) (buffer: RenderBuffer2D) =
    let size = float32 Tiles.TileSize

    // Placement preview: the hovered cell's build status.

    match frame.PlacementPreview with
    | PlacementStatus.Hidden -> ()
    | PlacementStatus.Blocked -> drawOutline size Mibo.Color.Red frame buffer
    | PlacementStatus.Affordable ->
      drawOutline size Mibo.Color.Green frame buffer
    | PlacementStatus.TooExpensive ->
      drawOutline size (Mibo.Color.rgb 255uy 210uy 0uy) frame buffer

    // Range ring: hovering an own tower shows its range circle.
    frame.HoverCell
    |> ValueOption.iter2
      (fun def c ->
        let center =
          Cells.center
            c
            (Vector2(float32 Tiles.TileSize, float32 Tiles.TileSize))

        buffer
          .circleOutline(
            center,
            float32 def.Range * size,
            Mibo.Color.Blue,
            layer = Layers.Effects
          )
          .drop())
      frame.RangeRing

  /// The camera'd world pass (its own renderer — clears black).
  let worldView
    (shell: Shell)
    (vfx: VfxView)
    (ctx: GameContext)
    (frame: RenderFrame)
    (buffer: RenderBuffer2D)
    =
    Diagnostics.drawn (Diagnostics.tickStart()) shell.Diag
    let viewport = Vector2(float32 ctx.WindowWidth, float32 ctx.WindowHeight)
    let size = Vector2(float32 Tiles.TileSize, float32 Tiles.TileSize)

    // Camera block: the edge builds + clamps + shakes the native
    // camera from the frame's neutral snapshot; everything world-space
    // renders inside; the HUD renderer (separate noClear pass) owns
    // screen space.
    CameraView.beginFrame frame.Camera viewport buffer

    let visible = CameraView.cullingBounds frame.Camera viewport

    MapView.view ctx frame.Map visible buffer
    TowersView.view ctx frame.TowerStatics frame.TowerLevels size buffer
    EnemiesView.view ctx frame.Alive frame.Defs frame.Map.Path buffer
    ProjectilesView.view ctx frame.Projectiles buffer
    vfx.View ctx frame.Vfx buffer
    hoverOverlays frame buffer

    buffer.endCamera(layer = Layers.Effects).drop()

  /// Screen-space HUD pass (own renderer, noClear): reads the frame
  /// only.
  let hudView
    (shell: Shell)
    (ctx: GameContext)
    (frame: RenderFrame)
    (buffer: RenderBuffer2D)
    =
    let font = Raylib.GetFontDefault()

    buffer
      .text(
        font,
        $"Gold: %d{frame.Gold}   Lives: %d{frame.Lives}   %s{frame.Banner}   Tower: %s{frame.SelectedTower.Name} (1/2/3)",
        Vector2(12f, 10f),
        22f,
        layer = Layers.Hud
      )
      .drop()

    buffer
      .text(
        font,
        "WASD/arrows or middle-drag: pan   wheel: zoom   Home: reset   right-click: upgrade",
        Vector2(12f, float32 ctx.WindowHeight - 30f),
        16f,
        layer = Layers.Hud
      )
      .drop()

    if frame.GameOver then
      buffer
        .text(
          font,
          "GAME OVER — press R to restart",
          Vector2(430f, 360f),
          40f,
          layer = Layers.Hud
        )
        .drop()

    if shell.Diag.Visible then
      buffer.frameDiagnostics(font, shell.Diag, Vector2(12f, 40f)).drop()
      buffer.worldDiagnostics(font, frame.Diag, Vector2(12f, 64f)).drop()
