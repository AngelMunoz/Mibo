namespace Mibo.Elmish.Next.Graphics2D

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Next
open Mibo.Elmish.Next.Graphics2D.Base

module ParticleDraw =

  let particles
    (texture: Texture2D)
    (data: Particle2D[])
    (count: int)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    let hTex = buffer.Textures.Register texture

    let pData: ParticleData[] =
      if count > 0 then
        let arr = buffer.ParticlePool.Rent count

        for i = 0 to count - 1 do
          let p = data[i]

          arr[i] <-
            ({
              Position = Convert.toSysVec2 p.Position
              Size = Convert.toSysVec2 p.Size
              Rotation = p.Rotation
              SourceRect = Convert.toRect p.SourceRect
              Color = Convert.toColor p.Color
            }
            : ParticleData)

        arr
      else
        Array.empty

    buffer.Add(Command2D.Particle(hTex, pData, count, layer))
    buffer
