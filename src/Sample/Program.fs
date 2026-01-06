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
  | PlayerMsg of pm: Player.Msg
  | DemoBoxBounced of count: int

let init() =
  let struct (pModel, _) = Player.init (Vector2(100.f, 100.f)) Color.Red
  let struct (partModel, _) = Particles.init()

  let initialModel = {
    Player = pModel
    Particles = partModel
    BoxBounces = 0
  }

  struct (initialModel, Cmd.none)

let update
  (boxRef: ComponentRef<InteractiveBoxOverlay>)
  (msg: Msg)
  (model: Model)
  =
  match msg with
  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    // 1. Update Player (Time Tick)
    let struct (newPlayer, _) = Player.update (Player.Tick dt) model.Player

    // 2. Update Particles (Time Tick)
    let struct (newParticles, _) =
      Particles.update (Particles.Update dt) model.Particles

    // 3. Orchestration: Check if Player is "Firing"
    let struct (finalParticles, _) =
      if newPlayer.IsFiring then
        let spawnPos = newPlayer.Player.Position
        Particles.update (Particles.Emit(spawnPos, 5)) newParticles
      else
        struct (newParticles, Cmd.none)

    // Elmish -> MonoGame component interop:
    // Use the player's "IsFiring" state to control the component.
    let cmd =
      Cmd.ofEffect(
        Effect(fun _dispatch ->
          boxRef.TryGet()
          |> ValueOption.iter(fun box ->
            box.SpeedScale <- if newPlayer.IsFiring then 2.5f else 1.0f

            box.Tint <-
              if newPlayer.IsFiring then
                Color.HotPink
              else
                Color.DeepSkyBlue

            box.SetVisible(true)))
      )

    struct ({
              model with
                  Player = newPlayer
                  Particles = finalParticles
            },
            cmd)

  | PlayerMsg pMsg ->
    let struct (newPlayer, _) = Player.update pMsg model.Player
    struct ({ model with Player = newPlayer }, Cmd.none)

  | DemoBoxBounced count ->
    // Component -> Elmish interop:
    // When the component bounces, we update the model and emit some particles.
    let struct (newParticles, _) =
      Particles.update
        (Particles.Emit(model.Player.Player.Position, 20))
        model.Particles

    struct ({
              model with
                  BoxBounces = count
                  Particles = newParticles
            },
            Cmd.none)

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
