namespace Mibo.Elmish.Graphics3D

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// <summary>
/// A reusable fullscreen quad for post-process blits. Built against a graphics device on
/// first use; <see cref="M:Mibo.Elmish.Graphics3D.FullScreenQuad.Draw"/> applies an effect
/// over a two-triangle quad in clip space. The caller is responsible for binding the source
/// texture to a sampler and setting the active render target.
/// </summary>
type FullScreenQuad(gd: GraphicsDevice) =

  let verts: VertexPositionTexture[] = [|
    // position (clip space)       uv
    VertexPositionTexture(Vector3(-1.0f, -1.0f, 0.0f), Vector2(0.0f, 1.0f))
    VertexPositionTexture(Vector3(-1.0f, 1.0f, 0.0f), Vector2(0.0f, 0.0f))
    VertexPositionTexture(Vector3(1.0f, 1.0f, 0.0f), Vector2(1.0f, 0.0f))
    VertexPositionTexture(Vector3(1.0f, -1.0f, 0.0f), Vector2(1.0f, 1.0f))
  |]

  let indices: uint16[] = [| 0us; 1us; 2us; 0us; 2us; 3us |]

  let vb =
    new VertexBuffer(
      gd,
      typeof<VertexPositionTexture>,
      verts.Length,
      BufferUsage.WriteOnly
    )

  let ib =
    new IndexBuffer(
      gd,
      IndexElementSize.SixteenBits,
      indices.Length,
      BufferUsage.WriteOnly
    )

  do
    vb.SetData(verts)
    ib.SetData(indices)

  /// <summary>
  /// Draws the quad with <paramref name="effect"/> applied. The caller sets the render
  /// target and binds the source texture to the effect's sampler before calling.
  /// </summary>
  member _.Draw(effect: Effect) =
    gd.SetVertexBuffer(vb)
    gd.Indices <- ib

    for pass in effect.CurrentTechnique.Passes do
      pass.Apply()

      // Use the non-deprecated 4-arg overload: (primitiveType, baseVertex, startIndex, primitiveCount).
      gd.DrawIndexedPrimitives(
        PrimitiveType.TriangleList,
        0, // baseVertex
        0, // startIndex
        2 // primitiveCount -> 2 triangles (6 indices)
      )

  interface System.IDisposable with
    member _.Dispose() =
      vb.Dispose()
      ib.Dispose()
