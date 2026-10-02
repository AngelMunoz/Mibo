module Mibo.Markup.Tests.Kdl

open Expecto
open Mibo.Markup

let private kdlDoc =
  """map 36 20 {
    field { fill grass }
    element thicket { generate forest }
    plot x=1 y=1 w=5 h=5 pack=scatter seed=13 { boulder; boulder; boulder }
    set 1 2 waypoint
    weather 0.45 grass dirt
    /- dropped whole node { still parsed }
}"""

let private mapSrc = "map 36 20 {\n  plot x=1 {}\n  my-set 1\n  set 1 2 way\n}"

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

    testCase "a property named like a later node never captures its position"
    <| fun _ ->
      // `set=13` reads as a property; the `set` node below it must take
      // its own offset, not the property's
      match Kdl.parse "map 4 4 set=13\nset 1 2 way" with
      | Ok roots ->
        Expect.hasLength roots 2 "two root nodes"

        let set = roots[1]
        Expect.equal set.Kind "set" "the node parsed"

        Expect.equal
          (Markup.where "map 4 4 set=13\nset 1 2 way" set.Position)
          "2:1"
          "the node's own line"

      | Error e -> failtest $"parse failed: {e}"

    testCase "a kind spelled in a line comment never captures a position"
    <| fun _ ->
      match
        Kdl.parse "map 4 4 {\n    // fill the meadow\n    fill grass\n}"
      with
      | Ok roots ->
        let fill = roots[0].Children[0]
        Expect.equal fill.Kind "fill" "the node parsed"

        Expect.equal
          (Markup.where
            "map 4 4 {\n    // fill the meadow\n    fill grass\n}"
            fill.Position)
          "3:5"
          "the node's own line"

      | Error e -> failtest $"parse failed: {e}"

    testCase "an integer past the int32 range becomes a decimal"
    <| fun _ ->
      match
        Kdl.parse
          "map 4 4 {\n    plot {\n        fillRect 1 1 4000000000 1 stone\n    }\n}"
      with
      | Error e -> failtest $"parse failed: {e}"
      | Ok roots ->
        // the resolver would reject the width; parse-level typing is
        // the point here — no overflow, a decimal instead
        Expect.equal roots.Length 1 "parsed"

        match Kdl.parse "thing 4000000000" with
        | Ok single ->
          Expect.equal
            (Seq.toList single[0].Args)
            [ Arg.Decimal 4e+09 ]
            "typed as decimal"

        | Error e -> failtest $"parse failed: {e}"

    testCase "typed KDL literals pin their own typing"
    <| fun _ ->
      // KDL has typed literals XML attributes cannot spell; these pins
      // hold the KDL side steady so the documented divergences stay
      // divergences and not drift
      match Kdl.parse "thing 0x1F 1_000 1e3 0o17 \"36\"" with
      | Error e -> failtest $"parse failed: {e}"
      | Ok roots ->
        Expect.equal
          (Seq.toList roots[0].Args)
          [
            Arg.Number 31 // hex is a whole number, usable in an int slot
            Arg.Number 1000 // underscores
            Arg.Decimal 1000.0 // exponent
            Arg.Number 15 // octal
            Arg.Word "36" // quoted stays a word
          ]
          "hex, underscore, exponent, quoted-number typing"

    testCase "an empty document parses to zero roots"
    <| fun _ ->
      match Kdl.parse "" with
      | Ok roots -> Expect.hasLength roots 0 "no nodes"
      | Error e -> failtest $"empty input must parse: {e}"

    testCase "positions come from the reader, exactly"
    <| fun _ ->
      // reader line/column at the node's name token — no text scanning
      match Kdl.parse mapSrc with
      | Error e -> failtest $"parse failed: {e}"
      | Ok roots ->
        let map = roots[0]
        Expect.equal (Markup.where mapSrc map.Position) "1:1" "the root's line"

        let plot = map.Children[0]
        Expect.equal (Markup.where mapSrc plot.Position) "2:3" "the plot's line"

        // a kind that is a hyphen suffix of an earlier node never moves
        // the later node's position
        let mySet = map.Children[1]

        Expect.equal
          (Markup.where mapSrc mySet.Position)
          "3:3"
          "my-set's own line"

        let set = map.Children[2]

        Expect.equal
          (Markup.where mapSrc set.Position)
          "4:3"
          "set's own line, not my-set's"

    testCase "a stray node after a closing brace fails"
    <| fun _ ->
      // KDL 2.0: nothing but whitespace and comments may follow `}` on
      // its line — the stray word must not parse as a second root
      match Kdl.parse "map {\n  plot {}\n} set 1 2" with
      | Ok roots ->
        failtest $"the stray node must not parse: {roots.Length} roots"
      | Error e ->
        Expect.stringContains e "cannot follow '}'" "the stray node is named"
        Expect.stringContains e "(Ln 3, Col 3)" "the stray token's position"

    testCase "sibling roots on separate lines still parse"
    <| fun _ ->
      // the rejection is same-line only; a node on its own line is a
      // normal root
      match Kdl.parse "map {}\nset 1 2" with
      | Error e -> failtest $"separate-line roots must parse: {e}"
      | Ok roots ->
        Expect.hasLength roots 2 "two roots"
        Expect.equal roots[1].Kind "set" "the second root's kind"

    testCase "an unclosed outer brace names the node awaiting its brace"
    <| fun _ ->
      // the single `}` on the last line closes field, so map is the
      // innermost node still awaiting its brace — that is the name the
      // error gives
      match Kdl.parse "map {\n  field { fill grass\n}" with
      | Ok roots -> failtest "the unclosed brace must not parse"
      | Error e ->
        Expect.stringContains e "unclosed '}' in node 'map'" "the awaiting node"
        // the parse appends the trailing newline, so EOF lands on Ln 4
        Expect.stringContains e "(Ln 4, Col 1)" "the end position"
  ]
