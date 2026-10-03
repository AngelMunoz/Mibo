namespace Mibo.Markup

/// The Flow backend for the document surface: parses, resolves, and
/// lays a document out through the Flow authoring API
/// (`Mibo.Layout.Flow`) — one `Flow.run` per layer; `build` paints
/// through the no-registry path and reports no structure.
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

  /// How many cells a track holds. `Fixed` states it; every other track kind
  /// is sized when the layout runs, so the even share of the container is the
  /// closest a slot assignment can get before that.
  let private trackCells (track: Track) (share: int) =
    match track with
    | Fixed n -> max 1 n
    | _ -> max 1 share

  /// How many tracks a child of `footprint` cells covers from track `from`,
  /// or 0 when the tracks run out before the footprint fits. A zero or
  /// negative footprint covers one track: the child stretches over it.
  let private tracksFor
    (cellsAt: int -> int)
    (limit: int)
    (footprint: int)
    (from: int)
    : int =
    if footprint <= 0 then
      1
    else
      let mutable taken = 0
      let mutable count = 0
      let mutable i = from

      while taken < footprint && i < limit do
        taken <- taken + cellsAt i
        count <- count + 1
        i <- i + 1

      if taken < footprint then 0 else count

  /// Slot and area children claim their cells first; flow children take
  /// the next free cell scanning row first, claiming as many tracks as the
  /// child's own size needs. Slot spans below one, negative col=/row=, a col
  /// past the declared cols, and an area child with a stated span fail the
  /// build instead of clamping or dropping. `who` is the container's name —
  /// every failure names it, so a deep document points at the offending grid.
  let private assignSlots
    (
      who: string,
      areas: string[][],
      colCount: int,
      children: Doc.Item<'T>[],
      colCellAt: int -> int,
      rowCellAt: int -> int
    ) : struct (int * int * int * int)[] =
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

    // a flow child that covers several cells claims the tracks its own size
    // needs, so a two-cell piece flows into two tracks of one cell each
    // instead of landing in one track and painting over its neighbour
    for i in 0 .. children.Length - 1 do
      if not claimed[i] then
        let size = childSize children[i]

        if
          tracksFor colCellAt colCount size.W 0 = 0
          || tracksFor rowCellAt Int32.MaxValue size.H 0 = 0
        then
          failwith
            $"in the '{who}' grid: the child '{children[i].Name}' covers {max 1 size.W} by {max 1 size.H} cells, which the declared tracks cannot hold"

        let mutable placed = false
        let mutable r = 0

        // every claimed and placed cell sits in a row at or above
        // `maxRow`, so the row below it is free: that row bounds the
        // scan, and each placement extends the bound by one row, so flow
        // children stack up instead of failing on a full first row
        while not placed && r <= maxRow + 1 do
          let mutable c = 0

          while not placed && c < colCount do
            let cs = tracksFor colCellAt colCount size.W c
            let rs = tracksFor rowCellAt Int32.MaxValue size.H r

            if cs = 0 then
              // no room left in this row for the piece: wrap to the next
              c <- colCount
            elif
              // every cell of the block the child covers is free
              seq {
                for dr in 0 .. rs - 1 do
                  for dc in 0 .. cs - 1 do
                    yield struct (c + dc, r + dr)
              }
              |> Seq.forall(fun cell -> not(occupied.Contains cell))
            then
              for dr in 0 .. rs - 1 do
                for dc in 0 .. cs - 1 do
                  occupied.Add(struct (c + dc, r + dr)) |> ignore

              slots[i] <- struct (c, r, cs, rs)
              maxRow <- max maxRow (r + rs - 1)
              placed <- true
            else
              c <- c + 1

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

  // One recursive function: the pack builders are locals of `emitTags` and
  // the only recursion is `emitTags` itself, called once per child inside
  // plain loops. Depth equals the document's nesting depth — a handful of
  // frames for real documents — and each call builds a stamp bottom-up,
  // so the walk is a fold over the item tree.

  // The one emit walk; `reportTags` gates the tag channel. `build` keeps
  // no landmarks, so it emits with the gate off and allocates no tag
  // lists; every other caller reports under the element's name.
  let rec emitTags (reportTags: bool) (item: Doc.Item<'T>) : Stamp<'T> =
    // One stack child: an exact-At child mounts at its origin (a zero
    // axis stretching to the inner far edge), a placed child mounts as a
    // docked child anchored by its alignment.
    let stackChild(inner: CellRect, c: Doc.Item<'T>) : Stamp<'T> =
      let stamp = emitTags reportTags c
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
    let scatterChildren
      (seed: int, inner: CellRect, children: Doc.Item<'T>[])
      : Stamp<'T> =
      let scattered = Array.zeroCreate<Stamp<'T>> children.Length

      for i in 0 .. children.Length - 1 do
        let size = childSize children[i]

        scattered[i] <- {
          emitTags reportTags children[i] with
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
      let colShare = max 1 (inner.W / max 1 colCount)

      // a declared row states its own height; an undeclared one is created
      // by the flow pass and shares what is left, so one cell is the
      // estimate a slot assignment starts from
      let rowCellsAt i =
        if i < item.Rows.Length then
          trackCells item.Rows[i] (max 1 (inner.H / max 1 item.Rows.Length))
        else
          1

      let slots =
        assignSlots(
          item.Name,
          item.Areas,
          colCount,
          children,
          (fun i -> trackCells cols[i] colShare),
          rowCellsAt
        )

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
                    {
                      emitTags reportTags c with
                          W = size.W
                          H = size.H
                    }
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
      //
      // The channel is gated: `build` emits with the gate off and paints
      // through the no-registry path, so a build allocates no tag lists
      // and no per-cell grids.
      Tags =
        if not reportTags || item.Name = "map" || item.Layer.IsSome then
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
                let childStamps =
                  Array.zeroCreate<Stamp<'T>> item.Children.Length

                for i in 0 .. item.Children.Length - 1 do
                  childStamps[i] <- stackChild(inner, item.Children[i])

                Flow.overlay childStamps
              | Doc.Scatter -> scatterChildren(seed, inner, item.Children)
              | Doc.Flow -> flowGrid(gap, inner)

            // The composite paints with THIS call's registry. Painting
            // through `Flow.paint` would drop it: that helper is the
            // ad-hoc path and records no positions by design, so every
            // landmark a child raises would vanish between the root's
            // record and the child's paint.
            composite.Paint (sectionOf s.BackingGrid inner) registry
    }

  /// Emits one item as a Flow stamp: its own body paints its box, then its
  /// children paint by the container's pack (stack children, a grid, or
  /// seeded scatter). The composite is built inside the stamp's paint, so
  /// an emitted document painted onto a second grid lays out again.
  /// Every element reports its resolved rectangle under its own name
  /// through the tag channel, so a document painted with a landmarks
  /// registry reports its structure.
  let inline emit(item: Doc.Item<'T>) : Stamp<'T> = emitTags true item

  // ── the map shell ────────────────────────────────────────────

  // The gate lives here too: `build` calls this with the tag channel
  // off, and the public entry below always reports.
  let emitLayersTags
    (reportTags: bool)
    (root: Doc.Item<'T>)
    : struct (string * Stamp<'T>)[] =
    let stated = root.Children |> Array.filter(fun c -> c.Layer.IsSome)

    if stated.Length = 0 then
      [| struct ("main", emitTags reportTags root) |]
    else
      let plain = root.Children |> Array.filter(fun c -> c.Layer.IsNone)

      let main =
        if plain.Length > 0 || root.Element.Body.Length > 0 then
          [|
            struct ("main", emitTags reportTags { root with Children = plain })
          |]
        else
          [||]

      let layers = Array.zeroCreate stated.Length

      for i in 0 .. stated.Length - 1 do
        // a layer always carries its name: the resolver stamps it
        let name = stated[i].Layer |> ValueOption.defaultValue "layer"

        // the stretch is load-bearing: a layer whose statements are all
        // area-bound measures a partial extent, and a plain stack child
        // would dock at its top-left corner instead of spanning the grid
        layers[i] <- struct (name, Flow.stretch(emitTags reportTags stated[i]))

      Array.append main layers

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
    emitLayersTags true root

  /// One built layer of a document: its name, its grid, the landmarks that
  /// grid reported, and the occupancy that answers which instance owns each
  /// cell.
  type BuiltLayer<'T> = {
    Name: string
    Grid: CellGrid2D<'T>
    Landmarks: Landmarks
    Occupancy: Occupancy
  }

  /// The projection a build scans with: the surface's own, or the identity
  /// when the surface states none.
  let private spanProjection(surface: Doc.Surface<'T>) : 'T -> InstanceSpan =
    match surface.Span with
    | ValueSome read -> read
    | ValueNone -> fun _ -> One

  /// Does one painted cell cover more than one cell? The identity forms read
  /// as `false`, so a map with no spans pays one comparison per cell.
  let private spansCells (surface: Doc.Surface<'T>) (cell: 'T) : bool =
    surface.Span
    |> ValueOption.map(fun read -> not(Occupancy.isIdentity(read cell)))
    |> ValueOption.defaultValue false

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
      |> Result.bind(fun struct (root, dims) ->
        let emitted = emitLayers root

        let stamps = emitted |> Array.map(fun struct (_, stamp) -> stamp)

        // one grid per layer, every grid the size the document states:
        // `runLayers` rejects a mismatch instead of silently clipping one
        // layer to another's box
        let grids = Array.init stamps.Length (fun _ -> gridOf dims)

        let painted = Flow.runLayers stamps grids

        // every layer carries its occupancy: one instance per anchor, and the
        // answer to which instance owns a cell. A layer that breaks a span
        // rule fails the whole build with its own name in front of the reason.
        let spanOf = spanProjection surface
        let built = Array.zeroCreate painted.Length
        let mutable failure = ValueNone
        let mutable i = 0

        while failure.IsNone && i < painted.Length do
          let struct (name, _) = emitted[i]
          let struct (grid, landmarks) = painted[i]

          (match Occupancy.scan spanOf grid with
           | Error reason -> failure <- ValueSome $"layer '{name}': {reason}"
           | Ok occupancy ->
             built[i] <- {
               Name = name
               Grid = grid
               Landmarks = landmarks
               Occupancy = occupancy
             })

          i <- i + 1

        match failure with
        | ValueSome reason -> Error reason
        | ValueNone -> Ok built)
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
        // the gate is off, and the paint goes through the no-registry
        // path: nothing records, so the build allocates no landmark
        // memory — not even an empty registry
        match emitLayersTags false root with
        | [| struct (_, stamp) |] ->
          let grid = gridOf dims

          let full = {
            X = 0
            Y = 0
            W = grid.Width
            H = grid.Height
          }

          Flow.paint stamp (sectionOf grid full) |> ignore

          // a single grid cannot report the occupancy a spanning instance
          // needs, so the build refuses the document instead of drawing one
          // cell of a plate
          let mutable spanned = false
          let mutable y = 0

          while not spanned && y < grid.Height do
            let mutable x = 0

            while not spanned && x < grid.Width do
              (match CellGrid2D.get x y grid with
               | ValueSome cell -> spanned <- spansCells surface cell
               | ValueNone -> ())

              x <- x + 1

            y <- y + 1

          if spanned then
            Error
              "the document places a spanning word, so the map needs its occupancy: DocFlow.buildLayers builds it"
          else
            Ok grid
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
  /// The build returns the painted grid and records no landmarks: the
  /// emit runs with the tag channel off, and the paint goes through the
  /// no-registry path, so a build allocates no landmark memory. A caller
  /// that needs the structure — a hover that names the region under the
  /// cursor, a walkability walk over a tagged area — runs the same four
  /// steps itself and keeps what `Flow.run` hands back:
  ///
  ///   `parse src |> Result.bind (Doc.resolve surface src)
  ///    |> Result.bind (fun items -> Doc.findMapNode roots ...)
  ///    |> Result.map (fun dims -> grid |> Flow.run (emit root))`
  ///
  /// Every element of the document reports its resolved rectangle under
  /// its own name through `Landmarks.Tagged`, plus a per-cell grid for
  /// `Flow.isTag`. That registry allocates one grid per name, so a very
  /// large document with very many elements pays for each one: build-time
  /// memory, released with the build — memory `build` never spends.
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
