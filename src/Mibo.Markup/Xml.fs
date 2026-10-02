namespace Mibo.Markup

open System
open System.Collections.Immutable
open System.Globalization
open System.Xml.Linq

/// The XML front-end. Produces the format-neutral `Node` tree with the
/// BCL parser doing all the parsing — no hand-rolled scanning:
///
/// - an element is a node (the tag is the kind); child elements are the
///   node's children
/// - an attribute is a property — the scalar channel, the way XML means
///   it (`map w="36" h="20"`); a node's `Args` stay empty from this
///   front-end, and the resolver reads every scalar by property name
/// - the value is typed int, then finite float, then word (`x="1"` is a
///   number, `place="center"` is a word)
/// - an `element` definition carries its template name in the `name`
///   attribute (the label)
/// - comments and processing instructions are free; text is not markup —
///   a non-whitespace text node inside an element fails the parse
///
/// Namespace-free documents: `xmlns` attributes would become plain
/// properties. Parse failures carry the parser's line and position.
/// This front-end does not track node positions (the BCL line-info
/// surface is not reachable from F# without fragile reference tricks),
/// so a `Node` from XML carries `Position = -1` and resolution errors
/// name the element instead of pointing at a line; KDL documents carry
/// real positions.
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
       | true, v when Double.IsFinite v -> Arg.Decimal v
       | _ -> Arg.Word t)

  let rec private convert(el: XElement) : Node =
    // text is not markup: a non-whitespace text node (or CDATA, which
    // derives from XText) where a child element belongs is an authoring
    // mistake, and dropping it silently hides a forgotten statement
    for node in el.Nodes() do
      match node with
      | :? XText as t when not(String.IsNullOrWhiteSpace t.Value) ->
        failwith
          $"<{el.Name.LocalName}> contains text that is not markup: '{t.Value.Trim()}'"
      | _ -> ()

    // an element definition names itself through the name attribute;
    // the KDL front-end does the same with its first word argument
    let mutable label = ValueNone

    if el.Name.LocalName = "element" then
      for at in el.Attributes() do
        if label.IsNone && at.Name.LocalName = "name" then
          label <- ValueSome at.Value

    {
      Kind = el.Name.LocalName
      Position = -1
      Label = label
      Props =
        el.Attributes()
        |> Seq.filter(fun at ->
          not(el.Name.LocalName = "element" && at.Name.LocalName = "name"))
        |> Seq.map(fun at -> {
          Name = at.Name.LocalName
          Value = parseScalar at.Value
        })
        |> ImmutableArray.CreateRange
      Args = ImmutableArray.Empty
      Children = el.Elements() |> Seq.map convert |> ImmutableArray.CreateRange
    }

  /// Parses one XML markup document into its root nodes (one root
  /// element; `map` is the usual one). Parse failures carry the
  /// parser's line and position.
  let parse(src: string) : Result<ImmutableArray<Node>, string> =
    if String.IsNullOrWhiteSpace src then
      Error "the document has no root element"
    else
      try
        let root = XDocument.Parse(src, LoadOptions.None).Root

        if obj.ReferenceEquals(root, null) then
          Error "the document has no root element"
        else
          Ok(ImmutableArray.Create(convert root))
      with e ->
        // the BCL parser's message already embeds its line and position;
        // the text-not-markup failure carries the element and the text
        Error e.Message
