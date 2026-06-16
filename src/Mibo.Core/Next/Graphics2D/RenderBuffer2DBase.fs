namespace Mibo.Elmish.Next.Graphics2D

open System
open System.Buffers

// ─────────────────────────────────────────────────────────────────
// Layer extraction from Core Command2D
// ─────────────────────────────────────────────────────────────────

[<AutoOpen>]
module internal Command2DLayer =

  let getLayer(cmd: Command2D) : int<RenderLayer> =
    match cmd with
    | Command2D.Sprite(_, _, _, _, _, _, layer) -> layer
    | Command2D.Text(_, _, _, _, _, _, layer) -> layer
    | Command2D.FillRect(_, _, layer) -> layer
    | Command2D.RectOutline(_, _, _, layer) -> layer
    | Command2D.FillRectRounded(_, _, _, _, layer) -> layer
    | Command2D.RectRoundedOutline(_, _, _, _, _, layer) -> layer
    | Command2D.RectGradientV(_, _, _, _, _, _, layer) -> layer
    | Command2D.RectGradientH(_, _, _, _, _, _, layer) -> layer
    | Command2D.RectGradient(_, _, _, _, _, layer) -> layer
    | Command2D.FillCircle(_, _, _, layer) -> layer
    | Command2D.CircleOutline(_, _, _, layer) -> layer
    | Command2D.CircleSector(_, _, _, _, _, _, layer) -> layer
    | Command2D.CircleSectorOutline(_, _, _, _, _, _, layer) -> layer
    | Command2D.CircleGradient(_, _, _, _, _, layer) -> layer
    | Command2D.FillRing(_, _, _, _, _, _, _, layer) -> layer
    | Command2D.RingOutline(_, _, _, _, _, _, _, layer) -> layer
    | Command2D.FillEllipse(_, _, _, _, _, layer) -> layer
    | Command2D.EllipseOutline(_, _, _, _, _, layer) -> layer
    | Command2D.Line(_, _, _, layer) -> layer
    | Command2D.LineThick(_, _, _, _, layer) -> layer
    | Command2D.LineStrip(_, _, layer) -> layer
    | Command2D.Bezier(_, _, _, _, _, layer) -> layer
    | Command2D.Triangle(_, _, _, _, layer) -> layer
    | Command2D.TriangleFan(_, _, layer) -> layer
    | Command2D.TriangleStrip(_, _, layer) -> layer
    | Command2D.FillPoly(_, _, _, _, _, layer) -> layer
    | Command2D.PolyOutline(_, _, _, _, _, _, layer) -> layer
    | Command2D.BeginCamera(_, layer) -> layer
    | Command2D.BeginCameraConfig(_, layer) -> layer
    | Command2D.EndCamera layer -> layer
    | Command2D.BeginShader(_, layer) -> layer
    | Command2D.EndShader layer -> layer
    | Command2D.BeginTarget(_, layer) -> layer
    | Command2D.EndTarget layer -> layer
    | Command2D.SetBlend(_, layer) -> layer
    | Command2D.SetScissor(_, _, _, _, layer) -> layer
    | Command2D.ClearScissor layer -> layer
    | Command2D.SetLineWidth(_, layer) -> layer
    | Command2D.SetViewport(_, _, _, _, layer) -> layer
    | Command2D.DrawImmediate(_, layer) -> layer
    | Command2D.Clear(_, layer) -> layer
    | Command2D.NoopLight layer -> layer
    | Command2D.LitSprite(_, _, _, _, _, _, _, _, layer) -> layer
    | Command2D.EndLighting(_, layer) -> layer
    | Command2D.EnableShadows(_, layer) -> layer
    | Command2D.DisableShadows(_, layer) -> layer
    | Command2D.Particle(_, _, _, layer) -> layer

// ─────────────────────────────────────────────────────────────────
// Abstract buffer base
// ─────────────────────────────────────────────────────────────────

/// <summary>
/// Backend-neutral base for a 2D render-command buffer.
/// Backends subclass this to carry resource registries (see
/// <c>RenderBuffer2D</c> in each backend) and expose them to the DSL.
/// </summary>
/// <remarks>
/// <para>
/// Commands are accumulated each frame via <see cref="M:add"/>,
/// sorted by layer, then executed by the renderer.
/// </para>
/// <para>
/// Uses <see cref="T:System.Buffers.ArrayPool`1"/> for the backing store to
/// avoid per-frame heap allocations.
/// </para>
/// </remarks>
[<AbstractClass>]
type RenderBuffer2DBase(?capacity: int) =

  let initialCapacity = defaultArg capacity 1024

  let mutable items = ArrayPool<Command2D>.Shared.Rent(initialCapacity)

  let mutable keys = ArrayPool<int64>.Shared.Rent(initialCapacity)
  let mutable count = 0
  let mutable disposed = false

  let ensureCapacity(needed: int) =
    if count + needed > items.Length then
      let newSize = max (items.Length * 2) (count + needed)

      let newItems = ArrayPool<Command2D>.Shared.Rent(newSize)
      let newKeys = ArrayPool<int64>.Shared.Rent(newSize)

      Array.Copy(items, newItems, count)
      Array.Copy(keys, newKeys, count)
      ArrayPool<Command2D>.Shared.Return(items, true)
      ArrayPool<int64>.Shared.Return(keys)
      items <- newItems
      keys <- newKeys

  /// <summary>The number of commands currently in the buffer.</summary>
  member _.Count = count

  /// <summary>Gets the command at the specified index.</summary>
  member _.Item(i: int) = items[i]

  /// <summary>Adds a render command to the buffer.</summary>
  member _.Add(cmd: Command2D) =
    ensureCapacity 1
    items[count] <- cmd
    keys[count] <- (int64(int(getLayer cmd)) <<< 32) ||| int64 count
    count <- count + 1

  /// <summary>
  /// Clears all commands from the buffer without deallocating.
  /// Nulls reference-bearing slots to release GC pressure.
  /// Call at the start of each frame before populating with new commands.
  /// </summary>
  member _.Clear() =
    Array.Clear(items, 0, count)
    count <- 0

  /// <summary>
  /// Sorts commands by layer in ascending order, preserving insertion order
  /// for same-layer commands via packed <c>int64</c> keys.
  /// </summary>
  member _.Sort() = Array.Sort(keys, items, 0, count)

  /// <summary>
  /// Returns the rented backing arrays to <see cref="T:System.Buffers.ArrayPool`1"/>.
  /// After disposal the buffer must not be used.
  /// </summary>
  member _.Dispose() =
    if not disposed then
      disposed <- true
      ArrayPool<Command2D>.Shared.Return(items, true)
      ArrayPool<int64>.Shared.Return(keys)
      items <- Array.empty
      keys <- Array.empty
      count <- 0

  interface IDisposable with
    member this.Dispose() = this.Dispose()
