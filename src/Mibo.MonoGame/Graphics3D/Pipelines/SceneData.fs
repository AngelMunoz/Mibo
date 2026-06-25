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
// LightBuffers + ShadowResult moved to SceneContext.fs (compiled before Command3D.fs)
// so the DrawImmediate callback can carry a SceneContext that references them.
// This file keeps the per-frame gather record + ForwardState.
// ─────────────────────────────────────────────────────────────────────────────

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

// ShadowResult + SceneContext moved to SceneContext.fs (compiled before Command3D.fs).
