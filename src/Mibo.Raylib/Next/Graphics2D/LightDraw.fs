namespace Mibo.Elmish.Next.Graphics2D

open Mibo.Elmish.Next.Graphics2D.Base

open System.Numerics
open Raylib_cs
open Mibo.Elmish.Next
open Mibo.Elmish.Next.Animation
open Mibo.Elmish.Next.Graphics2D.Lighting

module LightDraw =

  let inline setAmbient
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>, ambient: AmbientLight2D)
    (buffer: RenderBuffer2D)
    =
    lightCtx.Ambient <- ambient.Color
    buffer.Add(Command2D.NoopLight layer)
    buffer

  let inline addPointLight
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (light: PointLight2D)
    (buffer: RenderBuffer2D)
    =
    lightCtx.PointLights.Add light
    buffer.Add(Command2D.NoopLight layer)
    buffer

  let inline addDirectionalLight
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (light: DirectionalLight2D)
    (buffer: RenderBuffer2D)
    =
    lightCtx.DirLights.Add light
    buffer.Add(Command2D.NoopLight layer)
    buffer

  let inline addOccluder
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (occluder: Occluder2D)
    (buffer: RenderBuffer2D)
    =
    lightCtx.Occluders.Add occluder
    buffer.Add(Command2D.NoopLight layer)
    buffer

  let inline litSprite
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
        sprite.Origin,
        sprite.Rotation,
        Convert.toColor sprite.Color,
        hNorm,
        sprite.Layer
      )
    )

    buffer

  let inline litAnimatedSprite
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (dest: Rectangle)
    (animSprite: AnimatedSprite)
    (buffer: RenderBuffer2D)
    =
    let src = AnimatedSprite.currentSource animSprite

    let src = {
      src with
          Width = if animSprite.FlipX then -src.Width else src.Width
          Height = if animSprite.FlipY then -src.Height else src.Height
    }

    litSprite
      lightCtx
      ({
        Texture = buffer.Textures.Resolve animSprite.Sheet.Texture
        Dest = dest
        Source = Convert.toRaylibRect src
        Origin = animSprite.Sheet.Origin
        Rotation = animSprite.Rotation
        Color = Convert.toRaylibColor animSprite.Color
        Layer = layer
        NormalMap =
          animSprite.Sheet.NormalMap |> ValueOption.map buffer.Textures.Resolve
      })
      buffer

  let inline endLighting
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.EndLighting(buffer.LightContexts.Register lightCtx, layer)
    )

    buffer

  let inline enableShadows
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    lightCtx.ShadowsEnabled <- true

    buffer.Add(
      Command2D.EnableShadows(buffer.LightContexts.Register lightCtx, layer)
    )

    buffer

  let inline disableShadows
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    lightCtx.ShadowsEnabled <- false

    buffer.Add(
      Command2D.DisableShadows(buffer.LightContexts.Register lightCtx, layer)
    )

    buffer
