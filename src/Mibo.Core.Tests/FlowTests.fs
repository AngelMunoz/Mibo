module Mibo.Core.Tests.Flow

open Expecto
open System.Numerics
open Mibo.Layout

let private mkGrid w h : CellGrid2D<int> =
  CellGrid2D.create w h (Vector2(1f, 1f)) Vector2.Zero

let private tile w h content : Stamp<int> =
  Stamp.sized w h (fun s -> s |> Layout.fill 0 0 w h content)

let private fillTile content : Stamp<int> = Flow.canvas [ Flow.fill content ]

let private expectCell
  (g: CellGrid2D<int>)
  x
  y
  (expected: int voption)
  message
  =
  Expect.equal (CellGrid2D.get x y g) expected message

let private runInto
  (w: int)
  (h: int)
  (stamp: Stamp<int>)
  : CellGrid2D<int> * Landmarks =
  let g = mkGrid w h
  let struct (g, placed) = g |> Flow.run stamp
  g, placed

/// Tile vocabulary for the harbour example map.
type Tile =
  | Grass
  | Path
  | Water
  | Sand
  | Wall
  | Tree
  | Rock
  | Stump
  | Crate
  | Stall
  | Lamp
  | Torch
  | Fish
  | Fruit
  | Chest
  | Spawn

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
        let g, _ = runInto 6 5 stamp

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

        let g, _ = runInto 20 1 stamp

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

        let g, _ = runInto 10 1 stamp

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

        let g, _ = runInto 8 1 stamp

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

        let g, _ = runInto 10 5 stamp

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

        let g, _ = runInto 5 3 stamp

        expectCell g 0 0 (ValueSome 1) "line 1 child 1"
        expectCell g 2 0 (ValueSome 2) "line 1 child 2"
        expectCell g 4 0 ValueNone "no room on line 1"
        expectCell g 0 1 (ValueSome 3) "wrapped child on line 2"

      testCase "justify pushes children to the end"
      <| fun _ ->
        let stamp =
          Flow.row { FlowOpts.Default with Justify = End } [ tile 3 1 1 ]

        let g, _ = runInto 10 1 stamp

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

        let _ = Flow.paint stamp section

        expectCell g 5 0 (ValueSome 2) "clamped at container edge"
        expectCell g 6 0 ValueNone "beyond the container"
    ]

    testList "grid" [
      testCase "fixed tracks and template areas"
      <| fun _ ->
        let stamp =
          Flow.grid {
            Cols = [ Fixed 4; Fixed 6 ]
            Rows = [ Fixed 2; Fixed 3 ]
            Gap = 1
            Areas = [ "a a"; "b ." ]
            Places = [ "a", fillTile 1; "b", fillTile 2 ]
          }

        let g, _ = runInto 12 7 stamp

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
          Flow.grid {
            Cols = [ Fixed 2; Weight 1f; Weight 3f ]
            Rows = [ Fixed 4 ]
            Gap = 0
            Areas = [ "s m b" ]
            Places = [ "s", fillTile 1; "m", fillTile 2; "b", fillTile 3 ]
          }

        let g, _ = runInto 10 4 stamp

        expectCell g 1 0 (ValueSome 1) "fixed column"
        expectCell g 2 0 (ValueSome 2) "weight 1 column"
        expectCell g 3 0 (ValueSome 2) "weight 1 end"
        expectCell g 4 0 (ValueSome 3) "weight 3 start"
        expectCell g 9 0 (ValueSome 3) "weight 3 end"

      testCase "percent tracks take a fraction of the container"
      <| fun _ ->
        let stamp =
          Flow.grid {
            Cols = [ Percent 0.5f; Fixed 2 ]
            Rows = [ Fixed 1 ]
            Gap = 0
            Areas = [ "a b" ]
            Places = [ "a", fillTile 1; "b", fillTile 2 ]
          }

        let g, _ = runInto 10 1 stamp

        expectCell g 4 0 (ValueSome 1) "percent column end"
        expectCell g 5 0 (ValueSome 2) "fixed column start"
        expectCell g 6 0 (ValueSome 2) "fixed column end"

      testCase "unknown area name throws"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Flow.grid {
              Cols = [ Fixed 1 ]
              Rows = [ Fixed 1 ]
              Gap = 0
              Areas = [ "a" ]
              Places = [ "z", tile 1 1 1 ]
            }
            |> ignore)
          "unknown area"

      testCase "empty tracks throw"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Flow.grid {
              Cols = []
              Rows = [ Fixed 1 ]
              Gap = 0
              Areas = [ "a" ]
              Places = []
            }
            |> ignore)
          "no column tracks"

      testCase "template wider than the tracks throws"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Flow.grid {
              Cols = [ Fixed 1 ]
              Rows = [ Fixed 1 ]
              Gap = 0
              Areas = [ "a b" ]
              Places = []
            }
            |> ignore)
          "template column overflow"
    ]

    testList "layers" [
      testCase "overlay stacks full-bleed children, later on top"
      <| fun _ ->
        let stamp =
          Flow.overlay [
            fillTile 1
            Stamp.sized 1 1 (fun s -> s |> Layout.set 0 0 2)
          ]

        let g, _ = runInto 3 1 stamp

        expectCell g 0 0 (ValueSome 2) "later child paints on top"
        expectCell g 1 0 (ValueSome 1) "earlier child shows elsewhere"
        expectCell g 2 0 (ValueSome 1) "base fills the whole area"

      testCase
        "docked elements anchor inside the container and report positions"
      <| fun _ ->
        let gate =
          Stamp.named
            "gate"
            (Stamp.sized 4 1 (fun s -> s |> Layout.fill 0 0 s.Width s.Height 7))
          |> Flow.docked (Dock.StretchX ||| Dock.Bottom) 0

        let stamp = Flow.overlay [ gate ]

        let g, placed = runInto 10 6 stamp

        expectCell g 0 5 (ValueSome 7) "stretched across the bottom row"
        expectCell g 9 5 (ValueSome 7) "stretched across the bottom row"

        Expect.equal
          (Flow.tryPosition "gate" placed)
          (ValueSome { X = 0; Y = 5; W = 10; H = 1 })
          "gate reports the docked rectangle"
    ]

    testList "dock" [
      testCase "top right corner with inset"
      <| fun _ ->
        let g, _ = runInto 10 6 (Stamp.empty())

        let _ =
          g |> Layout.run(Flow.dock (Dock.Top ||| Dock.Right) 1 (tile 2 1 9))

        expectCell g 7 1 (ValueSome 9) "docked start"
        expectCell g 8 1 (ValueSome 9) "docked end"
        expectCell g 6 1 ValueNone "left of docked"

      testCase "center of the section"
      <| fun _ ->
        let g, _ = runInto 10 6 (Stamp.empty())

        let _ =
          g
          |> Layout.run(
            Flow.dock (Dock.CenterX ||| Dock.CenterY) 0 (tile 2 1 9)
          )

        expectCell g 4 2 (ValueSome 9) "centered"
        expectCell g 5 2 (ValueSome 9) "centered"

      testCase "stretch spans the section minus insets"
      <| fun _ ->
        let g, _ = runInto 10 6 (Stamp.empty())

        let _ =
          g
          |> Layout.run(
            Flow.dock
              (Dock.StretchX ||| Dock.Bottom)
              1
              (Stamp.sized 1 1 (fun s ->
                s |> Layout.fill 0 0 s.Width s.Height 5))
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

        let _, result = runInto 20 1 stamp

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
          Flow.grid {
            Cols = [ Fixed 4 ]
            Rows = [ Fixed 4 ]
            Gap = 0
            Areas = [ "a" ]
            Places = [ "a", inner; "a", Stamp.named "area" (Stamp.empty()) ]
          }

        let g, result = runInto 4 4 stamp

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

[<Tests>]
let harbourTests =
  testList "creative map: old harbour" [
    testCase "a noisy harbour built from stamps, grid areas, and docks"
    <| fun _ ->
      // A fountain: pool with lantern posts at the corners.
      let fountain = Stamp.box 5 5 [ Flow.fill Water; Flow.corners Torch ]

      // A cobble plaza with weathered stones and the fountain at its heart.
      // The group states the size once; the fountain just gets centered.
      let plaza =
        Flow.group 30 20 [
          Flow.canvas [
            Flow.fill Path
            Flow.border Grass
            Flow.noise 24 7 Rock
          ]
          Flow.docked
            (Dock.CenterX ||| Dock.CenterY)
            0
            (Stamp.named "fountain" fountain)
        ]

      // A market stall: awning over crates of goods, signed center-front.
      let stall goods =
        Stamp.above
          (Stamp.box 4 1 [ Flow.fill Stall ])
          (Flow.group 4 2 [
            Flow.canvas [ Flow.fill Crate ]
            Flow.docked
              (Dock.CenterX ||| Dock.Top)
              0
              (Stamp.box 1 1 [ Flow.fill goods ])
          ])

      // The market: stalls wrap onto new rows when the area runs out.
      let market =
        Flow.row
          {
            FlowOpts.Default with
                Gap = 1
                Wrap = true
          }
          [ stall Fish; stall Fruit; stall Fish; stall Fruit ]

      // Woods: seeded tree noise, weathered into clearings, rock clusters,
      // fallen stumps, and one chest somewhere in the undergrowth.
      let woods =
        Stamp.box 18 30 [
          Flow.texture(fun x y ->
            if (x * 7 + y * 13) % 4 = 0 then Tree else Grass)
          Flow.weather Tree Grass 0.3f 11
          Flow.clumps 4 5 (fun c -> c |> Layout.circle 1 1 1 true Rock)
          Flow.noise 6 9 Stump
          Flow.noise 1 15 Chest
        ]

      // Docks: water with piers reaching in, lanterns at the pier heads.
      let pier planks =
        Stamp.above
          (Stamp.box 1 1 [ Flow.fill Lamp ])
          (Stamp.box 1 planks [ Flow.fill Path ])

      let docks =
        Flow.group 34 10 [
          Flow.canvas [ Flow.fill Water ]
          Flow.row { FlowOpts.Default with Gap = 9 } [ pier 5; pier 7; pier 5 ]
        ]

      // The map: structure by layout, noise by stamps.
      let harbour =
        Flow.grid {
          Cols = [ Weight 2f; Weight 1f ]
          Rows = [ Fixed 10; Weight 1f; Weight 1f ]
          Gap = 1
          Areas = [ "plaza market"; "plaza woods"; "docks woods" ]
          Places = [
            "plaza", Stamp.tagged [ "safe-zone" ] plaza
            "market", market
            "woods", woods
            "docks", docks
          ]
        }

      let g: CellGrid2D<Tile> =
        CellGrid2D.create 60 44 (Vector2(1f, 1f)) Vector2.Zero

      let expect x y (expected: Tile voption) message =
        Expect.equal (CellGrid2D.get x y g) expected message

      // The banner floats over the map, the gate strip closes the bottom
      // edge, and the harbour fills what is left. One expression, one pipe.
      let struct (_, placed) =
        g
        |> Flow.run(
          Flow.overlay [
            Stamp.expand harbour

            Stamp.box 12 3 [ Flow.fill Wall ]
            |> Flow.docked (Dock.Top ||| Dock.CenterX) 0

            // zero footprint width = stretch over the container
            Stamp.named "gate" (Stamp.box 0 1 [ Flow.fill Sand ])
            |> Flow.docked (Dock.StretchX ||| Dock.Bottom) 0
          ]
        )

      // The layout decided where things are; read the answer back.
      Expect.equal
        (Flow.tryPosition "gate" placed)
        (ValueSome { X = 0; Y = 43; W = 60; H = 1 })
        "gate strip at the bottom edge"

      // Spawn the player at the middle of the gate strip.
      match Flow.tryPosition "gate" placed with
      | ValueSome gate -> CellGrid2D.set (gate.X + gate.W / 2) gate.Y Spawn g
      | ValueNone -> ()

      Expect.equal
        (Flow.tryPosition "fountain" placed)
        (ValueSome { X = 12; Y = 7; W = 5; H = 5 })
        "fountain centered in the plaza"

      expect 30 43 (ValueSome Spawn) "spawn read back from the gate"

      expect 12 7 (ValueSome Torch) "fountain lantern post"
      expect 14 9 (ValueSome Water) "fountain pool"

      expect 2 2 (ValueSome Path) "plaza cobble inside the fringe"

      expect 41 0 (ValueSome Stall) "market awning"

      expect 0 28 (ValueSome Lamp) "pier head lantern"
      expect 0 30 (ValueSome Path) "pier planks"
      expect 4 37 (ValueSome Water) "docks water past the piers"
  ]

[<Tests>]
let styleTests =
  testList "styles" [
    testCase "box paints its style list over its own area"
    <| fun _ ->
      let stamp = Stamp.box 3 3 [ Flow.fill 1; Flow.border 2 ]
      let g, _ = runInto 3 3 stamp

      expectCell g 0 0 (ValueSome 2) "border corner"
      expectCell g 1 1 (ValueSome 1) "fill interior"

    testCase "canvas stretches in rows by default"
    <| fun _ ->
      let stamp =
        Flow.row FlowOpts.Default [
          tile 2 1 1
          Stamp.expand(Flow.canvas [ Flow.fill 2 ])
        ]

      let g, _ = runInto 6 3 stamp

      expectCell g 0 0 (ValueSome 1) "fixed child"
      expectCell g 2 0 (ValueSome 2) "canvas fills the rest of the row"
      expectCell g 5 2 (ValueSome 2) "canvas stretches across, too"

    testCase "canvas stretches over a grid area"
    <| fun _ ->
      let stamp =
        Flow.grid {
          Cols = [ Fixed 4 ]
          Rows = [ Fixed 2 ]
          Gap = 0
          Areas = [ "a" ]
          Places = [ "a", Flow.canvas [ Flow.fill 7 ] ]
        }

      let g, _ = runInto 4 2 stamp

      expectCell g 0 0 (ValueSome 7) "canvas fills the area"
      expectCell g 3 1 (ValueSome 7) "canvas fills the area"

    testCase "fixed boxes keep their size in a grid area"
    <| fun _ ->
      let stamp =
        Flow.grid {
          Cols = [ Fixed 4 ]
          Rows = [ Fixed 4 ]
          Gap = 0
          Areas = [ "a" ]
          Places = [ "a", Stamp.box 2 1 [ Flow.fill 5 ] ]
        }

      let g, _ = runInto 4 4 stamp

      expectCell g 1 0 (ValueSome 5) "box keeps its width"
      expectCell g 2 0 ValueNone "no stretch past the box"

    testCase "strip docks a full-length bar"
    <| fun _ ->
      let stamp = Flow.overlay [ Flow.strip Dock.Bottom 1 [ Flow.fill 9 ] ]
      let g, _ = runInto 5 4 stamp

      expectCell g 0 3 (ValueSome 9) "strip start"
      expectCell g 4 3 (ValueSome 9) "strip end"
      expectCell g 2 2 ValueNone "above the strip"

    testCase "group sizes the canvas and anchors children"
    <| fun _ ->
      let stamp =
        Flow.group 4 2 [
          Flow.canvas [ Flow.fill 1 ]
          Flow.docked
            (Dock.CenterX ||| Dock.CenterY)
            0
            (Stamp.box 2 2 [ Flow.fill 2 ])
        ]

      let g, _ = runInto 10 10 stamp

      expectCell g 0 0 (ValueSome 1) "canvas fills the group"
      expectCell g 3 1 (ValueSome 1) "canvas fills the group"
      expectCell g 1 0 (ValueSome 2) "centered child start"
      expectCell g 2 1 (ValueSome 2) "centered child end"
      expectCell g 4 0 ValueNone "group does not stretch"

    testCase "weather replaces box-wide with a probability"
    <| fun _ ->
      let stamp = Stamp.box 2 1 [ Flow.fill 1; Flow.weather 1 2 1.0f 3 ]
      let g, _ = runInto 4 1 stamp

      expectCell g 0 0 (ValueSome 2) "weathered"
      expectCell g 1 0 (ValueSome 2) "weathered"
  ]

[<Tests>]
let landmarkTests =
  testList "landmarks" [
    testCase "tagged elements group their rectangles"
    <| fun _ ->
      let stamp =
        Flow.row { FlowOpts.Default with Gap = 2 } [
          Stamp.tagged [ "spawn" ] (tile 2 1 1)
          Stamp.tagged [ "spawn"; "danger" ] (tile 2 1 2)
        ]

      let _, placed = runInto 10 1 stamp

      let spawns = Flow.taggedRects "spawn" placed
      Expect.hasLength spawns 2 "two spawn rects"

      Expect.equal
        (Flow.taggedRects "danger" placed)
        [ { X = 4; Y = 0; W = 2; H = 1 } ]
        "danger rect, most recent first"

      Expect.equal (Flow.taggedRects "nothing" placed) [] "unknown tag"

    testCase "tagged cells answer walking queries"
    <| fun _ ->
      let stamp =
        Flow.grid {
          Cols = [ Fixed 3; Fixed 3 ]
          Rows = [ Fixed 4 ]
          Gap = 0
          Areas = [ "zone rest" ]
          Places = [
            "zone", Flow.region [ "no-build" ] 0 0
            "rest", Flow.canvas [ Flow.fill 9 ]
          ]
        }

      let g, placed = runInto 6 4 stamp

      Expect.isTrue (Flow.isTag "no-build" 0 0 placed) "inside the region"
      Expect.isTrue (Flow.isTag "no-build" 2 1 placed) "region corner"
      Expect.isFalse (Flow.isTag "no-build" 3 1 placed) "outside the region"
      Expect.isFalse (Flow.isTag "no-build" 5 3 placed) "far outside"

      Expect.isFalse
        (Flow.isTag "no-build" -1 0 placed)
        "out of range is never tagged"

      Expect.isFalse (Flow.isTag "unknown" 0 0 placed) "unknown tag"

      expectCell g 3 1 (ValueSome 9) "the neighbor area paints normally"

    testCase "region stretches like any zero-footprint child"
    <| fun _ ->
      let stamp =
        Flow.grid {
          Cols = [ Fixed 4 ]
          Rows = [ Fixed 4 ]
          Gap = 0
          Areas = [ "a" ]
          Places = [ "a", Flow.region [ "arena" ] 0 0 ]
        }

      let _, placed = runInto 4 4 stamp

      Expect.isTrue (Flow.isTag "arena" 3 3 placed) "stretched over the area"

      Expect.equal
        (Flow.taggedRects "arena" placed)
        [ { X = 0; Y = 0; W = 4; H = 4 } ]
        "region rect covers the area"

    testCase "scanTiles derives cell tags from the painted tiles"
    <| fun _ ->
      let stamp = Flow.row FlowOpts.Default [ tile 2 1 1; tile 2 1 2 ]

      let g, placed = runInto 5 1 stamp

      let tileTags _ _ (v: int) =
        if v = 2 then seq { "dangerous" } else seq { "safe" }

      let scanned = placed |> Landmarks.scanTiles tileTags g

      Expect.isTrue (Flow.isTag "safe" 0 0 scanned) "tile 1 is safe"
      Expect.isTrue (Flow.isTag "safe" 1 0 scanned) "tile 1 is safe"
      Expect.isTrue (Flow.isTag "dangerous" 2 0 scanned) "tile 2 is dangerous"
      Expect.isTrue (Flow.isTag "dangerous" 3 0 scanned) "tile 2 is dangerous"

      Expect.isFalse
        (Flow.isTag "dangerous" 0 0 scanned)
        "tile 1 is not dangerous"

      Expect.isFalse (Flow.isTag "safe" 4 0 scanned) "empty cell has no tags"

    testCase "tagged and named compose with docking"
    <| fun _ ->
      let gate =
        Stamp.tagged [ "exit"; "no-build" ] (Stamp.box 0 1 [ Flow.fill 7 ])
        |> Flow.docked (Dock.StretchX ||| Dock.Bottom) 0

      let stamp = Flow.overlay [ gate ]
      let g, placed = runInto 6 4 stamp

      expectCell g 0 3 (ValueSome 7) "gate painted on the bottom edge"

      Expect.equal
        (Flow.tryPosition "gate2" placed)
        ValueNone
        "no name, no position"

      Expect.equal
        (Flow.taggedRects "exit" placed)
        [ { X = 0; Y = 3; W = 6; H = 1 } ]
        "tagged with the docked rectangle"

      Expect.isTrue
        (Flow.isTag "exit" 3 3 placed)
        "walkable query on the docked rect"

      Expect.isFalse (Flow.isTag "exit" 3 2 placed) "one above is not the gate"
  ]
