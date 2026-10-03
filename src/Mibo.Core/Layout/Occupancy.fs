namespace Mibo.Layout

/// How many cells one drawn instance covers.
[<Struct>]
type InstanceSpan =
  /// A rectangle of offset cells on a square grid, growing toward +X and +Z
  /// from the anchor cell.
  | Span of across: int * deep: int
  /// A disc of hex steps on a hex grid, centred on the anchor cell. The
  /// bounding rectangle is `(x - r, y - r, 2r + 1, 2r + 1)` in offset space.
  | Radius of r: int
  /// The identity: the anchor cell only. `Span(1, 1)` and `Radius 0` mean the
  /// same thing, so a computed span needs no special case.
  | One

/// The occupancy a build derives from a painted grid: the cell of every drawn
/// instance, the rectangle each one covers, and which instance owns each cell.
/// A plain cell owns itself; a spanning cell owns its whole rectangle.
///
/// Every cell that carries a value also carries an owner, so a query no longer
/// reads `ValueNone` for a cell that an instance covers from elsewhere. Build
/// it once with `Occupancy.scan` and read it per frame.
type Occupancy = {
  Width: int
  Height: int
  /// Anchor index -> the anchor's cell.
  Cells: CellPoint[]
  /// Anchor index -> the anchor's bounding rectangle, in offset space.
  Rects: CellRect[]
  /// Cell index (`x + y * Width`) -> anchor index + 1, or 0 where nothing
  /// covers the cell.
  Owner: int[]
  /// How many covered cells held a value before a span claimed them, so they
  /// draw as part of the anchor instead of on their own.
  Claimed: int
}

module Occupancy =
  let inline private toIndex x y width = x + y * width

  /// The identity spans: `One`, `Span(1, 1)`, and `Radius 0` all cover one
  /// cell. A word that declares one of them places a single cell, and needs no
  /// expansion or geometry check.
  let isIdentity(span: InstanceSpan) =
    match span with
    | One -> true
    | Span(across, deep) -> across = 1 && deep = 1
    | Radius r -> r = 0

  /// How a span reads in a message: `6x4`, `radius 2`, or `one cell`.
  let describe(span: InstanceSpan) =
    match span with
    | Span(across, deep) -> $"{across}x{deep}"
    | Radius r -> $"radius {r}"
    | One -> "one cell"

  let inline private rectFor (at: CellPoint) (span: InstanceSpan) =
    match span with
    | Span(across, deep) -> {
        X = at.X
        Y = at.Y
        W = across
        H = deep
      }
    | Radius r -> {
        X = at.X - r
        Y = at.Y - r
        W = 2 * r + 1
        H = 2 * r + 1
      }
    | One -> { X = at.X; Y = at.Y; W = 1; H = 1 }

  /// A span must cover at least one cell, and its shape must match the grid
  /// geometry. The identity is legal on either geometry.
  let private validateShape
    (geometry: CellGeometry)
    (at: CellPoint)
    (span: InstanceSpan)
    : Result<unit, string> =
    match span, geometry with
    | Span _, CellGeometry.Hex _ ->
      Error $"Span spans need a square grid; ({at.X},{at.Y}) is hex"
    | Radius _, CellGeometry.Square ->
      Error $"Radius spans need a hex grid; ({at.X},{at.Y}) is square"
    | Span(across, deep), _ when across < 1 || deep < 1 ->
      Error $"the span at ({at.X},{at.Y}) spans nothing"
    | Radius r, _ when r < 1 ->
      Error $"the span at ({at.X},{at.Y}) spans nothing"
    | _ -> Ok()

  /// Walks the cells a span covers: the rectangle for `Span` and `One`, the
  /// disc for `Radius`. The disc walk clips to the grid, so the caller bounds
  /// the rectangle before it expands anything.
  let inline private forEachCovered
    (grid: CellGrid2D<'T>)
    (rect: CellRect)
    (at: CellPoint)
    (span: InstanceSpan)
    ([<InlineIfLambda>] action: int -> int -> unit)
    : unit =
    match span with
    | Radius r ->
      Hex2DSpatial.forEachInRange
        at.X
        at.Y
        r
        grid.Width
        grid.Height
        (CellGrid2D.hexOrientation grid)
        action
    | Span _
    | One ->
      for y in rect.Y .. rect.Y + rect.H - 1 do
        for x in rect.X .. rect.X + rect.W - 1 do
          action x y

  /// Expands every populated cell through `spanOf`, validates the result, and
  /// answers which instance owns each cell. A span claims the cells it covers:
  /// a plain cell inside a span's rectangle is covered rather than reported as
  /// a second instance.
  ///
  /// Fails with a cell-named reason when a span leaves the grid, when two spans
  /// overlap, when a span covers nothing, or when the span shape and the grid
  /// geometry disagree.
  let scan
    (spanOf: 'T -> InstanceSpan)
    (grid: CellGrid2D<'T>)
    : Result<Occupancy, string> =
    let width = grid.Width
    let height = grid.Height
    let owner = Array.zeroCreate(width * height)
    let cells = ResizeArray<CellPoint>()
    let rects = ResizeArray<CellRect>()
    let mutable claimed = 0
    let mutable failure = ValueNone

    let claim (at: CellPoint) (rect: CellRect) (span: InstanceSpan) : unit =
      let index = cells.Count
      cells.Add at
      rects.Add rect

      forEachCovered grid rect at span (fun x y ->
        if failure.IsNone then
          let slot = toIndex x y width

          match owner.[slot] with
          | 0 ->
            owner.[slot] <- index + 1

            let covered = x <> at.X || y <> at.Y

            if covered && (CellGrid2D.get x y grid).IsSome then
              claimed <- claimed + 1
          | taken ->
            let other = cells.[taken - 1]

            failure <-
              ValueSome
                $"the span at ({at.X},{at.Y}) overlaps the span at ({other.X},{other.Y})")

    let addSpan (x: int) (y: int) (span: InstanceSpan) : unit =
      let at = { X = x; Y = y }

      let placed =
        validateShape grid.Geometry at span
        |> Result.bind(fun () ->
          let rect = rectFor at span

          if
            rect.X < 0
            || rect.Y < 0
            || rect.X + rect.W > width
            || rect.Y + rect.H > height
          then
            Error $"the span at ({x},{y}) covers past the grid edge"
          else
            claim at rect span
            Ok())

      match placed with
      | Ok() -> ()
      | Error reason -> failure <- ValueSome reason

    // the spanning cells first: they claim their rectangles before the plain
    // cells that no span covers become anchors of their own
    CellGrid2D.iter
      (fun x y content ->
        if failure.IsNone then
          let span = spanOf content

          if not(isIdentity span) then
            addSpan x y span)
      grid

    if failure.IsNone then
      CellGrid2D.iter
        (fun x y _ ->
          let slot = toIndex x y width

          if owner.[slot] = 0 then
            let at = { X = x; Y = y }
            owner.[slot] <- cells.Count + 1
            cells.Add at
            rects.Add { X = x; Y = y; W = 1; H = 1 })
        grid

    match failure with
    | ValueSome reason -> Error reason
    | ValueNone ->
      Ok {
        Width = width
        Height = height
        Cells = cells.ToArray()
        Rects = rects.ToArray()
        Owner = owner
        Claimed = claimed
      }

  /// An occupancy where every populated cell owns itself, built without
  /// consulting a span. For a map whose words never span.
  let identity(grid: CellGrid2D<'T>) : Occupancy =
    let width = grid.Width
    let height = grid.Height
    let owner = Array.zeroCreate(width * height)
    let cells = ResizeArray<CellPoint>()
    let rects = ResizeArray<CellRect>()

    CellGrid2D.iter
      (fun x y _ ->
        let at = { X = x; Y = y }
        let index = cells.Count
        cells.Add at
        rects.Add { X = x; Y = y; W = 1; H = 1 }
        owner.[toIndex x y width] <- index + 1)
      grid

    {
      Width = width
      Height = height
      Cells = cells.ToArray()
      Rects = rects.ToArray()
      Owner = owner
      Claimed = 0
    }

  /// The anchor that owns a cell: the cell itself when it is populated and
  /// plain, the covering anchor when a span covers it, `ValueNone` when the
  /// cell is empty or out of range. This is the query every consumer reads.
  let owner (x: int) (y: int) (occupancy: Occupancy) : CellPoint voption =
    if x < 0 || y < 0 || x >= occupancy.Width || y >= occupancy.Height then
      ValueNone
    else
      match occupancy.Owner.[toIndex x y occupancy.Width] with
      | 0 -> ValueNone
      | slot -> ValueSome occupancy.Cells.[slot - 1]

  /// The bounding rectangle of the anchor that owns a cell, in offset space.
  let rectOf (at: CellPoint) (occupancy: Occupancy) : CellRect voption =
    if
      at.X < 0
      || at.Y < 0
      || at.X >= occupancy.Width
      || at.Y >= occupancy.Height
    then
      ValueNone
    else
      match occupancy.Owner.[toIndex at.X at.Y occupancy.Width] with
      | 0 -> ValueNone
      | slot -> ValueSome occupancy.Rects.[slot - 1]

  /// Calls `action` with the cell and the rectangle of every anchor whose
  /// rectangle intersects the cell-space box. `left`, `top`, `right`, and
  /// `bottom` are inclusive cell coordinates, so an anchor that covers a
  /// window cell from outside it is still reported.
  ///
  /// The walk reads every anchor, so prefer it for maps up to a few thousand
  /// instances and hoist it out of a hot loop otherwise.
  let inline iterInWindow
    (left: int)
    (top: int)
    (right: int)
    (bottom: int)
    ([<InlineIfLambda>] action: CellPoint -> CellRect -> unit)
    (occupancy: Occupancy)
    : unit =
    for i in 0 .. occupancy.Rects.Length - 1 do
      let rect = occupancy.Rects.[i]

      if
        rect.X <= right
        && rect.X + rect.W - 1 >= left
        && rect.Y <= bottom
        && rect.Y + rect.H - 1 >= top
      then
        action occupancy.Cells.[i] rect

/// How a stack of layers stands in height. A layer that draws above another
/// needs the height the layers below it reach at each cell, so its columns
/// stand on them instead of replacing them.
module Stack =
  /// For each layer, the height the layers below it reach at each cell, flat at
  /// `x + y * Width`. Layer 0 stands on the plane, so its array is all zeroes.
  ///
  /// `occupancies` and `grids` describe the same layers, bottom first, and a
  /// spanning anchor contributes its height over its whole rectangle. Heights
  /// add, because every layer is a slab of its own.
  ///
  /// `let feet = Stack.feet occupancies grids heightOf` then
  /// `{ cell with Lift = feet[i].[x + y * width] }` per layer.
  let feet
    (occupancies: Occupancy[])
    (grids: CellGrid2D<'T>[])
    (heightOf: 'T -> float32)
    : float32[][] =
    if occupancies.Length <> grids.Length then
      invalidArg
        "grids"
        $"Stack.feet takes one occupancy for each grid: {occupancies.Length} occupancies, {grids.Length} grids"

    if grids.Length = 0 then
      Array.empty
    else
      let width = grids.[0].Width
      let height = grids.[0].Height

      for grid in grids do
        if grid.Width <> width || grid.Height <> height then
          invalidArg
            "grids"
            $"every layer spans the same map: the first grid is {width}x{height}, another is {grid.Width}x{grid.Height}"

      // the top of the stack so far, per cell, in cells above the plane
      let tops = Array.zeroCreate(width * height)

      let advance (occupancy: Occupancy) (grid: CellGrid2D<'T>) =
        for i in 0 .. occupancy.Rects.Length - 1 do
          let at = occupancy.Cells.[i]
          let rect = occupancy.Rects.[i]

          let lift =
            CellGrid2D.get at.X at.Y grid
            |> ValueOption.map heightOf
            |> ValueOption.defaultValue 0f

          for y in rect.Y .. rect.Y + rect.H - 1 do
            for x in rect.X .. rect.X + rect.W - 1 do
              let slot = x + y * width
              tops.[slot] <- tops.[slot] + lift

      Array.init grids.Length (fun i ->
        // the height under this layer, before this layer adds its own
        let level = Array.copy tops
        advance occupancies.[i] grids.[i]
        level)
