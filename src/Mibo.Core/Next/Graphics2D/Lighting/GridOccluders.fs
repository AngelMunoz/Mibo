namespace Mibo.Elmish.Next.Graphics2D.Lighting

open System
open System.Numerics
open Mibo.Layout
open Mibo.Elmish.Next.Graphics2D.Base

/// <summary>
/// Helpers for generating shadow-casting occluders from grid-based levels.
/// </summary>
module GridOccluders =

  /// <summary>
  /// Flags specifying which edges of a grid cell should generate shadow-casting occluders.
  /// </summary>
  [<Flags>]
  type Edge =
    | None = 0
    | Top = 1
    | Bottom = 2
    | Left = 4
    | Right = 8
    | All = 15

  /// <summary>
  /// Generates <see cref="Occluder2D"/> line segments for exposed edges of solid cells
  /// in a <see cref="CellGrid2D"/>, filtering to only the requested <paramref name="edges"/>.
  /// </summary>
  /// <param name="isSolid">Predicate that returns true for solid/obstacle cell contents.</param>
  /// <param name="edges">Which cell edges may produce occluders (e.g. <c>Edge.Bottom ||| Edge.Left ||| Edge.Right</c> for platformers, <c>Edge.All</c> for top-down).</param>
  /// <param name="grid">The grid to scan.</param>
  let fromCellGrid
    (isSolid: 'T -> bool)
    (edges: Edge)
    (grid: CellGrid2D<'T>)
    : Occluder2D[] =
    let occluders = ResizeArray<Occluder2D>()
    let cellW = grid.CellSize.X
    let cellH = grid.CellSize.Y

    let inline tryAddEdge
      (edgeFlag: Edge)
      (nx: int)
      (ny: int)
      (p1: Vector2)
      (p2: Vector2)
      =
      if edges &&& edgeFlag = edgeFlag then
        match CellGrid2D.get nx ny grid with
        | ValueNone -> occluders.Add({ P1 = p1; P2 = p2 })
        | ValueSome neighbor ->
          if not(isSolid neighbor) then
            occluders.Add({ P1 = p1; P2 = p2 })

    for y in 0 .. grid.Height - 1 do
      for x in 0 .. grid.Width - 1 do
        match CellGrid2D.get x y grid with
        | ValueNone -> ()
        | ValueSome tile ->
          if isSolid tile then
            let wx = grid.Origin.X + float32 x * cellW
            let wy = grid.Origin.Y + float32 y * cellH

            tryAddEdge
              Edge.Bottom
              x
              (y + 1)
              (Vector2(wx, wy + cellH))
              (Vector2(wx + cellW, wy + cellH))

            tryAddEdge
              Edge.Top
              x
              (y - 1)
              (Vector2(wx, wy))
              (Vector2(wx + cellW, wy))

            tryAddEdge
              Edge.Left
              (x - 1)
              y
              (Vector2(wx, wy))
              (Vector2(wx, wy + cellH))

            tryAddEdge
              Edge.Right
              (x + 1)
              y
              (Vector2(wx + cellW, wy))
              (Vector2(wx + cellW, wy + cellH))

    occluders.ToArray()
