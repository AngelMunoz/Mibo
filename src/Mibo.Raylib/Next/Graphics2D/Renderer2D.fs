namespace Mibo.Elmish.Next.Graphics2D

open Mibo.Elmish.Next.Graphics2D.Base

open System
open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Next

module private CommandHandlers =

  [<Struct>]
  type RendererState = {
    mutable Camera: Raylib_cs.Camera2D voption
    mutable Shader: Raylib_cs.Shader voption
    mutable HasViewport: bool
    WindowWidth: int
    WindowHeight: int
  }

  let private beginCamera
    (c: Raylib_cs.Camera2D)
    (state: byref<RendererState>)
    =
    Rlgl.DrawRenderBatchActive()

    if state.Camera.IsSome then
      Raylib.EndMode2D()

    Raylib.BeginMode2D(c)
    state.Camera <- ValueSome c

  let private endCamera(state: byref<RendererState>) =
    if state.Camera.IsSome then
      Rlgl.DrawRenderBatchActive()
      Raylib.EndMode2D()
      state.Camera <- ValueNone

    if state.HasViewport then
      Rlgl.Viewport(0, 0, state.WindowWidth, state.WindowHeight)
      state.HasViewport <- false

  let private beginShader (s: Raylib_cs.Shader) (state: byref<RendererState>) =
    match state.Shader with
    | ValueSome cur when cur.Id = s.Id -> ()
    | _ ->
      Rlgl.DrawRenderBatchActive()

      if state.Shader.IsSome then
        Raylib.EndShaderMode()

      Raylib.BeginShaderMode(s)
      state.Shader <- ValueSome s

  let private endShader(state: byref<RendererState>) =
    if state.Shader.IsSome then
      Rlgl.DrawRenderBatchActive()
      Raylib.EndShaderMode()
      state.Shader <- ValueNone

  let private drawImmediate
    (action: unit -> unit)
    (state: byref<RendererState>)
    =
    Rlgl.DrawRenderBatchActive()
    let savedCam = state.Camera
    let savedShader = state.Shader

    if state.Shader.IsSome then
      Raylib.EndShaderMode()
      state.Shader <- ValueNone

    if state.Camera.IsSome then
      Raylib.EndMode2D()
      state.Camera <- ValueNone

    try
      action()
    finally
      match savedCam with
      | ValueSome c ->
        Raylib.BeginMode2D(c)
        state.Camera <- savedCam
      | ValueNone -> ()

      match savedShader with
      | ValueSome s ->
        Raylib.BeginShaderMode(s)
        state.Shader <- savedShader
      | ValueNone -> ()

  let private handleLitSprite
    (
      buffer: RenderBuffer2D,
      hCtx: int<LightContext>,
      hTex: int<Texture>,
      dest: Rect,
      source: Rect,
      origin: Vector2,
      rotation: float32,
      color: Mibo.Elmish.Next.Graphics2D.Base.Color,
      hNorm: int<Texture> voption,
      state: byref<RendererState>
    ) =
    let lightCtx = buffer.LightContexts.Resolve hCtx

    let targetShader =
      match hNorm with
      | ValueSome _ -> lightCtx.NormalMapShader
      | ValueNone -> lightCtx.Shader

    beginShader targetShader &state
    lightCtx.ShaderActive <- true

    if lightCtx.UniformsDirty then
      lightCtx.UploadUniforms()
      lightCtx.UniformsDirty <- false

    lightCtx.EnsureLocationsCached()

    match hNorm with
    | ValueSome hNm ->
      let nm = buffer.Textures.Resolve hNm
      Raylib.SetShaderValueTexture(targetShader, lightCtx.LocNormalMap, nm)
    | ValueNone -> ()

    let tex = buffer.Textures.Resolve hTex

    Raylib.DrawTexturePro(
      tex,
      Convert.toRaylibRect source,
      Convert.toRaylibRect dest,
      origin,
      rotation,
      Convert.toRaylibColor color
    )

  let execute(state: byref<RendererState>, buffer: RenderBuffer2D) =
    for i = 0 to buffer.Count - 1 do
      match buffer[i] with
      | Command2D.Sprite(hTex, dest, source, origin, rotation, color, _) ->
        let tex = buffer.Textures.Resolve hTex

        Raylib.DrawTexturePro(
          tex,
          Convert.toRaylibRect source,
          Convert.toRaylibRect dest,
          origin,
          rotation,
          Convert.toRaylibColor color
        )
      | Command2D.Text(hFont, text, position, fontSize, spacing, color, _) ->
        let font = buffer.Fonts.Resolve hFont

        Raylib.DrawTextEx(
          font,
          text,
          position,
          fontSize,
          spacing,
          Convert.toRaylibColor color
        )
      | Command2D.FillRect(rect, color, _) ->
        Raylib.DrawRectangleRec(
          Convert.toRaylibRect rect,
          Convert.toRaylibColor color
        )
      | Command2D.RectOutline(rect, thickness, color, _) ->
        Raylib.DrawRectangleLinesEx(
          Convert.toRaylibRect rect,
          thickness,
          Convert.toRaylibColor color
        )
      | Command2D.FillRectRounded(rect, roundness, segments, color, _) ->
        Raylib.DrawRectangleRounded(
          Convert.toRaylibRect rect,
          roundness,
          segments,
          Convert.toRaylibColor color
        )
      | Command2D.RectRoundedOutline(rect,
                                     roundness,
                                     segments,
                                     thickness,
                                     color,
                                     _) ->
        Raylib.DrawRectangleRoundedLinesEx(
          Convert.toRaylibRect rect,
          roundness,
          segments,
          thickness,
          Convert.toRaylibColor color
        )
      | Command2D.RectGradientV(x, y, w, h, top, bottom, _) ->
        Raylib.DrawRectangleGradientV(
          x,
          y,
          w,
          h,
          Convert.toRaylibColor top,
          Convert.toRaylibColor bottom
        )
      | Command2D.RectGradientH(x, y, w, h, left, right, _) ->
        Raylib.DrawRectangleGradientH(
          x,
          y,
          w,
          h,
          Convert.toRaylibColor left,
          Convert.toRaylibColor right
        )
      | Command2D.RectGradient(rect, tl, bl, tr, br, _) ->
        Raylib.DrawRectangleGradientEx(
          Convert.toRaylibRect rect,
          Convert.toRaylibColor tl,
          Convert.toRaylibColor bl,
          Convert.toRaylibColor tr,
          Convert.toRaylibColor br
        )
      | Command2D.FillCircle(center, radius, color, _) ->
        Raylib.DrawCircleV(center, radius, Convert.toRaylibColor color)
      | Command2D.CircleOutline(center, radius, color, _) ->
        Raylib.DrawCircleLinesV(center, radius, Convert.toRaylibColor color)
      | Command2D.CircleSector(center,
                               radius,
                               startAngle,
                               endAngle,
                               segments,
                               color,
                               _) ->
        Raylib.DrawCircleSector(
          center,
          radius,
          startAngle,
          endAngle,
          segments,
          Convert.toRaylibColor color
        )
      | Command2D.CircleSectorOutline(center,
                                      radius,
                                      startAngle,
                                      endAngle,
                                      segments,
                                      color,
                                      _) ->
        Raylib.DrawCircleSectorLines(
          center,
          radius,
          startAngle,
          endAngle,
          segments,
          Convert.toRaylibColor color
        )
      | Command2D.CircleGradient(centerX, centerY, radius, inner, outer, _) ->
        Raylib.DrawCircleGradient(
          Vector2(float32 centerX, float32 centerY),
          radius,
          Convert.toRaylibColor inner,
          Convert.toRaylibColor outer
        )
      | Command2D.FillRing(center,
                           innerR,
                           outerR,
                           startAngle,
                           endAngle,
                           segments,
                           color,
                           _) ->
        Raylib.DrawRing(
          center,
          innerR,
          outerR,
          startAngle,
          endAngle,
          segments,
          Convert.toRaylibColor color
        )
      | Command2D.RingOutline(center,
                              innerR,
                              outerR,
                              startAngle,
                              endAngle,
                              segments,
                              color,
                              _) ->
        Raylib.DrawRingLines(
          center,
          innerR,
          outerR,
          startAngle,
          endAngle,
          segments,
          Convert.toRaylibColor color
        )
      | Command2D.FillEllipse(centerX, centerY, radiusH, radiusV, color, _) ->
        Raylib.DrawEllipse(
          centerX,
          centerY,
          radiusH,
          radiusV,
          Convert.toRaylibColor color
        )
      | Command2D.EllipseOutline(centerX, centerY, radiusH, radiusV, color, _) ->
        Raylib.DrawEllipseLines(
          centerX,
          centerY,
          radiusH,
          radiusV,
          Convert.toRaylibColor color
        )
      | Command2D.Line(start, finish, color, _) ->
        Raylib.DrawLineV(start, finish, Convert.toRaylibColor color)
      | Command2D.LineThick(start, finish, thickness, color, _) ->
        Raylib.DrawLineEx(start, finish, thickness, Convert.toRaylibColor color)
      | Command2D.LineStrip(points, color, _) ->
        Raylib.DrawLineStrip(points, points.Length, Convert.toRaylibColor color)
      | Command2D.Bezier(start, control, finish, thickness, color, _) ->
        Raylib.DrawSplineSegmentBezierQuadratic(
          start,
          control,
          finish,
          thickness,
          Convert.toRaylibColor color
        )
      | Command2D.Triangle(v1, v2, v3, color, _) ->
        Raylib.DrawTriangle(v1, v2, v3, Convert.toRaylibColor color)
      | Command2D.TriangleFan(points, color, _) ->
        Raylib.DrawTriangleFan(
          points,
          points.Length,
          Convert.toRaylibColor color
        )
      | Command2D.TriangleStrip(points, color, _) ->
        Raylib.DrawTriangleStrip(
          points,
          points.Length,
          Convert.toRaylibColor color
        )
      | Command2D.FillPoly(center, sides, radius, rotation, color, _) ->
        Raylib.DrawPoly(
          center,
          sides,
          radius,
          rotation,
          Convert.toRaylibColor color
        )
      | Command2D.PolyOutline(center,
                              sides,
                              radius,
                              rotation,
                              thickness,
                              color,
                              _) ->
        Raylib.DrawPolyLinesEx(
          center,
          sides,
          radius,
          rotation,
          thickness,
          Convert.toRaylibColor color
        )
      | Command2D.BeginCamera(cam, _) ->
        beginCamera (Convert.toRaylibCamera2D cam) &state
      | Command2D.BeginCameraConfig(config, _) ->
        match config.Viewport with
        | ValueSome vp ->
          let vpX = int(vp.X * float32 state.WindowWidth)
          let vpY = int(vp.Y * float32 state.WindowHeight)
          let vpW = int(vp.Width * float32 state.WindowWidth)
          let vpH = int(vp.Height * float32 state.WindowHeight)
          Rlgl.DrawRenderBatchActive()
          Rlgl.Viewport(vpX, vpY, vpW, vpH)
          state.HasViewport <- true
        | ValueNone -> ()

        match config.ClearColor with
        | ValueSome c -> Raylib.ClearBackground(Convert.toRaylibColor c)
        | ValueNone -> ()

        beginCamera (Convert.toRaylibCamera2D config.Camera) &state
      | Command2D.EndCamera _ -> endCamera &state
      | Command2D.BeginShader(hShader, _) ->
        beginShader (buffer.Shaders.Resolve hShader) &state
      | Command2D.EndShader _ -> endShader &state
      | Command2D.BeginTarget(hTarget, _) ->
        Raylib.BeginTextureMode(buffer.RenderTargets.Resolve hTarget)
      | Command2D.EndTarget _ -> Raylib.EndTextureMode()
      | Command2D.SetBlend(mode, _) ->
        Rlgl.SetBlendMode(Convert.toRaylibBlendMode mode)
      | Command2D.SetScissor(x, y, w, h, _) ->
        Rlgl.EnableScissorTest()
        Rlgl.Scissor(x, y, w, h)
      | Command2D.ClearScissor _ -> Rlgl.DisableScissorTest()
      | Command2D.SetLineWidth(width, _) -> Rlgl.SetLineWidth(width)
      | Command2D.SetViewport(x, y, w, h, _) -> Rlgl.Viewport(x, y, w, h)
      | Command2D.DrawImmediate(action, _) -> drawImmediate action &state
      | Command2D.Clear(color, _) ->
        Raylib.ClearBackground(Convert.toRaylibColor color)
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
          buffer,
          hCtx,
          hTex,
          dest,
          source,
          origin,
          rotation,
          color,
          hNorm,
          &state
        )
      | Command2D.EndLighting(hCtx, _) ->
        let lightCtx = buffer.LightContexts.Resolve hCtx

        if lightCtx.ShaderActive then
          endShader &state
          lightCtx.ShaderActive <- false
          lightCtx.UniformsDirty <- true
      | Command2D.EnableShadows(hCtx, _) ->
        let lightCtx = buffer.LightContexts.Resolve hCtx
        lightCtx.UniformsDirty <- true
      | Command2D.DisableShadows(hCtx, _) ->
        let lightCtx = buffer.LightContexts.Resolve hCtx
        lightCtx.UniformsDirty <- true
      | Command2D.Particle(hTex, particles, count, _) ->
        let tex = buffer.Textures.Resolve hTex

        for j = 0 to count - 1 do
          let p = particles[j]
          let halfW = p.Size.X * 0.5f
          let halfH = p.Size.Y * 0.5f

          let src = Convert.toRaylibRect p.SourceRect

          let dst =
            Rectangle(
              p.Position.X - halfW,
              p.Position.Y - halfH,
              p.Size.X,
              p.Size.Y
            )

          Raylib.DrawTexturePro(
            tex,
            src,
            dst,
            Vector2(halfW, halfH),
            p.Rotation,
            Convert.toRaylibColor p.Color
          )

    endShader &state
    endCamera &state

type Renderer2D<'Model>
  (
    view: GameContext -> 'Model -> RenderBuffer2D -> unit,
    clearColor: Raylib_cs.Color voption
  ) =

  let buffer = RenderBuffer2D(capacity = 4096)

  let mutable _camera: Raylib_cs.Camera2D voption = ValueNone
  let mutable _shader: Raylib_cs.Shader voption = ValueNone
  let mutable _hasViewport = false

  interface IRenderer<'Model> with
    member _.Draw(ctx, model, _gameTime) =
      buffer.Clear()
      view ctx model buffer
      buffer.Sort()

      let mutable state: CommandHandlers.RendererState = {
        Camera = _camera
        Shader = _shader
        HasViewport = _hasViewport
        WindowWidth = ctx.WindowWidth
        WindowHeight = ctx.WindowHeight
      }

      match clearColor with
      | ValueSome c -> Raylib.ClearBackground(c)
      | ValueNone -> ()

      CommandHandlers.execute(&state, buffer)

      _camera <- state.Camera
      _shader <- state.Shader
      _hasViewport <- state.HasViewport

  interface IDisposable with
    member _.Dispose() = buffer.Dispose()

module Renderer2D =

  let create
    (view: GameContext -> 'Model -> RenderBuffer2D -> unit)
    : IRenderer<'Model> =
    new Renderer2D<'Model>(view, ValueSome Raylib_cs.Color.Black)
    :> IRenderer<'Model>
