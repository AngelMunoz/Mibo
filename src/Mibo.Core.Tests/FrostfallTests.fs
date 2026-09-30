module FrostfallTests

open Expecto
open System.Numerics
open Mibo.Layout

// Frostfall Keep — a full-surface example. It exercises the whole flow DSL:
// box styles, canvas, group, strip, overlay, docked, grid areas and tracks,
// props, regions, named elements, landmark tags, scanTiles, and the
// walking query. Read it top to bottom like a document: vocabulary first,
// then the level, then build and query.

type Tile =
  | Snow
  | Stone
  | Ice
  | Wood
  | Water
  | Wall
  | Door
  | Brazier
  | Barrel
  | Pine
  | Rock
  | Chest
  | Ember
  | Rubble

// ── 1. The game's semantic vocabulary — written once, grows with the game ──

module Vocabulary =
  /// A single-cell prop.
  let prop c = Flow.prop c

  /// A pine: canopy over a wooden trunk.
  let pine =
    Stamp.above
      (Stamp.box 1 1 [ Flow.fill Pine ])
      (Stamp.box 1 1 [ Flow.fill Wood ])

  /// The great hall: stone floor, wall ring, barrel clutter, braziers
  /// flanking the entrance, the king's chest dead center.
  let greatHall =
    Flow.group 40 24 [
      Flow.canvas [
        Flow.rect Wall Stone
        Flow.noise { Count = 30; Seed = 11 } Barrel
      ]

      Flow.docked {
        Anchor = Dock.Bottom ||| Dock.StretchX
        Inset = 1
        Stamp =
          Flow.row
            {
              FlowOpts.Default with
                  Gap = 4
                  Justify = Center
            }
            [ prop Brazier; prop Brazier ]
      }

      Flow.docked {
        Anchor = Dock.CenterX ||| Dock.CenterY
        Inset = 0
        Stamp =
          Stamp.named
            "king-chest"
            (Stamp.tagged [ "loot"; "quest" ] (prop Chest))
      }
    ]

  /// The frozen lair: ice floor, rubble drifts, a warded arena circle.
  let lair =
    Flow.group 26 18 [
      Flow.canvas [
        Flow.fill Ice
        Flow.clumps { Count = 6; Seed = 3 } (fun c ->
          c |> Layout.circle 2 2 3 true Rubble)
      ]
      Flow.docked {
        Anchor = Dock.CenterX ||| Dock.CenterY
        Inset = 0
        Stamp = Flow.region [ "boss-arena" ] 12 12
      }
      Flow.docked {
        Anchor = Dock.CenterX ||| Dock.CenterY
        Inset = 0
        Stamp = Stamp.named "boss-spawn" (prop Ember)
      }
    ]

  /// The frozen forest: snow underfoot, pine rows, icy patches, rocks.
  let forest =
    Flow.overlay [
      Flow.canvas [
        Flow.fill Snow
        Flow.texture(fun x y -> if (x * 5 + y * 3) % 7 = 0 then Pine else Snow)
        Flow.noise { Count = 40; Seed = 9 } Rock
        Flow.weather { Probability = 0.2f; Seed = 5 } Snow Ice
      ]
      Flow.row
        {
          FlowOpts.Default with
              Gap = 4
              Wrap = true
        }
        [ pine; pine; pine; pine; pine; pine ]
    ]

// ── 2. The level document ──────────────────────────────────────────────────

let keep =
  Flow.grid {
    Cols = [| Fixed 40; Weight 1f |]
    Rows = [| Weight 1f; Weight 1f |]
    Gap = 1
    Areas = [| "hall  forest"; "lair  forest" |]
    Places = [|
      struct ("hall", Stamp.tagged [ "safe-zone" ] Vocabulary.greatHall)
      struct ("lair", Stamp.tagged [ "danger-zone" ] Vocabulary.lair)
      struct ("forest", Vocabulary.forest)
    |]
  }

let frostfallKeep =
  Flow.overlay [
    keep
    Flow.strip Dock.Top 2 [ Flow.rect Wall Snow ] // the ramparts
    Flow.strip Dock.Bottom 1 [ Flow.fill Water ] // the moat

    Flow.docked {
      Anchor = Dock.Bottom ||| Dock.CenterX
      Inset = 1
      Stamp =
        Stamp.named
          "keep-entrance"
          (Stamp.tagged [ "entrance" ] (Stamp.box 6 1 [ Flow.fill Door ]))
    }
  ]

// ── 3. Build, derive, query ────────────────────────────────────────────────

let grid = CellGrid2D.create 80 50 (Vector2(16f, 16f)) Vector2.Zero

// cell truth: which tiles mean what to the simulation
let tileTags _ _ tile : string seq =
  match tile with
  | Wall -> [ "solid" ]
  | Water -> [ "solid"; "wet" ]
  | Ice -> [ "slippery" ]
  | _ -> []

let struct (level, landmarks) = grid |> Flow.build tileTags frostfallKeep

// the walking query, per move attempt
let canStand (x: int) (y: int) : bool =
  not(Flow.isTag "solid" { X = x; Y = y } landmarks)

// anchors and groups
let center(r: CellRect) = struct (r.X + r.W / 2, r.Y + r.H / 2)

let bossCell = Flow.tryPosition "boss-spawn" landmarks |> ValueOption.map center

let lootRects = Flow.taggedRects "loot" landmarks

[<Tests>]
let frostfall =
  testList "frostfall keep" [
    testCase "builds a full level and reads back sanely"
    <| fun _ ->
      Expect.isFalse (canStand 0 0) "rampart wall"
      Expect.isTrue (canStand 5 5) "hall floor"
      Expect.isFalse (canStand 10 49) "moat"

      Expect.equal
        bossCell
        (ValueSome struct (12, 33))
        "boss spawn centered in the lair"

      Expect.equal
        lootRects
        [| { X = 19; Y = 11; W = 1; H = 1 } |]
        "king chest centered in the hall"

      Expect.isTrue
        (Flow.isTag "boss-arena" { X = 13; Y = 31 } landmarks)
        "inside the warded circle"

      Expect.isFalse
        (Flow.isTag "boss-arena" { X = 1; Y = 26 } landmarks)
        "outside the circle"

      Expect.isTrue
        (Flow.isTag "safe-zone" { X = 20; Y = 12 } landmarks)
        "hall is safe"

      Expect.isTrue
        (Flow.isTag "danger-zone" { X = 5; Y = 30 } landmarks)
        "lair is dangerous"

      Expect.isTrue
        (Flow.isTag "slippery" { X = 5; Y = 30 } landmarks)
        "lair ice floor"

      Expect.isTrue
        (Flow.isTag "entrance" { X = 40; Y = 48 } landmarks)
        "the keep door"
  ]
