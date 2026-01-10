module MiboSample.Player

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open FSharp.UMX
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Animation
open MiboSample.Domain

// ─────────────────────────────────────────────────────────────
// Player Module: Input handling and ReadonlySystem for actions
// ─────────────────────────────────────────────────────────────


/// ReadonlySystem: processes player actions and generates commands
let processActions<'Msg>(onFired: Guid<EntityId> -> Vector2 -> 'Msg) =
  fun (snapshot: ModelSnapshot) ->
    let playerId = snapshot.PlayerId

    let cmd =
      match snapshot.Positions.TryGetValue(playerId) with
      | (true, position) when snapshot.Actions.Held.Contains Fire ->
        Cmd.ofEffect(
          Effect<'Msg>(fun dispatch -> dispatch(onFired playerId position))
        )
      | _ -> Cmd.none

    struct (snapshot, cmd)

// ─────────────────────────────────────────────────────────────
// View: Render player entity with animated sprite
// ─────────────────────────────────────────────────────────────

let view
  (ctx: GameContext)
  (position: Vector2)
  (playerSprite: AnimatedSprite)
  (color: Color)
  (buffer: RenderBuffer<RenderCmd2D>)
  =
  // Set up camera following player
  let vp = ctx.GraphicsDevice.Viewport
  let cam = Camera2D.create position 1.0f (Point(vp.Width, vp.Height))
  Draw2D.camera cam 0<RenderLayer> buffer

  // Draw player sprite at position with color tint
  playerSprite
  |> AnimatedSprite.withColor color
  |> AnimatedSprite.draw position 10<RenderLayer> buffer
