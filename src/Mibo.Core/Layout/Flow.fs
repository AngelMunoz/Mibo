namespace Mibo.Layout

open System
open System.Collections.Generic

/// Resolved rectangle of a placed element, in grid cells.
[<Struct>]
type CellRect = { X: int; Y: int; W: int; H: int }

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
  /// Rectangles grouped by tag, one entry per `Stamp.tagged` element.
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
        match CellGrid2D.get x y grid with
        | ValueNone -> ()
        | ValueSome tile ->
          for tag in extract x y tile do
            match landmarks.Cells.TryGetValue tag with
            | true, cells -> cells.[x + y * landmarks.Width] <- true
            | false, _ ->
              let cells =
                Array.create (landmarks.Width * landmarks.Height) false

              cells.[x + y * landmarks.Width] <- true
              landmarks.Cells.[tag] <- cells

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

/// A single track of a `Flow.grid` template. `Weight` tracks (`fr`) share the
/// space left after `Fixed` and `Percent` tracks; `Percent` is a fraction of
/// the assigned container length.
[<Struct>]
type Track =
  | Fixed of length: int
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

/// Options and content for `Flow.grid`: track sizes, the area template, the
/// gap between tracks, and the stamps placed into named areas. Area names
/// must appear in `Areas`; every name in `Places` must match a template area.
type GridOpts<'T> = {
  /// Column tracks; `Fixed` tracks count toward the intrinsic footprint.
  Cols: Track list
  /// Row tracks; `Fixed` tracks count toward the intrinsic footprint.
  Rows: Track list
  /// Empty cells between tracks.
  Gap: int
  /// Grid-area template strings (`"main main side"`; `.` is an empty cell).
  Areas: string list
  /// Stamps placed into named template areas.
  Places: (string * Stamp<'T>) list
}

module internal FlowImpl =

  let inline rectOf(s: GridSection2D<'T>) : CellRect = {
    X = s.OffsetX
    Y = s.OffsetY
    W = s.Width
    H = s.Height
  }

  /// Resolves the docked rectangle for `footprint` inside `bounds` with the
  /// given anchor flags and inset. A zero footprint dimension stretches over
  /// its axis; `StretchX`/`StretchY` also stretch a nonzero footprint.
  let dockRect
    (flags: Dock)
    (inset: int)
    (bounds: CellRect)
    (footprint: CellRect)
    : CellRect =
    let ins = max 0 inset

    let stretchX = (flags &&& Dock.StretchX <> Dock.None) || footprint.W = 0

    let stretchY = (flags &&& Dock.StretchY <> Dock.None) || footprint.H = 0

    let x =
      if stretchX then
        ins
      elif flags &&& Dock.Right <> Dock.None then
        bounds.W - footprint.W - ins
      elif flags &&& Dock.CenterX <> Dock.None then
        (bounds.W - footprint.W) / 2
      else
        ins

    let y =
      if stretchY then
        ins
      elif flags &&& Dock.Bottom <> Dock.None then
        bounds.H - footprint.H - ins
      elif flags &&& Dock.CenterY <> Dock.None then
        (bounds.H - footprint.H) / 2
      else
        ins

    let w = if stretchX then bounds.W - 2 * ins else footprint.W
    let h = if stretchY then bounds.H - 2 * ins else footprint.H

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
    match registry with
    | ValueNone -> ()
    | ValueSome landmarks ->
      match stamp.Name with
      | ValueSome n ->
        match landmarks.Named.TryGetValue n with
        | true, _ ->
          invalidArg
            "stamp"
            ("duplicate element name '" + n + "': names must be unique")
        | false, _ -> landmarks.Named.[n] <- r
      | ValueNone -> ()

      if not stamp.Tags.IsEmpty then
        let x1 = max 0 r.X
        let y1 = max 0 r.Y
        let x2 = min (r.X + r.W) (landmarks.Width)
        let y2 = min (r.Y + r.H) (landmarks.Height)

        for tag in stamp.Tags do
          let existing =
            match landmarks.Tagged.TryGetValue tag with
            | true, rects -> rects
            | false, _ -> []

          landmarks.Tagged.[tag] <- r :: existing

          match landmarks.Cells.TryGetValue tag with
          | true, cells ->
            for y in y1 .. y2 - 1 do
              for x in x1 .. x2 - 1 do
                cells.[x + y * landmarks.Width] <- true
          | false, _ ->
            let cells = Array.create (landmarks.Width * landmarks.Height) false

            for y in y1 .. y2 - 1 do
              for x in x1 .. x2 - 1 do
                cells.[x + y * landmarks.Width] <- true

            landmarks.Cells.[tag] <- cells

  /// Paints a child into `r`, skipping it when it lies fully outside the
  /// parent. The child section keeps the child's own origin, so stamps paint
  /// exactly where they are placed; ops that respect section bounds
  /// (fill/border/checker/...) clamp at the parent's right/bottom edge.
  /// Landmarks record the intersection of `r` with the parent, so reported
  /// rects never describe cells that nothing painted.
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
          OffsetX = r.X
          OffsetY = r.Y
          Width = x2 - r.X
          Height = y2 - r.Y
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

  /// Shrinks the paint area of `stamp` by explicit amounts per side.
  let insetEx
    (left: int)
    (top: int)
    (right: int)
    (bottom: int)
    (stamp: Stamp<'T>)
    : Stamp<'T> =
    FlowImpl.checkNoExpand "Stamp.insetEx" "stamp" stamp

    let l = max 0 left
    let t = max 0 top
    let r = max 0 right
    let b = max 0 bottom
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
    insetEx n n n n stamp

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
  let fill(content: 'T) : BoxStyle<'T> =
    fun s -> Layout.fill 0 0 s.Width s.Height content s |> ignore

  /// Outlines the box with one content.
  let border(content: 'T) : BoxStyle<'T> =
    fun s -> Layout.border 0 0 s.Width s.Height content s |> ignore

  /// Fills the box, then outlines it.
  let rect (borderContent: 'T) (fillContent: 'T) : BoxStyle<'T> =
    fun s ->
      Layout.rect 0 0 s.Width s.Height borderContent fillContent s |> ignore

  /// Puts one content on the four corners of the box.
  let corners(content: 'T) : BoxStyle<'T> =
    fun s -> Layout.corners 0 0 s.Width s.Height content s |> ignore

  /// Checkerboards the box between two contents.
  let checker (odd: 'T) (even: 'T) : BoxStyle<'T> =
    fun s -> Layout.checker odd even s |> ignore

  /// Scatters `count` cells of one content over the box (seeded).
  let noise (count: int) (seed: int) (content: 'T) : BoxStyle<'T> =
    fun s -> Layout.scatter count seed content s |> ignore

  /// Generates the box cell by cell; the callback receives local coordinates
  /// (x across the box, y down the box).
  let texture(generator: int -> int -> 'T) : BoxStyle<'T> =
    fun s -> Layout.generate 0 0 s.Width s.Height generator s |> ignore

  /// Replaces every occurrence of one content with another, box-wide.
  let replace (oldContent: 'T) (newContent: 'T) : BoxStyle<'T> =
    fun s -> Layout.replace oldContent newContent s |> ignore

  /// Replaces content box-wide with a probability (seeded) — weathering.
  let weather
    (oldContent: 'T)
    (newContent: 'T)
    (probability: float32)
    (seed: int)
    : BoxStyle<'T> =
    fun s ->
      Layout.replaceScatter oldContent newContent probability seed s |> ignore

  /// Stamps a small paint pipeline `count` times at random spots in the box
  /// (seeded) — rock clusters, puddles, rubble. The pipeline receives a
  /// section whose offset is the chosen cell and whose extent runs to the
  /// box's bottom-right corner, so paint relative to the section origin.
  let clumps
    (count: int)
    (seed: int)
    (paint: GridSection2D<'T> -> GridSection2D<'T>)
    : BoxStyle<'T> =
    fun s -> Layout.scatterStamp count seed paint s |> ignore

  /// Scatters `count` cells of generated content over the box (seeded); the
  /// callback receives the cell coordinates (x across the box, y down the
  /// box) and returns the content. The sparse counterpart of `texture` —
  /// prop variety picked per scattered cell.
  let noiseBy
    (count: int)
    (seed: int)
    (generator: int -> int -> 'T)
    : BoxStyle<'T> =
    fun s -> Layout.scatterBy count seed generator s |> ignore

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
        let v = max 0 (int(float32 total * f))
        sizes.[i] <- v
        pctSum <- pctSum + v
      | Weight w -> weightTotal <- weightTotal + max 0f w

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
  /// tracks count toward the intrinsic footprint, `Weight` tracks share the
  /// leftover length of the assigned area, `Percent` tracks take a fraction
  /// of it. `Areas` uses CSS grid-area strings (`"main main side"` spans
  /// columns, `.` is an empty cell). Every place in `Places` must name an
  /// area from the template:
  ///
  /// `grid { Cols = [ Fixed 20; Weight 1f ]; Rows = [ Fixed 6 ]; Gap = 1; Areas = [ "map side" ]; Places = [ "map", dungeon ] }`
  let grid(opts: GridOpts<'T>) : Stamp<'T> =
    let colArr = Array.ofList opts.Cols
    let rowArr = Array.ofList opts.Rows
    let gap = max 0 opts.Gap

    if colArr.Length = 0 || rowArr.Length = 0 then
      invalidArg "Cols" "grid needs at least one column track and one row track"

    let cells =
      opts.Areas
      |> Seq.map(fun line ->
        line.Split(
          [| ' '; '\t' |],
          System.StringSplitOptions.RemoveEmptyEntries
        ))
      |> Seq.toArray

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
          match areas.TryGetValue name with
          | true, struct (c0, r0, cs, rs) ->
            let c1 = c0 + cs
            let r1 = r0 + rs
            let nc0 = min c0 c
            let nc1 = max c1 (c + 1)
            let nr0 = min r0 r
            let nr1 = max r1 (r + 1)
            areas.[name] <- struct (nc0, nr0, nc1 - nc0, nr1 - nr0)
          | false, _ -> areas.[name] <- struct (c, r, 1, 1)

    for (name, stamp) in opts.Places do
      if not(areas.ContainsKey name) then
        invalidArg
          "Places"
          ("area '" + name + "' is not defined in the grid template")

      FlowImpl.checkNoExpand ("Flow.grid area '" + name + "'") "Places" stamp

    let placements = Array.ofList opts.Places

    let wFixed =
      colArr
      |> Array.sumBy (function
        | Fixed v -> max 0 v
        | _ -> 0)

    let hFixed =
      rowArr
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
          let colSizes = resolveTracks assigned.W colArr gap
          let rowSizes = resolveTracks assigned.H rowArr gap
          let colOff = prefixOffsets colSizes gap
          let rowOff = prefixOffsets rowSizes gap

          for (name, stamp) in placements do
            match areas.TryGetValue name with
            | true, struct (c0, r0, cs, rs) ->
              let ax = assigned.X + colOff.[c0]
              let ay = assigned.Y + rowOff.[r0]
              let aw = spanSize colSizes c0 cs gap
              let ah = spanSize rowSizes r0 rs gap
              // A zero footprint dimension stretches over the area (canvas,
              // strips); a fixed dimension keeps its size, anchored to the
              // area start.
              let w = if stamp.W = 0 then aw else min stamp.W aw
              let h = if stamp.H = 0 then ah else min stamp.H ah

              FlowImpl.paintChild
                s
                { X = ax; Y = ay; W = w; H = h }
                registry
                stamp
            | false, _ -> ()
    }

  /// Paints a stamp into a docked rectangle of `section`. The anchor comes
  /// from `flags` (`Dock.Top ||| Dock.CenterX` and so on); `inset` distances
  /// the element from the chosen edges. `StretchX`/`StretchY` span the
  /// section minus twice the inset, and a zero footprint dimension stretches
  /// on its axis without them. Returns `section` for pipeline chaining.
  /// Records no positions; `Flow.docked` is the level-document form that
  /// reports its rectangle.
  let dock
    (flags: Dock)
    (inset: int)
    (stamp: Stamp<'T>)
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    FlowImpl.checkNoExpand "Flow.dock" "stamp" stamp

    let r =
      FlowImpl.dockRect flags inset (FlowImpl.rectOf section) {
        X = 0
        Y = 0
        W = stamp.W
        H = stamp.H
      }

    FlowImpl.paintChild section r ValueNone stamp

    section

  /// A docked element for composition: paints `stamp` into a docked rectangle
  /// of whatever container it mounts into and occupies no flow space (zero
  /// footprint). The anchor comes from `flags` (`Dock.Top ||| Dock.CenterX`
  /// and so on); `inset` distances the element from the chosen edges.
  /// `StretchX`/`StretchY` span the container minus twice the inset, and a
  /// zero footprint dimension of the wrapped stamp stretches on its axis
  /// without them. Pair with `overlay` to place docks over a base layout.
  /// Name the stamp you pass in to report its docked rectangle.
  let docked (flags: Dock) (inset: int) (stamp: Stamp<'T>) : Stamp<'T> =
    FlowImpl.checkNoExpand "Flow.docked" "stamp" stamp

    {
      W = 0
      H = 0
      Expand = 0
      Name = ValueNone
      Tags = []
      Paint =
        fun s registry ->
          let r =
            FlowImpl.dockRect flags inset (FlowImpl.rectOf s) {
              X = 0
              Y = 0
              W = stamp.W
              H = stamp.H
            }

          FlowImpl.paintChild s r registry stamp
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

    docked flags 0 (Stamp.box w h styles)

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
  let tryPosition (name: string) (landmarks: Landmarks) : CellRect voption =
    match landmarks.Named.TryGetValue name with
    | true, r -> ValueSome r
    | false, _ -> ValueNone

  /// Returns every rectangle recorded under a tag, most recent first.
  /// Build-time/occasional queries; the per-cell hot path is `Flow.isTag`.
  let taggedRects (tag: string) (landmarks: Landmarks) : CellRect list =
    match landmarks.Tagged.TryGetValue tag with
    | true, rects -> rects
    | false, _ -> []

  /// The raw per-cell bit grid of `tag` (`x + y * landmarks.Width`), for hot
  /// loops: hoist the lookup out of the loop and read the array per cell.
  /// `ValueNone` when no cell carries the tag. Out of range indices are
  /// never tagged, so bounds checks stay with the caller.
  let tryTagGrid (tag: string) (landmarks: Landmarks) : bool[] voption =
    match landmarks.Cells.TryGetValue tag with
    | true, cells -> ValueSome cells
    | false, _ -> ValueNone

  /// Per-cell walking query: is cell (x, y) covered by an element tagged
  /// `tag` (or marked with it in the tiles via `Flow.scanTiles`)? Out of
  /// range cells are never tagged. One dictionary lookup and one array
  /// read, no allocation; hoist the lookup with `Flow.tryTagGrid` in tight
  /// loops.
  let isTag (tag: string) (x: int) (y: int) (landmarks: Landmarks) : bool =
    if x < 0 || y < 0 || x >= landmarks.Width || y >= landmarks.Height then
      false
    else
      match landmarks.Cells.TryGetValue tag with
      | true, cells -> cells.[x + y * landmarks.Width]
      | false, _ -> false

  /// A non-painting landmark: records `tags` over the element's whole
  /// rectangle. Equivalent to a tagless `Stamp.box`, so it lays out like any
  /// other child (fixed footprint in a grid area, `expand` in a row, ...).
  /// Place it through containers that honor footprints (grid areas,
  /// row/column); inside `overlay`/`group` layers, dock it.
  let region (tags: string list) (w: int) (h: int) : Stamp<'T> =
    Stamp.tagged tags (Stamp.box w h [])
