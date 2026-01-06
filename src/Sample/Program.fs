open Microsoft.Xna.Framework

open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open MiboSample
open MiboSample.DemoComponents

[<Struct>]
type Model = {
  Player: Player.Model
  Particles: Particles.Model
  BoxBounces: int
}

[<Struct>]
type Msg =
  | Tick of gt: GameTime
  | PlayerMsg of playerMsg: Player.Msg
  | ParticlesMsg of particlesMsg: Particles.Msg
  | DemoBoxBounced of count: int

let private updateInteractiveBoxOverlay
  (boxRef: ComponentRef<InteractiveBoxOverlay>)
  (isFiring: bool)
  : Cmd<Msg> =
  Cmd.ofEffect(
    Effect(fun _dispatch ->
      boxRef.TryGet()
      |> ValueOption.iter(fun box ->
        box.SpeedScale <- if isFiring then 2.5f else 1.0f

        box.Tint <- if isFiring then Color.HotPink else Color.DeepSkyBlue

        box.SetVisible(true)))
  )

let init() =
  let struct (pModel, pCmd) = Player.init (Vector2(100.f, 100.f)) Color.Red
  let struct (partModel, partCmd) = Particles.init()

  let initialModel = {
    Player = pModel
    Particles = partModel
    BoxBounces = 0
  }

  struct (initialModel,
          Cmd.batch [ Cmd.map PlayerMsg pCmd; Cmd.map ParticlesMsg partCmd ])

let update
  (boxRef: ComponentRef<InteractiveBoxOverlay>)
  (msg: Msg)
  (model: Model)
  =
  match msg with
  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    // Route time to children.
    let struct (newPlayer, playerCmd) =
      Player.update (Player.Tick dt) model.Player

    let struct (newParticles, particlesCmd) =
      Particles.update (Particles.Update dt) model.Particles

    // Elmish -> MonoGame component interop.
    let interopCmd = updateInteractiveBoxOverlay boxRef newPlayer.IsFiring

    let cmd =
      Cmd.batch [
        Cmd.map PlayerMsg playerCmd
        Cmd.map ParticlesMsg particlesCmd
        interopCmd
      ]

    struct ({
              model with
                  Player = newPlayer
                  Particles = newParticles
            },
            cmd)

  | PlayerMsg pMsg ->
    let struct (newPlayer, playerCmd) = Player.update pMsg model.Player

    // Orchestrate cross-module interactions by reacting to child events.
    let struct (newParticles, particlesCmd) =
      match pMsg with
      | Player.Fired pos ->
        Particles.update (Particles.Emit(pos, 5)) model.Particles
      | _ -> struct (model.Particles, Cmd.none)

    struct ({
              model with
                  Player = newPlayer
                  Particles = newParticles
            },
            Cmd.batch [
              Cmd.map PlayerMsg playerCmd
              Cmd.map ParticlesMsg particlesCmd
            ])

  | ParticlesMsg pMsg ->
    let struct (newParticles, particlesCmd) =
      Particles.update pMsg model.Particles

    struct ({ model with Particles = newParticles },
            Cmd.map ParticlesMsg particlesCmd)

  | DemoBoxBounced count ->
    // Component -> Elmish interop:
    // When the component bounces, we update the model and emit some particles.
    let struct (newParticles, particlesCmd) =
      Particles.update
        (Particles.Emit(model.Player.Player.Position, 20))
        model.Particles

    struct ({
              model with
                  BoxBounces = count
                  Particles = newParticles
            },
            Cmd.map ParticlesMsg particlesCmd)

// --- Subscriptions ---

let subscribe (boxRef: ComponentRef<InteractiveBoxOverlay>) (model: Model) =
  // Compose subscriptions
  Sub.batch [
    Player.subscribe model.Player |> Sub.map "Player" PlayerMsg
    InteractiveBoxOverlayBridge.subscribeBounced boxRef DemoBoxBounced
  ]

// --- Composition Root ---

let view (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
  Player.view model.Player buffer
  Particles.view model.Particles buffer

[<EntryPoint>]
let main argv =
  let interactiveBoxRef = ComponentRef<InteractiveBoxOverlay>()

  let program =
    Program.mkProgram init (update interactiveBoxRef)
    |> Program.withSubscription(subscribe interactiveBoxRef)
    |> Program.withTick Tick
    |> Program.withLoadContent Player.Resources.loadContent
    |> Program.withLoadContent Particles.Resources.loadContent
    |> Program.withRenderer(Batch2DRenderer.create view)
    |> Program.withService(fun _ -> Input.InputService() :> IEngineService)
    |> Program.withComponent BouncingBoxOverlay.create
    |> Program.withComponentRef
      interactiveBoxRef
      InteractiveBoxOverlayBridge.create

  use game = new ElmishGame<Model, Msg>(program)
  game.Run()
  0
