namespace Defli.Raylib

open System
open System.Collections.Generic
open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Raylib_cs
open Defli.World
open Defli.World.Systems

// ─────────────────────────────────────────────────────────────
// ProjectilesView — the shell sprite per row from the frame's
// Homing projection snapshot.
// ─────────────────────────────────────────────────────────────

module ProjectilesView =

  let view
    (ctx: GameContext)
    (homing: IReadOnlyDictionary<int<ProjectileId>, HomingView>)
    (buffer: RenderBuffer2D)
    =
    let assets = GameContext.getService<IAssets> ctx
    let tex = assets.Texture Tiles.SheetPath

    for KeyValueV(pid, v) in homing do
      let tile =
        v.Sprite
        |> Tiles.tryByName
        |> ValueOption.defaultValue Tiles.rocketSmall

      let scale = 28f / max (float32 tile.Width) (float32 tile.Height)
      let w = float32 tile.Width * scale
      let h = float32 tile.Height * scale

      // Heading toward the (possibly last recorded) target position
      // (0° = up; raylib rotates CW).
      let d = v.TargetPos - v.Pos
      let angle = 90f + MathF.Atan2(d.Y, d.X) * 180f / MathF.PI

      buffer
        .sprite(
          SpriteState.create(
            tex,
            Rectangle(v.Pos.X - w / 2f, v.Pos.Y - h / 2f, w, h),
            MapView.tileRect tile
          )
          |> SpriteState.withOrigin(Vector2(w / 2f, h / 2f))
          |> SpriteState.withRotation angle
          |> SpriteState.withLayer Layers.Projectiles
        )
        .drop()
