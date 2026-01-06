module Gamino.Particles

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Gamino.Elmish

// --- Domain ---

type Particle =
    { Position: Vector2
      Velocity: Vector2
      Life: float32
      MaxLife: float32
      Color: Color }

type Model =
    { Particles: Particle list // Using list for immutability, could be array for perf
      Texture: Texture2D option }

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
    struct ({ Particles = []; Texture = None }, Cmd.none)

let update (msg: Msg) (model: Model) =
    match msg with
    | Emit(pos, count) ->
        let newParticles = List.init count (fun _ -> createParticle pos rng)

        struct ({ model with
                    Particles = newParticles @ model.Particles },
                Cmd.none)

    | Update dt ->
        let updatedParticles =
            model.Particles
            |> List.choose (fun p ->
                let newLife = p.Life - dt

                if newLife <= 0.0f then
                    None
                else
                    Some
                        { p with
                            Position = p.Position + p.Velocity * dt
                            Life = newLife
                            Color = Color.Lerp(Color.Transparent, Color.Yellow, newLife / p.MaxLife) })

        struct ({ model with
                    Particles = updatedParticles },
                Cmd.none)

// --- Rendering ---

module Resources =
    let mutable private _pixel: Texture2D option = None

    let loadContent (gd: GraphicsDevice) =
        if _pixel.IsNone then
            let tex = new Texture2D(gd, 2, 2)
            tex.SetData([| Color.White; Color.White; Color.White; Color.White |])
            _pixel <- Some tex

    let getTexture () = _pixel

let view (model: Model) (buffer: RenderBuffer) =
    match Resources.getTexture () with
    | Some tex ->
        for p in model.Particles do
            let rect = Rectangle(int p.Position.X, int p.Position.Y, 2, 2)
            Render.draw tex rect p.Color RenderLayer.Particles buffer |> ignore
    | None -> ()
