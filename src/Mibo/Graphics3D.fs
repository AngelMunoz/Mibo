namespace Mibo.Elmish.Graphics3D

open System
open System.Runtime.CompilerServices
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

// --- 3D Rendering Implementation ---

/// <summary>Coarse rendering pass selection for 3D.</summary>
/// <remarks>
/// <para><see cref="F:Mibo.Elmish.Graphics3D.RenderPass.Opaque"/>: depth testing + opaque blending. Can be depth-sorted for performance.</para>
/// <para><see cref="F:Mibo.Elmish.Graphics3D.RenderPass.Transparent"/>: typically depth read + alpha blending. Must be sorted back-to-front.</para>
/// </remarks>
[<Struct>]
type RenderPass =
  | Opaque
  | Transparent

/// <summary>Standard transformation matrices used during effect setup.</summary>
[<Struct>]
type EffectContext = {
  World: Matrix
  View: Matrix
  Projection: Matrix
}

/// <summary>Callback for configuring an effect before a draw operation.</summary>
type EffectSetup = Effect -> EffectContext -> unit

/// <summary>A 3D render command.</summary>
/// <remarks>These commands are queued to a <see cref="T:Mibo.Elmish.RenderBuffer`1"/> and executed by <see cref="T:Mibo.Elmish.Graphics3D.Batch3DRenderer`1"/>.</remarks>
[<Struct>]
type RenderCmd3D =
  /// <summary>Set viewport for multi-camera rendering (split-screen, minimaps, etc).</summary>
  | SetViewport of viewport: Viewport
  /// <summary>Clear the render target. Use between cameras in multi-camera setups.</summary>
  | ClearTarget of clearColor: Color voption * clearDepth: bool
  /// <summary>Sets the camera for subsequent draws.</summary>
  | SetCamera of camera: Camera
  /// <summary>Draws a 3D mesh from a <see cref="T:Microsoft.Xna.Framework.Graphics.Model"/>.</summary>
  | DrawMesh of
    pass: RenderPass *
    model: Model *
    transform: Matrix *
    diffuseColor: Color voption *
    texture: Texture2D voption *
    setup: EffectSetup voption

  /// <summary>Escape hatch: run an arbitrary draw function.</summary>
  /// <remarks>The function is invoked with the current camera View/Projection matrices (as last set by <c>SetCamera</c>) so userland can integrate custom effects without forking the renderer.</remarks>
  | DrawCustom of draw: (GameContext * Matrix * Matrix -> unit)

  /// <summary>Contract for skinned/animated models.</summary>
  /// <remarks>If the underlying model uses <see cref="T:Microsoft.Xna.Framework.Graphics.SkinnedEffect"/>, this renderer will apply <c>bones</c> via <c>SkinnedEffect.SetBoneTransforms</c>.</remarks>
  | DrawSkinned of
    pass: RenderPass *
    model: Model *
    transform: Matrix *
    bones: Matrix[] *
    diffuseColor: Color voption *
    texture: Texture2D voption *
    setup: EffectSetup voption

  /// <summary>Draw an axis-aligned textured quad in 3D space.</summary>
  /// <remarks>Caller is responsible for setting up the effect (View, Projection, Texture, etc).</remarks>
  | DrawQuad of effect: Effect * position: Vector3 * dQSize: Vector2

  /// <summary>Draw a camera-facing billboard (particles, sprites in 3D).</summary>
  /// <remarks>Caller is responsible for setting up the effect.</remarks>
  | DrawBillboard of
    effect: Effect *
    position: Vector3 *
    size: Vector2 *
    rotation: float32 *
    color: Color

/// <summary>Convenience alias for a render buffer for 3D commands.</summary>
/// <remarks>3D rendering typically does not rely on a 2D-style render-layer ordering. We preserve submission order (do not sort), so the key is <c>unit</c>.</remarks>
type RenderBuffer<'Cmd> = RenderBuffer<unit, 'Cmd>

/// <summary>Configuration for <see cref="T:Mibo.Elmish.Graphics3D.Batch3DRenderer`1"/>.</summary>
/// <remarks>This controls the rendering pass defaults (clears + device state), not per-mesh material setup.</remarks>
[<Struct>]
type Batch3DConfig = {
  /// Optional color buffer clear.
  ClearColor: Color voption
  /// Whether to clear the depth buffer before rendering.
  ClearDepth: bool

  /// <summary>Whether to restore the previous <see cref="T:Microsoft.Xna.Framework.Graphics.GraphicsDevice"/> states after rendering.</summary>
  /// <remarks>This makes composition with other renderers more predictable (at a small cost).</remarks>
  RestoreDeviceStates: bool

  OpaqueBlendState: BlendState
  OpaqueDepthStencilState: DepthStencilState

  TransparentBlendState: BlendState
  TransparentDepthStencilState: DepthStencilState

  RasterizerState: RasterizerState

  /// <summary>If true, opaque draws are sorted front-to-back by distance to camera.</summary>
  /// <remarks>This can reduce overdraw.</remarks>
  SortOpaqueFrontToBack: bool

  /// <summary>Enables caching of <see cref="T:Microsoft.Xna.Framework.Graphics.BasicEffect"/> lighting setup.</summary>
  /// <remarks>If you want to animate lights every frame, disable caching.</remarks>
  CacheBasicEffectLighting: bool
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
  }

module StandardEffects =
  let defaultLighting(effect: BasicEffect) =
    effect.LightingEnabled <- true
    effect.AmbientLightColor <- Vector3(0.2f, 0.2f, 0.2f)
    effect.DirectionalLight0.Enabled <- true
    effect.DirectionalLight0.DiffuseColor <- Vector3(0.8f, 0.8f, 0.8f)
    effect.DirectionalLight0.Direction <- Vector3(-1.0f, -1.0f, -1.0f)
    effect.DirectionalLight0.SpecularColor <- Vector3.Zero

/// <summary>Standard 3D Renderer using <see cref="T:Microsoft.Xna.Framework.Graphics.BasicEffect"/>.</summary>
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

  let applyBasicEffectLighting(effect: BasicEffect) =
    if config.CacheBasicEffectLighting then
      // Apply at most once per BasicEffect instance.
      match configuredBasicEffects.TryGetValue(effect) with
      | true, _ -> ()
      | false, _ ->
        StandardEffects.defaultLighting effect
        configuredBasicEffects.Add(effect, box())
    else
      // Dynamic lights: apply every time.
      StandardEffects.defaultLighting effect

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

  // Batchers for quads and billboards
  let mutable quadBatch = QuadBatch.create(game.GraphicsDevice)
  let mutable billboardBatch = BillboardBatch.create(game.GraphicsDevice)

  interface IDisposable with
    member _.Dispose() =
      QuadBatch.dispose &quadBatch
      BillboardBatch.dispose &billboardBatch

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

      // Track camera basis vectors for billboards
      let mutable camRight = Vector3.Right
      let mutable camUp = Vector3.Up

      let mutable currentQuadEffect = null
      let mutable currentBillboardEffect = null

      for i = 0 to buffer.Count - 1 do
        let struct (_, cmd) = buffer.Item(i)

        match cmd with
        | SetViewport vp -> gd.Viewport <- vp

        | ClearTarget(colorOpt, clearDepth) ->
          match colorOpt, clearDepth with
          | ValueSome c, true ->
            gd.Clear(
              ClearOptions.Target ||| ClearOptions.DepthBuffer,
              c,
              1.0f,
              0
            )
          | ValueSome c, false -> gd.Clear(ClearOptions.Target, c, 1.0f, 0)
          | ValueNone, true ->
            gd.Clear(ClearOptions.DepthBuffer, Color.Black, 1.0f, 0)
          | ValueNone, false -> ()

        | SetCamera cam ->
          viewMatrix <- cam.View
          projectionMatrix <- cam.Projection
          camPos <- getCameraWorldPosition viewMatrix

          // Extract camera basis from view matrix for billboards
          let invView = Matrix.Invert(viewMatrix)
          camRight <- Vector3(invView.M11, invView.M21, invView.M31)
          camUp <- Vector3(invView.M12, invView.M22, invView.M32)

        | DrawCustom draw -> draw(ctx, viewMatrix, projectionMatrix)

        | DrawMesh(pass, m, transform, _colorOpt, _texOpt, _setupOpt) ->
          let pos = transform.Translation
          let distSq = Vector3.DistanceSquared(camPos, pos)

          match pass with
          | Opaque -> opaque.Add struct (distSq, cmd)
          | Transparent -> transparent.Add struct (distSq, cmd)

        | DrawSkinned(pass, m, transform, _bones, _colorOpt, _texOpt, _setupOpt) ->
          let pos = transform.Translation
          let distSq = Vector3.DistanceSquared(camPos, pos)

          match pass with
          | Opaque -> opaque.Add struct (distSq, cmd)
          | Transparent -> transparent.Add struct (distSq, cmd)

        | DrawQuad(effect, position, size) ->
          if effect <> currentQuadEffect then
            QuadBatch.flush &quadBatch
            QuadBatch.begin' effect &quadBatch
            currentQuadEffect <- effect

          QuadBatch.draw position size &quadBatch

        | DrawBillboard(effect, position, size, rotation, color) ->
          if effect <> currentBillboardEffect then
            BillboardBatch.end' &billboardBatch // flush previous
            BillboardBatch.begin' effect &billboardBatch
            currentBillboardEffect <- effect

          BillboardBatch.draw
            position
            size
            rotation
            color
            camRight
            camUp
            &billboardBatch

      // Flush remaining batches
      QuadBatch.end' &quadBatch
      BillboardBatch.end' &billboardBatch

      // Optional opaque sorting (front-to-back).
      if config.SortOpaqueFrontToBack then
        opaque.Sort
          { new Collections.Generic.IComparer<struct (float32 * RenderCmd3D)> with
              member _.Compare(struct (da, _), struct (db, _)) = compare da db
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
        (setupOpt: EffectSetup voption)
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
          for part in mesh.MeshParts do
            let effect = part.Effect

            match setupOpt with
            | ValueSome setup ->
              setup effect {
                World = transform
                View = viewMatrix
                Projection = projectionMatrix
              }
            | ValueNone -> ()

            let inline setWvp(e: ^T) =
              if ValueOption.isNone setupOpt then
                (^T: (member set_World: Matrix -> unit) (e, transform))
                (^T: (member set_View: Matrix -> unit) (e, viewMatrix))

                (^T: (member set_Projection: Matrix -> unit) (e,
                                                              projectionMatrix))

            match effect with
            | :? BasicEffect as be ->
              setWvp be

              if config.CacheBasicEffectLighting then
                match configuredBasicEffects.TryGetValue(be) with
                | true, _ -> ()
                | false, _ ->
                  StandardEffects.defaultLighting be
                  configuredBasicEffects.Add(be, box())
              else
                StandardEffects.defaultLighting be

              colorOpt
              |> ValueOption.iter(fun c -> be.DiffuseColor <- c.ToVector3())

              texOpt
              |> ValueOption.iter(fun t ->
                be.TextureEnabled <- true
                be.Texture <- t)

            | :? SkinnedEffect as se ->
              setWvp se
              bonesOpt |> ValueOption.iter se.SetBoneTransforms

              colorOpt
              |> ValueOption.iter(fun c -> se.DiffuseColor <- c.ToVector3())

              texOpt |> ValueOption.iter(fun t -> se.Texture <- t)

            | _ -> ()

            mesh.Draw()

      // Execute passes
      for i = 0 to opaque.Count - 1 do
        let struct (_, cmd) = opaque[i]

        match cmd with
        | DrawMesh(pass, m, transform, colorOpt, texOpt, setupOpt) ->
          drawModelMeshes pass m transform colorOpt texOpt ValueNone setupOpt
        | DrawSkinned(pass, m, transform, bones, colorOpt, texOpt, setupOpt) ->
          drawModelMeshes
            pass
            m
            transform
            colorOpt
            texOpt
            (ValueSome bones)
            setupOpt
        | _ -> ()

      for i = 0 to transparent.Count - 1 do
        let struct (_, cmd) = transparent[i]

        match cmd with
        | DrawMesh(pass, m, transform, colorOpt, texOpt, setupOpt) ->
          drawModelMeshes pass m transform colorOpt texOpt ValueNone setupOpt
        | DrawSkinned(pass, m, transform, bones, colorOpt, texOpt, setupOpt) ->
          drawModelMeshes
            pass
            m
            transform
            colorOpt
            texOpt
            (ValueSome bones)
            setupOpt
        | _ -> ()

      if config.RestoreDeviceStates then
        gd.BlendState <- prevBlend
        gd.DepthStencilState <- prevDepth
        gd.RasterizerState <- prevRasterizer

module Batch3DRenderer =
  /// <summary>Creates a standard 3D renderer.</summary>
  let inline create<'Model>
    ([<InlineIfLambda>] view:
      GameContext -> 'Model -> RenderBuffer<RenderCmd3D> -> unit)
    (game: Game)
    =
    new Batch3DRenderer<'Model>(
      game,
      Batch3DConfig.defaults,
      fun (ctx, model, buffer) -> view ctx model buffer
    )
    :> IRenderer<'Model>

  /// <summary>Creates a 3D renderer with custom configuration.</summary>
  let inline createWithConfig<'Model>
    (config: Batch3DConfig)
    ([<InlineIfLambda>] view:
      GameContext -> 'Model -> RenderBuffer<RenderCmd3D> -> unit)
    (game: Game)
    =
    new Batch3DRenderer<'Model>(
      game,
      config,
      fun (ctx, model, buffer) -> view ctx model buffer
    )
    :> IRenderer<'Model>

/// <summary>Fluent builder for <see cref="T:Mibo.Elmish.Graphics3D.RenderCmd3D"/>.</summary>
[<Struct>]
type Draw3DBuilder = {
  Model: Model
  Transform: Matrix
  Color: Color voption
  Texture: Texture2D voption
  Pass: RenderPass
  Setup: EffectSetup voption
}

/// <summary>Functions for building and submitting 3D draw commands.</summary>
module Draw3D =
  /// <summary>Starts a mesh drawing command.</summary>
  let mesh model transform = {
    Model = model
    Transform = transform
    Color = ValueNone
    Texture = ValueNone
    Pass = Opaque
    Setup = ValueNone
  }

  let meshTransparent model transform = {
    mesh model transform with
        Pass = Transparent
  }

  let inPass pass (b: Draw3DBuilder) = { b with Pass = pass }

  let withColor col (b: Draw3DBuilder) = { b with Color = ValueSome col }
  let withTexture tex (b: Draw3DBuilder) = { b with Texture = ValueSome tex }

  /// <summary>Configure the effect for this draw command.</summary>
  let withEffect (setup: EffectSetup) (b: Draw3DBuilder) = {
    b with
        Setup = ValueSome setup
  }

  /// <summary>Helper: configure a standard <see cref="T:Microsoft.Xna.Framework.Graphics.BasicEffect"/> with typical parameters (World/View/Proj).</summary>
  /// <remarks>This restores the default behavior of previous versions.</remarks>
  let withBasicEffect(b: Draw3DBuilder) =
    b
    |> withEffect(fun effect ctx ->
      match effect with
      | :? BasicEffect as be ->
        be.World <- ctx.World
        be.View <- ctx.View
        be.Projection <- ctx.Projection
        StandardEffects.defaultLighting be
      | _ -> ())

  /// <summary>Submits the draw command to the renderer's buffer.</summary>
  let submit (buffer: RenderBuffer<RenderCmd3D>) (b: Draw3DBuilder) =
    buffer.Add(
      (),
      DrawMesh(b.Pass, b.Model, b.Transform, b.Color, b.Texture, b.Setup)
    )

  /// <summary>Submits a camera change command to the buffer.</summary>
  let camera (cam: Camera) (buffer: RenderBuffer<RenderCmd3D>) =
    buffer.Add((), SetCamera cam)

  /// <summary>Set viewport for multi-camera rendering (split-screen, minimaps, etc).</summary>
  let viewport (vp: Viewport) (buffer: RenderBuffer<RenderCmd3D>) =
    buffer.Add((), SetViewport vp)

  /// <summary>Clear color and/or depth buffer. Use between cameras in multi-camera setups.</summary>
  let clear
    (color: Color voption)
    (clearDepth: bool)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    buffer.Add((), ClearTarget(color, clearDepth))

  /// <summary>Submits a custom drawing command to the buffer.</summary>
  let custom
    (draw: GameContext * Matrix * Matrix -> unit)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    buffer.Add((), DrawCustom draw)

  /// <summary>Submits a skinned model draw command to the buffer.</summary>
  let skinned
    (pass: RenderPass)
    (model: Model)
    (transform: Matrix)
    (bones: Matrix[])
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    buffer.Add(
      (),
      DrawSkinned(
        pass,
        model,
        transform,
        bones,
        ValueNone,
        ValueNone,
        ValueNone
      )
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
      DrawSkinned(
        pass,
        model,
        transform,
        bones,
        ValueSome color,
        ValueNone,
        ValueNone
      )
    )

  /// <summary>Draw an axis-aligned textured quad. Caller must configure effect.</summary>
  let quad
    (effect: Effect)
    (position: Vector3)
    (size: Vector2)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    buffer.Add((), DrawQuad(effect, position, size))

  /// <summary>Draw a camera-facing billboard. Caller must configure effect.</summary>
  let billboard
    (effect: Effect)
    (position: Vector3)
    (size: float32)
    (rotation: float32)
    (color: Color)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    buffer.Add(
      (),
      DrawBillboard(effect, position, Vector2(size), rotation, color)
    )
