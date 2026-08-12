namespace Mibo.Adaptive

open System
open System.Collections.Generic

// =============================================================================
// Incremental reductions (PLAN.md Section 7.2)
//
// The FDA AdaptiveReduction protocol: a struct record of seed/add/sub/view.
// The node keeps the reduction state and applies journal deltas at read time
// (pull-lazy). `sub` returns ValueNone when the operation cannot be inverted
// for the given element: the node then recomputes the whole state from the
// current view (the fallback protocol). Map reductions keep a mirror of the
// source values so removals and updates can invert.
// =============================================================================

/// <summary>
/// An incremental reduction protocol over elements of type 'a: the state 's is
/// updated with <c>add</c> for added elements and <c>sub</c> for removed ones.
/// <c>sub</c> returns <c>ValueNone</c> when it cannot invert the removal; the
/// library then recomputes the state from the current collection. <c>view</c>
/// projects the state to the observed value.
/// </summary>
/// <remarks>
/// Parity: FDA <c>AdaptiveReduction</c> (AdaptiveValue/AdaptiveReduction.fs).
/// Order of element application is undefined.
/// </remarks>
[<Struct>]
type AdaptiveReduction<'a, 's, 'v> = {
  /// <summary>The initial state.</summary>
  seed: 's
  /// <summary>Applies one added element to the state.</summary>
  add: 's -> 'a -> 's
  /// <summary>
  /// Inverts one removed element. Returns <c>ValueNone</c> when the removal
  /// cannot be inverted; the library recomputes from the current collection.
  /// </summary>
  sub: 's -> 'a -> 's voption
  /// <summary>Projects the state to the observed value.</summary>
  view: 's -> 'v
}

/// <summary>Combinators for building <see cref="AdaptiveReduction"/> values.</summary>
module AdaptiveReduction =

  /// <summary>Maps the observed value of a reduction.</summary>
  let inline mapOut
    ([<InlineIfLambda>] mapping: 'v -> 'w)
    (reduction: AdaptiveReduction<'a, 's, 'v>)
    : AdaptiveReduction<'a, 's, 'w> =
    {
      seed = reduction.seed
      add = reduction.add
      sub = reduction.sub
      view = fun s -> mapping(reduction.view s)
    }

  /// <summary>
  /// Composes two reductions over the same element in parallel (FDA
  /// <c>AdaptiveReduction.par</c> parity; tuple state). The subtract falls
  /// back to a full recompute when either side cannot invert.
  /// </summary>
  let inline par
    (left: AdaptiveReduction<'a, 's, 'v>)
    (right: AdaptiveReduction<'a, 't, 'w>)
    : AdaptiveReduction<'a, 's * 't, 'v * 'w> =
    {
      seed = (left.seed, right.seed)
      add = fun (s, t) a -> (left.add s a, right.add t a)
      sub =
        fun (s, t) a ->
          match left.sub s a with
          | ValueSome s' ->
            match right.sub t a with
            | ValueSome t' -> ValueSome(s', t')
            | ValueNone -> ValueNone
          | ValueNone -> ValueNone
      view = fun (s, t) -> (left.view s, right.view t)
    }

  /// <summary>
  /// Composes two reductions over the same element in parallel (FDA
  /// <c>AdaptiveReduction.structpar</c> parity; struct state, no tuple
  /// allocation).
  /// </summary>
  let inline structpar
    (left: AdaptiveReduction<'a, 's, 'v>)
    (right: AdaptiveReduction<'a, 't, 'w>)
    : AdaptiveReduction<'a, struct ('s * 't), struct ('v * 'w)> =
    {
      seed = struct (left.seed, right.seed)
      add = fun struct (s, t) a -> struct (left.add s a, right.add t a)
      sub =
        fun struct (s, t) a ->
          match left.sub s a with
          | ValueSome s' ->
            match right.sub t a with
            | ValueSome t' -> ValueSome(struct (s', t'))
            | ValueNone -> ValueNone
          | ValueNone -> ValueNone
      view = fun struct (s, t) -> struct (left.view s, right.view t)
    }

  /// <summary>
  /// Maps the element side of a reduction (FDA <c>AdaptiveReduction.mapIn</c>
  /// parity).
  /// </summary>
  let inline mapIn
    ([<InlineIfLambda>] mapping: 'a -> 'b)
    (reduction: AdaptiveReduction<'b, 's, 'v>)
    : AdaptiveReduction<'a, 's, 'v> =
    {
      seed = reduction.seed
      add = fun s a -> reduction.add s (mapping a)
      sub = fun s a -> reduction.sub s (mapping a)
      view = reduction.view
    }

  /// <summary>Counts the elements (FDA <c>AdaptiveReduction.count</c> parity).</summary>
  let count<'a> : AdaptiveReduction<'a, int, int> = {
    seed = 0
    add = fun s _ -> s + 1
    sub = fun s _ -> ValueSome(s - 1)
    view = id
  }


  /// <summary>A reduction with an invertible subtract operation.</summary>
  let inline group
    (zero: 's)
    ([<InlineIfLambda>] add: 's -> 'a -> 's)
    (subtract: 's -> 'a -> 's)
    : AdaptiveReduction<'a, 's, 's> =
    {
      seed = zero
      add = add
      sub = fun s a -> ValueSome(subtract s a)
      view = id
    }

  /// <summary>A reduction whose subtract may fall back to a full recompute.</summary>
  let inline halfGroup
    (zero: 's)
    ([<InlineIfLambda>] add: 's -> 'a -> 's)
    (trySubtract: 's -> 'a -> 's voption)
    : AdaptiveReduction<'a, 's, 's> =
    {
      seed = zero
      add = add
      sub = trySubtract
      view = id
    }

  /// <summary>A reduction that recomputes the whole state on every removal.</summary>
  let inline fold
    (zero: 's)
    ([<InlineIfLambda>] add: 's -> 'a -> 's)
    : AdaptiveReduction<'a, 's, 's> =
    {
      seed = zero
      add = add
      sub = fun _ _ -> ValueNone
      view = id
    }

  /// <summary>Counts the elements for which the mapped value is true.</summary>
  let countPositive: AdaptiveReduction<bool, int, int> = {
    seed = 0
    add = fun s b -> if b then s + 1 else s
    sub = fun s b -> if b then ValueSome(s - 1) else ValueSome s
    view = id
  }

  /// <summary>Counts the elements for which the mapped value is false.</summary>
  let countNegative: AdaptiveReduction<bool, int, int> = {
    seed = 0
    add = fun s b -> if b then s else s + 1
    sub = fun s b -> if b then ValueSome s else ValueSome(s - 1)
    view = id
  }

  /// <summary>Sums the mapped values. Needs an additive numeric type.</summary>
  let inline sum() : AdaptiveReduction<'a, 'a, 'a> = {
    seed = LanguagePrimitives.GenericZero<'a>
    add = fun s v -> s + v
    sub = fun s v -> ValueSome(s - v)
    view = id
  }

  /// <summary>The minimum of the mapped values, or ValueNone when empty. A removal recomputes.</summary>
  let inline tryMin() : AdaptiveReduction<'a, 'a voption, 'a voption> = {
    seed = ValueNone
    add =
      fun s v ->
        match s with
        | ValueSome m -> ValueSome(min m v)
        | ValueNone -> ValueSome v
    sub = fun _ _ -> ValueNone
    view = id
  }

  /// <summary>The maximum of the mapped values, or ValueNone when empty. A removal recomputes.</summary>
  let inline tryMax() : AdaptiveReduction<'a, 'a voption, 'a voption> = {
    seed = ValueNone
    add =
      fun s v ->
        match s with
        | ValueSome m -> ValueSome(max m v)
        | ValueNone -> ValueSome v
    sub = fun _ _ -> ValueNone
    view = id
  }

/// <summary>
/// A delta-driven reduction over a set. Registers as a delta sink on
/// the source; the journal is applied to the reduction state on read (drain),
/// with a full recompute fallback when <c>sub</c> cannot invert a removal.
/// Implements the scalar protocol: version and dependency snapshot.
/// </summary>
type SetReduceNode<'a, 'b, 's, 'v when 'a: equality>
  (
    source: IAdaptiveSet<'a>,
    [<InlineIfLambda>] mapping: 'a -> 'b,
    reduction: AdaptiveReduction<'b, 's, 'v>
  ) =
  let mutable version = 0L
  let mutable depVersions = [| 0L |]
  let mutable journal = SetDelta<'a>.Create()
  let mutable initialized = false
  let mutable disposed = false
  let mutable red = reduction.seed
  let mutable value = reduction.view reduction.seed

  member private this.Register() =
    match box source with
    | :? ISetSinkRegistry as r -> r.AddSetSink(box(this :> ISetDeltaSink<'a>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? ISetSinkRegistry as r ->
      r.RemoveSetSink(box(this :> ISetDeltaSink<'a>))
    | _ -> ()

  /// Full recompute from the current view. The rebuilt state reflects every
  /// pending delta, so the whole journal is consumed.
  member private this.Rebuild() =
    let mutable acc = reduction.seed
    // The view is a HashSet in this implementation; interface
    // iteration would box the enumerator (measured 40 B per element).
    let data = Collections.asHashSet(source.GetValue())

    for x in data do
      acc <- reduction.add acc (mapping x)

    red <- acc
    value <- reduction.view red
    journal.Clear()

  /// Apply the journal to the reduction state. Entries appended during
  /// processing (reentrant writes) survive; a rebuild consumes everything.
  member private this.Drain() =
    let remStart = journal.Rems.Count
    let addStart = journal.Adds.Count
    let mutable i = 0
    let mutable rebuilt = false
    // Suspend the append-time cross-kind cancellation while the journal
    // is replayed (see journalAppendSet): the mapping is user code.
    journal.InDrain[0] <- 1
    // Consumed counts: applied entries must never be applied again; the
    // entry that threw survives for the next drain.
    let mutable remsDone = 0
    let mutable addsDone = 0

    try
      while i < remStart do
        if not rebuilt then
          let x = journal.Rems.Items[i]

          match reduction.sub red (mapping x) with
          | ValueSome s -> red <- s
          | ValueNone ->
            this.Rebuild()
            rebuilt <- true

        i <- i + 1
        remsDone <- i

      i <- 0

      while i < addStart do
        if not rebuilt then
          let x = journal.Adds.Items[i]
          red <- reduction.add red (mapping x)

        i <- i + 1
        addsDone <- i
    finally
      journal.InDrain[0] <- 0
      // Compact in the finally: a throwing mapping must not make the next
      // drain re-apply consumed entries (double subtract corrupts the
      // reduction).
      let remLive = journal.Rems.Count

      if remLive > remsDone then
        Array.Copy(
          journal.Rems.Items,
          remsDone,
          journal.Rems.Items,
          0,
          remLive - remsDone
        )

        journal.Rems.Count <- remLive - remsDone
      else
        journal.Rems.Count <- 0

      let addLive = journal.Adds.Count

      if addLive > addsDone then
        Array.Copy(
          journal.Adds.Items,
          addsDone,
          journal.Adds.Items,
          0,
          addLive - addsDone
        )

        journal.Adds.Count <- addLive - addsDone
      else
        journal.Adds.Count <- 0

    if not rebuilt then
      value <- reduction.view red

  interface ISetDeltaSink<'a> with
    member this.OnDeltas(adds: 'a[], addCnt: int, rems: 'a[], remCnt: int) =
      if not disposed then
        Collections.journalAppendSet &journal adds addCnt rems remCnt
        version <- version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveValue<'v> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive value has been disposed."

        if not initialized then
          // Snapshot first, register between (see MapSetNode.EnsureInitialized
          // in SetNodes.fs): the mapping is user code that may write to
          // the source, and the write must land in our journal. A dirty
          // source draining during the snapshot read pushes to nobody.
          // The flag is set last: an exception leaves the node
          // uninitialized.
          let snapshot = HashSet<'a>(source.GetValue())
          this.Register()
          let mutable acc = reduction.seed

          for x in snapshot do
            acc <- reduction.add acc (mapping x)

          red <- acc
          value <- reduction.view red
          depVersions[0] <- source.Version
          initialized <- true

        if source.Version <> depVersions[0] then
          source.GetValue() |> ignore
          depVersions[0] <- source.Version

        if not journal.IsEmpty then
          this.Drain()

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        value
      finally
        ctx.ReleaseOwner()

    member _.Version =
      // Dirty indicator (see ListReduceNode.Version).
      if source.Version <> depVersions[0] then
        version + 1L
      else
        version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.Unregister()

/// <summary>
/// A reduction over an adaptive list (FDA <c>AList.reduce</c> parity). The
/// reduction state is maintained per delta: an insert adds the mapped value,
/// a remove subtracts it (falling back to a full recompute when the reduction
/// cannot invert, e.g. <c>AdaptiveReduction.fold</c>), an update subtracts the
/// old and adds the new. The mirror is aligned with the input positions, so
/// structural ops shift it with the source. Order-sensitive reductions are the
/// user's contract (the reduction's add/sub must be delta-consistent), the
/// same contract as the set/map reduction nodes.
/// </summary>
type ListReduceNode<'a, 'b, 's, 'v>
  (
    source: IAdaptiveList<'a>,
    [<InlineIfLambda>] mapping: 'a -> 'b,
    reduction: AdaptiveReduction<'b, 's, 'v>
  ) =
  let mutable version = 0L
  let mutable depVersion = 0L
  let mutable journal = ListDelta<'a>.Create()
  // Mirror of the source values plus their mapped values, aligned with the
  // input positions (a remove inverts with the stored mapped old value; the
  // mapping runs once per journal element).
  let mirror = ResizeArray<struct ('a * 'b)>()
  let mutable initialized = false
  let mutable disposed = false
  let mutable red = reduction.seed
  let mutable value = reduction.view reduction.seed

  member private this.Register() =
    match box source with
    | :? IListSinkRegistry as r ->
      r.AddListSink(box(this :> IListDeltaSink<'a>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? IListSinkRegistry as r ->
      r.RemoveListSink(box(this :> IListDeltaSink<'a>))
    | _ -> ()

  member private this.EnsureInitialized() =
    if not initialized then
      this.Register()
      this.Rebuild()
      depVersion <- source.Version
      initialized <- true

  /// Full recompute from the current view, rebuilding the mirror. Consumes
  /// the whole journal: the rebuilt state reflects every pending delta.
  member private this.Rebuild() =
    mirror.Clear()
    let mutable acc = reduction.seed
    let view = source.GetValue()

    for i in 0 .. view.Count - 1 do
      let v = view[i]
      let m = mapping v
      mirror.Add(struct (v, m))
      acc <- reduction.add acc m

    red <- acc
    value <- reduction.view red
    journal.Clear()

  /// Apply the journal to the reduction state. The ops are applied in order
  /// (each position refers to the state as of the previous op); an update
  /// with an equal source value is skipped (no-op updates do not rebuild).
  member private this.Drain() =
    let ops = journal.Ops
    let cnt = journal.Ops.Count
    let mutable i = 0
    let mutable rebuilt = false

    try
      while i < cnt && not rebuilt do
        let op = ops.Items[i]
        let p = op.Position

        match op.Kind with
        | ListOpKind.Insert ->
          if p >= mirror.Count then
            // Append: the incremental add is valid for every reduction.
            let m = mapping op.Value
            mirror.Insert(p, struct (op.Value, m))
            red <- reduction.add red m
          else
            // Mid-list insert: the incremental add assumes an
            // append (or a commutative reduction); the protocol
            // does not expose commutativity, so rebuild.
            this.Rebuild()
            rebuilt <- true
        | ListOpKind.Remove ->
          if p >= 0 && p < mirror.Count then
            let struct (_, mappedOld) = mirror[p]

            match reduction.sub red mappedOld with
            | ValueSome s -> red <- s
            | ValueNone ->
              this.Rebuild()
              rebuilt <- true

            if not rebuilt then
              mirror.RemoveAt p
        | _ -> // Update
          if p >= 0 && p < mirror.Count then
            let struct (oldV, mappedOld) = mirror[p]

            if EqualityComparer<'a>.Default.Equals(oldV, op.Value) then
              // No-op update: nothing to invert, nothing to add.
              ()
            else
              let m = mapping op.Value

              match reduction.sub red mappedOld with
              | ValueSome s -> red <- s
              | ValueNone ->
                this.Rebuild()
                rebuilt <- true

              if not rebuilt then
                mirror[p] <- struct (op.Value, m)
                red <- reduction.add red m

        i <- i + 1
    finally
      // Consumed ops: the journal is cleared even when the drain threw;
      // a rebuilt state already reflects every pending delta, and an
      // applied op must never be applied again.
      if rebuilt then
        journal.Clear()
      else
        let consumed = i

        if consumed > 0 then
          Array.Copy(ops.Items, consumed, ops.Items, 0, cnt - consumed)
          journal.Ops.Count <- cnt - consumed

    if not rebuilt then
      value <- reduction.view red
      version <- version + 1L

  interface IListDeltaSink<'a> with
    member this.OnDeltas(ops: ListOp<'a>[], opCnt: int) =
      if not disposed then
        Collections.journalAppendList &journal ops opCnt
        version <- version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveValue<'v> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive value has been disposed."

        this.EnsureInitialized()

        if source.Version <> depVersion then
          source.GetValue() |> ignore
          depVersion <- source.Version

        if not journal.IsEmpty then
          this.Drain()

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        value
      finally
        ctx.ReleaseOwner()

    member _.Version =
      // Dirty indicator (see MapMapNode.Version in MapNodes.fs): while
      // the source has unprocessed changes, report version + 1 so
      // version-checking consumers re-read; the re-read drains the
      // journal and settles the chain recursively.
      if source.Version <> depVersion then
        version + 1L
      else
        version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.Unregister()

/// <summary>
/// A delta-driven reduction over a map. Keeps a mirror of the source
/// values so removals and updates can invert (<c>sub</c> receives the old
/// mapped value). The mapping is applied per journal element at drain time.
/// </summary>
type MapReduceNode<'k, 'a, 'b, 's, 'v when 'k: equality>
  (
    source: IAdaptiveMap<'k, 'a>,
    [<InlineIfLambda>] mapping: 'k -> 'a -> 'b,
    reduction: AdaptiveReduction<'b, 's, 'v>
  ) =
  let mutable version = 0L
  let mutable depVersions = [| 0L |]
  let mutable journal = MapDelta<'k, 'a>.Create()
  // Mirror of source values plus their mapped values: a Set on an existing
  // key inverts with the stored mapped old value (the mapping runs once per
  // journal element, not once per sub and once per add).
  let mirror = Dictionary<'k, struct ('a * 'b)>()
  let mutable initialized = false
  let mutable disposed = false
  let mutable red = reduction.seed
  let mutable value = reduction.view reduction.seed

  member private this.Register() =
    match box source with
    | :? IMapSinkRegistry as r ->
      r.AddMapSink(box(this :> IMapDeltaSink<'k, 'a>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? IMapSinkRegistry as r ->
      r.RemoveMapSink(box(this :> IMapDeltaSink<'k, 'a>))
    | _ -> ()

  /// Full recompute from the current view, rebuilding the mirror. Consumes
  /// the whole journal: the rebuilt state reflects every pending delta.
  member private this.Rebuild() =
    mirror.Clear()
    let mutable acc = reduction.seed
    // The view is a Dictionary in this implementation; interface
    // iteration would box the enumerator (measured 48 B per entry).
    let data = Collections.asDictionary(source.GetValue())

    for KeyValue(k, v) in data do
      let m = mapping k v
      mirror[k] <- struct (v, m)
      acc <- reduction.add acc m

    red <- acc
    value <- reduction.view red
    journal.Clear()

  /// Apply the journal to the reduction state. A Set on an existing key
  /// subtracts the stored mapped old value, then adds the new one; an equal
  /// source value is skipped (no-op updates do not rebuild).
  member private this.Drain() =
    let remStart = journal.Rems.Count
    let setStart = journal.Sets.Count
    let mutable i = 0
    let mutable rebuilt = false
    // Suspend the append-time cross-kind cancellation while the journal
    // is replayed (see journalAppendMap): the mapping is user code.
    journal.InDrain[0] <- 1
    // Consumed counts: see the set reduction drain.
    let mutable remsDone = 0
    let mutable setsDone = 0

    try
      while i < remStart do
        if not rebuilt then
          let k = journal.Rems.Items[i]
          let mutable old = Unchecked.defaultof<struct ('a * 'b)>

          if mirror.TryGetValue(k, &old) then
            let struct (_, mappedOld) = old

            match reduction.sub red mappedOld with
            | ValueSome s -> red <- s
            | ValueNone ->
              this.Rebuild()
              rebuilt <- true

            if not rebuilt then
              mirror.Remove k |> ignore

        i <- i + 1
        remsDone <- i

      i <- 0

      while i < setStart do
        if not rebuilt then
          let struct (k, v) = journal.Sets.Items[i]
          let mutable old = Unchecked.defaultof<struct ('a * 'b)>

          if mirror.TryGetValue(k, &old) then
            let struct (oldV, mappedOld) = old

            if EqualityComparer<'a>.Default.Equals(oldV, v) then
              // No-op update: nothing to invert, nothing to add.
              ()
            else
              match reduction.sub red mappedOld with
              | ValueSome s -> red <- s
              | ValueNone ->
                this.Rebuild()
                rebuilt <- true

              if not rebuilt then
                let m = mapping k v
                red <- reduction.add red m
                mirror[k] <- struct (v, m)
          else
            let m = mapping k v
            red <- reduction.add red m
            mirror[k] <- struct (v, m)

        i <- i + 1
        setsDone <- i
    finally
      journal.InDrain[0] <- 0
      // Compact in the finally: a throwing mapping must not make the next
      // drain re-apply consumed entries (double subtract corrupts the
      // reduction).
      let remLive = journal.Rems.Count

      if remLive > remsDone then
        Array.Copy(
          journal.Rems.Items,
          remsDone,
          journal.Rems.Items,
          0,
          remLive - remsDone
        )

        journal.Rems.Count <- remLive - remsDone
      else
        journal.Rems.Count <- 0

      let setLive = journal.Sets.Count

      if setLive > setsDone then
        Array.Copy(
          journal.Sets.Items,
          setsDone,
          journal.Sets.Items,
          0,
          setLive - setsDone
        )

        journal.Sets.Count <- setLive - setsDone
      else
        journal.Sets.Count <- 0

    if not rebuilt then
      value <- reduction.view red

  interface IMapDeltaSink<'k, 'a> with
    member this.OnDeltas
      (sets: struct ('k * 'a)[], setCnt: int, rems: 'k[], remCnt: int)
      =
      if not disposed then
        Collections.journalAppendMap &journal sets setCnt rems remCnt
        version <- version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveValue<'v> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive value has been disposed."

        if not initialized then
          // Snapshot first, register between (see the set reduction node).
          // The flag is set last: an exception leaves the node
          // uninitialized.
          let snapshot = Dictionary<'k, 'a>()

          for KeyValue(k, v) in source.GetValue() do
            snapshot[k] <- v

          this.Register()
          let mutable acc = reduction.seed

          for KeyValue(k, v) in snapshot do
            let m = mapping k v
            mirror[k] <- struct (v, m)
            acc <- reduction.add acc m

          red <- acc
          value <- reduction.view red
          depVersions[0] <- source.Version
          initialized <- true

        if source.Version <> depVersions[0] then
          source.GetValue() |> ignore
          depVersions[0] <- source.Version

        if not journal.IsEmpty then
          this.Drain()

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        value
      finally
        ctx.ReleaseOwner()

    member _.Version =
      // Dirty indicator (see ListReduceNode.Version).
      if source.Version <> depVersions[0] then
        version + 1L
      else
        version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.Unregister()
