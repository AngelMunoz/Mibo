namespace Mibo.Elmish.Next.Graphics2D

open Mibo.Elmish.Next.Graphics2D.Base

open System.Numerics
open Raylib_cs
open Mibo.Elmish.Next
open Mibo.Animation

module LightDraw =

  let inline setAmbient
    (lightCtx: Mibo.Elmish.Graphics2D.Lighting.LightContext2D)
    (
      layer: int<RenderLayer>,
      ambient: Mibo.Elmish.Graphics2D.Lighting.AmbientLight2D
    )
    (buffer: RenderBuffer2D)
    =
    lightCtx.Ambient <- ambient.Color
    buffer.Add(Command2D.NoopLight layer)
    buffer

  let inline addPointLight
    (lightCtx: Mibo.Elmish.Graphics2D.Lighting.LightContext2D)
    (layer: int<RenderLayer>)
    (light: Mibo.Elmish.Graphics2D.Lighting.PointLight2D)
    (buffer: RenderBuffer2D)
    =
    lightCtx.PointLights.Add light
    buffer.Add(Command2D.NoopLight layer)
    buffer

  let inline addDirectionalLight
    (lightCtx: Mibo.Elmish.Graphics2D.Lighting.LightContext2D)
    (layer: int<RenderLayer>)
    (light: Mibo.Elmish.Graphics2D.Lighting.DirectionalLight2D)
    (buffer: RenderBuffer2D)
    =
    lightCtx.DirLights.Add light
    buffer.Add(Command2D.NoopLight layer)
    buffer

  let inline addOccluder
    (lightCtx: Mibo.Elmish.Graphics2D.Lighting.LightContext2D)
    (layer: int<RenderLayer>)
    (occluder: Mibo.Elmish.Graphics2D.Lighting.Occluder2D)
    (buffer: RenderBuffer2D)
    =
    lightCtx.Occluders.Add occluder
    buffer.Add(Command2D.NoopLight layer)
    buffer

  let inline litSprite
    (lightCtx: Mibo.Elmish.Graphics2D.Lighting.LightContext2D)
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
    (lightCtx: Mibo.Elmish.Graphics2D.Lighting.LightContext2D)
    (layer: int<RenderLayer>)
    (dest: Rectangle)
    (animSprite: AnimatedSprite)
    (buffer: RenderBuffer2D)
    =
    let src = AnimatedSprite.currentSource animSprite

    let src =
      if animSprite.FlipX then
        Rectangle(src.X, src.Y, -src.Width, src.Height)
      else
        src

    let src =
      if animSprite.FlipY then
        Rectangle(src.X, src.Y, src.Width, -src.Height)
      else
        src

    litSprite
      lightCtx
      ({
        Texture = animSprite.Sheet.Texture
        Dest = dest
        Source = src
        Origin = animSprite.Sheet.Origin
        Rotation = animSprite.Rotation
        Color = animSprite.Color
        Layer = layer
        NormalMap = animSprite.Sheet.NormalMap
      })
      buffer

  let inline endLighting
    (lightCtx: Mibo.Elmish.Graphics2D.Lighting.LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(
      Command2D.EndLighting(buffer.LightContexts.Register lightCtx, layer)
    )

    buffer

  let inline enableShadows
    (lightCtx: Mibo.Elmish.Graphics2D.Lighting.LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    lightCtx.ShadowsEnabled <- true

    buffer.Add(
      Command2D.EnableShadows(buffer.LightContexts.Register lightCtx, layer)
    )

    buffer

  let inline disableShadows
    (lightCtx: Mibo.Elmish.Graphics2D.Lighting.LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    lightCtx.ShadowsEnabled <- false

    buffer.Add(
      Command2D.DisableShadows(buffer.LightContexts.Register lightCtx, layer)
    )

    buffer
