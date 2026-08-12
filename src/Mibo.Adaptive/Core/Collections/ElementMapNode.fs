namespace Mibo.Adaptive

open System
open System.Collections.Generic

// =============================================================================
// Per-element adaptive map node (docs/2026-08-05-MAPA-DESIGN.md)
//
// AMap.mapA / chooseA / filterA share one node, the keyed sibling of
// ElementSetNode: the mapping returns an aval per entry, cached by key. See
// ElementSetNode.fs for the dirty gate and scan contracts; this node differs
// only in the journal shape (map deltas) and the output state (a plain keyed
// dictionary).
// =============================================================================

/// <summary>
/// Maps every entry of a map to an adaptive value (or chooses/filters, when
/// the aval's value is <c>ValueNone</c> to drop the entry).
/// </summary>
type ElementMapNode<'K, 'V, 'U when 'K: equality>
  (
    source: IAdaptiveMap<'K, 'V>,
    [<InlineIfLambda>] mapping: 'K -> 'V -> aval<'U voption>
  ) =
  let mutable state = MapNodeState<'K, 'V, 'U>.Create(1)
  let mutable cache = Dictionary<'K, ElementEntry<'U>>()
  let mutable initialized = false
  let mutable disposed = false
  let mutable elementDirty = false
  let mutable lastDrainWriteGen = -1L

  member private this.Register() =
    match box source with
    | :? IMapSinkRegistry as r ->
      r.AddMapSink(box(this :> IMapDeltaSink<'K, 'V>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? IMapSinkRegistry as r ->
      r.RemoveMapSink(box(this :> IMapDeltaSink<'K, 'V>))
    | _ -> ()

  /// The dirty gate: a scan is needed when the self-healing flag is set, or
  /// when a write moved the generation since the last scan.
  member private _.NeedsElementScan() =
    elementDirty || GraphContext.Default.WriteGeneration <> lastDrainWriteGen

  member private this.EnsureInitialized() =
    if not initialized then
      // Snapshot first, register between, then load (see ElementSetNode).
      let snapshot = Dictionary<'K, 'V>(source.GetValue())
      this.Register()

      for KeyValue(k, v) in snapshot do
        let aval = mapping k v
        // Version read BEFORE the force: a mid-force write leaves the
        // stored version stale, so the next scan re-forces.
        let preV = aval.Version
        let newV = aval.GetValue()
        let entry = ElementEntry(aval, preV, newV)

        match newV with
        | ValueSome u -> state.Data[k] <- u
        | ValueNone -> ()

        cache[k] <- entry

      state.DepVersions[0] <- source.Version
      initialized <- true

  /// Apply the source journal with cache maintenance: removed keys drop
  /// their cache entry (unregistered) and their output; set keys run the
  /// mapping, force the aval, and replace the output.
  member private this.DrainJournal() =
    let rems = state.Journal.Rems
    let sets = state.Journal.Sets
    let remStart = rems.Count
    let setStart = sets.Count
    let mutable i = 0
    // Suspend the append-time cross-kind cancellation while the journal
    // is replayed (see journalAppendMap).
    state.Journal.InDrain[0] <- 1
    // Consumed counts: entries before these positions were applied and
    // must never be applied again; the throwing entry (and reentrant
    // entries appended live, beyond the start bounds) survive.
    let mutable remsDone = 0
    let mutable setsDone = 0

    try
      while i < remStart do
        let k = rems.Items[i]
        cache.Remove k |> ignore

        if state.Data.Remove k then
          state.Out.Rems <- Collections.bufferAppend state.Out.Rems k

        i <- i + 1
        remsDone <- i

      i <- 0

      while i < setStart do
        let struct (k, v) = sets.Items[i]
        let aval = mapping k v
        // Version read BEFORE the force (see EnsureInitialized).
        let preV = aval.Version
        let newV = aval.GetValue()
        let newEntry = ElementEntry(aval, preV, newV)

        match newV with
        | ValueSome u ->
          let mutable old = Unchecked.defaultof<'U>

          if
            state.Data.TryGetValue(k, &old)
            && EqualityComparer<'U>.Default.Equals(old, u)
          then
            ()
          else
            state.Data[k] <- u

            state.Out.Sets <-
              Collections.bufferAppend state.Out.Sets (struct (k, u))
        | ValueNone ->
          if state.Data.Remove k then
            state.Out.Rems <- Collections.bufferAppend state.Out.Rems k

        cache[k] <- newEntry
        i <- i + 1
        setsDone <- i
    finally
      state.Journal.InDrain[0] <- 0
      // Compact against the LIVE journal counts (see ElementSetNode).
      let remLive = state.Journal.Rems.Count

      if remLive > remsDone then
        Array.Copy(
          state.Journal.Rems.Items,
          remsDone,
          state.Journal.Rems.Items,
          0,
          remLive - remsDone
        )

        state.Journal.Rems.Count <- remLive - remsDone
      else
        state.Journal.Rems.Count <- 0

      let setLive = state.Journal.Sets.Count

      if setLive > setsDone then
        Array.Copy(
          state.Journal.Sets.Items,
          setsDone,
          state.Journal.Sets.Items,
          0,
          setLive - setsDone
        )

        state.Journal.Sets.Count <- setLive - setsDone
      else
        state.Journal.Sets.Count <- 0

  /// The element scan: re-read every cached aval's version; force the
  /// changed ones and apply the contribution change. Self-healing: a
  /// reentrant write mid-scan keeps the dirty flag set so the next read
  /// rescans.
  member private this.ScanElements() =
    let startGen = GraphContext.Default.WriteGeneration
    let mutable changed = false
    let mutable e = cache.GetEnumerator()

    while e.MoveNext() do
      let kvp = e.Current
      let mutable entry = kvp.Value
      // Version read BEFORE the force (see EnsureInitialized).
      let preV = entry.Aval.Version

      if preV <> entry.Version then
        let newV = entry.Aval.GetValue()

        match newV with
        | ValueSome u ->
          let mutable old = Unchecked.defaultof<'U>

          if
            state.Data.TryGetValue(kvp.Key, &old)
            && EqualityComparer<'U>.Default.Equals(old, u)
          then
            () // version bumped to an equal value: no delta
          else
            state.Data[kvp.Key] <- u

            state.Out.Sets <-
              Collections.bufferAppend state.Out.Sets (struct (kvp.Key, u))

            changed <- true
        | ValueNone ->
          if state.Data.Remove kvp.Key then
            state.Out.Rems <- Collections.bufferAppend state.Out.Rems kvp.Key
            changed <- true

        entry.Version <- preV
        entry.Last <- newV
        cache[kvp.Key] <- entry

    if changed then
      state.Version <- state.Version + 1L

    if GraphContext.Default.WriteGeneration <> startGen then
      // A reentrant write landed mid-scan: the output may be stale, keep
      // the flag so the next read rescans.
      elementDirty <- true
    else
      elementDirty <- false

  /// Drain the journal, then the element scan, then push the accumulated
  /// output delta once, with notification delivery deferred.
  member private this.Process() =
    let ctx = GraphContext.Default
    let wasActive = ctx.TxActive
    ctx.TxActive <- true

    try
      if not state.Journal.IsEmpty then
        this.DrainJournal()

      if this.NeedsElementScan() then
        this.ScanElements()

      if not state.Out.IsEmpty then
        state.Version <- state.Version + 1L
        Collections.pushMapDelta &state.Sinks state.Out
        state.Out.Clear()
    finally
      ctx.TxActive <- wasActive

    // Capture AFTER the push: the downstream sink's write notification
    // advances the write generation during the delta delivery. Capturing
    // in the scan (before the push) left the gate permanently open and
    // the Version dirty indicator inflated, so a version-gated consumer
    // recorded a phantom version and missed the next change.
    lastDrainWriteGen <- GraphContext.Default.WriteGeneration

  interface IMapDeltaSink<'K, 'V> with
    member this.OnDeltas
      (
        setEntries: struct ('K * 'V)[],
        setCnt: int,
        removedKeys: 'K[],
        remCnt: int
      ) =
      if not disposed then
        Collections.journalAppendMap
          &state.Journal
          setEntries
          setCnt
          removedKeys
          remCnt

        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveMap<'K, 'U> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive map has been disposed."

        this.EnsureInitialized()

        if source.Version <> state.DepVersions[0] then
          source.GetValue() |> ignore
          state.DepVersions[0] <- source.Version

        this.Process()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Data :> IReadOnlyDictionary<'K, 'U>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      // Dirty indicator for version-checking readers (see ElementSetNode).
      if this.NeedsElementScan() then
        state.Version + 1L
      else
        state.Version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.Unregister()
        Collections.clearSinks &state.Sinks

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveMapSink(sink) =
      Collections.removeSink &state.Sinks sink

// =============================================================================
// The per-key join node (docs/2026-08-10-JOIN-DESIGN.md)
//
// AMap.joinOn — an equi-join over two maps with a computed join key. The left
// map is enumerated per key; the join key is computed from the left entry
// (keyOfLeft); the right map is looked up per entry (MapLookupNode: registers
// nothing, read-time gate), never enumerated or rebuilt.
//
// The mapping receives the left value as a SWAPPABLE input: a ChangeableValue
// the node re-applies on every update of the entry. The mapping's subgraph is
// built once per key and survives updates — an in-place input swap instead of
// the rebuild-on-journal of a plain mapA closure (the measured join
// projection: ~5% of busy time as AdaptiveNode ZeroCreate, from the
// per-update rebuild of every per-key subgraph).
// Join-key changes (rare) rebuild the subgraph against the new lookup; the
// cell survives the rebuild.
//
// The right map is never a dependency of the node itself: right-side changes
// reach the entries through the lookups (version-gated, re-read at force
// time) and through the element scan (gated on the write generation, which
// every delivery bumps).
// =============================================================================

/// <summary>
/// Cache entry of <see cref="JoinMapNode&lt;'K1,'V1,'K2,'V2,'U&gt;"/>. The
/// swappable left input (<c>Cell</c>), the current join key, and the
/// version/last protocol of the element nodes.
/// </summary>
[<Struct>]
type internal JoinEntry<'K2, 'V1, 'U> =
  val mutable Aval: aval<'U voption>
  val mutable Version: int64
  val mutable Last: 'U voption
  val mutable Cell: ChangeableValue<'V1>
  val mutable JoinKey: 'K2

  new
    (
      aval: aval<'U voption>,
      version: int64,
      last: 'U voption,
      cell: ChangeableValue<'V1>,
      joinKey: 'K2
    ) =
    {
      Aval = aval
      Version = version
      Last = last
      Cell = cell
      JoinKey = joinKey
    }

/// <summary>
/// Per-key equi-join over two adaptive maps (the node behind
/// <c>AMap.joinOn</c>). Every left entry maps to an output entry keyed by the
/// left key; the join key is computed from the left entry and looked up in the
/// right map. The mapping receives the left key, the left value as an
/// adaptive value, and the right-side value (or <c>ValueNone</c>), and returns
/// the output aval; a <c>ValueNone</c> output drops the entry (choose
/// semantics). The per-key subgraph is built once and updated in place: left
/// updates re-apply the value cell (no rebuild), join-key changes re-run the
/// mapping against the new lookup.
/// </summary>
type JoinMapNode<'K1, 'V1, 'K2, 'V2, 'U when 'K1: equality and 'K2: equality>
  (
    left: IAdaptiveMap<'K1, 'V1>,
    right: IAdaptiveMap<'K2, 'V2>,
    [<InlineIfLambda>] keyOfLeft: 'K1 -> 'V1 -> 'K2,
    [<InlineIfLambda>] mapping:
      'K1 -> aval<'V1> -> aval<'V2 voption> -> aval<'U voption>
  ) =
  let mutable state = MapNodeState<'K1, 'V1, 'U>.Create(1)
  let mutable cache = Dictionary<'K1, JoinEntry<'K2, 'V1, 'U>>()
  let mutable initialized = false
  let mutable disposed = false
  let mutable elementDirty = false
  let mutable lastDrainWriteGen = -1L

  member private this.Register() =
    match box left with
    | :? IMapSinkRegistry as r ->
      r.AddMapSink(box(this :> IMapDeltaSink<'K1, 'V1>))
    | _ -> ()

  member private this.Unregister() =
    match box left with
    | :? IMapSinkRegistry as r ->
      r.RemoveMapSink(box(this :> IMapDeltaSink<'K1, 'V1>))
    | _ -> ()

  /// The dirty gate: a scan is needed when the self-healing flag is set, or
  /// when a write moved the generation since the last scan.
  member private _.NeedsElementScan() =
    elementDirty || GraphContext.Default.WriteGeneration <> lastDrainWriteGen

  /// Build the per-key subgraph once: the swappable value cell, the right
  /// lookup at the computed join key, and the mapping over both. The entry
  /// keeps the cell and the join key; updates re-apply the cell in place.
  member private this.CreateEntry(k: 'K1, v: 'V1) =
    let cell = ChangeableValue<'V1>(v)
    let joinKey = keyOfLeft k v
    let lookup = new MapLookupNode<'K2, 'V2>(right, joinKey)
    let aval = mapping k (cell :> aval<'V1>) (lookup :> aval<'V2 voption>)
    // Version read BEFORE the force (see ElementSetNode).
    let preV = aval.Version
    let newV = aval.GetValue()
    JoinEntry(aval, preV, newV, cell, joinKey)

  member private this.EnsureInitialized() =
    if not initialized then
      // Snapshot first, register between, then load (see ElementSetNode).
      let snapshot = Dictionary<'K1, 'V1>(left.GetValue())
      this.Register()
      let mutable e = snapshot.GetEnumerator()

      while e.MoveNext() do
        let kvp = e.Current
        let entry = this.CreateEntry(kvp.Key, kvp.Value)

        match entry.Last with
        | ValueSome u -> state.Data[kvp.Key] <- u
        | ValueNone -> ()

        cache[kvp.Key] <- entry

      state.DepVersions[0] <- left.Version
      initialized <- true

  /// Apply the source journal with cache maintenance: removed keys drop
  /// their cache entry (and the right lookup dies with it); set keys swap
  /// the entry's value cell in place (no rebuild), re-running the mapping
  /// only when the computed join key changed.
  member private this.DrainJournal() =
    let rems = state.Journal.Rems
    let sets = state.Journal.Sets
    let remStart = rems.Count
    let setStart = sets.Count
    let mutable i = 0
    // Suspend the append-time cross-kind cancellation while the journal
    // is replayed (see journalAppendMap).
    state.Journal.InDrain[0] <- 1
    let mutable remsDone = 0
    let mutable setsDone = 0

    try
      while i < remStart do
        let k = rems.Items[i]
        cache.Remove k |> ignore

        if state.Data.Remove k then
          state.Out.Rems <- Collections.bufferAppend state.Out.Rems k

        i <- i + 1
        remsDone <- i

      i <- 0

      while i < setStart do
        let struct (k, v) = sets.Items[i]
        let mutable entry = Unchecked.defaultof<JoinEntry<'K2, 'V1, 'U>>

        if cache.TryGetValue(k, &entry) then
          // In-place swap: re-apply the cell (equality-gated, marks
          // the subgraph dirty), then re-point the lookup when the
          // join key moved (rare: a target switch).
          entry.Cell.Apply(v)

          let joinKey = keyOfLeft k v

          if joinKey <> entry.JoinKey then
            let lookup = new MapLookupNode<'K2, 'V2>(right, joinKey)

            let aval =
              mapping k (entry.Cell :> aval<'V1>) (lookup :> aval<'V2 voption>)

            entry.Aval <- aval
            entry.JoinKey <- joinKey

          // Version read BEFORE the force (see EnsureInitialized).
          let preV = entry.Aval.Version
          let newV = entry.Aval.GetValue()

          match newV with
          | ValueSome u ->
            let mutable old = Unchecked.defaultof<'U>

            if
              state.Data.TryGetValue(k, &old)
              && EqualityComparer<'U>.Default.Equals(old, u)
            then
              ()
            else
              state.Data[k] <- u

              state.Out.Sets <-
                Collections.bufferAppend state.Out.Sets (struct (k, u))
          | ValueNone ->
            if state.Data.Remove k then
              state.Out.Rems <- Collections.bufferAppend state.Out.Rems k

          entry.Version <- preV
          entry.Last <- newV
          cache[k] <- entry
        else
          let entry = this.CreateEntry(k, v)

          match entry.Last with
          | ValueSome u ->
            let mutable old = Unchecked.defaultof<'U>

            if
              state.Data.TryGetValue(k, &old)
              && EqualityComparer<'U>.Default.Equals(old, u)
            then
              ()
            else
              state.Data[k] <- u

              state.Out.Sets <-
                Collections.bufferAppend state.Out.Sets (struct (k, u))
          | ValueNone ->
            if state.Data.Remove k then
              state.Out.Rems <- Collections.bufferAppend state.Out.Rems k

          cache[k] <- entry

        i <- i + 1
        setsDone <- i
    finally
      state.Journal.InDrain[0] <- 0
      // Compact against the LIVE journal counts (see ElementSetNode).
      let remLive = state.Journal.Rems.Count

      if remLive > remsDone then
        Array.Copy(
          state.Journal.Rems.Items,
          remsDone,
          state.Journal.Rems.Items,
          0,
          remLive - remsDone
        )

        state.Journal.Rems.Count <- remLive - remsDone
      else
        state.Journal.Rems.Count <- 0

      let setLive = state.Journal.Sets.Count

      if setLive > setsDone then
        Array.Copy(
          state.Journal.Sets.Items,
          setsDone,
          state.Journal.Sets.Items,
          0,
          setLive - setsDone
        )

        state.Journal.Sets.Count <- setLive - setsDone
      else
        state.Journal.Sets.Count <- 0

  /// The element scan: re-read every cached aval's version; force the
  /// changed ones and apply the contribution change. Self-healing: a
  /// reentrant write mid-scan keeps the dirty flag set so the next read
  /// rescans. Right-map changes reach the entries here (the lookup version
  /// moves; the entry aval reports it; the write generation gate opens).
  member private this.ScanElements() =
    let startGen = GraphContext.Default.WriteGeneration
    let mutable changed = false
    let mutable e = cache.GetEnumerator()

    while e.MoveNext() do
      let kvp = e.Current
      let mutable entry = kvp.Value
      // Version read BEFORE the force (see EnsureInitialized).
      let preV = entry.Aval.Version

      if preV <> entry.Version then
        let newV = entry.Aval.GetValue()

        match newV with
        | ValueSome u ->
          let mutable old = Unchecked.defaultof<'U>

          if
            state.Data.TryGetValue(kvp.Key, &old)
            && EqualityComparer<'U>.Default.Equals(old, u)
          then
            () // version bumped to an equal value: no delta
          else
            state.Data[kvp.Key] <- u

            state.Out.Sets <-
              Collections.bufferAppend state.Out.Sets (struct (kvp.Key, u))

            changed <- true
        | ValueNone ->
          if state.Data.Remove kvp.Key then
            state.Out.Rems <- Collections.bufferAppend state.Out.Rems kvp.Key
            changed <- true

        entry.Version <- preV
        entry.Last <- newV
        cache[kvp.Key] <- entry

    if changed then
      state.Version <- state.Version + 1L

    if GraphContext.Default.WriteGeneration <> startGen then
      // A reentrant write landed mid-scan: the output may be stale, keep
      // the flag so the next read rescans.
      elementDirty <- true
    else
      elementDirty <- false

  /// Drain the journal, then the element scan, then push the accumulated
  /// output delta once, with notification delivery deferred.
  member private this.Process() =
    let ctx = GraphContext.Default
    let wasActive = ctx.TxActive
    ctx.TxActive <- true

    try
      if not state.Journal.IsEmpty then
        this.DrainJournal()

      if this.NeedsElementScan() then
        this.ScanElements()

      if not state.Out.IsEmpty then
        state.Version <- state.Version + 1L
        Collections.pushMapDelta &state.Sinks state.Out
        state.Out.Clear()
    finally
      ctx.TxActive <- wasActive

    // Capture AFTER the push (see ElementMapNode.Process).
    lastDrainWriteGen <- GraphContext.Default.WriteGeneration

  interface IMapDeltaSink<'K1, 'V1> with
    member this.OnDeltas
      (
        setEntries: struct ('K1 * 'V1)[],
        setCnt: int,
        removedKeys: 'K1[],
        remCnt: int
      ) =
      if not disposed then
        Collections.journalAppendMap
          &state.Journal
          setEntries
          setCnt
          removedKeys
          remCnt

        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveMap<'K1, 'U> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive map has been disposed."

        this.EnsureInitialized()

        if left.Version <> state.DepVersions[0] then
          left.GetValue() |> ignore
          state.DepVersions[0] <- left.Version

        this.Process()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Data :> IReadOnlyDictionary<'K1, 'U>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      // Dirty indicator for version-checking readers (see ElementSetNode).
      if this.NeedsElementScan() then
        state.Version + 1L
      else
        state.Version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.Unregister()
        Collections.clearSinks &state.Sinks

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveMapSink(sink) =
      Collections.removeSink &state.Sinks sink
