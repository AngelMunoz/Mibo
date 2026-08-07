namespace Mibo.Elmish.Graphics3D.Pipelines

open System
open System.Runtime.InteropServices
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics3D

// ─────────────────────────────────────────────────────────────────────────────
// PaletteTexturePool — per-frame pool of RGBA32F bone-palette textures for
// skinned-instanced draws (ForwardPbr.fx SkinnedInstanced / DepthShadow.fx
// DepthSkinnedInstanced). Shared by the forward PBR pass (PbrResources) and the
// shadow pass (ShadowResources) — each owns one instance.
//
// The former per-frame scene gather (ForwardState / SceneData / gather) lived here
// too; main moved it into SceneContext.fs (early scene types) and BlockPlan.fs
// (per-camera-block plan). What remains is the palette machinery plus
// InstanceWorldCache (per-frame world-row staging for instanced draws).
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

  /// <summary>Bone matrices per group for the FORWARD grouped effect — the shader's
  /// <c>bonePaletteGroup[448]</c>. mgfx packs ALL of an effect's globals into one shared
  /// <c>$Globals</c> constant buffer whose size is stored as a signed Int16 (32767 cap —
  /// Effect.cs ReadEffect reads it as Int16). Measured non-palette $Globals cost
  /// (cb-size probe, 2026-08-01): ForwardPbrGrouped 3156B, so 448×64+3156 = 31828B —
  /// under the cap with headroom for small shader edits. A named cbuffer at register(b1)
  /// was tried and rejected: the MonoGame native DX12 backend only wires b0 ($Globals).
  /// Instances per group are <c>maxMatrices / boneCount</c>; draws with more instances
  /// are split into groups.</summary>
  [<Literal>]
  let MaxMatrices = 448

  /// <summary>Bone matrices per group for the DEPTH grouped effect — the shader's
  /// <c>bonePaletteGroup[500]</c>. The depth effect's non-palette $Globals cost is only
  /// 128B (matModel + viewProj), so 500×64+128 = 32128B fits the Int16
  /// cap. Larger depth groups mean fewer shadow-pass draws per frame.</summary>
  [<Literal>]
  let MaxMatricesDepth = 500

  /// <summary>Instances per group for boneCount; 0 when boneCount exceeds
  /// maxMatrices (the caller must fall back to per-instance draws).</summary>
  let groupSizeFor (maxMatrices: int) (boneCount: int) : int =
    if boneCount > maxMatrices then
      0
    else
      max 1 (maxMatrices / boneCount)

  /// <summary>Number of groups for instanceCount at boneCount; 0 when boneCount
  /// exceeds maxMatrices.</summary>
  let groupCountFor
    (maxMatrices: int)
    (instanceCount: int)
    (boneCount: int)
    : int =
    let groupSize = groupSizeFor maxMatrices boneCount

    if groupSize = 0 then
      0
    else
      (instanceCount + groupSize - 1) / groupSize

  /// <summary>Fill scratch with (start, count, null-texture) group descriptors
  /// for the DX12 grouped-uniform skinned-instanced path; returns the group
  /// count, or -1 when boneCount exceeds maxMatrices (scratch contents are then
  /// undefined). scratch must hold at least groupCountFor entries.</summary>
  let planGroups
    (maxMatrices: int)
    (instanceCount: int)
    (boneCount: int)
    (scratch: struct (int * int * Texture2D)[])
    : int =
    if boneCount > maxMatrices then
      -1
    else
      let groupSize = groupSizeFor maxMatrices boneCount
      let total = groupCountFor maxMatrices instanceCount boneCount

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

  // Textures checked out this frame per size. ReleaseAll keeps up to this many idle
  // per size (never less than maxIdle): steady frames create and dispose nothing once
  // demand stabilizes — a flat cap churns creates + disposes every frame whenever the
  // frame's chunk count exceeds it.
  let usedThisFrame =
    System.Collections.Generic.Dictionary<struct (int * int), int>()

  /// <summary>Acquires a palette texture of the given size, reusing a released one when
  /// available. The texture stays checked out until the next <c>ReleaseAll</c>.</summary>
  member _.Acquire(gd: GraphicsDevice, width: int, height: int) : Texture2D =
    let key = struct (width, height)

    match Dictionary.tryGetValue key usedThisFrame with
    | ValueSome n -> usedThisFrame[key] <- n + 1
    | ValueNone -> usedThisFrame[key] <- 1

    match Dictionary.tryGetValue key pool with
    | ValueSome queue when queue.Count > 0 ->
      let tex = queue.Dequeue()
      inUse.Add(tex)
      tex
    | ValueNone ->
      let tex = new Texture2D(gd, width, height, false, SurfaceFormat.Vector4)

      inUse.Add(tex)
      tex

  /// <summary>Returns all checked-out textures to the pool. Call once per frame, before the
  /// frame's first skinned-instanced draw (the previous frame's draws were already submitted).
  /// Idle textures kept per size track this frame's demand (floor: the cap); extras are
  /// disposed.</summary>
  member _.ReleaseAll() =
    for tex in inUse do
      let key = struct (tex.Width, tex.Height)

      let keep =
        match Dictionary.tryGetValue key usedThisFrame with
        | ValueSome n -> max maxIdle n
        | ValueNone -> maxIdle

      match Dictionary.tryGetValue key pool with
      | ValueSome queue when queue.Count < keep -> queue.Enqueue(tex)
      | ValueSome _ -> tex.Dispose()
      | ValueNone ->
        let queue = System.Collections.Generic.Queue<Texture2D>()
        queue.Enqueue(tex)
        pool[key] <- queue

    // Trim idle overflow back toward this frame's demand, so a one-frame spike
    // doesn't pin the pool at spike size forever.
    for KeyValueV(key, queue) in pool do
      let keep =
        match Dictionary.tryGetValue key usedThisFrame with
        | ValueSome n -> max maxIdle n
        | ValueNone -> maxIdle

      while queue.Count > keep do
        queue.Dequeue().Dispose()

    inUse.Clear()
    usedThisFrame.Clear()

  interface System.IDisposable with
    member _.Dispose() =
      for tex in inUse do
        tex.Dispose()

      inUse.Clear()

      for KeyValueV(_, queue) in pool do
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
    match Dictionary.tryGetValue palettes memo with
    | ValueSome(b, c, chunks) when b = boneCount && c = count -> chunks
    | ValueNone ->
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
    member this.Dispose() = (pool :> System.IDisposable).Dispose()

/// <summary>
/// Per-frame cache of staged per-instance world rows (<c>VertexInstanceWorldPalette</c>:
/// world matrix + chunk-local palette row) for skinned + instanced draws, SHARED by the
/// shadow pass and the forward PBR pass on the palette-texture backends (DX11/Vulkan).
/// Both passes stage the same rows every frame — the chunk plan is already shared via
/// <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.PaletteChunkCache"/>, so chunk-local
/// offsets match and one staging pass can serve both passes' vertex-buffer uploads
/// (each pass still uploads into its OWN DynamicVertexBuffer — the two passes never
/// share a buffer, preserving the no-race DX12 upload-ordering design).
/// </summary>
/// <remarks>
/// Memo validity is per frame, same as <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.PaletteChunkCache"/>:
/// the buffer copies caller transforms into per-frame rented arrays at record time, so each
/// command's array is stable for the duration of the render flush, and
/// <c>ReleaseAll</c> — called once per frame alongside the palette cache's — drops every
/// memo. Keyed by array reference + count. The DX12 grouped path does NOT use this cache:
/// its forward/depth group budgets differ (PaletteGroup.MaxMatrices vs MaxMatricesDepth),
/// so chunk-local offsets differ per pass and staging stays per pass there.
/// </remarks>
type InstanceWorldCache() =
  // Pooled per-command staging arrays: slot per command, grow-only, kept across
  // frames (memo alone clears per frame). Zero steady-state allocation.
  let pool = ResizeArray<VertexInstanceWorldPalette[]>()
  let mutable used = 0

  // transforms array → (count, pool slot), keyed by reference identity.
  let memo =
    System.Collections.Generic.Dictionary<Matrix[], int * int>(
      { new System.Collections.Generic.IEqualityComparer<Matrix[]> with
          member _.Equals(a, b) = obj.ReferenceEquals(a, b)

          member _.GetHashCode(a) =
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a)
      }
    )

  /// <summary>Returns the staged rows covering <paramref name="count"/> instances under
  /// the given chunk plan ((chunkStart, chunkCount, _) triples), staging them on first
  /// request this frame. Row j of a chunk carries PaletteOffset j — chunk-local, matching
  /// the palette texture that holds only that chunk.</summary>
  member this.Obtain
    (
      transforms: Matrix[],
      count: int,
      chunks: struct (int * int * 'T)[],
      chunkTotal: int
    ) : VertexInstanceWorldPalette[] =
    match Dictionary.tryGetValue transforms memo with
    | ValueSome(c, slot) when c = count -> pool[slot]
    | ValueNone ->
      let slot = used
      used <- used + 1

      if pool.Count <= slot then
        pool.Add([||])

      if pool[slot].Length < count then
        pool[slot] <- Array.zeroCreate count

      let rows = pool[slot]

      for k = 0 to chunkTotal - 1 do
        let struct (chunkStart, chunkCount, _) = chunks[k]

        for i = 0 to chunkCount - 1 do
          // PaletteOffset is chunk-local: palette storage (texture chunk or uniform
          // group on the DX12 path) holds this chunk only.
          rows[chunkStart + i] <-
            VertexInstanceWorldPalette.Create(
              transforms[chunkStart + i],
              float32 i
            )

      memo[transforms] <- (count, slot)
      rows

  /// <summary>Drops every memo (per-frame lifetime); pool arrays are kept (grow-only).</summary>
  member this.ReleaseAll() =
    memo.Clear()
    used <- 0
