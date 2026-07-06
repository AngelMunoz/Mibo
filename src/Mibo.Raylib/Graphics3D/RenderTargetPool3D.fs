namespace Mibo.Elmish.Graphics3D

open System.Collections.Generic
open Microsoft.FSharp.NativeInterop
open Raylib_cs

/// <summary>
/// Provides pooled render textures to avoid per-frame allocation and disposal
/// of <see cref="T:Raylib_cs.RenderTexture2D"/> resources for 3D rendering.
/// </summary>
/// <remarks>
/// Acquired textures remain in use until <see cref="M:Mibo.Elmish.Graphics3D.IRenderTargetPool3D.ReleaseAll"/>
/// is called, typically once per frame. Textures are keyed by dimensions and reused
/// across frames without being destroyed, avoiding GPU allocation overhead.
/// </remarks>
type IRenderTargetPool3D =

  /// <summary>
  /// Acquires a render texture matching the given dimensions.
  /// Reuses a previously released texture if available, otherwise creates a new one.
  /// The depth attachment is a renderbuffer (not sampleable).
  /// </summary>
  /// <returns>A render texture with the specified width and height.</returns>
  abstract Acquire: width: int * height: int -> RenderTexture2D

  /// <summary>
  /// Acquires a render texture whose depth attachment is a sampleable <b>texture</b> (not a
  /// renderbuffer). Use this for scene RTs when post-process effects need to sample depth
  /// (fog, depth-of-field, SSAO). Reuses a previously released texture if available, otherwise
  /// creates a new custom FBO (color texture + depth texture). More expensive to create than
  /// <see cref="M:Mibo.Elmish.Graphics3D.IRenderTargetPool3D.Acquire"/>, so only request it when depth
  /// sampling is actually needed.
  /// </summary>
  /// <returns>A render texture with a sampleable depth attachment.</returns>
  abstract AcquireWithDepth: width: int * height: int -> RenderTexture2D

  /// <summary>
  /// Returns all currently held textures to the pool. Call once per frame
  /// after rendering is complete. Textures are retained for future reuse.
  /// </summary>
  abstract ReleaseAll: unit -> unit

/// <summary>
/// Default implementation of <see cref="T:Mibo.Elmish.Graphics3D.IRenderTargetPool3D"/>
/// using a dictionary keyed by (width, height) dimensions.
/// Stores textures in per-dimension queues for FIFO reuse.
/// </summary>
/// <remarks>
/// Dispose the pool when the application shuts down to unload all pooled textures.
/// </remarks>
type RenderTargetPool3D() =

  let maxIdle = 2

  // ── Standard RTs (color texture + depth renderbuffer via raylib's LoadRenderTexture) ──
  let pool = Dictionary<struct (int * int), Queue<RenderTexture2D>>()
  let inUse = ResizeArray<RenderTexture2D>()

  // ── Depth-sampleable RTs (custom rlgl FBO: color texture + depth texture) ──
  // Separate pool so depth-needing frames don't pay the custom-FBO cost when they don't need it.
  let depthPool = Dictionary<struct (int * int), Queue<RenderTexture2D>>()
  let depthInUse = ResizeArray<RenderTexture2D>()

  /// <summary>
  /// Creates a custom FBO with a color texture + a sampleable depth texture (not renderbuffer).
  /// raylib's <c>LoadRenderTexture</c> attaches depth as a renderbuffer, which can't be sampled
  /// in a shader — this builds the FBO manually so the depth attachment is a real texture.
  /// </summary>
  static member private CreateDepthTextureRT
    (width: int, height: int)
    : RenderTexture2D =
    let fboId = Rlgl.LoadFramebuffer()
    Rlgl.EnableFramebuffer(fboId)

    // Color texture (R8G8B8A8). Allocate a zeroed buffer and upload it once; raylib-cs's
    // LoadTexture requires a non-null data pointer (DisableRuntimeMarshalling).
    let colorBytes = Array.zeroCreate<byte>(width * height * 4)
    use pcb = fixed &colorBytes[0]

    let colorId =
      Rlgl.LoadTexture(
        NativePtr.toVoidPtr pcb,
        width,
        height,
        PixelFormat.UncompressedR8G8B8A8,
        1
      )

    // Depth texture (sampleable). false = texture, not renderbuffer.
    let depthId = Rlgl.LoadTextureDepth(width, height, false)

    Rlgl.FramebufferAttach(
      fboId,
      colorId,
      FramebufferAttachType.ColorChannel0,
      FramebufferAttachTextureType.Texture2D,
      0
    )

    Rlgl.FramebufferAttach(
      fboId,
      depthId,
      FramebufferAttachType.Depth,
      FramebufferAttachTextureType.Texture2D,
      0
    )

    Rlgl.DisableFramebuffer()

    RenderTexture2D(
      Id = fboId,
      Texture =
        Texture2D(
          Id = colorId,
          Width = width,
          Height = height,
          Mipmaps = 1,
          Format = PixelFormat.UncompressedR8G8B8A8
        ),
      Depth =
        Texture2D(
          Id = depthId,
          Width = width,
          Height = height,
          Mipmaps = 1,
          Format = enum<PixelFormat> 19 // Depth24Unorm — matches ShadowAtlas.fs convention
        )
    )

  interface IRenderTargetPool3D with
    member _.Acquire(width, height) =
      let key = struct (width, height)

      match pool.TryGetValue(key) with
      | true, queue when queue.Count > 0 ->
        let rt = queue.Dequeue()
        inUse.Add(rt)
        rt
      | _ ->
        let rt = Raylib.LoadRenderTexture(width, height)
        inUse.Add(rt)
        rt

    member _.AcquireWithDepth(width, height) =
      let key = struct (width, height)

      match depthPool.TryGetValue(key) with
      | true, queue when queue.Count > 0 ->
        let rt = queue.Dequeue()
        depthInUse.Add(rt)
        rt
      | _ ->
        let rt = RenderTargetPool3D.CreateDepthTextureRT(width, height)
        depthInUse.Add(rt)
        rt

    member _.ReleaseAll() =
      for rt in inUse do
        let key = struct (rt.Texture.Width, rt.Texture.Height)

        match pool.TryGetValue(key) with
        | true, queue when queue.Count < maxIdle -> queue.Enqueue(rt)
        | _ ->
          // Pool full for this dimension — unload instead of retaining.
          match pool.TryGetValue(key) with
          | true, queue when queue.Count >= maxIdle ->
            Raylib.UnloadRenderTexture(rt)
          | false, _ ->
            let queue = Queue<RenderTexture2D>()
            queue.Enqueue(rt)
            pool[key] <- queue

      inUse.Clear()

      for rt in depthInUse do
        let key = struct (rt.Texture.Width, rt.Texture.Height)

        match depthPool.TryGetValue(key) with
        | true, queue when queue.Count < maxIdle -> queue.Enqueue(rt)
        | _ ->
          Rlgl.UnloadTexture(rt.Texture.Id)
          Rlgl.UnloadTexture(rt.Depth.Id)
          Rlgl.UnloadFramebuffer(rt.Id)

      depthInUse.Clear()

  interface System.IDisposable with
    member _.Dispose() =
      for rt in inUse do
        Raylib.UnloadRenderTexture(rt)

      inUse.Clear()

      for KeyValue(_, queue) in pool do
        for rt in queue do
          Raylib.UnloadRenderTexture(rt)

        queue.Clear()

      pool.Clear()

      // Depth-texture RTs use a custom FBO with separate texture resources — unload each
      // (FBO + color texture + depth texture) individually, matching ShadowAtlas.Shutdown.
      for rt in depthInUse do
        Rlgl.UnloadTexture(rt.Texture.Id)
        Rlgl.UnloadTexture(rt.Depth.Id)
        Rlgl.UnloadFramebuffer(rt.Id)

      depthInUse.Clear()

      for KeyValue(_, queue) in depthPool do
        for rt in queue do
          Rlgl.UnloadTexture(rt.Texture.Id)
          Rlgl.UnloadTexture(rt.Depth.Id)
          Rlgl.UnloadFramebuffer(rt.Id)

        queue.Clear()

      depthPool.Clear()
