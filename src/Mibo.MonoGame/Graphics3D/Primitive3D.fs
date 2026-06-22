namespace Mibo.Elmish.Graphics3D

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// <summary>
/// Effectless procedural geometry — the MonoGame analog of raylib's universal <c>Mesh</c>.
/// Wraps a <see cref="T:Microsoft.Xna.Framework.Graphics.VertexBuffer"/> +
/// <see cref="T:Microsoft.Xna.Framework.Graphics.IndexBuffer"/> plus a primitive count.
/// </summary>
/// <remarks>
/// <b>Per §4.1 of the monogame3d plan</b>, <c>PrimitiveMesh</c> is the unit that
/// <c>Material3D</c> (the PBR-param carrier) is allowed to pair with — unlike
/// <c>ModelMeshPart</c>, it owns no <c>Effect</c>, so there is no material conflation.
/// The pipeline (<c>ForwardPipeline</c>) binds the active camera matrices and lighting
/// onto a <c>BasicEffect</c> (B5/B6) or the custom PBR <c>Effect</c> (B9) when drawing.
/// </remarks>
type PrimitiveMesh = {

  /// <summary>Vertex buffer holding <see cref="T:Microsoft.Xna.Framework.Graphics.VertexPositionNormalTexture"/> vertices.</summary>
  Vertices: VertexBuffer

  /// <summary>Index buffer (16-bit, <c>IndexElementSize.SixteenBits</c>).</summary>
  Indices: IndexBuffer

  /// <summary>Number of triangles (primitive count for <c>DrawIndexedPrimitives</c>).</summary>
  PrimitiveCount: int
} with


  /// <summary>
  /// Draws this primitive with a configured effect. Binds the vertex/index buffers,
  /// applies each pass of the effect's current technique, and issues a single
  /// <c>DrawIndexedPrimitives</c> per pass. The caller is responsible for setting
  /// <c>World</c>/<c>View</c>/<c>Projection</c> (and lighting) on the effect first.
  /// </summary>
  member this.Draw(gd: GraphicsDevice, effect: Effect) =
    gd.SetVertexBuffer(this.Vertices)
    gd.Indices <- this.Indices

    for p in effect.CurrentTechnique.Passes do
      p.Apply()

      gd.DrawIndexedPrimitives(
        PrimitiveType.TriangleList,
        0, // baseVertex
        0, // startIndex
        this.PrimitiveCount
      )

  /// <summary>Releases the underlying GPU buffers. Idempotent.</summary>
  member this.Dispose() =
    // Dispose is safe to call multiple times in MonoGame.
    this.Vertices.Dispose()
    this.Indices.Dispose()

  interface IDisposable with
    member this.Dispose() = this.Dispose()


/// <summary>
/// Per-instance vertex data for hardware instancing: a 4×4 world matrix packed as four
/// <see cref="T:Microsoft.Xna.Framework.Vector4"/> rows (stream 1, instanceFrequency = 1).
/// </summary>
/// <remarks>
/// Matches <c>Shaders/Instanced.fx</c>'s <c>TEXCOORD1..4</c> inputs. The instance
/// <c>VertexDeclaration</c> declares usage indices 1–4 so it does not collide with
/// <see cref="T:Microsoft.Xna.Framework.Graphics.VertexPositionNormalTexture"/>'s own
/// <c>TEXCOORD0</c> on stream 0.
/// </remarks>
[<Struct>]
type VertexInstanceWorld = {

  /// <summary>Row 0 of the per-instance world matrix (M11, M12, M13, M14).</summary>
  Row0: Vector4

  /// <summary>Row 1 of the per-instance world matrix (M21, M22, M23, M24).</summary>
  Row1: Vector4

  /// <summary>Row 2 of the per-instance world matrix (M31, M32, M33, M34).</summary>
  Row2: Vector4

  /// <summary>Row 3 of the per-instance world matrix (M41, M42, M43, M44).</summary>
  Row3: Vector4
} with


  /// <summary>Constructs a per-instance vertex from a world <see cref="T:Microsoft.Xna.Framework.Matrix"/>.</summary>
  static member Create(world: Matrix) : VertexInstanceWorld = {
    // MonoGame Matrix is row-major; each row is exposed as a Vector4.
    Row0 = Vector4(world.M11, world.M12, world.M13, world.M14)
    Row1 = Vector4(world.M21, world.M22, world.M23, world.M24)
    Row2 = Vector4(world.M31, world.M32, world.M33, world.M34)
    Row3 = Vector4(world.M41, world.M42, world.M43, world.M44)
  }

  interface IVertexType with

    member this.VertexDeclaration =
      // Lazily-initialized static declaration. usageIndex 1–4 to avoid TEXCOORD0
      // (owned by the mesh's own texture coords on stream 0).
      match VertexInstanceWorld.s_declaration with
      | null ->
        let decl =
          new VertexDeclaration(
            sizeof<VertexInstanceWorld>,
            VertexElement(
              0,
              VertexElementFormat.Vector4,
              VertexElementUsage.TextureCoordinate,
              1
            ),
            VertexElement(
              16,
              VertexElementFormat.Vector4,
              VertexElementUsage.TextureCoordinate,
              2
            ),
            VertexElement(
              32,
              VertexElementFormat.Vector4,
              VertexElementUsage.TextureCoordinate,
              3
            ),
            VertexElement(
              48,
              VertexElementFormat.Vector4,
              VertexElementUsage.TextureCoordinate,
              4
            )
          )

        VertexInstanceWorld.s_declaration <- decl
        decl
      | decl -> decl

  /// <summary>Static backing field for the lazily-initialized <see cref="T:Microsoft.Xna.Framework.Graphics.VertexDeclaration"/>.</summary>
  static member val private s_declaration: VertexDeclaration =
    null with get, set


/// <summary>
/// Pre-generated primitive meshes for 3D rendering. The MonoGame analog of raylib's
/// <c>GenMeshCube</c>/<c>GenMeshSphere</c>/etc. — there is no native generator, so these
/// are built once from <see cref="T:Microsoft.Xna.Framework.Graphics.VertexPositionNormalTexture"/>
/// arrays.
/// </summary>
/// <remarks>
/// Unlike the raylib canonical (which exposes module-level <c>let</c> values), the MonoGame
/// port needs a <see cref="T:Microsoft.Xna.Framework.Graphics.GraphicsDevice"/> to construct
/// the GPU buffers, so call <c>Primitive3D.create gd</c> once at startup and hold the result.
/// Each mesh is unit-sized (cube 1³, sphere r=1, etc.); scale via the draw transform.
/// </remarks>
module Primitive3D =

  // ----------------------------------------------------------------
  // Internal vertex builders (return fresh arrays; the caller uploads them)
  // ----------------------------------------------------------------

  let private buildCube() : struct (VertexPositionNormalTexture[] * int[]) =
    // 24 vertices (4 per face, separate normals), 36 indices (6 faces × 2 tris).
    let verts = Array.zeroCreate<VertexPositionNormalTexture> 24
    let indices = Array.zeroCreate<int> 36
    let mutable v = 0
    let mutable i = 0

    // Each face: normal, then 4 corners (CCW when viewed from outside).
    let face
      (normal: Vector3)
      (v0: Vector3)
      (v1: Vector3)
      (v2: Vector3)
      (v3: Vector3)
      =
      let uv = [|
        Vector2(0f, 1f)
        Vector2(1f, 1f)
        Vector2(1f, 0f)
        Vector2(0f, 0f)
      |]

      let corners = [| v0; v1; v2; v3 |]

      for k = 0 to 3 do
        verts[v + k] <- VertexPositionNormalTexture(corners[k], normal, uv[k])

      // Two triangles: (v0,v1,v2) and (v0,v2,v3)
      indices[i + 0] <- v + 0
      indices[i + 1] <- v + 1
      indices[i + 2] <- v + 2
      indices[i + 3] <- v + 0
      indices[i + 4] <- v + 2
      indices[i + 5] <- v + 3
      v <- v + 4
      i <- i + 6

    // +X
    face
      Vector3.UnitX
      (Vector3(0.5f, -0.5f, 0.5f))
      (Vector3(0.5f, -0.5f, -0.5f))
      (Vector3(0.5f, 0.5f, -0.5f))
      (Vector3(0.5f, 0.5f, 0.5f))
    // -X
    face
      (-Vector3.UnitX)
      (Vector3(-0.5f, -0.5f, -0.5f))
      (Vector3(-0.5f, -0.5f, 0.5f))
      (Vector3(-0.5f, 0.5f, 0.5f))
      (Vector3(-0.5f, 0.5f, -0.5f))
    // +Y
    face
      Vector3.UnitY
      (Vector3(-0.5f, 0.5f, 0.5f))
      (Vector3(0.5f, 0.5f, 0.5f))
      (Vector3(0.5f, 0.5f, -0.5f))
      (Vector3(-0.5f, 0.5f, -0.5f))
    // -Y
    face
      (-Vector3.UnitY)
      (Vector3(-0.5f, -0.5f, -0.5f))
      (Vector3(0.5f, -0.5f, -0.5f))
      (Vector3(0.5f, -0.5f, 0.5f))
      (Vector3(-0.5f, -0.5f, 0.5f))
    // +Z
    face
      Vector3.UnitZ
      (Vector3(-0.5f, -0.5f, 0.5f))
      (Vector3(0.5f, -0.5f, 0.5f))
      (Vector3(0.5f, 0.5f, 0.5f))
      (Vector3(-0.5f, 0.5f, 0.5f))
    // -Z
    face
      (-Vector3.UnitZ)
      (Vector3(0.5f, -0.5f, -0.5f))
      (Vector3(-0.5f, -0.5f, -0.5f))
      (Vector3(-0.5f, 0.5f, -0.5f))
      (Vector3(0.5f, 0.5f, -0.5f))

    struct (verts, indices)

  let private buildPlane() : struct (VertexPositionNormalTexture[] * int[]) =
    // 1×1 plane on XY (normal +Z), 4 verts / 2 tris.
    let verts = [|
      VertexPositionNormalTexture(
        Vector3(-0.5f, -0.5f, 0f),
        Vector3.UnitZ,
        Vector2(0f, 1f)
      )
      VertexPositionNormalTexture(
        Vector3(0.5f, -0.5f, 0f),
        Vector3.UnitZ,
        Vector2(1f, 1f)
      )
      VertexPositionNormalTexture(
        Vector3(0.5f, 0.5f, 0f),
        Vector3.UnitZ,
        Vector2(1f, 0f)
      )
      VertexPositionNormalTexture(
        Vector3(-0.5f, 0.5f, 0f),
        Vector3.UnitZ,
        Vector2(0f, 0f)
      )
    |]

    let indices = [| 0; 1; 2; 0; 2; 3 |]
    struct (verts, indices)

  let private buildSphere
    (rings: int)
    (segments: int)
    : struct (VertexPositionNormalTexture[] * int[]) =
    // UV sphere. rings = latitude stacks, segments = longitude slices.
    let vertexCount = (rings + 1) * (segments + 1)
    let verts = Array.zeroCreate<VertexPositionNormalTexture> vertexCount
    let mutable vi = 0

    for ring = 0 to rings do
      let v0 = float32 ring / float32 rings
      let phi = v0 * float32 Math.PI // 0..PI from +Y to -Y
      let sinPhi = MathF.Sin(phi)
      let cosPhi = MathF.Cos(phi)

      for seg = 0 to segments do
        let u0 = float32 seg / float32 segments
        let theta = u0 * 2f * float32 Math.PI
        // Standard UV-sphere parametrization: y = cos(phi), horizontal ring scaled by sin(phi).
        // (The earlier draft swapped sin/cos, producing only the northern hemisphere — a dome.)
        let x = sinPhi * MathF.Sin(theta)
        let y = cosPhi
        let z = sinPhi * MathF.Cos(theta)

        verts[vi] <-
          VertexPositionNormalTexture(
            Vector3(x, y, z),
            Vector3(x, y, z),
            Vector2(u0, v0)
          )

        vi <- vi + 1

    let indexCount = rings * segments * 6
    let indices = Array.zeroCreate<int> indexCount
    let mutable ii = 0

    for ring = 0 to rings - 1 do
      for seg = 0 to segments - 1 do
        let a = ring * (segments + 1) + seg
        let b = a + 1
        let c = a + (segments + 1)
        let d = c + 1
        indices[ii + 0] <- a
        indices[ii + 1] <- b
        indices[ii + 2] <- c
        indices[ii + 3] <- c
        indices[ii + 4] <- b
        indices[ii + 5] <- d
        ii <- ii + 6

    struct (verts, indices)

  let private buildCylinder
    (segments: int)
    : struct (VertexPositionNormalTexture[] * int[]) =
    // Unit radius, unit height, centered on origin (Y from -0.5 to +0.5).
    // Side: 2 rings × (segments+1) verts. Caps: 2 fans, each (segments+1) ring verts + 1 center.
    let sideVerts = 2 * (segments + 1)
    let capVerts = 2 * ((segments + 1) + 1) // +2 center verts (one per cap)

    let verts =
      Array.zeroCreate<VertexPositionNormalTexture>(sideVerts + capVerts)

    let mutable vi = 0

    // Side ring (top y=+0.5, bottom y=-0.5), normal = radial.
    for seg = 0 to segments do
      let t = float32 seg / float32 segments
      let theta = t * 2f * float32 Math.PI
      let nx = MathF.Sin(theta)
      let nz = MathF.Cos(theta)
      let x = nx
      let z = nz

      verts[vi] <-
        VertexPositionNormalTexture(
          Vector3(x, 0.5f, z),
          Vector3(nx, 0f, nz),
          Vector2(t, 0f)
        )

      verts[vi + 1] <-
        VertexPositionNormalTexture(
          Vector3(x, -0.5f, z),
          Vector3(nx, 0f, nz),
          Vector2(t, 1f)
        )

      vi <- vi + 2

    let sideIndexCount = segments * 6
    let indices = Array.zeroCreate<int>(sideIndexCount + segments * 6)
    let mutable ii = 0

    for seg = 0 to segments - 1 do
      let i0 = seg * 2
      let i1 = i0 + 1
      let i2 = i0 + 2
      let i3 = i0 + 3
      indices[ii + 0] <- i0
      indices[ii + 1] <- i2
      indices[ii + 2] <- i1
      indices[ii + 3] <- i1
      indices[ii + 4] <- i2
      indices[ii + 5] <- i3
      ii <- ii + 6

    // Top cap fan (normal +Y), starting at sideVerts.
    let topBase = sideVerts

    for seg = 0 to segments do
      let t = float32 seg / float32 segments
      let theta = t * 2f * float32 Math.PI
      let x = MathF.Sin(theta)
      let z = MathF.Cos(theta)

      verts[vi] <-
        VertexPositionNormalTexture(
          Vector3(x, 0.5f, z),
          Vector3.UnitY,
          Vector2(t, 0f)
        )

      vi <- vi + 1

    // Top cap center vertex (referenced by the fan as topBase + segments + 1).
    verts[vi] <-
      VertexPositionNormalTexture(
        Vector3(0f, 0.5f, 0f),
        Vector3.UnitY,
        Vector2(0.5f, 0.5f)
      )

    vi <- vi + 1

    for seg = 0 to segments - 1 do
      indices[ii + 0] <- topBase + seg
      indices[ii + 1] <- topBase + seg + 1
      indices[ii + 2] <- topBase + segments + 1 // center
      ii <- ii + 3

    // Bottom cap fan (normal -Y), starting right after the top cap center (vi is authoritative).
    let botBase = vi

    for seg = 0 to segments do
      let t = float32 seg / float32 segments
      let theta = t * 2f * float32 Math.PI
      let x = MathF.Sin(theta)
      let z = MathF.Cos(theta)

      verts[vi] <-
        VertexPositionNormalTexture(
          Vector3(x, -0.5f, z),
          (-Vector3.UnitY),
          Vector2(t, 0f)
        )

      vi <- vi + 1

    // Bottom cap center vertex (referenced by the fan as botBase + segments + 1).
    verts[vi] <-
      VertexPositionNormalTexture(
        Vector3(0f, -0.5f, 0f),
        (-Vector3.UnitY),
        Vector2(0.5f, 0.5f)
      )

    vi <- vi + 1

    for seg = 0 to segments - 1 do
      indices[ii + 0] <- botBase + seg + 1
      indices[ii + 1] <- botBase + seg
      indices[ii + 2] <- botBase + segments + 1 // center
      ii <- ii + 3

    // Note: we reserved verts generously above; trim to actual count written.
    let actualVerts = vi
    let actualIndices = ii
    let trimmedVerts = verts[0 .. actualVerts - 1]
    let trimmedIndices = indices[0 .. actualIndices - 1]
    struct (trimmedVerts, trimmedIndices)

  let private buildCone
    (segments: int)
    : struct (VertexPositionNormalTexture[] * int[]) =
    // Unit radius base at y=-0.5, apex at y=+0.5.
    let sideVerts = (segments + 1) * 2 // base ring + apex ring (apex repeated per segment for hard normals)

    let verts =
      Array.zeroCreate<VertexPositionNormalTexture>(
        sideVerts + (segments + 1) + 1
      )

    let mutable vi = 0

    // Side: base ring (y=-0.5) + apex (y=+0.5), per-segment apex for per-face normals.
    for seg = 0 to segments do
      let t = float32 seg / float32 segments
      let theta = t * 2f * float32 Math.PI
      let bx = MathF.Sin(theta)
      let bz = MathF.Cos(theta)
      // Outward side normal for a unit cone (base radius 1 at y=-0.5, apex at y=+0.5):
      // the slant face's outward normal is (bx, 1, bz) normalized. The earlier draft used
      // Cross(up, tangent), which produced (-bx, 0, -bz) — radially inward. Fixed.
      let normal = Vector3.Normalize(Vector3(bx, 1f, bz))

      verts[vi] <-
        VertexPositionNormalTexture(
          Vector3(bx, -0.5f, bz),
          normal,
          Vector2(t, 1f)
        )

      verts[vi + 1] <-
        VertexPositionNormalTexture(
          Vector3(0f, 0.5f, 0f),
          normal,
          Vector2(t, 0f)
        )

      vi <- vi + 2

    // Side: 2 triangles (6 indices) per segment; base fan: 1 triangle (3 indices) per segment.
    // (The earlier draft set sideIndexCount = segments*3 but the side loop writes 6/segment,
    // underallocating the buffer and causing IndexOutOfRangeException in the base fan loop.)
    let sideIndexCount = segments * 6
    let indices = Array.zeroCreate<int>(sideIndexCount + segments * 3)
    let mutable ii = 0

    for seg = 0 to segments - 1 do
      let i0 = seg * 2
      let i1 = i0 + 1
      let i2 = i0 + 2
      let i3 = i0 + 3
      indices[ii + 0] <- i0
      indices[ii + 1] <- i3
      indices[ii + 2] <- i1
      indices[ii + 3] <- i0
      indices[ii + 4] <- i2
      indices[ii + 5] <- i3
      ii <- ii + 6

    // Base fan (normal -Y).
    let baseBase = vi

    for seg = 0 to segments do
      let t = float32 seg / float32 segments
      let theta = t * 2f * float32 Math.PI
      let x = MathF.Sin(theta)
      let z = MathF.Cos(theta)

      verts[vi] <-
        VertexPositionNormalTexture(
          Vector3(x, -0.5f, z),
          (-Vector3.UnitY),
          Vector2(t, 0f)
        )

      vi <- vi + 1

    // Base center vertex (referenced by the fan as baseBase + segments + 1).
    verts[vi] <-
      VertexPositionNormalTexture(
        Vector3(0f, -0.5f, 0f),
        (-Vector3.UnitY),
        Vector2(0.5f, 0.5f)
      )

    vi <- vi + 1

    for seg = 0 to segments - 1 do
      indices[ii + 0] <- baseBase + seg + 1
      indices[ii + 1] <- baseBase + seg
      indices[ii + 2] <- baseBase + segments + 1 // center
      ii <- ii + 3

    let actualVerts = vi
    let actualIndices = ii
    let trimmedVerts = verts[0 .. actualVerts - 1]
    let trimmedIndices = indices[0 .. actualIndices - 1]
    struct (trimmedVerts, trimmedIndices)

  let private buildTorus
    (rings: int)
    (segments: int)
    : struct (VertexPositionNormalTexture[] * int[]) =
    // Inner radius 0.5, outer radius 1.0 → tube radius 0.25, ring radius 0.75.
    let tubeR = 0.25f
    let ringR = 0.75f
    let vertexCount = (rings + 1) * (segments + 1)
    let verts = Array.zeroCreate<VertexPositionNormalTexture> vertexCount
    let mutable vi = 0

    for ring = 0 to rings do
      let v0 = float32 ring / float32 rings
      let phi = v0 * 2f * float32 Math.PI
      let sinPhi = MathF.Sin(phi)
      let cosPhi = MathF.Cos(phi)

      for seg = 0 to segments do
        let u0 = float32 seg / float32 segments
        let theta = u0 * 2f * float32 Math.PI
        let cx = MathF.Cos(theta) * ringR
        let cz = MathF.Sin(theta) * ringR
        let x = cx + cosPhi * tubeR * MathF.Cos(theta)
        let y = sinPhi * tubeR
        let z = cz + cosPhi * tubeR * MathF.Sin(theta)
        // Normal points from the ring center outward through the tube surface.
        let nx = cosPhi * MathF.Cos(theta)
        let ny = sinPhi
        let nz = cosPhi * MathF.Sin(theta)

        verts[vi] <-
          VertexPositionNormalTexture(
            Vector3(x, y, z),
            Vector3(nx, ny, nz),
            Vector2(u0, v0)
          )

        vi <- vi + 1

    let indexCount = rings * segments * 6
    let indices = Array.zeroCreate<int> indexCount
    let mutable ii = 0

    for ring = 0 to rings - 1 do
      for seg = 0 to segments - 1 do
        let a = ring * (segments + 1) + seg
        let b = a + 1
        let c = a + (segments + 1)
        let d = c + 1
        indices[ii + 0] <- a
        indices[ii + 1] <- b
        indices[ii + 2] <- c
        indices[ii + 3] <- c
        indices[ii + 4] <- b
        indices[ii + 5] <- d
        ii <- ii + 6

    struct (verts, indices)

  // ----------------------------------------------------------------
  // Upload helper
  // ----------------------------------------------------------------

  let private upload
    (gd: GraphicsDevice)
    (verts: VertexPositionNormalTexture[])
    (indices: int[])
    : PrimitiveMesh =
    let vb =
      new VertexBuffer(
        gd,
        typeof<VertexPositionNormalTexture>,
        verts.Length,
        BufferUsage.WriteOnly
      )

    vb.SetData(verts)

    // 16-bit indices suffice for all unit primitives (well under 65536 verts).
    let shortIndices = indices |> Array.map int16

    let ib =
      new IndexBuffer(
        gd,
        IndexElementSize.SixteenBits,
        shortIndices.Length,
        BufferUsage.WriteOnly
      )

    ib.SetData(shortIndices)

    {
      Vertices = vb
      Indices = ib
      PrimitiveCount = indices.Length / 3
    }

  // ----------------------------------------------------------------
  // Public API: build-all-once
  // ----------------------------------------------------------------

  /// <summary>A bundle of the six unit primitives. Build once and hold.</summary>
  type PrimitiveSet = {

    /// <summary>Unit cube (1×1×1, centered on origin).</summary>
    Cube: PrimitiveMesh

    /// <summary>Unit sphere (radius 1, 32×32 segments).</summary>
    Sphere: PrimitiveMesh

    /// <summary>Unit cylinder (radius 1, height 1, 32 segments).</summary>
    Cylinder: PrimitiveMesh

    /// <summary>Unit plane (1×1, on XY, normal +Z).</summary>
    Plane: PrimitiveMesh

    /// <summary>Torus (inner radius 0.5, outer 1.0, 32×32).</summary>
    Torus: PrimitiveMesh

    /// <summary>Unit cone (base radius 1, height 1, 32 segments).</summary>
    Cone: PrimitiveMesh
  }

  /// <summary>Creates all six unit primitives, uploading their GPU buffers once.</summary>
  /// <param name="gd">The graphics device used to allocate the vertex/index buffers.</param>
  /// <returns>A <see cref="T:Mibo.Elmish.Graphics3D.Primitive3D.PrimitiveSet"/> holding the six meshes.</returns>
  let create(gd: GraphicsDevice) : PrimitiveSet =
    let mk(builder: unit -> struct (VertexPositionNormalTexture[] * int[])) =
      let struct (v, i) = builder()
      upload gd v i

    {
      Cube = mk buildCube
      Sphere = mk(fun () -> buildSphere 32 32)
      Cylinder = mk(fun () -> buildCylinder 32)
      Plane = mk buildPlane
      Torus = mk(fun () -> buildTorus 32 32)
      Cone = mk(fun () -> buildCone 32)
    }
