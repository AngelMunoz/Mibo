namespace Mibo.Elmish.Graphics3D.Pipelines

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
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
[<Struct>]
type ForwardFrame = {
  /// <summary>The frame's accumulated lights.</summary>
  Lights: LightBuffers
  /// <summary>The pooled bone-palette scratch (shared with the shadow pass) for skinned draws.</summary>
  BonePaletteScratch: Matrix[]
  /// <summary>Per-light shadow atlas slots (-1 = no shadow), indexed by PointLights position.</summary>
  PointShadowSlots: int[]
  /// <summary>Per-light shadow atlas slots (-1 = no shadow), indexed by SpotLights position.</summary>
  SpotShadowSlots: int[]
  /// <summary>The frame's shadow pass output — ValueNone when no shadow-casting light / missing DepthShadow.fx.
  /// The user-effect scope (<see cref="M:Mibo.Elmish.Graphics3D.Pipelines.PbrShading.shadeWithEffect"/>)
  /// uploads these uniforms by name so a custom effect can opt into shadow sampling.</summary>
  Shadows: ShadowResult voption
  /// <summary>The total elapsed game time, in seconds (<c>Game.TotalGameTime.TotalSeconds</c>). Uploaded
  /// as the <c>time</c> uniform so an animated shader (water ripples, flowing textures, pulsing emissive)
  /// has a clock to read — the only animation input the scene-data contract provides.</summary>
  Time: float32
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

  /// <summary>MaterialKey short-circuit: whether the last draw's material is still current.</summary>
  member val HasLastMaterial = false with get, set

  /// <summary>The last draw's MaterialKey (valid when HasLastMaterial).</summary>
  member val LastKey: MaterialKey =
    Unchecked.defaultof<MaterialKey> with get, set

  // Reused each frame to avoid per-frame allocation. Sized generously; grows if a larger model is
  // seen. A raw array (not ResizeArray) so we can pass it directly to CopyAbsoluteBoneTransformsTo.
  member val BoneTransforms = Array.zeroCreate<Matrix> 64 with get, set

/// <summary>The extracted PBR draw handlers + the user-effect scope shading path.</summary>
module internal PbrShading =

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

        PbrUniforms.uploadLights(
          &p,
          frame.Lights,
          frame.PointShadowSlots,
          frame.SpotShadowSlots
        )

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

        let mutable partIndex = 0

        for mesh in model.Meshes do
          let world = res.BoneTransforms[mesh.ParentBone.Index] * transform
          let mutable t = world
          let mutable inv = Matrix.Identity
          Matrix.Invert(&t, &inv) |> ignore

          PbrUniforms.setMatrix p.Matrix.MatModel world

          PbrUniforms.setMatrix
            p.Matrix.ViewProj
            (state.View * state.Projection)

          PbrUniforms.setMatrix p.Matrix.NormalMatrix (Matrix.Transpose inv)
          PbrUniforms.setVec3 p.Matrix.CameraPos state.CurrentCamera.Position

          PbrUniforms.uploadLights(
            &p,
            frame.Lights,
            frame.PointShadowSlots,
            frame.SpotShadowSlots
          )

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

        PbrUniforms.uploadLights(
          &p,
          frame.Lights,
          frame.PointShadowSlots,
          frame.SpotShadowSlots
        )

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
  /// Handles <c>DrawInstanced</c>: native hardware instancing via two vertex streams (mesh + per-
  /// instance VertexInstanceWorld rows). Prefers the PBR Instanced technique; falls back to minimal
  /// Instanced.fx (flat albedo + 1 directional) when the PBR effect can't load.
  /// </summary>
  let drawInstanced
    (
      gd: GraphicsDevice,
      state: byref<ForwardState>,
      frame: byref<ForwardFrame>,
      res: PbrResources,
      mesh: PrimitiveMesh,
      transforms: Matrix[],
      material: Material3D,
      instanceCount: int
    ) =
    // Clamp to the transforms array: an instanceCount larger than the buffer
    // would index out of range when staging per-instance world matrices.
    let instanceCount =
      min instanceCount (if isNull transforms then 0 else transforms.Length)

    if instanceCount <= 0 then
      ()
    else
      if res.InstanceStaging.Length < instanceCount then
        res.InstanceStaging <-
          Array.zeroCreate<VertexInstanceWorld> instanceCount

      for i = 0 to instanceCount - 1 do
        res.InstanceStaging[i] <- VertexInstanceWorld.Create transforms[i]

      match res.InstanceVertexBuffer with
      | ValueNone ->
        let vb =
          new VertexBuffer(
            gd,
            typeof<VertexInstanceWorld>,
            instanceCount,
            BufferUsage.WriteOnly
          )

        res.InstanceVertexBuffer <- ValueSome vb
      | ValueSome vb when vb.VertexCount < instanceCount ->
        vb.Dispose()

        let vb' =
          new VertexBuffer(
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

      gd.Indices <- mesh.Indices
      let viewProj = state.View * state.Projection

      if ensureEffect(gd, res) then
        match res.Effect, res.Params with
        | ValueSome e, ValueSome p ->
          e.CurrentTechnique <- e.Techniques["Instanced"]
          PbrUniforms.setMatrix p.Matrix.ViewProj viewProj
          PbrUniforms.setVec3 p.Matrix.CameraPos state.CurrentCamera.Position
          // Instanced draws always upload the material (one material across all instances).
          PbrUniforms.uploadMaterial(&p, &material)
          PbrUniforms.bindTextures(&p, &material, whiteTex res)

          PbrUniforms.uploadLights(
            &p,
            frame.Lights,
            frame.PointShadowSlots,
            frame.SpotShadowSlots
          )

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

          for pass in effect.CurrentTechnique.Passes do
            pass.Apply()

            gd.DrawInstancedPrimitives(
              PrimitiveType.TriangleList,
              0,
              0,
              mesh.PrimitiveCount,
              instanceCount
            )

  // ── User-effect scope shading: uploads scene data to an arbitrary effect via SceneUpload. ──

  /// <summary>
  /// Shades a draw with a user-supplied <c>effect</c>: uploads the gathered scene data (matrices +
  /// material + lights + bones) via <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.SceneUpload.uploadToEffect"/>
  /// (name-resolved; absent uniforms skipped), then draws through the effect's own CurrentTechnique.
  /// The effect inherits scene DATA, not the PBR shader (v2 §3). DrawInstanced under a user scope
  /// falls back to the PBR instanced path (instancing needs a vertex stream a generic effect won't declare).
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

    // Techniques are stable for this effect — resolve once instead of per part per frame.
    let standardTech =
      effect.Techniques |> Seq.tryFind(fun t -> t.Name = "Standard")

    let skinnedTech =
      effect.Techniques |> Seq.tryFind(fun t -> t.Name = "Skinned")

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

    | Command3D.DrawInstanced(mesh, transforms, material, count) ->
      // Instancing under a user scope falls back to the PBR path (see remarks).
      drawInstanced(gd, &state, &frame, res, mesh, transforms, material, count)

    | _ -> ()
