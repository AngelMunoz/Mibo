namespace Mibo.Elmish.Graphics2D

open System
open System.Buffers

/// <summary>
/// An allocation-free buffer for 2D render commands, sorted by layer.
/// </summary>
/// <remarks>
/// Commands are accumulated each frame via <see cref="M:Mibo.Elmish.Graphics2D.RenderBuffer2D.Add"/>,
/// sorted by layer, then executed in order via pattern matching.
/// Uses <see cref="T:System.Buffers.ArrayPool`1"/> for the backing store to avoid per-frame
/// heap allocations.
///
/// The buffer is designed to be cleared and repopulated each frame.
/// <see cref="M:Mibo.Elmish.Graphics2D.RenderBuffer2D.Clear"/> resets the count
/// without deallocating the internal array.
/// </remarks>
type RenderBuffer2D
  (
  /// <summary>Initial capacity. Defaults to 1024 if not specified.</summary>
  ?capacity: int) =

  let initialCapacity = defaultArg capacity 1024
  let mutable items = ArrayPool<Command2D>.Shared.Rent(initialCapacity)
  let mutable keys = ArrayPool<int64>.Shared.Rent(initialCapacity)
  let mutable count = 0
  let mutable clearCounter = 0
  let mutable postProcessCount = 0

  let getLayer(cmd: Command2D) =
    match cmd with
    | Command2D.Sprite(_, _, _, _, _, _, layer) -> layer
    | Command2D.Text(_, _, _, _, _, layer) -> layer
    // Rectangles
    | Command2D.FillRect(_, _, layer) -> layer
    | Command2D.RectOutline(_, _, _, layer) -> layer
    | Command2D.FillRectRounded(_, _, _, _, layer) -> layer
    | Command2D.RectRoundedOutline(_, _, _, _, _, layer) -> layer
    | Command2D.RectGradientV(_, _, _, _, _, _, layer) -> layer
    | Command2D.RectGradientH(_, _, _, _, _, _, layer) -> layer
    | Command2D.RectGradient(_, _, _, _, _, layer) -> layer
    // Circles & Ellipses
    | Command2D.FillCircle(_, _, _, layer) -> layer
    | Command2D.CircleOutline(_, _, _, layer) -> layer
    | Command2D.CircleSector(_, _, _, _, _, _, layer) -> layer
    | Command2D.CircleSectorOutline(_, _, _, _, _, _, layer) -> layer
    | Command2D.CircleGradient(_, _, _, _, _, layer) -> layer
    | Command2D.FillRing(_, _, _, _, _, _, _, layer) -> layer
    | Command2D.RingOutline(_, _, _, _, _, _, _, layer) -> layer
    | Command2D.FillEllipse(_, _, _, _, _, layer) -> layer
    | Command2D.EllipseOutline(_, _, _, _, _, layer) -> layer
    // Lines & Curves
    | Command2D.Line(_, _, _, layer) -> layer
    | Command2D.LineThick(_, _, _, _, layer) -> layer
    | Command2D.LineStrip(_, _, layer) -> layer
    | Command2D.Bezier(_, _, _, _, _, layer) -> layer
    // Triangles & Polygons
    | Command2D.Triangle(_, _, _, _, layer) -> layer
    | Command2D.TriangleFan(_, _, layer) -> layer
    | Command2D.TriangleStrip(_, _, layer) -> layer
    | Command2D.FillPoly(_, _, _, _, _, layer) -> layer
    | Command2D.PolyOutline(_, _, _, _, _, _, layer) -> layer
    // Camera, Targets, Shaders, State
    | Command2D.BeginCamera(_, layer) -> layer
    | Command2D.BeginCameraConfig(_, layer) -> layer
    | Command2D.EndCamera layer -> layer
    | Command2D.BeginShader(_, layer) -> layer
    | Command2D.EndShader layer -> layer
    | Command2D.BeginTarget(_, layer) -> layer
    | Command2D.EndTarget layer -> layer
    | Command2D.SetBlend(_, layer) -> layer
    | Command2D.SetSamplerState(_, layer) -> layer
    | Command2D.SetScissor(_, _, _, _, layer) -> layer
    | Command2D.ClearScissor layer -> layer
    | Command2D.SetLineWidth(_, layer) -> layer
    | Command2D.SetViewport(_, _, _, _, layer) -> layer
    // Escape Hatches
    | Command2D.DrawImmediate(_, layer) -> layer
    | Command2D.Clear(_, layer) -> layer
    // Lighting
    | Command2D.NoopLight layer -> layer
    | Command2D.LitSprite(_, sprite) -> sprite.Layer
    | Command2D.EndLighting(_, layer) -> layer
    | Command2D.EnableShadows(_, layer) -> layer
    | Command2D.DisableShadows(_, layer) -> layer
    | Command2D.Particle(_, _, _, layer) -> layer
    // Post-process has no layer — runs after the scene, sorted to the end (layer 0).
    | Command2D.PostProcess _ -> 0<RenderLayer>

  let ensureCapacity(needed: int) =
    if count + needed > items.Length then
      let newSize = max (items.Length * 2) (count + needed)

      let newItems = ArrayPool<Command2D>.Shared.Rent(newSize)
      let newKeys = ArrayPool<int64>.Shared.Rent(newSize)

      Array.Copy(items, newItems, count)
      Array.Copy(keys, newKeys, count)
      ArrayPool<Command2D>.Shared.Return(items, true)
      ArrayPool<int64>.Shared.Return(keys)
      items <- newItems
      keys <- newKeys

  /// <summary>The number of commands currently in the buffer.</summary>
  member _.Count = count

  /// <summary>
  /// Number of <c>PostProcess</c> commands added since the last <c>Clear</c>. Lets a renderer
  /// skip the post-process drain (and its per-frame allocation) when the view emits none.
  /// </summary>
  member _.PostProcessCount = postProcessCount

  /// <summary>Gets the command at the specified index.</summary>
  member _.Item(i: int) = items[i]

  /// <summary>Adds a render command to the buffer.</summary>
  member _.Add(cmd: Command2D) =
    ensureCapacity 1
    items[count] <- cmd
    keys[count] <- (int64(int(getLayer cmd)) <<< 32) ||| int64 count

    match cmd with
    | Command2D.PostProcess _ -> postProcessCount <- postProcessCount + 1
    | _ -> ()

    count <- count + 1

  /// <summary>
  /// Clears all commands from the buffer without deallocating the backing array.
  /// Call this at the start of each frame before populating with new commands.
  /// </summary>
  member _.Clear() =
    count <- 0
    postProcessCount <- 0
    clearCounter <- clearCounter + 1

    if clearCounter >= 300 then
      clearCounter <- 0
      Array.Clear(items, 0, items.Length)

  /// <summary>
  /// Sorts commands by layer in ascending order, preserving insertion order for same-layer commands.
  /// Uses precomputed int64 keys (layer in high 32 bits, insertion index in low 32 bits) to avoid
  /// repeated pattern matching during comparisons and guarantee stable sort.
  /// Must be called after <see cref="M:Mibo.Elmish.Graphics2D.RenderBuffer2D.Clear"/>
  /// and population, before iteration.
  /// </summary>
  member _.Sort() = Array.Sort(keys, items, 0, count)

  interface IDisposable with
    member _.Dispose() =
      if items.Length > 0 then
        let toReturnItems = items
        let toReturnKeys = keys
        items <- Array.empty<Command2D>
        keys <- Array.empty<int64>
        ArrayPool<Command2D>.Shared.Return(toReturnItems, true)
        ArrayPool<int64>.Shared.Return(toReturnKeys)


// ─────────────────────────────────────────────────────────────────────────────
// Fluent Draw DSL witnesses (backing Mibo.Elmish.Graphics.Draw).
//
// One `member inline` per Core Draw member: convert neutral types (Mibo.Color,
// System.Numerics vectors, float rects) to XNA types and construct the
// Command2D case directly — no dependency on the piped DSL or the Command2D
// builder module. Everything is inline — the layer erases at the call site.
// Augmentations must live in the buffer's own file: the SRTP solver only
// considers extension members in the type's declaration group.
// ─────────────────────────────────────────────────────────────────────────────

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open System.Numerics
open Mibo.Animation
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting
open Mibo

/// <summary>Inline XNA conversions for the witness surface.</summary>
module internal DrawWitnessConvert =

  let inline v2(v: Vector2) =
    Microsoft.Xna.Framework.Vector2.op_Implicit v

  let inline rect (x: float32) (y: float32) (w: float32) (h: float32) =
    Microsoft.Xna.Framework.Rectangle(int x, int y, int w, int h)

  let inline color(c: Color) = MonoGameColor.toMonoGameColor c

/// <summary>SRTP witnesses backing <see cref="T:Mibo.Elmish.Graphics.Draw"/> on the MonoGame 2D buffer.</summary>
type RenderBuffer2D with

  // ── Sprites & Text ──

  member inline b.AddSpriteState(state: SpriteState) =
    b.Add(
      Command2D.Sprite(
        state.Texture,
        state.Dest,
        state.Source,
        state.Origin,
        state.Rotation,
        state.Color,
        state.Layer
      )
    )

  member inline b.AddTextState(state: TextState) =
    b.Add(
      Command2D.Text(
        state.Font,
        state.Text,
        state.Position,
        state.Scale,
        state.Color,
        state.Layer
      )
    )

  /// size→Scale; spacing DROPPED (SpriteBatch has no per-draw spacing) — the
  /// documented semantic adaptation for the differing TextState shapes.
  member inline b.AddText
    (
      font: SpriteFont,
      text: string,
      position: Vector2,
      size: float32,
      _spacing: float32,
      tint: Color,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.Text(
        font,
        text,
        DrawWitnessConvert.v2 position,
        size,
        DrawWitnessConvert.color tint,
        layer
      )
    )

  // ── Rectangles ──

  member inline b.AddFillRect
    (
      x: float32,
      y: float32,
      w: float32,
      h: float32,
      color: Color,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.FillRect(
        DrawWitnessConvert.rect x y w h,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddRectOutline
    (
      x: float32,
      y: float32,
      w: float32,
      h: float32,
      color: Color,
      thickness: float32,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.RectOutline(
        DrawWitnessConvert.rect x y w h,
        thickness,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddFillRectRounded
    (
      x: float32,
      y: float32,
      w: float32,
      h: float32,
      color: Color,
      roundness: float32,
      segments: int,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.FillRectRounded(
        DrawWitnessConvert.rect x y w h,
        roundness,
        segments,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddRectRoundedOutline
    (
      x: float32,
      y: float32,
      w: float32,
      h: float32,
      color: Color,
      roundness: float32,
      segments: int,
      thickness: float32,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.RectRoundedOutline(
        DrawWitnessConvert.rect x y w h,
        roundness,
        segments,
        thickness,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddRectGradientV
    (
      x: int,
      y: int,
      w: int,
      h: int,
      top: Color,
      bottom: Color,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.RectGradientV(
        x,
        y,
        w,
        h,
        DrawWitnessConvert.color top,
        DrawWitnessConvert.color bottom,
        layer
      )
    )

  member inline b.AddRectGradientH
    (
      x: int,
      y: int,
      w: int,
      h: int,
      left: Color,
      right: Color,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.RectGradientH(
        x,
        y,
        w,
        h,
        DrawWitnessConvert.color left,
        DrawWitnessConvert.color right,
        layer
      )
    )

  member inline b.AddRectGradient
    (
      x: float32,
      y: float32,
      w: float32,
      h: float32,
      tl: Color,
      bl: Color,
      tr: Color,
      br: Color,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.RectGradient(
        DrawWitnessConvert.rect x y w h,
        DrawWitnessConvert.color tl,
        DrawWitnessConvert.color bl,
        DrawWitnessConvert.color tr,
        DrawWitnessConvert.color br,
        layer
      )
    )

  // ── Circles, Rings, Ellipses ──

  member inline b.AddFillCircle
    (center: Vector2, radius: float32, color: Color, layer: int<RenderLayer>)
    =
    b.Add(
      Command2D.FillCircle(
        DrawWitnessConvert.v2 center,
        radius,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddCircleOutline
    (center: Vector2, radius: float32, color: Color, layer: int<RenderLayer>)
    =
    b.Add(
      Command2D.CircleOutline(
        DrawWitnessConvert.v2 center,
        radius,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddCircleSector
    (
      center: Vector2,
      radius: float32,
      startAngle: float32,
      endAngle: float32,
      color: Color,
      segments: int,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.CircleSector(
        DrawWitnessConvert.v2 center,
        radius,
        startAngle,
        endAngle,
        segments,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddCircleSectorOutline
    (
      center: Vector2,
      radius: float32,
      startAngle: float32,
      endAngle: float32,
      color: Color,
      segments: int,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.CircleSectorOutline(
        DrawWitnessConvert.v2 center,
        radius,
        startAngle,
        endAngle,
        segments,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddCircleGradient
    (
      centerX: int,
      centerY: int,
      radius: float32,
      inner: Color,
      outer: Color,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.CircleGradient(
        centerX,
        centerY,
        radius,
        DrawWitnessConvert.color inner,
        DrawWitnessConvert.color outer,
        layer
      )
    )

  member inline b.AddFillRing
    (
      center: Vector2,
      innerR: float32,
      outerR: float32,
      startAngle: float32,
      endAngle: float32,
      color: Color,
      segments: int,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.FillRing(
        DrawWitnessConvert.v2 center,
        innerR,
        outerR,
        startAngle,
        endAngle,
        segments,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddRingOutline
    (
      center: Vector2,
      innerR: float32,
      outerR: float32,
      startAngle: float32,
      endAngle: float32,
      color: Color,
      segments: int,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.RingOutline(
        DrawWitnessConvert.v2 center,
        innerR,
        outerR,
        startAngle,
        endAngle,
        segments,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddFillEllipse
    (
      centerX: int,
      centerY: int,
      radiusH: float32,
      radiusV: float32,
      color: Color,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.FillEllipse(
        centerX,
        centerY,
        radiusH,
        radiusV,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddEllipseOutline
    (
      centerX: int,
      centerY: int,
      radiusH: float32,
      radiusV: float32,
      color: Color,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.EllipseOutline(
        centerX,
        centerY,
        radiusH,
        radiusV,
        DrawWitnessConvert.color color,
        layer
      )
    )

  // ── Lines & Curves ──

  member inline b.AddLine
    (start: Vector2, finish: Vector2, color: Color, layer: int<RenderLayer>)
    =
    b.Add(
      Command2D.Line(
        DrawWitnessConvert.v2 start,
        DrawWitnessConvert.v2 finish,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddLineThick
    (
      start: Vector2,
      finish: Vector2,
      color: Color,
      thickness: float32,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.LineThick(
        DrawWitnessConvert.v2 start,
        DrawWitnessConvert.v2 finish,
        thickness,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddLineStrip
    (
      points: Microsoft.Xna.Framework.Vector2[],
      color: Color,
      layer: int<RenderLayer>
    ) =
    b.Add(Command2D.LineStrip(points, DrawWitnessConvert.color color, layer))

  member inline b.AddBezier
    (
      start: Vector2,
      control: Vector2,
      finish: Vector2,
      color: Color,
      thickness: float32,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.Bezier(
        DrawWitnessConvert.v2 start,
        DrawWitnessConvert.v2 control,
        DrawWitnessConvert.v2 finish,
        thickness,
        DrawWitnessConvert.color color,
        layer
      )
    )

  // ── Triangles & Polygons ──

  member inline b.AddTriangle
    (
      v1: Vector2,
      v2: Vector2,
      v3: Vector2,
      color: Color,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.Triangle(
        DrawWitnessConvert.v2 v1,
        DrawWitnessConvert.v2 v2,
        DrawWitnessConvert.v2 v3,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddTriangleFan
    (
      points: Microsoft.Xna.Framework.Vector2[],
      color: Color,
      layer: int<RenderLayer>
    ) =
    b.Add(Command2D.TriangleFan(points, DrawWitnessConvert.color color, layer))

  member inline b.AddTriangleStrip
    (
      points: Microsoft.Xna.Framework.Vector2[],
      color: Color,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.TriangleStrip(points, DrawWitnessConvert.color color, layer)
    )

  member inline b.AddFillPoly
    (
      center: Vector2,
      sides: int,
      radius: float32,
      rotation: float32,
      color: Color,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.FillPoly(
        DrawWitnessConvert.v2 center,
        sides,
        radius,
        rotation,
        DrawWitnessConvert.color color,
        layer
      )
    )

  member inline b.AddPolyOutline
    (
      center: Vector2,
      sides: int,
      radius: float32,
      rotation: float32,
      color: Color,
      thickness: float32,
      layer: int<RenderLayer>
    ) =
    b.Add(
      Command2D.PolyOutline(
        DrawWitnessConvert.v2 center,
        sides,
        radius,
        rotation,
        thickness,
        DrawWitnessConvert.color color,
        layer
      )
    )

  // ── Camera, Shader, Target ──

  member inline b.AddBeginCamera(camera: Camera2D, layer: int<RenderLayer>) =
    b.Add(Command2D.BeginCamera(camera, layer))

  member inline b.AddBeginCameraConfig
    (config: Camera2DConfig, layer: int<RenderLayer>)
    =
    b.Add(Command2D.BeginCameraConfig(config, layer))

  member inline b.AddEndCamera(layer: int<RenderLayer>) =
    b.Add(Command2D.EndCamera layer)

  member inline b.AddBeginShader(shader: Effect, layer: int<RenderLayer>) =
    b.Add(Command2D.BeginShader(shader, layer))

  member inline b.AddEndShader(layer: int<RenderLayer>) =
    b.Add(Command2D.EndShader layer)

  member inline b.AddBeginTarget
    (target: RenderTarget2D, layer: int<RenderLayer>)
    =
    b.Add(Command2D.BeginTarget(target, layer))

  member inline b.AddEndTarget(layer: int<RenderLayer>) =
    b.Add(Command2D.EndTarget layer)

  // ── Render State ──

  member inline b.AddSetBlend(mode: BlendMode, layer: int<RenderLayer>) =
    b.Add(Command2D.SetBlend(mode, layer))

  /// MonoGame-only witness — the Core Draw.SetSamplerState member has no
  /// raylib counterpart; capability gating via witness presence.
  member inline b.AddSamplerState
    (sampler: SamplerState, layer: int<RenderLayer>)
    =
    b.Add(Command2D.SetSamplerState(sampler, layer))

  member inline b.AddSetScissor
    (x: int, y: int, w: int, h: int, layer: int<RenderLayer>)
    =
    b.Add(Command2D.SetScissor(x, y, w, h, layer))

  member inline b.AddClearScissor(layer: int<RenderLayer>) =
    b.Add(Command2D.ClearScissor layer)

  member inline b.AddSetLineWidth(width: float32, layer: int<RenderLayer>) =
    b.Add(Command2D.SetLineWidth(width, layer))

  member inline b.AddSetViewport
    (x: int, y: int, w: int, h: int, layer: int<RenderLayer>)
    =
    b.Add(Command2D.SetViewport(x, y, w, h, layer))

  // ── Escape Hatches ──

  member inline b.AddDrawImmediate
    ([<InlineIfLambda>] action: unit -> unit, layer: int<RenderLayer>)
    =
    b.Add(Command2D.DrawImmediate(action, layer))

  member inline b.AddClear(color: Color, layer: int<RenderLayer>) =
    b.Add(Command2D.Clear(DrawWitnessConvert.color color, layer))

  member inline b.AddPostProcess
    ([<InlineIfLambda>] action: PostProcessContext2D -> unit)
    =
    b.Add(Command2D.PostProcess action)

  // ── Particles ──

  member inline b.AddParticles
    (
      texture: Texture2D,
      particles: Particle2D[],
      count: int,
      layer: int<RenderLayer>
    ) =
    b.Add(Command2D.Particle(texture, particles, count, layer))

  // ── Lighting ──
  // LightCommands.fs compiles AFTER this file (LightDraw needs the buffer),
  // so the witnesses inline the (small, stable) light-command bodies instead.

  member inline b.AddSetAmbient
    (lightCtx: LightContext2D, color: Color, layer: int<RenderLayer>)
    =
    lightCtx.Ambient <- DrawWitnessConvert.color color
    b.Add(Command2D.NoopLight layer)

  member inline b.AddPointLight
    (lightCtx: LightContext2D, light: PointLight2D, layer: int<RenderLayer>)
    =
    lightCtx.PointLights.Add light
    b.Add(Command2D.NoopLight layer)

  member inline b.AddDirectionalLightState
    (
      lightCtx: LightContext2D,
      light: DirectionalLight2D,
      layer: int<RenderLayer>
    ) =
    lightCtx.DirLights.Add light
    b.Add(Command2D.NoopLight layer)

  member inline b.AddDirectionalLight
    (
      lightCtx: LightContext2D,
      direction: Vector2,
      color: Color,
      intensity: float32,
      castsShadows: bool,
      layer: int<RenderLayer>
    ) =
    lightCtx.DirLights.Add {
      Direction = DrawWitnessConvert.v2 direction
      Color = DrawWitnessConvert.color color
      Intensity = intensity
      CastsShadows = castsShadows
    }

    b.Add(Command2D.NoopLight layer)

  member inline b.AddOccluder
    (lightCtx: LightContext2D, occluder: Occluder2D, layer: int<RenderLayer>)
    =
    lightCtx.Occluders.Add occluder
    b.Add(Command2D.NoopLight layer)

  member inline b.AddLitSprite(lightCtx: LightContext2D, sprite: SpriteState) =
    b.Add(Command2D.LitSprite(lightCtx, sprite))

  member inline b.AddLitAnimatedSprite
    (
      lightCtx: LightContext2D,
      dest: Microsoft.Xna.Framework.Rectangle,
      animSprite: AnimatedSprite,
      layer: int<RenderLayer>
    ) =
    let src = AnimatedSprite.currentSource animSprite

    let src =
      if animSprite.FlipX then
        Rectangle(src.X, src.Y, -src.Width, src.Height)
      else
        src

    let src =
      if animSprite.FlipY then
        Rectangle(src.X, src.Y, src.Width, -src.Height)
      else
        src

    b.Add(
      Command2D.LitSprite(
        lightCtx,
        {
          Texture = animSprite.Sheet.Texture
          Dest = dest
          Source = src
          Origin = animSprite.Sheet.Origin
          Rotation = animSprite.Rotation
          Color = animSprite.Color
          Layer = layer
          NormalMap = animSprite.Sheet.NormalMap
        }
      )
    )

  member inline b.AddEndLighting
    (lightCtx: LightContext2D, layer: int<RenderLayer>)
    =
    b.Add(Command2D.EndLighting(lightCtx, layer))

  member inline b.AddEnableShadows
    (lightCtx: LightContext2D, layer: int<RenderLayer>)
    =
    b.Add(Command2D.EnableShadows(lightCtx, layer))

  member inline b.AddDisableShadows
    (lightCtx: LightContext2D, layer: int<RenderLayer>)
    =
    b.Add(Command2D.DisableShadows(lightCtx, layer))
