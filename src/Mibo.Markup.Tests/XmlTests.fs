module Mibo.Markup.Tests.Xml

open Expecto
open Mibo.Markup

let private doc =
  """<map w="36" h="20">
  <!-- the meadow -->
  <field><fill cell="grass" /></field>
  <element name="thicket"><generate kernel="forest" /></element>
  <plot x="1" y="1" w="5" h="5" pack="scatter" seed="13">
    <boulder /><boulder /><boulder />
  </plot>
</map>"""

[<Tests>]
let xmlTests =
  testList "Xml" [
    testCase
      "elements are nodes, attributes are properties, children are children"
    <| fun _ ->
      match Xml.parse doc with
      | Ok roots ->
        Expect.hasLength roots 1 "one root element"

        let map = roots[0]

        Expect.equal map.Kind "map" "the tag is the kind"
        Expect.equal map.Label ValueNone "no label outside element definitions"
        Expect.equal (Seq.toList map.Args) [] "XML carries no positional args"
        Expect.equal map.Position -1 "XML does not track node positions"

        Expect.equal
          (Seq.toList map.Props)
          [
            { Name = "w"; Value = Arg.Number 36 }
            { Name = "h"; Value = Arg.Number 20 }
          ]
          "attributes are properties, in document order"

        // comments never appear; children are elements only
        Expect.hasLength
          map.Children
          3
          "the element definition, field, and plot"

        let field = map.Children[0]
        Expect.equal field.Kind "field" "child element"

        Expect.equal
          (Seq.toList field.Children[0].Props)
          [
            {
              Name = "cell"
              Value = Arg.Word "grass"
            }
          ]
          "statement scalars are properties too"

        let element = map.Children[1]
        Expect.equal element.Kind "element" "template definition node"

        Expect.equal
          element.Label
          (ValueSome "thicket")
          "the name attribute is the label"

        Expect.equal
          (Seq.toList element.Props)
          []
          "the name attribute leaves the properties"

        let plot = map.Children[2]

        Expect.equal
          (Seq.toList plot.Props)
          [
            { Name = "x"; Value = Arg.Number 1 }
            { Name = "y"; Value = Arg.Number 1 }
            { Name = "w"; Value = Arg.Number 5 }
            { Name = "h"; Value = Arg.Number 5 }
            {
              Name = "pack"
              Value = Arg.Word "scatter"
            }
            { Name = "seed"; Value = Arg.Number 13 }
          ]
          "attribute typing"

        Expect.hasLength plot.Children 3 "the scattered elements are children"

      | Error e -> failtest $"parse failed: {e}"

    testCase "attribute values type int, then finite float, then word"
    <| fun _ ->
      match
        Xml.parse
          """<thing n="7" f="0.45" w="center" neg="-3" bad="NaN" worse="Infinity" />"""
      with
      | Ok roots ->
        let props =
          roots[0].Props |> Seq.map(fun p -> p.Name, p.Value) |> Seq.toList

        Expect.equal
          props
          [
            "n", Arg.Number 7
            "f", Arg.Decimal 0.45
            "w", Arg.Word "center"
            "neg", Arg.Number -3
            // non-finite floats stay words: the resolver never computes
            // with NaN or Infinity
            "bad", Arg.Word "NaN"
            "worse", Arg.Word "Infinity"
          ]
          "int, decimal, word, negative int, non-finite stays a word"

      | Error e -> failtest $"parse failed: {e}"

    testCase "text content is not markup and fails the parse"
    <| fun _ ->
      match Xml.parse """<map w="4" h="4"><plot>oops</plot></map>""" with
      | Ok _ -> failtest "text inside an element must not be dropped"
      | Error e ->
        Expect.stringContains e "not markup" "the error says what is wrong"
        Expect.stringContains e "plot" "the error names the element"

      // whitespace between elements stays legal
      match Xml.parse "<map w=\"4\" h=\"4\">\n  <plot />\n</map>" with
      | Ok roots -> Expect.hasLength roots[0].Children 1 "whitespace stays free"
      | Error e -> failtest $"whitespace must parse: {e}"

    testCase "parse failures carry the parser's line and position"
    <| fun _ ->
      match Xml.parse "<map>\n  <broken>\n</map>" with
      | Ok _ -> failtest "malformed XML must not parse"
      | Error e ->
        Expect.stringContains e "line 2" "the error names the line"
        Expect.stringContains e "broken" "the error names the element"

    testCase "an empty document has no root"
    <| fun _ ->
      match Xml.parse "" with
      | Ok _ -> failtest "empty input must not parse"
      | Error e -> Expect.stringContains e "no root element" "a stable message"
  ]
