module _3DSample.Platform

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

// ─────────────────────────────────────────────────────────────
// Platform System: Bounds computation and collision detection
// ─────────────────────────────────────────────────────────────

/// Compute bounding box from a 3D Model by scanning vertex data
let computeBounds(model: Model) : BoundingBox =
  let mutable min = Vector3(infinityf, infinityf, infinityf)
  let mutable max = Vector3(-infinityf, -infinityf, -infinityf)

  for mesh in model.Meshes do
    for part in mesh.MeshParts do
      let vertexSize = part.VertexBuffer.VertexDeclaration.VertexStride / 4
      let data = Array.zeroCreate<float32>(part.NumVertices * vertexSize)
      part.VertexBuffer.GetData(data)

      for i in 0 .. part.NumVertices - 1 do
        let idx = i * vertexSize
        let pos = Vector3(data.[idx], data.[idx + 1], data.[idx + 2])
        min <- Vector3.Min(min, pos)
        max <- Vector3.Max(max, pos)

  BoundingBox(min, max)

/// Create platform at position with bounds offset from base bounds
let create (baseBounds: BoundingBox) (pos: Vector3) : PlatformData =
  let min = baseBounds.Min + pos
  let max = baseBounds.Max + pos

  {
    Position = pos
    Bounds = BoundingBox(min, max)
  }

/// Check if player collides with any platform (from above, landing)
/// Returns Some(topY) if landing on a platform, None otherwise
let checkCollision
  (playerRadius: float32)
  (prevPos: Vector3)
  (newPos: Vector3)
  (platforms: PlatformData list)
  : float32 option =
  platforms
  |> List.tryPick(fun plat ->
    let topY = plat.Bounds.Max.Y
    let inX = newPos.X >= plat.Bounds.Min.X && newPos.X <= plat.Bounds.Max.X
    let inZ = newPos.Z >= plat.Bounds.Min.Z && newPos.Z <= plat.Bounds.Max.Z

    if inX && inZ then
      if prevPos.Y >= topY && newPos.Y < topY then
        Some topY
      elif
        newPos.Y >= plat.Bounds.Min.Y && newPos.Y <= topY + playerRadius
      then
        Some topY
      else
        None
    else
      None)

/// Level platform positions
let positions: Vector3 list = [
  Vector3(0f, -1f, 0f)
  Vector3(6f, 0f, 0f)
  Vector3(12f, 1f, 0f)
  Vector3(12f, 2f, -6f)
  Vector3(0f, 1f, -6f)
]
