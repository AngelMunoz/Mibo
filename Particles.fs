module Gamino.Particles

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Gamino.Elmish

module ResizeArray =
    let inline init (count: int) f =
        let ra = ResizeArray<_>(count)

        for i in 0 .. count - 1 do
            ra.Add(f i)

        ra

    let inline ofSeq (s: seq<'T>) = ResizeArray<_>(s)

    let inline chooseV (f: 'T -> 'U voption) (ra: ResizeArray<'T>) =
        let result = ResizeArray<_>()

        for item in ra do
            match f item with
            | ValueSome v -> result.Add(v)
            | ValueNone -> ()

        result

    let inline choose (f: 'T -> 'U option) (ra: ResizeArray<'T>) =
        let result = ResizeArray<_>()

        for item in ra do
            match f item with
            | Some v -> result.Add(v)
            | None -> ()

        result

    let inline map (f: 'T -> 'U) (ra: ResizeArray<'T>) =
        let result = ResizeArray<_>(ra.Count)

        for item in ra do
            result.Add(f item)

        result

    let inline addFrom (from: ResizeArray<'T>) (into: ResizeArray<'T>) =
        into.AddRange from
        into


// --- Domain ---
[<Struct>]
type Particle =
    { Position: Vector2
      Velocity: Vector2
      Life: float32
      MaxLife: float32
      Color: Color }

[<Struct>]
type Model =
    { Particles: Particle ResizeArray
      Texture: Texture2D voption }

type Msg =
    | Emit of position: Vector2 * count: int
    | Update of dt: float32

// --- Internal Logic ---

let private createParticle (pos: Vector2) (rng: Random) =
    let angle = rng.NextDouble() * Math.PI * 2.0
    let speed = rng.NextDouble() * 100.0 + 50.0

    let velocity =
        Vector2(float32 (Math.Cos angle), float32 (Math.Sin angle)) * float32 speed

    { Position = pos
      Velocity = velocity
      Life = 1.0f
      MaxLife = 1.0f
      Color = Color.Yellow }

let private rng = System.Random.Shared

// --- Elmish Interface ---

let init () =
    struct ({ Particles = ResizeArray()
              Texture = ValueNone },
            Cmd.none)

let update (msg: Msg) (model: Model) =
    match msg with
    | Emit(pos, count) ->
        let newParticles =
            ResizeArray.init count (fun _ -> createParticle pos rng)
            |> ResizeArray.addFrom model.Particles

        struct ({ model with Particles = newParticles }, Cmd.none)

    | Update dt ->
        let updatedParticles =
            model.Particles
            |> ResizeArray.chooseV (fun p ->
                let newLife = p.Life - dt

                if newLife <= 0.0f then
                    ValueNone
                else
                    ValueSome
                        { p with
                            Position = p.Position + p.Velocity * dt
                            Life = newLife
                            Color = Color.Lerp(Color.Transparent, Color.Yellow, newLife / p.MaxLife) })

        struct ({ model with
                    Particles = updatedParticles },
                Cmd.none)

// --- Rendering ---

module Resources =
    let mutable private _pixel: Texture2D voption = ValueNone

    let loadContent (gd: GraphicsDevice) =
        if _pixel.IsNone then
            let tex = new Texture2D(gd, 2, 2)
            tex.SetData([| Color.White; Color.White; Color.White; Color.White |])
            _pixel <- ValueSome tex

    let getTexture () = _pixel

let view (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
    Resources.getTexture ()
    |> ValueOption.iter (fun tex ->
        for p in model.Particles do
            let rect = Rectangle(int p.Position.X, int p.Position.Y, 2, 2)

            Draw2D.sprite tex rect
            |> Draw2D.withColor p.Color
            |> Draw2D.atLayer 5<RenderLayer>
            |> Draw2D.submit buffer)
