module MiboSample.Player

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open FSharp.UMX
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open MiboSample.Domain

/// Event produced when player fires - dispatched to parent as a message
[<Struct>]
type FireEvent = Fired of id: Guid<EntityId> * position: Vector2

/// Result of a player tick - new position and optional fire event
[<Struct>]
type TickResult = {
  NewPosition: Vector2
  FireEvent: FireEvent voption
}

// ─────────────────────────────────────────────────────────────
// World Module: Pure computations that produce values
//
// These functions compute new values but do NOT mutate. The calling
// site (parent update) decides when and how to apply the mutation.
// This keeps logic testable and gives the parent full control.
// ─────────────────────────────────────────────────────────────

module World =

  /// Compute new input state after key press
  let keyDown (key: Keys) (input: InputState) : InputState =
    match key with
    | Keys.Left -> { input with MovingLeft = true }
    | Keys.Right -> { input with MovingRight = true }
    | Keys.Up -> { input with MovingUp = true }
    | Keys.Down -> { input with MovingDown = true }
    | Keys.Space -> { input with IsFiring = true }
    | _ -> input

  /// Compute new input state after key release
  let keyUp (key: Keys) (input: InputState) : InputState =
    match key with
    | Keys.Left -> { input with MovingLeft = false }
    | Keys.Right -> { input with MovingRight = false }
    | Keys.Up -> { input with MovingUp = false }
    | Keys.Down -> { input with MovingDown = false }
    | Keys.Space -> { input with IsFiring = false }
    | _ -> input

  /// Compute player tick: reads current state, returns new position and events.
  ///
  /// This is a pure function - it computes results but does not mutate.
  /// The parent orchestrator applies the position update to the dictionary.
  let tick
    (dt: float32)
    (entityId: Guid<EntityId>)
    (position: Vector2)
    (speed: float32)
    (input: InputState)
    : TickResult =

    // Accumulate movement direction from input state
    let mutable dir = Vector2.Zero

    if input.MovingLeft then
      dir <- dir - Vector2.UnitX

    if input.MovingRight then
      dir <- dir + Vector2.UnitX

    if input.MovingUp then
      dir <- dir - Vector2.UnitY

    if input.MovingDown then
      dir <- dir + Vector2.UnitY

    // Apply velocity only if moving
    let newPos =
      if dir = Vector2.Zero then
        position
      else
        position + dir * speed * dt

    // Fire events are rare control-plane messages
    let fireEvent =
      if input.IsFiring then
        ValueSome(Fired(entityId, newPos))
      else
        ValueNone

    {
      NewPosition = newPos
      FireEvent = fireEvent
    }

/// Render a player entity. Takes component data directly, not the whole model.
let view
  (ctx: GameContext)
  (position: Vector2)
  (color: Color)
  (size: Vector2)
  (buffer: RenderBuffer<RenderCmd2D>)
  =
  let tex =
    ctx
    |> Assets.getOrCreate<Texture2D> "pixel" (fun gd ->
      let t = new Texture2D(gd, 1, 1)
      t.SetData([| Color.White |])
      t)

  // Camera follows player - this is a 2D world-space camera
  let vp = ctx.GraphicsDevice.Viewport
  let cam = Camera2D.create position 1.0f (Point(vp.Width, vp.Height))
  Draw2D.camera cam 0<RenderLayer> buffer

  let rect = Rectangle(int position.X, int position.Y, int size.X, int size.Y)

  Draw2D.sprite tex rect
  |> Draw2D.withColor color
  |> Draw2D.atLayer 10<RenderLayer>
  |> Draw2D.submit buffer
