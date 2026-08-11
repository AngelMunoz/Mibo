namespace Defli.Raylib

open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Layout
open Raylib_cs
open Defli.World
open Defli.World.Systems

// ─────────────────────────────────────────────────────────────
// MapView — the map passes (terrain/road/decorations/base mount),
// restored from the original Defli module Map, reading the frame's
// static MapModel. The map never changes, so the view is a pure
// function of (textures, frame, culling rect).
// ─────────────────────────────────────────────────────────────

module MapView =

  /// Builds the native raylib atlas rectangle from a TileInfo's raw
  /// coordinates. The sim carries only the backend-neutral X/Y/Width/
  /// Height; the native rectangle is constructed here, at the view edge.
  /// Shared by all Defli.Raylib views that draw atlas tiles.
  let inline tileRect(t: TileInfo) =
    Rectangle(float32 t.X, float32 t.Y, float32 t.Width, float32 t.Height)

  /// Picks the path tile frame for a cell from its path neighbors.
  /// Corners fall back to the vertical piece (placeholder — a nicer
  /// corner mapping can land later). No rotation is returned: the path
  /// frames are solid dirt, and raylib's origin handling would shift
  /// the draw position (see the view).
  let private pathFrame
    (grid: CellGrid2D<MapTile>)
    (x: int)
    (y: int)
    : TileInfo =
    let isPath x y =
      grid |> CellGrid2D.get x y |> ValueOption.exists(fun t -> t.IsPath)

    let n = isPath x (y - 1)
    let s = isPath x (y + 1)
    let e = isPath (x + 1) y
    let w = isPath (x - 1) y

    let count =
      (if n then 1 else 0)
      + (if s then 1 else 0)
      + (if e then 1 else 0)
      + if w then 1 else 0

    match count with
    | 1 ->
      // End piece — the frame's opening faces the road's continuation.
      if n then Tiles.pathEndUpDirt
      elif s then Tiles.pathEndUpDirt
      elif e then Tiles.pathEndLeftDirt
      else Tiles.pathEndLeftDirt
    | 2 when n && s -> Tiles.pathVerticalDirt
    | 2 when e && w -> Tiles.pathHorizontalDirt
    | _ -> Tiles.pathVerticalDirt // straight / corner placeholder

  /// Deterministic grass variety — no RNG needed for static content.
  let inline private grassVariant (x: int) (y: int) =
    Tiles.groundGrass[(x * 7 + y * 13) % 3]

  /// `visible` is the camera's world-space view rect (camera bounds
  /// from CameraView.cullingBounds — iterVisible culls to it).
  let view
    (ctx: GameContext)
    (model: MapModel)
    (visible: Rectangle)
    (buffer: RenderBuffer2D)
    =
    let assets = GameContext.getService<IAssets> ctx
    let tex = assets.Texture Tiles.SheetPath
    let size = float32 Tiles.TileSize

    let terrain = MapModel.terrain model
    let pathGrid = MapModel.pathGrid model
    let waypoints = MapModel.waypoints model
    let decorations = MapModel.decorations model

    let left = int visible.X
    let top = int visible.Y
    let right = left + int visible.Width
    let bottom = top + int visible.Height

    // Terrain (grass) — only the visible cells.
    CellGrid2D.iterVisible
      left
      top
      right
      bottom
      (fun x y _ ->
        let pos = CellGrid2D.getWorldPos x y terrain
        let frame = grassVariant x y

        buffer
          .sprite(
            SpriteState.create(
              tex,
              Rectangle(pos.X, pos.Y, size, size),
              tileRect frame
            )
            |> SpriteState.withLayer Layers.Ground
          )
          .drop())
      terrain

    // Road — the carved cells with path-aware frames. NO origin/
    // rotation here: raylib's DrawTexturePro subtracts the origin from
    // the draw position even at rotation 0 (translate(dest) → rotate →
    // translate(-origin)), so an origin of (32,32) would shift every
    // tile half a cell. The path frames are solid dirt — rotation is
    // invisible and omitted.
    CellGrid2D.iterVisible
      left
      top
      right
      bottom
      (fun x y _ ->
        let frame = pathFrame pathGrid x y
        let pos = CellGrid2D.getWorldPos x y pathGrid

        buffer
          .sprite(
            SpriteState.create(
              tex,
              Rectangle(pos.X, pos.Y, size, size),
              tileRect frame
            )
            |> SpriteState.withLayer Layers.Path
          )
          .drop())
      pathGrid

    // Decorations — props + dirt blends (drawn over the road edge so
    // the blends merge into it). Culled like the other layers.
    CellGrid2D.iterVisible
      left
      top
      right
      bottom
      (fun x y tile ->
        match tile.Decoration with
        | ValueSome frame ->
          let pos = CellGrid2D.getWorldPos x y decorations

          buffer
            .sprite(
              SpriteState.create(
                tex,
                Rectangle(pos.X, pos.Y, size, size),
                tileRect frame
              )
              |> SpriteState.withLayer Layers.Path
            )
            .drop()
        | ValueNone -> ())
      decorations

    // Base mount pad — from the waypoints layer (the base vertex).
    CellGrid2D.iterVisible
      left
      top
      right
      bottom
      (fun x y tile ->
        if tile.IsWaypoint && struct (x, y) = model.BaseCell then
          let pos = CellGrid2D.getWorldPos x y waypoints

          buffer
            .sprite(
              SpriteState.create(
                tex,
                Rectangle(pos.X, pos.Y, size, size),
                tileRect Tiles.turretMountEmpty
              )
              |> SpriteState.withLayer Layers.Path
            )
            .drop())
      waypoints
