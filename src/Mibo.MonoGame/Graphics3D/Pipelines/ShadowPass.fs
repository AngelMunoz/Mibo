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
  /// Whether this draw was emitted while shadows were enabled. The shadow pass renders only
  /// draws with <c>CastsShadow = true</c>; the scene-depth pass renders all of them.
  CastsShadow: bool
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
  /// Whether this draw was emitted while shadows were enabled (see ShadowMeshDraw).
  CastsShadow: bool
}

/// <summary>A static <c>ModelMeshPart</c> caster collected for the shadow pass.</summary>
/// <remarks>
/// Like <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.ShadowSkinnedDraw"/>, this carries no
/// world-space bounds: a bare <c>ModelMeshPart</c> has no parent reference, so the part's
/// <c>ModelMesh.BoundingSphere</c> isn't reachable at collection time. Static-model casters are
/// therefore drawn unconditionally (no per-light frustum cull) — correct for typical scene sizes.
/// </remarks>
[<Struct>]
type ShadowModelPartDraw = {
  Part: ModelMeshPart
  Transform: Matrix
  /// Whether this draw was emitted while shadows were enabled (see ShadowMeshDraw).
  CastsShadow: bool
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
  /// Whether this draw was emitted while shadows were enabled (see ShadowMeshDraw).
  CastsShadow: bool
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

  /// <summary>Pooled static-ModelMeshPart caster draws (DrawModel/DrawModelWith). Grows on demand.</summary>
  member val ModelPartDraws =
    Array.zeroCreate<ShadowModelPartDraw> 32 with get, set

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

  /// <summary>Opaque-geometry counts from the last unified collection. Populated whenever the
  /// shadow pass or a scene-depth pass collected geometry. The scene-depth render reads the same
  /// collected arrays (all entries), while the shadow render reads only <c>CastsShadow</c> entries.</summary>
  member val CollectedDrawCount = 0 with get, set
  member val CollectedSkinnedCount = 0 with get, set
  member val CollectedModelPartCount = 0 with get, set
  member val CollectedInstancedCount = 0 with get, set

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

    // Effective directional tile resolution. With a dedicated directional region
    // (DirectionalAtlasRatio > 0) the directional caster occupies a top rectangle of size
    // (Resolution × Resolution×ratio); the texel density is set by the smaller side. Without
    // it, the legacy uniform grid gives the directional caster a 1/gridSize tile.
    let dirTileResolution =
      if atlasCfg.DirectionalAtlasRatio > 0.0f then
        let h = resolution * atlasCfg.DirectionalAtlasRatio
        min resolution h
      else
        let gridSize = MathF.Sqrt(float32 atlasCfg.MaxCasters)
        resolution / gridSize

    // World-space size of one shadow texel in the directional light's X/Y plane.
    // The config's GridSnapSize overrides this when set; otherwise we default to the
    // texel size so the shadow-map pixels stay locked to world geometry.
    // DirectionalLightSize is the FULL ortho height (matches the raylib backend).
    let texelWorld = orthoSize / dirTileResolution
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
    // The light sits at snappedOrigin + lightFromDir*lightDistance. Caster geometry lies
    // within the ortho window (half-height orthoSize/2) of the origin on the near side;
    // lightDistance + orthoSize keeps a generous margin without doubling the z-range like
    // the old (+orthoSize*2) did, which wasted depth precision on empty space behind the
    // scene (more shadow acne). Matches the raylib backend.
    let shadowFar = lightDistance + orthoSize + 1.0f

    let view = Matrix.CreateLookAt(lightPos, snappedOrigin, safeUp)

    // DirectionalLightSize is the FULL ortho height, matching the raylib backend's ortho
    // FovY semantics — same config value, same coverage, same texel density.
    let halfSize = orthoSize * 0.5f

    let proj =
      Matrix.CreateOrthographicOffCenter(
        -halfSize,
        halfSize,
        -halfSize,
        halfSize,
        shadowNear,
        shadowFar
      )

    view * proj

  /// <summary>
  /// Builds a point light's shadow ViewProj — a single-face 90° perspective frustum
  /// covering the given shadow direction. Used by the forward shader for point-light shadows.
  /// </summary>
  let buildPointViewProj
    (lightPos: Vector3, shadowDir: Vector3, lightRadius: float32)
    : Matrix =
    // Normalize so the parallel-up threshold check is accurate for non-unit
    // inputs; fall back to the default on zero-length (avoids NaN from a
    // degenerate forward in CreateLookAt).
    let dir =
      let len = shadowDir.Length()
      if len > 0.0001f then shadowDir / len else -Vector3.UnitY

    let safeUp = if abs dir.Y > 0.99f then Vector3.UnitZ else Vector3.UnitY

    let view = Matrix.CreateLookAt(lightPos, lightPos + dir, safeUp)
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
    let lightDir =
      Vector3.Normalize(Conversions.fromNumericsVector3 light.Direction)

    let safeUp =
      if abs lightDir.Y > 0.99f then
        Vector3.UnitZ
      else
        Vector3.UnitY

    let view =
      let pos = Conversions.fromNumericsVector3 light.Position
      Matrix.CreateLookAt(pos, pos + lightDir, safeUp)

    // Outer cutoff is a cosine; half-angle FOV = acos(outerCutoff), full FOV = 2× that.
    // Clamp to a safe open interval — CreatePerspectiveFieldOfView throws if FOV ∉ (0, π).
    let fov =
      max 0.01f (min (MathF.PI - 0.01f) (2.0f * MathF.Acos(light.OuterCutoff)))

    let nearPlane = max 0.0001f (min 0.1f (light.Radius * 0.5f))

    let proj =
      Matrix.CreatePerspectiveFieldOfView(fov, 1.0f, nearPlane, light.Radius)

    view * proj

  // ── drawPart: draw a single ModelMeshPart manually (part has no Draw() of its own). ──
  let drawPart(gd: GraphicsDevice, part: ModelMeshPart) =
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
  /// Scans the command buffer once and collects every opaque draw into the pooled arrays on
  /// <paramref name="res"/>, recording a <c>CastsShadow</c> flag per entry (snapshot of the
  /// <c>EnableShadows</c>/<c>DisableShadows</c> state when the draw was emitted). Shared by the
  /// shadow render (filters to <c>CastsShadow = true</c>) and the scene-depth render (all entries),
  /// so shadow + depth never re-scan the buffer. Stashes the four counts on <paramref name="res"/>.
  /// </summary>
  let collectGeometry (buffer: RenderBuffer3D) (res: ShadowResources) =
    let mutable castEnabled = true
    let mutable drawCount = 0
    let mutable skinnedCount = 0
    let mutable instancedCount = 0
    let mutable modelPartCount = 0
    let mutable shadowDraws = res.Draws
    let mutable shadowSkinnedDraws = res.SkinnedDraws
    let mutable shadowInstancedDraws = res.InstancedDraws
    let mutable shadowModelPartDraws = res.ModelPartDraws

    for i = 0 to buffer.Count - 1 do
      match buffer[i] with
      | Command3D.EnableShadows -> castEnabled <- true
      | Command3D.DisableShadows -> castEnabled <- false
      | Command3D.DrawPrimitive(mesh, transform, _) ->
        if drawCount >= shadowDraws.Length then
          Array.Resize(&shadowDraws, shadowDraws.Length * 2)
          res.Draws <- shadowDraws

        let worldBounds = mesh.Bounds.Transform transform

        shadowDraws[drawCount] <- {
          Mesh = mesh
          Transform = transform
          WorldBounds = worldBounds
          CastsShadow = castEnabled
        }

        drawCount <- drawCount + 1
      | Command3D.DrawAnimatedModel(model, transform, bones) ->
        for mesh in model.Meshes do
          for part in mesh.MeshParts do
            match part.Effect with
            | :? SkinnedEffect ->
              if skinnedCount >= shadowSkinnedDraws.Length then
                Array.Resize(&shadowSkinnedDraws, shadowSkinnedDraws.Length * 2)
                res.SkinnedDraws <- shadowSkinnedDraws

              shadowSkinnedDraws[skinnedCount] <- {
                Part = part
                Transform = transform
                Bones = bones
                CastsShadow = castEnabled
              }

              skinnedCount <- skinnedCount + 1
            | _ -> ()
      | Command3D.DrawModel(model, transform) ->
        for mesh in model.Meshes do
          for part in mesh.MeshParts do
            match part.Effect with
            | :? SkinnedEffect ->
              if skinnedCount >= shadowSkinnedDraws.Length then
                Array.Resize(&shadowSkinnedDraws, shadowSkinnedDraws.Length * 2)
                res.SkinnedDraws <- shadowSkinnedDraws

              shadowSkinnedDraws[skinnedCount] <- {
                Part = part
                Transform = transform
                Bones = Array.empty
                CastsShadow = castEnabled
              }

              skinnedCount <- skinnedCount + 1
            | _ ->
              if modelPartCount >= shadowModelPartDraws.Length then
                Array.Resize(
                  &shadowModelPartDraws,
                  shadowModelPartDraws.Length * 2
                )

                res.ModelPartDraws <- shadowModelPartDraws

              shadowModelPartDraws[modelPartCount] <- {
                Part = part
                Transform = transform
                CastsShadow = castEnabled
              }

              modelPartCount <- modelPartCount + 1
      | Command3D.DrawModelWith(model, transform, _) ->
        // Override material is irrelevant to depth; gather identically to DrawModel.
        for mesh in model.Meshes do
          for part in mesh.MeshParts do
            match part.Effect with
            | :? SkinnedEffect ->
              if skinnedCount >= shadowSkinnedDraws.Length then
                Array.Resize(&shadowSkinnedDraws, shadowSkinnedDraws.Length * 2)
                res.SkinnedDraws <- shadowSkinnedDraws

              shadowSkinnedDraws[skinnedCount] <- {
                Part = part
                Transform = transform
                Bones = Array.empty
                CastsShadow = castEnabled
              }

              skinnedCount <- skinnedCount + 1
            | _ ->
              if modelPartCount >= shadowModelPartDraws.Length then
                Array.Resize(
                  &shadowModelPartDraws,
                  shadowModelPartDraws.Length * 2
                )

                res.ModelPartDraws <- shadowModelPartDraws

              shadowModelPartDraws[modelPartCount] <- {
                Part = part
                Transform = transform
                CastsShadow = castEnabled
              }

              modelPartCount <- modelPartCount + 1
      | Command3D.DrawAnimatedModelWith(model, transform, bones, _) ->
        // Mirror DrawAnimatedModel: SkinnedEffect parts only.
        for mesh in model.Meshes do
          for part in mesh.MeshParts do
            match part.Effect with
            | :? SkinnedEffect ->
              if skinnedCount >= shadowSkinnedDraws.Length then
                Array.Resize(&shadowSkinnedDraws, shadowSkinnedDraws.Length * 2)
                res.SkinnedDraws <- shadowSkinnedDraws

              shadowSkinnedDraws[skinnedCount] <- {
                Part = part
                Transform = transform
                Bones = bones
                CastsShadow = castEnabled
              }

              skinnedCount <- skinnedCount + 1
            | _ -> ()
      | Command3D.DrawInstanced(mesh, transforms, _material, instanceCount) when
        instanceCount > 0
        ->
        // The world's instanced geometry (block grid, platforms, etc.). Collected whole (one entry
        // per emitted DrawInstanced). No per-instance cull — the sample chunk-culls the source
        // commands, so the emitted count is already bounded.
        if instancedCount >= shadowInstancedDraws.Length then
          Array.Resize(&shadowInstancedDraws, shadowInstancedDraws.Length * 2)
          res.InstancedDraws <- shadowInstancedDraws

        shadowInstancedDraws[instancedCount] <- {
          Mesh = mesh
          Transforms = transforms
          InstanceCount = instanceCount
          CastsShadow = castEnabled
        }

        instancedCount <- instancedCount + 1
      | _ -> ()

    res.CollectedDrawCount <- drawCount
    res.CollectedSkinnedCount <- skinnedCount
    res.CollectedModelPartCount <- modelPartCount
    res.CollectedInstancedCount <- instancedCount

  // ─────────────────────────────────────────────────────────────────────────────
  // Per-draw-type render helpers
  //
  // Each renders a span of collected draws through DepthShadow.fx into the currently-bound
  // render target. A shouldRender predicate decides per-entry inclusion:
  //   shadow pass → CastsShadow && (frustum-visible for primitives)
  //   scene-depth pass → always true (all opaque draws contribute depth)
  // ─────────────────────────────────────────────────────────────────────────────

  let renderPrimitiveSpan
    (gd: GraphicsDevice)
    (effect: Effect)
    (shadowParams: ShadowEffectParams)
    (viewProj: Matrix)
    (draws: ShadowMeshDraw[])
    (count: int)
    (shouldRender: int -> bool)
    =
    for d = 0 to count - 1 do
      if shouldRender d then
        let draw = draws[d]
        PbrUniforms.setMatrix shadowParams.MatModel draw.Transform
        PbrUniforms.setMatrix shadowParams.ViewProj viewProj

        for pass in effect.CurrentTechnique.Passes do
          pass.Apply()
          draw.Mesh.Draw(gd, effect)

  let renderModelPartSpan
    (gd: GraphicsDevice)
    (effect: Effect)
    (shadowParams: ShadowEffectParams)
    (viewProj: Matrix)
    (draws: ShadowModelPartDraw[])
    (count: int)
    (shouldRender: int -> bool)
    =
    for d = 0 to count - 1 do
      if shouldRender d then
        let draw = draws[d]
        PbrUniforms.setMatrix shadowParams.MatModel draw.Transform
        PbrUniforms.setMatrix shadowParams.ViewProj viewProj

        let saved = draw.Part.Effect
        draw.Part.Effect <- effect

        try
          drawPart(gd, draw.Part)
        finally
          draw.Part.Effect <- saved

  let renderSkinnedSpan
    (gd: GraphicsDevice)
    (effect: Effect)
    (shadowParams: ShadowEffectParams)
    (viewProj: Matrix)
    (boneScratch: Matrix[])
    (draws: ShadowSkinnedDraw[])
    (count: int)
    (shouldRender: int -> bool)
    =
    effect.CurrentTechnique <- effect.Techniques["DepthSkinned"]

    for d = 0 to count - 1 do
      if shouldRender d then
        let draw = draws[d]
        PbrUniforms.setMatrix shadowParams.MatModel draw.Transform
        PbrUniforms.setMatrix shadowParams.ViewProj viewProj

        let boneCount = min draw.Bones.Length boneScratch.Length

        for i = 0 to boneCount - 1 do
          boneScratch[i] <- draw.Bones[i]

        for i = boneCount to boneScratch.Length - 1 do
          boneScratch[i] <- Matrix.Identity

        PbrUniforms.setMatrixArray shadowParams.Bones boneScratch

        let saved = draw.Part.Effect
        draw.Part.Effect <- effect

        try
          drawPart(gd, draw.Part)
        finally
          draw.Part.Effect <- saved

    effect.CurrentTechnique <- effect.Techniques["Depth"]

  let renderInstancedSpan
    (gd: GraphicsDevice)
    (effect: Effect)
    (shadowParams: ShadowEffectParams)
    (viewProj: Matrix)
    (res: ShadowResources)
    (draws: ShadowInstancedDraw[])
    (count: int)
    (shouldRender: int -> bool)
    =
    effect.CurrentTechnique <- effect.Techniques["DepthInstanced"]
    PbrUniforms.setMatrix shadowParams.MatModel Matrix.Identity

    for d = 0 to count - 1 do
      if shouldRender d then
        let draw = draws[d]
        let instanceCount = min draw.InstanceCount draw.Transforms.Length

        if instanceCount > 0 then
          if res.InstanceStaging.Length < instanceCount then
            res.InstanceStaging <-
              Array.zeroCreate<VertexInstanceWorld> instanceCount

          for i = 0 to instanceCount - 1 do
            res.InstanceStaging[i] <-
              VertexInstanceWorld.Create draw.Transforms[i]

          // NOTE: must stay a DynamicVertexBuffer — same DX12 upload-ordering
          // hazard as the forward pass (see PbrShading.stageInstanceData): static
          // buffer SetData executes out of order vs draws, so instanced shadow
          // casters would read the last group's matrices.
          match res.InstanceVertexBuffer with
          | ValueNone ->
            let vb =
              new DynamicVertexBuffer(
                gd,
                typeof<VertexInstanceWorld>,
                instanceCount,
                BufferUsage.WriteOnly
              )

            res.InstanceVertexBuffer <- ValueSome vb
          | ValueSome vb when vb.VertexCount < instanceCount ->
            vb.Dispose()

            let vb' =
              new DynamicVertexBuffer(
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
          PbrUniforms.setMatrix shadowParams.ViewProj viewProj

          for pass in effect.CurrentTechnique.Passes do
            pass.Apply()

            gd.DrawInstancedPrimitives(
              PrimitiveType.TriangleList,
              0,
              0,
              draw.Mesh.PrimitiveCount,
              instanceCount
            )

    effect.CurrentTechnique <- effect.Techniques["Depth"]

  // ─────────────────────────────────────────────────────────────────────────────
  // High-level passes
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Renders collected casters into the shadow atlas from each registered light's view-projection.
  /// Filters to <c>CastsShadow = true</c> entries and frustum-culls per caster (primitives only;
  /// skinned/model-part/instanced draw unconditionally as before). Saves/restores device state.
  /// </summary>
  let renderAtlasCasters
    (gd: GraphicsDevice)
    (res: ShadowResources)
    (biasCfg: ShadowBiasConfig)
    (depthEffect: Effect)
    (depthParams: ShadowEffectParams)
    =
    let shadowDraws = res.Draws
    let skinnedDraws = res.SkinnedDraws
    let instancedDraws = res.InstancedDraws
    let modelPartDraws = res.ModelPartDraws
    let drawCount = res.CollectedDrawCount
    let skinnedCount = res.CollectedSkinnedCount
    let modelPartCount = res.CollectedModelPartCount
    let instancedCount = res.CollectedInstancedCount

    if
      drawCount = 0
      && skinnedCount = 0
      && instancedCount = 0
      && modelPartCount = 0
    then
      ()
    else
      let prevViewport = gd.Viewport
      let prevRaster = gd.RasterizerState
      let prevBlend = gd.BlendState
      let prevDepth = gd.DepthStencilState

      gd.SetRenderTarget(res.Atlas.Fbo)

      gd.Clear(
        ClearOptions.Target ||| ClearOptions.DepthBuffer,
        Color.White.ToVector4(),
        1.0f,
        0
      )

      if obj.ReferenceEquals(res.Raster, null) then
        let sr = new RasterizerState()
        sr.CullMode <- CullMode.CullCounterClockwiseFace
        sr.DepthBias <- biasCfg.DirectionalBias
        sr.SlopeScaleDepthBias <- biasCfg.SlopeScaleBias
        res.Raster <- sr

      gd.RasterizerState <- res.Raster
      gd.BlendState <- BlendState.Opaque
      gd.DepthStencilState <- DepthStencilState.Default
      depthEffect.CurrentTechnique <- depthEffect.Techniques["Depth"]

      let shadowFrustum = res.Frustum

      for caster in res.Atlas.Casters do
        if caster.Enabled then
          let casterVP = res.Atlas.GetRegionViewProj caster.AtlasRegion
          gd.Viewport <- res.Atlas.GetRegionViewport(caster.AtlasRegion)
          shadowFrustum.Matrix <- casterVP

          renderPrimitiveSpan
            gd
            depthEffect
            depthParams
            casterVP
            shadowDraws
            drawCount
            (fun d ->
              shadowDraws[d].CastsShadow
              && Culling.isVisible shadowFrustum shadowDraws[d].WorldBounds)

          renderModelPartSpan
            gd
            depthEffect
            depthParams
            casterVP
            modelPartDraws
            modelPartCount
            (fun d -> modelPartDraws[d].CastsShadow)

          renderSkinnedSpan
            gd
            depthEffect
            depthParams
            casterVP
            res.BonePaletteScratch
            skinnedDraws
            skinnedCount
            (fun d -> skinnedDraws[d].CastsShadow)

          renderInstancedSpan
            gd
            depthEffect
            depthParams
            casterVP
            res
            instancedDraws
            instancedCount
            (fun d -> instancedDraws[d].CastsShadow)

      gd.SetRenderTarget null
      gd.Viewport <- prevViewport
      gd.RasterizerState <- prevRaster
      gd.BlendState <- prevBlend
      gd.DepthStencilState <- prevDepth

  /// <summary>
  /// Renders ALL collected opaque geometry from a camera view-projection into an R32F depth target
  /// (NDC z in [0,1], 1.0 = far). No frustum cull (the camera frustum already bounds the visible
  /// scene), no <c>CastsShadow</c> filter — every opaque draw contributes to depth so post-process
  /// distance effects (fog, DOF, SSAO) get correct per-pixel distance. Saves/restores device state.
  /// </summary>
  let renderSceneDepth
    (gd: GraphicsDevice)
    (res: ShadowResources)
    (depthEffect: Effect)
    (depthParams: ShadowEffectParams)
    (viewProj: Matrix)
    (depthTarget: RenderTarget2D)
    =
    let prevViewport = gd.Viewport
    let prevRaster = gd.RasterizerState
    let prevBlend = gd.BlendState
    let prevDepth = gd.DepthStencilState

    gd.SetRenderTarget depthTarget
    // 1.0 = far: uncovered pixels (skybox, gaps) read as far so post-process fog treats them as
    // fully fogged rather than near. Clear as an explicit Vector4 so the R32F target gets .r=1.0.
    // Clear depth too — the RT has a Depth24 buffer for hardware depth testing during the pre-pass.
    gd.Clear(
      ClearOptions.Target ||| ClearOptions.DepthBuffer,
      Color.White.ToVector4(),
      1.0f,
      0
    )

    gd.RasterizerState <- RasterizerState.CullCounterClockwise
    gd.BlendState <- BlendState.Opaque
    gd.DepthStencilState <- DepthStencilState.Default
    depthEffect.CurrentTechnique <- depthEffect.Techniques["Depth"]

    renderPrimitiveSpan
      gd
      depthEffect
      depthParams
      viewProj
      res.Draws
      res.CollectedDrawCount
      (fun _ -> true)

    renderModelPartSpan
      gd
      depthEffect
      depthParams
      viewProj
      res.ModelPartDraws
      res.CollectedModelPartCount
      (fun _ -> true)

    renderSkinnedSpan
      gd
      depthEffect
      depthParams
      viewProj
      res.BonePaletteScratch
      res.SkinnedDraws
      res.CollectedSkinnedCount
      (fun _ -> true)

    renderInstancedSpan
      gd
      depthEffect
      depthParams
      viewProj
      res
      res.InstancedDraws
      res.CollectedInstancedCount
      (fun _ -> true)

    gd.SetRenderTarget null
    gd.Viewport <- prevViewport
    gd.RasterizerState <- prevRaster
    gd.BlendState <- prevBlend
    gd.DepthStencilState <- prevDepth
  // ─────────────────────────────────────────────────────────────────────────────
  // Shadow-specific helpers (caster registration + uniform upload)
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Registers shadow-casting lights (dir → point → spot) into the atlas and returns the number
  /// of casters that fit. The flat shader-array index == registration order (matches
  /// <c>PrepareUniforms</c>). Returns 0 when the atlas is full or no caster registered.
  /// </summary>
  let registerCasters
    (atlasCfg: ShadowAtlasConfig)
    (res: ShadowResources)
    (lights: LightBuffers)
    (activeCamera: Camera3D)
    : int =
    res.Atlas.Clear()
    let mutable casterSlot = 0

    // Directional caster first (slot 0 by convention).
    let hasDirCaster =
      let mutable found = false

      for i = 0 to lights.DirLights.Count - 1 do
        if lights.DirLights[i].CastsShadows then
          found <- true

      found

    if hasDirCaster then
      let mutable dirIdx = 0

      while not lights.DirLights[dirIdx].CastsShadows do
        dirIdx <- dirIdx + 1

      let dirLight = lights.DirLights[dirIdx]
      let dir = Conversions.fromNumericsVector3 dirLight.Direction
      let vp = buildDirectionalViewProj atlasCfg res.Origin dir activeCamera

      match
        res.Atlas.AddCaster(
          ShadowCasterType.Directional,
          Vector3.Zero,
          dir,
          Vector3.Zero,
          true,
          ValueNone
        )
      with
      | ValueSome _ ->
        res.Atlas.SetRegionViewProj(casterSlot, vp)
        casterSlot <- casterSlot + 1
      | ValueNone -> ()

    // Point lights.
    for i = 0 to lights.PointLights.Count - 1 do
      let pt = lights.PointLights[i]

      if pt.CastsShadows then
        let ptPos = Conversions.fromNumericsVector3 pt.Position

        let shadowDir =
          Conversions.fromNumericsVector3(
            match pt.ShadowDirection with
            | ValueSome d -> d
            | ValueNone -> -System.Numerics.Vector3.UnitY
          )

        let vp = buildPointViewProj(ptPos, shadowDir, pt.Radius)

        match
          res.Atlas.AddCaster(
            ShadowCasterType.Point,
            ptPos,
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
        let spPos = Conversions.fromNumericsVector3 sp.Position
        let spDir = Conversions.fromNumericsVector3 sp.Direction

        match
          res.Atlas.AddCaster(
            ShadowCasterType.Spot,
            spPos,
            spDir,
            spPos + spDir,
            true,
            sp.ShadowBias
          )
        with
        | ValueSome _ ->
          res.Atlas.SetRegionViewProj(casterSlot, vp)
          res.SpotShadowSlots[i] <- casterSlot
          casterSlot <- casterSlot + 1
        | ValueNone -> ()

    casterSlot

  /// <summary>
  /// Copies shadow atlas data (view-projections, UV offsets, biases, texel size, atlas texture)
  /// into the PBR effect's shadow uniform group so the forward pass can sample shadows.
  /// </summary>
  let uploadShadowUniforms
    (gd: GraphicsDevice)
    (res: ShadowResources)
    (atlasCfg: ShadowAtlasConfig)
    (pbrParams: PbrEffectParams voption)
    (hasDirCaster: bool)
    =
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
        PbrUniforms.setMatrixArray p.Shadow.ShadowViewProjs res.ViewProjsScratch
        PbrUniforms.setVec4Array p.Shadow.ShadowUVOffsets res.UVOffsetsScratch
        PbrUniforms.setFloatArray p.Shadow.ShadowBiases res.BiasesScratch

      PbrUniforms.setVec2 p.Shadow.ShadowTexelSize (Vector2(texel, texel))

      if not(obj.ReferenceEquals(p.Shadow.ShadowAtlasTex, null)) then
        p.Shadow.ShadowAtlasTex.SetValue(res.Atlas.Fbo)

      gd.Textures[5] <- res.Atlas.Fbo
      // PointClamp on every backend: the forward shader point-samples depth and does
      // the 3×3 PCF comparison in-shader (matches ForwardPbr.fx and the raylib backend).
      gd.SamplerStates[5] <- SamplerState.PointClamp
    | ValueNone -> ()

  // ─────────────────────────────────────────────────────────────────────────────
  // Orchestrator
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Runs the shadow pass: scans lights, collects opaque geometry once (shared with scene-depth),
  /// registers casters into the atlas, renders depth, and uploads shadow uniforms to the PBR
  /// effect. When <paramref name="needsDepth"/> is true, geometry is collected even without a
  /// shadow-casting light so <c>renderSceneDepth</c> can reuse it.
  /// </summary>
  let run
    (gd: GraphicsDevice)
    (atlasCfg: ShadowAtlasConfig)
    (biasCfg: ShadowBiasConfig)
    (res: ShadowResources)
    (lights: LightBuffers)
    (pbrParams: PbrEffectParams voption)
    (buffer: RenderBuffer3D)
    (activeCamera: Camera3D)
    (needsDepth: bool)
    : ShadowResult voption =
    // ── Scan lights for casters ──
    let mutable hasDirCaster = false
    let mutable hasPointCaster = false
    let mutable hasSpotCaster = false

    for i = 0 to lights.DirLights.Count - 1 do
      if lights.DirLights[i].CastsShadows then
        hasDirCaster <- true

    for i = 0 to lights.PointLights.Count - 1 do
      if lights.PointLights[i].CastsShadows then
        hasPointCaster <- true

    for i = 0 to lights.SpotLights.Count - 1 do
      if lights.SpotLights[i].CastsShadows then
        hasSpotCaster <- true

    let hasAnyCaster = hasDirCaster || hasPointCaster || hasSpotCaster

    // ── Init per-light slot mappings (-1 = no shadow) ──
    if res.PointShadowSlots.Length < lights.PointLights.Count then
      res.PointShadowSlots <- Array.create<int> lights.PointLights.Count -1
    else
      Array.Fill(res.PointShadowSlots, -1)

    if res.SpotShadowSlots.Length < lights.SpotLights.Count then
      res.SpotShadowSlots <- Array.create<int> lights.SpotLights.Count -1
    else
      Array.Fill(res.SpotShadowSlots, -1)

    // ── Collect opaque geometry ONCE (shared by shadow render + scene-depth render) ──
    if hasAnyCaster || needsDepth then
      collectGeometry buffer res

    // ── Shadow pass (only when a shadow-casting light exists) ──
    if not hasAnyCaster then
      match pbrParams with
      | ValueSome p -> PbrUniforms.setInt p.Shadow.DirLightCastsShadows 0
      | ValueNone -> ()

      // Even without shadow casters, load the DepthShadow effect when needsDepth is true so
      // renderSceneDepth can run (fog/DoF/SSOA in a shadowless scene). Without this the effect
      // is never loaded and every postProcessWithDepth action silently receives Depth = None.
      if needsDepth then
        match res.Effect, res.Params with
        | ValueSome _, ValueSome _ -> ()
        | _ ->
          match ShaderLoader.loadEffect gd "DepthShadow" with
          | ValueSome e ->
            res.Params <- ValueSome(buildShadowParams e)
            res.Effect <- ValueSome e
          | ValueNone -> ()

      ValueNone
    else
      res.Atlas.EnsureResources gd

      // Load DepthShadow effect on first use.
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
        let casterSlot = registerCasters atlasCfg res lights activeCamera

        if casterSlot = 0 then
          // Atlas full — clear the flag so the PBR shader doesn't sample stale shadow data.
          match pbrParams with
          | ValueSome p -> PbrUniforms.setInt p.Shadow.DirLightCastsShadows 0
          | ValueNone -> ()

          ValueNone
        else
          res.Atlas.PrepareUniforms()
          renderAtlasCasters gd res biasCfg depthEffect depthParams
          uploadShadowUniforms gd res atlasCfg pbrParams hasDirCaster

          ValueSome {
            Atlas = res.Atlas.Fbo
            ViewProjs = res.ViewProjsScratch
            UVOffsets = res.UVOffsetsScratch
            ActiveCasterCount = res.Atlas.ActiveCasterCount
            TexelSize = 1.0f / float32 atlasCfg.Resolution
            Biases = res.BiasesScratch
            DirLightCastsShadows = hasDirCaster
            PointLightShadowIdx = res.PointShadowSlots
            SpotLightShadowIdx = res.SpotShadowSlots
          }
      | _ ->
        // DepthShadow.fx missing — render unshadowed.
        ValueNone
