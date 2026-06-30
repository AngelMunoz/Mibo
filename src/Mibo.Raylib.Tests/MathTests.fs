module Mibo.Raylib.Tests.Math

open System
open System.Numerics
open Expecto
open Raylib_cs
open Mibo.Elmish
open Mibo.Layout

// ──────────────────────────────────────────────
// Frustum Tests
// ──────────────────────────────────────────────

let frustumTests =
  testList "Frustum" [
    testList "Contains(BoundingSphere)" [
      test "sphere at origin is contained by identity-VP frustum" {
        // Identity VP means clip space = world space, NDC [-1,1]³
        // A sphere at origin with small radius should be contained
        let frustum = Frustum(Matrix4x4.Identity)
        let sphere = { Center = Vector3.Zero; Radius = 0.5f }

        Expect.equal
          (frustum.Contains(sphere))
          ContainmentType.Contains
          "Should be fully contained"
      }

      test "sphere far outside is disjoint" {
        let frustum = Frustum(Matrix4x4.Identity)

        let sphere = {
          Center = Vector3(100.0f, 0.0f, 0.0f)
          Radius = 1.0f
        }

        Expect.equal
          (frustum.Contains(sphere))
          ContainmentType.Disjoint
          "Should be disjoint"
      }

      test "sphere partially overlapping is intersects" {
        let frustum = Frustum(Matrix4x4.Identity)
        // Sphere center just inside boundary, radius extends outside
        let sphere = {
          Center = Vector3(0.9f, 0.0f, 0.0f)
          Radius = 0.2f
        }

        Expect.equal
          (frustum.Contains(sphere))
          ContainmentType.Intersects
          "Should intersect"
      }

      test "sphere at negative boundary is contained" {
        let frustum = Frustum(Matrix4x4.Identity)

        let sphere = {
          Center = Vector3(-0.5f, -0.5f, -0.5f)
          Radius = 0.1f
        }

        Expect.equal
          (frustum.Contains(sphere))
          ContainmentType.Contains
          "Should be contained"
      }

      test "sphere at far positive corner is disjoint" {
        let frustum = Frustum(Matrix4x4.Identity)

        let sphere = {
          Center = Vector3(5.0f, 5.0f, 5.0f)
          Radius = 0.1f
        }

        Expect.equal
          (frustum.Contains(sphere))
          ContainmentType.Disjoint
          "Should be disjoint"
      }
    ]

    testList "Contains(BoundingBox)" [
      test "box at origin is contained by identity-VP frustum" {
        let frustum = Frustum(Matrix4x4.Identity)

        let box =
          BoundingBox(Vector3(-0.5f, -0.5f, -0.5f), Vector3(0.5f, 0.5f, 0.5f))

        Expect.equal
          (frustum.Contains(box))
          ContainmentType.Contains
          "Should be fully contained"
      }

      test "box far outside is disjoint" {
        let frustum = Frustum(Matrix4x4.Identity)

        let box =
          BoundingBox(Vector3(50.0f, 0.0f, 0.0f), Vector3(60.0f, 1.0f, 1.0f))

        Expect.equal
          (frustum.Contains(box))
          ContainmentType.Disjoint
          "Should be disjoint"
      }

      test "box partially overlapping is intersects" {
        let frustum = Frustum(Matrix4x4.Identity)
        // Box straddles the right plane (x=1)
        let box =
          BoundingBox(Vector3(0.8f, -0.1f, -0.1f), Vector3(1.2f, 0.1f, 0.1f))

        Expect.equal
          (frustum.Contains(box))
          ContainmentType.Intersects
          "Should intersect"
      }

      test "box straddling left plane is intersects" {
        let frustum = Frustum(Matrix4x4.Identity)

        let box =
          BoundingBox(Vector3(-1.2f, -0.1f, -0.1f), Vector3(-0.8f, 0.1f, 0.1f))

        Expect.equal
          (frustum.Contains(box))
          ContainmentType.Intersects
          "Should intersect"
      }

      test "tiny box at exact boundary" {
        let frustum = Frustum(Matrix4x4.Identity)
        // Box entirely at x > 1, outside right plane
        let box =
          BoundingBox(Vector3(1.01f, 0.0f, 0.0f), Vector3(1.02f, 0.01f, 0.01f))

        Expect.equal
          (frustum.Contains(box))
          ContainmentType.Disjoint
          "Should be disjoint"
      }
    ]

    testList "with perspective projection" [
      test "sphere in front of camera is contained" {
        let view =
          Matrix4x4.CreateLookAt(
            Vector3(0f, 0f, 5f),
            Vector3.Zero,
            Vector3.UnitY
          )

        let proj =
          Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f,
            1.0f,
            0.1f,
            100.0f
          )

        let vp = view * proj
        let frustum = Frustum(vp)
        // Sphere at origin should be visible from camera at z=5 looking at origin
        let sphere = { Center = Vector3.Zero; Radius = 0.5f }
        let result = frustum.Contains(sphere)

        Expect.isTrue
          (result <> ContainmentType.Disjoint)
          "Should not be disjoint"
      }

      test "sphere behind camera is disjoint" {
        let view =
          Matrix4x4.CreateLookAt(
            Vector3(0f, 0f, 5f),
            Vector3.Zero,
            Vector3.UnitY
          )

        let proj =
          Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f,
            1.0f,
            0.1f,
            100.0f
          )

        let vp = view * proj
        let frustum = Frustum(vp)
        // Sphere behind the camera
        let sphere = {
          Center = Vector3(0f, 0f, 20f)
          Radius = 0.5f
        }

        Expect.equal
          (frustum.Contains(sphere))
          ContainmentType.Disjoint
          "Should be disjoint behind camera"
      }

      test "sphere beyond far plane is disjoint" {
        let view =
          Matrix4x4.CreateLookAt(
            Vector3(0f, 0f, 5f),
            Vector3.Zero,
            Vector3.UnitY
          )

        let proj =
          Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f,
            1.0f,
            0.1f,
            10.0f
          )

        let vp = view * proj
        let frustum = Frustum(vp)

        let sphere = {
          Center = Vector3(0f, 0f, -50f)
          Radius = 0.5f
        }

        Expect.equal
          (frustum.Contains(sphere))
          ContainmentType.Disjoint
          "Should be disjoint beyond far plane"
      }
    ]
  ]

// ──────────────────────────────────────────────
// Culling.isVisible2D Tests
// ──────────────────────────────────────────────

let culling2DTests =
  testList "Culling.isVisible2D" [
    test "overlapping rectangles are visible" {
      let view = Rectangle(0.0f, 0.0f, 800.0f, 600.0f)
      let item = Rectangle(100.0f, 100.0f, 50.0f, 50.0f)

      Expect.isTrue
        (Culling.isVisible2D view item)
        "Overlapping should be visible"
    }

    test "non-overlapping rectangles are not visible" {
      let view = Rectangle(0.0f, 0.0f, 800.0f, 600.0f)
      let item = Rectangle(900.0f, 100.0f, 50.0f, 50.0f)

      Expect.isFalse
        (Culling.isVisible2D view item)
        "Non-overlapping should not be visible"
    }

    test "partially overlapping right edge is visible" {
      let view = Rectangle(0.0f, 0.0f, 800.0f, 600.0f)
      let item = Rectangle(780.0f, 100.0f, 50.0f, 50.0f)

      Expect.isTrue
        (Culling.isVisible2D view item)
        "Partially overlapping should be visible"
    }

    test "partially overlapping bottom edge is visible" {
      let view = Rectangle(0.0f, 0.0f, 800.0f, 600.0f)
      let item = Rectangle(100.0f, 580.0f, 50.0f, 50.0f)

      Expect.isTrue
        (Culling.isVisible2D view item)
        "Partially overlapping bottom should be visible"
    }

    test "item to the left is not visible" {
      let view = Rectangle(100.0f, 100.0f, 200.0f, 200.0f)
      let item = Rectangle(0.0f, 150.0f, 50.0f, 50.0f)

      Expect.isFalse
        (Culling.isVisible2D view item)
        "Item to the left should not be visible"
    }

    test "item above is not visible" {
      let view = Rectangle(100.0f, 100.0f, 200.0f, 200.0f)
      let item = Rectangle(150.0f, 0.0f, 50.0f, 50.0f)

      Expect.isFalse
        (Culling.isVisible2D view item)
        "Item above should not be visible"
    }

    test "identical rectangles are visible" {
      let r = Rectangle(10.0f, 20.0f, 100.0f, 100.0f)

      Expect.isTrue
        (Culling.isVisible2D r r)
        "Identical rectangles should be visible"
    }

    test "contained item is visible" {
      let view = Rectangle(0.0f, 0.0f, 800.0f, 600.0f)
      let item = Rectangle(100.0f, 100.0f, 10.0f, 10.0f)

      Expect.isTrue
        (Culling.isVisible2D view item)
        "Contained item should be visible"
    }
  ]

// ──────────────────────────────────────────────
// Layout.polygon Tests
// ──────────────────────────────────────────────

let polygonTests =
  testList "Layout.polygon" [
    test "filled triangle fills interior" {
      let pts = [| struct (5, 2); struct (10, 8); struct (0, 8) |]

      let grid =
        CellGrid2D.create 20 20 (Vector2(1.0f, 1.0f)) Vector2.Zero
        |> Layout.run(Layout.polygon pts true 1)

      // Apex area should be filled
      Expect.equal
        (CellGrid2D.get 5 2 grid)
        (ValueSome 1)
        "Apex should be filled"
      // Interior should be filled
      Expect.equal
        (CellGrid2D.get 5 5 grid)
        (ValueSome 1)
        "Interior should be filled"
      // Upper interior
      Expect.equal
        (CellGrid2D.get 5 3 grid)
        (ValueSome 1)
        "Upper interior should be filled"
    }

    test "outline triangle only draws edges" {
      let pts = [| struct (5, 2); struct (10, 8); struct (0, 8) |]

      let grid =
        CellGrid2D.create 20 20 (Vector2(1.0f, 1.0f)) Vector2.Zero
        |> Layout.run(Layout.polygon pts false 1)

      // Vertices should be set
      Expect.equal
        (CellGrid2D.get 5 2 grid)
        (ValueSome 1)
        "Apex vertex should be set"
      // Center of triangle should NOT be set (outline only)
      Expect.equal
        (CellGrid2D.get 5 6 grid)
        ValueNone
        "Interior should be empty in outline mode"
    }

    test "empty polygon does nothing" {
      let grid =
        CellGrid2D.create 10 10 (Vector2(1.0f, 1.0f)) Vector2.Zero
        |> Layout.run(Layout.polygon [||] true 1)

      for x in 0..9 do
        for y in 0..9 do
          Expect.equal
            (CellGrid2D.get x y grid)
            ValueNone
            "All cells should be empty"
    }

    test "filled rectangle polygon fills area" {
      let pts = [| struct (2, 2); struct (7, 2); struct (7, 7); struct (2, 7) |]

      let grid =
        CellGrid2D.create 20 20 (Vector2(1.0f, 1.0f)) Vector2.Zero
        |> Layout.run(Layout.polygon pts true 1)

      // Top corners (horizontal top edge: yi <= y && yj > y triggers)
      Expect.equal (CellGrid2D.get 2 2 grid) (ValueSome 1) "Top-left corner"
      Expect.equal (CellGrid2D.get 7 2 grid) (ValueSome 1) "Top-right corner"
      // Interior (rows between top and bottom edges)
      Expect.equal
        (CellGrid2D.get 5 5 grid)
        (ValueSome 1)
        "Interior should be filled"

      Expect.equal
        (CellGrid2D.get 3 3 grid)
        (ValueSome 1)
        "Upper interior should be filled"
    }
  ]

// ──────────────────────────────────────────────
// CellGrid2D.iterVisible Tests
// ──────────────────────────────────────────────

let iterVisibleTests =
  testList "CellGrid2D.iterVisible" [
    test "iterVisible visits cells within viewport" {
      let grid = CellGrid2D.create 10 10 (Vector2(32.0f, 32.0f)) Vector2.Zero
      CellGrid2D.set 3 3 42 grid
      CellGrid2D.set 7 7 99 grid

      let visited = System.Collections.Generic.List<int * int * int>()
      // Viewport covering cells 2..5 in both axes
      CellGrid2D.iterVisible
        64
        64
        192
        192
        (fun x y v -> visited.Add((x, y, v)))
        grid

      let has33 = visited |> Seq.exists(fun (x, y, _) -> x = 3 && y = 3)
      let has77 = visited |> Seq.exists(fun (x, y, _) -> x = 7 && y = 7)
      Expect.isTrue has33 "Cell (3,3) should be visited"
      Expect.isFalse has77 "Cell (7,7) should NOT be visited"
    }

    test "iterVisible with empty viewport visits nothing" {
      let grid = CellGrid2D.create 10 10 (Vector2(32.0f, 32.0f)) Vector2.Zero
      CellGrid2D.set 5 5 42 grid

      let visited = System.Collections.Generic.List<int>()
      // Viewport outside grid bounds
      CellGrid2D.iterVisible
        1000
        1000
        2000
        2000
        (fun _ _ v -> visited.Add(v))
        grid

      Expect.isEmpty visited "No cells should be visited"
    }

    test "iterVisible with origin offset" {
      let grid =
        CellGrid2D.create
          10
          10
          (Vector2(32.0f, 32.0f))
          (Vector2(100.0f, 100.0f))

      CellGrid2D.set 2 2 77 grid

      let visited = System.Collections.Generic.List<int * int * int>()
      // Viewport covering the origin-offset area: world 100..200 → cells ~0..3
      CellGrid2D.iterVisible
        100
        100
        200
        200
        (fun x y v -> visited.Add((x, y, v)))
        grid

      let has22 = visited |> Seq.exists(fun (x, y, _) -> x = 2 && y = 2)
      Expect.isTrue has22 "Cell (2,2) should be visited with origin offset"
    }

    test "iterVisible full viewport visits all cells" {
      let grid = CellGrid2D.create 5 5 (Vector2(10.0f, 10.0f)) Vector2.Zero

      for x in 0..4 do
        for y in 0..4 do
          CellGrid2D.set x y (x * 5 + y) grid

      let count = ref 0

      CellGrid2D.iterVisible
        0
        0
        100
        100
        (fun _ _ _ -> count.Value <- count.Value + 1)
        grid

      Expect.equal count.Value 25 "Should visit all 25 cells"
    }
  ]

// ──────────────────────────────────────────────
// Main test list
// ──────────────────────────────────────────────

[<Tests>]
let tests =
  testList "Math" [
    frustumTests
    culling2DTests
    polygonTests
    iterVisibleTests
  ]
