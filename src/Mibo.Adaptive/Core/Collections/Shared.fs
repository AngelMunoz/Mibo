namespace Mibo.Adaptive

open System
open System.Collections.Generic
open System.Collections.Frozen

// =============================================================================
// Collection contracts (PLAN.md Section 6.9)
// =============================================================================

/// <summary>
/// An adaptive set: either a changeable source or a derived node.
/// </summary>
/// <remarks>
/// <para>
/// <c>GetValue</c> returns a transient view of the internal state. The view is
/// valid only until the next write on the owner thread. Computations consume it;
/// they must not retain it or mutate it. <c>ASet.force</c> materializes an
/// immutable <see cref="FrozenSet&lt;'T&gt;"/> that is safe to retain; the library
/// never touches a forced value again.
/// </para>
/// <para>
/// Derived sets are disposable: disposal unregisters the node from its
/// dependencies and stops all delta processing. Disposing a changeable source is
/// a no-op; sources are owned by the application. Dispose derived nodes before
/// their consumers. Reading a disposed node throws.
/// </para>
/// </remarks>
type IAdaptiveSet<'T> =
  inherit IAdaptiveObject
  inherit IDisposable
  abstract member GetValue: unit -> IReadOnlySet<'T>

/// <summary>An abbreviation for <see cref="IAdaptiveSet&lt;'T&gt;"/> (FDA <c>aset&lt;'T&gt;</c> parity).</summary>
type aset<'T> = IAdaptiveSet<'T>

/// <summary>
/// An adaptive map: either a changeable source or a derived node. See
/// <see cref="IAdaptiveSet&lt;'T&gt;"/> for the view and disposal contracts.
/// </summary>
type IAdaptiveMap<'K, 'V when 'K: equality> =
  inherit IAdaptiveObject
  inherit IDisposable
  abstract member GetValue: unit -> IReadOnlyDictionary<'K, 'V>

/// <summary>An abbreviation for <see cref="IAdaptiveMap&lt;'K,'V&gt;"/> (FDA <c>amap&lt;'K,'V&gt;</c> parity).</summary>
type amap<'K, 'V when 'K: equality> = IAdaptiveMap<'K, 'V>

/// <summary>
/// Internal. Receives deltas from a set dependency. The implementation appends
/// the delta to its journal; processing happens on the next read (drain).
/// </summary>
type internal ISetDeltaSink<'T> =
  abstract member OnDeltas:
    added: 'T[] * addedCount: int * removed: 'T[] * removedCount: int -> unit

/// <summary>
/// Internal. Receives deltas from a map dependency. The implementation appends
/// the delta to its journal; processing happens on the next read (drain).
/// </summary>
type internal IMapDeltaSink<'K, 'V> =
  abstract member OnDeltas:
    setEntries: struct ('K * 'V)[] *
    setCount: int *
    removedKeys: 'K[] *
    removedCount: int ->
      unit

/// <summary>Internal. Register/unregister a set delta sink with a dependency.</summary>
type internal ISetSinkRegistry =
  abstract member AddSetSink: sink: obj -> unit
  abstract member RemoveSetSink: sink: obj -> unit

/// <summary>Internal. Register/unregister a map delta sink with a dependency.</summary>
type internal IMapSinkRegistry =
  abstract member AddMapSink: sink: obj -> unit
  abstract member RemoveMapSink: sink: obj -> unit

/// <summary>
/// An adaptive list: either a changeable source or a derived node. See
/// <see cref="IAdaptiveSet&lt;'T&gt;"/> for the view and disposal contracts.
/// Positions in list operations are 0-based and refer to the state as of the
/// previous operation in the same delta; deltas are applied in order
/// (docs/ALIST-DESIGN.md §3).
/// </summary>
type IAdaptiveList<'T> =
  inherit IAdaptiveObject
  inherit IDisposable
  abstract member GetValue: unit -> IReadOnlyList<'T>

/// <summary>An abbreviation for <see cref="IAdaptiveList&lt;'T&gt;"/> (FDA <c>alist&lt;'T&gt;</c> parity).</summary>
type alist<'T> = IAdaptiveList<'T>

// =============================================================================
// Struct state holders (PLAN.md Section 6.9, shared code)
//
// The node state lives in structs so the shared operations can address it
// without abstract classes or virtual dispatch. F# forbids byref parameters in
// inline functions (FS0412), and per-element byref calls through a byref
// parameter allocate (measured 24 B per call), so:
//   - the hot per-element operations take the state struct BY VALUE and return
//     the updated struct (zero allocation, small copies);
//   - byref parameters appear only at the top-level call sites (a class field
//     address: measured zero allocation).
// =============================================================================

/// <summary>One reusable array plus count. Node-owned; grows amortized.</summary>
[<Struct>]
type internal DeltaBuffer<'T> =
  val mutable Items: 'T[]
  val mutable Count: int

  new(items: 'T[], count: int) = { Items = items; Count = count }

  static member Create() = DeltaBuffer(Array.zeroCreate 16, 0)

  member this.IsEmpty = this.Count = 0

/// <summary>
/// A set delta: elements added and removed since the previous delivery. The
/// buffers are transient: valid only during the delivery that received the
/// delta.
/// </summary>
[<Struct>]
type SetDelta<'T> =
  val mutable internal Adds: DeltaBuffer<'T>
  val mutable internal Rems: DeltaBuffer<'T>
  // Shared in-drain flag (a one-slot array: the drains work on struct
  // copies, so a plain field would not be visible to reentrant appends).
  // Nonzero while a drain is replaying this journal; the append-time
  // cross-kind coalescing (see journalAppendSet) is suspended then, so it
  // can never remove an entry the in-flight drain is about to process.
  val mutable internal InDrain: int[]

  internal new(adds: DeltaBuffer<'T>, rems: DeltaBuffer<'T>) =
    {
      Adds = adds
      Rems = rems
      InDrain = Array.zeroCreate 1
    }

  static member internal Create() =
    SetDelta(DeltaBuffer<_>.Create(), DeltaBuffer<_>.Create())

  /// <summary>Gets whether this delta contains no operations.</summary>
  member this.IsEmpty = this.Adds.IsEmpty && this.Rems.IsEmpty

  member internal this.Clear() =
    this.Adds.Count <- 0
    this.Rems.Count <- 0

  /// <summary>The elements added. Transient: valid during the callback only.</summary>
  member this.Added = this.Adds.Items.AsMemory(0, this.Adds.Count)

  /// <summary>The elements removed. Transient: valid during the callback only.</summary>
  member this.Removed = this.Rems.Items.AsMemory(0, this.Rems.Count)

  /// <summary>Appends an add operation. For <see cref="ASet.custom"/> computes.</summary>
  member this.Add(item: 'T) =
    if isNull this.Adds.Items then
      this.Adds.Items <- Array.zeroCreate 16
    elif this.Adds.Count = this.Adds.Items.Length then
      let next = Array.zeroCreate(this.Adds.Items.Length * 2)
      Array.Copy(this.Adds.Items, next, this.Adds.Items.Length)
      this.Adds.Items <- next

    this.Adds.Items[this.Adds.Count] <- item
    this.Adds.Count <- this.Adds.Count + 1

  /// <summary>Appends a remove operation. For <see cref="ASet.custom"/> computes.</summary>
  member this.Remove(item: 'T) =
    if isNull this.Rems.Items then
      this.Rems.Items <- Array.zeroCreate 16
    elif this.Rems.Count = this.Rems.Items.Length then
      let next = Array.zeroCreate(this.Rems.Items.Length * 2)
      Array.Copy(this.Rems.Items, next, this.Rems.Items.Length)
      this.Rems.Items <- next

    this.Rems.Items[this.Rems.Count] <- item
    this.Rems.Count <- this.Rems.Count + 1

/// <summary>
/// A mutable delta builder for <see cref="ASet.custom"/> computes. The compute
/// receives the current view and this builder, appends the operations that
/// describe the change since the previous call, and returns. The builder is a
/// class: appends mutate the node's pending delta directly (a struct delta
/// passed by value would be copied and lost).
/// </summary>
type SetDeltaBuilder<'T>() =
  let mutable adds = DeltaBuffer<_>.Create()
  let mutable rems = DeltaBuffer<_>.Create()

  /// <summary>Appends an add operation.</summary>
  member _.Add(item: 'T) =
    if adds.Count = adds.Items.Length then
      let next = Array.zeroCreate(adds.Items.Length * 2)
      Array.Copy(adds.Items, next, adds.Items.Length)
      adds.Items <- next

    adds.Items[adds.Count] <- item
    adds.Count <- adds.Count + 1

  /// <summary>Appends a remove operation.</summary>
  member _.Remove(item: 'T) =
    if rems.Count = rems.Items.Length then
      let next = Array.zeroCreate(rems.Items.Length * 2)
      Array.Copy(rems.Items, next, rems.Items.Length)
      rems.Items <- next

    rems.Items[rems.Count] <- item
    rems.Count <- rems.Count + 1

  member internal _.IsEmpty = adds.IsEmpty && rems.IsEmpty

  member internal _.Clear() =
    adds.Count <- 0
    rems.Count <- 0

  member internal _.Adds = adds
  member internal _.Rems = rems

  member internal this.Snapshot() = SetDelta(adds, rems)

/// <summary>
/// A map delta: upserted entries and removed keys since the previous
/// delivery. The buffers are transient: valid only during the delivery that
/// received the delta.
/// </summary>
[<Struct>]
type MapDelta<'K, 'V> =
  val mutable internal Sets: DeltaBuffer<struct ('K * 'V)>
  val mutable internal Rems: DeltaBuffer<'K>
  // Shared in-drain flag; see SetDelta.InDrain.
  val mutable internal InDrain: int[]

  internal new(sets: DeltaBuffer<struct ('K * 'V)>, rems: DeltaBuffer<'K>) =
    {
      Sets = sets
      Rems = rems
      InDrain = Array.zeroCreate 1
    }

  static member internal Create() =
    MapDelta(DeltaBuffer<_>.Create(), DeltaBuffer<_>.Create())

  /// <summary>Gets whether this delta contains no operations.</summary>
  member this.IsEmpty = this.Sets.IsEmpty && this.Rems.IsEmpty

  member internal this.Clear() =
    this.Sets.Count <- 0
    this.Rems.Count <- 0

  /// <summary>The entries set (added or updated). Transient: valid during the callback only.</summary>
  member this.SetEntries = this.Sets.Items.AsMemory(0, this.Sets.Count)

  /// <summary>The keys removed. Transient: valid during the callback only.</summary>
  member this.RemovedKeys = this.Rems.Items.AsMemory(0, this.Rems.Count)

  /// <summary>Appends an upsert operation. For <see cref="AMap.custom"/> computes.</summary>
  member this.Set(key: 'K, value: 'V) =
    if isNull this.Sets.Items then
      this.Sets.Items <- Array.zeroCreate 16
    elif this.Sets.Count = this.Sets.Items.Length then
      let next = Array.zeroCreate(this.Sets.Items.Length * 2)
      Array.Copy(this.Sets.Items, next, this.Sets.Items.Length)
      this.Sets.Items <- next

    this.Sets.Items[this.Sets.Count] <- struct (key, value)
    this.Sets.Count <- this.Sets.Count + 1

  /// <summary>Appends a remove operation. For <see cref="AMap.custom"/> computes.</summary>
  member this.Remove(key: 'K) =
    if isNull this.Rems.Items then
      this.Rems.Items <- Array.zeroCreate 16
    elif this.Rems.Count = this.Rems.Items.Length then
      let next = Array.zeroCreate(this.Rems.Items.Length * 2)
      Array.Copy(this.Rems.Items, next, this.Rems.Items.Length)
      this.Rems.Items <- next

    this.Rems.Items[this.Rems.Count] <- key
    this.Rems.Count <- this.Rems.Count + 1

/// <summary>
/// A mutable delta builder for <see cref="AMap.custom"/> computes. See
/// <see cref="SetDeltaBuilder&lt;'T&gt;"/> for the protocol.
/// </summary>
type MapDeltaBuilder<'K, 'V>() =
  let mutable sets = DeltaBuffer<_>.Create()
  let mutable rems = DeltaBuffer<_>.Create()

  /// <summary>Appends an upsert operation.</summary>
  member _.Set(key: 'K, value: 'V) =
    if sets.Count = sets.Items.Length then
      let next = Array.zeroCreate(sets.Items.Length * 2)
      Array.Copy(sets.Items, next, sets.Items.Length)
      sets.Items <- next

    sets.Items[sets.Count] <- struct (key, value)
    sets.Count <- sets.Count + 1

  /// <summary>Appends a remove operation.</summary>
  member _.Remove(key: 'K) =
    if rems.Count = rems.Items.Length then
      let next = Array.zeroCreate(rems.Items.Length * 2)
      Array.Copy(rems.Items, next, rems.Items.Length)
      rems.Items <- next

    rems.Items[rems.Count] <- key
    rems.Count <- rems.Count + 1

  member internal _.IsEmpty = sets.IsEmpty && rems.IsEmpty

  member internal _.Clear() =
    sets.Count <- 0
    rems.Count <- 0

  member internal _.Sets = sets
  member internal _.Rems = rems

  member internal this.Snapshot() = MapDelta(sets, rems)

/// <summary>The kind of a list operation (docs/ALIST-DESIGN.md §3.1).</summary>
/// <remarks>
/// <c>Clear</c> is used only in changeable-source transaction journals as a
/// marker for a full clear; it is never part of a delivered delta (the source
/// expands it into descending removes).
/// </remarks>
type ListOpKind =
  /// Insert before the element currently at <c>Position</c>; <c>Position = count</c> appends.
  | Insert = 0
  /// Remove the element currently at <c>Position</c>.
  | Remove = 1
  /// Replace the element currently at <c>Position</c>.
  | Update = 2
  /// Internal transaction-journal marker only; never delivered.
  | Clear = 3

/// <summary>
/// One list operation. Positions are 0-based and refer to the state as of the
/// previous operation in the same delta; a delta is applied in order.
/// </summary>
/// <remarks>
/// <c>Source</c> is internal machinery for multi-source nodes (0 = primary or
/// left, 1 = right); delivered deltas always carry 0.
/// </remarks>
[<Struct>]
type ListOp<'T> =
  val Kind: ListOpKind
  val Position: int
  val Value: 'T
  val Source: byte

  new(kind: ListOpKind, position: int, value: 'T, source: byte) =
    {
      Kind = kind
      Position = position
      Value = value
      Source = source
    }

/// <summary>
/// A list delta: ordered operations since the previous delivery. The buffer
/// is transient: valid only during the delivery that received the delta.
/// Order is the semantics: apply the operations sequentially
/// (docs/ALIST-DESIGN.md §3.2).
/// </summary>
[<Struct>]
type ListDelta<'T> =
  val mutable internal Ops: DeltaBuffer<ListOp<'T>>

  internal new(ops: DeltaBuffer<ListOp<'T>>) = { Ops = ops }

  static member internal Create() = ListDelta(DeltaBuffer<_>.Create())

  /// <summary>Gets whether this delta contains no operations.</summary>
  member this.IsEmpty = this.Ops.Count = 0

  member internal this.Clear() = this.Ops.Count <- 0

  /// <summary>The operations, in application order. Transient: valid during the callback only.</summary>
  member this.Operations = this.Ops.Items.AsMemory(0, this.Ops.Count)

  /// <summary>Appends an insert operation. For <see cref="AList.custom"/> computes.</summary>
  member this.Insert(position: int, value: 'T) =
    if this.Ops.Count = this.Ops.Items.Length then
      let next = Array.zeroCreate(this.Ops.Items.Length * 2)
      Array.Copy(this.Ops.Items, next, this.Ops.Items.Length)
      this.Ops.Items <- next

    this.Ops.Items[this.Ops.Count] <-
      ListOp(ListOpKind.Insert, position, value, 0uy)

    this.Ops.Count <- this.Ops.Count + 1

  /// <summary>Appends a remove operation. For <see cref="AList.custom"/> computes.</summary>
  member this.Remove(position: int) =
    if this.Ops.Count = this.Ops.Items.Length then
      let next = Array.zeroCreate(this.Ops.Items.Length * 2)
      Array.Copy(this.Ops.Items, next, this.Ops.Items.Length)
      this.Ops.Items <- next

    this.Ops.Items[this.Ops.Count] <-
      ListOp(ListOpKind.Remove, position, Unchecked.defaultof<'T>, 0uy)

    this.Ops.Count <- this.Ops.Count + 1

  /// <summary>Appends an update operation. For <see cref="AList.custom"/> computes.</summary>
  member this.Update(position: int, value: 'T) =
    if this.Ops.Count = this.Ops.Items.Length then
      let next = Array.zeroCreate(this.Ops.Items.Length * 2)
      Array.Copy(this.Ops.Items, next, this.Ops.Items.Length)
      this.Ops.Items <- next

    this.Ops.Items[this.Ops.Count] <-
      ListOp(ListOpKind.Update, position, value, 0uy)

    this.Ops.Count <- this.Ops.Count + 1

/// <summary>
/// A class-based delta builder for <see cref="AList.custom"/> computes. The
/// struct <see cref="ListDelta&lt;'T&gt;"/> is passed by value (its counter
/// would be copied); this class keeps the appends visible to the node.
/// </summary>
type ListDeltaBuilder<'T>() =
  let mutable delta = ListDelta<'T>.Create()

  member internal _.IsEmpty = delta.IsEmpty

  member internal this.Clear() = delta.Clear()

  member internal this.Snapshot() = delta

  /// <summary>Appends an insert operation. Positions refer to the state as of the previous operation.</summary>
  member this.Insert(position: int, value: 'T) = delta.Insert(position, value)

  /// <summary>Appends a remove operation. Positions refer to the state as of the previous operation.</summary>
  member this.Remove(position: int) = delta.Remove(position)

  /// <summary>Appends an update operation. Positions refer to the state as of the previous operation.</summary>
  member this.Update(position: int, value: 'T) = delta.Update(position, value)

/// <summary>Internal. Receives deltas from a list dependency.</summary>
type internal IListDeltaSink<'T> =
  abstract member OnDeltas: ops: ListOp<'T>[] * opCount: int -> unit

/// <summary>Internal. Register/unregister a list delta sink with a dependency.</summary>
type internal IListSinkRegistry =
  abstract member AddListSink: sink: obj -> unit
  abstract member RemoveListSink: sink: obj -> unit

/// <summary>
/// Registered consumers of a collection node. Passed by value to the push
/// operations, so reentrant sink growth during delivery is safe.
/// </summary>
[<Struct>]
/// <summary>
/// The sink list of a source. Entries are <see cref="WeakReference"/>: a
/// derived node the user dropped (and that is not observed) is collected, and
/// delivery skips its dead entry (FDA precedent: <c>WeakOutputSet</c> stores
/// <c>WeakReference&lt;IAdaptiveObject&gt;</c>, Core.fs:210-219). A live sink
/// is strongly reachable through its owner (the user, an observation, or a
/// downstream node), so delivery always resolves it.
/// </summary>
type internal SinkList =
  val mutable Sinks: WeakReference[]
  val mutable Count: int

  new(sinks: WeakReference[], count: int) = { Sinks = sinks; Count = count }

  static member Create() = SinkList(Array.zeroCreate 4, 0)

  member this.IsEmpty = this.Count = 0

/// <summary>
/// A set with per-element reference counts: two source elements can map onto one
/// output element, and the output element disappears only when the last source
/// reference disappears.
/// </summary>
[<Struct>]
type internal RefCountedSet<'T when 'T: equality> =
  val mutable Data: HashSet<'T>
  val mutable Refcounts: Dictionary<'T, int>

  new(data: HashSet<'T>, refcounts: Dictionary<'T, int>) =
    { Data = data; Refcounts = refcounts }

  static member Create() =
    RefCountedSet(HashSet<'T>(), Dictionary<'T, int>())

/// <summary>
/// State of a derived set node (map over set, filter, union). 'T is the input
/// element type (the journal holds input-coordinate deltas); 'U is the output
/// element type (the state and output deltas live in output coordinates).
/// </summary>
[<Struct>]
type internal SetNodeState<'T, 'U when 'U: equality> =
  val mutable Version: int64
  val mutable Sinks: SinkList
  val mutable DepVersions: int64[]
  val mutable Set: RefCountedSet<'U>
  val mutable Journal: SetDelta<'T>
  val mutable Out: SetDelta<'U>

  new
    (
      version: int64,
      sinks: SinkList,
      depVersions: int64[],
      set: RefCountedSet<'U>,
      journal: SetDelta<'T>,
      out: SetDelta<'U>
    ) =
    {
      Version = version
      Sinks = sinks
      DepVersions = depVersions
      Set = set
      Journal = journal
      Out = out
    }

  static member Create(depCount: int) =
    SetNodeState(
      0L,
      SinkList.Create(),
      Array.zeroCreate depCount,
      RefCountedSet.Create(),
      SetDelta<_>.Create(),
      SetDelta<_>.Create()
    )

/// <summary>
/// State of a derived map node (map over map, filter). The journal holds
/// input-coordinate deltas (source entries); the state and output deltas live in
/// output coordinates.
/// </summary>
[<Struct>]
type internal MapNodeState<'K, 'V, 'U when 'K: equality> =
  val mutable Version: int64
  val mutable Sinks: SinkList
  val mutable DepVersions: int64[]
  val mutable Data: Dictionary<'K, 'U>
  val mutable Journal: MapDelta<'K, 'V>
  val mutable Out: MapDelta<'K, 'U>

  new
    (
      version: int64,
      sinks: SinkList,
      depVersions: int64[],
      data: Dictionary<'K, 'U>,
      journal: MapDelta<'K, 'V>,
      out: MapDelta<'K, 'U>
    ) =
    {
      Version = version
      Sinks = sinks
      DepVersions = depVersions
      Data = data
      Journal = journal
      Out = out
    }

  static member Create(depCount: int) =
    MapNodeState(
      0L,
      SinkList.Create(),
      Array.zeroCreate depCount,
      Dictionary<'K, 'U>(),
      MapDelta<_, _>.Create(),
      MapDelta<_, _>.Create()
    )

/// <summary>
/// Per-element cache entry of the <c>*A</c> nodes (mapA/chooseA/filterA,
/// docs/2026-08-05-MAPA-DESIGN.md). Holds the element's aval, its version at
/// the last force (the version read BEFORE the force: a mid-force write then
/// leaves the stored version stale, so the next scan re-forces), and its last
/// contribution to the output.
/// </summary>
[<Struct>]
type internal ElementEntry<'U> =
  val mutable Aval: aval<'U voption>
  val mutable Version: int64
  val mutable Last: 'U voption

  new(aval: aval<'U voption>, version: int64, last: 'U voption) =
    {
      Aval = aval
      Version = version
      Last = last
    }

// =============================================================================
// Shared operations
// =============================================================================

/// <summary>
/// The binary set operation of <see cref="TwoSourceSetNode&lt;'T&gt;"/>:
/// difference (left minus right), intersection, or symmetric difference.
/// </summary>
[<Struct>]
type TwoSetOp =
  | Difference
  | Intersect
  | Xor

module internal Collections =

  /// Grow an array to hold at least n items. Amortized O(1); array growth only.
  let ensureCapacity (arr: 'T[] byref) (n: int) =
    if arr.Length < n then
      let next = Array.zeroCreate(max n (arr.Length * 2))
      Array.Copy(arr, next, arr.Length)
      arr <- next

  /// Append one item to a delta buffer. By value: the per-element hot path
  /// must not pass byrefs through byref parameters (measured 24 B per call).
  let bufferAppend (buffer: DeltaBuffer<'T>) (item: 'T) : DeltaBuffer<'T> =
    if buffer.Count = buffer.Items.Length then
      let next = Array.zeroCreate(buffer.Items.Length * 2)
      Array.Copy(buffer.Items, next, buffer.Items.Length)
      next[buffer.Count] <- item
      DeltaBuffer(next, buffer.Count + 1)
    else
      buffer.Items[buffer.Count] <- item
      DeltaBuffer(buffer.Items, buffer.Count + 1)

  /// Drop the entries of <paramref name="buffer" /> whose key appears in
  /// <paramref name="keys" />, preserving order (shift-compact, one pass).
  /// Linear scan for small products (zero allocation); a hash set above the
  /// threshold. The caller guarantees no drain is in flight, so every entry
  /// in the buffer is quiescent and removable.
  let coalesceDeltaKeys<'T, 'S, 'K when 'K: equality>
    (keyOfItem: 'T -> 'K)
    (keyOfKey: 'S -> 'K)
    (buffer: DeltaBuffer<'T> byref)
    (keys: 'S[])
    (keyCnt: int)
    =
    if keyCnt > 0 && buffer.Count > 0 then
      let comparer = EqualityComparer<'K>.Default
      let mutable w = 0

      if int64 keyCnt * int64 buffer.Count <= 4096L then
        for r in 0 .. buffer.Count - 1 do
          let item = buffer.Items[r]
          let key = keyOfItem item
          let mutable found = false
          let mutable j = 0

          while not found && j < keyCnt do
            if comparer.Equals(key, keyOfKey keys[j]) then
              found <- true

            j <- j + 1

          if not found then
            buffer.Items[w] <- item
            w <- w + 1
      else
        let keySet = HashSet<'K>()

        for j in 0 .. keyCnt - 1 do
          keySet.Add(keyOfKey keys[j]) |> ignore

        for r in 0 .. buffer.Count - 1 do
          let item = buffer.Items[r]

          if not(keySet.Contains(keyOfItem item)) then
            buffer.Items[w] <- item
            w <- w + 1

      buffer.Count <- w

  /// Cancel cross-kind duplicates between a journal buffer and an incoming
  /// batch: an incoming entry with a pending opposite-kind entry of the
  /// same key drops BOTH (the element never left the derived state, so
  /// replaying the survivor alone would double its refcount and
  /// double-count a reduction). The pending buffer is compacted in place
  /// and the surviving incoming entries are appended to
  /// <paramref name="target" />. Linear for small products (zero
  /// allocation); hash sets above the threshold. The caller guarantees no
  /// drain is in flight (the InDrain flag).
  let cancelCrossKind<'T when 'T: equality>
    (pending: DeltaBuffer<'T> byref)
    (incoming: 'T[])
    (incomingCnt: int)
    (target: DeltaBuffer<'T> byref)
    =
    if incomingCnt > 0 then
      if pending.Count = 0 then
        ensureCapacity &target.Items (target.Count + incomingCnt)
        Array.Copy(incoming, 0, target.Items, target.Count, incomingCnt)
        target.Count <- target.Count + incomingCnt
      elif int64 incomingCnt * int64 pending.Count <= 4096L then
        let comparer = EqualityComparer<'T>.Default
        ensureCapacity &target.Items (target.Count + incomingCnt)
        let mutable tw = target.Count

        for i in 0 .. incomingCnt - 1 do
          let x = incoming[i]
          let mutable found = -1
          let mutable j = 0

          while found < 0 && j < pending.Count do
            if comparer.Equals(pending.Items[j], x) then
              found <- j

            j <- j + 1

          if found >= 0 then
            for k in found .. pending.Count - 2 do
              pending.Items[k] <- pending.Items[k + 1]

            pending.Count <- pending.Count - 1
          else
            target.Items[tw] <- x
            tw <- tw + 1

        target.Count <- tw
      else
        let pendingKeys = HashSet<'T>()
        let incomingKeys = HashSet<'T>()

        for j in 0 .. pending.Count - 1 do
          pendingKeys.Add(pending.Items[j]) |> ignore

        for i in 0 .. incomingCnt - 1 do
          incomingKeys.Add(incoming[i]) |> ignore

        let mutable w = 0

        for j in 0 .. pending.Count - 1 do
          let item = pending.Items[j]

          if not(incomingKeys.Contains item) then
            pending.Items[w] <- item
            w <- w + 1

        pending.Count <- w
        ensureCapacity &target.Items (target.Count + incomingCnt)
        let mutable tw = target.Count

        for i in 0 .. incomingCnt - 1 do
          let x = incoming[i]

          if not(pendingKeys.Contains x) then
            target.Items[tw] <- x
            tw <- tw + 1

        target.Count <- tw

  /// Append a delta to a set journal (called at write time by the pusher).
  /// Cross-kind duplicates are cancelled first (an add cancels a pending
  /// rem of the same element and vice versa; arrival order = append order):
  /// without this the journal can hold [add x, rem x] across two deliveries
  /// with no drain between, and the drain replays rems before adds
  /// regardless of arrival order, leaving x present although the source
  /// removed it. Suspended while a drain is in flight (the InDrain flag):
  /// the drain works on a struct copy, so a removal then could shift
  /// entries under its captured indexes and double-process them.
  let journalAppendSet<'T when 'T: equality>
    (journal: SetDelta<'T> byref)
    (adds: 'T[])
    (addCnt: int)
    (rems: 'T[])
    (remCnt: int)
    =
    if journal.InDrain[0] = 0 then
      cancelCrossKind &journal.Rems adds addCnt &journal.Adds
      cancelCrossKind &journal.Adds rems remCnt &journal.Rems
    else
      ensureCapacity &journal.Adds.Items (journal.Adds.Count + addCnt)
      Array.Copy(adds, 0, journal.Adds.Items, journal.Adds.Count, addCnt)
      journal.Adds.Count <- journal.Adds.Count + addCnt
      ensureCapacity &journal.Rems.Items (journal.Rems.Count + remCnt)
      Array.Copy(rems, 0, journal.Rems.Items, journal.Rems.Count, remCnt)
      journal.Rems.Count <- journal.Rems.Count + remCnt

  /// Append a delta to a map journal (called at write time by the pusher).
  /// Cross-kind duplicates are coalesced first (last op wins: an incoming
  /// set supersedes a pending rem of the same key and vice versa, safe
  /// because the map-reduce mirror preserves old values). See
  /// <see cref="journalAppendSet"/> for the divergence this prevents and
  /// the InDrain suspension rule.
  let journalAppendMap<'K, 'V when 'K: equality>
    (journal: MapDelta<'K, 'V> byref)
    (sets: struct ('K * 'V)[])
    (setCnt: int)
    (rems: 'K[])
    (remCnt: int)
    =
    if journal.InDrain[0] = 0 then
      coalesceDeltaKeys id (fun (struct (k, _)) -> k) &journal.Rems sets setCnt
      coalesceDeltaKeys (fun (struct (k, _)) -> k) id &journal.Sets rems remCnt

    ensureCapacity &journal.Sets.Items (journal.Sets.Count + setCnt)
    Array.Copy(sets, 0, journal.Sets.Items, journal.Sets.Count, setCnt)
    journal.Sets.Count <- journal.Sets.Count + setCnt
    ensureCapacity &journal.Rems.Items (journal.Rems.Count + remCnt)
    Array.Copy(rems, 0, journal.Rems.Items, journal.Rems.Count, remCnt)
    journal.Rems.Count <- journal.Rems.Count + remCnt

  /// Append a delta to a list journal (called at write time by the pusher).
  let inline journalAppendList
    (journal: ListDelta<'T> byref)
    (ops: ListOp<'T>[])
    (opCnt: int)
    =
    ensureCapacity &journal.Ops.Items (journal.Ops.Count + opCnt)
    Array.Copy(ops, 0, journal.Ops.Items, journal.Ops.Count, opCnt)
    journal.Ops.Count <- journal.Ops.Count + opCnt

  /// Drop dead sink entries (their node was collected). Runs at the start of
  /// every delivery and on registration: swap-pop is safe here because no
  /// user code can interleave. Amortized O(1) per dead entry, zero
  /// allocation.
  let inline compactDeadSinks(sinks: SinkList byref) =
    let mutable i = 0

    while i < sinks.Count do
      if isNull sinks.Sinks[i].Target then
        sinks.Sinks[i] <- sinks.Sinks[sinks.Count - 1]
        sinks.Sinks[sinks.Count - 1] <- null
        sinks.Count <- sinks.Count - 1
      else
        i <- i + 1

  /// Register a sink (weakly). Returns nothing; the caller decides when to
  /// unregister. One WeakReference per registration (amortized edge
  /// formation, not a hot path). Dead entries are swept on registration so
  /// the list does not accumulate between deliveries.
  let inline addSink (sinks: SinkList byref) (sink: obj) =
    compactDeadSinks &sinks
    ensureCapacity &sinks.Sinks (sinks.Count + 1)
    sinks.Sinks[sinks.Count] <- WeakReference(sink)
    sinks.Count <- sinks.Count + 1

  /// Remove a sink by identity (matches the weak entry's target).
  let removeSink (sinks: SinkList byref) (sink: obj) =
    let mutable found = -1
    let mutable i = 0

    while found < 0 && i < sinks.Count do
      if obj.ReferenceEquals(sinks.Sinks[i].Target, sink) then
        found <- i
      else
        i <- i + 1

    if found >= 0 then
      sinks.Count <- sinks.Count - 1

      for j in found .. sinks.Count - 1 do
        sinks.Sinks[j] <- sinks.Sinks[j + 1]

      sinks.Sinks[sinks.Count] <- null

  /// Drop all sinks (disposal): releases the downstream references.
  let inline clearSinks(sinks: SinkList byref) =
    Array.Clear(sinks.Sinks, 0, sinks.Count)
    sinks.Count <- 0

  /// Add one reference to a refcounted set. By value: per-element hot path.
  /// Returns the updated state and whether the element is newly present.
  ///
  /// Note: the F# `match dict.TryGetValue key with | true, v ->` pattern
  /// allocates 24 B per call (measured); the explicit out-param form is
  /// zero-allocation. Hot paths must use the explicit form.
  let refAdd
    (state: RefCountedSet<'T>)
    (item: 'T)
    : struct (RefCountedSet<'T> * bool) =
    let mutable n = 0

    if state.Refcounts.TryGetValue(item, &n) then
      state.Refcounts[item] <- n + 1
      struct (state, false)
    else
      state.Refcounts[item] <- 1
      struct (state, state.Data.Add item)

  /// Remove one reference from a refcounted set. By value: per-element hot
  /// path. Returns the updated state and whether the element is fully removed.
  let refRemove
    (state: RefCountedSet<'T>)
    (item: 'T)
    : struct (RefCountedSet<'T> * bool) =
    let mutable n = 0

    if state.Refcounts.TryGetValue(item, &n) then
      if n = 1 then
        state.Refcounts.Remove item |> ignore
        struct (state, state.Data.Remove item)
      else
        state.Refcounts[item] <- n - 1
        struct (state, false)
    else
      struct (state, false)

  /// Push a set delta to every registered sink. The batch delivers only to
  /// the sinks registered at the start (bound captured): a sink registered
  /// reentrantly during delivery is not delivered, because its init snapshot
  /// already reflects the change (register between snapshot and load) and
  /// delivering would double-apply. Dead entries are compacted before the
  /// loop; an entry that dies mid-delivery (a GC inside user code) is
  /// skipped and compacted by the next delivery. The resolved target is
  /// rooted by the local, so a mid-delivery GC cannot collect it.
  let pushSetDelta (sinks: SinkList byref) (delta: SetDelta<'T>) =
    if not delta.IsEmpty then
      compactDeadSinks &sinks
      let adds = delta.Adds.Items
      let addCnt = delta.Adds.Count
      let rems = delta.Rems.Items
      let remCnt = delta.Rems.Count
      let bound = sinks.Count
      let mutable i = 0

      while i < bound do
        let target = sinks.Sinks[i].Target

        if not(isNull target) then
          (unbox<ISetDeltaSink<'T>> target).OnDeltas(adds, addCnt, rems, remCnt)

        i <- i + 1

  /// Push a map delta to every registered sink. See <see cref="pushSetDelta"/>
  /// for the dead-entry and reentrancy handling.
  let pushMapDelta (sinks: SinkList byref) (delta: MapDelta<'K, 'V>) =
    if not delta.IsEmpty then
      compactDeadSinks &sinks
      let sets = delta.Sets.Items
      let setCnt = delta.Sets.Count
      let rems = delta.Rems.Items
      let remCnt = delta.Rems.Count
      let bound = sinks.Count
      let mutable i = 0

      while i < bound do
        let target = sinks.Sinks[i].Target

        if not(isNull target) then
          (unbox<IMapDeltaSink<'K, 'V>> target)
            .OnDeltas(sets, setCnt, rems, remCnt)

        i <- i + 1

  /// Push a set delta to every registered sink and record the write: the
  /// write generation advances, so version-gated readers (the *A gates, the
  /// scalar dirty caches) re-check on their next read.
  let inline pushAndBumpSet
    (ctx: GraphContext)
    (delta: SetDelta<'T>)
    (sinks: SinkList byref)
    =
    ctx.BumpWriteGeneration()
    pushSetDelta &sinks delta

  /// Push a map delta to every registered sink and record the write (see
  /// <see cref="pushAndBumpSet"/>).
  let inline pushAndBumpMap
    (ctx: GraphContext)
    (delta: MapDelta<'K, 'V>)
    (sinks: SinkList byref)
    =
    ctx.BumpWriteGeneration()
    pushMapDelta &sinks delta

  /// Push a list delta to every registered sink. See <see cref="pushSetDelta"/>
  /// for the dead-entry and reentrancy handling.
  let pushListDelta (sinks: SinkList byref) (delta: ListDelta<'T>) =
    compactDeadSinks &sinks
    let ops = delta.Ops.Items
    let opCnt = delta.Ops.Count
    let bound = sinks.Count
    let mutable i = 0

    while i < bound do
      let target = sinks.Sinks[i].Target

      if not(isNull target) then
        (unbox<IListDeltaSink<'T>> target).OnDeltas(ops, opCnt)

      i <- i + 1

  /// Push a list delta to every registered sink and record the write (see
  /// <see cref="pushAndBumpSet"/>).
  let inline pushAndBumpList
    (ctx: GraphContext)
    (delta: ListDelta<'T>)
    (sinks: SinkList byref)
    =
    ctx.BumpWriteGeneration()
    pushListDelta &sinks delta

  /// Drain the journal of a set node with refcounts (map over set, union):
  /// apply each pending delta to the state and collect the reduced output
  /// delta. Entries appended during processing (reentrant writes) survive.
  /// Returns the updated state and whether the state changed.
  let inline drainRefSet
    ([<InlineIfLambda>] map: 'T -> 'U voption)
    (state: SetNodeState<'T, 'U>)
    : struct (SetNodeState<'T, 'U> * bool) =
    let mutable s = state
    let mutable changed = false
    let rems = s.Journal.Rems
    let adds = s.Journal.Adds
    let remStart = rems.Count
    let addStart = adds.Count
    let mutable i = 0
    // Consumed counts: entries before these positions were applied and must
    // never be applied again; the entry that threw (and reentrant entries)
    // survive for the next drain.
    let mutable remsDone = 0
    let mutable addsDone = 0

    try
      while i < remStart do
        let x = rems.Items[i]

        match map x with
        | ValueSome y ->
          let struct (set2, removed) = refRemove s.Set y
          s.Set <- set2

          if removed then
            s.Out.Rems <- bufferAppend s.Out.Rems y
            changed <- true
        | ValueNone -> ()

        i <- i + 1
        remsDone <- i

      i <- 0

      while i < addStart do
        let y = adds.Items[i]

        match map y with
        | ValueSome z ->
          let struct (set2, added) = refAdd s.Set z
          s.Set <- set2

          if added then
            s.Out.Adds <- bufferAppend s.Out.Adds z
            changed <- true
        | ValueNone -> ()

        i <- i + 1
        addsDone <- i
    finally
      // Compact in the finally so a throwing mapping cannot make the next
      // drain re-apply consumed entries (double-apply corrupts refcounts).
      let remLive = s.Journal.Rems.Count

      if remLive > remsDone then
        Array.Copy(
          s.Journal.Rems.Items,
          remsDone,
          s.Journal.Rems.Items,
          0,
          remLive - remsDone
        )

        s.Journal.Rems.Count <- remLive - remsDone
      else
        s.Journal.Rems.Count <- 0

      let addLive = s.Journal.Adds.Count

      if addLive > addsDone then
        Array.Copy(
          s.Journal.Adds.Items,
          addsDone,
          s.Journal.Adds.Items,
          0,
          addLive - addsDone
        )

        s.Journal.Adds.Count <- addLive - addsDone
      else
        s.Journal.Adds.Count <- 0

    struct (s, changed)

  /// Drain the journal of a set node without refcounts (filter): plain
  /// membership. Returns the updated state and whether the state changed.
  let inline drainPlainSet
    ([<InlineIfLambda>] map: 'T -> 'T voption)
    (state: SetNodeState<'T, 'T>)
    : struct (SetNodeState<'T, 'T> * bool) =
    let mutable s = state
    let mutable changed = false
    let rems = s.Journal.Rems
    let adds = s.Journal.Adds
    let remStart = rems.Count
    let addStart = adds.Count
    let mutable i = 0
    // Consumed counts: see drainRefSet (the throwing entry survives).
    let mutable remsDone = 0
    let mutable addsDone = 0

    try
      while i < remStart do
        let x = rems.Items[i]

        if s.Set.Data.Remove x then
          s.Out.Rems <- bufferAppend s.Out.Rems x
          changed <- true

        i <- i + 1
        remsDone <- i

      i <- 0

      while i < addStart do
        let x = adds.Items[i]

        match map x with
        | ValueSome z ->
          if s.Set.Data.Add z then
            s.Out.Adds <- bufferAppend s.Out.Adds z
            changed <- true
        | ValueNone -> ()

        i <- i + 1
        addsDone <- i
    finally
      // Compact in the finally so a throwing predicate cannot make the
      // next drain re-apply consumed entries.
      let remLive = s.Journal.Rems.Count

      if remLive > remsDone then
        Array.Copy(
          s.Journal.Rems.Items,
          remsDone,
          s.Journal.Rems.Items,
          0,
          remLive - remsDone
        )

        s.Journal.Rems.Count <- remLive - remsDone
      else
        s.Journal.Rems.Count <- 0

      let addLive = s.Journal.Adds.Count

      if addLive > addsDone then
        Array.Copy(
          s.Journal.Adds.Items,
          addsDone,
          s.Journal.Adds.Items,
          0,
          addLive - addsDone
        )

        s.Journal.Adds.Count <- addLive - addsDone
      else
        s.Journal.Adds.Count <- 0

    struct (s, changed)

  /// Drain the journal of a map node: apply each pending delta to the state
  /// and collect the reduced output delta. The lambda returns ValueNone for
  /// elements to drop (filter). Returns the updated state and whether the
  /// state changed.
  let inline drainMap
    ([<InlineIfLambda>] map: 'K -> 'V -> 'U voption)
    (state: MapNodeState<'K, 'V, 'U>)
    : struct (MapNodeState<'K, 'V, 'U> * bool) =
    let mutable s = state
    let mutable changed = false
    let rems = s.Journal.Rems
    let sets = s.Journal.Sets
    let remStart = rems.Count
    let setStart = sets.Count
    let mutable i = 0
    // Consumed counts: see drainRefSet (the throwing entry survives).
    let mutable remsDone = 0
    let mutable setsDone = 0

    try
      while i < remStart do
        let k = rems.Items[i]

        if s.Data.Remove k then
          s.Out.Rems <- bufferAppend s.Out.Rems k
          changed <- true

        i <- i + 1
        remsDone <- i

      i <- 0

      while i < setStart do
        let struct (k, v) = sets.Items[i]

        match map k v with
        | ValueSome u ->
          let mutable old = Unchecked.defaultof<'U>

          if
            s.Data.TryGetValue(k, &old)
            && EqualityComparer<'U>.Default.Equals(old, u)
          then
            ()
          else
            s.Data[k] <- u
            s.Out.Sets <- bufferAppend s.Out.Sets (struct (k, u))
            changed <- true
        | ValueNone ->
          if s.Data.Remove k then
            s.Out.Rems <- bufferAppend s.Out.Rems k
            changed <- true

        i <- i + 1
        setsDone <- i
    finally
      // Compact in the finally so a throwing mapping cannot make the next
      // drain re-apply consumed entries.
      let remLive = s.Journal.Rems.Count

      if remLive > remsDone then
        Array.Copy(
          s.Journal.Rems.Items,
          remsDone,
          s.Journal.Rems.Items,
          0,
          remLive - remsDone
        )

        s.Journal.Rems.Count <- remLive - remsDone
      else
        s.Journal.Rems.Count <- 0

      let setLive = s.Journal.Sets.Count

      if setLive > setsDone then
        Array.Copy(
          s.Journal.Sets.Items,
          setsDone,
          s.Journal.Sets.Items,
          0,
          setLive - setsDone
        )

        s.Journal.Sets.Count <- setLive - setsDone
      else
        s.Journal.Sets.Count <- 0

    struct (s, changed)

  /// Drain a set node and push the reduced output delta to its sinks. The
  /// byref appears only at this top-level call site (a class field address:
  /// 0 allocation).
  let inline drainSetPush
    ([<InlineIfLambda>] map: 'T -> 'U voption)
    (state: SetNodeState<'T, 'U> byref)
    =
    let ctx = GraphContext.Current
    let wasActive = ctx.TxActive
    ctx.TxActive <- true
    // Suspend the append-time coalescing while the journal is replayed:
    // the drain works on a struct copy, and a removal from the real
    // journal could shift entries under the copy's captured indexes.
    state.Journal.InDrain[0] <- 1

    try
      let struct (s2, changed) = drainRefSet map state
      state <- s2

      if changed then
        pushSetDelta &state.Sinks state.Out
        state.Out.Clear()
    finally
      state.Journal.InDrain[0] <- 0
      ctx.TxActive <- wasActive

  /// Drain a plain set node (filter) and push the reduced output delta.
  let inline drainPlainSetPush
    ([<InlineIfLambda>] map: 'T -> 'T voption)
    (state: SetNodeState<'T, 'T> byref)
    =
    let ctx = GraphContext.Current
    let wasActive = ctx.TxActive
    ctx.TxActive <- true
    state.Journal.InDrain[0] <- 1

    try
      let struct (s2, changed) = drainPlainSet map state
      state <- s2

      if changed then
        pushSetDelta &state.Sinks state.Out
        state.Out.Clear()
    finally
      state.Journal.InDrain[0] <- 0
      ctx.TxActive <- wasActive

  /// Drain a map node and push the reduced output delta to its sinks.
  let inline drainMapPush
    ([<InlineIfLambda>] map: 'K -> 'V -> 'U voption)
    (state: MapNodeState<'K, 'V, 'U> byref)
    =
    let ctx = GraphContext.Current
    let wasActive = ctx.TxActive
    ctx.TxActive <- true
    state.Journal.InDrain[0] <- 1

    try
      let struct (s2, changed) = drainMap map state
      state <- s2

      if changed then
        pushMapDelta &state.Sinks state.Out
        state.Out.Clear()
    finally
      state.Journal.InDrain[0] <- 0
      ctx.TxActive <- wasActive

  /// Initial load of a refcounted set node: build the internal state from a
  /// snapshot of the source view. The node takes the snapshot and registers
  /// its sink between the snapshot and this call, so user-code writes from
  /// the mapping land in the journal instead of mutating the transient view
  /// mid-iteration.
  let inline loadRefSet
    ([<InlineIfLambda>] map: 'T -> 'U voption)
    (snapshot: HashSet<'T>)
    (state: SetNodeState<'T, 'U> byref)
    =
    for item in snapshot do
      match map item with
      | ValueSome z ->
        let struct (set2, _) = refAdd state.Set z
        state.Set <- set2
      | ValueNone -> ()

  /// Initial load of a plain set node (filter). See <see cref="loadRefSet"/>.
  let inline loadPlainSet
    ([<InlineIfLambda>] map: 'T -> 'T voption)
    (snapshot: HashSet<'T>)
    (state: SetNodeState<'T, 'T> byref)
    =
    for item in snapshot do
      match map item with
      | ValueSome z -> state.Set.Data.Add z |> ignore
      | ValueNone -> ()

  /// Initial load of a map node. See <see cref="loadRefSet"/>.
  let inline loadMap
    ([<InlineIfLambda>] map: 'K -> 'V -> 'U voption)
    (snapshot: Dictionary<'K, 'V>)
    (state: MapNodeState<'K, 'V, 'U> byref)
    =
    for KeyValue(k, v) in snapshot do
      match map k v with
      | ValueSome u -> state.Data[k] <- u
      | ValueNone -> ()

  // =============================================================================
  // Two-source set algebra (PLAN.md Section 7.3): difference, intersect, xor.
  //
  // Each source has its own journal (side sinks route deliveries by side). The
  // state keeps per-side reference counts; the output membership is derived per
  // operation. Cross-side ordering does not matter: each side's counts update
  // independently, and the output transition is decided from the counts.
  // =============================================================================

  /// <summary>Internal. Receives side-routed set deltas of a two-source node.</summary>
  type internal ITwoSetSinkTarget<'T> =
    abstract member OnSideDeltas:
      side: int * adds: 'T[] * addCnt: int * rems: 'T[] * remCnt: int -> unit

  /// <summary>
  /// Internal. A per-side set sink: appends the delivery to the side's journal.
  /// Two instances per two-source node (construction-time allocation only).
  /// </summary>
  type internal SideSetSink<'T>(target: obj, side: int) =
    interface ISetDeltaSink<'T> with
      member this.OnDeltas(adds: 'T[], addCnt: int, rems: 'T[], remCnt: int) =
        (unbox<ITwoSetSinkTarget<'T>> target)
          .OnSideDeltas(side, adds, addCnt, rems, remCnt)

  /// <summary>
  /// Internal. Routes a delivery to the node's side handler through a plain
  /// interface call (a stored multi-arg closure would invoke curried,
  /// allocating intermediate closures per delivery). The deltas cross as
  /// <c>obj</c> and are unboxed once per delivery by the node (zero
  /// allocation; arrays are reference types).
  /// </summary>
  type internal ISideMapSinkTarget =
    abstract member OnSideDeltas:
      side: int * sets: obj * setCnt: int * rems: obj * remCnt: int -> unit

  type internal SideMapSink<'K, 'V>(target: ISideMapSinkTarget, side: int) =
    interface IMapDeltaSink<'K, 'V> with
      member this.OnDeltas
        (sets: struct ('K * 'V)[], setCnt: int, rems: 'K[], remCnt: int)
        =
        target.OnSideDeltas(side, sets, setCnt, rems, remCnt)

  /// <summary>Internal. State of a two-source set node.</summary>
  [<Struct>]
  type internal TwoSetState<'T when 'T: equality> =
    val mutable Version: int64
    val mutable Sinks: SinkList
    val mutable DepVersions: int64[]
    val mutable Left: RefCountedSet<'T>
    val mutable Right: RefCountedSet<'T>
    val mutable Out: HashSet<'T>
    val mutable JournalL: SetDelta<'T>
    val mutable JournalR: SetDelta<'T>
    val mutable OutDelta: SetDelta<'T>
    // Reused scratch for the net-delta post-pass (construction-time
    // allocation only; zero steady-state allocation).
    val mutable Scratch: HashSet<'T>

    new
      (
        version: int64,
        sinks: SinkList,
        depVersions: int64[],
        left: RefCountedSet<'T>,
        right: RefCountedSet<'T>,
        out: HashSet<'T>,
        journalL: SetDelta<'T>,
        journalR: SetDelta<'T>,
        outDelta: SetDelta<'T>,
        scratch: HashSet<'T>
      ) =
      {
        Version = version
        Sinks = sinks
        DepVersions = depVersions
        Left = left
        Right = right
        Out = out
        JournalL = journalL
        JournalR = journalR
        OutDelta = outDelta
        Scratch = scratch
      }

    static member Create(depCount: int) =
      TwoSetState(
        0L,
        SinkList.Create(),
        Array.zeroCreate depCount,
        RefCountedSet.Create(),
        RefCountedSet.Create(),
        HashSet<'T>(),
        SetDelta<_>.Create(),
        SetDelta<_>.Create(),
        SetDelta<_>.Create(),
        HashSet<'T>(16)
      )

  /// <summary>
  /// Process one side's journal of a two-source set node. By value: the hot
  /// per-element path must not pass byrefs through byref parameters (measured
  /// 24 B per call). Returns the updated state and whether the output changed.
  /// </summary>
  let processTwoSide
    (op: TwoSetOp)
    (side: int)
    (s: TwoSetState<'T>)
    : struct (TwoSetState<'T> * bool) =
    let mutable s = s
    let mutable changed = false
    let rems = if side = 0 then s.JournalL.Rems else s.JournalR.Rems
    let adds = if side = 0 then s.JournalL.Adds else s.JournalR.Adds
    let remStart = rems.Count
    let addStart = adds.Count
    let mutable i = 0

    try
      while i < remStart do
        let x = rems.Items[i]
        let mutable removed = false

        if side = 0 then
          let struct (set2, r) = refRemove s.Left x
          s.Left <- set2
          removed <- r
        else
          let struct (set2, r) = refRemove s.Right x
          s.Right <- set2
          removed <- r

        if removed then
          let otherHas =
            if side = 0 then
              s.Right.Data.Contains x
            else
              s.Left.Data.Contains x

          match op with
          | Difference ->
            if side = 0 then
              if not otherHas && s.Out.Remove x then
                s.OutDelta.Rems <- bufferAppend s.OutDelta.Rems x
                changed <- true
            elif otherHas && s.Out.Add x then
              s.OutDelta.Adds <- bufferAppend s.OutDelta.Adds x
              changed <- true
          | Intersect ->
            if otherHas && s.Out.Remove x then
              s.OutDelta.Rems <- bufferAppend s.OutDelta.Rems x
              changed <- true
          | Xor ->
            if otherHas then
              if s.Out.Add x then
                s.OutDelta.Adds <- bufferAppend s.OutDelta.Adds x
                changed <- true
            elif s.Out.Remove x then
              s.OutDelta.Rems <- bufferAppend s.OutDelta.Rems x
              changed <- true

        i <- i + 1

      i <- 0

      while i < addStart do
        let x = adds.Items[i]
        let mutable added = false

        if side = 0 then
          let struct (set2, a) = refAdd s.Left x
          s.Left <- set2
          added <- a
        else
          let struct (set2, a) = refAdd s.Right x
          s.Right <- set2
          added <- a

        if added then
          let otherHas =
            if side = 0 then
              s.Right.Data.Contains x
            else
              s.Left.Data.Contains x

          match op with
          | Difference ->
            if side = 0 then
              if not otherHas && s.Out.Add x then
                s.OutDelta.Adds <- bufferAppend s.OutDelta.Adds x
                changed <- true
            elif otherHas && s.Out.Remove x then
              s.OutDelta.Rems <- bufferAppend s.OutDelta.Rems x
              changed <- true
          | Intersect ->
            if otherHas && s.Out.Add x then
              s.OutDelta.Adds <- bufferAppend s.OutDelta.Adds x
              changed <- true
          | Xor ->
            if otherHas then
              if s.Out.Remove x then
                s.OutDelta.Rems <- bufferAppend s.OutDelta.Rems x
                changed <- true
            elif s.Out.Add x then
              s.OutDelta.Adds <- bufferAppend s.OutDelta.Adds x
              changed <- true

        i <- i + 1
    finally
      // Compact the side's journal even when an op threw: consumed
      // entries must not be applied twice by the next drain. Entries
      // appended during processing (reentrant writes) survive.
      if side = 0 then
        let remLive = s.JournalL.Rems.Count

        if remLive > remStart then
          Array.Copy(
            s.JournalL.Rems.Items,
            remStart,
            s.JournalL.Rems.Items,
            0,
            remLive - remStart
          )

          s.JournalL.Rems.Count <- remLive - remStart
        else
          s.JournalL.Rems.Count <- 0

        let addLive = s.JournalL.Adds.Count

        if addLive > addStart then
          Array.Copy(
            s.JournalL.Adds.Items,
            addStart,
            s.JournalL.Adds.Items,
            0,
            addLive - addStart
          )

          s.JournalL.Adds.Count <- addLive - addStart
        else
          s.JournalL.Adds.Count <- 0
      else
        let remLive = s.JournalR.Rems.Count

        if remLive > remStart then
          Array.Copy(
            s.JournalR.Rems.Items,
            remStart,
            s.JournalR.Rems.Items,
            0,
            remLive - remStart
          )

          s.JournalR.Rems.Count <- remLive - remStart
        else
          s.JournalR.Rems.Count <- 0

        let addLive = s.JournalR.Adds.Count

        if addLive > addStart then
          Array.Copy(
            s.JournalR.Adds.Items,
            addStart,
            s.JournalR.Adds.Items,
            0,
            addLive - addStart
          )

          s.JournalR.Adds.Count <- addLive - addStart
        else
          s.JournalR.Adds.Count <- 0

    struct (s, changed)

  /// <summary>
  /// Net-delta post-pass for the two-source set producers: a batch must not
  /// carry the same element in both adds and rems (consumers apply the
  /// buffers in either order; a same-element pair would diverge). For a
  /// refcounted set an add is only emitted when the element was absent, so a
  /// same-element add+remove in one batch always nets to nothing: drop both.
  /// By value, like the drains: a byref to this generic struct allocates
  /// (measured 104 B per batch).
  /// </summary>
  let inline netifyTwoSetDelta(s: TwoSetState<'T>) : TwoSetState<'T> =
    let mutable s = s

    if s.OutDelta.Adds.Count > 0 && s.OutDelta.Rems.Count > 0 then
      s.Scratch.Clear()

      for i in 0 .. s.OutDelta.Rems.Count - 1 do
        s.Scratch.Add s.OutDelta.Rems.Items[i] |> ignore

      let mutable ai = 0

      while ai < s.OutDelta.Adds.Count do
        if s.Scratch.Contains s.OutDelta.Adds.Items[ai] then
          // Swap-pop: order within a set delta buffer does not matter.
          s.OutDelta.Adds.Items[ai] <-
            s.OutDelta.Adds.Items[s.OutDelta.Adds.Count - 1]

          s.OutDelta.Adds.Count <- s.OutDelta.Adds.Count - 1
        else
          ai <- ai + 1

      let mutable ri = 0

      while ri < s.OutDelta.Rems.Count do
        if s.Scratch.Contains s.OutDelta.Rems.Items[ri] then
          s.OutDelta.Rems.Items[ri] <-
            s.OutDelta.Rems.Items[s.OutDelta.Rems.Count - 1]

          s.OutDelta.Rems.Count <- s.OutDelta.Rems.Count - 1
        else
          ri <- ri + 1

    s

  /// <summary>Drain both journals of a two-source set node. Returns the updated state and whether the output changed.</summary>
  let inline drainTwoSet
    (op: TwoSetOp)
    (s: TwoSetState<'T>)
    : struct (TwoSetState<'T> * bool) =
    let struct (s1, c1) = processTwoSide op 0 s
    let mutable struct (s2, c2) = processTwoSide op 1 s1

    // Net-delta invariant (see netifyTwoSetDelta): same-element add+rem
    // pairs cancel; consumers apply net deltas order-free.
    if c1 || c2 then
      struct (netifyTwoSetDelta s2, c1 || c2)
    else
      struct (s2, false)

  /// <summary>
  /// Drain a two-source set node and push the reduced output delta to its
  /// sinks. The byref appears only at this top-level call site (a class
  /// field address: 0 allocation).
  /// </summary>
  let inline drainTwoSetPush (op: TwoSetOp) (state: TwoSetState<'T> byref) =
    let ctx = GraphContext.Current
    let wasActive = ctx.TxActive
    ctx.TxActive <- true

    try
      let struct (s2, changed) = drainTwoSet op state
      state <- s2

      if changed then
        pushSetDelta &state.Sinks state.OutDelta
        state.OutDelta.Clear()
    finally
      ctx.TxActive <- wasActive

  /// <summary>
  /// The concrete HashSet view of a set node when available (the hot path,
  /// zero allocation); constant nodes hold FrozenSets, so a foreign view is
  /// materialized once.
  /// </summary>
  let inline asHashSet(view: IReadOnlySet<'T>) =
    match view with
    | :? HashSet<'T> as h -> h
    | other -> HashSet<'T>(other)

  /// <summary>
  /// The concrete Dictionary view of a map node when available (the hot
  /// path, zero allocation); constant nodes hold FrozenDictionaries, so a
  /// foreign view is materialized once.
  /// </summary>
  let inline asDictionary(view: IReadOnlyDictionary<'K, 'V>) =
    match view with
    | :? Dictionary<'K, 'V> as d -> d
    | other -> Dictionary<'K, 'V>(other)

  /// <summary>Initial load of a two-source set node: build the state from both source views.</summary>
  let loadTwoSet
    (op: TwoSetOp)
    (left: IAdaptiveSet<'T>)
    (right: IAdaptiveSet<'T>)
    (state: TwoSetState<'T> byref)
    =
    // The views are HashSets in this implementation; interface
    // iteration would box the enumerator (measured 40 B per element).
    let leftView = asHashSet(left.GetValue())

    for x in leftView do
      let struct (set2, _) = refAdd state.Left x
      state.Left <- set2

      match op with
      | Difference
      | Xor -> state.Out.Add x |> ignore
      | Intersect -> () // the right loop seeds the intersection

    let rightView = asHashSet(right.GetValue())

    for x in rightView do
      let struct (set2, _) = refAdd state.Right x
      state.Right <- set2

      match op with
      | Difference -> state.Out.Remove x |> ignore
      | Intersect ->
        if state.Left.Data.Contains x then
          state.Out.Add x |> ignore
      | Xor ->
        if state.Left.Data.Contains x then
          state.Out.Remove x |> ignore
        else
          state.Out.Add x |> ignore

  // =============================================================================
  // Two-source map algebra (PLAN.md Section 7.3): choose2, intersect(With),
  // union(With). One node shape; the mapping decides the semantics (FDA models
  // all of them on Choose2VReader). The mapping receives the key and both side
  // values (voptions) and returns the output value (voption). It is called only
  // when at least one side has a value (FDA parity: "mapping will always receive
  // at least one *Some* argument"); a key with no value on either side is removed
  // without calling it.
  // =============================================================================

  /// <summary>Internal. State of a choose2 map node.</summary>
  [<Struct>]
  type internal Choose2State<'K, 'V1, 'V2, 'V3 when 'K: equality> =
    val mutable Version: int64
    val mutable Sinks: SinkList
    val mutable DepVersions: int64[]
    val mutable Sides: Dictionary<'K, struct ('V1 voption * 'V2 voption)>
    val mutable Out: Dictionary<'K, 'V3>
    val mutable JournalL: MapDelta<'K, 'V1>
    val mutable JournalR: MapDelta<'K, 'V2>
    val mutable OutDelta: MapDelta<'K, 'V3>
    // Reused scratch for the net-delta post-pass (construction-time
    // allocation only; zero steady-state allocation).
    val mutable Scratch: HashSet<'K>
    val mutable Scratch2: HashSet<'K>

    new
      (
        version: int64,
        sinks: SinkList,
        depVersions: int64[],
        sides: Dictionary<'K, struct ('V1 voption * 'V2 voption)>,
        out: Dictionary<'K, 'V3>,
        journalL: MapDelta<'K, 'V1>,
        journalR: MapDelta<'K, 'V2>,
        outDelta: MapDelta<'K, 'V3>,
        scratch: HashSet<'K>,
        scratch2: HashSet<'K>
      ) =
      {
        Version = version
        Sinks = sinks
        DepVersions = depVersions
        Sides = sides
        Out = out
        JournalL = journalL
        JournalR = journalR
        OutDelta = outDelta
        Scratch = scratch
        Scratch2 = scratch2
      }

    static member Create(depCount: int) =
      Choose2State(
        0L,
        SinkList.Create(),
        Array.zeroCreate depCount,
        Dictionary<'K, struct ('V1 voption * 'V2 voption)>(),
        Dictionary<'K, 'V3>(),
        MapDelta<_, _>.Create(),
        MapDelta<_, _>.Create(),
        MapDelta<_, _>.Create(),
        HashSet<'K>(16),
        HashSet<'K>(16)
      )

  /// <summary>
  /// Apply one output transition of a choose2 drain: compare with the stored
  /// output, emit the delta (with equal-value elision), update the output.
  /// By value: the hot per-element path must not pass byrefs through byref
  /// parameters (measured 24 B per call).
  /// </summary>
  let applyChoose2Out
    (s: Choose2State<'K, 'V1, 'V2, 'V3>)
    (k: 'K)
    (newOut: 'V3 voption)
    : struct (Choose2State<'K, 'V1, 'V2, 'V3> * bool) =
    let mutable s = s
    let mutable changed = false
    let mutable old = Unchecked.defaultof<'V3>

    if s.Out.TryGetValue(k, &old) then
      if newOut.IsSome then
        let v = newOut.Value

        if EqualityComparer<'V3>.Default.Equals(old, v) then
          ()
        else
          s.Out[k] <- v
          s.OutDelta.Sets <- bufferAppend s.OutDelta.Sets (struct (k, v))
          changed <- true
      else
        s.Out.Remove k |> ignore
        s.OutDelta.Rems <- bufferAppend s.OutDelta.Rems k
        changed <- true
    elif newOut.IsSome then
      let v = newOut.Value
      s.Out[k] <- v
      s.OutDelta.Sets <- bufferAppend s.OutDelta.Sets (struct (k, v))
      changed <- true

    struct (s, changed)

  /// <summary>
  /// Process one side's journal of a choose2 node. The mapping is called only
  /// when at least one side has a value (FDA parity). Returns the updated state
  /// and whether the output changed.
  /// </summary>
  let inline processChoose2Side
    ([<InlineIfLambda>] mapping: 'K -> 'V1 voption -> 'V2 voption -> 'V3 voption)
    (side: int)
    (s: Choose2State<'K, 'V1, 'V2, 'V3>)
    : struct (Choose2State<'K, 'V1, 'V2, 'V3> * bool) =
    let mutable s = s
    let mutable changed = false

    // The two side journals have different value types, so the loops are
    // per side (an `if side` over the journals would unify 'V1 with 'V2).
    if side = 0 then
      let rems = s.JournalL.Rems
      let sets = s.JournalL.Sets
      let remStart = rems.Count
      let setStart = sets.Count
      let mutable i = 0

      while i < remStart do
        let k = rems.Items[i]
        let mutable cur = struct (ValueNone, ValueNone)

        if s.Sides.TryGetValue(k, &cur) then
          let struct (_, rv) = cur

          match rv with
          | ValueNone ->
            // both sides gone: remove without calling the mapping.
            s.Sides.Remove k |> ignore
            let struct (s2, c) = applyChoose2Out s k ValueNone
            s <- s2
            changed <- changed || c
          | ValueSome _ ->
            let newOut = mapping k ValueNone rv
            let struct (s2, c) = applyChoose2Out s k newOut
            s <- s2
            changed <- changed || c
            s.Sides[k] <- struct (ValueNone, rv)

        i <- i + 1

      i <- 0

      while i < setStart do
        let struct (k, v) = sets.Items[i]
        let mutable cur = struct (ValueNone, ValueNone)

        let rv =
          if s.Sides.TryGetValue(k, &cur) then
            let struct (_, rv) = cur
            rv
          else
            ValueNone

        let newOut = mapping k (ValueSome v) rv
        let struct (s2, c) = applyChoose2Out s k newOut
        s <- s2
        changed <- changed || c
        s.Sides[k] <- struct (ValueSome v, rv)
        i <- i + 1

      let remLive = s.JournalL.Rems.Count

      if remLive > remStart then
        Array.Copy(
          s.JournalL.Rems.Items,
          remStart,
          s.JournalL.Rems.Items,
          0,
          remLive - remStart
        )

        s.JournalL.Rems.Count <- remLive - remStart
      else
        s.JournalL.Rems.Count <- 0

      let setLive = s.JournalL.Sets.Count

      if setLive > setStart then
        Array.Copy(
          s.JournalL.Sets.Items,
          setStart,
          s.JournalL.Sets.Items,
          0,
          setLive - setStart
        )

        s.JournalL.Sets.Count <- setLive - setStart
      else
        s.JournalL.Sets.Count <- 0
    else
      let rems = s.JournalR.Rems
      let sets = s.JournalR.Sets
      let remStart = rems.Count
      let setStart = sets.Count
      let mutable i = 0

      while i < remStart do
        let k = rems.Items[i]
        let mutable cur = struct (ValueNone, ValueNone)

        if s.Sides.TryGetValue(k, &cur) then
          let struct (lv, _) = cur

          match lv with
          | ValueNone ->
            // both sides gone: remove without calling the mapping.
            s.Sides.Remove k |> ignore
            let struct (s2, c) = applyChoose2Out s k ValueNone
            s <- s2
            changed <- changed || c
          | ValueSome _ ->
            let newOut = mapping k lv ValueNone
            let struct (s2, c) = applyChoose2Out s k newOut
            s <- s2
            changed <- changed || c
            s.Sides[k] <- struct (lv, ValueNone)

        i <- i + 1

      i <- 0

      while i < setStart do
        let struct (k, v) = sets.Items[i]
        let mutable cur = struct (ValueNone, ValueNone)

        let lv =
          if s.Sides.TryGetValue(k, &cur) then
            let struct (lv, _) = cur
            lv
          else
            ValueNone

        let newOut = mapping k lv (ValueSome v)
        let struct (s2, c) = applyChoose2Out s k newOut
        s <- s2
        changed <- changed || c
        s.Sides[k] <- struct (lv, ValueSome v)
        i <- i + 1

      let remLive = s.JournalR.Rems.Count

      if remLive > remStart then
        Array.Copy(
          s.JournalR.Rems.Items,
          remStart,
          s.JournalR.Rems.Items,
          0,
          remLive - remStart
        )

        s.JournalR.Rems.Count <- remLive - remStart
      else
        s.JournalR.Rems.Count <- 0

      let setLive = s.JournalR.Sets.Count

      if setLive > setStart then
        Array.Copy(
          s.JournalR.Sets.Items,
          setStart,
          s.JournalR.Sets.Items,
          0,
          setLive - setStart
        )

        s.JournalR.Sets.Count <- setLive - setStart
      else
        s.JournalR.Sets.Count <- 0

    struct (s, changed)

  /// <summary>
  /// Net-delta post-pass for choose2 producers: a batch must not carry the
  /// same key in both sets and rems. Both sides can touch one key in one
  /// batch (Set k then Rem k, or Rem k then Set k); the final membership
  /// (state.Out) decides the net: present -> keep the sets, drop the rems;
  /// absent -> keep the rems, drop the sets.
  /// </summary>
  let inline netifyChoose2Delta(state: Choose2State<'K, 'V1, 'V2, 'V3> byref) =
    if state.OutDelta.Sets.Count > 0 && state.OutDelta.Rems.Count > 0 then
      state.Scratch.Clear()
      state.Scratch2.Clear()

      for i in 0 .. state.OutDelta.Sets.Count - 1 do
        let struct (k, _) = state.OutDelta.Sets.Items[i]
        state.Scratch.Add k |> ignore

      let mutable ri = 0

      while ri < state.OutDelta.Rems.Count do
        let k = state.OutDelta.Rems.Items[ri]

        if state.Scratch.Contains k then
          if state.Out.ContainsKey k then
            // Present at the end: the net is a Set. Drop this Rem.
            state.OutDelta.Rems.Items[ri] <-
              state.OutDelta.Rems.Items[state.OutDelta.Rems.Count - 1]

            state.OutDelta.Rems.Count <- state.OutDelta.Rems.Count - 1
          else
            // Absent at the end: the net is a Rem. Drop the Sets.
            state.Scratch2.Add k |> ignore
            ri <- ri + 1
        else
          ri <- ri + 1

      let mutable si = 0

      while si < state.OutDelta.Sets.Count do
        let struct (k, _) = state.OutDelta.Sets.Items[si]

        if state.Scratch2.Contains k then
          state.OutDelta.Sets.Items[si] <-
            state.OutDelta.Sets.Items[state.OutDelta.Sets.Count - 1]

          state.OutDelta.Sets.Count <- state.OutDelta.Sets.Count - 1
        else
          si <- si + 1

  /// <summary>Drain both journals of a choose2 node. Returns the updated state and whether the output changed.</summary>
  let inline drainChoose2
    ([<InlineIfLambda>] mapping: 'K -> 'V1 voption -> 'V2 voption -> 'V3 voption)
    (s: Choose2State<'K, 'V1, 'V2, 'V3>)
    : struct (Choose2State<'K, 'V1, 'V2, 'V3> * bool) =
    let struct (s1, c1) = processChoose2Side mapping 0 s
    let struct (s2, c2) = processChoose2Side mapping 1 s1

    // Net-delta invariant (see netifyChoose2Delta): same-key set+rem pairs
    // reduce to the final state; consumers apply net deltas order-free.
    if c1 || c2 then
      let mutable s3 = s2
      netifyChoose2Delta &s3
      struct (s3, c1 || c2)
    else
      struct (s2, false)

  /// <summary>
  /// Drain a choose2 node and push the reduced output delta to its sinks.
  /// </summary>
  let inline drainChoose2Push
    ([<InlineIfLambda>] mapping: 'K -> 'V1 voption -> 'V2 voption -> 'V3 voption)
    (state: Choose2State<'K, 'V1, 'V2, 'V3> byref)
    =
    let ctx = GraphContext.Current
    let wasActive = ctx.TxActive
    ctx.TxActive <- true
    // Suspend the append-time coalescing while the journals are replayed
    // (the mapping is user code that can write to either source).
    state.JournalL.InDrain[0] <- 1
    state.JournalR.InDrain[0] <- 1

    try
      let struct (s2, changed) = drainChoose2 mapping state
      state <- s2

      if changed then
        pushMapDelta &state.Sinks state.OutDelta
        state.OutDelta.Clear()
    finally
      state.JournalL.InDrain[0] <- 0
      state.JournalR.InDrain[0] <- 0
      ctx.TxActive <- wasActive

  /// <summary>Initial load of a choose2 node: merge both source snapshots through the mapping.</summary>
  let inline loadChoose2
    ([<InlineIfLambda>] mapping: 'K -> 'V1 voption -> 'V2 voption -> 'V3 voption)
    (leftSnapshot: Dictionary<'K, 'V1>)
    (rightSnapshot: Dictionary<'K, 'V2>)
    (state: Choose2State<'K, 'V1, 'V2, 'V3> byref)
    =
    for KeyValue(k, v) in leftSnapshot do
      state.Sides[k] <- struct (ValueSome v, ValueNone)

      match mapping k (ValueSome v) ValueNone with
      | ValueSome o -> state.Out[k] <- o
      | ValueNone -> ()

    for KeyValue(k, v) in rightSnapshot do
      let mutable cur = struct (ValueNone, ValueNone)

      if state.Sides.TryGetValue(k, &cur) then
        let struct (lv, _) = cur
        state.Sides[k] <- struct (lv, ValueSome v)

        match mapping k lv (ValueSome v) with
        | ValueSome o -> state.Out[k] <- o
        | ValueNone -> state.Out.Remove k |> ignore
      else
        state.Sides[k] <- struct (ValueNone, ValueSome v)

        match mapping k ValueNone (ValueSome v) with
        | ValueSome o -> state.Out[k] <- o
        | ValueNone -> ()

  // =============================================================================
  // Rebuild helpers (PLAN.md Section 7.3): ofAval replaces the whole state on
  // every value change and emits the diff as the output delta.
  // =============================================================================

  /// <summary>
  /// Replace the state of a plain set node with <paramref name="next"/> and
  /// collect the diff as the output delta. Returns whether anything changed.
  /// The removals are collected first: mutating a HashSet while iterating it is
  /// undefined.
  /// </summary>
  let rebuildSetDiff
    (next: HashSet<'T>)
    (state: SetNodeState<'T, 'T> byref)
    : bool =
    let mutable changed = false
    let mutable e = next.GetEnumerator()

    while e.MoveNext() do
      let x = e.Current

      if state.Set.Data.Add x then
        state.Out.Adds <- bufferAppend state.Out.Adds x
        changed <- true

    let mutable e2 = state.Set.Data.GetEnumerator()

    while e2.MoveNext() do
      let x = e2.Current

      if not(next.Contains x) then
        state.Out.Rems <- bufferAppend state.Out.Rems x
        changed <- true

    for i in 0 .. state.Out.Rems.Count - 1 do
      state.Set.Data.Remove state.Out.Rems.Items[i] |> ignore

    changed

  /// <summary>
  /// Replace the state of a map node with <paramref name="next"/> and collect
  /// the diff as the output delta (equal values elided). Returns whether
  /// anything changed. The removals are collected first: mutating a Dictionary
  /// while iterating it is undefined.
  /// </summary>
  let rebuildMapDiff
    (next: Dictionary<'K, 'V>)
    (state: MapNodeState<'K, 'V, 'V> byref)
    : bool =
    let mutable changed = false

    for KeyValue(k, v) in next do
      let mutable old = Unchecked.defaultof<'V>

      if
        state.Data.TryGetValue(k, &old)
        && EqualityComparer<'V>.Default.Equals(old, v)
      then
        ()
      else
        state.Data[k] <- v
        state.Out.Sets <- bufferAppend state.Out.Sets (struct (k, v))
        changed <- true

    let mutable e = state.Data.GetEnumerator()
    let mutable removeCount = 0

    while e.MoveNext() do
      let k = e.Current.Key

      if not(next.ContainsKey k) then
        state.Out.Rems <- bufferAppend state.Out.Rems k
        removeCount <- removeCount + 1
        // Removals are a change too: without this, a removals-only poll
        // leaves Out uncleared and the stale removals replay on the
        // next diff, deleting freshly added entries.
        changed <- true

    for i in 0 .. state.Out.Rems.Count - 1 do
      state.Data.Remove state.Out.Rems.Items[i] |> ignore

    changed

  /// <summary>
  /// Replace the state of a list node with <paramref name="next"/> and
  /// collect the positional diff as the output delta (prefix/suffix, the
  /// <c>ChangeableList.ApplyDiff</c> algorithm). Returns whether anything
  /// changed. The ops are appended via the public <see cref="ListDelta"/>
  /// helpers (application order: removals then inserts for the structural
  /// case, updates in place for the equal-count case).
  /// </summary>
  let rebuildListDiff
    (next: IReadOnlyList<'T>)
    (data: ResizeArray<'T>)
    (out: ListDelta<'T> byref)
    : bool =
    let oldCount = data.Count
    let newCount = next.Count
    let mutable prefix = 0
    let limit = min oldCount newCount

    while prefix < limit
          && EqualityComparer<'T>.Default.Equals(data[prefix], next[prefix]) do
      prefix <- prefix + 1

    let mutable suffix = 0
    let mutable trimming = true

    while trimming do
      if
        suffix < limit - prefix
        && EqualityComparer<'T>.Default
          .Equals(data[oldCount - 1 - suffix], next[newCount - 1 - suffix])
      then
        suffix <- suffix + 1
      else
        trimming <- false

    let oldMid = oldCount - prefix - suffix
    let newMid = newCount - prefix - suffix

    if oldMid = newMid then
      for i in 0 .. oldMid - 1 do
        let v = next[prefix + i]
        data[prefix + i] <- v
        out.Update(prefix + i, v)
    else
      for i in oldMid - 1 .. -1 .. 0 do
        data.RemoveAt(prefix + i)
        out.Remove(prefix + i)

      for i in 0 .. newMid - 1 do
        let v = next[prefix + i]
        data.Insert(prefix + i, v)
        out.Insert(prefix + i, v)

    not out.IsEmpty

  // =============================================================================
  // Dynamic dependencies (PLAN.md Section 7.4): collect and bind.
  //
  // CollectSetNode unions one inner adaptive set per source element. Each
  // element's contribution is tracked separately (content set + journal +
  // sink); the output is the refcounted union (the CountingHashSet role). A
  // removed source element unregisters its inner sink eagerly (ANALYSIS-FDA.md
  // Pitfall 1). BindSetNode/BindMapNode swap the whole inner collection when
  // their scalar value changes (FDA BindReader semantics).
  // =============================================================================

  /// <summary>
  /// Internal. One source element's contribution to a collect node: the inner
  /// adaptive set, its last-seen version, the current content, the pending
  /// journal, and the registered sink. A struct so the shared operations can
  /// address its journal byref; the dictionary holds the authoritative copy.
  /// </summary>
  [<Struct>]
  type internal CollectEntry<'U when 'U: equality> =
    val mutable Node: IAdaptiveSet<'U>
    val mutable Version: int64
    val mutable Content: HashSet<'U>
    val mutable Journal: SetDelta<'U>
    val mutable Sink: obj

    new(node: IAdaptiveSet<'U>) =
      {
        Node = node
        Version = 0L
        Content = HashSet<'U>()
        Journal = SetDelta<'U>.Create()
        Sink = null
      }

  /// <summary>Internal. Receives the side-routed deltas of one inner set.</summary>
  type internal ICollectTarget<'T, 'U> =
    abstract member OnInnerDeltas:
      key: 'T * adds: 'U[] * addCnt: int * rems: 'U[] * remCnt: int -> unit

  /// <summary>
  /// Internal. A per-element sink: routes an inner set's delivery to the
  /// entry of its source element. Per-element allocation (amortized edge
  /// formation; zero steady-state allocation).
  /// </summary>
  type internal CollectSink<'T, 'U>(target: ICollectTarget<'T, 'U>, key: 'T) =
    interface ISetDeltaSink<'U> with
      member this.OnDeltas(adds: 'U[], addCnt: int, rems: 'U[], remCnt: int) =
        target.OnInnerDeltas(key, adds, addCnt, rems, remCnt)

  /// <summary>Internal. State of a collect node (PLAN.md Section 7.4).</summary>
  [<Struct>]
  type internal CollectState<'T, 'U when 'T: equality and 'U: equality> =
    val mutable Version: int64
    val mutable Sinks: SinkList
    val mutable DepVersions: int64[]
    val mutable Journal: SetDelta<'T>
    val mutable Inner: Dictionary<'T, CollectEntry<'U>>
    val mutable Global: RefCountedSet<'U>
    val mutable OutDelta: SetDelta<'U>
    // Reused scratch for the net-delta pass: prior presence of every
    // output element touched this batch (construction-time allocation
    // only; zero steady-state allocation).
    val mutable Scratch: Dictionary<'U, bool>

    new
      (
        version: int64,
        sinks: SinkList,
        depVersions: int64[],
        journal: SetDelta<'T>,
        inner: Dictionary<'T, CollectEntry<'U>>,
        counts: RefCountedSet<'U>,
        outDelta: SetDelta<'U>,
        scratch: Dictionary<'U, bool>
      ) =
      {
        Version = version
        Sinks = sinks
        DepVersions = depVersions
        Journal = journal
        Inner = inner
        Global = counts
        OutDelta = outDelta
        Scratch = scratch
      }

    static member Create(depCount: int) =
      CollectState(
        0L,
        SinkList.Create(),
        Array.zeroCreate depCount,
        SetDelta<_>.Create(),
        Dictionary<'T, CollectEntry<'U>>(),
        RefCountedSet.Create(),
        SetDelta<_>.Create(),
        Dictionary<'U, bool>(16)
      )

  /// <summary>
  /// Drain a collect node: process the source journal (removed elements drop
  /// their contribution and unregister their inner sink; added elements create
  /// an entry and load the inner content) and then every entry journal. By
  /// value: the per-element hot path must not pass byrefs through byref
  /// parameters. Returns the updated state and whether the output changed.
  /// </summary>
  let inline drainCollect
    (target: ICollectTarget<'T, 'U>)
    (mapping: 'T -> IAdaptiveSet<'U>)
    (s: CollectState<'T, 'U>)
    : struct (CollectState<'T, 'U> * bool) =
    let mutable s = s
    let mutable changed = false
    // Reused scratch: prior presence of every output element touched this
    // batch. The net out delta is derived at the end: a same-element
    // add+remove in one batch (two inner sets, or reentrant writes) must
    // not reach consumers as a same-element pair (net-delta invariant).
    s.Scratch.Clear()

    // ---- source journal: removals first (drop entries), then adds (create).
    let rems = s.Journal.Rems
    let remStart = rems.Count
    let adds = s.Journal.Adds
    let addStart = adds.Count
    let mutable i = 0
    // Consumed counts: see drainRefSet (the throwing mapping entry
    // survives for the next drain).
    let mutable remsDone = 0
    let mutable addsDone = 0

    try
      while i < remStart do
        let x = rems.Items[i]
        let mutable entry = Unchecked.defaultof<CollectEntry<'U>>

        if s.Inner.TryGetValue(x, &entry) then
          // Eager edge removal (Pitfall 1): unregister before dropping.
          match box entry.Node with
          | :? ISetSinkRegistry as r -> r.RemoveSetSink(entry.Sink)
          | _ -> ()

          for u in entry.Content do
            if not(s.Scratch.ContainsKey u) then
              s.Scratch[u] <- s.Global.Data.Contains u

            let struct (g2, removed) = refRemove s.Global u
            s.Global <- g2

            if removed then
              changed <- true

          s.Inner.Remove x |> ignore

        i <- i + 1
        remsDone <- i

      i <- 0

      while i < addStart do
        let x = adds.Items[i]
        let mutable existing = Unchecked.defaultof<CollectEntry<'U>>

        if not(s.Inner.TryGetValue(x, &existing)) then
          let inner = mapping x
          // Read first, register after: the view is complete, and the sink
          // sees only deltas that follow this point in time.
          let view = inner.GetValue()
          let mutable entry = CollectEntry<'U>(inner)
          // The view is a HashSet in this implementation; an
          // interface iteration would box the enumerator (measured
          // 40 B per element).
          let data = asHashSet view

          for u in data do
            if not(s.Scratch.ContainsKey u) then
              s.Scratch[u] <- s.Global.Data.Contains u

            let struct (g2, added) = refAdd s.Global u
            s.Global <- g2

            if added then
              changed <- true

            entry.Content.Add u |> ignore

          entry.Sink <- box(CollectSink<'T, 'U>(target, x))

          match box inner with
          | :? ISetSinkRegistry as r -> r.AddSetSink(entry.Sink)
          | _ -> ()

          entry.Version <- inner.Version
          s.Inner[x] <- entry

        i <- i + 1
        addsDone <- i

      // ---- entry journals. No user code runs here: the dictionary is stable.
      // Explicit struct enumerator (the KeyValue pattern allocates per
      // element; measured 88 B/entry in the version-check loop).
      let mutable de = s.Inner.GetEnumerator()

      while de.MoveNext() do
        let x = de.Current.Key
        let mutable entry = de.Current.Value

        if not entry.Journal.IsEmpty then
          let remStart = entry.Journal.Rems.Count
          let addStart = entry.Journal.Adds.Count
          let mutable k = 0

          while k < remStart do
            let u = entry.Journal.Rems.Items[k]

            if entry.Content.Remove u then
              if not(s.Scratch.ContainsKey u) then
                s.Scratch[u] <- s.Global.Data.Contains u

              let struct (g2, removed) = refRemove s.Global u
              s.Global <- g2

              if removed then
                changed <- true

            k <- k + 1

          k <- 0

          while k < addStart do
            let u = entry.Journal.Adds.Items[k]

            if entry.Content.Add u then
              if not(s.Scratch.ContainsKey u) then
                s.Scratch[u] <- s.Global.Data.Contains u

              let struct (g2, added) = refAdd s.Global u
              s.Global <- g2

              if added then
                changed <- true

            k <- k + 1

          // Compact the entry journal (no reentrancy in this pass, but
          // keep the marker pattern: entries appended during processing
          // would survive).
          let remLive = entry.Journal.Rems.Count

          if remLive > remStart then
            Array.Copy(
              entry.Journal.Rems.Items,
              remStart,
              entry.Journal.Rems.Items,
              0,
              remLive - remStart
            )

            entry.Journal.Rems.Count <- remLive - remStart
          else
            entry.Journal.Rems.Count <- 0

          let addLive = entry.Journal.Adds.Count

          if addLive > addStart then
            Array.Copy(
              entry.Journal.Adds.Items,
              addStart,
              entry.Journal.Adds.Items,
              0,
              addLive - addStart
            )

            entry.Journal.Adds.Count <- addLive - addStart
          else
            entry.Journal.Adds.Count <- 0

          s.Inner[x] <- entry
    finally
      // Compact the source journal even when the mapping threw: consumed
      // entries must not be applied twice by the next drain (double
      // subtract corrupts the refcounted union); the entry that threw
      // survives.
      let remLive = s.Journal.Rems.Count

      if remLive > remsDone then
        Array.Copy(
          s.Journal.Rems.Items,
          remsDone,
          s.Journal.Rems.Items,
          0,
          remLive - remsDone
        )

        s.Journal.Rems.Count <- remLive - remsDone
      else
        s.Journal.Rems.Count <- 0

      let addLive = s.Journal.Adds.Count

      if addLive > addsDone then
        Array.Copy(
          s.Journal.Adds.Items,
          addsDone,
          s.Journal.Adds.Items,
          0,
          addLive - addsDone
        )

        s.Journal.Adds.Count <- addLive - addsDone
      else
        s.Journal.Adds.Count <- 0

    // Net out delta: prior vs final presence per touched element (the
    // intermediate ops already moved the state; the delta describes the
    // true batch transition).
    let mutable e = s.Scratch.GetEnumerator()

    while e.MoveNext() do
      let u = e.Current.Key
      let prior = e.Current.Value
      let final = s.Global.Data.Contains u

      if prior <> final then
        if final then
          s.OutDelta.Adds <- bufferAppend s.OutDelta.Adds u
        else
          s.OutDelta.Rems <- bufferAppend s.OutDelta.Rems u

        changed <- true

    struct (s, changed)

  /// <summary>
  /// Drain a collect node and push the reduced output delta to its sinks.
  /// The byref appears
  /// only at this top-level call site (a class field address: 0 allocation).
  /// </summary>
  let inline drainCollectPush
    (target: ICollectTarget<'T, 'U>)
    (mapping: 'T -> IAdaptiveSet<'U>)
    (state: CollectState<'T, 'U> byref)
    =
    let ctx = GraphContext.Current
    let wasActive = ctx.TxActive
    ctx.TxActive <- true
    // Suspend the append-time coalescing while the journal is replayed:
    // the mapping is user code that can write to the source.
    state.Journal.InDrain[0] <- 1

    try
      let struct (s2, changed) = drainCollect target mapping state
      state <- s2

      if changed then
        pushSetDelta &state.Sinks state.OutDelta
        state.OutDelta.Clear()
    finally
      state.Journal.InDrain[0] <- 0
      ctx.TxActive <- wasActive

  /// <summary>
  /// Internal. State of a bind node over a scalar value (PLAN.md Section 7.4):
  /// one inner set, swapped when the value changes. The content set is the
  /// output (a single contribution needs no refcounts).
  /// </summary>
  [<Struct>]
  type internal BindSetState<'U when 'U: equality> =
    val mutable Version: int64
    val mutable Sinks: SinkList
    val mutable DepVersions: int64[]
    val mutable Journal: SetDelta<'U>
    val mutable Data: HashSet<'U>
    val mutable OutDelta: SetDelta<'U>

    new
      (
        version: int64,
        sinks: SinkList,
        depVersions: int64[],
        journal: SetDelta<'U>,
        data: HashSet<'U>,
        outDelta: SetDelta<'U>
      ) =
      {
        Version = version
        Sinks = sinks
        DepVersions = depVersions
        Journal = journal
        Data = data
        OutDelta = outDelta
      }

    static member Create(depCount: int) =
      BindSetState(
        0L,
        SinkList.Create(),
        Array.zeroCreate depCount,
        SetDelta<_>.Create(),
        HashSet<'U>(),
        SetDelta<_>.Create()
      )

  /// <summary>
  /// Drain a bind set node: apply the inner journal to the output. By value:
  /// the per-element hot path must not pass byrefs through byref parameters.
  /// Returns the updated state and whether the output changed.
  /// </summary>
  let inline drainBindSet
    (s: BindSetState<'U>)
    : struct (BindSetState<'U> * bool) =
    let mutable s = s
    let mutable changed = false
    let rems = s.Journal.Rems
    let remStart = rems.Count
    let mutable i = 0

    while i < remStart do
      let u = rems.Items[i]

      if s.Data.Remove u then
        s.OutDelta.Rems <- bufferAppend s.OutDelta.Rems u
        changed <- true

      i <- i + 1

    let adds = s.Journal.Adds
    let addStart = adds.Count
    i <- 0

    while i < addStart do
      let u = adds.Items[i]

      if s.Data.Add u then
        s.OutDelta.Adds <- bufferAppend s.OutDelta.Adds u
        changed <- true

      i <- i + 1

    let remLive = s.Journal.Rems.Count

    if remLive > remStart then
      Array.Copy(
        s.Journal.Rems.Items,
        remStart,
        s.Journal.Rems.Items,
        0,
        remLive - remStart
      )

      s.Journal.Rems.Count <- remLive - remStart
    else
      s.Journal.Rems.Count <- 0

    let addLive = s.Journal.Adds.Count

    if addLive > addStart then
      Array.Copy(
        s.Journal.Adds.Items,
        addStart,
        s.Journal.Adds.Items,
        0,
        addLive - addStart
      )

      s.Journal.Adds.Count <- addLive - addStart
    else
      s.Journal.Adds.Count <- 0

    struct (s, changed)

  /// <summary>
  /// Drain a bind set node and push the reduced output delta to its sinks.
  /// The byref appears
  /// only at this top-level call site (a class field address: 0 allocation).
  /// </summary>
  let inline drainBindSetPush(state: BindSetState<'U> byref) =
    let ctx = GraphContext.Current
    let wasActive = ctx.TxActive
    ctx.TxActive <- true

    try
      let struct (s2, changed) = drainBindSet state
      state <- s2

      if changed then
        pushSetDelta &state.Sinks state.OutDelta
        state.OutDelta.Clear()
    finally
      ctx.TxActive <- wasActive

  /// <summary>
  /// Internal. State of a bind map node over a scalar value (PLAN.md Section
  /// 7.4): one inner map, swapped when the value changes.
  /// </summary>
  [<Struct>]
  type internal BindMapState<'K, 'V when 'K: equality> =
    val mutable Version: int64
    val mutable Sinks: SinkList
    val mutable DepVersions: int64[]
    val mutable Journal: MapDelta<'K, 'V>
    val mutable Data: Dictionary<'K, 'V>
    val mutable OutDelta: MapDelta<'K, 'V>

    new
      (
        version: int64,
        sinks: SinkList,
        depVersions: int64[],
        journal: MapDelta<'K, 'V>,
        data: Dictionary<'K, 'V>,
        outDelta: MapDelta<'K, 'V>
      ) =
      {
        Version = version
        Sinks = sinks
        DepVersions = depVersions
        Journal = journal
        Data = data
        OutDelta = outDelta
      }

    static member Create(depCount: int) =
      BindMapState(
        0L,
        SinkList.Create(),
        Array.zeroCreate depCount,
        MapDelta<_, _>.Create(),
        Dictionary<'K, 'V>(),
        MapDelta<_, _>.Create()
      )

  /// <summary>
  /// Drain a bind map node: apply the inner journal to the output (equal
  /// values elided defensively; the boundary already emits effective deltas).
  /// By value: the per-element hot path must not pass byrefs through byref
  /// parameters. Returns the updated state and whether the output changed.
  /// </summary>
  let inline drainBindMap
    (s: BindMapState<'K, 'V>)
    : struct (BindMapState<'K, 'V> * bool) =
    let mutable s = s
    let mutable changed = false
    let rems = s.Journal.Rems
    let remStart = rems.Count
    let mutable i = 0

    while i < remStart do
      let k = rems.Items[i]

      if s.Data.Remove k then
        s.OutDelta.Rems <- bufferAppend s.OutDelta.Rems k
        changed <- true

      i <- i + 1

    let sets = s.Journal.Sets
    let setStart = sets.Count
    i <- 0

    while i < setStart do
      let struct (k, v) = sets.Items[i]
      let mutable old = Unchecked.defaultof<'V>

      if
        s.Data.TryGetValue(k, &old)
        && EqualityComparer<'V>.Default.Equals(old, v)
      then
        ()
      else
        s.Data[k] <- v
        s.OutDelta.Sets <- bufferAppend s.OutDelta.Sets (struct (k, v))
        changed <- true

      i <- i + 1

    let remLive = s.Journal.Rems.Count

    if remLive > remStart then
      Array.Copy(
        s.Journal.Rems.Items,
        remStart,
        s.Journal.Rems.Items,
        0,
        remLive - remStart
      )

      s.Journal.Rems.Count <- remLive - remStart
    else
      s.Journal.Rems.Count <- 0

    let setLive = s.Journal.Sets.Count

    if setLive > setStart then
      Array.Copy(
        s.Journal.Sets.Items,
        setStart,
        s.Journal.Sets.Items,
        0,
        setLive - setStart
      )

      s.Journal.Sets.Count <- setLive - setStart
    else
      s.Journal.Sets.Count <- 0

    struct (s, changed)

  /// <summary>
  /// Drain a bind map node and push the reduced output delta to its sinks.
  /// The byref appears
  /// only at this top-level call site (a class field address: 0 allocation).
  /// </summary>
  let inline drainBindMapPush(state: BindMapState<'K, 'V> byref) =
    let ctx = GraphContext.Current
    let wasActive = ctx.TxActive
    ctx.TxActive <- true

    try
      let struct (s2, changed) = drainBindMap state
      state <- s2

      if changed then
        pushMapDelta &state.Sinks state.OutDelta
        state.OutDelta.Clear()
    finally
      ctx.TxActive <- wasActive
