namespace Mibo.Elmish.Graphics3D.Pipelines

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open MonoGame.Framework.Utilities
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D

// ─────────────────────────────────────────────────────────────────────────────
// PbrShading — the PBR draw handlers, extracted from the pipeline (v2 pipeline-
// staging refactor). Each shaded draw kind (model / animated model / primitive /
// instanced) routes through the custom Cook-Torrance ForwardPbr.fx via these module
// functions, plus the user-effect scope path (shadeWithEffect) that uploads scene
// data to an arbitrary effect through SceneUpload.
//
// All PBR-owned state (the lazily-loaded effect + params, the BasicEffect fallback,
// the instancing effect + vertex buffer, the MaterialKey short-circuit cache, the
// bone-transforms scratch) lives on PbrResources, owned once by the pipeline. Per-
// frame scene state (lights, bone palette, per-light shadow slots) is passed in a
// small ForwardFrame struct. No per-draw heap allocation on the hot path.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Structural identity key for a <see cref="T:Mibo.Elmish.Graphics3D.Material3D"/> — texture map
/// references + scalar/color fields. Used to skip uniform re-uploads when consecutive PBR draws
/// share the same material (mirrors the canonical raylib MaterialKey short-circuit). Texture fields
/// use reference equality (a Texture2D has no stable numeric ID on MonoGame, unlike raylib's .Id).
/// </summary>
[<Struct>]
type internal MaterialKey = {
  AlbedoMap: Texture2D
  RoughnessMap: Texture2D
  MetallicMap: Texture2D
  NormalMap: Texture2D
  EmissionMap: Texture2D
  AlbedoColor: Color
  Roughness: float32
  Metallic: float32
  EmissionColor: Color
  Opacity: float32
  TilingX: float32
  TilingY: float32
}

/// <summary>Per-frame scene state the PBR handlers read (passed byref, no allocation).</summary>
/// <remarks>
/// <see cref="F:Mibo.Elmish.Graphics3D.Pipelines.ForwardFrame.Lights"/> is frame-global in
/// single-camera frames; in frames with more than one camera block it is scoped to the block
/// currently being drawn (reset-with-inheritance — see
/// <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.LightBuffers"/>).
/// </remarks>
[<Struct>]
type ForwardFrame = {
  /// <summary>The active light set (see type remarks).</summary>
  Lights: LightBuffers
  /// <summary>The pooled bone-palette scratch (shared with the shadow pass) for skinned draws.</summary>
  BonePaletteScratch: Matrix[]
  /// <summary>Per-light shadow atlas slots (-1 = no shadow), indexed by PointLights position.
  /// Reseated from the shadow pass output at each camera block's start.</summary>
  mutable PointShadowSlots: int[]
  /// <summary>Per-light shadow atlas slots (-1 = no shadow), indexed by SpotLights position.
  /// Reseated from the shadow pass output at each camera block's start.</summary>
  mutable SpotShadowSlots: int[]
  /// <summary>The active shadow pass output — ValueNone when no shadow-casting light / missing DepthShadow.fx.
  /// Reseated from the shadow pass output at each camera block's start.
  /// The user-effect scope (<see cref="M:Mibo.Elmish.Graphics3D.Pipelines.PbrShading.shadeWithEffect"/>)
  /// uploads these uniforms by name so a custom effect can opt into shadow sampling.</summary>
  mutable Shadows: ShadowResult voption
  /// <summary>The total elapsed game time, in seconds (<c>Game.TotalGameTime.TotalSeconds</c>). Uploaded
  /// as the <c>time</c> uniform so an animated shader (water ripples, flowing textures, pulsing emissive)
  /// has a clock to read — the only animation input the scene-data contract provides.</summary>
  Time: float32
}

/// <summary>
/// Per-mesh-part draw state for a skinned + instanced command, resolved once per
/// command: technique, material, and matModel don't vary across chunks — with
/// DX12's small uniform groups (hundreds of chunks per frame per command) the old
/// per-chunk re-resolution multiplied string-keyed technique lookups, material
/// resolver calls, and matrix inversions by the chunk count.
/// </summary>
[<Struct>]
type internal SkinnedInstancedPartInfo = {
  Part: ModelMeshPart
  IsSkinned: bool
  World: Matrix
  NormalMatrix: Matrix
  Mat: Material3D
  MatKey: MaterialKey
  UseGrouped: bool
  Technique: EffectTechnique
}

/// <summary>
/// One drawable unit of a skinned + instanced command: either an original mesh part
/// (<see cref="F:Mibo.Elmish.Graphics3D.Pipelines.PbrResources.SkinnedInstancedUnits"/>
/// entries with <c>SourcePart = ValueSome</c>) or a merged group of parts whose
/// resolved materials matched for this command (<c>SourcePart = ValueNone</c> — draw
/// the merged buffers directly, always with VertexOffset/StartIndex = 0). See
/// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.MergedModelParts.tryGet"/>.
/// </summary>
[<Struct>]
type internal SkinnedInstancedDrawUnit = {
  VB: VertexBuffer
  IB: IndexBuffer
  VertexOffset: int
  StartIndex: int
  PrimitiveCount: int
  Info: SkinnedInstancedPartInfo
  SourcePart: ModelMeshPart voption
}

/// <summary>
/// Owns the lazily-loaded PBR effect + cached params, the BasicEffect fallback, the instancing
/// effect + growable instance vertex buffer + staging, the MaterialKey short-circuit cache, and
/// the bone-transforms scratch — all reused across frames. Constructed once by the pipeline.
/// </summary>
type internal PbrResources() =
  /// <summary>The custom Cook-Torrance effect (ForwardPbr.fx), loaded lazily on first PBR draw.</summary>
  member val Effect: Effect voption = ValueNone with get, set

  /// <summary>Cached PBR effect uniform handles (built when the effect loads).</summary>
  member val Params: PbrEffectParams voption = ValueNone with get, set

  /// <summary>The isolated grouped-uniform PBR effect (ForwardPbrGrouped.fx), loaded lazily
  /// on DX12 only. On DX11/Vulkan/OpenGL this stays ValueNone — those backends use VTF
  /// through the main effect's SkinnedInstanced techniques. On DX12 the main effect's
  /// grouped techniques are dropped by the mgfx reflection parser, so this isolated effect
  /// carries the grouped-uniform params that survive reflection.</summary>
  member val GroupedEffect: Effect voption = ValueNone with get, set

  /// <summary>Cached grouped-effect uniform handles (built when the grouped effect loads).</summary>
  member val GroupedParams: PbrEffectParams voption = ValueNone with get, set

  /// <summary>BasicEffect fallback for DrawPrimitive when the PBR effect can't load (B5/B6 floor).</summary>
  member val FallbackEffect: BasicEffect voption = ValueNone with get, set

  /// <summary>1×1 white fallback bound for absent texture maps so textureless PBR materials
  /// (e.g. <c>Material3D.colored</c>) sample white (identity multiplier) instead of null, which
  /// <c>EffectPass.Apply</c> turns into black. Created once alongside the effect.</summary>
  member val WhiteTex: Texture2D voption = ValueNone with get, set

  /// <summary>The minimal Instanced.fx fallback (flat albedo + 1 directional), loaded lazily.</summary>
  member val InstancedEffect: Effect voption = ValueNone with get, set

  /// <summary>Growable per-instance vertex buffer (VertexInstanceWorld rows). Grown on demand.</summary>
  member val InstanceVertexBuffer: VertexBuffer voption =
    ValueNone with get, set

  /// <summary>CPU staging array — packed VertexInstanceWorld rows per instance. Grows as needed.</summary>
  member val InstanceStaging =
    Array.zeroCreate<VertexInstanceWorld> 64 with get, set

  /// <summary>Growable per-instance vertex buffer (VertexInstanceWorldColor rows) for colored
  /// instanced draws. Grown on demand; stays a DynamicVertexBuffer (see stageInstanceData).</summary>
  member val InstanceColorVertexBuffer: VertexBuffer voption =
    ValueNone with get, set

  /// <summary>CPU staging array — packed VertexInstanceWorldColor rows per instance. Grows as needed.</summary>
  member val InstanceColorStaging =
    Array.zeroCreate<VertexInstanceWorldColor> 64 with get, set

  /// <summary>Growable per-instance vertex buffer (VertexInstanceWorldPalette rows) for skinned +
  /// instanced draws. Grown on demand; stays a DynamicVertexBuffer (see stageInstanceData).</summary>
  member val InstancePaletteVertexBuffer: VertexBuffer voption =
    ValueNone with get, set

  /// <summary>CPU staging array — packed VertexInstanceWorldPalette rows per chunk. Grows as needed.</summary>
  member val InstancePaletteStaging =
    Array.zeroCreate<VertexInstanceWorldPalette> 64 with get, set

  /// <summary>Growable per-instance vertex buffer (VertexInstanceWorldPaletteColor rows) for colored
  /// skinned + instanced draws. Grown on demand; stays a DynamicVertexBuffer (see stageInstanceData).</summary>
  member val InstancePaletteColorVertexBuffer: VertexBuffer voption =
    ValueNone with get, set

  /// <summary>CPU staging array — packed VertexInstanceWorldPaletteColor rows per chunk. Grows as needed.</summary>
  member val InstancePaletteColorStaging =
    Array.zeroCreate<VertexInstanceWorldPaletteColor> 64 with get, set

  /// <summary>Reusable bone-palette slice for the OpenGL / DX12-user-effect per-instance
  /// fallback of skinned + instanced draws. Grown on demand, reused across frames.</summary>
  member val BoneSliceScratch = Array.empty<Matrix> with get, set

  /// <summary>Shared per-frame palette-chunk cache for skinned + instanced draws (aliased
  /// with the shadow pass by ForwardPipeline so each frame's palettes are staged + uploaded
  /// once, not per pass — see <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.PaletteChunkCache"/>).</summary>
  member val PaletteChunks = new PaletteChunkCache() with get, set

  /// <summary>Pooled bone-palette scratch for the grouped-uniform skinned + instanced path
  /// (the DX12 fallback — SkinnedInstancedGrouped techniques). Sized to
  /// <see cref="F:Mibo.Elmish.Graphics3D.Pipelines.PaletteGroup.MaxMatrices"/>; grown on demand.</summary>
  member val GroupPaletteScratch: Matrix[] = [||] with get, set

  /// <summary>Pooled DX12 group descriptors ((start, count, null-texture) triples)
  /// for the grouped-uniform skinned + instanced path; grown on demand — see
  /// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.PaletteGroup.planGroups"/>.</summary>
  member val GroupChunkScratch: struct (int * int * Texture2D)[] =
    [||] with get, set

  /// <summary>Pooled per-part invariants for skinned + instanced draws; cleared
  /// and rebuilt per command (replaces a fresh ResizeArray per command).</summary>
  member val SkinnedInstancedPartInfos =
    ResizeArray<SkinnedInstancedPartInfo>() with get

  /// <summary>Per-command draw units for skinned + instanced draws (merged groups or
  /// original parts); cleared and rebuilt per command — see
  /// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.MergedModelParts.tryGet"/>.</summary>
  member val SkinnedInstancedUnits =
    ResizeArray<SkinnedInstancedDrawUnit>() with get

  /// <summary>Scratch: original part → its merged group, rebuilt per command.</summary>
  member val PartMergedMap =
    System.Collections.Generic.Dictionary<ModelMeshPart, MergedPart>() with get

  /// <summary>Scratch: original part → its partInfo index, rebuilt per command.</summary>
  member val PartInfoIndex =
    System.Collections.Generic.Dictionary<ModelMeshPart, int>() with get

  /// <summary>Scratch: handled flags for the merged-group fan-out; grown on demand,
  /// cleared per command.</summary>
  member val SkinnedInstancedHandled: bool[] = [||] with get, set

  /// <summary>MaterialKey short-circuit: whether the last draw's material is still current.</summary>
  member val HasLastMaterial = false with get, set

  /// <summary>Whether light uniforms need re-uploading to the PBR effect. Set at frame start;
  /// cleared after the first upload so subsequent draws in the same frame skip the cost
  /// (lights are stable within a frame — gathered once in the pre-scan).</summary>
  member val LightsDirty = true with get, set

  /// <summary>The last draw's MaterialKey (valid when HasLastMaterial).</summary>
  member val LastKey: MaterialKey =
    Unchecked.defaultof<MaterialKey> with get, set

  // Reused each frame to avoid per-frame allocation. Sized generously; grows if a larger model is
  // seen. A raw array (not ResizeArray) so we can pass it directly to CopyAbsoluteBoneTransformsTo.
  member val BoneTransforms = Array.zeroCreate<Matrix> 64 with get, set

  /// <summary>Per-effect memoization of the <c>Instanced</c>-technique probe (the convention
  /// ForwardPbr.fx and Instanced.fx already use). Maps every effect seen on first instanced draw
  /// inside a <c>beginEffect</c> scope to its resolved <c>Instanced</c> technique — null when the
  /// effect doesn't opt in — so neither the probe nor the technique handle is re-resolved per
  /// draw. An effect without the technique falls back to the PBR instanced path. See
  /// docs/graphics3d/instancing.md.</summary>
  member val InstancedTechniques: System.Collections.Generic.Dictionary<
    Effect,
    EffectTechnique
   > =
    System.Collections.Generic.Dictionary<Effect, EffectTechnique>() with get, set

  /// <summary>The effect's <c>Instanced</c> technique when it opts into instancing; null
  /// otherwise. Probes + memoizes on first lookup (both outcomes); subsequent lookups are a
  /// dictionary read, never a re-probe.</summary>
  member this.TryInstancedTechnique(effect: Effect) : EffectTechnique =
    match this.InstancedTechniques.TryGetValue(effect) with
    | true, tech -> tech
    | false, _ ->
      let tech = effect.Techniques["Instanced"]
      this.InstancedTechniques[effect] <- tech
      tech

  /// <summary>Per-effect memoization of the <c>SkinnedInstanced</c>-technique probe (the
  /// skinned-instanced opt-in for user effects inside a <c>beginEffect</c> scope), following
  /// <see cref="F:Mibo.Elmish.Graphics3D.Pipelines.PbrResources.InstancedTechniques"/>. An effect
  /// without the technique falls back to the framework PBR skinned-instanced path.</summary>
  member val SkinnedInstancedTechniques: System.Collections.Generic.Dictionary<
    Effect,
    EffectTechnique
   > =
    System.Collections.Generic.Dictionary<Effect, EffectTechnique>() with get, set

  /// <summary>The effect's <c>SkinnedInstanced</c> technique when it opts into skinned
  /// instancing; null otherwise. Probes + memoizes on first lookup (both outcomes).</summary>
  member this.TrySkinnedInstancedTechnique(effect: Effect) : EffectTechnique =
    match this.SkinnedInstancedTechniques.TryGetValue(effect) with
    | true, tech -> tech
    | false, _ ->
      let tech = effect.Techniques["SkinnedInstanced"]
      this.SkinnedInstancedTechniques[effect] <- tech
      tech

/// <summary>
/// Which effect shades a skinned + instanced draw: the framework PBR effect
/// (<c>SkinnedInstanced</c>/<c>SkinnedInstancedColor</c> techniques) or a user effect inside a
/// <c>beginEffect</c> scope that opted in by declaring a <c>SkinnedInstanced</c> technique
/// (the memoized probe on <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.PbrResources"/>).
/// </summary>
[<Struct>]
type internal SkinnedInstancedTarget =
  | PbrTarget
  | UserEffectTarget of effect: Effect * technique: EffectTechnique

/// <summary>The extracted PBR draw handlers + the user-effect scope shading path.</summary>
module internal PbrShading =

  /// <summary>True on the OpenGL backend, which has no vertex texture fetch — skinned +
  /// instanced draws fall back to per-instance skinned draws there.</summary>
  let inline isOpenGLBackend() =
    PlatformInfo.GraphicsBackend = GraphicsBackend.OpenGL

  /// <summary>
  /// True on the DirectX 12 backend: the native runtime never delivers
  /// vertex-stage textures to the VS (the palette SRV samples zeros regardless of slot
  /// or content — the PS reads the same SRV fine), so skinned + instanced draws use the
  /// grouped-uniform <c>SkinnedInstancedGrouped</c> techniques there instead.</summary>
  let inline isDirectX12Backend() =
    PlatformInfo.GraphicsBackend = GraphicsBackend.DirectX12

  /// <summary>
  /// Applies accumulated lighting to any <see cref="T:Microsoft.Xna.Framework.Graphics.IEffectLights"/>
  /// effect (BasicEffect, SkinnedEffect). The native floor: 1 ambient + up to 3 directional slots;
  /// no native point/spot (those are consumed only by the PBR pipeline). Excess directionals clamped,
  /// unused slots disabled, fog off. Slots unrolled (no temp array) — runs once per effect draw.
  /// </summary>
  let private applyLighting(effect: IEffectLights, lights: LightBuffers) =
    match lights.Ambient with
    | ValueSome a ->
      effect.AmbientLightColor <-
        Conversions.fromNumericsVector3(Mibo.Color.toVector3 a.Color)
        * a.Intensity
    | ValueNone -> effect.AmbientLightColor <- Vector3.Zero

    let dirs = lights.DirLights
    let count = dirs.Count

    if count > 0 then
      let d = dirs[0]
      effect.DirectionalLight0.Enabled <- true
      effect.DirectionalLight0.Direction <- d.Direction

      effect.DirectionalLight0.DiffuseColor <-
        Conversions.fromNumericsVector3(Mibo.Color.toVector3 d.Color)
        * d.Intensity
    else
      effect.DirectionalLight0.Enabled <- false

    if count > 1 then
      let d = dirs[1]
      effect.DirectionalLight1.Enabled <- true
      effect.DirectionalLight1.Direction <- d.Direction

      effect.DirectionalLight1.DiffuseColor <-
        Conversions.fromNumericsVector3(Mibo.Color.toVector3 d.Color)
        * d.Intensity
    else
      effect.DirectionalLight1.Enabled <- false

    if count > 2 then
      let d = dirs[2]
      effect.DirectionalLight2.Enabled <- true
      effect.DirectionalLight2.Direction <- d.Direction

      effect.DirectionalLight2.DiffuseColor <-
        Conversions.fromNumericsVector3(Mibo.Color.toVector3 d.Color)
        * d.Intensity
    else
      effect.DirectionalLight2.Enabled <- false

    // FogEnabled is on IEffectFog; PreferPerPixelLighting is on BasicEffect/SkinnedEffect directly.
    match box effect with
    | :? IEffectFog as f -> f.FogEnabled <- false
    | _ -> ()

    match box effect with
    | :? BasicEffect as be -> be.PreferPerPixelLighting <- true
    | :? SkinnedEffect as se -> se.PreferPerPixelLighting <- true
    | _ -> ()

  /// <summary>Builds a <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.MaterialKey"/> from a material (null for absent maps).</summary>
  let inline materialKey(mat: inref<Material3D>) : MaterialKey =
    let texOrNull(t: Texture2D voption) =
      match t with
      | ValueSome x -> x
      | ValueNone -> null

    {
      AlbedoMap = texOrNull mat.AlbedoMap
      RoughnessMap = texOrNull mat.RoughnessMap
      MetallicMap = texOrNull mat.MetallicMap
      NormalMap = texOrNull mat.NormalMap
      EmissionMap = texOrNull mat.EmissionMap
      AlbedoColor = mat.AlbedoColor
      Roughness = mat.Roughness
      Metallic = mat.Metallic
      EmissionColor = mat.EmissionColor
      Opacity = mat.Opacity
      TilingX = mat.Tiling.X
      TilingY = mat.Tiling.Y
    }

  /// <summary>
  /// Lazily loads the custom PBR <c>Effect</c> on first PBR draw against the real device. Returns
  /// <c>true</c> when Effect/Params are usable; <c>false</c> when the embedded resource is missing
  /// (caller falls back to BasicEffect).
  /// </summary>
  let ensureEffect(gd: GraphicsDevice, res: PbrResources) : bool =
    match res.Effect with
    | ValueSome _ -> true
    | ValueNone ->
      match ShaderLoader.loadEffect gd "ForwardPbr" with
      | ValueSome e ->
        res.Params <- ValueSome(PbrUniforms.build e)
        res.Effect <- ValueSome e

        match res.WhiteTex with
        | ValueNone ->
          let tex = new Texture2D(gd, 1, 1)
          tex.SetData([| Color.White |])
          res.WhiteTex <- ValueSome tex
        | ValueSome _ -> ()

        true
      | ValueNone -> false

  /// <summary>
  /// Lazily loads the isolated grouped-uniform PBR effect (ForwardPbrGrouped.fx) on DX12.
  /// On other backends this is a no-op (ValueNone stays). On DX12 the main ForwardPbr.fx's
  /// grouped-uniform params are dropped by the mgfx reflection parser; this isolated effect
  /// carries them. Returns true when the grouped effect is usable.
  /// </summary>
  let ensureGroupedEffect(gd: GraphicsDevice, res: PbrResources) : bool =
    match res.GroupedEffect with
    | ValueSome _ -> true
    | ValueNone ->
      if isDirectX12Backend() then
        match ShaderLoader.loadEffect gd "ForwardPbrGrouped" with
        | ValueSome e ->
          res.GroupedParams <- ValueSome(PbrUniforms.build e)
          res.GroupedEffect <- ValueSome e
          true
        | ValueNone -> false
      else
        false

  /// <summary>Extracts the cached white fallback texture (created by ensureEffect); null before load.</summary>
  let inline whiteTex(res: PbrResources) : Texture2D =
    match res.WhiteTex with
    | ValueSome t -> t
    | ValueNone -> null

  // ── drawPart: draw a single ModelMeshPart manually (part has no Draw() of its own). ──
  let private drawPart(gd: GraphicsDevice, part: ModelMeshPart) =
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
  /// Handles <c>DrawModel</c>: routes every mesh part through the PBR effect. For each part the baked
  /// native effect is read into a Material3D, the part's effect is swapped to the PBR Standard technique
  /// around the draw, and lighting/shadows come from the frame's accumulated lights.
  /// </summary>
  let drawModel
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      frame: byref<ForwardFrame>,
      res: PbrResources,
      model: Model,
      transform: Matrix,
      matOverride: MaterialOverride voption
    ) =
    if ensureEffect(gd, res) then
      match res.Effect, res.Params with
      | ValueSome e, ValueSome p ->
        let boneCount = model.Bones.Count

        if res.BoneTransforms.Length < boneCount then
          res.BoneTransforms <- Array.zeroCreate<Matrix> boneCount

        model.CopyAbsoluteBoneTransformsTo(res.BoneTransforms)
        e.CurrentTechnique <- e.Techniques["Standard"]

        // Frame-global uniforms don't depend on the mesh — set once per draw, not per mesh.
        let viewProj = state.View * state.Projection

        PbrUniforms.setMatrix p.Matrix.ViewProj viewProj
        PbrUniforms.setVec3 p.Matrix.CameraPos state.CurrentCamera.Position

        if res.LightsDirty then
          PbrUniforms.uploadLights(
            &p,
            frame.Lights,
            frame.PointShadowSlots,
            frame.SpotShadowSlots
          )

          res.LightsDirty <- false

        let mutable partIndex = 0

        for mesh in model.Meshes do
          let world = res.BoneTransforms[mesh.ParentBone.Index] * transform
          let mutable t = world
          let mutable inv = Matrix.Identity
          Matrix.Invert(&t, &inv) |> ignore
          let normalMatrix = Matrix.Transpose inv

          PbrUniforms.setMatrix p.Matrix.MatModel world
          PbrUniforms.setMatrix p.Matrix.NormalMatrix normalMatrix

          for part in mesh.MeshParts do
            let mat =
              match matOverride with
              | ValueNone -> Material3D.fromModelMeshPart part
              | ValueSome(MaterialOverride.All m) -> m
              | ValueSome(MaterialOverride.PerMesh f) -> f partIndex

            partIndex <- partIndex + 1
            let key = materialKey &mat

            if not res.HasLastMaterial || key <> res.LastKey then
              PbrUniforms.uploadMaterial(&p, &mat)
              PbrUniforms.bindTextures(&p, &mat, whiteTex res)
              res.LastKey <- key
              res.HasLastMaterial <- true

            let saved = part.Effect
            part.Effect <- e

            try
              drawPart(gd, part)
            finally
              part.Effect <- saved
      | _ -> ()

  /// <summary>Handles <c>DrawAnimatedModel</c>: Skinned technique for SkinnedEffect parts,
  /// Standard for the rest; bone palette uploaded once per draw, tail zero-filled to identity.
  /// <paramref name="tint"/> modulates the resolved material's albedo color and opacity per
  /// draw (mirrors <c>shadePBR</c>'s per-instance color application) — the OpenGL fallback of
  /// skinned + instanced draws uses it for per-instance colors; the regular path passes
  /// <c>ValueNone</c>.</summary>
  let private drawAnimatedModelCore
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      frame: byref<ForwardFrame>,
      res: PbrResources,
      model: Model,
      transform: Matrix,
      bones: Matrix[],
      matOverride: MaterialOverride voption,
      tint: Color voption
    ) =
    if ensureEffect(gd, res) then
      match res.Effect, res.Params with
      | ValueSome e, ValueSome p ->
        let boneCount = model.Bones.Count

        if res.BoneTransforms.Length < boneCount then
          res.BoneTransforms <- Array.zeroCreate<Matrix> boneCount

        model.CopyAbsoluteBoneTransformsTo(res.BoneTransforms)
        let bonePaletteScratch = frame.BonePaletteScratch
        let palCount = min bones.Length bonePaletteScratch.Length

        for i = 0 to palCount - 1 do
          bonePaletteScratch[i] <- bones[i]

        for i = palCount to bonePaletteScratch.Length - 1 do
          bonePaletteScratch[i] <- Matrix.Identity

        // Frame-global uniforms don't depend on the mesh — set once per draw, not per mesh.
        let viewProj = state.View * state.Projection

        PbrUniforms.setMatrix p.Matrix.ViewProj viewProj
        PbrUniforms.setVec3 p.Matrix.CameraPos state.CurrentCamera.Position

        if res.LightsDirty then
          PbrUniforms.uploadLights(
            &p,
            frame.Lights,
            frame.PointShadowSlots,
            frame.SpotShadowSlots
          )

          res.LightsDirty <- false

        let mutable partIndex = 0

        for mesh in model.Meshes do
          let world = res.BoneTransforms[mesh.ParentBone.Index] * transform
          let mutable t = world
          let mutable inv = Matrix.Identity
          Matrix.Invert(&t, &inv) |> ignore

          PbrUniforms.setMatrix p.Matrix.MatModel world
          PbrUniforms.setMatrix p.Matrix.NormalMatrix (Matrix.Transpose inv)

          for part in mesh.MeshParts do
            let isSkinned =
              match part.Effect with
              | :? SkinnedEffect -> true
              | _ -> false

            if isSkinned then
              e.CurrentTechnique <- e.Techniques["Skinned"]
              PbrUniforms.setMatrixArray p.Matrix.Bones bonePaletteScratch
            else
              e.CurrentTechnique <- e.Techniques["Standard"]

            let mat =
              match matOverride with
              | ValueNone -> Material3D.fromModelMeshPart part
              | ValueSome(MaterialOverride.All m) -> m
              | ValueSome(MaterialOverride.PerMesh f) -> f partIndex

            partIndex <- partIndex + 1

            let mat =
              match tint with
              | ValueNone -> mat
              | ValueSome c ->
                let tintVec = c.ToVector4()

                {
                  mat with
                      AlbedoColor = Color(mat.AlbedoColor.ToVector4() * tintVec)
                      Opacity = mat.Opacity * tintVec.W
                }

            let key = materialKey &mat

            if not res.HasLastMaterial || key <> res.LastKey then
              PbrUniforms.uploadMaterial(&p, &mat)
              PbrUniforms.bindTextures(&p, &mat, whiteTex res)
              res.LastKey <- key
              res.HasLastMaterial <- true

            let saved = part.Effect
            part.Effect <- e

            try
              drawPart(gd, part)
            finally
              part.Effect <- saved
      | _ -> ()

  /// <summary>Handles <c>DrawAnimatedModel</c>: Skinned technique for SkinnedEffect parts,
  /// Standard for the rest; bone palette uploaded once per draw, tail zero-filled to identity.</summary>
  let drawAnimatedModel
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      frame: byref<ForwardFrame>,
      res: PbrResources,
      model: Model,
      transform: Matrix,
      bones: Matrix[],
      matOverride: MaterialOverride voption
    ) =
    drawAnimatedModelCore(
      gd,
      &state,
      &frame,
      res,
      model,
      transform,
      bones,
      matOverride,
      ValueNone
    )

  /// <summary>
  /// Handles <c>DrawPrimitive</c>: PBR Standard technique with a MaterialKey short-circuit. Falls
  /// back to the BasicEffect albedo-color-only path when the PBR effect can't load (B5/B6 floor).
  /// </summary>
  let drawPrimitive
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      frame: byref<ForwardFrame>,
      res: PbrResources,
      mesh: PrimitiveMesh,
      transform: Matrix,
      material: Material3D
    ) =
    if ensureEffect(gd, res) then
      match res.Effect, res.Params with
      | ValueSome e, ValueSome p ->
        e.CurrentTechnique <- e.Techniques["Standard"]
        let mutable t = transform
        let mutable inv = Matrix.Identity
        Matrix.Invert(&t, &inv) |> ignore
        let normalMatrix = Matrix.Transpose inv

        PbrUniforms.setMatrix p.Matrix.MatModel transform
        PbrUniforms.setMatrix p.Matrix.ViewProj (state.View * state.Projection)
        PbrUniforms.setMatrix p.Matrix.NormalMatrix normalMatrix
        PbrUniforms.setVec3 p.Matrix.CameraPos state.CurrentCamera.Position

        let key = materialKey &material

        if not res.HasLastMaterial || key <> res.LastKey then
          PbrUniforms.uploadMaterial(&p, &material)
          PbrUniforms.bindTextures(&p, &material, whiteTex res)
          res.LastKey <- key
          res.HasLastMaterial <- true

        if res.LightsDirty then
          PbrUniforms.uploadLights(
            &p,
            frame.Lights,
            frame.PointShadowSlots,
            frame.SpotShadowSlots
          )

          res.LightsDirty <- false

        mesh.Draw(gd, e)
      | _ -> ()
    else
      // ── BasicEffect fallback (B5/B6 floor) — albedo color only. ──
      let effect =
        match res.FallbackEffect with
        | ValueSome e -> e
        | ValueNone ->
          let e = new BasicEffect(gd)
          res.FallbackEffect <- ValueSome e
          e

      let c = material.AlbedoColor

      effect.DiffuseColor <-
        Vector3(
          float32 c.R / 255.0f,
          float32 c.G / 255.0f,
          float32 c.B / 255.0f
        )

      effect.Alpha <- material.Opacity
      effect.Texture <- null
      effect.TextureEnabled <- false
      effect.VertexColorEnabled <- false
      effect.World <- transform
      effect.View <- state.View
      effect.Projection <- state.Projection
      applyLighting(effect, frame.Lights)
      mesh.Draw(gd, effect)

  /// <summary>
  /// Stages per-instance world matrices into the reusable <see cref="T:Mibo.Elmish.Graphics3D.VertexInstanceWorld"/>
  /// buffer (growing it on demand), uploads them, and binds the two-stream vertex layout (mesh on
  /// stream 0, per-instance rows on stream 1). Returns the clamped instance count (0 when there is
  /// nothing to draw) and the instance vertex buffer. Shared by the PBR instanced path and the
  /// user-effect instanced path so the two-stream bind lives in one place.
  /// </summary>
  /// <remarks>Does not bind indices or draw — the caller selects its effect/technique, uploads
  /// scene data, and issues <c>DrawInstancedPrimitives</c>.</remarks>
  let stageInstanceData
    (
      gd: GraphicsDevice,
      res: PbrResources,
      mesh: PrimitiveMesh,
      transforms: Matrix[],
      instanceCount: int
    ) : struct (int * VertexBuffer) =
    // Clamp to the transforms array: an instanceCount larger than the buffer
    // would index out of range when staging per-instance world matrices.
    let instanceCount =
      min instanceCount (if isNull transforms then 0 else transforms.Length)

    if instanceCount <= 0 then
      struct (0, Unchecked.defaultof<VertexBuffer>)
    else
      if res.InstanceStaging.Length < instanceCount then
        res.InstanceStaging <-
          Array.zeroCreate<VertexInstanceWorld> instanceCount

      for i = 0 to instanceCount - 1 do
        res.InstanceStaging[i] <- VertexInstanceWorld.Create transforms[i]

      // NOTE: this buffer must stay a DynamicVertexBuffer. It is re-uploaded once
      // per instance group within a single frame; on the native DX12 backend,
      // SetData on a *static* buffer is recorded into a separate command list that
      // executes immediately, while draws execute at end of frame — so every draw
      // would read the LAST group's matrices (garbage/flickering instances).
      // Dynamic buffers take the discard-rename path (fresh buffer per upload),
      // which keeps each draw's instance data intact.
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
        | ValueNone -> Unchecked.defaultof<VertexBuffer> // unreachable (created above)

      instVB.SetData(res.InstanceStaging, 0, instanceCount)

      gd.SetVertexBuffers(
        VertexBufferBinding(mesh.Vertices, 0, 0),
        VertexBufferBinding(instVB, 0, 1)
      )

      struct (instanceCount, instVB)

  /// <summary>
  /// The colored counterpart of <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.PbrShading.stageInstanceData"/>:
  /// stages per-instance world matrices + colors into the reusable
  /// <see cref="T:Mibo.Elmish.Graphics3D.VertexInstanceWorldColor"/> buffer (growing it on demand),
  /// uploads them, and binds the two-stream vertex layout (mesh on stream 0, colored per-instance
  /// rows on stream 1 — world on TEXCOORD1..4, color on TEXCOORD5). Instances past
  /// <paramref name="colors"/>' length are clamped to <c>Color.White</c> (identity multiplier).
  /// Returns the clamped instance count (0 when there is nothing to draw) and the instance vertex
  /// buffer.
  /// </summary>
  /// <remarks>Does not bind indices or draw — the caller selects its effect/technique, uploads
  /// scene data, and issues <c>DrawInstancedPrimitives</c>.</remarks>
  let stageInstanceColorData
    (
      gd: GraphicsDevice,
      res: PbrResources,
      mesh: PrimitiveMesh,
      transforms: Matrix[],
      colors: Color[],
      instanceCount: int
    ) : struct (int * VertexBuffer) =
    // Clamp to the transforms array: an instanceCount larger than the buffer
    // would index out of range when staging per-instance world matrices.
    let instanceCount =
      min instanceCount (if isNull transforms then 0 else transforms.Length)

    if instanceCount <= 0 then
      struct (0, Unchecked.defaultof<VertexBuffer>)
    else
      if res.InstanceColorStaging.Length < instanceCount then
        res.InstanceColorStaging <-
          Array.zeroCreate<VertexInstanceWorldColor> instanceCount

      let colorCount = if isNull colors then 0 else colors.Length

      for i = 0 to instanceCount - 1 do
        let color = if i < colorCount then colors[i] else Color.White

        res.InstanceColorStaging[i] <-
          VertexInstanceWorldColor.Create(transforms[i], color)

      // Must stay a DynamicVertexBuffer for the same reason as stageInstanceData:
      // the native DX12 backend needs the discard-rename path for intra-frame re-uploads.
      match res.InstanceColorVertexBuffer with
      | ValueNone ->
        let vb =
          new DynamicVertexBuffer(
            gd,
            typeof<VertexInstanceWorldColor>,
            instanceCount,
            BufferUsage.WriteOnly
          )

        res.InstanceColorVertexBuffer <- ValueSome vb
      | ValueSome vb when vb.VertexCount < instanceCount ->
        vb.Dispose()

        let vb' =
          new DynamicVertexBuffer(
            gd,
            typeof<VertexInstanceWorldColor>,
            instanceCount,
            BufferUsage.WriteOnly
          )

        res.InstanceColorVertexBuffer <- ValueSome vb'
      | _ -> ()

      let instVB =
        match res.InstanceColorVertexBuffer with
        | ValueSome vb -> vb
        | ValueNone -> Unchecked.defaultof<VertexBuffer> // unreachable (created above)

      instVB.SetData(res.InstanceColorStaging, 0, instanceCount)

      gd.SetVertexBuffers(
        VertexBufferBinding(mesh.Vertices, 0, 0),
        VertexBufferBinding(instVB, 0, 1)
      )

      struct (instanceCount, instVB)

  /// <summary>
  /// Handles <c>DrawInstanced</c>: native hardware instancing via two vertex streams (mesh + per-
  /// instance rows). Prefers the PBR Instanced technique; falls back to minimal Instanced.fx
  /// (flat albedo + 1 directional) when the PBR effect can't load. With <c>ValueSome</c> colors the
  /// colored stream (<see cref="T:Mibo.Elmish.Graphics3D.VertexInstanceWorldColor"/>, color on
  /// TEXCOORD5) is bound and the <c>InstancedColor</c> technique is used (PBR effect and the
  /// Instanced.fx fallback alike); instances past the colors array's length draw white.
  /// <c>ValueNone</c> keeps the plain <see cref="T:Mibo.Elmish.Graphics3D.VertexInstanceWorld"/> path.
  /// </summary>
  let drawInstanced
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      frame: byref<ForwardFrame>,
      res: PbrResources,
      mesh: PrimitiveMesh,
      transforms: Matrix[],
      colors: Color[] voption,
      material: Material3D,
      instanceCount: int
    ) =
    let struct (instanceCount, _instVB) =
      match colors with
      | ValueNone -> stageInstanceData(gd, res, mesh, transforms, instanceCount)
      | ValueSome cs ->
        stageInstanceColorData(gd, res, mesh, transforms, cs, instanceCount)

    if instanceCount > 0 then
      gd.Indices <- mesh.Indices
      let viewProj = state.View * state.Projection

      if ensureEffect(gd, res) then
        match res.Effect, res.Params with
        | ValueSome e, ValueSome p ->
          e.CurrentTechnique <-
            e.Techniques[match colors with
                         | ValueSome _ -> "InstancedColor"
                         | ValueNone -> "Instanced"]

          PbrUniforms.setMatrix p.Matrix.ViewProj viewProj
          PbrUniforms.setVec3 p.Matrix.CameraPos state.CurrentCamera.Position
          // Instanced draws always upload the material (one material across all instances).
          PbrUniforms.uploadMaterial(&p, &material)
          PbrUniforms.bindTextures(&p, &material, whiteTex res)
          // Invalidate the material short-circuit: instanced draws always upload,
          // but the cache still holds the last non-instanced key. Without this,
          // a subsequent non-instanced draw whose key matches the stale cache
          // would skip texture binding and sample the instanced pass's textures.
          res.HasLastMaterial <- false

          if res.LightsDirty then
            PbrUniforms.uploadLights(
              &p,
              frame.Lights,
              frame.PointShadowSlots,
              frame.SpotShadowSlots
            )

            res.LightsDirty <- false

          for pass in e.CurrentTechnique.Passes do
            pass.Apply()

            gd.DrawInstancedPrimitives(
              PrimitiveType.TriangleList,
              0,
              0,
              mesh.PrimitiveCount,
              instanceCount
            )
        | _ -> ()
      else
        // ── B7 fallback: minimal Instanced.fx (flat albedo + 1 directional). ──
        let effect =
          match res.InstancedEffect with
          | ValueSome e -> e
          | ValueNone ->
            match ShaderLoader.loadEffect gd "Instanced" with
            | ValueSome e ->
              res.InstancedEffect <- ValueSome e
              e
            | ValueNone -> Unchecked.defaultof<_>

        if obj.ReferenceEquals(effect, null) then
          ()
        else
          let c = material.AlbedoColor

          match effect.Parameters.["ViewProj"] with
          | null -> ()
          | pp -> pp.SetValue viewProj

          match effect.Parameters.["AlbedoColor"] with
          | null -> ()
          | p ->
            p.SetValue(
              Vector3(
                float32 c.R / 255.0f,
                float32 c.G / 255.0f,
                float32 c.B / 255.0f
              )
            )

          match effect.Parameters.["AmbientColor"] with
          | null -> ()
          | p ->
            let amb =
              match frame.Lights.Ambient with
              | ValueSome a ->
                Conversions.fromNumericsVector3(Mibo.Color.toVector3 a.Color)
                * a.Intensity
              | ValueNone -> Vector3.Zero

            p.SetValue amb

          match effect.Parameters.["DirLightDir"], frame.Lights.DirLights with
          | null, _ -> ()
          | p, dl when dl.Count > 0 ->
            let d = dl[0]
            p.SetValue d.Direction

            match effect.Parameters.["DirLightColor"] with
            | null -> ()
            | pc ->
              pc.SetValue(
                Conversions.fromNumericsVector3(Mibo.Color.toVector3 d.Color)
                * d.Intensity
              )
          | _, _ ->
            match effect.Parameters.["DirLightColor"] with
            | null -> ()
            | pc -> pc.SetValue Vector3.Zero

          // Colored draws bind the VertexInstanceWorldColor stream (TEXCOORD5) — select the
          // fallback effect's matching technique. ValueNone keeps the effect's default
          // (CurrentTechnique untouched, the historical behavior).
          match colors with
          | ValueSome _ ->
            match effect.Techniques.["InstancedColor"] with
            | null -> ()
            | t -> effect.CurrentTechnique <- t
          | ValueNone -> ()

          for pass in effect.CurrentTechnique.Passes do
            pass.Apply()

            gd.DrawInstancedPrimitives(
              PrimitiveType.TriangleList,
              0,
              0,
              mesh.PrimitiveCount,
              instanceCount
            )

  /// <summary>
  /// Stages one chunk (or group) of a skinned + instanced draw: packs the chunk's
  /// per-instance rows (world matrix + chunk-local palette index, plus color when
  /// <paramref name="colors"/> is <c>ValueSome</c>) into the matching growable instance
  /// vertex buffer. The palette data itself does NOT flow through here — it rides the
  /// shared <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.PaletteChunkCache"/> textures
  /// (SkinnedInstanced) or the <c>bonePaletteGroup</c> constant array (SkinnedInstancedGrouped,
  /// the DX12 path). Does NOT bind — the caller binds stream 0 per mesh part. Instances
  /// past <paramref name="colors"/>' length are clamped to <c>Color.White</c> (identity
  /// multiplier), matching
  /// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.PbrShading.stageInstanceColorData"/>.
  /// </summary>
  let private stagePaletteInstanceVB
    (
      gd: GraphicsDevice,
      res: PbrResources,
      transforms: Matrix[],
      colors: Color[] voption,
      chunkStart: int,
      chunkCount: int
    ) : VertexBuffer =
    match colors with
    | ValueSome cs ->
      if res.InstancePaletteColorStaging.Length < chunkCount then
        res.InstancePaletteColorStaging <-
          Array.zeroCreate<VertexInstanceWorldPaletteColor> chunkCount

      let colorCount = if isNull cs then 0 else cs.Length

      for i = 0 to chunkCount - 1 do
        let gi = chunkStart + i
        let color = if gi < colorCount then cs[gi] else Color.White

        // PaletteOffset is chunk-local: palette storage (texture chunk or uniform
        // group on the DX12 path) holds this chunk only.
        res.InstancePaletteColorStaging[i] <-
          VertexInstanceWorldPaletteColor.Create(
            transforms[gi],
            color,
            float32 i
          )

      // Must stay a DynamicVertexBuffer for the same reason as stageInstanceData:
      // the native DX12 backend needs the discard-rename path for intra-frame re-uploads.
      match res.InstancePaletteColorVertexBuffer with
      | ValueNone ->
        let vb =
          new DynamicVertexBuffer(
            gd,
            typeof<VertexInstanceWorldPaletteColor>,
            chunkCount,
            BufferUsage.WriteOnly
          )

        res.InstancePaletteColorVertexBuffer <- ValueSome vb
      | ValueSome vb when vb.VertexCount < chunkCount ->
        vb.Dispose()

        let vb' =
          new DynamicVertexBuffer(
            gd,
            typeof<VertexInstanceWorldPaletteColor>,
            chunkCount,
            BufferUsage.WriteOnly
          )

        res.InstancePaletteColorVertexBuffer <- ValueSome vb'
      | _ -> ()

      let instVB =
        match res.InstancePaletteColorVertexBuffer with
        | ValueSome vb -> vb
        | ValueNone -> Unchecked.defaultof<DynamicVertexBuffer> // unreachable (created above)

      instVB.SetData(res.InstancePaletteColorStaging, 0, chunkCount)
      instVB
    | ValueNone ->
      if res.InstancePaletteStaging.Length < chunkCount then
        res.InstancePaletteStaging <-
          Array.zeroCreate<VertexInstanceWorldPalette> chunkCount

      for i = 0 to chunkCount - 1 do
        // PaletteOffset is chunk-local (see the colored branch above).
        res.InstancePaletteStaging[i] <-
          VertexInstanceWorldPalette.Create(
            transforms[chunkStart + i],
            float32 i
          )

      // Must stay a DynamicVertexBuffer (see the colored branch above).
      match res.InstancePaletteVertexBuffer with
      | ValueNone ->
        let vb =
          new DynamicVertexBuffer(
            gd,
            typeof<VertexInstanceWorldPalette>,
            chunkCount,
            BufferUsage.WriteOnly
          )

        res.InstancePaletteVertexBuffer <- ValueSome vb
      | ValueSome vb when vb.VertexCount < chunkCount ->
        vb.Dispose()

        let vb' =
          new DynamicVertexBuffer(
            gd,
            typeof<VertexInstanceWorldPalette>,
            chunkCount,
            BufferUsage.WriteOnly
          )

        res.InstancePaletteVertexBuffer <- ValueSome vb'
      | _ -> ()

      let instVB =
        match res.InstancePaletteVertexBuffer with
        | ValueSome vb -> vb
        | ValueNone -> Unchecked.defaultof<DynamicVertexBuffer> // unreachable (created above)

      instVB.SetData(res.InstancePaletteStaging, 0, chunkCount)
      instVB

  /// <summary>
  /// Handles <c>DrawAnimatedModelInstanced</c>: one instanced draw per mesh part per chunk for
  /// N posed instances of the same animated model. On DX11/Vulkan bone palettes ride RGBA32F
  /// palette textures (one per chunk, ≤ <c>PaletteTexture.MaxHeight</c> instances per chunk —
  /// staged + uploaded once per frame via the shared
  /// <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.PaletteChunkCache"/>) sampled by the
  /// <c>SkinnedInstanced</c>/<c>SkinnedInstancedColor</c> techniques; on DX12 (no working
  /// vertex texture fetch) palettes ride the <c>bonePaletteGroup</c> constant array via the
  /// <c>SkinnedInstancedGrouped</c>/<c>SkinnedInstancedGroupedColor</c> techniques, chunked
  /// to <c>PaletteGroup.MaxMatrices / boneCount</c> instances per group. <c>matModel</c>
  /// carries each mesh's parent-bone world and the per-instance world arrives on stream 1.
  /// Non-skinned parts (no bone channels on stream 0) draw through the plain
  /// <c>Instanced</c>/<c>InstancedColor</c> techniques — the extra stream-1 elements go unread.
  /// Materials resolve per part via <paramref name="matOverride"/> like
  /// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.PbrShading.drawAnimatedModel"/>.
  /// With a <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.SkinnedInstancedTarget.UserEffectTarget"/>
  /// the skinned parts shade through the user effect (scene data uploaded by name, palette
  /// texture via <c>paletteTex</c>/<c>paletteTexSize</c>); non-skinned parts still use the
  /// framework PBR effect. Falls back to per-instance draws through the existing
  /// <c>Skinned</c> path on OpenGL (no vertex texture fetch) and for user effects on DX12
  /// (the grouped-uniform contract is framework-PBR-only) — per-instance colors then
  /// modulate the resolved material's albedo/opacity.
  /// </summary>
  let drawAnimatedModelInstanced
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      frame: byref<ForwardFrame>,
      res: PbrResources,
      target: SkinnedInstancedTarget,
      model: Model,
      transforms: Matrix[],
      palettes: Matrix[],
      matOverride: MaterialOverride voption,
      colors: Color[] voption,
      instanceCount: int,
      boneCount: int
    ) =
    // Clamp to the transforms array: an instanceCount larger than the buffer would index
    // out of range when staging per-instance rows.
    let transformCount = if isNull transforms then 0 else transforms.Length
    let paletteLen = if isNull palettes then 0 else palettes.Length
    let count = min instanceCount transformCount

    if count > 0 && paletteLen > 0 && boneCount > 0 then
      // Per-instance fallback: OpenGL (no vertex texture fetch). DX12 uses the
      // grouped-uniform path (SkinnedInstancedGrouped via the isolated ForwardPbrGrouped
      // effect — the main effect's grouped params are dropped by DX12 mgfx reflection)
      // unless the skeleton exceeds the grouped-uniform budget
      // (PaletteGroup.MaxMatrices) — more bones than a group holds can't ride the
      // constant array, so DX12 falls back to per-instance draws too.
      // User effects on DX12 still fall back to per-instance (a user effect's
      // SkinnedInstanced technique expects the VS-texture contract — broken on DX12;
      // the grouped-uniform path is framework-PBR-only).
      let perInstanceFallback =
        isOpenGLBackend()
        || (isDirectX12Backend()
            && (boneCount > PaletteGroup.MaxMatrices
                || (match target with
                    | UserEffectTarget _ -> true
                    | PbrTarget -> false)))

      if perInstanceFallback then
        // ── Per-instance draws through the existing Skinned path, slicing each
        // instance's palette out of the flat array. Per-instance colors modulate
        // the material (this path has no per-instance color channel).
        if res.BoneSliceScratch.Length < boneCount then
          res.BoneSliceScratch <- Array.zeroCreate<Matrix> boneCount

        let slice = res.BoneSliceScratch

        for i = 0 to count - 1 do
          Array.Copy(palettes, i * boneCount, slice, 0, boneCount)

          let tint =
            match colors with
            | ValueSome cs when not(isNull cs) && i < cs.Length ->
              ValueSome cs[i]
            | _ -> ValueNone

          drawAnimatedModelCore(
            gd,
            &state,
            &frame,
            res,
            model,
            transforms[i],
            slice,
            matOverride,
            tint
          )
      else
        // Bone transforms give each mesh its parent-bone world (the matModel the
        // SkinnedInstanced VS composes with the per-instance world on stream 1).
        let modelBoneCount = model.Bones.Count

        if res.BoneTransforms.Length < modelBoneCount then
          res.BoneTransforms <- Array.zeroCreate<Matrix> modelBoneCount

        model.CopyAbsoluteBoneTransformsTo(res.BoneTransforms)
        let viewProj = state.View * state.Projection

        // The framework PBR effect is always prepared: it shades the PbrTarget path and
        // the non-skinned parts under a user effect. Frame-global uniforms don't depend
        // on the mesh — set once per draw, not per mesh.
        if ensureEffect(gd, res) then
          match res.Effect, res.Params with
          | ValueSome e, ValueSome p ->
            PbrUniforms.setMatrix p.Matrix.ViewProj viewProj

            PbrUniforms.setVec3 p.Matrix.CameraPos state.CurrentCamera.Position

            if res.LightsDirty then
              PbrUniforms.uploadLights(
                &p,
                frame.Lights,
                frame.PointShadowSlots,
                frame.SpotShadowSlots
              )

              res.LightsDirty <- false
          | _ -> ()

        // DX12: prepare the isolated grouped effect (ForwardPbrGrouped.fx) with the
        // same frame-global uniforms. The grouped effect has its own uniform handles
        // — the main effect's bonePaletteGroup params are null on DX12 (dropped by
        // mgfx reflection), so the grouped effect is the only one that can receive
        // bone palette data. Lights are uploaded unconditionally (once per draw call,
        // not per part — negligible cost vs. the per-instance fallback it replaces).
        if isDirectX12Backend() && ensureGroupedEffect(gd, res) then
          match res.GroupedParams with
          | ValueSome gp ->
            PbrUniforms.setMatrix gp.Matrix.ViewProj viewProj

            PbrUniforms.setVec3 gp.Matrix.CameraPos state.CurrentCamera.Position

            PbrUniforms.uploadLights(
              &gp,
              frame.Lights,
              frame.PointShadowSlots,
              frame.SpotShadowSlots
            )

            match frame.Shadows with
            | ValueSome s ->
              PbrUniforms.setInt
                gp.Shadow.DirLightCastsShadows
                (if s.DirLightCastsShadows then 1 else 0)

              PbrUniforms.setMatrixArray gp.Shadow.ShadowViewProjs s.ViewProjs
              PbrUniforms.setVec4Array gp.Shadow.ShadowUVOffsets s.UVOffsets
              PbrUniforms.setFloatArray gp.Shadow.ShadowBiases s.Biases

              PbrUniforms.setVec2
                gp.Shadow.ShadowTexelSize
                (Vector2(s.TexelSize, s.TexelSize))

              if not(obj.ReferenceEquals(gp.Shadow.ShadowAtlasTex, null)) then
                gp.Shadow.ShadowAtlasTex.SetValue(s.Atlas)
            | ValueNone -> PbrUniforms.setInt gp.Shadow.DirLightCastsShadows 0
          | ValueNone -> ()

        // A user-effect target selects its SkinnedInstanced technique once around the
        // whole draw (restored afterwards, so it can't leak into subsequent draws in
        // the same scope).
        let savedUserTechnique =
          match target with
          | UserEffectTarget(effect, technique) ->
            let saved = effect.CurrentTechnique
            effect.CurrentTechnique <- technique
            saved
          | PbrTarget -> null

        try
          // Chunk driver for both instanced paths: (chunkStart, chunkCount,
          // paletteTex) triples. DX11/Vulkan: real palette-texture chunks from the
          // shared per-frame cache (staged + uploaded once across both passes).
          // DX12: uniform GROUPS of PaletteGroup.MaxMatrices / boneCount instances
          // with a null paletteTex — palettes ride the bonePaletteGroup constant
          // array (no working vertex texture fetch on DX12).
          let chunks, chunkTotal =
            if isDirectX12Backend() then
              // boneCount <= MaxMatrices here — larger skeletons took the
              // per-instance fallback above.
              let needed = PaletteGroup.groupCountFor count boneCount

              if res.GroupChunkScratch.Length < needed then
                res.GroupChunkScratch <- Array.zeroCreate needed

              (res.GroupChunkScratch,
               PaletteGroup.planGroups count boneCount res.GroupChunkScratch)
            else
              let obtained =
                res.PaletteChunks.Obtain(gd, palettes, boneCount, count)

              (obtained, obtained.Length)

          // Per-part invariants hoisted out of the chunk loop: technique,
          // material, and matModel don't vary across chunks. PerMesh material
          // resolvers index the model's parts in pipeline iteration order — the
          // resolver now runs once per part per command instead of per chunk.
          let partInfos = res.SkinnedInstancedPartInfos
          partInfos.Clear()
          let mutable partIndex = 0

          for mesh in model.Meshes do
            let world = res.BoneTransforms[mesh.ParentBone.Index]

            for part in mesh.MeshParts do
              let isSkinned =
                match part.Effect with
                | :? SkinnedEffect -> true
                | _ -> false

              let mat =
                match matOverride with
                | ValueNone -> Material3D.fromModelMeshPart part
                | ValueSome(MaterialOverride.All m) -> m
                | ValueSome(MaterialOverride.PerMesh f) -> f partIndex

              partIndex <- partIndex + 1

              if part.PrimitiveCount > 0 then
                // The grouped effect exists only on DX12 (paletteTex is null
                // for every chunk there, non-null everywhere else).
                let useGrouped = isSkinned && isDirectX12Backend()

                let techniqueName =
                  match isSkinned, colors, useGrouped with
                  | true, ValueSome _, true -> "SkinnedInstancedGroupedColor"
                  | true, ValueNone, true -> "SkinnedInstancedGrouped"
                  | true, ValueSome _, false -> "SkinnedInstancedColor"
                  | true, ValueNone, false -> "SkinnedInstanced"
                  | false, ValueSome _, _ -> "InstancedColor"
                  | false, ValueNone, _ -> "Instanced"

                let technique =
                  match
                    (if useGrouped then res.GroupedEffect else res.Effect)
                  with
                  | ValueSome e -> e.Techniques[techniqueName]
                  | ValueNone -> null

                let mutable t = world
                let mutable inv = Matrix.Identity
                Matrix.Invert(&t, &inv) |> ignore

                partInfos.Add(
                  {
                    Part = part
                    IsSkinned = isSkinned
                    World = world
                    NormalMatrix = Matrix.Transpose inv
                    Mat = mat
                    MatKey = materialKey &mat
                    UseGrouped = useGrouped
                    Technique = technique
                  }
                )

          // Draw units: one per original part — or one per MERGED part group when
          // the model has mergeable parts (MergedModelParts) and every source part
          // of the group resolved to the same MaterialKey for this command. A
          // non-uniform group (e.g. a PerMesh override splitting it) falls back to
          // per-part units for this command. User-effect targets never merge —
          // per-part scene uploads are their contract.
          let units = res.SkinnedInstancedUnits
          units.Clear()

          let addPartUnit(info: SkinnedInstancedPartInfo) =
            units.Add {
              VB = info.Part.VertexBuffer
              IB = info.Part.IndexBuffer
              VertexOffset = info.Part.VertexOffset
              StartIndex = info.Part.StartIndex
              PrimitiveCount = info.Part.PrimitiveCount
              Info = info
              SourcePart = ValueSome info.Part
            }

          match
            (match target with
             | PbrTarget -> MergedModelParts.tryGet(gd, model)
             | UserEffectTarget _ -> ValueNone)
          with
          | ValueSome merged ->
            res.PartMergedMap.Clear()
            res.PartInfoIndex.Clear()

            for mp in merged do
              for sp in mp.SourceParts do
                res.PartMergedMap[sp] <- mp

            for i = 0 to partInfos.Count - 1 do
              res.PartInfoIndex[partInfos[i].Part] <- i

            if res.SkinnedInstancedHandled.Length < partInfos.Count then
              res.SkinnedInstancedHandled <- Array.zeroCreate partInfos.Count
            else
              Array.Clear(res.SkinnedInstancedHandled, 0, partInfos.Count)

            let handled = res.SkinnedInstancedHandled

            for i = 0 to partInfos.Count - 1 do
              if not handled[i] then
                let info = partInfos[i]

                match res.PartMergedMap.TryGetValue info.Part with
                | true, mp ->
                  // Group members all share parent bone / declaration / skinned
                  // flag by construction, so the first member's info (world,
                  // technique) represents the group; only materials can split it.
                  let mutable uniform = true

                  for sp in mp.SourceParts do
                    match res.PartInfoIndex.TryGetValue sp with
                    | true, memberIdx ->
                      handled[memberIdx] <- true

                      if partInfos[memberIdx].MatKey <> info.MatKey then
                        uniform <- false
                    | _ -> ()

                  if uniform then
                    units.Add {
                      VB = mp.VertexBuffer
                      IB = mp.IndexBuffer
                      VertexOffset = 0
                      StartIndex = 0
                      PrimitiveCount = mp.PrimitiveCount
                      Info = info
                      SourcePart = ValueNone
                    }
                  else
                    for sp in mp.SourceParts do
                      match res.PartInfoIndex.TryGetValue sp with
                      | true, memberIdx -> addPartUnit partInfos[memberIdx]
                      | _ -> ()
                | _ -> addPartUnit info
          | ValueNone ->
            for info in partInfos do
              addPartUnit info

          let mutable chunkIdx = 0

          while chunkIdx < chunkTotal do
            let struct (chunkStart, chunkCount, paletteTex) = chunks[chunkIdx]

            let instVB =
              stagePaletteInstanceVB(
                gd,
                res,
                transforms,
                colors,
                chunkStart,
                chunkCount
              )

            // Per-chunk palette storage (effect params are effect-global — set
            // once per chunk, not per part). On DX11/Vulkan the palette texture
            // uploads to the main effect; on DX12 the bone palette array uploads
            // to the isolated grouped effect (the main effect's grouped params
            // are null on DX12 — dropped by mgfx reflection).
            if isNull paletteTex then
              match res.GroupedParams with
              | ValueSome gp ->
                if
                  res.GroupPaletteScratch.Length < PaletteGroup.MaxMatrices
                then
                  res.GroupPaletteScratch <-
                    Array.zeroCreate PaletteGroup.MaxMatrices

                Array.Copy(
                  palettes,
                  chunkStart * boneCount,
                  res.GroupPaletteScratch,
                  0,
                  chunkCount * boneCount
                )

                PbrUniforms.setMatrixArray
                  gp.Matrix.BonePaletteGroup
                  res.GroupPaletteScratch

                PbrUniforms.setInt gp.Matrix.GroupBoneCount boneCount
              | ValueNone -> ()
            else
              match res.Params with
              | ValueSome p ->
                PbrUniforms.setTexture p.Matrix.PaletteTex paletteTex

                PbrUniforms.setVec2
                  p.Matrix.PaletteTexSize
                  (Vector2(float32(boneCount * 4), float32 chunkCount))
              | ValueNone -> ()



            for unit in units do
              let info = unit.Info

              match target with
              | UserEffectTarget(effect, _) when info.IsSkinned ->
                // User effect opted in: it inherits scene DATA by name (not the PBR
                // shader). matModel carries the mesh parent-bone world; the instance
                // world arrives on stream 1 and the bone palette via
                // paletteTex/paletteTexSize — the contract the built-in
                // SkinnedInstanced VS implements.
                SceneUpload.uploadToEffect(
                  gd,
                  effect,
                  state.View,
                  state.Projection,
                  state.CurrentCamera.Position,
                  info.World,
                  info.NormalMatrix,
                  frame.Lights,
                  frame.Shadows,
                  ValueNone,
                  info.Mat,
                  frame.Time
                )

                match effect.Parameters["paletteTex"] with
                | null -> ()
                | pp -> pp.SetValue paletteTex

                match effect.Parameters["paletteTexSize"] with
                | null -> ()
                | pp ->
                  pp.SetValue(
                    Vector2(float32(boneCount * 4), float32 chunkCount)
                  )

                gd.SetVertexBuffers(
                  VertexBufferBinding(unit.VB, 0, 0),
                  VertexBufferBinding(instVB, 0, 1)
                )

                gd.Indices <- unit.IB

                for pass in effect.CurrentTechnique.Passes do
                  pass.Apply()

                  gd.DrawInstancedPrimitives(
                    PrimitiveType.TriangleList,
                    unit.VertexOffset,
                    unit.StartIndex,
                    unit.PrimitiveCount,
                    chunkCount
                  )
              | _ ->
                // Framework PBR effect: SkinnedInstanced(+Color) — or the
                // grouped-uniform SkinnedInstancedGrouped(+Color) on DX12 —
                // for skinned parts; Instanced(+Color) for the rest (their
                // stream 0 has no bone channels; the extra stream-1 elements
                // are simply unread). On DX12 the grouped-uniform skinned path
                // uses the isolated ForwardPbrGrouped effect (the main effect's
                // bonePaletteGroup params are null on DX12).
                let drawEffect, drawParams =
                  if info.UseGrouped then
                    res.GroupedEffect, res.GroupedParams
                  else
                    res.Effect, res.Params

                match drawEffect, drawParams with
                | ValueSome e, ValueSome p ->
                  e.CurrentTechnique <- info.Technique

                  PbrUniforms.setMatrix p.Matrix.MatModel info.World

                  // The grouped effect has separate uniform handles from the
                  // main effect, so the HasLastMaterial short-circuit (keyed
                  // on the main effect's uploads) doesn't apply — always upload
                  // material + textures on the grouped path.
                  let mat = info.Mat

                  if info.UseGrouped then
                    PbrUniforms.uploadMaterial(&p, &mat)
                    PbrUniforms.bindTextures(&p, &mat, whiteTex res)
                  else if
                    not res.HasLastMaterial || info.MatKey <> res.LastKey
                  then
                    PbrUniforms.uploadMaterial(&p, &mat)
                    PbrUniforms.bindTextures(&p, &mat, whiteTex res)
                    res.LastKey <- info.MatKey
                    res.HasLastMaterial <- true

                  gd.SetVertexBuffers(
                    VertexBufferBinding(unit.VB, 0, 0),
                    VertexBufferBinding(instVB, 0, 1)
                  )

                  gd.Indices <- unit.IB

                  // The Effect save/swap only applies to original parts (a merged
                  // unit has no ModelMeshPart of its own).
                  let saved =
                    match unit.SourcePart with
                    | ValueSome part ->
                      let s = part.Effect
                      part.Effect <- e
                      s
                    | ValueNone -> null

                  try
                    for pass in e.CurrentTechnique.Passes do
                      pass.Apply()

                      gd.DrawInstancedPrimitives(
                        PrimitiveType.TriangleList,
                        unit.VertexOffset,
                        unit.StartIndex,
                        unit.PrimitiveCount,
                        chunkCount
                      )
                  finally
                    match unit.SourcePart with
                    | ValueSome part -> part.Effect <- saved
                    | ValueNone -> ()
                | _ -> ()

            chunkIdx <- chunkIdx + 1
        finally
          match target with
          | UserEffectTarget(effect, _) ->
            effect.CurrentTechnique <- savedUserTechnique
          | PbrTarget -> ()

  // ── User-effect scope shading: uploads scene data to an arbitrary effect via SceneUpload. ──

  /// <summary>
  /// Shades a draw with a user-supplied <c>effect</c>: uploads the gathered scene data (matrices +
  /// material + lights + bones) via <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.SceneUpload.uploadToEffect"/>
  /// (name-resolved; absent uniforms skipped), then draws through the effect's own CurrentTechnique.
  /// The effect inherits scene DATA, not the PBR shader (v2 §3). DrawInstanced under a user scope is
  /// shaded by the user effect when it exposes an <c>Instanced</c> technique (the instancing opt-in);
  /// otherwise it falls back to the PBR instanced path. See docs/graphics3d/instancing.md.
  /// </summary>
  let shadeWithEffect
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      frame: byref<ForwardFrame>,
      res: PbrResources,
      effect: Effect,
      draw: Command3D
    ) =
    let camPos = state.CurrentCamera.Position

    let normalMatrixOf(world: Matrix) =
      let mutable t = world
      let mutable inv = Matrix.Identity
      Matrix.Invert(&t, &inv) |> ignore
      Matrix.Transpose inv

    // Techniques are stable for this effect — resolve once per draw instead of per part.
    // The name indexer returns null when absent (no enumerator/closure allocation).
    let standardTech =
      match effect.Techniques["Standard"] with
      | null -> None
      | t -> Some t

    let skinnedTech =
      match effect.Techniques["Skinned"] with
      | null -> None
      | t -> Some t

    match draw with
    | Command3D.DrawPrimitive(mesh, transform, material) ->
      SceneUpload.uploadToEffect(
        gd,
        effect,
        state.View,
        state.Projection,
        camPos,
        transform,
        normalMatrixOf transform,
        frame.Lights,
        frame.Shadows,
        ValueNone,
        material,
        frame.Time
      )

      mesh.Draw(gd, effect)

    | Command3D.DrawModel(model, transform) ->
      let boneCount = model.Bones.Count

      if res.BoneTransforms.Length < boneCount then
        res.BoneTransforms <- Array.zeroCreate<Matrix> boneCount

      model.CopyAbsoluteBoneTransformsTo(res.BoneTransforms)

      for mesh in model.Meshes do
        let world = res.BoneTransforms[mesh.ParentBone.Index] * transform

        for part in mesh.MeshParts do
          let mat = Material3D.fromModelMeshPart part

          // DrawModel binds the Standard technique (matches the PBR handleDrawModel — a static
          // model draw doesn't upload a bone palette, even if the parts are skinned).
          match standardTech with
          | Some st -> effect.CurrentTechnique <- st
          | None -> ()

          SceneUpload.uploadToEffect(
            gd,
            effect,
            state.View,
            state.Projection,
            camPos,
            world,
            normalMatrixOf world,
            frame.Lights,
            frame.Shadows,
            ValueNone,
            mat,
            frame.Time
          )

          let saved = part.Effect
          part.Effect <- effect

          try
            drawPart(gd, part)
          finally
            part.Effect <- saved

    | Command3D.DrawModelWith(model, transform, matOverride) ->
      let boneCount = model.Bones.Count

      if res.BoneTransforms.Length < boneCount then
        res.BoneTransforms <- Array.zeroCreate<Matrix> boneCount

      model.CopyAbsoluteBoneTransformsTo(res.BoneTransforms)

      let mutable partIndex = 0

      for mesh in model.Meshes do
        let world = res.BoneTransforms[mesh.ParentBone.Index] * transform

        for part in mesh.MeshParts do
          let mat =
            match matOverride with
            | MaterialOverride.All m -> m
            | MaterialOverride.PerMesh f -> f partIndex

          partIndex <- partIndex + 1

          // DrawModel binds the Standard technique (matches the PBR handleDrawModel — a static
          // model draw doesn't upload a bone palette, even if the parts are skinned).
          match standardTech with
          | Some st -> effect.CurrentTechnique <- st
          | None -> ()

          SceneUpload.uploadToEffect(
            gd,
            effect,
            state.View,
            state.Projection,
            camPos,
            world,
            normalMatrixOf world,
            frame.Lights,
            frame.Shadows,
            ValueNone,
            mat,
            frame.Time
          )

          let saved = part.Effect
          part.Effect <- effect

          try
            drawPart(gd, part)
          finally
            part.Effect <- saved

    | Command3D.DrawAnimatedModel(model, transform, bones) ->
      let boneCount = model.Bones.Count

      if res.BoneTransforms.Length < boneCount then
        res.BoneTransforms <- Array.zeroCreate<Matrix> boneCount

      model.CopyAbsoluteBoneTransformsTo(res.BoneTransforms)
      let bonePaletteScratch = frame.BonePaletteScratch
      let palCount = min bones.Length bonePaletteScratch.Length

      for i = 0 to palCount - 1 do
        bonePaletteScratch[i] <- bones[i]

      for i = palCount to bonePaletteScratch.Length - 1 do
        bonePaletteScratch[i] <- Matrix.Identity

      for mesh in model.Meshes do
        let world = res.BoneTransforms[mesh.ParentBone.Index] * transform

        for part in mesh.MeshParts do
          let mat = Material3D.fromModelMeshPart part

          // Select the technique by part kind, matching the PBR path: a SkinnedEffect part
          // (the content-pipeline signal for BLENDINDICES0/BLENDWEIGHT0) needs the effect's
          // Skinned technique so VS_Skinning + boneMatrices apply. A user effect without a
          // Skinned technique falls back to its CurrentTechnique — safe, just unskinned.
          let isSkinned =
            match part.Effect with
            | :? SkinnedEffect -> true
            | _ -> false

          if isSkinned then
            match skinnedTech with
            | Some sk -> effect.CurrentTechnique <- sk
            | None -> ()
          else
            match standardTech with
            | Some st -> effect.CurrentTechnique <- st
            | None -> ()

          SceneUpload.uploadToEffect(
            gd,
            effect,
            state.View,
            state.Projection,
            camPos,
            world,
            normalMatrixOf world,
            frame.Lights,
            frame.Shadows,
            ValueSome bonePaletteScratch,
            mat,
            frame.Time
          )

          let saved = part.Effect
          part.Effect <- effect

          try
            drawPart(gd, part)
          finally
            part.Effect <- saved

    | Command3D.DrawAnimatedModelWith(model, transform, bones, matOverride) ->
      let boneCount = model.Bones.Count

      if res.BoneTransforms.Length < boneCount then
        res.BoneTransforms <- Array.zeroCreate<Matrix> boneCount

      model.CopyAbsoluteBoneTransformsTo(res.BoneTransforms)
      let bonePaletteScratch = frame.BonePaletteScratch
      let palCount = min bones.Length bonePaletteScratch.Length

      for i = 0 to palCount - 1 do
        bonePaletteScratch[i] <- bones[i]

      for i = palCount to bonePaletteScratch.Length - 1 do
        bonePaletteScratch[i] <- Matrix.Identity

      let mutable partIndex = 0

      for mesh in model.Meshes do
        let world = res.BoneTransforms[mesh.ParentBone.Index] * transform

        for part in mesh.MeshParts do
          let mat =
            match matOverride with
            | MaterialOverride.All m -> m
            | MaterialOverride.PerMesh f -> f partIndex

          partIndex <- partIndex + 1

          // Select the technique by part kind, matching the PBR path: a SkinnedEffect part
          // (the content-pipeline signal for BLENDINDICES0/BLENDWEIGHT0) needs the effect's
          // Skinned technique so VS_Skinning + boneMatrices apply. A user effect without a
          // Skinned technique falls back to its CurrentTechnique — safe, just unskinned.
          let isSkinned =
            match part.Effect with
            | :? SkinnedEffect -> true
            | _ -> false

          if isSkinned then
            match skinnedTech with
            | Some sk -> effect.CurrentTechnique <- sk
            | None -> ()
          else
            match standardTech with
            | Some st -> effect.CurrentTechnique <- st
            | None -> ()

          SceneUpload.uploadToEffect(
            gd,
            effect,
            state.View,
            state.Projection,
            camPos,
            world,
            normalMatrixOf world,
            frame.Lights,
            frame.Shadows,
            ValueSome bonePaletteScratch,
            mat,
            frame.Time
          )

          let saved = part.Effect
          part.Effect <- effect

          try
            drawPart(gd, part)
          finally
            part.Effect <- saved

    | Command3D.DrawInstanced(mesh, transforms, colors, material, count) ->
      // Does the user effect opt into instancing? An effect exposing an `Instanced` technique
      // (the convention ForwardPbr.fx and Instanced.fx already use) shades the instances directly;
      // one that doesn't falls back to the PBR instanced path (see remarks). The probe result —
      // including the technique handle — is memoized per effect. Colored draws bind the
      // VertexInstanceWorldColor stream: the user shader may declare
      // `float4 InstanceColor : TEXCOORD5` to read the per-instance color.
      match res.TryInstancedTechnique(effect) with
      | null ->
        // Effect didn't opt in — fall back to the PBR instanced path (see remarks).
        drawInstanced(
          gd,
          &state,
          &frame,
          res,
          mesh,
          transforms,
          colors,
          material,
          count
        )
      | instancedTech ->
        let struct (instanceCount, _instVB) =
          match colors with
          | ValueNone -> stageInstanceData(gd, res, mesh, transforms, count)
          | ValueSome cs ->
            stageInstanceColorData(gd, res, mesh, transforms, cs, count)

        if instanceCount > 0 then
          gd.Indices <- mesh.Indices

          // Restore the previous technique afterwards: leaving `Instanced` current would leak
          // into subsequent non-instanced draws in the same scope, which would read
          // per-instance rows from the stale stream-1 buffer.
          let savedTechnique = effect.CurrentTechnique
          effect.CurrentTechnique <- instancedTech

          try
            // matModel is identity: the per-instance world transform arrives on stream 1
            // (VertexInstanceWorld rows), so a shader that still declares matModel sees a benign value.
            SceneUpload.uploadToEffect(
              gd,
              effect,
              state.View,
              state.Projection,
              camPos,
              Matrix.Identity,
              Matrix.Identity,
              frame.Lights,
              frame.Shadows,
              ValueNone,
              material,
              frame.Time
            )

            for pass in instancedTech.Passes do
              pass.Apply()

              gd.DrawInstancedPrimitives(
                PrimitiveType.TriangleList,
                0,
                0,
                mesh.PrimitiveCount,
                instanceCount
              )
          finally
            effect.CurrentTechnique <- savedTechnique

    | Command3D.DrawAnimatedModelInstanced(model,
                                           transforms,
                                           palettes,
                                           matOverride,
                                           colors,
                                           count,
                                           boneCount) ->
      // Does the user effect opt into skinned instancing? An effect exposing a
      // `SkinnedInstanced` technique (the convention ForwardPbr.fx uses) shades the skinned
      // parts directly — the bone palette reaches it via the paletteTex/paletteTexSize
      // uniforms (name-resolved, absent = skipped) and the per-instance rows on stream 1.
      // The probe result is memoized per effect. Effects without the technique — and the
      // OpenGL backend, where vertex texture fetch doesn't exist — fall back to the
      // framework PBR skinned-instanced path.
      let target =
        if isOpenGLBackend() then
          PbrTarget
        else
          match res.TrySkinnedInstancedTechnique(effect) with
          | null -> PbrTarget
          | tech -> UserEffectTarget(effect, tech)

      drawAnimatedModelInstanced(
        gd,
        &state,
        &frame,
        res,
        target,
        model,
        transforms,
        palettes,
        matOverride,
        colors,
        count,
        boneCount
      )

    | _ -> ()
