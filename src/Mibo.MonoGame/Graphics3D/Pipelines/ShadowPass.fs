namespace Mibo.Elmish.Graphics3D.Pipelines

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D

// ─────────────────────────────────────────────────────────────────────────────
// ShadowPass — the 3D shadow pass, extracted from the pipeline (v2 pipeline-staging
// refactor). Mirrors the raylib backend's ShadowPassHelpers: caster geometry types,
// the per-light-type shadow ViewProj builders, and the pass body (collect casters →
// register light casters → render depth to the atlas → upload shadow uniforms to the
// PBR effect).
//
// All mutable shadow state lives on ShadowResources (owned by the pipeline); the
// module functions take it by ref plus the per-frame scene. No per-frame heap
// allocation on the hot path — scratch arrays are pooled on ShadowResources and the
// light-upload scratch lives on PbrUniforms.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A mesh draw collected for the shadow pass (caster geometry).</summary>
[<Struct>]
type ShadowMeshDraw = {
  Mesh: PrimitiveMesh
  Transform: Matrix
  /// World-space bounds (mesh.Bounds transformed by Transform). Precomputed at collection
  /// time so the per-caster frustum cull in runShadowPass is a single ContainmentType check.
  WorldBounds: BoundingSphere
}

/// <summary>A skinned caster draw collected for the shadow pass (B12).</summary>
/// <remarks>
/// Unlike <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.ShadowMeshDraw"/>, a skinned caster
/// carries no world-space bounds: a bare <c>ModelMeshPart</c> has no parent reference, so the
/// part's <c>ModelMesh.BoundingSphere</c> isn't reachable at collection time. Skinned casters
/// are therefore drawn unconditionally (no per-light frustum cull) — correct for the sample's
/// single animated character; the per-caster cull only matters at scale for instanced terrain.
/// </remarks>
[<Struct>]
type ShadowSkinnedDraw = {
  Part: ModelMeshPart
  Transform: Matrix
  Bones: Matrix[]
}

/// <summary>An instanced caster draw collected for the shadow pass.</summary>
/// <remarks>
/// Instanced geometry (e.g. a game world's block grid rendered via <c>DrawInstanced</c>) MUST
/// cast shadows. Unlike <c>ShadowMeshDraw</c> this carries many per-instance world transforms;
/// the pass renders it via the depth effect's <c>DepthInstanced</c> technique with a two-stream
/// vertex bind (mesh + per-instance <c>VertexInstanceWorld</c>), one <c>DrawInstancedPrimitives</c>
/// per light. No per-instance frustum cull — the sample already chunk-culls the source commands,
/// so the emitted count is small; the cost is bounded by the surviving instance counts.
/// </remarks>
[<Struct>]
type ShadowInstancedDraw = {
  Mesh: PrimitiveMesh
  Transforms: Matrix[]
  InstanceCount: int
}

/// <summary>
/// Cached <see cref="T:Microsoft.Xna.Framework.Graphics.EffectParameter"/> handles for the
/// depth-only shadow pass effect (<c>DepthShadow.fx</c>).
/// </summary>
[<Struct>]
type ShadowEffectParams = {
  MatModel: EffectParameter
  ViewProj: EffectParameter
  // Skinning (B12 DepthSkinned technique): bone palette. null on the plain Depth technique —
  // the skinned-caster path uploads only when present.
  Bones: EffectParameter
}

// ─────────────────────────────────────────────────────────────────────────────
// ShadowResources — all mutable shadow state owned by the pipeline, reused across
// frames (no per-frame allocation). Held as a single field so the pass module can
// touch it without threading a dozen separate arrays through the pipeline type.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Owns the shadow atlas + depth effect + pooled scratch the shadow pass needs, reused across
/// frames. Constructed once by the pipeline; <c>EnsureResources</c> allocates the GPU atlas lazily
/// against the real device on the first shadow pass.
/// </summary>
type ShadowResources(atlasCfg: ShadowAtlasConfig, biasCfg: ShadowBiasConfig) =

  /// <summary>The shadow atlas (owns the R32F RenderTarget2D, allocated lazily).</summary>
  member val Atlas = ShadowAtlas(atlasCfg, biasCfg)

  /// <summary>The depth-only effect (DepthShadow.fx), loaded lazily on first shadow pass.</summary>
  member val Effect: Effect voption = ValueNone with get, set

  /// <summary>Cached depth-effect parameter handles (built when the effect loads).</summary>
  member val Params: ShadowEffectParams voption = ValueNone with get, set

  /// <summary>The frame's shadow-origin override (SetShadowOrigin); ValueNone = use the atlas strategy.</summary>
  member val Origin: Vector3 voption = ValueNone with get, set

  /// <summary>
  /// Cached RasterizerState (polygon-offset bias + back-face culling). Created once on first
  /// shadow pass; disposed by the pipeline. Avoids per-frame GPU-state allocation.
  /// </summary>
  member val Raster: RasterizerState = null with get, set

  /// <summary>Pooled caster mesh draws collected from the buffer (gated by EnableShadows/DisableShadows).</summary>
  member val Draws = Array.zeroCreate<ShadowMeshDraw> 64 with get, set

  /// <summary>Pooled skinned-caster draws (B12). Grows on demand.</summary>
  member val SkinnedDraws = Array.zeroCreate<ShadowSkinnedDraw> 8 with get, set

  /// <summary>Pooled instanced-caster draws (the world's block grid etc.). Grows on demand.</summary>
  member val InstancedDraws =
    Array.zeroCreate<ShadowInstancedDraw> 32 with get, set

  /// <summary>CPU staging array for the per-instance <c>VertexInstanceWorld</c> rows. Grows to the
  /// largest instanceCount seen across collected instanced casters. Reused across frames.</summary>
  member val InstanceStaging =
    Array.zeroCreate<VertexInstanceWorld> 64 with get, set

  /// <summary>Growable per-instance vertex buffer for instanced shadow casters. Owned by the shadow
  /// pass (not shared with the forward-pass PBR instance VB) so the two passes never race on it.
  /// Disposed at shutdown; recreated when an instanced caster exceeds the current capacity.</summary>
  member val InstanceVertexBuffer: VertexBuffer voption =
    ValueNone with get, set

  /// <summary>Per-light shadow slot mapping, indexed by lights.PointLights/SpotLights position; -1 = no shadow.</summary>
  member val PointShadowSlots: int[] = [||] with get, set

  /// <summary>Per-light shadow slot mapping for spot lights; -1 = no shadow.</summary>
  member val SpotShadowSlots: int[] = [||] with get, set

  /// <summary>Pooled scratch for the multi-caster shadowViewProjs upload.</summary>
  member val ViewProjsScratch = Array.zeroCreate<Matrix> 16 with get, set

  /// <summary>Pooled scratch for the per-caster <c>shadowBiases</c> upload (receiver-side
  /// depth bias). Copied from <c>Atlas.Biases</c> each frame.</summary>
  member val BiasesScratch = Array.zeroCreate<float32> 16 with get, set

  /// <summary>Pooled scratch for the multi-caster shadowUVOffsets upload.</summary>
  member val UVOffsetsScratch = Array.zeroCreate<Vector4> 16 with get, set

  /// <summary>Reused per-caster frustum (updated in-place via .Matrix) to avoid per-frame allocation.</summary>
  member val Frustum = BoundingFrustum(Matrix.Identity)

  /// <summary>Pooled bone-palette staging array for skinned casters (B12). Shader's MAX_BONES is 128.</summary>
  member val BonePaletteScratch = Array.zeroCreate<Matrix> 128 with get, set

  /// <summary>The last frame's shadow pass output — read by the forward pass (Shade overrides +
  /// user-effect scopes) so a custom shader can opt into shadow sampling. ValueNone when no
  /// shadow-casting light exists or DepthShadow.fx is unavailable.</summary>
  member val ShadowResult: ShadowResult voption = ValueNone with get, set

/// <summary>The extracted shadow pass: caster geometry types, per-light ViewProj builders, and the pass body.</summary>
module internal ShadowPass =

  /// <summary>Builds the shadow-pass parameter handles once after load.</summary>
  let buildShadowParams(e: Effect) : ShadowEffectParams = {
    MatModel = e.Parameters["matModel"]
    ViewProj = e.Parameters["viewProj"]
    Bones = e.Parameters["boneMatrices"]
  }

  /// <summary>
  /// Builds the directional light's orthographic shadow ViewProj with texel-space snapping.
  /// World-space origin snapping alone leaves sub-texel crawl as the camera or sun moves; this
  /// rounds the light clip-space projection to whole atlas texels so shadow-map pixels stay
  /// locked to world geometry and the shadow stops rotating/sliding.
  /// </summary>
  let buildDirectionalViewProj
    (atlasCfg: ShadowAtlasConfig)
    (shadowOrigin: Vector3 voption)
    (lightDir: Vector3)
    (activeCamera: Camera3D)
    : Matrix =
    let lightFromDir = Vector3.Normalize(-lightDir)

    let rawOrigin =
      match shadowOrigin with
      | ValueSome origin -> origin
      | ValueNone ->
        match atlasCfg.OriginStrategy with
        | ShadowOriginStrategy.CameraTarget -> activeCamera.Target
        | ShadowOriginStrategy.SceneCenter -> Vector3.Zero
        | ShadowOriginStrategy.Custom f -> f activeCamera

    let lightDistance =
      match atlasCfg.DirectionalLightDistance with
      | ValueSome d -> d
      | ValueNone -> 100.0f

    let orthoSize =
      match atlasCfg.DirectionalLightSize with
      | ValueSome s -> s
      | ValueNone -> 50.0f

    let resolution = float32 atlasCfg.Resolution
    let gridSize = MathF.Sqrt(float32 atlasCfg.MaxCasters)
    let slotResolution = resolution / gridSize
    // World-space size of one shadow texel in the directional light's X/Y plane.
    // The config's GridSnapSize overrides this when set; otherwise we default to the
    // texel size so the shadow-map pixels stay locked to world geometry.
    let texelWorld = orthoSize * 2.0f / slotResolution
    let snapSize = max atlasCfg.GridSnapSize texelWorld

    // Lock the shadow origin Y to the configured world height so jumping does not slide
    // the frustum vertically. Snap X/Z to the chosen grid for stability.
    let originY = atlasCfg.DirectionalOriginY

    let snappedX =
      if snapSize > 0.0f then
        MathF.Round(rawOrigin.X / snapSize) * snapSize
      else
        rawOrigin.X

    let snappedZ =
      if snapSize > 0.0f then
        MathF.Round(rawOrigin.Z / snapSize) * snapSize
      else
        rawOrigin.Z

    let snappedOrigin = Vector3(snappedX, originY, snappedZ)

    let lightPos = snappedOrigin + lightFromDir * lightDistance

    let safeUp =
      if abs lightDir.Y > 0.99f then
        Vector3.UnitZ
      else
        Vector3.UnitY

    let shadowNear = 1.0f
    let shadowFar = lightDistance + orthoSize * 2.0f

    let view = Matrix.CreateLookAt(lightPos, snappedOrigin, safeUp)

    let proj =
      Matrix.CreateOrthographicOffCenter(
        -orthoSize,
        orthoSize,
        -orthoSize,
        orthoSize,
        shadowNear,
        shadowFar
      )

    view * proj

  /// <summary>
  /// Builds a point light's shadow ViewProj — a downward-facing 90° perspective frustum
  /// covering +Z. Used by the forward shader for point-light shadows. (B13 point-light
  /// shadows are rendered as a single face into one atlas slot.)
  /// </summary>
  let buildPointViewProj(lightPos: Vector3, lightRadius: float32) : Matrix =
    let view =
      Matrix.CreateLookAt(lightPos, lightPos - Vector3.UnitY, Vector3.UnitZ)
    // Dynamic near plane: CreatePerspectiveFieldOfView throws if near <= 0.
    let nearPlane = max 0.0001f (min 0.1f (lightRadius * 0.5f))

    let proj =
      Matrix.CreatePerspectiveFieldOfView(
        MathF.PI * 0.5f,
        1.0f,
        nearPlane,
        lightRadius
      )

    view * proj

  /// <summary>
  /// Builds a spot light's shadow ViewProj — a perspective frustum from the light position toward
  /// <c>pos + dir</c>, with FOV from the outer cutoff cone. §6.1 application order.
  /// </summary>
  let buildSpotViewProj(light: SpotLight3D) : Matrix =
    let lightDir = Vector3.Normalize light.Direction

    let safeUp =
      if abs lightDir.Y > 0.99f then
        Vector3.UnitZ
      else
        Vector3.UnitY

    let view =
      Matrix.CreateLookAt(light.Position, light.Position + lightDir, safeUp)

    // Outer cutoff is a cosine; half-angle FOV = acos(outerCutoff), full FOV = 2× that.
    // Clamp to a safe open interval — CreatePerspectiveFieldOfView throws if FOV ∉ (0, π).
    let fov =
      max 0.01f (min (MathF.PI - 0.01f) (2.0f * MathF.Acos(light.OuterCutoff)))

    let nearPlane = max 0.0001f (min 0.1f (light.Radius * 0.5f))

    let proj =
      Matrix.CreatePerspectiveFieldOfView(fov, 1.0f, nearPlane, light.Radius)

    view * proj

  // ── drawPart: draw a single ModelMeshPart manually (part has no Draw() of its own). ──
  let private drawPart(gd: GraphicsDevice, part: ModelMeshPart) =
    if part.PrimitiveCount > 0 then
      gd.SetVertexBuffer(part.VertexBuffer)
      gd.Indices <- part.IndexBuffer

      for p in part.Effect.CurrentTechnique.Passes do
        p.Apply()

        gd.DrawIndexedPrimitives(
          PrimitiveType.TriangleList,
          part.VertexOffset,
          part.StartIndex,
          part.PrimitiveCount
        )

  /// <summary>
  /// Runs the shadow pass: collects dir + point + spot casters (B11 extends B10's dir-only), renders
  /// depth to the atlas using <c>DepthShadow.fx</c> with <c>RasterizerState.SlopeScaleDepthBias</c>,
  /// then uploads shadow uniforms to the PBR effect for forward-pass sampling. Per-light frustum
  /// culling skips caster meshes outside each light's frustum (Layer 3 of the cost analysis).
  /// </summary>
  /// <param name="atlasCfg">Shadow atlas config (sizes/snap/distance).</param>
  /// <param name="biasCfg">Shadow bias config (rasterizer polygon-offset).</param>
  /// <param name="res">The pipeline's pooled shadow resources (atlas, effect, scratch, slots).</param>
  /// <param name="lights">The frame's accumulated lights.</param>
  /// <param name="pbrParams">The PBR effect params (shadow uniforms upload to its Shadow group).</param>
  /// <param name="buffer">The command buffer (casters collected from it).</param>
  /// <param name="activeCamera">The active camera (directional shadow origin).</param>
  let run
    (gd: GraphicsDevice)
    (atlasCfg: ShadowAtlasConfig)
    (biasCfg: ShadowBiasConfig)
    (res: ShadowResources)
    (lights: LightBuffers)
    (pbrParams: PbrEffectParams voption)
    (buffer: RenderBuffer3D)
    (activeCamera: Camera3D)
    : ShadowResult voption =
    let mutable hasDirCaster = false

    for i = 0 to lights.DirLights.Count - 1 do
      if lights.DirLights[i].CastsShadows then
        hasDirCaster <- true

    let mutable hasPointCaster = false

    for i = 0 to lights.PointLights.Count - 1 do
      if lights.PointLights[i].CastsShadows then
        hasPointCaster <- true

    let mutable hasSpotCaster = false

    for i = 0 to lights.SpotLights.Count - 1 do
      if lights.SpotLights[i].CastsShadows then
        hasSpotCaster <- true

    // Always init the per-light slot mappings (default -1 = no shadow). Even with no casters,
    // the forward pass reads these to upload pointLightShadowIdx/spotLightShadowIdx. Reuse across
    // frames (reallocate only if the light count grew).
    if res.PointShadowSlots.Length < lights.PointLights.Count then
      res.PointShadowSlots <- Array.create<int> lights.PointLights.Count -1
    else
      Array.Fill(res.PointShadowSlots, -1)

    if res.SpotShadowSlots.Length < lights.SpotLights.Count then
      res.SpotShadowSlots <- Array.create<int> lights.SpotLights.Count -1
    else
      Array.Fill(res.SpotShadowSlots, -1)

    let mutable result: ShadowResult voption = ValueNone

    if not(hasDirCaster || hasPointCaster || hasSpotCaster) then
      match pbrParams with
      | ValueSome p -> PbrUniforms.setInt p.Shadow.DirLightCastsShadows 0
      | ValueNone -> ()
    // No shadow-casting light → no result; scene renders unshadowed.
    else
      res.Atlas.EnsureResources gd

      match res.Effect, res.Params with
      | ValueSome _, ValueSome _ -> ()
      | _ ->
        match ShaderLoader.loadEffect gd "DepthShadow" with
        | ValueSome e ->
          res.Params <- ValueSome(buildShadowParams e)
          res.Effect <- ValueSome e
        | ValueNone -> ()

      match res.Effect, res.Params with
      | ValueSome depthEffect, ValueSome depthParams ->
        res.Atlas.Clear()

        // ── Register casters: dir first (slot 0), then point, then spot ──
        // The flat shader-array index == registration order (matches PrepareUniforms).
        let mutable casterSlot = 0

        if hasDirCaster then
          let mutable dirIdx = 0

          while not lights.DirLights[dirIdx].CastsShadows do
            dirIdx <- dirIdx + 1

          let dirLight = lights.DirLights[dirIdx]

          let vp =
            buildDirectionalViewProj
              atlasCfg
              res.Origin
              dirLight.Direction
              activeCamera

          match
            res.Atlas.AddCaster(
              ShadowCasterType.Directional,
              Vector3.Zero,
              dirLight.Direction,
              Vector3.Zero,
              true,
              ValueNone
            )
          with
          | ValueSome _ ->
            res.Atlas.SetRegionViewProj(casterSlot, vp)
            casterSlot <- casterSlot + 1
          | ValueNone -> ()

        // Point lights (for loop, not Seq.iteri — avoids per-frame enumerator allocation).
        for i = 0 to lights.PointLights.Count - 1 do
          let pt = lights.PointLights[i]

          if pt.CastsShadows then
            let vp = buildPointViewProj(pt.Position, pt.Radius)

            match
              res.Atlas.AddCaster(
                ShadowCasterType.Point,
                pt.Position,
                Vector3.Zero,
                Vector3.Zero,
                true,
                pt.ShadowBias
              )
            with
            | ValueSome _ ->
              res.Atlas.SetRegionViewProj(casterSlot, vp)
              res.PointShadowSlots[i] <- casterSlot
              casterSlot <- casterSlot + 1
            | ValueNone -> ()

        // Spot lights.
        for i = 0 to lights.SpotLights.Count - 1 do
          let sp = lights.SpotLights[i]

          if sp.CastsShadows then
            let vp = buildSpotViewProj sp

            match
              res.Atlas.AddCaster(
                ShadowCasterType.Spot,
                sp.Position,
                sp.Direction,
                sp.Position + sp.Direction,
                true,
                sp.ShadowBias
              )
            with
            | ValueSome _ ->
              res.Atlas.SetRegionViewProj(casterSlot, vp)
              res.SpotShadowSlots[i] <- casterSlot
              casterSlot <- casterSlot + 1
            | ValueNone -> ()

        if casterSlot = 0 then
          // Caster registration failed for every shadow light (atlas full). Effect params
          // persist between frames in MonoGame, so DirLightCastsShadows (set to 1 on a
          // previous successful frame) would leave the PBR shader sampling stale shadow
          // matrices/UVs. Zero it explicitly so the scene renders unshadowed this frame.
          match pbrParams with
          | ValueSome p -> PbrUniforms.setInt p.Shadow.DirLightCastsShadows 0
          | ValueNone -> ()
        else
          // ── Collect caster meshes (PrimitiveMesh + skinned draws, gated by EnableShadows/DisableShadows) ──
          let mutable castEnabled = true
          let mutable drawCount = 0
          let mutable skinnedCount = 0
          let mutable instancedCount = 0
          let mutable shadowDraws = res.Draws
          let mutable shadowSkinnedDraws = res.SkinnedDraws
          let mutable shadowInstancedDraws = res.InstancedDraws

          for i = 0 to buffer.Count - 1 do
            match buffer[i] with
            | Command3D.EnableShadows -> castEnabled <- true
            | Command3D.DisableShadows -> castEnabled <- false
            | Command3D.DrawPrimitive(mesh, transform, _) when castEnabled ->
              if drawCount >= shadowDraws.Length then
                Array.Resize(&shadowDraws, shadowDraws.Length * 2)
                res.Draws <- shadowDraws

              let worldBounds = mesh.Bounds.Transform transform

              shadowDraws[drawCount] <- {
                Mesh = mesh
                Transform = transform
                WorldBounds = worldBounds
              }

              drawCount <- drawCount + 1
            | Command3D.DrawAnimatedModel(model, transform, bones) when
              castEnabled
              ->
              for mesh in model.Meshes do
                for part in mesh.MeshParts do
                  match part.Effect with
                  | :? SkinnedEffect ->
                    if skinnedCount >= shadowSkinnedDraws.Length then
                      Array.Resize(
                        &shadowSkinnedDraws,
                        shadowSkinnedDraws.Length * 2
                      )

                      res.SkinnedDraws <- shadowSkinnedDraws

                    shadowSkinnedDraws[skinnedCount] <- {
                      Part = part
                      Transform = transform
                      Bones = bones
                    }

                    skinnedCount <- skinnedCount + 1
                  | _ -> ()
            | Command3D.DrawInstanced(mesh, transforms, _material, instanceCount) when
              castEnabled && instanceCount > 0
              ->
              // The world's instanced geometry (block grid, platforms, etc.) casts shadows too.
              // Collected whole (one entry per emitted DrawInstanced); rendered via the
              // DepthInstanced technique. No per-instance cull — the sample chunk-culls the
              // source commands, so the emitted count is already bounded.
              if instancedCount >= shadowInstancedDraws.Length then
                Array.Resize(
                  &shadowInstancedDraws,
                  shadowInstancedDraws.Length * 2
                )

                res.InstancedDraws <- shadowInstancedDraws

              shadowInstancedDraws[instancedCount] <- {
                Mesh = mesh
                Transforms = transforms
                InstanceCount = instanceCount
              }

              instancedCount <- instancedCount + 1
            | _ -> ()

          if drawCount = 0 && skinnedCount = 0 && instancedCount = 0 then
            ()
          else
            res.Atlas.PrepareUniforms()

            // ── Render depth into the atlas ──
            let prevViewport = gd.Viewport
            let prevRaster = gd.RasterizerState
            let prevBlend = gd.BlendState
            let prevDepth = gd.DepthStencilState

            gd.SetRenderTarget(res.Atlas.Fbo)
            // Clear color (white = far = lit) AND depth (depth test enabled for hidden-surface removal).
            gd.Clear(
              ClearOptions.Target ||| ClearOptions.DepthBuffer,
              Color.White.ToVector4(),
              1.0f,
              0
            )

            // Native polygon-offset bias. Cached (config-driven). For B11 the bias uses
            // DirectionalBias as the base — per-type bias swap is deferred.
            if obj.ReferenceEquals(res.Raster, null) then
              let sr = new RasterizerState()
              // Render front faces into the shadow map. The imported geometry (Kenney
              // assets and typical MonoGame content) is clockwise-wound; keeping the
              // front-facing sides writes the caster surfaces that are actually visible
              // to the light. Back-face rendering was tried but produced identical
              // self-shadowing results once receiver-side bias was added, so the
              // simpler front-face path is kept.
              sr.CullMode <- CullMode.CullCounterClockwiseFace
              sr.DepthBias <- biasCfg.DirectionalBias
              sr.SlopeScaleDepthBias <- biasCfg.SlopeScaleBias
              res.Raster <- sr

            gd.RasterizerState <- res.Raster
            gd.BlendState <- BlendState.Opaque
            gd.DepthStencilState <- DepthStencilState.Default
            depthEffect.CurrentTechnique <- depthEffect.Techniques["Depth"]
            let shadowFrustum = res.Frustum
            let bonePaletteScratch = res.BonePaletteScratch

            for caster in res.Atlas.Casters do
              if caster.Enabled then
                let casterVP = res.Atlas.GetRegionViewProj caster.AtlasRegion
                gd.Viewport <- res.Atlas.GetRegionViewport(caster.AtlasRegion)
                // Per-light frustum cull: skip meshes the caster's frustum can't see. Update in-place.
                shadowFrustum.Matrix <- casterVP

                // ── Non-skinned casters (PrimitiveMesh, plain Depth technique) ──
                for d = 0 to drawCount - 1 do
                  let draw = shadowDraws[d]

                  if Culling.isVisible shadowFrustum draw.WorldBounds then
                    PbrUniforms.setMatrix depthParams.MatModel draw.Transform
                    PbrUniforms.setMatrix depthParams.ViewProj casterVP

                    for pass in depthEffect.CurrentTechnique.Passes do
                      pass.Apply()
                      draw.Mesh.Draw(gd, depthEffect)

                // ── Skinned casters (B12: DepthSkinned technique, bone palette upload) ──
                if skinnedCount > 0 then
                  depthEffect.CurrentTechnique <-
                    depthEffect.Techniques["DepthSkinned"]

                  for d = 0 to skinnedCount - 1 do
                    let draw = shadowSkinnedDraws[d]
                    PbrUniforms.setMatrix depthParams.MatModel draw.Transform
                    PbrUniforms.setMatrix depthParams.ViewProj casterVP

                    let boneCount =
                      min draw.Bones.Length bonePaletteScratch.Length

                    for i = 0 to boneCount - 1 do
                      bonePaletteScratch[i] <- draw.Bones[i]

                    for i = boneCount to bonePaletteScratch.Length - 1 do
                      bonePaletteScratch[i] <- Matrix.Identity

                    PbrUniforms.setMatrixArray
                      depthParams.Bones
                      bonePaletteScratch

                    // drawPart applies part.Effect.CurrentTechnique.Passes, so DepthSkinned must be
                    // bound on the part's own Effect slot — swap it for the depth effect around the draw.
                    let saved = draw.Part.Effect
                    draw.Part.Effect <- depthEffect

                    try
                      drawPart(gd, draw.Part)
                    finally
                      draw.Part.Effect <- saved

                  depthEffect.CurrentTechnique <-
                    depthEffect.Techniques["Depth"]

                // ── Instanced casters: hardware instancing via two-stream vertex bind. ──
                if instancedCount > 0 then
                  depthEffect.CurrentTechnique <-
                    depthEffect.Techniques["DepthInstanced"]

                  // matModel is identity for instanced draws: the per-instance world transform
                  // is supplied as VertexInstanceWorld rows on stream 1.
                  PbrUniforms.setMatrix depthParams.MatModel Matrix.Identity

                  for d = 0 to instancedCount - 1 do
                    let draw = shadowInstancedDraws[d]

                    // Defensive: the source DrawInstanced command should always have a
                    // transforms array matching instanceCount, but clamp to the available
                    // data so a malformed command cannot read past the array.
                    let instanceCount =
                      min draw.InstanceCount draw.Transforms.Length

                    if instanceCount > 0 then
                      if res.InstanceStaging.Length < instanceCount then
                        res.InstanceStaging <-
                          Array.zeroCreate<VertexInstanceWorld> instanceCount

                      for i = 0 to instanceCount - 1 do
                        res.InstanceStaging[i] <-
                          VertexInstanceWorld.Create draw.Transforms[i]

                      match res.InstanceVertexBuffer with
                      | ValueNone ->
                        let vb =
                          new VertexBuffer(
                            gd,
                            typeof<VertexInstanceWorld>,
                            instanceCount,
                            BufferUsage.WriteOnly
                          )

                        res.InstanceVertexBuffer <- ValueSome vb
                      | ValueSome vb when vb.VertexCount < instanceCount ->
                        vb.Dispose()

                        let vb' =
                          new VertexBuffer(
                            gd,
                            typeof<VertexInstanceWorld>,
                            instanceCount,
                            BufferUsage.WriteOnly
                          )

                        res.InstanceVertexBuffer <- ValueSome vb'
                      | _ -> ()

                      let instVB =
                        match res.InstanceVertexBuffer with
                        | ValueSome vb -> vb
                        | ValueNone -> Unchecked.defaultof<VertexBuffer> // unreachable

                      instVB.SetData(res.InstanceStaging, 0, instanceCount)

                      gd.SetVertexBuffers(
                        VertexBufferBinding(draw.Mesh.Vertices, 0, 0),
                        VertexBufferBinding(instVB, 0, 1)
                      )

                      gd.Indices <- draw.Mesh.Indices

                      PbrUniforms.setMatrix depthParams.ViewProj casterVP

                      for pass in depthEffect.CurrentTechnique.Passes do
                        pass.Apply()

                        gd.DrawInstancedPrimitives(
                          PrimitiveType.TriangleList,
                          0,
                          0,
                          draw.Mesh.PrimitiveCount,
                          instanceCount
                        )

                  depthEffect.CurrentTechnique <-
                    depthEffect.Techniques["Depth"]

            // ── Restore device state ──
            (gd.SetRenderTarget null)
            gd.Viewport <- prevViewport
            gd.RasterizerState <- prevRaster
            gd.BlendState <- prevBlend
            gd.DepthStencilState <- prevDepth

            // ── Upload shadow uniforms to the PBR effect ──
            let active = res.Atlas.ActiveCasterCount
            let maxC = atlasCfg.MaxCasters

            if res.ViewProjsScratch.Length <> maxC then
              res.ViewProjsScratch <- Array.zeroCreate<Matrix> maxC

            if res.UVOffsetsScratch.Length <> maxC then
              res.UVOffsetsScratch <- Array.zeroCreate<Vector4> maxC

            if res.BiasesScratch.Length <> maxC then
              res.BiasesScratch <- Array.zeroCreate<float32> maxC

            Array.Clear(res.ViewProjsScratch, 0, maxC)
            Array.Clear(res.UVOffsetsScratch, 0, maxC)
            Array.Clear(res.BiasesScratch, 0, maxC)

            if active > 0 then
              let vpArr = res.Atlas.ViewProjs
              let uvArr = res.Atlas.UVOffsets
              let biasArr = res.Atlas.Biases

              for i = 0 to active - 1 do
                res.ViewProjsScratch[i] <- vpArr[i]
                res.UVOffsetsScratch[i] <- uvArr[i]
                res.BiasesScratch[i] <- biasArr[i]

            let texel = 1.0f / float32 atlasCfg.Resolution

            match pbrParams with
            | ValueSome p ->
              PbrUniforms.setInt
                p.Shadow.DirLightCastsShadows
                (if hasDirCaster then 1 else 0)

              if active > 0 then
                PbrUniforms.setMatrixArray
                  p.Shadow.ShadowViewProjs
                  res.ViewProjsScratch

                PbrUniforms.setVec4Array
                  p.Shadow.ShadowUVOffsets
                  res.UVOffsetsScratch

                PbrUniforms.setFloatArray
                  p.Shadow.ShadowBiases
                  res.BiasesScratch

              PbrUniforms.setVec2
                p.Shadow.ShadowTexelSize
                (Vector2(texel, texel))

              if not(obj.ReferenceEquals(p.Shadow.ShadowAtlasTex, null)) then
                p.Shadow.ShadowAtlasTex.SetValue(res.Atlas.Fbo)

              gd.Textures[5] <- res.Atlas.Fbo
              gd.SamplerStates[5] <- SamplerState.PointClamp
            | ValueNone -> ()

            result <-
              ValueSome {
                Atlas = res.Atlas.Fbo
                ViewProjs = res.ViewProjsScratch
                UVOffsets = res.UVOffsetsScratch
                ActiveCasterCount = active
                TexelSize = texel
                DirLightCastsShadows = hasDirCaster
                PointLightShadowIdx = res.PointShadowSlots
                SpotLightShadowIdx = res.SpotShadowSlots
              }
      | _ -> () // DepthShadow.fx missing — render unshadowed (result stays ValueNone).

    result
