module MiboSample.Player

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Input

[<Struct>]
type Player = {
  Position: Vector2
  Color: Color
  Size: Vector2
}

[<Struct>]
type Model = {
  Player: Player
  Speed: float32
  MovingLeft: bool
  MovingRight: bool
  MovingUp: bool
  MovingDown: bool
  IsFiring: bool
}

[<Struct>]
type Msg =
  | KeyDown of down: Keys
  | KeyUp of up: Keys
  | Tick of tick: float32
  | Fired of position: Vector2

let init (startPos: Vector2) (color: Color) : struct (Model * Cmd<Msg>) =
  {
    Player = {
      Position = startPos
      Color = color
      Size = Vector2(32.f, 32.f)
    }
    Speed = 200.f
    MovingLeft = false
    MovingRight = false
    MovingUp = false
    MovingDown = false
    IsFiring = false
  },
  Cmd.none

let subscribe (ctx: GameContext) (model: Model) : Sub<Msg> =
  Keyboard.listen KeyDown KeyUp ctx

let update (msg: Msg) (model: Model) : struct (Model * Cmd<Msg>) =
  match msg with
  | Fired _ -> model, Cmd.none
  | KeyDown k ->
    match k with
    | Keys.Left -> { model with MovingLeft = true }, Cmd.none
    | Keys.Right -> { model with MovingRight = true }, Cmd.none
    | Keys.Up -> { model with MovingUp = true }, Cmd.none
    | Keys.Down -> { model with MovingDown = true }, Cmd.none
    | Keys.Space -> { model with IsFiring = true }, Cmd.none
    | _ -> model, Cmd.none
  | KeyUp k ->
    match k with
    | Keys.Left -> { model with MovingLeft = false }, Cmd.none
    | Keys.Right -> { model with MovingRight = false }, Cmd.none
    | Keys.Up -> { model with MovingUp = false }, Cmd.none
    | Keys.Down -> { model with MovingDown = false }, Cmd.none
    | Keys.Space -> { model with IsFiring = false }, Cmd.none
    | _ -> model, Cmd.none
  | Tick dt ->
    let mutable dir = Vector2.Zero

    if model.MovingLeft then
      dir <- dir - Vector2.UnitX

    if model.MovingRight then
      dir <- dir + Vector2.UnitX

    if model.MovingUp then
      dir <- dir - Vector2.UnitY

    if model.MovingDown then
      dir <- dir + Vector2.UnitY

    let newModel =
      if dir = Vector2.Zero then
        model
      else
        let velocity = dir * model.Speed * dt
        let newPos = model.Player.Position + velocity

        {
          model with
              Player = { model.Player with Position = newPos }
        }

    let cmd =
      if newModel.IsFiring then
        Cmd.ofEffect(
          Effect<Msg>(fun dispatch -> dispatch(Fired newModel.Player.Position))
        )
      else
        Cmd.none

    newModel, cmd

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
  let tex =
    ctx
    |> Assets.getOrCreate<Texture2D> "pixel" (fun gd ->
      let t = new Texture2D(gd, 1, 1)
      t.SetData([| Color.White |])
      t)

  let p = model.Player

  // Create a camera centered on the player
  let vp = ctx.GraphicsDevice.Viewport
  let cam = Camera2D.create p.Position 1.0f (Point(vp.Width, vp.Height))

  // Set camera for the World layer.
  // We use Layer 0 to ensure it applies to all World entities (Particles are Layer 5, Player is Layer 10).
  Draw2D.camera cam 0<RenderLayer> buffer

  let rect =
    Rectangle(int p.Position.X, int p.Position.Y, int p.Size.X, int p.Size.Y)

  Draw2D.sprite tex rect
  |> Draw2D.withColor p.Color
  |> Draw2D.atLayer 10<RenderLayer>
  |> Draw2D.submit buffer
