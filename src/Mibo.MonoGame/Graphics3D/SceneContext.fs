namespace Mibo.Elmish.Graphics3D.Pipelines

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics3D

// ─────────────────────────────────────────────────────────────────────────────
// Early-defined scene types (LightBuffers, ShadowResult, SceneContext, ForwardState).
//
// These live in their own file, compiled BEFORE Command3D.fs, because the
// DrawImmediate command's callback carries a SceneContext — and SceneContext
// references LightBuffers + ShadowResult. ForwardState shares the file so the
// pipeline files (compiled later) can thread it byref.
//
// Same namespace as the pipelines (Mibo.Elmish.Graphics3D.Pipelines) so existing
// references resolve unchanged — a namespace can span multiple files.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-pipeline light accumulator. Created once at construction; cleared and repopulated
/// each frame (mirrors the canonical raylib <c>LightBuffers</c> double-scan pattern).
/// </summary>
/// <remarks>
/// Public so custom pipelines and the shadow pass can read the gathered lights.
/// <para>
/// In single-camera frames the accumulator holds every light command in the buffer,
/// frame-globally. In frames with more than one camera block the lights are scoped per
/// block: a block that issues its own light commands resets the accumulator to the frame
/// defaults (the light commands issued outside any camera block) and applies its own
/// commands in-order; a block that issues none inherits the previous block's set.
/// </para>
/// </remarks>
type LightBuffers = {
  /// <summary>Ambient light for the active camera block (single slot).</summary>
  mutable Ambient: AmbientLight3D voption

  /// <summary>Directional lights accumulated for the active camera block.</summary>
  DirLights: ResizeArray<DirectionalLight3D>

  /// <summary>Point lights accumulated for the active camera block.</summary>
  PointLights: ResizeArray<PointLight3D>

  /// <summary>Spot lights accumulated for the active camera block.</summary>
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
  [<System.Obsolete("Shared mutable accumulator: all consumers alias the same buffers. Use LightBuffers.create for per-instance state.")>]
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
// ShadowResult — the shadow pass output, threaded to both Shade overrides and
// SceneUpload so a custom/user effect can opt into shadow sampling by name.
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
// SceneContext — the bundle handed to a drawImmediate callback: full control of
// the device PLUS the scene data the pipeline already gathered this frame, so a
// fully-custom draw (water, screen-space effects, multi-pass) can read the active
// camera/lights/shadows/time without re-implementing the gather.
//
// Deliberately a public, stable record (not the internal ForwardFrame): drawImmediate
// is a public escape hatch, so its callback signature can't leak internal types.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The scene state passed to a <see cref="M:Mibo.Elmish.Graphics3D.Draw3D.drawImmediate"/> callback: the raw
/// graphics device plus everything the pipeline gathered for the current frame (active camera,
/// lights, the shadow pass output, elapsed time). Use it for fully-custom draws that need both
/// device control and the scene data — water/refraction, screen-space effects, multi-pass.
/// </summary>
/// <remarks>
/// Read-only snapshot of the frame's gather; mutate device state at your own risk (the pipeline
/// restores viewport + camera scope around the callback). <see cref="F:Mibo.Elmish.Graphics3D.Pipelines.SceneContext.Shadows"/>
/// is <c>ValueNone</c> when no shadow-casting light exists.
/// </remarks>
[<Struct>]
type SceneContext = {
  /// <summary>The graphics device — bind render targets, blend/raster/depth states, issue raw draws.</summary>
  Device: GraphicsDevice

  /// <summary>The active camera's view matrix.</summary>
  View: Matrix

  /// <summary>The active camera's projection matrix.</summary>
  Projection: Matrix

  /// <summary>The active camera config.</summary>
  Camera: Camera3D

  /// <summary>The active light set (ambient + directional + point + spot). Frame-global in
  /// single-camera frames; scoped to the current camera block in multi-camera-block frames.</summary>
  Lights: LightBuffers

  /// <summary>The frame's shadow pass output — ValueNone when no shadow-casting light / missing DepthShadow.fx.</summary>
  Shadows: ShadowResult voption

  /// <summary>Total elapsed game time, in seconds — the animation clock.</summary>
  Time: float32
}

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
