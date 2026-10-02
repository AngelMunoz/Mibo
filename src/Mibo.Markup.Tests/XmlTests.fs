module Mibo.Markup.Tests.Xml

open Expecto
open Mibo.Markup

let private doc =
  """<map>
  <a>36</a>
  <a>20</a>
  <!-- a full map: field, a scattered plot, one waypoint -->
  <field fill="grass" />
  <plot x="1" y="1" w="5" h="5" pack="scatter" seed="13">
    <a>boulder</a>
    <a>boulder</a>
    <a>boulder</a>
  </plot>
  <set>
    <a>1</a>
    <a>2</a>
    <a>waypoint</a>
  </set>
</map>"""

[<Tests>]
let xmlTests =
  testList "Xml" [
    testCase "elements, attributes, and a-args map onto the node tree"
    <| fun _ ->
      match Xml.parse doc with
      | Ok roots ->
        Expect.hasLength roots 1 "one root element"

        let map = roots[0]
        Expect.equal map.Kind "map" "the tag is the kind"
        Expect.equal map.Label ValueNone "no label in XML"

        Expect.equal
          (Seq.toList map.Args)
          [ Number 36; Number 20 ]
          "a-args carry positionals"

        Expect.hasLength map.Children 3 "comments do not count as children"

        let field = map.Children[0]
        Expect.equal field.Kind "field" "child element"
        Expect.equal (Seq.toList field.Args) [] "no args"

        Expect.equal
          (Seq.toList field.Props)
          [ { Name = "fill"; Value = Word "grass" } ]
          "attributes are props"

        let plot = map.Children[1]

        Expect.equal
          (Seq.toList plot.Props)
          [
            { Name = "x"; Value = Number 1 }
            { Name = "y"; Value = Number 1 }
            { Name = "w"; Value = Number 5 }
            { Name = "h"; Value = Number 5 }
            {
              Name = "pack"
              Value = Word "scatter"
            }
            { Name = "seed"; Value = Number 13 }
          ]
          "attribute typing"

        Expect.equal
          (Seq.toList plot.Args)
          [ Word "boulder"; Word "boulder"; Word "boulder" ]
          "args keep document order"

        let set = map.Children[2]

        Expect.equal
          (Seq.toList set.Args)
          [ Number 1; Number 2; Word "waypoint" ]
          "scalar typing runs over a-args too"

      | Error e -> failtest $"parse failed: {e}"

    testCase "attribute values type int, then float, then word"
    <| fun _ ->
      match Xml.parse """<thing n="7" f="0.45" w="center" neg="-3" />""" with
      | Ok roots ->
        let props =
          roots[0].Props |> Seq.map(fun p -> p.Name, p.Value) |> Seq.toList

        Expect.equal
          props
          [
            "n", Number 7
            "f", Decimal 0.45
            "w", Word "center"
            "neg", Number -3
          ]
          "int, decimal, word, negative int"

      | Error e -> failtest $"parse failed: {e}"

    testCase "positions carry to line and column"
    <| fun _ ->
      Expect.equal (Markup.where "ab\ncd" 0) "1:1" "start of the first line"
      Expect.equal (Markup.where "ab\ncd" 2) "1:3" "end of the first line"
      Expect.equal (Markup.where "ab\ncd" 3) "2:1" "start of the second line"

      match Xml.parse "<level>\n  <thing />\n</level>" with
      | Ok roots ->
        let level = roots[0]

        Expect.equal
          (Markup.where "<level>\n  <thing />\n</level>" level.Position)
          "1:1"
          "root position"

        let thing = level.Children[0]

        Expect.equal
          (Markup.where "<level>\n  <thing />\n</level>" thing.Position)
          "2:3"
          "child position"

      | Error e -> failtest $"parse failed: {e}"

    testCase "parse failures fail visibly"
    <| fun _ ->
      match Xml.parse "<map>\n  <broken>\n</map>" with
      | Ok _ -> failtest "malformed XML must not parse"
      | Error e -> Expect.isGreaterThan e.Length 0 "the error carries a message"

    testCase "an empty document has no root"
    <| fun _ ->
      match Xml.parse "" with
      | Ok _ -> failtest "empty input must not parse"
      | Error e -> Expect.stringContains e "no root element" "a stable message"

    testCase "a tag spelled inside a comment never captures a position"
    <| fun _ ->
      let src =
        "<map>\n  <!-- <field x=\"1\" /> -->\n  <field x=\"2\" />\n</map>"

      match Xml.parse src with
      | Ok roots ->
        let field = roots[0].Children[0]

        Expect.equal
          (Markup.where src field.Position)
          "3:3"
          "the real element's line"

      | Error e -> failtest $"parse failed: {e}"

    testCase "CDATA regions never capture positions either"
    <| fun _ ->
      let src =
        "<map>\n  <![CDATA[ <set 1 2 /> ]]>\n  <set><a>1</a><a>2</a></set>\n</map>"

      match Xml.parse src with
      | Ok roots ->
        let set = roots[0].Children[0]

        Expect.equal
          (Markup.where src set.Position)
          "3:3"
          "the real element's line"

      | Error e -> failtest $"parse failed: {e}"
  ]
