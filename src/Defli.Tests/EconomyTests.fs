module Defli.Tests.EconomyTests

open Expecto
open AdaptiveSlop.Core
open TestData
open Defli.World
open Defli.World.Systems
open Defli.World.Systems.Economy

let tests =
  testList "Economy" [
    testCase "init reads config" (fun () ->
      let m = Economy.init Fixtures.cfg
      Expect.equal (AVal.getValue m.Gold) Fixtures.cfg.StartingGold "gold"
      Expect.equal (AVal.getValue m.Lives) Fixtures.cfg.StartingLives "lives"
      Expect.isFalse (AVal.getValue m.GameOver) "not over")

    testCase "gold spend/earn clamps at zero" (fun () ->
      let m = Economy.init Fixtures.cfg
      Economy.update (EconomyMsg.SpendGold 1000) m
      Expect.equal (AVal.getValue m.Gold) 0 "clamped at zero"
      Economy.update (EconomyMsg.EarnGold 25) m
      Expect.equal (AVal.getValue m.Gold) 25 "earned")

    testCase "lives clamp and drive game over" (fun () ->
      let m = Economy.init Fixtures.cfg

      for _ in 1 .. Fixtures.cfg.StartingLives do
        Economy.update EconomyMsg.LoseLife m

      Expect.equal (AVal.getValue m.Lives) 0 "lives zero"
      Expect.isTrue (AVal.getValue m.GameOver) "game over"
      Economy.update EconomyMsg.LoseLife m
      Expect.equal (AVal.getValue m.Lives) 0 "lives stay clamped")
  ]
