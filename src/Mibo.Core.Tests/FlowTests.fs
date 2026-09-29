module Mibo.Core.Tests.Flow

open Expecto
open System.Numerics
open Mibo.Layout

let private mkGrid w h : CellGrid2D<int> =
  CellGrid2D.create w h (Vector2(1f, 1f)) Vector2.Zero

let private tile w h content : Stamp<int> =
  Stamp.sized w h (fun s -> s |> Layout.fill 0 0 w h content)

let private fillTile content : Stamp<int> =
  Stamp.sized 1 1 (fun s -> s |> Layout.fill 0 0 s.Width s.Height content)

let private expectCell
  (g: CellGrid2D<int>)
  x
  y
  (expected: int voption)
  message
  =
  Expect.equal (CellGrid2D.get x y g) expected message

let private mountInto
  (w: int)
  (h: int)
  (stamp: Stamp<int>)
  : CellGrid2D<int> * MountResult =
  let g = mkGrid w h

  let mutable result = { Positions = null }

  g
  |> Layout.run(fun s ->
    result <- Flow.mount stamp s
    s)
  |> ignore

  g, result

[<Tests>]
let tests =
  testList "Flow" [
    testList "Stamp" [
      testCase "combinator sizes"
      <| fun _ ->
        let a = Stamp.create 3 1 ignore
        let b = Stamp.create 2 2 ignore

        let beside = Stamp.beside a b
        Expect.equal beside.W 5 "beside width"
        Expect.equal beside.H 2 "beside height"

        let above = Stamp.above a b
        Expect.equal above.W 3 "above width"
        Expect.equal above.H 3 "above height"

        let overlay = Stamp.overlay a b
        Expect.equal overlay.W 3 "overlay width"
        Expect.equal overlay.H 2 "overlay height"

        let inset = Stamp.inset 1 (Stamp.create 3 2 ignore)
        Expect.equal inset.W 5 "inset width"
        Expect.equal inset.H 4 "inset height"

        let offset = Stamp.offset -1 -1 (Stamp.create 2 1 ignore)
        Expect.equal offset.W 2 "offset width"
        Expect.equal offset.H 1 "offset height"

        let repeated = Stamp.repeat 3 (Stamp.create 2 1 ignore)
        Expect.equal repeated.W 6 "repeat width"
        Expect.equal repeated.H 1 "repeat height"

        let none = Stamp.repeat 0 a
        Expect.equal none.W 0 "repeat 0 width"
        Expect.equal none.H 0 "repeat 0 height"

      testCase "sized wraps a section pipeline"
      <| fun _ ->
        let stamp = Stamp.sized 4 3 (fun s -> s |> Layout.border 0 0 4 3 7)
        let g, _ = mountInto 6 5 stamp

        expectCell g 0 0 (ValueSome 7) "border corner"
        expectCell g 3 2 (ValueSome 7) "border corner"
        expectCell g 1 1 ValueNone "interior stays empty"
        expectCell g 4 2 ValueNone "outside the stamp footprint"

      testCase "expand marks the element and keeps its footprint"
      <| fun _ ->
        let s = Stamp.expand(Stamp.create 2 2 ignore)
        Expect.equal s.Expand 1 "expand weight"

        let s = Stamp.expandWeight 3 (Stamp.create 2 2 ignore)
        Expect.equal s.Expand 3 "explicit weight"
    ]

    testList "row" [
      testCase "places children with gap"
      <| fun _ ->
        let stamp =
          Flow.row { FlowOpts.Default with Gap = 2 } [ tile 3 1 1; tile 3 1 2 ]

        let g, _ = mountInto 20 1 stamp

        expectCell g 0 0 (ValueSome 1) "first child start"
        expectCell g 2 0 (ValueSome 1) "first child end"
        expectCell g 3 0 ValueNone "gap"
        expectCell g 5 0 (ValueSome 2) "second child start"
        expectCell g 7 0 (ValueSome 2) "second child end"
        expectCell g 8 0 ValueNone "after children"

      testCase "expand children share the leftover width"
      <| fun _ ->
        let stamp =
          Flow.row FlowOpts.Default [ tile 1 1 1; Stamp.expand(fillTile 2) ]

        let g, _ = mountInto 10 1 stamp

        expectCell g 0 0 (ValueSome 1) "fixed child"
        expectCell g 1 0 (ValueSome 2) "expanded child start"
        expectCell g 9 0 (ValueSome 2) "expanded child end"

      testCase "expand weights split the leftover space"
      <| fun _ ->
        let stamp =
          Flow.row FlowOpts.Default [
            Stamp.expandWeight 1 (fillTile 1)
            Stamp.expandWeight 3 (fillTile 2)
          ]

        let g, _ = mountInto 8 1 stamp

        expectCell g 1 0 (ValueSome 1) "weight 1 ends at 1/4"
        expectCell g 2 0 (ValueSome 2) "weight 3 starts at 1/4"
        expectCell g 7 0 (ValueSome 2) "weight 3 fills the rest"

      testCase "align centers children on the cross axis"
      <| fun _ ->
        let stamp =
          Flow.column
            {
              FlowOpts.Default with
                  Gap = 1
                  Align = Center
            }
            [ tile 2 1 1; tile 2 2 2 ]

        let g, _ = mountInto 10 5 stamp

        expectCell g 4 0 (ValueSome 1) "centered tile start"
        expectCell g 5 0 (ValueSome 1) "centered tile end"
        expectCell g 4 1 ValueNone "gap row"
        expectCell g 4 2 (ValueSome 2) "second tile start"
        expectCell g 4 3 (ValueSome 2) "second tile end"
        expectCell g 4 4 ValueNone "after children"

      testCase "wrap moves children to a new line"
      <| fun _ ->
        let stamp =
          Flow.row { FlowOpts.Default with Wrap = true } [
            tile 2 1 1
            tile 2 1 2
            tile 2 1 3
          ]

        let g, _ = mountInto 5 3 stamp

        expectCell g 0 0 (ValueSome 1) "line 1 child 1"
        expectCell g 2 0 (ValueSome 2) "line 1 child 2"
        expectCell g 4 0 ValueNone "no room on line 1"
        expectCell g 0 1 (ValueSome 3) "wrapped child on line 2"

      testCase "justify pushes children to the end"
      <| fun _ ->
        let stamp =
          Flow.row { FlowOpts.Default with Justify = End } [ tile 3 1 1 ]

        let g, _ = mountInto 10 1 stamp

        expectCell g 6 0 ValueNone "before children"
        expectCell g 7 0 (ValueSome 1) "justified start"
        expectCell g 9 0 (ValueSome 1) "justified end"

      testCase "children clamp to the assigned container"
      <| fun _ ->
        let g = mkGrid 10 1

        let section: GridSection2D<int> = {
          BackingGrid = g
          OffsetX = 0
          OffsetY = 0
          Width = 6
          Height = 1
        }

        let stamp =
          Flow.row FlowOpts.Default [ tile 3 1 1; Stamp.expand(fillTile 2) ]

        let _ = Flow.mount stamp section

        expectCell g 5 0 (ValueSome 2) "clamped at container edge"
        expectCell g 6 0 ValueNone "beyond the container"
    ]

    testList "grid" [
      testCase "fixed tracks and template areas"
      <| fun _ ->
        let stamp =
          Flow.grid
            [ Fixed 4; Fixed 6 ]
            [ Fixed 2; Fixed 3 ]
            1
            [ "a a"; "b ." ]
            (fun place ->
              place "a" (fillTile 1)
              place "b" (fillTile 2))

        let g, _ = mountInto 12 7 stamp

        expectCell g 0 0 (ValueSome 1) "area a start"
        expectCell g 10 0 (ValueSome 1) "area a spans both columns"
        expectCell g 0 1 (ValueSome 1) "area a bottom row"
        expectCell g 0 2 ValueNone "row gap"
        expectCell g 0 3 (ValueSome 2) "area b start"
        expectCell g 3 5 (ValueSome 2) "area b end"
        expectCell g 4 4 ValueNone "right of area b"
        expectCell g 0 6 ValueNone "below area b"

      testCase "weight tracks share the leftover width"
      <| fun _ ->
        let stamp =
          Flow.grid
            [ Fixed 2; Weight 1f; Weight 3f ]
            [ Fixed 4 ]
            0
            [ "s m b" ]
            (fun place ->
              place "s" (fillTile 1)
              place "m" (fillTile 2)
              place "b" (fillTile 3))

        let g, _ = mountInto 10 4 stamp

        expectCell g 1 0 (ValueSome 1) "fixed column"
        expectCell g 2 0 (ValueSome 2) "weight 1 column"
        expectCell g 3 0 (ValueSome 2) "weight 1 end"
        expectCell g 4 0 (ValueSome 3) "weight 3 start"
        expectCell g 9 0 (ValueSome 3) "weight 3 end"

      testCase "percent tracks take a fraction of the container"
      <| fun _ ->
        let stamp =
          Flow.grid
            [ Percent 0.5f; Fixed 2 ]
            [ Fixed 1 ]
            0
            [ "a b" ]
            (fun place ->
              place "a" (fillTile 1)
              place "b" (fillTile 2))

        let g, _ = mountInto 10 1 stamp

        expectCell g 4 0 (ValueSome 1) "percent column end"
        expectCell g 5 0 (ValueSome 2) "fixed column start"
        expectCell g 6 0 (ValueSome 2) "fixed column end"

      testCase "unknown area name throws"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Flow.grid [ Fixed 1 ] [ Fixed 1 ] 0 [ "a" ] (fun place ->
              place "z" (tile 1 1 1))
            |> ignore)
          "unknown area"

      testCase "empty tracks throw"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () -> Flow.grid [] [ Fixed 1 ] 0 [ "a" ] (fun _ -> ()) |> ignore)
          "no column tracks"
    ]

    testList "dock" [
      testCase "top right corner with inset"
      <| fun _ ->
        let g, _ = mountInto 10 6 (Stamp.empty())

        let _ =
          g |> Layout.run(Flow.dock (Dock.Top ||| Dock.Right) 1 (tile 2 1 9))

        expectCell g 7 1 (ValueSome 9) "docked start"
        expectCell g 8 1 (ValueSome 9) "docked end"
        expectCell g 6 1 ValueNone "left of docked"

      testCase "center of the section"
      <| fun _ ->
        let g, _ = mountInto 10 6 (Stamp.empty())

        let _ =
          g
          |> Layout.run(
            Flow.dock (Dock.CenterX ||| Dock.CenterY) 0 (tile 2 1 9)
          )

        expectCell g 4 2 (ValueSome 9) "centered"
        expectCell g 5 2 (ValueSome 9) "centered"

      testCase "stretch spans the section minus insets"
      <| fun _ ->
        let g, _ = mountInto 10 6 (Stamp.empty())

        let _ =
          g
          |> Layout.run(
            Flow.dock (Dock.StretchX ||| Dock.Bottom) 1 (fillTile 5)
          )

        expectCell g 0 4 ValueNone "left inset"
        expectCell g 1 4 (ValueSome 5) "stretch start"
        expectCell g 8 4 (ValueSome 5) "stretch end"
        expectCell g 9 4 ValueNone "right inset"
    ]

    testList "mount" [
      testCase "reports named element positions"
      <| fun _ ->
        let stamp =
          Flow.row { FlowOpts.Default with Gap = 2 } [
            Stamp.named "left" (tile 3 1 1)
            Stamp.named "right" (tile 3 1 2)
          ]

        let _, result = mountInto 20 1 stamp

        Expect.equal
          (Flow.tryPosition "left" result)
          (ValueSome { X = 0; Y = 0; W = 3; H = 1 })
          "left position"

        Expect.equal
          (Flow.tryPosition "right" result)
          (ValueSome { X = 5; Y = 0; W = 3; H = 1 })
          "right position"

        Expect.equal
          (Flow.tryPosition "missing" result)
          ValueNone
          "unknown name"

      testCase "reports nested and grid-placed names"
      <| fun _ ->
        let inner =
          Flow.column FlowOpts.Default [ Stamp.named "dot" (tile 1 1 1) ]

        let stamp =
          Flow.grid [ Fixed 4 ] [ Fixed 4 ] 0 [ "a" ] (fun place ->
            place "a" inner
            place "a" (Stamp.named "area" (Stamp.empty())))

        let g, result = mountInto 4 4 stamp

        Expect.equal
          (Flow.tryPosition "dot" result)
          (ValueSome { X = 0; Y = 0; W = 1; H = 1 })
          "nested name inside area"

        Expect.equal
          (Flow.tryPosition "area" result)
          (ValueSome { X = 0; Y = 0; W = 4; H = 4 })
          "grid area position"

        expectCell g 0 0 (ValueSome 1) "content painted"

      testCase "paint records no positions"
      <| fun _ ->
        let g = mkGrid 4 4

        let _ = g |> Layout.run(Flow.paint(Stamp.named "x" (tile 1 1 1)))

        expectCell g 0 0 (ValueSome 1) "painted"
    ]
  ]
