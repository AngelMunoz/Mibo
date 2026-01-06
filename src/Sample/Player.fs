module MiboSample.Player

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open Mibo.Elmish

// --- Domain ---

[<Struct>]
type Player =
    { Position: Vector2
      Color: Color
      Size: Vector2 }

[<Struct>]
type Model =
    { Player: Player
      Speed: float32
      MovingLeft: bool
      MovingRight: bool
      MovingUp: bool
      MovingDown: bool
      IsFiring: bool }

[<Struct>]
type Msg =
    | KeyDown of down: Keys
    | KeyUp of up: Keys
    | Tick of tick: float32

let init (startPos: Vector2) (color: Color) =
    struct ({ Player =
                { Position = startPos
                  Color = color
                  Size = Vector2(32.f, 32.f) }
              Speed = 200.f
              MovingLeft = false
              MovingRight = false
              MovingUp = false
              MovingDown = false
              IsFiring = false },
            Cmd.none)

let subscribe (model: Model) : Sub<Msg> =
    // We listen to all keys and filter in Update, OR we map specific keys here.
    // For simplicity, we just forward all Key events.
    MiboSample.Input.Keyboard.listen KeyDown KeyUp

let update (msg: Msg) (model: Model) =
    match msg with
    | KeyDown k ->
        match k with
        | Keys.Left -> struct ({ model with MovingLeft = true }, Cmd.none)
        | Keys.Right -> struct ({ model with MovingRight = true }, Cmd.none)
        | Keys.Up -> struct ({ model with MovingUp = true }, Cmd.none)
        | Keys.Down -> struct ({ model with MovingDown = true }, Cmd.none)
        | Keys.Space -> struct ({ model with IsFiring = true }, Cmd.none)
        | _ -> struct (model, Cmd.none)

    | KeyUp k ->
        match k with
        | Keys.Left -> struct ({ model with MovingLeft = false }, Cmd.none)
        | Keys.Right -> struct ({ model with MovingRight = false }, Cmd.none)
        | Keys.Up -> struct ({ model with MovingUp = false }, Cmd.none)
        | Keys.Down -> struct ({ model with MovingDown = false }, Cmd.none)
        | Keys.Space -> struct ({ model with IsFiring = false }, Cmd.none)
        | _ -> struct (model, Cmd.none)

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

        if dir = Vector2.Zero then
            struct (model, Cmd.none)
        else
            let velocity = dir * model.Speed * dt
            let newPos = model.Player.Position + velocity

            struct ({ model with
                        Player = { model.Player with Position = newPos } },
                    Cmd.none)

module Resources =
    let mutable private _pixel: Texture2D voption = ValueNone

    // Called by Main Game during LoadContent
    let loadContent (gd: GraphicsDevice) =
        if _pixel.IsNone then
            let tex = new Texture2D(gd, 1, 1)
            tex.SetData([| Color.White |])
            _pixel <- ValueSome tex

    let getTexture () = _pixel

let view (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
    Resources.getTexture ()
    |> ValueOption.iter (fun tex ->
        let p = model.Player
        let rect = Rectangle(int p.Position.X, int p.Position.Y, int p.Size.X, int p.Size.Y)

        Draw2D.sprite tex rect
        |> Draw2D.withColor p.Color
        |> Draw2D.atLayer 10<RenderLayer>
        |> Draw2D.submit buffer)
