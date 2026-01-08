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
      // Idempotent behavior: if input is already registered, do nothing.
      // This avoids accidentally creating multiple polling components when a user
      // composes `withInput`/`withInputMapper`/`withInputMapping` together.
      let existing = game.Services.GetService(typeof<IInput>)

      if not(isNull existing) then
        new GameComponent(game) :> IGameComponent
      else
        let input = Input.create game

        // Register as service so subscriptions can find it
        game.Services.AddService(typeof<IInput>, input)
        input :> IGameComponent)
    program


/// Configures the game to register an `IInputMapper<'Action>` service.
///
/// This helper lives in `Mibo.Elmish.Program` alongside other `Program.withX` helpers.
///
/// Notes:
/// - This registers `IInput` automatically (equivalent to `withInput`).
/// - The mapper is ticked via a MonoGame `GameComponent`.
/// - If you want to stay fully "Elmish" (no service access), consider using
///   `Mibo.Input.InputMapper.subscribe` instead and handle a single message.
let withInputMapper<'Model, 'Msg, 'Action when 'Action: comparison>
  (initialMap: InputMap<'Action>)
  (program: Program<'Model, 'Msg>)
  : Program<'Model, 'Msg> =

  // Ensure core input exists.
  let program = program |> withInput

  let originalInit = program.Init

  let wrappedInit ctx =
    let coreInput = Input.getService ctx

    // Replace any existing mapper registration (defensive).
    let existing = ctx.Game.Services.GetService(typeof<IInputMapper<'Action>>)

    match existing with
    | null -> ()
    | :? IDisposable as d ->
      try
        d.Dispose()
      with _ ->
        ()

      try
        ctx.Game.Services.RemoveService typeof<IInputMapper<'Action>>
      with _ ->
        ()
    | _ ->
      try
        ctx.Game.Services.RemoveService typeof<IInputMapper<'Action>>
      with _ ->
        ()

    let mapper = new InputMapperService<'Action>(coreInput, initialMap)
    ctx.Game.Services.AddService(typeof<IInputMapper<'Action>>, mapper)

    // Best-effort cleanup when the game is exiting.
    ctx.Game.Exiting.Add(fun _ ->
      try
        (mapper :> IDisposable).Dispose()
      with _ ->
        ())

    // Tick mapper via GameComponent.
    ctx.Game.Components.Add
      { new GameComponent(ctx.Game) with
          override _.Update _gameTime =
            (mapper :> IInputMapper<'Action>).Update()
      }

    originalInit ctx

  { program with Init = wrappedInit }
