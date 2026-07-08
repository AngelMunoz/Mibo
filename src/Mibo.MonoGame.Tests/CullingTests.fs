module Mibo.MonoGame.Tests.Culling

open System
open Expecto
open Microsoft.Xna.Framework
open Mibo.Elmish

// A frustum for a camera at (0,0,10) looking down -Z toward the origin.
let makeFrustum() =
  let view =
    Matrix.CreateLookAt(Vector3(0.f, 0.f, 10.f), Vector3.Zero, Vector3.Up)

  let proj =
    Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1.0f, 0.1f, 100.f)

  BoundingFrustum(view * proj)

let isVisibleSphereTests =
  testList "Culling.isVisible (sphere)" [
    test "sphere at origin is visible" {
      let f = makeFrustum()
      let sphere = BoundingSphere(Vector3.Zero, 1.0f)
      Expect.isTrue (Culling.isVisible f sphere) "Origin is inside the frustum"
    }

    test "sphere behind the camera is not visible" {
      let f = makeFrustum()
      let sphere = BoundingSphere(Vector3(0.f, 0.f, 1000.f), 1.0f)

      Expect.isFalse
        (Culling.isVisible f sphere)
        "Behind the camera should be culled"
    }

    test "sphere far off to the side is not visible" {
      let f = makeFrustum()
      let sphere = BoundingSphere(Vector3(1000.f, 0.f, 0.f), 1.0f)

      Expect.isFalse
        (Culling.isVisible f sphere)
        "Far outside the frustum should be culled"
    }
  ]

let isVisibleBoxTests =
  testList "Culling.isVisibleBox (box)" [
    test "box around origin is visible" {
      let f = makeFrustum()
      let box = BoundingBox(Vector3(-1.f, -1.f, -1.f), Vector3(1.f, 1.f, 1.f))

      Expect.isTrue
        (Culling.isVisibleBox f box)
        "Box at origin is inside the frustum"
    }

    test "box far off to the side is not visible" {
      let f = makeFrustum()

      let box =
        BoundingBox(Vector3(999.f, -1.f, -1.f), Vector3(1001.f, 1.f, 1.f))

      Expect.isFalse
        (Culling.isVisibleBox f box)
        "Far outside the frustum should be culled"
    }

    test "box behind the camera is not visible" {
      let f = makeFrustum()

      let box =
        BoundingBox(Vector3(-1.f, -1.f, 999.f), Vector3(1.f, 1.f, 1001.f))

      Expect.isFalse
        (Culling.isVisibleBox f box)
        "Behind the camera should be culled"
    }
  ]

let isVisible2DTests =
  testList "Culling.isVisible2D" [
    test "overlapping rectangles are visible" {
      let view = Rectangle(0, 0, 800, 600)
      let item = Rectangle(100, 100, 50, 50)

      Expect.isTrue
        (Culling.isVisible2D view item)
        "Overlapping should be visible"
    }

    test "non-overlapping rectangles are not visible" {
      let view = Rectangle(0, 0, 800, 600)
      let item = Rectangle(900, 100, 50, 50)

      Expect.isFalse
        (Culling.isVisible2D view item)
        "Non-overlapping should not be visible"
    }

    test "partially overlapping right edge is visible" {
      let view = Rectangle(0, 0, 800, 600)
      let item = Rectangle(780, 100, 50, 50)

      Expect.isTrue
        (Culling.isVisible2D view item)
        "Partially overlapping should be visible"
    }

    test "partially overlapping bottom edge is visible" {
      let view = Rectangle(0, 0, 800, 600)
      let item = Rectangle(100, 580, 50, 50)

      Expect.isTrue
        (Culling.isVisible2D view item)
        "Partially overlapping bottom should be visible"
    }

    test "item to the left is not visible" {
      let view = Rectangle(100, 100, 200, 200)
      let item = Rectangle(0, 150, 50, 50)

      Expect.isFalse
        (Culling.isVisible2D view item)
        "Item to the left should not be visible"
    }

    test "item above is not visible" {
      let view = Rectangle(100, 100, 200, 200)
      let item = Rectangle(150, 0, 50, 50)

      Expect.isFalse
        (Culling.isVisible2D view item)
        "Item above should not be visible"
    }

    test "identical rectangles are visible" {
      let r = Rectangle(10, 20, 100, 100)

      Expect.isTrue
        (Culling.isVisible2D r r)
        "Identical rectangles should be visible"
    }

    test "contained item is visible" {
      let view = Rectangle(0, 0, 800, 600)
      let item = Rectangle(100, 100, 10, 10)

      Expect.isTrue
        (Culling.isVisible2D view item)
        "Contained item should be visible"
    }
  ]

[<Tests>]
let tests =
  testList "Culling" [
    isVisibleSphereTests
    isVisibleBoxTests
    isVisible2DTests
  ]
