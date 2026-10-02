namespace Mibo.Markup

open System
open System.Collections.Immutable
open System.IO
open KdlSharp
open KdlSharp.Parsing

/// The KDL front-end. Produces the format-neutral `Node` tree from KDL
/// 2.0 text on KdlSharp's token reader:
///
/// - a node is `kind args? props? { children }?`
/// - `name=value` pairs are properties (the layout channel); bare values
///   are positional args
/// - a node whose kind is `element` carries its template name as the
///   first word argument (moved to `Node.Label`)
/// - slashdash `/-` comments out a whole node — parsed and dropped, so a
///   bad body still errors
///
/// Node positions resolve after parsing through a monotonic cursor (the
/// reader's `Position` is not a character offset): a node's kind token
/// follows a boundary — start of file, a newline, `;`, `{`, or a
/// slashdash — which property values and arguments never do. Unclosed
/// and unbalanced braces fail visibly.
module Kdl =

  let private findKind (src: string) (from: int) (kind: string) : int =
    let mutable i = from
    let mutable found = -1

    while found < 0 && i <= src.Length - kind.Length do
      if String.CompareOrdinal(src, i, kind, 0, kind.Length) = 0 then
        let afterOk =
          i + kind.Length >= src.Length
          || not(Char.IsLetterOrDigit src[i + kind.Length])

        // the previous non-space character marks a node boundary
        let mutable j = i - 1

        while j >= 0 && (src[j] = ' ' || src[j] = '\t' || src[j] = '\r') do
          j <- j - 1

        let beforeOk =
          j < 0
          || src[j] = '\n'
          || src[j] = ';'
          || src[j] = '{'
          || src[j] = '-' // the tail of a slashdash
          || src[j] = '/'

        if afterOk && beforeOk then
          found <- i

      if found < 0 then
        i <- i + 1

    if found >= 0 then found else from

  // Positions land after parsing: the parse order is the document order,
  // so one pre-order cursor pass resolves every node's offset.
  let rec private place (src: string) (cursor: int ref) (n: Node) : Node =
    let at = findKind src cursor.Value n.Kind
    cursor.Value <- at + 1

    {
      n with
          Position = at
          Children =
            n.Children
            |> Seq.map(place src cursor)
            |> ImmutableArray.CreateRange
    }

  let parse(src: string) : Result<ImmutableArray<Node>, string> =
    let src = if src.EndsWith("\n") then src else src + "\n"

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

          if raw <> null && raw.IndexOfAny([| '.'; 'e'; 'E' |]) >= 0 then
            Arg.Decimal(float dec)
          else
            Arg.Number(int dec)
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
          | t -> failwith $"unexpected token {t}; expected a node {here()}"

        builder.ToImmutable()

      and parseNode() : Node =
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
              failwith $"unclosed '}}' in node '{kind}' {here()}"

            reader.Read() |> ignore // past the brace
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
          Position = 0
          Label = label
          Props = ImmutableArray.CreateRange(props)
          Args = ImmutableArray.Create<Arg>(rest)
          Children = ImmutableArray.CreateRange(children)
        }

      reader.Read() |> ignore // first token
      let roots = parseNodes()

      if reader.TokenType <> KdlTokenType.EndOfFile then
        failwith $"unbalanced '}}' {here()}"

      let cursor = ref 0
      Ok(roots |> Seq.map(place src cursor) |> ImmutableArray.CreateRange)
    with e ->
      Error $"{e.GetType().Name}: {e.Message}"
