namespace Mibo.Adaptive

open System
open System.Collections.Generic
open System.Collections.Frozen

// =============================================================================
// Public API (PLAN.md Section 6.9)
//
// `force` is the materialization point: it drains and returns an immutable
// FrozenSet/FrozenDictionary that the library never touches again. `getValue`
// returns a transient view for computations. `toSet`/`toMap` materialize the
// F# Set/Map counterparts (sorted, structural equality) for interop.
// =============================================================================

/// <summary>Operations on adaptive sets.</summary>
module ASet =
  /// <summary>An empty adaptive set (FDA <c>ASet.empty</c> parity).</summary>
  let empty<'T> : aset<'T> = new ConstantSet<'T>(fun () -> FrozenSet<'T>.Empty)

  /// <summary>An adaptive set over fixed, immutable items.</summary>
  let inline ofSeq(items: seq<'T>) : aset<'T> =
    new ConstantSet<'T>(fun () -> items.ToFrozenSet())

  /// <summary>An adaptive set over a fixed array.</summary>
  let inline ofArray(items: 'T[]) : aset<'T> =
    new ConstantSet<'T>(fun () -> items.ToFrozenSet())

  /// <summary>An adaptive set over a fixed list.</summary>
  let inline ofList(items: 'T list) : aset<'T> =
    new ConstantSet<'T>(fun () -> items.ToFrozenSet())

  /// <summary>An adaptive set over a fixed HashSet.</summary>
  let inline ofHashSet(items: HashSet<'T>) : aset<'T> =
    new ConstantSet<'T>(fun () -> items.ToFrozenSet())

  /// <summary>
  /// An adaptive set whose content is fixed but computed lazily, once, at
  /// first read. Enables self-referential definitions (FDA parity: the
  /// create function runs at most once).
  /// </summary>
  let inline constant
    ([<InlineIfLambda>] create: unit -> HashSet<'T>)
    : aset<'T> =
    new ConstantSet<'T>(fun () -> create().ToFrozenSet())

  /// <summary>Alias of <see cref="constant"/> (FDA parity: delay is constant).</summary>
  let inline delay([<InlineIfLambda>] create: unit -> HashSet<'T>) : aset<'T> =
    constant create

  /// <summary>Maps every element of the set.</summary>
  let inline map ([<InlineIfLambda>] f: 'T -> 'U) (set: aset<'T>) : aset<'U> =
    new MapSetNode<'T, 'U>(set, fun x -> ValueSome(f x))

  /// <summary>Maps every element, keeping only the ones the mapping returns a value for.</summary>
  let inline choose
    ([<InlineIfLambda>] f: 'T -> 'U option)
    (set: aset<'T>)
    : aset<'U> =
    new MapSetNode<'T, 'U>(set, f >> Option.toValueOption)

  /// <summary>Maps every element, keeping only the ones the mapping returns a value for.</summary>
  let inline chooseV
    ([<InlineIfLambda>] f: 'T -> 'U voption)
    (set: aset<'T>)
    : aset<'U> =
    new MapSetNode<'T, 'U>(set, f)

  /// <summary>Keeps the elements that satisfy the predicate.</summary>
  let inline filter
    ([<InlineIfLambda>] predicate: 'T -> bool)
    (set: aset<'T>)
    : aset<'T> =
    new FilterSetNode<'T>(set, predicate)

  /// <summary>
  /// Adaptively maps every element of the set to an adaptive value (FDA
  /// <c>ASet.mapA</c> parity). The output follows the aval returned for
  /// each element; writes to the avals deliver targeted deltas.
  /// </summary>
  let inline mapA
    ([<InlineIfLambda>] mapping: 'T -> aval<'U>)
    (set: aset<'T>)
    : aset<'U> =
    new ElementSetNode<'T, 'U>(set, fun x -> AVal.map ValueSome (mapping x))

  /// <summary>
  /// Adaptively maps every element of the set to an adaptive value, keeping
  /// only the elements whose aval holds <c>Some</c> (FDA
  /// <c>ASet.chooseA</c> parity).
  /// </summary>
  let inline chooseA
    ([<InlineIfLambda>] mapping: 'T -> aval<'U option>)
    (set: aset<'T>)
    : aset<'U> =
    new ElementSetNode<'T, 'U>(
      set,
      fun x -> AVal.map Option.toValueOption (mapping x)
    )

  /// <summary>
  /// The voption counterpart of <see cref="chooseA"/>: the mapping returns
  /// <c>aval&lt;'U voption&gt;</c> directly, without the option-to-voption
  /// wrapper node per element (the no-allocation path).
  /// </summary>
  let inline chooseAV
    ([<InlineIfLambda>] mapping: 'T -> aval<'U voption>)
    (set: aset<'T>)
    : aset<'U> =
    new ElementSetNode<'T, 'U>(set, mapping)

  /// <summary>
  /// Adaptively keeps the elements whose predicate aval holds <c>true</c>
  /// (FDA <c>ASet.filterA</c> parity).
  /// </summary>
  let inline filterA
    ([<InlineIfLambda>] predicate: 'T -> aval<bool>)
    (set: aset<'T>)
    : aset<'T> =
    new ElementSetNode<'T, 'T>(
      set,
      fun x ->
        AVal.map (fun b -> if b then ValueSome x else ValueNone) (predicate x)
    )

  /// <summary>The union of two sets.</summary>
  let inline union (left: aset<'T>) (right: aset<'T>) : aset<'T> =
    new UnionSetNode<'T>(left, right)

  /// <summary>
  /// The union of all given sets. Deviation from FDA: FDA's
  /// <c>unionMany</c> takes an adaptive set of sets (the dynamic form lands
  /// with <c>bind</c>/<c>collect</c>, PLAN.md 7.4); this overload takes a
  /// static sequence and folds <see cref="union"/>.
  /// </summary>
  let unionMany(sets: seq<aset<'T>>) : aset<'T> =
    let mutable acc = Unchecked.defaultof<aset<'T>>
    let mutable first = true

    for s in sets do
      if first then
        acc <- s
        first <- false
      else
        acc <- new UnionSetNode<'T>(acc, s)

    if first then
      new ConstantSet<'T>(fun () -> FrozenSet<'T>.Empty)
    else
      acc

  /// <summary>
  /// Adaptively maps over the given set and unions all resulting sets (FDA
  /// <c>ASet.collect</c> parity; PLAN.md Section 7.4). The output is the
  /// refcounted union: an element contributed by several inner sets
  /// disappears only when the last contributor drops it. This is also the
  /// dynamic <c>unionMany</c>: <c>ASet.collect id</c> over
  /// <c>IAdaptiveSet&lt;IAdaptiveSet&lt;'T&gt;&gt;</c>.
  /// </summary>
  /// <example>
  /// <code>
  /// // group numbers by parity: the output follows both the outer set and
  /// // the inner sets.
  /// let buckets = CSet.empty&lt;int&gt;
  /// let odds = CSet.empty&lt;int&gt;
  /// let evens = CSet.empty&lt;int&gt;
  /// let all = ASet.collect (fun b -&gt; if b % 2 = 0 then evens else odds) (CSet.value buckets)
  /// CSet.add 1 buckets
  /// CSet.add 2 buckets
  /// CSet.add 3 odds
  /// // all is now {3}
  /// </code>
  /// </example>
  let inline collect
    ([<InlineIfLambda>] mapping: 'T -> aset<'U>)
    (set: aset<'T>)
    : aset<'U> =
    new CollectSetNode<'T, 'U>(set, mapping)

  /// <summary>
  /// Flattens the set by statically expanding each element to a sequence
  /// (FDA <c>ASet.collect'</c> parity). The expansion runs per source
  /// element change; the inner sequences are constant.
  /// </summary>
  let inline collect'
    ([<InlineIfLambda>] mapping: 'T -> seq<'U>)
    (set: aset<'T>)
    : aset<'U> =
    collect (mapping >> ofSeq) set

  /// <summary>
  /// Maps every element, disposing the mapped value when its last source
  /// occurrence leaves (FDA <c>ASet.mapUse</c> parity). The mapped values
  /// are stable (the mapping runs once per source element). Disposing the
  /// returned disposable disposes all live mapped values and clears the
  /// output set.
  /// </summary>
  /// <example>
  /// <code>
  /// let src = CSet.ofSeq [ 1; 2 ]
  /// let cleanup, mapped = src |> ASet.mapUse (fun id -&gt; Resource(id))
  /// CSet.remove 1 src          // the resource for 1 is disposed
  /// cleanup.Dispose()          // all remaining resources are disposed
  /// </code>
  /// </example>
  let inline mapUse
    ([<InlineIfLambda>] mapping: 'A -> 'B)
    (set: aset<'A>)
    : IDisposable * aset<'B> =
    let node = new MapUseSetNode<'A, 'B>(set, mapping)
    (node :> IDisposable, node :> aset<'B>)

  /// <summary>The elements of the left set that are not in the right set.</summary>
  let inline difference (left: aset<'T>) (right: aset<'T>) : aset<'T> =
    new TwoSourceSetNode<'T>(TwoSetOp.Difference, left, right)

  /// <summary>The elements present in both sets.</summary>
  let inline intersect (left: aset<'T>) (right: aset<'T>) : aset<'T> =
    new TwoSourceSetNode<'T>(TwoSetOp.Intersect, left, right)

  /// <summary>The symmetric difference: elements present in exactly one set.</summary>
  let inline xor (left: aset<'T>) (right: aset<'T>) : aset<'T> =
    new TwoSourceSetNode<'T>(TwoSetOp.Xor, left, right)

  /// <summary>
  /// An adaptive set over an adaptive value of a sequence. Every change of
  /// the value replaces the whole state and emits the diff as the delta
  /// (FDA <c>ASet.ofAVal</c> parity; the value carries no deltas).
  /// </summary>
  let inline ofAVal<'T, 'S when 'T: equality and 'S :> seq<'T>>
    (value: aval<'S>)
    : aset<'T> =
    new OfAvalSetNode<'T, 'S>(value)

  /// <summary>
  /// Adaptively maps over the given value and returns the resulting set (FDA
  /// <c>ASet.bind</c> parity; PLAN.md Section 7.4). When the value changes,
  /// the whole inner set is swapped: the old content is removed, the inner
  /// sink is unregistered eagerly, and <c>mapping</c> selects the new inner
  /// set. The inner set's own changes propagate while it is bound.
  /// </summary>
  /// <example>
  /// <code>
  /// // a set that follows the currently selected bucket
  /// let selected = CVal.create 0
  /// let buckets = [| CSet.empty&lt;int&gt;; CSet.empty&lt;int&gt; |]
  /// let visible = ASet.bind (fun i -&gt; buckets[i]) (CVal.value selected)
  /// CSet.add 7 (buckets[0])
  /// CVal.setValue 1 selected
  /// // visible is now empty (bucket 1), and bucket 0's later changes do not leak
  /// </code>
  /// </example>
  let inline bind
    ([<InlineIfLambda>] mapping: 'T -> aset<'U>)
    (value: aval<'T>)
    : aset<'U> =
    new BindSetNode<'T, 'U>(value, mapping)

  /// <summary>
  /// An adaptive numeric range (FDA <c>ASet.range</c> parity). The set is
  /// rebuilt when either bound changes; the bounds are inclusive.
  /// </summary>
  let inline range (min: aval< ^T >) (max: aval< ^T >) : aset< ^T > =
    ofAVal(AVal.map2 (fun lo hi -> seq { lo..hi }) min max)

  /// <summary>
  /// Adaptively maps over the two values and returns the resulting set (FDA
  /// <c>ASet.bind2</c> parity). When either value changes, the whole inner
  /// set is swapped (the bind semantics). Composed as one bind over the
  /// mapped pair (the FDA approach: nested binds would miss the inner
  /// bind's swap, which signals by version only, not by delta).
  /// </summary>
  let inline bind2
    ([<InlineIfLambda>] mapping: 'A -> 'B -> aset<'C>)
    (a: aval<'A>)
    (b: aval<'B>)
    : aset<'C> =
    bind (fun (av, bv) -> mapping av bv) (AVal.map2 (fun av bv -> (av, bv)) a b)

  /// <summary>
  /// Adaptively maps over the three values and returns the resulting set
  /// (FDA <c>ASet.bind3</c> parity). When any value changes, the whole
  /// inner set is swapped (the bind semantics).
  /// </summary>
  let inline bind3
    ([<InlineIfLambda>] mapping: 'A -> 'B -> 'C -> aset<'D>)
    (a: aval<'A>)
    (b: aval<'B>)
    (c: aval<'C>)
    : aset<'D> =
    bind
      (fun (av, bv, cv) -> mapping av bv cv)
      (AVal.map3 (fun av bv cv -> (av, bv, cv)) a b c)

  /// <summary>
  /// An adaptive set over an external reader function. The reader is called
  /// on every read (poll); the node diffs the result against its state and
  /// emits the diff as the delta. Pull-based: nothing tells this node when
  /// the underlying data changes, so consumers must re-read it (FDA
  /// <c>ASet.ofReader</c> is pull-based too).
  /// </summary>
  let inline ofReader
    ([<InlineIfLambda>] reader: unit -> HashSet<'T>)
    : aset<'T> =
    new ReaderSetNode<'T>(reader)

  /// <summary>
  /// An adaptive set driven by a compute function (FDA <c>ASet.custom</c>
  /// parity, pull model). The compute receives the current view and a delta
  /// builder; it appends the operations that describe the change since the
  /// previous call (for example, consuming its own event queue).
  /// </summary>
  let inline custom
    ([<InlineIfLambda>] compute: IReadOnlySet<'T> -> SetDeltaBuilder<'T> -> unit)
    : aset<'T> =
    new CustomSetNode<'T>(compute)

  /// <summary>
  /// Creates an adaptive set from an external snapshot function and an
  /// invalidate handle (FDA <c>ASet.ofExternal</c> parity, MAPA-DESIGN §1.1).
  /// The snapshot runs at most once per invalidate, on the next read, and is
  /// diffed against the previous snapshot; not invalidated → reads are
  /// O(1) and allocate nothing. The handle is O(1) to call and thread-safe
  /// (a foreign-thread call is posted to the owner context and applied at
  /// the next graph operation).
  /// </summary>
  /// <example>
  /// <code>
  /// let mutable current = HashSet [ 1; 2; 3 ]
  /// let set, invalidate = ASet.ofExternal (fun () -&gt; current :&gt; IReadOnlySet&lt;_&gt;)
  /// current &lt;- HashSet [ 1; 3 ]
  /// invalidate ()
  /// let forced = ASet.force set   // { 1; 3 }
  /// </code>
  /// </example>
  let inline ofExternal
    ([<InlineIfLambda>] snapshot: unit -> IReadOnlySet<'T>)
    : aset<'T> * (unit -> unit) =
    let node = new ExternalSetNode<'T>(snapshot)
    (node :> aset<'T>, fun () -> node.Invalidate())

  /// <summary>
  /// Adaptively reduces the set with the given <see cref="AdaptiveReduction"/>.
  /// The state is updated incrementally from deltas: added elements apply
  /// <c>add</c>; removed elements apply <c>sub</c> (or recompute the whole
  /// state when <c>sub</c> returns <c>ValueNone</c>).
  /// </summary>
  let inline reduce
    (reduction: AdaptiveReduction<'a, 's, 'v>)
    (set: aset<'a>)
    : aval<'v> =
    new SetReduceNode<'a, 'a, 's, 'v>(set, id, reduction)

  /// <summary>
  /// Maps every element, then reduces the mapped values with the given
  /// <see cref="AdaptiveReduction"/>. The mapping runs per delta element.
  /// </summary>
  let inline reduceBy
    (reduction: AdaptiveReduction<'b, 's, 'v>)
    ([<InlineIfLambda>] mapping: 'a -> 'b)
    (set: aset<'a>)
    : aval<'v> =
    new SetReduceNode<'a, 'b, 's, 'v>(set, mapping, reduction)

  /// <summary>
  /// Adaptively folds the set with <c>add</c>; every removal recomputes the
  /// whole fold (the fold operation is not invertible in general). Use
  /// <see cref="foldGroup"/> when the operation has an inverse.
  /// </summary>
  let inline fold
    ([<InlineIfLambda>] add: 's -> 'a -> 's)
    (zero: 's)
    (set: aset<'a>)
    : aval<'s> =
    reduce (AdaptiveReduction.fold zero add) set

  /// <summary>
  /// Adaptively folds the set with an invertible <c>subtract</c>: removals
  /// update the state without a recompute.
  /// </summary>
  let inline foldGroup
    ([<InlineIfLambda>] add: 's -> 'a -> 's)
    ([<InlineIfLambda>] subtract: 's -> 'a -> 's)
    (zero: 's)
    (set: aset<'a>)
    : aval<'s> =
    reduce (AdaptiveReduction.group zero add subtract) set

  /// <summary>
  /// Adaptively folds the set; a removal applies <c>trySubtract</c> when it
  /// returns a value, otherwise the whole fold recomputes.
  /// </summary>
  let inline foldHalfGroup
    ([<InlineIfLambda>] add: 's -> 'a -> 's)
    ([<InlineIfLambda>] trySubtract: 's -> 'a -> 's voption)
    (zero: 's)
    (set: aset<'a>)
    : aval<'s> =
    reduce (AdaptiveReduction.halfGroup zero add trySubtract) set

  /// <summary>
  /// Adaptively gets the number of elements. Incremental: maintained per
  /// delta, no full rescan.
  /// </summary>
  let inline count(set: aset<'T>) : aval<int> =
    new SetCountNode<'T, int>(set, id)

  /// <summary>
  /// Adaptively tests if the set is empty. Incremental: only a change that
  /// crosses the empty/non-empty boundary re-evaluates this value or its
  /// dependents.
  /// </summary>
  let inline isEmpty(set: aset<'T>) : aval<bool> =
    new SetCountNode<'T, bool>(set, fun c -> c = 0)

  /// <summary>
  /// Adaptively tests if the set contains the given element. Per-element
  /// precise (O(1) on read) for a direct changeable source: a write to an
  /// unrelated element does not re-evaluate this value or its dependents.
  /// On a derived source the branch re-evaluates at most once per upstream
  /// change (pull-lazy: the per-element gate runs at the next read's
  /// drain).
  /// </summary>
  let inline contains (value: 'T) (set: aset<'T>) : aval<bool> =
    new SetContainsNode<'T>(set, value)

  /// <summary>Adaptively tests if any element satisfies the predicate.</summary>
  let inline exists
    ([<InlineIfLambda>] predicate: 'T -> bool)
    (set: aset<'T>)
    : aval<bool> =
    let reduction =
      AdaptiveReduction.countPositive
      |> AdaptiveReduction.mapOut(fun c -> c <> 0)

    new SetReduceNode<'T, bool, int, bool>(set, predicate, reduction)

  /// <summary>Adaptively tests if every element satisfies the predicate.</summary>
  let inline forall
    ([<InlineIfLambda>] predicate: 'T -> bool)
    (set: aset<'T>)
    : aval<bool> =
    new SetReduceNode<'T, bool, int, bool>(
      set,
      predicate,
      AdaptiveReduction.countNegative
      |> AdaptiveReduction.mapOut(fun c -> c = 0)
    )

  /// <summary>Adaptively counts the elements that satisfy the predicate.</summary>
  let inline countBy
    ([<InlineIfLambda>] predicate: 'T -> bool)
    (set: aset<'T>)
    : aval<int> =
    new SetReduceNode<'T, bool, int, int>(
      set,
      predicate,
      AdaptiveReduction.countPositive
    )

  // =========================================================================
  // The *A reductions (docs/2026-08-05-MAPA-DESIGN.md §10, v2): composition
  // over mapA + the existing reduction nodes. No new node types. FDA
  // argument order: reduction, mapping, set.
  // =========================================================================

  /// <summary>
  /// Adaptively reduces the set after mapping every element to an adaptive
  /// value (FDA <c>ASet.reduceByA</c> parity). The mapped values must be
  /// equality-comparable (the mapA node's constraint). Composition: the
  /// mapping produces distinct pairs <c>struct (x, v)</c>, so duplicate
  /// mapped values keep their multiplicity (a plain mapA would deduplicate
  /// them); the reduction projects the value side.
  /// </summary>
  let inline reduceByA
    (reduction: AdaptiveReduction<'U, 's, 'v>)
    ([<InlineIfLambda>] mapping: 'T -> aval<'U>)
    (set: aset<'T>)
    : aval<'v> =
    set
    |> mapA(fun x -> AVal.map (fun v -> struct (x, v)) (mapping x))
    |> reduceBy reduction (fun struct (_, v) -> v)

  /// <summary>
  /// Adaptively counts the elements whose predicate aval holds <c>true</c>
  /// (FDA <c>ASet.countByA</c> parity). Composition: filterA + count
  /// (element-preserving, unlike a bool-mapped reduce).
  /// </summary>
  let inline countByA
    ([<InlineIfLambda>] predicate: 'T -> aval<bool>)
    (set: aset<'T>)
    : aval<int> =
    set |> filterA predicate |> count

  /// <summary>Adaptively tests if any element's predicate aval holds <c>true</c> (FDA <c>ASet.existsA</c> parity).</summary>
  let inline existsA
    ([<InlineIfLambda>] predicate: 'T -> aval<bool>)
    (set: aset<'T>)
    : aval<bool> =
    set |> countByA predicate |> AVal.map(fun c -> c <> 0)

  /// <summary>Adaptively tests if every element's predicate aval holds <c>true</c> (FDA <c>ASet.forallA</c> parity).</summary>
  let inline forallA
    ([<InlineIfLambda>] predicate: 'T -> aval<bool>)
    (set: aset<'T>)
    : aval<bool> =
    set
    |> filterA(fun x -> AVal.map not (predicate x))
    |> count
    |> AVal.map(fun c -> c = 0)

  /// <summary>Adaptively sums the avals mapped from the elements (FDA <c>ASet.sumByA</c> parity).</summary>
  let inline sumByA
    ([<InlineIfLambda>] mapping: 'T -> aval<'U>)
    (set: aset<'T>)
    : aval<'U> =
    reduceByA (AdaptiveReduction.sum()) mapping set

  /// <summary>
  /// Adaptively averages the avals mapped from the elements (FDA
  /// <c>ASet.averageByA</c> parity; needs a numeric type with
  /// <c>DivideByInt</c>, e.g. <c>float</c>).
  /// </summary>
  let inline averageByA
    ([<InlineIfLambda>] mapping: 'T -> aval<'U>)
    (set: aset<'T>)
    : aval<'U> =
    AVal.map2
      (fun total count -> LanguagePrimitives.DivideByInt total count)
      (reduceByA (AdaptiveReduction.sum()) mapping set)
      (count set)

  /// <summary>Adaptively sums the elements.</summary>
  let inline sum(set: aset<'T>) : aval<'T> =
    reduce (AdaptiveReduction.sum()) set

  /// <summary>Adaptively sums the mapped elements.</summary>
  let inline sumBy
    ([<InlineIfLambda>] mapping: 'T -> 'U)
    (set: aset<'T>)
    : aval<'U> =
    reduceBy (AdaptiveReduction.sum()) mapping set

  /// <summary>
  /// Adaptively averages the elements (needs a numeric type with
  /// <c>DivideByInt</c>, e.g. <c>float</c>). The average is sum/count.
  /// </summary>
  let inline average(set: aset< ^T >) : aval< ^T > =
    AVal.map2
      (fun total count -> LanguagePrimitives.DivideByInt total count)
      (sum set)
      (count set)

  /// <summary>
  /// Adaptively averages the mapped elements (needs a numeric type with
  /// <c>DivideByInt</c>, e.g. <c>float</c>).
  /// </summary>
  let inline averageBy
    ([<InlineIfLambda>] mapping: 'T -> ^U)
    (set: aset<'T>)
    : aval< ^U > =
    average(map mapping set)

  /// <summary>Adaptively gets the minimum element, or <c>ValueNone</c> when empty.</summary>
  let inline tryMin(set: aset<'T>) : aval<'T voption> =
    reduce (AdaptiveReduction.tryMin()) set

  /// <summary>Adaptively gets the maximum element, or <c>ValueNone</c> when empty.</summary>
  let inline tryMax(set: aset<'T>) : aval<'T voption> =
    reduce (AdaptiveReduction.tryMax()) set

  /// <summary>
  /// Adaptively gets the minimum of the avals mapped from the elements, or
  /// <c>ValueNone</c> when empty (the voption counterpart of the <c>*A</c>
  /// family, mirroring <see cref="tryMin"/>).
  /// </summary>
  let inline tryMinA
    ([<InlineIfLambda>] mapping: 'T -> aval<'U>)
    (set: aset<'T>)
    : aval<'U voption> =
    reduceByA (AdaptiveReduction.tryMin()) mapping set

  /// <summary>
  /// Adaptively gets the maximum of the avals mapped from the elements, or
  /// <c>ValueNone</c> when empty (the voption counterpart of the <c>*A</c>
  /// family, mirroring <see cref="tryMax"/>).
  /// </summary>
  let inline tryMaxA
    ([<InlineIfLambda>] mapping: 'T -> aval<'U>)
    (set: aset<'T>)
    : aval<'U voption> =
    reduceByA (AdaptiveReduction.tryMax()) mapping set

  /// <summary>A constant set with a single element.</summary>
  let inline single(value: 'T) : aset<'T> =
    new ConstantSet<'T>(fun () -> [ value ].ToFrozenSet())

  /// <summary>
  /// Materializes the set as an adaptive value. Every change materializes a
  /// new immutable <see cref="FrozenSet&lt;'T&gt;"/> (the retain boundary,
  /// like <see cref="force"/>); the value is safe to retain.
  /// </summary>
  let inline toAVal(set: aset<'T>) : aval<FrozenSet<'T>> =
    AdaptiveNode<FrozenSet<'T>>(fun () -> set.GetValue().ToFrozenSet())

  /// <summary>
  /// Returns a transient view of the current state. Valid only until the next
  /// write on the owner thread; do not retain or mutate it. Use
  /// <see cref="force"/> to materialize a snapshot that is safe to retain.
  /// </summary>
  let inline getValue(set: aset<'T>) = set.GetValue()

  /// <summary>
  /// Materializes the current state as an immutable <see cref="FrozenSet&lt;'T&gt;"/>.
  /// This is the only collection operation that allocates; the result is safe to
  /// retain and the library never touches it again. Runs the pending delta
  /// processing (drain) first.
  /// </summary>
  let inline force(set: aset<'T>) : FrozenSet<'T> = set.GetValue().ToFrozenSet()

  /// <summary>
  /// The sorted set as a list, using the given comparison (gap sheet §10.2,
  /// stable, poll node; a sorted set is a list by construction).
  /// </summary>
  let inline sortWith
    ([<InlineIfLambda>] comparer: 'T -> 'T -> int)
    (set: aset<'T>)
    : alist<'T> =
    new SortListNode<'T, 'T>(
      new SetToListNode<'T>(set),
      (fun _ v -> v),
      comparer
    )

  /// <summary>The sorted set as a list, ascending (gap sheet §10.2, stable).</summary>
  let inline sort(set: aset<'T>) : alist<'T> = sortWith compare set

  /// <summary>The sorted set as a list, descending (gap sheet §10.2, stable).</summary>
  let inline sortDescending(set: aset<'T>) : alist<'T> =
    sortWith (fun a b -> compare b a) set

  /// <summary>
  /// The set sorted by the keys given by the projection, as a list (gap
  /// sheet §10.2, stable).
  /// </summary>
  let inline sortBy
    ([<InlineIfLambda>] f: 'T -> 'K)
    (set: aset<'T>)
    : alist<'T> =
    new SortListNode<'T, 'K>(
      new SetToListNode<'T>(set),
      (fun _ v -> f v),
      compare
    )

  /// <summary>The set sorted by the keys given by the projection, descending (gap sheet §10.2, stable).</summary>
  let inline sortByDescending
    ([<InlineIfLambda>] f: 'T -> 'K)
    (set: aset<'T>)
    : alist<'T> =
    new SortListNode<'T, 'K>(
      new SetToListNode<'T>(set),
      (fun _ v -> f v),
      fun a b -> compare b a
    )

  /// <summary>Materializes the F# <c>Set</c> counterpart (sorted, structural equality).</summary>
  let inline toSet(set: aset<'T>) : Set<'T> = Set.ofSeq(set.GetValue())

/// <summary>Operations on changeable sets.</summary>
module CSet =
  /// <summary>An empty changeable set.</summary>
  let inline empty<'T> = new cset<'T>(Seq.empty)

  /// <summary>A changeable set with the given items.</summary>
  let inline ofSeq(items: seq<'T>) = new cset<'T>(items)

  /// <summary>Adds an element. No-op when already present.</summary>
  let inline add (item: 'T) (set: cset<'T>) = set.Add item

  /// <summary>Removes an element. No-op when absent.</summary>
  let inline remove (item: 'T) (set: cset<'T>) = set.Remove item

  /// <summary>
  /// Posts an add (the <c>cval.Post</c> handoff pattern): queues the
  /// operation and returns immediately. Safe from any thread. The owner
  /// thread applies the queued operations at the next graph operation
  /// (reads and writes auto-drain) or at <c>Posting.pump</c>, as one batch:
  /// one net delta, one notification delivery. A burst is coalesced into a
  /// single handoff.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CSet.postAdd item items
  /// // owner thread: the next read applies the post automatically
  /// let view = ASet.force items
  /// </code>
  /// </example>
  let inline postAdd (item: 'T) (set: cset<'T>) = set.PostAdd item

  /// <summary>Posts a remove. Safe from any thread. See <see cref="postAdd"/> for the application contract.</summary>
  let inline postRemove (item: 'T) (set: cset<'T>) = set.PostRemove item

  /// <summary>
  /// Posts a full replace. Safe from any thread. See <see cref="postAdd"/>
  /// for the application contract; a posted replace supersedes the other
  /// ops of the same pending batch (the transaction semantics of
  /// <see cref="set"/>).
  /// </summary>
  let inline postSet (value: Set<'T>) (set: cset<'T>) = set.PostSet value

  /// <summary>Replaces the whole set.</summary>
  let inline set (value: Set<'T>) (set: cset<'T>) = set.Set value

  /// <summary>
  /// Replaces the whole set and returns whether the content changed (FDA
  /// <c>cset.UpdateTo</c> parity). An equal target marks nothing.
  /// </summary>
  let inline updateTo (target: seq<'T>) (set: cset<'T>) : bool =
    let targetSet = HashSet<'T>(target)
    let view = ASet.getValue set
    let mutable changed = view.Count <> targetSet.Count

    if not changed then
      for x in view do
        if not(targetSet.Contains x) then
          changed <- true

    if changed then
      set.Set target

    changed

  /// <summary>
  /// Applies a batch of set operations (FDA <c>cset.Perform</c> parity). The
  /// batch is applied atomically: sinks receive one net delta. Adding and
  /// removing the same element within the batch cancels.
  /// </summary>
  let perform (delta: SetDeltaBuilder<'T>) (set: cset<'T>) : unit =
    let d = delta.Snapshot()

    if not d.IsEmpty then
      Transaction.run(fun () ->
        let adds = d.Added

        for i in 0 .. adds.Length - 1 do
          set.Add adds.Span[i] |> ignore

        let rems = d.Removed

        for i in 0 .. rems.Length - 1 do
          set.Remove rems.Span[i] |> ignore)

  /// <summary>Adds all the given elements (FDA <c>cset.UnionWith</c> parity; one atomic batch).</summary>
  let inline unionWith (other: seq<'T>) (set: cset<'T>) : unit =
    Transaction.run(fun () ->
      for x in other do
        set.Add x |> ignore)

  /// <summary>Removes all the given elements (FDA <c>cset.ExceptWith</c> parity; one atomic batch).</summary>
  let inline exceptWith (other: seq<'T>) (set: cset<'T>) : unit =
    Transaction.run(fun () ->
      for x in other do
        set.Remove x |> ignore)

  /// <summary>Keeps only the elements also present in <c>other</c> (FDA <c>cset.IntersectWith</c> parity; one atomic batch).</summary>
  let inline intersectWith (other: seq<'T>) (set: cset<'T>) : unit =
    Transaction.run(fun () ->
      let otherSet = HashSet<'T>(other)
      let view = ASet.getValue set

      for x in view do
        if not(otherSet.Contains x) then
          set.Remove x |> ignore)

  /// <summary>Views the changeable set as an adaptive set.</summary>
  let inline value(set: cset<'T>) : aset<'T> = set

  /// <summary>Materializes the current state as an immutable snapshot.</summary>
  let inline force(set: cset<'T>) : FrozenSet<'T> = ASet.force set

  /// <summary>Materializes the F# <c>Set</c> counterpart.</summary>
  let inline toSet(set: cset<'T>) : Set<'T> = ASet.toSet set

/// <summary>Operations on adaptive maps.</summary>
module AMap =
  /// <summary>An empty adaptive map (FDA <c>AMap.empty</c> parity).</summary>
  let empty<'K, 'V when 'K: equality> : amap<'K, 'V> =
    new ConstantMap<'K, 'V>(fun () -> FrozenDictionary<'K, 'V>.Empty)

  /// <summary>
  /// An adaptive map whose content is fixed but computed lazily, once, at
  /// first read. Enables self-referential definitions (FDA parity: the
  /// create function runs at most once; deviation: FDA's create returns a
  /// <c>HashMap</c>, ours returns a <c>Dictionary</c>).
  /// </summary>
  let inline constant
    ([<InlineIfLambda>] create: unit -> Dictionary<'K, 'V>)
    : amap<'K, 'V> =
    new ConstantMap<'K, 'V>(fun () -> create().ToFrozenDictionary())

  /// <summary>Alias of <see cref="constant"/> (symmetry with <c>ASet.delay</c>; FDA has no <c>AMap.delay</c>).</summary>
  let inline delay
    ([<InlineIfLambda>] create: unit -> Dictionary<'K, 'V>)
    : amap<'K, 'V> =
    constant create

  /// <summary>An adaptive map over fixed, immutable entries.</summary>
  let inline ofSeq(items: seq<'K * 'V>) : amap<'K, 'V> =
    new ConstantMap<'K, 'V>(fun () ->
      items
      |> Seq.map(fun (k, v) -> KeyValuePair(k, v))
      |> FrozenDictionary.ToFrozenDictionary)

  /// <summary>An adaptive map over a fixed array of entries.</summary>
  let inline ofArray(items: ('K * 'V)[]) : amap<'K, 'V> =
    new ConstantMap<'K, 'V>(fun () ->
      items
      |> Seq.map(fun (k, v) -> KeyValuePair(k, v))
      |> FrozenDictionary.ToFrozenDictionary)

  /// <summary>An adaptive map over a fixed list of entries.</summary>
  let inline ofList(items: ('K * 'V) list) : amap<'K, 'V> =
    new ConstantMap<'K, 'V>(fun () ->
      items
      |> Seq.map(fun (k, v) -> KeyValuePair(k, v))
      |> FrozenDictionary.ToFrozenDictionary)

  /// <summary>An adaptive map over a fixed F# <c>Map</c>.</summary>
  let inline ofMap(items: Map<'K, 'V>) : amap<'K, 'V> =
    new ConstantMap<'K, 'V>(fun () ->
      items
      |> Seq.map(fun (KeyValue(k, v)) -> KeyValuePair(k, v))
      |> FrozenDictionary.ToFrozenDictionary)

  /// <summary>Maps every entry of the map.</summary>
  let inline map
    ([<InlineIfLambda>] f: 'K -> 'V -> 'U)
    (mapValue: amap<'K, 'V>)
    : amap<'K, 'U> =
    new MapMapNode<'K, 'V, 'U>(mapValue, fun k v -> ValueSome(f k v))

  /// <summary>Maps every entry, keeping only the ones the mapping returns a value for.</summary>
  let inline choose
    ([<InlineIfLambda>] f: 'K -> 'V -> 'U option)
    (mapValue: amap<'K, 'V>)
    : amap<'K, 'U> =
    new MapMapNode<'K, 'V, 'U>(
      mapValue,
      fun k v -> f k v |> Option.toValueOption
    )

  /// <summary>Maps every entry, keeping only the ones the mapping returns a value for.</summary>
  let inline chooseV
    ([<InlineIfLambda>] f: 'K -> 'V -> 'U voption)
    (mapValue: amap<'K, 'V>)
    : amap<'K, 'U> =
    new MapMapNode<'K, 'V, 'U>(mapValue, f)

  /// <summary>Maps the values only (FDA <c>AMap.map'</c> parity; the V suffix is the value-only convention).</summary>
  let inline mapV
    ([<InlineIfLambda>] f: 'V -> 'U)
    (mapValue: amap<'K, 'V>)
    : amap<'K, 'U> =
    map (fun _ v -> f v) mapValue

  /// <summary>Unions both maps, resolving colliding keys with the given function.</summary>
  let inline unionWith
    ([<InlineIfLambda>] resolve: 'K -> 'V -> 'V -> 'V)
    (left: amap<'K, 'V>)
    (right: amap<'K, 'V>)
    : amap<'K, 'V> =
    new Choose2MapNode<'K, 'V, 'V, 'V>(
      left,
      right,
      fun k lv rv ->
        match struct (lv, rv) with
        | ValueSome l, ValueSome r -> ValueSome(resolve k l r)
        | ValueSome l, ValueNone -> ValueSome l
        | ValueNone, ValueSome r -> ValueSome r
        | ValueNone, ValueNone -> ValueNone
    )

  /// <summary>
  /// Unions both maps, preferring the RIGHT value when keys collide
  /// (FDA parity: <c>union a b = unionWith (fun _ _ r -> r) a b</c>).
  /// </summary>
  let inline union (left: amap<'K, 'V>) (right: amap<'K, 'V>) : amap<'K, 'V> =
    unionWith (fun _ _ r -> r) left right

  /// <summary>
  /// The keys present in both maps, with the values paired. Struct pair:
  /// the voption-first convention collapses FDA's <c>intersect</c> (tuple)
  /// and <c>intersectV</c> (struct) into the struct form.
  /// </summary>
  let inline intersect
    (left: amap<'K, 'V1>)
    (right: amap<'K, 'V2>)
    : amap<'K, struct ('V1 * 'V2)> =
    new Choose2MapNode<'K, 'V1, 'V2, struct ('V1 * 'V2)>(
      left,
      right,
      fun k lv rv ->
        match struct (lv, rv) with
        | ValueSome l, ValueSome r -> ValueSome(struct (l, r))
        | _ -> ValueNone
    )

  /// <summary>
  /// Alias of <see cref="intersect"/> (FDA parity name; our intersect is
  /// already the struct-pair form, gap sheet §4.6).
  /// </summary>
  let inline intersectV
    (left: amap<'K, 'V1>)
    (right: amap<'K, 'V2>)
    : amap<'K, struct ('V1 * 'V2)> =
    intersect left right

  /// <summary>Intersects both maps, combining the paired values.</summary>
  let inline intersectWith
    ([<InlineIfLambda>] combine: 'K -> 'V1 -> 'V2 -> 'V3)
    (left: amap<'K, 'V1>)
    (right: amap<'K, 'V2>)
    : amap<'K, 'V3> =
    new Choose2MapNode<'K, 'V1, 'V2, 'V3>(
      left,
      right,
      fun k lv rv ->
        match struct (lv, rv) with
        | ValueSome l, ValueSome r -> ValueSome(combine k l r)
        | _ -> ValueNone
    )

  /// <summary>
  /// The keys present in the left map but not in the right map, with the
  /// left values (the AMap counterpart of <c>ASet.difference</c>). Right
  /// values are ignored; only the right keys matter.
  /// </summary>
  let inline difference
    (left: amap<'K, 'V>)
    (right: amap<'K, 'V>)
    : amap<'K, 'V> =
    new Choose2MapNode<'K, 'V, 'V, 'V>(
      left,
      right,
      fun k lv rv ->
        match struct (lv, rv) with
        | ValueSome l, ValueNone -> ValueSome l
        | _ -> ValueNone
    )

  /// <summary>
  /// Groups the entries of a map by a computed key (FDA <c>AMap.groupBy</c>
  /// parity). The output entries are live adaptive maps: the per-group
  /// content follows the source adaptively, and the groups' own changes
  /// reach the consumers without re-reading the whole map. A group
  /// disappears when it becomes empty (removed at the next drain); a key
  /// whose value changes group is moved between groups.
  /// </summary>
  /// <example>
  /// <code>
  /// // documents grouped by author; the per-author map stays live
  /// let byAuthor = AMap.groupBy (fun _ doc -> doc.Author) documents
  /// // adaptively count each author
  /// let counts =
  ///     byAuthor
  ///     |> AMap.mapA (fun _ group -> AMap.count group)
  /// </code>
  /// </example>
  let inline groupBy
    ([<InlineIfLambda>] keyOf: 'K -> 'V -> 'G)
    (mapValue: amap<'K, 'V>)
    : amap<'G, amap<'K, 'V>> =
    new GroupByMapNode<'K, 'V, 'G>(mapValue, keyOf)

  /// <summary>
  /// Merges both maps with a mapping that receives the key and both side
  /// values (voptions) and returns the output value (voption). The mapping
  /// is called only when at least one side has a value (FDA parity); a key
  /// with no value on either side is removed without a call. Voption-first:
  /// this is FDA's <c>choose2V</c> (the option variant is not provided).
  /// </summary>
  let inline choose2
    ([<InlineIfLambda>] mapping: 'K -> 'V1 option -> 'V2 option -> 'V3 option)
    (left: amap<'K, 'V1>)
    (right: amap<'K, 'V2>)
    : amap<'K, 'V3> =
    new Choose2MapNode<'K, 'V1, 'V2, 'V3>(
      left,
      right,
      fun k v v2 ->
        mapping k (v |> Option.ofValueOption) (v2 |> Option.ofValueOption)
        |> Option.toValueOption
    )

  /// <summary>
  /// Merges both maps with a mapping that receives the key and both side
  /// values (voptions) and returns the output value (voption). The mapping
  /// is called only when at least one side has a value (FDA parity); a key
  /// with no value on either side is removed without a call. Voption-first:
  /// this is FDA's <c>choose2V</c> (the option variant is not provided).
  /// </summary>
  let inline choose2V
    ([<InlineIfLambda>] mapping: 'K -> 'V1 voption -> 'V2 voption -> 'V3 voption)
    (left: amap<'K, 'V1>)
    (right: amap<'K, 'V2>)
    : amap<'K, 'V3> =
    new Choose2MapNode<'K, 'V1, 'V2, 'V3>(left, right, mapping)

  /// <summary>
  /// A map from a set of entries, keeping ALL values of a key in a HashSet
  /// (FDA <c>ofASet</c> parity). A changed value set emits a fresh HashSet
  /// in the delta (this node allocates by design).
  /// </summary>
  let inline ofASet(elements: aset<'K * 'V>) : amap<'K, HashSet<'V>> =
    new SetToMapKeepAllNode<'K, 'V, 'K * 'V>(elements, id)

  /// <summary>
  /// A map from a set of entries; duplicate keys keep the LAST value
  /// (FDA <c>ofASetIgnoreDuplicates</c> parity: the constant path keeps the
  /// last value, the delta path is arbitrary).
  /// </summary>
  let inline ofASetIgnoreDuplicates(elements: aset<'K * 'V>) : amap<'K, 'V> =
    new SetToMapNode<'K, 'V, 'K * 'V>(elements, id, true)

  /// <summary>
  /// A map from a set, deriving the key from every value and keeping ALL
  /// values of a key in a HashSet (FDA <c>ofASetMapped</c> parity).
  /// </summary>
  let inline ofASetMapped
    ([<InlineIfLambda>] getKey: 'V -> 'K)
    (elements: aset<'V>)
    : amap<'K, HashSet<'V>> =
    new SetToMapKeepAllNode<'K, 'V, 'V>(elements, fun v -> (getKey v, v))

  /// <summary>
  /// A map from a set, deriving the key from every value; duplicate keys
  /// keep the LAST value.
  /// </summary>
  let inline ofASetMappedIgnoreDuplicates
    ([<InlineIfLambda>] getKey: 'V -> 'K)
    (elements: aset<'V>)
    : amap<'K, 'V> =
    new SetToMapNode<'K, 'V, 'V>(elements, (fun v -> (getKey v, v)), true)

  /// <summary>Maps the keys of a set to entries (FDA <c>mapSet</c> parity: the mapping runs per key).</summary>
  let inline mapSet
    ([<InlineIfLambda>] mapping: 'K -> 'V)
    (set: aset<'K>)
    : amap<'K, 'V> =
    new SetToMapNode<'K, 'V, 'K>(set, (fun k -> (k, mapping k)), false)

  /// <summary>
  /// An adaptive set of the map's key/value pairs (FDA <c>AMap.toASet</c>
  /// parity, gap sheet §4.14). Struct pairs: the library convention (cf.
  /// <see cref="intersect"/>). The former keys behavior moved to
  /// <see cref="keys"/>.
  /// </summary>
  let inline toASet(mapValue: amap<'K, 'V>) : aset<struct ('K * 'V)> =
    new MapToSetNode<'K, 'V, struct ('K * 'V)>(
      mapValue,
      fun k v -> struct (k, v)
    )

  /// <summary>An adaptive set of the map's keys (gap sheet §4.14).</summary>
  let inline keys(mapValue: amap<'K, 'V>) : aset<'K> =
    new MapToSetNode<'K, 'V, 'K>(mapValue, fun k _ -> k)

  /// <summary>
  /// An adaptive list of the map's entries (FDA <c>AMap.toAList</c> parity,
  /// poll node). The order is the map's iteration order, stable while the
  /// map does not change.
  /// </summary>
  let inline toAList(mapValue: amap<'K, 'V>) : alist<'K * 'V> =
    new MapToAListNode<'K, 'V>(mapValue)

  /// <summary>
  /// An adaptive map of a list of entries (FDA <c>AMap.ofAList</c> parity).
  /// Duplicate keys: the last entry wins.
  /// </summary>
  let inline ofAList(list: alist<'K * 'V>) : amap<'K, 'V> =
    new AListToMapNode<'K, 'V>(list)

  /// <summary>An adaptive set of the map's distinct values (FDA <c>toASetValues</c> parity).</summary>
  let inline toASetValues(mapValue: amap<'K, 'V>) : aset<'V> =
    new MapToSetNode<'K, 'V, 'V>(mapValue, fun _ v -> v)

  /// <summary>
  /// An adaptive map over an adaptive value of a sequence of entries. Every
  /// change of the value replaces the whole state and emits the diff as the
  /// delta (FDA <c>AMap.ofAVal</c> parity; the value carries no deltas).
  /// </summary>
  let inline ofAVal<'K, 'V, 'S when 'K: equality and 'S :> seq<'K * 'V>>
    (value: aval<'S>)
    : amap<'K, 'V> =
    new OfAvalMapNode<'K, 'V, 'S>(value)

  /// <summary>
  /// Adaptively maps over the given value and returns the resulting map (FDA
  /// <c>AMap.bind</c> parity; PLAN.md Section 7.4). When the value changes,
  /// the whole inner map is swapped: the old content is removed, the inner
  /// sink is unregistered eagerly, and <c>mapping</c> selects the new inner
  /// map. The inner map's own changes propagate while it is bound.
  /// </summary>
  /// <example>
  /// <code>
  /// // a map that follows the currently selected table
  /// let selected = CVal.create 0
  /// let tables = [| CMap.empty&lt;string, int&gt;; CMap.empty&lt;string, int&gt; |]
  /// let visible = AMap.bind (fun i -&gt; tables[i]) (CVal.value selected)
  /// CMap.addOrUpdate "health" 10 (tables[0])
  /// CVal.setValue 1 selected
  /// // visible is now empty, and table 0's later changes do not leak
  /// </code>
  /// </example>
  let inline bind
    ([<InlineIfLambda>] mapping: 'T -> amap<'K, 'V>)
    (value: aval<'T>)
    : amap<'K, 'V> =
    new BindMapNode<'K, 'V, 'T>(value, mapping)

  /// <summary>
  /// Adaptively maps over the two values and returns the resulting map (FDA
  /// <c>AMap.bind2</c> parity). When either value changes, the whole inner
  /// map is swapped (the bind semantics). Composed as one bind over the
  /// mapped pair (the FDA approach: nested binds would miss the inner
  /// bind's swap, which signals by version only, not by delta).
  /// </summary>
  let inline bind2
    ([<InlineIfLambda>] mapping: 'A -> 'B -> amap<'K, 'V>)
    (a: aval<'A>)
    (b: aval<'B>)
    : amap<'K, 'V> =
    bind (fun (av, bv) -> mapping av bv) (AVal.map2 (fun av bv -> (av, bv)) a b)

  /// <summary>
  /// Adaptively maps over the three values and returns the resulting map
  /// (FDA <c>AMap.bind3</c> parity). When any value changes, the whole
  /// inner map is swapped (the bind semantics).
  /// </summary>
  let inline bind3
    ([<InlineIfLambda>] mapping: 'A -> 'B -> 'C -> amap<'K, 'V>)
    (a: aval<'A>)
    (b: aval<'B>)
    (c: aval<'C>)
    : amap<'K, 'V> =
    bind
      (fun (av, bv, cv) -> mapping av bv cv)
      (AVal.map3 (fun av bv cv -> (av, bv, cv)) a b c)

  /// <summary>
  /// An adaptive map driven by a compute function (FDA <c>AMap.custom</c>
  /// parity, pull model). The compute receives the current view and a delta
  /// builder; it appends the operations that describe the change since the
  /// previous call (for example, consuming its own event queue).
  /// </summary>
  let inline custom
    ([<InlineIfLambda>] compute:
      IReadOnlyDictionary<'K, 'V> -> MapDeltaBuilder<'K, 'V> -> unit)
    : amap<'K, 'V> =
    new CustomMapNode<'K, 'V>(compute)

  /// <summary>
  /// Creates an adaptive map from an external snapshot function and an
  /// invalidate handle (FDA <c>AMap.ofExternal</c> parity, MAPA-DESIGN §1.1).
  /// The snapshot runs at most once per invalidate, on the next read, and is
  /// diffed against the previous snapshot (equal values elided); not
  /// invalidated → reads are O(1) and allocate nothing. The handle is O(1)
  /// to call and thread-safe (a foreign-thread call is posted to the owner
  /// context and applied at the next graph operation).
  /// </summary>
  /// <example>
  /// <code>
  /// let mutable current = dict [ 1, "a" ]
  /// let map, invalidate = AMap.ofExternal (fun () -&gt; current :&gt; IReadOnlyDictionary&lt;_, _&gt;)
  /// current &lt;- dict [ 1, "a"; 2, "b" ]
  /// invalidate ()
  /// let forced = AMap.force map   // [ 1, "a"; 2, "b" ]
  /// </code>
  /// </example>
  let inline ofExternal
    ([<InlineIfLambda>] snapshot: unit -> IReadOnlyDictionary<'K, 'V>)
    : amap<'K, 'V> * (unit -> unit) =
    let node = new ExternalMapNode<'K, 'V>(snapshot)
    (node :> amap<'K, 'V>, fun () -> node.Invalidate())

  /// <summary>
  /// Maps every entry, disposing the mapped value when its key leaves (FDA
  /// <c>AMap.mapUse</c> parity). The mapped values are stable (the mapping
  /// runs once per key). Disposing the returned disposable disposes all
  /// live mapped values and clears the output map.
  /// </summary>
  /// <example>
  /// <code>
  /// let src = CMap.ofSeq [ 1, "a"; 2, "b" ]
  /// let cleanup, mapped = src |> AMap.mapUse (fun id _ -&gt; Resource(id))
  /// CMap.remove 1 src              // the resource for key 1 is disposed
  /// cleanup.Dispose()              // all remaining resources are disposed
  /// </code>
  /// </example>
  let inline mapUse
    ([<InlineIfLambda>] mapping: 'K -> 'V -> 'W)
    (mapValue: amap<'K, 'V>)
    : IDisposable * amap<'K, 'W> =
    let node = new MapUseMapNode<'K, 'V, 'W>(mapValue, mapping)
    (node :> IDisposable, node :> amap<'K, 'W>)

  /// <summary>Keeps the entries that satisfy the predicate.</summary>
  let inline filter
    ([<InlineIfLambda>] predicate: 'K -> 'V -> bool)
    (mapValue: amap<'K, 'V>)
    : amap<'K, 'V> =
    new FilterMapNode<'K, 'V>(mapValue, predicate)

  /// <summary>Keeps the entries whose value satisfies the predicate (FDA <c>AMap.filter'</c> parity; the V suffix is the value-only convention).</summary>
  let inline filterV
    ([<InlineIfLambda>] predicate: 'V -> bool)
    (mapValue: amap<'K, 'V>)
    : amap<'K, 'V> =
    filter (fun _ v -> predicate v) mapValue

  /// <summary>
  /// Adaptively maps every entry of the map to an adaptive value (FDA
  /// <c>AMap.mapA</c> parity). The output follows the aval returned for
  /// each entry; writes to the avals deliver targeted deltas.
  /// </summary>
  let inline mapA
    ([<InlineIfLambda>] mapping: 'K -> 'V -> aval<'U>)
    (mapValue: amap<'K, 'V>)
    : amap<'K, 'U> =
    new ElementMapNode<'K, 'V, 'U>(
      mapValue,
      fun k v -> AVal.map ValueSome (mapping k v)
    )

  /// <summary>
  /// Adaptively maps every entry of the map to an adaptive value, keeping
  /// only the entries whose aval holds <c>Some</c> (FDA
  /// <c>AMap.chooseA</c> parity).
  /// </summary>
  let inline chooseA
    ([<InlineIfLambda>] mapping: 'K -> 'V -> aval<'U option>)
    (mapValue: amap<'K, 'V>)
    : amap<'K, 'U> =
    new ElementMapNode<'K, 'V, 'U>(
      mapValue,
      fun k v -> AVal.map Option.toValueOption (mapping k v)
    )

  /// <summary>
  /// The voption counterpart of <see cref="chooseA"/>: the mapping returns
  /// <c>aval&lt;'U voption&gt;</c> directly, without the option-to-voption
  /// wrapper node per entry (the no-allocation path).
  /// </summary>
  let inline chooseAV
    ([<InlineIfLambda>] mapping: 'K -> 'V -> aval<'U voption>)
    (mapValue: amap<'K, 'V>)
    : amap<'K, 'U> =
    new ElementMapNode<'K, 'V, 'U>(mapValue, mapping)

  /// <summary>
  /// Adaptively keeps the entries whose predicate aval holds <c>true</c>
  /// (FDA <c>AMap.filterA</c> parity).
  /// </summary>
  let inline filterA
    ([<InlineIfLambda>] predicate: 'K -> 'V -> aval<bool>)
    (mapValue: amap<'K, 'V>)
    : amap<'K, 'V> =
    new ElementMapNode<'K, 'V, 'V>(
      mapValue,
      fun k v ->
        AVal.map (fun b -> if b then ValueSome v else ValueNone) (predicate k v)
    )

  /// <summary>
  /// Equi-joins two maps on a computed key (the map analog of
  /// <c>AVal.map2</c>). The left map is enumerated per key; the join key is
  /// computed from the left entry; the right map is looked up per entry
  /// (never enumerated or rebuilt). The mapping receives the left key, the
  /// left value as an adaptive value, and the right-side value (or
  /// <c>ValueNone</c> when the join key is absent), and returns the output
  /// aval; a <c>ValueNone</c> output drops the entry (choose semantics).
  /// Output entries are keyed by the left key.
  /// </summary>
  /// <remarks>
  /// The per-key subgraph is built once and updated in place: a left update
  /// re-applies a swappable value cell (no rebuild, no per-frame
  /// allocation), and a join-key change (rare) re-runs the mapping against
  /// the new lookup. Right-map changes reach the entries through the lookup
  /// (read-time gate) on the next read. A left-join is the <c>ValueNone</c>
  /// case of the mapping; an inner join drops the entry when the right side
  /// is <c>ValueNone</c>.
  /// </remarks>
  /// <remarks>
  /// Argument order: the two maps come first, so the lambdas elaborate
  /// after the map types are pinned — a record-field access in
  /// <c>keyOfLeft</c> or the mapping resolves against the actual map types,
  /// not the first record in scope with a matching field. The pipe form
  /// does not apply: a piped value lands in the mapping slot, a compile
  /// error.
  /// </remarks>
  /// <remarks>
  /// Performance guidance for hot per-key subgraphs (docs/2026-08-10-JOIN-
  /// DESIGN.md §6): the per-key subgraph is forced per updated key per read.
  /// A coarser join key reduces the entry count, the direct lever. A static-
  /// input subgraph with same-typed inputs is cheaper on a fixed-dependency
  /// node (<c>AVal.mapN</c>/<c>AVal.reduce</c>) than on the general node
  /// (~40% measured on the recompute); heterogeneous inputs (this mapping's
  /// cell + lookup) have no fixed-dependency combinator yet. Batching the
  /// left-map writes of one frame in <c>Transaction.run</c> cuts the
  /// write-side cost; a clean read (no write since the last read) costs
  /// nothing.
  /// </remarks>
  /// <example>
  /// <code>
  /// // orders join their product's current price; a product removed from
  /// // the catalog falls back to the price stored on the order
  /// let orderViews =
  ///     AMap.joinOn
  ///         orders
  ///         products
  ///         (fun _ order -> order.ProductId)
  ///         (fun _ orderV priceV ->
  ///             AVal.map2
  ///                 (fun order price ->
  ///                     ValueSome
  ///                         { Order = order
  ///                           Price = price |> ValueOption.defaultValue order.StoredPrice })
  ///                 orderV
  ///                 priceV)
  /// </code>
  /// </example>
  let inline joinOn
    (left: amap<'K1, 'V1>)
    (right: amap<'K2, 'V2>)
    ([<InlineIfLambda>] keyOfLeft: 'K1 -> 'V1 -> 'K2)
    ([<InlineIfLambda>] mapping:
      'K1 -> aval<'V1> -> aval<'V2 voption> -> aval<'U voption>)
    : amap<'K1, 'U> =
    new JoinMapNode<'K1, 'V1, 'K2, 'V2, 'U>(left, right, keyOfLeft, mapping)

  /// <summary>
  /// Adaptively reduces the map with the given <see cref="AdaptiveReduction"/>
  /// over the values. The state is updated incrementally from deltas: a Set
  /// on an existing key subtracts the old value, then adds the new one.
  /// </summary>
  let inline reduce
    (reduction: AdaptiveReduction<'a, 's, 'v>)
    (mapValue: amap<'k, 'a>)
    : aval<'v> =
    new MapReduceNode<'k, 'a, 'a, 's, 'v>(mapValue, (fun _ v -> v), reduction)

  /// <summary>
  /// Maps every entry, then reduces the mapped values with the given
  /// <see cref="AdaptiveReduction"/>. The mapping runs per delta entry.
  /// </summary>
  let inline reduceBy
    (reduction: AdaptiveReduction<'b, 's, 'v>)
    ([<InlineIfLambda>] mapping: 'k -> 'a -> 'b)
    (mapValue: amap<'k, 'a>)
    : aval<'v> =
    new MapReduceNode<'k, 'a, 'b, 's, 'v>(mapValue, mapping, reduction)

  /// <summary>
  /// Adaptively folds the map with <c>add</c>; every removal recomputes the
  /// whole fold. Use <see cref="foldGroup"/> when the operation has an inverse.
  /// </summary>
  let inline fold
    ([<InlineIfLambda>] add: 's -> 'k -> 'v -> 's)
    (zero: 's)
    (mapValue: amap<'k, 'v>)
    : aval<'s> =
    let mapping k v = struct (k, v)
    let add2 s struct (k, v) = add s k v

    new MapReduceNode<'k, 'v, struct ('k * 'v), 's, 's>(
      mapValue,
      mapping,
      AdaptiveReduction.fold zero add2
    )


  /// <summary>
  /// Adaptively folds the map with an invertible <c>subtract</c>: removals
  /// update the state without a recompute.
  /// </summary>
  let inline foldGroup
    ([<InlineIfLambda>] add: 's -> 'k -> 'v -> 's)
    ([<InlineIfLambda>] subtract: 's -> 'k -> 'v -> 's)
    (zero: 's)
    (mapValue: amap<'k, 'v>)
    : aval<'s> =
    let inline mapping k v = struct (k, v)
    let inline add2 s struct (k, v) = add s k v
    let inline sub2 s struct (k, v) = subtract s k v

    new MapReduceNode<'k, 'v, struct ('k * 'v), 's, 's>(
      mapValue,
      mapping,
      AdaptiveReduction.group zero add2 sub2
    )

  /// <summary>
  /// Adaptively folds the map with a partially invertible <c>trySubtract</c>:
  /// removals that cannot be inverted recompute the whole fold (FDA
  /// <c>AMap.foldHalfGroup</c> parity).
  /// </summary>
  let inline foldHalfGroup
    ([<InlineIfLambda>] add: 's -> 'k -> 'v -> 's)
    ([<InlineIfLambda>] trySubtract: 's -> 'k -> 'v -> 's voption)
    (zero: 's)
    (mapValue: amap<'k, 'v>)
    : aval<'s> =
    let inline mapping k v = struct (k, v)
    let inline add2 s struct (k, v) = add s k v
    let inline sub2 s struct (k, v) = trySubtract s k v

    new MapReduceNode<'k, 'v, struct ('k * 'v), 's, 's>(
      mapValue,
      mapping,
      AdaptiveReduction.halfGroup zero add2 sub2
    )

  /// <summary>
  /// Adaptively sums the mapped entries (FDA <c>AMap.sumBy</c> parity).
  /// </summary>
  let inline sumBy
    ([<InlineIfLambda>] mapping: 'k -> 'v -> 'u)
    (mapValue: amap<'k, 'v>)
    : aval<'u> =
    reduceBy (AdaptiveReduction.sum()) mapping mapValue


  /// <summary>
  /// Adaptively gets the number of entries. Incremental: an update of an
  /// existing key does not re-evaluate this value or its dependents.
  /// </summary>
  let inline count(mapValue: amap<'K, 'V>) : aval<int> =
    new MapCountNode<'K, 'V, int>(mapValue, id)

  /// <summary>
  /// Adaptively averages the mapped entries (needs a numeric type with
  /// <c>DivideByInt</c>, e.g. <c>float</c>). The average is sum/count.
  /// </summary>
  let inline averageBy
    ([<InlineIfLambda>] mapping: 'k -> 'v -> ^u)
    (mapValue: amap<'k, 'v>)
    : aval< ^u > =
    AVal.map2
      (fun total c -> LanguagePrimitives.DivideByInt total c)
      (reduceBy (AdaptiveReduction.sum()) mapping mapValue)
      (count mapValue)

  /// <summary>
  /// Adaptively tests if the map is empty. Incremental: only a change that
  /// crosses the empty/non-empty boundary re-evaluates this value or its
  /// dependents.
  /// </summary>
  let inline isEmpty(mapValue: amap<'K, 'V>) : aval<bool> =
    new MapCountNode<'K, 'V, bool>(mapValue, fun c -> c = 0)

  /// <summary>Adaptively tests if any entry satisfies the predicate.</summary>
  let inline exists
    ([<InlineIfLambda>] predicate: 'K -> 'V -> bool)
    (mapValue: amap<'K, 'V>)
    : aval<bool> =
    new MapReduceNode<'K, 'V, bool, int, bool>(
      mapValue,
      predicate,
      AdaptiveReduction.countPositive
      |> AdaptiveReduction.mapOut(fun c -> c <> 0)
    )

  /// <summary>Adaptively tests if every entry satisfies the predicate.</summary>
  let inline forall
    ([<InlineIfLambda>] predicate: 'K -> 'V -> bool)
    (mapValue: amap<'K, 'V>)
    : aval<bool> =
    new MapReduceNode<'K, 'V, bool, int, bool>(
      mapValue,
      predicate,
      AdaptiveReduction.countNegative
      |> AdaptiveReduction.mapOut(fun c -> c = 0)
    )

  /// <summary>Adaptively counts the entries that satisfy the predicate.</summary>
  let inline countBy
    ([<InlineIfLambda>] predicate: 'K -> 'V -> bool)
    (mapValue: amap<'K, 'V>)
    : aval<int> =
    new MapReduceNode<'K, 'V, bool, int, int>(
      mapValue,
      predicate,
      AdaptiveReduction.countPositive
    )

  // =========================================================================
  // The *A reductions (docs/2026-08-05-MAPA-DESIGN.md §10): composition over
  // mapA/filterA + the existing reduction nodes. No new node types. FDA
  // argument order: reduction, mapping, map.
  // =========================================================================

  /// <summary>
  /// Adaptively reduces the map after mapping every entry to an adaptive
  /// value (the AMap counterpart of <c>ASet.reduceByA</c>). The mapping
  /// produces distinct pairs <c>struct (k, x)</c>, so duplicate mapped
  /// values keep their multiplicity (a plain mapA would deduplicate them);
  /// the reduction projects the value side.
  /// </summary>
  let inline reduceByA
    (reduction: AdaptiveReduction<'U, 's, 'v>)
    ([<InlineIfLambda>] mapping: 'K -> 'V -> aval<'U>)
    (mapValue: amap<'K, 'V>)
    : aval<'v> =
    mapValue
    |> mapA(fun k v -> AVal.map (fun x -> struct (k, x)) (mapping k v))
    |> reduceBy reduction (fun _ struct (_, x) -> x)

  /// <summary>Adaptively counts the entries whose predicate aval holds <c>true</c> (the AMap counterpart of <c>ASet.countByA</c>).</summary>
  let inline countByA
    ([<InlineIfLambda>] predicate: 'K -> 'V -> aval<bool>)
    (mapValue: amap<'K, 'V>)
    : aval<int> =
    mapValue |> filterA predicate |> count

  /// <summary>Adaptively tests if any entry's predicate aval holds <c>true</c> (the AMap counterpart of <c>ASet.existsA</c>).</summary>
  let inline existsA
    ([<InlineIfLambda>] predicate: 'K -> 'V -> aval<bool>)
    (mapValue: amap<'K, 'V>)
    : aval<bool> =
    mapValue |> countByA predicate |> AVal.map(fun c -> c <> 0)

  /// <summary>Adaptively tests if every entry's predicate aval holds <c>true</c> (the AMap counterpart of <c>ASet.forallA</c>).</summary>
  let inline forallA
    ([<InlineIfLambda>] predicate: 'K -> 'V -> aval<bool>)
    (mapValue: amap<'K, 'V>)
    : aval<bool> =
    mapValue
    |> filterA(fun k v -> AVal.map not (predicate k v))
    |> count
    |> AVal.map(fun c -> c = 0)

  /// <summary>Adaptively sums the avals mapped from the entries (the AMap counterpart of <c>ASet.sumByA</c>).</summary>
  let inline sumByA
    ([<InlineIfLambda>] mapping: 'K -> 'V -> aval<'U>)
    (mapValue: amap<'K, 'V>)
    : aval<'U> =
    reduceByA (AdaptiveReduction.sum()) mapping mapValue

  /// <summary>
  /// Adaptively averages the avals mapped from the entries (needs a numeric
  /// type with <c>DivideByInt</c>, e.g. <c>float</c>; the AMap counterpart
  /// of <c>ASet.averageByA</c>).
  /// </summary>
  let inline averageByA
    ([<InlineIfLambda>] mapping: 'K -> 'V -> aval<'U>)
    (mapValue: amap<'K, 'V>)
    : aval<'U> =
    AVal.map2
      (fun total count -> LanguagePrimitives.DivideByInt total count)
      (reduceByA (AdaptiveReduction.sum()) mapping mapValue)
      (count mapValue)

  /// <summary>
  /// Adaptively gets the minimum of the avals mapped from the entries, or
  /// <c>ValueNone</c> when empty (the voption counterpart of the <c>*A</c>
  /// family; AMap has no plain <c>tryMin</c>, this mirrors the ASet/AList
  /// members for family symmetry).
  /// </summary>
  let inline tryMinA
    ([<InlineIfLambda>] mapping: 'K -> 'V -> aval<'U>)
    (mapValue: amap<'K, 'V>)
    : aval<'U voption> =
    reduceByA (AdaptiveReduction.tryMin()) mapping mapValue

  /// <summary>
  /// Adaptively gets the maximum of the avals mapped from the entries, or
  /// <c>ValueNone</c> when empty (the voption counterpart of the <c>*A</c>
  /// family; AMap has no plain <c>tryMax</c>, this mirrors the ASet/AList
  /// members for family symmetry).
  /// </summary>
  let inline tryMaxA
    ([<InlineIfLambda>] mapping: 'K -> 'V -> aval<'U>)
    (mapValue: amap<'K, 'V>)
    : aval<'U voption> =
    reduceByA (AdaptiveReduction.tryMax()) mapping mapValue

  /// <summary>
  /// Adaptively looks up the key: the value, or <c>ValueNone</c> when the
  /// key is absent. The lookup is per-key precise (O(1) on read) for a
  /// direct changeable source: a write to an unrelated key does not
  /// re-evaluate this value or its dependents. On a derived source the
  /// branch re-evaluates at most once per upstream change (pull-lazy: the
  /// per-key gate runs at the next read's drain).
  /// </summary>
  let inline tryFind (key: 'K) (mapValue: amap<'K, 'V>) : aval<'V voption> =
    new MapLookupNode<'K, 'V>(mapValue, key)


  /// <summary>
  /// Adaptively looks up the key. Reading the value throws
  /// <see cref="KeyNotFoundException"/> when the key is absent. Per-key
  /// precise, like <see cref="tryFind"/>.
  /// </summary>
  let inline find (key: 'K) (mapValue: amap<'K, 'V>) : aval<'V> =
    tryFind key mapValue
    |> AVal.map(fun v ->
      match v with
      | ValueSome value -> value
      | ValueNone ->
        raise(KeyNotFoundException(sprintf "could not get key: %A" key)))

  /// <summary>A constant map with a single entry.</summary>
  let inline single (key: 'K) (value: 'V) : amap<'K, 'V> =
    new ConstantMap<'K, 'V>(fun () ->
      [| KeyValuePair(key, value) |] |> FrozenDictionary.ToFrozenDictionary)

  /// <summary>
  /// Materializes the map as an adaptive value. Every change materializes a
  /// new immutable <see cref="FrozenDictionary&lt;'K,'V&gt;"/> (the retain
  /// boundary, like <see cref="force"/>); the value is safe to retain.
  /// </summary>
  let inline toAVal(mapValue: amap<'K, 'V>) : aval<FrozenDictionary<'K, 'V>> =
    AdaptiveNode<FrozenDictionary<'K, 'V>>(fun () ->
      mapValue.GetValue().ToFrozenDictionary())

  /// <summary>
  /// Returns a transient view of the current state. Valid only until the next
  /// write on the owner thread; do not retain or mutate it. Use
  /// <see cref="force"/> to materialize a snapshot that is safe to retain.
  /// </summary>
  let inline getValue(mapValue: amap<'K, 'V>) = mapValue.GetValue()

  /// <summary>
  /// Materializes the current state as an immutable
  /// <see cref="FrozenDictionary&lt;'K,'V&gt;"/>. This is the only collection
  /// operation that allocates; the result is safe to retain and the library
  /// never touches it again. Runs the pending delta processing (drain) first.
  /// </summary>
  let inline force(mapValue: amap<'K, 'V>) : FrozenDictionary<'K, 'V> =
    mapValue.GetValue().ToFrozenDictionary()

  /// <summary>Materializes the F# <c>Map</c> counterpart (sorted, structural equality).</summary>
  let inline toMap(mapValue: amap<'K, 'V>) : Map<'K, 'V> =
    [ for KeyValue(k, v) in mapValue.GetValue() -> k, v ] |> Map.ofList

/// <summary>Operations on changeable maps.</summary>
module CMap =
  /// <summary>An empty changeable map.</summary>
  let inline empty<'K, 'V when 'K: equality> = new cmap<'K, 'V>(Seq.empty)

  /// <summary>A changeable map with the given entries.</summary>
  let inline ofSeq(items: seq<'K * 'V>) = new cmap<'K, 'V>(items)

  /// <summary>Adds or updates an entry. No-op when the value is unchanged.</summary>
  let inline addOrUpdate (key: 'K) (value: 'V) (mapValue: cmap<'K, 'V>) =
    mapValue.AddOrUpdate key value

  /// <summary>Removes an entry. No-op when absent.</summary>
  let inline remove (key: 'K) (mapValue: cmap<'K, 'V>) = mapValue.Remove key

  /// <summary>
  /// Posts an add or update (the <c>cval.Post</c> handoff pattern): queues
  /// the operation and returns immediately. Safe from any thread. The owner
  /// thread applies the queued operations at the next graph operation
  /// (reads and writes auto-drain) or at <c>Posting.pump</c>, as one batch:
  /// one net delta, one notification delivery. A burst is coalesced into a
  /// single handoff.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CMap.postAddOrUpdate key value map
  /// // owner thread: the next read applies the post automatically
  /// let view = AMap.force map
  /// </code>
  /// </example>
  let inline postAddOrUpdate (key: 'K) (value: 'V) (mapValue: cmap<'K, 'V>) =
    mapValue.PostAddOrUpdate key value

  /// <summary>Posts a remove. Safe from any thread. See <see cref="postAddOrUpdate"/> for the application contract.</summary>
  let inline postRemove (key: 'K) (mapValue: cmap<'K, 'V>) =
    mapValue.PostRemove key

  /// <summary>
  /// Posts a full replace. Safe from any thread. See
  /// <see cref="postAddOrUpdate"/> for the application contract; a posted
  /// replace supersedes the other ops of the same pending batch (the
  /// transaction semantics of <see cref="set"/>). The content is passed as
  /// a seq so the posting thread allocates nothing: convert a <c>Map</c>
  /// with <c>Map.toSeq</c> at the call site when needed.
  /// </summary>
  let inline postSet (value: seq<'K * 'V>) (mapValue: cmap<'K, 'V>) =
    mapValue.PostSet value

  /// <summary>
  /// Posts a clear (a full replace with the empty map). Safe from any
  /// thread. See <see cref="postAddOrUpdate"/> for the application
  /// contract.
  /// </summary>
  let inline postClear(mapValue: cmap<'K, 'V>) = mapValue.PostClear()

  /// <summary>Replaces the whole map.</summary>
  let inline set (value: Map<'K, 'V>) (mapValue: cmap<'K, 'V>) =
    mapValue.Set(Map.toSeq value)

  /// <summary>Tests whether the key is present (FDA <c>cmap.ContainsKey</c> parity).</summary>
  let inline containsKey (key: 'K) (mapValue: cmap<'K, 'V>) : bool =
    (AMap.getValue mapValue).ContainsKey key

  /// <summary>Gets the value for the key, or <c>ValueNone</c> when absent (FDA <c>cmap.TryGetValue</c> parity).</summary>
  let inline tryGetValue (key: 'K) (mapValue: cmap<'K, 'V>) : 'V voption =
    let view = AMap.getValue mapValue
    let mutable v = Unchecked.defaultof<'V>

    if view.TryGetValue(key, &v) then ValueSome v else ValueNone

  /// <summary>Gets the value for the key (FDA <c>cmap.Item</c> parity; <see cref="KeyNotFoundException"/> when absent).</summary>
  let inline item (key: 'K) (mapValue: cmap<'K, 'V>) : 'V =
    (AMap.getValue mapValue).[key]

  /// <summary>
  /// Replaces the whole map and returns whether the content changed (FDA
  /// <c>cmap.UpdateTo</c> parity; deviation: FDA merges with init/update, we
  /// replace, matching <see cref="set"/>). An equal target marks nothing.
  /// </summary>
  let inline updateTo (target: seq<'K * 'V>) (mapValue: cmap<'K, 'V>) : bool =
    let targetMap = Dictionary<'K, 'V>()

    for k, v in target do
      targetMap[k] <- v

    let view = AMap.getValue mapValue
    let mutable changed = view.Count <> targetMap.Count

    if not changed then
      for KeyValue(k, v) in view do
        let mutable t = Unchecked.defaultof<'V>

        if
          not(targetMap.TryGetValue(k, &t))
          || not(EqualityComparer<'V>.Default.Equals(t, v))
        then
          changed <- true

    if changed then
      mapValue.Set target

    changed

  /// <summary>
  /// Applies a batch of map operations (FDA <c>cmap.Perform</c> parity). The
  /// batch is applied atomically: sinks receive one net delta.
  /// </summary>
  let perform (delta: MapDeltaBuilder<'K, 'V>) (mapValue: cmap<'K, 'V>) : unit =
    let d = delta.Snapshot()

    if not d.IsEmpty then
      Transaction.run(fun () ->
        let sets = d.SetEntries

        for i in 0 .. sets.Length - 1 do
          let struct (k, v) = sets.Span[i]
          mapValue.AddOrUpdate k v |> ignore

        let rems = d.RemovedKeys

        for i in 0 .. rems.Length - 1 do
          mapValue.Remove rems.Span[i] |> ignore)

  /// <summary>Removes all entries (FDA <c>cmap.Clear</c> parity; one atomic batch).</summary>
  let inline clear(mapValue: cmap<'K, 'V>) : unit =
    Transaction.run(fun () ->
      let view = AMap.getValue mapValue

      for KeyValue(k, _) in view do
        mapValue.Remove k |> ignore)

  /// <summary>Views the changeable map as an adaptive map.</summary>
  let inline value(mapValue: cmap<'K, 'V>) : amap<'K, 'V> = mapValue

  /// <summary>Materializes the current state as an immutable snapshot.</summary>
  let inline force(mapValue: cmap<'K, 'V>) : FrozenDictionary<'K, 'V> =
    AMap.force mapValue

  /// <summary>Materializes the F# <c>Map</c> counterpart.</summary>
  let inline toMap(mapValue: cmap<'K, 'V>) : Map<'K, 'V> = AMap.toMap mapValue

/// <summary>Operations on adaptive lists (docs/ALIST-DESIGN.md §4).</summary>
module AList =
  /// <summary>An empty adaptive list (FDA <c>AList.empty</c> parity).</summary>
  let empty<'T> : alist<'T> = new ConstantList<'T>(fun () -> Array.empty)

  /// <summary>An adaptive list over fixed, immutable items.</summary>
  let inline ofSeq(items: seq<'T>) : alist<'T> =
    new ConstantList<'T>(fun () -> Seq.toArray items)

  /// <summary>An adaptive list over a fixed array.</summary>
  let inline ofArray(items: 'T[]) : alist<'T> =
    new ConstantList<'T>(fun () -> items)

  /// <summary>An adaptive list over a fixed list.</summary>
  let inline ofList(items: 'T list) : alist<'T> =
    new ConstantList<'T>(fun () -> List.toArray items)

  /// <summary>An adaptive list over a fixed ResizeArray.</summary>
  let inline ofResizeArray(items: ResizeArray<'T>) : alist<'T> =
    new ConstantList<'T>(fun () -> items.ToArray())

  /// <summary>A constant list with a single element.</summary>
  let inline single(value: 'T) : alist<'T> =
    new ConstantList<'T>(fun () -> [| value |])

  /// <summary>
  /// An adaptive list whose content is fixed but computed lazily, once, at
  /// first read (FDA parity: the create function runs at most once).
  /// </summary>
  let inline constant(create: unit -> ResizeArray<'T>) : alist<'T> =
    new ConstantList<'T>(fun () -> create().ToArray())

  /// <summary>Alias of <see cref="constant"/> (FDA parity: delay is constant).</summary>
  let inline delay
    ([<InlineIfLambda>] create: unit -> ResizeArray<'T>)
    : alist<'T> =
    constant create

  /// <summary>Maps every element of the list.</summary>
  let inline map
    ([<InlineIfLambda>] f: 'T -> 'U)
    (list: alist<'T>)
    : alist<'U> =
    new FilterMapListNode<'T, 'U>(list, fun _ x -> ValueSome(f x))

  /// <summary>Maps every element, keeping only the ones the mapping returns a value for.</summary>
  let inline choose
    ([<InlineIfLambda>] f: 'T -> 'U option)
    (list: alist<'T>)
    : alist<'U> =
    new FilterMapListNode<'T, 'U>(
      list,
      fun _ x ->
        match f x with
        | Some u -> ValueSome u
        | None -> ValueNone
    )

  /// <summary>Maps every element, keeping only the ones the mapping returns a value for (voption form).</summary>
  let inline chooseV
    ([<InlineIfLambda>] f: 'T -> 'U voption)
    (list: alist<'T>)
    : alist<'U> =
    new FilterMapListNode<'T, 'U>(list, fun _ x -> f x)

  /// <summary>Keeps the elements that satisfy the predicate.</summary>
  let inline filter
    ([<InlineIfLambda>] predicate: 'T -> bool)
    (list: alist<'T>)
    : alist<'T> =
    new FilterMapListNode<'T, 'T>(
      list,
      fun _ x -> if predicate x then ValueSome x else ValueNone
    )

  /// <summary>
  /// Maps every element, passing the input position to the mapping (FDA
  /// <c>AList.mapi</c> parity; the index is the <c>int</c> input position,
  /// the positional deviation per ALIST-DESIGN §5).
  /// </summary>
  let inline mapi
    ([<InlineIfLambda>] f: int -> 'T -> 'U)
    (list: alist<'T>)
    : alist<'U> =
    new FilterMapListNode<'T, 'U>(list, fun i x -> ValueSome(f i x))

  /// <summary>Keeps the entries whose index-aware mapping returns a value (FDA <c>AList.choosei</c> parity).</summary>
  let inline choosei
    ([<InlineIfLambda>] f: int -> 'T -> 'U option)
    (list: alist<'T>)
    : alist<'U> =
    new FilterMapListNode<'T, 'U>(
      list,
      fun i x ->
        match f i x with
        | Some u -> ValueSome u
        | None -> ValueNone
    )

  /// <summary>Keeps the entries whose index-aware mapping returns a value (the voption counterpart of <see cref="choosei"/>).</summary>
  let inline chooseiV
    ([<InlineIfLambda>] f: int -> 'T -> 'U voption)
    (list: alist<'T>)
    : alist<'U> =
    new FilterMapListNode<'T, 'U>(list, fun i x -> f i x)

  /// <summary>Keeps the elements whose index-aware predicate holds (FDA <c>AList.filteri</c> parity).</summary>
  let inline filteri
    ([<InlineIfLambda>] predicate: int -> 'T -> bool)
    (list: alist<'T>)
    : alist<'T> =
    new FilterMapListNode<'T, 'T>(
      list,
      fun i x -> if predicate i x then ValueSome x else ValueNone
    )

  /// <summary>
  /// An adaptive list of the elements paired with their input positions
  /// (FDA <c>AList.indexed</c> parity; struct pair, the library convention;
  /// the position is the <c>int</c> input position).
  /// </summary>
  let inline indexed(list: alist<'T>) : alist<struct (int * 'T)> =
    mapi (fun i v -> struct (i, v)) list

  /// <summary>
  /// Adaptively maps every element of the list to an adaptive value (FDA
  /// <c>AList.mapA</c> parity). The output follows the aval returned for
  /// each element; writes to the avals deliver targeted deltas.
  /// </summary>
  let inline mapA
    ([<InlineIfLambda>] mapping: 'T -> aval<'U>)
    (list: alist<'T>)
    : alist<'U> =
    new ElementListNode<'T, 'U>(list, fun _ x -> AVal.map ValueSome (mapping x))

  /// <summary>
  /// Adaptively maps every element of the list to an adaptive value, keeping
  /// only the elements whose aval holds <c>Some</c> (FDA
  /// <c>AList.chooseA</c> parity).
  /// </summary>
  let inline chooseA
    ([<InlineIfLambda>] mapping: 'T -> aval<'U option>)
    (list: alist<'T>)
    : alist<'U> =
    new ElementListNode<'T, 'U>(
      list,
      fun _ x -> AVal.map Option.toValueOption (mapping x)
    )

  /// <summary>
  /// The voption counterpart of <see cref="chooseA"/>: the mapping returns
  /// <c>aval&lt;'U voption&gt;</c> directly, without the option-to-voption
  /// wrapper node per element (the no-allocation path).
  /// </summary>
  let inline chooseAV
    ([<InlineIfLambda>] mapping: 'T -> aval<'U voption>)
    (list: alist<'T>)
    : alist<'U> =
    new ElementListNode<'T, 'U>(list, fun _ x -> mapping x)

  /// <summary>
  /// Adaptively keeps the elements whose predicate aval holds <c>true</c>
  /// (FDA <c>AList.filterA</c> parity).
  /// </summary>
  let inline filterA
    ([<InlineIfLambda>] predicate: 'T -> aval<bool>)
    (list: alist<'T>)
    : alist<'T> =
    new ElementListNode<'T, 'T>(
      list,
      fun _ x ->
        AVal.map (fun b -> if b then ValueSome x else ValueNone) (predicate x)
    )

  /// <summary>
  /// Adaptively maps every element of the list to an adaptive value, passing
  /// the input position to the mapping (FDA <c>AList.mapiA</c> parity;
  /// FDA passes an <c>Index</c>, we pass the <c>int</c> position).
  /// </summary>
  let inline mapiA
    ([<InlineIfLambda>] mapping: int -> 'T -> aval<'U>)
    (list: alist<'T>)
    : alist<'U> =
    new ElementListNode<'T, 'U>(
      list,
      fun i x -> AVal.map ValueSome (mapping i x)
    )

  /// <summary>
  /// Adaptively maps every element of the list to an adaptive value, keeping
  /// only the elements whose aval holds <c>Some</c>, passing the input
  /// position to the mapping (FDA <c>AList.chooseiA</c> parity).
  /// </summary>
  let inline chooseiA
    ([<InlineIfLambda>] mapping: int -> 'T -> aval<'U option>)
    (list: alist<'T>)
    : alist<'U> =
    new ElementListNode<'T, 'U>(
      list,
      fun i x -> AVal.map Option.toValueOption (mapping i x)
    )

  /// <summary>
  /// The voption counterpart of <see cref="chooseiA"/>: the mapping returns
  /// <c>aval&lt;'U voption&gt;</c> directly, without the option-to-voption
  /// wrapper node per element (the no-allocation path).
  /// </summary>
  let inline chooseiAV
    ([<InlineIfLambda>] mapping: int -> 'T -> aval<'U voption>)
    (list: alist<'T>)
    : alist<'U> =
    new ElementListNode<'T, 'U>(list, fun i x -> mapping i x)

  /// <summary>
  /// Adaptively keeps the elements whose predicate aval holds <c>true</c>,
  /// passing the input position to the predicate (FDA <c>AList.filteriA</c>
  /// parity).
  /// </summary>
  let inline filteriA
    ([<InlineIfLambda>] predicate: int -> 'T -> aval<bool>)
    (list: alist<'T>)
    : alist<'T> =
    new ElementListNode<'T, 'T>(
      list,
      fun i x ->
        AVal.map (fun b -> if b then ValueSome x else ValueNone) (predicate i x)
    )

  /// <summary>The concatenation of two lists (FDA <c>AList.append</c> parity).</summary>
  let inline append (left: alist<'T>) (right: alist<'T>) : alist<'T> =
    new AppendListNode<'T>(left, right)

  /// <summary>
  /// Returns a transient view of the current state. Valid only until the next
  /// write on the owner thread; do not retain or mutate it. Use
  /// <see cref="force"/> to materialize a snapshot that is safe to retain.
  /// </summary>
  let inline getValue(list: alist<'T>) = list.GetValue()

  /// <summary>
  /// Materializes the current state as a fresh array. This is the only list
  /// operation that allocates; the result is safe to retain and the library
  /// never touches it again. Runs the pending delta processing (drain) first.
  /// There is no <c>FrozenList</c> in <c>System.Collections.Frozen</c> on
  /// net8/net10, so the array is the retain boundary (docs/ALIST-DESIGN.md
  /// §3.3).
  /// </summary>
  let inline force(list: alist<'T>) : 'T[] = Seq.toArray(list.GetValue())

  /// <summary>Materializes the F# <c>list</c> counterpart.</summary>
  let inline toList(list: alist<'T>) : 'T list = List.ofSeq(list.GetValue())

  /// <summary>Materializes the array counterpart.</summary>
  let inline toArray(list: alist<'T>) : 'T[] = Seq.toArray(list.GetValue())

  /// <summary>Adaptively gets the number of elements.</summary>
  let inline count(list: alist<'T>) : aval<int> =
    new ListCountNode<'T, int>(list, id)

  /// <summary>
  /// Adaptively tests if the list is empty. Incremental: only a change that
  /// crosses the empty/non-empty boundary re-evaluates this value or its
  /// dependents.
  /// </summary>
  let inline isEmpty(list: alist<'T>) : aval<bool> =
    new ListCountNode<'T, bool>(list, fun c -> c = 0)

  /// <summary>
  /// Adaptively reduces the list with the given <see cref="AdaptiveReduction"/>
  /// (FDA <c>AList.reduce</c> parity). The reduction state is maintained per
  /// delta; a reduction that cannot invert a removal recomputes (e.g.
  /// <see cref="AdaptiveReduction.fold"/>). Order-sensitive reductions are
  /// the caller's contract (the add/sub must be delta-consistent).
  /// </summary>
  let inline reduce
    (reduction: AdaptiveReduction<'a, 's, 'v>)
    (list: alist<'a>)
    : aval<'v> =
    new ListReduceNode<'a, 'a, 's, 'v>(list, (fun v -> v), reduction)

  /// <summary>
  /// Maps every element, then reduces the mapped values with the given
  /// <see cref="AdaptiveReduction"/> (FDA <c>AList.reduceBy</c> parity). The
  /// mapping runs per delta entry.
  /// </summary>
  let inline reduceBy
    (reduction: AdaptiveReduction<'b, 's, 'v>)
    ([<InlineIfLambda>] mapping: 'a -> 'b)
    (list: alist<'a>)
    : aval<'v> =
    new ListReduceNode<'a, 'b, 's, 'v>(list, mapping, reduction)

  /// <summary>
  /// Adaptively folds the list with <c>add</c>; every removal recomputes the
  /// whole fold (FDA <c>AList.fold</c> parity).
  /// </summary>
  let inline fold
    ([<InlineIfLambda>] add: 's -> 'a -> 's)
    (zero: 's)
    (list: alist<'a>)
    : aval<'s> =
    reduceBy (AdaptiveReduction.fold zero add) (fun v -> v) list

  /// <summary>
  /// Adaptively folds the list with an invertible <c>subtract</c>: removals
  /// update the state without a recompute (FDA <c>AList.foldGroup</c>
  /// parity; the add/sub must be delta-consistent, see
  /// <see cref="reduce"/>).
  /// </summary>
  let inline foldGroup
    ([<InlineIfLambda>] add: 's -> 'a -> 's)
    ([<InlineIfLambda>] subtract: 's -> 'a -> 's)
    (zero: 's)
    (list: alist<'a>)
    : aval<'s> =
    reduceBy (AdaptiveReduction.group zero add subtract) (fun v -> v) list

  /// <summary>
  /// Adaptively folds the list with a partially invertible
  /// <c>trySubtract</c>: removals that cannot be inverted recompute the
  /// whole fold (FDA <c>AList.foldHalfGroup</c> parity).
  /// </summary>
  let inline foldHalfGroup
    ([<InlineIfLambda>] add: 's -> 'a -> 's)
    ([<InlineIfLambda>] trySubtract: 's -> 'a -> 's voption)
    (zero: 's)
    (list: alist<'a>)
    : aval<'s> =
    reduceBy
      (AdaptiveReduction.halfGroup zero add trySubtract)
      (fun v -> v)
      list

  /// <summary>Adaptively tests if any element satisfies the predicate (FDA <c>AList.exists</c> parity).</summary>
  let inline exists
    ([<InlineIfLambda>] predicate: 'T -> bool)
    (list: alist<'T>)
    : aval<bool> =
    let reduction =
      AdaptiveReduction.countPositive
      |> AdaptiveReduction.mapOut(fun c -> c <> 0)

    new ListReduceNode<'T, bool, int, bool>(list, predicate, reduction)

  /// <summary>Adaptively tests if every element satisfies the predicate (FDA <c>AList.forall</c> parity).</summary>
  let inline forall
    ([<InlineIfLambda>] predicate: 'T -> bool)
    (list: alist<'T>)
    : aval<bool> =
    new ListReduceNode<'T, bool, int, bool>(
      list,
      predicate,
      AdaptiveReduction.countNegative
      |> AdaptiveReduction.mapOut(fun c -> c = 0)
    )

  /// <summary>Adaptively counts the elements that satisfy the predicate (FDA <c>AList.countBy</c> parity).</summary>
  let inline countBy
    ([<InlineIfLambda>] predicate: 'T -> bool)
    (list: alist<'T>)
    : aval<int> =
    new ListReduceNode<'T, bool, int, int>(
      list,
      predicate,
      AdaptiveReduction.countPositive
    )

  /// <summary>Adaptively gets the minimum element, or <c>ValueNone</c> when empty (FDA <c>AList.tryMin</c> parity).</summary>
  let inline tryMin(list: alist<'T>) : aval<'T voption> =
    reduce (AdaptiveReduction.tryMin()) list

  /// <summary>Adaptively gets the maximum element, or <c>ValueNone</c> when empty (FDA <c>AList.tryMax</c> parity).</summary>
  let inline tryMax(list: alist<'T>) : aval<'T voption> =
    reduce (AdaptiveReduction.tryMax()) list

  /// <summary>Adaptively sums the elements (FDA <c>AList.sum</c> parity; needs an additive numeric type).</summary>
  let inline sum(list: alist<'T>) : aval<'T> =
    reduce (AdaptiveReduction.sum()) list

  /// <summary>Adaptively sums the mapped elements (FDA <c>AList.sumBy</c> parity).</summary>
  let inline sumBy
    ([<InlineIfLambda>] mapping: 'T -> 'U)
    (list: alist<'T>)
    : aval<'U> =
    reduceBy (AdaptiveReduction.sum()) mapping list

  /// <summary>
  /// Adaptively averages the elements (needs a numeric type with
  /// <c>DivideByInt</c>, e.g. <c>float</c>; FDA <c>AList.average</c> parity).
  /// </summary>
  let inline average(list: alist< ^T >) : aval< ^T > =
    AVal.map2
      (fun total c -> LanguagePrimitives.DivideByInt total c)
      (sum list)
      (count list)

  /// <summary>
  /// Adaptively averages the mapped elements (needs a numeric type with
  /// <c>DivideByInt</c>, e.g. <c>float</c>; FDA <c>AList.averageBy</c> parity).
  /// </summary>
  let inline averageBy
    ([<InlineIfLambda>] mapping: 'T -> ^U)
    (list: alist<'T>)
    : aval< ^U > =
    AVal.map2
      (fun total c -> LanguagePrimitives.DivideByInt total c)
      (sumBy mapping list)
      (count list)

  // =========================================================================
  // The *A reductions (docs/2026-08-05-MAPA-DESIGN.md §10): composition over
  // mapA/filterA + the existing reduction nodes. Value-only mapping
  // ('T -> aval<'U>), FDA parity: the mapped value follows the ELEMENT, not
  // the position (mapiA's mapping-time positions stick on shifts, so an
  // index-aware reduction would not track live positions). No new node
  // types. FDA argument order: reduction, mapping, list.
  // =========================================================================

  /// <summary>
  /// Adaptively reduces the list after mapping every element to an adaptive
  /// value (the AList counterpart of <c>ASet.reduceByA</c>). The mapped
  /// values keep their multiplicity (a list has no deduplication).
  /// </summary>
  let inline reduceByA
    (reduction: AdaptiveReduction<'U, 's, 'v>)
    ([<InlineIfLambda>] mapping: 'T -> aval<'U>)
    (list: alist<'T>)
    : aval<'v> =
    list |> mapA mapping |> reduceBy reduction id

  /// <summary>Adaptively counts the elements whose predicate aval holds <c>true</c> (the AList counterpart of <c>ASet.countByA</c>).</summary>
  let inline countByA
    ([<InlineIfLambda>] predicate: 'T -> aval<bool>)
    (list: alist<'T>)
    : aval<int> =
    list |> filterA predicate |> count

  /// <summary>Adaptively tests if any element's predicate aval holds <c>true</c> (the AList counterpart of <c>ASet.existsA</c>).</summary>
  let inline existsA
    ([<InlineIfLambda>] predicate: 'T -> aval<bool>)
    (list: alist<'T>)
    : aval<bool> =
    list |> countByA predicate |> AVal.map(fun c -> c <> 0)

  /// <summary>Adaptively tests if every element's predicate aval holds <c>true</c> (the AList counterpart of <c>ASet.forallA</c>).</summary>
  let inline forallA
    ([<InlineIfLambda>] predicate: 'T -> aval<bool>)
    (list: alist<'T>)
    : aval<bool> =
    list
    |> filterA(fun x -> AVal.map not (predicate x))
    |> count
    |> AVal.map(fun c -> c = 0)

  /// <summary>Adaptively sums the avals mapped from the elements (the AList counterpart of <c>ASet.sumByA</c>).</summary>
  let inline sumByA
    ([<InlineIfLambda>] mapping: 'T -> aval<'U>)
    (list: alist<'T>)
    : aval<'U> =
    reduceByA (AdaptiveReduction.sum()) mapping list

  /// <summary>
  /// Adaptively averages the avals mapped from the elements (needs a
  /// numeric type with <c>DivideByInt</c>, e.g. <c>float</c>; the AList
  /// counterpart of <c>ASet.averageByA</c>).
  /// </summary>
  let inline averageByA
    ([<InlineIfLambda>] mapping: 'T -> aval<'U>)
    (list: alist<'T>)
    : aval<'U> =
    AVal.map2
      (fun total count -> LanguagePrimitives.DivideByInt total count)
      (reduceByA (AdaptiveReduction.sum()) mapping list)
      (count list)

  /// <summary>
  /// Adaptively gets the minimum of the avals mapped from the elements, or
  /// <c>ValueNone</c> when empty (the voption counterpart of the <c>*A</c>
  /// family, mirroring <see cref="tryMin"/>).
  /// </summary>
  let inline tryMinA
    ([<InlineIfLambda>] mapping: 'T -> aval<'U>)
    (list: alist<'T>)
    : aval<'U voption> =
    reduceByA (AdaptiveReduction.tryMin()) mapping list

  /// <summary>
  /// Adaptively gets the maximum of the avals mapped from the elements, or
  /// <c>ValueNone</c> when empty (the voption counterpart of the <c>*A</c>
  /// family, mirroring <see cref="tryMax"/>).
  /// </summary>
  let inline tryMaxA
    ([<InlineIfLambda>] mapping: 'T -> aval<'U>)
    (list: alist<'T>)
    : aval<'U voption> =
    reduceByA (AdaptiveReduction.tryMax()) mapping list

  /// <summary>
  /// An adaptive list over an adaptive value of a sequence (FDA
  /// <c>AList.ofAVal</c> parity). Every change of the value replaces the
  /// whole state and emits the positional diff as the delta.
  /// </summary>
  let inline ofAVal<'T, 'S when 'S :> seq<'T>>(value: aval<'S>) : alist<'T> =
    new OfAvalListNode<'T, 'S>(value)

  /// <summary>
  /// An adaptive list generated from a count and a generator (FDA
  /// <c>AList.init</c> parity). The list is rebuilt when the count changes.
  /// </summary>
  let inline init
    ([<InlineIfLambda>] f: int -> 'T)
    (count: aval<int>)
    : alist<'T> =
    ofAVal(AVal.map (fun c -> Array.init c f) count)

  /// <summary>
  /// An adaptive numeric range as a list (FDA <c>AList.range</c> parity).
  /// The list is rebuilt when either bound changes; the bounds are inclusive.
  /// </summary>
  let inline range (min: aval< ^T >) (max: aval< ^T >) : alist< ^T > =
    ofAVal(AVal.map2 (fun lo hi -> seq { lo..hi }) min max)

  /// <summary>
  /// Adaptively looks up the element at the given position (FDA
  /// <c>AList.tryAt</c> parity; the position is the <c>int</c> input
  /// position, the positional deviation).
  /// </summary>
  /// <summary>
  /// Adaptively looks up the element at the given position (FDA
  /// <c>AList.tryAt</c> parity; the position is the <c>int</c> input
  /// position, the positional deviation). Per-position precise (O(1) on
  /// read) for a direct changeable source: an op that does not touch the
  /// position (an insert or remove after it, an update elsewhere) does not
  /// re-evaluate this value or its dependents. On a derived source the
  /// branch re-evaluates at most once per upstream change (pull-lazy: the
  /// gate runs at the next read's drain).
  /// </summary>
  let inline tryAt (index: int) (list: alist<'T>) : aval<'T voption> =
    new ListLookupNode<'T>(list, index)

  /// <summary>Alias of <see cref="tryAt"/> (FDA parity name; both take the <c>int</c> position).</summary>
  let inline tryGet (index: int) (list: alist<'T>) : aval<'T voption> =
    tryAt index list

  /// <summary>
  /// Adaptively gets the first element, or <c>ValueNone</c> when empty (FDA
  /// <c>AList.tryFirst</c> parity). Per-position precise, like
  /// <see cref="tryAt"/>.
  /// </summary>
  let inline tryFirst(list: alist<'T>) : aval<'T voption> =
    new ListLookupNode<'T>(list, 0)

  /// <summary>
  /// Adaptively gets the last element, or <c>ValueNone</c> when empty (FDA
  /// <c>AList.tryLast</c> parity). Per-position precise: only an op that
  /// changes the last element (append, remove or update of the last
  /// element) re-evaluates this value or its dependents.
  /// </summary>
  let inline tryLast(list: alist<'T>) : aval<'T voption> =
    new ListLastNode<'T>(list)

  /// <summary>
  /// Materializes the list as an adaptive value. Every change materializes a
  /// new array (the retain boundary, like <see cref="force"/>); the value
  /// is safe to retain (FDA <c>AList.toAVal</c> parity, as <c>aval&lt;'T[]&gt;</c>,
  /// the positional deviation).
  /// </summary>
  let inline toAVal(list: alist<'T>) : aval<'T[]> =
    AdaptiveNode<'T[]>(fun () -> Seq.toArray(list.GetValue()))

  /// <summary>
  /// An adaptive set of the list's elements, deduplicated (FDA
  /// <c>AList.toASet</c> parity). An element leaves the output only when its
  /// last occurrence leaves.
  /// </summary>
  let inline toASet(list: alist<'T>) : aset<'T> = new ToSetListNode<'T>(list)

  /// <summary>
  /// An adaptive set of the elements paired with their input positions
  /// (FDA <c>AList.toIndexedASet</c> parity; struct pairs, the library
  /// convention).
  /// </summary>
  let inline toIndexedASet(list: alist<'T>) : aset<struct (int * 'T)> =
    list |> indexed |> toASet

  /// <summary>
  /// An adaptive list of a set's elements (FDA <c>AList.ofASet</c> parity,
  /// poll node). The order is the set's iteration order, stable while the
  /// set does not change.
  /// </summary>
  let inline ofASet(set: aset<'T>) : alist<'T> = new SetToListNode<'T>(set)

  /// <summary>
  /// Reverses the list (FDA <c>AList.rev</c> parity, poll node).
  /// </summary>
  let inline rev(list: alist<'T>) : alist<'T> =
    new PollListSourceNode<'T, 'T>(
      list,
      fun view ->
        let next = ResizeArray<'T>(view.Count)

        for i in view.Count - 1 .. -1 .. 0 do
          next.Add view[i]

        next
    )

  /// <summary>
  /// Adaptively maps over the given value and returns the resulting list
  /// (FDA <c>AList.bind</c> parity). When the value changes, <c>mapping</c>
  /// selects the new inner list; the output is rebuilt on any change (the
  /// value's or the inner list's), emitting the positional diff.
  /// </summary>
  let inline bind
    ([<InlineIfLambda>] mapping: 'T -> alist<'U>)
    (value: aval<'T>)
    : alist<'U> =
    new BindListNode<'T, 'U>(value, mapping)

  /// <summary>
  /// Adaptively maps over the two values and returns the resulting list
  /// (FDA <c>AList.bind2</c> parity). Composed as one bind over the mapped
  /// pair (the ASet lesson: nested binds miss the inner bind's swap, which
  /// signals by version only, not by delta).
  /// </summary>
  let inline bind2
    ([<InlineIfLambda>] mapping: 'A -> 'B -> alist<'C>)
    (a: aval<'A>)
    (b: aval<'B>)
    : alist<'C> =
    bind (fun (av, bv) -> mapping av bv) (AVal.map2 (fun av bv -> (av, bv)) a b)

  /// <summary>
  /// Adaptively maps over the three values and returns the resulting list
  /// (FDA <c>AList.bind3</c> parity).
  /// </summary>
  let inline bind3
    ([<InlineIfLambda>] mapping: 'A -> 'B -> 'C -> alist<'D>)
    (a: aval<'A>)
    (b: aval<'B>)
    (c: aval<'C>)
    : alist<'D> =
    bind
      (fun (av, bv, cv) -> mapping av bv cv)
      (AVal.map3 (fun av bv cv -> (av, bv, cv)) a b c)

  /// <summary>
  /// Concatenates a fixed sequence of lists (FDA <c>AList.concat</c> parity,
  /// poll node; generalizes <see cref="append"/>).
  /// </summary>
  let inline concat(lists: #seq<alist<'T>>) : alist<'T> =
    new ConcatListNode<'T>(Seq.toArray lists)

  /// <summary>
  /// The window <c>[offset, offset + count)</c> of the list (FDA
  /// <c>AList.subA</c> parity, poll node; the bounds are adaptive).
  /// </summary>
  let inline subA
    (offset: aval<int>)
    (count: aval<int>)
    (list: alist<'T>)
    : alist<'T> =
    new PollListSourceNode<'T, 'T>(
      list,
      fun view ->
        let o = max 0 (offset.GetValue())
        let c = max 0 (count.GetValue())
        let start = min o view.Count
        let n = min c (view.Count - start)
        let next = ResizeArray<'T>(n)

        for i in start .. start + n - 1 do
          next.Add view[i]

        next
    )

  /// <summary>The window <c>[offset, offset + count)</c> of the list (FDA <c>AList.sub</c> parity).</summary>
  let inline sub (offset: int) (count: int) (list: alist<'T>) : alist<'T> =
    subA (AVal.constant offset) (AVal.constant count) list

  /// <summary>The first <c>count</c> elements (FDA <c>AList.takeA</c> parity).</summary>
  let inline takeA (count: aval<int>) (list: alist<'T>) : alist<'T> =
    subA (AVal.constant 0) count list

  /// <summary>The first <c>count</c> elements (FDA <c>AList.take</c> parity).</summary>
  let inline take (count: int) (list: alist<'T>) : alist<'T> =
    takeA (AVal.constant count) list

  /// <summary>All elements after the first <c>count</c> (FDA <c>AList.skipA</c> parity).</summary>
  let inline skipA (count: aval<int>) (list: alist<'T>) : alist<'T> =
    subA count (AVal.constant System.Int32.MaxValue) list

  /// <summary>All elements after the first <c>count</c> (FDA <c>AList.skip</c> parity).</summary>
  let inline skip (count: int) (list: alist<'T>) : alist<'T> =
    skipA (AVal.constant count) list

  /// <summary>
  /// Sorts the list with the given comparison (FDA <c>AList.sortWith</c>
  /// parity, stable, poll node).
  /// </summary>
  let inline sortWith
    ([<InlineIfLambda>] comparer: 'T -> 'T -> int)
    (list: alist<'T>)
    : alist<'T> =
    new SortListNode<'T, 'T>(list, (fun _ v -> v), comparer)

  /// <summary>Sorts the list ascending (FDA <c>AList.sort</c> parity, stable, poll node).</summary>
  let inline sort(list: alist<'T>) : alist<'T> = sortWith compare list

  /// <summary>Sorts the list descending (FDA <c>AList.sortDescending</c> parity, stable, poll node).</summary>
  let inline sortDescending(list: alist<'T>) : alist<'T> =
    sortWith (fun a b -> compare b a) list

  /// <summary>
  /// Sorts the list by the keys given by the projection (FDA
  /// <c>AList.sortBy</c> parity, stable, poll node).
  /// </summary>
  let inline sortBy
    ([<InlineIfLambda>] f: 'T -> 'K)
    (list: alist<'T>)
    : alist<'T> =
    new SortListNode<'T, 'K>(list, (fun _ v -> f v), compare)

  /// <summary>
  /// Sorts the list by the keys given by the projection, passing the input
  /// position to the projection (FDA <c>AList.sortByi</c> parity, stable,
  /// poll node; the index is the <c>int</c> input position).
  /// </summary>
  let inline sortByi
    ([<InlineIfLambda>] f: int -> 'T -> 'K)
    (list: alist<'T>)
    : alist<'T> =
    new SortListNode<'T, 'K>(list, f, compare)

  /// <summary>Sorts the list by the keys given by the projection, descending (FDA <c>AList.sortByDescending</c> parity).</summary>
  let inline sortByDescending
    ([<InlineIfLambda>] f: 'T -> 'K)
    (list: alist<'T>)
    : alist<'T> =
    new SortListNode<'T, 'K>(list, (fun _ v -> f v), fun a b -> compare b a)

  /// <summary>Sorts the list by the keys given by the projection, descending, index-aware (FDA <c>AList.sortByDescendingi</c> parity).</summary>
  let inline sortByDescendingi
    ([<InlineIfLambda>] f: int -> 'T -> 'K)
    (list: alist<'T>)
    : alist<'T> =
    new SortListNode<'T, 'K>(list, f, fun a b -> compare b a)

  /// <summary>
  /// An adaptive list of adjacent pairs (FDA <c>AList.pairwise</c> parity,
  /// poll node; struct pairs, the library convention).
  /// </summary>
  let inline pairwise(list: alist<'T>) : alist<struct ('T * 'T)> =
    new PollListSourceNode<'T, struct ('T * 'T)>(
      list,
      fun view ->
        let next = ResizeArray<struct ('T * 'T)>(max 0 (view.Count - 1))

        for i in 0 .. view.Count - 2 do
          next.Add(struct (view[i], view[i + 1]))

        next
    )

  /// <summary>
  /// An adaptive list of adjacent pairs, with the last element paired with
  /// the first (FDA <c>AList.pairwiseCyclic</c> parity, poll node).
  /// </summary>
  let inline pairwiseCyclic(list: alist<'T>) : alist<struct ('T * 'T)> =
    new PollListSourceNode<'T, struct ('T * 'T)>(
      list,
      fun view ->
        let next = ResizeArray<struct ('T * 'T)>(view.Count)

        for i in 0 .. view.Count - 1 do
          next.Add(struct (view[i], view[(i + 1) % view.Count]))

        next
    )

  /// <summary>
  /// Maps every element, disposing the mapped value when the element leaves
  /// its position (FDA <c>AList.mapUse</c> parity). The output is 1:1 with
  /// the input. Disposing the returned disposable disposes all live mapped
  /// values and clears the output.
  /// </summary>
  let inline mapUse
    ([<InlineIfLambda>] mapping: 'T -> 'W)
    (list: alist<'T>)
    : IDisposable * alist<'W> =
    let node = new MapUseListNode<'T, 'W>(list, fun _ v -> mapping v)
    (node :> IDisposable, node :> alist<'W>)

  /// <summary>
  /// Maps every element, passing the input position to the mapping and
  /// disposing the mapped value when the element leaves its position (FDA
  /// <c>AList.mapUsei</c> parity; the index is the <c>int</c> input
  /// position, the positional deviation).
  /// </summary>
  let inline mapUsei
    ([<InlineIfLambda>] mapping: int -> 'T -> 'W)
    (list: alist<'T>)
    : IDisposable * alist<'W> =
    let node = new MapUseListNode<'T, 'W>(list, mapping)
    (node :> IDisposable, node :> alist<'W>)

  /// <summary>
  /// Creates an adaptive list from an external snapshot function and an
  /// invalidate handle (FDA <c>AList.ofExternal</c> parity, MAPA-DESIGN
  /// §1.1). The snapshot runs at most once per invalidate, on the next read,
  /// and is diffed against the previous snapshot positionally (prefix/suffix,
  /// the <c>ChangeableList.ApplyDiff</c> algorithm); not invalidated → reads
  /// are O(1) and allocate nothing. The handle is O(1) to call and
  /// thread-safe (a foreign-thread call is posted to the owner context and
  /// applied at the next graph operation).
  /// </summary>
  /// <example>
  /// <code>
  /// let mutable current = ResizeArray [ 1; 2; 3 ]
  /// let list, invalidate = AList.ofExternal (fun () -&gt; current :&gt; IReadOnlyList&lt;_&gt;)
  /// current.RemoveAt 0
  /// invalidate ()
  /// let forced = AList.force list   // [ 2; 3 ]
  /// </code>
  /// </example>
  let inline ofExternal
    ([<InlineIfLambda>] snapshot: unit -> IReadOnlyList<'T>)
    : alist<'T> * (unit -> unit) =
    let node = new ExternalListNode<'T>(snapshot)
    (node :> alist<'T>, fun () -> node.Invalidate())

  /// <summary>
  /// An adaptive list driven by a compute function (FDA <c>AList.custom</c>
  /// parity, MAPA-DESIGN §1.3, pull model). The compute receives the current
  /// view and a delta builder; it appends the operations that describe the
  /// change since the previous call (for example, consuming its own event
  /// queue). The operations are positional and applied in order.
  /// </summary>
  /// <example>
  /// <code>
  /// let mutable offset = 0
  /// let list =
  ///     AList.custom (fun view delta -&gt;
  ///         // rebuild the whole view on each poll (the simplest compute)
  ///         if offset &lt;&gt; 0 then
  ///             for i in view.Count - 1 .. -1 .. 0 do
  ///                 delta.Remove i
  ///
  ///             for i in 0 .. view.Count - 1 do
  ///                 delta.Insert(i, i + offset)
  ///
  ///             offset &lt;- 0)
  /// </code>
  /// </example>
  let inline custom
    ([<InlineIfLambda>] compute:
      IReadOnlyList<'T> -> ListDeltaBuilder<'T> -> unit)
    : alist<'T> =
    new CustomListNode<'T>(compute)

/// <summary>Operations on changeable lists.</summary>
module CList =
  /// <summary>An empty changeable list.</summary>
  let inline empty<'T> : clist<'T> = new ChangeableList<'T>(Seq.empty)

  /// <summary>A changeable list with the given items.</summary>
  let inline ofSeq(items: seq<'T>) : clist<'T> = new ChangeableList<'T>(items)

  /// <summary>A changeable list with the given items.</summary>
  let inline ofArray(items: 'T[]) : clist<'T> = new ChangeableList<'T>(items)

  /// <summary>A changeable list with the given items.</summary>
  let inline ofList(items: 'T list) : clist<'T> = new ChangeableList<'T>(items)

  /// <summary>Appends an element at the end of the list.</summary>
  let inline append (value: 'T) (list: clist<'T>) = list.Append value

  /// <summary>Inserts an element at the start of the list.</summary>
  let inline prepend (value: 'T) (list: clist<'T>) = list.Prepend value

  /// <summary>Inserts an element before the element currently at the position.</summary>
  let inline insertAt (position: int) (value: 'T) (list: clist<'T>) =
    list.InsertAt(position, value)

  /// <summary>Removes the element currently at the position.</summary>
  let inline removeAt (position: int) (list: clist<'T>) = list.RemoveAt position

  /// <summary>Replaces the element currently at the position.</summary>
  let inline updateAt (position: int) (value: 'T) (list: clist<'T>) =
    list.UpdateAt(position, value)

  /// <summary>Removes the first occurrence of the value. No-op when absent.</summary>
  let inline remove (value: 'T) (list: clist<'T>) = list.Remove value

  /// <summary>
  /// Posts an append (the <c>cval.Post</c> handoff pattern): queues the
  /// operation and returns immediately. Safe from any thread. The owner
  /// thread applies the queued operations at the next graph operation
  /// (reads and writes auto-drain) or at <c>Posting.pump</c>, as one batch:
  /// one delta, one notification delivery. A burst is coalesced into a
  /// single handoff. The positions of the batch refer to the state built by
  /// its earlier ops and are validated when the batch applies.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CList.postAppend item items
  /// // owner thread: the next read applies the post automatically
  /// let view = AList.force items
  /// </code>
  /// </example>
  let inline postAppend (value: 'T) (list: clist<'T>) = list.PostAppend value

  /// <summary>Posts an insert at the start. Safe from any thread. See <see cref="postAppend"/> for the application contract.</summary>
  let inline postPrepend (value: 'T) (list: clist<'T>) = list.PostPrepend value

  /// <summary>Posts an insert before the element currently at the position. Safe from any thread. See <see cref="postAppend"/> for the application contract.</summary>
  let inline postInsertAt (position: int) (value: 'T) (list: clist<'T>) =
    list.PostInsertAt(position, value)

  /// <summary>Posts a remove at the position. Safe from any thread. See <see cref="postAppend"/> for the application contract.</summary>
  let inline postRemoveAt (position: int) (list: clist<'T>) =
    list.PostRemoveAt position

  /// <summary>Posts a replace at the position. Safe from any thread. See <see cref="postAppend"/> for the application contract.</summary>
  let inline postUpdateAt (position: int) (value: 'T) (list: clist<'T>) =
    list.PostUpdateAt(position, value)

  /// <summary>Posts a remove of the first occurrence of the value. Safe from any thread. See <see cref="postAppend"/> for the application contract.</summary>
  let inline postRemove (value: 'T) (list: clist<'T>) = list.PostRemove value

  /// <summary>Posts a clear. Safe from any thread. See <see cref="postAppend"/> for the application contract.</summary>
  let inline postClear(list: clist<'T>) = list.PostClear()

  /// <summary>
  /// Posts a full replace. Safe from any thread. See <see cref="postAppend"/>
  /// for the application contract; a posted replace supersedes the other ops
  /// of the same pending batch (the transaction semantics of <see cref="set"/>).
  /// </summary>
  let inline postSet (values: seq<'T>) (list: clist<'T>) = list.PostSet values

  /// <summary>Removes all elements.</summary>
  let inline clear(list: clist<'T>) = list.Clear()

  /// <summary>Replaces the whole list. Last-wins over the whole batch inside a transaction.</summary>
  let inline set (values: seq<'T>) (list: clist<'T>) = list.Set values

  /// <summary>
  /// Replaces the whole list and returns whether the content changed (FDA
  /// <c>clist.UpdateTo</c> parity). An equal target marks nothing.
  /// </summary>
  let inline updateTo (target: 'T[]) (list: clist<'T>) : bool =
    let view = AList.getValue list
    let mutable changed = view.Count <> target.Length

    if not changed then
      let mutable i = 0

      while not changed && i < target.Length do
        if not(EqualityComparer<'T>.Default.Equals(view[i], target[i])) then
          changed <- true

        i <- i + 1

    if changed then
      list.Set target

    changed

  /// <summary>
  /// Applies a batch of list operations (FDA <c>clist.Perform</c> parity). The
  /// operations are positional and applied in order; the batch is atomic
  /// (sinks receive one delta).
  /// </summary>
  let perform (delta: ListDeltaBuilder<'T>) (list: clist<'T>) : unit =
    let d = delta.Snapshot()

    if not d.IsEmpty then
      Transaction.run(fun () ->
        let ops = d.Operations

        for i in 0 .. ops.Length - 1 do
          let op = ops.Span[i]

          match op.Kind with
          | ListOpKind.Insert -> list.InsertAt(op.Position, op.Value)
          | ListOpKind.Remove -> list.RemoveAt op.Position
          | _ -> list.UpdateAt(op.Position, op.Value))

  /// <summary>Appends all the given elements (FDA <c>clist.AddRange</c> parity; one atomic batch).</summary>
  let inline addRange (items: seq<'T>) (list: clist<'T>) : unit =
    Transaction.run(fun () ->
      for x in items do
        list.Append x |> ignore)

  /// <summary>Views the changeable list as an adaptive list.</summary>
  let inline value(list: clist<'T>) : alist<'T> = list

  /// <summary>Materializes the current state as an immutable array snapshot.</summary>
  let inline force(list: clist<'T>) : 'T[] = AList.force list

  /// <summary>Materializes the F# <c>list</c> counterpart.</summary>
  let inline toList(list: clist<'T>) : 'T list = AList.toList list

/// <summary>
/// Slicing for adaptive lists (gap sheet §10.1): <c>list.[a..b]</c>. The
/// bounds are clamped; the slice is the window <c>[a, b]</c> inclusive.
/// </summary>
[<AutoOpen>]
module AListSliceExtensions =
  type IAdaptiveList<'T> with
    /// <summary>
    /// Slicing: <c>list.[a..b]</c> (gap sheet §10.1). The bounds are
    /// clamped; the slice is the window <c>[a, b]</c> inclusive.
    /// </summary>
    /// <example>
    /// <code>
    /// let items = CList.ofSeq [ 0; 1; 2; 3; 4 ]
    /// let middle = (CList.value items).[1..3]   // [ 1; 2; 3 ]
    /// </code>
    /// </example>
    member this.GetSlice(start: int option, finish: int option) : alist<'T> =
      let s = defaultArg start 0
      let f = defaultArg finish System.Int32.MaxValue
      AList.sub s (max 0 (f - s + 1)) this
