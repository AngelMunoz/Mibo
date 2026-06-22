namespace Mibo.Elmish.Graphics3D

open System
open System.Buffers
open System.Collections.Generic

/// <summary>
/// An allocation-friendly buffer for 3D render commands.
/// </summary>
/// <remarks>
/// Commands are accumulated each frame via <see cref="M:Mibo.Elmish.Graphics3D.RenderBuffer3D.Add"/>,
/// then executed in insertion order by the active pipeline.
/// The pipeline may re-sort internally if needed for state efficiency (e.g., front-to-back,
/// material batching), but the buffer itself does not impose an order.
///
/// Uses <see cref="T:System.Buffers.ArrayPool`1"/> for the backing store to avoid per-frame
/// heap allocations.
///
/// The buffer is designed to be cleared and repopulated each frame.
/// <see cref="M:Mibo.Elmish.Graphics3D.RenderBuffer3D.Clear"/> resets the count
/// without deallocating the internal array.
/// </remarks>
type RenderBuffer3D([<Struct>] ?capacity: int) =

  let mutable items =
    ArrayPool<Command3D>.Shared.Rent(defaultValueArg capacity 1024)

  let mutable count = 0

  let ensureCapacity(needed: int) =
    if count + needed > items.Length then
      let newSize = max (items.Length * 2) (count + needed)

      let newArr = ArrayPool<Command3D>.Shared.Rent(newSize)

      Array.Copy(items, newArr, count)
      ArrayPool<Command3D>.Shared.Return(items)
      items <- newArr

  /// <summary>The number of commands currently in the buffer.</summary>
  member _.Count = count

  /// <summary>Gets the command at the specified index.</summary>
  member _.Item(i: int) = items[i]

  /// <summary>Adds a render command to the buffer.</summary>
  member _.Add(cmd: Command3D) =
    ensureCapacity 1
    items[count] <- cmd
    count <- count + 1

  /// <summary>
  /// Clears all commands from the buffer without deallocating the backing array.
  /// Call this at the start of each frame before populating with new commands.
  /// </summary>
  member _.Clear() = count <- 0

  /// <summary>
  /// Sorts commands using the provided comparer.
  /// Pipelines may call this internally to optimize draw order.
  /// </summary>
  member _.Sort(comparer: IComparer<Command3D>) =
    Array.Sort(items, 0, count, comparer)

  interface System.IDisposable with
    member _.Dispose() =
      ArrayPool<Command3D>.Shared.Return(items, clearArray = true)
      items <- Array.empty
      count <- 0
