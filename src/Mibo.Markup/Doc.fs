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
  /// template. Carries a paint closure, so it never takes structural
  /// equality.
  [<NoEquality; NoComparison>]
  type Element<'T> = {
    Extent: CellSize voption
    Paint: GridSection2D<'T> -> unit
  }

  /// One resolved item of the document tree. `Name` selects the style
  /// rules; `Element` paints the body; `Cols`, `Rows` and `Areas` are
  /// the container's declared tracks and area template. Carries a paint
  /// closure, so it never takes structural equality.
  [<NoEquality; NoComparison>]
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

  // The positioned half of an error message; XML nodes carry Position
  // -1 (that front-end tracks no positions), so the message names the
  // element only.
  let at(src: string, n: Node) : string = Markup.at src n.Position

  /// Reads one whole-number scalar by name first (the XML channel),
  /// then by positional index (the KDL channel).
  let wantInt
    (src: string, n: Node, name: string, i: int)
    : Result<int, string> =
    let bad why =
      Error $"`{n.Kind}` wants a whole number for '{name}', {why}{at(src, n)}"

    let mutable slot = ValueNone

    for p in n.Props do
      if slot.IsNone && p.Name = name then
        slot <- ValueSome p.Value

    match slot with
    | ValueNone ->
      if i >= 0 && i < n.Args.Length then
        match n.Args[i] with
        | Number v -> Ok v
        | Decimal _ -> bad "not a decimal"
        | Word w -> bad $"got '{w}'"
      else
        Error $"'{n.Kind}' wants '{name}'{at(src, n)}"
    | ValueSome(Number v) -> Ok v
    | ValueSome(Decimal _) -> bad "not a decimal"
    | ValueSome(Word w) -> bad $"got '{w}'"

  /// Reads one word scalar by name first, then by positional index.
  let wantWord
    (src: string, n: Node, name: string, i: int)
    : Result<string, string> =
    let read(arg: Arg) : string voption =
      match arg with
      | Word w -> ValueSome w
      | Number v -> ValueSome(string v)
      | Decimal d -> ValueSome(string d)

    let mutable slot = ValueNone

    for p in n.Props do
      if slot.IsNone && p.Name = name then
        slot <- ValueSome p.Value

    match slot with
    | ValueNone ->
      if i >= 0 && i < n.Args.Length then
        match read n.Args[i] with
        | ValueSome w -> Ok w
        | ValueNone -> Error $"'{n.Kind}' wants '{name}'{at(src, n)}"
      else
        Error $"'{n.Kind}' wants '{name}'{at(src, n)}"
    | ValueSome arg ->
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
    | _ -> Error $"unknown property '{p.Name}'{at(src, n)}"

  let styleOfProps
    (src: string, n: Node, baseStyle: Style)
    : Result<Style, string> =
    n.Props
    |> Seq.fold
      (fun acc p -> acc |> Result.bind(fun s -> applyProp(src, n, s, p)))
      (Ok baseStyle)

  // ── Track and area directives ────────────────────────────────

  /// One track token: a positive ratio, `auto`, or `fixed n`. Track
  /// tokens come from positional args (KDL: `cols 1 fixed 3 auto`) or
  /// from the space-split `v` property (XML: `<cols v="1 fixed 3 auto" />`).
  let private trackTokens(src: string, n: Node) : Result<Arg[], string> =
    if n.Args.Length > 0 then
      Ok(Seq.toArray n.Args)
    else
      let mutable slot = ValueNone

      for p in n.Props do
        if slot.IsNone && (p.Name = "v" || p.Name = "tracks") then
          slot <- ValueSome p.Value

      match slot with
      | ValueSome(Word w) ->
        w.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map(fun t ->
          match
            Int32.TryParse(
              t,
              Globalization.NumberStyles.Integer,
              Globalization.CultureInfo.InvariantCulture
            )
          with
          | true, v -> Number v
          | _ -> Word t)
        |> Ok
      | ValueSome _ -> Error $"a track list wants words{at(src, n)}"
      | ValueNone -> Ok [||]

  /// Resolves a container's declared tracks.
  let tracksOf(src: string, n: Node) : Result<Track[], string> =
    match trackTokens(src, n) with
    | Error e -> Error e
    | Ok tokens ->
      let tracks = ResizeArray<Track>()
      let mutable i = 0
      let mutable failed = ValueNone

      while i < tokens.Length && failed.IsNone do
        match tokens[i] with
        | Number v when v > 0 ->
          tracks.Add(Weight(float32 v))
          i <- i + 1
        | Number _ ->
          failed <- ValueSome $"track ratios must be positive{at(src, n)}"
        | Word "auto" ->
          tracks.Add Auto
          i <- i + 1
        | Word "fixed" when i + 1 < tokens.Length ->
          (match tokens[i + 1] with
           | Number v when v > 0 ->
             tracks.Add(Fixed v)
             i <- i + 2
           | _ ->
             failed <-
               ValueSome $"a fixed track wants a positive number{at(src, n)}")
        | Word w ->
          failed <-
            ValueSome
              $"a track wants a ratio, 'fixed n' or 'auto', got '{w}'{at(src, n)}"
        | _ -> failed <- ValueSome $"bad track{at(src, n)}"

      match failed with
      | ValueSome e -> Error e
      | ValueNone -> Ok(tracks.ToArray())

  /// Resolves an area template's rows. Row names come from a child
  /// node's positional args (KDL: `areas { r west main } { ... }`) or
  /// from each row's space-split `names` property (XML:
  /// `<areas><row names="west main" /><row names="..." /></areas>`).
  let areasOf(src: string, n: Node) : Result<string[][], string> =
    let rows = ResizeArray<string[]>()
    let mutable failed = ValueNone

    for row in n.Children do
      if failed.IsNone then
        let names = ResizeArray<string>()

        if row.Args.Length > 0 then
          for a in row.Args do
            if failed.IsNone then
              (match a with
               | Word w -> names.Add w
               | _ ->
                 failed <- ValueSome $"an area row wants names{at(src, row)}")
        else
          let mutable slot = ValueNone

          for p in row.Props do
            if slot.IsNone && p.Name = "names" then
              slot <- ValueSome p.Value

          match slot with
          | ValueSome(Word w) ->
            for part in
              w.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries) do
              names.Add part
          | ValueSome _ ->
            failed <- ValueSome $"an area row's names want words{at(src, row)}"
          | ValueNone -> ()

        if failed.IsNone && names.Count > 0 then
          rows.Add(names.ToArray())

    match failed with
    | ValueSome e -> Error e
    | ValueNone -> Ok(rows.ToArray())

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

    let takeCell slot =
      match take slot with
      | ValueSome(Word w) ->
        (match surface.Words.TryGetValue w with
         | true, cell -> Ok cell
         | false, _ ->
           Error
             $"`{n.Kind}` wants a cell word for '{slot}', got '{w}'{at(src, n)}")
      | _ -> Error $"`{n.Kind}` wants a cell word for '{slot}'{at(src, n)}"

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
      takeCell "cell"
      |> Result.bind(fun cell ->
        checkedOp
          [| "x"; "y"; "w"; "h"; "cell" |]
          (Op.FillRect(rectOf(x, y, w, h), cell)))

    let setStatement() =
      // exact: `set x=1 y=2 cell` or `set 1 2 cell`; anchored:
      // `set hplace=center vplace=center cell` or the bare anchor
      // `set c` — the cell places by the two aligns, Start by default
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
                [|
                  "x"
                  "y"
                  "cell"
                  "halign"
                  "valign"
                  "hplace"
                  "vplace"
                |]
                (Op.Set(ValueSome { X = x; Y = y }, Start, Start, cell)))))
      elif anchored then
        takeAlign hName
        |> Result.bind(fun h ->
          takeAlign vName
          |> Result.bind(fun v ->
            takeCell "cell"
            |> Result.bind(fun cell ->
              checkedOp
                [| "halign"; "valign"; "hplace"; "vplace"; "cell" |]
                (Op.Set(ValueNone, h, v, cell)))))
      elif positional.Count = 1 then
        // the `set c` anchor form: no coordinates, the aligns default
        takeCell "cell"
        |> Result.bind(fun cell ->
          checkedOp
            [| "cell"; "halign"; "valign"; "hplace"; "vplace" |]
            (Op.Set(ValueNone, Start, Start, cell)))
      else
        Error
          $"'set' wants x= y= coordinates, hplace= vplace= alignment, or a cell to anchor{at(src, n)}"

    let borderStatement() =
      if usesRectArea then
        takeRect()
        |> Result.bind(fun struct (x, y, w, h) ->
          takeCell "cell"
          |> Result.bind(fun cell ->
            checkedOp
              [| "x"; "y"; "w"; "h"; "cell" |]
              (Op.Border(ValueSome(rectOf(x, y, w, h)), cell))))
      else
        takeCell "cell"
        |> Result.bind(fun cell ->
          checkedOp [| "cell" |] (Op.Border(ValueNone, cell)))

    let rectStatement() =
      takeCell "edge"
      |> Result.bind(fun edge ->
        takeCell "floor"
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
    | "fill" -> takeCell "cell" |> Result.bind fillStatement
    | "fillRect" -> takeRect() |> Result.bind fillRectStatement
    | "set" -> setStatement()
    | "border" -> borderStatement()
    | "rect" -> rectStatement()
    | "generate" -> generateStatement()
    | _ -> Error $"unknown statement '{n.Kind}'{at(src, n)}"

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
  /// greedy — its size is whatever the container assigns. Pure
  /// arithmetic over the `Op` data: no grid, no allocation. This is
  /// the measurement the emitter path uses; a `ValueNone` result means
  /// "greedy, stretch".
  let measureOps(body: Op<'T>[]) : CellSize voption =
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
      | Op.Set(ValueSome p, _, _, _) ->
        maxX <- max maxX p.X
        maxY <- max maxY p.Y

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
      | ValueNone -> measureOps body

    {
      Extent = extent
      Paint =
        fun box ->
          for op in body do
            interpret(surface, box, op)
    }

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
                 $"an element definition's w= and h= want numbers{at(src, n)}"
           | _ ->
             failed <-
               ValueSome
                 $"an element definition takes w= and h= only{at(src, n)}")

      match w, h with
      | ValueSome w', ValueSome h' -> ValueSome { W = w'; H = h' }
      | ValueNone, ValueNone -> ValueNone
      | _ ->
        failed <-
          ValueSome
            $"an element definition needs w= and h= together{at(src, n)}"

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
             (match styleOfProps(src, n, emptyStyle) with
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

  // ── Container resolution ─────────────────────────────────────

  /// Resolves one container node into an `Item`: expands repeats,
  /// cascades the style (rule sheet, then inline props), splits the
  /// children into paint statements (this element's body), track and
  /// area directives, and child containers. Declarations (`element`,
  /// `style`) are leaves here — their collectors ran first.
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

  /// Locates the document's map container. Its dimensions ride
  /// positional args (`map 36 20`) or the `w=`/`h=` properties
  /// (`map w="36" h="20"`).
  let findMapNode(nodes: ImmutableArray<Node>) : Node voption =
    let isMap(n: Node) =
      if n.Kind <> "map" then
        false
      else
        // one pass finds both named dimensions
        let mutable w = false
        let mutable h = false

        for p in n.Props do
          if p.Name = "w" then
            w <- true
          elif p.Name = "h" then
            h <- true

        n.Args.Length >= 2 || (w && h)

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

  /// Reads and validates the map node's dimensions: `w=`/`h=` by name
  /// (the XML channel) or the first two positional args (the KDL
  /// channel). Zero and negative dimensions fail with a position, and
  /// positional args beyond the two dimension slots are leftovers, not
  /// silent drops.
  let dimsOf(src: string, n: Node) : Result<CellSize, string> =
    // each named dimension closes its positional slot
    let mutable slots = 0

    for p in n.Props do
      if p.Name = "w" || p.Name = "h" then
        slots <- slots + 1

    let slots = 2 - min 2 slots

    if n.Args.Length > slots then
      Error
        $"'{n.Kind}' has {n.Args.Length - slots} extra argument(s){at(src, n)}"
    else
      (match wantInt(src, n, "w", 0) with
       | Error e -> Error e
       | Ok w ->
         (match wantInt(src, n, "h", 1) with
          | Error e -> Error e
          | Ok h ->
            if w > 0 && h > 0 then
              Ok { W = w; H = h }
            else
              Error $"map dimensions must be positive{at(src, n)}"))

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
  /// and column when the front-end tracks positions (`Markup.where`
  /// turns each node's offset into them; XML nodes carry no positions,
  /// so their errors name the element).
  ///
  /// Cascade: solver defaults, then document `style` rules in order,
  /// then inline properties. `map` is the root container (its `w=`/`h=`
  /// dimensions are validated here); `element` nodes define templates
  /// (name as first argument or `name` property); `repeat n` duplicates
  /// its children; `cols`, `rows` and `areas` child nodes declare a
  /// container's tracks and named areas. Declared `cols`/`rows` imply
  /// `pack=flow`. `plot` is the built-in anonymous container: empty
  /// body, exact box.
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
          dimsOf(src, mapNode)
          |> Result.bind(fun _ ->
            resolveContainer(surface, src, templates, styles, "map", mapNode)
            |> Result.map(fun root -> [| root |]))

    validateSurface surface
    |> Result.bind(fun () -> collectTemplates(surface, src, roots))
    |> Result.bind withDecls
