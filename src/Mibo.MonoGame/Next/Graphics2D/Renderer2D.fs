namespace Mibo.Elmish.Next.Graphics2D

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Next
open Mibo.Elmish.Next.Graphics2D

module private Geometry =

  let inline v3 (x: float32) (y: float32) = Vector3(x, y, 0.0f)

  let circleTriangles
    (center: System.Numerics.Vector2)
    (radius: float32)
    (c: Color)
    (segments: int)
    =
    let centerV = v3 center.X center.Y
    let arr = Array.zeroCreate<VertexPositionColor>(segments * 3)
    let step = MathF.PI * 2.0f / float32 segments

    for i = 0 to segments - 1 do
      let a0 = float32 i * step
      let a1 = float32(i + 1) * step
      let x0 = center.X + cos a0 * radius
      let y0 = center.Y + sin a0 * radius
      let x1 = center.X + cos a1 * radius
      let y1 = center.Y + sin a1 * radius
      arr.[i * 3] <- VertexPositionColor(centerV, c)
      arr.[i * 3 + 1] <- VertexPositionColor(v3 x0 y0, c)
      arr.[i * 3 + 2] <- VertexPositionColor(v3 x1 y1, c)

    arr

  let ringTriangles
    (center: System.Numerics.Vector2)
    (innerR: float32)
    (outerR: float32)
    (startAngle: float32)
    (endAngle: float32)
    (segments: int)
    (c: Color)
    =
    let span = endAngle - startAngle
    let steps = max 1 segments
    let step = span / float32 steps
    let arr = Array.zeroCreate<VertexPositionColor>(steps * 6)

    for i = 0 to steps - 1 do
      let a0 = startAngle + float32 i * step
      let a1 = startAngle + float32(i + 1) * step
      let c0 = cos a0
      let s0 = sin a0
      let c1 = cos a1
      let s1 = sin a1
      let idx = i * 6

      arr.[idx] <-
        VertexPositionColor(
          v3 (center.X + c0 * outerR) (center.Y + s0 * outerR),
          c
        )

      arr.[idx + 1] <-
        VertexPositionColor(
          v3 (center.X + c1 * outerR) (center.Y + s1 * outerR),
          c
        )

      arr.[idx + 2] <-
        VertexPositionColor(
          v3 (center.X + c0 * innerR) (center.Y + s0 * innerR),
          c
        )

      arr.[idx + 3] <-
        VertexPositionColor(
          v3 (center.X + c1 * outerR) (center.Y + s1 * outerR),
          c
        )

      arr.[idx + 4] <-
        VertexPositionColor(
          v3 (center.X + c1 * innerR) (center.Y + s1 * innerR),
          c
        )

      arr.[idx + 5] <-
        VertexPositionColor(
          v3 (center.X + c0 * innerR) (center.Y + s0 * innerR),
          c
        )

    arr

  let ellipseTriangles
    (centerX: int)
    (centerY: int)
    (radiusH: float32)
    (radiusV: float32)
    (c: Color)
    (segments: int)
    =
    let cx = float32 centerX
    let cy = float32 centerY
    let centerV = v3 cx cy
    let arr = Array.zeroCreate<VertexPositionColor>(segments * 3)
    let step = MathF.PI * 2.0f / float32 segments

    for i = 0 to segments - 1 do
      let a0 = float32 i * step
      let a1 = float32(i + 1) * step
      let x0 = cx + cos a0 * radiusH
      let y0 = cy + sin a0 * radiusV
      let x1 = cx + cos a1 * radiusH
      let y1 = cy + sin a1 * radiusV
      arr.[i * 3] <- VertexPositionColor(centerV, c)
      arr.[i * 3 + 1] <- VertexPositionColor(v3 x0 y0, c)
      arr.[i * 3 + 2] <- VertexPositionColor(v3 x1 y1, c)

    arr

  let polyTriangles
    (center: System.Numerics.Vector2)
    (sides: int)
    (radius: float32)
    (rotation: float32)
    (c: Color)
    =
    let segments = max 3 sides
    let arr = Array.zeroCreate<VertexPositionColor>(segments * 3)
    let step = MathF.PI * 2.0f / float32 segments

    for i = 0 to segments - 1 do
      let a0 = float32 i * step + rotation
      let a1 = float32(i + 1) * step + rotation
      let x0 = center.X + cos a0 * radius
      let y0 = center.Y + sin a0 * radius
      let x1 = center.X + cos a1 * radius
      let y1 = center.Y + sin a1 * radius
      arr.[i * 3] <- VertexPositionColor(v3 center.X center.Y, c)
      arr.[i * 3 + 1] <- VertexPositionColor(v3 x0 y0, c)
      arr.[i * 3 + 2] <- VertexPositionColor(v3 x1 y1, c)

    arr

  let bezierPoints
    (start: System.Numerics.Vector2)
    (control: System.Numerics.Vector2)
    (finish: System.Numerics.Vector2)
    (segments: int)
    =
    let arr = Array.zeroCreate<System.Numerics.Vector2>(segments + 1)
    let inv = 1.0f / float32 segments

    for i = 0 to segments do
      let t = float32 i * inv
      let u = 1.0f - t
      let p = u * u * start + 2.0f * u * t * control + t * t * finish
      arr.[i] <- p

    arr

  let roundedRect
    (rect: Mibo.Elmish.Next.Graphics2D.Rect)
    (roundness: float32)
    (c: Color)
    (segmentsPerCorner: int)
    =
    let x = rect.X
    let y = rect.Y
    let w = rect.Width
    let h = rect.Height
    let r = MathF.Min(roundness * 0.5f, MathF.Min(w * 0.5f, h * 0.5f))
    let cornerSegs = max 2 segmentsPerCorner
    let totalVerts = 4 + cornerSegs * 4
    let arr = Array.zeroCreate<VertexPositionColor>(totalVerts)
    let mutable idx = 0

    let inline add(x', y') =
      arr.[idx] <- VertexPositionColor(v3 (x + x') (y + y'), c)
      idx <- idx + 1

    let corner(cx, cy, startAngle) =
      let step = MathF.PI / 2.0f / float32 cornerSegs

      for i = 0 to cornerSegs do
        let a = startAngle + float32 i * step
        add(cx + cos a * r, cy + sin a * r)

    add(r, 0.0f)
    add(w - r, 0.0f)
    corner(w - r, r, -MathF.PI / 2.0f)
    add(w, r)
    add(w, h - r)
    corner(w - r, h - r, 0.0f)
    add(w - r, h)
    add(r, h)
    corner(r, h - r, MathF.PI / 2.0f)
    add(0.0f, h - r)
    add(0.0f, r)
    corner(r, r, MathF.PI)
    arr

  let lineTriangles
    (points: System.Numerics.Vector2[])
    (thickness: float32)
    (c: Color)
    =
    if points.Length < 2 then
      Array.empty
    else
      let halfThick = thickness * 0.5f
      let arr = Array.zeroCreate<VertexPositionColor>((points.Length - 1) * 6)
      let mutable idx = 0

      for i = 0 to points.Length - 2 do
        let a = points.[i]
        let b = points.[i + 1]
        let d = b - a
        let len = d.Length()

        if len > 0.0001f then
          let perp = System.Numerics.Vector2(-d.Y, d.X) / len * halfThick
          let p0 = a + perp
          let p1 = b + perp
          let p2 = a - perp
          let p3 = b - perp
          arr.[idx] <- VertexPositionColor(v3 p0.X p0.Y, c)
          arr.[idx + 1] <- VertexPositionColor(v3 p1.X p1.Y, c)
          arr.[idx + 2] <- VertexPositionColor(v3 p2.X p2.Y, c)
          arr.[idx + 3] <- VertexPositionColor(v3 p1.X p1.Y, c)
          arr.[idx + 4] <- VertexPositionColor(v3 p3.X p3.Y, c)
          arr.[idx + 5] <- VertexPositionColor(v3 p2.X p2.Y, c)
          idx <- idx + 6

      arr

  let triangleFanAsTriangles
    (center: System.Numerics.Vector2)
    (points: System.Numerics.Vector2[])
    (c: Color)
    =
    if points.Length < 2 then
      Array.empty
    else
      let centerV = VertexPositionColor(v3 center.X center.Y, c)
      let arr = Array.zeroCreate<VertexPositionColor>((points.Length - 1) * 3)

      for i = 0 to points.Length - 2 do
        arr.[i * 3] <- centerV
        arr.[i * 3 + 1] <- VertexPositionColor(v3 points.[i].X points.[i].Y, c)

        arr.[i * 3 + 2] <-
          VertexPositionColor(v3 points.[i + 1].X points.[i + 1].Y, c)

      arr

  let triangleStripAsTriangles (points: System.Numerics.Vector2[]) (c: Color) =
    if points.Length < 3 then
      Array.empty
    else
      let arr = Array.zeroCreate<VertexPositionColor>((points.Length - 2) * 3)

      for i = 0 to points.Length - 3 do
        arr.[i * 3] <- VertexPositionColor(v3 points.[i].X points.[i].Y, c)

        arr.[i * 3 + 1] <-
          VertexPositionColor(v3 points.[i + 1].X points.[i + 1].Y, c)

        arr.[i * 3 + 2] <-
          VertexPositionColor(v3 points.[i + 2].X points.[i + 2].Y, c)

      arr

  let quad
    (rect: Mibo.Elmish.Next.Graphics2D.Rect)
    (tl: Mibo.Elmish.Next.Graphics2D.Base.Color)
    (bl: Mibo.Elmish.Next.Graphics2D.Base.Color)
    (tr: Mibo.Elmish.Next.Graphics2D.Base.Color)
    (br: Mibo.Elmish.Next.Graphics2D.Base.Color)
    =
    let x = rect.X
    let y = rect.Y
    let w = rect.Width
    let h = rect.Height
    let c0 = Convert.toMgColor tl
    let c1 = Convert.toMgColor tr
    let c2 = Convert.toMgColor bl
    let c3 = Convert.toMgColor br
    let arr = Array.zeroCreate<VertexPositionColor>(6)
    arr.[0] <- VertexPositionColor(v3 x y, c0)
    arr.[1] <- VertexPositionColor(v3 (x + w) y, c1)
    arr.[2] <- VertexPositionColor(v3 x (y + h), c2)
    arr.[3] <- arr.[1]
    arr.[4] <- VertexPositionColor(v3 (x + w) (y + h), c3)
    arr.[5] <- arr.[2]
    arr

  let gradientCircle
    (centerX: int)
    (centerY: int)
    (radius: float32)
    (inner: Color)
    (outer: Color)
    (segments: int)
    =
    let cx = float32 centerX
    let cy = float32 centerY
    let arr = Array.zeroCreate<VertexPositionColor>(segments * 3)
    let step = MathF.PI * 2.0f / float32 segments

    for i = 0 to segments - 1 do
      let a0 = float32 i * step
      let a1 = float32(i + 1) * step
      let x0 = cx + cos a0 * radius
      let y0 = cy + sin a0 * radius
      let x1 = cx + cos a1 * radius
      let y1 = cy + sin a1 * radius
      arr.[i * 3] <- VertexPositionColor(v3 cx cy, inner)
      arr.[i * 3 + 1] <- VertexPositionColor(v3 x0 y0, outer)
      arr.[i * 3 + 2] <- VertexPositionColor(v3 x1 y1, outer)

    arr

  let lineAsQuad
    (start: System.Numerics.Vector2)
    (finish: System.Numerics.Vector2)
    (thickness: float32)
    (c: Color)
    =
    let d = finish - start
    let len = d.Length()

    if len <= 0.0001f then
      Array.empty
    else
      let perp = System.Numerics.Vector2(-d.Y, d.X) / len * (thickness * 0.5f)
      let p0 = start + perp
      let p1 = finish + perp
      let p2 = start - perp
      let p3 = finish - perp

      [|
        VertexPositionColor(v3 p0.X p0.Y, c)
        VertexPositionColor(v3 p1.X p1.Y, c)
        VertexPositionColor(v3 p2.X p2.Y, c)
        VertexPositionColor(v3 p1.X p1.Y, c)
        VertexPositionColor(v3 p3.X p3.Y, c)
        VertexPositionColor(v3 p2.X p2.Y, c)
      |]

  let circleOutline
    (center: System.Numerics.Vector2)
    (radius: float32)
    (c: Color)
    (segments: int)
    =
    let arr = Array.zeroCreate<VertexPositionColor>(segments + 1)
    let step = MathF.PI * 2.0f / float32 segments

    for i = 0 to segments do
      let a = float32 i * step
      let x = center.X + cos a * radius
      let y = center.Y + sin a * radius
      arr.[i] <- VertexPositionColor(v3 x y, c)

    arr

  let sectorOutline
    (center: System.Numerics.Vector2)
    (radius: float32)
    (startAngle: float32)
    (endAngle: float32)
    (segments: int)
    (c: Color)
    =
    let span = endAngle - startAngle
    let steps = max 1 segments
    let step = span / float32 steps
    let arr = Array.zeroCreate<VertexPositionColor>(steps + 1)

    for i = 0 to steps do
      let a = startAngle + float32 i * step
      let x = center.X + cos a * radius
      let y = center.Y + sin a * radius
      arr.[i] <- VertexPositionColor(v3 x y, c)

    arr

  let ellipseOutline
    (centerX: int)
    (centerY: int)
    (radiusH: float32)
    (radiusV: float32)
    (c: Color)
    (segments: int)
    =
    let cx = float32 centerX
    let cy = float32 centerY
    let arr = Array.zeroCreate<VertexPositionColor>(segments + 1)
    let step = MathF.PI * 2.0f / float32 segments

    for i = 0 to segments do
      let a = float32 i * step
      let x = cx + cos a * radiusH
      let y = cy + sin a * radiusV
      arr.[i] <- VertexPositionColor(v3 x y, c)

    arr

  let polyOutline
    (center: System.Numerics.Vector2)
    (sides: int)
    (radius: float32)
    (rotation: float32)
    (c: Color)
    =
    let segments = max 3 sides
    let arr = Array.zeroCreate<VertexPositionColor>(segments + 1)
    let step = MathF.PI * 2.0f / float32 segments

    for i = 0 to segments do
      let a = float32 i * step + rotation
      let x = center.X + cos a * radius
      let y = center.Y + sin a * radius
      arr.[i] <- VertexPositionColor(v3 x y, c)

    arr

type Renderer2D<'Model>
  (
    view: GameContext -> 'Model -> RenderBuffer2D -> unit,
    clearColor: Color voption
  ) =

  let buffer = new RenderBuffer2D()
  let mutable spriteBatch: SpriteBatch voption = ValueNone
  let mutable basicEffect: BasicEffect voption = ValueNone
  let mutable pixel: Texture2D voption = ValueNone
  let mutable circleTex: Texture2D voption = ValueNone
  let mutable rasterDefault: RasterizerState voption = ValueNone
  let mutable rasterScissor: RasterizerState voption = ValueNone

  let mutable batchActive = false
  let mutable activeEffect: Effect voption = ValueNone
  let mutable activeBlend: BlendState = BlendState.AlphaBlend
  let mutable activeTransform = Matrix.Identity
  let mutable scissorRect = Rectangle.Empty
  let mutable lineWidth = 1.0f

  let initResources(ctx: GameContext) =
    match spriteBatch with
    | ValueSome _ -> ()
    | ValueNone ->
      let gd = MonoGameGameContext.getGraphicsDevice ctx
      spriteBatch <- ValueSome(new SpriteBatch(gd))
      basicEffect <- ValueSome(new BasicEffect(gd))
      basicEffect.Value.VertexColorEnabled <- true

      basicEffect.Value.Projection <-
        Matrix.CreateOrthographicOffCenter(
          0.0f,
          float32 gd.PresentationParameters.BackBufferWidth,
          float32 gd.PresentationParameters.BackBufferHeight,
          0.0f,
          0.0f,
          1.0f
        )

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
            data.[y * size + x] <- Color.White

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

  let drawPrimitives
    (device: GraphicsDevice)
    (vertices: VertexPositionColor[])
    (primitiveType: PrimitiveType)
    (primitiveCount: int)
    =
    match basicEffect with
    | ValueNone -> ()
    | ValueSome fx ->
      fx.World <- activeTransform

      fx.Projection <-
        Matrix.CreateOrthographicOffCenter(
          0.0f,
          float32 device.PresentationParameters.BackBufferWidth,
          float32 device.PresentationParameters.BackBufferHeight,
          0.0f,
          0.0f,
          1.0f
        )

      for pass in fx.CurrentTechnique.Passes do
        pass.Apply()
        device.DrawUserPrimitives(primitiveType, vertices, 0, primitiveCount)

  let handleLitSprite
    (
      hCtx: int<LightContext>,
      hTex: int<Texture>,
      dest: Mibo.Elmish.Next.Graphics2D.Rect,
      source: Mibo.Elmish.Next.Graphics2D.Rect,
      origin: System.Numerics.Vector2,
      rotation: float32,
      color: Mibo.Elmish.Next.Graphics2D.Base.Color,
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
      effect.Parameters.["NormalMap"].SetValue(buffer.Textures.Resolve hNm)
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
        Convert.toMgColor color,
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
        match buffer.[i] with
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
        | Command2D.FillRectRounded(rect, roundness, segments, color, _) ->
          flush()

          let verts =
            Geometry.roundedRect
              rect
              roundness
              (Convert.toMgColor color)
              segments

          drawPrimitives
            device
            verts
            PrimitiveType.TriangleList
            (verts.Length - 2)
        | Command2D.RectRoundedOutline(rect,
                                       roundness,
                                       segments,
                                       _thickness,
                                       color,
                                       _) ->
          flush()
          device.RasterizerState <- getRasterizer()

          let verts =
            Geometry.roundedRect
              rect
              roundness
              (Convert.toMgColor color)
              segments

          let edges =
            Array.zeroCreate<VertexPositionColor>((verts.Length - 1) * 2)

          for j = 1 to verts.Length - 1 do
            edges.[(j - 1) * 2] <- verts.[j]
            edges.[(j - 1) * 2 + 1] <- verts.[j % (verts.Length - 1) + 1]

          drawPrimitives device edges PrimitiveType.LineList (edges.Length / 2)
        | Command2D.RectGradientV(x, y, w, h, top, bottom, _) ->
          flush()

          let rect = {
            X = float32 x
            Y = float32 y
            Width = float32 w
            Height = float32 h
          }

          let verts =
            Geometry.quad rect top { top with A = bottom.A } bottom {
              bottom with
                  A = top.A
            }

          drawPrimitives device verts PrimitiveType.TriangleList 2
        | Command2D.RectGradientH(x, y, w, h, left, right, _) ->
          flush()

          let rect = {
            X = float32 x
            Y = float32 y
            Width = float32 w
            Height = float32 h
          }

          let verts =
            Geometry.quad rect left right { left with A = right.A } {
              right with
                  A = left.A
            }

          drawPrimitives device verts PrimitiveType.TriangleList 2
        | Command2D.RectGradient(rect, tl, bl, tr, br, _) ->
          flush()
          let verts = Geometry.quad rect tl bl tr br
          drawPrimitives device verts PrimitiveType.TriangleList 2
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
          flush()
          let steps = max 16 (int(radius * 0.5f))

          let verts =
            Geometry.circleOutline center radius (Convert.toMgColor color) steps

          drawPrimitives device verts PrimitiveType.LineStrip steps
        | Command2D.CircleSector(center,
                                 radius,
                                 startAngle,
                                 endAngle,
                                 segments,
                                 color,
                                 _) ->
          flush()

          let verts =
            Geometry.circleTriangles
              center
              radius
              (Convert.toMgColor color)
              (max 1 segments)

          drawPrimitives
            device
            verts
            PrimitiveType.TriangleList
            (verts.Length / 3)
        | Command2D.CircleSectorOutline(center,
                                        radius,
                                        startAngle,
                                        endAngle,
                                        segments,
                                        color,
                                        _) ->
          flush()

          let verts =
            Geometry.sectorOutline
              center
              radius
              startAngle
              endAngle
              (max 1 segments)
              (Convert.toMgColor color)

          drawPrimitives device verts PrimitiveType.LineStrip (verts.Length - 1)
        | Command2D.CircleGradient(centerX, centerY, radius, inner, outer, _) ->
          flush()

          let verts =
            Geometry.gradientCircle
              centerX
              centerY
              radius
              (Convert.toMgColor inner)
              (Convert.toMgColor outer)
              32

          drawPrimitives
            device
            verts
            PrimitiveType.TriangleList
            (verts.Length / 3)
        | Command2D.FillRing(center,
                             innerR,
                             outerR,
                             startAngle,
                             endAngle,
                             segments,
                             color,
                             _) ->
          flush()

          let verts =
            Geometry.ringTriangles
              center
              innerR
              outerR
              startAngle
              endAngle
              (max 1 segments)
              (Convert.toMgColor color)

          drawPrimitives
            device
            verts
            PrimitiveType.TriangleList
            (verts.Length / 3)
        | Command2D.RingOutline(center,
                                innerR,
                                outerR,
                                startAngle,
                                endAngle,
                                segments,
                                color,
                                _) ->
          flush()
          let color = Convert.toMgColor color

          let verts =
            Geometry.ringTriangles
              center
              innerR
              outerR
              startAngle
              endAngle
              (max 1 segments)
              color

          let inner = Array.zeroCreate<VertexPositionColor>(segments + 1)
          let outer = Array.zeroCreate<VertexPositionColor>(segments + 1)

          for j = 0 to segments do
            inner.[j] <- verts.[j * 6 + 2]
            outer.[j] <- verts.[j * 6]

          drawPrimitives device inner PrimitiveType.LineStrip segments
          drawPrimitives device outer PrimitiveType.LineStrip segments
        | Command2D.FillEllipse(centerX, centerY, radiusH, radiusV, color, _) ->
          flush()

          let verts =
            Geometry.ellipseTriangles
              centerX
              centerY
              radiusH
              radiusV
              (Convert.toMgColor color)
              32

          drawPrimitives
            device
            verts
            PrimitiveType.TriangleList
            (verts.Length / 3)
        | Command2D.EllipseOutline(centerX, centerY, radiusH, radiusV, color, _) ->
          flush()

          let verts =
            Geometry.ellipseOutline
              centerX
              centerY
              radiusH
              radiusV
              (Convert.toMgColor color)
              64

          drawPrimitives device verts PrimitiveType.LineStrip (verts.Length - 1)
        | Command2D.Line(start, finish, color, _) ->
          ensureActive(ValueNone, activeBlend, activeTransform)
          let c = Convert.toMgColor color
          let diff = Convert.toMgVec2 finish - Convert.toMgVec2 start
          let len = diff.Length()

          if len > 0.5f then
            let angle = atan2 diff.Y diff.X

            sb.Draw(
              getPixel(),
              Rectangle(int start.X, int start.Y, int len, 1),
              Nullable(Rectangle(0, 0, 1, 1)),
              c,
              angle,
              Vector2.Zero,
              SpriteEffects.None,
              0.0f
            )
        | Command2D.LineThick(start, finish, thickness, color, _) ->
          flush()

          let verts =
            Geometry.lineAsQuad
              start
              finish
              (max lineWidth thickness)
              (Convert.toMgColor color)

          if verts.Length > 0 then
            drawPrimitives
              device
              verts
              PrimitiveType.TriangleList
              (verts.Length / 3)
        | Command2D.LineStrip(points, color, _) ->
          flush()

          let verts =
            Geometry.lineTriangles
              points
              (max 1.0f lineWidth)
              (Convert.toMgColor color)

          if verts.Length > 0 then
            drawPrimitives
              device
              verts
              PrimitiveType.TriangleList
              (verts.Length / 3)
        | Command2D.Bezier(start, control, finish, thickness, color, _) ->
          flush()
          let pts = Geometry.bezierPoints start control finish 24

          let verts =
            Geometry.lineTriangles
              pts
              (max lineWidth thickness)
              (Convert.toMgColor color)

          if verts.Length > 0 then
            drawPrimitives
              device
              verts
              PrimitiveType.TriangleList
              (verts.Length / 3)
        | Command2D.Triangle(v1, v2, v3, color, _) ->
          flush()
          let c = Convert.toMgColor color

          let verts = [|
            VertexPositionColor(Geometry.v3 v1.X v1.Y, c)
            VertexPositionColor(Geometry.v3 v2.X v2.Y, c)
            VertexPositionColor(Geometry.v3 v3.X v3.Y, c)
          |]

          drawPrimitives device verts PrimitiveType.TriangleList 1
        | Command2D.TriangleFan(points, color, _) ->
          flush()

          if points.Length >= 3 then
            let c = Convert.toMgColor color
            let verts = Geometry.triangleFanAsTriangles points.[0] points c

            drawPrimitives
              device
              verts
              PrimitiveType.TriangleList
              (verts.Length / 3)
        | Command2D.TriangleStrip(points, color, _) ->
          flush()

          if points.Length >= 3 then
            let verts =
              Geometry.triangleStripAsTriangles points (Convert.toMgColor color)

            drawPrimitives
              device
              verts
              PrimitiveType.TriangleList
              (verts.Length / 3)
        | Command2D.FillPoly(center, sides, radius, rotation, color, _) ->
          flush()

          let verts =
            Geometry.polyTriangles
              center
              sides
              radius
              rotation
              (Convert.toMgColor color)

          drawPrimitives
            device
            verts
            PrimitiveType.TriangleList
            (verts.Length / 3)
        | Command2D.PolyOutline(center,
                                sides,
                                radius,
                                rotation,
                                _thickness,
                                color,
                                _) ->
          flush()

          let verts =
            Geometry.polyOutline
              center
              sides
              radius
              rotation
              (Convert.toMgColor color)

          drawPrimitives device verts PrimitiveType.LineStrip (verts.Length - 1)
        | Command2D.BeginCamera(cam, _) ->
          flush()
          activeTransform <- Convert.cameraTransform cam
        | Command2D.BeginCameraConfig(config, _) ->
          flush()
          activeTransform <- Convert.cameraTransform config.Camera

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
        | Command2D.SetBlend(mode, _) ->
          flush()
          activeBlend <- Convert.toMgBlendState mode
        | Command2D.SetScissor(x, y, w, h, _) ->
          flush()
          scissorRect <- Rectangle(x, y, w, h)
        | Command2D.ClearScissor _ ->
          flush()
          scissorRect <- Rectangle.Empty
        | Command2D.SetLineWidth(width, _) -> lineWidth <- width
        | Command2D.SetViewport(x, y, w, h, _) ->
          flush()
          device.Viewport <- Viewport(x, y, w, h)
        | Command2D.DrawImmediate(action, _) ->
          flush()
          action()
        | Command2D.Clear(color, _) ->
          flush()
          device.Clear(Convert.toMgColor color)
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
            color,
            hNorm
          )
        | Command2D.EndLighting(hCtx, _) -> handleEndLighting hCtx
        | Command2D.EnableShadows(hCtx, _) ->
          buffer.LightContexts.Resolve(hCtx).UniformsDirty <- true
        | Command2D.DisableShadows(hCtx, _) ->
          buffer.LightContexts.Resolve(hCtx).UniformsDirty <- true
        | Command2D.Particle(hTex, pData, count, _) ->
          ensureActive(ValueNone, activeBlend, activeTransform)
          let tex = buffer.Textures.Resolve hTex

          for j = 0 to count - 1 do
            let p = pData.[j]
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
      basicEffect |> ValueOption.iter(fun fx -> fx.Dispose())
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
