namespace Mibo.Elmish.Next.Graphics2D

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Next
open Mibo.Elmish.Next.Graphics2D

type Renderer2D<'Model>
  (
    view: GameContext -> 'Model -> RenderBuffer2D -> unit,
    clearColor: Color voption
  ) =

  let buffer = new RenderBuffer2D()
  let mutable spriteBatch: SpriteBatch voption = ValueNone
  let mutable pixel: Texture2D voption = ValueNone
  let mutable circleTex: Texture2D voption = ValueNone
  let mutable rasterDefault: RasterizerState voption = ValueNone
  let mutable rasterScissor: RasterizerState voption = ValueNone

  let mutable batchActive = false
  let mutable activeEffect: Effect voption = ValueNone
  let mutable activeBlend: BlendState = BlendState.AlphaBlend
  let mutable activeTransform = Matrix.Identity
  let mutable scissorRect = Rectangle.Empty

  let initResources(ctx: GameContext) =
    match spriteBatch with
    | ValueSome _ -> ()
    | ValueNone ->
      let gd = MonoGameGameContext.getGraphicsDevice ctx
      spriteBatch <- ValueSome(new SpriteBatch(gd))
      let px = new Texture2D(gd, 1, 1)
      px.SetData([| Color.White |])
      pixel <- ValueSome px
      let size = 64
      let ct = new Texture2D(gd, size, size)
      let data = Array.create (size * size) Color.Transparent
      let center = float32 size / 2.0f
      let radius = center - 0.5f

      for y = 0 to size - 1 do
        for x = 0 to size - 1 do
          let dx = float32 x - center + 0.5f
          let dy = float32 y - center + 0.5f

          if dx * dx + dy * dy <= radius * radius then
            data[y * size + x] <- Color.White

      ct.SetData(data)
      circleTex <- ValueSome ct
      rasterDefault <- ValueSome(new RasterizerState())
      rasterScissor <- ValueSome(new RasterizerState(ScissorTestEnable = true))

  let getPixel() = pixel.Value
  let getCircle() = circleTex.Value

  let getRasterizer() =
    if scissorRect <> Rectangle.Empty then
      rasterScissor.Value
    else
      rasterDefault.Value

  let drawLine
    (sb: SpriteBatch)
    (start: System.Numerics.Vector2)
    (finish: System.Numerics.Vector2)
    (thickness: int)
    (color: Color)
    =
    let diff = finish - start
    let len = diff.Length()

    if len > 0.5f then
      let angle = atan2 diff.Y diff.X

      sb.Draw(
        getPixel(),
        Rectangle(int start.X, int start.Y, int len, max 1 thickness),
        Nullable(Rectangle(0, 0, 1, 1)),
        color,
        angle,
        Vector2.Zero,
        SpriteEffects.None,
        0.0f
      )

  let flush() =
    match spriteBatch with
    | ValueSome sb when batchActive ->
      sb.End()
      batchActive <- false
    | _ -> ()

  let beginBatch(effect: Effect, blend: BlendState, transform: Matrix) =
    match spriteBatch with
    | ValueSome sb ->
      sb.Begin(
        SpriteSortMode.Deferred,
        blend,
        SamplerState.PointClamp,
        null,
        getRasterizer(),
        effect,
        transform
      )

      batchActive <- true
    | _ -> ()

  let ensureActive
    (effect: Effect voption, blend: BlendState, transform: Matrix)
    =
    if
      batchActive
      && (activeEffect <> effect
          || activeBlend <> blend
          || activeTransform <> transform)
    then
      flush()

    if not batchActive then
      let fx =
        match effect with
        | ValueSome e -> e
        | ValueNone -> null

      beginBatch(fx, blend, transform)
      activeEffect <- effect
      activeBlend <- blend
      activeTransform <- transform

  let handleLitSprite
    (
      hCtx: int<LightContext>,
      hTex: int<Texture>,
      dest: Rect,
      source: Rect,
      origin: System.Numerics.Vector2,
      rotation: float32,
      color: Color,
      hNorm: int<Texture> voption
    ) =
    let lightCtx = buffer.LightContexts.Resolve hCtx
    let tex = buffer.Textures.Resolve hTex

    let effect =
      match hNorm with
      | ValueSome _ -> lightCtx.NormalMapShader
      | ValueNone -> lightCtx.Shader

    if lightCtx.UniformsDirty then
      lightCtx.UploadUniforms()
      lightCtx.UniformsDirty <- false

    match hNorm with
    | ValueSome hNm ->
      effect.Parameters["NormalMap"].SetValue(buffer.Textures.Resolve hNm)
    | ValueNone -> ()

    flush()
    beginBatch(effect, activeBlend, activeTransform)
    lightCtx.ShaderActive <- true

    match spriteBatch with
    | ValueSome sb ->
      sb.Draw(
        tex,
        Convert.toMgRect dest,
        Nullable(Convert.toMgRect source),
        color,
        rotation,
        Convert.toMgVec2 origin,
        SpriteEffects.None,
        0.0f
      )
    | _ -> ()

  let handleEndLighting(hCtx: int<LightContext>) =
    let lightCtx = buffer.LightContexts.Resolve hCtx

    if lightCtx.ShaderActive then
      flush()
      lightCtx.ShaderActive <- false
      lightCtx.UniformsDirty <- true

  let gd(ctx: GameContext) =
    MonoGameGameContext.getGraphicsDevice ctx

  interface IRenderer<'Model> with
    member _.Draw(ctx, model, _gameTime) =
      initResources ctx
      let device = gd ctx
      let sb = spriteBatch.Value
      buffer.Clear()
      view ctx model buffer
      buffer.Sort()

      match clearColor with
      | ValueSome c -> device.Clear(c)
      | ValueNone -> ()

      for i = 0 to buffer.Count - 1 do
        match buffer[i] with
        // ── Sprite & Text ──────────────────────────────────────
        | Command2D.Sprite(hTex, dest, source, origin, rotation, color, _) ->
          ensureActive(ValueNone, activeBlend, activeTransform)

          sb.Draw(
            buffer.Textures.Resolve hTex,
            Convert.toMgRect dest,
            Nullable(Convert.toMgRect source),
            Convert.toMgColor color,
            rotation,
            Convert.toMgVec2 origin,
            SpriteEffects.None,
            0.0f
          )

        | Command2D.Text(hFont, text, position, _fontSize, _spacing, color, _) ->
          ensureActive(ValueNone, activeBlend, activeTransform)

          sb.DrawString(
            buffer.Fonts.Resolve hFont,
            text,
            Convert.toMgVec2 position,
            Convert.toMgColor color
          )

        // ── Rectangles ─────────────────────────────────────────
        | Command2D.FillRect(rect, color, _) ->
          ensureActive(ValueNone, activeBlend, activeTransform)

          sb.Draw(
            getPixel(),
            Convert.toMgRect rect,
            Nullable(Rectangle(0, 0, 1, 1)),
            Convert.toMgColor color
          )

        | Command2D.RectOutline(rect, thickness, color, _) ->
          ensureActive(ValueNone, activeBlend, activeTransform)
          let r = Convert.toMgRect rect
          let c = Convert.toMgColor color
          let t = max 1 (int thickness)

          sb.Draw(
            getPixel(),
            Rectangle(r.X, r.Y, r.Width, t),
            Nullable(Rectangle(0, 0, 1, 1)),
            c
          )

          sb.Draw(
            getPixel(),
            Rectangle(r.X, r.Y + r.Height - t, r.Width, t),
            Nullable(Rectangle(0, 0, 1, 1)),
            c
          )

          sb.Draw(
            getPixel(),
            Rectangle(r.X, r.Y, t, r.Height),
            Nullable(Rectangle(0, 0, 1, 1)),
            c
          )

          sb.Draw(
            getPixel(),
            Rectangle(r.X + r.Width - t, r.Y, t, r.Height),
            Nullable(Rectangle(0, 0, 1, 1)),
            c
          )

        | Command2D.FillRectRounded _ -> ()
        | Command2D.RectRoundedOutline _ -> ()

        | Command2D.RectGradientV(x, y, w, h, top, bottom, _) ->
          ensureActive(ValueNone, activeBlend, activeTransform)
          let halfH = h / 2

          sb.Draw(
            getPixel(),
            Rectangle(x, y, w, halfH),
            Nullable(Rectangle(0, 0, 1, 1)),
            Convert.toMgColor top
          )

          sb.Draw(
            getPixel(),
            Rectangle(x, y + halfH, w, h - halfH),
            Nullable(Rectangle(0, 0, 1, 1)),
            Convert.toMgColor bottom
          )

        | Command2D.RectGradientH(x, y, w, h, left, right, _) ->
          ensureActive(ValueNone, activeBlend, activeTransform)
          let halfW = w / 2

          sb.Draw(
            getPixel(),
            Rectangle(x, y, halfW, h),
            Nullable(Rectangle(0, 0, 1, 1)),
            Convert.toMgColor left
          )

          sb.Draw(
            getPixel(),
            Rectangle(x + halfW, y, w - halfW, h),
            Nullable(Rectangle(0, 0, 1, 1)),
            Convert.toMgColor right
          )

        | Command2D.RectGradient _ -> ()

        // ── Circles & Ellipses ─────────────────────────────────
        | Command2D.FillCircle(center, radius, color, _) ->
          ensureActive(ValueNone, activeBlend, activeTransform)
          let d = int(radius * 2.0f)

          sb.Draw(
            getCircle(),
            Rectangle(
              int center.X - int radius,
              int center.Y - int radius,
              d,
              d
            ),
            Nullable(Rectangle(0, 0, 64, 64)),
            Convert.toMgColor color
          )

        | Command2D.CircleOutline(center, radius, color, _) ->
          ensureActive(ValueNone, activeBlend, activeTransform)
          let c = Convert.toMgColor color
          let steps = max 16 (int(radius * 0.5f))
          let stepAngle = MathF.PI * 2.0f / float32 steps

          for s = 0 to steps - 1 do
            let a1 = float32 s * stepAngle
            let a2 = float32(s + 1) * stepAngle
            let x1 = center.X + cos a1 * radius
            let y1 = center.Y + sin a1 * radius
            let x2 = center.X + cos a2 * radius
            let y2 = center.Y + sin a2 * radius
            let diff = Vector2(x2 - x1, y2 - y1)
            let len = diff.Length()
            let angle = atan2 diff.Y diff.X

            sb.Draw(
              getPixel(),
              Rectangle(int x1, int y1, int len, 1),
              Nullable(Rectangle(0, 0, 1, 1)),
              c,
              angle,
              Vector2.Zero,
              SpriteEffects.None,
              0.0f
            )

        | Command2D.CircleSector _ -> ()
        | Command2D.CircleSectorOutline _ -> ()
        | Command2D.CircleGradient _ -> ()
        | Command2D.FillRing _ -> ()
        | Command2D.RingOutline _ -> ()
        | Command2D.FillEllipse _ -> ()
        | Command2D.EllipseOutline _ -> ()

        // ── Lines & Curves ─────────────────────────────────────
        | Command2D.Line(start, finish, color, _) ->
          ensureActive(ValueNone, activeBlend, activeTransform)
          drawLine sb start finish 1 (Convert.toMgColor color)

        | Command2D.LineThick(start, finish, thickness, color, _) ->
          ensureActive(ValueNone, activeBlend, activeTransform)
          drawLine sb start finish (int thickness) (Convert.toMgColor color)

        | Command2D.LineStrip(points, color, _) ->
          ensureActive(ValueNone, activeBlend, activeTransform)
          let c = Convert.toMgColor color

          for j = 0 to points.Length - 2 do
            drawLine sb points[j] points[j + 1] 1 c

        | Command2D.Bezier _ -> ()
        | Command2D.Triangle _ -> ()
        | Command2D.TriangleFan _ -> ()
        | Command2D.TriangleStrip _ -> ()
        | Command2D.FillPoly _ -> ()
        | Command2D.PolyOutline _ -> ()

        // ── Camera (stub — matrix interop deferred) ────────────
        | Command2D.BeginCamera _ -> flush()

        | Command2D.BeginCameraConfig(config, _) ->
          flush()

          match config.Viewport with
          | ValueSome vp ->
            device.Viewport <-
              Viewport(int vp.X, int vp.Y, int vp.Width, int vp.Height)
          | ValueNone -> ()

          match config.ClearColor with
          | ValueSome c -> device.Clear(Convert.toMgColor c)
          | ValueNone -> ()

        | Command2D.EndCamera _ ->
          flush()
          activeTransform <- Matrix.Identity
          device.Viewport <- Viewport(0, 0, ctx.WindowWidth, ctx.WindowHeight)

        // ── Shader & Target ────────────────────────────────────
        | Command2D.BeginShader(hEffect, _) ->
          flush()
          activeEffect <- ValueSome(buffer.Shaders.Resolve hEffect)

        | Command2D.EndShader _ ->
          flush()
          activeEffect <- ValueNone

        | Command2D.BeginTarget(hTarget, _) ->
          flush()
          device.SetRenderTarget(buffer.RenderTargets.Resolve hTarget)

        | Command2D.EndTarget _ ->
          flush()
          device.SetRenderTarget(null)

        // ── Render State ───────────────────────────────────────
        | Command2D.SetBlend(mode, _) ->
          flush()
          activeBlend <- Convert.toMgBlendState mode

        | Command2D.SetScissor(x, y, w, h, _) ->
          flush()
          scissorRect <- Rectangle(x, y, w, h)

        | Command2D.ClearScissor _ ->
          flush()
          scissorRect <- Rectangle.Empty

        | Command2D.SetLineWidth _ -> ()

        | Command2D.SetViewport(x, y, w, h, _) ->
          flush()
          device.Viewport <- Viewport(x, y, w, h)

        // ── Escape Hatch ───────────────────────────────────────
        | Command2D.DrawImmediate(action, _) ->
          flush()
          action()

        | Command2D.Clear(color, _) ->
          flush()
          device.Clear(Convert.toMgColor color)

        // ── Lighting ───────────────────────────────────────────
        | Command2D.NoopLight _ -> ()

        | Command2D.LitSprite(hCtx,
                              hTex,
                              dest,
                              source,
                              origin,
                              rotation,
                              color,
                              hNorm,
                              _) ->
          handleLitSprite(
            hCtx,
            hTex,
            dest,
            source,
            origin,
            rotation,
            Convert.toMgColor color,
            hNorm
          )

        | Command2D.EndLighting(hCtx, _) -> handleEndLighting hCtx

        | Command2D.EnableShadows(hCtx, _) ->
          buffer.LightContexts.Resolve(hCtx).UniformsDirty <- true

        | Command2D.DisableShadows(hCtx, _) ->
          buffer.LightContexts.Resolve(hCtx).UniformsDirty <- true

        // ── Particles ──────────────────────────────────────────
        | Command2D.Particle(hTex, pData, count, _) ->
          ensureActive(ValueNone, activeBlend, activeTransform)
          let tex = buffer.Textures.Resolve hTex

          for j = 0 to count - 1 do
            let p = pData[j]
            let halfW = p.Size.X * 0.5f
            let halfH = p.Size.Y * 0.5f

            sb.Draw(
              tex,
              Rectangle(
                int(p.Position.X - halfW),
                int(p.Position.Y - halfH),
                int p.Size.X,
                int p.Size.Y
              ),
              Nullable(Convert.toMgRect p.SourceRect),
              Convert.toMgColor p.Color,
              p.Rotation,
              Vector2(halfW, halfH),
              SpriteEffects.None,
              0.0f
            )

      flush()
      buffer.ParticlePool.ReturnAll()

  interface IDisposable with
    member _.Dispose() =
      spriteBatch |> ValueOption.iter(fun sb -> sb.Dispose())
      pixel |> ValueOption.iter(fun px -> px.Dispose())
      circleTex |> ValueOption.iter(fun ct -> ct.Dispose())
      rasterDefault |> ValueOption.iter(fun r -> r.Dispose())
      rasterScissor |> ValueOption.iter(fun r -> r.Dispose())
      buffer.Dispose()

module Renderer2D =

  let create
    (view: GameContext -> 'Model -> RenderBuffer2D -> unit)
    : IRenderer<'Model> =
    new Renderer2D<'Model>(view, ValueSome Color.Black) :> IRenderer<'Model>

  let createWith
    (clearColor: Color voption)
    (view: GameContext -> 'Model -> RenderBuffer2D -> unit)
    : IRenderer<'Model> =
    new Renderer2D<'Model>(view, clearColor) :> IRenderer<'Model>
