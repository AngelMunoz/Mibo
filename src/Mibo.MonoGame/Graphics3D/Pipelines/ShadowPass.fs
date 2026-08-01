namespace Mibo.Elmish.Graphics3D.Pipelines

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open MonoGame.Framework.Utilities
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

/// <summary>A skinned + instanced caster draw collected for the shadow pass.</summary>
/// <remarks>
/// One entry per skinned <c>ModelMeshPart</c> of a <c>DrawAnimatedModelInstanced</c> command —
/// or one per MERGED part group when the model has mergeable skinned parts (off-GL;
/// depth binds no material state, so merged geometry is always valid here). Shares
/// the command's per-instance transforms + flat bone palettes
/// (<c>InstanceCount * boneCount</c>, instance-major). Rendered via the depth effect's
/// <c>DepthSkinnedInstanced</c> technique (palette texture + two-stream bind); on the OpenGL
/// backend (no vertex texture fetch) the pass falls back to per-instance <c>DepthSkinned</c>
/// draws. Colors/material overrides are irrelevant to depth and not carried. Like the other
/// part-based casters, no world-space bounds — drawn unconditionally.
/// </remarks>
[<Struct>]
type ShadowSkinnedInstancedDraw = {
  /// The original part — used ONLY by the OpenGL per-instance fallback (drawPart needs
  /// its Effect). Null for merged entries, which that path never produces.
  Part: ModelMeshPart
  /// Draw geometry: the original part's buffers (VertexOffset/StartIndex/PrimitiveCount
  /// of the part) or the merged group's buffers (offsets 0) — see
  /// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.MergedModelParts.tryGet"/>.
  VertexBuffer: VertexBuffer
  IndexBuffer: IndexBuffer
  VertexOffset: int
  StartIndex: int
  PrimitiveCount: int
  Transforms: Matrix[]
  Palettes: Matrix[]
  InstanceCount: int
  BoneCount: int
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
  // Skinned + instanced (DepthSkinnedInstanced technique): the RGBA32F bone-palette
  // texture + its texel dimensions. null on the OpenGL effect (the technique is
  // compiled out there) — the null-safe setters no-op.
  PaletteTex: EffectParameter
  PaletteTexSize: EffectParameter
  // Grouped-uniform skinned + instanced (DepthSkinnedInstancedGrouped technique — the
  // DX12 fallback): one group of bone palettes as a constant array + the bone count
  // per instance. null on the OpenGL effect; unused on DX11/Vulkan.
  BonePaletteGroup: EffectParameter
  GroupBoneCount: EffectParameter
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

  /// <summary>The isolated grouped-uniform depth effect (DepthShadowGrouped.fx), loaded lazily
  /// on DX12 only. On DX12 the main DepthShadow.fx's bonePaletteGroup params are dropped by
  /// mgfx reflection; this isolated effect carries them.</summary>
  member val GroupedEffect: Effect voption = ValueNone with get, set

  /// <summary>Cached grouped depth-effect parameter handles (built when the grouped effect loads).</summary>
  member val GroupedParams: ShadowEffectParams voption = ValueNone with get, set

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

  /// <summary>Pooled skinned + instanced caster draws (DrawAnimatedModelInstanced). Grows on demand.</summary>
  member val SkinnedInstancedDraws =
    Array.zeroCreate<ShadowSkinnedInstancedDraw> 8 with get, set

  /// <summary>Scratch: original parts covered by a merged group during skinned +
  /// instanced caster collection; cleared per command.</summary>
  member val MergedCovered =
    System.Collections.Generic.HashSet<ModelMeshPart>() with get

  /// <summary>CPU staging array for the per-instance <c>VertexInstanceWorldPalette</c> rows of
  /// skinned + instanced casters. Grows to the largest chunk seen. Reused across frames.</summary>
  member val SkinnedInstancedStaging =
    Array.zeroCreate<VertexInstanceWorldPalette> 64 with get, set

  /// <summary>Growable per-instance vertex buffer for skinned + instanced shadow casters. Owned
  /// by the shadow pass (not shared with the forward-pass PBR buffers) so the two passes never
  /// race on it. Disposed at shutdown; recreated when a chunk exceeds the current capacity.</summary>
  member val SkinnedInstancedVertexBuffer: VertexBuffer voption =
    ValueNone with get, set

  /// <summary>Cached 2-slot binding array for SetVertexBuffers on the instanced paths
  /// (avoids the params-array allocation per call — thousands per frame on DX12).
  /// Contents are rewritten per call and consumed immediately.</summary>
  member val InstanceBindings = Array.zeroCreate<VertexBufferBinding> 2 with get

  /// <summary>Shared per-frame palette-chunk cache for skinned + instanced casters (aliased
  /// with the forward pass by ForwardPipeline so each frame's palettes are staged + uploaded
  /// once, not per pass — see <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.PaletteChunkCache"/>).</summary>
  member val PaletteChunks = new PaletteChunkCache() with get, set

  /// <summary>Shared per-frame instance-world staging cache for skinned + instanced casters
  /// (aliased with the forward pass by ForwardPipeline — DX11/Vulkan only, see
  /// <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.InstanceWorldCache"/>).</summary>
  member val InstanceWorlds = new InstanceWorldCache() with get, set

  /// <summary>Pooled bone-palette scratch for the grouped-uniform skinned + instanced depth
  /// path (the DX12 fallback — DepthSkinnedInstancedGrouped). Sized to
  /// <see cref="F:Mibo.Elmish.Graphics3D.Pipelines.PaletteGroup.MaxMatricesDepth"/>; grown on demand.</summary>
  member val GroupPaletteScratch: Matrix[] = [||] with get, set

  /// <summary>Pooled DX12 group descriptors ((start, count, null-texture) triples)
  /// for the grouped-uniform skinned + instanced depth path; grown on demand — see
  /// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.PaletteGroup.planGroups"/>.</summary>
  member val GroupChunkScratch: struct (int * int * Texture2D)[] =
    [||] with get, set

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

  /// <summary>Grow-only scratch for save/restore of the caller's render-target bindings around
  /// a shadow/depth pass — avoids a <c>GetRenderTargets()</c> array allocation per pass.
  /// Resized only when the bound count changes; used sequentially (atlas pass, then depth
  /// pre-pass), never re-entrantly.</summary>
  member val RenderTargetScratch: RenderTargetBinding[] = [||] with get, set

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
  member val CollectedSkinnedInstancedCount = 0 with get, set

  /// <summary>Running <c>EnableShadows</c>/<c>DisableShadows</c> state while a collection
  /// walk is in progress (set by <c>ShadowPass.beginCollect</c>, read by
  /// <c>ShadowPass.collectCommand</c>). Outside a collection walk its value is stale.</summary>
  member val CollectCastEnabled = true with get, set

/// <summary>The extracted shadow pass: caster geometry types, per-light ViewProj builders, and the pass body.</summary>
module internal ShadowPass =

  /// <summary>Builds the shadow-pass parameter handles once after load.</summary>
  let buildShadowParams(e: Effect) : ShadowEffectParams = {
    MatModel = e.Parameters["matModel"]
    ViewProj = e.Parameters["viewProj"]
    Bones = e.Parameters["boneMatrices"]
    PaletteTex = e.Parameters["paletteTex"]
    PaletteTexSize = e.Parameters["paletteTexSize"]
    BonePaletteGroup = e.Parameters["bonePaletteGroup"]
    GroupBoneCount = e.Parameters["groupBoneCount"]
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
    // within the ortho window (half-height orthoSize/2) of the origin on the near side, so
    // lightDistance + orthoSize covers it with margin; the extra unit keeps casters at the
    // ortho boundary from clipping at the far plane. Coverage matches the previous release:
    // the old lightDistance + orthoSize*2 used the half-size orthoSize, i.e. the same
    // distance plus the full window height. Matches the raylib backend.
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
  /// Starts a geometry-collection walk: resets the collected counts and the running
  /// <c>EnableShadows</c>/<c>DisableShadows</c> state on <paramref name="res"/>. Pair with
  /// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ShadowPass.collectCommand"/> per command —
  /// either driven by <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ShadowPass.collectGeometry"/>
  /// over a buffer slice, or inline in the forward pipeline's pre-scan (single-camera frames,
  /// so the frame's buffer is walked twice instead of three times).
  /// </summary>
  let beginCollect (res: ShadowResources) (initialCastEnabled: bool) =
    res.CollectCastEnabled <- initialCastEnabled
    res.CollectedDrawCount <- 0
    res.CollectedSkinnedCount <- 0
    res.CollectedModelPartCount <- 0
    res.CollectedInstancedCount <- 0
    res.CollectedSkinnedInstancedCount <- 0

  /// <summary>
  /// Collects one buffer command into the pooled arrays on <paramref name="res"/>, recording a
  /// <c>CastsShadow</c> flag per entry (snapshot of the running cast state — see
  /// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ShadowPass.beginCollect"/>). Shared by the
  /// shadow render (filters to <c>CastsShadow = true</c>) and the scene-depth render (all
  /// entries). <paramref name="gd"/> is used only to lazily build merged part groups for
  /// skinned + instanced models (<see cref="M:Mibo.Elmish.Graphics3D.Pipelines.MergedModelParts.tryGet"/>).
  /// </summary>
  let collectCommand
    (gd: GraphicsDevice)
    (res: ShadowResources)
    (cmd: Command3D)
    =
    let castEnabled = res.CollectCastEnabled
    let mutable shadowDraws = res.Draws
    let mutable shadowSkinnedDraws = res.SkinnedDraws
    let mutable shadowInstancedDraws = res.InstancedDraws
    let mutable shadowModelPartDraws = res.ModelPartDraws
    let mutable shadowSkinnedInstancedDraws = res.SkinnedInstancedDraws

    match cmd with
    | Command3D.EnableShadows -> res.CollectCastEnabled <- true
    | Command3D.DisableShadows -> res.CollectCastEnabled <- false
    | Command3D.DrawPrimitive(mesh, transform, _) ->
      if res.CollectedDrawCount >= shadowDraws.Length then
        Array.Resize(&shadowDraws, shadowDraws.Length * 2)
        res.Draws <- shadowDraws

      let worldBounds = mesh.Bounds.Transform transform

      shadowDraws[res.CollectedDrawCount] <- {
        Mesh = mesh
        Transform = transform
        WorldBounds = worldBounds
        CastsShadow = castEnabled
      }

      res.CollectedDrawCount <- res.CollectedDrawCount + 1
    | Command3D.DrawAnimatedModel(model, transform, bones) ->
      for mesh in model.Meshes do
        for part in mesh.MeshParts do
          match part.Effect with
          | :? SkinnedEffect ->
            if res.CollectedSkinnedCount >= shadowSkinnedDraws.Length then
              Array.Resize(&shadowSkinnedDraws, shadowSkinnedDraws.Length * 2)
              res.SkinnedDraws <- shadowSkinnedDraws

            shadowSkinnedDraws[res.CollectedSkinnedCount] <- {
              Part = part
              Transform = transform
              Bones = bones
              CastsShadow = castEnabled
            }

            res.CollectedSkinnedCount <- res.CollectedSkinnedCount + 1
          | _ -> ()
    | Command3D.DrawModel(model, transform) ->
      for mesh in model.Meshes do
        for part in mesh.MeshParts do
          match part.Effect with
          | :? SkinnedEffect ->
            if res.CollectedSkinnedCount >= shadowSkinnedDraws.Length then
              Array.Resize(&shadowSkinnedDraws, shadowSkinnedDraws.Length * 2)
              res.SkinnedDraws <- shadowSkinnedDraws

            shadowSkinnedDraws[res.CollectedSkinnedCount] <- {
              Part = part
              Transform = transform
              Bones = Array.empty
              CastsShadow = castEnabled
            }

            res.CollectedSkinnedCount <- res.CollectedSkinnedCount + 1
          | _ ->
            if res.CollectedModelPartCount >= shadowModelPartDraws.Length then
              Array.Resize(
                &shadowModelPartDraws,
                shadowModelPartDraws.Length * 2
              )

              res.ModelPartDraws <- shadowModelPartDraws

            shadowModelPartDraws[res.CollectedModelPartCount] <- {
              Part = part
              Transform = transform
              CastsShadow = castEnabled
            }

            res.CollectedModelPartCount <- res.CollectedModelPartCount + 1
    | Command3D.DrawModelWith(model, transform, _) ->
      // Override material is irrelevant to depth; gather identically to DrawModel.
      for mesh in model.Meshes do
        for part in mesh.MeshParts do
          match part.Effect with
          | :? SkinnedEffect ->
            if res.CollectedSkinnedCount >= shadowSkinnedDraws.Length then
              Array.Resize(&shadowSkinnedDraws, shadowSkinnedDraws.Length * 2)
              res.SkinnedDraws <- shadowSkinnedDraws

            shadowSkinnedDraws[res.CollectedSkinnedCount] <- {
              Part = part
              Transform = transform
              Bones = Array.empty
              CastsShadow = castEnabled
            }

            res.CollectedSkinnedCount <- res.CollectedSkinnedCount + 1
          | _ ->
            if res.CollectedModelPartCount >= shadowModelPartDraws.Length then
              Array.Resize(
                &shadowModelPartDraws,
                shadowModelPartDraws.Length * 2
              )

              res.ModelPartDraws <- shadowModelPartDraws

            shadowModelPartDraws[res.CollectedModelPartCount] <- {
              Part = part
              Transform = transform
              CastsShadow = castEnabled
            }

            res.CollectedModelPartCount <- res.CollectedModelPartCount + 1
    | Command3D.DrawAnimatedModelWith(model, transform, bones, _) ->
      // Mirror DrawAnimatedModel: SkinnedEffect parts only.
      for mesh in model.Meshes do
        for part in mesh.MeshParts do
          match part.Effect with
          | :? SkinnedEffect ->
            if res.CollectedSkinnedCount >= shadowSkinnedDraws.Length then
              Array.Resize(&shadowSkinnedDraws, shadowSkinnedDraws.Length * 2)
              res.SkinnedDraws <- shadowSkinnedDraws

            shadowSkinnedDraws[res.CollectedSkinnedCount] <- {
              Part = part
              Transform = transform
              Bones = bones
              CastsShadow = castEnabled
            }

            res.CollectedSkinnedCount <- res.CollectedSkinnedCount + 1
          | _ -> ()
    | Command3D.DrawInstanced(mesh,
                              transforms,
                              _colors,
                              _material,
                              instanceCount) when instanceCount > 0 ->
      // The world's instanced geometry (block grid, platforms, etc.). Collected whole (one entry
      // per emitted DrawInstanced). No per-instance cull — the sample chunk-culls the source
      // commands, so the emitted count is already bounded.
      if res.CollectedInstancedCount >= shadowInstancedDraws.Length then
        Array.Resize(&shadowInstancedDraws, shadowInstancedDraws.Length * 2)
        res.InstancedDraws <- shadowInstancedDraws

      shadowInstancedDraws[res.CollectedInstancedCount] <- {
        Mesh = mesh
        Transforms = transforms
        InstanceCount = instanceCount
        CastsShadow = castEnabled
      }

      res.CollectedInstancedCount <- res.CollectedInstancedCount + 1
    | Command3D.DrawAnimatedModelInstanced(model,
                                           transforms,
                                           palettes,
                                           _,
                                           _,
                                           instanceCount,
                                           boneCount) when instanceCount > 0 ->
      // Skinned + instanced casters: one entry per SkinnedEffect part — or one per
      // MERGED skinned part group off-GL (depth binds no material state, so merged
      // geometry is always valid here; the GL per-instance fallback needs the real
      // parts for their Effect). Sharing the command's transforms + flat palettes.
      let addDraw
        (
          part: ModelMeshPart,
          vb: VertexBuffer,
          ib: IndexBuffer,
          vertexOffset: int,
          startIndex: int,
          primitiveCount: int
        ) =
        if
          res.CollectedSkinnedInstancedCount
          >= shadowSkinnedInstancedDraws.Length
        then
          Array.Resize(
            &shadowSkinnedInstancedDraws,
            shadowSkinnedInstancedDraws.Length * 2
          )

          res.SkinnedInstancedDraws <- shadowSkinnedInstancedDraws

        shadowSkinnedInstancedDraws[res.CollectedSkinnedInstancedCount] <- {
          Part = part
          VertexBuffer = vb
          IndexBuffer = ib
          VertexOffset = vertexOffset
          StartIndex = startIndex
          PrimitiveCount = primitiveCount
          Transforms = transforms
          Palettes = palettes
          InstanceCount = instanceCount
          BoneCount = boneCount
          CastsShadow = castEnabled
        }

        res.CollectedSkinnedInstancedCount <-
          res.CollectedSkinnedInstancedCount + 1

      let mergedParts =
        if PlatformInfo.GraphicsBackend = GraphicsBackend.OpenGL then
          ValueNone
        else
          MergedModelParts.tryGet(gd, model)

      match mergedParts with
      | ValueSome merged ->
        res.MergedCovered.Clear()

        for mp in merged do
          for sp in mp.SourceParts do
            res.MergedCovered.Add sp |> ignore

          if mp.IsSkinned then
            addDraw(
              null,
              mp.VertexBuffer,
              mp.IndexBuffer,
              0,
              0,
              mp.PrimitiveCount
            )

        for mesh in model.Meshes do
          for part in mesh.MeshParts do
            match part.Effect with
            | :? SkinnedEffect when not(res.MergedCovered.Contains part) ->
              addDraw(
                part,
                part.VertexBuffer,
                part.IndexBuffer,
                part.VertexOffset,
                part.StartIndex,
                part.PrimitiveCount
              )
            | _ -> ()
      | ValueNone ->
        for mesh in model.Meshes do
          for part in mesh.MeshParts do
            match part.Effect with
            | :? SkinnedEffect ->
              addDraw(
                part,
                part.VertexBuffer,
                part.IndexBuffer,
                part.VertexOffset,
                part.StartIndex,
                part.PrimitiveCount
              )
            | _ -> ()
    | _ -> ()

  /// <summary>
  /// Scans the buffer range <c>[startIdx, endIdx)</c> and collects every opaque draw into the
  /// pooled arrays on <paramref name="res"/>, recording a <c>CastsShadow</c> flag per entry
  /// (snapshot of the <c>EnableShadows</c>/<c>DisableShadows</c> state when the draw was emitted,
  /// starting from <paramref name="initialCastEnabled"/>). Shared by the shadow render (filters
  /// to <c>CastsShadow = true</c>) and the scene-depth render (all entries), so shadow + depth
  /// never re-scan the buffer. Stashes the counts on <paramref name="res"/>. Thin slice
  /// wrapper over <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ShadowPass.beginCollect"/> +
  /// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ShadowPass.collectCommand"/> — single-camera
  /// frames collect inline in the forward pipeline's pre-scan instead.
  /// </summary>
  let collectGeometry
    (gd: GraphicsDevice)
    (buffer: RenderBuffer3D)
    (startIdx: int)
    (endIdx: int)
    (initialCastEnabled: bool)
    (res: ShadowResources)
    =
    beginCollect res initialCastEnabled

    for i = startIdx to endIdx - 1 do
      collectCommand gd res buffer[i]

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

          let bindings = res.InstanceBindings
          bindings[0] <- VertexBufferBinding(draw.Mesh.Vertices, 0, 0)
          bindings[1] <- VertexBufferBinding(instVB, 0, 1)
          gd.SetVertexBuffers(bindings)

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

  /// <summary>
  /// Renders skinned + instanced casters through the depth effect's
  /// <c>DepthSkinnedInstanced</c> technique: per chunk (≤ <c>PaletteTexture.MaxHeight</c>
  /// instances) the chunk's bone matrices arrive via the shared
  /// <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.PaletteChunkCache"/> palette textures
  /// (staged + uploaded once per frame across both passes) and the instance stream
  /// carries each instance's palette row. On DX12 (no working vertex texture fetch)
  /// the <c>DepthSkinnedInstancedGrouped</c> technique reads the
  /// <c>bonePaletteGroup</c> constant array instead, chunked to
  /// <c>PaletteGroup.MaxMatricesDepth / boneCount</c> instances per group (the depth
  /// effect's $Globals budget is larger than the forward effect's). On the OpenGL
  /// backend (no vertex texture fetch) — and on DX12 when a skeleton exceeds the
  /// forward budget <c>PaletteGroup.MaxMatrices</c> — falls back to per-instance
  /// <c>DepthSkinned</c> draws with the bone palette uploaded as a uniform array
  /// (the threshold follows the forward pass so both passes take the same path).
  /// matModel is Identity in
  /// the instanced path — the per-instance world matrix on stream 1 IS the model
  /// transform (same convention as DepthInstanced).
  /// </summary>
  let renderSkinnedInstancedSpan
    (gd: GraphicsDevice)
    (effect: Effect)
    (shadowParams: ShadowEffectParams)
    (viewProj: Matrix)
    (res: ShadowResources)
    (draws: ShadowSkinnedInstancedDraw[])
    (count: int)
    (shouldRender: int -> bool)
    =
    if count > 0 then
      // A skeleton beyond the grouped-uniform budget can't ride the DX12
      // bonePaletteGroup constant array — render the whole span through the
      // same per-instance fallback as OpenGL, keeping depth consistent with
      // the forward pass.
      let dx12BeyondGroupBudget =
        if PlatformInfo.GraphicsBackend = GraphicsBackend.DirectX12 then
          let mutable beyond = false
          let mutable d = 0

          while not beyond && d < count do
            beyond <- draws[d].BoneCount > PaletteGroup.MaxMatrices
            d <- d + 1

          beyond
        else
          false

      if
        PlatformInfo.GraphicsBackend = GraphicsBackend.OpenGL
        || dx12BeyondGroupBudget
      then
        // ── GL fallback: per-instance DepthSkinned draws (uniform bone palette). ──
        effect.CurrentTechnique <- effect.Techniques["DepthSkinned"]
        let boneScratch = res.BonePaletteScratch

        for d = 0 to count - 1 do
          if shouldRender d then
            let draw = draws[d]
            let instanceCount = min draw.InstanceCount draw.Transforms.Length

            let boneCount = draw.BoneCount

            if boneCount > 0 then
              for i = 0 to instanceCount - 1 do
                PbrUniforms.setMatrix shadowParams.MatModel draw.Transforms[i]
                PbrUniforms.setMatrix shadowParams.ViewProj viewProj

                let palCount = min boneCount boneScratch.Length

                for b = 0 to palCount - 1 do
                  boneScratch[b] <- draw.Palettes[i * boneCount + b]

                for b = palCount to boneScratch.Length - 1 do
                  boneScratch[b] <- Matrix.Identity

                PbrUniforms.setMatrixArray shadowParams.Bones boneScratch

                let saved = draw.Part.Effect
                draw.Part.Effect <- effect

                try
                  drawPart(gd, draw.Part)
                finally
                  draw.Part.Effect <- saved

        effect.CurrentTechnique <- effect.Techniques["Depth"]
      else
        // DX12 uses the grouped-uniform depth technique via the isolated
        // DepthShadowGrouped effect (the main effect's bonePaletteGroup params
        // are null on DX12 — dropped by mgfx reflection). DX11/Vulkan sample
        // the palette texture through the main DepthShadow effect.
        let isDX12 = PlatformInfo.GraphicsBackend = GraphicsBackend.DirectX12

        // Select the effect + params: grouped on DX12, main on DX11/Vulkan.
        let struct (shadowEffect, shadowEffectParams) =
          if isDX12 then
            match struct (res.GroupedEffect, res.GroupedParams) with
            | struct (ValueSome ge, ValueSome gp) -> struct (ge, gp)
            | _ -> struct (effect, shadowParams) // fallback (grouped effect missing)
          else
            struct (effect, shadowParams)

        match
          shadowEffect.Techniques[if isDX12 then
                                    "DepthSkinnedInstancedGrouped"
                                  else
                                    "DepthSkinnedInstanced"]
        with
        | null -> () // technique absent (unexpected off GL) — skip rather than crash
        | tech ->
          shadowEffect.CurrentTechnique <- tech
          PbrUniforms.setMatrix shadowEffectParams.MatModel Matrix.Identity
          PbrUniforms.setMatrix shadowEffectParams.ViewProj viewProj

          for d = 0 to count - 1 do
            if shouldRender d then
              let draw = draws[d]
              let instanceCount = min draw.InstanceCount draw.Transforms.Length

              let boneCount = draw.BoneCount

              if boneCount > 0 then
                // Chunk driver (mirrors the forward pass): palette-texture chunks
                // from the shared per-frame cache on DX11/Vulkan; uniform groups
                // with a null paletteTex on DX12.
                let chunks, chunkTotal =
                  if isDX12 then
                    // boneCount <= MaxMatrices here — larger skeletons took the
                    // per-instance fallback at the top of the span.
                    let needed =
                      PaletteGroup.groupCountFor
                        PaletteGroup.MaxMatricesDepth
                        instanceCount
                        boneCount

                    if res.GroupChunkScratch.Length < needed then
                      res.GroupChunkScratch <- Array.zeroCreate needed

                    (res.GroupChunkScratch,
                     PaletteGroup.planGroups
                       PaletteGroup.MaxMatricesDepth
                       instanceCount
                       boneCount
                       res.GroupChunkScratch)
                  else
                    let obtained =
                      res.PaletteChunks.Obtain(
                        gd,
                        draw.Palettes,
                        boneCount,
                        instanceCount
                      )

                    (obtained, obtained.Length)

                // Command-invariant on DX12: the group's bone stride never
                // changes across this draw's chunks.
                if isDX12 then
                  PbrUniforms.setInt shadowEffectParams.GroupBoneCount boneCount

                let mutable chunkIdx = 0

                while chunkIdx < chunkTotal do
                  let struct (chunkStart, chunkCount, paletteTex) =
                    chunks[chunkIdx]

                  // DX11/Vulkan: shared per-frame staging (InstanceWorldCache) —
                  // the chunk plan is shared with the forward pass, so one staging
                  // pass per frame serves both passes' VB uploads. DX12 stages per
                  // pass (its forward/depth group budgets differ).
                  let staged =
                    if isDX12 then
                      if res.SkinnedInstancedStaging.Length < chunkCount then
                        res.SkinnedInstancedStaging <-
                          Array.zeroCreate<VertexInstanceWorldPalette>
                            chunkCount

                      for i = 0 to chunkCount - 1 do
                        res.SkinnedInstancedStaging[i] <-
                          VertexInstanceWorldPalette.Create(
                            draw.Transforms[chunkStart + i],
                            float32 i // palette row is chunk-local (texture holds this chunk only)
                          )

                      res.SkinnedInstancedStaging
                    else
                      res.InstanceWorlds.Obtain(
                        draw.Transforms,
                        instanceCount,
                        chunks,
                        chunkTotal
                      )

                  // Must stay a DynamicVertexBuffer — same DX12 upload-ordering hazard
                  // as the other instanced paths (see renderInstancedSpan).
                  match res.SkinnedInstancedVertexBuffer with
                  | ValueNone ->
                    let vb =
                      new DynamicVertexBuffer(
                        gd,
                        typeof<VertexInstanceWorldPalette>,
                        chunkCount,
                        BufferUsage.WriteOnly
                      )

                    res.SkinnedInstancedVertexBuffer <- ValueSome vb
                  | ValueSome vb when vb.VertexCount < chunkCount ->
                    vb.Dispose()

                    let vb' =
                      new DynamicVertexBuffer(
                        gd,
                        typeof<VertexInstanceWorldPalette>,
                        chunkCount,
                        BufferUsage.WriteOnly
                      )

                    res.SkinnedInstancedVertexBuffer <- ValueSome vb'
                  | _ -> ()

                  let instVB =
                    match res.SkinnedInstancedVertexBuffer with
                    | ValueSome vb -> vb
                    | ValueNone -> Unchecked.defaultof<VertexBuffer> // unreachable

                  // Cached rows are command-global: this chunk's rows start at
                  // chunkStart (DX12's per-pass staging starts at 0).
                  instVB.SetData(
                    staged,
                    (if isDX12 then 0 else chunkStart),
                    chunkCount
                  )

                  // Per-chunk palette storage: the cached texture on DX11/Vulkan,
                  // the bonePaletteGroup constant array on DX12 (null paletteTex).
                  if isNull paletteTex then
                    if
                      res.GroupPaletteScratch.Length < PaletteGroup.MaxMatricesDepth
                    then
                      res.GroupPaletteScratch <-
                        Array.zeroCreate PaletteGroup.MaxMatricesDepth

                    Array.Copy(
                      draw.Palettes,
                      chunkStart * boneCount,
                      res.GroupPaletteScratch,
                      0,
                      chunkCount * boneCount
                    )

                    PbrUniforms.setMatrixArray
                      shadowEffectParams.BonePaletteGroup
                      res.GroupPaletteScratch
                  else
                    PbrUniforms.setTexture
                      shadowEffectParams.PaletteTex
                      paletteTex

                    PbrUniforms.setVec2
                      shadowEffectParams.PaletteTexSize
                      (Vector2(float32(boneCount * 4), float32 chunkCount))

                  let bindings = res.InstanceBindings
                  bindings[0] <- VertexBufferBinding(draw.VertexBuffer, 0, 0)
                  bindings[1] <- VertexBufferBinding(instVB, 0, 1)
                  gd.SetVertexBuffers(bindings)

                  gd.Indices <- draw.IndexBuffer

                  for pass in shadowEffect.CurrentTechnique.Passes do
                    pass.Apply()

                    gd.DrawInstancedPrimitives(
                      PrimitiveType.TriangleList,
                      draw.VertexOffset,
                      draw.StartIndex,
                      draw.PrimitiveCount,
                      chunkCount
                    )

                  chunkIdx <- chunkIdx + 1

          shadowEffect.CurrentTechnique <- shadowEffect.Techniques["Depth"]

  // ─────────────────────────────────────────────────────────────────────────────
  // High-level passes
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Saves the caller's render-target bindings into the pooled scratch (resized only when the
  /// bound count changes) — avoids <c>GetRenderTargets()</c>'s per-call array allocation, which
  /// would otherwise happen once per shadow/depth pass (N per multi-camera-block frame).
  /// Restore with <c>SetRenderTargets</c> on the returned array.
  /// </summary>
  let saveRenderTargets
    (gd: GraphicsDevice)
    (res: ShadowResources)
    : RenderTargetBinding[] =
    let count = gd.RenderTargetCount

    if res.RenderTargetScratch.Length <> count then
      res.RenderTargetScratch <- Array.zeroCreate count

    gd.GetRenderTargets(res.RenderTargetScratch)
    res.RenderTargetScratch

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
    let skinnedInstancedDraws = res.SkinnedInstancedDraws
    let drawCount = res.CollectedDrawCount
    let skinnedCount = res.CollectedSkinnedCount
    let modelPartCount = res.CollectedModelPartCount
    let instancedCount = res.CollectedInstancedCount
    let skinnedInstancedCount = res.CollectedSkinnedInstancedCount

    if
      drawCount = 0
      && skinnedCount = 0
      && instancedCount = 0
      && modelPartCount = 0
      && skinnedInstancedCount = 0
    then
      ()
    else
      let prevViewport = gd.Viewport
      let prevRaster = gd.RasterizerState
      let prevBlend = gd.BlendState
      let prevDepth = gd.DepthStencilState
      // Restore the caller's bindings, not the back-buffer: under post-processing this pass
      // runs interleaved while the scene render target is bound.
      let prevTargets = saveRenderTargets gd res

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

      // Span predicates are caster-invariant: they capture the span arrays (and the
      // frustum, read at call time — the per-caster Matrix assign below stays visible
      // through the shared reference). Create them once per pass, not per caster.
      let primitiveShouldRender d =
        shadowDraws[d].CastsShadow
        && Culling.isVisible shadowFrustum shadowDraws[d].WorldBounds

      let modelPartShouldRender d = modelPartDraws[d].CastsShadow
      let skinnedShouldRender d = skinnedDraws[d].CastsShadow
      let instancedShouldRender d = instancedDraws[d].CastsShadow

      let skinnedInstancedShouldRender d = skinnedInstancedDraws[d].CastsShadow

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
            primitiveShouldRender

          renderModelPartSpan
            gd
            depthEffect
            depthParams
            casterVP
            modelPartDraws
            modelPartCount
            modelPartShouldRender

          renderSkinnedSpan
            gd
            depthEffect
            depthParams
            casterVP
            res.BonePaletteScratch
            skinnedDraws
            skinnedCount
            skinnedShouldRender

          renderInstancedSpan
            gd
            depthEffect
            depthParams
            casterVP
            res
            instancedDraws
            instancedCount
            instancedShouldRender

          renderSkinnedInstancedSpan
            gd
            depthEffect
            depthParams
            casterVP
            res
            skinnedInstancedDraws
            skinnedInstancedCount
            skinnedInstancedShouldRender

      gd.SetRenderTargets prevTargets
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
    // Restore the caller's bindings, not the back-buffer (see renderAtlasCasters).
    let prevTargets = saveRenderTargets gd res

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

    renderSkinnedInstancedSpan
      gd
      depthEffect
      depthParams
      viewProj
      res
      res.SkinnedInstancedDraws
      res.CollectedSkinnedInstancedCount
      (fun _ -> true)

    gd.SetRenderTargets prevTargets
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

    // Directional caster first (slot 0 by convention). Only the first directional light is
    // shaded, so only it can cast — a non-casting DirLights[0] means no directional caster,
    // even if a later directional light has CastsShadows set.
    if lights.DirLights.Count > 0 && lights.DirLights[0].CastsShadows then
      let dirLight = lights.DirLights[0]
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

  /// <summary>Loads the DepthShadow effect on first use (no-op once loaded).</summary>
  let ensureDepthEffect (gd: GraphicsDevice) (res: ShadowResources) =
    match struct (res.Effect, res.Params) with
    | struct (ValueSome _, ValueSome _) -> ()
    | _ ->
      match ShaderLoader.loadEffect gd "DepthShadow" with
      | ValueSome e ->
        res.Params <- ValueSome(buildShadowParams e)
        res.Effect <- ValueSome e
      | ValueNone -> ()

  /// <summary>
  /// Runs the shadow pass: scans lights, collects opaque geometry from the buffer range
  /// <c>[startIdx, endIdx)</c> (shared with scene-depth), registers casters into the atlas,
  /// renders depth, and uploads shadow uniforms to the PBR effect. When <paramref name="needsDepth"/>
  /// is true, geometry is collected even without a shadow-casting light so <c>renderSceneDepth</c>
  /// can reuse it. Only the first directional light can cast (it is the one the shader lights with).
  /// </summary>
  let run
    (gd: GraphicsDevice)
    (atlasCfg: ShadowAtlasConfig)
    (biasCfg: ShadowBiasConfig)
    (res: ShadowResources)
    (lights: LightBuffers)
    (pbrParams: PbrEffectParams voption)
    (buffer: RenderBuffer3D)
    (startIdx: int)
    (endIdx: int)
    (initialCastEnabled: bool)
    (activeCamera: Camera3D)
    (needsDepth: bool)
    (precollected: bool)
    : ShadowResult voption =
    // ── Scan lights for casters ──
    let hasDirCaster =
      lights.DirLights.Count > 0 && lights.DirLights[0].CastsShadows

    let mutable hasPointCaster = false
    let mutable hasSpotCaster = false

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
    // Skipped when the caller already collected this frame: single-camera frames collect
    // inline in the forward pipeline's pre-scan (one less full buffer walk per frame).
    if (hasAnyCaster || needsDepth) && not precollected then
      collectGeometry gd buffer startIdx endIdx initialCastEnabled res

    // ── Shadow pass (only when a shadow-casting light exists) ──
    if not hasAnyCaster then
      match pbrParams with
      | ValueSome p -> PbrUniforms.setInt p.Shadow.DirLightCastsShadows 0
      | ValueNone -> ()

      // Even without shadow casters, load the DepthShadow effect when needsDepth is true so
      // renderSceneDepth can run (fog/DoF/SSOA in a shadowless scene). Without this the effect
      // is never loaded and every postProcessWithDepth action silently receives Depth = None.
      if needsDepth then
        ensureDepthEffect gd res

        // DX12: load the isolated grouped depth effect alongside the main one.
        if PlatformInfo.GraphicsBackend = GraphicsBackend.DirectX12 then
          match res.GroupedEffect with
          | ValueSome _ -> ()
          | ValueNone ->
            match ShaderLoader.loadEffect gd "DepthShadowGrouped" with
            | ValueSome e ->
              res.GroupedParams <- ValueSome(buildShadowParams e)
              res.GroupedEffect <- ValueSome e
            | ValueNone -> ()

      ValueNone
    else
      res.Atlas.EnsureResources gd

      ensureDepthEffect gd res

      // DX12: load the isolated grouped depth effect alongside the main one.
      if PlatformInfo.GraphicsBackend = GraphicsBackend.DirectX12 then
        match res.GroupedEffect with
        | ValueSome _ -> ()
        | ValueNone ->
          match ShaderLoader.loadEffect gd "DepthShadowGrouped" with
          | ValueSome e ->
            res.GroupedParams <- ValueSome(buildShadowParams e)
            res.GroupedEffect <- ValueSome e
          | ValueNone -> ()

      match struct (res.Effect, res.Params) with
      | struct (ValueSome depthEffect, ValueSome depthParams) ->
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
