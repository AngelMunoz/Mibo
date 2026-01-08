namespace Mibo.Elmish.Graphics2D

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open Mibo.Elmish

/// <summary>Unit of measure for render layer ordering.</summary>
/// <remarks>Lower values are drawn first (background), higher values drawn last (foreground).</remarks>
[<Measure>]
type RenderLayer

/// <summary>Convenience alias for a render buffer keyed by <see cref="T:Mibo.Elmish.Graphics2D.RenderLayer"/>.</summary>
/// <remarks>This preserves the simple <c>RenderBuffer&lt;'Cmd&gt;</c> API at call sites while the core buffer remains generic (<see cref="T:Mibo.Elmish.RenderBuffer`2"/>).</remarks>
type RenderBuffer<'Cmd> = RenderBuffer<int<RenderLayer>, 'Cmd>

/// <summary>A 2D render command.</summary>
/// <remarks>These commands are queued to a <see cref="T:Mibo.Elmish.RenderBuffer`1"/> and executed by <see cref="T:Mibo.Elmish.Graphics2D.Batch2DRenderer`1"/>.</remarks>
[<Struct>]
type RenderCmd2D =
  /// Changes the camera transform for subsequent draws.
  | SetCamera of camera: Camera
  /// Draws a textured quad.
  | DrawTexture of
    texture: Texture2D *
    dest: Rectangle *
    source: Nullable<Rectangle> *
    color: Color *
    rotation: float32 *
    origin: Vector2 *
    effects: SpriteEffects *
    depth: float32

/// <summary>Configuration for <see cref="T:Mibo.Elmish.Graphics2D.Batch2DRenderer`1"/>.</summary>
/// <remarks>These settings configure the *rendering pass* (Clear + SpriteBatch.Begin parameters), not individual sprites (those are controlled by <see cref="T:Mibo.Elmish.Graphics2D.RenderCmd2D"/> / <see cref="T:Mibo.Elmish.Graphics2D.Draw2DBuilder"/>).</remarks>
[<Struct>]
type Batch2DConfig = {
  /// Optional color to clear the screen with before drawing.
  ClearColor: Color voption
  /// Whether to sort the command buffer by `RenderLayer` before issuing draws.
  /// Keep this enabled if you rely on `RenderLayer` for deterministic ordering.
  SortCommands: bool
  /// SpriteBatch sort mode (Deferred, Immediate, etc).
  SortMode: SpriteSortMode
  /// Blend state for sprite drawing.
  BlendState: BlendState
  /// Sampler state for texture filtering.
  SamplerState: SamplerState
  /// Depth stencil state.
  DepthStencilState: DepthStencilState
  /// Rasterizer state.
  RasterizerState: RasterizerState
  /// Optional shader effect for all sprites.
  Effect: Effect
  /// Global transform matrix for the batch.
  /// Note: If using `SetCamera` commands, this initial matrix might be overridden during the pass.
  TransformMatrix: Matrix voption
}

module Batch2DConfig =

  /// Sensible defaults for a typical 2D game.
  /// Matches the current historical behavior of this renderer (clears CornflowerBlue).
  let defaults: Batch2DConfig = {
    ClearColor = ValueSome Color.CornflowerBlue
    SortCommands = true
    SortMode = SpriteSortMode.Deferred
    BlendState = BlendState.AlphaBlend
    SamplerState = SamplerState.LinearClamp
    DepthStencilState = DepthStencilState.None
    RasterizerState = RasterizerState.CullCounterClockwise
    Effect = null
    TransformMatrix = ValueNone
  }

/// <summary>Standard 2D Renderer using <see cref="T:Microsoft.Xna.Framework.Graphics.SpriteBatch"/>.</summary>
type Batch2DRenderer<'Model>
  (
    game: Game,
    config: Batch2DConfig,
    [<InlineIfLambda>] view:
      GameContext * 'Model * RenderBuffer<RenderCmd2D> -> unit
  ) =
  let mutable spriteBatch: SpriteBatch = null
  let buffer = RenderBuffer<RenderCmd2D>()

  interface IRenderer<'Model> with
    member _.Draw(ctx: GameContext, model: 'Model, gameTime: GameTime) =
      if isNull spriteBatch then
        spriteBatch <- new SpriteBatch(ctx.GraphicsDevice)

      config.ClearColor |> ValueOption.iter(fun c -> ctx.GraphicsDevice.Clear c)

      buffer.Clear()
      view(ctx, model, buffer)

      if config.SortCommands then
        buffer.Sort()

      let mutable currentTransform =
        match config.TransformMatrix with
        | ValueSome m -> Nullable m
        | ValueNone -> Nullable()

      // Begin the initial batch
      spriteBatch.Begin(
        config.SortMode,
        config.BlendState,
        config.SamplerState,
        config.DepthStencilState,
        config.RasterizerState,
        config.Effect,
        currentTransform
      )

      let mutable isBatching = true

      for i = 0 to buffer.Count - 1 do
        let struct (_, cmd) = buffer.Item(i)

        match cmd with
        | SetCamera cam ->
          if isBatching then
            spriteBatch.End()
            isBatching <- false

          currentTransform <- Nullable cam.View
          // Restart batch with new transform
          spriteBatch.Begin(
            config.SortMode,
            config.BlendState,
            config.SamplerState,
            config.DepthStencilState,
            config.RasterizerState,
            config.Effect,
            currentTransform
          )

          isBatching <- true

        | DrawTexture(tex, dest, src, color, rot, origin, fx, depth) ->
          if not isBatching then
            // Should not happen if logic above is correct, but safe guard
            spriteBatch.Begin(
              config.SortMode,
              config.BlendState,
              config.SamplerState,
              config.DepthStencilState,
              config.RasterizerState,
              config.Effect,
              currentTransform
            )

            isBatching <- true

          if src.HasValue then
            spriteBatch.Draw(
              tex,
              dest,
              src.Value,
              color,
              rot,
              origin,
              fx,
              depth
            )
          else
            spriteBatch.Draw(tex, dest, color)

      if isBatching then
        spriteBatch.End()

module Batch2DRenderer =
  /// <summary>Creates a standard 2D renderer.</summary>
  let inline create<'Model>
    ([<InlineIfLambda>] view:
      GameContext -> 'Model -> RenderBuffer<RenderCmd2D> -> unit)
    (game: Game)
    =
    Batch2DRenderer<'Model>(
      game,
      Batch2DConfig.defaults,
      fun (ctx, model, buffer) -> view ctx model buffer
    )
    :> IRenderer<'Model>

  /// <summary>Creates a 2D renderer with custom configuration.</summary>
  let inline createWithConfig<'Model>
    (config: Batch2DConfig)
    ([<InlineIfLambda>] view:
      GameContext -> 'Model -> RenderBuffer<RenderCmd2D> -> unit)
    (game: Game)
    =
    Batch2DRenderer<'Model>(
      game,
      config,
      fun (ctx, model, buffer) -> view ctx model buffer
    )
    :> IRenderer<'Model>


/// <summary>Fluent builder for <see cref="T:Mibo.Elmish.Graphics2D.RenderCmd2D"/>.</summary>
[<Struct>]
type Draw2DBuilder = {
  Texture: Texture2D
  Dest: Rectangle
  Source: Nullable<Rectangle>
  Color: Color
  Rotation: float32
  Origin: Vector2
  Effects: SpriteEffects
  Depth: float32
  Layer: int<RenderLayer>
}

/// <summary>Functions for building and submitting 2D draw commands.</summary>
module Draw2D =
  /// <summary>Starts a sprite drawing command.</summary>
  let sprite tex dest = {
    Texture = tex
    Dest = dest
    Source = Nullable()
    Color = Color.White
    Rotation = 0.0f
    Origin = Vector2.Zero
    Effects = SpriteEffects.None
    Depth = 0.0f
    Layer = 0<RenderLayer>
  }

  let withSource src (b: Draw2DBuilder) = { b with Source = Nullable src }
  let withColor col (b: Draw2DBuilder) = { b with Color = col }
  let atLayer layer (b: Draw2DBuilder) = { b with Layer = layer }

  /// <summary>Submits the draw command to the renderer's buffer.</summary>
  let submit (buffer: RenderBuffer<RenderCmd2D>) (b: Draw2DBuilder) =
    buffer.Add(
      b.Layer,
      DrawTexture(
        b.Texture,
        b.Dest,
        b.Source,
        b.Color,
        b.Rotation,
        b.Origin,
        b.Effects,
        b.Depth
      )
    )

  /// <summary>Submits a camera change command to the buffer.</summary>
  let camera
    (cam: Camera)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer<RenderCmd2D>)
    =
    buffer.Add(layer, SetCamera cam)
