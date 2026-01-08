namespace Mibo.Elmish.Graphics3D

open System
open System.Buffers
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// <summary>
/// A batcher for drawing camera-facing billboards (particles, sprites in 3D space).
/// </summary>
/// <remarks>
/// Billboards always face the camera using camera right/up vectors.
/// </remarks>
module BillboardBatch =

  [<Struct>]
  type State = {
    mutable Vertices: VertexPositionColorTexture[]
    mutable Indices: int16[]
    mutable VertexBuffer: DynamicVertexBuffer
    mutable IndexBuffer: IndexBuffer
    mutable SpriteCount: int
    GraphicsDevice: GraphicsDevice
  }

  let private ensureBuffers(state: byref<State>) =
    if isNull state.VertexBuffer then
      state.VertexBuffer <-
        new DynamicVertexBuffer(
          state.GraphicsDevice,
          typeof<VertexPositionColorTexture>,
          state.Vertices.Length,
          BufferUsage.WriteOnly
        )

      // Pre-calculate indices
      for i = 0 to state.Vertices.Length / 4 - 1 do
        state.Indices[i * 6 + 0] <- int16(i * 4 + 0)
        state.Indices[i * 6 + 1] <- int16(i * 4 + 1)
        state.Indices[i * 6 + 2] <- int16(i * 4 + 2)
        state.Indices[i * 6 + 3] <- int16(i * 4 + 0)
        state.Indices[i * 6 + 4] <- int16(i * 4 + 2)
        state.Indices[i * 6 + 5] <- int16(i * 4 + 3)

      state.IndexBuffer <-
        new IndexBuffer(
          state.GraphicsDevice,
          typeof<int16>,
          state.Indices.Length,
          BufferUsage.WriteOnly
        )

      state.IndexBuffer.SetData(state.Indices)

  let private ensureCapacity (numSprites: int) (state: byref<State>) =
    let requiredVerts = (state.SpriteCount + numSprites) * 4

    if requiredVerts > state.Vertices.Length then
      let newSize = Math.Max(state.Vertices.Length * 2, requiredVerts)
      let newVerts = ArrayPool.Shared.Rent(newSize)
      state.Vertices.AsSpan().CopyTo(newVerts.AsSpan())
      ArrayPool.Shared.Return(state.Vertices)
      state.Vertices <- newVerts

      let newIndicesSize = (newSize / 4) * 6
      let newIndices = ArrayPool.Shared.Rent(newIndicesSize)
      state.Indices.AsSpan().CopyTo(newIndices.AsSpan())
      ArrayPool.Shared.Return state.Indices
      state.Indices <- newIndices

      // Re-fill indices
      for i = 0 to state.Vertices.Length / 4 - 1 do
        state.Indices[i * 6 + 0] <- int16(i * 4 + 0)
        state.Indices[i * 6 + 1] <- int16(i * 4 + 1)
        state.Indices[i * 6 + 2] <- int16(i * 4 + 2)
        state.Indices[i * 6 + 3] <- int16(i * 4 + 0)
        state.Indices[i * 6 + 4] <- int16(i * 4 + 2)
        state.Indices[i * 6 + 5] <- int16(i * 4 + 3)

      state.VertexBuffer <-
        new DynamicVertexBuffer(
          state.GraphicsDevice,
          typeof<VertexPositionColorTexture>,
          state.Vertices.Length,
          BufferUsage.WriteOnly
        )

      state.IndexBuffer <-
        new IndexBuffer(
          state.GraphicsDevice,
          typeof<int16>,
          state.Indices.Length,
          BufferUsage.WriteOnly
        )

      state.IndexBuffer.SetData(state.Indices)

  // 512 billboards × 4 vertices = 2048, 512 billboards × 6 indices = 3072
  [<Literal>]
  let private DefaultVertexCapacity = 2048

  [<Literal>]
  let private DefaultIndexCapacity = 3072

  /// <summary>Creates a new billboard batcher.</summary>
  let create(graphicsDevice: GraphicsDevice) = {
    Vertices = ArrayPool.Shared.Rent DefaultVertexCapacity
    Indices = ArrayPool.Shared.Rent DefaultIndexCapacity
    VertexBuffer = null
    IndexBuffer = null
    SpriteCount = 0
    GraphicsDevice = graphicsDevice
  }

  /// <summary>Return pooled arrays. Call when the batch is no longer needed.</summary>
  let dispose(state: byref<State>) =
    if not(isNull state.Vertices) then
      ArrayPool.Shared.Return state.Vertices
      state.Vertices <- null

    if not(isNull state.Indices) then
      ArrayPool.Shared.Return state.Indices
      state.Indices <- null

  /// <summary>Begin a batch.</summary>
  /// <remarks>Caller is responsible for configuring their effect before calling. The effect's first pass will be applied.</remarks>
  let inline begin' (effect: Effect) (state: byref<State>) =
    state.SpriteCount <- 0
    effect.CurrentTechnique.Passes.[0].Apply()

  /// <summary>Adds a billboard to the batch.</summary>
  let draw
    (position: Vector3)
    (size: Vector2)
    (rotation: float32)
    (color: Color)
    (camRight: Vector3)
    (camUp: Vector3)
    (state: byref<State>)
    =
    ensureCapacity 1 &state

    let halfSize = size * 0.5f

    // Apply rotation
    let cos = MathF.Cos rotation
    let sin = MathF.Sin rotation

    // Rotated basis vectors
    let rotRight = camRight * cos + camUp * sin
    let rotUp = camUp * cos - camRight * sin

    let w = rotRight * halfSize.X
    let h = rotUp * halfSize.Y

    // Quad vertices relative to center position
    let v0 = position - w + h // TopLeft
    let v1 = position + w + h // TopRight
    let v2 = position + w - h // BottomRight
    let v3 = position - w - h // BottomLeft

    let idx = state.SpriteCount * 4

    state.Vertices[idx + 0] <-
      VertexPositionColorTexture(v0, color, Vector2(0.0f, 0.0f))

    state.Vertices[idx + 1] <-
      VertexPositionColorTexture(v1, color, Vector2(1.0f, 0.0f))

    state.Vertices[idx + 2] <-
      VertexPositionColorTexture(v2, color, Vector2(1.0f, 1.0f))

    state.Vertices[idx + 3] <-
      VertexPositionColorTexture(v3, color, Vector2(0.0f, 1.0f))

    state.SpriteCount <- state.SpriteCount + 1

  /// <summary>Ends the batch and flushes all draw commands to the GPU.</summary>
  let end'(state: byref<State>) =
    if state.SpriteCount > 0 then
      ensureBuffers &state
      state.VertexBuffer.SetData(state.Vertices, 0, state.SpriteCount * 4)
      state.GraphicsDevice.SetVertexBuffer state.VertexBuffer
      state.GraphicsDevice.Indices <- state.IndexBuffer

      state.GraphicsDevice.DrawIndexedPrimitives(
        PrimitiveType.TriangleList,
        0,
        0,
        state.SpriteCount * 2
      )
