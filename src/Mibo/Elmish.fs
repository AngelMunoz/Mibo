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
  | DeferNextFrame of batch: Effect<'Msg>[]
  | NowAndDeferNextFrame of now: Effect<'Msg>[] * next: Effect<'Msg>[]

module Cmd =
  let none: Cmd<'Msg> = Empty
  let inline ofEffect(eff: Effect<'Msg>) = Single eff

  let inline ofMsg(msg: 'Msg) : Cmd<'Msg> =
    Single(Effect<'Msg>(fun dispatch -> dispatch msg))

  /// Defer command execution until the next frame.
  ///
  /// In the runtime, deferred commands are executed at the start of the next frame,
  /// before `Tick` is enqueued.
  let inline deferNextFrame(cmd: Cmd<'Msg>) : Cmd<'Msg> =
    match cmd with
    | Empty -> Empty
    | Single eff -> DeferNextFrame [| eff |]
    | Batch effs -> DeferNextFrame effs
    | DeferNextFrame effs -> DeferNextFrame effs
    | NowAndDeferNextFrame(now, next) ->
      let combined = Array.zeroCreate<Effect<'Msg>>(now.Length + next.Length)
      Array.Copy(now, 0, combined, 0, now.Length)
      Array.Copy(next, 0, combined, now.Length, next.Length)
      DeferNextFrame combined

  let inline private split
    (cmd: Cmd<'Msg>)
    : struct (Effect<'Msg>[] * Effect<'Msg>[]) =
    match cmd with
    | Empty -> struct ([||], [||])
    | Single eff -> struct ([| eff |], [||])
    | Batch effs -> struct (effs, [||])
    | DeferNextFrame effs -> struct ([||], effs)
    | NowAndDeferNextFrame(now, next) -> struct (now, next)

  /// Map a command producing messages of type 'A into a command producing messages of type 'Msg.
  ///
  /// This is the command equivalent of `Sub.map` and is required for parent-child composition.
  let map (f: 'A -> 'Msg) (cmd: Cmd<'A>) : Cmd<'Msg> =
    match cmd with
    | Empty -> Empty
    | Single eff ->
      Single(
        Effect<'Msg>(fun dispatch ->
          let innerDispatch(a: 'A) = dispatch(f a)
          eff.Invoke(innerDispatch))
      )
    | Batch effs ->
      let mapped = Array.zeroCreate<Effect<'Msg>> effs.Length

      for i = 0 to effs.Length - 1 do
        let eff = effs[i]

        mapped[i] <-
          Effect<'Msg>(fun dispatch ->
            let innerDispatch(a: 'A) = dispatch(f a)
            eff.Invoke(innerDispatch))

      Batch mapped

    | DeferNextFrame effs ->
      let mapped = Array.zeroCreate<Effect<'Msg>> effs.Length

      for i = 0 to effs.Length - 1 do
        let eff = effs[i]

        mapped[i] <-
          Effect<'Msg>(fun dispatch ->
            let innerDispatch(a: 'A) = dispatch(f a)
            eff.Invoke(innerDispatch))

      DeferNextFrame mapped

    | NowAndDeferNextFrame(now, next) ->
      let mapBatch(effs: Effect<'A>[]) : Effect<'Msg>[] =
        let mapped = Array.zeroCreate<Effect<'Msg>> effs.Length

        for i = 0 to effs.Length - 1 do
          let eff = effs[i]

          mapped[i] <-
            Effect<'Msg>(fun dispatch ->
              let innerDispatch(a: 'A) = dispatch(f a)
              eff.Invoke(innerDispatch))

        mapped

      NowAndDeferNextFrame(mapBatch now, mapBatch next)

  let batch(cmds: seq<Cmd<'Msg>>) : Cmd<'Msg> =
    let mutable nowCount = 0
    let mutable nextCount = 0

    for c in cmds do
      let struct (now, next) = split c
      nowCount <- nowCount + now.Length
      nextCount <- nextCount + next.Length

    if nowCount = 0 && nextCount = 0 then
      Empty
    elif nextCount = 0 then
      if nowCount = 1 then
        // Avoid allocation when possible
        let mutable eff = Unchecked.defaultof<Effect<'Msg>>

        for c in cmds do
          match c with
          | Single e -> eff <- e
          | Batch b when b.Length = 1 -> eff <- b[0]
          | _ -> ()

        Single eff
      else
        let arr = Array.zeroCreate<Effect<'Msg>> nowCount
        let mutable i = 0

        for c in cmds do
          let struct (now, _) = split c

          if now.Length <> 0 then
            Array.Copy(now, 0, arr, i, now.Length)
            i <- i + now.Length

        Batch arr
    elif nowCount = 0 then
      let arr = Array.zeroCreate<Effect<'Msg>> nextCount
      let mutable i = 0

      for c in cmds do
        let struct (_, next) = split c

        if next.Length <> 0 then
          Array.Copy(next, 0, arr, i, next.Length)
          i <- i + next.Length

      DeferNextFrame arr
    else
      let nowArr = Array.zeroCreate<Effect<'Msg>> nowCount
      let nextArr = Array.zeroCreate<Effect<'Msg>> nextCount
      let mutable ni = 0
      let mutable xi = 0

      for c in cmds do
        let struct (now, next) = split c

        if now.Length <> 0 then
          Array.Copy(now, 0, nowArr, ni, now.Length)
          ni <- ni + now.Length

        if next.Length <> 0 then
          Array.Copy(next, 0, nextArr, xi, next.Length)
          xi <- xi + next.Length

      NowAndDeferNextFrame(nowArr, nextArr)

  /// Allocation-friendly command batching for 2 commands.
  let batch2(a: Cmd<'Msg>, b: Cmd<'Msg>) : Cmd<'Msg> = batch [ a; b ]

  /// Allocation-friendly command batching for 3 commands.
  let batch3(a: Cmd<'Msg>, b: Cmd<'Msg>, c: Cmd<'Msg>) : Cmd<'Msg> =
    batch2(batch2(a, b), c)

  /// Allocation-friendly command batching for 4 commands.
  let batch4
    (a: Cmd<'Msg>, b: Cmd<'Msg>, c: Cmd<'Msg>, d: Cmd<'Msg>)
    : Cmd<'Msg> =
    batch2(batch3(a, b, c), d)

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


/// Subscription identifier.
///
/// This is used as the key for subscription diffing.
/// Keep this allocation-free in hot paths (avoid list-based IDs).
[<Measure>]
type subId

type SubId = string<subId>

module SubId =
  let inline ofString(value: string) : SubId = UMX.tag<subId> value

  let inline value(id: SubId) : string = UMX.untag id

  let inline prefix (prefix: string) (id: SubId) : SubId =
    if String.IsNullOrEmpty(prefix) then
      id
    else
      let idStr = value id

      if String.IsNullOrEmpty(idStr) then
        ofString prefix
      else
        ofString(prefix + "/" + idStr)

type Dispatch<'Msg> = 'Msg -> unit
type Subscribe<'Msg> = Dispatch<'Msg> -> IDisposable

[<Struct>]
type Sub<'Msg> =
  | NoSub
  | Active of SubId * Subscribe<'Msg>
  | BatchSub of Sub<'Msg>[]

module Sub =
  let none = NoSub

  let batch(subs: seq<Sub<'Msg>>) : Sub<'Msg> =
    // Keep this allocation-friendly: avoid intermediate ResizeArray and
    // avoid wrapping a single sub into a BatchSub array.
    let inline isNoSub s =
      match s with
      | NoSub -> true
      | _ -> false

    let mutable count = 0

    for s in subs do
      match s with
      | NoSub -> ()
      | Active _ -> count <- count + 1
      | BatchSub b -> count <- count + b.Length

    if count = 0 then
      NoSub
    elif count = 1 then
      // Return the single sub directly to avoid allocating an array.
      let mutable found = NoSub

      for s in subs do
        match s with
        | NoSub -> ()
        | Active _ ->
          if isNoSub found then
            found <- s
        | BatchSub b ->
          if b.Length = 1 && isNoSub found then
            found <- b[0]

      found
    else
      let arr = Array.zeroCreate<Sub<'Msg>> count
      let mutable i = 0

      for s in subs do
        match s with
        | NoSub -> ()
        | Active _ ->
          arr[i] <- s
          i <- i + 1
        | BatchSub b ->
          Array.Copy(b, 0, arr, i, b.Length)
          i <- i + b.Length

      BatchSub arr

  /// Allocation-friendly subscription batching for 2 subs.
  let inline batch2(a: Sub<'Msg>, b: Sub<'Msg>) : Sub<'Msg> =
    match a, b with
    | NoSub, x
    | x, NoSub -> x
    | BatchSub aa, BatchSub bb ->
      let merged = Array.zeroCreate<Sub<'Msg>>(aa.Length + bb.Length)
      Array.Copy(aa, 0, merged, 0, aa.Length)
      Array.Copy(bb, 0, merged, aa.Length, bb.Length)
      BatchSub merged
    | BatchSub aa, x ->
      let merged = Array.zeroCreate<Sub<'Msg>>(aa.Length + 1)
      Array.Copy(aa, 0, merged, 0, aa.Length)
      merged[merged.Length - 1] <- x
      BatchSub merged
    | x, BatchSub bb ->
      let merged = Array.zeroCreate<Sub<'Msg>>(1 + bb.Length)
      merged[0] <- x
      Array.Copy(bb, 0, merged, 1, bb.Length)
      BatchSub merged
    | x, y -> BatchSub [| x; y |]

  /// Allocation-friendly subscription batching for 3 subs.
  let inline batch3(a: Sub<'Msg>, b: Sub<'Msg>, c: Sub<'Msg>) : Sub<'Msg> =
    batch2(batch2(a, b), c)

  /// Allocation-friendly subscription batching for 4 subs.
  let inline batch4
    (a: Sub<'Msg>, b: Sub<'Msg>, c: Sub<'Msg>, d: Sub<'Msg>)
    : Sub<'Msg> =
    batch2(batch3(a, b, c), d)

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
      | Active(id, func) -> results.Add(struct (id, func))
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
          let newId = SubId.prefix idPrefix subId

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

/// Context passed to init, providing access to game resources.
type GameContext = {
  GraphicsDevice: GraphicsDevice
  Content: Content.ContentManager
  Game: Game
}

// IEngineService has been removed. Use GameComponent or implement IUpdateable directly.

type IRenderer<'Model> =
  abstract member Draw: GameContext * 'Model * GameTime -> unit

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
  let initialCapacity = defaultArg capacity 1024

  let mutable items =
    Buffers.ArrayPool<struct ('Key * 'Cmd)>.Shared.Rent initialCapacity

  let mutable count = 0
  let keyComparer = defaultArg keyComparer Comparer<'Key>.Default

  let comparer =
    { new IComparer<struct ('Key * 'Cmd)> with
        member _.Compare(x, y) =
          let struct (kx, _) = x
          let struct (ky, _) = y
          keyComparer.Compare(kx, ky)
    }

  let ensureCapacity(needed: int) =
    if count + needed > items.Length then
      let newSize = max (items.Length * 2) (count + needed)
      let newArr = Buffers.ArrayPool<struct ('Key * 'Cmd)>.Shared.Rent(newSize)
      items.AsSpan(0, count).CopyTo(newArr.AsSpan())
      Buffers.ArrayPool<struct ('Key * 'Cmd)>.Shared.Return(items)
      items <- newArr

  member _.Clear() = count <- 0

  member _.Add(key: 'Key, cmd: 'Cmd) =
    ensureCapacity 1
    items[count] <- struct (key, cmd)
    count <- count + 1

  member _.Sort() =
    // Sort only the used portion via Span
    let span = items.AsSpan(0, count)
    span.Sort comparer

  member _.Count = count
  member _.Item(i) = items[i]

type Program<'Model, 'Msg> = {
  Init: GameContext -> struct ('Model * Cmd<'Msg>)
  Update: 'Msg -> 'Model -> struct ('Model * Cmd<'Msg>)
  Subscribe: GameContext -> 'Model -> Sub<'Msg>
  /// Configuration callback invoked in the game constructor.
  /// Use this to set resolution, vsync, window settings, etc.
  Config: (Game * GraphicsDeviceManager -> unit) voption
  Renderers: (Game -> IRenderer<'Model>) list
  Components: (Game -> IGameComponent) list
  Tick: (GameTime -> 'Msg) voption
}


type ElmishGame<'Model, 'Msg>(program: Program<'Model, 'Msg>) as this =
  inherit Game()

  let graphics = new GraphicsDeviceManager(this)
  let mutable state: 'Model = Unchecked.defaultof<'Model>
  let mutable ctxOpt: GameContext voption = ValueNone
  let pendingMsgs = Collections.Concurrent.ConcurrentQueue<'Msg>()
  let activeSubs = Collections.Generic.Dictionary<SubId, IDisposable>()
  let subIdsInUse = Collections.Generic.HashSet<SubId>()
  let subIdsToRemove = ResizeArray<SubId>(32)
  let renderers = ResizeArray<IRenderer<'Model>>()
  let subBuffer = ResizeArray<struct (SubId * Subscribe<'Msg>)>()
  let subStack = ResizeArray<Sub<'Msg>>()
  let deferredEffs = ResizeArray<Effect<'Msg>>(64)
  let deferredEffsRun = ResizeArray<Effect<'Msg>>(64)

  let dispatch(msg: 'Msg) = pendingMsgs.Enqueue(msg)

  let execCmd(cmd: Cmd<'Msg>) =
    match cmd with
    | Empty -> ()
    | Single eff -> eff.Invoke(dispatch)
    | Batch effs ->
      for i = 0 to effs.Length - 1 do
        effs[i].Invoke(dispatch)
    | DeferNextFrame effs -> deferredEffs.AddRange(effs)
    | NowAndDeferNextFrame(now, next) ->
      for i = 0 to now.Length - 1 do
        now[i].Invoke(dispatch)

      deferredEffs.AddRange(next)

  let updateSubs(ctx: GameContext) =
    subBuffer.Clear()
    subStack.Clear()
    subStack.Add(program.Subscribe ctx state)
    Sub.flatten subStack subBuffer

    subIdsInUse.Clear()
    subIdsToRemove.Clear()

    for id, subscribeFn in subBuffer do
      subIdsInUse.Add(id) |> ignore

      if not(activeSubs.ContainsKey(id)) then
        try
          activeSubs.Add(id, subscribeFn dispatch)
        with ex ->
          Console.WriteLine($"Error starting sub {SubId.value id}: {ex}")

    // Remove any subscriptions that are no longer present.
    // Avoid allocating an array of keys; gather to a reusable buffer first.
    for KeyValue(key, _disp) in activeSubs do
      if not(subIdsInUse.Contains(key)) then
        subIdsToRemove.Add(key)

    for i = 0 to subIdsToRemove.Count - 1 do
      let key = subIdsToRemove[i]

      match activeSubs.TryGetValue(key) with
      | true, disp ->
        disp.Dispose()
        activeSubs.Remove(key) |> ignore
      | _ -> ()

  do
    // Apply user configuration or use sensible defaults
    match program.Config with
    | ValueSome configure -> configure(this, graphics)
    | ValueNone ->
      // Default settings (can be overridden via Program.withConfig)
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

    for f in program.Renderers do
      renderers.Add(f this)

    base.Initialize()

  override _.LoadContent() =
    base.LoadContent()
    // Create context and call init after content is ready
    let ctx = {
      GraphicsDevice = this.GraphicsDevice
      Content = this.Content
      Game = this
    }

    // Persist the context for the lifetime of this game instance.
    ctxOpt <- ValueSome ctx

    let struct (initialState, initialCmds) = program.Init ctx
    state <- initialState

    execCmd initialCmds
    updateSubs ctx

  override _.Update gameTime =
    // MonoGame calls LoadContent before the first Update, but be defensive.
    match ctxOpt with
    | ValueNone -> base.Update gameTime
    | ValueSome ctx ->
    // Run MonoGame components first (e.g. input polling) so any messages
    // dispatched via subscriptions can be processed in this same frame.
    base.Update gameTime

    // Execute commands deferred from the previous frame before we enqueue Tick.
    // This provides a deterministic "next-frame" boundary without changing the Elmish
    // `update : Msg -> Model -> Model * Cmd` contract.
    if deferredEffs.Count <> 0 then
      deferredEffsRun.Clear()
      deferredEffsRun.AddRange(deferredEffs)
      deferredEffs.Clear()

      for i = 0 to deferredEffsRun.Count - 1 do
        deferredEffsRun[i].Invoke(dispatch)

    program.Tick |> ValueOption.iter(fun map -> dispatch(map gameTime))

    let mutable stateChanged = false
    let mutable msg = Unchecked.defaultof<'Msg>

    while pendingMsgs.TryDequeue(&msg) do
      let struct (newState, cmds) = program.Update msg state
      state <- newState
      execCmd cmds
      stateChanged <- true

    if stateChanged then
      updateSubs ctx

  override _.Draw gameTime =
    match ctxOpt with
    | ValueNone -> base.Draw gameTime
    | ValueSome ctx ->
      for i = 0 to renderers.Count - 1 do
        renderers[i].Draw(ctx, state, gameTime)

      base.Draw gameTime
