namespace Gamino.Elmish

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX


[<Measure>]
type RenderLayer

type Effect<'Msg> = delegate of ('Msg -> unit) -> unit

[<Struct>]
type Cmd<'Msg> =
    | Empty
    | Single of single: Effect<'Msg>
    | Batch of batch: Effect<'Msg>[]

module Cmd =
    let none: Cmd<'Msg> = Empty
    let inline ofEffect (eff: Effect<'Msg>) = Single eff

    let batch (cmds: seq<Cmd<'Msg>>) : Cmd<'Msg> =
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
        let arr = ResizeArray<Sub<'Msg>>()

        for s in subs do
            match s with
            | NoSub -> ()
            | Active _ -> arr.Add(s)
            | BatchSub b -> arr.AddRange(b)

        if arr.Count = 0 then NoSub else BatchSub(arr.ToArray())

    [<TailCall>]
    let rec internal flatten (stack: ResizeArray<Sub<'Msg>>) (results: ResizeArray<SubId * Subscribe<'Msg>>) =
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

                    let newSub (dispatch: Dispatch<'Msg>) =
                        let innerDispatch msgA = dispatch (f msgA)
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

type RenderBuffer<'Cmd>() =
    let items = ResizeArray<struct (int<RenderLayer> * 'Cmd)>(1024)

    static let comparer =
        { new IComparer<struct (int<RenderLayer> * 'Cmd)> with
            member _.Compare(x, y) =
                let struct (lx, _) = x
                let struct (ly, _) = y

                if lx < ly then -1
                elif lx > ly then 1
                else 0 }

    member _.Clear() = items.Clear()
    member _.Add(layer: int<RenderLayer>, cmd: 'Cmd) = items.Add(struct (layer, cmd))
    member _.Sort() = items.Sort(comparer)
    member _.Count = items.Count
    member _.Item(i) = items.[i]


type Program<'Model, 'Msg> =
    { Init: unit -> struct ('Model * Cmd<'Msg>)
      Update: 'Msg -> 'Model -> struct ('Model * Cmd<'Msg>)
      Subscribe: 'Model -> Sub<'Msg>
      Services: (Game -> IEngineService) list
      Renderers: (Game -> IRenderer<'Model>) list
      Load: (GraphicsDevice -> unit) list
      Tick: (GameTime -> 'Msg) voption }

module Program =
    let mkProgram init update =
        { Init = init
          Update = update
          Subscribe = (fun _ -> Sub.none)
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


type ElmishGame<'Model, 'Msg>(program: Program<'Model, 'Msg>) as this =
    inherit Game()

    let graphics = new GraphicsDeviceManager(this)
    let mutable state: 'Model = Unchecked.defaultof<'Model>
    let pendingMsgs = System.Collections.Concurrent.ConcurrentQueue<'Msg>()
    let activeSubs = Dictionary<SubId, IDisposable>()
    let services = ResizeArray<IEngineService>()
    let renderers = ResizeArray<IRenderer<'Model>>()
    let subBuffer = ResizeArray<SubId * Subscribe<'Msg>>()
    let subStack = ResizeArray<Sub<'Msg>>()

    let dispatch (msg: 'Msg) = pendingMsgs.Enqueue(msg)

    let execCmd (cmd: Cmd<'Msg>) =
        match cmd with
        | Empty -> ()
        | Single eff -> eff.Invoke(dispatch)
        | Batch effs ->
            for i = 0 to effs.Length - 1 do
                effs[i].Invoke(dispatch)

    let updateSubs () =
        subBuffer.Clear()
        subStack.Clear()
        subStack.Add(program.Subscribe state)
        Sub.flatten subStack subBuffer

        let currentKeys = activeSubs.Keys |> Seq.toArray
        let newKeysSet = HashSet<SubId>()

        for (id, subscribeFn) in subBuffer do
            newKeysSet.Add(id) |> ignore

            if not (activeSubs.ContainsKey(id)) then
                try
                    activeSubs.Add(id, subscribeFn dispatch)
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

    override _.Initialize() =
        for f in program.Services do
            services.Add(f this)

        for f in program.Renderers do
            renderers.Add(f this)

        base.Initialize()
        let struct (initialState, initialCmds) = program.Init()
        state <- initialState
        execCmd initialCmds
        updateSubs ()

    override _.LoadContent() =
        for load in program.Load do
            load this.GraphicsDevice

        base.LoadContent()

    override _.Update(gameTime) =
        program.Tick
        |> ValueOption.iter (fun map ->
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
            updateSubs ()

        base.Update(gameTime)

    override _.Draw(gameTime) =
        for i = 0 to renderers.Count - 1 do
            renderers[i].Draw state gameTime

        base.Draw(gameTime)
