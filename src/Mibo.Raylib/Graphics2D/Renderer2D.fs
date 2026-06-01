#nowarn "9"

namespace Mibo.Elmish.Graphics2D

open System
open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting

/// <summary>A single post-processing pass applied to the rendered scene.</summary>
[<Struct>]
type PostProcessPass = {

  /// <summary>Shader used for this pass. Receives the scene/render-texture as <c>texture0</c>.</summary>
  Shader: Shader

  /// <summary>
  /// Optional callback to set shader uniforms before rendering the fullscreen quad.
  /// Called once per frame when this pass executes. The raylib <see cref="T:Raylib_cs.Shader"/>
  /// is already active via <c>BeginShaderMode</c> when this callback runs.
  /// </summary>
  OnSetup: (Shader -> GameContext -> unit) voption
}

/// <summary>Configuration for the <see cref="T:Mibo.Elmish.Graphics2D.Renderer2D`1"/>.</summary>
[<Struct>]
type Renderer2DConfig = {

  /// <summary>
  /// Optional post-processing passes. Applied in order after the scene is rendered
  /// to a render texture, chaining via pooled render textures between passes.
  /// The last pass renders directly to the backbuffer.
  /// </summary>
  PostProcess: PostProcessPass[] voption

  /// <summary>
  /// Background clear color applied before rendering commands.
  /// <see cref="F:Microsoft.FSharp.Core.ValueOption`1.ValueNone"/> skips clearing entirely,
  /// which is useful when composing multiple renderers (e.g., 2D overlay on 3D scene).
  /// <see cref="F:Microsoft.FSharp.Core.ValueOption`1.ValueSome"/> clears with the specified color.
  /// </summary>
  ClearColor: Color voption
}

/// <summary>Convenience values and functions for <see cref="T:Mibo.Elmish.Graphics2D.Renderer2DConfig"/>.</summary>
module Renderer2DConfig =

  /// <summary>
  /// Default configuration: no post-processing, black clear color.
  /// Suitable for most 2D games that don't need screen-space effects.
  /// </summary>
  let defaults: Renderer2DConfig = {
    PostProcess = ValueNone
    ClearColor = ValueSome Color.Black
  }

  /// <summary>
  /// Configuration that skips clearing the background.
  /// Use when this renderer composites on top of another renderer's output.
  /// </summary>
  let noClear: Renderer2DConfig = {
    PostProcess = ValueNone
    ClearColor = ValueNone
  }

/// <summary>
/// A deferred 2D renderer that sorts commands by layer and executes them
/// via pattern matching on <see cref="T:Mibo.Elmish.Graphics2D.Command2D"/>.
/// </summary>
/// <remarks>
/// <para>
/// Commands are accumulated each frame via the <c>view</c> function into a
/// <see cref="T:Mibo.Elmish.Graphics2D.RenderBuffer2D"/>, sorted by layer, then executed
/// in order. raylib handles internal draw-call batching automatically.
/// </para>
/// <para>
/// When <see cref="P:Mibo.Elmish.Graphics2D.Renderer2DConfig.PostProcess"/> is
/// configured, the scene renders to a <see cref="T:Raylib_cs.RenderTexture2D"/>
/// and each pass is applied sequentially via ping-pong render textures from the
/// <see cref="T:Mibo.Elmish.Graphics2D.IRenderTargetPool"/>.
/// </para>
/// <para>
/// Register via <c>Program.withRenderer</c>:
/// <code lang="fsharp">
/// Program.mkProgram init update view
/// |> Program.withRenderer(fun () -> Renderer2D.create view)
/// </code>
/// </para>
/// </remarks>
/// <typeparam name="Model">The application model type, passed to the view function.</typeparam>
type Renderer2D<'Model>
  (
    view: GameContext -> 'Model -> RenderBuffer2D -> unit,
    config: Renderer2DConfig
  ) =

  let buffer = RenderBuffer2D(capacity = 4096)
  let rtPool: IRenderTargetPool = new RenderTargetPool()

  let mutable _camera: Camera2D voption = ValueNone
  let mutable _shader: Shader voption = ValueNone
  let mutable _hasViewport = false
  let mutable _windowWidth = 0
  let mutable _windowHeight = 0

  let beginCamera(c: Camera2D) =
    Rlgl.DrawRenderBatchActive()

    if _camera.IsSome then
      Raylib.EndMode2D()

    Raylib.BeginMode2D(c)
    _camera <- ValueSome c

  let endCamera() =
    if _camera.IsSome then
      Rlgl.DrawRenderBatchActive()
      Raylib.EndMode2D()
      _camera <- ValueNone

    if _hasViewport then
      Rlgl.Viewport(0, 0, _windowWidth, _windowHeight)
      _hasViewport <- false

  let beginShader(s: Shader) =
    match _shader with
    | ValueSome cur when cur.Id = s.Id -> ()
    | _ ->
      Rlgl.DrawRenderBatchActive()

      if _shader.IsSome then
        Raylib.EndShaderMode()

      Raylib.BeginShaderMode(s)
      _shader <- ValueSome s

  let endShader() =
    if _shader.IsSome then
      Rlgl.DrawRenderBatchActive()
      Raylib.EndShaderMode()
      _shader <- ValueNone

  let drawImmediate(action: unit -> unit) =
    Rlgl.DrawRenderBatchActive()
    let savedCam = _camera
    let savedShader = _shader

    if _shader.IsSome then
      Raylib.EndShaderMode()
      _shader <- ValueNone

    if _camera.IsSome then
      Raylib.EndMode2D()
      _camera <- ValueNone

    try
      action()
    finally
      match savedCam with
      | ValueSome c ->
        Raylib.BeginMode2D(c)
        _camera <- savedCam
      | ValueNone -> ()

      match savedShader with
      | ValueSome s ->
        Raylib.BeginShaderMode(s)
        _shader <- savedShader
      | ValueNone -> ()

  let executeCommands() =
    for i = 0 to buffer.Count - 1 do
      match buffer[i] with
      | Command2D.Sprite(texture, dest, source, origin, rotation, color, _) ->
        Raylib.DrawTexturePro(texture, source, dest, origin, rotation, color)
      | Command2D.Text(font, text, position, fontSize, spacing, color, _) ->
        Raylib.DrawTextEx(font, text, position, fontSize, spacing, color)
      | Command2D.FillRect(rect, color, _) ->
        Raylib.DrawRectangleRec(rect, color)
      | Command2D.RectOutline(rect, thickness, color, _) ->
        Raylib.DrawRectangleLinesEx(rect, thickness, color)
      | Command2D.FillRectRounded(rect, roundness, segments, color, _) ->
        Raylib.DrawRectangleRounded(rect, roundness, segments, color)
      | Command2D.RectRoundedOutline(rect,
                                     roundness,
                                     segments,
                                     thickness,
                                     color,
                                     _) ->
        Raylib.DrawRectangleRoundedLinesEx(
          rect,
          roundness,
          segments,
          thickness,
          color
        )
      | Command2D.RectGradientV(x, y, w, h, top, bottom, _) ->
        Raylib.DrawRectangleGradientV(x, y, w, h, top, bottom)
      | Command2D.RectGradientH(x, y, w, h, left, right, _) ->
        Raylib.DrawRectangleGradientH(x, y, w, h, left, right)
      | Command2D.RectGradient(rect, tl, bl, tr, br, _) ->
        Raylib.DrawRectangleGradientEx(rect, tl, bl, tr, br)
      | Command2D.FillCircle(center, radius, color, _) ->
        Raylib.DrawCircleV(center, radius, color)
      | Command2D.CircleOutline(center, radius, color, _) ->
        Raylib.DrawCircleLinesV(center, radius, color)
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
          color
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
          color
        )
      | Command2D.CircleGradient(centerX, centerY, radius, inner, outer, _) ->
        Raylib.DrawCircleGradient(
          Vector2(float32 centerX, float32 centerY),
          radius,
          inner,
          outer
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
          color
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
          color
        )
      | Command2D.FillEllipse(centerX, centerY, radiusH, radiusV, color, _) ->
        Raylib.DrawEllipse(centerX, centerY, radiusH, radiusV, color)
      | Command2D.EllipseOutline(centerX, centerY, radiusH, radiusV, color, _) ->
        Raylib.DrawEllipseLines(centerX, centerY, radiusH, radiusV, color)
      | Command2D.Line(start, finish, color, _) ->
        Raylib.DrawLineV(start, finish, color)
      | Command2D.LineThick(start, finish, thickness, color, _) ->
        Raylib.DrawLineEx(start, finish, thickness, color)
      | Command2D.LineStrip(points, color, _) ->
        Raylib.DrawLineStrip(points, points.Length, color)
      | Command2D.Bezier(start, control, finish, thickness, color, _) ->
        Raylib.DrawSplineSegmentBezierQuadratic(
          start,
          control,
          finish,
          thickness,
          color
        )
      | Command2D.Triangle(v1, v2, v3, color, _) ->
        Raylib.DrawTriangle(v1, v2, v3, color)
      | Command2D.TriangleFan(points, color, _) ->
        Raylib.DrawTriangleFan(points, points.Length, color)
      | Command2D.TriangleStrip(points, color, _) ->
        Raylib.DrawTriangleStrip(points, points.Length, color)
      | Command2D.FillPoly(center, sides, radius, rotation, color, _) ->
        Raylib.DrawPoly(center, sides, radius, rotation, color)
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
          color
        )
      | Command2D.BeginCamera(camera, _) -> beginCamera camera
      | Command2D.BeginCameraConfig(config: Camera2DConfig, _) ->
        // Apply viewport if specified
        match config.Viewport with
        | ValueSome vp ->
          let vpX = int(vp.X * float32 _windowWidth)
          let vpY = int(vp.Y * float32 _windowHeight)
          let vpW = int(vp.Width * float32 _windowWidth)
          let vpH = int(vp.Height * float32 _windowHeight)
          Rlgl.DrawRenderBatchActive()
          Rlgl.Viewport(vpX, vpY, vpW, vpH)
          _hasViewport <- true
        | ValueNone -> ()

        // Clear if specified
        match config.ClearColor with
        | ValueSome c -> Raylib.ClearBackground(c)
        | ValueNone -> ()

        beginCamera config.Camera
      | Command2D.EndCamera _ -> endCamera()
      | Command2D.BeginShader(shader, _) -> beginShader shader
      | Command2D.EndShader _ -> endShader()
      | Command2D.BeginTarget(target, _) -> Raylib.BeginTextureMode(target)
      | Command2D.EndTarget _ -> Raylib.EndTextureMode()
      | Command2D.SetBlend(mode, _) -> Rlgl.SetBlendMode(mode)
      | Command2D.SetScissor(x, y, w, h, _) ->
        Rlgl.EnableScissorTest()
        Rlgl.Scissor(x, y, w, h)
      | Command2D.ClearScissor _ -> Rlgl.DisableScissorTest()
      | Command2D.SetLineWidth(width, _) -> Rlgl.SetLineWidth(width)
      | Command2D.SetViewport(x, y, w, h, _) -> Rlgl.Viewport(x, y, w, h)
      | Command2D.DrawImmediate(action, _) -> drawImmediate action
      | Command2D.Clear(color, _) -> Raylib.ClearBackground(color)
      | Command2D.NoopLight _ -> ()
      | Command2D.LitSprite(lightCtx, sprite) ->
        // Select the correct shader variant for this sprite.
        // beginShader handles the batch flush and BeginShaderMode switch
        // when the shader ID changes — the standard raylib pattern.
        let targetShader =
          match sprite.NormalMap with
          | ValueSome _ -> lightCtx.NormalMapShader
          | ValueNone -> lightCtx.Shader

        beginShader targetShader
        lightCtx.ShaderActive <- true

        // Upload light uniforms once per frame (to both shader variants).
        if lightCtx.UniformsDirty then
          lightCtx.UploadUniforms()
          lightCtx.UniformsDirty <- false

        lightCtx.EnsureLocationsCached()

        // Bind the normal map texture when using the normal-map shader.
        match sprite.NormalMap with
        | ValueSome nm ->
          Raylib.SetShaderValueTexture(targetShader, lightCtx.LocNormalMap, nm)
        | ValueNone -> ()

        Raylib.DrawTexturePro(
          sprite.Texture,
          sprite.Source,
          sprite.Dest,
          sprite.Origin,
          sprite.Rotation,
          sprite.Color
        )
      | Command2D.EndLighting(lightCtx, _) ->
        if lightCtx.ShaderActive then
          endShader()
          lightCtx.ShaderActive <- false
          lightCtx.UniformsDirty <- true
      | Command2D.EnableShadows(lightCtx, _) -> lightCtx.UniformsDirty <- true
      | Command2D.DisableShadows(lightCtx, _) -> lightCtx.UniformsDirty <- true
      | Command2D.Particle(texture, particles, count, _) ->
        for j = 0 to count - 1 do
          let p = particles[j]
          let halfW = p.Size.X * 0.5f
          let halfH = p.Size.Y * 0.5f

          let src =
            Rectangle(0.f, 0.f, float32 texture.Width, float32 texture.Height)

          let dst =
            Rectangle(
              p.Position.X - halfW,
              p.Position.Y - halfH,
              p.Size.X,
              p.Size.Y
            )

          Raylib.DrawTexturePro(texture, src, dst, Vector2.Zero, 0.f, p.Color)

    endShader()
    endCamera()

  let applyPostProcess
    (ctx: GameContext, sceneTarget: RenderTexture2D, passes: PostProcessPass[])
    =
    let mutable src = sceneTarget
    let w = ctx.WindowWidth
    let h = ctx.WindowHeight

    for i = 0 to passes.Length - 1 do
      let pass = passes[i]
      let isLast = i = passes.Length - 1

      let dst: RenderTexture2D voption =
        if isLast then
          ValueNone
        else
          ValueSome(rtPool.Acquire(w, h))

      match dst with
      | ValueSome target ->
        Raylib.BeginTextureMode(target)
        Raylib.ClearBackground(Color.Black)
      | ValueNone -> ()

      Raylib.BeginShaderMode(pass.Shader)

      match pass.OnSetup with
      | ValueSome f -> f pass.Shader ctx
      | ValueNone -> ()

      let sourceRect = Raylib_cs.Rectangle(0f, 0f, float32 w, float32 -h)

      let destRect = Raylib_cs.Rectangle(0f, 0f, float32 w, float32 h)

      Raylib.DrawTexturePro(
        src.Texture,
        sourceRect,
        destRect,
        Vector2.Zero,
        0f,
        Color.White
      )

      Raylib.EndShaderMode()

      match dst with
      | ValueSome target ->
        Raylib.EndTextureMode()
        src <- target
      | ValueNone -> ()

  interface IRenderer<'Model> with
    member _.Draw(ctx, model, _gameTime) =
      _windowWidth <- ctx.WindowWidth
      _windowHeight <- ctx.WindowHeight
      buffer.Clear()

      view ctx model buffer
      buffer.Sort()

      match config.PostProcess with
      | ValueNone ->
        match config.ClearColor with
        | ValueSome c -> Raylib.ClearBackground(c)
        | ValueNone -> ()

        executeCommands()
      | ValueSome passes ->
        let sceneRT = rtPool.Acquire(ctx.WindowWidth, ctx.WindowHeight)

        Raylib.BeginTextureMode(sceneRT)

        match config.ClearColor with
        | ValueSome c -> Raylib.ClearBackground(c)
        | ValueNone -> ()

        executeCommands()
        Raylib.EndTextureMode()

        applyPostProcess(ctx, sceneRT, passes)
        rtPool.ReleaseAll()

  interface IDisposable with
    member _.Dispose() =
      (rtPool :?> System.IDisposable).Dispose()

/// <summary>Convenience constructors for <see cref="T:Mibo.Elmish.Graphics2D.Renderer2D`1"/>.</summary>
module Renderer2D =

  /// <summary>
  /// Creates a renderer with default configuration (no post-processing, black clear color).
  /// </summary>
  /// <param name="view">
  /// The view function that populates the render buffer each frame.
  /// Receives the game context, current model, and a mutable buffer.
  /// </param>
  let create
    (view: GameContext -> 'Model -> RenderBuffer2D -> unit)
    : IRenderer<'Model> =
    new Renderer2D<'Model>(view, Renderer2DConfig.defaults) :> IRenderer<'Model>

  /// <summary>
  /// Creates a renderer with custom configuration.
  /// </summary>
  /// <param name="config">The renderer configuration.</param>
  /// <param name="view">
  /// The view function that populates the render buffer each frame.
  /// Receives the game context, current model, and a mutable buffer.
  /// </param>
  let createWith
    (config: Renderer2DConfig)
    (view: GameContext -> 'Model -> RenderBuffer2D -> unit)
    : IRenderer<'Model> =
    new Renderer2D<'Model>(view, config) :> IRenderer<'Model>
