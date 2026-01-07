namespace Mibo.Elmish.Graphics3D

open System
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

  let create(graphicsDevice: GraphicsDevice) =
    let vertices = Array.zeroCreate<VertexPositionTexture> 2048
    let indices = Array.zeroCreate<int16> 3072 // 6 indices per quad

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

  /// Begin a batch. Caller is responsible for configuring the effect before calling.
  let inline begin' (effect: Effect) (state: byref<State>) =
    state.QuadCount <- 0
    state.CurrentEffect <- effect

  let flush(state: byref<State>) =
    if state.QuadCount > 0 && not(isNull state.CurrentEffect) then
      let gd = state.GraphicsDevice
      gd.BlendState <- BlendState.AlphaBlend
      gd.DepthStencilState <- DepthStencilState.Default
      gd.SamplerStates.[0] <- SamplerState.PointClamp
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
    state.Vertices.[idx + 0] <-
      VertexPositionTexture(Vector3(x, y, z), Vector2(0.0f, 0.0f))
    // TR
    state.Vertices.[idx + 1] <-
      VertexPositionTexture(Vector3(x + w, y, z), Vector2(1.0f, 0.0f))
    // BR
    state.Vertices.[idx + 2] <-
      VertexPositionTexture(Vector3(x + w, y, z + h), Vector2(1.0f, 1.0f))
    // BL
    state.Vertices.[idx + 3] <-
      VertexPositionTexture(Vector3(x, y, z + h), Vector2(0.0f, 1.0f))

    state.QuadCount <- state.QuadCount + 1

  let inline end'(state: byref<State>) = flush &state
