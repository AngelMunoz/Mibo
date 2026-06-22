namespace Mibo.Elmish.Graphics3D

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

/// <summary>Configuration for the <see cref="T:Mibo.Elmish.Graphics3D.Renderer3D`1"/>.</summary>
[<Struct>]
type Renderer3DConfig = {
  /// <summary>
  /// Background clear color applied before rendering commands.
  /// <see cref="F:Microsoft.FSharp.Core.ValueOption`1.ValueNone"/> skips clearing entirely,
  /// which is useful when composing multiple renderers (e.g., 2D overlay on 3D scene).
  /// <see cref="F:Microsoft.FSharp.Core.ValueOption`1.ValueSome"/> clears with the specified color.
  /// </summary>
  ClearColor: Color voption
}

/// <summary>Convenience values and functions for <see cref="T:Mibo.Elmish.Graphics3D.Renderer3DConfig"/>.</summary>
module Renderer3DConfig =

  /// <summary>
  /// Default configuration: black clear color.
  /// </summary>
  let defaults: Renderer3DConfig = {
    ClearColor = ValueSome Microsoft.Xna.Framework.Color.Black
  }

  /// <summary>
  /// Configuration that skips clearing the background.
  /// Use when this renderer composites on top of another renderer's output.
  /// </summary>
  let noClear: Renderer3DConfig = { ClearColor = ValueNone }

/// <summary>
/// A no-operation pipeline that clears the screen and does nothing else.
/// Useful as a placeholder before a real pipeline (e.g., ForwardPipeline) is wired in.
/// </summary>
/// <remarks>
/// Register this when you want to verify that the renderer integrates correctly
/// with <see cref="T:Mibo.Elmish.Program"/> and <see cref="T:Mibo.Elmish.MiboGame"/>.
/// </remarks>
type NoopPipeline() =
  interface IRenderPipeline3D with
    member _.Execute(_, _, _) = ()
    member _.Initialize() = ()
    member _.Shutdown() = ()

/// <summary>
/// A deferred 3D renderer that accumulates commands each frame and executes them
/// through a pluggable <see cref="T:Mibo.Elmish.Graphics3D.IRenderPipeline3D"/>.
/// </summary>
/// <remarks>
/// <para>
/// Commands are accumulated each frame via the <c>view</c> function into a
/// <see cref="T:Mibo.Elmish.Graphics3D.RenderBuffer3D"/>, then passed to the pipeline
/// for execution. The renderer owns the buffer and render target pool lifecycle;
/// the pipeline owns pass order, shader binding, and lighting math.
/// </para>
/// <para>
/// Register via <c>Program.withRenderer</c>:
/// <code lang="fsharp">
/// Program.mkProgram init update view
/// |> Program.withRenderer(fun () -> Renderer3D.create pipeline view)
/// </code>
/// </para>
/// </remarks>
/// <typeparam name="Model">The application model type, passed to the view function.</typeparam>
type Renderer3D<'Model>
  (
    view: GameContext -> 'Model -> RenderBuffer3D -> unit,
    pipeline: IRenderPipeline3D,
    config: Renderer3DConfig
  ) =

  let buffer = new RenderBuffer3D(capacity = 4096)

  let mutable _rtPool: RenderTargetPool3D voption = ValueNone

  do pipeline.Initialize()

  interface IRenderer<'Model> with
    member _.Draw(ctx, model, _gameTime) =
      let gd = MonoGameGameContext.getGraphicsDevice ctx

      match _rtPool with
      | ValueNone -> _rtPool <- ValueSome(new RenderTargetPool3D(gd))
      | ValueSome _ -> ()

      let rtPool = _rtPool.Value

      match config.ClearColor with
      | ValueSome c -> gd.Clear(c)
      | ValueNone -> ()

      buffer.Clear()

      // Wrapped in try/finally so pooled render targets are always released
      // (and the back-buffer restored) even if view or the pipeline throws —
      // otherwise an exception leaves the RTs in the pool's inUse list and it
      // allocates new targets every frame, leaking GPU memory. Mirrors Renderer2D.
      try
        view ctx model buffer
        pipeline.Execute(ctx, buffer, rtPool)
      finally
        (rtPool :> IRenderTargetPool3D).ReleaseAll()

  interface IDisposable with
    member _.Dispose() =
      pipeline.Shutdown()
      (buffer :> IDisposable).Dispose()

      match _rtPool with
      | ValueSome pool -> (pool :> IDisposable).Dispose()
      | ValueNone -> ()

/// <summary>Convenience constructors for <see cref="T:Mibo.Elmish.Graphics3D.Renderer3D`1"/>.</summary>
module Renderer3D =

  /// <summary>
  /// Creates a renderer with default configuration (black clear color).
  /// </summary>
  /// <param name="pipeline">The 3D rendering pipeline that interprets commands.</param>
  /// <param name="view">
  /// The view function that populates the render buffer each frame.
  /// </param>
  let create
    (pipeline: IRenderPipeline3D)
    (view: GameContext -> 'Model -> RenderBuffer3D -> unit)
    : IRenderer<'Model> =
    new Renderer3D<'Model>(view, pipeline, Renderer3DConfig.defaults)

  /// <summary>
  /// Creates a renderer with custom configuration.
  /// </summary>
  /// <param name="config">The renderer configuration.</param>
  /// <param name="pipeline">The 3D rendering pipeline that interprets commands.</param>
  /// <param name="view">
  /// The view function that populates the render buffer each frame.
  /// </param>
  let createWith
    (config: Renderer3DConfig)
    (pipeline: IRenderPipeline3D)
    (view: GameContext -> 'Model -> RenderBuffer3D -> unit)
    : IRenderer<'Model> =
    new Renderer3D<'Model>(view, pipeline, config)
