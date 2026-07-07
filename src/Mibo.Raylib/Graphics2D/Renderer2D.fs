#nowarn "9"

namespace Mibo.Elmish.Graphics2D

open System
open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting

// ═══════════════════════════════════════════════════════════════════
// Post-process drain — ping-pongs the scene through each emitted action
// ═══════════════════════════════════════════════════════════════════

module private PostProcessDrain =

  /// <summary>
  /// Runs each post-process action in order, ping-ponging the scene texture through
  /// pooled render textures. Each action receives the current source as a
  /// <see cref="T:Mibo.Elmish.Graphics2D.PostProcessContext2D"/> and owns its shader +
  /// fullscreen draw. The last action draws to the back-buffer.
  /// </summary>
  let apply
    (ctx: GameContext)
    (sceneTarget: RenderTexture2D)
    (lights: LightContext2D voption)
    (camera: Camera2D voption)
    (rtPool: IRenderTargetPool)
    (actions: ResizeArray<PostProcessContext2D -> unit>)
    (frameTime: float32)
    =
    let mutable src = sceneTarget
    let w = ctx.WindowWidth
    let h = ctx.WindowHeight

    for i = 0 to actions.Count - 1 do
      let isLast = i = actions.Count - 1

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

      let ppCtx: PostProcessContext2D = {
        Source = src
        Width = w
        Height = h
        Time = frameTime
        Lights = lights
        Camera = camera
        Context = ctx
      }

      actions[i]ppCtx

      match dst with
      | ValueSome target ->
        Raylib.EndTextureMode()
        src <- target
      | ValueNone -> ()

/// <summary>Configuration for the <see cref="T:Mibo.Elmish.Graphics2D.Renderer2D`1"/>.</summary>
[<Struct>]
type Renderer2DConfig = {

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
  /// Default configuration: black clear color. Post-processing is driven by
  /// <c>Command2D.PostProcess</c> emitted from the view, not configured here.
  /// </summary>
  let defaults: Renderer2DConfig = { ClearColor = ValueSome Color.Black }

  /// <summary>
  /// Configuration that skips clearing the background.
  /// Use when this renderer composites on top of another renderer's output.
  /// </summary>
  let noClear: Renderer2DConfig = { ClearColor = ValueNone }

// ═══════════════════════════════════════════════════════════════════
// Private command handlers — extracted from Renderer2D for readability
// ═══════════════════════════════════════════════════════════════════

module private CommandHandlers =

  /// <summary>Mutable renderer state threaded through command dispatch byref.</summary>
  [<Struct>]
  type RendererState = {
    mutable Camera: Camera2D voption
    mutable Shader: Shader voption
    mutable HasViewport: bool
    WindowWidth: int
    WindowHeight: int
  }

  // ── Camera state management ──────────────────────────────────

  let private beginCamera (c: Camera2D) (state: byref<RendererState>) =
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

  // ── Shader state management ──────────────────────────────────

  let private beginShader (s: Shader) (state: byref<RendererState>) =
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

  // ── Escape hatch ─────────────────────────────────────────────

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

  // ── Lighting handlers ────────────────────────────────────────

  let private handleLitSprite
    (lightCtx: LightContext2D, sprite: SpriteState, state: byref<RendererState>)
    =
    let targetShader =
      match sprite.NormalMap with
      | ValueSome _ -> lightCtx.NormalMapShader
      | ValueNone -> lightCtx.Shader

    beginShader targetShader &state
    lightCtx.ShaderActive <- true

    if lightCtx.UniformsDirty then
      lightCtx.UploadUniforms()
      lightCtx.UniformsDirty <- false

    lightCtx.EnsureLocationsCached()

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

  let private handleEndLighting
    (lightCtx: LightContext2D, state: byref<RendererState>)
    =
    if lightCtx.ShaderActive then
      endShader &state
      lightCtx.ShaderActive <- false
      lightCtx.UniformsDirty <- true

  // ── Main dispatch ────────────────────────────────────────────

  let execute(state: byref<RendererState>, buffer: RenderBuffer2D) =
    for i = 0 to buffer.Count - 1 do
      match buffer[i] with
      // Sprite & Text
      | Command2D.Sprite(texture, dest, source, origin, rotation, color, _) ->
        Raylib.DrawTexturePro(texture, source, dest, origin, rotation, color)
      | Command2D.Text(font, text, position, fontSize, spacing, color, _) ->
        Raylib.DrawTextEx(font, text, position, fontSize, spacing, color)
      // Rectangles
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
      // Circles & Ellipses
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
      // Lines & Curves
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
      // Triangles & Polygons
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
      // Camera
      | Command2D.BeginCamera(camera, _) -> beginCamera camera &state
      | Command2D.BeginCameraConfig(config: Camera2DConfig, _) ->
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
        | ValueSome c -> Raylib.ClearBackground(c)
        | ValueNone -> ()

        beginCamera config.Camera &state
      | Command2D.EndCamera _ -> endCamera &state
      // Shader
      | Command2D.BeginShader(shader, _) -> beginShader shader &state
      | Command2D.EndShader _ -> endShader &state
      // Render Targets
      | Command2D.BeginTarget(target, _) -> Raylib.BeginTextureMode(target)
      | Command2D.EndTarget _ -> Raylib.EndTextureMode()
      // Render State
      | Command2D.SetBlend(mode, _) -> Rlgl.SetBlendMode(mode)
      | Command2D.SetScissor(x, y, w, h, _) ->
        Rlgl.EnableScissorTest()
        Rlgl.Scissor(x, y, w, h)
      | Command2D.ClearScissor _ -> Rlgl.DisableScissorTest()
      | Command2D.SetLineWidth(width, _) -> Rlgl.SetLineWidth(width)
      | Command2D.SetViewport(x, y, w, h, _) -> Rlgl.Viewport(x, y, w, h)
      // Escape Hatches
      | Command2D.DrawImmediate(action, _) -> drawImmediate action &state
      | Command2D.Clear(color, _) -> Raylib.ClearBackground(color)
      // Lighting
      | Command2D.NoopLight _ -> ()
      | Command2D.LitSprite(lightCtx, sprite) ->
        handleLitSprite(lightCtx, sprite, &state)
      | Command2D.EndLighting(lightCtx, _) ->
        handleEndLighting(lightCtx, &state)
      | Command2D.EnableShadows(lightCtx, _) -> lightCtx.UniformsDirty <- true
      | Command2D.DisableShadows(lightCtx, _) -> lightCtx.UniformsDirty <- true
      // Particles
      | Command2D.Particle(texture, particles, count, _) ->
        let fullSrc =
          Rectangle(0.f, 0.f, float32 texture.Width, float32 texture.Height)

        for j = 0 to count - 1 do
          let p = particles[j]
          let halfW = p.Size.X * 0.5f
          let halfH = p.Size.Y * 0.5f

          let src =
            if p.SourceRect.Width > 0.f && p.SourceRect.Height > 0.f then
              p.SourceRect
            else
              fullSrc

          let dst =
            Rectangle(
              p.Position.X - halfW,
              p.Position.Y - halfH,
              p.Size.X,
              p.Size.Y
            )

          Raylib.DrawTexturePro(texture, src, dst, Vector2.Zero, 0.f, p.Color)
      // Post-process actions are drained after the scene renders; nothing to do here.
      | Command2D.PostProcess _ -> ()

    endShader &state
    endCamera &state

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
/// When the view emits <c>Command2D.PostProcess</c> actions, the scene renders to a
/// <see cref="T:Raylib_cs.RenderTexture2D"/> and each action runs in order, chaining via
/// ping-pong render textures from the <see cref="T:Mibo.Elmish.Graphics2D.IRenderTargetPool"/>.
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

  interface IRenderer<'Model> with
    member _.Draw(ctx, model, gameTime) =
      _windowWidth <- ctx.WindowWidth
      _windowHeight <- ctx.WindowHeight
      buffer.Clear()

      view ctx model buffer
      buffer.Sort()

      let mutable state: CommandHandlers.RendererState = {
        Camera = _camera
        Shader = _shader
        HasViewport = _hasViewport
        WindowWidth = _windowWidth
        WindowHeight = _windowHeight
      }

      if buffer.PostProcessCount = 0 then
        match config.ClearColor with
        | ValueSome c -> Raylib.ClearBackground(c)
        | ValueNone -> ()

        CommandHandlers.execute(&state, buffer)
      else
        let ppActions =
          ResizeArray<PostProcessContext2D -> unit>(buffer.PostProcessCount)

        let mutable lightCtx: LightContext2D voption = ValueNone

        for i = 0 to buffer.Count - 1 do
          match buffer[i] with
          | Command2D.PostProcess a -> ppActions.Add a
          | Command2D.LitSprite(ctx, _)
          | Command2D.EndLighting(ctx, _)
          | Command2D.EnableShadows(ctx, _)
          | Command2D.DisableShadows(ctx, _) ->
            if lightCtx.IsNone then
              lightCtx <- ValueSome ctx
          | _ -> ()

        let sceneRT = rtPool.Acquire(ctx.WindowWidth, ctx.WindowHeight)
        Raylib.BeginTextureMode(sceneRT)

        match config.ClearColor with
        | ValueSome c -> Raylib.ClearBackground(c)
        | ValueNone -> ()

        CommandHandlers.execute(&state, buffer)
        Raylib.EndTextureMode()

        PostProcessDrain.apply
          ctx
          sceneRT
          lightCtx
          state.Camera
          rtPool
          ppActions
          (float32 gameTime.TotalTime.TotalSeconds)

        rtPool.ReleaseAll()

      _camera <- state.Camera
      _shader <- state.Shader
      _hasViewport <- state.HasViewport

  interface IDisposable with
    member _.Dispose() =
      (rtPool :?> System.IDisposable).Dispose()

/// <summary>Convenience constructors for <see cref="T:Mibo.Elmish.Graphics2D.Renderer2D`1"/>.</summary>
module Renderer2D =

  /// <summary>
  /// Creates a renderer with default configuration (black clear color). Post-processing
  /// is driven by <c>Command2D.PostProcess</c> emitted from the view.
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
