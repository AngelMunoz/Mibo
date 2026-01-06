namespace Mibo.Elmish

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX

type Effect<'Msg> = delegate of ('Msg -> unit) -> unit

[<Struct>]
type Cmd<'Msg> =
  | Empty
  | Single of single: Effect<'Msg>
  | Batch of batch: Effect<'Msg>[]

module Cmd =
  let none: Cmd<'Msg> = Empty
  let inline ofEffect(eff: Effect<'Msg>) = Single eff

  let batch(cmds: seq<Cmd<'Msg>>) : Cmd<'Msg> =
    let mutable count = 0

    for c in cmds do
      match c with
      | Empty -> ()
      | Single _ -> count <- count + 1
      | Batch b -> count <- count + b.Length

    if count = 0 then
      Empty
    else
      let arr = Array.zeroCreate<Effect<'Msg>> count
      let mutable i = 0

      for c in cmds do
        match c with
        | Empty -> ()
        | Single eff ->
          arr[i] <- eff
          i <- i + 1
        | Batch b ->
          Array.Copy(b, 0, arr, i, b.Length)
          i <- i + b.Length

      Batch arr

  let ofAsync
    (task: Async<'T>)
    (ofSuccess: 'T -> 'Msg)
    (ofError: exn -> 'Msg)
    : Cmd<'Msg> =
    Single(
      Effect<'Msg>(fun dispatch ->
        async {
          try
            let! result = task
            dispatch(ofSuccess result)
          with ex ->
            dispatch(ofError ex)
        }
        |> Async.StartImmediate)
    )

  let ofTask
    (task: Threading.Tasks.Task<'T>)
    (ofSuccess: 'T -> 'Msg)
    (ofError: exn -> 'Msg)
    : Cmd<'Msg> =
    Single(
      Effect<'Msg>(fun dispatch ->
        async {
          try
            let! result = task |> Async.AwaitTask
            dispatch(ofSuccess result)
          with ex ->
            dispatch(ofError ex)
        }
        |> Async.StartImmediate)
    )


type SubId = string list
type Dispatch<'Msg> = 'Msg -> unit
type Subscribe<'Msg> = Dispatch<'Msg> -> IDisposable

[<Struct>]
type Sub<'Msg> =
  | NoSub
  | Active of SubId * Subscribe<'Msg>
  | BatchSub of Sub<'Msg>[]

module Sub =
  let none = NoSub

  let batch(subs: seq<Sub<'Msg>>) =
    let arr = ResizeArray<Sub<'Msg>>()

    for s in subs do
      match s with
      | NoSub -> ()
      | Active _ -> arr.Add(s)
      | BatchSub b -> arr.AddRange(b)

    if arr.Count = 0 then NoSub else BatchSub(arr.ToArray())

  [<TailCall>]
  let rec internal flatten
    (stack: ResizeArray<Sub<'Msg>>)
    (results: ResizeArray<struct (SubId * Subscribe<'Msg>)>)
    =
    if stack.Count = 0 then
      ()
    else
      let last = stack.Count - 1
      let s = stack.[last]
      stack.RemoveAt(last)

      match s with
      | NoSub -> ()
      | Active(id, func) -> results.Add(id, func)
      | BatchSub subs ->
        for i = subs.Length - 1 downto 0 do
          stack.Add(subs.[i])

      flatten stack results

  [<Struct>]
  type private MapWork<'A> =
    | Visit of sub: Sub<'A>
    | BuildBatch of len: int

  let map (idPrefix: string) (f: 'A -> 'Msg) (sub: Sub<'A>) : Sub<'Msg> =
    let work = ResizeArray<MapWork<'A>>(64)
    let results = ResizeArray<Sub<'Msg>>(64)

    work.Add(Visit sub)

    while work.Count <> 0 do
      let last = work.Count - 1
      let item = work.[last]
      work.RemoveAt(last)

      match item with
      | Visit s ->
        match s with
        | NoSub -> results.Add(NoSub)
        | Active(subId, subscribe) ->
          let newId = idPrefix :: subId

          let newSub(dispatch: Dispatch<'Msg>) =
            let innerDispatch msgA = dispatch(f msgA)
            subscribe innerDispatch

          results.Add(Active(newId, newSub))
        | BatchSub subs ->
          let len = subs.Length
          work.Add(BuildBatch len)

          for i = len - 1 downto 0 do
            work.Add(Visit subs.[i])

      | BuildBatch len ->
        if len = 0 then
          results.Add(BatchSub [||])
        else
          let start = results.Count - len
          let mapped = Array.zeroCreate<Sub<'Msg>> len

          for i = 0 to len - 1 do
            mapped.[i] <- results.[start + i]

          results.RemoveRange(start, len)
          results.Add(BatchSub mapped)

    if results.Count = 0 then
      NoSub
    else
      results.[results.Count - 1]

type IEngineService =
  abstract member Update: GameTime -> unit

type IRenderer<'Model> =
  abstract member Draw: 'Model -> GameTime -> unit

/// Mutable handle for a MonoGame component instance created during game initialization.
///
/// This is intended to be allocated in the composition root (per game instance) and then
/// threaded into Elmish `update`/`subscribe` functions, avoiding global/module-level mutable state.
type ComponentRef<'T when 'T :> IGameComponent>() =
  let mutable value: 'T voption = ValueNone

  member _.TryGet() : 'T voption = value
  member _.Set(v: 'T) = value <- ValueSome v
  member _.Clear() = value <- ValueNone

/// A small, allocation-friendly buffer that stores commands tagged with a sort key.
///
/// This is intentionally rendering-agnostic. Graphics plugins can define their own key type
/// (e.g. `int<RenderLayer>`) and either rely on the default comparer or provide one.
type RenderBuffer<'Key, 'Cmd>(?capacity: int, ?keyComparer: IComparer<'Key>) =
  let items = ResizeArray<struct ('Key * 'Cmd)>(defaultArg capacity 1024)
  let keyComparer = defaultArg keyComparer Comparer<'Key>.Default

  let comparer =
    { new IComparer<struct ('Key * 'Cmd)> with
        member _.Compare(x, y) =
          let struct (kx, _) = x
          let struct (ky, _) = y
          keyComparer.Compare(kx, ky)
    }

  member _.Clear() = items.Clear()
  member _.Add(key: 'Key, cmd: 'Cmd) = items.Add(struct (key, cmd))
  member _.Sort() = items.Sort(comparer)
  member _.Count = items.Count
  member _.Item(i) = items.[i]


type Program<'Model, 'Msg> = {
  Init: unit -> struct ('Model * Cmd<'Msg>)
  Update: 'Msg -> 'Model -> struct ('Model * Cmd<'Msg>)
  Subscribe: 'Model -> Sub<'Msg>
  Services: (Game -> IEngineService) list
  Renderers: (Game -> IRenderer<'Model>) list
  // NOTE: Program can host both Elmish-style services/renderers and native MonoGame components.
  // `Components` are attached to `Game.Components` during initialization to enable drop-in
  // compatibility with third-party GameComponent/DrawableGameComponent libraries.
  Components: (Game -> IGameComponent) list
  Load: (GraphicsDevice -> unit) list
  Tick: (GameTime -> 'Msg) voption
}

module Program =
  let mkProgram init update = {
    Init = init
    Update = update
    Subscribe = (fun _ -> Sub.none)
    Services = []
    Renderers = []
    Components = []
    Load = []
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

  /// Attach a MonoGame component to the game.
  ///
  /// Components are created and added to `Game.Components` during `ElmishGame.Initialize()`.
  /// Use this to integrate third-party MonoGame libraries that expose `GameComponent`/
  /// `DrawableGameComponent` implementations without writing adapters.
  let withComponent
    (factory: Game -> IGameComponent)
    (program: Program<'Model, 'Msg>)
    =
    {
      program with
          Components = factory :: program.Components
    }

  /// Attach a MonoGame component to the game and capture its instance in a `ComponentRef`.
  ///
  /// This keeps third-party components "native" (they live in `Game.Components`) while still
  /// allowing Elmish code to interact with them without global/module-level mutable bindings.
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

  let withLoadContent
    (load: GraphicsDevice -> unit)
    (program: Program<'Model, 'Msg>)
    =
    {
      program with
          Load = load :: program.Load
    }


type ElmishGame<'Model, 'Msg>(program: Program<'Model, 'Msg>) as this =
  inherit Game()

  let graphics = new GraphicsDeviceManager(this)
  let mutable state: 'Model = Unchecked.defaultof<'Model>
  let pendingMsgs = System.Collections.Concurrent.ConcurrentQueue<'Msg>()
  let activeSubs = Dictionary<SubId, IDisposable>()
  let services = ResizeArray<IEngineService>()
  let renderers = ResizeArray<IRenderer<'Model>>()
  let subBuffer = ResizeArray<struct (SubId * Subscribe<'Msg>)>()
  let subStack = ResizeArray<Sub<'Msg>>()

  let dispatch(msg: 'Msg) = pendingMsgs.Enqueue(msg)

  let execCmd(cmd: Cmd<'Msg>) =
    match cmd with
    | Empty -> ()
    | Single eff -> eff.Invoke(dispatch)
    | Batch effs ->
      for i = 0 to effs.Length - 1 do
        effs[i].Invoke(dispatch)

  let updateSubs() =
    subBuffer.Clear()
    subStack.Clear()
    subStack.Add(program.Subscribe state)
    Sub.flatten subStack subBuffer

    let currentKeys = activeSubs.Keys |> Seq.toArray
    let newKeysSet = HashSet<SubId>()

    for (id, subscribeFn) in subBuffer do
      newKeysSet.Add(id) |> ignore

      if not(activeSubs.ContainsKey(id)) then
        try
          activeSubs.Add(id, subscribeFn dispatch)
        with ex ->
          Console.WriteLine($"Error starting sub {id}: {ex}")

    for key in currentKeys do
      if not(newKeysSet.Contains(key)) then
        match activeSubs.TryGetValue(key) with
        | true, disp ->
          disp.Dispose()
          activeSubs.Remove(key) |> ignore
        | _ -> ()

  do
    this.Content.RootDirectory <- "Content"
    this.IsMouseVisible <- true
    this.Window.AllowUserResizing <- true
    graphics.PreferredBackBufferWidth <- 800
    graphics.PreferredBackBufferHeight <- 600

  override _.Initialize() =
    // Add MonoGame components *before* base.Initialize() so they receive Initialize/LoadContent
    // lifecycle callbacks according to MonoGame's normal component pipeline.
    for f in program.Components do
      try
        this.Components.Add(f this) |> ignore
      with ex ->
        Console.WriteLine($"Error adding component: {ex}")

    for f in program.Services do
      services.Add(f this)

    for f in program.Renderers do
      renderers.Add(f this)

    base.Initialize()
    let struct (initialState, initialCmds) = program.Init()
    state <- initialState
    execCmd initialCmds
    updateSubs()

  override _.LoadContent() =
    for load in program.Load do
      load this.GraphicsDevice

    base.LoadContent()

  override _.Update gameTime =
    program.Tick
    |> ValueOption.iter(fun map ->
      let msg = map gameTime
      dispatch msg)

    for i = 0 to services.Count - 1 do
      services[i].Update(gameTime)

    let mutable stateChanged = false
    let mutable msg = Unchecked.defaultof<'Msg>

    while pendingMsgs.TryDequeue(&msg) do
      let struct (newState, cmds) = program.Update msg state
      state <- newState
      execCmd cmds
      stateChanged <- true

    if stateChanged then
      updateSubs()

    base.Update gameTime

  override _.Draw gameTime =
    for i = 0 to renderers.Count - 1 do
      renderers[i].Draw state gameTime

    base.Draw gameTime
