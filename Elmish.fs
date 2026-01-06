namespace Gamino.Elmish

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input

// --- Performance Core ---

/// A side-effect task.
/// Defined as a delegate to avoid F# function closure overhead when possible,
/// though often mapped from F# funcs.
type Effect<'Msg> = delegate of ('Msg -> unit) -> unit

/// A container for effects.
/// Struct-based to avoid allocation when passing it around.
[<Struct>]
type Cmd<'Msg> =
    | Empty
    | Single of single: Effect<'Msg>
    | Batch of batch: Effect<'Msg>[]

module Cmd =
    let none: Cmd<'Msg> = Empty

    let inline ofEffect (eff: Effect<'Msg>) = Single eff

    let batch (cmds: seq<Cmd<'Msg>>) : Cmd<'Msg> =
        // Flattens the sequence into a single array
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

    let ofAsync (task: Async<'T>) (ofSuccess: 'T -> 'Msg) (ofError: exn -> 'Msg) : Cmd<'Msg> =
        Single(
            Effect<'Msg>(fun dispatch ->
                async {
                    try
                        let! result = task
                        dispatch (ofSuccess result)
                    with ex ->
                        dispatch (ofError ex)
                }
                |> Async.StartImmediate)
        )

// --- Subscriptions ---

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

    let batch (subs: seq<Sub<'Msg>>) =
        // Flatten
        let arr = ResizeArray<Sub<'Msg>>()

        for s in subs do
            match s with
            | NoSub -> ()
            | Active _ -> arr.Add(s)
            | BatchSub b -> arr.AddRange(b)

        if arr.Count = 0 then NoSub else BatchSub(arr.ToArray())

    // Internal helper to flatten a tree of subs into a list of (Id, Subscribe)
    let internal toList (sub: Sub<'Msg>) =
        let results = ResizeArray<SubId * Subscribe<'Msg>>()

        let rec traverse s =
            match s with
            | NoSub -> ()
            | Active(id, func) -> results.Add(id, func)
            | BatchSub subs ->
                for item in subs do
                    traverse item

        traverse sub
        results

    /// Maps a subscription to a new message type
    let rec map (idPrefix: string) (f: 'A -> 'Msg) (sub: Sub<'A>) : Sub<'Msg> =
        match sub with
        | NoSub -> NoSub
        | Active(subId, subscribe) ->
            let newId = idPrefix :: subId

            let newSubscribe (dispatch: Dispatch<'Msg>) =
                // Map the dispatch function
                let innerDispatch msgA = dispatch (f msgA)
                subscribe innerDispatch

            Active(newId, newSubscribe)
        | BatchSub subs ->
            let mapped = Array.zeroCreate subs.Length

            for i = 0 to subs.Length - 1 do
                mapped[i] <- map idPrefix f subs[i]

            BatchSub mapped

// --- Pluggability ---

/// Interface for external systems that need to hook into the main Game Loop
type IEngineService =
    abstract member Update: GameTime -> unit

type IRenderer<'Model> =
    abstract member Draw: 'Model -> GameTime -> unit

// --- Core ---

type Program<'Model, 'Msg> =
    { Init: unit -> struct ('Model * Cmd<'Msg>)
      Update: 'Msg -> 'Model -> struct ('Model * Cmd<'Msg>)
      Subscribe: 'Model -> Sub<'Msg>
      // Pluggable Factories
      Services: (Game -> IEngineService) list
      Renderers: (Game -> IRenderer<'Model>) list
      // Resource Loading
      Load: (GraphicsDevice -> unit) list
      // System Events
      Tick: (GameTime -> 'Msg) voption }

module Program =
    let mkProgram init update =
        { Init = init
          Update = update
          Subscribe = fun _ -> Sub.none
          Services = []
          Renderers = []
          Load = []
          Tick = ValueNone }

    let withSubscription (subscribe: 'Model -> Sub<'Msg>) (program: Program<'Model, 'Msg>) =
        { program with Subscribe = subscribe }

    let withService (factory: Game -> IEngineService) (program: Program<'Model, 'Msg>) =
        { program with
            Services = factory :: program.Services }

    let withRenderer (factory: Game -> IRenderer<'Model>) (program: Program<'Model, 'Msg>) =
        { program with
            Renderers = factory :: program.Renderers }

    let withTick (map: GameTime -> 'Msg) (program: Program<'Model, 'Msg>) = { program with Tick = ValueSome map }

    let withLoadContent (load: GraphicsDevice -> unit) (program: Program<'Model, 'Msg>) =
        { program with
            Load = load :: program.Load }

// --- The Generic Game Runner ---

type ElmishGame<'Model, 'Msg>(program: Program<'Model, 'Msg>) as this =
    inherit Game()

    let graphics = new GraphicsDeviceManager(this)

    let mutable state: 'Model = Unchecked.defaultof<'Model>
    let pendingMsgs = System.Collections.Concurrent.ConcurrentQueue<'Msg>()
    let activeSubs = Dictionary<SubId, IDisposable>()

    let services = ResizeArray<IEngineService>()
    let renderers = ResizeArray<IRenderer<'Model>>()

    let dispatch (msg: 'Msg) = pendingMsgs.Enqueue(msg)

    let execCmd (cmd: Cmd<'Msg>) =
        match cmd with
        | Empty -> ()
        | Single eff -> eff.Invoke dispatch
        | Batch effs ->
            for i = 0 to effs.Length - 1 do
                effs[i].Invoke dispatch


    let updateSubs () =
        let newSubsList = Sub.toList (program.Subscribe state)
        let currentKeys = activeSubs.Keys |> Seq.toArray
        let newKeysSet = HashSet<SubId>()

        for id, subscribeFn in newSubsList do
            newKeysSet.Add(id) |> ignore

            if not (activeSubs.ContainsKey(id)) then
                try
                    let disposable = subscribeFn dispatch
                    activeSubs.Add(id, disposable)
                with ex ->
                    Console.WriteLine($"Error starting sub {id}: {ex}")

        for key in currentKeys do
            if not (newKeysSet.Contains(key)) then
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

    override this.Initialize() =
        // Bootstrap factories
        for factory in program.Services do
            services.Add(factory this)

        for factory in program.Renderers do
            renderers.Add(factory this)

        base.Initialize()
        let struct (initialState, initialCmds) = program.Init()
        state <- initialState
        execCmd initialCmds
        updateSubs ()

    override this.LoadContent() =
        for load in program.Load do
            load this.GraphicsDevice

        base.LoadContent()

    override this.Update gameTime =
        // System Tick
        match program.Tick with
        | ValueSome map -> dispatch (map gameTime)
        | ValueNone -> ()

        for i = 0 to services.Count - 1 do
            services[i].Update gameTime


        let mutable stateChanged = false
        let mutable msg = Unchecked.defaultof<'Msg>

        while pendingMsgs.TryDequeue(&msg) do
            let struct (newState, cmds) = program.Update msg state
            state <- newState
            execCmd cmds
            stateChanged <- true

        if stateChanged then
            updateSubs ()

        base.Update gameTime

    override _.Draw gameTime =
        for i = 0 to renderers.Count - 1 do
            renderers[i].Draw state gameTime

        base.Draw gameTime
