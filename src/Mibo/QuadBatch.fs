namespace Mibo.Elmish.Graphics3D

open System
open System.Buffers
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// A simple batcher for drawing 3D textured quads.
/// Quads are axis-aligned (not camera-facing like billboards).
module QuadBatch =

  [<Struct>]
  type State = {
    mutable Vertices: VertexPositionTexture[]
    mutable Indices: int16[]
    mutable QuadCount: int
    mutable CurrentEffect: Effect
    GraphicsDevice: GraphicsDevice
  }

  // 512 quads × 4 vertices = 2048, 512 quads × 6 indices = 3072
  [<Literal>]
  let private DefaultVertexCapacity = 2048

  [<Literal>]
  let private DefaultIndexCapacity = 3072

  let create(graphicsDevice: GraphicsDevice) =
    let vertices = ArrayPool.Shared.Rent DefaultVertexCapacity
    let indices = ArrayPool.Shared.Rent DefaultIndexCapacity

    // Pre-fill indices for max capacity
    for i in 0..511 do
      let vBase = i * 4
      let iBase = i * 6
      indices.[iBase + 0] <- int16(vBase + 0)
      indices.[iBase + 1] <- int16(vBase + 1)
      indices.[iBase + 2] <- int16(vBase + 2)
      indices.[iBase + 3] <- int16(vBase + 0)
      indices.[iBase + 4] <- int16(vBase + 2)
      indices.[iBase + 5] <- int16(vBase + 3)

    {
      Vertices = vertices
      Indices = indices
      QuadCount = 0
      CurrentEffect = null
      GraphicsDevice = graphicsDevice
    }

  /// Return pooled arrays. Call when the batch is no longer needed.
  let dispose(state: byref<State>) =
    if not(isNull state.Vertices) then
      ArrayPool.Shared.Return state.Vertices
      state.Vertices <- null

    if not(isNull state.Indices) then
      ArrayPool.Shared.Return state.Indices
      state.Indices <- null

  /// Begin a batch. Caller is responsible for configuring the effect before calling.
  let inline begin' (effect: Effect) (state: byref<State>) =
    state.QuadCount <- 0
    state.CurrentEffect <- effect

  let flush(state: byref<State>) =
    if state.QuadCount > 0 && not(isNull state.CurrentEffect) then
      let gd = state.GraphicsDevice
      gd.BlendState <- BlendState.AlphaBlend
      gd.DepthStencilState <- DepthStencilState.Default
      gd.SamplerStates[0] <- SamplerState.PointClamp
      gd.RasterizerState <- RasterizerState.CullNone

      for pass in state.CurrentEffect.CurrentTechnique.Passes do
        pass.Apply()

        gd.DrawUserIndexedPrimitives(
          PrimitiveType.TriangleList,
          state.Vertices,
          0,
          state.QuadCount * 4,
          state.Indices,
          0,
          state.QuadCount * 2
        )

  let draw (position: Vector3) (size: Vector2) (state: byref<State>) =
    // Auto-flush if buffer full
    if state.QuadCount >= 512 then
      flush &state
      state.QuadCount <- 0

    let w = size.X
    let h = size.Y
    let x = position.X
    let y = position.Y
    let z = position.Z

    let idx = state.QuadCount * 4

    // TL
    state.Vertices[idx + 0] <-
      VertexPositionTexture(Vector3(x, y, z), Vector2(0.0f, 0.0f))
    // TR
    state.Vertices[idx + 1] <-
      VertexPositionTexture(Vector3(x + w, y, z), Vector2(1.0f, 0.0f))
    // BR
    state.Vertices[idx + 2] <-
      VertexPositionTexture(Vector3(x + w, y, z + h), Vector2(1.0f, 1.0f))
    // BL
    state.Vertices[idx + 3] <-
      VertexPositionTexture(Vector3(x, y, z + h), Vector2(0.0f, 1.0f))

    state.QuadCount <- state.QuadCount + 1

  let inline end'(state: byref<State>) = flush &state
