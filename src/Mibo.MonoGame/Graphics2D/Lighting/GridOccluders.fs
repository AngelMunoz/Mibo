namespace Mibo.Elmish.Graphics2D.Lighting

open System
open System.Numerics
open Microsoft.Xna.Framework
open Mibo.Layout

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

    for y in 0 .. grid.Height - 1 do
      for x in 0 .. grid.Width - 1 do
        match CellGrid2D.get x y grid with
        | ValueNone -> ()
        | ValueSome tile ->
          if isSolid tile then
            let wx = grid.Origin.X + float32 x * cellW
            let wy = grid.Origin.Y + float32 y * cellH

            // Bottom edge
            if edges &&& Edge.Bottom = Edge.Bottom then
              match CellGrid2D.get x (y + 1) grid with
              | ValueNone ->
                occluders.Add(
                  {
                    P1 = Vector2(wx, wy + cellH)
                    P2 = Vector2(wx + cellW, wy + cellH)
                  }
                )
              | ValueSome neighbor ->
                if not(isSolid neighbor) then
                  occluders.Add(
                    {
                      P1 = Vector2(wx, wy + cellH)
                      P2 = Vector2(wx + cellW, wy + cellH)
                    }
                  )

            // Top edge
            if edges &&& Edge.Top = Edge.Top then
              match CellGrid2D.get x (y - 1) grid with
              | ValueNone ->
                occluders.Add(
                  {
                    P1 = Vector2(wx, wy)
                    P2 = Vector2(wx + cellW, wy)
                  }
                )
              | ValueSome neighbor ->
                if not(isSolid neighbor) then
                  occluders.Add(
                    {
                      P1 = Vector2(wx, wy)
                      P2 = Vector2(wx + cellW, wy)
                    }
                  )

            // Left edge
            if edges &&& Edge.Left = Edge.Left then
              match CellGrid2D.get (x - 1) y grid with
              | ValueNone ->
                occluders.Add(
                  {
                    P1 = Vector2(wx, wy)
                    P2 = Vector2(wx, wy + cellH)
                  }
                )
              | ValueSome neighbor ->
                if not(isSolid neighbor) then
                  occluders.Add(
                    {
                      P1 = Vector2(wx, wy)
                      P2 = Vector2(wx, wy + cellH)
                    }
                  )

            // Right edge
            if edges &&& Edge.Right = Edge.Right then
              match CellGrid2D.get (x + 1) y grid with
              | ValueNone ->
                occluders.Add(
                  {
                    P1 = Vector2(wx + cellW, wy)
                    P2 = Vector2(wx + cellW, wy + cellH)
                  }
                )
              | ValueSome neighbor ->
                if not(isSolid neighbor) then
                  occluders.Add(
                    {
                      P1 = Vector2(wx + cellW, wy)
                      P2 = Vector2(wx + cellW, wy + cellH)
                    }
                  )

    occluders.ToArray()
