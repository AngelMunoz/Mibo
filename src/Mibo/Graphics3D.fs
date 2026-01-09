namespace Mibo.Elmish.Graphics3D

open System
open System.Runtime.CompilerServices
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

// --- 3D Rendering Implementation ---

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

  /// <summary>Sprite-style quad draw (90% case): unlit textured quad using built-in sprite effect.</summary>
  | DrawSpriteQuad of spriteQuad: SpriteQuadCmd

  /// <summary>Sprite-style billboard draw (90% case): unlit textured billboard using built-in sprite effect.</summary>
  | DrawSpriteBillboard of spriteBillboard: SpriteBillboardCmd

  /// <summary>Effect-driven quad draw: user provides an effect and optional setup callback.</summary>
  | DrawQuadEffect of quadEffect: EffectQuadCmd

  /// <summary>Effect-driven billboard draw: user provides an effect and optional setup callback.</summary>
  | DrawBillboardEffect of billboardEffect: EffectBillboardCmd

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

  /// <summary>Sampler state used for Sprite3D draws (billboards/quads).</summary>
  SpriteSamplerState: SamplerState

  /// <summary>Rasterizer state used for Sprite3D draws (billboards/quads).</summary>
  SpriteRasterizerState: RasterizerState

  SpriteOpaqueBlendState: BlendState
  SpriteOpaqueDepthStencilState: DepthStencilState

  SpriteTransparentBlendState: BlendState
  SpriteTransparentDepthStencilState: DepthStencilState

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

    // Sprite3D defaults: unlit textured quads/billboards.
    SpriteSamplerState = SamplerState.LinearClamp
    SpriteRasterizerState = RasterizerState.CullNone

    SpriteOpaqueBlendState = BlendState.Opaque
    SpriteOpaqueDepthStencilState = DepthStencilState.Default

    SpriteTransparentBlendState = BlendState.AlphaBlend
    SpriteTransparentDepthStencilState = DepthStencilState.DepthRead

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

  // Sprite batchers
  let mutable spriteQuadBatch = SpriteQuadBatch.create(game.GraphicsDevice)
  let mutable billboardBatch = BillboardBatch.create(game.GraphicsDevice)

  // Built-in unlit sprite effect (texture + vertex color)
  let spriteEffect =
    let e = new BasicEffect(game.GraphicsDevice)
    e.LightingEnabled <- false
    e.TextureEnabled <- true
    e.VertexColorEnabled <- true
    e

  interface IDisposable with
    member _.Dispose() =
      SpriteQuadBatch.dispose spriteQuadBatch
      BillboardBatch.dispose billboardBatch

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
      // We treat changes to viewport/camera/clears/custom as barriers. Between barriers,
      // we sort and execute opaque/transparent draws. This keeps sorting correct per camera.

      let mutable camPos = getCameraWorldPosition viewMatrix

      // Camera basis vectors for spherical billboards
      let mutable camRight = Vector3.Right
      let mutable camUp = Vector3.Up

      let updateCameraBasis() =
        let invView = Matrix.Invert(viewMatrix)
        camRight <- Vector3(invView.M11, invView.M21, invView.M31)
        camUp <- Vector3(invView.M12, invView.M22, invView.M32)

      let calcBillboardBasis
        (mode: BillboardMode)
        (pos: Vector3)
        : struct (Vector3 * Vector3) =
        match mode with
        | Spherical -> struct (camRight, camUp)
        | Cylindrical upAxis ->
          // Keep up fixed, rotate around it to face the camera.
          let viewDir = camPos - pos
          let mutable right = Vector3.Cross(upAxis, viewDir)

          if right.LengthSquared() < 0.000001f then
            // Degenerate: camera aligned with up axis; fall back to spherical.
            struct (camRight, camUp)
          else
            right.Normalize()
            let mutable up = Vector3.Cross(viewDir, right)

            if up.LengthSquared() < 0.000001f then
              struct (camRight, camUp)
            else
              up.Normalize()
              struct (right, up)

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

        gd.RasterizerState <- config.RasterizerState

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
              applyBasicEffectLighting be

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

      let flushSpriteQuadBatch (effect: Effect) (state: SpriteQuadBatch.State) =
        // Caller has configured effect + device state.
        SpriteQuadBatch.end' effect state
        SpriteQuadBatch.begin' state

      let flushBillboardBatch(state: BillboardBatch.State) =
        BillboardBatch.end' state

      let sortOpaqueIfNeeded() =
        if config.SortOpaqueFrontToBack then
          opaque.Sort
            { new Collections.Generic.IComparer<struct (float32 * RenderCmd3D)> with
                member _.Compare(struct (da, _), struct (db, _)) = compare da db
            }

      let sortTransparentBackToFront() =
        transparent.Sort
          { new Collections.Generic.IComparer<struct (float32 * RenderCmd3D)> with
              member _.Compare(struct (da, _), struct (db, _)) =
                // Descending: far -> near
                compare db da
          }

      let drawSpritesInList
        (pass: RenderPass)
        (items: ResizeArray<struct (float32 * RenderCmd3D)>)
        =
        let mutable currentSpriteQuadTexture: Texture2D = null
        let mutable currentSpriteBillboardTexture: Texture2D = null

        // Reset batches
        SpriteQuadBatch.begin' spriteQuadBatch
        BillboardBatch.end' billboardBatch

        let applySpriteStates(pass: RenderPass) =
          match pass with
          | Opaque ->
            gd.BlendState <- config.SpriteOpaqueBlendState
            gd.DepthStencilState <- config.SpriteOpaqueDepthStencilState
          | Transparent ->
            gd.BlendState <- config.SpriteTransparentBlendState
            gd.DepthStencilState <- config.SpriteTransparentDepthStencilState

          gd.RasterizerState <- config.SpriteRasterizerState
          gd.SamplerStates[0] <- config.SpriteSamplerState

        let ensureSpriteQuadEffect(tex: Texture2D) =
          if tex <> currentSpriteQuadTexture then
            // Flush pending sprite quads before switching textures.
            if not(isNull currentSpriteQuadTexture) then
              applySpriteStates pass
              spriteEffect.View <- viewMatrix
              spriteEffect.Projection <- projectionMatrix
              flushSpriteQuadBatch spriteEffect spriteQuadBatch

            currentSpriteQuadTexture <- tex
            spriteEffect.Texture <- tex

        let flushPendingQuads() =
          if
            spriteQuadBatch.QuadCount > 0
            && not(isNull currentSpriteQuadTexture)
          then
            applySpriteStates pass
            spriteEffect.View <- viewMatrix
            spriteEffect.Projection <- projectionMatrix
            flushSpriteQuadBatch spriteEffect spriteQuadBatch

        let flushPendingBillboards() =
          if billboardBatch.SpriteCount > 0 then
            BillboardBatch.end' billboardBatch

        for i = 0 to items.Count - 1 do
          let struct (_, cmd) = items[i]

          match cmd with
          | DrawSpriteQuad s ->
            // Switching from billboards: flush.
            flushPendingBillboards()

            ensureSpriteQuadEffect s.Texture
            let q = s.Quad

            SpriteQuadBatch.draw
              q.Center
              q.Right
              q.Up
              q.Color
              q.Uv
              spriteQuadBatch

          | DrawSpriteBillboard s ->
            // Flush any pending quads before drawing billboards.
            flushPendingQuads()

            if s.Texture <> currentSpriteBillboardTexture then
              flushPendingBillboards()
              currentSpriteBillboardTexture <- s.Texture
              applySpriteStates pass
              spriteEffect.View <- viewMatrix
              spriteEffect.Projection <- projectionMatrix
              spriteEffect.Texture <- s.Texture
              BillboardBatch.begin' (spriteEffect :> Effect) billboardBatch
            else
              applySpriteStates pass

            let b = s.Billboard
            let struct (right, up) = calcBillboardBasis b.Mode b.Position

            BillboardBatch.drawUv
              b.Position
              b.Size
              b.Rotation
              b.Color
              b.Uv
              right
              up
              billboardBatch

          | DrawQuadEffect e ->
            // Flush sprite batches before custom-effect quads.
            flushPendingBillboards()
            flushPendingQuads()

            let effect = e.Effect

            // Per-command setup; since we can't assume parameters are stable across draws,
            // render this command immediately (one quad per command) for correctness.
            e.Setup
            |> ValueOption.iter(fun setup ->
              setup effect {
                World = Matrix.Identity
                View = viewMatrix
                Projection = projectionMatrix
              })

            applySpriteStates pass

            SpriteQuadBatch.begin' spriteQuadBatch
            let q = e.Quad

            SpriteQuadBatch.draw
              q.Center
              q.Right
              q.Up
              q.Color
              q.Uv
              spriteQuadBatch

            SpriteQuadBatch.end' effect spriteQuadBatch

          | DrawBillboardEffect e ->
            // Flush sprite batches before effect billboards.
            flushPendingQuads()
            flushPendingBillboards()

            let effect = e.Effect

            e.Setup
            |> ValueOption.iter(fun setup ->
              setup effect {
                World = Matrix.Identity
                View = viewMatrix
                Projection = projectionMatrix
              })

            applySpriteStates pass

            BillboardBatch.begin' effect billboardBatch
            let b = e.Billboard
            let struct (right, up) = calcBillboardBasis b.Mode b.Position

            BillboardBatch.drawUv
              b.Position
              b.Size
              b.Rotation
              b.Color
              b.Uv
              right
              up
              billboardBatch

            BillboardBatch.end' billboardBatch

          | _ -> ()

        // Final flush
        flushPendingQuads()

        flushPendingBillboards()

      let flushSegment() =
        if opaque.Count = 0 && transparent.Count = 0 then
          ()
        else
          sortOpaqueIfNeeded()
          sortTransparentBackToFront()

          // Execute opaque then transparent
          for i = 0 to opaque.Count - 1 do
            let struct (_, cmd) = opaque[i]

            match cmd with
            | DrawMesh(pass, m, transform, colorOpt, texOpt, setupOpt) ->
              drawModelMeshes
                pass
                m
                transform
                colorOpt
                texOpt
                ValueNone
                setupOpt
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

          drawSpritesInList Opaque opaque

          for i = 0 to transparent.Count - 1 do
            let struct (_, cmd) = transparent[i]

            match cmd with
            | DrawMesh(pass, m, transform, colorOpt, texOpt, setupOpt) ->
              drawModelMeshes
                pass
                m
                transform
                colorOpt
                texOpt
                ValueNone
                setupOpt
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

          drawSpritesInList Transparent transparent

          clearLists()

      // Initial basis
      updateCameraBasis()

      clearLists()

      for i = 0 to buffer.Count - 1 do
        let struct (_, cmd) = buffer.Item(i)

        match cmd with
        | SetViewport vp ->
          flushSegment()
          gd.Viewport <- vp

        | ClearTarget(colorOpt, clearDepth) ->
          flushSegment()

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
          flushSegment()

          viewMatrix <- cam.View
          projectionMatrix <- cam.Projection
          camPos <- getCameraWorldPosition viewMatrix
          updateCameraBasis()

        | DrawCustom draw ->
          flushSegment()
          draw(ctx, viewMatrix, projectionMatrix)
          // Re-apply baseline state for subsequent draws.
          gd.RasterizerState <- config.RasterizerState

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

        | DrawSpriteQuad s ->
          let distSq = Vector3.DistanceSquared(camPos, s.Quad.Center)

          match s.Pass with
          | Opaque -> opaque.Add struct (distSq, cmd)
          | Transparent -> transparent.Add struct (distSq, cmd)

        | DrawSpriteBillboard s ->
          let distSq = Vector3.DistanceSquared(camPos, s.Billboard.Position)

          match s.Pass with
          | Opaque -> opaque.Add struct (distSq, cmd)
          | Transparent -> transparent.Add struct (distSq, cmd)

        | DrawQuadEffect e ->
          let distSq = Vector3.DistanceSquared(camPos, e.Quad.Center)

          match e.Pass with
          | Opaque -> opaque.Add struct (distSq, cmd)
          | Transparent -> transparent.Add struct (distSq, cmd)

        | DrawBillboardEffect e ->
          let distSq = Vector3.DistanceSquared(camPos, e.Billboard.Position)

          match e.Pass with
          | Opaque -> opaque.Add struct (distSq, cmd)
          | Transparent -> transparent.Add struct (distSq, cmd)

      flushSegment()

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

  // --- Sprite3D helpers (90% path) ---

  /// <summary>Create a quad with sensible defaults (white tint, full UVs).</summary>
  let quad3D (center: Vector3) (right: Vector3) (up: Vector3) : Quad3D = {
    Center = center
    Right = right
    Up = up
    Color = Color.White
    Uv = UvRect.full
  }

  /// <summary>Create a quad on the XZ plane (useful for ground decals).</summary>
  let quadOnXZ (center: Vector3) (size: Vector2) : Quad3D =
    let right = Vector3(size.X * 0.5f, 0.0f, 0.0f)
    let up = Vector3(0.0f, 0.0f, size.Y * 0.5f)
    quad3D center right up

  /// <summary>Create a quad on the XY plane (useful for in-world UI).</summary>
  let quadOnXY (center: Vector3) (size: Vector2) : Quad3D =
    let right = Vector3(size.X * 0.5f, 0.0f, 0.0f)
    let up = Vector3(0.0f, size.Y * 0.5f, 0.0f)
    quad3D center right up

  let withQuadColor (color: Color) (q: Quad3D) = { q with Color = color }
  let withQuadUv (uv: UvRect) (q: Quad3D) = { q with Uv = uv }

  /// <summary>Create a billboard with sensible defaults (white tint, full UVs, spherical).</summary>
  let billboard3D (position: Vector3) (size: Vector2) : Billboard3D = {
    Position = position
    Size = size
    Rotation = 0.0f
    Color = Color.White
    Uv = UvRect.full
    Mode = Spherical
  }

  let withBillboardRotation (rotation: float32) (b: Billboard3D) = {
    b with
        Rotation = rotation
  }

  let withBillboardColor (color: Color) (b: Billboard3D) = {
    b with
        Color = color
  }

  let withBillboardUv (uv: UvRect) (b: Billboard3D) = { b with Uv = uv }

  let cylindrical (upAxis: Vector3) (b: Billboard3D) = {
    b with
        Mode = Cylindrical upAxis
  }

  /// <summary>Draw a textured quad using the built-in unlit Sprite3D pipeline.</summary>
  let quad
    (texture: Texture2D)
    (quad: Quad3D)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    buffer.Add(
      (),
      DrawSpriteQuad {
        Pass = Opaque
        Texture = texture
        Quad = quad
      }
    )

  /// <summary>Draw a textured quad (transparent pass) using the built-in unlit Sprite3D pipeline.</summary>
  let quadTransparent
    (texture: Texture2D)
    (quad: Quad3D)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    buffer.Add(
      (),
      DrawSpriteQuad {
        Pass = Transparent
        Texture = texture
        Quad = quad
      }
    )

  /// <summary>Draw a camera-facing billboard using the built-in unlit Sprite3D pipeline.</summary>
  let billboard
    (texture: Texture2D)
    (billboard: Billboard3D)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    buffer.Add(
      (),
      DrawSpriteBillboard {
        Pass = Transparent
        Texture = texture
        Billboard = billboard
      }
    )

  /// <summary>Draw a billboard in the opaque pass using the built-in unlit Sprite3D pipeline.</summary>
  let billboardOpaque
    (texture: Texture2D)
    (billboard: Billboard3D)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    buffer.Add(
      (),
      DrawSpriteBillboard {
        Pass = Opaque
        Texture = texture
        Billboard = billboard
      }
    )

  // --- Effect-driven helpers (advanced path) ---

  /// <summary>Draw a quad using a custom effect. Setup is invoked for this command (View/Proj provided).</summary>
  let quadEffect
    (pass: RenderPass)
    (effect: Effect)
    (setup: EffectSetup voption)
    (quad: Quad3D)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    buffer.Add(
      (),
      DrawQuadEffect {
        Pass = pass
        Effect = effect
        Setup = setup
        Quad = quad
      }
    )

  /// <summary>Draw a billboard using a custom effect. Setup is invoked for this command (View/Proj provided).</summary>
  let billboardEffect
    (pass: RenderPass)
    (effect: Effect)
    (setup: EffectSetup voption)
    (billboard: Billboard3D)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    buffer.Add(
      (),
      DrawBillboardEffect {
        Pass = pass
        Effect = effect
        Setup = setup
        Billboard = billboard
      }
    )
