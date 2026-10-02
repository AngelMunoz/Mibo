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

  let private rectOf(s: GridSection2D<'T>) : CellRect = {
    X = s.OffsetX
    Y = s.OffsetY
    W = s.Width
    H = s.Height
  }

  let private sectionOf(grid: CellGrid2D<'T>, r: CellRect) : GridSection2D<'T> = {
    BackingGrid = grid
    OffsetX = r.X
    OffsetY = r.Y
    Width = r.W
    Height = r.H
  }

  let private insetRect(n: int, r: CellRect) : CellRect =
    let n = max 0 n

    {
      X = r.X + n
      Y = r.Y + n
      W = max 0 (r.W - 2 * n)
      H = max 0 (r.H - 2 * n)
    }

  let private sizeOf(r: CellRect) : CellSize = { W = r.W; H = r.H }

  // ── child reads ──────────────────────────────────────────────

  let private childSize(inner: CellSize, c: Doc.Item<'T>) : CellSize =
    let declaredOrMeasured =
      c.Element.Extent
      |> ValueOption.defaultWith(fun () ->
        let m = Doc.measure(c.Element, inner)

        if m = inner then { W = 0; H = 0 } else m)

    c.Style.Size |> ValueOption.defaultValue declaredOrMeasured

  let private alignH(c: Doc.Item<'T>) : Align =
    c.Style.PlaceH |> ValueOption.defaultValue Start

  let private alignV(c: Doc.Item<'T>) : Align =
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
  /// the next free cell scanning row first.
  let private assignSlots
    (areas: string[][], colCount: int, children: Doc.Item<'T>[])
    : struct (int * int * int * int)[] =
    let slots = Array.zeroCreate children.Length
    let occupied = HashSet<struct (int * int)>()

    for i in 0 .. children.Length - 1 do
      let c = children[i]

      let claim struct (sc, sr, scs, srs) =
        slots[i] <- struct (sc, sr, scs, srs)

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
        (match areaSpan(areas, name) with
         | ValueSome span -> claim span
         | ValueNone ->
           failwith
             $"no area named '{name}' (the resolver must have caught this)")
      | ValueNone ->
        (match c.Style.Col, c.Style.Row with
         | ValueSome col, ValueSome row ->
           claim struct (max 0 col, max 0 row, cs, rs)
         | ValueSome col, ValueNone -> claim struct (max 0 col, 0, cs, rs)
         | ValueNone, ValueSome row -> claim struct (0, max 0 row, cs, rs)
         | ValueNone, ValueNone -> ())

    for i in 0 .. children.Length - 1 do
      if slots[i] = Unchecked.defaultof<_> then
        let mutable placed = false
        let mutable r = 0

        while not placed && r <= children.Length do
          let mutable c = 0

          while not placed && c < colCount do
            if occupied.Contains struct (c, r) then
              c <- c + 1
            else
              occupied.Add struct (c, r) |> ignore
              slots[i] <- struct (c, r, 1, 1)
              placed <- true

          r <- r + 1

        if not placed then
          failwith $"child {i + 1} has no free flow cell"

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

  let rec emit(item: Doc.Item<'T>) : Stamp<'T> =
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
          item.Element.Paint(sectionOf(s.BackingGrid, rectOf s))

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

            let gap =
              item.Style.Gap |> ValueOption.defaultValue { W = 0; H = 0 }

            let seed = item.Style.Seed |> ValueOption.defaultValue 0

            let composite =
              match pack with
              | Doc.Stack ->
                Flow.overlay(
                  item.Children |> Seq.map(fun c -> stackLayer(inner, c))
                )
              | Doc.Scatter -> scatterLayers(seed, inner, item.Children)
              | Doc.Flow -> flowGrid(gap, inner, item)

            Flow.paint composite (sectionOf(s.BackingGrid, inner)) |> ignore
    }

  /// One stack child as a layer: an exact-At child mounts at its origin
  /// (a zero axis stretching to the inner far edge), a placed child
  /// mounts as a docked layer anchored by its alignment.
  and stackLayer(inner: CellRect, c: Doc.Item<'T>) : Stamp<'T> =
    let stamp = emit c
    let size = childSize(sizeOf inner, c)

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

  /// Scatter keeps the framework's seeded rule: sized children place at
  /// non-overlapping origins over the assigned area.
  and scatterLayers
    (seed: int, inner: CellRect, children: Doc.Item<'T>[])
    : Stamp<'T> =
    children
    |> Array.map(fun c ->
      let size = childSize(sizeOf inner, c)

      {
        emit c with
            W = max 1 size.W
            H = max 1 size.H
      })
    |> Flow.scatter seed

  /// The flow pack rides `Flow.grid`: named areas pass through, slot and
  /// flow children place as explicit slots, and `auto` tracks size from
  /// the children's footprints inside the grid itself.
  and flowGrid(gap: CellSize, inner: CellRect, item: Doc.Item<'T>) : Stamp<'T> =
    if gap.W <> gap.H then
      failwith
        "the Flow grid takes one gap for both axes; gapx= and gapy= differ"

    let children = item.Children
    let cols = if item.Cols.Length = 0 then [| Weight 1f |] else item.Cols
    let colCount = cols.Length
    let slots = assignSlots(item.Areas, colCount, children)
    let sizes = children |> Array.map(fun c -> childSize(sizeOf inner, c))

    let mutable rowCount = item.Rows.Length

    for struct (_, r, _, rs) in slots do
      rowCount <- max rowCount (r + rs)

    rowCount <- max rowCount item.Areas.Length

    let rows =
      if item.Rows.Length = 0 then
        Array.create (max 1 rowCount) (Weight 1f)
      elif rowCount > item.Rows.Length then
        Array.append item.Rows (Array.create (rowCount - item.Rows.Length) Auto)
      else
        item.Rows

    // named areas from the document become template rows; slot children
    // need no template names — `Place.Slot` addresses the tracks
    let places =
      (children, slots, sizes)
      |||> Array.map3(fun c struct (c0, r0, cs, rs) size ->
        let place =
          match c.Style.Area with
          | ValueSome name -> Place.Area name
          | ValueNone -> Place.Slot(c0, r0, cs, rs)

        struct (place,
                areaPlace(
                  alignH c,
                  alignV c,
                  size,
                  { emit c with W = size.W; H = size.H }
                )))

    Flow.grid {
      Cols = cols
      Rows = rows
      Gap = max 0 gap.W
      Areas = item.Areas |> Array.map(String.concat " ")
      Places = places
    }

  // ── the map shell ────────────────────────────────────────────

  /// Parses, resolves and lays the document out through the Flow API.
  /// Error builds nothing and carries the same positioned messages as
  /// `Doc.resolve`.
  let build
    (surface: Doc.Surface<'T>, src: string)
    : Result<CellGrid2D<'T>, string> =
    try
      Kdl.parse src
      |> Result.bind(fun roots ->
        Doc.resolve surface src roots
        |> Result.map(fun items -> struct (roots, items)))
      |> Result.bind(fun struct (roots, items) ->
        match items with
        | [| root |] ->
          (match Doc.findMapNode roots with
           | ValueNone -> Error "the document needs a map node"
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

                Ok built))
        | _ -> Error "the document needs exactly one map container")
    with e ->
      Error e.Message
