#nowarn "9"

namespace Mibo.Elmish.Graphics3D.Pipelines

open System
open System.Collections.Generic
open System.Numerics
open FSharp.NativeInterop
open FSharp.UMX
open Raylib_cs

// ------------------------------------------------------------------
// Shadow Atlas Types
// ------------------------------------------------------------------

/// <summary>Unique identifier for a shadow caster in the atlas.</summary>
[<Measure>]
type ShadowCasterId

/// <summary>Type of shadow caster determines projection and face count.</summary>
type ShadowCasterType =
  | Directional = 0
  | Point = 1
  | Spot = 2

/// <summary>Data for a single shadow caster in the atlas.</summary>
[<Struct>]
type ShadowCasterData = {
  /// <summary>Unique identifier for this caster.</summary>
  Id: int<ShadowCasterId>
  /// <summary>Type of light (directional, point, spot).</summary>
  Type: ShadowCasterType
  /// <summary>World-space position of the light (for point/spot).</summary>
  LightPosition: Vector3
  /// <summary>Direction the light shines (normalized).</summary>
  LightDirection: Vector3
  /// <summary>Target point for spot lights.</summary>
  LightTarget: Vector3
  /// <summary>Index of first atlas region (0-based).</summary>
  AtlasRegion: int
  /// <summary>Number of atlas regions used. Always 1 in the current implementation —
  /// point lights render a single downward-facing shadow map, not a 6-face cubemap.</summary>
  RegionCount: int
  /// <summary>Whether this caster is currently casting shadows.</summary>
  Enabled: bool
  /// <summary>Per-caster shadow bias override (None = use global).</summary>
  BiasOverride: float32 voption
  /// <summary>View-projection matrix for this caster (filled during shadow pass).</summary>
  mutable ViewProj: Matrix4x4
}

// ------------------------------------------------------------------
// Shadow Atlas Configuration
// ------------------------------------------------------------------

/// <summary>Strategy for determining the origin point of shadow maps.</summary>
/// <remarks>
/// The shadow origin determines where shadow maps are centered. This affects
/// which parts of the scene receive shadows and how shadows move with the camera.
/// </remarks>
[<Struct>]
type ShadowOriginStrategy =
  /// <summary>Use the camera's target point as shadow origin. Good for third-person games.</summary>
  | CameraTarget
  /// <summary>Use world origin (0,0,0) as shadow origin. Good for fixed scenes.</summary>
  | SceneCenter
  /// <summary>Use a custom function to compute shadow origin from camera state.</summary>
  | Custom of (Camera3D -> Vector3)

/// <summary>Configuration for the shadow atlas system.</summary>
/// <remarks>
/// <para>
/// This configuration controls both the atlas texture layout and shadow rendering behavior.
/// Some fields (marked as "ForwardPbr-specific") are only used by the ForwardPbrPipeline
/// implementation. Other pipelines may ignore these fields or use different strategies.
/// </para>
/// </remarks>
[<Struct>]
type ShadowAtlasConfig = {
  /// <summary>Resolution of the atlas texture (square). Default 2048.</summary>
  Resolution: int
  /// <summary>Maximum number of shadow casters. Must be perfect square (4, 9, 16, 25, 36).</summary>
  MaxCasters: int

  /// <summary>
  /// Strategy for determining shadow map origin. Default: CameraTarget.
  /// </summary>
  /// <remarks>
  /// <b>ForwardPbr-specific:</b> Controls where directional light shadows are centered.
  /// CameraTarget works well for third-person games where the camera follows a player.
  /// SceneCenter works for fixed scenes. Use Custom for first-person or special cases.
  /// </remarks>
  OriginStrategy: ShadowOriginStrategy

  /// <summary>
  /// Distance to place directional light camera behind the shadow origin. Default: auto-derived.
  /// </summary>
  /// <remarks>
  /// <b>ForwardPbr-specific:</b> Larger values capture more of the scene but reduce shadow precision.
  /// When None, derived from camera far plane (far * 0.5). Typical range: 50-200 units.
  /// </remarks>
  DirectionalLightDistance: float32 voption

  /// <summary>
  /// Half-size of directional light orthographic projection. Default: auto-derived.
  /// </summary>
  /// <remarks>
  /// <b>ForwardPbr-specific:</b> Controls the coverage area of directional shadows.
  /// Larger values cast shadows over a wider area but reduce resolution.
  /// When None, derived from camera frustum at mid-distance. Typical range: 20-100 units.
  /// </remarks>
  DirectionalLightSize: float32 voption

  /// <summary>
  /// Grid snap size for shadow origin to reduce flickering. Default: 2.0.
  /// </summary>
  /// <remarks>
  /// <b>ForwardPbr-specific:</b> Snaps the shadow origin to a grid to prevent shadow shimmer
  /// as the camera moves. Larger values = more stable but less precise shadows.
  /// Set to 0 to disable snapping. Typical range: 1.0-5.0 units.
  /// </remarks>
  GridSnapSize: float32

  /// <summary>
  /// Maximum distance from the camera at which point/spot lights cast shadows. Default: 50.
  /// </summary>
  /// <remarks>
  /// <b>ForwardPbr-specific:</b> Lights beyond this distance are excluded from the shadow pass
  /// for performance. Increase for open-world or RTS games with large scenes; decrease for
  /// tighter, more shadow-dense scenes. Measured in world units (distance, not squared).
  /// </remarks>
  MaxShadowLightDistance: float32

  /// <summary>
  /// Fraction of the atlas the single directional caster occupies. Default: 0.5.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The directional light is registered first (slot 0) and there is exactly one of it.
  /// With a non-zero ratio it gets a dedicated top region of the atlas sized
  /// <c>(Resolution × Resolution × ratio)</c> — e.g. 0.5 of an 8192² atlas is 8192×4096
  /// (~4K), instead of the <c>1/MaxCasters</c> tile the uniform grid would give it. This
  /// makes directional shadows high-resolution by default without the user tuning
  /// <c>MaxCasters</c> to their scene's light count.
  /// </para>
  /// <para>
  /// Point/spot casters subdivide the remaining bottom strip <c>(Resolution × (1-ratio))</c>
  /// into a square grid based on their active count. <c>1.0</c> gives the directional light
  /// the whole atlas (directional-only scenes). <c>0.0</c> restores the legacy uniform grid
  /// (all casters share <c>1/MaxCasters</c> tiles — backward compatible).
  /// </para>
  /// </remarks>
  DirectionalAtlasRatio: float32
}

/// <summary>Global shadow bias configuration.</summary>
[<Struct>]
type ShadowBiasConfig = {
  /// <summary>Bias for directional light shadows. Default 0.0005.</summary>
  DirectionalBias: float32
  /// <summary>Bias for point light shadows. Default 0.01.</summary>
  PointBias: float32
  /// <summary>Bias for spot light shadows. Default 0.001.</summary>
  SpotBias: float32
  /// <summary>Slope-scale bias multiplier. Default 0.0005.</summary>
  SlopeScaleBias: float32
}

module ShadowAtlasConfig =
  let defaults: ShadowAtlasConfig = {
    Resolution = 2048
    MaxCasters = 16
    OriginStrategy = CameraTarget
    DirectionalLightDistance = ValueNone
    DirectionalLightSize = ValueNone
    GridSnapSize = 2.0f
    MaxShadowLightDistance = 50.0f
    DirectionalAtlasRatio = 0.5f
  }

module ShadowBiasConfig =
  let defaults: ShadowBiasConfig = {
    DirectionalBias = 0.0005f
    PointBias = 0.01f
    SpotBias = 0.001f
    SlopeScaleBias = 0.0005f
  }

// ------------------------------------------------------------------
// Shadow Atlas Implementation
// ------------------------------------------------------------------

/// <summary>
/// Manages a texture atlas for multiple shadow maps.
/// Supports directional, point (cubemap), and spot light shadows.
/// </summary>
[<Sealed>]
type ShadowAtlas(config: ShadowAtlasConfig, biasConfig: ShadowBiasConfig) =

  do
    if
      config.DirectionalAtlasRatio < 0.0f || config.DirectionalAtlasRatio > 1.0f
    then
      failwithf
        "DirectionalAtlasRatio must be between 0.0 and 1.0. Got %f."
        (float config.DirectionalAtlasRatio)

  let gridSize =
    let sqrt = Math.Sqrt(float config.MaxCasters) |> int

    if sqrt * sqrt <> config.MaxCasters then
      failwithf
        "MaxCasters must be perfect square. Got %d, nearest is %d."
        config.MaxCasters
        (sqrt * sqrt)

    sqrt

  let regionsPerRow = gridSize
  let regionSize = config.Resolution / gridSize

  let mutable fbo: RenderTexture2D = Unchecked.defaultof<RenderTexture2D>
  let casters = Dictionary<int<ShadowCasterId>, ShadowCasterData>()
  let viewProjs = Dictionary<int, Matrix4x4>()
  let mutable nextId = 0
  let mutable slotAllocator = 0

  // Pre-allocate uniform arrays
  let viewProjsUniforms = Array.zeroCreate<Matrix4x4> config.MaxCasters
  let uvOffsets = Array.zeroCreate<Vector4> config.MaxCasters
  let lightPositions = Array.zeroCreate<Vector3> config.MaxCasters
  let biases = Array.zeroCreate<float32> config.MaxCasters
  let casterTypes = Array.zeroCreate<int> config.MaxCasters
  let mutable activeCasterCount = 0

  // Number of enabled non-directional casters (point/spot). Drives the bottom-strip
  // subdivision in the dedicated-directional layout. Kept current by every caster mutation
  // (Add/Remove/Update/Clear) via UpdateRegionCount, so the viewports rendered during the
  // shadow pass and the UVs uploaded afterwards always derive from the same count.
  // Defaults to 0 so a directional-only frame gives the directional caster its full ratio
  // region.
  let mutable activePointSpotCount = 0

  // ── Region layout ──
  // When DirectionalAtlasRatio > 0, the directional caster (always slot 0) gets a dedicated
  // top rectangle; point/spot casters subdivide the remaining bottom strip. When the ratio
  // is 0, the legacy uniform grid (1/MaxCasters tiles) is used unchanged.
  let useDedicatedDirectional = config.DirectionalAtlasRatio > 0.0f

  let dirRegionHeight =
    int(float config.Resolution * float config.DirectionalAtlasRatio)

  // Compute the pixel rect (x, y, w, h) for a flat region index. Pixels are integers (the
  // viewport), so non-power-of-two subdivisions round — that's fine for rendering but would
  // drift the UV scale, so UVs are computed separately by regionUV (grid-fraction math for
  // the legacy path, exact rect-derived math for the dedicated region).
  let regionRect(regionIndex: int) : struct (int * int * int * int) =
    if not useDedicatedDirectional then
      let row = regionIndex / regionsPerRow
      let col = regionIndex % regionsPerRow
      struct (col * regionSize, row * regionSize, regionSize, regionSize)
    elif regionIndex = 0 then
      struct (0, 0, config.Resolution, dirRegionHeight)
    else
      let stripHeight = config.Resolution - dirRegionHeight
      let cols = max 1 (int(ceil(sqrt(float activePointSpotCount))))
      let tileW = config.Resolution / cols
      let tileH = stripHeight / cols
      let pi = regionIndex - 1
      let row = pi / cols
      let col = pi % cols
      struct (col * tileW, dirRegionHeight + row * tileH, tileW, tileH)

  // UV offset/scale (xy=offset, zw=scale) for a region. The legacy uniform-grid path uses
  // exact 1/gridSize fractions (preserving the original UV layout — integer pixel rounding
  // in the viewport would otherwise drift the scale, e.g. 2048/3 vs 1/3). The dedicated
  // directional path derives UVs from the pixel rect (the ratio slice is exact).
  let regionUV(regionIndex: int) : Vector4 =
    if not useDedicatedDirectional then
      let row = regionIndex / regionsPerRow
      let col = regionIndex % regionsPerRow
      let g = float32 gridSize
      Vector4(float32 col / g, float32 row / g, 1.0f / g, 1.0f / g)
    else
      let res = float32 config.Resolution
      let struct (x, y, w, h) = regionRect regionIndex

      Vector4(
        float32 x / res,
        float32 y / res,
        float32 w / res,
        float32 h / res
      )

  /// <summary>Grid size (rows/columns) of the atlas.</summary>
  member _.GridSize = gridSize

  /// <summary>Size of each region in pixels.</summary>
  member _.RegionSize = regionSize

  /// <summary>The depth-only FBO for the atlas.</summary>
  member _.Fbo = fbo

  /// <summary>Number of currently allocated casters.</summary>
  member _.Count = casters.Count

  /// <summary>Get all active casters (for iteration).</summary>
  member _.Casters = casters.Values

  /// <summary>Get bias for a caster type, respecting per-caster override.</summary>
  member _.GetBias(caster: ShadowCasterData) =
    match caster.BiasOverride with
    | ValueSome b -> b
    | ValueNone ->
      match caster.Type with
      | ShadowCasterType.Directional -> biasConfig.DirectionalBias
      | ShadowCasterType.Point -> biasConfig.PointBias
      | ShadowCasterType.Spot -> biasConfig.SpotBias
      | _ -> biasConfig.DirectionalBias

  /// <summary>
  /// Creates the atlas FBO and depth texture.
  /// Must be called during pipeline initialization.
  /// </summary>
  member _.Initialize() =
    if fbo.Id <> 0u then
      failwith "ShadowAtlas already initialized"

    let fboId = Rlgl.LoadFramebuffer()
    Rlgl.EnableFramebuffer(fboId)

    let depthId =
      Rlgl.LoadTextureDepth(config.Resolution, config.Resolution, false)

    Rlgl.FramebufferAttach(
      fboId,
      depthId,
      FramebufferAttachType.Depth,
      FramebufferAttachTextureType.Texture2D,
      0
    )

    Rlgl.DisableFramebuffer()

    fbo <-
      RenderTexture2D(
        Id = fboId,
        Texture =
          Texture2D(
            Id = 0u,
            Width = config.Resolution,
            Height = config.Resolution,
            Mipmaps = 1,
            Format = PixelFormat.UncompressedR8G8B8A8
          ),
        Depth =
          Texture2D(
            Id = depthId,
            Width = config.Resolution,
            Height = config.Resolution,
            Mipmaps = 1,
            Format = enum<PixelFormat> 19
          )
      )

  /// <summary>
  /// Destroys the atlas FBO and releases resources.
  /// Must be called during pipeline shutdown.
  /// </summary>
  member _.Shutdown() =
    if fbo.Id <> 0u then
      Rlgl.UnloadTexture(fbo.Depth.Id)
      Rlgl.UnloadFramebuffer(fbo.Id)
      fbo <- Unchecked.defaultof<RenderTexture2D>

    casters.Clear()
    viewProjs.Clear()
    slotAllocator <- 0

  /// <summary>Clear all casters and reset slot allocator. Call at start of each frame.</summary>
  member _.Clear() =
    casters.Clear()
    viewProjs.Clear()
    slotAllocator <- 0
    activePointSpotCount <- 0

  /// <summary>Allocate a slot in the atlas. Returns region index, or ValueNone if full.</summary>
  member private _.AllocateSlot(regionCount: int) =
    if slotAllocator + regionCount > config.MaxCasters then
      ValueNone
    else
      let slot = slotAllocator
      slotAllocator <- slotAllocator + regionCount
      ValueSome slot

  /// <summary>Free a slot in the atlas.</summary>
  member private _.FreeSlot(regionIndex: int, regionCount: int) =
    // Simple linear allocator - just decrement count
    // In practice, we'd need a more sophisticated allocator for defragmentation
    slotAllocator <- slotAllocator - regionCount

    if slotAllocator < 0 then
      slotAllocator <- 0

  /// <summary>
  /// Register a new shadow caster and allocate atlas regions.
  /// Returns the caster ID, or ValueNone if the atlas is full.
  /// </summary>
  member this.AddCaster
    (
      casterType: ShadowCasterType,
      lightPosition: Vector3,
      lightDirection: Vector3,
      lightTarget: Vector3,
      enabled: bool,
      biasOverride: float32 voption
    ) : int<ShadowCasterId> voption =
    let regionCount = 1

    match this.AllocateSlot(regionCount) with
    | ValueNone -> ValueNone
    | ValueSome region ->
      let id = UMX.tag<ShadowCasterId> nextId
      nextId <- nextId + 1

      let caster = {
        Id = id
        Type = casterType
        LightPosition = lightPosition
        LightDirection = lightDirection
        LightTarget = lightTarget
        AtlasRegion = region
        RegionCount = regionCount
        Enabled = enabled
        BiasOverride = biasOverride
        ViewProj = Matrix4x4.Identity
      }

      casters[id] <- caster
      this.UpdateRegionCount()
      ValueSome id

  /// <summary>Remove a shadow caster and free its atlas regions.</summary>
  member this.RemoveCaster(id: int<ShadowCasterId>) =
    match casters.TryGetValue(id) with
    | true, caster ->
      this.FreeSlot(caster.AtlasRegion, caster.RegionCount)
      casters.Remove(id) |> ignore
      this.UpdateRegionCount()
    | false, _ -> ()

  /// <summary>Update a shadow caster's properties.</summary>
  member this.UpdateCaster
    (
      id: int<ShadowCasterId>,
      ?lightPosition: Vector3,
      ?lightDirection: Vector3,
      ?lightTarget: Vector3,
      ?enabled: bool,
      ?biasOverride: float32 voption
    ) =
    match casters.TryGetValue(id) with
    | true, caster ->
      casters[id] <- {
        caster with
            LightPosition = defaultArg lightPosition caster.LightPosition
            LightDirection = defaultArg lightDirection caster.LightDirection
            LightTarget = defaultArg lightTarget caster.LightTarget
            Enabled = defaultArg enabled caster.Enabled
            BiasOverride = defaultArg biasOverride caster.BiasOverride
      }

      this.UpdateRegionCount()
    | false, _ -> ()

  /// <summary>Get UV offset/scale for a region index.</summary>
  member _.GetUVOffsetScale(regionIndex: int) = regionUV regionIndex

  /// <summary>Set the view-projection matrix for a specific atlas region.</summary>
  member _.SetRegionViewProj(regionIndex: int, vp: Matrix4x4) =
    viewProjs[regionIndex] <- vp

  /// <summary>Set the view-projection matrix for a single-region caster.</summary>
  member this.SetCasterViewProj(id: int<ShadowCasterId>, vp: Matrix4x4) =
    match casters.TryGetValue(id) with
    | true, caster -> this.SetRegionViewProj(caster.AtlasRegion, vp)
    | false, _ -> ()

  /// <summary>Get viewport rectangle for a region index.</summary>
  member _.GetRegionViewport(regionIndex: int) =
    let struct (x, y, w, h) = regionRect regionIndex
    Rlgl.Viewport(x, y, w, h)

  /// <summary>Get scissor rectangle for a region index.</summary>
  member _.GetRegionScissor(regionIndex: int) =
    let struct (x, y, w, h) = regionRect regionIndex
    Rlgl.Scissor(x, y, w, h)

  /// <summary>Clear a specific region in the atlas.</summary>
  member this.ClearRegion(regionIndex: int) =
    this.GetRegionViewport(regionIndex)
    Raylib.ClearBackground(Color.White)
    Rlgl.Viewport(0, 0, config.Resolution, config.Resolution)

  /// <summary>
  /// Recompute the enabled non-directional caster count driving the bottom-strip
  /// subdivision. Called by every caster mutation so the region layout is always current:
  /// the shadow pass renders region viewports before PrepareUniforms runs, so the count
  /// must not wait for it. O(casters), bounded by MaxCasters.
  /// </summary>
  member private _.UpdateRegionCount() =
    let mutable psCount = 0

    for kvp in casters do
      let c = kvp.Value

      if c.Enabled && c.Type <> ShadowCasterType.Directional then
        psCount <- psCount + c.RegionCount

    activePointSpotCount <- psCount

  /// <summary>
  /// Prepare uniform arrays for upload to shader.
  /// Call each frame before rendering.
  /// </summary>
  member this.PrepareUniforms() =
    this.UpdateRegionCount()

    let mutable index = 0

    for kvp in casters do
      let caster = kvp.Value

      if caster.Enabled && index < config.MaxCasters then
        // Fill regions (for point lights, fill all 6 faces)
        for r = 0 to caster.RegionCount - 1 do
          if index < config.MaxCasters then
            // Get VP from dictionary by region index
            let regionIndex = caster.AtlasRegion + r

            match viewProjs.TryGetValue(regionIndex) with
            | true, vp -> viewProjsUniforms[index] <- vp
            | false, _ -> viewProjsUniforms[index] <- Matrix4x4.Identity

            // UV offset/scale (legacy grid uses exact 1/gridSize fractions; dedicated
            // region derives from the pixel rect).
            uvOffsets[index] <- regionUV regionIndex

            lightPositions[index] <- caster.LightPosition

            // Inline GetBias
            biases[index] <-
              match caster.BiasOverride with
              | ValueSome b -> b
              | ValueNone ->
                match caster.Type with
                | ShadowCasterType.Directional -> biasConfig.DirectionalBias
                | ShadowCasterType.Point -> biasConfig.PointBias
                | ShadowCasterType.Spot -> biasConfig.SpotBias
                | _ -> biasConfig.DirectionalBias

            casterTypes[index] <- int caster.Type
            index <- index + 1

    // Zero out remaining
    for i = index to config.MaxCasters - 1 do
      viewProjsUniforms[i] <- Matrix4x4.Identity
      uvOffsets[i] <- Vector4.Zero
      lightPositions[i] <- Vector3.Zero
      biases[i] <- 0.0f
      casterTypes[i] <- -1

    activeCasterCount <- index

  /// <summary>Get prepared uniform arrays (call after PrepareUniforms).</summary>
  member _.ViewProjs = viewProjsUniforms
  member _.UVOffsets = uvOffsets
  member _.LightPositions = lightPositions
  member _.Biases = biases
  member _.CasterTypes = casterTypes

  /// <summary>Get the number of active caster regions (computed by PrepareUniforms).</summary>
  member _.ActiveCasterCount = activeCasterCount

// ------------------------------------------------------------------
// Helper Functions for Shadow Rendering
// ------------------------------------------------------------------
