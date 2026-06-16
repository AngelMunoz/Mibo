namespace Mibo.Elmish.Next.Graphics2D

open System.Buffers

type ParticlePool() =
  let pool = ArrayPool<ParticleData>.Create()
  let rented = ResizeArray<ParticleData[]>()

  member _.Rent(count: int) =
    let arr = pool.Rent count
    rented.Add arr
    arr

  member _.ReturnAll() =
    for arr in rented do
      pool.Return(arr, false)

    rented.Clear()

/// <summary>
/// MonoGame-backed 2D render buffer.
/// Inherits the Core buffer logic and carries resource registries.
/// </summary>
type RenderBuffer2D(?capacity: int) =
  inherit RenderBuffer2DBase(?capacity = capacity)

  member val Textures = Mibo.Elmish.Next.MgTextureRegistry()
  member val Fonts = Mibo.Elmish.Next.MgFontRegistry()
  member val Shaders = Mibo.Elmish.Next.MgEffectRegistry()
  member val RenderTargets = Mibo.Elmish.Next.MgRenderTargetRegistry()
  member val LightContexts = MgLightContextRegistry()
  member val ParticlePool = ParticlePool()
