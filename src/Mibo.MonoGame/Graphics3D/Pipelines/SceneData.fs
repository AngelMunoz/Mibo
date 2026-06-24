namespace Mibo.Elmish.Graphics3D.Pipelines

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics3D

// ─────────────────────────────────────────────────────────────────────────────
// ForwardState — per-frame forward-rendering state, threaded byref through dispatch.
//
// Mirrors the RendererState pattern from Renderer2D.fs: a mutable struct threaded by reference so
// dispatch avoids heap allocation on the hot path. Public because the staged base's virtual Shade
// exposes it (byref) to subclass / object-expression overrides — a shading strategy needs the
// active camera's view/projection. Repopulated each frame by the gather + forward-pass; overrides
// read it, they should not mutate it.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Per-frame forward-rendering state, threaded byref through dispatch.</summary>
/// <remarks>Mutable struct (hot path, no allocation); repopulated each frame by the forward pass.</remarks>
[<Struct>]
type ForwardState = {
  mutable HasCamera: bool
  mutable View: Matrix
  mutable Projection: Matrix
  mutable CurrentCamera: Camera3D
  mutable CurrentConfig: Camera3DConfig voption
  mutable SavedViewport: Viewport
}

// ─────────────────────────────────────────────────────────────────────────────
// SceneData — the reusable per-frame scene gather.
//
// Extracted from ForwardPipeline's private ForwardHelpers (Phase 1 of the v2
// pipeline-staging work). Holds the lights + shadow origin gathered by walking
// the command buffer once per frame — the data both the default pipeline and a
// custom pipeline (Forward+/Toon/etc.) consume.
//
// The camera/view/projection portion stays on ForwardState (the per-draw camera
// tracker in ForwardPipeline) for now; Phase 3 (ForwardPipelineBase) reconciles
// them into a unified scene record when the staged base lands.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-pipeline light accumulator. Created once at construction; cleared and repopulated
/// each frame (mirrors the canonical raylib <c>LightBuffers</c> double-scan pattern).
/// </summary>
/// <remarks>
/// Now public (was private in ForwardHelpers) so custom pipelines and the shadow pass
/// can read the gathered lights.
/// </remarks>
type LightBuffers = {
  /// <summary>Ambient light for the frame (single slot).</summary>
  mutable Ambient: AmbientLight3D voption

  /// <summary>Directional lights accumulated this frame.</summary>
  DirLights: ResizeArray<DirectionalLight3D>

  /// <summary>Point lights accumulated this frame.</summary>
  PointLights: ResizeArray<PointLight3D>

  /// <summary>Spot lights accumulated this frame.</summary>
  SpotLights: ResizeArray<SpotLight3D>
}

/// <summary>Convenience builders for <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.LightBuffers"/>.</summary>
module LightBuffers =

  /// <summary>Creates an empty accumulator with the given initial capacities.</summary>
  let create
    (dirCapacity: int)
    (pointCapacity: int)
    (spotCapacity: int)
    : LightBuffers =
    {
      Ambient = ValueNone
      DirLights = ResizeArray<DirectionalLight3D>(dirCapacity)
      PointLights = ResizeArray<PointLight3D>(pointCapacity)
      SpotLights = ResizeArray<SpotLight3D>(spotCapacity)
    }

  /// <summary>Default-capacity empty accumulator (3 dir / 8 point / 4 spot).</summary>
  let defaults: LightBuffers = create 3 8 4

  /// <summary>Resets all light accumulators to empty.</summary>
  let inline clear(lights: LightBuffers) =
    lights.Ambient <- ValueNone
    lights.DirLights.Clear()
    lights.PointLights.Clear()
    lights.SpotLights.Clear()

/// <summary>
/// The per-frame gather of scene-global state that the shadow pass and the shading
/// pass both need: the active camera's view/projection, the accumulated lights, and the
/// shadow origin override.
/// </summary>
/// <remarks>
/// Populated once per frame by <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.SceneData.gather"/>
/// walking the command buffer. The <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.LightBuffers"/>
/// instance is owned by the caller (reused across frames — no per-frame allocation); gather
/// clears and repopulates it.
/// </remarks>
[<Struct>]
type SceneData = {
  /// <summary>Whether a camera command was seen this frame (gates shadow/forward passes).</summary>
  mutable HasCamera: bool

  /// <summary>The active camera's view matrix.</summary>
  mutable View: Matrix

  /// <summary>The active camera's projection matrix (aspect-corrected in the forward pass).</summary>
  mutable Projection: Matrix

  /// <summary>The active camera config.</summary>
  mutable CurrentCamera: Camera3D

  /// <summary>The camera config (viewport/clear/post-process), if a BeginCameraConfig was used.</summary>
  mutable CurrentConfig: Camera3DConfig voption

  /// <summary>The accumulated lights (owned by the caller; cleared + repopulated here).</summary>
  Lights: LightBuffers

  /// <summary>The frame's shadow-origin override, if <c>SetShadowOrigin</c> was issued.</summary>
  mutable ShadowOrigin: Vector3 voption
}

/// <summary>The reusable scene-gather: walks the buffer once for camera + lights + shadow origin.</summary>
module SceneData =

  /// <summary>
  /// Clears the lights and gathers the scene-global state from the buffer: the active camera
  /// (BeginCamera/BeginCameraConfig — last one wins), all lights, and the shadow origin.
  /// Draw commands are ignored (they're handled in the forward pass).
  /// </summary>
  /// <param name="buildMatrices">Camera → (view, projection). Passed in so this module has no
  /// dependency on the matrix-construction convention (lives in ForwardPipeline/ForwardPipelineBase).</param>
  /// <param name="data">The caller-owned gather record (Lights is reused across frames).</param>
  let gather
    (buildMatrices: Camera3D -> struct (Matrix * Matrix))
    (data: byref<SceneData>)
    (buffer: RenderBuffer3D)
    : unit =
    LightBuffers.clear data.Lights
    data.HasCamera <- false
    data.View <- Matrix.Identity
    data.Projection <- Matrix.Identity
    data.CurrentCamera <- Unchecked.defaultof<Camera3D>
    data.CurrentConfig <- ValueNone
    data.ShadowOrigin <- ValueNone

    for i = 0 to buffer.Count - 1 do
      match buffer[i] with
      | Command3D.BeginCamera cam ->
        let struct (v, p) = buildMatrices cam
        data.HasCamera <- true
        data.View <- v
        data.Projection <- p
        data.CurrentCamera <- cam
        data.CurrentConfig <- ValueNone

      | Command3D.BeginCameraConfig cfg ->
        let struct (v, p) = buildMatrices cfg.Camera
        data.HasCamera <- true
        data.View <- v
        data.Projection <- p
        data.CurrentCamera <- cfg.Camera
        data.CurrentConfig <- ValueSome cfg

      | Command3D.SetAmbientLight a -> data.Lights.Ambient <- ValueSome a
      | Command3D.AddDirectionalLight d -> data.Lights.DirLights.Add d
      | Command3D.AddPointLight p -> data.Lights.PointLights.Add p
      | Command3D.AddSpotLight s -> data.Lights.SpotLights.Add s
      | Command3D.SetShadowOrigin origin ->
        data.ShadowOrigin <- ValueSome origin
      | _ -> ()

// ─────────────────────────────────────────────────────────────────────────────
// ShadowResult — the shadow pass output, threaded to both Shade overrides and
// SceneUpload so a custom/user effect can opt into shadow sampling by name.
//
// Built once per frame by ShadowPass.run (ValueNone when no light casts shadows
// or DepthShadow.fx is missing — the scene renders unshadowed). Carries the atlas
// texture + the packed uniform arrays a shader samples (shadowViewProjs[],
// shadowUVOffsets[], shadowTexelSize, dirLightCastsShadows, the per-light
// *LightShadowIdx slots, the active caster count). The atlas texture itself is
// bound to sampler slot 5 by the uploader (PointClamp).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The shadow pass output for a frame: the atlas texture + the packed shadow-sampling uniforms a
/// shader consumes. <see cref="F:Mibo.Elmish.Graphics3D.Pipelines.ShadowResult.Atlas"/> is the depth
/// atlas (sampler slot 5, PointClamp); the arrays are already sized to the active caster count.
/// </summary>
/// <remarks>
/// Built by <c>ShadowPass.run</c>; <c>ValueNone</c> when no shadow-casting light exists or
/// <c>DepthShadow.fx</c> is unavailable. A custom/user effect opts into shadows by declaring these
/// uniforms (by name) — see <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.SceneUpload.uploadToEffect"/>.
/// </remarks>
[<Struct>]
type ShadowResult = {
  /// <summary>The shadow depth atlas (R32F). Bind to sampler slot 5 with PointClamp.</summary>
  Atlas: Texture2D

  /// <summary>The packed <c>shadowViewProjs[]</c> (one per active caster region).</summary>
  ViewProjs: Matrix[]

  /// <summary>The packed <c>shadowUVOffsets[]</c> (atlas-region UV scale/offset per caster).</summary>
  UVOffsets: Vector4[]

  /// <summary>The number of active caster regions (the live length of the packed arrays).</summary>
  ActiveCasterCount: int

  /// <summary><c>1.0f / atlasResolution</c> — for the <c>shadowTexelSize</c> PCF spread.</summary>
  TexelSize: float32

  /// <summary>Whether the directional light casts shadows (the <c>dirLightCastsShadows</c> flag).</summary>
  DirLightCastsShadows: bool

  /// <summary>The per-point-light shadow atlas slot (-1 = no shadow), indexed by PointLights position.</summary>
  PointLightShadowIdx: int[]

  /// <summary>The per-spot-light shadow atlas slot (-1 = no shadow), indexed by SpotLights position.</summary>
  SpotLightShadowIdx: int[]
}
