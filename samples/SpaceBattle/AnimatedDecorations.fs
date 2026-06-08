namespace SpaceBattle

open Mibo.Animation
open Mibo.Layout
open SpaceBattle.Types

module AnimatedDecorations =

  let init
    (map: HexGrid<Tile>)
    (assets: GameAssets)
    : Map<struct (int * int), AnimatedSprite> =
    let mutable sprites = Map.empty

    map
    |> HexGrid.iter(fun col row tile ->
      match tile with
      | Asteroid1 ->
        let sheet =
          assets.Decorations
          |> Map.tryFind Asteroid
          |> Option.bind Array.tryHead
          |> Option.defaultWith(fun () -> failwith "no asteroid")

        let animated = AnimatedSprite.create sheet "spin"
        sprites <- Map.add struct (col, row) animated sprites
      | Asteroid2 ->
        let sheet =
          assets.Decorations
          |> Map.tryFind Asteroid
          |> Option.bind Array.tryLast
          |> Option.defaultWith(fun () -> failwith "no asteroid")

        let animated = AnimatedSprite.create sheet "spin"
        sprites <- Map.add struct (col, row) animated sprites
      | Crate1 ->
        let sheet =
          assets.Decorations
          |> Map.tryFind Crate
          |> Option.bind Array.tryHead
          |> Option.defaultWith(fun () -> failwith "no crate")

        let animated = AnimatedSprite.create sheet "spin"
        sprites <- Map.add struct (col, row) animated sprites
      | Crate2 ->
        let sheet =
          assets.Decorations
          |> Map.tryFind Crate
          |> Option.bind Array.tryLast
          |> Option.defaultWith(fun () -> failwith "no crate")

        let animated = AnimatedSprite.create sheet "spin"
        sprites <- Map.add struct (col, row) animated sprites
      | Station ->
        let animated = AnimatedSprite.create assets.Station "spin"
        sprites <- Map.add struct (col, row) animated sprites
      | DeepSpace -> ())

    sprites

  let update
    (dt: float32)
    (map: HexGrid<Tile>)
    (camera: Raylib_cs.Camera2D)
    (vpWidth: float32)
    (vpHeight: float32)
    (sprites: Map<struct (int * int), AnimatedSprite>)
    : Map<struct (int * int), AnimatedSprite> =
    let topLeft =
      Raylib_cs.Raylib.GetScreenToWorld2D(System.Numerics.Vector2.Zero, camera)

    let bottomRight =
      Raylib_cs.Raylib.GetScreenToWorld2D(
        System.Numerics.Vector2(vpWidth, vpHeight),
        camera
      )

    let mutable sprites = sprites

    map
    |> HexGrid.iterVisible
      topLeft.X
      topLeft.Y
      bottomRight.X
      bottomRight.Y
      (fun col row tile ->
        match sprites |> Map.tryFind struct (col, row) with
        | Some animated ->
          sprites <-
            Map.add
              struct (col, row)
              (AnimatedSprite.update dt animated)
              sprites
        | None -> ())

    sprites
