namespace Mibo.Elmish

open System
open System.Collections.Generic
open FSharp.UMX

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
