namespace Mibo.Elmish.Next.Graphics3D

open System
open System.Buffers
open System.Collections.Generic

// ─────────────────────────────────────────────────────────────────
// Abstract 3D buffer base
// ─────────────────────────────────────────────────────────────────

/// <summary>
/// Backend-neutral base for a 3D render-command buffer.
/// Commands are accumulated each frame and executed in insertion order
/// by the pipeline. The pipeline may re-sort internally if needed.
/// </summary>
[<AbstractClass>]
type RenderBuffer3DBase(?capacity: int) =

  let mutable items = ArrayPool<Command3D>.Shared.Rent(defaultArg capacity 1024)

  let mutable count = 0
  let mutable disposed = false

  let checkDisposed() =
    if disposed then
      raise(ObjectDisposedException(nameof RenderBuffer3DBase))

  let ensureCapacity(needed: int) =
    if count + needed > items.Length then
      let newSize = max (items.Length * 2) (count + needed)

      let newArr = ArrayPool<Command3D>.Shared.Rent(newSize)

      Array.Copy(items, newArr, count)
      ArrayPool<Command3D>.Shared.Return(items, true)
      items <- newArr

  /// <summary>The number of commands currently in the buffer.</summary>
  member _.Count =
    checkDisposed()
    count

  /// <summary>Gets the command at the specified index.</summary>
  member _.Item(i: int) =
    checkDisposed()
    items[i]

  /// <summary>Adds a render command to the buffer.</summary>
  member _.Add(cmd: Command3D) =
    checkDisposed()
    ensureCapacity 1
    items[count] <- cmd
    count <- count + 1

  /// <summary>
  /// Clears all commands from the buffer without deallocating.
  /// Nulls reference-bearing slots to release GC pressure.
  /// Call at the start of each frame.
  /// </summary>
  member _.Clear() =
    checkDisposed()
    Array.Clear(items, 0, count)
    count <- 0

  /// <summary>
  /// Sorts commands using the provided comparer.
  /// Pipelines may call this internally to optimize draw order.
  /// </summary>
  member _.Sort(comparer: IComparer<Command3D>) =
    checkDisposed()
    Array.Sort(items, 0, count, comparer)

  /// <summary>
  /// Returns the rented backing array to <see cref="T:System.Buffers.ArrayPool`1"/>.
  /// After disposal the buffer must not be used.
  /// </summary>
  member _.Dispose() =
    if not disposed then
      disposed <- true
      ArrayPool<Command3D>.Shared.Return(items, true)
      items <- Array.empty
      count <- 0

  interface IDisposable with
    member this.Dispose() = this.Dispose()
