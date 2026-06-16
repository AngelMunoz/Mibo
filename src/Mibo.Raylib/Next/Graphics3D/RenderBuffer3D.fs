namespace Mibo.Elmish.Next.Graphics3D

// ─────────────────────────────────────────────────────────────────
// Raylib RenderBuffer3D — subclass carrying resource registries
// ─────────────────────────────────────────────────────────────────

/// <summary>
/// Raylib-backed 3D render buffer.
/// Inherits the Core buffer logic and carries resource registries.
/// </summary>
type RenderBuffer3D(?capacity: int) =
  inherit RenderBuffer3DBase(?capacity = capacity)

  member val Textures = Mibo.Elmish.Next.RaylibTextureRegistry()
  member val Meshes = Mibo.Elmish.Next.RaylibMeshRegistry()
  member val Models = Mibo.Elmish.Next.RaylibModelRegistry()
