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
  /// <summary>Whether to show debug overlay. Default false.</summary>
  ShowDebugOverlay: bool

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
    ShowDebugOverlay = false
    OriginStrategy = CameraTarget
    DirectionalLightDistance = ValueNone
    DirectionalLightSize = ValueNone
    GridSnapSize = 2.0f
    MaxShadowLightDistance = 50.0f
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
      ValueSome id

  /// <summary>Remove a shadow caster and free its atlas regions.</summary>
  member this.RemoveCaster(id: int<ShadowCasterId>) =
    match casters.TryGetValue(id) with
    | true, caster ->
      this.FreeSlot(caster.AtlasRegion, caster.RegionCount)
      casters.Remove(id) |> ignore
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
    | false, _ -> ()

  /// <summary>Get UV offset/scale for a region index.</summary>
  member _.GetUVOffsetScale(regionIndex: int) =
    let row = regionIndex / regionsPerRow
    let col = regionIndex % regionsPerRow

    let offset =
      Vector2(float32 col / float32 gridSize, float32 row / float32 gridSize)

    let scale = Vector2(1.0f / float32 gridSize, 1.0f / float32 gridSize)
    Vector4(offset.X, offset.Y, scale.X, scale.Y)

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
    let row = regionIndex / regionsPerRow
    let col = regionIndex % regionsPerRow
    Rlgl.Viewport(col * regionSize, row * regionSize, regionSize, regionSize)

  /// <summary>Get scissor rectangle for a region index.</summary>
  member _.GetRegionScissor(regionIndex: int) =
    let row = regionIndex / regionsPerRow
    let col = regionIndex % regionsPerRow
    Rlgl.Scissor(col * regionSize, row * regionSize, regionSize, regionSize)

  /// <summary>Clear a specific region in the atlas.</summary>
  member this.ClearRegion(regionIndex: int) =
    this.GetRegionViewport(regionIndex)
    Raylib.ClearBackground(Color.White)
    Rlgl.Viewport(0, 0, config.Resolution, config.Resolution)

  /// <summary>
  /// Prepare uniform arrays for upload to shader.
  /// Call this each frame before rendering.
  /// </summary>
  member _.PrepareUniforms() =
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

            // Inline GetUVOffsetScale
            let regionIndex = caster.AtlasRegion + r
            let row = regionIndex / regionsPerRow
            let col = regionIndex % regionsPerRow

            let offset =
              Vector2(
                float32 col / float32 gridSize,
                float32 row / float32 gridSize
              )

            let scale =
              Vector2(1.0f / float32 gridSize, 1.0f / float32 gridSize)

            uvOffsets[index] <- Vector4(offset.X, offset.Y, scale.X, scale.Y)

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

  /// <summary>
  /// Render the debug overlay showing atlas regions.
  /// </summary>
  member _.RenderDebugOverlay(screenWidth: int, screenHeight: int) =
    if config.ShowDebugOverlay && fbo.Id <> 0u then
      let previewSize = min 256.0f (float32 screenWidth * 0.3f)
      let previewX = float32 screenWidth - previewSize - 10.0f
      let previewY = float32 screenHeight - previewSize - 10.0f

      // Draw atlas preview
      let srcRect =
        Raylib_cs.Rectangle(
          0.0f,
          0.0f,
          float32 config.Resolution,
          float32 -config.Resolution
        )

      let dstRect =
        Raylib_cs.Rectangle(previewX, previewY, previewSize, previewSize)

      Raylib.DrawTexturePro(
        fbo.Depth,
        srcRect,
        dstRect,
        Vector2.Zero,
        0.0f,
        Color.White
      )

      // Draw grid lines
      let gridLines = gridSize

      for i = 0 to gridLines do
        let x = previewX + (previewSize * float32 i / float32 gridLines)
        let y = previewY + (previewSize * float32 i / float32 gridLines)

        Raylib.DrawLine(
          int x,
          int previewY,
          int x,
          int(previewY + previewSize),
          Color.Red
        )

        Raylib.DrawLine(
          int previewX,
          int y,
          int(previewX + previewSize),
          int y,
          Color.Red
        )

      // Highlight used regions
      for kvp in casters do
        let caster = kvp.Value

        if caster.Enabled then
          for r = 0 to caster.RegionCount - 1 do
            let regionIndex = caster.AtlasRegion + r
            let row = regionIndex / regionsPerRow
            let col = regionIndex % regionsPerRow

            let regionX =
              previewX + (previewSize * float32 col / float32 gridSize)

            let regionY =
              previewY + (previewSize * float32 row / float32 gridSize)

            let regionW = previewSize / float32 gridSize
            let regionH = previewSize / float32 gridSize

            Raylib.DrawRectangleLines(
              int regionX,
              int regionY,
              int regionW,
              int regionH,
              Color.Yellow
            )

      // Draw border
      Raylib.DrawRectangleLines(
        int previewX - 1,
        int previewY - 1,
        int previewSize + 2,
        int previewSize + 2,
        Color.White
      )

// ------------------------------------------------------------------
// Helper Functions for Shadow Rendering
// ------------------------------------------------------------------
