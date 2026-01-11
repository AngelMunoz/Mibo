module _3DSample.Grid

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Graphics3D

/// <summary>
/// Pre-calculates grid vertices for a set of platform bounds.
/// </summary>
let create (platforms: PlatformData list) (padding: float32) (color: Color) =
  let vertices = ResizeArray<VertexPositionColor>()

  for plat in platforms do
    let min = plat.Bounds.Min
    let max = plat.Bounds.Max
    let y = min.Y - 0.01f

    let startX = floor(min.X - padding)
    let endX = ceil(max.X + padding)
    let startZ = floor(min.Z - padding)
    let endZ = ceil(max.Z + padding)

    // X lines
    let mutable x = startX

    while x <= endX + 0.001f do
      vertices.Add(VertexPositionColor(Vector3(x, y, startZ), color))
      vertices.Add(VertexPositionColor(Vector3(x, y, endZ), color))
      x <- x + 1.0f

    // Z lines
    let mutable z = startZ

    while z <= endZ + 0.001f do
      vertices.Add(VertexPositionColor(Vector3(startX, y, z), color))
      vertices.Add(VertexPositionColor(Vector3(endX, y, z), color))
      z <- z + 1.0f

  let result = vertices.ToArray()
  result, result.Length / 2

/// <summary>
/// Draws the grid using a custom shader effect.
/// </summary>
let draw
  (playerPos: Vector3)
  (maxDist: float32)
  (effect: Effect)
  (vertices: VertexPositionColor[])
  (lineCount: int)
  (buffer: RenderBuffer<RenderCmd3D>)
  =

  if lineCount > 0 then
    let setup (e: Effect) (ctx: EffectContext) =
      e.Parameters.["World"].SetValue(ctx.World)
      e.Parameters.["View"].SetValue(ctx.View)
      e.Parameters.["Projection"].SetValue(ctx.Projection)
      e.Parameters.["PlayerPosition"].SetValue(playerPos)
      e.Parameters.["MaxDistance"].SetValue(maxDist)

    Draw3D.linesEffect
      Transparent
      effect
      (ValueSome setup)
      vertices
      lineCount
      buffer
