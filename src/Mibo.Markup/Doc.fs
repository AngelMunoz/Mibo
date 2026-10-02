namespace Mibo.Markup

/// Document resolution: the resolved `Item` tree a front-end's `Node`
/// tree becomes. Layout lives in properties (the style cascade); paint
/// lives in bodies (`Op` data, interpreted at render time — the document
/// text produces no closures of its own beyond one interpreter wrapper
/// per element). Geometry rides the framework's own nouns
/// (`CellPoint`, `CellSize`, `CellRect`, `Align`, `Track`).
module Doc =

  open System
  open System.Collections.Generic
  open System.Collections.Immutable
  open System.Collections.Frozen
  open System.Numerics
  open Mibo.Layout

  /// A per-cell kernel, referenced from a document by name through
  /// `generate`. Gen2 generates 2D cells.
  [<Struct>]
  type Kernel<'T> = Gen2 of gen2: (int -> int -> 'T)

  /// A paint statement as data. The interpreter runs it at render time.
  ///
  /// Closed by design — the library owns statement semantics, and every
  /// statement means the same thing in every game. Consumers extend
  /// through elements and words, not through new cases.
  ///
  /// Geometry is box-local: `FillRect` covers a local rect, `Set` places
  /// a local point (or, with `at` absent, places one cell by the two
  /// aligns — the `set c` anchor form), `Border` outlines a local rect
  /// (absent rect: the whole box), `Generate` runs a kernel over a local
  /// rect (absent area: the whole box).
  type Op<'T> =
    | Fill of 'T
    | FillRect of CellRect * 'T
    | Set of at: CellPoint voption * h: Align * v: Align * 'T
    | Border of area: CellRect voption * 'T
    | Rect of edge: 'T * floor: 'T
    | Generate of kernel: string * area: CellRect voption

  /// One element declared by the game: a name, an optional intrinsic
  /// size, and a body of operations. Variations are separate
  /// declarations (six corridor directions are six declarations from one
  /// F# builder); there are no parameters.
  type ElementDecl<'T> = {
    Name: string
    Extent: CellSize voption
    Body: Op<'T>[]
  }

  /// The game's whole say: cell words, kernels, and its element library.
  /// Constructed once, read per build — hence the frozen dictionaries,
  /// the fastest read-side dictionary in the BCL.
  type Surface<'T> = {
    Words: FrozenDictionary<string, 'T>
    Kernels: FrozenDictionary<string, Kernel<'T>>
    Elements: FrozenDictionary<string, ElementDecl<'T>>
  }

  /// How a container places its children: stacked layers (default), flow
  /// tracks, or seeded scatter.
  [<Struct>]
  type Pack =
    | Stack
    | Flow
    | Scatter

  /// Merged layout properties. Absent means inherit. Two halves: where
  /// and how big the element itself is inside its parent (Size, At, Col,
  /// Row, Span, Area, PlaceH, PlaceV), and how it places its own children
  /// (Pack, Gap, Pad, Seed). Layout only — paint stays in bodies.
  [<Struct>]
  type Style = {
    Size: CellSize voption // w= h=
    At: CellPoint voption // x= y= — exact placement, skips the pack
    Col: int voption // col=
    Row: int voption // row=
    Span: struct (int * int) voption // colspan= rowspan=
    Area: string voption // area=
    PlaceH: Align voption // hplace=
    PlaceV: Align voption // vplace=
    Pack: Pack voption // pack=
    Gap: CellSize voption // gapx= gapy=
    Pad: int voption // pad=
    Seed: int voption // seed= (Scatter only)
  }

  /// One resolvable element of the document tree: the paint body, the
  /// merged style, and the container's declared tracks and area
  /// template.
  type Element<'T> = {
    Extent: CellSize voption
    Paint: GridSection2D<'T> -> unit
  }

  /// One resolved item of the document tree. `Name` selects the style
  /// rules; `Element` paints the body; `Cols`, `Rows` and `Areas` are
  /// the container's declared tracks and area template.
  type Item<'T> = {
    Name: string
    Element: Element<'T>
    Style: Style
    Cols: Track[]
    Rows: Track[]
    Areas: string[][]
    Children: Item<'T>[]
  }

  // ── Positioned failures and argument readers ─────────────────

  let at(src: string, n: Node) : string = Markup.where src n.Position

  let wantInt(src: string, n: Node, i: int) : Result<int, string> =
    if i >= n.Args.Length then
      Error $"'{n.Kind}' takes at least {i + 1} arguments ({at(src, n)})"
    else
      match n.Args[i] with
      | Number v -> Ok v
      | Decimal _ ->
        Error $"`{n.Kind}` wants a whole number, not a decimal ({at(src, n)})"
      | Word w ->
        Error $"`{n.Kind}` wants a whole number, got '{w}' ({at(src, n)})"

  let axisWord(w: string) : Align voption =
    match w with
    | "start" -> ValueSome Start
    | "center" -> ValueSome Center
    | "end" -> ValueSome End
    | "stretch" -> ValueSome Stretch
    | _ -> ValueNone

  // ── Style: the layout property channel ───────────────────────

  let emptyStyle: Style = {
    Size = ValueNone
    At = ValueNone
    Col = ValueNone
    Row = ValueNone
    Span = ValueNone
    Area = ValueNone
    PlaceH = ValueNone
    PlaceV = ValueNone
    Pack = ValueNone
    Gap = ValueNone
    Pad = ValueNone
    Seed = ValueNone
  }

  let inline wOf(v: CellSize voption) =
    match v with
    | ValueSome sz -> sz.W
    | _ -> 0

  let inline hOf(v: CellSize voption) =
    match v with
    | ValueSome sz -> sz.H
    | _ -> 0

  let inline xOf(v: CellPoint voption) =
    match v with
    | ValueSome pt -> pt.X
    | _ -> 0

  let inline yOf(v: CellPoint voption) =
    match v with
    | ValueSome pt -> pt.Y
    | _ -> 0

  let inline gapXOf(v: CellSize voption) =
    match v with
    | ValueSome g -> g.W
    | _ -> 0

  let inline gapYOf(v: CellSize voption) =
    match v with
    | ValueSome g -> g.H
    | _ -> 0

  let inline csOf(v: struct (int * int) voption) =
    match v with
    | ValueSome(cs, _) -> cs
    | _ -> 1

  let inline rsOf(v: struct (int * int) voption) =
    match v with
    | ValueSome(_, rs) -> rs
    | _ -> 1

  /// Field-wise cascade: the patch wins where it states a value.
  let mergeStyle(baseStyle: Style, patch: Style) : Style = {
    Size = if patch.Size.IsSome then patch.Size else baseStyle.Size
    At = if patch.At.IsSome then patch.At else baseStyle.At
    Col = if patch.Col.IsSome then patch.Col else baseStyle.Col
    Row = if patch.Row.IsSome then patch.Row else baseStyle.Row
    Span = if patch.Span.IsSome then patch.Span else baseStyle.Span
    Area = if patch.Area.IsSome then patch.Area else baseStyle.Area
    PlaceH =
      if patch.PlaceH.IsSome then
        patch.PlaceH
      else
        baseStyle.PlaceH
    PlaceV =
      if patch.PlaceV.IsSome then
        patch.PlaceV
      else
        baseStyle.PlaceV
    Pack = if patch.Pack.IsSome then patch.Pack else baseStyle.Pack
    Gap = if patch.Gap.IsSome then patch.Gap else baseStyle.Gap
    Pad = if patch.Pad.IsSome then patch.Pad else baseStyle.Pad
    Seed = if patch.Seed.IsSome then patch.Seed else baseStyle.Seed
  }

  let applyProp
    (src: string, n: Node, s: Style, p: Prop)
    : Result<Style, string> =
    let bad v =
      Error $"property '{p.Name}' {v} ({at(src, n)})"

    let inline num() =
      match p.Value with
      | Number v -> Ok v
      | _ -> bad "wants a number"

    let inline wrd() =
      match p.Value with
      | Word w -> Ok w
      | _ -> bad "wants a word"

    let inline axisOf w =
      match axisWord w with
      | ValueSome a -> Ok a
      | ValueNone -> bad $"wants start, center, end or stretch, got '{w}'"

    let inline both w =
      match axisWord w with
      | ValueSome a ->
        Ok {
          s with
              PlaceH = ValueSome a
              PlaceV = ValueSome a
        }
      | ValueNone ->
        bad $"wants an axis word (start, center, end, stretch), got '{w}'"

    let inline packOf w =
      match w with
      | "stack" -> Ok { s with Pack = ValueSome Stack }
      | "flow" -> Ok { s with Pack = ValueSome Flow }
      | "scatter" -> Ok { s with Pack = ValueSome Scatter }
      | _ -> bad $"wants stack, flow or scatter, got '{w}'"

    match p.Name with
    | "w" ->
      num()
      |> Result.map(fun v -> {
        s with
            Size = ValueSome { W = v; H = hOf s.Size }
      })
    | "h" ->
      num()
      |> Result.map(fun v -> {
        s with
            Size = ValueSome { W = wOf s.Size; H = v }
      })
    | "x" ->
      num()
      |> Result.map(fun v -> {
        s with
            At = ValueSome { X = v; Y = yOf s.At }
      })
    | "y" ->
      num()
      |> Result.map(fun v -> {
        s with
            At = ValueSome { X = xOf s.At; Y = v }
      })
    | "col" -> num() |> Result.map(fun v -> { s with Col = ValueSome v })
    | "row" -> num() |> Result.map(fun v -> { s with Row = ValueSome v })
    | "colspan" ->
      num()
      |> Result.map(fun v -> {
        s with
            Span = ValueSome struct (v, rsOf s.Span)
      })
    | "rowspan" ->
      num()
      |> Result.map(fun v -> {
        s with
            Span = ValueSome struct (csOf s.Span, v)
      })
    | "area" -> wrd() |> Result.map(fun w -> { s with Area = ValueSome w })
    | "hplace" ->
      wrd()
      |> Result.bind axisOf
      |> Result.map(fun a -> { s with PlaceH = ValueSome a })
    | "vplace" ->
      wrd()
      |> Result.bind axisOf
      |> Result.map(fun a -> { s with PlaceV = ValueSome a })
    | "place" -> wrd() |> Result.bind both
    | "pack" -> wrd() |> Result.bind packOf
    | "gapx" ->
      num()
      |> Result.map(fun v -> {
        s with
            Gap = ValueSome { W = v; H = gapYOf s.Gap }
      })
    | "gapy" ->
      num()
      |> Result.map(fun v -> {
        s with
            Gap = ValueSome { W = gapXOf s.Gap; H = v }
      })
    | "pad" -> num() |> Result.map(fun v -> { s with Pad = ValueSome v })
    | "seed" -> num() |> Result.map(fun v -> { s with Seed = ValueSome v })
    | _ -> Error $"unknown property '{p.Name}' ({at(src, n)})"

  let styleOfProps
    (src: string, n: Node, baseStyle: Style)
    : Result<Style, string> =
    n.Props
    |> Seq.fold
      (fun acc p -> acc |> Result.bind(fun s -> applyProp(src, n, s, p)))
      (Ok baseStyle)

  // ── Track and area directives ────────────────────────────────

  let tracksOf(src: string, n: Node) : Result<Track[], string> =
    let tracks = ResizeArray<Track>()
    let mutable i = 0
    let mutable failed = ValueNone

    while i < n.Args.Length && failed.IsNone do
      match n.Args[i] with
      | Number v when v > 0 ->
        tracks.Add(Weight(float32 v))
        i <- i + 1
      | Number _ ->
        failed <- ValueSome $"track ratios must be positive ({at(src, n)})"
      | Word "auto" ->
        tracks.Add Auto
        i <- i + 1
      | Word "fixed" when i + 1 < n.Args.Length ->
        (match n.Args[i + 1] with
         | Number v when v > 0 ->
           tracks.Add(Fixed v)
           i <- i + 2
         | _ ->
           failed <-
             ValueSome $"a fixed track wants a positive number ({at(src, n)})")
      | Word w ->
        failed <-
          ValueSome
            $"a track wants a ratio, 'fixed n' or 'auto', got '{w}' ({at(src, n)})"
      | _ -> failed <- ValueSome $"bad track ({at(src, n)})"

    match failed with
    | ValueSome e -> Error e
    | ValueNone -> Ok(tracks.ToArray())

  let areasOf(src: string, n: Node) : Result<string[][], string> =
    let rows = ResizeArray<string[]>()
    let mutable failed = ValueNone

    for row in n.Children do
      if failed.IsNone then
        let names = ResizeArray<string>()

        for a in row.Args do
          if failed.IsNone then
            match a with
            | Word w -> names.Add w
            | _ ->
              failed <- ValueSome $"an area row wants names ({at(src, row)})"

        if failed.IsNone then
          rows.Add(names.ToArray())

    match failed with
    | ValueSome e -> Error e
    | ValueNone -> Ok(rows.ToArray())

  // ── Operation resolution ─────────────────────────────────────

  let isOpKind(kind: string) : bool =
    match kind with
    | "fill"
    | "fillRect"
    | "set"
    | "border"
    | "rect"
    | "generate" -> true
    | _ -> false

  /// A statement reads its arguments by name: a property is a named slot
  /// (`rect edge=stone`), a positional argument fills the next slot in
  /// order. Leftovers of either kind are errors.
  let resolveOp
    (surface: Surface<'T>, src: string, n: Node)
    : Result<Op<'T>, string> =
    let named = Dictionary<string, Arg>()

    for p in n.Props do
      named[p.Name] <- p.Value

    let positional = ResizeArray<Arg>(Seq.toArray n.Args)

    let take(slot: string) : Arg voption =
      match named.TryGetValue slot with
      | true, v -> ValueSome v
      | _ ->
        if positional.Count > 0 then
          let v = positional[0]
          positional.RemoveAt 0
          ValueSome v
        else
          ValueNone

    let missing slot =
      Error $"'{n.Kind}' wants '{slot}' ({at(src, n)})"

    let takeInt slot =
      match take slot with
      | ValueSome(Number v) -> Ok v
      | ValueSome(Decimal _) ->
        Error $"`{n.Kind}` wants a whole number for '{slot}' ({at(src, n)})"
      | ValueSome(Word w) ->
        Error
          $"`{n.Kind}` wants a whole number for '{slot}', got '{w}' ({at(src, n)})"
      | ValueNone -> missing slot

    let takeCell slot =
      match take slot with
      | ValueSome(Word w) ->
        (match surface.Words.TryGetValue w with
         | true, cell -> Ok cell
         | false, _ ->
           Error
             $"`{n.Kind}` wants a cell word for '{slot}', got '{w}' ({at(src, n)})")
      | _ -> Error $"`{n.Kind}` wants a cell word for '{slot}' ({at(src, n)})"

    let takeKernel slot =
      match take slot with
      | ValueSome(Word w) ->
        if surface.Kernels.ContainsKey w then
          Ok w
        else
          Error $"unknown kernel '{w}' ({at(src, n)})"
      | _ -> Error $"`{n.Kind}` wants a kernel name for '{slot}' ({at(src, n)})"

    let takeAlign slot =
      match take slot with
      | ValueSome(Word w) ->
        (match axisWord w with
         | ValueSome a -> Ok a
         | ValueNone ->
           Error
             $"'{n.Kind}' wants an axis word (start, center, end, stretch) for '{slot}', got '{w}' ({at(src, n)})")
      | _ -> Error $"'{n.Kind}' wants an axis word for '{slot}' ({at(src, n)})"

    /// Reads the four rect slots in order; the first failure wins.
    let takeRect() =
      let values = ResizeArray<int>()
      let mutable failed = ValueNone

      for slot in [ "x"; "y"; "w"; "h" ] do
        if failed.IsNone then
          match takeInt slot with
          | Ok v -> values.Add v
          | Error e -> failed <- ValueSome e

      match failed with
      | ValueSome e -> Error e
      | ValueNone -> Ok struct (values[0], values[1], values[2], values[3])

    let noLeftovers allowed =
      if positional.Count > 0 then
        Error
          $"'{n.Kind}' has {positional.Count} extra argument(s) ({at(src, n)})"
      else
        let extra =
          named.Keys
          |> Seq.filter(fun k -> not(List.contains k allowed))
          |> Seq.toList

        match extra with
        | k :: _ -> Error $"'{n.Kind}' has no argument '{k}' ({at(src, n)})"
        | [] -> Ok()

    let rectOf(x: int, y: int, w: int, h: int) : CellRect = {
      X = x
      Y = y
      W = w
      H = h
    }

    let usesRectArea =
      named.ContainsKey "x" || named.ContainsKey "y" || positional.Count > 1

    /// Runs the leftover check behind an already-built statement.
    let checkedOp allowed op =
      noLeftovers allowed |> Result.map(fun () -> op)

    let fillStatement cell = checkedOp [ "cell" ] (Op.Fill cell)

    let fillRectStatement(struct (x, y, w, h)) =
      takeCell "cell"
      |> Result.bind(fun cell ->
        checkedOp
          [ "x"; "y"; "w"; "h"; "cell" ]
          (Op.FillRect(rectOf(x, y, w, h), cell)))

    let setStatement() =
      // exact: `set x=1 y=2 cell` or `set 1 2 cell`; anchored:
      // `set hplace=center vplace=center cell`
      let coords =
        named.ContainsKey "x" || named.ContainsKey "y" || positional.Count = 3

      let hName = if named.ContainsKey "halign" then "halign" else "hplace"

      let vName = if named.ContainsKey "valign" then "valign" else "vplace"

      let anchored = named.ContainsKey hName || named.ContainsKey vName

      if coords then
        takeInt "x"
        |> Result.bind(fun x ->
          takeInt "y"
          |> Result.bind(fun y ->
            takeCell "cell"
            |> Result.bind(fun cell ->
              checkedOp
                [ "x"; "y"; "cell"; "halign"; "valign"; "hplace"; "vplace" ]
                (Op.Set(ValueSome { X = x; Y = y }, Start, Start, cell)))))
      elif anchored then
        takeAlign hName
        |> Result.bind(fun h ->
          takeAlign vName
          |> Result.bind(fun v ->
            takeCell "cell"
            |> Result.bind(fun cell ->
              checkedOp
                [ "halign"; "valign"; "hplace"; "vplace"; "cell" ]
                (Op.Set(ValueNone, h, v, cell)))))
      elif positional.Count = 2 then
        takeCell "cell"
        |> Result.bind(fun cell ->
          checkedOp
            [ "cell"; "halign"; "valign"; "hplace"; "vplace" ]
            (Op.Set(ValueSome { X = 0; Y = 0 }, Start, Start, cell)))
      else
        Error
          $"'set' wants x= y= coordinates or hplace= vplace= alignment, and a cell ({at(src, n)})"

    let borderStatement() =
      if usesRectArea then
        takeRect()
        |> Result.bind(fun struct (x, y, w, h) ->
          takeCell "cell"
          |> Result.bind(fun cell ->
            checkedOp
              [ "x"; "y"; "w"; "h"; "cell" ]
              (Op.Border(ValueSome(rectOf(x, y, w, h)), cell))))
      else
        takeCell "cell"
        |> Result.bind(fun cell ->
          checkedOp [ "cell" ] (Op.Border(ValueNone, cell)))

    let rectStatement() =
      takeCell "edge"
      |> Result.bind(fun edge ->
        takeCell "floor"
        |> Result.bind(fun floor ->
          checkedOp [ "edge"; "floor" ] (Op.Rect(edge, floor))))

    let generateStatement() =
      if usesRectArea then
        takeRect()
        |> Result.bind(fun struct (x, y, w, h) ->
          takeKernel "kernel"
          |> Result.bind(fun name ->
            checkedOp
              [ "x"; "y"; "w"; "h"; "kernel" ]
              (Op.Generate(name, ValueSome(rectOf(x, y, w, h))))))
      else
        takeKernel "kernel"
        |> Result.bind(fun name ->
          checkedOp [ "kernel" ] (Op.Generate(name, ValueNone)))

    match n.Kind with
    | "fill" -> takeCell "cell" |> Result.bind fillStatement
    | "fillRect" -> takeRect() |> Result.bind fillRectStatement
    | "set" -> setStatement()
    | "border" -> borderStatement()
    | "rect" -> rectStatement()
    | "generate" -> generateStatement()
    | _ -> Error $"unknown statement '{n.Kind}' ({at(src, n)})"

  // ── The interpreter ──────────────────────────────────────────

  let interpret
    (surface: Surface<'T>, box: GridSection2D<'T>, op: Op<'T>)
    : unit =
    match op with
    | Op.Fill cell -> Layout.fill 0 0 box.Width box.Height cell box |> ignore
    | Op.FillRect(area, cell) ->
      Layout.fill area.X area.Y area.W area.H cell box |> ignore
    | Op.Set(ValueSome at, _, _, cell) ->
      Layout.set at.X at.Y cell box |> ignore
    | Op.Set(ValueNone, h, v, cell) ->
      let pos(a: Align, n: int) =
        match a with
        | Start -> 0
        | End -> max 0 (n - 1)
        | Center
        | Stretch -> max 0 ((n - 1) / 2)

      Layout.set (pos(h, box.Width)) (pos(v, box.Height)) cell box |> ignore
    | Op.Border(ValueSome area, cell) ->
      Layout.border area.X area.Y area.W area.H cell box |> ignore
    | Op.Border(ValueNone, cell) ->
      Layout.border 0 0 box.Width box.Height cell box |> ignore
    | Op.Rect(edge, floor) ->
      Layout.rect 0 0 box.Width box.Height edge floor box |> ignore
    | Op.Generate(name, area) ->
      (match surface.Kernels.TryGetValue name with
       | true, Gen2 k ->
         (match area with
          | ValueNone ->
            Layout.generate 0 0 box.Width box.Height k box |> ignore
          | ValueSome r -> Layout.generate r.X r.Y r.W r.H k box |> ignore)
       | false, _ ->
         failwith
           $"unknown kernel '{name}' (the resolver must have let a bad name through)")

  /// The one closure per element: the body as data, interpreted at
  /// render time. The text produces nothing else that runs.
  let paintOf
    (surface: Surface<'T>, extent: CellSize voption, body: Op<'T>[])
    : Element<'T> =
    {
      Extent = extent
      Paint =
        fun box ->
          for op in body do
            interpret(surface, box, op)
    }

  // ── Measurement ──────────────────────────────────────────────

  /// Scratch-grid run, then a scan for the covered area. `available`
  /// bounds the scratch grid, so greedy bodies report the available
  /// size, not an unbounded one.
  let measure(element: Element<'T>, available: CellSize) : CellSize =
    let w = max 0 available.W
    let h = max 0 available.H

    if w = 0 || h = 0 then
      { W = 0; H = 0 }
    else
      let scratch = CellGrid2D.create w h Vector2.One Vector2.Zero

      let section: GridSection2D<'T> = {
        BackingGrid = scratch
        OffsetX = 0
        OffsetY = 0
        Width = w
        Height = h
      }

      element.Paint section

      let mutable maxX = -1
      let mutable maxY = -1

      CellGrid2D.iter
        (fun x y _ ->
          maxX <- max maxX x
          maxY <- max maxY y)
        scratch

      if maxX < 0 then
        { W = 0; H = 0 }
      else
        { W = maxX + 1; H = maxY + 1 }

  // ── Repeat expansion ─────────────────────────────────────────

  /// `repeat n` duplicates its children, nested repeats included.
  let expandNodes
    (src: string, nodes: ImmutableArray<Node>)
    : Result<Node list, string> =
    let out = ResizeArray<Node>()
    let mutable failed = ValueNone

    let rec go(nodes: ImmutableArray<Node>) : unit =
      if failed.IsNone then
        for n in nodes do
          if failed.IsNone then
            if n.Kind = "repeat" then
              (match Seq.toArray n.Args with
               | [| Number count |] when count >= 0 ->
                 for _ in 1..count do
                   go n.Children
               | _ ->
                 failed <-
                   ValueSome
                     $"repeat takes one non-negative number ({at(src, n)})")
            else
              out.Add n

    go nodes

    match failed with
    | ValueSome e -> Error e
    | ValueNone -> Ok(List.ofSeq out)

  // ── Declarations ─────────────────────────────────────────────

  /// Collects `element name { ... }` templates from the whole tree. The
  /// definition's `w=`/`h=` state its extent; other properties are
  /// errors. The body resolves like any statement list.
  let collectTemplates
    (surface: Surface<'T>, src: string, roots: ImmutableArray<Node>)
    : Result<Dictionary<string, ElementDecl<'T>>, string> =
    let defs = Dictionary<string, ElementDecl<'T>>()
    let mutable failed = ValueNone

    let extentOf(n: Node) : CellSize voption =
      let mutable w = ValueNone
      let mutable h = ValueNone

      for p in n.Props do
        if failed.IsNone then
          (match p.Name, p.Value with
           | "w", Number v -> w <- ValueSome v
           | "h", Number v -> h <- ValueSome v
           | ("w" | "h"), _ ->
             failed <-
               ValueSome
                 $"an element definition's w= and h= want numbers ({at(src, n)})"
           | _ ->
             failed <-
               ValueSome
                 $"an element definition takes w= and h= only ({at(src, n)})")

      match w, h with
      | ValueSome w', ValueSome h' -> ValueSome { W = w'; H = h' }
      | ValueNone, ValueNone -> ValueNone
      | _ ->
        failed <-
          ValueSome
            $"an element definition needs w= and h= together ({at(src, n)})"

        ValueNone

    let rec go(n: Node) : unit =
      if failed.IsNone then
        if n.Kind = "element" then
          (match n.Label with
           | ValueSome name ->
             let extent = extentOf n

             if failed.IsNone then
               (match expandNodes(src, n.Children) with
                | Error e -> failed <- ValueSome e
                | Ok expanded ->
                  let body = ResizeArray<Op<'T>>()

                  for c in expanded do
                    if failed.IsNone then
                      (match resolveOp(surface, src, c) with
                       | Ok op -> body.Add op
                       | Error e -> failed <- ValueSome e)

                  if failed.IsNone then
                    defs[name] <- {
                      Name = name
                      Extent = extent
                      Body = body.ToArray()
                    })
           | ValueNone ->
             failed <-
               ValueSome $"an element definition needs a name ({at(src, n)})")
        else
          for c in n.Children do
            go c

    for r in roots do
      go r

    match failed with
    | ValueSome e -> Error e
    | ValueNone -> Ok defs

  /// Collects `style name ...` rules. Later rules win per property; a
  /// bad property fails the build with its position.
  let collectStyles
    (src: string, roots: ImmutableArray<Node>)
    : Result<Dictionary<string, Style>, string> =
    let rules = Dictionary<string, Style>()
    let mutable failed = ValueNone

    let rec go(n: Node) : unit =
      if failed.IsNone then
        if n.Kind = "style" then
          (match Seq.toArray n.Args with
           | [| Word name |] ->
             (match styleOfProps(src, n, emptyStyle) with
              | Ok patch ->
                let merged =
                  if rules.ContainsKey name then
                    mergeStyle(rules[name], patch)
                  else
                    patch

                rules[name] <- merged
              | Error e -> failed <- ValueSome e)
           | _ ->
             failed <-
               ValueSome $"a style rule takes an element name ({at(src, n)})")

        for c in n.Children do
          go c

    for r in roots do
      go r

    match failed with
    | ValueSome e -> Error e
    | ValueNone -> Ok rules

  // ── Container resolution ─────────────────────────────────────

  let rec resolveContainer
    (
      surface: Surface<'T>,
      src: string,
      templates: Dictionary<string, ElementDecl<'T>>,
      styles: Dictionary<string, Style>,
      name: string,
      n: Node
    ) : Result<Item<'T>, string> =
    match expandNodes(src, n.Children) with
    | Error e -> Error e
    | Ok children ->
      // cascade: solver defaults, document style rules, inline props
      let ruleStyle =
        if styles.ContainsKey name then styles[name] else emptyStyle

      match styleOfProps(src, n, ruleStyle) with
      | Error e -> Error e
      | Ok style ->
        let ops = ResizeArray<Op<'T>>()
        let itemNodes = ResizeArray<Node>()
        let mutable cols: Track[] = [||]
        let mutable rows: Track[] = [||]
        let mutable areas: string[][] = [||]
        let mutable failed = ValueNone

        for c in children do
          if failed.IsNone then
            if c.Kind = "cols" then
              (match tracksOf(src, c) with
               | Ok t -> cols <- t
               | Error e -> failed <- ValueSome e)
            elif c.Kind = "rows" then
              (match tracksOf(src, c) with
               | Ok t -> rows <- t
               | Error e -> failed <- ValueSome e)
            elif c.Kind = "areas" then
              (match areasOf(src, c) with
               | Ok a -> areas <- a
               | Error e -> failed <- ValueSome e)
            elif c.Kind = "element" || c.Kind = "style" then
              () // declarations, already collected
            elif isOpKind c.Kind then
              (match resolveOp(surface, src, c) with
               | Ok op -> ops.Add op
               | Error e -> failed <- ValueSome e)
            else
              let known =
                templates.ContainsKey c.Kind
                || surface.Elements.ContainsKey c.Kind
                || c.Kind = "plot"
                || c.Kind = "grid"

              if known then
                itemNodes.Add c
              else
                failed <- ValueSome $"unknown element '{c.Kind}' ({at(src, c)})"

        match failed with
        | ValueSome e -> Error e
        | ValueNone ->
          // the body: the declaration's statements first, the use
          // site's after
          let mutable declBody: Op<'T>[] = [||]
          let mutable declExtent: CellSize voption = ValueNone

          if templates.ContainsKey name then
            let d = templates[name]
            declBody <- d.Body
            declExtent <- d.Extent
          else
            let mutable d = Unchecked.defaultof<ElementDecl<'T>>

            if surface.Elements.TryGetValue(name, &d) then
              declBody <- d.Body
              declExtent <- d.Extent

          let body = Array.append declBody (ops.ToArray())
          let items = ResizeArray<Item<'T>>()

          for c in itemNodes do
            if failed.IsNone then
              (match
                resolveContainer(surface, src, templates, styles, c.Kind, c)
               with
               | Ok item -> items.Add item
               | Error e -> failed <- ValueSome e)

          match failed with
          | ValueSome e -> Error e
          | ValueNone ->
            Ok {
              Name = name
              Element = paintOf(surface, declExtent, body)
              Style = style
              Cols = cols
              Rows = rows
              Areas = areas
              Children = items.ToArray()
            }

  // ── Entry ────────────────────────────────────────────────────

  let findMapNode(nodes: ImmutableArray<Node>) : Node voption =
    let rec go(n: Node) : Node voption =
      if n.Kind = "map" && n.Args.Length >= 2 then
        ValueSome n
      else
        let mutable found = ValueNone

        for c in n.Children do
          if found.IsNone then
            found <- go c

        found

    let mutable root = ValueNone

    for n in nodes do
      if root.IsNone then
        root <- go n

    root

  let dimsOf(src: string, n: Node) : Result<CellSize, string> =
    match wantInt(src, n, 0) with
    | Error e -> Error e
    | Ok w ->
      (match wantInt(src, n, 1) with
       | Error e -> Error e
       | Ok h ->
         if w > 0 && h > 0 then
           Ok { W = w; H = h }
         else
           Error $"map dimensions must be positive ({at(src, n)})")

  /// Game-declared element bodies never pass through statement
  /// resolution, so their kernel references get checked here: a typo in
  /// an F# element body must fail the build, not vanish at render.
  let validateSurface(surface: Surface<'T>) : Result<unit, string> =
    let bad =
      surface.Elements.Values
      |> Seq.collect(fun decl ->
        decl.Body
        |> Seq.choose(fun op ->
          match op with
          | Op.Generate(kernelName, _) when
            not(surface.Kernels.ContainsKey kernelName)
            ->
            Some
              $"the element '{decl.Name}' references the unknown kernel '{kernelName}'"
          | _ -> None))
      |> Seq.tryHead

    match bad with
    | Some e -> Error e
    | None -> Ok()

  /// Templates expand, words resolve, style merges. Errors carry line
  /// and column (`Markup.where` turns each node's offset into them).
  ///
  /// Cascade: solver defaults, then document `style` rules in order,
  /// then inline properties. `map` is the root container; `element`
  /// nodes define templates (name as first argument); `repeat n`
  /// duplicates its children; `cols`, `rows` and `areas` child nodes
  /// declare a container's tracks and named areas. Declared
  /// `cols`/`rows` imply `pack=flow`. `plot` is the built-in anonymous
  /// container: empty body, exact box.
  let resolve
    (surface: Surface<'T>)
    (src: string)
    (roots: ImmutableArray<Node>)
    : Result<Item<'T>[], string> =
    let withDecls templates =
      match collectStyles(src, roots) with
      | Error e -> Error e
      | Ok styles ->
        match findMapNode roots with
        | ValueNone -> Error "the document needs a map node"
        | ValueSome mapNode ->
          resolveContainer(surface, src, templates, styles, "map", mapNode)
          |> Result.map(fun root -> [| root |])

    validateSurface surface
    |> Result.bind(fun () -> collectTemplates(surface, src, roots))
    |> Result.bind withDecls
