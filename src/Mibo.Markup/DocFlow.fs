namespace Mibo.Markup

/// The Flow backend for the document surface: parses, resolves, and
/// lays a document out through the Flow authoring API
/// (`Mibo.Layout.Flow`), one `Flow.run` per map.
///
///   Stack pack  -> `Flow.overlay` of layers: an `x= y=` child becomes
///                  `Flow.at`, alignment becomes `Dock` flags
///   Flow pack   -> `Flow.grid`: named areas pass through, `col=`/`row=`
///                  and flow children place as explicit slots, `auto`
///                  tracks size from the children's footprints
///   Scatter     -> `Flow.scatter`: the framework's seeded rule places
///                  the sized children
///
/// Build-time only, like every Flow consumer: the Item tree emits to
/// stamps that compose at paint time, so measurement sees the assigned
/// rectangle exactly as the document model describes it.
module DocFlow =

  open System
  open System.Collections.Generic
  open System.Collections.Immutable
  open System.Numerics
  open Mibo.Layout

  // ── geometry between the two vocabularies ────────────────────
  // All inline: thin record shuffles over struct inputs, and the emit
  // walk calls them per child per paint.

  let inline private rectOf(s: GridSection2D<'T>) : CellRect = {
    X = s.OffsetX
    Y = s.OffsetY
    W = s.Width
    H = s.Height
  }

  let inline private sectionOf
    (grid: CellGrid2D<'T>)
    (r: CellRect)
    : GridSection2D<'T> =
    {
      BackingGrid = grid
      OffsetX = r.X
      OffsetY = r.Y
      Width = r.W
      Height = r.H
    }

  let inline private insetRect(n: int, r: CellRect) : CellRect =
    let n = max 0 n

    {
      X = r.X + n
      Y = r.Y + n
      W = max 0 (r.W - 2 * n)
      H = max 0 (r.H - 2 * n)
    }

  let inline private sizeOf(r: CellRect) : CellSize = { W = r.W; H = r.H }

  // ── child reads ──────────────────────────────────────────────

  /// Size: stated first, then the element's extent — which the resolver
  /// already derived from the body's own geometry (`measureOps`, no
  /// allocation), so the scratch-grid measure never runs on the emitter
  /// path. Zero means "stretch this axis".
  let inline private childSize(c: Doc.Item<'T>) : CellSize =
    c.Style.Size
    |> ValueOption.defaultWith(fun () ->
      c.Element.Extent
      |> ValueOption.map id
      |> ValueOption.defaultValue { W = 0; H = 0 })

  let inline private alignH(c: Doc.Item<'T>) : Align =
    c.Style.PlaceH |> ValueOption.defaultValue Start

  let inline private alignV(c: Doc.Item<'T>) : Align =
    c.Style.PlaceV |> ValueOption.defaultValue Start

  let private dockFlagsOf(h: Align, v: Align) : Dock =
    let fx =
      match h with
      | Start -> Dock.Left
      | Center -> Dock.CenterX
      | End -> Dock.Right
      | Stretch -> Dock.StretchX

    let fy =
      match v with
      | Start -> Dock.Top
      | Center -> Dock.CenterY
      | End -> Dock.Bottom
      | Stretch -> Dock.StretchY

    fx ||| fy

  // ── the flow pack: slot assignment over the declared tracks ──

  let private areaSpan
    (areas: string[][], name: string)
    : struct (int * int * int * int) voption =
    let mutable c0 = Int32.MaxValue
    let mutable r0 = Int32.MaxValue
    let mutable c1 = -1
    let mutable r1 = -1

    for r in 0 .. areas.Length - 1 do
      let row = areas[r]

      for c in 0 .. row.Length - 1 do
        if row[c] = name then
          c0 <- min c0 c
          r0 <- min r0 r
          c1 <- max c1 c
          r1 <- max r1 r

    if c1 < 0 then
      ValueNone
    else
      ValueSome struct (c0, r0, c1 - c0 + 1, r1 - r0 + 1)

  /// Slot and area children claim their cells first; flow children take
  /// the next free cell scanning row first. Slot spans below one,
  /// negative col=/row=, a col past the declared cols, and an area child
  /// with a stated span fail the build instead of clamping or dropping.
  /// `who` is the container's name — every failure names it, so a deep
  /// document points at the offending grid.
  let private assignSlots
    (who: string, areas: string[][], colCount: int, children: Doc.Item<'T>[])
    : struct (int * int * int * int)[] =
    let slots = Array.zeroCreate children.Length
    // a claimed flag, not a default-value check: a legitimate
    // `col=0 row=0 colspan=0 rowspan=0` would otherwise read as
    // unclaimed and flip the child into the flow pass
    let claimed = Array.zeroCreate<bool> children.Length
    let occupied = HashSet<struct (int * int)>()
    let mutable maxRow = -1

    for i in 0 .. children.Length - 1 do
      let c = children[i]

      let claim struct (sc, sr, scs, srs) =
        slots[i] <- struct (sc, sr, scs, srs)
        claimed[i] <- true
        maxRow <- max maxRow (sr + srs - 1)

        for dr in 0 .. srs - 1 do
          for dc in 0 .. scs - 1 do
            occupied.Add(struct (sc + dc, sr + dr)) |> ignore

      let cs =
        c.Style.Span
        |> ValueOption.map(fun struct (v, _) -> v)
        |> ValueOption.defaultValue 1

      let rs =
        c.Style.Span
        |> ValueOption.map(fun struct (_, v) -> v)
        |> ValueOption.defaultValue 1

      match c.Style.Area with
      | ValueSome name ->
        // an unknown area name is an emitter-stage failure: resolve does
        // not read the areas template, so the message names the area
        (match areaSpan(areas, name) with
         | ValueSome span ->
           if c.Style.Col.IsSome || c.Style.Row.IsSome then
             failwith
               $"in the '{who}' grid: a child places by area= or by col=/row=, not both ('{name}')"
           elif c.Style.Span.IsSome then
             // an area child fills its whole area; a stated span would
             // be silently discarded
             failwith
               $"in the '{who}' grid: an area child fills its whole area; colspan=/rowspan= needs col= or row="

           claim span
         | ValueNone ->
           failwith
             $"in the '{who}' grid: no area named '{name}' in the declared areas")
      | ValueNone ->
        (match c.Style.Col, c.Style.Row with
         | ValueSome col, ValueSome row ->
           if col < 0 || row < 0 || cs < 1 || rs < 1 then
             failwith
               $"in the '{who}' grid: slot placement wants non-negative col=/row= and spans of at least one (got col {col}, row {row}, colspan {cs}, rowspan {rs})"
           elif col + cs > colCount then
             // the emitter owns this check so the failure names the
             // container; Flow.grid's own throw names only the tracks
             failwith
               $"in the '{who}' grid: col {col} with span {cs} runs past the {colCount} declared cols"

           claim struct (col, row, cs, rs)
         | ValueSome col, ValueNone ->
           if col < 0 || cs < 1 || rs < 1 then
             failwith
               $"in the '{who}' grid: slot placement wants non-negative col= and spans of at least one (got col {col}, colspan {cs}, rowspan {rs})"
           elif col + cs > colCount then
             failwith
               $"in the '{who}' grid: col {col} with span {cs} runs past the {colCount} declared cols"

           claim struct (col, 0, cs, rs)
         | ValueNone, ValueSome row ->
           if row < 0 || cs < 1 || rs < 1 then
             failwith
               $"in the '{who}' grid: slot placement wants non-negative row= and spans of at least one (got row {row}, colspan {cs}, rowspan {rs})"

           claim struct (0, row, cs, rs)
         | ValueNone, ValueNone ->
           // a flow child with a stated span would silently drop it
           if c.Style.Span.IsSome then
             failwith
               $"in the '{who}' grid: a flow child takes the next free cell; colspan=/rowspan= needs col= or row=")

    for i in 0 .. children.Length - 1 do
      if not claimed[i] then
        let mutable placed = false
        let mutable r = 0

        // every claimed and placed cell sits in a row at or above
        // `maxRow`, so the row below it is free: that row bounds the
        // scan, and each placement extends the bound by one row, so flow
        // children stack up instead of failing on a full first row
        while not placed && r <= maxRow + 1 do
          let mutable c = 0

          while not placed && c < colCount do
            if occupied.Contains struct (c, r) then
              c <- c + 1
            else
              occupied.Add struct (c, r) |> ignore
              slots[i] <- struct (c, r, 1, 1)
              maxRow <- max maxRow r
              placed <- true

          r <- r + 1

        if not placed then
          failwith
            $"in the '{who}' grid: the child '{children[i].Name}' has no free flow cell"

    slots

  /// One flow-pack child as a grid place: a plain stamp when the
  /// alignment is the grid default (start, or a stretching axis),
  /// otherwise a layer that docks the stamp by alignment inside the
  /// assigned tracks.
  let private areaPlace
    (h: Align, v: Align, size: CellSize, stamp: Stamp<'T>)
    : Stamp<'T> =
    let plain = (h = Start || size.W = 0) && (v = Start || size.H = 0)

    if plain then
      stamp
    else
      Flow.docked {
        Anchor = dockFlagsOf(h, v)
        Inset = InsetSpec.Zero
        Stamp = stamp
      }

  // ── the emit walk ────────────────────────────────────────────

  // One recursive function: the pack builders are locals of `emit` and
  // the only recursion is `emit` itself, called once per child inside
  // plain loops. Depth equals the document's nesting depth — a handful of
  // frames for real documents — and each call builds a stamp bottom-up,
  // so the walk is a fold over the item tree.

  /// Emits one item as a Flow stamp: its own body paints its box, then its
  /// children paint by the container's pack (stack layers, a grid, or
  /// seeded scatter). The composite is built inside the stamp's paint, so
  /// an emitted document painted onto a second grid lays out again.
  let rec emit(item: Doc.Item<'T>) : Stamp<'T> =
    // One stack child as a layer: an exact-At child mounts at its
    // origin (a zero axis stretching to the inner far edge), a placed
    // child mounts as a docked layer anchored by its alignment.
    let stackLayer(inner: CellRect, c: Doc.Item<'T>) : Stamp<'T> =
      let stamp = emit c
      let size = childSize c

      match c.Style.At with
      | ValueSome at ->
        let w =
          if size.W = 0 then
            max 0 (inner.W - at.X)
          else
            min size.W inner.W

        let h =
          if size.H = 0 then
            max 0 (inner.H - at.Y)
          else
            min size.H inner.H

        Flow.at at.X at.Y { stamp with W = w; H = h }
      | ValueNone ->
        let w = if size.W = 0 then 0 else min size.W inner.W
        let h = if size.H = 0 then 0 else min size.H inner.H

        Flow.docked {
          Anchor = dockFlagsOf(alignH c, alignV c)
          Inset = InsetSpec.Zero
          Stamp = { stamp with W = w; H = h }
        }

    // Scatter keeps the framework's seeded rule: sized children place
    // at non-overlapping origins over the assigned area.
    let scatterLayers
      (seed: int, inner: CellRect, children: Doc.Item<'T>[])
      : Stamp<'T> =
      let scattered = Array.zeroCreate<Stamp<'T>> children.Length

      for i in 0 .. children.Length - 1 do
        let size = childSize children[i]

        scattered[i] <- {
          emit children[i] with
              W = max 1 size.W
              H = max 1 size.H
        }

      Flow.scatter seed scattered

    // The flow pack rides `Flow.grid`: named areas pass through, slot
    // and flow children place as explicit slots, and `auto` tracks size
    // from the children's footprints inside the grid itself.
    let flowGrid(gap: CellSize, inner: CellRect) : Stamp<'T> =
      if gap.W <> gap.H then
        failwith
          $"the '{item.Name}' grid takes one gap for both axes; gapx= and gapy= differ"

      let children = item.Children
      let cols = if item.Cols.Length = 0 then [| Weight 1f |] else item.Cols
      let colCount = cols.Length
      let slots = assignSlots(item.Name, item.Areas, colCount, children)
      let sizes = children |> Array.map childSize

      let mutable rowCount = item.Rows.Length

      for struct (_, r, _, rs) in slots do
        rowCount <- max rowCount (r + rs)

      rowCount <- max rowCount item.Areas.Length

      let rows =
        if item.Rows.Length = 0 then
          Array.create (max 1 rowCount) (Weight 1f)
        elif rowCount > item.Rows.Length then
          Array.append
            item.Rows
            (Array.create (rowCount - item.Rows.Length) Auto)
        else
          item.Rows

      // named areas from the document become template rows; slot
      // children need no template names — `Place.Slot` addresses the
      // tracks directly
      let places = Array.zeroCreate<struct (Place * Stamp<'T>)> children.Length

      for i in 0 .. children.Length - 1 do
        let c = children[i]
        let size = sizes[i]

        let place =
          match c.Style.Area with
          | ValueSome name -> Place.Area name
          | ValueNone ->
            let struct (c0, r0, cs, rs) = slots[i]
            Place.Slot(c0, r0, cs, rs)

        places[i] <-
          struct (place,
                  areaPlace(
                    alignH c,
                    alignV c,
                    size,
                    { emit c with W = size.W; H = size.H }
                  ))

      Flow.grid {
        Cols = cols
        Rows = rows
        Gap = max 0 gap.W
        Areas = item.Areas |> Array.map(String.concat " ")
        Places = places
      }

    let fp =
      item.Style.Size
      |> ValueOption.orElse item.Element.Extent
      |> ValueOption.defaultValue { W = 0; H = 0 }

    {
      W = fp.W
      H = fp.H
      Expand = 0
      Name = ValueNone
      Tags = []
      Paint =
        fun s _ ->
          // the body first, the children after, per node
          item.Element.Paint(sectionOf s.BackingGrid (rectOf s))

          if item.Children.Length > 0 then
            let pad =
              item.Style.Pad
              |> ValueOption.map(max 0)
              |> ValueOption.defaultValue 0

            let inner = insetRect(pad, rectOf s)

            // declared tracks imply flow packing
            let impliedPack =
              if
                item.Cols.Length > 0
                || item.Rows.Length > 0
                || item.Areas.Length > 0
              then
                Doc.Flow
              else
                Doc.Stack

            let pack = item.Style.Pack |> ValueOption.defaultValue impliedPack

            // x=/y= exact placement is a stack-pack channel; a document
            // that mixes it into flow or scatter fails the build rather
            // than silently dropping the offsets
            if pack <> Doc.Stack then
              for c in item.Children do
                if c.Style.At.IsSome then
                  failwith
                    $"in the '{item.Name}' container: the child '{c.Name}' uses x= and y= exact placement, which works in the stack pack; a flow or scatter child places by area, slot, or the pack rule"

            let gap =
              item.Style.Gap |> ValueOption.defaultValue { W = 0; H = 0 }

            let seed = item.Style.Seed |> ValueOption.defaultValue 0

            // the composite is built per paint call: a stamp paints once
            // per build, so the slot assignment and grid construction
            // run once with it — re-painting one emitted document on a
            // second grid re-runs them all
            let composite =
              match pack with
              | Doc.Stack ->
                // build the layer array in a loop, not a map chain
                let layers = Array.zeroCreate<Stamp<'T>> item.Children.Length

                for i in 0 .. item.Children.Length - 1 do
                  layers[i] <- stackLayer(inner, item.Children[i])

                Flow.overlay layers
              | Doc.Scatter -> scatterLayers(seed, inner, item.Children)
              | Doc.Flow -> flowGrid(gap, inner)

            Flow.paint composite (sectionOf s.BackingGrid inner) |> ignore
    }

  // ── the map shell ────────────────────────────────────────────

  let private buildFrom
    (parse: string -> Result<ImmutableArray<Node>, string>)
    (surface: Doc.Surface<'T>)
    (src: string)
    : Result<CellGrid2D<'T>, string> =
    try
      parse src
      |> Result.bind(fun roots ->
        // resolve already guarantees one map container and one root item
        // (dimsOf runs inside it), so findMapNode here only locates the
        // node whose dimensions the grid needs
        Doc.resolve surface src roots
        |> Result.bind(fun items ->
          match items with
          | [| root |] ->
            (match Doc.findMapNode roots with
             | ValueSome n ->
               (match Doc.dimsOf(src, n) with
                | Error e -> Error e
                | Ok spec ->
                  let grid =
                    CellGrid2D.create
                      spec.W
                      spec.H
                      (Vector2(1f, 1f))
                      Vector2.Zero

                  let struct (built, _) = grid |> Flow.run(emit root)

                  Ok built)
             // resolve fails the build when no map node exists, so this
             // arm cannot run; it is a tripwire, not a failure mode
             | ValueNone ->
               failwith "unreachable: resolve guarantees a map node")
          | many ->
            failwith
              $"unreachable: resolve returns exactly one root item (got {many.Length})"))
    with e ->
      Error e.Message

  /// Parses (KDL or XML — the same document in either syntax builds the
  /// same grid, pinned by test), resolves, and lays the document out
  /// through the Flow API. Parse and resolution failures carry their
  /// document positions when the front-end tracks them; emitter-stage
  /// failures — a gap mismatch, a bad slot, an unknown area, a mixed
  /// pack channel — name the container, and the child when one is at
  /// fault. Error builds nothing. The build returns the painted grid;
  /// named and tagged landmarks stay a scope cut (derive gameplay
  /// regions from the tiles, the way `Flow.build` does).
  let build
    (surface: Doc.Surface<'T>, src: string)
    : Result<CellGrid2D<'T>, string> =
    buildFrom Kdl.parse surface src

  /// `build` with the XML front-end: attributes carry the scalars, so
  /// the document reads the way XML means it.
  let buildXml
    (surface: Doc.Surface<'T>, src: string)
    : Result<CellGrid2D<'T>, string> =
    buildFrom Xml.parse surface src
