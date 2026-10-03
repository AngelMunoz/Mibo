namespace Mibo.Markup

/// The Flow backend for the document surface: parses, resolves, and
/// lays a document out through the Flow authoring API
/// (`Mibo.Layout.Flow`), one `Flow.run` per layer.
///
///   Stack pack  -> `Flow.overlay` of stack children: an `x= y=` child
///                  becomes `Flow.at`, alignment becomes `Dock` flags
///   Flow pack   -> `Flow.grid`: named areas pass through, `col=`/`row=`
///                  and flow children place as explicit slots, `auto`
///                  tracks size from the children's footprints
///   Scatter     -> `Flow.scatter`: the framework's seeded rule places
///                  the sized children
///   Layers      -> `Flow.runLayers`: one stamp per layer, one grid per
///                  layer, bottom first
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
  /// otherwise a child that docks the stamp by alignment inside the
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
  /// children paint by the container's pack (stack children, a grid, or
  /// seeded scatter). The composite is built inside the stamp's paint, so
  /// an emitted document painted onto a second grid lays out again.
  let rec emit(item: Doc.Item<'T>) : Stamp<'T> =
    // One stack child: an exact-At child mounts at its origin (a zero
    // axis stretching to the inner far edge), a placed child mounts as a
    // docked child anchored by its alignment.
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
      // The element's name rides the TAG channel, never `Name`. A
      // document may use one element name many times, and the named
      // channel rejects a duplicate; tags are additive, so every use
      // reports its own resolved rectangle and its own per-cell bit
      // grid. The anonymous `plot` container reports under its own
      // word for the same reason.
      //
      // Two nodes report nothing, because their rectangle is the whole
      // grid and a caller already knows it: the `map` root, and a
      // `layer` container — which is stretched over the map, so a
      // region query would answer "the whole map" for every cell no
      // element covers, and a hover would outline the map itself. The
      // layer still reports under its name through `BuiltLayer.Name`,
      // and its elements report as usual.
      Tags =
        if item.Name = "map" || item.Layer.IsSome then
          []
        else
          [ item.Name ]
      Paint =
        fun s registry ->
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
                // build the child array in a loop, not a map chain
                let layers = Array.zeroCreate<Stamp<'T>> item.Children.Length

                for i in 0 .. item.Children.Length - 1 do
                  layers[i] <- stackLayer(inner, item.Children[i])

                Flow.overlay layers
              | Doc.Scatter -> scatterLayers(seed, inner, item.Children)
              | Doc.Flow -> flowGrid(gap, inner)

            // The composite paints with THIS call's registry. Painting
            // through `Flow.paint` would drop it: that helper is the
            // ad-hoc path and records no positions by design, so every
            // landmark a child raises would vanish between the root's
            // record and the child's paint.
            composite.Paint (sectionOf s.BackingGrid inner) registry
    }

  // ── the map shell ────────────────────────────────────────────

  /// The map's layers, bottom first: `main` — the map's own body and its
  /// non-layer children — when it has any content, then each stated layer
  /// in document order: `layer ground { ... }` in KDL,
  /// `<layer name="ground">` in XML. A layer's stamp is `Flow.stretch`
  /// over the layer's container, so it spans the whole grid whatever its
  /// statements cover; a plain stack child would dock at the layer's
  /// measured footprint instead. A document without layer nodes yields
  /// exactly one entry, `("main", the emitted root)`, which is the single
  /// grid `build` has always returned.
  ///
  /// `main` needs the map to paint something of its own: a bare statement
  /// in the map body, or a non-layer child. A map that holds only layer
  /// containers has no `main`.
  let emitLayers(root: Doc.Item<'T>) : struct (string * Stamp<'T>)[] =
    let stated = root.Children |> Array.filter(fun c -> c.Layer.IsSome)

    if stated.Length = 0 then
      [| struct ("main", emit root) |]
    else
      let plain = root.Children |> Array.filter(fun c -> c.Layer.IsNone)

      let main =
        if plain.Length > 0 || root.Element.Body.Length > 0 then
          [| struct ("main", emit { root with Children = plain }) |]
        else
          [||]

      let layers = Array.zeroCreate stated.Length

      for i in 0 .. stated.Length - 1 do
        // a layer always carries its name: the resolver stamps it
        let name = stated[i].Layer |> ValueOption.defaultValue "layer"

        // the stretch is load-bearing: a layer whose statements are all
        // area-bound measures a partial extent, and a plain stack child
        // would dock at its top-left corner instead of spanning the grid
        layers[i] <- struct (name, Flow.stretch(emit stated[i]))

      Array.append main layers

  /// One built layer of a document: its name, its grid, and the
  /// landmarks that grid reported.
  type BuiltLayer<'T> = {
    Name: string
    Grid: CellGrid2D<'T>
    Landmarks: Landmarks
  }

  /// The scratch grid a `buildLayers` call paints one layer into: cell
  /// content at one pixel per cell. A caller that needs another cell size,
  /// or the landmarks of its own grids, emits the layers itself.
  let private gridOf(spec: CellSize) : CellGrid2D<'T> =
    CellGrid2D.create spec.W spec.H (Vector2(1f, 1f)) Vector2.Zero

  /// Parses, resolves, and locates the map's dimensions — the steps both
  /// build entry points run before they paint anything. Every step already
  /// returns a `Result` or a `ValueOption`, so the pipeline binds rather
  /// than nests. Parse and resolution failures carry their document
  /// positions when the front-end tracks them.
  ///
  /// Resolution runs first, so a document that holds no map node, or a bad
  /// name beside one, reports what the resolver found. The two error arms
  /// after it are unreachable, because `resolve` fails the build when the
  /// document holds no map node or more than one root item; they are
  /// tripwires against a resolver regression, not failure modes.
  let private resolvedDoc
    (parse: string -> Result<ImmutableArray<Node>, string>)
    (surface: Doc.Surface<'T>)
    (src: string)
    : Result<struct (Doc.Item<'T> * CellSize), string> =
    parse src
    |> Result.bind(fun roots ->
      Doc.resolve surface src roots
      |> Result.bind(fun items ->
        Doc.findMapNode roots
        |> ValueOption.map(fun node ->
          Doc.dimsOf(src, node) |> Result.map(fun dims -> items, dims))
        |> ValueOption.defaultValue(
          Error "unreachable: resolve guarantees a map node"
        )))
    |> Result.bind(fun (items, dims) ->
      match items with
      | [| root |] -> Ok struct (root, dims)
      | many ->
        Error
          $"unreachable: resolve returns exactly one root item (got {many.Length})")

  let private buildLayersFrom
    (parse: string -> Result<ImmutableArray<Node>, string>)
    (surface: Doc.Surface<'T>)
    (src: string)
    : Result<BuiltLayer<'T>[], string> =
    try
      resolvedDoc parse surface src
      |> Result.map(fun struct (root, dims) ->
        let emitted = emitLayers root

        let stamps = emitted |> Array.map(fun struct (_, stamp) -> stamp)

        // one grid per layer, every grid the size the document states:
        // `runLayers` rejects a mismatch instead of silently clipping one
        // layer to another's box
        let grids = Array.init stamps.Length (fun _ -> gridOf<'T> dims)

        let painted = Flow.runLayers stamps grids

        Array.init painted.Length (fun i ->
          let struct (name, _) = emitted[i]
          let struct (grid, landmarks) = painted[i]

          {
            Name = name
            Grid = grid
            Landmarks = landmarks
          }))
    with e ->
      Error e.Message

  /// Parses, resolves, emits, and paints every layer of the document: one
  /// grid per layer, in layer order, each built the way `build` builds
  /// today (one cell per tile). A document without `layer` containers
  /// returns one layer named `main`.
  ///
  /// The syntax selects the parser, so this is the KDL entry point;
  /// `buildLayersXml` takes the same document in XML.
  let buildLayers
    (surface: Doc.Surface<'T>, src: string)
    : Result<BuiltLayer<'T>[], string> =
    buildLayersFrom Kdl.parse surface src

  /// `buildLayers` with the XML front-end.
  let buildLayersXml
    (surface: Doc.Surface<'T>, src: string)
    : Result<BuiltLayer<'T>[], string> =
    buildLayersFrom Xml.parse surface src

  let private buildFrom
    (parse: string -> Result<ImmutableArray<Node>, string>)
    (surface: Doc.Surface<'T>)
    (src: string)
    : Result<CellGrid2D<'T>, string> =
    try
      resolvedDoc parse surface src
      |> Result.bind(fun struct (root, dims) ->
        match emitLayers root with
        | [| struct (_, stamp) |] ->
          let struct (built, _) = gridOf<'T> dims |> Flow.run stamp
          Ok built
        | layers ->
          // a multi-layer document is one grid per layer, and a single
          // grid would silently paint only the first: the caller picks
          let names =
            layers
            |> Array.map(fun struct (name, _) -> $"'{name}'")
            |> String.concat ", "

          Error
            $"the document holds {layers.Length} layers ({names}); DocFlow.buildLayers builds every layer")
    with e ->
      Error e.Message

  /// Parses (KDL or XML — the same document in either syntax builds the
  /// same grid, pinned by test), resolves, and lays the document out
  /// through the Flow API. Parse and resolution failures carry their
  /// document positions when the front-end tracks them; emitter-stage
  /// failures — a gap mismatch, a bad slot, an unknown area, a mixed
  /// pack channel — name the container, and the child when one is at
  /// fault. Error builds nothing.
  ///
  /// The build returns the painted grid and drops the landmarks. A caller
  /// that needs them — a hover that names the region under the cursor, a
  /// walkability walk over a tagged area — runs the same four steps
  /// itself and keeps what `Flow.run` hands back:
  ///
  ///   `parse src |> Result.bind (Doc.resolve surface src)
  ///    |> Result.bind (fun items -> Doc.findMapNode roots ...)
  ///    |> Result.map (fun dims -> grid |> Flow.run (emit root))`
  ///
  /// Every element of the document reports its resolved rectangle under
  /// its own name through `Landmarks.Tagged`, plus a per-cell bit grid
  /// for `Flow.isTag`. The registry allocates one bit grid per name, so
  /// a very large document with very many elements pays for each one:
  /// build-time memory, released with the build.
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
