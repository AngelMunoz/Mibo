module Mibo.MonoGame.Tests.RenderBuffer3DTests

open Expecto
open Microsoft.Xna.Framework
open Mibo.Elmish.Graphics3D

// AddDrawInstanced witness: the buffer copies the caller's transforms into a
// pooled array at record time, so refilling the array after the call (e.g.
// for the next camera block) cannot corrupt the recorded command — the same
// guarantee AddAnimatedModelInstanced gives.

let private stubMesh: PrimitiveMesh = {
  Vertices = null
  Indices = null
  PrimitiveCount = 0
  Bounds = BoundingSphere()
}

[<Tests>]
let tests =
  testList "AddDrawInstanced witness" [
    test "copies transforms at record time and clamps the count" {
      use buffer = new RenderBuffer3D()

      let transforms = [|
        Matrix.Identity
        Matrix.CreateTranslation(1.0f, 2.0f, 3.0f)
      |]

      buffer.AddDrawInstanced(
        stubMesh,
        transforms,
        Material3D.defaults,
        5,
        ValueNone
      )

      // Simulate the caller refilling its persistent array before execution.
      transforms[0] <- Matrix.CreateTranslation(9.0f, 9.0f, 9.0f)

      Expect.equal buffer.Count 1 "expected exactly one command"

      match buffer[0] with
      | Command3D.DrawInstanced(_, t, _, _, count, _, _) ->
        Expect.equal count 2 "count clamps to transforms.Length (was 5)"

        Expect.isFalse
          (System.Object.ReferenceEquals(t, transforms))
          "transforms are copied at record time"

        Expect.equal
          t[0]
          Matrix.Identity
          "mutating the caller's array must not affect the recorded command"

        Expect.equal t[1] transforms[1] "transform 1 copied"
      | other -> failtest $"expected DrawInstanced, got %A{other}"
    }
  ]
