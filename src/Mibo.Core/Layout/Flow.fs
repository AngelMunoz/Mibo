namespace Mibo.Layout

open System
open System.Collections.Generic

/// Resolved rectangle of a placed element, in grid cells.
[<Struct>]
type CellRect = { X: int; Y: int; W: int; H: int }

/// Named element positions collected during a mount.
type Positions = Dictionary<string, CellRect>

/// Paint signature of a stamp. The registry carries named element positions
/// while a mount runs; ad-hoc paints pass ValueNone.
type StampPaint<'T> = GridSection2D<'T> -> Positions voption -> unit

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
/// wins; `Stretch` wins over the edge flags of its axis.
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
/// footprint. Holds a function value, so it never uses structural equality.
[<Struct; NoEquality; NoComparison>]
type Stamp<'T> = {
  W: int
  H: int
  Expand: int
  Name: string voption
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

/// Result of `Flow.mount`: the resolved rectangle of every named element.
type MountResult = { Positions: Positions }

module internal FlowImpl =

  let inline rectOf(s: GridSection2D<'T>) : CellRect = {
    X = s.OffsetX
    Y = s.OffsetY
    W = s.Width
    H = s.Height
  }

  let inline private overlaps (a: CellRect) (b: CellRect) : bool =
    a.X < b.X + b.W && a.Y < b.Y + b.H && a.X + a.W > b.X && a.Y + a.H > b.Y

  /// Paints a child into `r`, skipping it when it lies fully outside the
  /// parent. The child section keeps the child's own origin, so stamps paint
  /// exactly where they are placed; ops that respect section bounds
  /// (fill/border/checker/...) clamp at the parent's right/bottom edge.
  let paintChild
    (parent: GridSection2D<'T>)
    (r: CellRect)
    (registry: Positions voption)
    (stamp: Stamp<'T>)
    : unit =
    if r.W > 0 && r.H > 0 && overlaps r (rectOf parent) then
      match registry with
      | ValueSome dict ->
        match stamp.Name with
        | ValueSome n -> dict.[n] <- r
        | ValueNone -> ()
      | ValueNone -> ()

      let bounds = rectOf parent
      let w = min r.W (bounds.X + bounds.W - r.X)
      let h = min r.H (bounds.Y + bounds.H - r.Y)

      if w > 0 && h > 0 then
        let child: GridSection2D<'T> = {
          BackingGrid = parent.BackingGrid
          OffsetX = r.X
          OffsetY = r.Y
          Width = w
          Height = h
        }

        stamp.Paint child registry

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

  /// Empty element that paints nothing.
  let empty() : Stamp<'T> = create 0 0 ignore

  /// Names an element so `Flow.mount` reports its resolved rectangle.
  let named (name: string) (stamp: Stamp<'T>) : Stamp<'T> = {
    stamp with
        Name = ValueSome name
  }

  /// Marks an element to share the leftover main-axis space of its container
  /// with weight 1. Expanded elements split the leftover space by weight.
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
    let w = first.W + second.W
    let h = max first.H second.H

    {
      W = w
      H = h
      Expand = 0
      Name = ValueNone
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
    let w = max top.W bottom.W
    let h = top.H + bottom.H

    {
      W = w
      H = h
      Expand = 0
      Name = ValueNone
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

  /// Draws both elements at the same origin, `second` on top. Size is the
  /// larger of the two footprints.
  let overlay (first: Stamp<'T>) (second: Stamp<'T>) : Stamp<'T> =
    let w = max first.W second.W
    let h = max first.H second.H

    {
      W = w
      H = h
      Expand = 0
      Name = ValueNone
      Paint =
        fun s registry ->
          let r = {
            X = s.OffsetX
            Y = s.OffsetY
            W = w
            H = h
          }

          FlowImpl.paintChild s r registry first
          FlowImpl.paintChild s r registry second
    }

  /// Shrinks the paint area of `stamp` by explicit amounts per side.
  let insetEx
    (left: int)
    (top: int)
    (right: int)
    (bottom: int)
    (stamp: Stamp<'T>)
    : Stamp<'T> =
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
  let inset (n: int) (stamp: Stamp<'T>) : Stamp<'T> = insetEx n n n n stamp

  /// Shifts the paint position of `stamp` by a signed offset. The footprint
  /// grows to cover both the original and the shifted area.
  let offset (dx: int) (dy: int) (stamp: Stamp<'T>) : Stamp<'T> =
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
      Paint =
        fun s registry ->
          FlowImpl.paintChild
            s
            {
              X = s.OffsetX - x0
              Y = s.OffsetY - y0
              W = stamp.W
              H = stamp.H
            }
            registry
            stamp
    }

  /// Repeats `stamp` `count` times, side by side with no gap.
  let repeat (count: int) (stamp: Stamp<'T>) : Stamp<'T> =
    if count <= 0 then
      empty()
    else
      let w = stamp.W * count

      {
        W = w
        H = stamp.H
        Expand = 0
        Name = ValueNone
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
                // line spans the whole container.
                let lineLen = if opts.Wrap then lineCross else crossLen

                let extent = Array.sum sizes + gap * (n - 1)

                let startOffset =
                  match opts.Justify with
                  | Center -> max 0 ((mainLen - extent) / 2)
                  | End -> max 0 (mainLen - extent)
                  | _ -> 0

                let mutable mainCursor = startOffset

                for i in 0 .. n - 1 do
                  let c = line.[i]

                  let cSize =
                    match opts.Align with
                    | Stretch -> lineLen
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
  /// of it. `template` uses CSS grid-area strings (`"main main side"` spans
  /// columns, `.` is an empty cell). `fill` places stamps into named areas:
  ///
  /// `grid [ Fixed 20; Weight 1f ] [ Fixed 6 ] 1 [ "map side" ] (fun place -> place "map" dungeon)`
  let grid
    (cols: Track seq)
    (rows: Track seq)
    (gap: int)
    (template: string seq)
    (fill: (string -> Stamp<'T> -> unit) -> unit)
    : Stamp<'T> =
    let colArr = Array.ofSeq cols
    let rowArr = Array.ofSeq rows
    let gap = max 0 gap

    if colArr.Length = 0 || rowArr.Length = 0 then
      invalidArg "cols" "grid needs at least one column track and one row track"

    let cells =
      template
      |> Seq.map(fun line ->
        line.Split(
          [| ' '; '\t' |],
          System.StringSplitOptions.RemoveEmptyEntries
        ))
      |> Seq.toArray

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

    let placements = ResizeArray<string * Stamp<'T>>()

    fill(fun name stamp ->
      if not(areas.ContainsKey name) then
        invalidArg
          "fill"
          ("area '" + name + "' is not defined in the grid template")

      placements.Add(name, stamp))

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
              let x = assigned.X + colOff.[c0]
              let y = assigned.Y + rowOff.[r0]
              let w = spanSize colSizes c0 cs gap
              let h = spanSize rowSizes r0 rs gap

              FlowImpl.paintChild
                s
                { X = x; Y = y; W = w; H = h }
                registry
                stamp
            | false, _ -> ()
    }

  /// Paints a stamp into a docked rectangle of `section`. The anchor comes
  /// from `flags` (`Dock.Top ||| Dock.CenterX` and so on); `inset` distances
  /// the element from the chosen edges. `StretchX`/`StretchY` span the
  /// section minus twice the inset. Returns `section` for pipeline chaining.
  let dock
    (flags: Dock)
    (inset: int)
    (stamp: Stamp<'T>)
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    let r = FlowImpl.rectOf section
    let ins = max 0 inset

    let x =
      if flags &&& Dock.StretchX <> Dock.None then
        ins
      elif flags &&& Dock.Right <> Dock.None then
        r.W - stamp.W - ins
      elif flags &&& Dock.CenterX <> Dock.None then
        (r.W - stamp.W) / 2
      else
        ins

    let y =
      if flags &&& Dock.StretchY <> Dock.None then
        ins
      elif flags &&& Dock.Bottom <> Dock.None then
        r.H - stamp.H - ins
      elif flags &&& Dock.CenterY <> Dock.None then
        (r.H - stamp.H) / 2
      else
        ins

    let w =
      if flags &&& Dock.StretchX <> Dock.None then
        r.W - 2 * ins
      else
        stamp.W

    let h =
      if flags &&& Dock.StretchY <> Dock.None then
        r.H - 2 * ins
      else
        stamp.H

    FlowImpl.paintChild
      section
      {
        X = r.X + x
        Y = r.Y + y
        W = max 0 w
        H = max 0 h
      }
      ValueNone
      stamp

    section

  /// Paints a stamp with its top-left at the section origin and returns the
  /// section, for use in existing `Layout.*` pipelines. Records no positions.
  let paint
    (stamp: Stamp<'T>)
    (section: GridSection2D<'T>)
    : GridSection2D<'T> =
    stamp.Paint section ValueNone
    section

  /// Lays a stamp out inside `section` and reports the resolved rectangle of
  /// every named element it contains.
  let mount (stamp: Stamp<'T>) (section: GridSection2D<'T>) : MountResult =
    let positions = Positions()

    match stamp.Name with
    | ValueSome n -> positions.[n] <- FlowImpl.rectOf section
    | ValueNone -> ()

    stamp.Paint section (ValueSome positions)
    { Positions = positions }

  /// Looks up the resolved rectangle of a named element.
  let tryPosition (name: string) (result: MountResult) : CellRect voption =
    match result.Positions.TryGetValue name with
    | true, r -> ValueSome r
    | false, _ -> ValueNone
