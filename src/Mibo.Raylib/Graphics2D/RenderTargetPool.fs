namespace Mibo.Elmish.Graphics2D

open Mibo.Elmish
open System.Collections.Generic
open Raylib_cs

/// <summary>
/// Provides pooled render textures to avoid per-frame allocation and disposal
/// of <see cref="T:Raylib_cs.RenderTexture2D"/> resources.
/// </summary>
/// <remarks>
/// Acquired textures remain in use until <see cref="M:Mibo.Elmish.Graphics2D.IRenderTargetPool.ReleaseAll"/>
/// is called, typically once per frame. Textures are keyed by dimensions and reused
/// across frames without being destroyed, avoiding GPU allocation overhead.
/// </remarks>
type IRenderTargetPool =

  /// <summary>
  /// Acquires a render texture matching the given dimensions.
  /// Reuses a previously released texture if available, otherwise creates a new one.
  /// </summary>
  /// <returns>A render texture with the specified width and height.</returns>
  abstract Acquire: width: int * height: int -> RenderTexture2D

  /// <summary>
  /// Returns all currently held textures to the pool. Call once per frame
  /// after rendering is complete. Textures are retained for future reuse.
  /// </summary>
  abstract ReleaseAll: unit -> unit

/// <summary>
/// Default implementation of <see cref="T:Mibo.Elmish.Graphics2D.IRenderTargetPool"/>
/// using a dictionary keyed by (width, height) dimensions.
/// Stores textures in per-dimension queues for FIFO reuse.
/// </summary>
/// <remarks>
/// Dispose the pool when the application shuts down to unload all pooled textures.
/// </remarks>
type RenderTargetPool() =
  let pool = Dictionary<struct (int * int), Queue<RenderTexture2D>>()

  let inUse = ResizeArray<RenderTexture2D>()

  interface IRenderTargetPool with
    member _.Acquire(width, height) =
      let key = struct (width, height)

      match Dictionary.tryGetValue key pool with
      | ValueSome queue when queue.Count > 0 ->
        let rt = queue.Dequeue()
        inUse.Add(rt)
        rt
      | _ ->
        let rt = Raylib.LoadRenderTexture(width, height)
        inUse.Add(rt)
        rt

    member _.ReleaseAll() =
      for rt in inUse do
        let key = struct (rt.Texture.Width, rt.Texture.Height)

        match Dictionary.tryGetValue key pool with
        | ValueSome queue -> queue.Enqueue(rt)
        | ValueNone ->
          let queue = Queue<RenderTexture2D>()
          queue.Enqueue(rt)
          pool[key] <- queue

      inUse.Clear()

  interface System.IDisposable with
    member _.Dispose() =
      for rt in inUse do
        Raylib.UnloadRenderTexture(rt)

      inUse.Clear()

      for KeyValueV(_, queue) in pool do
        for rt in queue do
          Raylib.UnloadRenderTexture(rt)

        queue.Clear()

      pool.Clear()
