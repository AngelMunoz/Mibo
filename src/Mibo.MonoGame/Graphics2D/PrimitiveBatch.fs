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
  let basicEffect = new BasicEffect(graphicsDevice)
  let mutable customEffect: Effect voption = ValueNone
  let vertices = ResizeArray<VertexPositionColor>(1024)
  let groups = ResizeArray<PrimitiveGroup>(64)
  let mutable projectionDirty = true
  let mutable isInBatch = false
  let mutable currentMatrix = Matrix.Identity
  let mutable currentViewportWidth = 0
  let mutable currentViewportHeight = 0
  let mutable currentBlend = BlendState.NonPremultiplied
  let mutable currentRasterizer = RasterizerState.CullNone
  let mutable currentLineWidth = 1.0f

  do
    basicEffect.VertexColorEnabled <- true
    basicEffect.TextureEnabled <- false
    basicEffect.Projection <- Matrix.Identity
    basicEffect.View <- Matrix.Identity
    basicEffect.World <- Matrix.Identity

  let ensureProjection() =
    let w = gd.Viewport.Width
    let h = gd.Viewport.Height

    if
      w <> currentViewportWidth || h <> currentViewportHeight || projectionDirty
    then
      currentViewportWidth <- w
      currentViewportHeight <- h
      projectionDirty <- false

      let proj =
        Matrix.CreateOrthographicOffCenter(
          0.0f,
          float32 w,
          float32 h,
          0.0f,
          0.0f,
          -1.0f
        )

      basicEffect.Projection <- proj

      match customEffect with
      | ValueSome effect ->
        let p = effect.Parameters["MatrixTransform"]

        if p <> null then
          p.SetValue(currentMatrix * proj)
      | ValueNone -> ()

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

      let activeEffect =
        match customEffect with
        | ValueSome e -> e
        | ValueNone -> basicEffect :> Effect

      // BasicEffect has .World directly; custom effects may use a MatrixTransform parameter.
      match customEffect with
      | ValueSome e ->
        let p = e.Parameters["World"]

        if p <> null then
          p.SetValue(currentMatrix)
      | ValueNone -> basicEffect.World <- currentMatrix

      let gdPrevBlend = gd.BlendState
      let gdPrevDepth = gd.DepthStencilState
      let gdPrevRaster = gd.RasterizerState

      gd.BlendState <- currentBlend
      gd.DepthStencilState <- DepthStencilState.None
      gd.RasterizerState <- currentRasterizer

      let vertexArr = vertices.ToArray()

      for i = 0 to groups.Count - 1 do
        let g = groups[i]
        let count = primitiveCountOf g

        if count > 0 then
          for pass in activeEffect.CurrentTechnique.Passes do
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

  /// <summary>Gets or sets the default thick-line width used by SetLineWidth.</summary>
  member _.LineWidth
    with get () = currentLineWidth
    and set (value: float32) = currentLineWidth <- value

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

  /// <summary>Replaces the effect used during flush.</summary>
  /// <remarks>ValueNone restores the internal BasicEffect.</remarks>
  member _.SetEffect(effect: Effect voption) =
    if isInBatch then
      flush()

    customEffect <- effect
    projectionDirty <- true

  /// <summary>Changes the blend state applied during flush.</summary>
  member _.SetBlendState(blend: BlendState) = currentBlend <- blend

  /// <summary>Changes the rasterizer state applied during flush.</summary>
  member _.SetRasterizerState(rasterizer: RasterizerState) =
    currentRasterizer <- rasterizer

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

  /// <summary>Adds a triangle fan by decomposing into a triangle list.</summary>
  /// <param name="points">
  /// <c>points[0]</c> is the fan center; <c>points[1..]</c> are the rim vertices
  /// in order. <c>points.Length</c> must be at least 3.
  /// </param>
  /// <param name="color">Vertex color applied to every generated vertex.</param>
  /// <param name="closeLoop">
  /// When <c>true</c> (default), the last rim vertex is connected back to
  /// <c>points[1]</c>, closing the fan into a loop. Use <c>false</c> for open
  /// arcs (e.g. a partial circle sector) where connecting the ends would draw
  /// an unwanted chord across the mouth.
  /// </param>
  member _.AddTriangleFan(points: Vector2[], color: Color, ?closeLoop: bool) =
    let close = defaultArg closeLoop true

    if points.Length < 3 then
      ()
    else
      let center = points[0]
      let rimCount = points.Length - 1

      closeCurrentGroupIfDifferent PrimitiveType.TriangleList

      let last = groups[groups.Count - 1]

      // Each rim vertex (1..rimCount-1) emits one triangle. When closing, an
      // extra triangle connects the last rim vertex back to points[1].
      let triCount = if close && rimCount >= 2 then rimCount else rimCount - 1

      groups[groups.Count - 1] <- {
        last with
            VertexCount = last.VertexCount + triCount * 3
      }

      for i = 1 to rimCount - 1 do
        let nextIdx = i + 1

        vertices.Add(
          VertexPositionColor(Vector3(center.X, center.Y, 0.0f), color)
        )

        vertices.Add(
          VertexPositionColor(Vector3(points[i].X, points[i].Y, 0.0f), color)
        )

        vertices.Add(
          VertexPositionColor(
            Vector3(points[nextIdx].X, points[nextIdx].Y, 0.0f),
            color
          )
        )

      if close && rimCount >= 2 then
        vertices.Add(
          VertexPositionColor(Vector3(center.X, center.Y, 0.0f), color)
        )

        vertices.Add(
          VertexPositionColor(
            Vector3(points[rimCount].X, points[rimCount].Y, 0.0f),
            color
          )
        )

        vertices.Add(
          VertexPositionColor(Vector3(points[1].X, points[1].Y, 0.0f), color)
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

  /// <summary>
  /// Adds a thick line as a quad (two triangles) with the given thickness and color.
  /// </summary>
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
      basicEffect.Dispose()
      customEffect <- ValueNone
      vertices.Clear()
      groups.Clear()
