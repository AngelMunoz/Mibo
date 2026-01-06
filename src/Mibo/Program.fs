module Mibo.Elmish.Program

open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Input

let mkProgram (init: GameContext -> struct ('Model * Cmd<'Msg>)) update = {
  Init = init
  Update = update
  Subscribe = (fun _ -> Sub.none)
  Services = []
  Renderers = []
  Components = []
  Tick = ValueNone
}

let withSubscription
  (subscribe: 'Model -> Sub<'Msg>)
  (program: Program<'Model, 'Msg>)
  =
  { program with Subscribe = subscribe }

let withService
  (factory: Game -> IEngineService)
  (program: Program<'Model, 'Msg>)
  =
  {
    program with
        Services = factory :: program.Services
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
    AssetsInternal.initialize ctx.Content ctx.GraphicsDevice
    originalInit ctx

  { program with Init = wrappedInit }

let inline withInput(program: Program<'Model, 'Msg>) : Program<'Model, 'Msg> =
  withService (fun _ -> InputServiceInternal.create()) program
