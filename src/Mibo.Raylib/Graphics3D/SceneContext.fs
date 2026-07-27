namespace Mibo.Elmish.Graphics3D.Pipelines

open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics3D

// ─────────────────────────────────────────────────────────────────────────────
// Early-defined scene types (LightBuffers, ShadowResult, SceneContext).
//
// These live in their own file, compiled BEFORE Command3D.fs, because the
// DrawImmediate command's callback carries a SceneContext — and SceneContext
// references LightBuffers + ShadowResult. The rest of the scene gather
// (ForwardState, ForwardFrame) stays internal to the pipeline.
//
// Same namespace as the pipeline (Mibo.Elmish.Graphics3D.Pipelines) so the
// DrawImmediate case reads `Pipelines.SceneContext` — a namespace can span
// multiple files.
//
// Mirrors the MonoGame backend's SceneContext.fs contract (minus the Device
// field — raylib uses global device state via Raylib.*/Rlgl.*).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-pipeline light accumulator. Created once at construction; cleared and repopulated
/// each frame.
/// </summary>
/// <remarks>
/// Public so custom pipelines, the shadow pass, and the scene-data contract exposed via
/// <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.SceneContext"/> can read the gathered lights.
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

  /// <summary>Copies the contents of <paramref name="source"/> into <paramref name="target"/>, replacing whatever target held.</summary>
  let inline copyInto (source: LightBuffers) (target: LightBuffers) =
    target.Ambient <- source.Ambient
    target.DirLights.Clear()
    target.DirLights.AddRange(source.DirLights)
    target.PointLights.Clear()
    target.PointLights.AddRange(source.PointLights)
    target.SpotLights.Clear()
    target.SpotLights.AddRange(source.SpotLights)

// ─────────────────────────────────────────────────────────────────────────────
// ShadowResult — the shadow pass output, threaded to SceneUpload and SceneContext
// so a custom/user shader can opt into shadow sampling by name.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The shadow pass output for a frame: the atlas texture + the packed shadow-sampling uniforms a
/// shader consumes. <see cref="F:Mibo.Elmish.Graphics3D.Pipelines.ShadowResult.Atlas"/> is the depth
/// atlas; the arrays are sized to the active caster count.
/// </summary>
/// <remarks>
/// Built by the pipeline's shadow pass; <c>ValueNone</c> when no shadow-casting light exists.
/// A custom/user effect opts into shadows by declaring these uniforms (by name) — see
/// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.SceneUpload.uploadToEffect"/>.
/// </remarks>
[<Struct>]
type ShadowResult = {
  /// <summary>The shadow depth atlas. Bound to sampler slot 15 (PointClamp) by SceneUpload.</summary>
  Atlas: Texture2D

  /// <summary>The packed <c>shadowViewProjs[]</c> (one per active caster region).</summary>
  ViewProjs: Matrix4x4[]

  /// <summary>The packed <c>shadowUVOffsets[]</c> (atlas-region UV scale/offset per caster).</summary>
  UVOffsets: Vector4[]

  /// <summary>The number of active caster regions (the live length of the packed arrays).</summary>
  ActiveCasterCount: int

  /// <summary><c>1.0f / atlasResolution</c> — for the <c>shadowTexelSize</c> PCF spread.</summary>
  TexelSize: float32

  /// <summary>The per-caster receiver-side bias (<c>shadowBiases[]</c>), preventing self-shadow
  /// acne when a surface both casts and receives (e.g. the instanced floor).</summary>
  Biases: float32[]

  /// <summary>Whether the directional light casts shadows (the <c>dirLightCastsShadows</c> flag).</summary>
  DirLightCastsShadows: bool

  /// <summary>The per-point-light shadow atlas slot (-1 = no shadow), indexed by PointLights position.</summary>
  PointLightShadowIdx: int[]

  /// <summary>The per-spot-light shadow atlas slot (-1 = no shadow), indexed by SpotLights position.</summary>
  SpotLightShadowIdx: int[]
}

// ─────────────────────────────────────────────────────────────────────────────
// SceneContext — the bundle handed to a drawImmediate callback: the gathered
// scene data the pipeline collected this frame, so a fully-custom draw
// (water, screen-space effects, multi-pass) can read the active camera/lights/
// shadows/time without re-implementing the gather.
//
// Deliberately a public, stable record (not the internal ForwardFrame):
// drawImmediate is a public escape hatch, so its callback signature can't leak
// internal types. Mirrors the MonoGame SceneContext minus the Device field
// (raylib uses global device state).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The scene state passed to a <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.drawImmediate"/> callback: everything the
/// pipeline gathered for the current frame (active camera, view/projection matrices, lights, the
/// shadow pass output, elapsed time). Use it for fully-custom draws that need the scene data —
/// water/refraction, screen-space effects, multi-pass.
/// </summary>
/// <remarks>
/// Read-only snapshot of the frame's gather; raylib device state is global (<c>Raylib.*</c>/<c>Rlgl.*</c>),
/// so there is no Device field (unlike the MonoGame backend). The pipeline restores camera scope
/// around the callback. <see cref="F:Mibo.Elmish.Graphics3D.Pipelines.SceneContext.Shadows"/> is
/// <c>ValueNone</c> when no shadow-casting light exists.
/// </remarks>
[<Struct>]
type SceneContext = {
  /// <summary>The active camera config.</summary>
  Camera: Camera3D

  /// <summary>The active camera's view matrix.</summary>
  View: Matrix4x4

  /// <summary>The active camera's projection matrix.</summary>
  Projection: Matrix4x4

  /// <summary>The frame's accumulated lights (ambient + directional + point + spot).</summary>
  Lights: LightBuffers

  /// <summary>The frame's shadow pass output — ValueNone when no shadow-casting light exists.</summary>
  Shadows: ShadowResult voption

  /// <summary>Total elapsed game time, in seconds — the animation clock.</summary>
  Time: float32
}
