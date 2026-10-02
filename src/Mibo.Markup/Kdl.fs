namespace Mibo.Markup

open System
open System.Collections.Immutable
open System.Globalization
open System.IO
open KdlSharp
open KdlSharp.Parsing

/// The KDL front-end. Produces the format-neutral `Node` tree from KDL
/// 2.0 text on KdlSharp's token reader — the reader does all the
/// parsing, and node positions come from the reader's own line and
/// column, converted to character offsets against the source:
///
/// - a node is `kind args? props? { children }?`
/// - `name=value` pairs are properties (the layout channel); bare values
///   are positional args
/// - a node whose kind is `element` carries its template name as the
///   first word argument (moved to `Node.Label`)
/// - slashdash `/-` comments out a whole node — parsed and dropped, so a
///   bad body still errors
///
/// KDL's typed literals (hex, underscores, quoted numbers) have no XML
/// equivalent; XML attributes type by content. Unclosed and unbalanced
/// braces fail visibly, and adapter failures carry the reader's line
/// and column.
module Kdl =

  // One pass over the source gives the offset of every line start; the
  // reader's one-based line/column pairs convert to offsets against it.
  // The trailing newline the reader wants never starts a node, so the
  // table covers every position a node can have.
  let private lineStarts(src: string) : int[] =
    let starts = ResizeArray<int>()
    starts.Add 0

    for i in 0 .. src.Length - 1 do
      if src[i] = '\n' then
        starts.Add(i + 1)

    starts.ToArray()

  /// The int32 value of an integer literal, when the text is one: decimal,
  /// hex (`0x`), octal (`0o`) or binary (`0b`), with `_` separators. A
  /// fractional or exponent literal gives `ValueNone` — it is a float. An
  /// integer past the int32 range also gives `ValueNone`, so it becomes a
  /// decimal exactly like the XML front-end's typing.
  let private intOfRaw(raw: string) : int voption =
    let clean = raw.Replace("_", "")

    let sign, unsigned =
      if clean.StartsWith "-" then -1, clean.Substring 1
      elif clean.StartsWith "+" then 1, clean.Substring 1
      else 1, clean

    let radix, digits =
      if unsigned.StartsWith "0x" || unsigned.StartsWith "0X" then
        16, unsigned.Substring 2
      elif unsigned.StartsWith "0o" || unsigned.StartsWith "0O" then
        8, unsigned.Substring 2
      elif unsigned.StartsWith "0b" || unsigned.StartsWith "0B" then
        2, unsigned.Substring 2
      else
        10, unsigned

    if radix = 10 then
      if digits.IndexOfAny([| '.'; 'e'; 'E' |]) >= 0 then
        ValueNone
      else
        // `clean`, so Int32.MinValue keeps its sign
        match
          Int32.TryParse(
            clean,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture
          )
        with
        | true, v -> ValueSome v
        | _ -> ValueNone
    else
      // a hex, octal or binary literal is a whole number; a bad digit or
      // an overflow is not one we can carry, so it falls back to decimal
      try
        ValueSome(sign * Convert.ToInt32(digits, radix))
      with
      | :? FormatException -> ValueNone
      | :? ArgumentException -> ValueNone
      | :? OverflowException -> ValueNone

  let private offsetOf (starts: int[]) (line: int) (col: int) : int =
    if line < 1 || line > starts.Length then
      -1
    else
      starts[line - 1] + max 0 (col - 1)

  /// Parses one KDL markup document into its root nodes (one root per
  /// line; `map` is the usual one). An integer past the int32 range
  /// becomes a decimal, matching the XML front-end's typing. Failure
  /// messages carry the reader's line and column; node positions feed
  /// `Markup.where`, so resolution errors point at the authoring line.
  /// An empty document parses to zero roots.
  let parse(src: string) : Result<ImmutableArray<Node>, string> =
    let src = if src.EndsWith("\n") then src else src + "\n"
    let starts = lineStarts src

    try
      use reader = new KdlReader(new StringReader(src))

      // every adapter-level failure carries the reader's position, so
      // errors keep a line and column even before resolution
      let here() =
        $"(Ln {reader.Line}, Col {reader.Column})"

      let readArg() : Arg =
        match reader.TokenType with
        | KdlTokenType.String ->
          let v = reader.StringValue
          reader.Read() |> ignore
          Arg.Word v
        | KdlTokenType.Number ->
          let raw = reader.RawText
          let dec = reader.NumberValue.GetValueOrDefault()
          reader.Read() |> ignore

          match (if raw = null then ValueNone else intOfRaw raw) with
          | ValueSome v -> Arg.Number v
          | ValueNone -> Arg.Decimal(float dec)
        | KdlTokenType.True ->
          reader.Read() |> ignore
          Arg.Word "true"
        | KdlTokenType.False ->
          reader.Read() |> ignore
          Arg.Word "false"
        | KdlTokenType.Null ->
          failwith $"#null is not used in stamp markup {here()}"
        | KdlTokenType.Infinity ->
          failwith $"#inf is not used in stamp markup {here()}"
        | KdlTokenType.NaN ->
          failwith $"#nan is not used in stamp markup {here()}"
        | t ->
          failwith $"unexpected token {t} where a value was expected {here()}"

      // The caller consumes a closing brace: parseNodes stops on it, and
      // a node body that ends at end-of-file instead fails as unclosed.
      let rec parseNodes() : ImmutableArray<Node> =
        let builder = ImmutableArray.CreateBuilder<Node>()
        let mutable stop = false

        while not stop do
          match reader.TokenType with
          | KdlTokenType.Newline
          | KdlTokenType.Semicolon -> reader.Read() |> ignore
          | KdlTokenType.CloseBrace -> stop <- true
          | KdlTokenType.EndOfFile -> stop <- true
          | KdlTokenType.Slashdash ->
            // slashdash support: before a whole node only. The node is
            // parsed and dropped, so a bad body still errors.
            reader.Read() |> ignore

            if reader.TokenType = KdlTokenType.String then
              parseNode() |> ignore
            else
              failwith $"'/-' is supported before whole nodes only {here()}"
          | KdlTokenType.String -> builder.Add(parseNode())
          // the same rejections as value position — one message shape
          // per token, whatever context it appears in
          | KdlTokenType.Null ->
            failwith $"#null is not used in stamp markup {here()}"
          | KdlTokenType.Infinity ->
            failwith $"#inf is not used in stamp markup {here()}"
          | KdlTokenType.NaN ->
            failwith $"#nan is not used in stamp markup {here()}"
          | t -> failwith $"unexpected token {t}; expected a node {here()}"

        builder.ToImmutable()

      and parseNode() : Node =
        // the reader sits on the node's name token: its line and column
        // are the node's position
        let position = offsetOf starts reader.Line reader.Column
        let kind = reader.StringValue
        reader.Read() |> ignore // move off the name

        let args = ResizeArray<Arg>()
        let props = ResizeArray<Prop>()
        let children = ResizeArray<Node>()
        let mutable doneNode = false

        while not doneNode do
          match reader.TokenType with
          | KdlTokenType.String ->
            let s = reader.StringValue
            reader.Read() |> ignore

            if reader.TokenType = KdlTokenType.Equals then
              // a property: name=value, the layout channel
              reader.Read() |> ignore
              props.Add({ Name = s; Value = readArg() })
            else
              args.Add(Arg.Word s)
          | KdlTokenType.Number
          | KdlTokenType.True
          | KdlTokenType.False -> args.Add(readArg())
          | KdlTokenType.OpenBrace ->
            reader.Read() |> ignore
            children.AddRange(parseNodes())

            if reader.TokenType <> KdlTokenType.CloseBrace then
              // the named node is the innermost one still awaiting its
              // brace: in `map { field { fill grass` the single `}` on
              // the last line closes field, so map is the node reported
              failwith $"unclosed '}}' in node '{kind}' {here()}"

            // KDL 2.0: only whitespace and comments may follow the
            // closing brace on its line. The reader now sits on the next
            // token, so a same-line name is a stray node, not a sibling.
            let braceLine = reader.Line
            reader.Read() |> ignore // past the brace

            if
              reader.TokenType = KdlTokenType.String && reader.Line = braceLine
            then
              failwith $"a node cannot follow '}}' on the same line {here()}"

            doneNode <- true // the children block ends the node
          | KdlTokenType.Newline
          | KdlTokenType.Semicolon ->
            reader.Read() |> ignore
            doneNode <- true
          | KdlTokenType.CloseBrace -> doneNode <- true // parent consumes
          | KdlTokenType.EndOfFile -> doneNode <- true
          | KdlTokenType.Slashdash ->
            failwith $"'/-' is supported before whole nodes only {here()}"
          | KdlTokenType.Equals -> failwith $"unexpected '=' {here()}"
          // the same three rejections as value position — one message
          // shape per token, whatever context it appears in
          | KdlTokenType.Null ->
            failwith $"#null is not used in stamp markup {here()}"
          | KdlTokenType.Infinity ->
            failwith $"#inf is not used in stamp markup {here()}"
          | KdlTokenType.NaN ->
            failwith $"#nan is not used in stamp markup {here()}"
          | t -> failwith $"unexpected token {t} in node '{kind}' {here()}"

        // a template definition carries its name as the first argument
        // in KDL; move it to the label
        let arr = args.ToArray()

        let label, rest =
          if kind = "element" && arr.Length > 0 then
            match arr[0] with
            | Arg.Word nm -> ValueSome nm, arr[1..]
            | _ -> ValueNone, arr
          else
            ValueNone, arr

        {
          Kind = kind
          Position = position
          Label = label
          Props = ImmutableArray.CreateRange(props)
          Args = ImmutableArray.Create<Arg>(rest)
          Children = ImmutableArray.CreateRange(children)
        }

      reader.Read() |> ignore // first token
      let roots = parseNodes()

      if reader.TokenType <> KdlTokenType.EndOfFile then
        failwith $"unbalanced '}}' {here()}"

      Ok roots
    with e ->
      // bare message, like the XML front-end: our own failures already
      // carry kind and position, and the reader's carry their own text
      Error e.Message
