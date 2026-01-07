module Mibo.Elmish.Program

open System
open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Input

let mkProgram (init: GameContext -> struct ('Model * Cmd<'Msg>)) update = {
  Init = init
  Update = update
  Subscribe = (fun _ctx _model -> Sub.none)
  Config = ValueNone
  Renderers = []
  Components = []
  Tick = ValueNone
}

let withSubscription
  (subscribe: GameContext -> 'Model -> Sub<'Msg>)
  (program: Program<'Model, 'Msg>)
  =
  { program with Subscribe = subscribe }

/// Configure MonoGame game settings (resolution, vsync, window, etc).
/// The callback receives the Game instance and GraphicsDeviceManager.
let withConfig
  (configure: Game * GraphicsDeviceManager -> unit)
  (program: Program<'Model, 'Msg>)
  =
  {
    program with
        Config = ValueSome configure
  }

let withRenderer
  (factory: Game -> IRenderer<'Model>)
  (program: Program<'Model, 'Msg>)
  =
  {
    program with
        Renderers = factory :: program.Renderers
  }

let withComponent
  (factory: Game -> IGameComponent)
  (program: Program<'Model, 'Msg>)
  =
  {
    program with
        Components = factory :: program.Components
  }

let withComponentRef<'Model, 'Msg, 'T when 'T :> IGameComponent>
  (componentRef: ComponentRef<'T>)
  (factory: Game -> 'T)
  (program: Program<'Model, 'Msg>)
  =
  withComponent
    (fun game ->
      let c = factory game
      componentRef.Set c
      c :> IGameComponent)
    program

let withTick (map: GameTime -> 'Msg) (program: Program<'Model, 'Msg>) = {
  program with
      Tick = ValueSome map
}

let withAssets(program: Program<'Model, 'Msg>) : Program<'Model, 'Msg> =
  let originalInit = program.Init

  let wrappedInit ctx =
    let assets = AssetsService.createFromContext ctx

    // Replace any existing registration (defensive).
    try
      ctx.Game.Services.RemoveService typeof<IAssets>
    with _ ->
      ()

    ctx.Game.Services.AddService(typeof<IAssets>, assets)

    // Best-effort cleanup when the game is exiting.
    ctx.Game.Exiting.Add(fun _ ->
      try
        assets.Dispose()
      with _ ->
        ())

    originalInit ctx

  { program with Init = wrappedInit }

/// Register the input component (polls hardware each frame, publishes deltas via IInput).
let withInput(program: Program<'Model, 'Msg>) : Program<'Model, 'Msg> =
  withComponent
    (fun game ->
      let input = Input.create game

      // Register as service so subscriptions can find it
      try
        game.Services.RemoveService typeof<IInput>
      with _ ->
        ()

      game.Services.AddService(typeof<IInput>, input)
      input :> IGameComponent)
    program
