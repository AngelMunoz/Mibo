namespace Mibo.Elmish.Graphics2D

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

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
  /// Default configuration: black clear color.
  /// Suitable for most 2D games that don't need screen-space effects.
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
    WindowWidth: int
    WindowHeight: int
  }

  /// <summary>
  /// Backend resources the command handlers close over. The MonoGame analog
  /// of raylib's implicit global batch + primitives. Mirrors how the raylib
  /// handlers reach raylib's batch without it being passed in — here the
  /// SpriteBatch and procedural textures are captured once per frame.
  /// </summary>
  [<Struct>]
  type RenderResources = {
    SpriteBatch: SpriteBatch
    WhitePixel: Texture2D
    CircleTex: Texture2D
  }

  // ── Batch lifecycle ───────────────────────────────────────────
  // The SpriteBatch state (blend/sampler/depth/rasterizer) is renderer-internal:
  // userland only expresses intent via Draw.* commands, never framework state
  // objects. Consolidated here so there is a single source of truth. This is
  // the MonoGame analog of raylib's implicit batch defaults.

  let beginBatch(sb: SpriteBatch, matrix: Matrix) =
    sb.Begin(
      SpriteSortMode.Deferred,
      BlendState.NonPremultiplied,
      SamplerState.LinearClamp,
      DepthStencilState.None,
      RasterizerState.CullNone,
      null,
      matrix
    )

  // ── Camera state management ──────────────────────────────────
  // Analog of raylib's beginCamera/endCamera, which flush the active batch
  // (Rlgl.DrawRenderBatchActive) and re-enter BeginMode2D. Here we End the
  // SpriteBatch and re-Begin with the camera's transform matrix.

  let private beginCamera
    (c: Camera2D)
    (state: byref<RendererState>)
    (res: RenderResources)
    =
    res.SpriteBatch.End()
    beginBatch(res.SpriteBatch, Camera2D.toMatrix c)
    state.Camera <- ValueSome c

  let private endCamera(state: byref<RendererState>, res: RenderResources) =
    match state.Camera with
    | ValueSome _ ->
      res.SpriteBatch.End()
      beginBatch(res.SpriteBatch, Matrix.Identity)
      state.Camera <- ValueNone
    | ValueNone -> ()

  // ── Escape hatch ─────────────────────────────────────────────
  // Analog of raylib's drawImmediate: flush the batch, exit camera/shader,
  // run the action, then restore. MVP has no shader, so only camera is saved.

  let private drawImmediate
    (action: unit -> unit)
    (state: byref<RendererState>)
    (res: RenderResources)
    =
    res.SpriteBatch.End()
    let savedCam = state.Camera

    try
      action()
    finally
      beginBatch(
        res.SpriteBatch,
        match savedCam with
        | ValueSome c -> Camera2D.toMatrix c
        | ValueNone -> Matrix.Identity
      )

      state.Camera <- savedCam

  // ── Main dispatch ────────────────────────────────────────────

  let execute
    (state: byref<RendererState>, buffer: RenderBuffer2D, res: RenderResources)
    =
    let sb = res.SpriteBatch

    for i = 0 to buffer.Count - 1 do
      match buffer[i] with
      // Sprite & Text
      | Command2D.Sprite(texture, dest, source, origin, rotation, color, _) ->
        sb.Draw(
          texture,
          dest,
          Nullable source,
          color,
          rotation,
          origin,
          SpriteEffects.None,
          0.0f
        )

      | Command2D.Text(font, text, position, scale, color, _) ->
        sb.DrawString(
          font,
          text,
          position,
          color,
          0.0f,
          Vector2.Zero,
          scale,
          SpriteEffects.None,
          0.0f
        )

      // Rectangles
      | Command2D.FillRect(rect, color, _) ->
        sb.Draw(res.WhitePixel, rect, Nullable(), color)

      // Circles
      | Command2D.FillCircle(center, radius, color, _) ->
        let r = int radius

        let dest =
          Rectangle(
            int(center.X - radius),
            int(center.Y - radius),
            r * 2,
            r * 2
          )

        sb.Draw(res.CircleTex, dest, Nullable(), color)

      // Camera
      | Command2D.BeginCamera(camera, _) -> beginCamera camera &state res
      | Command2D.EndCamera _ -> endCamera(&state, res)

      // Escape Hatches
      | Command2D.DrawImmediate(action, _) -> drawImmediate action &state res
      | Command2D.Clear(color, _) -> sb.GraphicsDevice.Clear(color)

    endCamera(&state, res)

/// <summary>
/// A deferred 2D renderer that sorts commands by layer and executes them
/// via pattern matching on <see cref="T:Mibo.Elmish.Graphics2D.Command2D"/>.
/// </summary>
/// <remarks>
/// <para>
/// Commands are accumulated each frame via the <c>view</c> function into a
/// <see cref="T:Mibo.Elmish.Graphics2D.RenderBuffer2D"/>, sorted by layer, then executed
/// in order through a MonoGame <c>SpriteBatch</c>.
/// </para>
/// <para>
/// The renderer owns a single <c>SpriteBatch</c> (created lazily from the
/// <c>GraphicsDevice</c> registered in the <see cref="T:Mibo.Elmish.GameContext"/>).
/// State-transition commands (<c>BeginCamera</c>, <c>EndCamera</c>, <c>DrawImmediate</c>)
/// flush the batch and re-open it with updated settings.
/// </para>
/// <para>
/// Register via <c>Program.withRenderer</c>:
/// <code lang="fsharp">
/// Program.mkProgram init update
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

  let buffer = new RenderBuffer2D(capacity = 4096)

  let mutable _spriteBatch: SpriteBatch voption = ValueNone
  let mutable _whitePixel: Texture2D voption = ValueNone
  let mutable _circleTex: Texture2D voption = ValueNone
  let mutable _gd: GraphicsDevice voption = ValueNone

  let mutable _camera: Camera2D voption = ValueNone
  let mutable _windowWidth = 0
  let mutable _windowHeight = 0

  let createWhitePixel(gd: GraphicsDevice) =
    let tex = new Texture2D(gd, 1, 1)
    tex.SetData([| Color.White |])
    tex

  let createCircleTex(gd: GraphicsDevice) =
    let size = 64
    let tex = new Texture2D(gd, size, size)
    let data = Array.zeroCreate<Color>(size * size)
    let center = float32 size / 2.0f
    let radius = center - 0.5f

    for y = 0 to size - 1 do
      for x = 0 to size - 1 do
        let dx = float32 x - center
        let dy = float32 y - center
        let dist = sqrt(dx * dx + dy * dy)
        // Sharp circular coverage with a 1-texel anti-aliased edge: fully
        // opaque inside the radius, fully transparent outside, and a single
        // texel of partial coverage at the boundary. This keeps the circle
        // crisp when downscaled (the ball is small), avoiding the diffuse
        // look a wide linear alpha ramp produces under linear filtering.
        let alpha =
          if dist <= radius - 1.0f then
            255uy
          elif dist <= radius then
            let t = (radius - dist) // 0..1 across the boundary texel
            byte(MathHelper.Clamp(t, 0.0f, 1.0f) * 255.0f)
          else
            0uy

        data[y * size + x] <- Color(255uy, 255uy, 255uy, alpha)

    tex.SetData(data)
    tex

  let ensureDevice(gd: GraphicsDevice) =
    match _spriteBatch with
    | ValueNone ->
      _spriteBatch <- ValueSome(new SpriteBatch(gd))
      _whitePixel <- ValueSome(createWhitePixel gd)
      _circleTex <- ValueSome(createCircleTex gd)
      _gd <- ValueSome gd
    | ValueSome _ -> ()

  interface IRenderer<'Model> with
    member _.Draw(ctx, model, _gameTime) =
      _windowWidth <- ctx.WindowWidth
      _windowHeight <- ctx.WindowHeight
      buffer.Clear()

      view ctx model buffer
      buffer.Sort()

      let gd = MonoGameGameContext.getGraphicsDevice ctx
      ensureDevice gd

      match config.ClearColor with
      | ValueSome c -> gd.Clear(c)
      | ValueNone -> ()

      // Open the batch for the frame (analog of raylib's implicit batch being
      // active throughout execute). Camera transitions inside execute will
      // End/re-Begin with the new transform matrix.
      CommandHandlers.beginBatch(
        _spriteBatch.Value,
        match _camera with
        | ValueSome c -> Camera2D.toMatrix c
        | ValueNone -> Matrix.Identity
      )

      let mutable state: CommandHandlers.RendererState = {
        Camera = _camera
        WindowWidth = _windowWidth
        WindowHeight = _windowHeight
      }

      let res: CommandHandlers.RenderResources = {
        SpriteBatch = _spriteBatch.Value
        WhitePixel = _whitePixel.Value
        CircleTex = _circleTex.Value
      }

      CommandHandlers.execute(&state, buffer, res)

      _spriteBatch.Value.End()

      _camera <- state.Camera

  interface IDisposable with
    member _.Dispose() =
      match _spriteBatch with
      | ValueSome sb -> sb.Dispose()
      | ValueNone -> ()

      match _whitePixel with
      | ValueSome t -> t.Dispose()
      | ValueNone -> ()

      match _circleTex with
      | ValueSome t -> t.Dispose()
      | ValueNone -> ()

      (buffer :> IDisposable).Dispose()

/// <summary>Convenience constructors for <see cref="T:Mibo.Elmish.Graphics2D.Renderer2D`1"/>.</summary>
module Renderer2D =

  /// <summary>
  /// Creates a renderer with default configuration (black clear color).
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
