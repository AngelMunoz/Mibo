module Mibo.Markup.Tests.Kdl

open Expecto
open System.Collections.Immutable
open Mibo.Markup

/// Zeroes node positions so the two front-ends' trees compare equal:
/// offsets differ by construction, everything else must not.
let rec private normalize(n: Node) : Node = {
  n with
      Position = 0
      Children = n.Children |> Seq.map normalize |> ImmutableArray.CreateRange
}

let private kdlDoc =
  """map 36 20 {
    field { fill grass }
    element thicket { generate forest }
    plot x=1 y=1 w=5 h=5 pack=scatter seed=13 { boulder; boulder; boulder }
    set 1 2 waypoint
    weather 0.45 grass dirt
    /- dropped whole node { still parsed }
}"""

let private xmlDoc =
  """<map>
  <a>36</a><a>20</a>
  <field><fill><a>grass</a></fill></field>
  <element><a>thicket</a><generate><a>forest</a></generate></element>
  <plot x="1" y="1" w="5" h="5" pack="scatter" seed="13"><boulder /><boulder /><boulder /></plot>
  <set><a>1</a><a>2</a><a>waypoint</a></set>
  <weather><a>0.45</a><a>grass</a><a>dirt</a></weather>
  <!-- dropped whole node: comments are free -->
</map>"""

[<Tests>]
let kdlTests =
  testList "Kdl" [
    testCase "nodes, args, props, children, and labels"
    <| fun _ ->
      match Kdl.parse kdlDoc with
      | Ok roots ->
        Expect.hasLength roots 1 "one root node"

        let map = roots[0]
        Expect.equal map.Kind "map" "node kind"

        Expect.equal
          (Seq.toList map.Args)
          [ Arg.Number 36; Arg.Number 20 ]
          "positional args"

        Expect.hasLength map.Children 5 "slashdash drops a whole node"

        let element = map.Children[1]
        Expect.equal element.Kind "element" "template definition node"

        Expect.equal
          element.Label
          (ValueSome "thicket")
          "the first word arg becomes the label"

        Expect.equal
          (Seq.toList element.Args)
          []
          "the label arg leaves the args"

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
          "name=value pairs are properties"

        let weather = map.Children[4]

        Expect.equal
          (Seq.toList weather.Args)
          [ Arg.Decimal 0.45; Arg.Word "grass"; Arg.Word "dirt" ]
          "decimal point tokens are decimals"

      | Error e -> failtest $"parse failed: {e}"

    testCase "positions are token starts"
    <| fun _ ->
      match Kdl.parse "map 36 20 {\n  plot x=1 {}\n}" with
      | Ok roots ->
        let plot = roots[0].Children[0]

        Expect.equal
          (Markup.where "map 36 20 {\n  plot x=1 {}\n}" plot.Position)
          "2:3"
          "plot position"

      | Error e -> failtest $"parse failed: {e}"

    testCase "unbalanced braces fail visibly"
    <| fun _ ->
      match Kdl.parse "map 36 20 {" with
      | Ok _ -> failtest "unbalanced input must not parse"
      | Error e -> Expect.isGreaterThan e.Length 0 "the error carries a message"
  ]

[<Tests>]
let parityTests =
  testList "front-end parity" [
    testCase "the same document parses to the same tree in both syntaxes"
    <| fun _ ->
      match Kdl.parse kdlDoc, Xml.parse xmlDoc with
      | Ok kdlRoots, Ok xmlRoots ->
        let kdlTrees = kdlRoots |> Seq.map normalize |> Seq.toList
        let xmlTrees = xmlRoots |> Seq.map normalize |> Seq.toList

        Expect.equal kdlTrees xmlTrees "trees match modulo positions"

      | kdlRes, xmlRes -> failtest $"parse failed: kdl={kdlRes} xml={xmlRes}"
  ]
