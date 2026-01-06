module Gamino.Elmish

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

// --- Render Optimization ---

/// Defines the drawing order. Lower numbers draw first.
type RenderLayer =
    | Background = 0
    | World = 1
    | Particles = 2
    | UI = 3

/// A data-only command for rendering.
[<Struct>]
type RenderCmd =
    | DrawTexture of
        texture: Texture2D *
        dest: Rectangle *
        source: Nullable<Rectangle> *
        color: Color *
        rotation: float32 *
        origin: Vector2 *
        effects: SpriteEffects *
        depth: float32

/// A flat command buffer.
type RenderBuffer() =
    // One bucket per layer.
    // Optimization: We assume RenderLayer values are dense 0..N
    // We initialize 4 buckets corresponding to Background(0)..UI(3) (actually UI is 3000, wait!)
    // The previous RenderLayer enum used large gaps (1000, 2000, 3000).
    // We should normalize them or use a Dictionary/Map if sparse?
    // No, for performance, we want array indexing.
    //
    // Let's redefine RenderLayer to be 0, 1, 2, 3 for this optimization.
    // Or map them. Redefining is cleanest.

    // We need to access RenderLayer values here or assume max count.
    // Let's assume a fixed count for the engine core.
    static let LayerCount = 4
    let buckets = Array.init LayerCount (fun _ -> ResizeArray<RenderCmd>(1024))

    member _.Clear() =
        for b in buckets do
            b.Clear()

    member _.Add(layer: RenderLayer, cmd: RenderCmd) =
        // We trust the enum value is a valid index.
        // If we keep the 1000 gaps, this crashes.
        // We MUST re-define RenderLayer values to 0, 1, 2, 3 in the type definition above first!
        // See replacement note.
        buckets[int layer].Add(cmd)

    member _.Execute(sb: SpriteBatch) =
        // No sorting needed! Just iterate buckets in order.
        for b in buckets do
            let count = b.Count

            for i = 0 to count - 1 do
                let cmd = b[i]

                match cmd with
                | DrawTexture(tex, dest, src, color, rot, origin, fx, depth) ->
                    if src.HasValue then
                        sb.Draw(tex, dest, src.Value, color, rot, origin, fx, depth)
                    else
                        sb.Draw(tex, dest, color)

/// Builder API for Ergonomics
module Render =
    let inline draw (texture: Texture2D) (dest: Rectangle) (color: Color) (layer: RenderLayer) (buffer: RenderBuffer) =
        buffer.Add(layer, DrawTexture(texture, dest, Nullable(), color, 0.0f, Vector2.Zero, SpriteEffects.None, 0.0f))
        buffer

    let inline drawEx
        (texture: Texture2D)
        (dest: Rectangle)
        (src: Nullable<Rectangle>)
        (color: Color)
        (rot: float32)
        (origin: Vector2)
        (layer: RenderLayer)
        (buffer: RenderBuffer)
        =
        buffer.Add(layer, DrawTexture(texture, dest, src, color, rot, origin, SpriteEffects.None, 0.0f))
        buffer

/// The pure definition of the Game.
/// Uses Struct Tuples to avoid heap allocation on return.
type Program<'Model, 'Msg> =
    { Init: unit -> struct ('Model * Cmd<'Msg>)
      Update: 'Msg -> 'Model -> struct ('Model * Cmd<'Msg>)
      // View takes buffer and returns unit (populates it)
      View: 'Model -> RenderBuffer -> unit
      Subscribe: 'Model -> Sub<'Msg> }

module Program =
    let mkProgram init update view =
        { Init = init
          Update = update
          View = view
          Subscribe = fun _ -> Sub.none }

    let withSubscription (subscribe: 'Model -> Sub<'Msg>) (program: Program<'Model, 'Msg>) =
        { program with Subscribe = subscribe }

// --- Pluggability ---

/// Interface for external systems that need to hook into the main Game Loop
type IEngineService =
    abstract member Update: GameTime -> unit

// --- The Generic Game Runner ---

type ElmishGame<'Model, 'Msg>(program: Program<'Model, 'Msg>) as this =
    inherit Game()

    let graphics = new GraphicsDeviceManager(this)
    let mutable spriteBatch: SpriteBatch = null

    // The current state
    let mutable state: 'Model = Unchecked.defaultof<'Model>

    // Concurrent queue for thread-safety (Input/Tasks -> Update Thread)
    let pendingMsgs = System.Collections.Concurrent.ConcurrentQueue<'Msg>()

    // Reusable RenderBuffer (Zero per-frame allocation)
    let renderBuffer = RenderBuffer()

    // Active Subscriptions
    let activeSubs = Dictionary<SubId, IDisposable>()

    // Pluggable Services
    let services = ResizeArray<IEngineService>()

    do
        this.Content.RootDirectory <- "Content"
        this.IsMouseVisible <- true
        this.Window.AllowUserResizing <- true
        graphics.PreferredBackBufferWidth <- 800
        graphics.PreferredBackBufferHeight <- 600

    member this.Dispatch(msg: 'Msg) = pendingMsgs.Enqueue(msg)

    /// Registers a service to be updated every frame
    member this.RegisterService(service: IEngineService) = services.Add(service)

    // Diff and update subscriptions
    member private this.UpdateSubs() =
        let newSubsList = Sub.toList (program.Subscribe state)

        // 1. Identify subs to keep, start, and stop
        // Naive efficient approach:
        // - Mark all current as "potentially dead"
        // - Iterate new: if exists, mark "alive". If not, start and add.
        // - Stop all remaining "dead".

        // But SubId is string list (reference comparison is tricky if re-allocated).
        // We assume SubId structure is stable.

        let currentKeys = activeSubs.Keys |> Seq.toArray // Copy keys to iterate safely
        let newKeysSet = HashSet<SubId>()

        for (id, subscribeFn) in newSubsList do
            newKeysSet.Add(id) |> ignore

            if not (activeSubs.ContainsKey(id)) then
                // Start new sub
                try
                    let disposable = subscribeFn this.Dispatch
                    activeSubs.Add(id, disposable)
                with ex ->
                    Console.WriteLine($"Error starting sub {id}: {ex}")

        // Stop dead subs
        for key in currentKeys do
            if not (newKeysSet.Contains(key)) then
                match activeSubs.TryGetValue(key) with
                | true, disp ->
                    disp.Dispose()
                    activeSubs.Remove(key) |> ignore
                | _ -> ()

    override this.Initialize() =
        base.Initialize()

        let struct (initialState, initialCmds) = program.Init()
        state <- initialState

        this.ExecCmd initialCmds
        this.UpdateSubs() // Initial subs

    // Helper to execute Cmd struct
    member private this.ExecCmd(cmd: Cmd<'Msg>) =
        match cmd with
        | Empty -> ()
        | Single eff -> eff.Invoke(this.Dispatch)
        | Batch effs ->
            for i = 0 to effs.Length - 1 do
                effs[i].Invoke(this.Dispatch)

    override this.LoadContent() =
        spriteBatch <- new SpriteBatch(this.GraphicsDevice)

    override this.Update(gameTime) =
        // 1. Update Services
        for i = 0 to services.Count - 1 do
            services[i].Update(gameTime)

        // 2. Process Message Queue
        let mutable loop = true
        let mutable stateChanged = false

        while loop do
            match pendingMsgs.TryDequeue() with
            | true, msg ->
                let struct (newState, cmds) = program.Update msg state
                state <- newState
                this.ExecCmd cmds
                stateChanged <- true
            | _ -> loop <- false

        // Only update subs if state changed (optimization)
        if stateChanged then
            this.UpdateSubs()

        base.Update(gameTime)

    override this.Draw(gameTime) =
        this.GraphicsDevice.Clear Color.CornflowerBlue

        // 1. Reset Buffer
        renderBuffer.Clear()

        // 2. Populate Buffer (Pure View Logic)
        program.View state renderBuffer

        // 3. Execute Buffer
        spriteBatch.Begin()
        renderBuffer.Execute(spriteBatch)
        spriteBatch.End()

        base.Draw(gameTime)
