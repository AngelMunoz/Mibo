module Mibo.MonoGame.Tests.PaletteGroup

open Expecto
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Graphics3D.Pipelines

// The function tests pin the math at a 320-matrix budget; the per-pass budget
// constants (MaxMatrices = 448 forward, MaxMatricesDepth = 500 depth) get their
// own test list below.
let private groupSizeFor = PaletteGroup.groupSizeFor 320
let private groupCountFor = PaletteGroup.groupCountFor 320
let private planGroups = PaletteGroup.planGroups 320

[<Tests>]
let paletteGroupTests =
  testList "PaletteGroup (MonoGame)" [
    testList "groupSizeFor" [
      test "80 bones fit 4 instances per group" {
        Expect.equal (groupSizeFor 80) 4 "320 / 80"
      }

      test "320 bones fit exactly one instance per group" {
        Expect.equal (groupSizeFor 320) 1 "320 / 320"
      }

      test "321 bones exceed the grouped-uniform budget" {
        Expect.equal (groupSizeFor 321) 0 "no group fits"
      }

      test "a single bone fits 320 instances per group" {
        Expect.equal (groupSizeFor 1) 320 "320 / 1"
      }
    ]

    testList "groupCountFor" [
      test "1000 instances at 80 bones need 250 groups" {
        Expect.equal (groupCountFor 1000 80) 250 "1000 / 4 per group"
      }

      test "5 instances at 64 bones fit one group" {
        Expect.equal (groupCountFor 5 64) 1 "5 <= 5 per group"
      }

      test "zero instances need no groups" {
        Expect.equal (groupCountFor 0 80) 0 "empty draw"
      }

      test "oversized skeleton needs no groups (per-instance fallback)" {
        Expect.equal (groupCountFor 10 321) 0 "no group fits"
      }
    ]

    testList "planGroups" [
      test "1000 instances at 80 bones fill 250 descriptors" {
        let scratch = Array.zeroCreate<struct (int * int * Texture2D)> 250

        let total = planGroups 1000 80 scratch

        Expect.equal total 250 "group count"

        let struct (firstStart, firstCount, firstTex) = scratch[0]
        Expect.equal firstStart 0 "first group starts at instance 0"
        Expect.equal firstCount 4 "full group of 4 instances"
        Expect.isNull firstTex "null texture on the grouped path"

        let struct (lastStart, lastCount, lastTex) = scratch[249]
        Expect.equal lastStart 996 "last group starts at 249 * 4"
        Expect.equal lastCount 4 "1000 = 250 * 4 exactly"
        Expect.isNull lastTex "null texture on the grouped path"
      }

      test "5 instances at 64 bones fill one descriptor" {
        let scratch = Array.zeroCreate<struct (int * int * Texture2D)> 1

        let total = planGroups 5 64 scratch

        Expect.equal total 1 "one group"

        let struct (start, count, tex) = scratch[0]
        Expect.equal start 0 "starts at instance 0"
        Expect.equal count 5 "partial group carries the remainder"
        Expect.isNull tex "null texture on the grouped path"
      }

      test "oversized skeleton returns -1" {
        let scratch = Array.zeroCreate<struct (int * int * Texture2D)> 1

        Expect.equal (planGroups 10 321 scratch) -1 "no group fits"
      }
    ]

    testList "per-pass budgets" [
      test "forward budget: 23 bones fit 19 instances per group" {
        Expect.equal
          (PaletteGroup.groupSizeFor PaletteGroup.MaxMatrices 23)
          19
          "448 / 23"
      }

      test "depth budget: 23 bones fit 21 instances per group" {
        Expect.equal
          (PaletteGroup.groupSizeFor PaletteGroup.MaxMatricesDepth 23)
          21
          "500 / 23"
      }

      test "depth budget is larger than the forward budget" {
        Expect.isGreaterThan
          PaletteGroup.MaxMatricesDepth
          PaletteGroup.MaxMatrices
          "fewer shadow-pass draws per frame"
      }
    ]
  ]
