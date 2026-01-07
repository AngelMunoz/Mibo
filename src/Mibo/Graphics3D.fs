namespace Mibo.Elmish.Graphics3D

open System
open System.Runtime.CompilerServices
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

// --- 3D Rendering Implementation ---

/// Coarse rendering pass selection for 3D.
///
/// - Opaque: depth testing + opaque blending. Can be depth-sorted for performance.
/// - Transparent: typically depth read + alpha blending. Must be sorted back-to-front.
[<Struct>]
type RenderPass =
  | Opaque
  | Transparent

[<Struct>]
type RenderCmd3D =
  /// Set viewport for multi-camera rendering (split-screen, minimaps, etc).
  | SetViewport of viewport: Viewport
  /// Clear the render target. Use between cameras in multi-camera setups.
  | ClearTarget of clearColor: Color voption * clearDepth: bool
  | SetCamera of camera: Camera
  | DrawMesh of
    pass: RenderPass *
    model: Model *
    transform: Matrix *
    diffuseColor: Color voption *
    texture: Texture2D voption

  /// Escape hatch: run an arbitrary draw function.
  ///
  /// The function is invoked with the current camera View/Projection matrices (as last set
  /// by `SetCamera`) so userland can integrate custom effects without forking the renderer.
  | DrawCustom of draw: (GameContext * Matrix * Matrix -> unit)

  /// Contract for skinned/animated models.
  ///
  /// If the underlying model uses `SkinnedEffect`, this renderer will apply `bones` via
  /// `SkinnedEffect.SetBoneTransforms`.
  | DrawSkinned of
    pass: RenderPass *
    model: Model *
    transform: Matrix *
    bones: Matrix[] *
    diffuseColor: Color voption *
    texture: Texture2D voption

/// Convenience alias for a render buffer for 3D commands.
///
/// 3D rendering typically does not rely on a 2D-style render-layer ordering.
/// We preserve *submission order* (do not sort), so the key is `unit`.
type RenderBuffer<'Cmd> = RenderBuffer<unit, 'Cmd>

/// Configuration for `Batch3DRenderer`.
///
/// This controls the rendering *pass* defaults (clears + device state), not per-mesh material setup.
[<Struct>]
type Batch3DConfig = {
  /// Optional color buffer clear.
  ClearColor: Color voption
  /// Whether to clear the depth buffer before rendering.
  ClearDepth: bool

  /// Whether to restore the previous GraphicsDevice states after rendering.
  ///
  /// This makes composition with other renderers more predictable (at a small cost).
  RestoreDeviceStates: bool

  OpaqueBlendState: BlendState
  OpaqueDepthStencilState: DepthStencilState

  TransparentBlendState: BlendState
  TransparentDepthStencilState: DepthStencilState

  RasterizerState: RasterizerState

  /// If true, opaque draws are sorted front-to-back by distance to camera.
  /// This can reduce overdraw.
  SortOpaqueFrontToBack: bool

  /// Enables caching of BasicEffect lighting setup.
  ///
  /// If you want to animate lights every frame, disable caching and set `LightingSetup`.
  CacheBasicEffectLighting: bool

  /// Optional lighting setup hook for BasicEffect.
  /// If not provided, a simple default lighting setup is used.
  LightingSetup: (BasicEffect -> unit) voption
}

module Batch3DConfig =

  let defaults: Batch3DConfig = {
    ClearColor = ValueNone
    ClearDepth = true
    RestoreDeviceStates = false

    OpaqueBlendState = BlendState.Opaque
    OpaqueDepthStencilState = DepthStencilState.Default

    TransparentBlendState = BlendState.AlphaBlend
    // A common default: depth test on, depth writes off.
    TransparentDepthStencilState = DepthStencilState.DepthRead

    RasterizerState = RasterizerState.CullCounterClockwise

    SortOpaqueFrontToBack = false
    CacheBasicEffectLighting = true
    LightingSetup = ValueNone
  }

/// Standard 3D Renderer using BasicEffect
type Batch3DRenderer<'Model>
  (
    game: Game,
    config: Batch3DConfig,
    [<InlineIfLambda>] view:
      GameContext * 'Model * RenderBuffer<RenderCmd3D> -> unit
  ) =

  let buffer = RenderBuffer<RenderCmd3D>()

  // Default Camera state
  let mutable viewMatrix = Matrix.Identity
  let mutable projectionMatrix = Matrix.Identity
  let mutable warnedMissingCamera = false

  // Cache of BasicEffect instances that have had lighting applied.
  // Use a ConditionalWeakTable so we don't keep effects alive.
  let configuredBasicEffects = ConditionalWeakTable<BasicEffect, obj>()

  // Default lighting setup (kept intentionally simple).
  let defaultLighting(effect: BasicEffect) =
    effect.LightingEnabled <- true
    effect.AmbientLightColor <- Vector3(0.2f, 0.2f, 0.2f)
    effect.DirectionalLight0.Enabled <- true
    effect.DirectionalLight0.DiffuseColor <- Vector3(0.8f, 0.8f, 0.8f)
    effect.DirectionalLight0.Direction <- Vector3(-1.0f, -1.0f, -1.0f)
    effect.DirectionalLight0.SpecularColor <- Vector3.Zero

  let applyBasicEffectLighting(effect: BasicEffect) =
    let setup =
      match config.LightingSetup with
      | ValueSome f -> f
      | ValueNone -> defaultLighting

    if config.CacheBasicEffectLighting then
      // Apply at most once per BasicEffect instance.
      match configuredBasicEffects.TryGetValue(effect) with
      | true, _ -> ()
      | false, _ ->
        setup effect
        configuredBasicEffects.Add(effect, box())
    else
      // Dynamic lights: apply every time.
      setup effect

  let getCameraWorldPosition(view: Matrix) : Vector3 =
    // Camera position is translation of inverse view.
    let inv = Matrix.Invert(view)
    inv.Translation

  // Scratch buffers to avoid per-frame allocations.
  let opaque = ResizeArray<struct (float32 * RenderCmd3D)>(1024)
  let transparent = ResizeArray<struct (float32 * RenderCmd3D)>(1024)

  let clearLists() =
    opaque.Clear()
    transparent.Clear()

  interface IRenderer<'Model> with
    member _.Draw(ctx: GameContext, model: 'Model, gameTime: GameTime) =
      // Note: We don't clear here by default if mixing 2D/3D,
      // but usually 3D is the background.
      // game.GraphicsDevice.Clear(ClearOptions.Target ||| ClearOptions.DepthBuffer, Color.Black, 1.0f, 0)

      // Optionally save/restore device states to play nice with other renderers.
      let gd = game.GraphicsDevice

      let prevBlend = gd.BlendState
      let prevDepth = gd.DepthStencilState
      let prevRasterizer = gd.RasterizerState

      // Clear
      match config.ClearColor with
      | ValueSome c ->
        if config.ClearDepth then
          gd.Clear(ClearOptions.Target ||| ClearOptions.DepthBuffer, c, 1.0f, 0)
        else
          gd.Clear(ClearOptions.Target, c, 1.0f, 0)
      | ValueNone ->
        if config.ClearDepth then
          gd.Clear(ClearOptions.DepthBuffer, Color.Black, 1.0f, 0)
        else
          ()

      // Default states (will be refined per pass)
      gd.RasterizerState <- config.RasterizerState

      buffer.Clear()
      view(ctx, model, buffer)

      // NOTE: We intentionally do NOT sort in 3D.
      // Sorting by a single integer layer is generally the wrong abstraction for 3D
      // (opaque uses depth testing; transparent often needs camera-distance sorting).
      // Preserving submission order also avoids unstable ordering when keys are equal.

      // Detect missing camera once. Identity view/projection will produce confusing results.
      if not warnedMissingCamera then
        let mutable hasCamera = false

        for i = 0 to buffer.Count - 1 do
          let struct (_, cmd) = buffer.Item(i)

          match cmd with
          | SetCamera _ -> hasCamera <- true
          | _ -> ()

        if not hasCamera then
          warnedMissingCamera <- true

          Console.WriteLine(
            "[Mibo] Batch3DRenderer: no camera submitted this frame; using Identity view/projection."
          )

      // Partition the submission-ordered command stream into passes.
      // Camera and Custom draws preserve their relative ordering by being executed in-stream
      // (Custom) or applied to subsequent draws (Camera).
      clearLists()
      let mutable camPos = getCameraWorldPosition viewMatrix

      for i = 0 to buffer.Count - 1 do
        let struct (_, cmd) = buffer.Item(i)

        match cmd with
        | SetViewport vp ->
          // Multi-camera support: allow user to change viewport mid-frame
          gd.Viewport <- vp

        | ClearTarget(colorOpt, clearDepth) ->
          // Multi-camera support: clear between camera renders
          match colorOpt, clearDepth with
          | ValueSome c, true ->
            gd.Clear(ClearOptions.Target ||| ClearOptions.DepthBuffer, c, 1.0f, 0)
          | ValueSome c, false ->
            gd.Clear(ClearOptions.Target, c, 1.0f, 0)
          | ValueNone, true ->
            gd.Clear(ClearOptions.DepthBuffer, Color.Black, 1.0f, 0)
          | ValueNone, false -> ()

        | SetCamera cam ->
          viewMatrix <- cam.View
          projectionMatrix <- cam.Projection
          camPos <- getCameraWorldPosition viewMatrix

        | DrawCustom draw ->
          // Execute immediately to preserve submission ordering.
          draw(ctx, viewMatrix, projectionMatrix)

        | DrawMesh(pass, m, transform, _colorOpt, _texOpt) ->
          let pos = transform.Translation
          let distSq = Vector3.DistanceSquared(camPos, pos)

          match pass with
          | Opaque -> opaque.Add(struct (distSq, cmd))
          | Transparent -> transparent.Add(struct (distSq, cmd))

        | DrawSkinned(pass, m, transform, _bones, _colorOpt, _texOpt) ->
          let pos = transform.Translation
          let distSq = Vector3.DistanceSquared(camPos, pos)

          match pass with
          | Opaque -> opaque.Add(struct (distSq, cmd))
          | Transparent -> transparent.Add(struct (distSq, cmd))

      // Optional opaque sorting (front-to-back).
      if config.SortOpaqueFrontToBack then
        opaque.Sort
          { new Collections.Generic.IComparer<struct (float32 * RenderCmd3D)> with
              member _.Compare(struct (da, _), struct (db, _)) =
                // Ascending: near -> far
                compare da db
          }

      // Transparent must be back-to-front.
      transparent.Sort
        { new Collections.Generic.IComparer<struct (float32 * RenderCmd3D)> with
            member _.Compare(struct (da, _), struct (db, _)) =
              // Descending: far -> near
              compare db da
        }

      let drawModelMeshes
        (pass: RenderPass)
        (m: Model)
        (transform: Matrix)
        (colorOpt: Color voption)
        (texOpt: Texture2D voption)
        (bonesOpt: Matrix[] voption)
        =
        // Set pass-specific device state.
        match pass with
        | Opaque ->
          gd.DepthStencilState <- config.OpaqueDepthStencilState
          gd.BlendState <- config.OpaqueBlendState
        | Transparent ->
          gd.DepthStencilState <- config.TransparentDepthStencilState
          gd.BlendState <- config.TransparentBlendState

        for mesh in m.Meshes do
          for effect in mesh.Effects do
            match effect with
            | :? SkinnedEffect as se ->
              se.World <- transform
              se.View <- viewMatrix
              se.Projection <- projectionMatrix

              bonesOpt
              |> ValueOption.iter(fun bones ->
                try
                  se.SetBoneTransforms(bones)
                with _ ->
                  ())

              match colorOpt with
              | ValueSome c -> se.DiffuseColor <- c.ToVector3()
              | ValueNone -> ()

              match texOpt with
              | ValueSome t -> se.Texture <- t
              | ValueNone -> ()

            | :? BasicEffect as be ->
              be.World <- transform
              be.View <- viewMatrix
              be.Projection <- projectionMatrix

              applyBasicEffectLighting be

              match colorOpt with
              | ValueSome c -> be.DiffuseColor <- c.ToVector3()
              | ValueNone -> ()

              match texOpt with
              | ValueSome t ->
                be.TextureEnabled <- true
                be.Texture <- t
              | ValueNone -> ()

            | _ -> ()

          mesh.Draw()

      // Execute passes
      for i = 0 to opaque.Count - 1 do
        let struct (_, cmd) = opaque[i]

        match cmd with
        | DrawMesh(pass, m, transform, colorOpt, texOpt) ->
          drawModelMeshes pass m transform colorOpt texOpt ValueNone
        | DrawSkinned(pass, m, transform, bones, colorOpt, texOpt) ->
          drawModelMeshes pass m transform colorOpt texOpt (ValueSome bones)
        | _ -> ()

      for i = 0 to transparent.Count - 1 do
        let struct (_, cmd) = transparent[i]

        match cmd with
        | DrawMesh(pass, m, transform, colorOpt, texOpt) ->
          drawModelMeshes pass m transform colorOpt texOpt ValueNone
        | DrawSkinned(pass, m, transform, bones, colorOpt, texOpt) ->
          drawModelMeshes pass m transform colorOpt texOpt (ValueSome bones)
        | _ -> ()

      if config.RestoreDeviceStates then
        gd.BlendState <- prevBlend
        gd.DepthStencilState <- prevDepth
        gd.RasterizerState <- prevRasterizer

module Batch3DRenderer =
  let inline create<'Model>
    ([<InlineIfLambda>] view:
      GameContext -> 'Model -> RenderBuffer<RenderCmd3D> -> unit)
    (game: Game)
    =
    Batch3DRenderer<'Model>(
      game,
      Batch3DConfig.defaults,
      fun (ctx, model, buffer) -> view ctx model buffer
    )
    :> IRenderer<'Model>

  let inline createWithConfig<'Model>
    (config: Batch3DConfig)
    ([<InlineIfLambda>] view:
      GameContext -> 'Model -> RenderBuffer<RenderCmd3D> -> unit)
    (game: Game)
    =
    Batch3DRenderer<'Model>(
      game,
      config,
      fun (ctx, model, buffer) -> view ctx model buffer
    )
    :> IRenderer<'Model>


// --- Fluent Builder API ---

[<Struct>]
type Draw3DBuilder = {
  Model: Model
  Transform: Matrix
  Color: Color voption
  Texture: Texture2D voption
  Pass: RenderPass
}

module Draw3D =
  let mesh model transform = {
    Model = model
    Transform = transform
    Color = ValueNone
    Texture = ValueNone
    Pass = Opaque
  }

  let meshTransparent model transform = {
    mesh model transform with
        Pass = Transparent
  }

  let inPass pass (b: Draw3DBuilder) = { b with Pass = pass }

  let withColor col (b: Draw3DBuilder) = { b with Color = ValueSome col }
  let withTexture tex (b: Draw3DBuilder) = { b with Texture = ValueSome tex }

  let submit (buffer: RenderBuffer<RenderCmd3D>) (b: Draw3DBuilder) =
    buffer.Add((), DrawMesh(b.Pass, b.Model, b.Transform, b.Color, b.Texture))

  let camera (cam: Camera) (buffer: RenderBuffer<RenderCmd3D>) =
    buffer.Add((), SetCamera cam)

  /// Set viewport for multi-camera rendering (split-screen, minimaps, etc).
  let viewport (vp: Viewport) (buffer: RenderBuffer<RenderCmd3D>) =
    buffer.Add((), SetViewport vp)

  /// Clear color and/or depth buffer. Use between cameras in multi-camera setups.
  let clear (color: Color voption) (clearDepth: bool) (buffer: RenderBuffer<RenderCmd3D>) =
    buffer.Add((), ClearTarget(color, clearDepth))

  let custom
    (draw: GameContext * Matrix * Matrix -> unit)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    buffer.Add((), DrawCustom draw)

  let skinned
    (pass: RenderPass)
    (model: Model)
    (transform: Matrix)
    (bones: Matrix[])
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    buffer.Add(
      (),
      DrawSkinned(pass, model, transform, bones, ValueNone, ValueNone)
    )

  let skinnedWithColor
    (pass: RenderPass)
    (color: Color)
    (model: Model)
    (transform: Matrix)
    (bones: Matrix[])
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    buffer.Add(
      (),
      DrawSkinned(pass, model, transform, bones, ValueSome color, ValueNone)
    )
