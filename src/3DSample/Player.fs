module _3DSample.Player

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open _3DSample

// ─────────────────────────────────────────────────────────────
// Player System: Respawn and rendering
// ─────────────────────────────────────────────────────────────

/// Check if player has fallen and respawn if needed
let checkRespawn<'Msg>(state: State) : struct (State * Cmd<'Msg>) =
  if state.PlayerPosition.Y <= Constants.fallLimit then
    {
      state with
          PlayerPosition = Vector3(0f, 2f, 0f)
          Velocity = Vector3.Zero
          IsGrounded = false
    },
    Cmd.none
  else
    state, Cmd.none

/// Render the player ball with rotation
let view
  (_ctx: GameContext)
  (state: State)
  (buffer: RenderBuffer<RenderCmd3D>)
  : unit =
  let rotationMatrix = Matrix.CreateFromQuaternion state.Rotation

  let playerMatrix =
    rotationMatrix * Matrix.CreateTranslation state.PlayerPosition

  Draw3D.mesh state.Assets.PlayerModel playerMatrix |> Draw3D.submit buffer
