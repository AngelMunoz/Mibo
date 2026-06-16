namespace Mibo.Elmish.Next.Graphics2D

open System.Buffers
open Raylib_cs
open Mibo.Elmish.Next
open Mibo.Elmish.Next.Graphics2D.Base
open Mibo.Elmish.Graphics2D.Lighting

module ParticleDraw =

  let private pool = ArrayPool<ParticleData>.Create()
  let private rented = ResizeArray<ParticleData[]>()

  let returnAll() =
    for arr in rented do
      pool.Return(arr, false)

    rented.Clear()

  let particles
    (texture: Texture2D)
    (particles: Particle2D[])
    (count: int)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    let hTex = buffer.Textures.Register texture

    let data: ParticleData[] =
      if count > 0 then
        let arr = pool.Rent count
        rented.Add arr

        for i = 0 to count - 1 do
          let p = particles[i]

          arr[i] <-
            ({
              Position = p.Position
              Size = p.Size
              Rotation = p.Rotation
              SourceRect = Convert.toRect(p.SourceRect)
              Color = Convert.toColor(p.Color)
            }
            : ParticleData)

        arr
      else
        Array.empty

    buffer.Add(Command2D.Particle(hTex, data, count, layer))
    buffer
