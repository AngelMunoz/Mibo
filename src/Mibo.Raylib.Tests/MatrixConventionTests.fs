module Mibo.Raylib.Tests.MatrixConvention

open System.Numerics
open Expecto
open Raylib_cs

// ──────────────────────────────────────────────
// Raymath.* vs Matrix4x4.* convention probes
//
// raylib's native Matrix struct stores fields column-wise
// (m0, m4, m8, m12, m1, ...) while System.Numerics.Matrix4x4 stores
// rows (M11, M12, M13, M14, M21, ...). raylib-cs maps both onto the
// same Matrix4x4 blittable type, so a matrix built by Raymath.* is the
// TRANSPOSE of the equivalent Matrix4x4.* matrix. These tests pin the
// exact relations so the animation/pose code composes transforms in a
// single convention instead of mixing the two silently.
// ──────────────────────────────────────────────

let private expectMatrixClose
  (expected: Matrix4x4)
  (actual: Matrix4x4)
  message
  =
  let cells = [|
    expected.M11, actual.M11
    expected.M12, actual.M12
    expected.M13, actual.M13
    expected.M14, actual.M14
    expected.M21, actual.M21
    expected.M22, actual.M22
    expected.M23, actual.M23
    expected.M24, actual.M24
    expected.M31, actual.M31
    expected.M32, actual.M32
    expected.M33, actual.M33
    expected.M34, actual.M34
    expected.M41, actual.M41
    expected.M42, actual.M42
    expected.M43, actual.M43
    expected.M44, actual.M44
  |]

  for e, a in cells do
    Expect.floatClose Accuracy.medium (float e) (float a) message

/// A non-symmetric matrix with distinct values in every cell.
let private arbitraryMatrix =
  Matrix4x4(
    1.0f,
    2.0f,
    3.0f,
    4.0f,
    5.0f,
    6.0f,
    7.0f,
    8.0f,
    9.0f,
    10.0f,
    11.0f,
    12.0f,
    13.0f,
    14.0f,
    15.0f,
    16.0f
  )

let conventionTests =
  testList "Raymath vs Matrix4x4 conventions" [
    test
      "MatrixTranslate stores translation in M14/M24/M34 (transposed vs System.Numerics)" {
      let raymath = Raymath.MatrixTranslate(1.0f, 2.0f, 3.0f)
      let numerics = Matrix4x4.CreateTranslation(1.0f, 2.0f, 3.0f)

      Expect.equal raymath.M14 1.0f "Raymath puts X in M14"
      Expect.equal raymath.M24 2.0f "Raymath puts Y in M24"
      Expect.equal raymath.M34 3.0f "Raymath puts Z in M34"
      Expect.equal raymath.M41 0.0f "Raymath leaves M41 empty"

      expectMatrixClose
        (Matrix4x4.Transpose numerics)
        raymath
        "Raymath.MatrixTranslate == Transpose(CreateTranslation)"
    }

    test "QuaternionToMatrix is the transpose of CreateFromQuaternion" {
      let q = Quaternion.Normalize(Quaternion(0.3f, -0.5f, 0.7f, 0.36f))

      expectMatrixClose
        (Matrix4x4.Transpose(Matrix4x4.CreateFromQuaternion q))
        (Raymath.QuaternionToMatrix q)
        "Raymath.QuaternionToMatrix == Transpose(CreateFromQuaternion)"
    }

    test "MatrixMultiply multiplies with swapped operands vs Matrix4x4.Multiply" {
      let left = arbitraryMatrix

      let right =
        Matrix4x4(
          16.0f,
          15.0f,
          14.0f,
          13.0f,
          12.0f,
          11.0f,
          10.0f,
          9.0f,
          8.0f,
          7.0f,
          6.0f,
          5.0f,
          4.0f,
          3.0f,
          2.0f,
          1.0f
        )

      expectMatrixClose
        (Matrix4x4.Multiply(right, left))
        (Raymath.MatrixMultiply(left, right))
        "Raymath.MatrixMultiply(L, R) == Matrix4x4.Multiply(R, L)"
    }

    test
      "raylib-native skinning palette equals the transposed System.Numerics palette" {
      // A bind pose and current pose built via System.Numerics (as
      // computePose does), and their native-layout twins (as the model's
      // bindPose arrives from raylib).
      let bindSN =
        Matrix4x4.Multiply(
          Matrix4x4.CreateFromQuaternion(
            Quaternion.Normalize(Quaternion(0.1f, 0.2f, 0.3f, 0.92f))
          ),
          Matrix4x4.CreateTranslation(0.5f, 1.0f, -0.25f)
        )

      let poseSN =
        Matrix4x4.Multiply(
          Matrix4x4.CreateFromQuaternion(
            Quaternion.Normalize(Quaternion(-0.4f, 0.15f, 0.6f, 0.67f))
          ),
          Matrix4x4.CreateTranslation(1.5f, 0.75f, 2.0f)
        )

      let mutable invBindSN = Matrix4x4.Identity
      Matrix4x4.Invert(bindSN, &invBindSN) |> ignore

      // raylib native path: boneMatrices = MatrixMultiply(MatrixInvert(bind), current),
      // all in native layout (= transpose of the System.Numerics layout).
      let nativePalette =
        Raymath.MatrixMultiply(
          Raymath.MatrixInvert(Matrix4x4.Transpose bindSN),
          Matrix4x4.Transpose poseSN
        )

      // What computePose uploads today, transposed into native layout.
      let transposedSNPalette =
        Matrix4x4.Transpose(Matrix4x4.Multiply(invBindSN, poseSN))

      expectMatrixClose
        nativePalette
        transposedSNPalette
        "native boneMatrices == Transpose(Matrix4x4.Multiply(invBind, pose))"
    }
  ]

// ──────────────────────────────────────────────
// Main test list
// ──────────────────────────────────────────────

[<Tests>]
let tests = conventionTests
