namespace Mibo.Elmish.Graphics2D.Lighting

open Microsoft.Xna.Framework
open Mibo.Animation
open Mibo.Elmish.Graphics2D

/// <summary>
/// Factory functions that create lighting <see cref="T:Mibo.Elmish.Graphics2D.Command2D"/> values.
/// Light-accumulation commands mutate the <see cref="T:Mibo.Elmish.Graphics2D.Lighting.LightContext2D"/>
/// eagerly and return a <c>NoopLight</c> purely for layer-sort ordering.
/// </summary>
module LightCommands =

  /// <summary>Sets the ambient light color. Mutates the context eagerly.</summary>
  let inline setAmbient
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>, ambient: AmbientLight2D)
    =
    lightCtx.Ambient <- ambient.Color
    Command2D.NoopLight(layer)

  /// <summary>Adds a point light. Mutates the context eagerly.</summary>
  let inline addPointLight
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (light: PointLight2D)
    =
    lightCtx.PointLights.Add(light)
    Command2D.NoopLight(layer)

  /// <summary>Adds a directional light. Mutates the context eagerly.</summary>
  let inline addDirectionalLight
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (light: DirectionalLight2D)
    =
    lightCtx.DirLights.Add(light)
    Command2D.NoopLight(layer)

  /// <summary>Adds an occluder segment. Mutates the context eagerly.</summary>
  let inline addOccluder
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (occluder: Occluder2D)
    =
    lightCtx.Occluders.Add(occluder)
    Command2D.NoopLight(layer)

  /// <summary>Creates a lit-sprite command.</summary>
  let inline litSprite (lightCtx: LightContext2D) (sprite: SpriteState) =
    Command2D.LitSprite(lightCtx, sprite)

  /// <summary>
  /// Draws an animated sprite with the current lighting state.
  /// Automatically extracts texture, source rect, origin, rotation, color,
  /// and normal map from the AnimatedSprite and its SpriteSheet.
  /// Handles FlipX/FlipY by negating the source rect width/height.
  /// </summary>
  let inline litAnimatedSprite
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (dest: Rectangle)
    (animSprite: AnimatedSprite)
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

    Command2D.LitSprite(
      lightCtx,
      {
        Texture = animSprite.Sheet.Texture
        Dest = dest
        Source = src
        Origin = animSprite.Sheet.Origin
        Rotation = animSprite.Rotation
        Color = animSprite.Color
        Layer = layer
        NormalMap = animSprite.Sheet.NormalMap
      }
    )

  /// <summary>Ends the current lighting block, marking uniforms dirty.</summary>
  let inline endLighting (lightCtx: LightContext2D) (layer: int<RenderLayer>) =
    Command2D.EndLighting(lightCtx, layer)

  /// <summary>Enables shadows. Mutates the context eagerly.</summary>
  let inline enableShadows
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    =
    lightCtx.ShadowsEnabled <- true
    Command2D.EnableShadows(lightCtx, layer)

  /// <summary>Disables shadows. Mutates the context eagerly.</summary>
  let inline disableShadows
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    =
    lightCtx.ShadowsEnabled <- false
    Command2D.DisableShadows(lightCtx, layer)

/// <summary>
/// Pipe-friendly DSL for lighting commands. Each function takes a
/// <see cref="T:Mibo.Elmish.Graphics2D.RenderBuffer2D"/> as the last argument,
/// adds the command, and returns the buffer for chaining.
/// </summary>
module LightDraw =

  /// <summary>Sets the ambient light color.</summary>
  let inline setAmbient
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>, ambient: AmbientLight2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.setAmbient lightCtx (layer, ambient))
    buffer

  /// <summary>Adds a point light.</summary>
  let inline addPointLight
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (light: PointLight2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.addPointLight lightCtx layer light)
    buffer

  /// <summary>Adds a directional light.</summary>
  let inline addDirectionalLight
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (light: DirectionalLight2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.addDirectionalLight lightCtx layer light)
    buffer

  /// <summary>Adds an occluder segment.</summary>
  let inline addOccluder
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (occluder: Occluder2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.addOccluder lightCtx layer occluder)
    buffer

  /// <summary>Draws a lit sprite.</summary>
  let inline litSprite
    (lightCtx: LightContext2D)
    (sprite: SpriteState)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.litSprite lightCtx sprite)
    buffer

  /// <summary>Draws a lit animated sprite.</summary>
  let inline litAnimatedSprite
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (dest: Rectangle)
    (animSprite: AnimatedSprite)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.litAnimatedSprite lightCtx layer dest animSprite)
    buffer

  /// <summary>Ends the current lighting block.</summary>
  let inline endLighting
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.endLighting lightCtx layer)
    buffer

  /// <summary>Enables shadows.</summary>
  let inline enableShadows
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.enableShadows lightCtx layer)
    buffer

  /// <summary>Disables shadows.</summary>
  let inline disableShadows
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.disableShadows lightCtx layer)
    buffer
