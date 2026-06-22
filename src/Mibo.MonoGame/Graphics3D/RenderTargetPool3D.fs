namespace Mibo.Elmish.Graphics3D

open System.Collections.Generic
open Microsoft.Xna.Framework.Graphics

/// <summary>
/// Provides pooled render targets to avoid per-frame allocation and disposal
/// of <see cref="T:Microsoft.Xna.Framework.Graphics.RenderTarget2D"/> resources for 3D rendering.
/// </summary>
/// <remarks>
/// Acquired targets remain in use until <see cref="M:Mibo.Elmish.Graphics3D.IRenderTargetPool3D.ReleaseAll"/>
/// is called, typically once per frame. Targets are keyed by dimensions and reused
/// across frames without being disposed, avoiding GPU allocation overhead.
/// </remarks>
type IRenderTargetPool3D =

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
/// Default implementation of <see cref="T:Mibo.Elmish.Graphics3D.IRenderTargetPool3D"/>.
/// Uses a dictionary keyed by (width, height) dimensions.
/// </summary>
/// <remarks>
/// Dispose the pool when the application shuts down to dispose all pooled targets.
/// Idle targets kept per dimension are capped by <paramref name="maxIdlePerDimension"/>
/// so that repeated window resizes don't retain GPU memory for transient dimensions.
/// </remarks>
type RenderTargetPool3D(gd: GraphicsDevice, ?maxIdlePerDimension: int) =
  let maxIdle = defaultArg maxIdlePerDimension 2
  let pool = Dictionary<struct (int * int), Queue<RenderTarget2D>>()
  let inUse = ResizeArray<RenderTarget2D>()

  interface IRenderTargetPool3D with
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
            DepthFormat.Depth24,
            0,
            RenderTargetUsage.DiscardContents
          )

        inUse.Add(rt)
        rt

    member _.ReleaseAll() =
      for rt in inUse do
        let key = struct (rt.Width, rt.Height)

        match pool.TryGetValue(key) with
        | true, queue when queue.Count < maxIdle -> queue.Enqueue(rt)
        | true, _ -> rt.Dispose()
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
