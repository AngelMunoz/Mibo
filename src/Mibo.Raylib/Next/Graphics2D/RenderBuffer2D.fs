namespace Mibo.Elmish.Next.Graphics2D

// ─────────────────────────────────────────────────────────────────
// Raylib RenderBuffer2D — subclass carrying resource registries
// ─────────────────────────────────────────────────────────────────

/// <summary>
/// Raylib-backed 2D render buffer.
/// Inherits the Core buffer logic and carries resource registries so
/// the DSL (<c>Draw.*</c>) can resolve native handles to opaque
/// <c>int&lt;Resource&gt;</c> indices without global state.
/// </summary>
type RenderBuffer2D(?capacity: int) =
  inherit RenderBuffer2DBase(?capacity = capacity)

  member val Textures = Mibo.Elmish.Next.RaylibTextureRegistry()
  member val Fonts = Mibo.Elmish.Next.RaylibFontRegistry()
  member val Shaders = Mibo.Elmish.Next.RaylibShaderRegistry()
  member val RenderTargets = Mibo.Elmish.Next.RaylibRenderTargetRegistry()
  member val LightContexts = Mibo.Elmish.Next.LightContextRegistry()
