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

        // layers are full-bleed, so Stamp.overlay takes zero-footprint
        // elements and is context-sized
        let overlay = Stamp.overlay (Stamp.empty()) (Stamp.empty())
        Expect.equal overlay.W 0 "overlay width"
        Expect.equal overlay.H 0 "overlay height"

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

      testCase "a wrapped line of zero-cross children stretches"
      <| fun _ ->
        let stamp =
          Flow.row
            {
              FlowOpts.Default with
                  Wrap = true
                  Gap = 1
            }
            [ Stamp.box 2 0 [ Flow.fill 1 ]; Stamp.box 2 0 [ Flow.fill 2 ] ]

        let g, _ = runInto 5 3 stamp

        expectCell g 0 0 (ValueSome 1) "first child start"
        expectCell g 1 2 (ValueSome 1) "first child spans the height"
        expectCell g 3 0 (ValueSome 2) "second child start"
        expectCell g 4 2 (ValueSome 2) "second child spans the height"

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

    testList "expand containers" [
      testCase "overlay rejects expand children"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () -> Flow.overlay [ Stamp.expand(fillTile 1) ] |> ignore)
          "overlay ignores Expand"

      testCase "group rejects expand children"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () -> Flow.group 4 4 [ Stamp.expand(fillTile 1) ] |> ignore)
          "group ignores Expand"

      testCase "grid rejects expand places"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Flow.grid {
              Cols = [| Fixed 4 |]
              Rows = [| Fixed 4 |]
              Gap = 0
              Areas = [| "a" |]
              Places = [| struct (Area "a", Stamp.expand(tile 1 1 1)) |]
            }
            |> ignore)
          "grid ignores Expand"

      testCase "docked rejects an expand stamp"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Flow.docked {
              Anchor = Dock.CenterX ||| Dock.CenterY
              Inset = InsetSpec.Zero
              Stamp = Stamp.expand(fillTile 1)
            }
            |> ignore)
          "docked ignores Expand"

      testCase "run rejects an expand root"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () -> runInto 5 5 (Stamp.expand(Stamp.empty())) |> ignore)
          "run ignores Expand"
    ]

    testList "expand combinators" [
      testCase "beside rejects expand children"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Stamp.beside (Stamp.expand(fillTile 1)) (fillTile 2) |> ignore)
          "beside ignores Expand"

      testCase "beside rejects an expanded second child"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Stamp.beside (fillTile 2) (Stamp.expand(fillTile 1)) |> ignore)
          "beside ignores Expand on the second child"

      testCase "above rejects expand children"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Stamp.above (Stamp.expand(fillTile 1)) (fillTile 2) |> ignore)
          "above ignores Expand"

      testCase "overlay rejects expand children"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Stamp.overlay (Stamp.expand(fillTile 1)) (fillTile 2) |> ignore)
          "overlay ignores Expand"

      testCase "inset rejects an expand stamp"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () -> Stamp.inset 1 (Stamp.expand(fillTile 1)) |> ignore)
          "inset ignores Expand"

      testCase "offset rejects an expand stamp"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () -> Stamp.offset 1 0 (Stamp.expand(fillTile 1)) |> ignore)
          "offset ignores Expand"

      testCase "repeat rejects an expand stamp"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () -> Stamp.repeat 2 (Stamp.expand(fillTile 1)) |> ignore)
          "repeat ignores Expand"
    ]

    testList "grid" [
      testCase "fixed tracks and template areas"
      <| fun _ ->
        let stamp =
          Flow.grid {
            Cols = [| Fixed 4; Fixed 6 |]
            Rows = [| Fixed 2; Fixed 3 |]
            Gap = 1
            Areas = [| "a a"; "b ." |]
            Places = [|
              struct (Area "a", fillTile 1)
              struct (Area "b", fillTile 2)
            |]
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
            Cols = [| Fixed 2; Weight 1f; Weight 3f |]
            Rows = [| Fixed 4 |]
            Gap = 0
            Areas = [| "s m b" |]
            Places = [|
              struct (Area "s", fillTile 1)
              struct (Area "m", fillTile 2)
              struct (Area "b", fillTile 3)
            |]
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
            Cols = [| Percent 0.5f; Fixed 2 |]
            Rows = [| Fixed 1 |]
            Gap = 0
            Areas = [| "a b" |]
            Places = [|
              struct (Area "a", fillTile 1)
              struct (Area "b", fillTile 2)
            |]
          }

        let g, _ = runInto 10 1 stamp

        expectCell g 4 0 (ValueSome 1) "percent column end"
        expectCell g 5 0 (ValueSome 2) "fixed column start"
        expectCell g 6 0 (ValueSome 2) "fixed column end"

      testCase "percent tracks clamp at the container length"
      <| fun _ ->
        let stamp =
          Flow.grid {
            Cols = [| Percent 2f |]
            Rows = [| Fixed 1 |]
            Gap = 0
            Areas = [| "a" |]
            Places = [|
              struct (Area "a", Stamp.named "a" (Flow.canvas [ Flow.fill 7 ]))
            |]
          }

        let g, placed = runInto 6 1 stamp

        expectCell g 0 0 (ValueSome 7) "clamp start"
        expectCell g 5 0 (ValueSome 7) "clamp end"

        Expect.equal
          (Flow.tryPosition "a" placed)
          (ValueSome { X = 0; Y = 0; W = 6; H = 1 })
          "the reported rect stays inside the container"

      testCase "unknown area name throws"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Flow.grid {
              Cols = [| Fixed 1 |]
              Rows = [| Fixed 1 |]
              Gap = 0
              Areas = [| "a" |]
              Places = [| struct (Area "z", tile 1 1 1) |]
            }
            |> ignore)
          "unknown area"

      testCase "empty tracks throw"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Flow.grid {
              Cols = [||]
              Rows = [| Fixed 1 |]
              Gap = 0
              Areas = [| "a" |]
              Places = [||]
            }
            |> ignore)
          "no column tracks"

      testCase "template wider than the tracks throws"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Flow.grid {
              Cols = [| Fixed 1 |]
              Rows = [| Fixed 1 |]
              Gap = 0
              Areas = [| "a b" |]
              Places = [||]
            }
            |> ignore)
          "template column overflow"

      testCase "slots place children by track indices"
      <| fun _ ->
        let stamp =
          Flow.grid {
            Cols = [| Fixed 4; Fixed 6 |]
            Rows = [| Fixed 2; Fixed 3 |]
            Gap = 0
            Areas = [||]
            Places = [|
              struct (Slot(0, 1, 1, 1), fillTile 2)
              struct (Slot(1, 0, 1, 1), fillTile 1)
            |]
          }

        let g, _ = runInto 10 5 stamp

        expectCell g 4 0 (ValueSome 1) "slot (1,0) starts at the second column"
        expectCell g 9 1 (ValueSome 1) "slot (1,0) ends at the second column"
        expectCell g 0 2 (ValueSome 2) "slot (0,1) starts at the second row"
        expectCell g 3 4 (ValueSome 2) "slot (0,1) ends at the second row"
        expectCell g 0 0 ValueNone "unplaced track cell"

      testCase "slots span tracks and cover the gap"
      <| fun _ ->
        let stamp =
          Flow.grid {
            Cols = [| Fixed 2; Fixed 2 |]
            Rows = [| Fixed 1 |]
            Gap = 1
            Areas = [||]
            Places = [| struct (Slot(0, 0, 2, 1), fillTile 3) |]
          }

        Expect.equal stamp.W 5 "the span counts both tracks and the gap"

        let g, _ = runInto 6 1 stamp

        expectCell g 0 0 (ValueSome 3) "span start"
        expectCell g 4 0 (ValueSome 3) "span covers the gap"
        expectCell g 5 0 ValueNone "past the span"

      testCase "a slot outside the tracks throws"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Flow.grid {
              Cols = [| Fixed 2 |]
              Rows = [| Fixed 2 |]
              Gap = 0
              Areas = [||]
              Places = [| struct (Slot(1, 0, 1, 1), fillTile 1) |]
            }
            |> ignore)
          "slot past the last column"

      testCase "a slot index past the int32 range throws"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Flow.grid {
              Cols = [| Fixed 2 |]
              Rows = [| Fixed 2 |]
              Gap = 0
              Areas = [||]
              Places = [|
                struct (Slot(System.Int32.MaxValue, 0, 1, 1), fillTile 1)
              |]
            }
            |> ignore)
          "a huge column index must not wrap into a valid slot"

        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Flow.grid {
              Cols = [| Fixed 2 |]
              Rows = [| Fixed 2 |]
              Gap = 0
              Areas = [||]
              Places = [|
                struct (Slot(0, 0, System.Int32.MaxValue, 1), fillTile 1)
              |]
            }
            |> ignore)
          "a huge span must not wrap into a valid slot"

      testCase "a slot span below one throws"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () ->
            Flow.grid {
              Cols = [| Fixed 2 |]
              Rows = [| Fixed 2 |]
              Gap = 0
              Areas = [||]
              Places = [| struct (Slot(0, 0, 0, 1), fillTile 1) |]
            }
            |> ignore)
          "a zero span never silently clamps to one"

      testCase "overlapping slots paint in Places order"
      <| fun _ ->
        let stamp =
          Flow.grid {
            Cols = [| Fixed 4 |]
            Rows = [| Fixed 2 |]
            Gap = 0
            Areas = [||]
            Places = [|
              struct (Slot(0, 0, 1, 1), fillTile 1)
              struct (Slot(0, 0, 1, 1), Flow.at 2 0 (tile 2 1 2))
            |]
          }

        let g, _ = runInto 4 2 stamp

        expectCell g 2 0 (ValueSome 2) "the later place paints on top"
        expectCell g 0 1 (ValueSome 1) "the earlier place shows elsewhere"

      testCase "auto tracks size to their span-1 places"
      <| fun _ ->
        let stamp =
          Flow.grid {
            Cols = [| Auto; Weight 1f |]
            Rows = [| Auto |]
            Gap = 0
            Areas = [||]
            Places = [|
              struct (Slot(0, 0, 1, 1), tile 3 2 1)
              struct (Slot(1, 0, 1, 1), fillTile 2)
            |]
          }

        Expect.equal stamp.W 3 "the auto width counts toward the footprint"
        Expect.equal stamp.H 2 "the auto height counts toward the footprint"

        let g, _ = runInto 10 2 stamp

        expectCell g 0 0 (ValueSome 1) "auto column sized to the tile"
        expectCell g 2 1 (ValueSome 1) "auto column ends at the tile width"
        expectCell g 3 0 (ValueSome 2) "weight column takes the rest"
        expectCell g 9 1 (ValueSome 2) "weight column fills the container"

      testCase "canvas places contribute nothing to auto tracks"
      <| fun _ ->
        let stamp =
          Flow.grid {
            Cols = [| Auto |]
            Rows = [| Fixed 2 |]
            Gap = 0
            Areas = [||]
            Places = [| struct (Slot(0, 0, 1, 1), fillTile 1) |]
          }

        Expect.equal stamp.W 0 "a context-sized place reports no footprint"

        let g, _ = runInto 4 2 stamp

        expectCell g 0 0 ValueNone "an auto track with no content collapses"

      testCase "a spanning place sizes the auto tracks it covers"
      <| fun _ ->
        let stamp =
          Flow.grid {
            Cols = [| Auto; Auto |]
            Rows = [| Auto |]
            Gap = 0
            Areas = [||]
            Places = [|
              struct (Slot(0, 0, 2, 1), Stamp.named "hall" (tile 4 1 1))
            |]
          }

        // the place shares its four cells over both auto columns, so
        // neither collapses to zero and the place still paints
        Expect.equal stamp.W 4 "the spanning footprint sizes the tracks"

        let g, placed = runInto 8 1 stamp

        Expect.equal
          (Flow.tryPosition "hall" placed)
          (ValueSome { X = 0; Y = 0; W = 4; H = 1 })
          "the spanning place paints its whole footprint"

        expectCell g 0 0 (ValueSome 1) "the span starts at the first column"
        expectCell g 3 0 (ValueSome 1) "the span covers the shared width"
        expectCell g 4 0 ValueNone "nothing paints past the footprint"

      testCase "areas and slots share one grid"
      <| fun _ ->
        let stamp =
          Flow.grid {
            Cols = [| Fixed 4; Fixed 4 |]
            Rows = [| Fixed 2; Fixed 2 |]
            Gap = 0
            Areas = [| "top ." |]
            Places = [|
              struct (Area "top", fillTile 1)
              struct (Slot(1, 1, 1, 1), fillTile 2)
            |]
          }

        let g, _ = runInto 8 4 stamp

        expectCell g 0 0 (ValueSome 1) "the named area paints its cell"
        expectCell g 4 2 (ValueSome 2) "the slot paints at (1,1)"
        expectCell g 3 3 ValueNone "below the area stays empty"
    ]

    testList "layers" [
      testCase "overlay stacks full-bleed children, later on top"
      <| fun _ ->
        let stamp =
          Flow.overlay [
            fillTile 1
            Flow.at 0 0 (Stamp.sized 1 1 (fun s -> s |> Layout.set 0 0 2))
          ]

        let g, _ = runInto 3 1 stamp

        expectCell g 0 0 (ValueSome 2) "later child paints on top"
        expectCell g 1 0 (ValueSome 1) "earlier child shows elsewhere"
        expectCell g 2 0 (ValueSome 1) "base fills the whole area"

      testCase "overlay rejects sized children"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () -> Flow.overlay [ fillTile 1; tile 2 2 2 ] |> ignore)
          "overlay ignores footprints"

      testCase "group rejects sized children"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () -> Flow.group 4 4 [ fillTile 1; tile 1 1 1 ] |> ignore)
          "group ignores footprints"

      testCase "Stamp.overlay rejects sized children"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () -> Stamp.overlay (tile 1 1 1) (fillTile 2) |> ignore)
          "Stamp.overlay ignores footprints"

      testCase "overlay is context-sized"
      <| fun _ ->
        let stamp =
          Flow.overlay [ fillTile 1; Flow.strip Dock.Bottom 1 [ Flow.fill 2 ] ]

        Expect.equal stamp.W 0 "every legal layer is zero-footprint"
        Expect.equal stamp.H 0 "every legal layer is zero-footprint"

      testCase "stretch mounts a sized layout as a layer"
      <| fun _ ->
        // a sized box paints section-relative styles, so stretch makes it
        // fill the whole container — the opt-in full-bleed form
        let stamp =
          Flow.overlay [
            Flow.stretch(Stamp.named "base" (Stamp.box 3 2 [ Flow.fill 7 ]))
          ]

        let g, placed = runInto 6 4 stamp

        Expect.equal
          (Flow.tryPosition "base" placed)
          (ValueSome { X = 0; Y = 0; W = 6; H = 4 })
          "the sized layout stretched over the container"

        expectCell g 5 3 (ValueSome 7) "stretched to the far corner"
        expectCell g 0 0 (ValueSome 7) "stretched from the origin"

      testCase
        "docked elements anchor inside the container and report positions"
      <| fun _ ->
        let gate =
          Flow.docked {
            Anchor = Dock.StretchX ||| Dock.Bottom
            Inset = InsetSpec.Zero
            Stamp =
              Stamp.named
                "gate"
                (Stamp.sized 4 1 (fun s ->
                  s |> Layout.fill 0 0 s.Width s.Height 7))
          }

        let stamp = Flow.overlay [ gate ]

        let g, placed = runInto 10 6 stamp

        expectCell g 0 5 (ValueSome 7) "stretched across the bottom row"
        expectCell g 9 5 (ValueSome 7) "stretched across the bottom row"

        Expect.equal
          (Flow.tryPosition "gate" placed)
          (ValueSome { X = 0; Y = 5; W = 10; H = 1 })
          "gate reports the docked rectangle"
    ]

    testList "scatter" [
      testCase "footprint is the largest child"
      <| fun _ ->
        let stamp = Flow.scatter 1 [ tile 2 3 1; tile 4 1 2 ]

        Expect.equal stamp.W 4 "scatter width is the largest child width"
        Expect.equal stamp.H 3 "scatter height is the largest child height"

      testCase "scatters children inside the container without overlap"
      <| fun _ ->
        let stamp =
          Flow.scatter 7 [
            Stamp.named "a" (tile 2 2 1)
            Stamp.named "b" (tile 2 2 2)
            Stamp.named "c" (tile 2 2 3)
          ]

        let g, placed = runInto 8 8 stamp

        let disjoint (a: CellRect) (b: CellRect) =
          a.X >= b.X + b.W
          || b.X >= a.X + a.W
          || a.Y >= b.Y + b.H
          || b.Y >= a.Y + a.H

        let named =
          [ struct ("a", 1); struct ("b", 2); struct ("c", 3) ]
          |> List.choose(fun struct (n, content) ->
            match Flow.tryPosition n placed with
            | ValueSome r -> Some(n, content, r)
            | ValueNone -> None)

        Expect.hasLength named 3 "every child reports a rectangle"

        for (n, content, r) in named do
          Expect.equal r.W 2 $"{n} keeps its footprint"
          Expect.equal r.H 2 $"{n} keeps its footprint"
          Expect.isLessThan r.X 7 $"{n} stays inside the container"
          Expect.isLessThan r.Y 7 $"{n} stays inside the container"

          for x in r.X .. r.X + r.W - 1 do
            for y in r.Y .. r.Y + r.H - 1 do
              expectCell g x y (ValueSome content) $"{n} paints its rectangle"

        let rs = named |> List.map(fun (_, _, r) -> r)

        for i in 0 .. rs.Length - 1 do
          for j in i + 1 .. rs.Length - 1 do
            Expect.isTrue
              (disjoint rs.[i] rs.[j])
              $"scattered rect {i} and rect {j} do not overlap"

      testCase "the same seed builds the same level"
      <| fun _ ->
        let build() =
          Flow.scatter 13 [ tile 2 2 1; tile 2 2 2; tile 2 2 3 ]
          |> runInto 10 10
          |> fst

        let a = build()
        let b = build()
        let mutable same = true

        CellGrid2D.iter
          (fun x y v -> same <- same && (ValueSome v = CellGrid2D.get x y b))
          a

        Expect.isTrue same "rebuilds are identical"

      testCase "a different seed builds a different level"
      <| fun _ ->
        let build seed =
          Flow.scatter seed [ tile 2 2 1; tile 2 2 2; tile 2 2 3 ]
          |> runInto 10 10
          |> fst

        let a = build 1
        let b = build 2
        let mutable same = true

        CellGrid2D.iter
          (fun x y v -> same <- same && (ValueSome v = CellGrid2D.get x y b))
          a

        Expect.isFalse same "seeds 1 and 2 place differently"

      testCase "a child that fits nowhere fails the build"
      <| fun _ ->
        Expect.throwsT<System.InvalidOperationException>
          (fun () ->
            Flow.scatter 1 [ tile 6 6 1; tile 6 6 2 ] |> runInto 5 5 |> ignore)
          "a 6x6 child never fits a 5x5 container"

      testCase "the failure names a named child"
      <| fun _ ->
        let thrown =
          try
            Flow.scatter 1 [ Stamp.named "rock" (tile 6 6 1); tile 6 6 2 ]
            |> runInto 5 5
            |> ignore

            None
          with :? System.InvalidOperationException as e ->
            Some e.Message

        match thrown with
        | Some msg ->
          Expect.stringContains msg "'rock'" "the error names the child"
        | None -> failtest "the scatter must fail"

      testCase "a context-sized child places as a single cell"
      <| fun _ ->
        let stamp = Flow.scatter 7 [ fillTile 1; Flow.canvas [ Flow.fill 9 ] ]

        let g, _ = runInto 6 6 stamp

        let mutable painted = 0

        CellGrid2D.iter
          (fun _ _ v ->
            if v = 9 then
              painted <- painted + 1)
          g

        Expect.equal
          painted
          1
          "the zero-footprint child paints exactly one cell"

      testCase "the seed that would freeze the xorshift still shuffles"
      <| fun _ ->
        // uint32 seed * 2654435761 + 2891336453 wraps to 0 for exactly
        // one seed; without the re-mix, next() would always return 0
        // and the permutation would degenerate into scan order
        let frozen = -1623893909

        let build() =
          Flow.scatter frozen [
            Stamp.named "a" (tile 1 1 1)
            Stamp.named "b" (tile 1 1 2)
            Stamp.named "c" (tile 1 1 3)
          ]
          |> runInto 6 1
          |> snd

        let placed = build()
        let again = build()

        let rect name marks =
          match Flow.tryPosition name marks with
          | ValueSome r -> struct (r.X, r.Y)
          | ValueNone -> struct (-1, -1)

        Expect.equal (rect "a" placed) (rect "a" again) "rebuilds are identical"
        Expect.equal (rect "b" placed) (rect "b" again) "rebuilds are identical"
        Expect.equal (rect "c" placed) (rect "c" again) "rebuilds are identical"

        // a frozen shuffle would place the children in scan order:
        // a at (0,0), b at (1,0), c at (2,0)
        let scanOrder =
          struct (0, 0) = rect "a" placed
          && struct (1, 0) = rect "b" placed
          && struct (2, 0) = rect "c" placed

        Expect.isFalse scanOrder "the permutation is shuffled, not scan order"

      testCase "expand children throw"
      <| fun _ ->
        Expect.throwsT<System.ArgumentException>
          (fun () -> Flow.scatter 1 [ Stamp.expand(fillTile 1) ] |> ignore)
          "scatter ignores Expand"
    ]

    testList "dock" [
      testCase "top right corner with inset"
      <| fun _ ->
        let g, _ = runInto 10 6 (Stamp.empty())

        let _ =
          g
          |> Layout.run(
            Flow.dock {
              Anchor = Dock.Top ||| Dock.Right
              Inset = {
                Left = 1
                Top = 1
                Right = 1
                Bottom = 1
              }
              Stamp = tile 2 1 9
            }
          )

        expectCell g 7 1 (ValueSome 9) "docked start"
        expectCell g 8 1 (ValueSome 9) "docked end"
        expectCell g 6 1 ValueNone "left of docked"

      testCase "center of the section"
      <| fun _ ->
        let g, _ = runInto 10 6 (Stamp.empty())

        let _ =
          g
          |> Layout.run(
            Flow.dock {
              Anchor = Dock.CenterX ||| Dock.CenterY
              Inset = InsetSpec.Zero
              Stamp = tile 2 1 9
            }
          )

        expectCell g 4 2 (ValueSome 9) "centered"
        expectCell g 5 2 (ValueSome 9) "centered"

      testCase "stretch spans the section minus insets"
      <| fun _ ->
        let g, _ = runInto 10 6 (Stamp.empty())

        let _ =
          g
          |> Layout.run(
            Flow.dock {
              Anchor = Dock.StretchX ||| Dock.Bottom
              Inset = {
                Left = 1
                Top = 1
                Right = 1
                Bottom = 1
              }
              Stamp =
                Stamp.sized 1 1 (fun s ->
                  s |> Layout.fill 0 0 s.Width s.Height 5)
            }
          )

        expectCell g 0 4 ValueNone "left inset"
        expectCell g 1 4 (ValueSome 5) "stretch start"
        expectCell g 8 4 (ValueSome 5) "stretch end"
        expectCell g 9 4 ValueNone "right inset"

      testCase "zero footprint stretches without the stretch flags"
      <| fun _ ->
        let stamp =
          Flow.overlay [
            Flow.docked {
              Anchor = Dock.Bottom
              Inset = InsetSpec.Zero
              Stamp = Stamp.box 0 1 [ Flow.fill 7 ]
            }
          ]

        let g, _ = runInto 6 4 stamp

        expectCell g 0 3 (ValueSome 7) "stretched across the bottom row"
        expectCell g 5 3 (ValueSome 7) "stretched across the bottom row"
        expectCell g 0 2 ValueNone "above the docked row"

      testCase "a docked canvas fills the container"
      <| fun _ ->
        let stamp =
          Flow.overlay [
            Flow.docked {
              Anchor = Dock.CenterX ||| Dock.CenterY
              Inset = InsetSpec.Zero
              Stamp = Flow.canvas [ Flow.fill 7 ]
            }
          ]

        let g, _ = runInto 4 3 stamp

        expectCell g 0 0 (ValueSome 7) "full bleed start"
        expectCell g 3 2 (ValueSome 7) "full bleed end"
    ]

    testList "exact placement" [
      testCase "per-side insets place the x and y edges apart"
      <| fun _ ->
        let stamp =
          Flow.overlay [
            Flow.docked {
              Anchor = Dock.StretchX ||| Dock.Bottom
              Inset = {
                Left = 2
                Top = 0
                Right = 1
                Bottom = 1
              }
              Stamp = Stamp.named "hud" (Stamp.box 0 2 [ Flow.fill 9 ])
            }
          ]

        let g, placed = runInto 10 8 stamp

        Expect.equal
          (Flow.tryPosition "hud" placed)
          (ValueSome { X = 2; Y = 5; W = 7; H = 2 })
          "stretched between the left and right insets, off the bottom inset"

        expectCell g 2 5 (ValueSome 9) "hud start"
        expectCell g 8 6 (ValueSome 9) "hud end"
        expectCell g 1 5 ValueNone "left of the hud"

      testCase "a right-edge anchor honors its own inset"
      <| fun _ ->
        let stamp =
          Flow.overlay [
            Flow.docked {
              Anchor = Dock.Right ||| Dock.Top
              Inset = {
                Left = 0
                Top = 1
                Right = 3
                Bottom = 0
              }
              Stamp = Stamp.named "sign" (tile 2 2 7)
            }
          ]

        let _, placed = runInto 10 8 stamp

        Expect.equal
          (Flow.tryPosition "sign" placed)
          (ValueSome { X = 5; Y = 1; W = 2; H = 2 })
          "3 off the right edge, 1 off the top"

      testCase "at places an element at an exact offset"
      <| fun _ ->
        let stamp =
          Flow.overlay [
            fillTile 1
            Flow.at 3 5 (Stamp.named "prop" (tile 2 1 7))
          ]

        let g, placed = runInto 10 10 stamp

        Expect.equal
          (Flow.tryPosition "prop" placed)
          (ValueSome { X = 3; Y = 5; W = 2; H = 1 })
          "exact offset from the container origin"

        expectCell g 3 5 (ValueSome 7) "prop start"
        expectCell g 4 5 (ValueSome 7) "prop end"
        expectCell g 5 5 (ValueSome 1) "past the prop"

      testCase "a zero dimension of at stretches to the far edge"
      <| fun _ ->
        let stamp =
          Flow.overlay [
            Flow.at 2 1 (Stamp.named "bar" (Stamp.box 0 2 [ Flow.fill 7 ]))
          ]

        let g, placed = runInto 6 4 stamp

        Expect.equal
          (Flow.tryPosition "bar" placed)
          (ValueSome { X = 2; Y = 1; W = 4; H = 2 })
          "stretches from the origin to the container's right edge"

        expectCell g 5 2 (ValueSome 7) "stretched end"
        expectCell g 1 1 ValueNone "before the origin"

      testCase "fillRect paints a local sub-rectangle"
      <| fun _ ->
        let stamp =
          Stamp.box 6 4 [
            Flow.fill 1
            Flow.fillRect { X = 2; Y = 1; W = 2; H = 2 } 5
          ]

        let g, _ = runInto 6 4 stamp

        expectCell g 2 1 (ValueSome 5) "sub-rect start"
        expectCell g 3 2 (ValueSome 5) "sub-rect end"
        expectCell g 0 0 (ValueSome 1) "outside keeps the base fill"
        expectCell g 1 1 (ValueSome 1) "left of the sub-rect"
        expectCell g 4 1 (ValueSome 1) "right of the sub-rect"

      testCase "a negative inset side clamps at zero"
      <| fun _ ->
        let stamp =
          Flow.overlay [
            Flow.docked {
              Anchor = Dock.StretchX ||| Dock.Top
              Inset = {
                Left = -4
                Top = 1
                Right = 1
                Bottom = -2
              }
              Stamp = Stamp.named "bar" (Stamp.box 0 2 [ Flow.fill 7 ])
            }
          ]

        let g, placed = runInto 10 6 stamp

        // the negative Left clamps to 0, the negative Bottom stays unused
        Expect.equal
          (Flow.tryPosition "bar" placed)
          (ValueSome { X = 0; Y = 1; W = 9; H = 2 })
          "the negative sides clamp at zero"

        expectCell g 0 1 (ValueSome 7) "stretches from the left edge"
        expectCell g 8 2 (ValueSome 7) "ends before the right inset"

      testCase "a negative at offset clamps at zero"
      <| fun _ ->
        let stamp =
          Flow.overlay [ Flow.at -3 -2 (Stamp.named "p" (tile 2 1 7)) ]

        let _, placed = runInto 10 6 stamp

        Expect.equal
          (Flow.tryPosition "p" placed)
          (ValueSome { X = 0; Y = 0; W = 2; H = 1 })
          "negative offsets clamp at the container origin"

      testCase "an inset larger than the container clamps the span to zero"
      <| fun _ ->
        let stamp =
          Flow.overlay [
            Flow.docked {
              Anchor = Dock.StretchX ||| Dock.StretchY
              Inset = {
                Left = 4
                Top = 4
                Right = 4
                Bottom = 4
              }
              Stamp = Stamp.named "huge" (Stamp.box 0 0 [ Flow.fill 7 ])
            }
          ]

        let g, placed = runInto 6 6 stamp

        // the span clamps at zero (never negative), and a zero-size rect
        // paints and records nothing
        Expect.equal (Flow.tryPosition "huge" placed) ValueNone "no rectangle"
        expectCell g 0 0 ValueNone "nothing paints"

      testCase "an element past the container edge clips inside it"
      <| fun _ ->
        // a centered element wider than the container starts at a negative
        // origin: it must clip at the edge, not index outside the grid
        let centered =
          Flow.overlay [
            Flow.docked {
              Anchor = Dock.CenterX ||| Dock.CenterY
              Inset = InsetSpec.Zero
              Stamp = Stamp.named "huge" (Stamp.box 14 14 [ Flow.fill 7 ])
            }
          ]

        let g, placed = runInto 10 10 centered

        Expect.equal
          (Flow.tryPosition "huge" placed)
          (ValueSome { X = 0; Y = 0; W = 10; H = 10 })
          "the overflowing element clips at the container"

        expectCell g 0 0 (ValueSome 7) "paints from the corner"
        expectCell g 9 9 (ValueSome 7) "paints to the far corner"

        // a right-docked element whose inset pushes it past the left edge
        let pushed =
          Flow.overlay [
            Flow.docked {
              Anchor = Dock.Right ||| Dock.Top
              Inset = { InsetSpec.Zero with Right = 8 }
              Stamp = Stamp.named "bar" (Stamp.box 4 4 [ Flow.fill 3 ])
            }
          ]

        let g2, placed2 = runInto 10 10 pushed

        Expect.equal
          (Flow.tryPosition "bar" placed2)
          (ValueSome { X = 0; Y = 0; W = 2; H = 4 })
          "only the part inside the container is reported"

        expectCell g2 0 0 (ValueSome 3) "the visible part paints"
        expectCell g2 2 0 ValueNone "nothing paints past the visible part"
        expectCell g2 0 4 ValueNone "nothing paints past the bottom edge"

      testCase "at places inside a grid area like docked"
      <| fun _ ->
        let stamp =
          Flow.grid {
            Cols = [| Fixed 4; Fixed 4 |]
            Rows = [| Fixed 2 |]
            Gap = 0
            Areas = [| "a b" |]
            Places = [|
              struct (Area "a", fillTile 1)
              struct (Area "b", Flow.at 1 0 (Stamp.named "prop" (tile 2 1 7)))
            |]
          }

        let g, placed = runInto 8 2 stamp

        Expect.equal
          (Flow.tryPosition "prop" placed)
          (ValueSome { X = 5; Y = 0; W = 2; H = 1 })
          "at offsets from the area origin"

        expectCell g 3 0 (ValueSome 1) "area a fills its own tracks"
        expectCell g 4 0 ValueNone "area b before the prop stays empty"
        expectCell g 5 0 (ValueSome 7) "the prop paints at the offset"

      testCase "at names itself in the expand error"
      <| fun _ ->
        let thrown =
          try
            Flow.at 1 1 (Stamp.expand(fillTile 1)) |> ignore
            None
          with :? System.ArgumentException as e ->
            Some e.Message

        match thrown with
        | Some msg -> Expect.stringContains msg "Flow.at" "the error names at"
        | None -> failtest "at must reject an expand stamp"
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
            Cols = [| Fixed 4 |]
            Rows = [| Fixed 4 |]
            Gap = 0
            Areas = [| "a" |]
            Places = [|
              struct (Area "a", inner)
              struct (Area "a", Stamp.named "area" (Stamp.empty()))
            |]
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
            Flow.noise { Count = 24; Seed = 7 } Rock
          ]
          Flow.docked {
            Anchor = Dock.CenterX ||| Dock.CenterY
            Inset = InsetSpec.Zero
            Stamp = Stamp.named "fountain" fountain
          }
        ]

      // A market stall: awning over crates of goods, signed center-front.
      let stall goods =
        Stamp.above
          (Stamp.box 4 1 [ Flow.fill Stall ])
          (Flow.group 4 2 [
            Flow.canvas [ Flow.fill Crate ]
            Flow.docked {
              Anchor = Dock.CenterX ||| Dock.Top
              Inset = InsetSpec.Zero
              Stamp = Stamp.box 1 1 [ Flow.fill goods ]
            }
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
          Flow.weather { Probability = 0.3f; Seed = 11 } Tree Grass
          Flow.clumps { Count = 4; Seed = 5 } (fun c ->
            c |> Layout.circle 1 1 1 true Rock)
          Flow.noise { Count = 6; Seed = 9 } Stump
          Flow.noise { Count = 1; Seed = 15 } Chest
        ]

      // Docks: water with piers reaching in, lanterns at the pier heads.
      let pier planks =
        Stamp.above
          (Stamp.box 1 1 [ Flow.fill Lamp ])
          (Stamp.box 1 planks [ Flow.fill Path ])

      let docks =
        Flow.group 34 10 [
          Flow.canvas [ Flow.fill Water ]
          Flow.stretch(
            Flow.row { FlowOpts.Default with Gap = 9 } [
              pier 5
              pier 7
              pier 5
            ]
          )
        ]

      // The map: structure by layout, noise by stamps.
      let harbour =
        Flow.grid {
          Cols = [| Weight 2f; Weight 1f |]
          Rows = [| Fixed 10; Weight 1f; Weight 1f |]
          Gap = 1
          Areas = [| "plaza market"; "plaza woods"; "docks woods" |]
          Places = [|
            struct (Area "plaza", Stamp.tagged [ "safe-zone" ] plaza)
            struct (Area "market", market)
            struct (Area "woods", woods)
            struct (Area "docks", docks)
          |]
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
            Flow.stretch harbour

            Flow.docked {
              Anchor = Dock.Top ||| Dock.CenterX
              Inset = InsetSpec.Zero
              Stamp = Stamp.box 12 3 [ Flow.fill Wall ]
            }

            // zero footprint width = stretch over the container
            Flow.docked {
              Anchor = Dock.Bottom
              Inset = InsetSpec.Zero
              Stamp = Stamp.named "gate" (Stamp.box 0 1 [ Flow.fill Sand ])
            }
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
          Cols = [| Fixed 4 |]
          Rows = [| Fixed 2 |]
          Gap = 0
          Areas = [| "a" |]
          Places = [| struct (Area "a", Flow.canvas [ Flow.fill 7 ]) |]
        }

      let g, _ = runInto 4 2 stamp

      expectCell g 0 0 (ValueSome 7) "canvas fills the area"
      expectCell g 3 1 (ValueSome 7) "canvas fills the area"

    testCase "fixed boxes keep their size in a grid area"
    <| fun _ ->
      let stamp =
        Flow.grid {
          Cols = [| Fixed 4 |]
          Rows = [| Fixed 4 |]
          Gap = 0
          Areas = [| "a" |]
          Places = [| struct (Area "a", Stamp.box 2 1 [ Flow.fill 5 ]) |]
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
          Flow.docked {
            Anchor = Dock.CenterX ||| Dock.CenterY
            Inset = InsetSpec.Zero
            Stamp = Stamp.box 2 2 [ Flow.fill 2 ]
          }
        ]

      let g, _ = runInto 10 10 stamp

      expectCell g 0 0 (ValueSome 1) "canvas fills the group"
      expectCell g 3 1 (ValueSome 1) "canvas fills the group"
      expectCell g 1 0 (ValueSome 2) "centered child start"
      expectCell g 2 1 (ValueSome 2) "centered child end"
      expectCell g 4 0 ValueNone "group does not stretch"

    testCase "weather replaces box-wide with a probability"
    <| fun _ ->
      let stamp =
        Stamp.box 2 1 [
          Flow.fill 1
          Flow.weather { Probability = 1.0f; Seed = 3 } 1 2
        ]

      let g, _ = runInto 4 1 stamp

      expectCell g 0 0 (ValueSome 2) "weathered"
      expectCell g 1 0 (ValueSome 2) "weathered"

    testCase "noiseBy generates the scattered cells"
    <| fun _ ->
      let stamp =
        Stamp.box 4 1 [
          Flow.noiseBy { Count = 2; Seed = 1 } (fun x _ -> x * 10 + 1)
        ]

      let g, _ = runInto 4 1 stamp

      expectCell g 0 0 (ValueSome 1) "generated at x 0"
      expectCell g 1 0 (ValueSome 11) "generated at x 1"
      expectCell g 2 0 ValueNone "not scattered"
      expectCell g 3 0 ValueNone "not scattered"
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
          Cols = [| Fixed 3; Fixed 3 |]
          Rows = [| Fixed 4 |]
          Gap = 0
          Areas = [| "zone rest" |]
          Places = [|
            struct (Area "zone", Flow.region [ "no-build" ] 0 0)
            struct (Area "rest", Flow.canvas [ Flow.fill 9 ])
          |]
        }

      let g, placed = runInto 6 4 stamp

      Expect.isTrue
        (Flow.isTag "no-build" { X = 0; Y = 0 } placed)
        "inside the region"

      Expect.isTrue
        (Flow.isTag "no-build" { X = 2; Y = 1 } placed)
        "region corner"

      Expect.isFalse
        (Flow.isTag "no-build" { X = 3; Y = 1 } placed)
        "outside the region"

      Expect.isFalse
        (Flow.isTag "no-build" { X = 5; Y = 3 } placed)
        "far outside"

      Expect.isFalse
        (Flow.isTag "no-build" { X = -1; Y = 0 } placed)
        "out of range is never tagged"

      Expect.isFalse
        (Flow.isTag "unknown" { X = 0; Y = 0 } placed)
        "unknown tag"

      expectCell g 3 1 (ValueSome 9) "the neighbor area paints normally"

    testCase "region stretches like any zero-footprint child"
    <| fun _ ->
      let stamp =
        Flow.grid {
          Cols = [| Fixed 4 |]
          Rows = [| Fixed 4 |]
          Gap = 0
          Areas = [| "a" |]
          Places = [| struct (Area "a", Flow.region [ "arena" ] 0 0) |]
        }

      let _, placed = runInto 4 4 stamp

      Expect.isTrue
        (Flow.isTag "arena" { X = 3; Y = 3 } placed)
        "stretched over the area"

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

      Expect.isTrue
        (Flow.isTag "safe" { X = 0; Y = 0 } scanned)
        "tile 1 is safe"

      Expect.isTrue
        (Flow.isTag "safe" { X = 1; Y = 0 } scanned)
        "tile 1 is safe"

      Expect.isTrue
        (Flow.isTag "dangerous" { X = 2; Y = 0 } scanned)
        "tile 2 is dangerous"

      Expect.isTrue
        (Flow.isTag "dangerous" { X = 3; Y = 0 } scanned)
        "tile 2 is dangerous"

      Expect.isFalse
        (Flow.isTag "dangerous" { X = 0; Y = 0 } scanned)
        "tile 1 is not dangerous"

      Expect.isFalse
        (Flow.isTag "safe" { X = 4; Y = 0 } scanned)
        "empty cell has no tags"

    testCase "duplicate names throw"
    <| fun _ ->
      Expect.throwsT<System.ArgumentException>
        (fun () ->
          runInto
            10
            1
            (Flow.row FlowOpts.Default [
              Stamp.named "dup" (tile 1 1 1)
              Stamp.named "dup" (tile 1 1 2)
            ])
          |> ignore)
        "duplicate element name"

    testCase "landmark rects clip to the container"
    <| fun _ ->
      let stamp =
        Flow.overlay [
          Flow.docked {
            Anchor = Dock.Bottom ||| Dock.CenterX
            Inset = InsetSpec.Zero
            Stamp =
              Stamp.tagged [ "exit" ] (Stamp.box 20 2 [ Flow.fill 7 ])
              |> Stamp.named "gate"
          }
        ]

      let g, placed = runInto 10 4 stamp

      expectCell g 0 2 (ValueSome 7) "painted from the left edge"
      expectCell g 9 3 (ValueSome 7) "painted to the right edge"

      Expect.equal
        (Flow.tryPosition "gate" placed)
        (ValueSome { X = 0; Y = 2; W = 10; H = 2 })
        "clipped to the container"

      Expect.equal
        (Flow.taggedRects "exit" placed)
        [ { X = 0; Y = 2; W = 10; H = 2 } ]
        "tagged rect clips too"

    testCase "tryTagGrid hands out the raw bit grid"
    <| fun _ ->
      let stamp = Flow.overlay [ Flow.region [ "zone" ] 0 0 ]
      let _, placed = runInto 6 4 stamp

      match Flow.tryTagGrid "zone" placed with
      | ValueSome cells ->
        Expect.isTrue cells.[0 + 0 * 6] "inside the region"
        Expect.isTrue cells.[5 + 3 * 6] "inside the region"
      | ValueNone -> failtest "the region has a grid"

      Expect.equal (Flow.tryTagGrid "unknown" placed) ValueNone "unknown tag"

    testCase "tagged and named compose with docking"
    <| fun _ ->
      let gate =
        Flow.docked {
          Anchor = Dock.StretchX ||| Dock.Bottom
          Inset = InsetSpec.Zero
          Stamp =
            Stamp.tagged [ "exit"; "no-build" ] (Stamp.box 0 1 [ Flow.fill 7 ])
        }

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
        (Flow.isTag "exit" { X = 3; Y = 3 } placed)
        "walkable query on the docked rect"

      Expect.isFalse
        (Flow.isTag "exit" { X = 3; Y = 2 } placed)
        "one above is not the gate"
  ]

[<Tests>]
let hexTests =
  testList "Flow hex" [
    testCase "a grid-template document runs over hex storage"
    <| fun _ ->
      let doc =
        Flow.grid {
          Cols = [| Fixed 4; Fixed 6 |]
          Rows = [| Fixed 2; Fixed 2 |]
          Gap = 0
          Areas = [| "shore woods"; "shore woods" |]
          Places = [|
            struct (Area "shore", Stamp.named "shore" (fillTile 1))
            struct (Area "woods", fillTile 2)
          |]
        }

      let g =
        CellGrid2D.createHex {
          Orientation = HexOrientation.PointyTop
          Width = 10
          Height = 4
          Radius = 32f
          Origin = Vector2.Zero
        }

      let struct (g, placed) = g |> Flow.run doc

      expectCell g 0 0 (ValueSome 1) "shore area start"
      expectCell g 3 1 (ValueSome 1) "shore area end"
      expectCell g 4 0 (ValueSome 2) "woods area start"
      expectCell g 9 3 (ValueSome 2) "woods area end"

      Expect.equal
        (Flow.tryPosition "shore" placed)
        (ValueSome { X = 0; Y = 0; W = 4; H = 4 })
        "offset-space rect over hex storage"

    testCase "named and tagged landmarks work on hex storage"
    <| fun _ ->
      let depot =
        Flow.docked {
          Anchor = Dock.Bottom ||| Dock.Right
          Inset = InsetSpec.Zero
          Stamp =
            Stamp.tagged [ "depot" ] (Stamp.box 2 2 [ Flow.fill 3 ])
            |> Stamp.named "depot"
        }

      let g =
        CellGrid2D.createHex {
          Orientation = HexOrientation.FlatTop
          Width = 6
          Height = 4
          Radius = 32f
          Origin = Vector2.Zero
        }

      let struct (g, placed) = g |> Flow.run(Flow.overlay [ depot ])

      expectCell g 4 2 (ValueSome 3) "depot paints at the bottom right"

      Expect.equal
        (Flow.tryPosition "depot" placed)
        (ValueSome { X = 4; Y = 2; W = 2; H = 2 })
        "docked rect on hex storage"

      Expect.isTrue
        (Flow.isTag "depot" { X = 5; Y = 3 } placed)
        "tag bit grid indexes hex storage"

      Expect.isFalse
        (Flow.isTag "depot" { X = 3; Y = 3 } placed)
        "outside the depot"

    testCase "scanTiles derives tags from hex tiles"
    <| fun _ ->
      let doc = Flow.overlay [ Flow.canvas [ Flow.fill 9 ] ]

      let g =
        CellGrid2D.createHex {
          Orientation = HexOrientation.PointyTop
          Width = 3
          Height = 2
          Radius = 32f
          Origin = Vector2.Zero
        }

      let struct (g, placed) = g |> Flow.run doc

      let marks =
        Landmarks.scanTiles
          (fun _ _ t -> if t = 9 then [ "all" ] else [])
          g
          placed

      Expect.isTrue
        (Flow.isTag "all" { X = 0; Y = 0 } marks)
        "first cell tagged"

      Expect.isTrue (Flow.isTag "all" { X = 2; Y = 1 } marks) "last cell tagged"
  ]

[<Tests>]
let cellOpTests =
  testList "Flow cell ops" [
    testCase "cell paints one cell"
    <| fun _ ->
      let g, _ = runInto 3 1 (Stamp.box 3 1 [ Flow.cell { X = 1; Y = 0 } 5 ])

      expectCell g 0 0 ValueNone "left of the cell"
      expectCell g 1 0 (ValueSome 5) "the cell"
      expectCell g 2 0 ValueNone "right of the cell"

    testCase "repeatX paints a horizontal run"
    <| fun _ ->
      let g, _ = runInto 4 1 (Stamp.box 4 1 [ Flow.repeatX 3 7 ])

      expectCell g 0 0 (ValueSome 7) "run start"
      expectCell g 2 0 (ValueSome 7) "run end"
      expectCell g 3 0 ValueNone "past the run"

    testCase "repeatY paints a vertical run"
    <| fun _ ->
      let g, _ = runInto 1 4 (Stamp.box 1 4 [ Flow.repeatY 3 7 ])

      expectCell g 0 0 (ValueSome 7) "run start"
      expectCell g 0 2 (ValueSome 7) "run end"
      expectCell g 0 3 ValueNone "past the run"

    testCase "line paints a Bresenham diagonal"
    <| fun _ ->
      let g, _ =
        runInto
          5
          5
          (Stamp.box 5 5 [ Flow.line { X = 0; Y = 0 } { X = 4; Y = 4 } 9 ])

      expectCell g 0 0 (ValueSome 9) "start"
      expectCell g 2 2 (ValueSome 9) "middle"
      expectCell g 4 4 (ValueSome 9) "end"
      expectCell g 0 4 ValueNone "off the line"

    testCase "circle paints an outline or a disc"
    <| fun _ ->
      let outline, _ =
        runInto
          7
          7
          (Stamp.box 7 7 [
            Flow.circle
              {
                Center = { X = 3; Y = 3 }
                Radius = 3
                Filled = false
              }
              4
          ])

      let disc, _ =
        runInto
          7
          7
          (Stamp.box 7 7 [
            Flow.circle
              {
                Center = { X = 3; Y = 3 }
                Radius = 3
                Filled = true
              }
              4
          ])

      expectCell outline 3 0 (ValueSome 4) "top of the ring"
      expectCell outline 3 3 ValueNone "hollow center"
      expectCell disc 3 3 (ValueSome 4) "filled center"

    testCase "polygon paints a filled rectangle"
    <| fun _ ->
      let g, _ =
        runInto
          4
          3
          (Stamp.box 4 3 [
            Flow.polygon
              [| struct (0, 0); struct (3, 0); struct (3, 2); struct (0, 2) |]
              true
              6
          ])

      expectCell g 0 0 (ValueSome 6) "corner"
      expectCell g 3 1 (ValueSome 6) "right edge"
      expectCell g 1 1 (ValueSome 6) "interior"

    testCase "scatterBorder scatters up to count border cells"
    <| fun _ ->
      let g, _ =
        runInto
          4
          4
          (Stamp.box 4 4 [ Flow.scatterBorder { Count = 3; Seed = 11 } 2 ])

      let mutable filled = 0
      CellGrid2D.iter (fun _ _ _ -> filled <- filled + 1) g
      Expect.isGreaterThan filled 0 "at least one scattered cell"
      Expect.isLessThanOrEqual filled 3 "at most count scattered cells"

    testCase "scatterLine scatters up to count line cells"
    <| fun _ ->
      let g, _ =
        runInto
          5
          1
          (Stamp.box 5 1 [
            Flow.scatterLine
              {
                From = { X = 0; Y = 0 }
                To = { X = 4; Y = 0 }
                Count = 2
                Seed = 7
              }
              3
          ])

      let mutable filled = 0
      CellGrid2D.iter (fun _ _ _ -> filled <- filled + 1) g
      Expect.isGreaterThan filled 0 "at least one scattered cell"
      Expect.isLessThanOrEqual filled 2 "at most count scattered cells"

    testCase "checkerBorder alternates the border only"
    <| fun _ ->
      let g, _ = runInto 4 4 (Stamp.box 4 4 [ Flow.checkerBorder 1 2 ])

      expectCell g 0 0 (ValueSome 1) "odd border cell"
      expectCell g 1 0 (ValueSome 2) "even border cell"
      expectCell g 1 1 ValueNone "interior stays empty"

    testCase "clear erases the box"
    <| fun _ ->
      let g, _ = runInto 3 2 (Stamp.box 3 2 [ Flow.fill 5; Flow.clear() ])

      expectCell g 0 0 ValueNone "erased"
      expectCell g 2 1 ValueNone "erased"

    testCase "setIfEmpty respects occupied cells"
    <| fun _ ->
      let g, _ =
        runInto
          2
          1
          (Stamp.box 2 1 [
            Flow.cell { X = 0; Y = 0 } 1
            Flow.setIfEmpty { X = 0; Y = 0 } 8
            Flow.setIfEmpty { X = 1; Y = 0 } 8
          ])

      expectCell g 0 0 (ValueSome 1) "occupied cell keeps its content"
      expectCell g 1 0 (ValueSome 8) "empty cell takes the content"

    testCase "map rewrites existing cells"
    <| fun _ ->
      let g, _ =
        runInto 2 2 (Stamp.box 2 2 [ Flow.fill 3; Flow.map(fun v -> v * 10) ])

      expectCell g 0 0 (ValueSome 30) "mapped"
      expectCell g 1 1 (ValueSome 30) "mapped"
  ]
