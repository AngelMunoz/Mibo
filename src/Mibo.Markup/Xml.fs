namespace Mibo.Markup

open System
open System.Collections.Immutable
open System.Globalization
open System.Xml.Linq

/// The XML front-end. Produces the format-neutral `Node` tree from XML
/// on the BCL parser — no external dependency:
///
/// - an element is a node (the tag is the kind)
/// - an attribute is a property; the value is typed int, then float, then
///   word (`x="1"` is a number, `place="center"` is a word)
/// - a text-only `<a>` child element carries one positional argument in
///   its text content (`<a>36</a>`)
/// - comments and processing instructions are free
///
/// Documents are namespace-free: `xmlns` attributes would become plain
/// properties. Parse failures fail visibly with the parser's message.
module Xml =

  let private parseScalar(text: string) : Arg =
    let t = text.Trim()

    match
      Int32.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture)
    with
    | true, v -> Arg.Number v
    | _ ->
      (match
        Double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture)
       with
       | true, v -> Arg.Decimal v
       | _ -> Arg.Word t)

  // Positions: `Markup.where` wants character offsets. The parser hands
  // elements back in document order, and a raw '<' cannot occur inside
  // attribute values or text (XML requires escaping), so a monotonic
  // cursor scanning for the next "<kind" token finds each node's offset.
  let private findTag (src: string) (from: int) (kind: string) : int =
    let probe = "<" + kind
    let mutable i = from
    let mutable found = -1

    while found < 0 && i <= src.Length - probe.Length do
      if String.CompareOrdinal(src, i, probe, 0, probe.Length) = 0 then
        let after = i + probe.Length
        let c = if after < src.Length then src[after] else ' '

        if
          c = ' ' || c = '>' || c = '/' || c = '\t' || c = '\n' || c = '\r'
        then
          found <- i

      if found < 0 then
        i <- i + 1

    if found >= 0 then found else from

  let rec private convert
    (src: string)
    (cursor: int ref)
    (el: XElement)
    : Node =
    let start = findTag src cursor.Value el.Name.LocalName
    cursor.Value <- start + 1

    let args = ResizeArray<Arg>()
    let children = ResizeArray<Node>()

    for child in el.Nodes() do
      match child with
      | :? XElement as ce ->
        if ce.Name.LocalName = "a" then
          args.Add(parseScalar ce.Value)
        else
          children.Add(convert src cursor ce)
      | _ -> ()

    {
      Kind = el.Name.LocalName
      Position = start
      Label = ValueNone
      Props =
        el.Attributes()
        |> Seq.map(fun at -> {
          Name = at.Name.LocalName
          Value = parseScalar at.Value
        })
        |> ImmutableArray.CreateRange
      Args = ImmutableArray.CreateRange args
      Children = ImmutableArray.CreateRange children
    }

  /// Parses one XML markup document into its root nodes (one root
  /// element; `map` is the usual one).
  let parse(src: string) : Result<ImmutableArray<Node>, string> =
    try
      let root = XDocument.Parse(src, LoadOptions.None).Root

      if obj.ReferenceEquals(root, null) then
        Error "the document has no root element"
      else
        let cursor = ref 0
        Ok(ImmutableArray.Create(convert src cursor root))
    with e ->
      Error e.Message
