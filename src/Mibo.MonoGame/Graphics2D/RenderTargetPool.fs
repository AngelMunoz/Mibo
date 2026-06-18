namespace Mibo.Elmish.Graphics2D

open System.Collections.Generic
open Microsoft.Xna.Framework.Graphics

/// <summary>
/// Provides pooled render targets to avoid per-frame allocation and disposal
/// of <see cref="T:Microsoft.Xna.Framework.Graphics.RenderTarget2D"/> resources.
/// </summary>
/// <remarks>
/// Acquired targets remain in use until <see cref="M:Mibo.Elmish.Graphics2D.IRenderTargetPool.ReleaseAll"/>
/// is called, typically once per frame. Targets are keyed by dimensions and reused
/// across frames without being disposed, avoiding GPU allocation overhead.
/// </remarks>
type IRenderTargetPool =

  /// <summary>
  /// Acquires a render target matching the given dimensions.
  /// Reuses a previously released target if available, otherwise creates a new one.
  /// </summary>
  /// <returns>A render target with the specified width and height.</returns>
  abstract Acquire: width: int * height: int -> RenderTarget2D

  /// <summary>
  /// Returns all currently held targets to the pool. Call once per frame
  /// after rendering is complete. Targets are retained for future reuse.
  /// </summary>
  abstract ReleaseAll: unit -> unit

/// <summary>
/// Default implementation of <see cref="T:Mibo.Elmish.Graphics2D.IRenderTargetPool"/>
/// using a dictionary keyed by (width, height) dimensions.
/// Stores targets in per-dimension queues for FIFO reuse.
/// </summary>
/// <remarks>
/// Dispose the pool when the application shuts down to dispose all pooled targets.
/// </remarks>
type RenderTargetPool(gd: GraphicsDevice) =
  let pool = Dictionary<struct (int * int), Queue<RenderTarget2D>>()
  let inUse = ResizeArray<RenderTarget2D>()

  interface IRenderTargetPool with
    member _.Acquire(width, height) =
      let key = struct (width, height)

      match pool.TryGetValue(key) with
      | true, queue when queue.Count > 0 ->
        let rt = queue.Dequeue()
        inUse.Add(rt)
        rt
      | _ ->
        let rt =
          new RenderTarget2D(
            gd,
            width,
            height,
            false,
            SurfaceFormat.Color,
            DepthFormat.None,
            0,
            RenderTargetUsage.DiscardContents
          )

        inUse.Add(rt)
        rt

    member _.ReleaseAll() =
      for rt in inUse do
        let key = struct (rt.Width, rt.Height)

        match pool.TryGetValue(key) with
        | true, queue -> queue.Enqueue(rt)
        | false, _ ->
          let queue = Queue<RenderTarget2D>()
          queue.Enqueue(rt)
          pool[key] <- queue

      inUse.Clear()

  interface System.IDisposable with
    member _.Dispose() =
      for rt in inUse do
        rt.Dispose()

      inUse.Clear()

      for KeyValue(_, queue) in pool do
        for rt in queue do
          rt.Dispose()

        queue.Clear()

      pool.Clear()
