module MiboSample.Player

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open FSharp.UMX
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open MiboSample.Domain

// ─────────────────────────────────────────────────────────────
// Player Module: Input handling and ReadonlySystem for actions
// ─────────────────────────────────────────────────────────────


/// ReadonlySystem: processes player actions and generates commands
let processActions<'Msg> (onFired: Guid<EntityId> -> Vector2 -> 'Msg)=
  fun (snapshot: ModelSnapshot) ->
    let playerId = snapshot.PlayerId
    let cmds =
      match snapshot.Positions.TryGetValue(playerId) with
      | (true, position) when snapshot.Actions.Held.Contains Fire ->
        [ Cmd.ofEffect(Effect<'Msg>(fun dispatch -> dispatch(onFired playerId position))) ]
      | _ -> []
    struct (snapshot, cmds)

// ─────────────────────────────────────────────────────────────
// View: Render player entity
// ─────────────────────────────────────────────────────────────

let view (ctx: GameContext) (position: Vector2) (color: Color) (size: Vector2) (buffer: RenderBuffer<RenderCmd2D>) =
  let tex =
    ctx |> Assets.getOrCreate<Texture2D> "pixel" (fun gd ->
      let t = new Texture2D(gd, 1, 1)
      t.SetData([| Color.White |])
      t)

  let vp = ctx.GraphicsDevice.Viewport
  let cam = Camera2D.create position 1.0f (Point(vp.Width, vp.Height))
  Draw2D.camera cam 0<RenderLayer> buffer

  let rect = Rectangle(int position.X, int position.Y, int size.X, int size.Y)
  Draw2D.sprite tex rect
  |> Draw2D.withColor color
  |> Draw2D.atLayer 10<RenderLayer>
  |> Draw2D.submit buffer
