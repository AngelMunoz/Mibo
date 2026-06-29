namespace Mibo.Elmish.Graphics2D

open System
open System.Buffers

/// <summary>
/// An allocation-free buffer for 2D render commands, sorted by layer.
/// </summary>
/// <remarks>
/// Commands are accumulated each frame via <see cref="M:Mibo.Elmish.Graphics2D.RenderBuffer2D.Add"/>,
/// sorted by layer, then executed in order via pattern matching.
/// Uses <see cref="T:System.Buffers.ArrayPool`1"/> for the backing store to avoid per-frame
/// heap allocations.
///
/// The buffer is designed to be cleared and repopulated each frame.
/// <see cref="M:Mibo.Elmish.Graphics2D.RenderBuffer2D.Clear"/> resets the count
/// without deallocating the internal array.
/// </remarks>
type RenderBuffer2D
  (
  /// <summary>Initial capacity. Defaults to 1024 if not specified.</summary>
  ?capacity: int) =

  let initialCapacity = defaultArg capacity 1024
  let mutable items = ArrayPool<Command2D>.Shared.Rent(initialCapacity)
  let mutable keys = ArrayPool<int64>.Shared.Rent(initialCapacity)
  let mutable count = 0
  let mutable clearCounter = 0

  let getLayer(cmd: Command2D) =
    match cmd with
    | Command2D.Sprite(_, _, _, _, _, _, layer) -> layer
    | Command2D.Text(_, _, _, _, _, layer) -> layer
    // Rectangles
    | Command2D.FillRect(_, _, layer) -> layer
    | Command2D.RectOutline(_, _, _, layer) -> layer
    | Command2D.FillRectRounded(_, _, _, _, layer) -> layer
    | Command2D.RectRoundedOutline(_, _, _, _, _, layer) -> layer
    | Command2D.RectGradientV(_, _, _, _, _, _, layer) -> layer
    | Command2D.RectGradientH(_, _, _, _, _, _, layer) -> layer
    | Command2D.RectGradient(_, _, _, _, _, layer) -> layer
    // Circles & Ellipses
    | Command2D.FillCircle(_, _, _, layer) -> layer
    | Command2D.CircleOutline(_, _, _, layer) -> layer
    | Command2D.CircleSector(_, _, _, _, _, _, layer) -> layer
    | Command2D.CircleSectorOutline(_, _, _, _, _, _, layer) -> layer
    | Command2D.CircleGradient(_, _, _, _, _, layer) -> layer
    | Command2D.FillRing(_, _, _, _, _, _, _, layer) -> layer
    | Command2D.RingOutline(_, _, _, _, _, _, _, layer) -> layer
    | Command2D.FillEllipse(_, _, _, _, _, layer) -> layer
    | Command2D.EllipseOutline(_, _, _, _, _, layer) -> layer
    // Lines & Curves
    | Command2D.Line(_, _, _, layer) -> layer
    | Command2D.LineThick(_, _, _, _, layer) -> layer
    | Command2D.LineStrip(_, _, layer) -> layer
    | Command2D.Bezier(_, _, _, _, _, layer) -> layer
    // Triangles & Polygons
    | Command2D.Triangle(_, _, _, _, layer) -> layer
    | Command2D.TriangleFan(_, _, layer) -> layer
    | Command2D.TriangleStrip(_, _, layer) -> layer
    | Command2D.FillPoly(_, _, _, _, _, layer) -> layer
    | Command2D.PolyOutline(_, _, _, _, _, _, layer) -> layer
    // Camera, Targets, Shaders, State
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
    // Escape Hatches
    | Command2D.DrawImmediate(_, layer) -> layer
    | Command2D.Clear(_, layer) -> layer
    // Lighting
    | Command2D.NoopLight layer -> layer
    | Command2D.LitSprite(_, sprite) -> sprite.Layer
    | Command2D.EndLighting(_, layer) -> layer
    | Command2D.EnableShadows(_, layer) -> layer
    | Command2D.DisableShadows(_, layer) -> layer
    | Command2D.Particle(_, _, _, layer) -> layer

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
  /// Clears all commands from the buffer without deallocating the backing array.
  /// Call this at the start of each frame before populating with new commands.
  /// </summary>
  member _.Clear() =
    count <- 0
    clearCounter <- clearCounter + 1

    if clearCounter >= 300 then
      clearCounter <- 0
      Array.Clear(items, 0, items.Length)

  /// <summary>
  /// Sorts commands by layer in ascending order, preserving insertion order for same-layer commands.
  /// Uses precomputed int64 keys (layer in high 32 bits, insertion index in low 32 bits) to avoid
  /// repeated pattern matching during comparisons and guarantee stable sort.
  /// Must be called after <see cref="M:Mibo.Elmish.Graphics2D.RenderBuffer2D.Clear"/>
  /// and population, before iteration.
  /// </summary>
  member _.Sort() = Array.Sort(keys, items, 0, count)

  interface IDisposable with
    member _.Dispose() =
      if items.Length > 0 then
        let toReturnItems = items
        let toReturnKeys = keys
        items <- Array.empty<Command2D>
        keys <- Array.empty<int64>
        ArrayPool<Command2D>.Shared.Return(toReturnItems, true)
        ArrayPool<int64>.Shared.Return(toReturnKeys)
