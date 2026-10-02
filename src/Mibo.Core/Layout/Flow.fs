namespace Mibo.Layout

open System
open System.Collections.Generic
open Mibo.Elmish

/// Resolved rectangle of a placed element, in grid cells.
[<Struct>]
type CellRect = { X: int; Y: int; W: int; H: int }

/// A cell coordinate: x across, y down. The named pair the paint styles
/// take where bare x/y ints could be transposed.
[<Struct>]
type CellPoint = { X: int; Y: int }

/// Landmarks collected while a level lays out: named element rectangles,
/// tag groups of rectangles, and per-cell tag bit grids for fast walking
/// queries. Tags are opaque strings; the engine stores and queries them,
/// the game defines what they mean.
type Landmarks = {
  Width: int
  Height: int
  /// Resolved rectangle of every `Stamp.named` element (unique; a duplicate
  /// name throws).
  Named: Dictionary<string, CellRect>
  /// Rectangles grouped by tag, one entry per `Stamp.tagged` element,
  /// most recent first.
  Tagged: Dictionary<string, CellRect list>
  /// One flat bit grid per tag (`x + y * Width`); filled by tagged areas and
  /// by `Flow.scanTiles`.
  Cells: Dictionary<string, bool[]>
}

module Landmarks =
  /// Marks every cell of the grid that carries one of the tags returned by
  /// `extract` for its content. This derives the per-cell bit grids from the
  /// painted tiles, so cell-true regions (noise-carved woods, scattered
  /// chests) answer walking queries without the engine knowing the tags.
  let scanTiles
    (extract: int -> int -> 'T -> string seq)
    (grid: CellGrid2D<'T>)
    (landmarks: Landmarks)
    : Landmarks =
    for y in 0 .. landmarks.Height - 1 do
      for x in 0 .. landmarks.Width - 1 do
        CellGrid2D.get x y grid
        |> ValueOption.iter(fun tile ->
          for tag in extract x y tile do
            let cells =
              Dictionary.tryGetValue tag landmarks.Cells
              |> ValueOption.defaultWith(fun () ->
                Array.create (landmarks.Width * landmarks.Height) false)

            cells.[x + y * landmarks.Width] <- true
            landmarks.Cells.[tag] <- cells)

    landmarks

/// Paint signature of a stamp. The landmark registry is filled while the
/// level lays out; ad-hoc paints pass ValueNone.
type StampPaint<'T> = GridSection2D<'T> -> Landmarks voption -> unit

/// A box style: paints the whole area of the element it is applied to,
/// relative to that area. Box styles never take coordinates; they combine in
/// `Stamp.box` and `Flow.canvas` (`Flow.fill`, `Flow.border`, `Flow.noise`, ...).
type BoxStyle<'T> = GridSection2D<'T> -> unit

/// Alignment of children relative to the container. Used on the cross axis
/// (`FlowOpts.Align`) and the main axis (`FlowOpts.Justify`). `Stretch`
/// behaves as `Start` when used as `Justify`.
[<Struct>]
type Align =
  | Start
  | Center
  | End
  | Stretch

/// A single track of a `Flow.grid` template. `Fixed` tracks take literal
/// cells; `Auto` tracks size to the largest footprint of their span-1
/// places at construction (a context-sized place reports no footprint, so
/// it contributes nothing), and a place that spans several tracks shares
/// its footprint over the `Auto` tracks it covers; `Weight` tracks (`fr`)
/// share the space left after `Fixed`, `Auto` and `Percent` tracks;
/// `Percent` is a fraction of the assigned container length.
[<Struct>]
type Track =
  | Fixed of length: int
  | Auto
  | Weight of share: float32
  | Percent of fraction: float32

/// Edge anchoring flags for `Flow.dock`. Combine with `|||`, for example
/// `Dock.Top ||| Dock.CenterX`. When opposite edges are set the top/left edge
/// wins; `Stretch` wins over the edge flags of its axis. A zero footprint
/// dimension stretches on its axis, with or without the `Stretch` flag.
[<Flags>]
type Dock =
  | None = 0
  | Top = 1
  | Bottom = 2
  | Left = 4
  | Right = 8
  | CenterX = 16
  | CenterY = 32
  | StretchX = 64
  | StretchY = 128

/// A sized, composable element for the Flow layout DSL. `W`/`H` are the
/// element footprint in cells; `Paint` draws into a section of that
/// footprint. `Tags` are opaque strings recorded into the landmark registry
/// with the element's resolved rectangle. Holds a function value, so it
/// never uses structural equality.
[<Struct; NoEquality; NoComparison>]
type Stamp<'T> = {
  W: int
  H: int
  Expand: int
  Name: string voption
  Tags: string list
  Paint: StampPaint<'T>
}

/// Options for `Flow.row` and `Flow.column` containers.
[<Struct>]
type FlowOpts = {
  /// Empty cells inserted between children on the main axis.
  Gap: int
  /// Cross-axis alignment of children inside the container.
  Align: Align
  /// Main-axis distribution of the children of each line.
  Justify: Align
  /// Wrap children onto new lines when they exceed the container length.
  Wrap: bool
} with

  /// Default options: no gap, start alignment, no wrap.
  static member Default = {
    Gap = 0
    Align = Start
    Justify = Start
    Wrap = false
  }

/// Where a `Flow.grid` place mounts: a named template area, or an explicit
/// cell slot — the column and row track indices it starts at, with spans.
/// Slots may overlap; paint order is `Places` order, later places on top.
[<Struct>]
type Place =
  | Area of name: string
  | Slot of col: int * row: int * colspan: int * rowspan: int

/// Options and content for `Flow.grid`: track sizes, the area template, the
/// gap between tracks, and the stamps placed into named areas or explicit
/// slots. Every area name in `Places` must match a template area; every
/// slot must fit inside the declared tracks.
type GridOpts<'T> = {
  /// Column tracks; `Fixed` and `Auto` tracks count toward the intrinsic
  /// footprint.
  Cols: Track[]
  /// Row tracks; `Fixed` and `Auto` tracks count toward the intrinsic
  /// footprint.
  Rows: Track[]
  /// Empty cells between tracks.
  Gap: int
  /// Grid-area template strings (`"main main side"` spans columns, `.` is
  /// an empty cell).
  Areas: string[]
  /// Stamps placed into the grid, by named area or by explicit slot.
  Places: struct (Place * Stamp<'T>)[]
}

/// A midpoint circle spec: the center, the radius, and whether the
/// interior spans.
[<Struct>]
type CircleSpec = {
  Center: CellPoint
  Radius: int
  Filled: bool
}

/// A seeded scatter spec: how many cells to pick, and the generator seed.
[<Struct>]
type ScatterSpec = { Count: int; Seed: int }

/// A scattered-line spec: the segment to scatter along, how many cells to
/// pick, and the generator seed.
[<Struct>]
type ScatterLineSpec = {
  From: CellPoint
  To: CellPoint
  Count: int
  Seed: int
}

/// A probabilistic rewrite spec: the chance any matching cell changes, and
/// the generator seed.
[<Struct>]
type WeatherSpec = { Probability: float32; Seed: int }

/// Per-side inset amounts, in cells.
[<Struct>]
type InsetSpec = {
  Left: int
  Top: int
  Right: int
  Bottom: int
} with

  /// No inset on any side.
  static member Zero = {
    Left = 0
    Top = 0
    Right = 0
    Bottom = 0
  }

/// A dock spec: where the element anchors (`Anchor`, combining `Dock`
/// values with `|||`, for example `Dock.Top ||| Dock.CenterX`), the per-side
/// inset from the container edges in cells, and the element to dock.
[<Struct>]
type DockSpec<'T> = {
  Anchor: Dock
  Inset: InsetSpec
  Stamp: Stamp<'T>
}

module internal FlowImpl =

  let inline rectOf(s: GridSection2D<'T>) : CellRect = {
    X = s.OffsetX
    Y = s.OffsetY
    W = s.Width
    H = s.Height
  }

  /// Resolves the docked rectangle for `footprint` inside `bounds` with the
  /// given anchor flags and per-side inset. A zero footprint dimension
  /// stretches over its axis (from the left/top inset to the opposite
  /// inset); `StretchX`/`StretchY` also stretch a nonzero footprint.
  let dockRect
    (flags: Dock)
    (inset: InsetSpec)
    (bounds: CellRect)
    (footprint: CellRect)
    : CellRect =
    let l = max 0 inset.Left
    let t = max 0 inset.Top
    let r = max 0 inset.Right
    let b = max 0 inset.Bottom

    let stretchX = (flags &&& Dock.StretchX <> Dock.None) || footprint.W = 0

    let stretchY = (flags &&& Dock.StretchY <> Dock.None) || footprint.H = 0

    let x =
      if stretchX then
        l
      elif flags &&& Dock.Right <> Dock.None then
        bounds.W - footprint.W - r
      elif flags &&& Dock.CenterX <> Dock.None then
        (bounds.W - footprint.W) / 2
      else
        l

    let y =
      if stretchY then
        t
      elif flags &&& Dock.Bottom <> Dock.None then
        bounds.H - footprint.H - b
      elif flags &&& Dock.CenterY <> Dock.None then
        (bounds.H - footprint.H) / 2
      else
        t

    let w = if stretchX then bounds.W - l - r else footprint.W
    let h = if stretchY then bounds.H - t - b else footprint.H

    {
      X = bounds.X + x
      Y = bounds.Y + y
      W = max 0 w
      H = max 0 h
    }

  let inline private overlaps (a: CellRect) (b: CellRect) : bool =
    a.X < b.X + b.W && a.Y < b.Y + b.H && a.X + a.W > b.X && a.Y + a.H > b.Y

  /// Records one placed element under its name (unique; a duplicate name
  /// throws) and under each of its tags (additive), rasterizing the
  /// rectangle into the per-tag cell bit grids.
  let recordLandmarks
    (registry: Landmarks voption)
    (stamp: Stamp<'T>)
    (r: CellRect)
    : unit =
    registry
    |> ValueOption.iter(fun landmarks ->
      stamp.Name
      |> ValueOption.iter(fun n ->
        if landmarks.Named.ContainsKey n then
          invalidArg
            "stamp"
            ("duplicate element name '" + n + "': names must be unique")

        landmarks.Named.[n] <- r)

      if not stamp.Tags.IsEmpty then
        let x1 = max 0 r.X
        let y1 = max 0 r.Y
        let x2 = min (r.X + r.W) (landmarks.Width)
        let y2 = min (r.Y + r.H) (landmarks.Height)

        for tag in stamp.Tags do
          let existing =
            Dictionary.tryGetValue tag landmarks.Tagged
            |> ValueOption.defaultValue []

          landmarks.Tagged.[tag] <- r :: existing

          let cells =
            Dictionary.tryGetValue tag landmarks.Cells
            |> ValueOption.defaultWith(fun () ->
              Array.create (landmarks.Width * landmarks.Height) false)

          for y in y1 .. y2 - 1 do
            for x in x1 .. x2 - 1 do
              cells.[x + y * landmarks.Width] <- true

          landmarks.Cells.[tag] <- cells)

  /// Paints a child into `r`, skipping it when it lies fully outside the
  /// parent. The child section is the intersection of `r` with the parent,
  /// so a stamp whose origin sits before the parent's edge clips at that
  /// edge instead of writing outside the grid; ops that respect section
  /// bounds (fill/border/checker/...) clamp at the parent's right/bottom
  /// edge. Landmarks record the same intersection, so reported rects never
  /// describe cells that nothing painted.
  let paintChild
    (parent: GridSection2D<'T>)
    (r: CellRect)
    (registry: Landmarks voption)
    (stamp: Stamp<'T>)
    : unit =
    if r.W > 0 && r.H > 0 && overlaps r (rectOf parent) then
      let bounds = rectOf parent
      let x1 = max r.X bounds.X
      let y1 = max r.Y bounds.Y
      let x2 = min (r.X + r.W) (bounds.X + bounds.W)
      let y2 = min (r.Y + r.H) (bounds.Y + bounds.H)

      if x2 > x1 && y2 > y1 then
        recordLandmarks registry stamp {
          X = x1
          Y = y1
          W = x2 - x1
          H = y2 - y1
        }

        let child: GridSection2D<'T> = {
          BackingGrid = parent.BackingGrid
          OffsetX = x1
          OffsetY = y1
          Width = x2 - x1
          Height = y2 - y1
        }

        stamp.Paint child registry

  /// Rejects an element that asks for main-axis expansion in a container
  /// that ignores it: the request fails at construction instead of
  /// silently doing nothing.
  let checkNoExpand
    (container: string)
    (arg: string)
    (stamp: Stamp<'T>)
    : unit =
    if stamp.Expand > 0 then
      invalidArg
        arg
        (container
         + " ignores Expand; expanded elements only work as row/column children")

  let checkNoExpandAll
    (container: string)
    (arg: string)
    (stamps: Stamp<'T> seq)
    : unit =
    for c in stamps do
      checkNoExpand container arg c

  /// A deterministic permutation of `0..n-1`: xorshift seeded from `seed`,
  /// Fisher-Yates over the identity. No BCL RNG, so the sequence cannot
  /// drift across runtimes. A zero state would freeze the xorshift, so
  /// the one seed that mixes to zero re-mixes once.
  let permutation (seed: int) (n: int) : int[] =
    let mutable s = uint32 seed * 2654435761u + 2891336453u

    if s = 0u then
      s <- s ^^^ 0x9E3779B9u

    let next() =
      s <- s ^^^ (s <<< 13)
      s <- s ^^^ (s >>> 17)
      s <- s ^^^ (s <<< 5)
      s

    let order = Array.init n id

    for i in n - 1 .. -1 .. 1 do
      let j = int(next() % uint32(i + 1))
      let tmp = order.[i]
      order.[i] <- order.[j]
      order.[j] <- tmp

    order

  /// Places sized children at seeded, non-overlapping origins of `bounds`:
  /// candidate origins are the cells of `bounds` visited in the order of a
  /// seeded permutation, children place in array order, and each takes the
  /// first origin where its size fits without overlapping an earlier one.
  /// A child with no fitting origin fails the build naming the child when
  /// it is named. The overlap scan is linear in the placed count: typical
  /// level scatters place a handful of elements, and this runs once at
  /// build time — swap in an occupancy bitmap if huge dense scatters ever
  /// show up in a profile.
  let scatterRects
    (seed: int)
    (bounds: CellRect)
    (stamps: Stamp<'T>[])
    : CellRect[] =
    let origins = permutation seed (bounds.W * bounds.H)

    let placed = ResizeArray<CellRect>()
    let result = Array.zeroCreate stamps.Length

    for i in 0 .. stamps.Length - 1 do
      let w = max 1 stamps.[i].W
      let h = max 1 stamps.[i].H
      let mutable found = ValueNone
      let mutable k = 0

      while found.IsNone && k < origins.Length do
        let cell = origins.[k]
        let ox = bounds.X + (cell % bounds.W)
        let oy = bounds.Y + (cell / bounds.W)

        let fits =
          ox + w <= bounds.X + bounds.W && oy + h <= bounds.Y + bounds.H

        let candidate: CellRect = { X = ox; Y = oy; W = w; H = h }

        // Bounds first: a candidate that leaves the container must not
        // pay for the overlap scan. Manual loop, not a List.Exists
        // closure: this is the innermost scan and a closure capture
        // would allocate per candidate.
        if fits then
          let mutable clash = false
          let mutable j = 0

          while not clash && j < placed.Count do
            clash <- overlaps candidate placed.[j]
            j <- j + 1

          if not clash then
            found <- ValueSome candidate

        k <- k + 1

      match found with
      | ValueSome rect ->
        placed.Add rect
        result.[i] <- rect
      | ValueNone ->
        let who =
          match stamps.[i].Name with
          | ValueSome name -> $" '{name}'"
          | ValueNone -> ""

        invalidOp
          $"Flow.scatter: child{who} ({i + 1} of {stamps.Length}) does not fit the container"

    result

[<RequireQualifiedAccess>]
module Stamp =
  /// Creates a leaf element with a fixed cell footprint.
  let create (w: int) (h: int) (paint: GridSection2D<'T> -> unit) : Stamp<'T> =
    let w = max 0 w
    let h = max 0 h

    {
      W = w
      H = h
      Expand = 0
      Name = ValueNone
      Tags = []
      Paint = fun s _ -> paint s
    }

  /// Wraps an existing section-transforming stamp (any `Layout.*` pipeline)
  /// as a sized element. The wrapped stamp still paints with local coordinates.
  let sized
    (w: int)
    (h: int)
    (paint: GridSection2D<'T> -> GridSection2D<'T>)
    : Stamp<'T> =
    create w h (fun s -> paint s |> ignore)

  /// A sized box painted by a list of box styles (`Flow.fill`, `Flow.border`,
  /// `Flow.noise`, ...). The size is stated once, here; styles and containers
  /// never need coordinates. For a context-sized box (grid areas, docked
  /// rectangles, expanded slots) use `Flow.canvas`.
  let box (w: int) (h: int) (styles: BoxStyle<'T> list) : Stamp<'T> =
    create w h (fun s ->
      for style in styles do
        style s)

  /// Empty element that paints nothing.
  let empty() : Stamp<'T> = create 0 0 ignore

  /// Names an element so `Flow.run` reports its resolved rectangle. Names
  /// must be unique: a duplicate name throws at build time.
  let named (name: string) (stamp: Stamp<'T>) : Stamp<'T> = {
    stamp with
        Name = ValueSome name
  }

  /// Tags an element so `Flow.run` groups its resolved rectangle under each
  /// tag and marks the covered cells in the per-tag bit grids. Tags are
  /// opaque to the engine; the game defines what they mean. Multiple
  /// elements can share a tag, and one element can carry several.
  let tagged (tags: string list) (stamp: Stamp<'T>) : Stamp<'T> = {
    stamp with
        Tags = tags @ stamp.Tags
  }

  /// Marks an element to share the leftover main-axis space of its container
  /// with weight 1. Expanded elements split the leftover space by weight.
  /// Only `Flow.row` and `Flow.column` act on `Expand`; other containers
  /// throw when a direct child carries it. Combinators (`beside`, `above`,
  /// `overlay`, `inset`, `offset`, `repeat`) throw when the wrapped element
  /// carries it; apply `expand` to the composite instead.
  let expand(stamp: Stamp<'T>) : Stamp<'T> = {
    stamp with
        Expand = max 1 stamp.Expand
  }

  /// Like `expand` but with an explicit share weight.
  let expandWeight (weight: int) (stamp: Stamp<'T>) : Stamp<'T> = {
    stamp with
        Expand = max 1 weight
  }

  /// Places `second` right of `first`. Size is the sum of widths and the
  /// larger of the two heights.
  let beside (first: Stamp<'T>) (second: Stamp<'T>) : Stamp<'T> =
    FlowImpl.checkNoExpand "Stamp.beside" "first" first
    FlowImpl.checkNoExpand "Stamp.beside" "second" second

    let w = first.W + second.W
    let h = max first.H second.H

    {
      W = w
      H = h
      Expand = 0
      Name = ValueNone
      Tags = []
      Paint =
        fun s registry ->
          FlowImpl.paintChild
            s
            {
              X = s.OffsetX
              Y = s.OffsetY
              W = first.W
              H = first.H
            }
            registry
            first

          FlowImpl.paintChild
            s
            {
              X = s.OffsetX + first.W
              Y = s.OffsetY
              W = second.W
              H = second.H
            }
            registry
            second
    }

  /// Stacks `bottom` under `top`. Size is the larger of the two widths and
  /// the sum of heights.
  let above (top: Stamp<'T>) (bottom: Stamp<'T>) : Stamp<'T> =
    FlowImpl.checkNoExpand "Stamp.above" "top" top
    FlowImpl.checkNoExpand "Stamp.above" "bottom" bottom

    let w = max top.W bottom.W
    let h = top.H + bottom.H

    {
      W = w
      H = h
      Expand = 0
      Name = ValueNone
      Tags = []
      Paint =
        fun s registry ->
          FlowImpl.paintChild
            s
            {
              X = s.OffsetX
              Y = s.OffsetY
              W = top.W
              H = top.H
            }
            registry
            top

          FlowImpl.paintChild
            s
            {
              X = s.OffsetX
              Y = s.OffsetY + top.H
              W = bottom.W
              H = bottom.H
            }
            registry
            bottom
    }

  /// Draws both elements over the full area of the container, `second` on
  /// top. Size is the larger of the two footprints.
  let overlay (first: Stamp<'T>) (second: Stamp<'T>) : Stamp<'T> =
    FlowImpl.checkNoExpand "Stamp.overlay" "first" first
    FlowImpl.checkNoExpand "Stamp.overlay" "second" second

    let w = max first.W second.W
    let h = max first.H second.H

    {
      W = w
      H = h
      Expand = 0
      Name = ValueNone
      Tags = []
      Paint =
        fun s registry ->
          FlowImpl.paintChild s (FlowImpl.rectOf s) registry first
          FlowImpl.paintChild s (FlowImpl.rectOf s) registry second
    }

  /// Shrinks the paint area of `stamp` by explicit per-side amounts.
  let insetEx (inset: InsetSpec) (stamp: Stamp<'T>) : Stamp<'T> =
    FlowImpl.checkNoExpand "Stamp.insetEx" "stamp" stamp

    let l = max 0 inset.Left
    let t = max 0 inset.Top
    let r = max 0 inset.Right
    let b = max 0 inset.Bottom
    let w = stamp.W + l + r
    let h = stamp.H + t + b

    {
      W = w
      H = h
      Expand = 0
      Name = ValueNone
      Tags = []
      Paint =
        fun s registry ->
          FlowImpl.paintChild
            s
            {
              X = s.OffsetX + l
              Y = s.OffsetY + t
              W = stamp.W
              H = stamp.H
            }
            registry
            stamp
    }

  /// Shrinks the paint area of `stamp` by `n` cells on all sides and grows
  /// the footprint to `stamp + 2n`. The gutter stays empty.
  let inset (n: int) (stamp: Stamp<'T>) : Stamp<'T> =
    FlowImpl.checkNoExpand "Stamp.inset" "stamp" stamp

    insetEx
      {
        Left = n
        Top = n
        Right = n
        Bottom = n
      }
      stamp

  /// Shifts the paint position of `stamp` by a signed offset. The footprint
  /// grows to cover both the original and the shifted area. Negative offsets
  /// shift within the enlarged footprint and clamp at the container edge.
  let offset (dx: int) (dy: int) (stamp: Stamp<'T>) : Stamp<'T> =
    FlowImpl.checkNoExpand "Stamp.offset" "stamp" stamp

    let x0 = min 0 dx
    let y0 = min 0 dy
    let x1 = max dx (dx + stamp.W)
    let y1 = max dy (dy + stamp.H)
    let w = x1 - x0
    let h = y1 - y0

    {
      W = w
      H = h
      Expand = 0
      Name = ValueNone
      Tags = []
      Paint =
        fun s registry ->
          FlowImpl.paintChild
            s
            {
              X = s.OffsetX + dx - x0
              Y = s.OffsetY + dy - y0
              W = stamp.W
              H = stamp.H
            }
            registry
            stamp
    }

  /// Repeats `stamp` `count` times, side by side with no gap.
  let repeat (count: int) (stamp: Stamp<'T>) : Stamp<'T> =
    FlowImpl.checkNoExpand "Stamp.repeat" "stamp" stamp

    if count <= 0 then
      empty()
    else
      let w = stamp.W * count

      {
        W = w
        H = stamp.H
        Expand = 0
        Name = ValueNone
        Tags = []
        Paint =
          fun s registry ->
            for i in 0 .. count - 1 do
              FlowImpl.paintChild
                s
                {
                  X = s.OffsetX + i * stamp.W
                  Y = s.OffsetY
                  W = stamp.W
                  H = stamp.H
                }
                registry
                stamp
      }

/// Build-time level authoring: stamps, box styles, containers, and landmark
/// recording all run when the level builds (`Flow.run`/`Flow.build`), never
/// per frame. Build once, then keep per-frame queries on `Flow.isTag`, or
/// hoist the lookup with `Flow.tryTagGrid`.
///
/// Hex grids author identically: build the grid with
/// `CellGrid2D.createHex` and run the same document. Authoring is
/// cell-space, so geometry affects world positions and spatial queries
/// only; reported rectangles are offset-space bounding boxes.
[<RequireQualifiedAccess>]
module Flow =
  let private linearStamp
    (isRow: bool)
    (opts: FlowOpts)
    (children: Stamp<'T>[])
    : Stamp<'T> =
    if children.Length = 0 then
      Stamp.empty()
    else
      let gap = max 0 opts.Gap

      let mainSum = children |> Array.sumBy(fun c -> if isRow then c.W else c.H)

      let crossMax =
        children |> Array.map(fun c -> if isRow then c.H else c.W) |> Array.max

      let extent = mainSum + gap * (children.Length - 1)
      let w, h = if isRow then extent, crossMax else crossMax, extent

      {
        W = w
        H = h
        Expand = 0
        Name = ValueNone
        Tags = []
        Paint =
          fun s registry ->
            let assigned = FlowImpl.rectOf s
            let mainLen = if isRow then assigned.W else assigned.H
            let crossLen = if isRow then assigned.H else assigned.W

            let mainOf(c: Stamp<'T>) = if isRow then c.W else c.H

            let crossOf(c: Stamp<'T>) = if isRow then c.H else c.W

            // Break children into lines (a single line when Wrap is off).
            let lines = ResizeArray<Stamp<'T>[]>()

            if not opts.Wrap then
              lines.Add children
            else
              let mutable current = ResizeArray<Stamp<'T>>()
              let mutable currentMain = 0

              for c in children do
                let m = mainOf c

                if current.Count > 0 && currentMain + gap + m > mainLen then
                  lines.Add(current.ToArray())
                  current <- ResizeArray<Stamp<'T>>()
                  current.Add(c)
                  currentMain <- m
                else
                  currentMain <-
                    if current.Count = 0 then m else currentMain + gap + m

                  current.Add(c)

              if current.Count > 0 then
                lines.Add(current.ToArray())

            // Lay out each line.
            let mutable lineTop = 0

            for line in lines do
              let n = line.Length

              if n > 0 then
                let sizes = Array.zeroCreate<int> n
                let mutable fixedSum = 0
                let mutable expandTotal = 0

                for i in 0 .. n - 1 do
                  sizes.[i] <- mainOf line.[i]
                  fixedSum <- fixedSum + sizes.[i]

                  if line.[i].Expand > 0 then
                    expandTotal <- expandTotal + line.[i].Expand

                let leftover = mainLen - fixedSum - gap * (n - 1)

                if leftover > 0 && expandTotal > 0 then
                  let mutable cum = 0
                  let mutable distributed = 0

                  for i in 0 .. n - 1 do
                    let c = line.[i]

                    if c.Expand > 0 then
                      cum <- cum + c.Expand
                      let target = leftover * cum / expandTotal
                      sizes.[i] <- sizes.[i] + (target - distributed)
                      distributed <- target

                let mutable lineCross = 0

                for i in 0 .. n - 1 do
                  lineCross <- max lineCross (crossOf line.[i])

                // Wrapped lines are as tall as their tallest child; a single
                // line spans the whole container. A wrapped line without a
                // cross-sized child has no height of its own, so it spans
                // the container like a single line does.
                let lineLen =
                  if opts.Wrap && lineCross > 0 then lineCross else crossLen

                let extent = Array.sum sizes + gap * (n - 1)

                let startOffset =
                  match opts.Justify with
                  | Center -> max 0 ((mainLen - extent) / 2)
                  | End -> max 0 (mainLen - extent)
                  | _ -> 0

                let mutable mainCursor = startOffset

                for i in 0 .. n - 1 do
                  let c = line.[i]

                  // Like CSS align-items: stretch, children without a cross
                  // size of their own stretch over the line.
                  let cSize =
                    match opts.Align with
                    | Stretch -> lineLen
                    | _ when crossOf c = 0 -> lineLen
                    | _ -> crossOf c

                  let withinOff =
                    match opts.Align with
                    | Center -> (lineLen - cSize) / 2
                    | End -> lineLen - cSize
                    | _ -> 0

                  let crossOff = lineTop + withinOff

                  let r =
                    if isRow then
                      {
                        X = assigned.X + mainCursor
                        Y = assigned.Y + crossOff
                        W = sizes.[i]
                        H = cSize
                      }
                    else
                      {
                        X = assigned.X + crossOff
                        Y = assigned.Y + mainCursor
                        W = cSize
                        H = sizes.[i]
                      }

                  FlowImpl.paintChild s r registry c
                  mainCursor <- mainCursor + sizes.[i] + gap

                lineTop <- lineTop + lineLen + gap
      }

  /// Lays children out left to right and computes the container footprint.
  /// Fixed children keep their width; expanded children keep their width and
  /// split the leftover width of the assigned area by weight.
  let row (opts: FlowOpts) (children: Stamp<'T> seq) : Stamp<'T> =
    linearStamp true opts (Array.ofSeq children)

  /// Lays children out top to bottom. Same rules as `row` with the axes
  /// swapped.
  let column (opts: FlowOpts) (children: Stamp<'T> seq) : Stamp<'T> =
    linearStamp false opts (Array.ofSeq children)

  // ── Box styles ─────────────────────────────────────────────────────────
  // Box styles paint the full area of the element they are applied to. They
  // never take coordinates: combine them in `Stamp.box` or `Flow.canvas`,
  // and let containers decide where the box lands.

  /// Fills the whole box with one content.
  let inline fill (content: 'T) (section: GridSection2D<'T>) : unit =
    Layout.fill 0 0 section.Width section.Height content section |> ignore

  /// Fills a sub-rectangle of the box with one content, in box-local
  /// coordinates — the local-area counterpart of `fill` for rooms, roads,
  /// and platforms inside a bigger element.
  let inline fillRect
    (area: CellRect)
    (content: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.fill area.X area.Y area.W area.H content section |> ignore

  /// Outlines the box with one content.
  let inline border (content: 'T) (section: GridSection2D<'T>) : unit =
    Layout.border 0 0 section.Width section.Height content section |> ignore

  /// Fills the box, then outlines it.
  let inline rect
    (borderContent: 'T)
    (fillContent: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.rect
      0
      0
      section.Width
      section.Height
      borderContent
      fillContent
      section
    |> ignore

  /// Puts one content on the four corners of the box.
  let inline corners (content: 'T) (section: GridSection2D<'T>) : unit =
    Layout.corners 0 0 section.Width section.Height content section |> ignore

  /// Checkerboards the box between two contents.
  let inline checker (odd: 'T) (even: 'T) (section: GridSection2D<'T>) : unit =
    Layout.checker odd even section |> ignore

  /// Scatters the spec's `Count` cells of one content over the box
  /// (seeded).
  let inline noise
    (spec: ScatterSpec)
    (content: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.scatter spec.Count spec.Seed content section |> ignore

  /// Generates the box cell by cell; the callback receives local coordinates
  /// (x across the box, y down the box).
  let inline texture
    (generator: int -> int -> 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.generate 0 0 section.Width section.Height generator section |> ignore

  /// Replaces every occurrence of one content with another, box-wide.
  let inline replace
    (oldContent: 'T)
    (newContent: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.replace oldContent newContent section |> ignore

  /// Replaces content box-wide with the spec's `Probability` (seeded) —
  /// weathering.
  let inline weather
    (spec: WeatherSpec)
    (oldContent: 'T)
    (newContent: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.replaceScatter
      oldContent
      newContent
      spec.Probability
      spec.Seed
      section
    |> ignore

  /// Stamps a small paint pipeline the spec's `Count` times at random spots
  /// in the box (seeded) — rock clusters, puddles, rubble. The pipeline
  /// receives a section whose offset is the chosen cell and whose extent
  /// runs to the box's bottom-right corner, so paint relative to the
  /// section origin.
  let inline clumps
    (spec: ScatterSpec)
    (paint: GridSection2D<'T> -> GridSection2D<'T>)
    (section: GridSection2D<'T>)
    : unit =
    Layout.scatterStamp spec.Count spec.Seed paint section |> ignore

  /// Scatters the spec's `Count` cells of generated content over the box
  /// (seeded); the callback receives the cell coordinates (x across the
  /// box, y down the box) and returns the content. The sparse counterpart
  /// of `texture` — prop variety picked per scattered cell.
  let inline noiseBy
    (spec: ScatterSpec)
    (generator: int -> int -> 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.scatterBy spec.Count spec.Seed generator section |> ignore

  /// Paints one cell at a local coordinate — the content atom.
  let inline cell
    (at: CellPoint)
    (content: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.set at.X at.Y content section |> ignore

  /// Paints a horizontal run of `count` cells from the box origin —
  /// `background-repeat: repeat-x`.
  let inline repeatX
    (count: int)
    (content: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.repeatX 0 0 count content section |> ignore

  /// Paints a vertical run of `count` cells from the box origin —
  /// `background-repeat: repeat-y`.
  let inline repeatY
    (count: int)
    (content: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.repeatY 0 0 count content section |> ignore

  /// Paints a Bresenham line between two local points — roads, pipes,
  /// fences.
  let inline line
    (start: CellPoint)
    (finish: CellPoint)
    (content: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.line start.X start.Y finish.X finish.Y content section |> ignore

  /// Paints a midpoint circle from the spec; `Filled` spans the interior.
  /// Circle semantics are pixel-space; hex-true rings come from
  /// `Hex2DSpatial.ring`.
  let inline circle
    (spec: CircleSpec)
    (content: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.circle
      spec.Center.X
      spec.Center.Y
      spec.Radius
      spec.Filled
      content
      section
    |> ignore

  /// Paints a polygon from local vertices — `clip-path: polygon()`.
  /// `filled` spans the interior.
  let inline polygon
    (points: struct (int * int)[])
    (filled: bool)
    (content: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.polygon points filled content section |> ignore

  /// Scatters the spec's `Count` cells along the box border (seeded) — a
  /// weathered edge, crumbling ramparts, asteroid fringes.
  let inline scatterBorder
    (spec: ScatterSpec)
    (content: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.scatterBorder
      0
      0
      section.Width
      section.Height
      spec.Count
      spec.Seed
      content
      section
    |> ignore

  /// Scatters the spec's `Count` cells along the line between its `From`
  /// and `To` points (seeded) — a broken road, a dotted route.
  let inline scatterLine
    (spec: ScatterLineSpec)
    (content: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.scatterLine
      spec.From.X
      spec.From.Y
      spec.To.X
      spec.To.Y
      spec.Count
      spec.Seed
      content
      section
    |> ignore

  /// Paints alternating cells along the box border.
  let inline checkerBorder
    (odd: 'T)
    (even: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.checkerBorder 0 0 section.Width section.Height odd even section
    |> ignore

  /// Erases the whole box.
  let inline clear () (section: GridSection2D<'T>) : unit =
    Layout.clear 0 0 section.Width section.Height section |> ignore

  /// Sets one cell only when it is still empty — the `:empty` selector.
  /// Later styles still paint over it.
  let inline setIfEmpty
    (at: CellPoint)
    (content: 'T)
    (section: GridSection2D<'T>)
    : unit =
    Layout.setIfEmpty at.X at.Y content section |> ignore

  /// Rewrites the existing cells of the box through `mapping` — a derive
  /// pass after other styles.
  let inline map (mapping: 'T -> 'T) (section: GridSection2D<'T>) : unit =
    Layout.map 0 0 section.Width section.Height mapping section |> ignore

  /// A context-sized box of styles: paints whatever area its container
  /// assigns to it — a grid area, an overlay layer, a docked rectangle, an
  /// expanded slot. It has no intrinsic footprint; a zero dimension always
  /// means "stretch on that axis".
  let canvas(styles: BoxStyle<'T> list) : Stamp<'T> = {
    W = 0
    H = 0
    Expand = 0
    Name = ValueNone
    Tags = []
    Paint =
      fun s _ ->
        for style in styles do
          style s
  }

  /// A single-cell prop: one content, one cell. The workhorse of clutter.
  let prop(content: 'T) : Stamp<'T> = Stamp.box 1 1 [ fill content ]

  /// Marks an element to share the leftover main-axis space of its container
  /// (flex-grow). Alias for `Stamp.expand`, so level documents can stay on
  /// `Flow.*`. Only `row`/`column` honor it; containers and combinators
  /// reject it.
  let expand(stamp: Stamp<'T>) : Stamp<'T> = Stamp.expand stamp

  let private resolveTracks (total: int) (tracks: Track[]) (gap: int) : int[] =
    let n = tracks.Length
    let sizes = Array.zeroCreate<int> n
    let mutable fixedSum = 0
    let mutable pctSum = 0
    let mutable weightTotal = 0f

    for i in 0 .. n - 1 do
      match tracks.[i] with
      | Fixed v ->
        let v = max 0 v
        sizes.[i] <- v
        fixedSum <- fixedSum + v
      | Percent f ->
        // A fraction over 1 clamps to the container length; unclamped, it
        // would push later tracks and reported rectangles past the area.
        let v = max 0 (min total (int(float32 total * f)))
        sizes.[i] <- v
        pctSum <- pctSum + v
      | Weight w -> weightTotal <- weightTotal + max 0f w
      // Auto never reaches here: `Flow.grid` resolves it against its
      // places' footprints before laying out.
      | Auto -> ()

    let free = total - fixedSum - pctSum - gap * (n - 1)

    if weightTotal > 0f && free > 0 then
      let mutable cum = 0f
      let mutable distributed = 0

      for i in 0 .. n - 1 do
        match tracks.[i] with
        | Weight w ->
          cum <- cum + max 0f w
          let target = int(float32 free * cum / weightTotal)
          sizes.[i] <- target - distributed
          distributed <- target
        | _ -> ()

    sizes

  let private prefixOffsets (sizes: int[]) (gap: int) : int[] =
    let offs = Array.zeroCreate<int> sizes.Length
    let mutable acc = 0

    for i in 0 .. sizes.Length - 1 do
      offs.[i] <- acc
      acc <- acc + sizes.[i] + gap

    offs

  let inline private spanSize
    (sizes: int[])
    (start: int)
    (span: int)
    (gap: int)
    : int =
    let mutable sum = 0

    for i in start .. start + span - 1 do
      sum <- sum + sizes.[i]

    sum + gap * (span - 1)

  /// A CSS-grid-like container. Tracks size the columns and rows: `Fixed`
  /// tracks take literal cells, `Auto` tracks size to the largest footprint
  /// of their span-1 places (a spanning place shares its footprint over the
  /// `Auto` tracks in its span), `Weight` tracks share the leftover length
  /// of the assigned area, `Percent` tracks take a fraction of it. `Areas`
  /// uses CSS grid-area strings (`"main main side"` spans columns, `.` is
  /// an empty cell). Places mount by named template area or by explicit
  /// slot; slots may overlap, and paint order is `Places` order, later
  /// places on top:
  ///
  /// `grid { Cols = [| Fixed 20; Weight 1f |]; Rows = [| Fixed 6 |]; Gap = 1; Areas = [| "map side" |]; Places = [| struct (Area "map", dungeon) |] }`
  let grid(opts: GridOpts<'T>) : Stamp<'T> =
    let colArr = opts.Cols
    let rowArr = opts.Rows
    let gap = max 0 opts.Gap

    if colArr.Length = 0 || rowArr.Length = 0 then
      invalidArg "Cols" "grid needs at least one column track and one row track"

    let cells =
      opts.Areas
      |> Array.map(fun line ->
        line.Split(
          [| ' '; '\t' |],
          System.StringSplitOptions.RemoveEmptyEntries
        ))

    for line in cells do
      if line.Length > colArr.Length then
        invalidArg
          "Areas"
          ("grid template row has "
           + string line.Length
           + " columns but the grid defines "
           + string colArr.Length
           + " column tracks")

    if cells.Length > rowArr.Length then
      invalidArg
        "Areas"
        ("grid template has "
         + string cells.Length
         + " rows but the grid defines "
         + string rowArr.Length
         + " row tracks")

    // Bounding boxes of named areas, in track units.
    let areas = Dictionary<string, struct (int * int * int * int)>()

    for r in 0 .. cells.Length - 1 do
      for c in 0 .. cells.[r].Length - 1 do
        let name = cells.[r].[c]

        if name <> "." then
          areas.[name] <-
            Dictionary.tryGetValue name areas
            |> ValueOption.map(fun struct (c0, r0, cs, rs) ->
              let c1 = c0 + cs
              let r1 = r0 + rs
              let nc0 = min c0 c
              let nc1 = max c1 (c + 1)
              let nr0 = min r0 r
              let nr1 = max r1 (r + 1)
              struct (nc0, nr0, nc1 - nc0, nr1 - nr0))
            |> ValueOption.defaultValue(struct (c, r, 1, 1))

    // Every place resolves to a track-unit span at construction: a bad
    // area name or an out-of-grid slot fails here, before anything paints.
    let spans =
      opts.Places
      |> Array.map(fun struct (place, stamp) ->
        FlowImpl.checkNoExpand "Flow.grid" "Places" stamp

        match place with
        | Place.Area name ->
          match Dictionary.tryGetValue name areas with
          | ValueSome span -> span
          | ValueNone ->
            invalidArg
              "Places"
              ("area '" + name + "' is not defined in the grid template")
        | Place.Slot(c0, r0, cs, rs) ->
          if cs < 1 || rs < 1 then
            invalidArg
              "Places"
              $"slot spans must be positive, got colspan {cs} and rowspan {rs}"

          // `cs > colArr.Length - c0` rather than `c0 + cs > ...`: a
          // huge c0 would otherwise wrap negative and pass the check
          if
            c0 < 0
            || r0 < 0
            || cs > colArr.Length - c0
            || rs > rowArr.Length - r0
          then
            invalidArg
              "Places"
              $"slot (col {c0}, row {r0}, colspan {cs}, rowspan {rs}) sits outside the grid's {colArr.Length} column and {rowArr.Length} row tracks"

          struct (c0, r0, cs, rs))

    // Auto tracks size to the largest footprint of their span-1 places;
    // a place reports no footprint on a zero axis, so it contributes
    // nothing and the track collapses.
    let colAuto = Array.zeroCreate colArr.Length
    let rowAuto = Array.zeroCreate rowArr.Length

    // Shares one spanning place's footprint over the Auto tracks it
    // covers: without this, a span that no span-1 place can size would
    // collapse every track in it to zero and the place would paint
    // nothing at all.
    let shareOverAuto
      (tracks: Track[])
      (auto: int[])
      (c0: int)
      (span: int)
      (size: int)
      =
      let mutable autos = 0

      for i in c0 .. c0 + span - 1 do
        if tracks.[i] = Auto then
          autos <- autos + 1

      if autos > 0 then
        let share = (size + autos - 1) / autos

        for i in c0 .. c0 + span - 1 do
          if tracks.[i] = Auto then
            auto.[i] <- max auto.[i] share

    for i in 0 .. opts.Places.Length - 1 do
      let struct (_, stamp) = opts.Places.[i]
      let struct (c0, r0, cs, rs) = spans.[i]

      if cs = 1 && stamp.W > 0 then
        colAuto.[c0] <- max colAuto.[c0] stamp.W
      elif cs > 1 && stamp.W > 0 then
        shareOverAuto colArr colAuto c0 cs stamp.W

      if rs = 1 && stamp.H > 0 then
        rowAuto.[r0] <- max rowAuto.[r0] stamp.H
      elif rs > 1 && stamp.H > 0 then
        shareOverAuto rowArr rowAuto r0 rs stamp.H

    // The conversions copy the track arrays; skip them when no Auto
    // track needs resolving.
    let resolveAuto (tracks: Track[]) (auto: int[]) : Track[] =
      if not(Array.contains Auto tracks) then
        tracks
      else
        tracks
        |> Array.mapi(fun i t ->
          match t with
          | Auto -> Fixed(max 0 auto.[i])
          | t -> t)

    let cols = resolveAuto colArr colAuto
    let rows = resolveAuto rowArr rowAuto

    let placements = opts.Places

    let wFixed =
      cols
      |> Array.sumBy (function
        | Fixed v -> max 0 v
        | _ -> 0)

    let hFixed =
      rows
      |> Array.sumBy (function
        | Fixed v -> max 0 v
        | _ -> 0)

    let w = if wFixed = 0 then 0 else wFixed + gap * (colArr.Length - 1)
    let h = if hFixed = 0 then 0 else hFixed + gap * (rowArr.Length - 1)

    {
      W = w
      H = h
      Expand = 0
      Name = ValueNone
      Tags = []
      Paint =
        fun s registry ->
          let assigned = FlowImpl.rectOf s
          let colSizes = resolveTracks assigned.W cols gap
          let rowSizes = resolveTracks assigned.H rows gap
          let colOff = prefixOffsets colSizes gap
          let rowOff = prefixOffsets rowSizes gap

          for i in 0 .. placements.Length - 1 do
            let struct (_, stamp) = placements.[i]
            let struct (c0, r0, cs, rs) = spans.[i]
            let ax = assigned.X + colOff.[c0]
            let ay = assigned.Y + rowOff.[r0]
            let aw = spanSize colSizes c0 cs gap
            let ah = spanSize rowSizes r0 rs gap
            // A zero footprint dimension stretches over the assigned
            // tracks (canvas, strips); a fixed dimension keeps its size,
            // anchored to the track start.
            let w = if stamp.W = 0 then aw else min stamp.W aw
            let h = if stamp.H = 0 then ah else min stamp.H ah

            FlowImpl.paintChild
              s
              { X = ax; Y = ay; W = w; H = h }
              registry
              stamp
    }

  /// Paints the spec's `Stamp` into a docked rectangle of `section`. The
  /// spec's `Anchor` says where (`Dock.Top ||| Dock.CenterX` and so on);
  /// `Inset` distances the element from the container's edges, per side.
  /// `StretchX`/`StretchY` span the section minus the insets of their two
  /// edges, and a zero footprint dimension stretches on its axis without
  /// them. Returns `section` for pipeline chaining. Records no positions;
  /// `Flow.docked` is the level-document form that reports its rectangle.
  let dock
    (spec: DockSpec<'T>)
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    FlowImpl.checkNoExpand "Flow.dock" "Stamp" spec.Stamp

    let r =
      FlowImpl.dockRect spec.Anchor spec.Inset (FlowImpl.rectOf section) {
        X = 0
        Y = 0
        W = spec.Stamp.W
        H = spec.Stamp.H
      }

    FlowImpl.paintChild section r ValueNone spec.Stamp

    section

  /// A docked element for composition: paints the spec's `Stamp` into a
  /// docked rectangle of whatever container it mounts into and occupies no
  /// flow space (zero footprint). The spec's `Anchor` says where
  /// (`Dock.Top ||| Dock.CenterX` and so on); `Inset` distances the element
  /// from the container's edges, per side. `StretchX`/`StretchY` span the
  /// container minus the insets of their two edges, and a zero footprint
  /// dimension of the wrapped stamp stretches on its axis without them. Pair
  /// with `overlay` to place docks over a base layout. Name the stamped
  /// element to report its docked rectangle.
  let docked(spec: DockSpec<'T>) : Stamp<'T> =
    FlowImpl.checkNoExpand "Flow.docked" "Stamp" spec.Stamp

    {
      W = 0
      H = 0
      Expand = 0
      Name = ValueNone
      Tags = []
      Paint =
        fun s registry ->
          let r =
            FlowImpl.dockRect spec.Anchor spec.Inset (FlowImpl.rectOf s) {
              X = 0
              Y = 0
              W = spec.Stamp.W
              H = spec.Stamp.H
            }

          FlowImpl.paintChild s r registry spec.Stamp
    }

  /// Places `stamp` at an exact offset of its container: the top-left sits
  /// `x` cells from the left edge and `y` cells from the top edge. Exact
  /// placement occupies no flow space (zero footprint), so it mounts inside
  /// `overlay` layers and grid areas like `docked` does; a zero dimension of
  /// the wrapped stamp stretches on its axis from that origin to the
  /// container's far edge. Negative offsets clamp at 0. Name the stamped
  /// element to report its rectangle.
  let at (x: int) (y: int) (stamp: Stamp<'T>) : Stamp<'T> =
    FlowImpl.checkNoExpand "Flow.at" "stamp" stamp

    docked {
      Anchor = Dock.Left ||| Dock.Top
      Inset = {
        Left = x
        Top = y
        Right = 0
        Bottom = 0
      }
      Stamp = stamp
    }

  /// A full-length strip docked to one edge (`Dock.Top`, `Dock.Bottom`,
  /// `Dock.Left` or `Dock.Right`) with the given thickness in cells:
  /// `strip Dock.Bottom 1 [ fill Sand ]` is a full-width, one-cell-tall bar
  /// on the bottom edge. Occupies no flow space; pair with `overlay`.
  let strip
    (side: Dock)
    (thickness: int)
    (styles: BoxStyle<'T> list)
    : Stamp<'T> =
    let t = max 0 thickness

    let flags, w, h =
      if side &&& Dock.Top <> Dock.None then
        (Dock.StretchX ||| Dock.Top), 0, t
      elif side &&& Dock.Bottom <> Dock.None then
        (Dock.StretchX ||| Dock.Bottom), 0, t
      elif side &&& Dock.Right <> Dock.None then
        (Dock.StretchY ||| Dock.Right), t, 0
      elif side &&& Dock.Left <> Dock.None then
        (Dock.StretchY ||| Dock.Left), t, 0
      else
        invalidArg
          "side"
          "strip needs exactly one edge flag (Top, Bottom, Left or Right)"

    docked {
      Anchor = flags
      Inset = InsetSpec.Zero
      Stamp = Stamp.box w h styles
    }

  /// Stacks children over the full area of the container, later children
  /// paint over earlier ones. The footprint is the largest child. Layers are
  /// full-bleed: every child paints into the whole assigned area, so size a
  /// child with `docked`, or place fixed-footprint children through
  /// `grid`/`row`/`column` instead.
  let overlay(children: Stamp<'T> seq) : Stamp<'T> =
    let arr = Array.ofSeq children
    FlowImpl.checkNoExpandAll "Flow.overlay" "children" arr

    if arr.Length = 0 then
      Stamp.empty()
    else
      let w = arr |> Array.map(fun c -> c.W) |> Array.max
      let h = arr |> Array.map(fun c -> c.H) |> Array.max

      {
        W = w
        H = h
        Expand = 0
        Name = ValueNone
        Tags = []
        Paint =
          fun s registry ->
            let r = FlowImpl.rectOf s

            for c in arr do
              FlowImpl.paintChild s r registry c
      }

  /// Scatters sized children over the assigned area at seeded,
  /// non-overlapping origins — prop clusters, debris fields, spawn rings.
  /// Candidate origins are the container's cells in the order of a seeded
  /// permutation (a fixed xorshift shuffle — the placement cannot drift
  /// across .NET versions, unlike the BCL random generator behind the
  /// paint-side scatter styles); children place in the given order and
  /// each takes the first origin where its footprint fits without
  /// overlapping an earlier one. The same seed and the same container
  /// build the same level every run. A child that fits nowhere fails the
  /// build naming the child when it is named. The footprint is the
  /// largest child's, so scatter inside a sized context (`group`, a grid
  /// area, a docked stretch) to choose the region; a child with a zero
  /// axis (a context-sized child, or any element sized `0` on one side)
  /// places as one cell on that axis, and `expand` children throw.
  let scatter (seed: int) (children: Stamp<'T> seq) : Stamp<'T> =
    let arr = Array.ofSeq children
    FlowImpl.checkNoExpandAll "Flow.scatter" "children" arr

    if arr.Length = 0 then
      Stamp.empty()
    else
      let w = arr |> Array.map(fun c -> c.W) |> Array.max
      let h = arr |> Array.map(fun c -> c.H) |> Array.max

      {
        W = w
        H = h
        Expand = 0
        Name = ValueNone
        Tags = []
        Paint =
          fun s registry ->
            let assigned = FlowImpl.rectOf s
            let rects = FlowImpl.scatterRects seed assigned arr

            for i in 0 .. arr.Length - 1 do
              FlowImpl.paintChild s rects.[i] registry arr.[i]
      }

  /// A fixed-size group that lays its children out over its own area with
  /// overlay rules: each child paints into the group's box and later children
  /// paint on top. Children are layers (full-bleed), so give a child a
  /// specific rectangle with `docked`. The size is stated once, here;
  /// `canvas` children fill the
  /// box and `docked` children anchor within it.
  let group (w: int) (h: int) (children: Stamp<'T> list) : Stamp<'T> =
    let w = max 0 w
    let h = max 0 h
    let arr = Array.ofList children
    FlowImpl.checkNoExpandAll "Flow.group" "children" arr

    {
      W = w
      H = h
      Expand = 0
      Name = ValueNone
      Tags = []
      Paint =
        fun s registry ->
          let bounds = FlowImpl.rectOf s

          let r = {
            X = bounds.X
            Y = bounds.Y
            W = min w bounds.W
            H = min h bounds.H
          }

          for c in arr do
            FlowImpl.paintChild s r registry c
    }

  /// Paints a stamp with its top-left at the section origin and returns the
  /// section, for use in existing `Layout.*` pipelines. Records no positions.
  let paint
    (stamp: Stamp<'T>)
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    FlowImpl.checkNoExpand "Flow.paint" "stamp" stamp

    stamp.Paint section ValueNone
    section

  /// Lays a stamp out over the whole grid and returns the grid together with
  /// its landmarks: the resolved rectangles of named elements, tag groups,
  /// and per-cell tag bit grids:
  ///
  /// `let struct (level, placed) = grid |> Flow.run levelBody`
  let run
    (stamp: Stamp<'T>)
    (grid: CellGrid2D<'T>)
    : struct (CellGrid2D<'T> * Landmarks) =
    FlowImpl.checkNoExpand "Flow.run" "stamp" stamp

    let landmarks: Landmarks = {
      Width = grid.Width
      Height = grid.Height
      Named = Dictionary()
      Tagged = Dictionary()
      Cells = Dictionary()
    }

    let full: CellRect = {
      X = 0
      Y = 0
      W = grid.Width
      H = grid.Height
    }

    FlowImpl.recordLandmarks (ValueSome landmarks) stamp full

    let section: GridSection2D<'T> = {
      BackingGrid = grid
      OffsetX = 0
      OffsetY = 0
      Width = grid.Width
      Height = grid.Height
    }

    stamp.Paint section (ValueSome landmarks)
    struct (grid, landmarks)

  /// `run` and `Landmarks.scanTiles` in one call: lays `stamp` out over the
  /// grid, then derives the per-cell tag bit grids from the painted tiles
  /// through `extract` (x, y, content -> tags). Use it when tiles carry
  /// meaning the simulation should query:
  ///
  /// `let struct (level, marks) = grid |> Flow.build tileTags levelBody`
  let build
    (extract: int -> int -> 'T -> string seq)
    (stamp: Stamp<'T>)
    (grid: CellGrid2D<'T>)
    : struct (CellGrid2D<'T> * Landmarks) =
    let struct (grid, landmarks) = run stamp grid
    struct (grid, Landmarks.scanTiles extract grid landmarks)

  /// Looks up the resolved rectangle of a named element.
  let inline tryPosition
    (name: string)
    (landmarks: Landmarks)
    : CellRect voption =
    Dictionary.tryGetValue name landmarks.Named

  /// Returns every rectangle recorded under a tag, most recent first.
  /// Build-time/occasional queries; the per-cell hot path is `Flow.isTag`.
  let inline taggedRects (tag: string) (landmarks: Landmarks) : CellRect list =
    Dictionary.tryGetValue tag landmarks.Tagged |> ValueOption.defaultValue []

  /// The raw per-cell bit grid of `tag` (`x + y * landmarks.Width`), for hot
  /// loops: hoist the lookup out of the loop and read the array per cell.
  /// `ValueNone` when no cell carries the tag. Out of range indices are
  /// never tagged, so bounds checks stay with the caller.
  let inline tryTagGrid (tag: string) (landmarks: Landmarks) : bool[] voption =
    Dictionary.tryGetValue tag landmarks.Cells

  /// Per-cell walking query: is the cell covered by an element tagged
  /// `tag` (or marked with it in the tiles via `Flow.scanTiles`)? Out of
  /// range cells are never tagged. One dictionary lookup and one array
  /// read, no allocation — `CellPoint` is a struct; hoist the lookup with
  /// `Flow.tryTagGrid` in tight loops.
  let isTag (tag: string) (at: CellPoint) (landmarks: Landmarks) : bool =
    if
      at.X < 0
      || at.Y < 0
      || at.X >= landmarks.Width
      || at.Y >= landmarks.Height
    then
      false
    else
      match Dictionary.tryGetValue tag landmarks.Cells with
      | ValueSome cells -> cells.[at.X + at.Y * landmarks.Width]
      | ValueNone -> false

  /// A non-painting landmark: records `tags` over the element's whole
  /// rectangle. Equivalent to a tagless `Stamp.box`, so it lays out like any
  /// other child (fixed footprint in a grid area, `expand` in a row, ...).
  /// Place it through containers that honor footprints (grid areas,
  /// row/column); inside `overlay`/`group` layers, dock it.
  let region (tags: string list) (w: int) (h: int) : Stamp<'T> =
    Stamp.tagged tags (Stamp.box w h [])
