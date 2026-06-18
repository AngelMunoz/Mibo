namespace Mibo.Elmish.Graphics2D

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// <summary>
/// A primitive group tracked for flushing.
/// </summary>
[<Struct>]
type private PrimitiveGroup = {
  PrimitiveType: PrimitiveType
  StartVertex: int
  VertexCount: int
}

/// <summary>
/// Batch renderer for 2D primitive shapes using vertex arrays and
/// <c>GraphicsDevice.DrawUserPrimitives</c>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors raylib's primitive rendering (circles, rings, triangles, lines,
/// polygons, etc.) on top of MonoGame. The batch accumulates vertices in
/// managed arrays and flushes them as <c>PrimitiveType</c> groups. This avoids
/// per-geometry GPU resource allocation; <c>DrawUserPrimitives</c> copies the
/// current vertex span to the device on flush.
/// </para>
/// <para>
/// The renderer owns one <c>PrimitiveBatch</c> alongside its <c>SpriteBatch</c>.
/// State transitions (camera changes, <c>DrawImmediate</c>) flush both batches
/// before re-entering with updated transform settings.
/// </para>
/// </remarks>
type PrimitiveBatch(graphicsDevice: GraphicsDevice) =
  let gd = graphicsDevice
  let effect = new BasicEffect(graphicsDevice)
  let vertices = ResizeArray<VertexPositionColor>(1024)
  let groups = ResizeArray<PrimitiveGroup>(64)
  let mutable projectionDirty = true
  let mutable isInBatch = false
  let mutable currentMatrix = Matrix.Identity
  let mutable currentViewportWidth = 0
  let mutable currentViewportHeight = 0

  do
    effect.VertexColorEnabled <- true
    effect.TextureEnabled <- false
    effect.Projection <- Matrix.Identity
    effect.View <- Matrix.Identity
    effect.World <- Matrix.Identity

  let ensureProjection() =
    let w = gd.Viewport.Width
    let h = gd.Viewport.Height

    if
      w <> currentViewportWidth || h <> currentViewportHeight || projectionDirty
    then
      currentViewportWidth <- w
      currentViewportHeight <- h
      projectionDirty <- false

      effect.Projection <-
        Matrix.CreateOrthographicOffCenter(
          0.0f,
          float32 w,
          float32 h,
          0.0f,
          0.0f,
          -1.0f
        )

  let closeCurrentGroupIfDifferent(pt: PrimitiveType) =
    if groups.Count > 0 then
      let last = groups[groups.Count - 1]

      if last.PrimitiveType <> pt then
        let start = last.StartVertex + last.VertexCount

        groups.Add(
          {
            PrimitiveType = pt
            StartVertex = start
            VertexCount = 0
          }
        )
    else
      groups.Add(
        {
          PrimitiveType = pt
          StartVertex = 0
          VertexCount = 0
        }
      )

  let primitiveCountOf(g: PrimitiveGroup) =
    if g.VertexCount <= 0 then
      0
    else
      match g.PrimitiveType with
      | PrimitiveType.LineList -> g.VertexCount / 2
      | PrimitiveType.LineStrip when g.VertexCount >= 2 -> g.VertexCount - 1
      | PrimitiveType.TriangleList -> g.VertexCount / 3
      | PrimitiveType.TriangleStrip when g.VertexCount >= 3 -> g.VertexCount - 2
      | _ -> 0

  let flush() =
    if vertices.Count > 0 && groups.Count > 0 then
      ensureProjection()
      effect.World <- currentMatrix

      let gdPrevBlend = gd.BlendState
      let gdPrevDepth = gd.DepthStencilState
      let gdPrevRaster = gd.RasterizerState

      gd.BlendState <- BlendState.NonPremultiplied
      gd.DepthStencilState <- DepthStencilState.None
      gd.RasterizerState <- RasterizerState.CullNone

      let vertexArr = vertices.ToArray()

      for i = 0 to groups.Count - 1 do
        let g = groups[i]
        let count = primitiveCountOf g

        if count > 0 then
          for pass in effect.CurrentTechnique.Passes do
            pass.Apply()

            gd.DrawUserPrimitives(
              g.PrimitiveType,
              vertexArr,
              g.StartVertex,
              count
            )
            |> ignore

      gd.BlendState <- gdPrevBlend
      gd.DepthStencilState <- gdPrevDepth
      gd.RasterizerState <- gdPrevRaster

      vertices.Clear()
      groups.Clear()

  /// <summary>Begins a batch with the given world/camera transform.</summary>
  member _.Begin(matrix: Matrix) =
    if isInBatch then
      failwith "PrimitiveBatch.Begin called while already in a batch"

    projectionDirty <- true
    isInBatch <- true
    currentMatrix <- matrix
    vertices.Clear()
    groups.Clear()

  /// <summary>Ends the batch, flushing any remaining vertices.</summary>
  member _.End() =
    if not isInBatch then
      failwith "PrimitiveBatch.End called without matching Begin"

    flush()
    isInBatch <- false

  /// <summary>Changes the active world/camera transform mid-batch.</summary>
  /// <remarks>Flushes pending vertices, updates the transform, then resumes.</remarks>
  member _.SetTransform(matrix: Matrix) =
    if not isInBatch then
      failwith "PrimitiveBatch.SetTransform called outside of a batch"

    flush()
    currentMatrix <- matrix

  /// <summary>Flushes all buffered primitive groups to the GPU.</summary>
  member _.Flush() =
    if isInBatch then
      flush()

  /// <summary>Adds a single line segment with the given color.</summary>
  member _.AddLine(start: Vector2, ``end``: Vector2, color: Color) =
    closeCurrentGroupIfDifferent PrimitiveType.LineList

    let last = groups[groups.Count - 1]

    groups[groups.Count - 1] <- {
      last with
          VertexCount = last.VertexCount + 2
    }

    vertices.Add(VertexPositionColor(Vector3(start.X, start.Y, 0.0f), color))

    vertices.Add(
      VertexPositionColor(Vector3(``end``.X, ``end``.Y, 0.0f), color)
    )

  /// <summary>Adds a line strip with the given color.</summary>
  member _.AddLineStrip(points: Vector2[], color: Color) =
    if points.Length < 2 then
      ()
    else
      closeCurrentGroupIfDifferent PrimitiveType.LineStrip

      let last = groups[groups.Count - 1]

      groups[groups.Count - 1] <- {
        last with
            VertexCount = last.VertexCount + points.Length
      }

      for i = 0 to points.Length - 1 do
        vertices.Add(
          VertexPositionColor(Vector3(points[i].X, points[i].Y, 0.0f), color)
        )

  /// <summary>Adds a triangle list with the given vertices.</summary>
  member _.AddTriangles(verts: VertexPositionColor[]) =
    if verts.Length = 0 || verts.Length % 3 <> 0 then
      ()
    else
      closeCurrentGroupIfDifferent PrimitiveType.TriangleList

      let last = groups[groups.Count - 1]

      groups[groups.Count - 1] <- {
        last with
            VertexCount = last.VertexCount + verts.Length
      }

      for i = 0 to verts.Length - 1 do
        vertices.Add(verts[i])

  /// <summary>Adds a triangle fan by decomposing into triangle list.</summary>
  member _.AddTriangleFan(points: Vector2[], color: Color) =
    if points.Length < 3 then
      ()
    else
      let center = points[0]
      let rimCount = points.Length - 1

      closeCurrentGroupIfDifferent PrimitiveType.TriangleList

      let last = groups[groups.Count - 1]

      groups[groups.Count - 1] <- {
        last with
            VertexCount = last.VertexCount + rimCount * 3
      }

      for i = 1 to rimCount do
        let next = if i = rimCount then 1 else i + 1

        vertices.Add(
          VertexPositionColor(Vector3(center.X, center.Y, 0.0f), color)
        )

        vertices.Add(
          VertexPositionColor(Vector3(points[i].X, points[i].Y, 0.0f), color)
        )

        vertices.Add(
          VertexPositionColor(
            Vector3(points[next].X, points[next].Y, 0.0f),
            color
          )
        )

  /// <summary>Adds a triangle strip with the given color.</summary>
  member _.AddTriangleStrip(points: Vector2[], color: Color) =
    if points.Length < 3 then
      ()
    else
      closeCurrentGroupIfDifferent PrimitiveType.TriangleStrip

      let last = groups[groups.Count - 1]

      groups[groups.Count - 1] <- {
        last with
            VertexCount = last.VertexCount + points.Length
      }

      for i = 0 to points.Length - 1 do
        vertices.Add(
          VertexPositionColor(Vector3(points[i].X, points[i].Y, 0.0f), color)
        )

  /// <summary>Adds a thick line as a quad (two triangles) with the given thickness and color.</summary>
  member this.AddLineThick
    (start: Vector2, ``end``: Vector2, thickness: float32, color: Color)
    =
    let dx = ``end``.X - start.X
    let dy = ``end``.Y - start.Y
    let len = sqrt(dx * dx + dy * dy)

    if len <= 0.0f then
      ()
    else
      let nx = -dy / len
      let ny = dx / len
      let half = thickness * 0.5f
      let offX = nx * half
      let offY = ny * half

      let v1 =
        VertexPositionColor(
          Vector3(start.X + offX, start.Y + offY, 0.0f),
          color
        )

      let v2 =
        VertexPositionColor(
          Vector3(``end``.X + offX, ``end``.Y + offY, 0.0f),
          color
        )

      let v3 =
        VertexPositionColor(
          Vector3(``end``.X - offX, ``end``.Y - offY, 0.0f),
          color
        )

      let v4 =
        VertexPositionColor(
          Vector3(start.X - offX, start.Y - offY, 0.0f),
          color
        )

      this.AddTriangles([| v1; v2; v3; v1; v3; v4 |])

  interface IDisposable with
    member _.Dispose() =
      effect.Dispose()
      vertices.Clear()
      groups.Clear()
