module Mibo.MonoGame.Tests.InstanceWorldCache

open Expecto
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics3D.Pipelines

let private transformsOf count =
  Array.init count (fun i -> Matrix.CreateTranslation(float32 i, 0.0f, 0.0f))

let private chunksOf(pairs: (int * int) list) =
  pairs
  |> List.map(fun (start, count) -> struct (start, count, null))
  |> List.toArray

[<Tests>]
let instanceWorldCacheTests =
  testList "InstanceWorldCache (MonoGame)" [
    test "stages once per frame: second obtain returns the memoized rows" {
      let cache = InstanceWorldCache()
      let transforms = transformsOf 4
      let chunks = chunksOf [ 0, 2; 2, 2 ]

      let first = cache.Obtain(transforms, 4, chunks, 2)
      transforms[0] <- Matrix.CreateTranslation(99.0f, 0.0f, 0.0f)

      let second = cache.Obtain(transforms, 4, chunks, 2)

      Expect.isTrue
        (obj.ReferenceEquals(first, second))
        "same staged array, no restage"

      Expect.equal second[0].Row3.X 0.0f "rows keep the pre-mutation transform"
    }

    test "rows carry chunk-local palette offsets" {
      let cache = InstanceWorldCache()
      let transforms = transformsOf 5
      let chunks = chunksOf [ 0, 2; 2, 3 ]

      let rows = cache.Obtain(transforms, 5, chunks, 2)

      for j = 0 to 4 do
        let expected = if j < 2 then float32 j else float32(j - 2)
        Expect.equal rows[j].PaletteOffset expected $"row {j} offset"
        Expect.equal rows[j].Row3.X (float32 j) $"row {j} world"
    }

    test "ReleaseAll forces a restage on the next obtain" {
      let cache = InstanceWorldCache()
      let transforms = transformsOf 2
      let chunks = chunksOf [ 0, 2 ]

      let first = cache.Obtain(transforms, 2, chunks, 1)
      transforms[0] <- Matrix.CreateTranslation(42.0f, 0.0f, 0.0f)
      cache.ReleaseAll()

      let second = cache.Obtain(transforms, 2, chunks, 1)

      Expect.equal second[0].Row3.X 42.0f "restaged with the new transform"
    }

    test "a count mismatch restages instead of returning stale rows" {
      let cache = InstanceWorldCache()
      let transforms = transformsOf 4

      let four = cache.Obtain(transforms, 4, chunksOf [ 0, 4 ], 1)
      Expect.equal four[3].PaletteOffset 3.0f "count 4 staged"

      let two = cache.Obtain(transforms, 2, chunksOf [ 0, 2 ], 1)

      Expect.equal
        two[1].PaletteOffset
        1.0f
        "count 2 restaged under its own plan"
    }

    test "distinct transforms arrays stage independently" {
      let cache = InstanceWorldCache()
      let a = transformsOf 2
      let b = transformsOf 2
      b[0] <- Matrix.CreateTranslation(7.0f, 0.0f, 0.0f)

      let rowsA = cache.Obtain(a, 2, chunksOf [ 0, 2 ], 1)
      let rowsB = cache.Obtain(b, 2, chunksOf [ 0, 2 ], 1)

      Expect.isFalse
        (obj.ReferenceEquals(rowsA, rowsB))
        "each command gets its own pool slot"

      Expect.equal rowsA[0].Row3.X 0.0f "A untouched by B"
      Expect.equal rowsB[0].Row3.X 7.0f "B staged with its own transform"
    }
  ]
