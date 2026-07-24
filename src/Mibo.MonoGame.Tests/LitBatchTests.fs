module Mibo.MonoGame.Tests.LitBatchTests

open System
open Expecto
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D

// These tests cover the pure-CPU parts of the lit-sprite batching refactor:
//   - UV computation with the negative-source-size flip convention
//   - corner transform with origin/rotation
//   - the indexed-quad index pattern
//   - the batch-key change predicate (flush-trigger logic)
// The actual GPU submission (DrawUserIndexedPrimitives) needs a live
// GraphicsDevice and is out of scope for the headless test harness; the
// math below is the regression surface for the batching change.

let private approxEqual (a: float32) (b: float32) = abs(a - b) < 0.0001f

let private v2approxEqual (a: Vector2) (b: Vector2) =
  approxEqual a.X b.X && approxEqual a.Y b.Y

let uvTests =
  testList "LitBatchTessellation.computeUvs" [
    test "no flip maps source rect straight to [u0,u1]x[v0,v1]" {
      // 32x32 texture, source rect (0,0,16,16) -> upper-left quarter.
      let src = Rectangle(0, 0, 16, 16)

      let struct (u0, u1, v0, v1) =
        LitBatchTessellation.computeUvs src 32.0f 32.0f

      Expect.isTrue (approxEqual u0 0.0f) "u0 left edge"
      Expect.isTrue (approxEqual u1 0.5f) "u1 half"
      Expect.isTrue (approxEqual v0 0.0f) "v0 top edge"
      Expect.isTrue (approxEqual v1 0.5f) "v1 half"
    }

    test "offset source rect produces correct normalized UVs" {
      // 100x100 texture, source rect (25, 25, 50, 50) -> middle band.
      let src = Rectangle(25, 25, 50, 50)

      let struct (u0, u1, v0, v1) =
        LitBatchTessellation.computeUvs src 100.0f 100.0f

      Expect.isTrue (approxEqual u0 0.25f) "u0 at 0.25"
      Expect.isTrue (approxEqual u1 0.75f) "u1 at 0.75"
      Expect.isTrue (approxEqual v0 0.25f) "v0 at 0.25"
      Expect.isTrue (approxEqual v1 0.75f) "v1 at 0.75"
    }

    test "negative width flips the U axis (u0/u1 swapped)" {
      // Negative source width signals a horizontal flip; u0 and u1 swap so
      // the TL corner samples what the TR corner would have.
      let src = Rectangle(0, 0, -16, 16)

      let struct (u0, u1, v0, v1) =
        LitBatchTessellation.computeUvs src 32.0f 32.0f

      Expect.isTrue (approxEqual u0 0.5f) "flipped u0 becomes the right edge"
      Expect.isTrue (approxEqual u1 0.0f) "flipped u1 becomes the left edge"
      // V axis unaffected by a width-only flip.
      Expect.isTrue (approxEqual v0 0.0f) "v0 top edge"
      Expect.isTrue (approxEqual v1 0.5f) "v1 half"
    }

    test "negative height flips the V axis (v0/v1 swapped)" {
      let src = Rectangle(0, 0, 16, -16)

      let struct (u0, u1, v0, v1) =
        LitBatchTessellation.computeUvs src 32.0f 32.0f

      Expect.isTrue (approxEqual u0 0.0f) "u0 left edge"
      Expect.isTrue (approxEqual u1 0.5f) "u1 half"
      Expect.isTrue (approxEqual v0 0.5f) "flipped v0 becomes the bottom edge"
      Expect.isTrue (approxEqual v1 0.0f) "flipped v1 becomes the top edge"
    }

    test "both axes negative flips U and V" {
      let src = Rectangle(0, 0, -16, -16)

      let struct (u0, u1, v0, v1) =
        LitBatchTessellation.computeUvs src 32.0f 32.0f

      Expect.isTrue (approxEqual u0 0.5f) "flipped u0 right edge"
      Expect.isTrue (approxEqual u1 0.0f) "flipped u1 left edge"
      Expect.isTrue (approxEqual v0 0.5f) "flipped v0 bottom edge"
      Expect.isTrue (approxEqual v1 0.0f) "flipped v1 top edge"
    }
  ]

let cornerTests =
  testList "LitBatchTessellation.computeCorners" [
    test "no rotation, zero origin: corners are the dest rect" {
      let dest = Rectangle(10, 20, 30, 40)

      let struct (tl, tr, bl, br) =
        LitBatchTessellation.computeCorners dest Vector2.Zero 0.0f

      Expect.isTrue
        (v2approxEqual tl (Vector2(10.0f, 20.0f)))
        "TL = dest origin"

      Expect.isTrue (v2approxEqual tr (Vector2(40.0f, 20.0f))) "TR = dest right"

      Expect.isTrue
        (v2approxEqual bl (Vector2(10.0f, 60.0f)))
        "BL = dest bottom"

      Expect.isTrue
        (v2approxEqual br (Vector2(40.0f, 60.0f)))
        "BR = dest bottom-right"
    }

    test "nonzero origin translates before and after rotation" {
      // Origin shifts the pivot: corners are computed as (local - origin)
      // rotated then translated back by (dest.xy + origin).
      let dest = Rectangle(100, 100, 32, 32)
      let origin = Vector2(16.0f, 16.0f) // centered

      let struct (tl, tr, bl, br) =
        LitBatchTessellation.computeCorners dest origin 0.0f

      Expect.isTrue (v2approxEqual tl (Vector2(100.0f, 100.0f))) "TL"
      Expect.isTrue (v2approxEqual tr (Vector2(132.0f, 100.0f))) "TR"
      Expect.isTrue (v2approxEqual bl (Vector2(100.0f, 132.0f))) "BL"
      Expect.isTrue (v2approxEqual br (Vector2(132.0f, 132.0f))) "BR"
    }

    test "90 degree rotation around a centered origin rotates the corners" {
      // Rotating a 32x32 quad by 90° (Pi/2) around its center (16,16) swaps the
      // local axes: the TL corner moves to where TR was, etc. Dest origin stays
      // at (100,100), origin (16,16).
      let dest = Rectangle(100, 100, 32, 32)
      let origin = Vector2(16.0f, 16.0f)

      let struct (tl, tr, bl, br) =
        LitBatchTessellation.computeCorners dest origin (float32(Math.PI / 2.0))

      // Before rotation, local corners relative to origin:
      //   TL=(-16,-16), TR=(16,-16), BL=(-16,16), BR=(16,16).
      // After 90° CCW rotation (x,y)->(-y,x):
      //   TL->(16,-16), TR->(16,16), BL->(-16,-16), BR->(-16,16).
      // Translated back by (100+16, 100+16) = (116,116):
      Expect.isTrue (v2approxEqual tl (Vector2(132.0f, 100.0f))) "TL rotated"
      Expect.isTrue (v2approxEqual tr (Vector2(132.0f, 132.0f))) "TR rotated"
      Expect.isTrue (v2approxEqual bl (Vector2(100.0f, 100.0f))) "BL rotated"
      Expect.isTrue (v2approxEqual br (Vector2(100.0f, 132.0f))) "BR rotated"
    }
  ]

let indexTests =
  testList "LitBatchTessellation.writeIndices" [
    test "writes two triangles wound TL,TR,BR / TL,BR,BL at base vertex 0" {
      let indices = Array.zeroCreate<int> 6
      LitBatchTessellation.writeIndices indices 0 0
      Expect.equal indices.[0] 0 "i0 = TL"
      Expect.equal indices.[1] 1 "i1 = TR"
      Expect.equal indices.[2] 2 "i2 = BR"
      Expect.equal indices.[3] 0 "i3 = TL"
      Expect.equal indices.[4] 2 "i4 = BR"
      Expect.equal indices.[5] 3 "i5 = BL"
    }

    test "offsets by baseVertex for a second quad in the buffer" {
      let indices = Array.zeroCreate<int> 12
      LitBatchTessellation.writeIndices indices 0 0
      // Second quad starts at vertex 4 (the first quad used verts 0..3).
      LitBatchTessellation.writeIndices indices 6 4
      let secondQuad = indices.[6..11]
      Expect.equal secondQuad.[0] 4 "2nd i0 = TL"
      Expect.equal secondQuad.[1] 5 "2nd i1 = TR"
      Expect.equal secondQuad.[2] 6 "2nd i2 = BR"
      Expect.equal secondQuad.[3] 4 "2nd i3 = TL"
      Expect.equal secondQuad.[4] 6 "2nd i4 = BR"
      Expect.equal secondQuad.[5] 7 "2nd i5 = BL"
    }
  ]

let batchKeyTests =
  testList "LitBatchTessellation.batchKeyChanged" [
    // Use plain obj instances as stand-ins for Effect/Texture references.
    // The predicate only cares about reference identity, so any obj works.
    let eff1 = obj()
    let eff2 = obj()
    let tex1 = obj()
    let tex2 = obj()
    let nm1 = obj()
    let nm2 = obj()

    test "empty batch (hasBatch=false) always reports a key change" {
      // Even with identical refs, the first sprite starts a new batch.
      let changed =
        LitBatchTessellation.batchKeyChanged
          false
          eff1
          tex1
          ValueNone
          eff1
          tex1
          ValueNone

      Expect.isTrue changed "first sprite must start a batch"
    }

    test "same effect+texture (no normal map) keeps the batch" {
      let changed =
        LitBatchTessellation.batchKeyChanged
          true
          eff1
          tex1
          ValueNone
          eff1
          tex1
          ValueNone

      Expect.isFalse changed "identical key should not flush"
    }

    test "texture change forces a flush" {
      let changed =
        LitBatchTessellation.batchKeyChanged
          true
          eff1
          tex1
          ValueNone
          eff1
          tex2
          ValueNone

      Expect.isTrue changed "different albedo texture must flush"
    }

    test "effect change forces a flush" {
      let changed =
        LitBatchTessellation.batchKeyChanged
          true
          eff1
          tex1
          ValueNone
          eff2
          tex1
          ValueNone

      Expect.isTrue changed "different effect (plain<->normal-map) must flush"
    }

    test "normal-map change forces a flush (no last-wins sampler bug)" {
      let changed =
        LitBatchTessellation.batchKeyChanged
          true
          eff1
          tex1
          (ValueSome nm1)
          eff1
          tex1
          (ValueSome nm2)

      Expect.isTrue
        changed
        "different normal map must flush so each sprite samples its own"
    }

    test "gaining a normal map forces a flush" {
      let changed =
        LitBatchTessellation.batchKeyChanged
          true
          eff1
          tex1
          ValueNone
          eff1
          tex1
          (ValueSome nm1)

      Expect.isTrue changed "plain -> normal-map must flush"
    }

    test "losing a normal map forces a flush" {
      let changed =
        LitBatchTessellation.batchKeyChanged
          true
          eff1
          tex1
          (ValueSome nm1)
          eff1
          tex1
          ValueNone

      Expect.isTrue changed "normal-map -> plain must flush"
    }

    test "same normal map reference keeps the batch" {
      let changed =
        LitBatchTessellation.batchKeyChanged
          true
          eff1
          tex1
          (ValueSome nm1)
          eff1
          tex1
          (ValueSome nm1)

      Expect.isFalse changed "identical normal-map reference should not flush"
    }
  ]

/// Count how many flushes a sequence of (effect, texture, normalMap) keys would
/// produce, given that the first sprite always starts one batch (one draw) and
/// every key change starts another. This models the litBatchAdd flush behavior
/// without a GPU: a draw is issued per maximal run of identical keys.
let private countDraws(keys: (obj * obj * obj voption) list) : int =
  match keys with
  | [] -> 0
  | (e0, t0, n0) :: rest ->
    let mutable draws = 1
    let mutable curE, curT, curN = e0, t0, n0

    for (e, t, n) in rest do
      if LitBatchTessellation.batchKeyChanged true curE curT curN e t n then
        draws <- draws + 1
        curE <- e
        curT <- t
        curN <- n

    draws

let drawCountTests =
  testList "lit batch draw count per key sequence" [
    test "three sprites sharing one texture+effect = one draw" {
      let tex = obj()
      let eff = obj()

      let keys = [
        (eff, tex, ValueNone)
        (eff, tex, ValueNone)
        (eff, tex, ValueNone)
      ]

      Expect.equal (countDraws keys) 1 "should collapse into a single draw"
    }

    test "alternating textures = one draw per sprite" {
      let tex1 = obj()
      let tex2 = obj()
      let eff = obj()

      let keys = [
        (eff, tex1, ValueNone)
        (eff, tex2, ValueNone)
        (eff, tex1, ValueNone)
      ]

      Expect.equal (countDraws keys) 3 "no two consecutive share a key"
    }

    test "plain<->normal-map variant switch = flush on each switch" {
      let tex = obj()
      let effPlain = obj()
      let effNm = obj()
      let nm = obj()
      // plain, nm, plain -> 3 draws
      let keys = [
        (effPlain, tex, ValueNone)
        (effNm, tex, ValueSome nm)
        (effPlain, tex, ValueNone)
      ]

      Expect.equal (countDraws keys) 3 "each variant switch flushes"
    }

    test "same plain effect, two different normal maps = two draws" {
      let tex = obj()
      let eff = obj()
      let nm1 = obj()
      let nm2 = obj()

      let keys = [
        (eff, tex, ValueSome nm1)
        (eff, tex, ValueSome nm1)
        (eff, tex, ValueSome nm2)
      ]
      // First two share nm1 -> 1 draw, third changes to nm2 -> 2 draws total.
      Expect.equal (countDraws keys) 2 "normal-map change splits the batch"
    }

    test "grouped ordering minimizes draws" {
      // If the view groups sprites by texture, AAABBBC -> 3 draws
      // (vs. interleaved ABCABC -> 6 draws). This is the perf payoff of grouping.
      let eff = obj()
      let a = obj()
      let b = obj()
      let c = obj()

      let keys = [
        (eff, a, ValueNone)
        (eff, a, ValueNone)
        (eff, a, ValueNone)
        (eff, b, ValueNone)
        (eff, b, ValueNone)
        (eff, b, ValueNone)
        (eff, c, ValueNone)
      ]

      Expect.equal
        (countDraws keys)
        3
        "grouped sprites collapse to one draw per texture"
    }
  ]

let allTests =
  testList "Mibo.MonoGame lit-sprite batching" [
    uvTests
    cornerTests
    indexTests
    batchKeyTests
    drawCountTests
  ]
