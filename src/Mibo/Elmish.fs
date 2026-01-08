namespace Mibo.Elmish

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX

/// <summary>
/// Represents a side effect that can dispatch messages to the Elmish runtime.
/// </summary>
/// <remarks>
/// Effects are the building blocks of commands. They are executed asynchronously
/// by the runtime and can dispatch one or more messages back to the update loop.
/// </remarks>
/// <example>
/// <code>
/// let myEffect = Effect&lt;MyMsg&gt;(fun dispatch -&gt;
///     // Do some side effect work
///     dispatch (DataLoaded result)
/// )
/// </code>
/// </example>
type Effect<'Msg> = delegate of ('Msg -> unit) -> unit

/// <summary>
/// Represents a command that produces side effects in the Elmish runtime.
/// </summary>
/// <remarks>
/// Commands are returned from <c>init</c> and <c>update</c> functions to schedule
/// side effects that run outside the pure update cycle. They can dispatch
/// messages back into the runtime, either immediately or deferred.
/// </remarks>
[<Struct>]
type Cmd<'Msg> =
  /// No-op command (use <see cref="M:Mibo.Elmish.Cmd.none"/>)
  | Empty
  /// Single effect to execute
  | Single of single: Effect<'Msg>
  /// Multiple effects to execute in this frame
  | Batch of batch: Effect<'Msg>[]
  /// Effects deferred until the next frame begins
  | DeferNextFrame of batch: Effect<'Msg>[]
  /// Combination of immediate and deferred effects
  | NowAndDeferNextFrame of now: Effect<'Msg>[] * next: Effect<'Msg>[]

/// <summary>
/// Functions for creating and composing Elmish commands.
/// </summary>
/// <remarks>
/// Commands encapsulate side effects and allow message dispatch back to the update loop.
/// Use commands for async operations, timer callbacks, or any impure work.
/// </remarks>
module Cmd =
  /// <summary>An empty command that does nothing. Use when no side effects are needed.</summary>
  let none: Cmd<'Msg> = Empty

  /// <summary>Wraps a raw effect delegate into a command.</summary>
  let inline ofEffect(eff: Effect<'Msg>) = Single eff

  /// <summary>
  /// Creates a command that immediately dispatches the given message.
  /// </summary>
  /// <remarks>
  /// Useful for triggering follow-up messages from within the update cycle.
  /// </remarks>
  let inline ofMsg(msg: 'Msg) : Cmd<'Msg> =
    Single(Effect<'Msg>(fun dispatch -> dispatch msg))

  /// <summary>
  /// Defer command execution until the next frame.
  /// </summary>
  /// <remarks>
  /// In the runtime, deferred commands are executed at the start of the next frame,
  /// before <c>Tick</c> is enqueued. This is useful for avoiding infinite update loops
  /// or for scheduling work that should happen after the current frame completes.
  /// </remarks>
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

  /// <summary>
  /// Map a command producing messages of type 'A into a command producing messages of type 'Msg.
  /// </summary>
  /// <remarks>
  /// This is the command equivalent of <see cref="M:Mibo.Elmish.Sub.map"/> and is required for parent-child composition
  /// in nested Elmish architectures where child modules have their own message types.
  /// </remarks>
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

  /// <summary>
  /// Combines multiple commands into a single command.
  /// </summary>
  /// <remarks>
  /// Commands are merged efficiently, preserving the distinction between
  /// immediate and deferred effects. Use this when returning multiple commands
  /// from a single update branch.
  /// </remarks>
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

  /// Combines exactly 2 commands with minimal allocation overhead.
  let batch2(a: Cmd<'Msg>, b: Cmd<'Msg>) : Cmd<'Msg> = batch [ a; b ]

  /// Combines exactly 3 commands with minimal allocation overhead.
  let batch3(a: Cmd<'Msg>, b: Cmd<'Msg>, c: Cmd<'Msg>) : Cmd<'Msg> =
    batch2(batch2(a, b), c)

  /// Combines exactly 4 commands with minimal allocation overhead.
  let batch4
    (a: Cmd<'Msg>, b: Cmd<'Msg>, c: Cmd<'Msg>, d: Cmd<'Msg>)
    : Cmd<'Msg> =
    batch2(batch3(a, b, c), d)

  /// Creates a command from an F# async workflow.
  ///
  /// The async is started immediately and the result is mapped to a message.
  /// If the async throws, the error handler is invoked instead.
  ///
  /// ## Example
  /// ```fsharp
  /// Cmd.ofAsync (loadDataAsync url) DataLoaded LoadError
  /// ```
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

  /// <summary>
  /// Creates a command from a .NET Task.
  /// </summary>
  /// <remarks>
  /// The task result is awaited and mapped to a message.
  /// If the task throws, the error handler is invoked instead.
  /// </remarks>
  /// <example>
  /// <code>
  /// Cmd.ofTask (httpClient.GetAsync url) ResponseReceived RequestFailed
  /// </code>
  /// </example>
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


/// <summary>
/// Subscription identifier used as the key for subscription diffing.
/// </summary>
/// <remarks>
/// The Elmish runtime uses SubIds to determine which subscriptions to start,
/// stop, or keep running across frames. Use stable, unique IDs for each subscription.
/// Keep this allocation-free in hot paths (avoid list-based IDs).
/// </remarks>
[<Measure>]
type subId

/// A typed string wrapper for subscription identifiers.
type SubId = string<subId>

/// Functions for creating and manipulating subscription identifiers.
module SubId =
  /// <summary>Wraps a raw string into a <see cref="T:Mibo.Elmish.SubId"/>.</summary>
  let inline ofString(value: string) : SubId = UMX.tag<subId> value

  /// <summary>Extracts the raw string value from a <see cref="T:Mibo.Elmish.SubId"/>.</summary>
  let inline value(id: SubId) : string = UMX.untag id

  /// <summary>
  /// Prefixes a SubId with a namespace for parent-child subscription composition.
  /// </summary>
  /// <example>
  /// <code>
  /// // Creates "Player/moveInput"
  /// SubId.prefix "Player" (SubId.ofString "moveInput")
  /// </code>
  /// </example>
  let inline prefix (prefix: string) (id: SubId) : SubId =
    if String.IsNullOrEmpty(prefix) then
      id
    else
      let idStr = value id

      if String.IsNullOrEmpty(idStr) then
        ofString prefix
      else
        ofString(prefix + "/" + idStr)

/// <summary>A function that dispatches messages to the Elmish update loop.</summary>
type Dispatch<'Msg> = 'Msg -> unit

/// <summary>
/// A function that sets up a subscription and returns a disposable for cleanup.
/// </summary>
/// <remarks>
/// When the runtime calls this, it passes the dispatch function. The returned
/// <see cref="T:System.IDisposable"/> will be called when the subscription is no longer needed.
/// </remarks>
type Subscribe<'Msg> = Dispatch<'Msg> -> IDisposable

/// <summary>
/// Represents a subscription that listens for external events and dispatches messages.
/// </summary>
/// <remarks>
/// Subscriptions are the Elmish way to handle external event sources (input devices,
/// timers, network events). The runtime diffs subscriptions by SubId to determine
/// which to start/stop across frames.
/// </remarks>
[<Struct>]
type Sub<'Msg> =
  /// No subscription (use <see cref="M:Mibo.Elmish.Sub.none"/>)
  | NoSub
  /// An active subscription with a unique ID
  | Active of SubId * Subscribe<'Msg>
  /// Multiple subscriptions combined
  | BatchSub of Sub<'Msg>[]

/// <summary>
/// Functions for creating and composing Elmish subscriptions.
/// </summary>
/// <remarks>
/// Subscriptions connect external event sources to the Elmish update loop.
/// The runtime automatically manages subscription lifecycle based on SubId diffing.
/// </remarks>
module Sub =
  /// <summary>An empty subscription that does nothing. Use when no subscriptions are needed.</summary>
  let none = NoSub

  /// <summary>
  /// Combines multiple subscriptions into a single subscription.
  /// </summary>
  /// <remarks>
  /// Subscriptions are merged efficiently and duplicates are not filtered.
  /// Use unique SubIds to ensure proper subscription diffing.
  /// </remarks>
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

  /// Combines exactly 2 subscriptions with minimal allocation overhead.
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

  /// Combines exactly 3 subscriptions with minimal allocation overhead.
  let inline batch3(a: Sub<'Msg>, b: Sub<'Msg>, c: Sub<'Msg>) : Sub<'Msg> =
    batch2(batch2(a, b), c)

  /// Combines exactly 4 subscriptions with minimal allocation overhead.
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

  /// <summary>
  /// Maps a subscription producing messages of type 'A to produce messages of type 'Msg.
  /// </summary>
  /// <remarks>
  /// This is essential for parent-child composition where child modules have
  /// their own message types. The idPrefix is prepended to all subscription IDs
  /// to namespace them properly.
  /// </remarks>
  /// <example>
  /// <code>
  /// // In parent module:
  /// let childSub = Child.subscribe ctx |> Sub.map "child" ChildMsg
  /// </code>
  /// </example>
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

/// <summary>
/// Context passed to <c>init</c> and <c>subscribe</c> functions, providing access to MonoGame resources.
/// </summary>
/// <remarks>
/// This is the primary way to access game services and content from within
/// the Elmish architecture. Use it to load assets, access input services,
/// or interact with registered components.
/// </remarks>
/// <example>
/// <code>
/// let init ctx =
///     let texture = Assets.texture "player" ctx
///     struct ({ Texture = texture }, Cmd.none)
/// </code>
/// </example>
type GameContext = {
  /// The MonoGame graphics device for rendering operations.
  GraphicsDevice: GraphicsDevice
  /// The content manager for loading compiled game assets.
  Content: Content.ContentManager
  /// The Game instance for accessing services and components.
  Game: Game
}

/// <summary>
/// Interface for renderers that draw the model state each frame.
/// </summary>
/// <remarks>
/// Implement this to create custom rendering systems. The Draw method
/// is called once per frame with the current model state.
/// </remarks>
/// <example>
/// <code>
/// type MyRenderer() =
///     interface IRenderer&lt;Model&gt; with
///         member _.Draw(ctx, model, gameTime) =
///             // Render model to screen
///             ()
/// </code>
/// </example>
type IRenderer<'Model> =
  abstract member Draw: GameContext * 'Model * GameTime -> unit

/// <summary>
/// Mutable handle for a MonoGame component instance created during game initialization.
/// </summary>
/// <remarks>
/// This is intended to be allocated in the composition root (per game instance) and then
/// threaded into Elmish <c>update</c>/<c>subscribe</c> functions, avoiding global/module-level mutable state.
/// ComponentRef provides a type-safe way to access components from within Elmish code.
/// </remarks>
/// <example>
/// <code>
/// // In composition root:
/// let audioRef = ComponentRef&lt;AudioComponent&gt;()
///
/// // Wire up in program:
/// Program.withComponentRef audioRef AudioComponent program
///
/// // Use in update:
/// match audioRef.TryGet() with
/// | ValueSome audio -&gt; audio.Play("explosion")
/// | ValueNone -&gt; ()
/// </code>
/// </example>
type ComponentRef<'T when 'T :> IGameComponent>() =
  let mutable value: 'T voption = ValueNone

  /// Attempts to get the component, returning ValueNone if not yet initialized.
  member _.TryGet() : 'T voption = value

  /// Sets the component reference. Called automatically by Program.withComponentRef.
  member _.Set(v: 'T) = value <- ValueSome v

  /// Clears the component reference.
  member _.Clear() = value <- ValueNone

/// <summary>
/// A small, allocation-friendly buffer that stores render commands tagged with a sort key.
/// </summary>
/// <remarks>
/// This is the core data structure for deferred rendering. Commands are accumulated
/// during the view phase and then sorted/executed by the renderer. The buffer uses
/// <see cref="T:System.Buffers.ArrayPool`1"/> for zero-allocation resizing.
/// </remarks>
/// <typeparam name="Key">The sort key type (e.g., <c>int&lt;RenderLayer&gt;</c> for 2D, <c>unit</c> for 3D)</typeparam>
/// <typeparam name="Cmd">The render command type</typeparam>
/// <example>
/// <code>
/// let buffer = RenderBuffer&lt;int&lt;RenderLayer&gt;, RenderCmd2D&gt;()
/// buffer.Add(0&lt;RenderLayer&gt;, DrawTexture(...))
/// buffer.Sort()  // Sorts by layer
/// for i = 0 to buffer.Count - 1 do
///     let struct (_, cmd) = buffer.Item(i)
///     // Execute command
/// </code>
/// </example>
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

  /// Clears all commands from the buffer without deallocating.
  member _.Clear() = count <- 0

  /// Adds a command with its sort key to the buffer.
  member _.Add(key: 'Key, cmd: 'Cmd) =
    ensureCapacity 1
    items[count] <- struct (key, cmd)
    count <- count + 1

  /// Sorts the buffer by key. Call this before iterating if order matters.
  member _.Sort() =
    // Sort only the used portion via Span
    let span = items.AsSpan(0, count)
    span.Sort comparer

  /// The number of commands currently in the buffer.
  member _.Count = count

  /// Gets the command at the specified index as a (key, command) struct tuple.
  member _.Item(i) = items[i]

/// <summary>
/// The Elmish program record that defines the complete game architecture.
/// </summary>
/// <remarks>
/// A program ties together initialization, update logic, subscriptions, and rendering.
/// Use the <see cref="T:Mibo.Elmish.Program"/> module functions to construct and configure programs.
/// </remarks>
/// <example>
/// <code>
/// Program.mkProgram init update
/// |&gt; Program.withSubscription subscribe
/// |&gt; Program.withRenderer (Batch2DRenderer.create view)
/// |&gt; Program.withTick Tick
/// |&gt; Program.withAssets
/// |&gt; Program.withInput
/// </code>
/// </example>
type Program<'Model, 'Msg> = {
  /// <summary>Creates initial model and commands when the game starts.</summary>
  Init: GameContext -> struct ('Model * Cmd<'Msg>)
  /// <summary>Handles messages and returns updated model and commands.</summary>
  Update: 'Msg -> 'Model -> struct ('Model * Cmd<'Msg>)
  /// <summary>Returns subscriptions based on current model state.</summary>
  Subscribe: GameContext -> 'Model -> Sub<'Msg>
  /// <summary>
  /// Configuration callback invoked in the game constructor.
  /// Use this to set resolution, vsync, window settings, etc.
  /// </summary>
  Config: (Game * GraphicsDeviceManager -> unit) voption
  /// <summary>List of renderer factories for drawing.</summary>
  Renderers: (Game -> IRenderer<'Model>) list
  /// <summary>List of MonoGame component factories.</summary>
  Components: (Game -> IGameComponent) list
  /// <summary>Optional function to generate a message each frame.</summary>
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
