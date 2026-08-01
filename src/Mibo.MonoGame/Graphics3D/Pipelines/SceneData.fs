namespace Mibo.Elmish.Graphics3D.Pipelines

open System
open System.Runtime.InteropServices
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

// ─────────────────────────────────────────────────────────────────────────────
// PaletteTexturePool — per-frame pool of RGBA32F bone-palette textures for
// skinned-instanced draws (ForwardPbr.fx SkinnedInstanced / DepthShadow.fx
// DepthSkinnedInstanced). Shared by the forward PBR pass (PbrResources) and the
// shadow pass (ShadowResources) — each owns one instance.
//
// The former per-frame scene gather (ForwardState / SceneData / gather) lived here
// too; main moved it into SceneContext.fs (early scene types) and BlockPlan.fs
// (per-camera-block plan), so this file now holds only the palette machinery.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Constants for the skinned-instanced bone-palette texture.</summary>
module internal PaletteTexture =

  /// <summary>Max palette-texture height (instances per chunk). Well under every
  /// backend's max texture height; draws with more instances are split into chunks.</summary>
  [<Literal>]
  let MaxHeight = 2048

  /// <summary>Texture/sampler slot the palette texture is declared on in
  /// <c>ForwardPbr.fx</c> and <c>DepthShadow.fx</c> (<c>register(t6)</c>/<c>register(s6)</c>).</summary>
  [<Literal>]
  let SamplerSlot = 6

  /// <summary>
  /// Copies <paramref name="count"/> matrices from <paramref name="palettes"/> (starting at
  /// <paramref name="startIndex"/>) into <paramref name="staging"/> as 4 row-major Vector4
  /// texels each, growing it on demand, and returns the array to hand to
  /// <c>SetData&lt;Vector4&gt;</c> (with <c>count * 4</c> elements).
  /// </summary>
  /// <remarks>
  /// MonoGame rejects <c>SetData&lt;Matrix&gt;</c> on a <c>SurfaceFormat.Vector4</c> texture
  /// (<c>Matrix</c> is larger than the texel), so the upload goes through texel-sized
  /// <see cref="T:Microsoft.Xna.Framework.Vector4"/>s — 4 consecutive row texels per matrix,
  /// matching the shaders' <c>float4x4(r0..r3)</c> reconstruction. <c>Matrix</c> and
  /// <c>Vector4</c> are layout-compatible (16 / 4 contiguous sequential floats), so the
  /// "conversion" is a single <see cref="T:System.Runtime.InteropServices.MemoryMarshal"/>
  /// span copy (one memmove), not per-element field reads.
  /// </remarks>
  let stage
    (staging: Vector4[])
    (palettes: Matrix[])
    (startIndex: int)
    (count: int)
    : Vector4[] =
    let texelCount = count * 4

    let staging =
      if isNull staging || staging.Length < texelCount then
        Array.zeroCreate texelCount
      else
        staging

    let src =
      MemoryMarshal.Cast<Matrix, Vector4>(
        MemoryExtensions.AsSpan(palettes, startIndex, count)
      )

    src.CopyTo(MemoryExtensions.AsSpan(staging, 0, texelCount))

    staging

/// <summary>Constants for the grouped-uniform skinned-instanced path (the DX12
/// fallback — ForwardPbr.fx SkinnedInstancedGrouped / DepthShadow.fx
/// DepthSkinnedInstancedGrouped).</summary>
module internal PaletteGroup =

  /// <summary>Bone matrices per group — the shaders' <c>bonePaletteGroup[320]</c>.
  /// 320 × 64B = 20KB: mgfx packs ALL of an effect's globals into one shared
  /// <c>$Globals</c> constant buffer whose size is stored as a signed Int16 (32767
  /// cap). The DX12-only ForwardPbrGrouped.fx / DepthShadowGrouped.fx have ~11KB of
  /// other uniforms, so 320 matrices (20KB) is the budget that keeps $Globals under
  /// the cap. A named cbuffer at register(b1) was tried and rejected: the MonoGame
  /// native DX12 backend only wires b0 ($Globals), so b1 is never bound.
  /// Instances per group are <c>MaxMatrices / boneCount</c>; draws with more
  /// instances are split into groups.</summary>
  [<Literal>]
  let MaxMatrices = 320

  /// <summary>Instances per group for boneCount; 0 when boneCount exceeds
  /// MaxMatrices (the caller must fall back to per-instance draws).</summary>
  let groupSizeFor(boneCount: int) : int =
    if boneCount > MaxMatrices then
      0
    else
      max 1 (MaxMatrices / boneCount)

  /// <summary>Number of groups for instanceCount at boneCount; 0 when boneCount
  /// exceeds MaxMatrices.</summary>
  let groupCountFor (instanceCount: int) (boneCount: int) : int =
    let groupSize = groupSizeFor boneCount

    if groupSize = 0 then
      0
    else
      (instanceCount + groupSize - 1) / groupSize

  /// <summary>Fill scratch with (start, count, null-texture) group descriptors
  /// for the DX12 grouped-uniform skinned-instanced path; returns the group
  /// count, or -1 when boneCount exceeds MaxMatrices (scratch contents are then
  /// undefined). scratch must hold at least groupCountFor entries.</summary>
  let planGroups
    (instanceCount: int)
    (boneCount: int)
    (scratch: struct (int * int * Texture2D)[])
    : int =
    if boneCount > MaxMatrices then
      -1
    else
      let groupSize = groupSizeFor boneCount
      let total = groupCountFor instanceCount boneCount

      for i = 0 to total - 1 do
        let start = i * groupSize

        scratch[i] <-
          struct (start, min groupSize (instanceCount - start), null)

      total

/// <summary>
/// Pools <see cref="T:Microsoft.Xna.Framework.Graphics.Texture2D"/> bone-palette textures
/// (<see cref="F:Microsoft.Xna.Framework.Graphics.SurfaceFormat.Vector4"/>) keyed by
/// (width, height). Acquired textures stay checked out until
/// <see cref="M:Mibo.Elmish.Graphics3D.Pipelines.PaletteTexturePool.ReleaseAll"/> — called once
/// per frame, mirroring <see cref="T:Mibo.Elmish.Graphics3D.RenderTargetPool3D"/>'s lifetime.
/// </summary>
/// <remarks>
/// The per-frame lifetime is a correctness requirement, not just an allocation optimization:
/// palette textures are re-uploaded via <c>SetData</c> once per chunk within a single frame,
/// and on the native DX12 backend uploads execute immediately while draws execute at end of
/// frame — reusing one texture across chunks would make every draw read the LAST chunk's
/// palettes (the same hazard <c>DynamicVertexBuffer</c> solves for instance vertex buffers).
/// A fresh-from-pool texture per chunk keeps each draw's palette data intact.
/// </remarks>
type PaletteTexturePool(?maxIdlePerSize: int) =
  let maxIdle = defaultArg maxIdlePerSize 2

  let pool =
    System.Collections.Generic.Dictionary<
      struct (int * int),
      System.Collections.Generic.Queue<Texture2D>
     >()

  let inUse = ResizeArray<Texture2D>()

  /// <summary>Acquires a palette texture of the given size, reusing a released one when
  /// available. The texture stays checked out until the next <c>ReleaseAll</c>.</summary>
  member _.Acquire(gd: GraphicsDevice, width: int, height: int) : Texture2D =
    let key = struct (width, height)

    match pool.TryGetValue(key) with
    | true, queue when queue.Count > 0 ->
      let tex = queue.Dequeue()
      inUse.Add(tex)
      tex
    | _ ->
      let tex = new Texture2D(gd, width, height, false, SurfaceFormat.Vector4)

      inUse.Add(tex)
      tex

  /// <summary>Returns all checked-out textures to the pool. Call once per frame, before the
  /// frame's first skinned-instanced draw (the previous frame's draws were already submitted).
  /// Idle textures kept per size are capped; extras are disposed.</summary>
  member _.ReleaseAll() =
    for tex in inUse do
      let key = struct (tex.Width, tex.Height)

      match pool.TryGetValue(key) with
      | true, queue when queue.Count < maxIdle -> queue.Enqueue(tex)
      | true, _ -> tex.Dispose()
      | false, _ ->
        let queue = System.Collections.Generic.Queue<Texture2D>()
        queue.Enqueue(tex)
        pool[key] <- queue

    inUse.Clear()

  interface System.IDisposable with
    member _.Dispose() =
      for tex in inUse do
        tex.Dispose()

      inUse.Clear()

      for KeyValue(_, queue) in pool do
        for tex in queue do
          tex.Dispose()

        queue.Clear()

      pool.Clear()

/// <summary>
/// Per-frame cache of staged bone-palette texture chunks for skinned + instanced draws,
/// SHARED by the shadow pass and the forward PBR pass: both passes stage the same palettes
/// every frame, so doing it in each pass doubled the most expensive per-frame work
/// (<c>count * boneCount</c> matrices at 64B each, staged + uploaded). The first pass to
/// request a (palettes, boneCount, count) triple stages + uploads it; the second gets the
/// same chunk textures back.
/// </summary>
/// <remarks>
/// Memo validity is per frame: the palettes arrays are game-owned and stable for the
/// duration of the frame's render flush (both passes draw from the same command buffer
/// before the game mutates them again), and <c>ReleaseAll</c> — called once per frame
/// alongside the pool's — drops every memo. Keyed by array reference + (boneCount, count).
/// </remarks>
type PaletteChunkCache() =
  let pool = new PaletteTexturePool()
  let mutable staging: Vector4[] = [||]

  // (boneCount, count, chunks) per source palettes array, keyed by reference identity.
  let memo =
    System.Collections.Generic.Dictionary<
      Matrix[],
      int * int * struct (int * int * Texture2D)[]
     >(
      { new System.Collections.Generic.IEqualityComparer<Matrix[]> with
          member _.Equals(a, b) = obj.ReferenceEquals(a, b)

          member _.GetHashCode(a) =
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a)
      }
    )

  /// <summary>Returns the palette-texture chunks covering <paramref name="count"/>
  /// instances (≤ <see cref="F:Mibo.Elmish.Graphics3D.Pipelines.PaletteTexture.MaxHeight"/>
  /// instances per chunk) as (chunkStart, chunkCount, texture) triples, staging +
  /// uploading them on first request this frame.</summary>
  member this.Obtain
    (gd: GraphicsDevice, palettes: Matrix[], boneCount: int, count: int)
    : struct (int * int * Texture2D)[] =
    match memo.TryGetValue(palettes) with
    | true, (b, c, chunks) when b = boneCount && c = count -> chunks
    | _ ->
      let chunkTotal =
        (count + PaletteTexture.MaxHeight - 1) / PaletteTexture.MaxHeight

      let chunks = Array.zeroCreate chunkTotal
      let mutable chunkStart = 0

      for i = 0 to chunkTotal - 1 do
        let chunkCount = min PaletteTexture.MaxHeight (count - chunkStart)
        // Fresh-from-pool texture per chunk (per-frame lifetime — the native DX12
        // backend uploads textures immediately but draws at end of frame).
        let tex = pool.Acquire(gd, boneCount * 4, chunkCount)

        staging <-
          PaletteTexture.stage
            staging
            palettes
            (chunkStart * boneCount)
            (chunkCount * boneCount)

        tex.SetData<Vector4>(staging, 0, chunkCount * boneCount * 4)
        chunks[i] <- struct (chunkStart, chunkCount, tex)
        chunkStart <- chunkStart + chunkCount

      memo[palettes] <- (boneCount, count, chunks)
      chunks

  /// <summary>Returns last frame's chunk textures to the pool and drops every memo
  /// (per-frame lifetime — see <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.PaletteTexturePool"/>).</summary>
  member this.ReleaseAll() =
    pool.ReleaseAll()
    memo.Clear()

  interface System.IDisposable with
    member this.Dispose() =
      (pool :> System.IDisposable).Dispose()
      memo.Clear()
