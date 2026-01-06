open Microsoft.Xna.Framework

open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open MiboSample
open MiboSample.DemoComponents
open Mibo.Input

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

let init(_ctx: GameContext) : struct (Model * Cmd<Msg>) =
  let struct (pModel, pCmd) = Player.init (Vector2(100.f, 100.f)) Color.Red
  let struct (partModel, partCmd) = Particles.init()

  {
    Player = pModel
    Particles = partModel
    BoxBounces = 0
  },
  Cmd.batch [ Cmd.map PlayerMsg pCmd; Cmd.map ParticlesMsg partCmd ]

let update
  (boxRef: ComponentRef<InteractiveBoxOverlay>)
  (msg: Msg)
  (model: Model)
  : struct (Model * Cmd<Msg>) =
  match msg with
  | Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds

    let struct (newPlayer, playerCmd) =
      Player.update (Player.Tick dt) model.Player

    let struct (newParticles, particlesCmd) =
      Particles.update (Particles.Update dt) model.Particles

    let interopCmd = updateInteractiveBoxOverlay boxRef newPlayer.IsFiring

    {
      model with
          Player = newPlayer
          Particles = newParticles
    },
    Cmd.batch [
      Cmd.map PlayerMsg playerCmd
      Cmd.map ParticlesMsg particlesCmd
      interopCmd
    ]
  | PlayerMsg pMsg ->
    let struct (newPlayer, playerCmd) = Player.update pMsg model.Player

    let struct (newParticles, particlesCmd) =
      match pMsg with
      | Player.Fired pos ->
        Particles.update (Particles.Emit(pos, 5)) model.Particles
      | _ -> struct (model.Particles, Cmd.none)

    {
      model with
          Player = newPlayer
          Particles = newParticles
    },
    Cmd.batch [ Cmd.map PlayerMsg playerCmd; Cmd.map ParticlesMsg particlesCmd ]

  | ParticlesMsg pMsg ->
    let struct (newParticles, particlesCmd) =
      Particles.update pMsg model.Particles

    { model with Particles = newParticles }, Cmd.map ParticlesMsg particlesCmd

  | DemoBoxBounced count ->
    let struct (newParticles, particlesCmd) =
      Particles.update
        (Particles.Emit(model.Player.Player.Position, 20))
        model.Particles

    {
      model with
          BoxBounces = count
          Particles = newParticles
    },
    Cmd.map ParticlesMsg particlesCmd

let subscribe
  (boxRef: ComponentRef<InteractiveBoxOverlay>)
  (ctx: GameContext)
  (model: Model)
  =
  Sub.batch [
    Player.subscribe ctx model.Player |> Sub.map "Player" PlayerMsg
    InteractiveBoxOverlayBridge.subscribeBounced boxRef DemoBoxBounced
  ]

let view (ctx: GameContext) (model: Model) (buffer: RenderBuffer<RenderCmd2D>) =
  Player.view ctx model.Player buffer
  Particles.view ctx model.Particles buffer

[<EntryPoint>]
let main argv =
  let interactiveBoxRef = ComponentRef<InteractiveBoxOverlay>()

  let program =
    Program.mkProgram init (update interactiveBoxRef)
    |> Program.withAssets
    |> Program.withRenderer(Batch2DRenderer.create view)
    |> Program.withInput
    |> Program.withTick Tick
    |> Program.withSubscription(subscribe interactiveBoxRef)
    |> Program.withComponent BouncingBoxOverlay.create
    |> Program.withComponentRef
      interactiveBoxRef
      InteractiveBoxOverlayBridge.create

  use game = new ElmishGame<Model, Msg>(program)
  game.Run()
  0
