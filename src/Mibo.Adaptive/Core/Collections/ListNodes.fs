namespace Mibo.Adaptive

open System
open System.Collections.Generic

// =============================================================================
// Adaptive list nodes (docs/ALIST-DESIGN.md §3)
//
// Positional operations over ordered journals. A node applies journal
// operations sequentially to its internal ResizeArray and emits the translated
// output delta:
//   - FilterMapListNode: output position = Fenwick prefix sum over surviving
//     input positions (O(log n) per op, zero allocation, an int[] buffer);
//   - AppendListNode: absolute output position = leftCount + p for right ops
//     (O(1) per op; the journal carries a source tag to preserve cross-source
//     arrival order).
// Initial loads read the source view first and register the sink after
// (PLAN.md §7.4: no double-apply of a dirty source draining into the journal).
// =============================================================================

/// <summary>
/// A constant list: the content is fixed but computed lazily, once, at first
/// read (FDA parity: the create function runs at most once).
/// </summary>
type ConstantList<'T>([<InlineIfLambda>] create: unit -> 'T[]) =
  let value = lazy create()

  interface IAdaptiveList<'T> with
    member this.GetValue() =
      AdaptiveRuntime.addDependency (this :> IAdaptiveObject) 0L
      value.Value :> IReadOnlyList<'T>

    member _.Version = 0L

  interface IDisposable with
    member _.Dispose() = ()

/// <summary>
/// Maps every element of a list (or chooses/filters, when the mapping returns
/// <c>ValueNone</c> to drop an element).
/// </summary>
/// <remarks>
/// The output is the subsequence of input elements that survive the mapping.
/// The node keeps the input position of every output element in a sorted
/// array parallel to the output; the output position of an input element is
/// the index of its stored input position (binary search). Inserts and
/// removes shift the stored positions after the change point (tail fixup,
/// O(k), same class as the output array's own memmove). An update on a
/// non-surviving element that now passes the mapping inserts it into the
/// output; an update on a surviving element that now fails removes it (FDA
/// choose semantics, docs/ALIST-DESIGN.md §3.4).
/// </remarks>
type FilterMapListNode<'T, 'U>
  (
    source: IAdaptiveList<'T>,
    [<InlineIfLambda>] mapping: int -> 'T -> 'U voption
  ) =
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable depVersion = 0L
  let mutable initialized = false
  let mutable disposed = false
  // Output state, and the sorted input position of every output element
  // (parallel to the output).
  let mutable output = ResizeArray<'U>()
  let mutable inputPositions = ResizeArray<int>()
  let mutable inputCount = 0
  let mutable journal = ListDelta<'T>.Create()
  let mutable out = ListDelta<'U>.Create()

  member private this.Register() =
    match box source with
    | :? IListSinkRegistry as r ->
      r.AddListSink(box(this :> IListDeltaSink<'T>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? IListSinkRegistry as r ->
      r.RemoveListSink(box(this :> IListDeltaSink<'T>))
    | _ -> ()

  /// First index whose stored input position is >= p (the output position of
  /// the element at input position p when it survives).
  member private _.LowerBound(p: int) =
    let mutable lo = 0
    let mutable hi = inputPositions.Count

    while lo < hi do
      let mid = (lo + hi) >>> 1

      if inputPositions[mid] < p then lo <- mid + 1 else hi <- mid

    lo

  /// Whether the element at input position p is currently in the output.
  member private this.Contains(p: int, index: int) =
    index < inputPositions.Count && inputPositions[index] = p

  member private this.Load(snapshot: ResizeArray<'T>) =
    for i in 0 .. snapshot.Count - 1 do
      match mapping i snapshot[i] with
      | ValueSome u ->
        output.Add u
        inputPositions.Add i
      | ValueNone -> ()

    inputCount <- snapshot.Count

  member private this.EnsureInitialized() =
    if not initialized then
      // Snapshot first, register between, then load (PLAN.md §7.4).
      // The flag is set last: an exception leaves the node uninitialized.
      let snapshot = ResizeArray<'T>(source.GetValue())
      this.Register()
      this.Load(snapshot)
      depVersion <- Collections.committedVersion source
      initialized <- true

  member private this.Drain() =
    if not journal.IsEmpty then
      out.Clear()
      let ops = journal.Ops.Items
      let cnt = journal.Ops.Count
      let mutable i = 0
      // Consumed count: applied ops must never be applied again; the op
      // that threw survives for the next drain.
      let mutable opsDone = 0

      try
        while i < cnt do
          let op = ops[i]
          let p = op.Position
          let j = this.LowerBound p
          let present = this.Contains(p, j)

          match op.Kind with
          | ListOpKind.Insert ->
            match mapping p op.Value with
            | ValueSome u ->
              output.Insert(j, u)
              inputPositions.Insert(j, p)

              // Elements at input positions >= p shifted +1; the
              // tail starts after the inserted position.
              for k in j + 1 .. inputPositions.Count - 1 do
                inputPositions[k] <- inputPositions[k] + 1

              out.Ops <-
                Collections.bufferAppend
                  out.Ops
                  (ListOp(ListOpKind.Insert, j, u, 0uy))
            | ValueNone ->
              // Elements at input positions >= p shifted +1 even when
              // the new element does not survive.
              for k in j .. inputPositions.Count - 1 do
                inputPositions[k] <- inputPositions[k] + 1

            inputCount <- inputCount + 1
          | ListOpKind.Remove ->
            if present then
              output.RemoveAt j
              inputPositions.RemoveAt j

              out.Ops <-
                Collections.bufferAppend
                  out.Ops
                  (ListOp(ListOpKind.Remove, j, Unchecked.defaultof<'U>, 0uy))

            // Elements at input positions > p shifted -1, whether or
            // not the removed element was in the output.
            for k in j .. inputPositions.Count - 1 do
              inputPositions[k] <- inputPositions[k] - 1

            inputCount <- inputCount - 1
          | ListOpKind.Update ->
            match mapping p op.Value with
            | ValueSome u ->
              if present then
                output[j] <- u

                out.Ops <-
                  Collections.bufferAppend
                    out.Ops
                    (ListOp(ListOpKind.Update, j, u, 0uy))
              else
                // An update never moves other elements' input
                // positions: the new output element takes
                // position p, the tail keeps its positions.
                output.Insert(j, u)
                inputPositions.Insert(j, p)

                out.Ops <-
                  Collections.bufferAppend
                    out.Ops
                    (ListOp(ListOpKind.Insert, j, u, 0uy))
            | ValueNone ->
              if present then
                output.RemoveAt j
                inputPositions.RemoveAt j

                out.Ops <-
                  Collections.bufferAppend
                    out.Ops
                    (ListOp(ListOpKind.Remove, j, Unchecked.defaultof<'U>, 0uy))
          | _ -> ()

          i <- i + 1
          opsDone <- i
      finally
        // Compaction markers: entries appended during processing
        // (reentrant writes from the mapping) and the op that threw
        // survive for the next drain; consumed ops are dropped (double
        // removes corrupt the list).
        let live = journal.Ops.Count

        if live > opsDone then
          Array.Copy(
            journal.Ops.Items,
            opsDone,
            journal.Ops.Items,
            0,
            live - opsDone
          )

          journal.Ops.Count <- live - opsDone
        else
          journal.Ops.Count <- 0

      if not out.IsEmpty then
        version <- version + 1L
        Collections.pushListDelta &sinks out

  interface IListDeltaSink<'T> with
    member this.OnDeltas(ops: ListOp<'T>[], opCnt: int) =
      if not disposed then
        Collections.journalAppendList &journal ops opCnt
        version <- version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveList<'U> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive list has been disposed."

        this.EnsureInitialized()

        if source.Version <> depVersion then
          source.GetValue() |> ignore
          depVersion <- Collections.committedVersion source

        if not journal.IsEmpty then
          this.Drain()

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        output :> IReadOnlyList<'U>
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
        Collections.clearSinks &sinks

  interface IListSinkRegistry with
    member this.AddListSink(sink) = Collections.addSink &sinks sink

    member this.RemoveListSink(sink) = Collections.removeSink &sinks sink

/// <summary>
/// The concatenation of two lists. Ops from both sources share one journal in
/// arrival order with a source tag: cross-source order matters, because a right
/// op's absolute output position depends on <c>leftCount</c> at its application
/// point (docs/ALIST-DESIGN.md §3.4).
/// </summary>
type AppendListNode<'T>(left: IAdaptiveList<'T>, right: IAdaptiveList<'T>) =
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable leftVersion = 0L
  let mutable rightVersion = 0L
  let mutable initialized = false
  let mutable disposed = false
  let mutable output = ResizeArray<'T>()
  let mutable leftCount = 0
  // Tagged journal: Source 0 = left, 1 = right, in arrival order.
  let mutable journal: ListOp<'T>[] = Array.zeroCreate 16
  let mutable journalCount = 0
  let mutable out = ListDelta<'T>.Create()
  // One sink wrapper per source: the wrapper carries the source tag. Two
  // allocations at registration (amortized), zero on the steady state.
  let mutable leftSink: obj = null
  let mutable rightSink: obj = null

  member private this.CreateSink(source: byte) =
    { new IListDeltaSink<'T> with
        member _.OnDeltas(ops: ListOp<'T>[], opCnt: int) =
          if not disposed then
            this.JournalAppend(ops, opCnt, source)
            version <- version + 1L
            GraphContext.Default.BumpWriteGeneration()
    }

  member private this.Register() =
    leftSink <- box(this.CreateSink 0uy)
    rightSink <- box(this.CreateSink 1uy)

    match box left with
    | :? IListSinkRegistry as r -> r.AddListSink(leftSink)
    | _ -> ()

    match box right with
    | :? IListSinkRegistry as r -> r.AddListSink(rightSink)
    | _ -> ()

  member private this.Unregister() =
    if not(isNull leftSink) then
      match box left with
      | :? IListSinkRegistry as r -> r.RemoveListSink(leftSink)
      | _ -> ()

      leftSink <- null

    if not(isNull rightSink) then
      match box right with
      | :? IListSinkRegistry as r -> r.RemoveListSink(rightSink)
      | _ -> ()

      rightSink <- null

  member private this.JournalAppend
    (ops: ListOp<'T>[], opCnt: int, source: byte)
    =
    if opCnt > 0 then
      Collections.ensureCapacity &journal (journalCount + opCnt)

      for i in 0 .. opCnt - 1 do
        let op = ops[i]
        journal[journalCount] <- ListOp(op.Kind, op.Position, op.Value, source)
        journalCount <- journalCount + 1

  member private this.Load
    (leftSnapshot: ResizeArray<'T>, rightSnapshot: ResizeArray<'T>)
    =
    output <- ResizeArray<'T>(leftSnapshot.Count + rightSnapshot.Count)
    output.AddRange leftSnapshot
    output.AddRange rightSnapshot
    leftCount <- leftSnapshot.Count

  member private this.EnsureInitialized() =
    if not initialized then
      // The flag is set last: an exception leaves the node uninitialized.
      let ls = ResizeArray<'T>(left.GetValue())
      let rs = ResizeArray<'T>(right.GetValue())
      this.Register()
      this.Load(ls, rs)
      leftVersion <- Collections.committedVersion left
      rightVersion <- Collections.committedVersion right
      initialized <- true

  member private this.Drain() =
    if journalCount > 0 then
      out.Clear()
      let mutable i = 0

      while i < journalCount do
        let op = journal[i]
        let isLeft = op.Source = 0uy
        let abs = if isLeft then op.Position else leftCount + op.Position

        match op.Kind with
        | ListOpKind.Insert ->
          output.Insert(abs, op.Value)

          out.Ops <-
            Collections.bufferAppend
              out.Ops
              (ListOp(ListOpKind.Insert, abs, op.Value, 0uy))

          if isLeft then
            leftCount <- leftCount + 1
        | ListOpKind.Remove ->
          output.RemoveAt abs

          out.Ops <-
            Collections.bufferAppend
              out.Ops
              (ListOp(ListOpKind.Remove, abs, Unchecked.defaultof<'T>, 0uy))

          if isLeft then
            leftCount <- leftCount - 1
        | ListOpKind.Update ->
          output[abs] <- op.Value

          out.Ops <-
            Collections.bufferAppend
              out.Ops
              (ListOp(ListOpKind.Update, abs, op.Value, 0uy))
        | _ -> ()

        i <- i + 1

      journalCount <- 0

      if not out.IsEmpty then
        version <- version + 1L
        Collections.pushListDelta &sinks out

  interface IListDeltaSink<'T> with
    member this.OnDeltas(ops: ListOp<'T>[], opCnt: int) =
      if not disposed then
        this.JournalAppend(ops, opCnt, 1uy)
        version <- version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveList<'T> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive list has been disposed."

        this.EnsureInitialized()

        if left.Version <> leftVersion then
          left.GetValue() |> ignore
          leftVersion <- Collections.committedVersion left

        if right.Version <> rightVersion then
          right.GetValue() |> ignore
          rightVersion <- Collections.committedVersion right

        if journalCount > 0 then
          this.Drain()

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        output :> IReadOnlyList<'T>
      finally
        ctx.ReleaseOwner()

    member _.Version =
      // Dirty indicator (see MapMapNode.Version): either side with
      // unprocessed changes trips it.
      if left.Version <> leftVersion || right.Version <> rightVersion then
        version + 1L
      else
        version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.Unregister()
        Collections.clearSinks &sinks

  interface IListSinkRegistry with
    member this.AddListSink(sink) = Collections.addSink &sinks sink

    member this.RemoveListSink(sink) = Collections.removeSink &sinks sink

/// <summary>
/// An adaptive list whose content is driven by a compute function (FDA
/// <c>AList.custom</c> parity, MAPA-DESIGN §1.3). The compute receives the
/// current view and a delta builder; it appends the operations that describe
/// the change since the previous call (consuming its own event queue, for
/// example). Called on every read (poll), like <see cref="CustomSetNode"/>.
/// </summary>
type CustomListNode<'T when 'T: equality>
  ([<InlineIfLambda>] compute: IReadOnlyList<'T> -> ListDeltaBuilder<'T> -> unit)
  =
  let mutable data = ResizeArray<'T>()
  let builder = ListDeltaBuilder<'T>()
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable disposed = false

  member private this.Poll() =
    if not disposed then
      builder.Clear()
      compute (data :> IReadOnlyList<'T>) builder

      if not builder.IsEmpty then
        let out = builder.Snapshot()

        // Apply the ops to the local state in order (the delta
        // semantics: each position refers to the state as of the
        // previous op).
        let ops = out.Ops

        for i in 0 .. ops.Count - 1 do
          let op = ops.Items[i]

          match op.Kind with
          | ListOpKind.Insert -> data.Insert(op.Position, op.Value)
          | ListOpKind.Remove -> data.RemoveAt op.Position
          | _ -> data[op.Position] <- op.Value

        version <- version + 1L
        Collections.pushAndBumpList GraphContext.Current out &sinks
        builder.Clear()

  interface IAdaptiveList<'T> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive list has been disposed."

        this.Poll()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        data :> IReadOnlyList<'T>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      this.Poll()
      version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        Collections.clearSinks &sinks

  interface IListSinkRegistry with
    member this.AddListSink(sink) = Collections.addSink &sinks sink

    member this.RemoveListSink(sink) = Collections.removeSink &sinks sink

/// <summary>
/// An adaptive set of a list's elements, deduplicated (FDA
/// <c>AList.toASet</c> parity). The list deltas are converted to set deltas
/// with a per-value occurrence count: an element leaves the output only when
/// its last occurrence leaves. The mirror is aligned with the input positions.
/// </summary>
type ToSetListNode<'T when 'T: equality>(source: IAdaptiveList<'T>) =
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable depVersion = 0L
  let mutable initialized = false
  let mutable disposed = false
  let mutable output = HashSet<'T>()
  let mutable mirror = ResizeArray<'T>()
  let mutable refs = Dictionary<'T, int>()
  let mutable journal = ListDelta<'T>.Create()
  let mutable out = SetDelta<'T>.Create()

  member private this.Register() =
    match box source with
    | :? IListSinkRegistry as r ->
      r.AddListSink(box(this :> IListDeltaSink<'T>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? IListSinkRegistry as r ->
      r.RemoveListSink(box(this :> IListDeltaSink<'T>))
    | _ -> ()

  member private this.EnsureInitialized() =
    if not initialized then
      let snapshot = ResizeArray<'T>(source.GetValue())
      this.Register()

      for v in snapshot do
        mirror.Add v

        match refs.TryGetValue v with
        | true, r -> refs[v] <- r + 1
        | false, _ ->
          refs[v] <- 1
          output.Add v |> ignore

      depVersion <- Collections.committedVersion source
      initialized <- true

  member private this.Drain() =
    if not journal.IsEmpty then
      out.Clear()
      let ops = journal.Ops.Items
      let cnt = journal.Ops.Count

      for i in 0 .. cnt - 1 do
        let op = ops[i]
        let p = op.Position

        match op.Kind with
        | ListOpKind.Insert ->
          mirror.Insert(p, op.Value)

          match refs.TryGetValue op.Value with
          | true, r -> refs[op.Value] <- r + 1
          | false, _ ->
            refs[op.Value] <- 1
            output.Add op.Value |> ignore
            out.Adds <- Collections.bufferAppend out.Adds op.Value
        | ListOpKind.Remove ->
          let v = mirror[p]
          mirror.RemoveAt p
          let r = refs[v] - 1

          if r = 0 then
            refs.Remove v |> ignore
            output.Remove v |> ignore
            out.Rems <- Collections.bufferAppend out.Rems v
          else
            refs[v] <- r
        | _ -> // Update
          let old = mirror[p]

          if not(EqualityComparer<'T>.Default.Equals(old, op.Value)) then
            let r = refs[old] - 1

            if r = 0 then
              refs.Remove old |> ignore
              output.Remove old |> ignore
              out.Rems <- Collections.bufferAppend out.Rems old
            else
              refs[old] <- r

            match refs.TryGetValue op.Value with
            | true, r2 -> refs[op.Value] <- r2 + 1
            | false, _ ->
              refs[op.Value] <- 1
              output.Add op.Value |> ignore
              out.Adds <- Collections.bufferAppend out.Adds op.Value

          mirror[p] <- op.Value

      journal.Ops.Count <- 0
      version <- version + 1L
      Collections.pushAndBumpSet GraphContext.Current out &sinks

  interface IListDeltaSink<'T> with
    member this.OnDeltas(ops: ListOp<'T>[], opCnt: int) =
      if not disposed then
        Collections.journalAppendList &journal ops opCnt
        version <- version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveSet<'T> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive set has been disposed."

        this.EnsureInitialized()

        if source.Version <> depVersion then
          source.GetValue() |> ignore
          depVersion <- Collections.committedVersion source

        if not journal.IsEmpty then
          this.Drain()

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        output :> IReadOnlySet<'T>
      finally
        ctx.ReleaseOwner()

    member _.Version =
      // Dirty indicator (see MapMapNode.Version).
      if source.Version <> depVersion then
        version + 1L
      else
        version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.Unregister()
        Collections.clearSinks &sinks

  interface ISetSinkRegistry with
    member this.AddSetSink(sink) = Collections.addSink &sinks sink

    member this.RemoveSetSink(sink) = Collections.removeSink &sinks sink

/// <summary>
/// An adaptive list of a set's elements (FDA <c>AList.ofASet</c> parity, poll
/// node). The order is the set's iteration order, stable while the set does
/// not change. The set is re-read on every read (dependency registration); the
/// rebuild and the positional diff run only when the set's version moved, so
/// clean reads allocate nothing (library invariant 5).
/// </summary>
type SetToListNode<'T when 'T: equality>(source: IAdaptiveSet<'T>) =
  let mutable data = ResizeArray<'T>()
  let mutable out = ListDelta<'T>.Create()
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable depVersion = -1L
  let mutable disposed = false

  member private this.Poll() =
    if not disposed then
      let v = source.GetValue()

      if source.Version <> depVersion then
        depVersion <- source.Version
        let next = ResizeArray<'T>(v)

        if Collections.rebuildListDiff next data &out then
          version <- version + 1L
          Collections.pushAndBumpList GraphContext.Current out &sinks

        out.Clear()

  interface IAdaptiveList<'T> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive list has been disposed."

        this.Poll()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        data :> IReadOnlyList<'T>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      this.Poll()
      version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        Collections.clearSinks &sinks

  interface IListSinkRegistry with
    member this.AddListSink(sink) = Collections.addSink &sinks sink

    member this.RemoveListSink(sink) = Collections.removeSink &sinks sink

/// <summary>
/// An adaptive list bound to a scalar value (FDA <c>AList.bind</c> parity):
/// <c>mapping value</c> selects the inner list; when the value or the inner
/// list changes, the output is rebuilt and the positional diff is emitted.
/// The mapping runs only when the value changed. Rebuild-on-change semantics:
/// the deltas are full replaces (the inner list's own deltas are not streamed).
/// </summary>
type BindListNode<'T, 'U>
  (
    value: IAdaptiveValue<'T>,
    [<InlineIfLambda>] mapping: 'T -> IAdaptiveList<'U>
  ) =
  let mutable data = ResizeArray<'U>()
  let mutable out = ListDelta<'U>.Create()
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable hasInner = false
  let mutable current: 'T = Unchecked.defaultof<'T>
  let mutable inner: IAdaptiveList<'U> = Unchecked.defaultof<IAdaptiveList<'U>>
  let mutable lastValueVersion = -1L
  let mutable lastInnerVersion = -1L
  let mutable disposed = false

  member private this.Poll() =
    if not disposed then
      let v = value.GetValue()

      if
        not hasInner || not(EqualityComparer<'T>.Default.Equals(v, current))
      then
        current <- v
        inner <- mapping v
        hasInner <- true

      let innerView = inner.GetValue()

      if
        value.Version <> lastValueVersion || inner.Version <> lastInnerVersion
      then
        lastValueVersion <- Collections.committedVersion value
        lastInnerVersion <- Collections.committedVersion inner
        let next = ResizeArray<'U>(innerView)

        if Collections.rebuildListDiff next data &out then
          version <- version + 1L
          Collections.pushAndBumpList GraphContext.Current out &sinks

        out.Clear()

  interface IAdaptiveList<'U> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive list has been disposed."

        this.Poll()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        data :> IReadOnlyList<'U>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      this.Poll()
      version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        Collections.clearSinks &sinks

  interface IListSinkRegistry with
    member this.AddListSink(sink) = Collections.addSink &sinks sink

    member this.RemoveListSink(sink) = Collections.removeSink &sinks sink

/// <summary>
/// Concatenates a fixed sequence of lists (FDA <c>AList.concat</c> parity,
/// poll node). Every read re-reads all inner lists (dependency registration);
/// the rebuild and the positional diff run only when any inner list's version
/// moved, so clean reads allocate nothing (library invariant 5).
/// </summary>
type ConcatListNode<'T>(sources: IAdaptiveList<'T>[]) =
  let mutable data = ResizeArray<'T>()
  let mutable out = ListDelta<'T>.Create()
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable depVersions = Array.init sources.Length (fun _ -> -1L)
  let mutable disposed = false

  member private this.Poll() =
    if not disposed then
      let mutable moved = false

      for i = 0 to sources.Length - 1 do
        let s = sources[i]
        let _ = s.GetValue()

        if s.Version <> depVersions[i] then
          depVersions[i] <- Collections.committedVersion s
          moved <- true

      if moved then
        let next = ResizeArray<'T>()

        for s in sources do
          let view = s.GetValue()

          for i in 0 .. view.Count - 1 do
            next.Add view[i]

        if Collections.rebuildListDiff next data &out then
          version <- version + 1L
          Collections.pushAndBumpList GraphContext.Current out &sinks

        out.Clear()

  interface IAdaptiveList<'T> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive list has been disposed."

        this.Poll()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        data :> IReadOnlyList<'T>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      this.Poll()
      version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        Collections.clearSinks &sinks

  interface IListSinkRegistry with
    member this.AddListSink(sink) = Collections.addSink &sinks sink

    member this.RemoveListSink(sink) = Collections.removeSink &sinks sink

/// <summary>
/// An adaptive list over an adaptive value of a sequence (FDA
/// <c>AList.ofAVal</c> parity). Every change of the value replaces the whole
/// state and emits the positional diff as the delta. Poll model: the value is
/// re-read on every read; the diff runs only when its version moved.
/// </summary>
type OfAvalListNode<'T, 'S when 'S :> seq<'T>>(value: IAdaptiveValue<'S>) =
  let mutable data = ResizeArray<'T>()
  let mutable out = ListDelta<'T>.Create()
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable depVersion = -1L
  let mutable disposed = false

  member private this.Poll() =
    if not disposed then
      let v = value.GetValue()

      if value.Version <> depVersion then
        depVersion <- value.Version
        let next = ResizeArray<'T>(v)

        if Collections.rebuildListDiff next data &out then
          version <- version + 1L
          Collections.pushAndBumpList GraphContext.Current out &sinks

        out.Clear()

  interface IAdaptiveList<'T> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive list has been disposed."

        this.Poll()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        data :> IReadOnlyList<'T>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      this.Poll()
      version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        Collections.clearSinks &sinks

  interface IListSinkRegistry with
    member this.AddListSink(sink) = Collections.addSink &sinks sink

    member this.RemoveListSink(sink) = Collections.removeSink &sinks sink

/// <summary>
/// A poll node that rebuilds its output from the source and emits the
/// positional diff (the gap-sheet poll-node strategy for rev, sort, pairwise).
/// The source is re-read on every read. Rebuild-on-every-read is deliberate:
/// the <c>build</c> function may read additional adaptive inputs (the
/// <c>subA</c>/<c>takeA</c>/<c>skipA</c> bounds do), so a source-version gate
/// would be unsound — a change to an external input would be missed. Nodes
/// whose output is a pure function of the source only (e.g. the dedicated
/// <c>SubListNode</c>) use the gated pattern instead, holding invariant 5 on
/// clean reads.
/// </summary>
type PollListSourceNode<'T, 'U>
  (
    source: IAdaptiveList<'T>,
    [<InlineIfLambda>] build: IReadOnlyList<'T> -> ResizeArray<'U>
  ) =
  let mutable data = ResizeArray<'U>()
  let mutable out = ListDelta<'U>.Create()
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable disposed = false

  member private this.Poll() =
    if not disposed then
      let next = build(source.GetValue())

      if Collections.rebuildListDiff next data &out then
        version <- version + 1L
        Collections.pushAndBumpList GraphContext.Current out &sinks

      out.Clear()

  interface IAdaptiveList<'U> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive list has been disposed."

        this.Poll()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        data :> IReadOnlyList<'U>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      this.Poll()
      version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        Collections.clearSinks &sinks

  interface IListSinkRegistry with
    member this.AddListSink(sink) = Collections.addSink &sinks sink

    member this.RemoveListSink(sink) = Collections.removeSink &sinks sink

/// <summary>
/// The window <c>[offset, offset + count)</c> of a list with adaptive bounds
/// (FDA <c>AList.subA</c> parity, poll node). All three dependencies — the
/// source list and the two bound scalars — are re-read on every read
/// (dependency registration); the window and the positional diff run only
/// when any of their versions moved, so clean reads allocate nothing and run
/// no user code (library invariant 5). The <c>build</c> route of
/// <c>PollListSourceNode</c> cannot gate: its closure may read additional
/// adaptive inputs, so this node exists with the extra versions tracked
/// explicitly.
/// </summary>
type SubListNode<'T>
  (
    source: IAdaptiveList<'T>,
    offset: IAdaptiveValue<int>,
    count: IAdaptiveValue<int>
  ) =
  let mutable data = ResizeArray<'T>()
  let mutable out = ListDelta<'T>.Create()
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable lastSourceVersion = -1L
  let mutable lastOffsetVersion = -1L
  let mutable lastCountVersion = -1L
  let mutable disposed = false

  member private this.Poll() =
    if not disposed then
      let view = source.GetValue()
      let o = max 0 (offset.GetValue())
      let c = max 0 (count.GetValue())

      if
        source.Version <> lastSourceVersion
        || offset.Version <> lastOffsetVersion
        || count.Version <> lastCountVersion
      then
        lastSourceVersion <- Collections.committedVersion source
        lastOffsetVersion <- Collections.committedVersion offset
        lastCountVersion <- Collections.committedVersion count
        let start = min o view.Count
        let n = min c (view.Count - start)
        let next = ResizeArray<'T>(n)

        for i in start .. start + n - 1 do
          next.Add view[i]

        if Collections.rebuildListDiff next data &out then
          version <- version + 1L
          Collections.pushAndBumpList GraphContext.Current out &sinks

        out.Clear()

  interface IAdaptiveList<'T> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive list has been disposed."

        this.Poll()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        data :> IReadOnlyList<'T>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      this.Poll()
      version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        Collections.clearSinks &sinks

  interface IListSinkRegistry with
    member this.AddListSink(sink) = Collections.addSink &sinks sink

    member this.RemoveListSink(sink) = Collections.removeSink &sinks sink

/// <summary>
/// A stable sort node (FDA <c>AList.sortWith</c> parity, poll model). The
/// keys are computed with their input positions (the <c>sortByi</c> mapping
/// contract); the sort is stable by position. The source is re-read on every
/// read (dependency registration); keys, sort and diff run only when the
/// source's version moved, so clean reads allocate nothing and run no user
/// code (library invariant 5). The <c>keyMapping</c>/<c>comparer</c> must be
/// pure functions of the element — external adaptive reads are not tracked.
/// </summary>
type SortListNode<'T, 'K>
  (
    source: IAdaptiveList<'T>,
    [<InlineIfLambda>] keyMapping: int -> 'T -> 'K,
    [<InlineIfLambda>] comparer: 'K -> 'K -> int
  ) =
  let mutable data = ResizeArray<'T>()
  let mutable out = ListDelta<'T>.Create()
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable depVersion = -1L
  let mutable disposed = false

  member private this.Poll() =
    if not disposed then
      let view = source.GetValue()

      if source.Version <> depVersion then
        depVersion <- source.Version
        let n = view.Count
        let keys = Array.zeroCreate n
        let next = ResizeArray<'T>(n)

        for i in 0 .. n - 1 do
          next.Add view[i]
          keys[i] <- keyMapping i view[i]

        // Stable decorate-sort-undecorate: equal keys keep the input order.
        let order = Array.init n id

        Array.Sort(
          order,
          Comparison(fun a b ->
            let c = comparer keys[a] keys[b]
            if c <> 0 then c else compare a b)
        )

        let sorted = Array.zeroCreate n

        for i in 0 .. n - 1 do
          sorted[i] <- next[order[i]]

        let rebuilt = ResizeArray<'T>(sorted)

        if Collections.rebuildListDiff rebuilt data &out then
          version <- version + 1L
          Collections.pushAndBumpList GraphContext.Current out &sinks

        out.Clear()

  interface IAdaptiveList<'T> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive list has been disposed."

        this.Poll()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        data :> IReadOnlyList<'T>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      this.Poll()
      version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        Collections.clearSinks &sinks

  interface IListSinkRegistry with
    member this.AddListSink(sink) = Collections.addSink &sinks sink

    member this.RemoveListSink(sink) = Collections.removeSink &sinks sink

/// <summary>
/// Maps every element, disposing the mapped value when the element leaves its
/// position (FDA <c>AList.mapUsei</c> parity; the index is the <c>int</c>
/// input position, the positional deviation). The output is 1:1 with the
/// input; a removed or updated element disposes its mapped value. Disposing
/// the node disposes all live mapped values and clears the output.
/// </summary>
type MapUseListNode<'T, 'W when 'W: equality and 'W :> IDisposable>
  (source: IAdaptiveList<'T>, [<InlineIfLambda>] mapping: int -> 'T -> 'W) =
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable depVersion = 0L
  let mutable initialized = false
  let mutable disposed = false
  let mutable output = ResizeArray<'W>()
  let mutable inputCount = 0
  let mutable journal = ListDelta<'T>.Create()
  let mutable out = ListDelta<'W>.Create()

  member private this.Register() =
    match box source with
    | :? IListSinkRegistry as r ->
      r.AddListSink(box(this :> IListDeltaSink<'T>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? IListSinkRegistry as r ->
      r.RemoveListSink(box(this :> IListDeltaSink<'T>))
    | _ -> ()

  member private this.EnsureInitialized() =
    if not initialized then
      let snapshot = ResizeArray<'T>(source.GetValue())
      this.Register()

      for i in 0 .. snapshot.Count - 1 do
        output.Add(mapping i snapshot[i])

      inputCount <- snapshot.Count
      depVersion <- Collections.committedVersion source
      initialized <- true

  member private this.Drain() =
    if not journal.IsEmpty then
      out.Clear()
      let ops = journal.Ops.Items
      let cnt = journal.Ops.Count

      for i in 0 .. cnt - 1 do
        let op = ops[i]
        let p = op.Position

        match op.Kind with
        | ListOpKind.Insert ->
          let w = mapping p op.Value
          output.Insert(p, w)

          out.Ops <-
            Collections.bufferAppend
              out.Ops
              (ListOp(ListOpKind.Insert, p, w, 0uy))

          inputCount <- inputCount + 1
        | ListOpKind.Remove ->
          output[p].Dispose()
          output.RemoveAt p

          out.Ops <-
            Collections.bufferAppend
              out.Ops
              (ListOp(ListOpKind.Remove, p, Unchecked.defaultof<'W>, 0uy))

          inputCount <- inputCount - 1
        | _ -> // Update: dispose the old mapped value, keep the position.
          let w = mapping p op.Value
          output[p].Dispose()
          output[p] <- w

          out.Ops <-
            Collections.bufferAppend
              out.Ops
              (ListOp(ListOpKind.Update, p, w, 0uy))

      journal.Ops.Count <- 0
      version <- version + 1L
      Collections.pushAndBumpList GraphContext.Current out &sinks

  interface IListDeltaSink<'T> with
    member this.OnDeltas(ops: ListOp<'T>[], opCnt: int) =
      if not disposed then
        Collections.journalAppendList &journal ops opCnt
        version <- version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveList<'W> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive list has been disposed."

        this.EnsureInitialized()

        if source.Version <> depVersion then
          source.GetValue() |> ignore
          depVersion <- Collections.committedVersion source

        if not journal.IsEmpty then
          this.Drain()

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        output :> IReadOnlyList<'W>
      finally
        ctx.ReleaseOwner()

    member _.Version =
      // Dirty indicator (see MapMapNode.Version).
      if source.Version <> depVersion then
        version + 1L
      else
        version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.Unregister()
        Collections.clearSinks &sinks

        for i in 0 .. output.Count - 1 do
          output[i].Dispose()

        output.Clear()

  interface IListSinkRegistry with
    member this.AddListSink(sink) = Collections.addSink &sinks sink

    member this.RemoveListSink(sink) = Collections.removeSink &sinks sink

// =============================================================================
// Scalar escape hatches: positional lookup and incremental count.
//
// Lazy scalar nodes (see the map escapes in MapNodes.fs): they register
// nothing, writes only bump the source version (O(1)), and the node
// re-syncs at its next read. A watched position changes when the element
// there actually moved (an insert/remove at p &lt;= i shifts it, an update at
// p = i replaces it); the value is re-read lazily on the next read (O(1),
// pull-lazy). Ops on unrelated positions cost the node and its consumers
// nothing — and the write itself costs nothing either.
//
// Pull-only protocol: no parent edges, no write-time marking, no delivery
// (see SetNodes.fs for the full contract). For a direct changeable source
// the re-sync is O(1); pull-lazy derived sources move their version at
// write time, so the dirty indicator (version + 1) makes the consumer
// re-read exactly once (the documented depth-2 rule).
// =============================================================================

/// <summary>
/// A positional lookup over an adaptive list (the node behind
/// <c>AList.tryAt</c>/<c>AList.tryGet</c>/<c>AList.tryFirst</c>). Registers
/// nothing; the position is re-read at the next read after a write, and the
/// version advances only when the element at the watched position actually
/// changed (a shift that lands the same value bumps nothing). The value is
/// re-read from the source view (O(1)).
/// </summary>
type ListLookupNode<'T>(source: IAdaptiveList<'T>, index: int) =
  let mutable version = 0L
  let mutable depVersion = 0L
  let mutable initialized = false
  let mutable disposed = false
  let mutable value = ValueNone

  member private this.ReadView() =
    let view = source.GetValue()

    value <-
      if index >= 0 && index < view.Count then
        ValueSome view[index]
      else
        ValueNone

  /// Lazy re-sync (see the map escapes in MapNodes.fs): re-read the
  /// position and bump only when the element there actually moved.
  member private this.Resync() =
    let before = value
    this.ReadView()

    if not(EqualityComparer<'T voption>.Default.Equals(before, value)) then
      version <- version + 1L

    depVersion <- Collections.committedVersion source

  interface IAdaptiveValue<'T voption> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive value has been disposed."

        // Internal source reads (the init snapshot, the re-sync
        // below) are machinery of this node, not dependencies
        // of the consumer: suppress collection so the caller's frame
        // sees only this node. Without this, the whole-list
        // dependency leaks into the consumer's frame and defeats the
        // per-position gate.
        let collector = ctx.Collector
        let wasCollecting = ctx.CollectorActive

        // A throwaway frame, popped and discarded below: toggling
        // CollectorActive instead is NOT safe — a nested evaluation
        // inside the reads would reset the collector out from under
        // the caller's frame.
        if wasCollecting then
          collector.PushFrame()

        try
          if not initialized then
            // Snapshot first: there is no registration anymore.
            this.Resync()
            initialized <- true
          elif source.Version <> depVersion then
            this.Resync()
        finally
          if wasCollecting then
            collector.PopFrame() |> ignore

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        value
      finally
        ctx.ReleaseOwner()

    member this.Version =
      // Dirty indicator (the ExternalValueNode pattern): while the
      // source has unprocessed changes, report version + 1 so
      // version-checking consumers re-read exactly once; the re-sync
      // at GetValue applies the gate and decides the real version.
      if source.Version <> depVersion then
        version + 1L
      else
        version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true

/// <summary>
/// A last-element lookup over an adaptive list (the node behind
/// <c>AList.tryLast</c>). Registers nothing; the last element is re-read at
/// the next read after a write, and the version advances only when the last
/// element actually changed: an append, a remove of the last element, or an
/// update of the last element. An insert or remove elsewhere shifts
/// positions but leaves the last element untouched, so it bumps nothing.
/// The value is re-read from the source view (O(1)).
/// </summary>
type ListLastNode<'T>(source: IAdaptiveList<'T>) =
  let mutable version = 0L
  let mutable depVersion = 0L
  let mutable initialized = false
  let mutable disposed = false
  let mutable value = ValueNone

  member private this.ReadView() =
    let view = source.GetValue()

    value <-
      if view.Count > 0 then
        ValueSome view[view.Count - 1]
      else
        ValueNone

  /// Lazy re-sync (see ListLookupNode): re-read the last element and bump
  /// only when it actually moved.
  member private this.Resync() =
    let before = value
    this.ReadView()

    if not(EqualityComparer<'T voption>.Default.Equals(before, value)) then
      version <- version + 1L

    depVersion <- Collections.committedVersion source

  interface IAdaptiveValue<'T voption> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive value has been disposed."

        // Internal source reads are machinery of this node, not
        // dependencies of the consumer (see ListLookupNode.GetValue).
        let collector = ctx.Collector
        let wasCollecting = ctx.CollectorActive

        if wasCollecting then
          collector.PushFrame()

        try
          if not initialized then
            // Snapshot first: there is no registration anymore.
            this.Resync()
            initialized <- true
          elif source.Version <> depVersion then
            this.Resync()
        finally
          if wasCollecting then
            collector.PopFrame() |> ignore

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        value
      finally
        ctx.ReleaseOwner()

    member this.Version =
      // Dirty indicator (see ListLookupNode.Version).
      if source.Version <> depVersion then
        version + 1L
      else
        version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true

/// <summary>
/// A count over an adaptive list, projected through <paramref name="view"/>
/// (the node behind <c>AList.count</c> with <c>id</c> and <c>AList.isEmpty</c>
/// with <c>fun c -&gt; c = 0</c>). Registers nothing: the count is re-read at
/// the next read after a write; the version advances only when the projected
/// output changed, so an update (no count change) or a count change that the
/// projection collapses (2 -&gt; 3 under isEmpty) costs this node and its
/// consumers nothing.
/// </summary>
/// <remarks>
/// The view projection runs at re-sync time: keep it cheap (the built-in
/// uses are <c>id</c> and <c>fun c -&gt; c = 0</c>).
/// </remarks>
type ListCountNode<'T, 'Out>
  (source: IAdaptiveList<'T>, [<InlineIfLambda>] view: int -> 'Out) =
  let mutable version = 0L
  let mutable depVersion = 0L
  let mutable initialized = false
  let mutable disposed = false
  let mutable out = Unchecked.defaultof<'Out>

  /// Lazy re-sync (see ListLookupNode): re-read the count, project, and
  /// bump only when the projected output moved.
  member private this.Resync() =
    let before = out
    let nextOut = view(source.GetValue().Count)

    if not(EqualityComparer<'Out>.Default.Equals(before, nextOut)) then
      out <- nextOut
      version <- version + 1L

    depVersion <- Collections.committedVersion source

  interface IAdaptiveValue<'Out> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive value has been disposed."

        // Internal source reads are machinery of this node, not
        // dependencies of the consumer (see ListLookupNode.GetValue).
        let collector = ctx.Collector
        let wasCollecting = ctx.CollectorActive

        if wasCollecting then
          collector.PushFrame()

        try
          if not initialized then
            // Snapshot first: there is no registration anymore.
            this.Resync()
            initialized <- true
          elif source.Version <> depVersion then
            this.Resync()
        finally
          if wasCollecting then
            collector.PopFrame() |> ignore

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        out
      finally
        ctx.ReleaseOwner()

    member this.Version =
      // Dirty indicator (see ListLookupNode.Version).
      if source.Version <> depVersion then
        version + 1L
      else
        version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
