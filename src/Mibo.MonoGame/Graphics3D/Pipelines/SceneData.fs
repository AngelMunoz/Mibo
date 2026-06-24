namespace Mibo.Elmish.Graphics3D.Pipelines

open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Elmish.Graphics3D

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
