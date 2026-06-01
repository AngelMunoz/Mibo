namespace Mibo.Elmish.Graphics2D.Lighting

open System.Numerics
open Raylib_cs
open Mibo.Animation
open Mibo.Elmish.Graphics2D

// ═══════════════════════════════════════════════════════════════════
// Command2D Factory Functions
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Factory functions for lighting-related render commands.
/// Each function mutates the <see cref="T:Mibo.Elmish.Graphics2D.Lighting.LightContext2D"/> immediately
/// (during view population) and returns a <see cref="T:Mibo.Elmish.Graphics2D.Command2D"/>
/// for layer ordering.
/// </summary>
module LightCommands =

  /// <summary>
  /// Sets the ambient light color for the current frame.
  /// Mutates the light context immediately. The returned command is a layer placeholder.
  /// (lightCtx) can be bound first for partial application.
  /// </summary>
  let inline setAmbient
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>, ambient: AmbientLight2D)
    =
    lightCtx.Ambient <- ambient.Color
    Command2D.NoopLight(layer)

  /// <summary>
  /// Adds a point light for the current frame.
  /// Mutates the light context immediately. The returned command is a layer placeholder.
  /// </summary>
  let inline addPointLight
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (light: PointLight2D)
    =
    lightCtx.PointLights.Add(light)
    Command2D.NoopLight(layer)

  /// <summary>
  /// Adds a directional light for the current frame.
  /// Mutates the light context immediately. The returned command is a layer placeholder.
  /// </summary>
  let inline addDirectionalLight
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (light: DirectionalLight2D)
    =
    lightCtx.DirLights.Add(light)
    Command2D.NoopLight(layer)

  /// <summary>
  /// Adds an occluder segment for the current frame. Used by the shadow system.
  /// Mutates the light context immediately. The returned command is a layer placeholder.
  /// </summary>
  let inline addOccluder
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (occluder: Occluder2D)
    =
    lightCtx.Occluders.Add(occluder)
    Command2D.NoopLight(layer)

  /// <summary>
  /// Draws a sprite with the current lighting state from the given light context.
  /// Activates the lit shader and uploads light uniforms on first call each frame.
  /// The sprite's NormalMap field controls per-pixel lighting when set.
  /// </summary>
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

  /// <summary>
  /// Ends the current lighting pass. Deactivates the lit shader.
  /// Sprites after this point are unlit. Call again to re-enable lighting.
  /// </summary>
  let inline endLighting (lightCtx: LightContext2D) (layer: int<RenderLayer>) =
    Command2D.EndLighting(lightCtx, layer)

  /// <summary>
  /// Enables shadow raymarching for subsequent lit sprites in this context.
  /// Mutates the light context immediately. The returned command is a layer placeholder.
  /// </summary>
  let inline enableShadows
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    =
    lightCtx.ShadowsEnabled <- true
    Command2D.EnableShadows(lightCtx, layer)

  /// <summary>
  /// Disables shadow raymarching for subsequent lit sprites in this context.
  /// Occluders are still collected but not uploaded to the shader.
  /// Mutates the light context immediately. The returned command is a layer placeholder.
  /// </summary>
  let inline disableShadows
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    =
    lightCtx.ShadowsEnabled <- false
    Command2D.DisableShadows(lightCtx, layer)

// ═══════════════════════════════════════════════════════════════════
// Draw Module Wrappers (buffer-returning, pipe-friendly)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Pipe-friendly lighting draw functions. Each takes a
/// <see cref="T:Mibo.Elmish.Graphics2D.RenderBuffer2D"/> as the last argument,
/// adds the command, and returns the buffer for chaining.
/// </summary>
module LightDraw =

  let inline setAmbient
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>, ambient: AmbientLight2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.setAmbient lightCtx (layer, ambient))
    buffer

  let inline addPointLight
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (light: PointLight2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.addPointLight lightCtx layer light)
    buffer

  let inline addDirectionalLight
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (light: DirectionalLight2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.addDirectionalLight lightCtx layer light)
    buffer

  let inline addOccluder
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (occluder: Occluder2D)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.addOccluder lightCtx layer occluder)
    buffer

  let inline litSprite
    (lightCtx: LightContext2D)
    (sprite: SpriteState)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.litSprite lightCtx sprite)
    buffer

  let inline litAnimatedSprite
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (dest: Rectangle)
    (animSprite: AnimatedSprite)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.litAnimatedSprite lightCtx layer dest animSprite)
    buffer

  let inline endLighting
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.endLighting lightCtx layer)
    buffer

  let inline enableShadows
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.enableShadows lightCtx layer)
    buffer

  let inline disableShadows
    (lightCtx: LightContext2D)
    (layer: int<RenderLayer>)
    (buffer: RenderBuffer2D)
    =
    buffer.Add(LightCommands.disableShadows lightCtx layer)
    buffer
