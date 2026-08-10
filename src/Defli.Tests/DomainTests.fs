module Defli.Tests.DomainTests

open Expecto
open Defli.World

let tests =
  testList "Domain" [
    testCase "baked tower-defense dataset has 299 tiles" (fun () ->
      Expect.equal Tiles.all.Length 299 "all tile count"
      Expect.equal Tiles.byName.Count 299 "byName index count")

    testCase "named accessors resolve to the atlas positions" (fun () ->
      Expect.equal Tiles.grassFullA.Name "grass_full_a" "grassFullA name"
      Expect.equal Tiles.pathVerticalDirt.Width 64 "path tile size"

      Expect.equal Tiles.tankHullGreen.Name "tank_hull_green" "hull green"
      Expect.equal Tiles.tankHullBeige.Name "tank_hull_beige" "hull beige"

      Expect.equal
        Tiles.tankTurretGreen.Name
        "tank_turret_green"
        "turret green"

      Expect.equal
        Tiles.tankTurretBeige.Name
        "tank_turret_beige"
        "turret beige"

      Expect.equal Tiles.planeGray.Name "plane_gray" "planeGray name"
      Expect.equal Tiles.planeGreen.Name "plane_green" "planeGreen name")

    testCase "tryByName misses return ValueNone" (fun () ->
      Expect.isTrue (Tiles.tryByName "does_not_exist").IsNone "unknown tile"
      Expect.isTrue (Tiles.tryByName "tank_hull_green").IsSome "known tile")

    testCase "fixture enemy defs are wired to baked sprites" (fun () ->
      for def in TestData.Fixtures.all do
        Expect.isGreaterThan def.Hp 0 $"{def.Key} hp"
        Expect.isGreaterThan def.Speed 0f $"{def.Key} speed"
        Expect.isGreaterThan def.GoldReward 0 $"{def.Key} reward"

        Expect.isTrue
          (Tiles.tryByName def.Sprite).IsSome
          $"{def.Key} sprite baked"

        def.Turret
        |> ValueOption.iter(fun turret ->
          Expect.isTrue
            (Tiles.tryByName turret).IsSome
            $"{def.Key} turret baked"))

    testCase "named turret and enemy accessors are wired" (fun () ->
      Expect.equal Tiles.turretGreen.Name "turret_green" "green"
      Expect.equal Tiles.turretRedDual.Name "turret_red_dual" "red dual"

      Expect.equal
        Tiles.turretMissilesDual.Name
        "turret_missiles_dual"
        "missiles"

      Expect.equal Tiles.tankTurretGreen.Name "tank_turret_green" "tank green"
      Expect.equal Tiles.tankTurretBeige.Name "tank_turret_beige" "tank beige"
      Expect.equal Tiles.enemyHulls.Length 2 "hull group"
      Expect.equal Tiles.enemyPlanes.Length 4 "plane group")
  ]
