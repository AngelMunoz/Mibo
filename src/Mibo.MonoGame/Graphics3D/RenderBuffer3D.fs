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
  let mutable clearCounter = 0
  let mutable postProcessCount = 0

  let ensureCapacity(needed: int) =
    if count + needed > items.Length then
      let newSize = max (items.Length * 2) (count + needed)

      let newArr = ArrayPool<Command3D>.Shared.Rent(newSize)

      Array.Copy(items, newArr, count)
      // clearArray = true: a Command3D holds managed refs to Model/Texture2D/Effect,
      // so the pooled array would keep them alive across frames if not cleared.
      // Matches RenderBuffer2D.ensureCapacity.
      ArrayPool<Command3D>.Shared.Return(items, clearArray = true)
      items <- newArr

  /// <summary>The number of commands currently in the buffer.</summary>
  member _.Count = count

  /// <summary>
  /// Number of <c>PostProcess</c> commands added since the last <c>Clear</c>. Lets a pipeline
  /// skip the post-process drain (and its per-frame allocation) when the view emits none.
  /// </summary>
  member _.PostProcessCount = postProcessCount

  /// <summary>Gets the command at the specified index.</summary>
  member _.Item(i: int) = items[i]

  /// <summary>Adds a render command to the buffer.</summary>
  member _.Add(cmd: Command3D) =
    ensureCapacity 1
    items[count] <- cmd

    match cmd with
    | Command3D.PostProcess _ -> postProcessCount <- postProcessCount + 1
    | _ -> ()

    count <- count + 1

  /// <summary>
  /// Clears all commands from the buffer without deallocating the backing array.
  /// Call this at the start of each frame before populating with new commands.
  /// </summary>
  /// <remarks>
  /// Resets the count every frame (clearing thousands of struct-DU slots per frame
  /// is a hot-path cost we avoid), but periodically zeroes the backing array (~every
  /// 300 frames) so stale managed refs (Model/Texture2D/Effect) in slots above count
  /// can't keep unloaded assets alive indefinitely after a scene shrinks. Dispose also
  /// clears. This matches <c>RenderBuffer2D.Clear</c> and the raylib buffers.
  /// </remarks>
  member _.Clear() =
    count <- 0
    postProcessCount <- 0
    // Periodically zero the backing array so stale managed refs (Model/Texture2D/Effect)
    // in slots above count don't keep unloaded assets alive indefinitely after a scene
    // shrinks or chunks evict. ~5s at 60fps; Array.Clear on structs is a cheap memset.
    clearCounter <- clearCounter + 1

    if clearCounter >= 300 then
      clearCounter <- 0
      Array.Clear(items, 0, items.Length)

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
