namespace SpaceBattle

open Mibo.Elmish
open Mibo.Animation
open Raylib_cs
open SpaceBattle.Units

type Decoration =
  | Asteroid
  | Crate

type FactionAssets = {
  Fighter: SpriteSheet
  Cruiser: SpriteSheet
  BattleShip: SpriteSheet
}

type GameAssets = {
  Station: SpriteSheet
  FactionAssets: Map<Faction, FactionAssets>
  Decorations: Map<Decoration, SpriteSheet[]>
  Laser1: SpriteSheet
  Laser2: SpriteSheet
  MonoFont: Font
}

module SBAssets =

  let defaultPoses() = [|
    {
      Name = "N"
      Row = 0
      StartCol = 0
      FrameCount = 1
      Fps = 1f
      Loop = false
    }
    {
      Name = "NE"
      Row = 0
      StartCol = 1
      FrameCount = 1
      Fps = 1f
      Loop = false
    }
    {
      Name = "SE"
      Row = 0
      StartCol = 2
      FrameCount = 1
      Fps = 1f
      Loop = false
    }
    {
      Name = "S"
      Row = 1
      StartCol = 0
      FrameCount = 1
      Fps = 1f
      Loop = false
    }
    {
      Name = "SW"
      Row = 1
      StartCol = 1
      FrameCount = 1
      Fps = 1f
      Loop = false
    }
    {
      Name = "NW"
      Row = 1
      StartCol = 2
      FrameCount = 1
      Fps = 1f
      Loop = false
    }
  |]

  let spin fps = {
    Name = "spin"
    Row = 0
    StartCol = 0
    FrameCount = 6
    Fps = fps
    Loop = true
  }



  let loadFactionAssets faction (assets: IAssets) =
    match faction with
    | Federation ->
      let fighter = assets.Texture "assets/prerendered-spaceships/colony3.png"

      let cruiser = assets.Texture "assets/prerendered-spaceships/colony2.png"

      let battleship =
        assets.Texture "assets/prerendered-spaceships/colony4.png"

      {
        Fighter =
          SpriteSheet.fromGrid fighter 180 180 3 [|
            yield! defaultPoses()
            spin 6f
          |]
        Cruiser =
          SpriteSheet.fromGrid cruiser 180 180 3 [|
            yield! defaultPoses()
            spin 6f
          |]
        BattleShip =
          SpriteSheet.fromGrid battleship 180 180 3 [|
            yield! defaultPoses()
            spin 6f
          |]
      }
    | Empire ->
      let fighter = assets.Texture "assets/prerendered-spaceships/terrok1.png"

      let cruiser = assets.Texture "assets/prerendered-spaceships/terrok2.png"

      let battleship =
        assets.Texture "assets/prerendered-spaceships/terrok3.png"

      {
        Fighter =
          SpriteSheet.fromGrid fighter 180 180 3 [|
            yield! defaultPoses()
            spin 6f
          |]
        Cruiser =
          SpriteSheet.fromGrid cruiser 180 180 3 [|
            yield! defaultPoses()
            spin 6f
          |]
        BattleShip =
          SpriteSheet.fromGrid battleship 180 180 3 [|
            yield! defaultPoses()
            spin 6f
          |]
      }
    | Pirates ->
      let fighter = assets.Texture "assets/prerendered-spaceships/kelvor1.png"

      let cruiser = assets.Texture "assets/prerendered-spaceships/kelvor3.png"

      let battleship =
        assets.Texture "assets/prerendered-spaceships/kelvor2.png"

      {
        Fighter =
          SpriteSheet.fromGrid fighter 180 180 3 [|
            yield! defaultPoses()
            spin 6f
          |]
        Cruiser =
          SpriteSheet.fromGrid cruiser 180 180 3 [|
            yield! defaultPoses()
            spin 6f
          |]
        BattleShip =
          SpriteSheet.fromGrid battleship 180 180 3 [|
            yield! defaultPoses()
            spin 6f
          |]
      }

  let loadDecorationAssets decoration (assets: IAssets) =
    match decoration with
    | Asteroid ->
      let asteroid1 =
        assets.Texture "assets/prerendered-spaceships/asteroid1.png"

      let asteroid2 =
        assets.Texture "assets/prerendered-spaceships/asteroid2.png"

      [|
        SpriteSheet.fromGrid asteroid1 180 180 3 [| spin 6f |]
        SpriteSheet.fromGrid asteroid2 220 220 3 [| spin 6f |]
      |]
    | Crate ->
      let crate1 = assets.Texture "assets/prerendered-spaceships/crate1.png"
      let crate2 = assets.Texture "assets/prerendered-spaceships/crate2.png"

      [|
        SpriteSheet.fromGrid crate1 100 100 3 [| spin 2f |]
        SpriteSheet.fromGrid crate2 100 100 3 [| spin 2f |]
      |]

  let loadStation(assets: IAssets) =
    let station = assets.Texture "assets/prerendered-spaceships/station.png"
    SpriteSheet.fromGrid station 220 220 3 [| spin 6f |]


  let loadSpriteSheets ctx =
    let assets = GameContext.getService<IAssets> ctx

    {
      Station = loadStation assets
      FactionAssets =
        [| Federation; Empire; Pirates |]
        |> Array.map(fun f -> f, loadFactionAssets f assets)
        |> Map.ofArray
      Decorations =
        Map.ofList [
          Asteroid, loadDecorationAssets Asteroid assets
          Crate, loadDecorationAssets Crate assets
        ]
      Laser1 =
        let tex = assets.Texture "assets/prerendered-spaceships/laser1.png"
        SpriteSheet.fromGrid tex 60 60 3 [| yield! defaultPoses() |]
      Laser2 =
        let tex = assets.Texture "assets/prerendered-spaceships/laser2.png"
        SpriteSheet.fromGrid tex 60 60 3 [| yield! defaultPoses() |]
      MonoFont = assets.Font "assets/Fonts/monogram.ttf"
    }

  let initUnitSprites
    (assets: GameAssets)
    : Map<struct (Faction * UnitClass), SpriteSheet> =
    let mutable sprites = Map.empty

    for faction in [| Federation; Empire; Pirates |] do
      for unitClass in [| Fighter; Cruiser; Battleship |] do
        let sprite =
          match unitClass with
          | Fighter -> assets.FactionAssets[faction].Fighter
          | Cruiser -> assets.FactionAssets[faction].Cruiser
          | Battleship -> assets.FactionAssets[faction].BattleShip

        sprites <- Map.add struct (faction, unitClass) sprite sprites

    sprites
