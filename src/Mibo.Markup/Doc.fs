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
    /// Reads how many cells one instance of a cell covers. Absent: every
    /// cell covers one cell.
    Span: ('T -> InstanceSpan) voption
    /// Writes a span a statement states, so `set` can size one instance.
    /// Absent: a statement cannot state a span, and a word keeps the one it
    /// declares.
    WithSpan: ('T -> InstanceSpan -> 'T) voption
  }

  /// The span a cell's instance covers, or `ValueNone` when it covers one
  /// cell. The identity forms read as `ValueNone`, so a word that declares
  /// no span costs nothing to check.
  let inline private spanningOf
    (surface: Surface<'T>)
    (cell: 'T)
    : InstanceSpan voption =
    surface.Span
    |> ValueOption.bind(fun read ->
      let span = read cell

      if Occupancy.isIdentity span then
        ValueNone
      else
        ValueSome span)

  /// The extent a cell's instance measures: one cell for the identity, the
  /// rectangle for a `Span`, the offset bounding box for a `Radius`. A span
  /// that covers nothing still measures one cell, so a measurement never
  /// shrinks below a cell; the scan reports the span itself.
  let inline private spanSize (surface: Surface<'T>) (cell: 'T) : CellSize =
    match spanningOf surface cell with
    | ValueSome(Span(across, deep)) -> { W = max 1 across; H = max 1 deep }
    | ValueSome(Radius r) -> {
        W = max 1 (2 * r + 1)
        H = max 1 (2 * r + 1)
      }
    | _ -> { W = 1; H = 1 }

  /// Does this body place a spanning word? Only `set` can, so a body that
  /// never sets reads `false` without consulting the projection once.
  let inline private placesSpan (surface: Surface<'T>) (body: Op<'T>[]) : bool =
    match surface.Span with
    | ValueNone -> false
    | ValueSome _ ->
      body
      |> Array.exists(fun op ->
        match op with
        | Op.Set(_, _, _, cell) ->
          spanningOf surface cell |> ValueOption.isSome
        | _ -> false)

  /// How a container places its children: stacked children (default), flow
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
  /// template. Carries a paint closure, so it never takes structural
  /// equality.
  [<NoEquality; NoComparison>]
  type Element<'T> = {
    Extent: CellSize voption
    /// The merged ops the resolver built: the declaration's statements
    /// first, the use site's after. The paint closure interprets them, so
    /// a consumer that needs the ops themselves — the emitter telling a
    /// greedy body from an absent one — reads them here.
    Body: Op<'T>[]
    Paint: GridSection2D<'T> -> unit
  }

  /// One resolved item of the document tree. `Name` selects the style
  /// rules; `Element` paints the body; `Cols`, `Rows` and `Areas` are
  /// the container's declared tracks and area template. Carries a paint
  /// closure, so it never takes structural equality.
  [<NoEquality; NoComparison>]
  type Item<'T> = {
    Name: string
    /// The layer this item declares, when it is a `layer` container under
    /// the map. `ValueNone` on every other item: `main` is the map's own
    /// body plus its non-layer children, and it is not an item of its own.
    Layer: string voption
    Element: Element<'T>
    Style: Style
    Cols: Track[]
    Rows: Track[]
    Areas: string[][]
    Children: Item<'T>[]
  }

  // ── Positioned failures and argument readers ─────────────────

  /// The positioned half of an error message; XML nodes carry Position
  /// -1 (that front-end tracks no positions), so the message names the
  /// element only.
  let at(src: string, n: Node) : string = Markup.at src n.Position

  /// The value one node states for one property name, or `ValueNone`.
  /// A name stated twice fails here: KDL accepts a repeated name, and
  /// the readers disagreed on which one won.
  let private propOf
    (src: string, n: Node, name: string)
    : Result<Arg voption, string> =
    let mutable slot = ValueNone
    let mutable twice = false

    for p in n.Props do
      if p.Name = name then
        if slot.IsNone then
          slot <- ValueSome p.Value
        else
          twice <- true

    if twice then
      Error
        $"`{n.Kind}` states the property '{name}' more than once{at(src, n)}"
    else
      Ok slot

  /// Fails when a node states any property name more than once. The
  /// cascade kept the last value and the scalar readers kept the first,
  /// so a repeat says nothing about which one the author meant.
  let noRepeatedProps(src: string, n: Node) : Result<unit, string> =
    let seen = HashSet<string>(StringComparer.Ordinal)
    let mutable twice = ValueNone

    for p in n.Props do
      if twice.IsNone && not(seen.Add p.Name) then
        twice <- ValueSome p.Name

    match twice with
    | ValueSome name ->
      Error
        $"`{n.Kind}` states the property '{name}' more than once{at(src, n)}"
    | ValueNone -> Ok()

  /// Reads one whole-number scalar by name first (the XML channel),
  /// then by positional index (the KDL channel).
  let wantInt
    (src: string, n: Node, name: string, i: int)
    : Result<int, string> =
    let bad why =
      Error $"`{n.Kind}` wants a whole number for '{name}', {why}{at(src, n)}"

    match propOf(src, n, name) with
    | Error e -> Error e
    | Ok ValueNone ->
      if i >= 0 && i < n.Args.Length then
        match n.Args[i] with
        | Number v -> Ok v
        | Decimal _ -> bad "not a decimal"
        | Word w -> bad $"got '{w}'"
      else
        Error $"'{n.Kind}' wants '{name}'{at(src, n)}"
    | Ok(ValueSome(Number v)) -> Ok v
    | Ok(ValueSome(Decimal _)) -> bad "not a decimal"
    | Ok(ValueSome(Word w)) -> bad $"got '{w}'"

  /// Reads one word scalar by name first, then by positional index.
  let wantWord
    (src: string, n: Node, name: string, i: int)
    : Result<string, string> =
    let read(arg: Arg) : string voption =
      match arg with
      | Word w -> ValueSome w
      | Number v -> ValueSome(string v)
      | Decimal d -> ValueSome(string d)

    match propOf(src, n, name) with
    | Error e -> Error e
    | Ok ValueNone ->
      if i >= 0 && i < n.Args.Length then
        match read n.Args[i] with
        | ValueSome w -> Ok w
        | ValueNone -> Error $"'{n.Kind}' wants '{name}'{at(src, n)}"
      else
        Error $"'{n.Kind}' wants '{name}'{at(src, n)}"
    | Ok(ValueSome arg) ->
      match read arg with
      | ValueSome w -> Ok w
      | ValueNone -> Error $"'{n.Kind}' wants '{name}'{at(src, n)}"

  /// Reads one align word — `start`, `center`, `end`, `stretch` — into
  /// its `Align`; anything else is `ValueNone`.
  let axisWord(w: string) : Align voption =
    match w with
    | "start" -> ValueSome Start
    | "center" -> ValueSome Center
    | "end" -> ValueSome End
    | "stretch" -> ValueSome Stretch
    | _ -> ValueNone

  // ── Style: the layout property channel ───────────────────────

  /// The solver's defaults: no size, no placement, no pack, no gap. Every
  /// cascade starts here, so an absent property means "inherit".
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

  /// The `W` of a size, or 0 when absent — a half-stated `w=`/`h=` pair
  /// merges field-wise, so the missing axis reads 0 ("stretch this axis").
  let inline wOf(v: CellSize voption) =
    match v with
    | ValueSome sz -> sz.W
    | _ -> 0

  /// The `H` of a size, or 0 when absent.
  let inline hOf(v: CellSize voption) =
    match v with
    | ValueSome sz -> sz.H
    | _ -> 0

  /// The `X` of a point, or 0 when absent — a half-stated `x=`/`y=` pair
  /// merges field-wise, so the missing axis reads 0.
  let inline xOf(v: CellPoint voption) =
    match v with
    | ValueSome pt -> pt.X
    | _ -> 0

  /// The `Y` of a point, or 0 when absent.
  let inline yOf(v: CellPoint voption) =
    match v with
    | ValueSome pt -> pt.Y
    | _ -> 0

  /// The horizontal half of a `CellSize` gap pair, or 0 when absent.
  let inline gapXOf(v: CellSize voption) =
    match v with
    | ValueSome g -> g.W
    | _ -> 0

  /// The vertical half of a `CellSize` gap pair, or 0 when absent.
  let inline gapYOf(v: CellSize voption) =
    match v with
    | ValueSome g -> g.H
    | _ -> 0

  /// The column span of a `colspan`/`rowspan` pair, or 1 when absent — a
  /// half-stated span merges field-wise, and one track is the default.
  let inline csOf(v: struct (int * int) voption) =
    match v with
    | ValueSome(cs, _) -> cs
    | _ -> 1

  /// The row span of a `colspan`/`rowspan` pair, or 1 when absent.
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

  /// Applies one layout property (`w=`, `x=`, `col=`, `pack=`,
  /// `hplace=`, `seed=`, ...) on top of a style. A value of the wrong
  /// shape or an unknown property name is a positioned error.
  let applyProp
    (src: string, n: Node, s: Style, p: Prop)
    : Result<Style, string> =
    let bad v =
      Error $"property '{p.Name}' {v}{at(src, n)}"

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
    // the three lists describe the children a container places, so they
    // are the container's own: a rule that states one would do nothing
    | "cols"
    | "rows"
    | "areas" ->
      Error
        $"a style rule cannot state '{p.Name}': cols, rows and areas belong to the container{at(src, n)}"
    | _ -> Error $"unknown property '{p.Name}'{at(src, n)}"

  /// Merges a node's inline properties over a base style, in document
  /// order; the first bad property fails with its position. A name
  /// stated twice fails before any of them applies.
  let styleOfProps
    (src: string, n: Node, baseStyle: Style)
    : Result<Style, string> =
    noRepeatedProps(src, n)
    |> Result.bind(fun () ->
      n.Props
      |> Seq.fold
        (fun acc p -> acc |> Result.bind(fun s -> applyProp(src, n, s, p)))
        (Ok baseStyle))

  // ── Track and area directives ────────────────────────────────

  /// The three list properties a container states for its own children.
  /// They are the container's own channel: they never cascade, and a
  /// `style` rule that states one fails.
  let isListProp(name: string) : bool =
    match name with
    | "cols"
    | "rows"
    | "areas" -> true
    | _ -> false

  /// The value of one list property, or `ValueNone`. The XML front-end
  /// types an attribute by its content, so a one-token list arrives as a
  /// number there and as a word in KDL; both spell one list.
  let private listPropOf
    (src: string, n: Node, name: string)
    : Result<string voption, string> =
    match propOf(src, n, name) with
    | Error e -> Error e
    | Ok ValueNone -> Ok ValueNone
    | Ok(ValueSome(Word w)) ->
      if String.IsNullOrWhiteSpace w then
        Error $"`{n.Kind}` states an empty '{name}'{at(src, n)}"
      else
        Ok(ValueSome w)
    | Ok(ValueSome(Number v)) -> Ok(ValueSome(string v))
    | Ok(ValueSome(Decimal d)) ->
      Ok(ValueSome(d.ToString(Globalization.CultureInfo.InvariantCulture)))

  /// One list value as its tokens, on spaces and tabs.
  let private tokensOf(value: string) : string[] =
    value.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)

  /// Resolves one track list — `cols="1 fixed 3 auto"` — into tracks. A
  /// bare number is a weight, `fixed n` is a fixed size, `auto` sizes to
  /// the children. `Track.Percent` has no token: it carries a fraction
  /// of the container, and no document needs one yet.
  let tracksOf
    (src: string, n: Node, name: string)
    : Result<Track[] voption, string> =
    match listPropOf(src, n, name) with
    | Error e -> Error e
    | Ok ValueNone -> Ok ValueNone
    | Ok(ValueSome value) ->
      let tokens = tokensOf value
      let tracks = ResizeArray<Track>()
      let mutable i = 0
      let mutable failed = ValueNone

      while i < tokens.Length && failed.IsNone do
        let token = tokens[i]

        match
          Int32.TryParse(
            token,
            Globalization.NumberStyles.Integer,
            Globalization.CultureInfo.InvariantCulture
          )
        with
        | true, v when v > 0 ->
          tracks.Add(Weight(float32 v))
          i <- i + 1
        | true, _ ->
          failed <- ValueSome $"track ratios must be positive{at(src, n)}"
        | _ ->
          match token with
          | "auto" ->
            tracks.Add Auto
            i <- i + 1
          | "fixed" when i + 1 < tokens.Length ->
            (match
              Int32.TryParse(
                tokens[i + 1],
                Globalization.NumberStyles.Integer,
                Globalization.CultureInfo.InvariantCulture
              )
             with
             | true, v when v > 0 ->
               tracks.Add(Fixed v)
               i <- i + 2
             | _ ->
               failed <-
                 ValueSome $"a fixed track wants a positive number{at(src, n)}")
          | w ->
            failed <-
              ValueSome
                $"a track wants a ratio, 'fixed n' or 'auto', got '{w}'{at(src, n)}"

      match failed with
      | ValueSome e -> Error e
      | ValueNone -> Ok(ValueSome(tracks.ToArray()))

  /// Resolves one area template — `areas="road woods; road lake"` — into
  /// its rows of names. Rows split on `;`, names on spaces and tabs. A
  /// row with no name is an authoring slip, not an empty row: it would
  /// shift every row below it in the template.
  let areasOf(src: string, n: Node) : Result<string[][] voption, string> =
    match listPropOf(src, n, "areas") with
    | Error e -> Error e
    | Ok ValueNone -> Ok ValueNone
    | Ok(ValueSome value) ->
      let rows = ResizeArray<string[]>()
      let mutable failed = ValueNone

      for row in value.Split([| ';' |], StringSplitOptions.None) do
        if failed.IsNone then
          let names = tokensOf row

          if names.Length = 0 then
            failed <-
              ValueSome
                $"in '{n.Kind}': an area row needs at least one name{at(src, n)}"
          else
            rows.Add names

      match failed with
      | ValueSome e -> Error e
      | ValueNone -> Ok(ValueSome(rows.ToArray()))

  // ── Operation resolution ─────────────────────────────────────

  /// Whether the node kind is a paint statement (`fill`, `fillRect`,
  /// `set`, `border`, `rect`, `generate`). Everything else in a body is
  /// a child container or a declaration node.
  let isOpKind(kind: string) : bool =
    match kind with
    | "fill"
    | "fillRect"
    | "set"
    | "border"
    | "rect"
    | "generate" -> true
    | _ -> false

  /// Node kinds the vocabulary owns. A template or a surface element
  /// under one of these names would be unreachable, or would take over a
  /// container the resolver builds itself. `plot` and `grid` are not
  /// here: a template or a surface element of that name gives the
  /// built-in container its own body.
  let reservedNames = [|
    "map"
    "layer"
    "element"
    "style"
    "repeat"
    "cols"
    "rows"
    "areas"
    "fill"
    "fillRect"
    "set"
    "border"
    "rect"
    "generate"
  |]

  /// A statement reads its arguments by name: a property is a named slot
  /// (`rect edge=stone`), a positional argument fills the next slot in
  /// order. Leftovers of either kind are errors.
  let resolveOp
    (surface: Surface<'T>, src: string, n: Node)
    : Result<Op<'T>, string> =
    let named = Dictionary<string, Arg>()

    for p in n.Props do
      named[p.Name] <- p.Value

    let positional = ResizeArray<Arg>(n.Args)

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
      Error $"'{n.Kind}' wants '{slot}'{at(src, n)}"

    let takeInt slot =
      match take slot with
      | ValueSome(Number v) -> Ok v
      | ValueSome(Decimal _) ->
        Error $"`{n.Kind}` wants a whole number for '{slot}'{at(src, n)}"
      | ValueSome(Word w) ->
        Error
          $"`{n.Kind}` wants a whole number for '{slot}', got '{w}'{at(src, n)}"
      | ValueNone -> missing slot

    let takeWordCell slot : Result<struct (string * 'T), string> =
      match take slot with
      | ValueSome(Word w) ->
        (match surface.Words.TryGetValue w with
         | true, cell -> Ok(struct (w, cell))
         | false, _ ->
           Error
             $"`{n.Kind}` wants a cell word for '{slot}', got '{w}'{at(src, n)}")
      | _ -> Error $"`{n.Kind}` wants a cell word for '{slot}'{at(src, n)}"

    let takeCell slot =
      takeWordCell slot |> Result.map(fun struct (_, cell) -> cell)

    /// An area statement paints every cell of its box, so a word whose
    /// instance covers several cells has no single place to stand: only `set`
    /// states where one instance goes.
    let takeAreaCell slot =
      takeWordCell slot
      |> Result.bind(fun struct (word, cell) ->
        spanningOf surface cell
        |> ValueOption.map(fun span ->
          Error
            $"the word '{word}' spans {Occupancy.describe span}, so only set may place it{at(src, n)}")
        |> ValueOption.defaultValue(Ok cell))

    let takeKernel slot =
      match take slot with
      | ValueSome(Word w) ->
        if surface.Kernels.ContainsKey w then
          Ok w
        else
          Error $"unknown kernel '{w}'{at(src, n)}"
      | _ -> Error $"`{n.Kind}` wants a kernel name for '{slot}'{at(src, n)}"

    let takeAlign slot =
      match take slot with
      | ValueSome(Word w) ->
        (match axisWord w with
         | ValueSome a -> Ok a
         | ValueNone ->
           Error
             $"'{n.Kind}' wants an axis word (start, center, end, stretch) for '{slot}', got '{w}'{at(src, n)}")
      | _ -> Error $"'{n.Kind}' wants an axis word for '{slot}'{at(src, n)}"

    /// Reads the four rect slots in order; the first failure wins.
    /// Four mutable locals — no intermediate collection for four values.
    let takeRect() =
      let mutable failed = ValueNone
      let mutable x, y, w, h = 0, 0, 0, 0

      let put slot v =
        match slot with
        | "x" -> x <- v
        | "y" -> y <- v
        | "w" -> w <- v
        | _ -> h <- v

      let readSlot slot =
        if failed.IsNone then
          (match takeInt slot with
           | Ok v -> put slot v
           | Error e -> failed <- ValueSome e)

      readSlot "x"
      readSlot "y"
      readSlot "w"
      readSlot "h"

      match failed with
      | ValueSome e -> Error e
      | ValueNone -> Ok struct (x, y, w, h)

    let noLeftovers allowed =
      if positional.Count > 0 then
        Error $"'{n.Kind}' has {positional.Count} extra argument(s){at(src, n)}"
      else
        // one pass over the named keys; `allowed` is an array so the
        // membership test allocates nothing
        let mutable extra = ValueNone

        for k in named.Keys do
          if extra.IsNone && not(Array.contains k allowed) then
            extra <- ValueSome k

        match extra with
        | ValueSome k -> Error $"'{n.Kind}' has no argument '{k}'{at(src, n)}"
        | ValueNone -> Ok()

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

    let fillStatement cell = checkedOp [| "cell" |] (Op.Fill cell)

    let fillRectStatement(struct (x, y, w, h)) =
      takeAreaCell "cell"
      |> Result.bind(fun cell ->
        checkedOp
          [| "x"; "y"; "w"; "h"; "cell" |]
          (Op.FillRect(rectOf(x, y, w, h), cell)))

    /// The span a `set` states, written into the cell. A word carries the span
    /// it declares; a statement may size it instead, which needs a surface
    /// that can write one, and both sides of the box.
    let statedSpan (word: string) (cell: 'T) : Result<'T, string> =
      match named.ContainsKey "spanX", named.ContainsKey "spanZ" with
      | false, false -> Ok cell
      | true, false
      | false, true ->
        Error $"'set' states a span with both spanX and spanZ{at(src, n)}"
      | true, true ->
        match surface.WithSpan with
        | ValueNone ->
          Error
            $"the surface cannot state a span, so '{word}' keeps its own{at(src, n)}"
        | ValueSome withSpan ->
          takeInt "spanX"
          |> Result.bind(fun across ->
            takeInt "spanZ"
            |> Result.bind(fun deep ->
              match across, deep with
              | _ when across < 1 || deep < 1 ->
                Error
                  $"a span covers at least one cell: got spanX={across} spanZ={deep}{at(src, n)}"
              | _ ->
                match spanningOf surface cell with
                | ValueSome(Radius _) ->
                  Error
                    $"the word '{word}' is a hex span, so spanX and spanZ cannot size it{at(src, n)}"
                | _ -> Ok(withSpan cell (Span(across, deep)))))

    let setStatement() =
      // exact: `set x=1 y=2 cell` or `set 1 2 cell`; anchored:
      // `set hplace=center vplace=center cell` or the bare anchor
      // `set c` — the cell places by the two aligns, Start by default
      let coords =
        named.ContainsKey "x" || named.ContainsKey "y" || positional.Count = 3

      // one spelling per axis: the unpicked spelling of a mixed pair
      // would pass the leftover check unread
      let mixedSpelling =
        (named.ContainsKey "halign" && named.ContainsKey "hplace")
        || (named.ContainsKey "valign" && named.ContainsKey "vplace")

      let anchored =
        named.ContainsKey "halign"
        || named.ContainsKey "hplace"
        || named.ContainsKey "valign"
        || named.ContainsKey "vplace"

      if mixedSpelling then
        Error
          $"'set' takes one spelling per axis: halign/hplace, valign/vplace{at(src, n)}"
      elif coords && anchored then
        Error
          $"'set' places by x= y= coordinates or by alignment, not both{at(src, n)}"
      elif coords then
        takeInt "x"
        |> Result.bind(fun x ->
          takeInt "y"
          |> Result.bind(fun y ->
            takeWordCell "cell"
            |> Result.bind(fun struct (word, cell) ->
              statedSpan word cell
              |> Result.bind(fun cell ->
                checkedOp
                  [| "x"; "y"; "cell"; "spanX"; "spanZ" |]
                  (Op.Set(ValueSome { X = x; Y = y }, Start, Start, cell))))))
      elif anchored then
        let hName = if named.ContainsKey "halign" then "halign" else "hplace"

        let vName = if named.ContainsKey "valign" then "valign" else "vplace"

        takeAlign hName
        |> Result.bind(fun h ->
          takeAlign vName
          |> Result.bind(fun v ->
            takeCell "cell"
            |> Result.bind(fun cell ->
              checkedOp
                [| hName; vName; "cell" |]
                (Op.Set(ValueNone, h, v, cell)))))
      elif positional.Count = 1 then
        // the `set c` anchor form: no coordinates, the aligns default
        takeCell "cell"
        |> Result.bind(fun cell ->
          checkedOp [| "cell" |] (Op.Set(ValueNone, Start, Start, cell)))
      else
        Error
          $"'set' wants x= y= coordinates, hplace= vplace= alignment, or a cell to anchor{at(src, n)}"

    let borderStatement() =
      if usesRectArea then
        takeRect()
        |> Result.bind(fun struct (x, y, w, h) ->
          takeAreaCell "cell"
          |> Result.bind(fun cell ->
            checkedOp
              [| "x"; "y"; "w"; "h"; "cell" |]
              (Op.Border(ValueSome(rectOf(x, y, w, h)), cell))))
      else
        takeAreaCell "cell"
        |> Result.bind(fun cell ->
          checkedOp [| "cell" |] (Op.Border(ValueNone, cell)))

    let rectStatement() =
      takeAreaCell "edge"
      |> Result.bind(fun edge ->
        takeAreaCell "floor"
        |> Result.bind(fun floor ->
          checkedOp [| "edge"; "floor" |] (Op.Rect(edge, floor))))

    let generateStatement() =
      if usesRectArea then
        takeRect()
        |> Result.bind(fun struct (x, y, w, h) ->
          takeKernel "kernel"
          |> Result.bind(fun name ->
            checkedOp
              [| "x"; "y"; "w"; "h"; "kernel" |]
              (Op.Generate(name, ValueSome(rectOf(x, y, w, h))))))
      else
        takeKernel "kernel"
        |> Result.bind(fun name ->
          checkedOp [| "kernel" |] (Op.Generate(name, ValueNone)))

    match n.Kind with
    | "fill" -> takeAreaCell "cell" |> Result.bind fillStatement
    | "fillRect" -> takeRect() |> Result.bind fillRectStatement
    | "set" -> setStatement()
    | "border" -> borderStatement()
    | "rect" -> rectStatement()
    | "generate" -> generateStatement()
    // a layer body is not a statement: an `element` declaration that
    // holds one would paint nothing, so it fails where it is written
    | "layer" ->
      Error $"`layer` is legal only directly under the map{at(src, n)}"
    | _ -> Error $"unknown statement '{n.Kind}'{at(src, n)}"

  /// `resolveOp` behind the one-property-per-node rule.
  let private opOf
    (surface: Surface<'T>, src: string, n: Node)
    : Result<Op<'T>, string> =
    noRepeatedProps(src, n) |> Result.bind(fun () -> resolveOp(surface, src, n))

  // ── The interpreter ──────────────────────────────────────────

  /// Runs one statement's paint into `box`.
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
         // unreachable when validateSurface runs: a tripwire against a
         // resolver regression, not a user-facing path
         invalidOp
           $"unknown kernel '{name}' (the resolver must have let a bad name through)")

  /// Derives an element's extent from its body's own geometry: each
  /// statement contributes its furthest cell, and any box-relative
  /// statement (a whole-box fill, border, or generate) marks the body
  /// greedy — its size is whatever the container assigns. A `set` that
  /// places a spanning word contributes the whole instance, so an element
  /// reserves the cells its instance covers. Pure arithmetic over the `Op`
  /// data: no grid, no allocation. This is the measurement the emitter
  /// path uses; a `ValueNone` result means "greedy, stretch".
  let measureOps (surface: Surface<'T>) (body: Op<'T>[]) : CellSize voption =
    let mutable maxX = -1
    let mutable maxY = -1
    let mutable greedy = false

    for op in body do
      match op with
      | Op.Fill _ -> greedy <- true
      | Op.Rect _ -> greedy <- true
      | Op.Border(ValueNone, _) -> greedy <- true
      | Op.Generate(_, ValueNone) -> greedy <- true
      | Op.Set(ValueNone, _, _, _) -> greedy <- true
      | Op.FillRect(r, _) ->
        maxX <- max maxX (r.X + r.W - 1)
        maxY <- max maxY (r.Y + r.H - 1)
      | Op.Border(ValueSome r, _) ->
        maxX <- max maxX (r.X + r.W - 1)
        maxY <- max maxY (r.Y + r.H - 1)
      | Op.Generate(_, ValueSome r) ->
        maxX <- max maxX (r.X + r.W - 1)
        maxY <- max maxY (r.Y + r.H - 1)
      | Op.Set(ValueSome p, _, _, cell) ->
        let size = spanSize surface cell
        maxX <- max maxX (p.X + size.W - 1)
        maxY <- max maxY (p.Y + size.H - 1)

    if greedy || maxX < 0 then
      ValueNone
    else
      ValueSome { W = maxX + 1; H = maxY + 1 }

  /// The one closure per element: the body as data, interpreted at
  /// render time. The text produces nothing else that runs. The extent
  /// states a declared size; when none is declared, `measureOps`
  /// derives it from the body's own geometry (no allocation), so the
  /// emitter never needs the scratch-grid measure.
  let paintOf
    (surface: Surface<'T>, extent: CellSize voption, body: Op<'T>[])
    : Element<'T> =
    let extent =
      match extent with
      | ValueSome _ -> extent
      | ValueNone -> measureOps surface body

    {
      Extent = extent
      Body = body
      Paint =
        fun box ->
          for op in body do
            interpret(surface, box, op)
    }

  /// A stated size must agree with the extent a spanning body implies: the
  /// layout would reserve the stated box while the instance claims its own.
  /// Only a body that places a spanning word is checked, so every other
  /// element stretches as it always did, and only stated axes are compared —
  /// a zero axis means "stretch this axis" in the property channel.
  let private checkSpanSize
    (surface: Surface<'T>)
    (src: string)
    (n: Node)
    (name: string)
    (style: Style)
    (declExtent: CellSize voption)
    (body: Op<'T>[])
    : Result<unit, string> =
    if not(placesSpan surface body) then
      Ok()
    else
      match measureOps surface body with
      | ValueNone -> Ok()
      | ValueSome extent ->
        let axis (verb: string) (stated: int) (actual: int) (side: string) =
          if stated > 0 && stated <> actual then
            Error
              $"the element '{name}' {verb} {stated}, but the span in its body covers {actual} cells {side}{at(src, n)}"
          else
            Ok()

        axis "states w=" (wOf style.Size) extent.W "across"
        |> Result.bind(fun () ->
          axis "states h=" (hOf style.Size) extent.H "deep")
        |> Result.bind(fun () ->
          axis "declares w=" (wOf declExtent) extent.W "across")
        |> Result.bind(fun () ->
          axis "declares h=" (hOf declExtent) extent.H "deep")

  // ── Measurement ──────────────────────────────────────────────

  /// Scratch-grid run, then a scan for the covered area — the general
  /// measurement for bodies the op pass cannot see (game-declared
  /// elements with custom paint). `available` bounds the scratch grid,
  /// so greedy bodies report the available size, not an unbounded one.
  /// The resolver's op-derived extents cover the document path; this
  /// runs only for `Element<'T>` values a game builds by hand.
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

  /// `repeat n` duplicates its children, nested repeats included. The
  /// count is read by name (`count=`) or positionally. Two caps hold, a
  /// per-`repeat` count cap and a 100000-node cap on the expansion's
  /// total output — nested repeats multiply, so `repeat 100000 {
  /// repeat 100000 { x } }` fails on the running total instead of
  /// looping until memory stops. Returns an array; every consumer
  /// iterates.
  let expandNodes
    (src: string, nodes: ImmutableArray<Node>)
    : Result<Node[], string> =
    let out = ResizeArray<Node>()
    let mutable failed = ValueNone

    let rec go(nodes: ImmutableArray<Node>) : unit =
      if failed.IsNone then
        for n in nodes do
          if failed.IsNone then
            if n.Kind = "repeat" then
              (match wantInt(src, n, "count", 0) with
               | Error e -> failed <- ValueSome e
               | Ok count ->
                 if count > 100000 then
                   failed <-
                     ValueSome
                       $"repeat count {count} is past the 100000 cap{at(src, n)}"
                 else
                   // one cap check per child emission: the running total
                   // is what nesting multiplies, so it is what stops it
                   let mutable i = 0

                   while i < count && failed.IsNone do
                     if out.Count > 100000 then
                       failed <-
                         ValueSome
                           $"the repeat expansion is past the 100000 node cap{at(src, n)}"
                     else
                       go n.Children

                     i <- i + 1)
            else
              out.Add n

    go nodes

    match failed with
    | ValueSome e -> Error e
    | ValueNone -> Ok(out.ToArray())

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
      let mutable next = 0

      // a named slot claims its value and a positional value fills the
      // next free slot, so `element hut 3 3`, `element hut w=3 h=3`,
      // and one of each read the same; the declaration's own name is
      // already off the argument list
      let take(name: string) : Arg voption =
        match propOf(src, n, name) with
        | Error e ->
          failed <- ValueSome e
          ValueNone
        | Ok(ValueSome v) -> ValueSome v
        | Ok ValueNone ->
          if next < n.Args.Length then
            let v = n.Args[next]
            next <- next + 1
            ValueSome v
          else
            ValueNone

      let wArg = take "w"
      let hArg = take "h"

      if failed.IsNone then
        for p in n.Props do
          if failed.IsNone && p.Name <> "w" && p.Name <> "h" then
            failed <-
              ValueSome
                $"an element definition takes w= and h= only, got '{p.Name}'{at(src, n)}"

      if failed.IsNone && n.Args.Length > next then
        failed <-
          ValueSome
            $"'element' has {n.Args.Length - next} extra argument(s){at(src, n)}"

      if failed.IsNone then
        let asInt (name: string) (v: Arg voption) =
          match v with
          | ValueSome(Number v) -> Ok v
          | ValueSome(Decimal _) ->
            Error
              $"an element definition's {name}= wants a whole number{at(src, n)}"
          | ValueSome(Word w) ->
            Error
              $"an element definition's {name}= wants a whole number, got '{w}'{at(src, n)}"
          | ValueNone ->
            Error $"an element definition needs w= and h= together{at(src, n)}"

        match wArg, hArg with
        | ValueNone, ValueNone -> ValueNone
        | _ ->
          match asInt "w" wArg, asInt "h" hArg with
          | Ok w', Ok h' -> ValueSome { W = w'; H = h' }
          | Error e, _ ->
            failed <- ValueSome e
            ValueNone
          | _, Error e ->
            failed <- ValueSome e
            ValueNone
      else
        ValueNone

    let rec go(n: Node) : unit =
      if failed.IsNone then
        if n.Kind = "element" then
          (match n.Label with
           | ValueSome name ->
             let extent =
               if Array.contains name reservedNames then
                 failed <-
                   ValueSome
                     $"'{name}' is a name the markup vocabulary owns, so an element cannot take it{at(src, n)}"

                 ValueNone
               else
                 extentOf n

             if failed.IsNone then
               (match expandNodes(src, n.Children) with
                | Error e -> failed <- ValueSome e
                | Ok expanded ->
                  let body = ResizeArray<Op<'T>>()

                  for c in expanded do
                    if failed.IsNone then
                      (match opOf(surface, src, c) with
                       | Ok op -> body.Add op
                       | Error e -> failed <- ValueSome e)

                  if failed.IsNone then
                    if defs.ContainsKey name then
                      failed <-
                        ValueSome
                          $"the element '{name}' is defined twice{at(src, n)}"
                    elif surface.Elements.ContainsKey name then
                      failed <-
                        ValueSome
                          $"the element '{name}' collides with the game's surface element of that name{at(src, n)}"
                    else
                      defs[name] <- {
                        Name = name
                        Extent = extent
                        Body = body.ToArray()
                      })
           | ValueNone ->
             failed <-
               ValueSome $"an element definition needs a name{at(src, n)}")
        else
          for c in n.Children do
            go c

    for r in roots do
      go r

    match failed with
    | ValueSome e -> Error e
    | ValueNone -> Ok defs

  /// Collects `style name ...` rules — the name by word argument (KDL)
  /// or by the `name` property (XML). Later rules win per property; a
  /// bad property fails the build with its position. Like the template
  /// collector, a declaration is a leaf: rules do not nest.
  ///
  /// The `name` property names the rule; it is a key, not a style, so it
  /// is dropped before the properties are applied. A KDL rule states its
  /// name as a word argument and never carries the property.
  let collectStyles
    (src: string, roots: ImmutableArray<Node>)
    : Result<Dictionary<string, Style>, string> =
    let rules = Dictionary<string, Style>()
    let mutable failed = ValueNone

    let rec go(n: Node) : unit =
      if failed.IsNone then
        if n.Kind = "style" then
          (match wantWord(src, n, "name", 0) with
           | Error e -> failed <- ValueSome e
           | Ok name ->
             let properties =
               n.Props
               |> Seq.filter(fun p -> p.Name <> "name")
               |> ImmutableArray.CreateRange

             let node = { n with Props = properties }

             (match styleOfProps(src, node, emptyStyle) with
              | Ok patch ->
                let merged =
                  if rules.ContainsKey name then
                    mergeStyle(rules[name], patch)
                  else
                    patch

                rules[name] <- merged
              | Error e -> failed <- ValueSome e))
        else
          for c in n.Children do
            go c

    for r in roots do
      go r

    match failed with
    | ValueSome e -> Error e
    | ValueNone -> Ok rules

  // ── Layers ───────────────────────────────────────────────────

  /// Reads a layer container's single name: a word argument in KDL
  /// (`layer ground { ... }`), the `name` property in XML
  /// (`<layer name="ground">`). A layer carries nothing else, so any
  /// other argument or property fails with its position.
  let layerNameOf(src: string, n: Node) : Result<string, string> =
    let asWord(arg: Arg) : string =
      match arg with
      | Word w -> w
      | Number v -> string v
      | Decimal d -> string d

    let mutable named = ValueNone

    for p in n.Props do
      if named.IsNone && p.Name = "name" then
        named <- ValueSome p.Value

    // the name claims one slot: the property when the node states it,
    // the first argument otherwise. A list property belongs to the
    // container, so it is not extra; everything else is an error.
    let fromProperty = named.IsSome
    let extraArgs = if fromProperty then n.Args.Length else n.Args.Length - 1

    let extraProps =
      n.Props
      |> Seq.filter(fun p -> p.Name <> "name" && not(isListProp p.Name))
      |> Seq.length

    if extraArgs > 0 then
      Error
        $"a layer takes one name and nothing else: {extraArgs} extra argument(s){at(src, n)}"
    elif extraProps > 0 then
      Error
        $"a layer takes one name and nothing else: {extraProps} extra property(ies){at(src, n)}"
    else
      match named with
      | ValueSome arg -> Ok(asWord arg)
      | ValueNone when n.Args.Length = 1 -> Ok(asWord n.Args[0])
      | ValueNone ->
        Error
          $"a layer needs a name: a word argument, or the 'name' property{at(src, n)}"

  /// A layer must paint something. `element` and `style` declarations are
  /// leaves the collectors already read, so on their own they leave the
  /// layer empty — and an empty layer would silently paint nothing.
  let private hasContent(n: Node) : bool =
    n.Children |> Seq.exists(fun c -> c.Kind <> "element" && c.Kind <> "style")

  /// Resolves one `layer` child into the entry the map's child list holds:
  /// the node, and the name it reports under. `seen` is the map's
  /// seen-names list — `ValueNone` for every other container, where a
  /// layer is illegal.
  ///
  /// The three rules a layer must pass are one pipeline: the name reads
  /// (or fails with its position), the name is new, the layer holds
  /// content. The node loses its `name` property on the way out: that
  /// property is the layer's key, not a style, so the cascade never sees
  /// it.
  let private layerChild
    (src: string)
    (seen: ResizeArray<string> voption)
    (c: Node)
    : Result<struct (Node * string voption), string> =
    match seen with
    | ValueNone ->
      Error $"`layer` is legal only directly under the map{at(src, c)}"
    | ValueSome names ->
      layerNameOf(src, c)
      |> Result.bind(fun name ->
        if names.Contains name then
          Error $"layer '{name}' is declared twice{at(src, c)}"
        elif not(hasContent c) then
          Error $"layer '{name}' holds no statements or children{at(src, c)}"
        else
          names.Add name

          let stripped = {
            c with
                Props =
                  c.Props
                  |> Seq.filter(fun p -> p.Name <> "name")
                  |> ImmutableArray.CreateRange
          }

          Ok struct (stripped, ValueSome name))

  // ── Container resolution ─────────────────────────────────────

  /// Resolves one container node into an `Item`: expands repeats,
  /// cascades the style (rule sheet, then inline props), splits the
  /// children into paint statements (this element's body), track and
  /// area directives, and child containers. Declarations (`element`,
  /// `style`) are leaves here — their collectors ran first. A `layer`
  /// child is legal only under the map root; it resolves under the
  /// literal name `layer` and the item is stamped with the layer's own
  /// name.
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
      // the list channel: cols, rows and areas are the container's own
      // properties. They are read here, and the node the cascade sees
      // below carries none of them.
      let mutable cols: Track[] = [||]
      let mutable rows: Track[] = [||]
      let mutable areas: string[][] = [||]

      let mutable failed =
        match noRepeatedProps(src, n) with
        | Ok() -> ValueNone
        | Error e -> ValueSome e

      if failed.IsNone then
        (match tracksOf(src, n, "cols") with
         | Ok v -> cols <- ValueOption.defaultValue [||] v
         | Error e -> failed <- ValueSome e)

      if failed.IsNone then
        (match tracksOf(src, n, "rows") with
         | Ok v -> rows <- ValueOption.defaultValue [||] v
         | Error e -> failed <- ValueSome e)

      if failed.IsNone then
        (match areasOf(src, n) with
         | Ok v -> areas <- ValueOption.defaultValue [||] v
         | Error e -> failed <- ValueSome e)

      // the template and the columns are both in hand here, so a row
      // wider than the declared columns fails where it is written
      if failed.IsNone then
        let colCount = if cols.Length = 0 then 1 else cols.Length

        for row in areas do
          if failed.IsNone && row.Length > colCount then
            failed <-
              ValueSome
                $"in the '{name}' container: an area row has {row.Length} names but {colCount} column(s){at(src, n)}"

      let styleNode = {
        n with
            Props =
              n.Props
              |> Seq.filter(fun p -> not(isListProp p.Name))
              |> ImmutableArray.CreateRange
      }

      // cascade: solver defaults, document style rules, inline props
      let ruleStyle =
        if styles.ContainsKey name then styles[name] else emptyStyle

      match failed, styleOfProps(src, styleNode, ruleStyle) with
      | ValueSome e, _ -> Error e
      | ValueNone, Error e -> Error e
      | ValueNone, Ok style ->
        let ops = ResizeArray<Op<'T>>()
        let itemNodes = ResizeArray<struct (Node * string voption)>()

        // layer names are unique in the document. Only the map root can
        // hold layers, so only it allocates the seen-names list.
        let seenLayers =
          if name = "map" then
            ValueSome(ResizeArray<string>())
          else
            ValueNone

        for c in children do
          if failed.IsNone then
            if isListProp c.Kind then
              // tracks and templates are the container's own properties
              let example =
                if c.Kind = "areas" then
                  "areas=\"road woods; road lake\""
                else
                  $"{c.Kind}=\"auto fixed 3\""

              failed <-
                ValueSome
                  $"'{c.Kind}' is a property of the container: state it as {example}{at(src, c)}"
            elif c.Kind = "element" || c.Kind = "style" then
              () // declarations, already collected
            elif c.Kind = "layer" then
              (match layerChild src seenLayers c with
               | Ok node -> itemNodes.Add node
               | Error e -> failed <- ValueSome e)
            elif isOpKind c.Kind then
              (match opOf(surface, src, c) with
               | Ok op -> ops.Add op
               | Error e -> failed <- ValueSome e)
            else
              let known =
                templates.ContainsKey c.Kind
                || surface.Elements.ContainsKey c.Kind
                || c.Kind = "plot"
                || c.Kind = "grid"

              if known then
                itemNodes.Add struct (c, ValueNone)
              else
                failed <- ValueSome $"unknown element '{c.Kind}'{at(src, c)}"

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
            // Unchecked.defaultof feeds the byref out-slot of
            // TryGetValue; the bool return gates every read of it
            let mutable d = Unchecked.defaultof<ElementDecl<'T>>

            if surface.Elements.TryGetValue(name, &d) then
              declBody <- d.Body
              declExtent <- d.Extent

          let body = Array.append declBody (ops.ToArray())
          let items = ResizeArray<Item<'T>>()

          for struct (c, layerName) in itemNodes do
            if failed.IsNone then
              // A layer resolves under the literal name `layer`, the way a
              // plot resolves under `plot`: resolving it under its own name
              // would inherit a same-named template's or surface element's
              // body and extent.
              let asName =
                match layerName with
                | ValueSome _ -> "layer"
                | ValueNone -> c.Kind

              (match
                resolveContainer(surface, src, templates, styles, asName, c)
               with
               | Ok item ->
                 let resolved =
                   match layerName with
                   | ValueSome layerName ->
                       // the layer reports its rectangle under its own name
                       {
                         item with
                             Name = layerName
                             Layer = ValueSome layerName
                       }
                   | ValueNone -> item

                 items.Add resolved
               | Error e -> failed <- ValueSome e)

          match failed with
          | ValueSome e -> Error e
          | ValueNone ->
            checkSpanSize surface src n name style declExtent body
            |> Result.map(fun () -> {
              Name = name
              Layer = ValueNone
              Element = paintOf(surface, declExtent, body)
              Style = style
              Cols = cols
              Rows = rows
              Areas = areas
              Children = items.ToArray()
            })

  // ── Entry ────────────────────────────────────────────────────

  /// Locates the document's map container. Its dimensions ride
  /// positional args (`map 36 20`) or the `w=`/`h=` properties
  /// (`map w="36" h="20"`).
  let findMapNode(nodes: ImmutableArray<Node>) : Node voption =
    let isMap(n: Node) =
      if n.Kind <> "map" then
        false
      else
        // the two dimensions come from positional args, `w=`/`h=`
        // properties, or one of each: the count decides, not the channel
        let mutable named = 0

        for p in n.Props do
          if p.Name = "w" || p.Name = "h" then
            named <- named + 1

        n.Args.Length + named >= 2

    let rec go(n: Node) : Node voption =
      if isMap n then
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

  /// Reads and validates the map node's dimensions: `w=`/`h=` properties
  /// (the XML channel), positional args (the KDL channel), or one of
  /// each. A named dimension claims its slot, so `map 36 h=20` reads 36
  /// across and 20 down instead of failing on a mixed document. A
  /// doubled dimension, leftover args, and zero or negative dimensions
  /// fail with a position.
  let dimsOf(src: string, n: Node) : Result<CellSize, string> =
    let mutable w = ValueNone
    let mutable h = ValueNone
    let mutable failed = ValueNone

    for p in n.Props do
      if failed.IsNone then
        match p.Name with
        | "w" ->
          // a doubled dimension is an authoring typo, not a shadowing
          // rule — statements resolve last-wins, so first-wins silence
          // here would quietly disagree with every other argument
          (match w with
           | ValueNone -> w <- ValueSome p.Value
           | ValueSome _ ->
             failed <-
               ValueSome $"`{n.Kind}` defines 'w' more than once{at(src, n)}")
        | "h" ->
          (match h with
           | ValueNone -> h <- ValueSome p.Value
           | ValueSome _ ->
             failed <-
               ValueSome $"`{n.Kind}` defines 'h' more than once{at(src, n)}")
        | _ -> ()

    match failed with
    | ValueSome e -> Error e
    | ValueNone ->
      let mutable next = 0

      let take() =
        if next < n.Args.Length then
          let a = n.Args[next]
          next <- next + 1
          ValueSome a
        else
          ValueNone

      if w.IsNone then
        w <- take()

      if h.IsNone then
        h <- take()

      let asInt(slot: string, v: Arg voption) =
        match v with
        | ValueSome(Number v) -> Ok v
        | ValueSome(Decimal _) ->
          Error
            $"`{n.Kind}` wants a whole number for '{slot}', not a decimal{at(src, n)}"
        | ValueSome(Word word) ->
          Error
            $"`{n.Kind}` wants a whole number for '{slot}', got '{word}'{at(src, n)}"
        | ValueNone -> Error $"'{n.Kind}' wants '{slot}'{at(src, n)}"

      if next < n.Args.Length then
        Error
          $"'{n.Kind}' has {n.Args.Length - next} extra argument(s){at(src, n)}"
      else
        (match asInt("w", w) with
         | Error e -> Error e
         | Ok wv ->
           (match asInt("h", h) with
            | Error e -> Error e
            | Ok hv ->
              if wv > 0 && hv > 0 then
                Ok { W = wv; H = hv }
              else
                Error $"map dimensions must be positive{at(src, n)}"))

  /// Game-declared element bodies never pass through statement
  /// resolution, so their kernel references and their area words get
  /// checked here: a typo, or a spanning word that an area statement
  /// cannot place, must fail the build rather than vanish at render.
  let validateSurface(surface: Surface<'T>) : Result<unit, string> =
    let areaSpan (decl: ElementDecl<'T>) (cell: 'T) : string voption =
      spanningOf surface cell
      |> ValueOption.map(fun span ->
        $"the element '{decl.Name}' paints an area with a word that spans {Occupancy.describe span}; only set may place a spanning word")

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
          | Op.Fill cell
          | Op.FillRect(_, cell)
          | Op.Border(_, cell) -> areaSpan decl cell |> ValueOption.toOption
          | Op.Rect(edge, floor) ->
            areaSpan decl edge
            |> ValueOption.orElse(areaSpan decl floor)
            |> ValueOption.toOption
          | _ -> None))
      |> Seq.tryHead

    // a surface element under a reserved kind would shadow the node the
    // resolver means to build
    let reserved =
      surface.Elements.Keys
      |> Seq.tryFind(fun name -> Array.contains name reservedNames)
      |> Option.map(fun name ->
        $"the surface element '{name}' uses a name the markup vocabulary owns")

    match reserved |> Option.orElse bad with
    | Some e -> Error e
    | None -> Ok()

  /// Templates expand, words resolve, style merges. Errors carry line
  /// and column when the front-end tracks positions (`Markup.where`
  /// turns each node's offset into them; XML nodes carry no positions,
  /// so their errors name the element).
  ///
  /// Cascade: solver defaults, then document `style` rules in order,
  /// then inline properties. `map` is the root container (its `w=`/`h=`
  /// dimensions are validated here); `element` nodes define templates
  /// (name as first argument or `name` property); `repeat n` duplicates
  /// its children; the `cols`, `rows` and `areas` properties of a
  /// container declare its tracks and named areas, and a `style` rule
  /// cannot state one. Declared `cols`/`rows` imply `pack=flow`. `plot`
  /// is the built-in anonymous container: empty
  /// body, exact box. `layer` is the map's layer container: one name and
  /// nothing else, resolved under the literal name `layer` and reported
  /// under its own (`Item.Layer`). Every root is the map or a
  /// declaration: a stray root node would be dropped without a word, so
  /// it fails the build.
  let resolve
    (surface: Surface<'T>)
    (src: string)
    (roots: ImmutableArray<Node>)
    : Result<Item<'T>[], string> =
    let mutable strayRoot = ValueNone
    let mutable mapRoots = 0

    for n in roots do
      if n.Kind = "map" then
        mapRoots <- mapRoots + 1
      elif n.Kind <> "element" && n.Kind <> "style" && strayRoot.IsNone then
        strayRoot <- ValueSome n

    let withDecls templates =
      match collectStyles(src, roots) with
      | Error e -> Error e
      | Ok styles ->
        match findMapNode roots with
        | ValueNone ->
          Error
            "the document needs a map node with two dimensions: `map 36 20`, `map w=36 h=20`, or one of each"
        | ValueSome mapNode ->
          dimsOf(src, mapNode)
          |> Result.bind(fun _ ->
            resolveContainer(surface, src, templates, styles, "map", mapNode)
            |> Result.map(fun root -> [| root |]))

    match strayRoot, mapRoots with
    | ValueSome n, _ ->
      Error
        $"'{n.Kind}' is not a root node: a document holds one map and its element and style declarations{at(src, n)}"
    | ValueNone, m when m > 1 ->
      Error $"the document holds {m} map nodes; one document is one map"
    | ValueNone, _ ->
      validateSurface surface
      |> Result.bind(fun () -> collectTemplates(surface, src, roots))
      |> Result.bind withDecls
