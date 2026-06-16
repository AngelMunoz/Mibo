namespace Mibo.Elmish

open System
open System.Collections.Generic

/// <summary>
/// Context passed to <c>init</c>, <c>update</c>, and <c>subscribe</c> functions, providing access
/// to game services and runtime information.
/// </summary>
/// <remarks>
/// This is the primary way to access game services (assets, input, etc.) from within
/// the Elmish architecture. Use <see cref="M:Mibo.Elmish.GameContext.getService"/> or the typed accessor
/// conveniences to retrieve registered services.
/// </remarks>
type GameContext internal (width: int, height: int) =
  let services = Dictionary<Type, obj>()
  let mutable windowWidth = width
  let mutable windowHeight = height

  member val internal Services = services

  /// <summary>Current window width in pixels.</summary>
  member _.WindowWidth = windowWidth

  /// <summary>Current window height in pixels.</summary>
  member _.WindowHeight = windowHeight

  member internal ctx.UpdateDimensions(w: int, h: int) =
    windowWidth <- w
    windowHeight <- h

/// Functions for accessing and managing services in the GameContext.
module GameContext =
  let internal create(width: int, height: int) = GameContext(width, height)

  let internal register<'T> (svc: 'T) (ctx: GameContext) =
    ctx.Services[typeof<'T>] <- box svc

  /// <summary>Attempts to get a registered service by type.</summary>
  /// <returns><c>ValueSome</c> if the service is registered, <c>ValueNone</c> otherwise.</returns>
  let tryGetService<'T>(ctx: GameContext) : 'T voption =
    match ctx.Services.TryGetValue(typeof<'T>) with
    | true, (:? 'T as svc) -> ValueSome svc
    | _ -> ValueNone

  /// <summary>Gets a registered service by type.</summary>
  /// <exception cref="T:System.InvalidOperationException">Thrown when the service is not registered.</exception>
  let getService<'T>(ctx: GameContext) : 'T =
    match tryGetService<'T> ctx with
    | ValueSome svc -> svc
    | ValueNone ->
      failwithf "Service %s not registered in GameContext" typeof<'T>.Name

/// <summary>
/// Interface for renderers that draw the model state each frame.
/// </summary>
type IRenderer<'Model> =
  abstract Draw: GameContext * 'Model * GameTime -> unit

/// <summary>
/// A small, allocation-friendly buffer that stores render commands tagged with a sort key.
/// </summary>
/// <remarks>
/// This is the core data structure for deferred rendering. Commands are accumulated
/// during the view phase and then sorted/executed by the renderer.
/// </remarks>
/// <typeparam name="Key">The sort key type (e.g., <c>int&lt;RenderLayer&gt;</c> for 2D, <c>unit</c> for 3D)</typeparam>
/// <typeparam name="Cmd">The render command type</typeparam>
type RenderBuffer<'Key, 'Cmd when 'Key: comparison>
  (?capacity: int, ?keyComparer: IComparer<'Key>) =

  let initialCapacity = defaultArg capacity 1024

  let mutable items =
    System.Buffers.ArrayPool<struct ('Key * 'Cmd)>.Shared.Rent initialCapacity

  let mutable count = 0
  let mutable disposed = false
  let keyComparer = defaultArg keyComparer Comparer<'Key>.Default

  let sortComparer =
    { new IComparer<struct ('Key * 'Cmd)> with
        member _.Compare(struct (k1, _), struct (k2, _)) =
          keyComparer.Compare(k1, k2)
    }

  let ensureCapacity(needed: int) =
    if count + needed > items.Length then
      let newSize = max (items.Length * 2) (count + needed)

      let newArr =
        System.Buffers.ArrayPool<struct ('Key * 'Cmd)>.Shared.Rent(newSize)

      items.AsSpan(0, count).CopyTo(newArr.AsSpan())
      System.Buffers.ArrayPool<struct ('Key * 'Cmd)>.Shared.Return items
      items <- newArr

  /// Clears all commands from the buffer without deallocating.
  member _.Clear() = count <- 0

  /// Adds a command with its sort key to the buffer.
  member _.Add(key: 'Key, cmd: 'Cmd) =
    ensureCapacity 1
    items[count] <- struct (key, cmd)
    count <- count + 1

  /// Sorts the buffer by key. Call this before iterating if order matters.
  member _.Sort() =
    System.Array.Sort(items, 0, count, sortComparer)

  /// The number of commands currently in the buffer.
  member _.Count = count

  /// Gets the command at the specified index as a (key, command) struct tuple.
  member _.Item(i) = items[i]

  /// <summary>
  /// Returns the rented backing array to <see cref="T:System.Buffers.ArrayPool`1"/>.
  /// Call when the buffer is no longer needed (typically from a renderer's
  /// <c>Dispose</c>). After disposal the buffer must not be used.
  /// </summary>
  member _.Dispose() =
    if not disposed then
      disposed <- true
      System.Buffers.ArrayPool<struct ('Key * 'Cmd)>.Shared.Return items

  interface IDisposable with
    member this.Dispose() = this.Dispose()
