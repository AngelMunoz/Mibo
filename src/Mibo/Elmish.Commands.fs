namespace Mibo.Elmish

open System

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
  let batch2(a: Cmd<'Msg>, b: Cmd<'Msg>) : Cmd<'Msg> =
    match a, b with
    | Empty, x -> x
    | x, Empty -> x
    | _ ->
      let struct (aNow, aNext) = split a
      let struct (bNow, bNext) = split b

      let nowCount = aNow.Length + bNow.Length
      let nextCount = aNext.Length + bNext.Length

      if nowCount = 0 && nextCount = 0 then
        Empty
      elif nextCount = 0 then
        if nowCount = 1 then
          // Avoid array allocation when possible.
          let eff = if aNow.Length = 1 then aNow[0] else bNow[0]
          Single eff
        else
          let arr = Array.zeroCreate<Effect<'Msg>> nowCount

          if aNow.Length <> 0 then
            Array.Copy(aNow, 0, arr, 0, aNow.Length)

          if bNow.Length <> 0 then
            Array.Copy(bNow, 0, arr, aNow.Length, bNow.Length)

          Batch arr
      elif nowCount = 0 then
        let arr = Array.zeroCreate<Effect<'Msg>> nextCount

        if aNext.Length <> 0 then
          Array.Copy(aNext, 0, arr, 0, aNext.Length)

        if bNext.Length <> 0 then
          Array.Copy(bNext, 0, arr, aNext.Length, bNext.Length)

        DeferNextFrame arr
      else
        let nowArr = Array.zeroCreate<Effect<'Msg>> nowCount
        let nextArr = Array.zeroCreate<Effect<'Msg>> nextCount

        if aNow.Length <> 0 then
          Array.Copy(aNow, 0, nowArr, 0, aNow.Length)

        if bNow.Length <> 0 then
          Array.Copy(bNow, 0, nowArr, aNow.Length, bNow.Length)

        if aNext.Length <> 0 then
          Array.Copy(aNext, 0, nextArr, 0, aNext.Length)

        if bNext.Length <> 0 then
          Array.Copy(bNext, 0, nextArr, aNext.Length, bNext.Length)

        NowAndDeferNextFrame(nowArr, nextArr)

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
