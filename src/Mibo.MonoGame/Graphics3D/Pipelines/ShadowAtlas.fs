namespace Mibo.Elmish.Graphics3D.Pipelines

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

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
  /// <summary>Number of atlas regions used (1 for directional/spot; point uses 1 in B11).</summary>
  RegionCount: int
  /// <summary>Whether this caster is currently casting shadows.</summary>
  Enabled: bool
  /// <summary>Per-caster shadow bias override (None = use global).</summary>
  BiasOverride: float32 voption
  /// <summary>View-projection matrix for this caster (filled during shadow pass).</summary>
  mutable ViewProj: Matrix
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
/// Some fields are only used by the forward pipeline implementation. Other pipelines may
/// ignore these fields or use different strategies.
/// </para>
/// </remarks>
[<Struct>]
type ShadowAtlasConfig = {
  /// <summary>Resolution of the atlas texture (square). Default 2048.</summary>
  Resolution: int
  /// <summary>Maximum number of shadow casters. Must be perfect square (4, 9, 16, 25, 36).</summary>
  MaxCasters: int
  /// <summary>Whether to show debug overlay. Default false. (Reserved for B13.)</summary>
  ShowDebugOverlay: bool

  /// <summary>
  /// Strategy for determining shadow map origin. Default: CameraTarget.
  /// </summary>
  /// <remarks>
  /// Controls where directional light shadows are centered. CameraTarget works well for
  /// third-person games where the camera follows a player. SceneCenter works for fixed
  /// scenes. Use Custom for first-person or special cases.
  /// </remarks>
  OriginStrategy: ShadowOriginStrategy

  /// <summary>
  /// Distance to place directional light camera behind the shadow origin. Default: 100.
  /// </summary>
  /// <remarks>
  /// Larger values capture more of the scene but reduce shadow precision.
  /// Typical range: 50-200 units.
  /// </remarks>
  DirectionalLightDistance: float32 voption

  /// <summary>
  /// Half-size of directional light orthographic projection. Default: 50.
  /// </summary>
  /// <remarks>
  /// Controls the coverage area of directional shadows. Larger values cast shadows over
  /// a wider area but reduce resolution. Typical range: 20-100 units.
  /// </remarks>
  DirectionalLightSize: float32 voption

  /// <summary>
  /// Grid snap size for shadow origin to reduce flickering. Default: 2.0.
  /// </summary>
  /// <remarks>
  /// Snaps the shadow origin to a grid to prevent shadow shimmer as the camera moves.
  /// Larger values = more stable but less precise shadows. Set to 0 to disable snapping.
  /// Typical range: 1.0-5.0 units.
  /// </remarks>
  GridSnapSize: float32
}

/// <summary>Global shadow bias configuration.</summary>
/// <remarks>
/// <b>MonoGame-specific:</b> <c>SlopeScaleBias</c> maps to
/// <see cref="P:Microsoft.Xna.Framework.Graphics.RasterizerState.SlopeScaleDepthBias"/>
/// (native polygon-offset on both DX11 and OpenGL — replaces raylib's GLSL
/// <c>dFdx</c>/<c>dFdy</c> shader math, which has no SM3.0 equivalent).
/// The per-type bias maps to <see cref="P:Microsoft.Xna.Framework.Graphics.RasterizerState.DepthBias"/>.
/// </remarks>
[<Struct>]
type ShadowBiasConfig = {
  /// <summary>Bias for directional light shadows. Default 0.0005.</summary>
  DirectionalBias: float32
  /// <summary>Bias for point light shadows. Default 0.01.</summary>
  PointBias: float32
  /// <summary>Bias for spot light shadows. Default 0.001.</summary>
  SpotBias: float32
  /// <summary>Slope-scale bias multiplier (native RasterizerState). Default 0.0005.</summary>
  SlopeScaleBias: float32
}

/// <summary>Convenience values for <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.ShadowAtlasConfig"/>.</summary>
module ShadowAtlasConfig =
  /// <summary>Default atlas configuration: 2048² atlas, 16 casters (4×4 grid), camera-target origin, 2.0 grid snap.</summary>
  let defaults: ShadowAtlasConfig = {
    Resolution = 2048
    MaxCasters = 16
    ShowDebugOverlay = false
    OriginStrategy = CameraTarget
    DirectionalLightDistance = ValueNone
    DirectionalLightSize = ValueNone
    GridSnapSize = 2.0f
  }

/// <summary>Convenience values for <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.ShadowBiasConfig"/>.</summary>
module ShadowBiasConfig =
  /// <summary>Default bias values for each light type.</summary>
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
/// Manages a texture atlas for multiple shadow maps. MonoGame port of the canonical
/// raylib <c>ShadowAtlas</c>.
/// </summary>
/// <remarks>
/// <para>
/// The atlas is a single square <see cref="T:Microsoft.Xna.Framework.Graphics.RenderTarget2D"/>
/// with <see cref="F:Microsoft.Xna.Framework.Graphics.SurfaceFormat.Single"/> (R32F) —
/// MonoGame cannot create a sampleable depth-only render target on either backend (depth
/// buffers are non-sampleable), so the shadow depth is written into the color attachment
/// via <c>DepthShadow.fx</c> (non-linear <c>position.z</c> to <c>.r</c>). The forward pass
/// samples it with a comparison sampler (<c>SamplerState.ComparisonFunction</c>) for
/// hardware PCF — no <c>textureSize</c> or manual 3×3 loop required (SM3.0-clean).
/// </para>
/// <para>
/// The render target is allocated lazily against the real <c>GraphicsDevice</c> on first
/// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.ShadowAtlas.EnsureResources"/> call (mirrors
/// the B5/B8 lazy-effect pattern) — <c>GraphicsDevice</c> is not available at pipeline
/// construction time.
/// </para>
/// </remarks>
[<Sealed>]
type ShadowAtlas(config: ShadowAtlasConfig, biasConfig: ShadowBiasConfig) =

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

  let mutable fbo: RenderTarget2D = null
  let casters = Dictionary<int<ShadowCasterId>, ShadowCasterData>()
  let viewProjs = Dictionary<int, Matrix>()
  let mutable nextId = 0
  let mutable slotAllocator = 0

  // Pre-allocate uniform arrays (per-frame upload scratch — sized to MaxCasters).
  let viewProjsUniforms = Array.zeroCreate<Matrix> config.MaxCasters
  let uvOffsets = Array.zeroCreate<Vector4> config.MaxCasters
  let lightPositions = Array.zeroCreate<Vector3> config.MaxCasters
  let biases = Array.zeroCreate<float32> config.MaxCasters
  let casterTypes = Array.zeroCreate<int> config.MaxCasters
  let mutable activeCasterCount = 0

  /// <summary>Grid size (rows/columns) of the atlas.</summary>
  member _.GridSize = gridSize

  /// <summary>Size of each region in pixels.</summary>
  member _.RegionSize = regionSize

  /// <summary>The shadow depth render target (R32F color attachment).</summary>
  member _.Fbo = fbo

  /// <summary>Number of currently allocated casters.</summary>
  member _.Count = casters.Count

  /// <summary>Get all active casters (for iteration).</summary>
  member _.Casters = casters.Values

  /// <summary>The atlas configuration.</summary>
  member _.Config = config

  /// <summary>The bias configuration.</summary>
  member _.BiasConfig = biasConfig

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
  /// Lazily allocate the render target against the real <c>GraphicsDevice</c>.
  /// Called on first shadow pass (the device isn't available at construction).
  /// </summary>
  member _.EnsureResources(gd: GraphicsDevice) =
    if obj.ReferenceEquals(fbo, null) then
      fbo <-
        new RenderTarget2D(
          gd,
          config.Resolution,
          config.Resolution,
          false, // no mipmaps
          SurfaceFormat.Single, // R32F — depth value written to .r by DepthShadow.fx
          DepthFormat.Depth24,
          0,
          RenderTargetUsage.DiscardContents
        )

  /// <summary>
  /// Releases the render target. Called from <c>ForwardPipeline.Shutdown</c>.
  /// </summary>
  member _.Release() =
    if not(obj.ReferenceEquals(fbo, null)) then
      fbo.Dispose()
      fbo <- null

    casters.Clear()
    viewProjs.Clear()
    slotAllocator <- 0

  /// <summary>Clear all casters and reset slot allocator. Call at start of each frame.</summary>
  member _.Clear() =
    casters.Clear()
    viewProjs.Clear()
    slotAllocator <- 0

  /// <summary>Allocate a slot in the atlas. Returns region index, or ValueNone if full.</summary>
  member private _.AllocateSlot(regionCount: int) =
    if slotAllocator + regionCount > config.MaxCasters then
      ValueNone
    else
      let slot = slotAllocator
      slotAllocator <- slot + regionCount
      ValueSome slot

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
      let id = LanguagePrimitives.Int32WithMeasure<ShadowCasterId> nextId
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
        ViewProj = Matrix.Identity
      }

      casters[id] <- caster
      ValueSome id

  /// <summary>Remove a shadow caster and free its atlas regions.</summary>
  member this.RemoveCaster(id: int<ShadowCasterId>) =
    match casters.TryGetValue(id) with
    | true, caster ->
      slotAllocator <- slotAllocator - caster.RegionCount

      if slotAllocator < 0 then
        slotAllocator <- 0

      casters.Remove(id) |> ignore
    | false, _ -> ()

  /// <summary>Get UV offset/scale for a region index (xy=offset, zw=scale).</summary>
  member _.GetUVOffsetScale(regionIndex: int) =
    let row = regionIndex / regionsPerRow
    let col = regionIndex % regionsPerRow
    let rowF = float32 row / float32 gridSize
    let colF = float32 col / float32 gridSize
    let scale = 1.0f / float32 gridSize
    Vector4(colF, rowF, scale, scale)

  /// <summary>Set the view-projection matrix for a specific atlas region.</summary>
  member _.SetRegionViewProj(regionIndex: int, vp: Matrix) =
    viewProjs[regionIndex] <- vp

  /// <summary>
  /// Get the view-projection matrix for a specific atlas region. Returns <c>Matrix.Identity</c>
  /// when the region has no VP set. Use this to read back a caster's VP —
  /// <c>ShadowCasterData.ViewProj</c> is a struct field that is NOT kept in sync with the
  /// region store (it stays <c>Identity</c> after <c>AddCaster</c>); the authoritative copy
  /// lives in the region dictionary.
  /// </summary>
  member _.GetRegionViewProj(regionIndex: int) =
    match viewProjs.TryGetValue(regionIndex) with
    | true, vp -> vp
    | false, _ -> Matrix.Identity

  /// <summary>Get a <see cref="T:Microsoft.Xna.Framework.Graphics.Viewport"/> for a region index.</summary>
  member _.GetRegionViewport(regionIndex: int) =
    let row = regionIndex / regionsPerRow
    let col = regionIndex % regionsPerRow
    Viewport(col * regionSize, row * regionSize, regionSize, regionSize)

  /// <summary>
  /// Prepare uniform arrays for upload to the forward shader. Call each frame before
  /// uploading shadow uniforms.
  /// </summary>
  member this.PrepareUniforms() =
    let mutable index = 0

    for kvp in casters do
      let caster = kvp.Value

      if caster.Enabled && index < config.MaxCasters then
        for r = 0 to caster.RegionCount - 1 do
          if index < config.MaxCasters then
            let regionIndex = caster.AtlasRegion + r

            viewProjsUniforms[index] <-
              match viewProjs.TryGetValue(regionIndex) with
              | true, vp -> vp
              | false, _ -> Matrix.Identity

            let row = regionIndex / regionsPerRow
            let col = regionIndex % regionsPerRow
            let rowF = float32 row / float32 gridSize
            let colF = float32 col / float32 gridSize
            let scale = 1.0f / float32 gridSize
            uvOffsets[index] <- Vector4(colF, rowF, scale, scale)

            lightPositions[index] <- caster.LightPosition
            biases[index] <- this.GetBias caster
            casterTypes[index] <- int caster.Type
            index <- index + 1

    // Zero out remaining slots.
    for i = index to config.MaxCasters - 1 do
      viewProjsUniforms[i] <- Matrix.Identity
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
