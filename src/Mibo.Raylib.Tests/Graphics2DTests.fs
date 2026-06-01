module Mibo.Raylib.Tests.Graphics2D

open System
open System.Numerics
open Expecto
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Animation
open Mibo.Layout

// ──────────────────────────────────────────────
// Helpers
// ──────────────────────────────────────────────

let private tex =
  Texture2D(
    Id = 1u,
    Width = 64,
    Height = 64,
    Mipmaps = 1,
    Format = PixelFormat.UncompressedR8G8B8A8
  )

let private normalMapTex =
  Texture2D(
    Id = 2u,
    Width = 64,
    Height = 64,
    Mipmaps = 1,
    Format = PixelFormat.UncompressedR8G8B8A8
  )

let private font = Unchecked.defaultof<Font>
let private rect = Rectangle(10.0f, 20.0f, 30.0f, 40.0f)
let private v1 = Vector2(1.0f, 2.0f)
let private v2 = Vector2(3.0f, 4.0f)
let private v3 = Vector2(5.0f, 6.0f)

// ──────────────────────────────────────────────
// Command2D Factory Tests
// ──────────────────────────────────────────────

let commandFactoryTests =
  testList "Command2D factories" [
    test "fillRect maps rect, color, layer correctly" {
      let cmd = Command2D.fillRect (5<RenderLayer>, Color.Red) rect

      match cmd with
      | Command2D.FillRect(r, c, l) ->
        Expect.equal r rect "Rect should match"
        Expect.equal c Color.Red "Color should match"
        Expect.equal l 5<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected FillRect"
    }

    test "rectOutline maps rect, thickness, color, layer correctly" {
      let cmd = Command2D.rectOutline (3<RenderLayer>, Color.Blue, 2.5f) rect

      match cmd with
      | Command2D.RectOutline(r, t, c, l) ->
        Expect.equal r rect "Rect should match"
        Expect.floatClose Accuracy.medium (float t) 2.5 "Thickness should match"
        Expect.equal c Color.Blue "Color should match"
        Expect.equal l 3<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected RectOutline"
    }

    test "fillCircle maps center, radius, color, layer correctly" {
      let cmd = Command2D.fillCircle (1<RenderLayer>, Color.Green) (v1, 50.0f)

      match cmd with
      | Command2D.FillCircle(center, radius, c, l) ->
        Expect.equal center v1 "Center should match"

        Expect.floatClose
          Accuracy.medium
          (float radius)
          50.0
          "Radius should match"

        Expect.equal c Color.Green "Color should match"
        Expect.equal l 1<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected FillCircle"
    }

    test "circleOutline maps center, radius, color, layer correctly" {
      let cmd =
        Command2D.circleOutline (2<RenderLayer>, Color.White) (v1, 25.0f)

      match cmd with
      | Command2D.CircleOutline(center, radius, c, l) ->
        Expect.equal center v1 "Center should match"

        Expect.floatClose
          Accuracy.medium
          (float radius)
          25.0
          "Radius should match"

        Expect.equal c Color.White "Color should match"
        Expect.equal l 2<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected CircleOutline"
    }

    test "line maps start, finish, color, layer correctly" {
      let cmd = Command2D.line (4<RenderLayer>, Color.Yellow) (v1, v2)

      match cmd with
      | Command2D.Line(s, f, c, l) ->
        Expect.equal s v1 "Start should match"
        Expect.equal f v2 "Finish should match"
        Expect.equal c Color.Yellow "Color should match"
        Expect.equal l 4<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected Line"
    }

    test "lineThick maps start, finish, thickness, color, layer correctly" {
      let cmd = Command2D.lineThick (6<RenderLayer>, Color.Red, 3.0f) (v1, v2)

      match cmd with
      | Command2D.LineThick(s, f, t, c, l) ->
        Expect.equal s v1 "Start should match"
        Expect.equal f v2 "Finish should match"
        Expect.floatClose Accuracy.medium (float t) 3.0 "Thickness should match"
        Expect.equal c Color.Red "Color should match"
        Expect.equal l 6<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected LineThick"
    }

    test "lineStrip maps points, color, layer correctly" {
      let pts = [| v1; v2; v3 |]
      let cmd = Command2D.lineStrip (7<RenderLayer>, Color.SkyBlue) pts

      match cmd with
      | Command2D.LineStrip(points, c, l) ->
        Expect.equal points pts "Points array should match"
        Expect.equal c Color.SkyBlue "Color should match"
        Expect.equal l 7<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected LineStrip"
    }

    test "bezier stores start, control, finish, thickness" {
      let control = Vector2(100.0f, -50.0f)

      let cmd =
        Command2D.bezier (0<RenderLayer>, Color.Magenta, 2.0f) (v1, control, v2)

      match cmd with
      | Command2D.Bezier(s, ctrl, f, t, c, l) ->
        Expect.equal s v1 "Start should match"

        Expect.equal
          ctrl
          control
          "Control point should be preserved in command data"

        Expect.equal f v2 "Finish should match"
        Expect.floatClose Accuracy.medium (float t) 2.0 "Thickness should match"
        Expect.equal c Color.Magenta "Color should match"
        Expect.equal l 0<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected Bezier"
    }

    test "triangle maps v1, v2, v3, color, layer correctly" {
      let cmd = Command2D.triangle (8<RenderLayer>, Color.Red) (v1, v2, v3)

      match cmd with
      | Command2D.Triangle(a, b, c, col, l) ->
        Expect.equal a v1 "V1 should match"
        Expect.equal b v2 "V2 should match"
        Expect.equal c v3 "V3 should match"
        Expect.equal col Color.Red "Color should match"
        Expect.equal l 8<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected Triangle"
    }

    test "triangleFan maps points, color, layer correctly" {
      let pts = [| v1; v2; v3 |]
      let cmd = Command2D.triangleFan (9<RenderLayer>, Color.Blue) pts

      match cmd with
      | Command2D.TriangleFan(points, c, l) ->
        Expect.equal points pts "Points should match"
        Expect.equal c Color.Blue "Color should match"
        Expect.equal l 9<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected TriangleFan"
    }

    test "triangleStrip maps points, color, layer correctly" {
      let pts = [| v1; v2; v3 |]
      let cmd = Command2D.triangleStrip (10<RenderLayer>, Color.Green) pts

      match cmd with
      | Command2D.TriangleStrip(points, c, l) ->
        Expect.equal points pts "Points should match"
        Expect.equal c Color.Green "Color should match"
        Expect.equal l 10<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected TriangleStrip"
    }

    test "fillPoly maps center, sides, radius, rotation, color, layer correctly" {
      let center = Vector2(100.0f, 200.0f)

      let cmd =
        Command2D.fillPoly
          (11<RenderLayer>, Color.White)
          (center, 6, 50.0f, 45.0f)

      match cmd with
      | Command2D.FillPoly(c, sides, r, rot, col, l) ->
        Expect.equal c center "Center should match"
        Expect.equal sides 6 "Sides should match"
        Expect.floatClose Accuracy.medium (float r) 50.0 "Radius should match"

        Expect.floatClose
          Accuracy.medium
          (float rot)
          45.0
          "Rotation should match"

        Expect.equal col Color.White "Color should match"
        Expect.equal l 11<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected FillPoly"
    }

    test
      "polyOutline maps center, sides, radius, rotation, thickness, color, layer" {
      let center = Vector2(50.0f, 50.0f)

      let cmd =
        Command2D.polyOutline
          (12<RenderLayer>, Color.Black, 1.5f)
          (center, 5, 30.0f, 0.0f)

      match cmd with
      | Command2D.PolyOutline(c, sides, r, rot, t, col, l) ->
        Expect.equal c center "Center should match"
        Expect.equal sides 5 "Sides should match"
        Expect.floatClose Accuracy.medium (float r) 30.0 "Radius should match"

        Expect.floatClose
          Accuracy.medium
          (float rot)
          0.0
          "Rotation should match"

        Expect.floatClose Accuracy.medium (float t) 1.5 "Thickness should match"
        Expect.equal col Color.Black "Color should match"
        Expect.equal l 12<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected PolyOutline"
    }

    test "fillEllipse maps centerX, centerY, radiusH, radiusV, color, layer" {
      let cmd =
        Command2D.fillEllipse
          (13<RenderLayer>, Color.Red)
          (100, 200, 30.0f, 15.0f)

      match cmd with
      | Command2D.FillEllipse(cx, cy, rh, rv, c, l) ->
        Expect.equal cx 100 "CenterX should match"
        Expect.equal cy 200 "CenterY should match"
        Expect.floatClose Accuracy.medium (float rh) 30.0 "RadiusH should match"
        Expect.floatClose Accuracy.medium (float rv) 15.0 "RadiusV should match"
        Expect.equal c Color.Red "Color should match"
        Expect.equal l 13<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected FillEllipse"
    }

    test
      "fillRing maps center, innerR, outerR, startAngle, endAngle, segments, color, layer" {
      let center = Vector2(100.0f, 100.0f)

      let cmd =
        Command2D.fillRing
          (14<RenderLayer>, Color.Blue)
          (center, 20.0f, 50.0f, 0.0f, 180.0f, 32)

      match cmd with
      | Command2D.FillRing(c, ir, outerR, sa, ea, seg, col, l) ->
        Expect.equal c center "Center should match"
        Expect.floatClose Accuracy.medium (float ir) 20.0 "InnerR should match"

        Expect.floatClose
          Accuracy.medium
          (float outerR)
          50.0
          "OuterR should match"

        Expect.floatClose
          Accuracy.medium
          (float sa)
          0.0
          "StartAngle should match"

        Expect.floatClose
          Accuracy.medium
          (float ea)
          180.0
          "EndAngle should match"

        Expect.equal seg 32 "Segments should match"
        Expect.equal col Color.Blue "Color should match"
        Expect.equal l 14<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected FillRing"
    }

    test
      "circleSector maps center, radius, startAngle, endAngle, segments, color, layer" {
      let center = Vector2(50.0f, 50.0f)

      let cmd =
        Command2D.circleSector
          (15<RenderLayer>, Color.Green)
          (center, 40.0f, 0.0f, 90.0f, 16)

      match cmd with
      | Command2D.CircleSector(c, r, sa, ea, seg, col, l) ->
        Expect.equal c center "Center should match"
        Expect.floatClose Accuracy.medium (float r) 40.0 "Radius should match"

        Expect.floatClose
          Accuracy.medium
          (float sa)
          0.0
          "StartAngle should match"

        Expect.floatClose
          Accuracy.medium
          (float ea)
          90.0
          "EndAngle should match"

        Expect.equal seg 16 "Segments should match"
        Expect.equal col Color.Green "Color should match"
        Expect.equal l 15<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected CircleSector"
    }

    test "circleGradient maps centerX, centerY, radius, inner, outer, layer" {
      let cmd =
        Command2D.circleGradient
          (16<RenderLayer>)
          (100, 200, 50.0f, Color.Red, Color.Blue)

      match cmd with
      | Command2D.CircleGradient(cx, cy, r, inner, outer, l) ->
        Expect.equal cx 100 "CenterX should match"
        Expect.equal cy 200 "CenterY should match"
        Expect.floatClose Accuracy.medium (float r) 50.0 "Radius should match"
        Expect.equal inner Color.Red "Inner color should match"
        Expect.equal outer Color.Blue "Outer color should match"
        Expect.equal l 16<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected CircleGradient"
    }

    test "fillRectRounded maps rect, roundness, segments, color, layer" {
      let cmd =
        Command2D.fillRectRounded (17<RenderLayer>, Color.White, 0.5f, 8) rect

      match cmd with
      | Command2D.FillRectRounded(r, rnd, seg, c, l) ->
        Expect.equal r rect "Rect should match"

        Expect.floatClose
          Accuracy.medium
          (float rnd)
          0.5
          "Roundness should match"

        Expect.equal seg 8 "Segments should match"
        Expect.equal c Color.White "Color should match"
        Expect.equal l 17<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected FillRectRounded"
    }

    test
      "rectRoundedOutline maps rect, roundness, segments, thickness, color, layer" {
      let cmd =
        Command2D.rectRoundedOutline
          (18<RenderLayer>, Color.Black, 0.3f, 6, 2.0f)
          rect

      match cmd with
      | Command2D.RectRoundedOutline(r, rnd, seg, t, c, l) ->
        Expect.equal r rect "Rect should match"

        Expect.floatClose
          Accuracy.medium
          (float rnd)
          0.3
          "Roundness should match"

        Expect.equal seg 6 "Segments should match"
        Expect.floatClose Accuracy.medium (float t) 2.0 "Thickness should match"
        Expect.equal c Color.Black "Color should match"
        Expect.equal l 18<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected RectRoundedOutline"
    }

    test "rectGradientV maps x, y, w, h, top, bottom, layer" {
      let cmd =
        Command2D.rectGradientV
          (19<RenderLayer>)
          (10, 20, 100, 200, Color.Red, Color.Blue)

      match cmd with
      | Command2D.RectGradientV(x, y, w, h, top, bottom, l) ->
        Expect.equal x 10 "X should match"
        Expect.equal y 20 "Y should match"
        Expect.equal w 100 "W should match"
        Expect.equal h 200 "H should match"
        Expect.equal top Color.Red "Top color should match"
        Expect.equal bottom Color.Blue "Bottom color should match"
        Expect.equal l 19<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected RectGradientV"
    }

    test "rectGradientH maps x, y, w, h, left, right, layer" {
      let cmd =
        Command2D.rectGradientH
          (20<RenderLayer>)
          (10, 20, 100, 200, Color.Green, Color.Yellow)

      match cmd with
      | Command2D.RectGradientH(x, y, w, h, left, right, l) ->
        Expect.equal x 10 "X should match"
        Expect.equal y 20 "Y should match"
        Expect.equal w 100 "W should match"
        Expect.equal h 200 "H should match"
        Expect.equal left Color.Green "Left color should match"
        Expect.equal right Color.Yellow "Right color should match"
        Expect.equal l 20<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected RectGradientH"
    }

    test "rectGradient maps rect, tl, bl, tr, br, layer" {
      let cmd =
        Command2D.rectGradient
          (21<RenderLayer>)
          (rect, Color.Red, Color.Green, Color.Blue, Color.White)

      match cmd with
      | Command2D.RectGradient(r, tl, bl, tr, br, l) ->
        Expect.equal r rect "Rect should match"
        Expect.equal tl Color.Red "TL should match"
        Expect.equal bl Color.Green "BL should match"
        Expect.equal tr Color.Blue "TR should match"
        Expect.equal br Color.White "BR should match"
        Expect.equal l 21<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected RectGradient"
    }

    test "setBlend maps blend mode and layer" {
      let cmd = Command2D.setBlend 0<RenderLayer> BlendMode.Additive

      match cmd with
      | Command2D.SetBlend(mode, l) ->
        Expect.equal mode BlendMode.Additive "Blend mode should match"
        Expect.equal l 0<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected SetBlend"
    }

    test "setScissor maps x, y, w, h and layer" {
      let cmd = Command2D.setScissor 0<RenderLayer> (10, 20, 100, 200)

      match cmd with
      | Command2D.SetScissor(x, y, w, h, l) ->
        Expect.equal x 10 "X should match"
        Expect.equal y 20 "Y should match"
        Expect.equal w 100 "W should match"
        Expect.equal h 200 "H should match"
        Expect.equal l 0<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected SetScissor"
    }

    test "clearScissor maps layer" {
      let cmd = Command2D.clearScissor 5<RenderLayer>

      match cmd with
      | Command2D.ClearScissor l ->
        Expect.equal l 5<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected ClearScissor"
    }

    test "setLineWidth maps width and layer" {
      let cmd = Command2D.setLineWidth 0<RenderLayer> 3.5f

      match cmd with
      | Command2D.SetLineWidth(w, l) ->
        Expect.floatClose Accuracy.medium (float w) 3.5 "Width should match"
        Expect.equal l 0<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected SetLineWidth"
    }

    test "clear maps color and layer" {
      let cmd = Command2D.clear 0<RenderLayer> Color.Black

      match cmd with
      | Command2D.Clear(c, l) ->
        Expect.equal c Color.Black "Color should match"
        Expect.equal l 0<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected Clear"
    }

    test "sprite factory maps SpriteState fields correctly" {
      let state: SpriteState = {
        Texture = tex
        Dest = rect
        Source = Rectangle(0.0f, 0.0f, 64.0f, 64.0f)
        Origin = Vector2(0.5f, 0.5f)
        Rotation = 45.0f
        Color = Color.Red
        Layer = 3<RenderLayer>
        NormalMap = ValueNone
      }

      let cmd = Command2D.sprite state

      match cmd with
      | Command2D.Sprite(t, d, s, o, r, c, l) ->
        Expect.equal t tex "Texture should match"
        Expect.equal d rect "Dest should match"

        Expect.equal
          s
          (Rectangle(0.0f, 0.0f, 64.0f, 64.0f))
          "Source should match"

        Expect.equal o (Vector2(0.5f, 0.5f)) "Origin should match"
        Expect.floatClose Accuracy.medium (float r) 45.0 "Rotation should match"
        Expect.equal c Color.Red "Color should match"
        Expect.equal l 3<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected Sprite"
    }

    test "text factory maps TextState fields correctly" {
      let state: Command2D.TextState = {
        Font = font
        Text = "Hello"
        Position = v1
        FontSize = 24.0f
        Spacing = 2.0f
        Color = Color.Green
        Layer = 7<RenderLayer>
      }

      let cmd = Command2D.text state

      match cmd with
      | Command2D.Text(f, t, p, fs, sp, c, l) ->
        Expect.equal f font "Font should match"
        Expect.equal t "Hello" "Text should match"
        Expect.equal p v1 "Position should match"

        Expect.floatClose
          Accuracy.medium
          (float fs)
          24.0
          "FontSize should match"

        Expect.floatClose Accuracy.medium (float sp) 2.0 "Spacing should match"
        Expect.equal c Color.Green "Color should match"
        Expect.equal l 7<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected Text"
    }
  ]

// ──────────────────────────────────────────────
// SpriteState / TextState Builder Tests
// ──────────────────────────────────────────────

let spriteStateTests =
  testList "SpriteState builders" [
    test "create sets required fields and defaults for optional fields" {
      let dest = Rectangle(0.0f, 0.0f, 32.0f, 32.0f)
      let source = Rectangle(0.0f, 0.0f, 16.0f, 16.0f)
      let s = SpriteState.create(tex, dest, source)
      Expect.equal s.Texture tex "Texture"
      Expect.equal s.Dest dest "Dest"
      Expect.equal s.Source source "Source"
      Expect.equal s.Origin Vector2.Zero "Origin default should be Zero"

      Expect.floatClose
        Accuracy.medium
        (float s.Rotation)
        0.0
        "Rotation default should be 0"

      Expect.equal s.Color Color.White "Color default should be White"
      Expect.equal s.Layer 0<RenderLayer> "Layer default should be 0"
    }

    test "withOrigin overrides origin" {
      let s =
        SpriteState.create(tex, rect, rect)
        |> SpriteState.withOrigin(Vector2(0.5f, 0.5f))

      Expect.equal s.Origin (Vector2(0.5f, 0.5f)) "Origin should be overridden"
    }

    test "withRotation overrides rotation" {
      let s =
        SpriteState.create(tex, rect, rect) |> SpriteState.withRotation 90.0f

      Expect.floatClose
        Accuracy.medium
        (float s.Rotation)
        90.0
        "Rotation should be overridden"
    }

    test "withColor overrides color" {
      let s =
        SpriteState.create(tex, rect, rect) |> SpriteState.withColor Color.Red

      Expect.equal s.Color Color.Red "Color should be overridden"
    }

    test "withLayer overrides layer" {
      let s =
        SpriteState.create(tex, rect, rect)
        |> SpriteState.withLayer 5<RenderLayer>

      Expect.equal s.Layer 5<RenderLayer> "Layer should be overridden"
    }

    test "create defaults NormalMap to ValueNone" {
      let s = SpriteState.create(tex, rect, rect)
      Expect.isFalse s.NormalMap.IsSome "NormalMap should default to ValueNone"
    }

    test "withNormalMap sets NormalMap" {
      let s =
        SpriteState.create(tex, rect, rect)
        |> SpriteState.withNormalMap normalMapTex

      match s.NormalMap with
      | ValueSome nm -> Expect.equal nm normalMapTex "NormalMap should be set"
      | ValueNone -> Tests.failtest "Expected NormalMap to be ValueSome"
    }

    test "chained with* calls preserve earlier overrides" {
      let s =
        SpriteState.create(tex, rect, rect)
        |> SpriteState.withOrigin(Vector2(1.0f, 1.0f))
        |> SpriteState.withRotation 45.0f
        |> SpriteState.withColor Color.Blue
        |> SpriteState.withLayer 10<RenderLayer>
        |> SpriteState.withNormalMap normalMapTex

      Expect.equal s.Origin (Vector2(1.0f, 1.0f)) "Origin should persist"

      Expect.floatClose
        Accuracy.medium
        (float s.Rotation)
        45.0
        "Rotation should persist"

      Expect.equal s.Color Color.Blue "Color should persist"
      Expect.equal s.Layer 10<RenderLayer> "Layer should persist"

      match s.NormalMap with
      | ValueSome nm -> Expect.equal nm normalMapTex "NormalMap should persist"
      | ValueNone -> Tests.failtest "Expected NormalMap to persist"
    }
  ]

let textStateTests =
  testList "TextState builders" [
    test "create sets required fields and defaults for optional fields" {
      let pos = Vector2(10.0f, 20.0f)
      let s = TextState.create(font, "Hello", pos)
      Expect.equal s.Font font "Font"
      Expect.equal s.Text "Hello" "Text"
      Expect.equal s.Position pos "Position"

      Expect.floatClose
        Accuracy.medium
        (float s.FontSize)
        20.0
        "FontSize default should be 20"

      Expect.floatClose
        Accuracy.medium
        (float s.Spacing)
        1.0
        "Spacing default should be 1"

      Expect.equal s.Color Color.White "Color default should be White"
      Expect.equal s.Layer 0<RenderLayer> "Layer default should be 0"
    }

    test "withFontSize overrides font size" {
      let s = TextState.create(font, "test", v1) |> TextState.withFontSize 32.0f

      Expect.floatClose
        Accuracy.medium
        (float s.FontSize)
        32.0
        "FontSize should be overridden"
    }

    test "withSpacing overrides spacing" {
      let s = TextState.create(font, "test", v1) |> TextState.withSpacing 3.0f

      Expect.floatClose
        Accuracy.medium
        (float s.Spacing)
        3.0
        "Spacing should be overridden"
    }
  ]

// ──────────────────────────────────────────────
// RenderBuffer2D Tests
// ──────────────────────────────────────────────

let renderBuffer2DTests =
  testList "RenderBuffer2D" [
    test "new buffer has count 0" {
      let buf = RenderBuffer2D()
      Expect.equal buf.Count 0 "Empty buffer should have count 0"
    }

    test "Add increments count" {
      let buf = RenderBuffer2D()
      let cmd = Command2D.clear 0<RenderLayer> Color.Black
      buf.Add(cmd)
      Expect.equal buf.Count 1 "Count should be 1 after adding one command"
      buf.Add(cmd)
      Expect.equal buf.Count 2 "Count should be 2 after adding two commands"
    }

    test "Item returns the added command" {
      let buf = RenderBuffer2D()
      let cmd = Command2D.fillCircle (0<RenderLayer>, Color.Red) (v1, 10.0f)
      buf.Add(cmd)

      match buf.Item 0 with
      | Command2D.FillCircle(c, r, col, l) ->
        Expect.floatClose
          Accuracy.medium
          (float c.X)
          (float v1.X)
          "Center X should match"

        Expect.floatClose
          Accuracy.medium
          (float c.Y)
          (float v1.Y)
          "Center Y should match"

        Expect.floatClose Accuracy.medium (float r) 10.0 "Radius should match"
        Expect.equal col Color.Red "Color should match"
        Expect.equal l 0<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected FillCircle"
    }

    test "Clear resets count to 0" {
      let buf = RenderBuffer2D()
      buf.Add(Command2D.clear 0<RenderLayer> Color.Black)
      buf.Add(Command2D.clear 0<RenderLayer> Color.White)
      Expect.equal buf.Count 2 "Count should be 2"
      buf.Clear()
      Expect.equal buf.Count 0 "Count should be 0 after clear"
    }

    test "Sort orders commands by layer ascending" {
      let buf = RenderBuffer2D()

      let cmdHigh =
        Command2D.fillCircle (100<RenderLayer>, Color.Red) (v1, 10.0f)

      let cmdLow = Command2D.fillCircle (1<RenderLayer>, Color.Blue) (v2, 20.0f)

      let cmdMid =
        Command2D.fillCircle (50<RenderLayer>, Color.Green) (v3, 30.0f)

      buf.Add(cmdHigh)
      buf.Add(cmdLow)
      buf.Add(cmdMid)
      buf.Sort()

      match buf.Item 0 with
      | Command2D.FillCircle(_, _, c, l) ->
        Expect.equal l 1<RenderLayer> "First after sort should be layer 1"
        Expect.equal c Color.Blue "Color should be Blue for layer 1"
      | _ -> Tests.failtest "Expected FillCircle"

      match buf.Item 1 with
      | Command2D.FillCircle(_, _, _, l) ->
        Expect.equal l 50<RenderLayer> "Second should be layer 50"
      | _ -> Tests.failtest "Expected FillCircle"

      match buf.Item 2 with
      | Command2D.FillCircle(_, _, _, l) ->
        Expect.equal l 100<RenderLayer> "Third should be layer 100"
      | _ -> Tests.failtest "Expected FillCircle"
    }

    test "Sort on empty buffer does not crash" {
      let buf = RenderBuffer2D()
      buf.Sort() // Should not throw
      Expect.equal buf.Count 0 "Count should still be 0"
    }

    test "Sort with single item does not crash" {
      let buf = RenderBuffer2D()
      buf.Add(Command2D.clear 5<RenderLayer> Color.Black)
      buf.Sort()
      Expect.equal buf.Count 1 "Count should still be 1"
    }

    test "Sort handles negative layers correctly" {
      let buf = RenderBuffer2D()
      buf.Add(Command2D.clear 10<RenderLayer> Color.Black)
      buf.Add(Command2D.clear (-5<RenderLayer>) Color.White)
      buf.Add(Command2D.clear 0<RenderLayer> Color.Red)
      buf.Sort()

      match buf.Item 0 with
      | Command2D.Clear(_, l) ->
        Expect.equal l (-5<RenderLayer>) "First should be -5"
      | _ -> Tests.failtest "Expected Clear"

      match buf.Item 1 with
      | Command2D.Clear(_, l) ->
        Expect.equal l 0<RenderLayer> "Second should be 0"
      | _ -> Tests.failtest "Expected Clear"

      match buf.Item 2 with
      | Command2D.Clear(_, l) ->
        Expect.equal l 10<RenderLayer> "Third should be 10"
      | _ -> Tests.failtest "Expected Clear"
    }

    test "Sort preserves insertion order for same layer" {
      let buf = RenderBuffer2D()

      let cmdA =
        Command2D.fillCircle
          (5<RenderLayer>, Color.Red)
          (Vector2(1.0f, 0.0f), 10.0f)

      let cmdB =
        Command2D.fillCircle
          (5<RenderLayer>, Color.Blue)
          (Vector2(2.0f, 0.0f), 10.0f)

      let cmdC =
        Command2D.fillCircle
          (5<RenderLayer>, Color.Green)
          (Vector2(3.0f, 0.0f), 10.0f)

      buf.Add(cmdA)
      buf.Add(cmdB)
      buf.Add(cmdC)
      buf.Sort()

      match buf.Item 0, buf.Item 1, buf.Item 2 with
      | Command2D.FillCircle(_, _, c1, _),
        Command2D.FillCircle(_, _, c2, _),
        Command2D.FillCircle(_, _, c3, _) ->
        Expect.equal c1 Color.Red "First should be Red"
        Expect.equal c2 Color.Blue "Second should be Blue"
        Expect.equal c3 Color.Green "Third should be Green"
      | _ -> Tests.failtest "Expected FillCircle commands"
    }

    test "Buffer expands capacity when full" {
      let buf = RenderBuffer2D(capacity = 4)

      let cmds = [
        for i in 0..9 do
          Command2D.fillCircle
            (0<RenderLayer>, Color.White)
            (Vector2(float32 i, 0.0f), 1.0f)
      ]

      for cmd in cmds do
        buf.Add(cmd)

      Expect.equal
        buf.Count
        10
        "Should have 10 items after exceeding initial capacity"

      match buf.Item 9 with
      | Command2D.FillCircle(center, _, _, _) ->
        Expect.floatClose
          Accuracy.medium
          (float center.X)
          9.0
          "Last item should have X=9"
      | _ -> Tests.failtest "Expected FillCircle"
    }

    test "Clear + repopulate cycle works (frame lifecycle)" {
      let buf = RenderBuffer2D()
      // Frame 1
      buf.Add(Command2D.fillCircle (1<RenderLayer>, Color.Red) (v1, 10.0f))
      buf.Add(Command2D.fillCircle (2<RenderLayer>, Color.Blue) (v2, 20.0f))
      buf.Sort()
      Expect.equal buf.Count 2 "Frame 1 should have 2 commands"

      // Frame 2 - clear and repopulate
      buf.Clear()
      Expect.equal buf.Count 0 "After clear should be 0"
      buf.Add(Command2D.fillCircle (3<RenderLayer>, Color.Green) (v3, 30.0f))
      buf.Sort()
      Expect.equal buf.Count 1 "Frame 2 should have 1 command"

      match buf.Item 0 with
      | Command2D.FillCircle(_, _, c, l) ->
        Expect.equal c Color.Green "Should be the frame 2 command"
        Expect.equal l 3<RenderLayer> "Layer should be 3"
      | _ -> Tests.failtest "Expected FillCircle"
    }
  ]

// ──────────────────────────────────────────────
// Draw DSL Tests
// ──────────────────────────────────────────────

let drawDSLTests =
  testList "Draw DSL" [
    test "Draw functions return the same buffer for chaining" {
      let buf = RenderBuffer2D()
      let returned = buf |> Draw.fillRect (0<RenderLayer>, Color.Red) rect

      Expect.isTrue
        (obj.ReferenceEquals(buf, returned))
        "Draw should return the same buffer"
    }

    test "Draw pipeline adds commands in expected order" {
      let buf = RenderBuffer2D()

      buf
      |> Draw.fillCircle (1<RenderLayer>, Color.Red) (v1, 10.0f)
      |> Draw.fillRect (2<RenderLayer>, Color.Blue) rect
      |> Draw.line (3<RenderLayer>, Color.Green) (v1, v2)
      |> Draw.drop

      Expect.equal buf.Count 3 "Should have 3 commands"
      // Verify each command is the right type
      match buf.Item 0 with
      | Command2D.FillCircle _ -> ()
      | _ -> Tests.failtest "First should be FillCircle"

      match buf.Item 1 with
      | Command2D.FillRect _ -> ()
      | _ -> Tests.failtest "Second should be FillRect"

      match buf.Item 2 with
      | Command2D.Line _ -> ()
      | _ -> Tests.failtest "Third should be Line"
    }

    test "Draw.drop returns unit" {
      let buf = RenderBuffer2D()
      let result = Draw.drop buf
      Expect.equal result () "drop should return unit"
    }
  ]

// ──────────────────────────────────────────────
// 2D Light Type Builder Tests
// ──────────────────────────────────────────────

let lightTypeTests =
  testList "2D Light builders" [
    test "AmbientLight2D.create sets color" {
      let l = AmbientLight2D.create Color.White
      Expect.equal l.Color Color.White "Color should be White"
    }

    test "PointLight2D.create sets position, radius and defaults" {
      let pos = Vector2(100.0f, 200.0f)
      let l = PointLight2D.create(pos, 50.0f)
      Expect.equal l.Position pos "Position should match"
      Expect.equal l.Color Color.White "Default color should be White"

      Expect.floatClose
        Accuracy.medium
        (float l.Intensity)
        1.0
        "Default intensity should be 1"

      Expect.floatClose
        Accuracy.medium
        (float l.Radius)
        50.0
        "Radius should match"

      Expect.floatClose
        Accuracy.medium
        (float l.Falloff)
        2.0
        "Default falloff should be 2"

      Expect.isFalse l.CastsShadows "Default CastsShadows should be false"
    }

    test "PointLight2D with* functions override fields" {
      let l =
        PointLight2D.create(v1, 10.0f)
        |> PointLight2D.withColor Color.Red
        |> PointLight2D.withIntensity 0.5f
        |> PointLight2D.withFalloff 1.0f
        |> PointLight2D.withCastsShadows true

      Expect.equal l.Color Color.Red "Color should be overridden"

      Expect.floatClose
        Accuracy.medium
        (float l.Intensity)
        0.5
        "Intensity should be overridden"

      Expect.floatClose
        Accuracy.medium
        (float l.Falloff)
        1.0
        "Falloff should be overridden"

      Expect.isTrue l.CastsShadows "CastsShadows should be overridden"
    }

    test "DirectionalLight2D.create sets direction and defaults" {
      let dir = Vector2(0.3f, -0.7f)
      let l = DirectionalLight2D.create dir
      Expect.equal l.Direction dir "Direction should match"
      Expect.equal l.Color Color.White "Default color should be White"

      Expect.floatClose
        Accuracy.medium
        (float l.Intensity)
        1.0
        "Default intensity should be 1"

      Expect.isTrue l.CastsShadows "Default CastsShadows should be true"
    }

    test "DirectionalLight2D with* functions override fields" {
      let l =
        DirectionalLight2D.create v1
        |> DirectionalLight2D.withColor Color.Blue
        |> DirectionalLight2D.withIntensity 0.8f
        |> DirectionalLight2D.withCastsShadows false

      Expect.equal l.Color Color.Blue "Color should be overridden"

      Expect.floatClose
        Accuracy.medium
        (float l.Intensity)
        0.8
        "Intensity should be overridden"

      Expect.isFalse l.CastsShadows "CastsShadows should be overridden"
    }

    test "Occluder2D.create sets P1 and P2" {
      let o = Occluder2D.create(v1, v2)
      Expect.equal o.P1 v1 "P1 should match"
      Expect.equal o.P2 v2 "P2 should match"
    }
  ]

// ──────────────────────────────────────────────
// Particle2D Builder Tests
// ──────────────────────────────────────────────

let particle2DTests =
  testList "Particle2D builders" [
    test "create sets position, size and defaults" {
      let pos = Vector2(10.0f, 20.0f)
      let size = Vector2(5.0f, 5.0f)
      let p = Particle2D.create(pos, size)
      Expect.equal p.Position pos "Position should match"
      Expect.equal p.Size size "Size should match"

      Expect.floatClose
        Accuracy.medium
        (float p.Rotation)
        0.0
        "Default rotation should be 0"

      Expect.equal p.Color Color.White "Default color should be White"
    }

    test "withRotation overrides rotation" {
      let p = Particle2D.create(v1, v2) |> Particle2D.withRotation 90.0f

      Expect.floatClose
        Accuracy.medium
        (float p.Rotation)
        90.0
        "Rotation should be overridden"
    }

    test "withColor overrides color" {
      let p = Particle2D.create(v1, v2) |> Particle2D.withColor Color.Red
      Expect.equal p.Color Color.Red "Color should be overridden"
    }

    test "withSourceRect overrides source rect" {
      let src = Rectangle(0.0f, 0.0f, 32.0f, 32.0f)
      let p = Particle2D.create(v1, v2) |> Particle2D.withSourceRect src
      Expect.equal p.SourceRect src "SourceRect should be overridden"
    }
  ]

// ──────────────────────────────────────────────
// ParticleSimulation Tests
// ──────────────────────────────────────────────

let particleSimulationTests =
  testList "ParticleSimulation" [
    test "fadeAndCompact with empty array keeps count 0" {
      let particles = Array.zeroCreate<Particle2D> 10
      let mutable count = 0
      ParticleSimulation.fadeAndCompact particles &count 100.0f 0.016f
      Expect.equal count 0 "Count should remain 0"
    }

    test "fadeAndCompact reduces alpha by fadeSpeed * dt" {
      let particles = [|
        {
          Particle2D.create(v1, v2) with
              Color = Color(255uy, 255uy, 255uy, 255uy)
        }
      |]

      let mutable count = 1
      // fadeSpeed=100, dt=0.5 → fadeAmount=50
      ParticleSimulation.fadeAndCompact particles &count 100.0f 0.5f
      Expect.equal count 1 "Particle should survive"

      Expect.equal
        particles[0].Color.A
        (byte(255.0f - 50.0f))
        "Alpha should be reduced by 50"
    }

    test "fadeAndCompact removes dead particles (alpha <= 0)" {
      let particles = [|
        {
          Particle2D.create(v1, v2) with
              Color = Color(255uy, 255uy, 255uy, 10uy)
        }
      |]

      let mutable count = 1
      // fadeSpeed=1000, dt=0.1 → fadeAmount=100, alpha 10 → 0 → removed
      ParticleSimulation.fadeAndCompact particles &count 1000.0f 0.1f
      Expect.equal count 0 "Dead particle should be removed"
    }

    test "fadeAndCompact compacts correctly with mixed alive/dead" {
      let particles = [|
        {
          Particle2D.create(v1, v2) with
              Color = Color(255uy, 0uy, 0uy, 10uy)
        } // will die (alpha 10, fade 100)
        {
          Particle2D.create(v2, v2) with
              Color = Color(0uy, 255uy, 0uy, 200uy)
        } // will survive
        {
          Particle2D.create(v3, v2) with
              Color = Color(0uy, 0uy, 255uy, 5uy)
        } // will die
        {
          Particle2D.create(v1, v3) with
              Color = Color(255uy, 255uy, 0uy, 255uy)
        }
      |] // will survive

      let mutable count = 4
      // fadeSpeed=100, dt=1.0 → fadeAmount=100
      ParticleSimulation.fadeAndCompact particles &count 100.0f 1.0f
      Expect.equal count 2 "Two particles should survive"
      // First survivor should be the green one (originally index 1)
      Expect.equal particles[0].Color.G 255uy "First survivor should be green"
      // Second survivor should be the yellow one (originally index 3)
      Expect.equal particles[1].Color.R 255uy "Second survivor should be yellow"
      Expect.equal particles[1].Color.G 255uy "Second survivor should be yellow"
    }

    test "fadeAndCompact with fadeSpeed=0 does not change alpha" {
      let particles = [|
        {
          Particle2D.create(v1, v2) with
              Color = Color(255uy, 255uy, 255uy, 128uy)
        }
      |]

      let mutable count = 1
      ParticleSimulation.fadeAndCompact particles &count 0.0f 1.0f
      Expect.equal count 1 "Particle should survive"
      Expect.equal particles[0].Color.A 128uy "Alpha should be unchanged"
    }

    test "fadeAndCompact preserves particle position and size" {
      let pos = Vector2(42.0f, 99.0f)
      let size = Vector2(8.0f, 12.0f)

      let particles = [|
        {
          Particle2D.create(pos, size) with
              Color = Color(255uy, 255uy, 255uy, 200uy)
        }
      |]

      let mutable count = 1
      ParticleSimulation.fadeAndCompact particles &count 50.0f 0.5f
      Expect.equal particles[0].Position pos "Position should be preserved"
      Expect.equal particles[0].Size size "Size should be preserved"
    }

    test "fadeAndCompact clamps alpha to 0, never negative" {
      let particles = [|
        {
          Particle2D.create(v1, v2) with
              Color = Color(255uy, 255uy, 255uy, 1uy)
        }
      |]

      let mutable count = 1
      ParticleSimulation.fadeAndCompact particles &count 10000.0f 1.0f
      Expect.equal count 0 "Particle should be removed"
    }
  ]

// ──────────────────────────────────────────────
// Renderer2DConfig Tests
// ──────────────────────────────────────────────

let renderer2DConfigTests =
  testList "Renderer2DConfig" [
    test "defaults has black clear color" {
      let cfg = Renderer2DConfig.defaults

      match cfg.ClearColor with
      | ValueSome c ->
        Expect.equal c Color.Black "Default clear color should be Black"
      | ValueNone -> Tests.failtest "Expected ValueSome for ClearColor"
    }

    test "defaults has no post-process" {
      let cfg = Renderer2DConfig.defaults

      Expect.isFalse
        cfg.PostProcess.IsSome
        "Default should have no post-process"
    }

    test "noClear has ValueNone clear color" {
      let cfg = Renderer2DConfig.noClear

      Expect.isFalse
        cfg.ClearColor.IsSome
        "noClear should have ValueNone ClearColor"
    }
  ]

// ──────────────────────────────────────────────
// GridOccluders Tests
// ──────────────────────────────────────────────

let gridOccluderTests =
  testList "GridOccluders" [
    test "empty grid produces no occluders" {
      let grid = CellGrid2D.create 3 3 (Vector2(32.0f, 32.0f)) Vector2.Zero

      let occluders =
        GridOccluders.fromCellGrid
          (fun (x: bool) -> x)
          GridOccluders.Edge.All
          grid

      Expect.equal occluders.Length 0 "Empty grid should produce no occluders"
    }

    test "single solid cell in empty grid produces 4 edges" {
      let grid = CellGrid2D.create 3 3 (Vector2(32.0f, 32.0f)) Vector2.Zero
      CellGrid2D.set 1 1 true grid
      let occluders = GridOccluders.fromCellGrid id GridOccluders.Edge.All grid
      Expect.equal occluders.Length 4 "Single cell should have 4 exposed edges"
    }

    test "two adjacent solid cells share no edge between them" {
      // Arrange: cells (0,0) and (1,0) are solid, adjacent horizontally
      let grid = CellGrid2D.create 3 3 (Vector2(32.0f, 32.0f)) Vector2.Zero
      CellGrid2D.set 0 0 true grid
      CellGrid2D.set 1 0 true grid
      let occluders = GridOccluders.fromCellGrid id GridOccluders.Edge.All grid
      // Each cell has 4 edges, but the shared right/left edge is not exposed
      // Cell (0,0): left, top, bottom exposed (right is shared)
      // Cell (1,0): right, top, bottom exposed (left is shared)
      Expect.equal
        occluders.Length
        6
        "Two adjacent cells should have 6 exposed edges"
    }

    test "vertically adjacent solid cells share no edge between them" {
      let grid = CellGrid2D.create 3 3 (Vector2(32.0f, 32.0f)) Vector2.Zero
      CellGrid2D.set 1 0 true grid
      CellGrid2D.set 1 1 true grid
      let occluders = GridOccluders.fromCellGrid id GridOccluders.Edge.All grid

      Expect.equal
        occluders.Length
        6
        "Two vertically adjacent cells should have 6 exposed edges"
    }

    test "edge filter limits which edges are generated" {
      let grid = CellGrid2D.create 3 3 (Vector2(32.0f, 32.0f)) Vector2.Zero
      CellGrid2D.set 1 1 true grid

      let occluders =
        GridOccluders.fromCellGrid id GridOccluders.Edge.Bottom grid

      Expect.equal occluders.Length 1 "Only Bottom edge should be generated"
    }

    test "edge filter with multiple flags" {
      let grid = CellGrid2D.create 3 3 (Vector2(32.0f, 32.0f)) Vector2.Zero
      CellGrid2D.set 1 1 true grid
      let edges = GridOccluders.Edge.Bottom ||| GridOccluders.Edge.Left
      let occluders = GridOccluders.fromCellGrid id edges grid

      Expect.equal
        occluders.Length
        2
        "Bottom and Left edges should be generated"
    }

    test "L-shaped arrangement produces correct edge count" {
      // L shape: (0,0), (1,0), (0,1)
      let grid = CellGrid2D.create 3 3 (Vector2(32.0f, 32.0f)) Vector2.Zero
      CellGrid2D.set 0 0 true grid
      CellGrid2D.set 1 0 true grid
      CellGrid2D.set 0 1 true grid
      let occluders = GridOccluders.fromCellGrid id GridOccluders.Edge.All grid
      // Cell (0,0): right shared with (1,0), bottom shared with (0,1) → left, top = 2
      // Cell (1,0): left shared with (0,0) → right, top, bottom = 3
      // Cell (0,1): top shared with (0,0) → left, right, bottom = 3
      Expect.equal occluders.Length 8 "L-shape should have 8 exposed edges"
    }

    test "occluder coordinates account for cell size and origin" {
      let cellSize = Vector2(64.0f, 64.0f)
      let origin = Vector2(100.0f, 200.0f)
      let grid = CellGrid2D.create 2 2 cellSize origin
      CellGrid2D.set 0 0 true grid
      let occluders = GridOccluders.fromCellGrid id GridOccluders.Edge.Top grid
      Expect.equal occluders.Length 1 "Should have 1 top edge"
      let o = occluders[0]
      // Top edge of cell (0,0): from (origin.x, origin.y) to (origin.x + cellW, origin.y)
      Expect.floatClose
        Accuracy.medium
        (float o.P1.X)
        100.0
        "P1.X should be origin.X"

      Expect.floatClose
        Accuracy.medium
        (float o.P1.Y)
        200.0
        "P1.Y should be origin.Y"

      Expect.floatClose
        Accuracy.medium
        (float o.P2.X)
        164.0
        "P2.X should be origin.X + cellW"

      Expect.floatClose
        Accuracy.medium
        (float o.P2.Y)
        200.0
        "P2.Y should be origin.Y"
    }

    test "corner solid block (2x2) produces only outer edges" {
      let grid = CellGrid2D.create 4 4 (Vector2(32.0f, 32.0f)) Vector2.Zero
      // Fill a 2x2 block in the corner
      CellGrid2D.set 0 0 true grid
      CellGrid2D.set 1 0 true grid
      CellGrid2D.set 0 1 true grid
      CellGrid2D.set 1 1 true grid
      let occluders = GridOccluders.fromCellGrid id GridOccluders.Edge.All grid
      // 2x2 block perimeter: 8 edges (top 2, bottom 2, left 2, right 2)
      Expect.equal occluders.Length 8 "2x2 block should have 8 perimeter edges"
    }

    test "platformer-style edge filter (Bottom, Left, Right) excludes top edges" {
      let grid = CellGrid2D.create 3 3 (Vector2(32.0f, 32.0f)) Vector2.Zero
      CellGrid2D.set 1 1 true grid

      let edges =
        GridOccluders.Edge.Bottom
        ||| GridOccluders.Edge.Left
        ||| GridOccluders.Edge.Right

      let occluders = GridOccluders.fromCellGrid id edges grid
      // Single cell with Bottom+Left+Right = 3 edges (no top)
      Expect.equal
        occluders.Length
        3
        "Should have 3 edges (Bottom, Left, Right)"

      // Verify no top edge is present (top edge would be horizontal at y=32)
      let topEdges =
        occluders
        |> Array.filter(fun o -> o.P1.Y = o.P2.Y && float32 o.P1.Y = 32.0f)

      Expect.equal topEdges.Length 0 "No top edge should be present"
    }
  ]

// ──────────────────────────────────────────────
// LightContext2D mutation tests (no GPU needed)
// ──────────────────────────────────────────────

let lightContextTests =
  testList "LightContext2D accumulation" [
    test "Reset clears all accumulated state" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)
      ctx.Ambient <- Color(100uy, 100uy, 100uy, 255uy)
      ctx.DirLights.Add(DirectionalLight2D.create v1)
      ctx.PointLights.Add(PointLight2D.create(v2, 50.0f))
      ctx.Occluders.Add(Occluder2D.create(v1, v2))
      ctx.Reset()
      Expect.equal ctx.DirLights.Count 0 "DirLights should be empty after reset"

      Expect.equal
        ctx.PointLights.Count
        0
        "PointLights should be empty after reset"

      Expect.equal ctx.Occluders.Count 0 "Occluders should be empty after reset"
      Expect.isFalse ctx.ShaderActive "ShaderActive should be false after reset"
      Expect.isTrue ctx.UniformsDirty "UniformsDirty should be true after reset"

      Expect.isTrue
        ctx.ShadowsEnabled
        "ShadowsEnabled should be true after reset"
    }

    test "ShadowsEnabled can be toggled" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)
      Expect.isTrue ctx.ShadowsEnabled "Default should be true"
      ctx.ShadowsEnabled <- false
      Expect.isFalse ctx.ShadowsEnabled "Should be false after setting"
    }

    test "Ambient property can be set and retrieved" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)
      ctx.Ambient <- Color.Red
      Expect.equal ctx.Ambient Color.Red "Ambient should be Red"
    }
  ]

// ──────────────────────────────────────────────
// LightCommands mutation tests
// ──────────────────────────────────────────────

let lightCommandTests =
  testList "LightCommands" [
    test "setAmbient mutates context and returns NoopLight" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)
      let ambient = AmbientLight2D.create Color.White
      let cmd = LightCommands.setAmbient ctx (5<RenderLayer>, ambient)
      Expect.equal ctx.Ambient Color.White "Ambient should be set to White"

      match cmd with
      | Command2D.NoopLight l ->
        Expect.equal l 5<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected NoopLight"
    }

    test "addPointLight adds to context and returns NoopLight" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)
      let light = PointLight2D.create(v1, 50.0f)
      let cmd = LightCommands.addPointLight ctx 3<RenderLayer> light
      Expect.equal ctx.PointLights.Count 1 "Should have 1 point light"
      Expect.equal ctx.PointLights[0].Position v1 "Position should match"

      match cmd with
      | Command2D.NoopLight l ->
        Expect.equal l 3<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected NoopLight"
    }

    test "addDirectionalLight adds to context and returns NoopLight" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)
      let light = DirectionalLight2D.create v2
      let cmd = LightCommands.addDirectionalLight ctx 4<RenderLayer> light
      Expect.equal ctx.DirLights.Count 1 "Should have 1 directional light"
      Expect.equal ctx.DirLights[0].Direction v2 "Direction should match"

      match cmd with
      | Command2D.NoopLight l ->
        Expect.equal l 4<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected NoopLight"
    }

    test "addOccluder adds to context and returns NoopLight" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)
      let occ = Occluder2D.create(v1, v2)
      let cmd = LightCommands.addOccluder ctx 2<RenderLayer> occ
      Expect.equal ctx.Occluders.Count 1 "Should have 1 occluder"

      match cmd with
      | Command2D.NoopLight l ->
        Expect.equal l 2<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected NoopLight"
    }

    test "endLighting returns EndLighting command" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)
      let cmd = LightCommands.endLighting ctx 10<RenderLayer>

      match cmd with
      | Command2D.EndLighting(_, l) ->
        Expect.equal l 10<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected EndLighting"
    }

    test "enableShadows sets ShadowsEnabled and returns EnableShadows" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)
      ctx.ShadowsEnabled <- false
      let cmd = LightCommands.enableShadows ctx 1<RenderLayer>
      Expect.isTrue ctx.ShadowsEnabled "ShadowsEnabled should be true"

      match cmd with
      | Command2D.EnableShadows(_, l) ->
        Expect.equal l 1<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected EnableShadows"
    }

    test "disableShadows clears ShadowsEnabled and returns DisableShadows" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)
      let cmd = LightCommands.disableShadows ctx 1<RenderLayer>
      Expect.isFalse ctx.ShadowsEnabled "ShadowsEnabled should be false"

      match cmd with
      | Command2D.DisableShadows(_, l) ->
        Expect.equal l 1<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected DisableShadows"
    }

    test "light commands can be added to RenderBuffer2D via LightDraw" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)
      let buf = RenderBuffer2D()

      buf
      |> LightDraw.setAmbient
        ctx
        (0<RenderLayer>, AmbientLight2D.create Color.White)
      |> LightDraw.addPointLight
        ctx
        1<RenderLayer>
        (PointLight2D.create(v1, 100.0f))
      |> LightDraw.addDirectionalLight
        ctx
        2<RenderLayer>
        (DirectionalLight2D.create v2)
      |> LightDraw.addOccluder ctx 3<RenderLayer> (Occluder2D.create(v1, v2))
      |> LightDraw.endLighting ctx 10<RenderLayer>
      |> Draw.drop

      Expect.equal buf.Count 5 "Should have 5 commands"

      Expect.equal
        ctx.PointLights.Count
        1
        "Should have 1 point light accumulated"

      Expect.equal
        ctx.DirLights.Count
        1
        "Should have 1 directional light accumulated"

      Expect.equal ctx.Occluders.Count 1 "Should have 1 occluder accumulated"
    }

    test "litSprite with no normal map creates LitSprite with ValueNone" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)

      let sprite =
        SpriteState.create(tex, rect, rect)
        |> SpriteState.withLayer 5<RenderLayer>

      let cmd = LightCommands.litSprite ctx sprite

      match cmd with
      | Command2D.LitSprite(_, s) ->
        Expect.equal s.Texture tex "Texture should match"
        Expect.isFalse s.NormalMap.IsSome "NormalMap should be ValueNone"
        Expect.equal s.Layer 5<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected LitSprite"
    }

    test "litSprite with normal map creates LitSprite with ValueSome" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)

      let sprite =
        SpriteState.create(tex, rect, rect)
        |> SpriteState.withNormalMap normalMapTex
        |> SpriteState.withLayer 3<RenderLayer>

      let cmd = LightCommands.litSprite ctx sprite

      match cmd with
      | Command2D.LitSprite(_, s) ->
        Expect.equal s.Texture tex "Texture should match"

        match s.NormalMap with
        | ValueSome nm -> Expect.equal nm normalMapTex "NormalMap should match"
        | ValueNone -> Tests.failtest "Expected NormalMap to be ValueSome"

        Expect.equal s.Layer 3<RenderLayer> "Layer should match"
      | _ -> Tests.failtest "Expected LitSprite"
    }

    test "litAnimatedSprite extracts sheet texture and normal map" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)

      let sheet =
        SpriteSheet.fromFrames tex (Vector2(16.0f, 16.0f)) [|
          struct ("idle",
                  {
                    Frames = [| rect |]
                    FrameDuration = 1.0f
                    Loop = false
                  })
        |]
        |> SpriteSheet.withNormalMap normalMapTex

      let anim = AnimatedSprite.create sheet "idle"
      let dest = Rectangle(10.0f, 20.0f, 32.0f, 32.0f)
      let cmd = LightCommands.litAnimatedSprite ctx 7<RenderLayer> dest anim

      match cmd with
      | Command2D.LitSprite(_, s) ->
        Expect.equal s.Texture tex "Texture should match sheet texture"
        Expect.equal s.Dest dest "Dest should match"
        Expect.equal s.Layer 7<RenderLayer> "Layer should match"

        match s.NormalMap with
        | ValueSome nm ->
          Expect.equal nm normalMapTex "NormalMap should match sheet normal map"
        | ValueNone -> Tests.failtest "Expected NormalMap from sheet"
      | _ -> Tests.failtest "Expected LitSprite"
    }

    test "litAnimatedSprite respects FlipX" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)

      let sheet =
        SpriteSheet.fromFrames tex (Vector2(16.0f, 16.0f)) [|
          struct ("walk",
                  {
                    Frames = [| rect |]
                    FrameDuration = 0.1f
                    Loop = true
                  })
        |]

      let anim = AnimatedSprite.create sheet "walk" |> AnimatedSprite.flipX
      let dest = Rectangle(10.0f, 20.0f, 32.0f, 32.0f)
      let cmd = LightCommands.litAnimatedSprite ctx 5<RenderLayer> dest anim

      match cmd with
      | Command2D.LitSprite(_, s) ->
        Expect.isTrue
          (s.Source.Width < 0.0f)
          "Source width should be negative for FlipX"
      | _ -> Tests.failtest "Expected LitSprite"
    }

    test "LightDraw.litSprite works with normal map via pipe" {
      let shader = Unchecked.defaultof<Shader>
      use ctx = new LightContext2D(litShader = shader)
      let buf = RenderBuffer2D()

      let sprite =
        SpriteState.create(tex, rect, rect)
        |> SpriteState.withNormalMap normalMapTex
        |> SpriteState.withLayer 4<RenderLayer>

      buf |> LightDraw.litSprite ctx sprite |> Draw.drop

      Expect.equal buf.Count 1 "Should have 1 command"

      match buf[0] with
      | Command2D.LitSprite(_, s) ->
        match s.NormalMap with
        | ValueSome nm -> Expect.equal nm normalMapTex "NormalMap should match"
        | ValueNone -> Tests.failtest "Expected NormalMap"
      | _ -> Tests.failtest "Expected LitSprite"
    }
  ]

// ──────────────────────────────────────────────
// All tests
// ──────────────────────────────────────────────

[<Tests>]
let tests =
  testList "Graphics2D" [
    commandFactoryTests
    spriteStateTests
    textStateTests
    renderBuffer2DTests
    drawDSLTests
    lightTypeTests
    particle2DTests
    particleSimulationTests
    renderer2DConfigTests
    gridOccluderTests
    lightContextTests
    lightCommandTests
  ]
