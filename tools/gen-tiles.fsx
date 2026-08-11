// ─────────────────────────────────────────────────────────────
// Bakes the Kenney atlas XML files into World/Tiles.fs
// (compile-time datasets — no runtime XML parsing).
//
// Usage:  dotnet fsi tools/gen-tiles.fsx
// The generated file is committed; regenerate when a sheet
// changes. The curated mapping tables below are the single place
// that assigns semantic names to atlas tiles.
// ─────────────────────────────────────────────────────────────
open System
open System.IO
open System.Xml.Linq

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let outPath = Path.Combine(root, "src", "Defli", "World", "Tiles.fs")

type Sheet = {
  Module: string
  Xml: string
  SheetPath: string
  Named: (string * string)[]
  Groups: (string * string[])[]
}

let sheets: Sheet[] = [|
  {
    Module = "Tiles"
    Xml =
      Path.Combine(
        root,
        "assets",
        "kenney_tower-defense-top-down",
        "towerDefense_tilesheet.xml"
      )
    SheetPath = "kenney_tower-defense-top-down/towerDefense_tilesheet.png"
    Named = [|
      "grassFullA", "grass_full_a"
      "grassFullB", "grass_full_b"
      "grassFullC", "grass_full_c"
      "dirtFullA", "dirt_full_a"
      "dirtFullB", "dirt_full_b"
      "dirtFullC", "dirt_full_c"
      "pathVerticalDirt", "path_vertical_dirt"
      "pathHorizontalDirt", "path_horizontal_dirt"
      "pathEndUpDirt", "path_end_up_dirt"
      "pathEndLeftDirt", "path_end_left_dirt"
      "turretMountEmpty", "turret_mount_empty"
      "turretBaseA", "turret_base_a"
      "turretGreen", "turret_green"
      "turretRedDual", "turret_red_dual"
      "turretMissilesDual", "turret_missiles_dual"
      "tankTurretGreen", "tank_turret_green"
      "tankTurretBeige", "tank_turret_beige"
      "tankHullGreen", "tank_hull_green"
      "tankHullBeige", "tank_hull_beige"
      "planeGreen", "plane_green"
      "planeGray", "plane_gray"
      "planeGhostA", "plane_ghost_a"
      "planeGhostB", "plane_ghost_b"
      "rocketPodSingle", "rocket_pod_single"
      "rocketPodDual", "rocket_pod_dual"
      "rocketSmall", "rocket_small"
      "rocketLarge", "rocket_large"
      "coinGold", "coin_gold"
      "effectImpactBurst", "effect_impact_burst"
      "effectImpactRing", "effect_impact_ring"
      "effectImpactDebris", "effect_impact_debris"
      "crosshair", "crosshair"
      "bushSmall", "bush_small"
      "rockSmall", "rock_small"
      "rockMedium", "rock_medium"
      "rockLarge", "rock_large"
      "crateMetalSquare", "crate_metal_square"
      "crateMetalBeveled", "crate_metal_beveled"
      "crateMetalDiamond", "crate_metal_diamond"
      "crateMetalOctagon", "crate_metal_octagon"
      "containerLarge", "container_large"
      "containerSmall", "container_small"
      "treeRound", "tree_round"
      "treePine", "tree_pine"
      "dirtDotOnGrass", "dirt_dot_on_grass"
      "dirtCircleOnGrassTL", "dirt_circle_on_grass_top_left"
      "dirtCircleOnGrassTR", "dirt_circle_on_grass_top_right"
      "dirtCircleOnGrassBL", "dirt_circle_on_grass_bottom_left"
      "dirtCircleOnGrassBR", "dirt_circle_on_grass_bottom_right"
      "dirtPatchOnGrassTop", "dirt_patch_on_grass_top"
      "dirtPatchOnGrassBottom", "dirt_patch_on_grass_bottom"
      "dirtPatchOnGrassLeft", "dirt_patch_on_grass_left"
      "dirtPatchOnGrassRight", "dirt_patch_on_grass_right"
      "dirtPatchOnGrassTopLeft", "dirt_patch_on_grass_top_left"
      "dirtPatchOnGrassTopRight", "dirt_patch_on_grass_top_right"
      "dirtPatchOnGrassBottomLeft", "dirt_patch_on_grass_bottom_left"
      "dirtPatchOnGrassBottomRight", "dirt_patch_on_grass_bottom_right"
      "dirtPatchOnGrassCenter", "dirt_patch_on_grass_center"
    |]
    Groups = [|
      "groundGrass", [| "grassFullA"; "grassFullB"; "grassFullC" |]
      "groundDirt", [| "dirtFullA"; "dirtFullB"; "dirtFullC" |]
      "pathDirt",
      [|
        "pathVerticalDirt"
        "pathHorizontalDirt"
        "pathEndUpDirt"
        "pathEndLeftDirt"
      |]
      "effects",
      [| "effectImpactBurst"; "effectImpactRing"; "effectImpactDebris" |]
      "decoProps",
      [|
        "bushSmall"
        "rockSmall"
        "rockMedium"
        "rockLarge"
        "crateMetalSquare"
        "crateMetalBeveled"
        "crateMetalDiamond"
        "crateMetalOctagon"
        "containerLarge"
        "containerSmall"
        "treeRound"
        "treePine"
      |]
      "dirtBlends",
      [|
        "dirtDotOnGrass"
        "dirtCircleOnGrassTL"
        "dirtCircleOnGrassTR"
        "dirtCircleOnGrassBL"
        "dirtCircleOnGrassBR"
        "dirtPatchOnGrassTop"
        "dirtPatchOnGrassBottom"
        "dirtPatchOnGrassLeft"
        "dirtPatchOnGrassRight"
        "dirtPatchOnGrassTopLeft"
        "dirtPatchOnGrassTopRight"
        "dirtPatchOnGrassBottomLeft"
        "dirtPatchOnGrassBottomRight"
        "dirtPatchOnGrassCenter"
      |]
      "enemyHulls", [| "tankHullGreen"; "tankHullBeige" |]
      "enemyPlanes",
      [| "planeGreen"; "planeGray"; "planeGhostA"; "planeGhostB" |]
    |]
  }
|]

let findTile
  (subs: struct (string * int * int * int * int)[])
  (atlasName: string)
  =
  subs
  |> Array.tryFind(fun struct (n, _, _, _, _) -> n = atlasName)
  |> Option.defaultWith(fun () ->
    failwithf "tile not found in atlas: %s" atlasName)

let sb = System.Text.StringBuilder()
let line(s: string) = sb.AppendLine s |> ignore
let linef fmt = Printf.ksprintf line fmt

line "// ─────────────────────────────────────────────────────────────"
line "// GENERATED by tools/gen-tiles.fsx — DO NOT EDIT BY HAND."
line "// Regenerate with:  dotnet fsi tools/gen-tiles.fsx"

line
  "// Sources: assets/kenney_tower-defense-top-down/towerDefense_tilesheet.xml,"

line
  "//          assets/kenney_top-down-tanks-remastered/allSprites_default.xml"

line "// ─────────────────────────────────────────────────────────────"
line "namespace Defli.World"
line ""
line "open System.Collections.Frozen"
line "open System.Collections.Generic"
line ""

for sheet in sheets do
  let doc = XDocument.Load sheet.Xml

  let subs =
    doc.Descendants(XName.Get "SubTexture")
    |> Seq.map(fun e ->
      let name = (e.Attribute(XName.Get "name").Value).Replace(".png", "")
      let x = int(e.Attribute(XName.Get "x").Value)
      let y = int(e.Attribute(XName.Get "y").Value)
      let w = int(e.Attribute(XName.Get "width").Value)
      let h = int(e.Attribute(XName.Get "height").Value)
      struct (name, x, y, w, h))
    |> Seq.toArray

  linef "module %s =" sheet.Module
  line ""
  linef "  [<Literal>]"
  linef "  let SheetPath = %A" sheet.SheetPath
  line ""

  if sheet.Module = "Tiles" then
    linef "  [<Literal>]"
    linef "  let TileSize = %d" (let struct (_, _, _, w, _) = subs[0] in w)
    line ""

  linef
    "  /// All %d atlas tiles (name, position, size) baked at compile time."
    subs.Length

  line
    "  /// Canonical store: ordered and iterable. The name index is built from it."

  line "  let all: TileInfo[] = [|"

  for struct (name, x, y, w, h) in subs do
    linef
      "    { Name = %A; X = %d; Y = %d; Width = %d; Height = %d }"
      name
      x
      y
      w
      h

  line "  |]"
  line ""

  line
    "  /// O(1) name index over the baked dataset (built once at module init)."

  line "  let byName: FrozenDictionary<string, TileInfo> ="
  line "    all"
  line "    |> Seq.map (fun t -> KeyValuePair(t.Name, t))"
  line "    |> FrozenDictionary.ToFrozenDictionary"
  line ""

  line
    "  /// Safe name lookup for data-driven code (tower/enemy defs, procedural gen)."

  line "  let tryByName (name: string) : TileInfo voption ="
  line "    Defli.FrozenDict.tryGetValue name byName"
  line ""
  line "  // ── Semantically named tiles (curated in the generator) ──"

  for semantic, atlasName in sheet.Named do
    let struct (_, x, y, w, h) = findTile subs atlasName
    linef "  /// %s — atlas position (%d, %d), %dx%d." atlasName x y w h
    linef "  let %s = byName[%A]" semantic atlasName

  line ""
  line "  // ── Groups (curated in the generator) ──"

  for group, members in sheet.Groups do
    linef "  let %s = [| %s |]" group (members |> String.concat "; ")

  line ""

Directory.CreateDirectory(Path.GetDirectoryName outPath) |> ignore
File.WriteAllText(outPath, sb.ToString())

for sheet in sheets do
  let doc = XDocument.Load sheet.Xml
  let count = doc.Descendants(XName.Get "SubTexture") |> Seq.length
  printfn "%s: %d tiles" sheet.Module count

printfn "Wrote %s" outPath
