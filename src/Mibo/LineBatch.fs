namespace Mibo.Elmish.Graphics3D

open System
open System.Buffers
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// <summary>
/// A simple batcher for drawing line primitives in 3D.
/// </summary>
/// <remarks>
/// Lines use <see cref="T:Microsoft.Xna.Framework.Graphics.VertexPositionColor"/> and are drawn
/// with <see cref="F:Microsoft.Xna.Framework.Graphics.PrimitiveType.LineList"/>.
/// </remarks>
module LineBatch =

  type State = {
    mutable Vertices: VertexPositionColor[]
    mutable LineCount: int
    GraphicsDevice: GraphicsDevice
  }

  let private ensureCapacity (numLines: int) (state: State) =
    let requiredVerts = (state.LineCount + numLines) * 2

    if requiredVerts > state.Vertices.Length then
      let newSize = max (state.Vertices.Length * 2) requiredVerts
      let newVerts = ArrayPool.Shared.Rent(newSize)
      state.Vertices.AsSpan().CopyTo(newVerts.AsSpan())
      ArrayPool.Shared.Return(state.Vertices)
      state.Vertices <- newVerts

  // 512 lines × 2 vertices = 1024
  [<Literal>]
  let private DefaultVertexCapacity = 1024

  /// <summary>Creates a new line batcher.</summary>
  let create(graphicsDevice: GraphicsDevice) = {
    Vertices = ArrayPool.Shared.Rent DefaultVertexCapacity
    LineCount = 0
    GraphicsDevice = graphicsDevice
  }

  /// <summary>Return pooled arrays. Call when the batch is no longer needed.</summary>
  let dispose(state: State) =
    if not(isNull state.Vertices) then
      ArrayPool.Shared.Return state.Vertices
      state.Vertices <- null

  /// <summary>Begin a batch.</summary>
  let inline begin'(state: State) = state.LineCount <- 0

  /// <summary>Adds a single line segment to the batch.</summary>
  let addLine (p1: Vector3) (p2: Vector3) (color: Color) (state: State) =
    ensureCapacity 1 state

    let idx = state.LineCount * 2
    state.Vertices[idx + 0] <- VertexPositionColor(p1, color)
    state.Vertices[idx + 1] <- VertexPositionColor(p2, color)

    state.LineCount <- state.LineCount + 1

  /// <summary>Adds multiple line segments from a pre-built vertex array.</summary>
  /// <param name="vertices">Array of vertices (2 per line segment).</param>
  /// <param name="lineCount">Number of line segments to add.</param>
  let addLines
    (vertices: VertexPositionColor[])
    (lineCount: int)
    (state: State)
    =
    ensureCapacity lineCount state

    let idx = state.LineCount * 2
    let vertsToCopy = lineCount * 2
    vertices.AsSpan(0, vertsToCopy).CopyTo(state.Vertices.AsSpan(idx))

    state.LineCount <- state.LineCount + lineCount

  /// <summary>Flushes the current batch to the GPU.</summary>
  /// <remarks>Effect passes are applied by the caller; this only issues the draw call.</remarks>
  let flush (effect: Effect) (state: State) =
    if state.LineCount > 0 then
      let gd = state.GraphicsDevice

      for pass in effect.CurrentTechnique.Passes do
        pass.Apply()

        gd.DrawUserPrimitives(
          PrimitiveType.LineList,
          state.Vertices,
          0,
          state.LineCount
        )

  /// <summary>Ends the batch and flushes all draw commands to the GPU.</summary>
  let inline end' (effect: Effect) (state: State) = flush effect state
