namespace Mibo.Elmish

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// <summary>
/// Context passed to <c>init</c> and <c>subscribe</c> functions, providing access to MonoGame resources.
/// </summary>
/// <remarks>
/// This is the primary way to access game services and content from within
/// the Elmish architecture.
/// </remarks>
type GameContext = {
  /// The MonoGame graphics device for rendering operations.
  GraphicsDevice: GraphicsDevice
  /// The content manager for loading compiled game assets.
  Content: Content.ContentManager
  /// The Game instance for accessing services and components.
  Game: Game
}

/// <summary>
/// Interface for renderers that draw the model state each frame.
/// </summary>
type IRenderer<'Model> =
  abstract member Draw: GameContext * 'Model * GameTime -> unit

/// <summary>
/// A small, allocation-friendly buffer that stores render commands tagged with a sort key.
/// </summary>
/// <remarks>
/// This is the core data structure for deferred rendering. Commands are accumulated
/// during the view phase and then sorted/executed by the renderer.
/// </remarks>
/// <typeparam name="Key">The sort key type (e.g., <c>int&lt;RenderLayer&gt;</c> for 2D, <c>unit</c> for 3D)</typeparam>
/// <typeparam name="Cmd">The render command type</typeparam>
type RenderBuffer<'Key, 'Cmd>(?capacity: int, ?keyComparer: IComparer<'Key>) =
  let initialCapacity = defaultArg capacity 1024

  let mutable items =
    Buffers.ArrayPool<struct ('Key * 'Cmd)>.Shared.Rent initialCapacity

  let mutable count = 0
  let keyComparer = defaultArg keyComparer Comparer<'Key>.Default

  let comparer =
    { new IComparer<struct ('Key * 'Cmd)> with
        member _.Compare(x, y) =
          let struct (kx, _) = x
          let struct (ky, _) = y
          keyComparer.Compare(kx, ky)
    }

  let ensureCapacity(needed: int) =
    if count + needed > items.Length then
      let newSize = max (items.Length * 2) (count + needed)
      let newArr = Buffers.ArrayPool<struct ('Key * 'Cmd)>.Shared.Rent(newSize)
      items.AsSpan(0, count).CopyTo(newArr.AsSpan())
      Buffers.ArrayPool<struct ('Key * 'Cmd)>.Shared.Return(items)
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
    // Sort only the used portion via Span
    let span = items.AsSpan(0, count)
    span.Sort comparer

  /// The number of commands currently in the buffer.
  member _.Count = count

  /// Gets the command at the specified index as a (key, command) struct tuple.
  member _.Item(i) = items[i]
