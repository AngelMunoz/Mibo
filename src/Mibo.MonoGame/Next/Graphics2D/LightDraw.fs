namespace Mibo.Elmish.Next.Graphics2D

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Next
open Mibo.Elmish.Next.Graphics2D.Base

module LightDraw =

  let setAmbient
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>, ambient: AmbientLight2D)
    (buffer: RenderBuffer2D)
    =
    lightCtx.Ambient <- ambient.Color
    buffer.Add(Command2D.NoopLight layer)
    buffer

  let addPointLight
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (light: PointLight2D)
    (buffer: RenderBuffer2D)
    =
    lightCtx.PointLights.Add light
    buffer.Add(Command2D.NoopLight layer)
    buffer

  let addDirectionalLight
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (light: DirectionalLight2D)
    (buffer: RenderBuffer2D)
    =
    lightCtx.DirLights.Add light
    buffer.Add(Command2D.NoopLight layer)
    buffer

  let addOccluder
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (occluder: Occluder2D)
    (buffer: RenderBuffer2D)
    =
    lightCtx.Occluders.Add occluder
    buffer.Add(Command2D.NoopLight layer)
    buffer

  let litSprite
    (lightCtx: LightContext2D)
    (sprite: SpriteState)
    (buffer: RenderBuffer2D)
    =
    let hCtx = buffer.LightContexts.Register lightCtx
    let hTex = buffer.Textures.Register sprite.Texture

    let hNorm =
      match sprite.NormalMap with
      | ValueSome nm -> ValueSome(buffer.Textures.Register nm)
      | ValueNone -> ValueNone

    buffer.Add(
      Command2D.LitSprite(
        hCtx,
        hTex,
        Convert.toRect sprite.Dest,
        Convert.toRect sprite.Source,
        Convert.toSysVec2 sprite.Origin,
        sprite.Rotation,
        Convert.toColor sprite.Color,
        hNorm,
        sprite.Layer
      )
    )

    buffer

  let endLighting
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.EndLighting(buffer.LightContexts.Register lightCtx, layer)
    )

    buffer

  let enableShadows
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    lightCtx.ShadowsEnabled <- true

    buffer.Add(
      Command2D.EnableShadows(buffer.LightContexts.Register lightCtx, layer)
    )

    buffer

  let disableShadows
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    lightCtx.ShadowsEnabled <- false

    buffer.Add(
      Command2D.DisableShadows(buffer.LightContexts.Register lightCtx, layer)
    )

    buffer
