// Shares a collection with the main test module: the adaptive graph is
// confined to one owner thread (PLAN.md §7.1), so xUnit must not run this
// module's tests in parallel with the rest of the suite.
[<global.Xunit.Collection("Mibo.Adaptive")>]
module Mibo.Adaptive.Properties

// Test taxonomy:
// - "law:*" — one-shot algebraic law. FsCheck generates the input, the chain
//   is built once, forced, compared to a pure List/Set/Map function. Tests
//   the load path.
// - "matches the reference model" — the test owns a Dictionary/HashSet/
//   ResizeArray model, derives expected from it, compares after every op.
//   Tests the incremental path against an independent oracle.
// - "incremental law:*" — same shape as "matches the model" but for the
//   simple combinators (map, sort, rev, ...). The oracle is the test's own
//   model, not the library's read.

#nowarn "893"

open System
open System.Collections.Generic
open System.Threading.Tasks
open global.Xunit
open FsCheck
open FsCheck.FSharp
open Mibo.Adaptive

// =============================================================================
// Kipo usage shapes (E:\Kipo Pomo.Core\Projections.fs), split into small
// dedicated part tests. Part 1: the entity-scenario cross-map lookup
// (world.Scenarios |> AMap.tryFind inside AMap.mapA, then AMap.choose).
// =============================================================================

[<Fact>]
let ``part 1: cross-map lookup follows the entity-first sequence``() =
  let entities = CMap.empty<int, int> // entity -> lookup key
  let lookups = CMap.empty<int, int> // key -> value

  // should be empty here
  let contexts =
    entities
    |> AMap.mapA(fun _ key -> AMap.tryFind key lookups)
    |> AMap.chooseV(fun _ v -> v)

  // The shrunk counterexample of the failing property: [0; 2; -1].
  // op 0: upsert entity 0 -> lookup key 0 (lookups empty: no context yet).
  CMap.addOrUpdate 0 0 entities
  Assert.Equal<Map<int, int>>(Map.empty, AMap.toMap contexts)
  // op 2: upsert lookup 0 -> 0 (the context appears).
  CMap.addOrUpdate 0 0 lookups
  Assert.Equal<Map<int, int>>(Map.ofList [ 0, 0 ], AMap.toMap contexts)
  // op -1: remove lookup 0 (the context disappears).
  CMap.remove 0 lookups
  Assert.Equal<Map<int, int>>(Map.empty, AMap.toMap contexts)

// =============================================================================
// FsCheck property tests (FsCheck 3.x API, the built-in runner per the docs:
// Check.QuickThrowOnFailure inside plain xUnit facts). The reference-impl
// model tests (MAPA-DESIGN §12) live here.
// =============================================================================

// =============================================================================
// Algebraic laws over generated data. These are the real property tests: the
// law is a clean universal statement, FsCheck generates the data directly
// (built-in arbitraries), and a failure shrinks to a minimal concrete value.
// =============================================================================

[<Fact>]
let ``law: AList.map preserves the mapped content``() =
  let law(xs: int list) =
    let actual =
      xs |> AList.ofSeq |> AList.map((+) 1) |> AList.force |> List.ofArray

    let expected = xs |> List.map((+) 1)
    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AList.append concatenates``() =
  let law(a: int list, b: int list) =
    let actual =
      (AList.ofSeq a, AList.ofSeq b)
      ||> AList.append
      |> AList.force
      |> List.ofArray

    let expected = a @ b
    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AList.rev reverses and is involutive``() =
  let law(xs: int list) =
    let reversed = xs |> AList.ofSeq |> AList.rev |> AList.force |> List.ofArray

    let twice =
      xs |> AList.ofSeq |> AList.rev |> AList.rev |> AList.force |> List.ofArray

    reversed = List.rev xs && twice = xs

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AList.sort sorts and is idempotent``() =
  let law(xs: int list) =
    let sorted = xs |> AList.ofSeq |> AList.sort |> AList.force |> List.ofArray

    let twice =
      xs
      |> AList.ofSeq
      |> AList.sort
      |> AList.sort
      |> AList.force
      |> List.ofArray

    sorted = List.sort xs && twice = sorted

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AList.take truncates``() =
  let law(n: int, xs: int list) =
    let count = abs n % (List.length xs + 1)

    let actual =
      xs |> AList.ofSeq |> AList.take count |> AList.force |> List.ofArray

    let expected = xs |> List.truncate count
    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AList.skip skips and clamps``() =
  let law(n: int, xs: int list) =
    let count = abs n % (List.length xs + 2)

    let actual =
      xs |> AList.ofSeq |> AList.skip count |> AList.force |> List.ofArray

    let expected =
      if count >= List.length xs then
        []
      else
        xs |> List.skip count

    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AList.sub slices``() =
  let law(o: int, c: int, xs: int list) =
    let len = List.length xs
    let offset = abs o % (len + 1)
    let count = abs c % (len + 2)

    let actual =
      xs |> AList.ofSeq |> AList.sub offset count |> AList.force |> List.ofArray

    let expected =
      let from = min offset len
      let take = max 0 (min count (len - from))
      xs |> List.skip from |> List.truncate take

    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AList.pairwise pairs adjacent elements``() =
  let law(xs: int list) =
    let actual =
      xs |> AList.ofSeq |> AList.pairwise |> AList.force |> List.ofArray

    let expected = xs |> List.pairwise |> List.map(fun (a, b) -> struct (a, b))

    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AList.concat flattens``() =
  let law(xss: int list list) =
    let actual =
      xss |> List.map AList.ofSeq |> AList.concat |> AList.force |> List.ofArray

    let expected = xss |> List.concat
    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AList.choose selects``() =
  let law(xs: int list) =
    let f x =
      if x % 2 = 0 then Some(x * 10) else None

    let actual =
      xs |> AList.ofSeq |> AList.choose f |> AList.force |> List.ofArray

    let expected = xs |> List.choose f
    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AList.indexed pairs positions``() =
  let law(xs: int list) =
    let actual =
      xs |> AList.ofSeq |> AList.indexed |> AList.force |> List.ofArray

    let expected = xs |> List.indexed |> List.map(fun (i, x) -> struct (i, x))

    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AList.mapA with constants maps``() =
  let law(xs: int list) =
    let actual =
      xs
      |> AList.ofSeq
      |> AList.mapA(fun x -> AVal.constant(x * 2))
      |> AList.force
      |> List.ofArray

    let expected = xs |> List.map(fun x -> x * 2)
    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AList reductions match the forced content``() =
  let law(xs: int list) =
    let actualSum = xs |> AList.ofSeq |> AList.sum |> AVal.getValue
    let expectedSum = xs |> List.sum

    let actualEvenCount =
      xs |> AList.ofSeq |> AList.countBy(fun x -> x % 2 = 0) |> AVal.getValue

    let expectedEvenCount = xs |> List.filter(fun x -> x % 2 = 0) |> List.length

    let actualMin = xs |> AList.ofSeq |> AList.tryMin |> AVal.getValue

    let expectedMin =
      if List.isEmpty xs then
        ValueNone
      else
        ValueSome(List.min xs)

    let actualMax = xs |> AList.ofSeq |> AList.tryMax |> AVal.getValue

    let expectedMax =
      if List.isEmpty xs then
        ValueNone
      else
        ValueSome(List.max xs)

    actualSum = expectedSum
    && actualEvenCount = expectedEvenCount
    && actualMin = expectedMin
    && actualMax = expectedMax

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: ASet.map and filter preserve content``() =
  let law(s: Set<int>) =
    let actualMapped = s |> ASet.ofSeq |> ASet.map((+) 1) |> ASet.toSet
    let expectedMapped = s |> Set.map((+) 1)

    let actualFiltered =
      s |> ASet.ofSeq |> ASet.filter(fun x -> x % 2 = 0) |> ASet.toSet

    let expectedFiltered = s |> Set.filter(fun x -> x % 2 = 0)
    actualMapped = expectedMapped && actualFiltered = expectedFiltered

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: ASet union and intersect``() =
  let law(a: Set<int>, b: Set<int>) =
    let actualUnion = (ASet.ofSeq a, ASet.ofSeq b) ||> ASet.union |> ASet.toSet
    let expectedUnion = Set.union a b

    let actualIntersect =
      (ASet.ofSeq a, ASet.ofSeq b) ||> ASet.intersect |> ASet.toSet

    let expectedIntersect = Set.intersect a b
    actualUnion = expectedUnion && actualIntersect = expectedIntersect

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: ASet choose and count``() =
  let law(s: Set<int>) =
    let f x = if x % 3 = 0 then Some(x / 3) else None

    let actualChosen = s |> ASet.ofSeq |> ASet.choose f |> ASet.toSet
    let expectedChosen = s |> Seq.choose f |> Set.ofSeq

    let actualCount = s |> ASet.ofSeq |> ASet.count |> AVal.getValue
    let expectedCount = Set.count s
    actualChosen = expectedChosen && actualCount = expectedCount

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: ASet reductions over constant sources``() =
  let law(s: Set<int>) =
    let actualSum = s |> ASet.ofSeq |> ASet.sum |> AVal.getValue
    let expectedSum = s |> Seq.sum

    let actualCount =
      s |> ASet.ofSeq |> ASet.countBy(fun x -> x % 2 = 0) |> AVal.getValue

    let expectedCount = s |> Seq.filter(fun x -> x % 2 = 0) |> Seq.length
    actualSum = expectedSum && actualCount = expectedCount

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AMap reductions over constant sources``() =
  let law(m: Map<int, int>) =
    let actual =
      m
      |> Map.toSeq
      |> AMap.ofSeq
      |> AMap.fold (fun acc _ v -> acc + v) 0
      |> AVal.getValue

    let expected = m |> Map.toSeq |> Seq.sumBy snd
    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AMap *A reductions over constant sources``() =
  let law(m: Map<int, int>) =
    let byKey = fun k v -> AVal.constant(k + v)
    let even = fun _ v -> AVal.constant(v % 2 = 0)

    let actualSum =
      m |> Map.toSeq |> AMap.ofSeq |> AMap.sumByA byKey |> AVal.getValue

    let expectedSum = Map.fold (fun s k v -> s + k + v) 0 m

    let actualCount =
      m |> Map.toSeq |> AMap.ofSeq |> AMap.countByA even |> AVal.getValue

    let expectedCount = m |> Map.filter(fun _ v -> v % 2 = 0) |> Map.count
    actualSum = expectedSum && actualCount = expectedCount

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AList *A reductions over constant sources``() =
  let law(xs: int list) =
    let byValue = fun v -> AVal.constant(v * 2)
    let even = fun v -> AVal.constant(v % 2 = 0)

    let actualSum = xs |> AList.ofSeq |> AList.sumByA byValue |> AVal.getValue
    let expectedSum = xs |> List.map((*) 2) |> List.sum

    let actualMin = xs |> AList.ofSeq |> AList.tryMinA byValue |> AVal.getValue

    let expectedMin =
      if List.isEmpty xs then
        ValueNone
      else
        ValueSome(List.min(List.map ((*) 2) xs))

    let actualCount = xs |> AList.ofSeq |> AList.countByA even |> AVal.getValue
    let expectedCount = xs |> List.filter(fun v -> v % 2 = 0) |> List.length

    actualSum = expectedSum
    && actualMin = expectedMin
    && actualCount = expectedCount

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: ASet tryMinA and tryMaxA over constant sources``() =
  let law(s: Set<int>) =
    let mapping = fun x -> AVal.constant(x * 10)

    let actualMin = s |> ASet.ofSeq |> ASet.tryMinA mapping |> AVal.getValue

    let expectedMin =
      if Set.isEmpty s then
        ValueNone
      else
        ValueSome(Set.minElement s * 10)

    let actualMax = s |> ASet.ofSeq |> ASet.tryMaxA mapping |> AVal.getValue

    let expectedMax =
      if Set.isEmpty s then
        ValueNone
      else
        ValueSome(Set.maxElement s * 10)

    actualMin = expectedMin && actualMax = expectedMax

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: ASet chooseAV selects (voption form)``() =
  let law(s: Set<int>) =
    let choose = fun x -> if x % 2 = 0 then ValueSome(x * 10) else ValueNone

    let actual =
      s
      |> ASet.ofSeq
      |> ASet.chooseAV(fun x -> AVal.constant(choose x))
      |> ASet.toSet

    let expected = s |> Set.filter(fun x -> x % 2 = 0) |> Set.map((*) 10)
    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AMap chooseAV selects (voption form)``() =
  let law(m: Map<int, int>) =
    let choose = fun v -> if v % 2 = 0 then ValueSome(v * 10) else ValueNone

    let actual =
      m
      |> Map.toSeq
      |> AMap.ofSeq
      |> AMap.chooseAV(fun _ v -> AVal.constant(choose v))
      |> AMap.toMap

    let expected =
      m |> Map.filter(fun _ v -> v % 2 = 0) |> Map.map(fun _ v -> v * 10)

    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AList chooseAV and chooseiV select (voption forms)``() =
  let law(xs: int list) =
    let choose = fun x -> if x % 2 = 0 then ValueSome(x * 10) else ValueNone

    let actualAV =
      xs
      |> AList.ofSeq
      |> AList.chooseAV(fun x -> AVal.constant(choose x))
      |> AList.toList

    let expectedAV = xs |> List.filter(fun x -> x % 2 = 0) |> List.map((*) 10)

    // The index-aware form on a constant list maps at the final positions.
    let actualiV =
      xs
      |> AList.ofSeq
      |> AList.chooseiV(fun i x -> if i % 2 = 0 then choose x else ValueNone)
      |> AList.toList

    let expectediV =
      xs
      |> List.mapi(fun i x -> if i % 2 = 0 then choose x else ValueNone)
      |> List.filter ValueOption.isSome
      |> List.map ValueOption.get

    actualAV = expectedAV && actualiV = expectediV

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AMap tryMinA and tryMaxA over constant sources``() =
  let law(m: Map<int, int>) =
    let mapping = fun k v -> AVal.constant(k + v)
    let mapped = m |> Map.toSeq |> Seq.map(fun (k, v) -> k + v)

    let actualMin =
      m |> Map.toSeq |> AMap.ofSeq |> AMap.tryMinA mapping |> AVal.getValue

    let actualMax =
      m |> Map.toSeq |> AMap.ofSeq |> AMap.tryMaxA mapping |> AVal.getValue

    let expectedMin =
      if Seq.isEmpty mapped then
        ValueNone
      else
        ValueSome(Seq.min mapped)

    let expectedMax =
      if Seq.isEmpty mapped then
        ValueNone
      else
        ValueSome(Seq.max mapped)

    actualMin = expectedMin && actualMax = expectedMax

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: ASet mapA with constants maps``() =
  let law(s: Set<int>) =
    let actual =
      s |> ASet.ofSeq |> ASet.mapA(fun x -> AVal.constant(x * 2)) |> ASet.toSet

    let expected = s |> Set.map((*) 2)
    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AMap mapV and filter preserve content``() =
  let law(m: Map<int, int>) =
    let actualMapped =
      m |> Map.toSeq |> AMap.ofSeq |> AMap.mapV((+) 1) |> AMap.toMap

    let expectedMapped = m |> Map.map(fun _ v -> v + 1)

    let actualFiltered =
      m
      |> Map.toSeq
      |> AMap.ofSeq
      |> AMap.filter(fun _ v -> v % 2 = 0)
      |> AMap.toMap

    let expectedFiltered = m |> Map.filter(fun _ v -> v % 2 = 0)
    actualMapped = expectedMapped && actualFiltered = expectedFiltered

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AMap difference keeps left-only keys``() =
  let law(m: Map<int, int>, n: Map<int, int>) =
    let actual =
      AMap.difference (AMap.ofSeq(Map.toSeq m)) (AMap.ofSeq(Map.toSeq n))
      |> AMap.toMap

    let expected = m |> Map.filter(fun k _ -> not(Map.containsKey k n))
    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AMap keys and toASet``() =
  let law(m: Map<int, int>) =
    let actualKeys = m |> Map.toSeq |> AMap.ofSeq |> AMap.keys |> ASet.toSet
    let expectedKeys = m |> Map.keys |> Set.ofSeq

    let actualPairs = m |> Map.toSeq |> AMap.ofSeq |> AMap.toASet |> ASet.toSet

    let expectedPairs =
      m |> Map.toSeq |> Seq.map(fun (k, v) -> struct (k, v)) |> Set.ofSeq

    actualKeys = expectedKeys && actualPairs = expectedPairs

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AMap chooseV selects``() =
  let law(m: Map<int, int>) =
    let f (k: int) (v: int) =
      if v % 2 = 0 then ValueSome(k * 100 + v) else ValueNone

    let actual = m |> Map.toSeq |> AMap.ofSeq |> AMap.chooseV f |> AMap.toMap

    let expected =
      m
      |> Map.toSeq
      |> Seq.choose(fun (k, v) ->
        if v % 2 = 0 then Some(k, k * 100 + v) else None)
      |> Map.ofSeq

    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: changeable roundtrips``() =
  let law(xs: (int * int) list, s: Set<int>) =
    let actualMap = xs |> CMap.ofSeq |> CMap.value |> AMap.toMap
    let expectedMap = Map.ofSeq xs

    let actualSet = s |> CSet.ofSeq |> CSet.value |> ASet.toSet
    actualMap = expectedMap && actualSet = s

  Check.QuickThrowOnFailure law

[<Fact>]
let ``law: AVal constant, map and map2``() =
  let law(a: int, b: int) =
    let actualConstant = a |> AVal.constant |> AVal.getValue
    let actualMapped = a |> AVal.constant |> AVal.map((+) 1) |> AVal.getValue

    let actualMapped2 =
      (AVal.constant a, AVal.constant b)
      ||> AVal.map2(fun x y -> x * y)
      |> AVal.getValue

    actualConstant = a && actualMapped = a + 1 && actualMapped2 = a * b

  Check.QuickThrowOnFailure law

// =============================================================================
// Incremental laws: the same algebraic laws re-asserted after EVERY mutation
// of a changeable source. The one-shot laws above check the load path only;
// these check the incremental path (drain, deltas, version gates). The forced
// source content is the oracle; the mirrors exist only for position decoding.
// =============================================================================

/// Applies one op (kind = op % 3: insert / removeAt / updateAt) to a changeable
/// list and its mirror. The position is derived from the mirror length, so it
/// is always valid for the current state.
let applyListMutation
  (op: int)
  (l: ChangeableList<int>)
  (model: ResizeArray<int>)
  =
  let kind = op % 3
  let rest = op / 3
  let element = rest % 10

  let position =
    let p = (rest / 10) % (model.Count + 1)
    if p < 0 then p + (model.Count + 1) else p

  match kind with
  | 0 ->
    CList.insertAt position element l
    model.Insert(position, element)
  | 1 ->
    if model.Count > 0 && position < model.Count then
      CList.removeAt position l
      model.RemoveAt position
  | _ ->
    if model.Count > 0 && position < model.Count then
      CList.updateAt position element l
      model[position] <- element

/// Applies one op (kind = op % 2: add / remove) to a changeable set.
let applySetMutation (op: int) (s: ChangeableSet<int>) =
  let kind = op % 2
  let rest = op / 2
  let element = rest % 20

  match kind with
  | 0 -> CSet.add element s
  | _ -> CSet.remove element s

/// Applies one op (kind = op % 2: upsert / remove) to a changeable map.
let applyMapMutation (op: int) (m: ChangeableMap<int, int>) =
  let kind = op % 2
  let rest = op / 2
  let key = rest % 10
  let value = (rest / 10) % 100

  match kind with
  | 0 -> CMap.addOrUpdate key value m
  | _ -> CMap.remove key m

[<Fact>]
let ``incremental law: AList.sort stays sorted``() =
  let prop(ops: int list) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()
    let sorted = AList.sort(CList.value l)

    for op in ops do
      applyListMutation op l model

      let actual = AList.toList sorted
      let expected = model |> Seq.sort |> List.ofSeq

      if actual <> expected then
        failwithf
          "mismatch after op %A: actual=%A expected=%A"
          op
          actual
          expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AList.rev stays reversed``() =
  let prop(ops: int list) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()
    let reversed = AList.rev(CList.value l)

    for op in ops do
      applyListMutation op l model

      let actual = AList.toList reversed
      let expected = model |> Seq.rev |> List.ofSeq

      if actual <> expected then
        failwithf
          "mismatch after op %A: actual=%A expected=%A"
          op
          actual
          expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AList.map stays mapped``() =
  let prop(ops: int list) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()
    let mapped = AList.map ((+) 1) (CList.value l)

    for op in ops do
      applyListMutation op l model

      let actual = AList.toList mapped
      let expected = model |> Seq.map((+) 1) |> List.ofSeq

      if actual <> expected then
        failwithf
          "mismatch after op %A: actual=%A expected=%A"
          op
          actual
          expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AList.take stays truncated``() =
  let prop(ops: int list) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()
    let count = List.length ops % 5
    let taken = AList.take count (CList.value l)

    for op in ops do
      applyListMutation op l model

      let actual = AList.toList taken
      let expected = model |> Seq.truncate count |> List.ofSeq

      if actual <> expected then
        failwithf
          "mismatch after op %A: actual=%A expected=%A"
          op
          actual
          expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AList.skip stays skipped``() =
  let prop(ops: int list) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()
    let count = List.length ops % 5
    let skipped = AList.skip count (CList.value l)

    for op in ops do
      applyListMutation op l model

      let actual = AList.toList skipped

      let expected =
        if count >= model.Count then
          []
        else
          model |> Seq.skip count |> List.ofSeq

      if actual <> expected then
        failwithf
          "mismatch after op %A: actual=%A expected=%A"
          op
          actual
          expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AList.sub stays sliced``() =
  let prop(ops: int list) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()
    let offset = List.length ops % 3
    let count = List.length ops % 5
    let sliced = AList.sub offset count (CList.value l)

    for op in ops do
      applyListMutation op l model

      let actual = AList.toList sliced

      let expected =
        let from = min offset model.Count
        let take = max 0 (min count (model.Count - from))
        model |> Seq.skip from |> Seq.truncate take |> List.ofSeq

      if actual <> expected then
        failwithf
          "mismatch after op %A: actual=%A expected=%A"
          op
          actual
          expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AList.pairwise stays paired``() =
  let prop(ops: int list) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()
    let pairs = AList.pairwise(CList.value l)

    for op in ops do
      applyListMutation op l model

      let actual = AList.toList pairs

      let expected =
        model
        |> Seq.pairwise
        |> Seq.map(fun (a, b) -> struct (a, b))
        |> List.ofSeq

      if actual <> expected then
        failwithf
          "mismatch after op %A: actual=%A expected=%A"
          op
          actual
          expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AList.indexed stays indexed``() =
  let prop(ops: int list) =
    let l = CList.empty<int>
    // Mirror: element -> the index its mapping ran at (positions stick).
    let model = ResizeArray<struct (int * int)>()
    let indexed = AList.indexed(CList.value l)

    for op in ops do
      let kind = op % 3
      let rest = op / 3
      let element = rest % 10

      let position =
        let p = (rest / 10) % (model.Count + 1)
        if p < 0 then p + (model.Count + 1) else p

      match kind with
      | 0 ->
        CList.insertAt position element l
        model.Insert(position, struct (element, position))
      | 1 ->
        if model.Count > 0 && position < model.Count then
          CList.removeAt position l
          model.RemoveAt position
      | _ -> // Update: equal values are a no-op (no re-map, the index sticks).
        if model.Count > 0 && position < model.Count then
          let struct (oldE, _) = model[position]

          if oldE <> element then
            CList.updateAt position element l
            model[position] <- struct (element, position)

      let actual = AList.toList indexed

      let expected =
        model |> Seq.map(fun struct (e, i) -> struct (i, e)) |> List.ofSeq

      if actual <> expected then
        failwithf
          "mismatch after op %A: actual=%A expected=%A"
          op
          actual
          expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AList.mapA stays mapped``() =
  let prop(ops: int list) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()
    let mapped = AList.mapA (fun x -> AVal.constant(x * 2)) (CList.value l)

    for op in ops do
      applyListMutation op l model

      let actual = AList.toList mapped
      let expected = model |> Seq.map((*) 2) |> List.ofSeq

      if actual <> expected then
        failwithf
          "mismatch after op %A: actual=%A expected=%A"
          op
          actual
          expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AList.choose stays chosen``() =
  let prop(ops: int list) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()

    let f x =
      if x % 2 = 0 then Some(x * 10) else None

    let chosen = AList.choose f (CList.value l)

    for op in ops do
      applyListMutation op l model

      let actual = AList.toList chosen
      let expected = model |> Seq.choose f |> List.ofSeq

      if actual <> expected then
        failwithf
          "mismatch after op %A: actual=%A expected=%A"
          op
          actual
          expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AList reductions stay correct``() =
  let prop(ops: int list) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()
    let total = AList.sum(CList.value l)
    let evenCount = AList.countBy (fun x -> x % 2 = 0) (CList.value l)
    let acc = AList.fold (fun s x -> s * 10 + x) 0 (CList.value l)

    for op in ops do
      applyListMutation op l model

      let actualTotal = AVal.getValue total
      let expectedTotal = Seq.sum model

      let actualEvenCount = AVal.getValue evenCount

      let expectedEvenCount =
        model |> Seq.filter(fun x -> x % 2 = 0) |> Seq.length

      let actualAcc = AVal.getValue acc
      let expectedAcc = model |> Seq.fold (fun s x -> s * 10 + x) 0

      if
        actualTotal <> expectedTotal
        || actualEvenCount <> expectedEvenCount
        || actualAcc <> expectedAcc
      then
        failwithf
          "mismatch after op %A: total=%A evenCount=%A acc=%A"
          op
          actualTotal
          actualEvenCount
          actualAcc

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: ASet.map and filter stay correct``() =
  let prop(ops: int list) =
    let s = CSet.empty<int>
    let mapped = ASet.map ((+) 1) (CSet.value s)
    let filtered = ASet.filter (fun x -> x % 2 = 0) (CSet.value s)

    for op in ops do
      applySetMutation op s

      let source = Set.ofSeq(ASet.toSet(CSet.value s))

      let actualMapped = ASet.toSet mapped
      let expectedMapped = Set.map ((+) 1) source

      let actualFiltered = ASet.toSet filtered
      let expectedFiltered = Set.filter (fun x -> x % 2 = 0) source

      if
        actualMapped <> expectedMapped || actualFiltered <> expectedFiltered
      then
        failwithf
          "mismatch after op %A: mapped=%A filtered=%A"
          op
          actualMapped
          actualFiltered

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: ASet union and intersect stay correct``() =
  let prop(ops: int list) =
    let a = CSet.empty<int>
    let b = CSet.empty<int>
    let union = ASet.union (CSet.value a) (CSet.value b)
    let intersect = ASet.intersect (CSet.value a) (CSet.value b)

    for op in ops do
      let kind = op % 3
      let rest = op / 3
      let element = rest % 20
      let which = (rest / 20) % 2
      let target = if which = 0 then a else b

      match kind with
      | 0 -> CSet.add element target
      | 1 -> CSet.remove element target
      | _ -> () // no-op

      let sourceA = Set.ofSeq(ASet.toSet(CSet.value a))
      let sourceB = Set.ofSeq(ASet.toSet(CSet.value b))

      let actualUnion = ASet.toSet union
      let expectedUnion = Set.union sourceA sourceB

      let actualIntersect = ASet.toSet intersect
      let expectedIntersect = Set.intersect sourceA sourceB

      if
        actualUnion <> expectedUnion || actualIntersect <> expectedIntersect
      then
        failwithf
          "mismatch after op %A: union=%A intersect=%A"
          op
          actualUnion
          actualIntersect

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: ASet mapA and count stay correct``() =
  let prop(ops: int list) =
    let s = CSet.empty<int>
    let mapped = ASet.mapA (fun x -> AVal.constant(x * 2)) (CSet.value s)
    let count = ASet.count(CSet.value s)

    for op in ops do
      applySetMutation op s

      let source = Set.ofSeq(ASet.toSet(CSet.value s))

      let actualMapped = ASet.toSet mapped
      let expectedMapped = Set.map ((*) 2) source

      let actualCount = AVal.getValue count
      let expectedCount = Set.count source

      if actualMapped <> expectedMapped || actualCount <> expectedCount then
        failwithf
          "mismatch after op %A: mapped=%A count=%A"
          op
          actualMapped
          actualCount

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: ASet sum stays correct``() =
  let prop(ops: int list) =
    let s = CSet.empty<int>
    let total = ASet.sum(CSet.value s)

    for op in ops do
      applySetMutation op s

      let source = Set.ofSeq(ASet.toSet(CSet.value s))

      if AVal.getValue total <> Seq.sum source then
        failwithf "mismatch after op %A: total=%A" op (AVal.getValue total)

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AMap mapV and filter stay correct``() =
  let prop(ops: int list) =
    let m = CMap.empty<int, int>
    let mapped = AMap.mapV ((+) 1) (CMap.value m)
    let filtered = AMap.filter (fun _ v -> v % 2 = 0) (CMap.value m)

    for op in ops do
      applyMapMutation op m

      let source = AMap.toMap(CMap.value m)

      let actualMapped = AMap.toMap mapped
      let expectedMapped = Map.map (fun _ v -> v + 1) source

      let actualFiltered = AMap.toMap filtered
      let expectedFiltered = Map.filter (fun _ v -> v % 2 = 0) source

      if
        actualMapped <> expectedMapped || actualFiltered <> expectedFiltered
      then
        failwithf
          "mismatch after op %A: mapped=%A filtered=%A"
          op
          actualMapped
          actualFiltered

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AMap keys stay correct``() =
  let prop(ops: int list) =
    let m = CMap.empty<int, int>
    let keys = AMap.keys(CMap.value m)

    for op in ops do
      applyMapMutation op m

      let source = AMap.toMap(CMap.value m)

      let actual = ASet.toSet keys
      let expected = Set.ofSeq(Map.keys source)

      if actual <> expected then
        failwithf "mismatch after op %A: keys=%A" op actual

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AMap fold stays correct``() =
  let prop(ops: int list) =
    let m = CMap.empty<int, int>
    let total = AMap.fold (fun acc _ v -> acc + v) 0 (CMap.value m)

    for op in ops do
      applyMapMutation op m

      let source = AMap.toMap(CMap.value m)

      let actual = AVal.getValue total
      let expected = source |> Map.toSeq |> Seq.sumBy snd

      if actual <> expected then
        failwithf "mismatch after op %A: total=%A" op actual

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AMap *A reductions stay correct``() =
  let prop(ops: int list) =
    let m = CMap.empty<int, int>
    let even = fun _ v -> AVal.constant(v % 2 = 0)
    let byKey = fun k v -> AVal.constant(float(k + v)) // DivideByInt types: float

    let countEvens = AMap.countByA even (CMap.value m)
    let existsEvens = AMap.existsA even (CMap.value m)
    let forallEvens = AMap.forallA even (CMap.value m)
    let sum = AMap.sumByA byKey (CMap.value m)
    let average = AMap.averageByA byKey (CMap.value m)

    for op in ops do
      applyMapMutation op m

      let source = AMap.toMap(CMap.value m)

      let expectedCount =
        source |> Map.filter(fun _ v -> v % 2 = 0) |> Map.count

      let expectedSum = Map.fold (fun s k v -> s + float(k + v)) 0.0 source

      // DivideByInt on an empty map is NaN; compare NaN-aware.
      let expectedAverage = expectedSum / float(Map.count source)
      let actualAverage = AVal.getValue average

      if AVal.getValue countEvens <> expectedCount then
        failwithf "countByA after %A" op

      if AVal.getValue existsEvens <> (expectedCount > 0) then
        failwithf "existsA after %A" op

      if AVal.getValue forallEvens <> (expectedCount = Map.count source) then
        failwithf "forallA after %A" op

      if AVal.getValue sum <> expectedSum then
        failwithf "sumByA after %A" op

      if
        not(
          (Double.IsNaN actualAverage && Double.IsNaN expectedAverage)
          || actualAverage = expectedAverage
        )
      then
        failwithf
          "averageByA after %A: %A vs %A"
          op
          actualAverage
          expectedAverage

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AList *A reductions stay correct``() =
  let prop(ops: int list) =
    let l = CList.empty<int>
    let even = fun v -> AVal.constant(v % 2 = 0)
    let byValue = fun v -> AVal.constant(v * 2)

    let countEvens = AList.countByA even (CList.value l)
    let existsEvens = AList.existsA even (CList.value l)
    let forallEvens = AList.forallA even (CList.value l)
    let sum = AList.sumByA byValue (CList.value l)
    let min = AList.tryMinA byValue (CList.value l)
    let max = AList.tryMaxA byValue (CList.value l)
    let model = ResizeArray<int>()

    for op in ops do
      applyListMutation op l model

      let source = List.ofSeq model
      let mapped = source |> List.map((*) 2)

      let expectedCount =
        source |> List.filter(fun v -> v % 2 = 0) |> List.length

      let expectedSum = mapped |> List.sum

      let expectedMin =
        if List.isEmpty mapped then
          ValueNone
        else
          ValueSome(List.min mapped)

      let expectedMax =
        if List.isEmpty mapped then
          ValueNone
        else
          ValueSome(List.max mapped)

      if AVal.getValue countEvens <> expectedCount then
        failwithf "countByA after %A" op

      if AVal.getValue existsEvens <> (expectedCount > 0) then
        failwithf "existsA after %A" op

      if AVal.getValue forallEvens <> (expectedCount = List.length source) then
        failwithf "forallA after %A" op

      if AVal.getValue sum <> expectedSum then
        failwithf "sumByA after %A" op

      if AVal.getValue min <> expectedMin then
        failwithf "tryMinA after %A" op

      if AVal.getValue max <> expectedMax then
        failwithf "tryMaxA after %A" op

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: ASet chooseAV stays chosen``() =
  let prop(ops: int list) =
    let s = CSet.empty<int>
    let choose = fun x -> if x % 2 = 0 then ValueSome(x * 10) else ValueNone
    let chosen = ASet.chooseAV (fun x -> AVal.constant(choose x)) (CSet.value s)

    for op in ops do
      applySetMutation op s
      let source = Set.ofSeq(ASet.toSet(CSet.value s))
      let actual = ASet.toSet chosen
      let expected = source |> Set.filter(fun x -> x % 2 = 0) |> Set.map((*) 10)

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AMap chooseAV stays chosen``() =
  let prop(ops: int list) =
    let m = CMap.empty<int, int>
    let choose = fun v -> if v % 2 = 0 then ValueSome(v * 10) else ValueNone

    let chosen =
      AMap.chooseAV (fun _ v -> AVal.constant(choose v)) (CMap.value m)

    for op in ops do
      applyMapMutation op m
      let source = AMap.toMap(CMap.value m)
      let actual = AMap.toMap chosen

      let expected =
        source |> Map.filter(fun _ v -> v % 2 = 0) |> Map.map(fun _ v -> v * 10)

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AList chooseAV and chooseiV stay chosen``() =
  let prop(ops: int list) =
    let l = CList.empty<int>
    let choose = fun x -> if x % 2 = 0 then ValueSome(x * 10) else ValueNone

    // Position-independent mappings: chooseiV's mapping-time positions
    // stick on shifts (documented semantic), so the index-aware model
    // only holds for the constant case (see the static law).
    let chosen =
      AList.chooseAV (fun x -> AVal.constant(choose x)) (CList.value l)

    let choseni = AList.chooseiV (fun _ x -> choose x) (CList.value l)
    let model = ResizeArray<int>()

    for op in ops do
      applyListMutation op l model

      let source = List.ofSeq model

      let expected =
        source |> List.filter(fun x -> x % 2 = 0) |> List.map((*) 10)

      if AList.toList chosen <> expected then
        failwithf "chooseAV after %A" op

      if AList.toList choseni <> expected then
        failwithf "chooseiV after %A" op

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: AMap tryMinA and tryMaxA stay correct``() =
  let prop(ops: int list) =
    let m = CMap.empty<int, int>
    let mapping = fun k v -> AVal.constant(k + v)
    let min = AMap.tryMinA mapping (CMap.value m)
    let max = AMap.tryMaxA mapping (CMap.value m)

    for op in ops do
      applyMapMutation op m

      let mapped =
        AMap.toMap(CMap.value m) |> Map.toSeq |> Seq.map(fun (k, v) -> k + v)

      let expectedMin =
        if Seq.isEmpty mapped then
          ValueNone
        else
          ValueSome(Seq.min mapped)

      let expectedMax =
        if Seq.isEmpty mapped then
          ValueNone
        else
          ValueSome(Seq.max mapped)

      if AVal.getValue min <> expectedMin then
        failwithf "tryMinA after %A" op

      if AVal.getValue max <> expectedMax then
        failwithf "tryMaxA after %A" op

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``AMap chooseA and chooseAV agree``() =
  let m = CMap.empty<int, int>

  let opt =
    AMap.chooseA
      (fun _ v -> AVal.constant(if v % 2 = 0 then Some(v * 10) else None))
      (CMap.value m)

  let vopt =
    AMap.chooseAV
      (fun _ v ->
        AVal.constant(if v % 2 = 0 then ValueSome(v * 10) else ValueNone))
      (CMap.value m)

  CMap.addOrUpdate 1 2 m
  CMap.addOrUpdate 2 3 m

  if AMap.toMap opt <> AMap.toMap vopt then
    failwithf "init: %A vs %A" (AMap.toMap opt) (AMap.toMap vopt)

  CMap.addOrUpdate 1 5 m
  CMap.remove 2 m

  if AMap.toMap opt <> AMap.toMap vopt then
    failwithf "after ops: %A vs %A" (AMap.toMap opt) (AMap.toMap vopt)

// Tail-only reads of 2+ level chains (the stale-count report): the
// middle transforms are never read, so the tail's version gate must settle
// the whole chain from its own read. The oracle reads the changeable source
// only; it never touches the middle transforms.
[<Fact>]
let ``incremental law: tail-only map chain reads stay correct``() =
  let prop(ops: int list) =
    let m = CMap.empty<int, int>

    let cnt =
      CMap.value m
      |> AMap.map(fun _ v -> v * 2)
      |> AMap.filter(fun _ v -> v > 10)
      |> AMap.count

    AVal.getValue cnt |> ignore

    for op in ops do
      applyMapMutation op m

      let expected =
        AMap.toMap(CMap.value m)
        |> Map.filter(fun _ v -> v * 2 > 10)
        |> Map.count

      let actual = AVal.getValue cnt

      if actual <> expected then
        failwithf
          "mismatch after op %A: count=%A expected=%A"
          op
          actual
          expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``incremental law: tail-only set chain reads stay correct``() =
  let prop(ops: int list) =
    let s = CSet.empty<int>

    let cnt =
      CSet.value s
      |> ASet.map(fun v -> v * 2)
      |> ASet.filter(fun v -> v > 10)
      |> ASet.count

    AVal.getValue cnt |> ignore

    for op in ops do
      applySetMutation op s

      let expected =
        ASet.toSet(CSet.value s)
        |> Set.map(fun v -> v * 2)
        |> Set.filter(fun v -> v > 10)
        |> Set.count

      let actual = AVal.getValue cnt

      if actual <> expected then
        failwithf
          "mismatch after op %A: count=%A expected=%A"
          op
          actual
          expected

  Check.QuickThrowOnFailure prop

// =============================================================================
// Scalar combinators with a dedicated generator. The scalar layer (map,
// map2/3/4/N, bind/bind2/bind3) is the foundation of the collections (mapA
// mappings are avals, tryFind returns an aval); its dynamic behavior must be
// proven the same way. A typed op generator replaces the int decoding.
// =============================================================================

/// One scalar write: which input to set and to what value.
type ScalarOp = SetInput of inputIndex: int * value: int

/// Generates a sequence of scalar writes over four inputs, values in [0, 100).
let scalarOpsGen: Gen<ScalarOp list> =
  Gen.listOf(
    gen {
      let! idx = Gen.choose(0, 3)
      let! value = Gen.choose(0, 99)
      return SetInput(idx, value)
    }
  )

/// The registered arbitrary: FsCheck resolves the property parameter by type.
type ScalarArbs =
  static member ScalarOpList() : Arbitrary<ScalarOp list> =
    Arb.fromGen scalarOpsGen

let private scalarConfig = Config.Quick.WithArbitrary([| typeof<ScalarArbs> |])

[<Fact>]
let ``scalar map tracks its input``() =
  let prop(ops: ScalarOp list) =
    let inputs = [|
      CVal.create 0
      CVal.create 0
      CVal.create 0
      CVal.create 0
    |]

    let derived = AVal.map (fun x -> x * 2 + 1) (CVal.value inputs[0])
    let model = [| 0; 0; 0; 0 |]

    for op in ops do
      match op with
      | SetInput(i, v) ->
        CVal.set v inputs[i]
        model[i] <- v

      let actual = AVal.getValue derived
      let expected = model[0] * 2 + 1

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scalarConfig, prop)

[<Fact>]
let ``scalar map2 tracks its inputs``() =
  let prop(ops: ScalarOp list) =
    let inputs = [|
      CVal.create 0
      CVal.create 0
      CVal.create 0
      CVal.create 0
    |]

    let derived =
      AVal.map2
        (fun x y -> x * 10 + y)
        (CVal.value inputs[0])
        (CVal.value inputs[1])

    let model = [| 0; 0; 0; 0 |]

    for op in ops do
      match op with
      | SetInput(i, v) ->
        CVal.set v inputs[i]
        model[i] <- v

      let actual = AVal.getValue derived
      let expected = model[0] * 10 + model[1]

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scalarConfig, prop)

[<Fact>]
let ``scalar map3 tracks its inputs``() =
  let prop(ops: ScalarOp list) =
    let inputs = [|
      CVal.create 0
      CVal.create 0
      CVal.create 0
      CVal.create 0
    |]

    let derived =
      AVal.map3
        (fun x y z -> x * 100 + y * 10 + z)
        (CVal.value inputs[0])
        (CVal.value inputs[1])
        (CVal.value inputs[2])

    let model = [| 0; 0; 0; 0 |]

    for op in ops do
      match op with
      | SetInput(i, v) ->
        CVal.set v inputs[i]
        model[i] <- v

      let actual = AVal.getValue derived
      let expected = model[0] * 100 + model[1] * 10 + model[2]

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scalarConfig, prop)

[<Fact>]
let ``scalar map4 tracks its inputs``() =
  let prop(ops: ScalarOp list) =
    let inputs = [|
      CVal.create 0
      CVal.create 0
      CVal.create 0
      CVal.create 0
    |]

    let derived =
      AVal.map4
        (fun w x y z -> w * 1000 + x * 100 + y * 10 + z)
        (CVal.value inputs[0])
        (CVal.value inputs[1])
        (CVal.value inputs[2])
        (CVal.value inputs[3])

    let model = [| 0; 0; 0; 0 |]

    for op in ops do
      match op with
      | SetInput(i, v) ->
        CVal.set v inputs[i]
        model[i] <- v

      let actual = AVal.getValue derived
      let expected = model[0] * 1000 + model[1] * 100 + model[2] * 10 + model[3]

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scalarConfig, prop)

[<Fact>]
let ``scalar mapN tracks its inputs``() =
  let prop(ops: ScalarOp list) =
    let inputs = [|
      CVal.create 0
      CVal.create 0
      CVal.create 0
      CVal.create 0
    |]

    let derived =
      AVal.mapN
        (fun (arr: int[]) ->
          arr[0] * 1000 + arr[1] * 100 + arr[2] * 10 + arr[3])
        [|
          CVal.value inputs[0]
          CVal.value inputs[1]
          CVal.value inputs[2]
          CVal.value inputs[3]
        |]

    let model = [| 0; 0; 0; 0 |]

    for op in ops do
      match op with
      | SetInput(i, v) ->
        CVal.set v inputs[i]
        model[i] <- v

      let actual = AVal.getValue derived
      let expected = model[0] * 1000 + model[1] * 100 + model[2] * 10 + model[3]

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scalarConfig, prop)

[<Fact>]
let ``scalar bind tracks its input and its inner``() =
  let prop(ops: ScalarOp list) =
    let inputs = [|
      CVal.create 0
      CVal.create 0
      CVal.create 0
      CVal.create 0
    |]

    // The inner aval reads input 1; the bind's value (input 0) swaps it.
    let derived =
      AVal.bind
        (fun x -> AVal.map (fun y -> x * 10 + y) (CVal.value inputs[1]))
        (CVal.value inputs[0])

    let model = [| 0; 0; 0; 0 |]

    for op in ops do
      match op with
      | SetInput(i, v) ->
        CVal.set v inputs[i]
        model[i] <- v

      let actual = AVal.getValue derived
      let expected = model[0] * 10 + model[1]

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scalarConfig, prop)

[<Fact>]
let ``scalar bind2 tracks its inputs and its inner``() =
  let prop(ops: ScalarOp list) =
    let inputs = [|
      CVal.create 0
      CVal.create 0
      CVal.create 0
      CVal.create 0
    |]

    // The inner aval reads input 2; the bind's values (inputs 0, 1) swap it.
    let derived =
      AVal.bind2
        (fun x y ->
          AVal.map (fun z -> x * 100 + y * 10 + z) (CVal.value inputs[2]))
        (CVal.value inputs[0])
        (CVal.value inputs[1])

    let model = [| 0; 0; 0; 0 |]

    for op in ops do
      match op with
      | SetInput(i, v) ->
        CVal.set v inputs[i]
        model[i] <- v

      let actual = AVal.getValue derived
      let expected = model[0] * 100 + model[1] * 10 + model[2]

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scalarConfig, prop)

[<Fact>]
let ``scalar bind3 tracks its inputs and its inner``() =
  let prop(ops: ScalarOp list) =
    let inputs = [|
      CVal.create 0
      CVal.create 0
      CVal.create 0
      CVal.create 0
    |]

    // The inner aval reads input 3; the bind's values (inputs 0, 1, 2) swap it.
    let derived =
      AVal.bind3
        (fun x y z ->
          AVal.map
            (fun w -> x * 1000 + y * 100 + z * 10 + w)
            (CVal.value inputs[3]))
        (CVal.value inputs[0])
        (CVal.value inputs[1])
        (CVal.value inputs[2])

    let model = [| 0; 0; 0; 0 |]

    for op in ops do
      match op with
      | SetInput(i, v) ->
        CVal.set v inputs[i]
        model[i] <- v

      let actual = AVal.getValue derived
      let expected = model[0] * 1000 + model[1] * 100 + model[2] * 10 + model[3]

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scalarConfig, prop)

// =============================================================================
// Collection scenario generators: typed, weighted op sequences with a random
// initial state, registered as FsCheck arbitraries. The differential
// properties consume the scenarios instead of decoding int lists; the
// initial state exercises the load path, the ops the drain path.
// =============================================================================

type SetOp =
  | Add of int * int // element, initial aval value
  | Remove of int
  | SetValue of int * int // element, new aval value

type MapOp =
  | Upsert of int * int
  | Remove of int
  | SetValue of int * int // key, new aval value

type ListChange =
  | Insert of int * int // element, position payload
  | RemoveAt of int // position payload
  | UpdateAt of int * int // element, position payload
  | SetValue of int * int // element, new aval value

type CrossOp =
  | EntityUpsert of int * int
  | EntityRemove of int
  | LookupUpsert of int * int
  | LookupRemove of int

type JoinOp =
  | LeftEdit of MapOp
  | RightEdit of MapOp

type ExternalOp =
  | Replace of int
  | Invalidate

type SetScenario = {
  initial: (int * int) list
  ops: SetOp list
}

type MapScenario = {
  initial: (int * int) list
  ops: MapOp list
}

type ListScenario = {
  initial: (int * int) list
  ops: ListChange list
}

/// A two-list scenario for bind/concat: each op targets one of the two
/// lists, and Switch selects the bound list. Case names avoid ListChange's
/// (F# resolves colliding union case names to the last-defined type).
type TwoListOp =
  | InsertInto of element: int * payload: int * listIndex: int
  | RemoveFrom of payload: int * listIndex: int
  | UpdateIn of element: int * payload: int * listIndex: int
  | Switch of listIndex: int

type TwoListScenario = {
  initialA: (int * int) list
  initialB: (int * int) list
  ops: TwoListOp list
}

/// Applies one typed list change to a changeable list and its mirror. The
/// position is derived from the mirror length, so it is always valid for the
/// current state; SetValue has no list-content effect.
let applyListChange
  (op: ListChange)
  (l: ChangeableList<int>)
  (model: ResizeArray<int>)
  =
  match op with
  | Insert(element, payload) ->
    let position = payload % (model.Count + 1)
    CList.insertAt position element l
    model.Insert(position, element)
  | RemoveAt payload ->
    let position = payload % (model.Count + 1)

    if model.Count > 0 && position < model.Count then
      CList.removeAt position l
      model.RemoveAt position
  | UpdateAt(element, payload) ->
    let position = payload % (model.Count + 1)

    if model.Count > 0 && position < model.Count then
      CList.updateAt position element l
      model[position] <- element
  | ListChange.SetValue _ -> ()

type CrossScenario = {
  initialEntities: (int * int) list
  initialLookups: (int * int) list
  ops: CrossOp list
}

type JoinScenario = {
  initialA: (int * int) list
  initialB: (int * int) list
  ops: JoinOp list
}

type ExternalScenario = { ops: ExternalOp list }

/// One op on the effect-core map feeding a groupBy -> mapA(fold) -> join
/// chain (the issue-#113 shape). The payload doubles as the group key
/// (payload % 3), so upserts move cores between groups and removals empty
/// them.
type ChainCoreOp =
  | CoreUpsert of key: int * payload: int
  | CoreRemove of key: int

/// Equipment-side edits of the outer join's right map (join key 1 only).
type ChainEquipOp =
  | EquipUpsert of payload: int
  | EquipRemove

type ChainOp =
  | CoreEdit of ChainCoreOp
  | EquipEdit of ChainEquipOp

let chainCoreOpGen: Gen<ChainCoreOp> =
  Gen.frequency [
    (4,
     gen {
       let! k = Gen.choose(0, 5)
       let! v = Gen.choose(0, 99)
       return CoreUpsert(k, v)
     })
    (3,
     gen {
       let! k = Gen.choose(0, 5)
       return CoreRemove k
     })
  ]

let chainOpGen: Gen<ChainOp> =
  Gen.frequency [
    (8, Gen.map CoreEdit chainCoreOpGen)
    (1,
     gen {
       let! v = Gen.choose(0, 99)
       return EquipEdit(EquipUpsert v)
     })
    (1, Gen.constant(EquipEdit EquipRemove))
  ]

let pairGen = gen {
  let! k = Gen.choose(0, 19)
  let! v = Gen.choose(0, 99)
  return k, v
}

/// Deduplicates the initial pairs with the last value winning (the set/map
/// semantics), keeping the generated initial state valid for both sides.
let dedupPairs(xs: ('k * 'v) list) =
  let d = Dictionary<'k, 'v>()

  for (k, v) in xs do
    d[k] <- v

  [ for KeyValue(k, v) in d -> k, v ]

let setOpGen: Gen<SetOp> =
  Gen.frequency [
    (3,
     gen {
       let! e = Gen.choose(0, 19)
       let! v = Gen.choose(0, 99)
       return Add(e, v)
     })
    (2,
     gen {
       let! e = Gen.choose(0, 19)
       return SetOp.Remove e
     })
    (2,
     gen {
       let! e = Gen.choose(0, 19)
       let! v = Gen.choose(0, 99)
       return SetOp.SetValue(e, v)
     })
  ]

let mapOpGen: Gen<MapOp> =
  Gen.frequency [
    (3,
     gen {
       let! k = Gen.choose(0, 9)
       let! v = Gen.choose(0, 99)
       return Upsert(k, v)
     })
    (2,
     gen {
       let! k = Gen.choose(0, 9)
       return MapOp.Remove k
     })
    (2,
     gen {
       let! k = Gen.choose(0, 9)
       let! v = Gen.choose(0, 99)
       return MapOp.SetValue(k, v)
     })
  ]

let listChangeGen: Gen<ListChange> =
  Gen.frequency [
    (3,
     gen {
       let! e = Gen.choose(0, 9)
       let! p = Gen.choose(0, 200)
       return Insert(e, p)
     })
    (2,
     gen {
       let! p = Gen.choose(0, 200)
       return RemoveAt p
     })
    (2,
     gen {
       let! e = Gen.choose(0, 9)
       let! p = Gen.choose(0, 200)
       return UpdateAt(e, p)
     })
    (1,
     gen {
       let! e = Gen.choose(0, 9)
       let! v = Gen.choose(0, 99)
       return ListChange.SetValue(e, v)
     })
  ]

let crossOpGen: Gen<CrossOp> =
  Gen.frequency [
    (2,
     gen {
       let! k = Gen.choose(0, 9)
       let! v = Gen.choose(0, 99)
       return EntityUpsert(k, v)
     })
    (1,
     gen {
       let! k = Gen.choose(0, 9)
       return EntityRemove k
     })
    (2,
     gen {
       let! k = Gen.choose(0, 9)
       let! v = Gen.choose(0, 99)
       return LookupUpsert(k, v)
     })
    (1,
     gen {
       let! k = Gen.choose(0, 9)
       return LookupRemove k
     })
  ]

let joinOpGen: Gen<JoinOp> =
  Gen.frequency [
    (2, Gen.map LeftEdit mapOpGen)
    (2, Gen.map RightEdit mapOpGen)
  ]

let externalOpGen: Gen<ExternalOp> =
  Gen.frequency [
    (3,
     gen {
       let! v = Gen.choose(0, 99)
       return Replace v
     })
    (1, Gen.constant Invalidate)
  ]

let setScenarioGen: Gen<SetScenario> = gen {
  let! initial = Gen.listOf pairGen
  let! ops = Gen.listOf setOpGen

  return {
    initial = dedupPairs initial
    ops = ops
  }
}

let mapScenarioGen: Gen<MapScenario> = gen {
  let! initial = Gen.listOf pairGen
  let! ops = Gen.listOf mapOpGen

  return {
    initial = dedupPairs initial
    ops = ops
  }
}

let listScenarioGen: Gen<ListScenario> = gen {
  let! initial = Gen.listOf pairGen
  let! ops = Gen.listOf listChangeGen
  return { initial = initial; ops = ops }
}

let twoListOpGen: Gen<TwoListOp> =
  Gen.frequency [
    (3,
     gen {
       let! e = Gen.choose(0, 9)
       let! p = Gen.choose(0, 200)
       let! w = Gen.choose(0, 1)
       return InsertInto(e, p, w)
     })
    (2,
     gen {
       let! p = Gen.choose(0, 200)
       let! w = Gen.choose(0, 1)
       return TwoListOp.RemoveFrom(p, w)
     })
    (2,
     gen {
       let! e = Gen.choose(0, 9)
       let! p = Gen.choose(0, 200)
       let! w = Gen.choose(0, 1)
       return TwoListOp.UpdateIn(e, p, w)
     })
    (1,
     gen {
       let! w = Gen.choose(0, 1)
       return TwoListOp.Switch w
     })
  ]

let twoListScenarioGen: Gen<TwoListScenario> = gen {
  let! initialA = Gen.listOf pairGen
  let! initialB = Gen.listOf pairGen
  let! ops = Gen.listOf twoListOpGen

  return {
    initialA = initialA
    initialB = initialB
    ops = ops
  }
}

let crossScenarioGen: Gen<CrossScenario> = gen {
  let! initialEntities = Gen.listOf pairGen
  let! initialLookups = Gen.listOf pairGen
  let! ops = Gen.listOf crossOpGen

  return {
    initialEntities = dedupPairs initialEntities
    initialLookups = dedupPairs initialLookups
    ops = ops
  }
}

let joinScenarioGen: Gen<JoinScenario> = gen {
  let! initialA = Gen.listOf pairGen
  let! initialB = Gen.listOf pairGen
  let! ops = Gen.listOf joinOpGen

  return {
    initialA = dedupPairs initialA
    initialB = dedupPairs initialB
    ops = ops
  }
}

let externalScenarioGen: Gen<ExternalScenario> = gen {
  let! ops = Gen.listOf externalOpGen
  return { ops = ops }
}

/// The registered arbitraries: FsCheck resolves the property parameters by type.
type ScenarioArbs =
  static member SetScenario() : Arbitrary<SetScenario> =
    Arb.fromGen setScenarioGen

  static member MapScenario() : Arbitrary<MapScenario> =
    Arb.fromGen mapScenarioGen

  static member ListScenario() : Arbitrary<ListScenario> =
    Arb.fromGen listScenarioGen

  static member TwoListScenario() : Arbitrary<TwoListScenario> =
    Arb.fromGen twoListScenarioGen

  static member CrossScenario() : Arbitrary<CrossScenario> =
    Arb.fromGen crossScenarioGen

  static member JoinScenario() : Arbitrary<JoinScenario> =
    Arb.fromGen joinScenarioGen

  static member ExternalScenario() : Arbitrary<ExternalScenario> =
    Arb.fromGen externalScenarioGen

  static member SetOpList() : Arbitrary<SetOp list> =
    Arb.fromGen(Gen.listOf setOpGen)

  static member MapOpList() : Arbitrary<MapOp list> =
    Arb.fromGen(Gen.listOf mapOpGen)

  static member ListChangeList() : Arbitrary<ListChange list> =
    Arb.fromGen(Gen.listOf listChangeGen)

  static member ChainOpList() : Arbitrary<ChainOp list> =
    Arb.fromGen(Gen.listOf chainOpGen)

  static member ChainCoreOpList() : Arbitrary<ChainCoreOp list> =
    Arb.fromGen(Gen.listOf chainCoreOpGen)

let private scenarioConfig =
  Config.QuickThrowOnFailure.WithArbitrary([| typeof<ScenarioArbs> |])

// =============================================================================
// Reference-impl model: ASet.mapA (MAPA-DESIGN §12).
//
// The scenario generator provides a random initial state and a weighted op
// sequence; the model tracks element -> value plus a value refcount (the
// output set dedups); the library output is compared to the model after
// EVERY op.
// =============================================================================

[<Fact>]
let ``ASet mapA matches the reference model``() =
  let prop(sc: SetScenario) =
    let source = CSet.empty<int>
    let values = Dictionary<int, cval<int>>()

    for (e, v) in sc.initial do
      CSet.add e source
      values[e] <- CVal.create v

    let mapped = ASet.mapA (fun v -> CVal.value values[v]) (CSet.value source)
    // Model: element -> current value; value -> occurrence count; the output.
    let elementValue = Dictionary<int, int>()
    let valueRefs = Dictionary<int, int>()
    let model = HashSet<int>()

    for (e, v) in sc.initial do
      elementValue[e] <- v

      match valueRefs.TryGetValue v with
      | true, r -> valueRefs[v] <- r + 1
      | false, _ ->
        valueRefs[v] <- 1
        model.Add v |> ignore

    let apply(op: SetOp) =
      match op with
      | Add(element, value) -> // Add the element with a fresh aval holding the value.
        CSet.add element source

        if not(elementValue.ContainsKey element) then
          values[element] <- CVal.create value
          elementValue[element] <- value

          match valueRefs.TryGetValue value with
          | true, r -> valueRefs[value] <- r + 1
          | false, _ ->
            valueRefs[value] <- 1
            model.Add value |> ignore
      | SetOp.Remove element -> // Remove the element.
        CSet.remove element source

        if elementValue.ContainsKey element then
          let v = elementValue[element]
          elementValue.Remove element |> ignore
          values.Remove element |> ignore
          let r = valueRefs[v] - 1

          if r = 0 then
            valueRefs.Remove v |> ignore
            model.Remove v |> ignore
          else
            valueRefs[v] <- r
      | SetOp.SetValue(element, value) -> // Set the element's aval.
        if elementValue.ContainsKey element then
          let old = elementValue[element]

          if old <> value then
            let r = valueRefs[old] - 1

            if r = 0 then
              valueRefs.Remove old |> ignore
              model.Remove old |> ignore
            else
              valueRefs[old] <- r

            CVal.set value (values[element])
            elementValue[element] <- value

            match valueRefs.TryGetValue value with
            | true, r2 -> valueRefs[value] <- r2 + 1
            | false, _ ->
              valueRefs[value] <- 1
              model.Add value |> ignore

    for op in sc.ops do
      apply op

      let actual = Set.ofSeq(ASet.toSet mapped)
      let expected = Set.ofSeq model

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

// =============================================================================
// Reference-impl model: AList.mapA (MAPA-DESIGN §12).
//
// The scenario generator provides a random initial state and a weighted op
// sequence; the model tracks the element list (the output is a list:
// duplicates survive) plus element -> value; a re-inserted element reuses
// its aval (one aval per element).
// =============================================================================

[<Fact>]
let ``AList mapA matches the reference model``() =
  let prop(sc: ListScenario) =
    let source = CList.empty<int>
    let values = Dictionary<int, cval<int>>()

    for (e, v) in sc.initial do
      CList.append e source
      values[e] <- CVal.create v

    let mapped = AList.mapA (fun v -> CVal.value values[v]) (CList.value source)
    // Model: the element list in order, and the element -> value map.
    let elements = ResizeArray<int>(List.map fst sc.initial)
    let elementValue = Dictionary<int, int>()

    for (e, v) in sc.initial do
      elementValue[e] <- v

    let apply(op: ListChange) =
      match op with
      | Insert(element, payload) ->
        let position = payload % (elements.Count + 1)

        // Insert the element at the position; a fresh element gets a fresh aval.
        if not(elementValue.ContainsKey element) then
          values[element] <- CVal.create 0
          elementValue[element] <- 0

        CList.insertAt position element source
        elements.Insert(position, element)
      | RemoveAt payload ->
        let position = payload % (elements.Count + 1)

        if elements.Count > 0 && position < elements.Count then
          CList.removeAt position source
          elements.RemoveAt position
      | UpdateAt(element, payload) ->
        let position = payload % (elements.Count + 1)

        if elements.Count > 0 && position < elements.Count then
          if not(elementValue.ContainsKey element) then
            values[element] <- CVal.create 0
            elementValue[element] <- 0

          CList.updateAt position element source
          elements[position] <- element
      | ListChange.SetValue(element, value) -> // Set the element's aval.
        if elementValue.ContainsKey element then
          CVal.set value (values[element])
          elementValue[element] <- value

    for op in sc.ops do
      apply op

      let actual = AList.toList mapped
      let expected = elements |> Seq.map(fun e -> elementValue[e]) |> List.ofSeq

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

// =============================================================================
// Reference-impl model: AMap.mapA.
//
// The scenario generator provides a random initial state and a weighted op
// sequence; a fresh key gets a fresh aval; the model tracks key -> value.
// =============================================================================

[<Fact>]
let ``AMap mapA matches the reference model``() =
  let prop(sc: MapScenario) =
    let source = CMap.empty<int, int>
    let values = Dictionary<int, cval<int>>()

    for (k, v) in sc.initial do
      CMap.addOrUpdate k v source
      values[k] <- CVal.create v

    let mapped = AMap.mapA (fun k _ -> CVal.value values[k]) (CMap.value source)
    let model = Dictionary<int, int>()

    for (k, v) in sc.initial do
      model[k] <- v

    let apply(op: MapOp) =
      match op with
      | Upsert(key, value) -> // AddOrUpdate the key. The output follows the key's aval:
        // a fresh key gets a fresh aval, an existing key keeps it
        // (the mapping ignores the map value here).
        CMap.addOrUpdate key value source

        if not(model.ContainsKey key) then
          values[key] <- CVal.create value
          model[key] <- value
      | MapOp.Remove key -> // Remove the key.
        CMap.remove key source

        if model.ContainsKey key then
          model.Remove key |> ignore
      | MapOp.SetValue(key, value) -> // Set the key's aval.
        if model.ContainsKey key then
          CVal.set value (values[key])
          model[key] <- value

    for op in sc.ops do
      apply op

      let actual = AMap.toMap mapped
      let expected = Map.ofSeq(seq { for KeyValue(k, v) in model -> k, v })

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

// =============================================================================
// Reference-impl model: ASet.filterA and ASet.chooseA.
//
// Ops encoded as an int list: kind = op % 3 (0 Add, 1 Remove, 2 Set flag);
// element = rest % 10; the flag payload = (rest / 10) % 2. The filter model
// is the membership AND the flag; the choose model maps the element to
// ValueSome value when the flag holds, else ValueNone.
// =============================================================================

[<Fact>]
let ``ASet filterA matches the reference model``() =
  let prop(sc: SetScenario) =
    let source = CSet.empty<int>
    let flags = Dictionary<int, cval<bool>>()

    for (e, v) in sc.initial do
      CSet.add e source
      flags[e] <- CVal.create(v % 2 = 0)

    let filtered =
      ASet.filterA (fun v -> CVal.value flags[v]) (CSet.value source)

    let present = HashSet<int>(List.map fst sc.initial)
    let flagValue = Dictionary<int, bool>()

    for (e, v) in sc.initial do
      flagValue[e] <- v % 2 = 0

    let apply(op: SetOp) =
      match op with
      | Add(element, value) -> // Add the element with a fresh flag aval.
        let flag = value % 2 = 0
        CSet.add element source

        if present.Add element then
          flags[element] <- CVal.create flag
          flagValue[element] <- flag
      | SetOp.Remove element -> // Remove the element.
        CSet.remove element source
        present.Remove element |> ignore
      | SetOp.SetValue(element, value) -> // Set the element's flag aval.
        let flag = value % 2 = 0

        if present.Contains element then
          CVal.set flag (flags[element])
          flagValue[element] <- flag

    for op in sc.ops do
      apply op

      let actual = Set.ofSeq(ASet.toSet filtered)

      let expected =
        Set.ofSeq(
          seq {
            for e in present do
              if flagValue[e] then
                e
          }
        )

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``ASet chooseA matches the reference model``() =
  let prop(sc: SetScenario) =
    let source = CSet.empty<int>
    let flags = Dictionary<int, cval<bool>>()

    for (e, v) in sc.initial do
      CSet.add e source
      flags[e] <- CVal.create(v % 2 = 0)

    let chosen =
      ASet.chooseA
        (fun v ->
          AVal.map
            (fun f -> if f then Some(v * 10) else None)
            (CVal.value flags[v]))
        (CSet.value source)

    let present = HashSet<int>(List.map fst sc.initial)
    let flagValue = Dictionary<int, bool>()

    for (e, v) in sc.initial do
      flagValue[e] <- v % 2 = 0

    let apply(op: SetOp) =
      match op with
      | Add(element, value) -> // Add the element with a fresh flag aval.
        let flag = value % 2 = 0
        CSet.add element source

        if present.Add element then
          flags[element] <- CVal.create flag
          flagValue[element] <- flag
      | SetOp.Remove element -> // Remove the element.
        CSet.remove element source
        present.Remove element |> ignore
      | SetOp.SetValue(element, value) -> // Set the element's flag aval.
        let flag = value % 2 = 0

        if present.Contains element then
          CVal.set flag (flags[element])
          flagValue[element] <- flag

    for op in sc.ops do
      apply op

      let actual = Set.ofSeq(ASet.toSet chosen)

      let expected =
        Set.ofSeq(
          seq {
            for e in present do
              if flagValue[e] then
                e * 10
          }
        )

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

// =============================================================================
// Reference-impl model: ASet.ofExternal (MAPA-DESIGN §1.1).
//
// Ops encoded as an int list: kind = op % 2 (0 = replace the external
// snapshot, 1 = invalidate). The model: the library re-reads the snapshot
// only when invalidated, on the next read; the expected content is the
// snapshot when the model is dirty, else the last read snapshot.
// =============================================================================

[<Fact>]
let ``ASet ofExternal matches the reference model``() =
  let prop(sc: ExternalScenario) =
    let mutable snapshot = HashSet<int>()

    let ext, invalidate =
      ASet.ofExternal(fun () -> snapshot :> IReadOnlySet<int>)
    // Model: whether an invalidate is pending, and the last read snapshot.
    // The first read always re-reads (the node materializes initially).
    let mutable dirty = true
    let mutable lastSeen = HashSet<int>()

    let apply(op: ExternalOp) =
      match op with
      | Replace value -> // Replace the external snapshot with a single element.
        snapshot <- HashSet<int>([ value ])

        if value % 4 = 0 then
          snapshot.Add(value / 2) |> ignore // sometimes two elements
      | Invalidate -> // Invalidate: the next read re-reads the snapshot.
        invalidate()
        dirty <- true

      // Read after every op; the expected is the snapshot iff dirty.
      let expected = if dirty then snapshot else lastSeen
      let actual = ASet.toSet ext

      if Set.ofSeq actual <> Set.ofSeq expected then
        false
      else
        dirty <- false
        lastSeen <- HashSet<int>(expected)
        true

    for op in sc.ops do
      if not(apply op) then
        failwithf "mismatch after %A" op

  Check.One(scenarioConfig, prop)

// =============================================================================
// Kipo usage shapes (E:\Kipo Pomo.Core\Projections.fs):
//
// 1. The derived-stats join: AMap.choose2V over two maps + AMap.mapA.
// 2. The entity-scenario context: AMap.mapA whose mapping does a cross-map
//    lookup (world.Scenarios |> AMap.tryFind), then AMap.choose drops the
//    Nones.
// 3. The live-entities projection: AMap.filter + AMap.keys.
// 4. The physics cache: force the world maps, then compute per-scenario
//    snapshots (the RefreshAllCaches shape).
// 5. The nearby-entities query: cell-based radius query over the forced
//    snapshot vs the brute-force distance filter.
// 6. The combat statuses: AList.choose over the effect list.
// =============================================================================

[<Fact>]
let ``AMap join choose2V with mapA matches the model``() =
  let prop(sc: JoinScenario) =
    let baseStats = CMap.empty<int, int>
    let effects = CMap.empty<int, int>

    for (k, v) in sc.initialA do
      CMap.addOrUpdate k v baseStats

    for (k, v) in sc.initialB do
      CMap.addOrUpdate k v effects

    let derived =
      (CMap.value baseStats, CMap.value effects)
      ||> AMap.choose2V(fun _ av bv ->
        match struct (av, bv) with
        | ValueSome x, ValueSome y -> ValueSome(struct (x, y))
        | _ -> ValueNone)
      |> AMap.mapA(fun _ struct (x, y) -> AVal.constant(x + y))

    let modelA = Dictionary<int, int>()
    let modelB = Dictionary<int, int>()

    for (k, v) in sc.initialA do
      modelA[k] <- v

    for (k, v) in sc.initialB do
      modelB[k] <- v

    let apply(op: JoinOp) =
      match op with
      | LeftEdit(Upsert(key, value)) -> // Upsert into the base-stats map.
        CMap.addOrUpdate key value baseStats
        modelA[key] <- value
      | LeftEdit(MapOp.Remove key) -> // Remove from the base-stats map.
        CMap.remove key baseStats
        modelA.Remove key |> ignore
      | LeftEdit(MapOp.SetValue _) -> ()
      | RightEdit(Upsert(key, value)) -> // Upsert into the effects map.
        CMap.addOrUpdate key value effects
        modelB[key] <- value
      | RightEdit(MapOp.Remove key) -> // Remove from the effects map.
        CMap.remove key effects
        modelB.Remove key |> ignore
      | RightEdit(MapOp.SetValue _) -> ()

    for op in sc.ops do
      apply op

      let actual = AMap.toMap derived

      let expected =
        Map.ofSeq(
          seq {
            for KeyValue(k, a) in modelA do
              match modelB.TryGetValue k with
              | true, b -> k, a + b
              | false, _ -> ()
          }
        )

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AMap joinOn matches the model under both-side mutation``() =
  let prop(sc: JoinScenario) =
    let left = CMap.empty<int, int>
    let right = CMap.empty<int, int>

    for (k, v) in sc.initialA do
      CMap.addOrUpdate k v left

    for (k, v) in sc.initialB do
      CMap.addOrUpdate k v right

    let joined =
      AMap.joinOn
        (CMap.value left)
        (CMap.value right)
        (fun _ v -> v % 7)
        (fun  // join key from the value: updates churn the key
             _
             lV
             rV ->
          AVal.map2
            (fun l r ->
              match r with
              | ValueSome rv -> ValueSome(l + rv)
              | ValueNone -> ValueNone) // inner join: drop on a missing right side
            lV
            rV)

    let modelA = Dictionary<int, int>()
    let modelB = Dictionary<int, int>()

    for (k, v) in sc.initialA do
      modelA[k] <- v

    for (k, v) in sc.initialB do
      modelB[k] <- v

    let apply(op: JoinOp) =
      match op with
      | LeftEdit(Upsert(key, value)) ->
        CMap.addOrUpdate key value left
        modelA[key] <- value
      | LeftEdit(MapOp.Remove key) ->
        CMap.remove key left
        modelA.Remove key |> ignore
      | LeftEdit(MapOp.SetValue _) -> ()
      | RightEdit(Upsert(key, value)) ->
        CMap.addOrUpdate key value right
        modelB[key] <- value
      | RightEdit(MapOp.Remove key) ->
        CMap.remove key right
        modelB.Remove key |> ignore
      | RightEdit(MapOp.SetValue _) -> ()

    for op in sc.ops do
      apply op

      let actual = AMap.toMap joined

      let expected =
        Map.ofSeq(
          seq {
            for KeyValue(k, a) in modelA do
              match modelB.TryGetValue(a % 7) with
              | true, b -> k, a + b
              | false, _ -> ()
          }
        )

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AMap difference matches the model under both-side mutation``() =
  let prop(sc: JoinScenario) =
    let left = CMap.empty<int, int>
    let right = CMap.empty<int, int>

    for (k, v) in sc.initialA do
      CMap.addOrUpdate k v left

    for (k, v) in sc.initialB do
      CMap.addOrUpdate k v right

    let diff = AMap.difference (CMap.value left) (CMap.value right)
    let modelA = Dictionary<int, int>()
    let modelB = Dictionary<int, int>()

    for (k, v) in sc.initialA do
      modelA[k] <- v

    for (k, v) in sc.initialB do
      modelB[k] <- v

    let apply(op: JoinOp) =
      match op with
      | LeftEdit(Upsert(key, value)) ->
        CMap.addOrUpdate key value left
        modelA[key] <- value
      | LeftEdit(MapOp.Remove key) ->
        CMap.remove key left
        modelA.Remove key |> ignore
      | LeftEdit(MapOp.SetValue _) -> ()
      | RightEdit(Upsert(key, value)) ->
        CMap.addOrUpdate key value right
        modelB[key] <- value
      | RightEdit(MapOp.Remove key) ->
        CMap.remove key right
        modelB.Remove key |> ignore
      | RightEdit(MapOp.SetValue _) -> ()

    for op in sc.ops do
      apply op

      let actual = AMap.toMap diff

      let expected =
        Map.ofSeq(
          seq {
            for KeyValue(k, v) in modelA do
              if not(modelB.ContainsKey k) then
                k, v
          }
        )

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AMap *A reductions follow adaptive predicates``() =
  let m = CMap.empty<int, int>
  let threshold = CVal.create 50
  CMap.addOrUpdate 1 10 m
  CMap.addOrUpdate 2 60 m
  CMap.addOrUpdate 3 90 m

  let over =
    AMap.countByA
      (fun _ v -> AVal.map (fun t -> v > t) (CVal.value threshold))
      (CMap.value m)

  let sum =
    AMap.sumByA
      (fun _ v -> AVal.map (fun t -> v + t) (CVal.value threshold))
      (CMap.value m)

  if AVal.getValue over <> 2 then
    failwithf "over 50: %d" (AVal.getValue over)

  if AVal.getValue sum <> 310 then
    failwithf "sum with 50: %d" (AVal.getValue sum)

  CVal.set 80 threshold

  if AVal.getValue over <> 1 then
    failwithf "over 80: %d" (AVal.getValue over)

  if AVal.getValue sum <> 400 then
    failwithf "sum with 80: %d" (AVal.getValue sum)

[<Fact>]
let ``AMap groupBy groups by the computed key``() =
  let law(m: Map<int, int>) =
    let materialize(g: amap<int, amap<int, int>>) =
      AMap.toMap g |> Map.map(fun _ child -> AMap.toMap child)

    let actual =
      AMap.ofSeq(Map.toSeq m) |> AMap.groupBy(fun _ v -> v % 3) |> materialize

    let expected =
      Map.toSeq m
      |> Seq.groupBy(fun (_, v) -> v % 3)
      |> Seq.map(fun (g, xs) -> g, Map.ofSeq xs)
      |> Map.ofSeq

    actual = expected

  Check.QuickThrowOnFailure law

[<Fact>]
let ``incremental law: AMap groupBy stays grouped``() =
  let prop(ops: int list) =
    let materialize(g: amap<int, amap<int, int>>) =
      AMap.toMap g |> Map.map(fun _ child -> AMap.toMap child)

    let m = CMap.empty<int, int>
    let grouped = AMap.groupBy (fun _ v -> v % 3) (CMap.value m)

    for op in ops do
      applyMapMutation op m

      let source = AMap.toMap(CMap.value m)
      let actual = materialize grouped

      let expected =
        Map.toSeq source
        |> Seq.groupBy(fun (_, v) -> v % 3)
        |> Seq.map(fun (g, xs) -> g, Map.ofSeq xs)
        |> Map.ofSeq

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``AMap groupBy moves entries between groups and drops empty groups``() =
  let materialize(g: amap<int, amap<int, int>>) =
    AMap.toMap g |> Map.map(fun _ child -> AMap.toMap child)

  let m = CMap.empty<int, int>
  let grouped = AMap.groupBy (fun _ v -> v % 3) (CMap.value m)

  CMap.addOrUpdate 1 1 m // group 1
  CMap.addOrUpdate 2 2 m // group 2
  CMap.addOrUpdate 3 6 m // group 0
  let v1 = materialize grouped

  if
    v1
    <> Map.ofList [
      0, Map.ofList [ 3, 6 ]
      1, Map.ofList [ 1, 1 ]
      2, Map.ofList [ 2, 2 ]
    ]
  then
    failwithf "init: %A" v1

  CMap.addOrUpdate 1 3 m // move key 1: group 1 -> 0; group 1 becomes empty and disappears
  let v2 = materialize grouped

  if
    v2 <> Map.ofList [ 0, Map.ofList [ 1, 3; 3, 6 ]; 2, Map.ofList [ 2, 2 ] ]
  then
    failwithf "move: %A" v2

  CMap.remove 3 m // group 0 loses 3 but keeps 1
  let v3 = materialize grouped

  if v3 <> Map.ofList [ 0, Map.ofList [ 1, 3 ]; 2, Map.ofList [ 2, 2 ] ] then
    failwithf "remove member: %A" v3

  CMap.remove 1 m // group 0 becomes empty and disappears; group 2 remains
  let v4 = materialize grouped

  if v4 <> Map.ofList [ 2, Map.ofList [ 2, 2 ] ] then
    failwithf "after group 0 drops: %A" v4

  CMap.remove 2 m // the last group disappears: nothing left
  let v5 = materialize grouped

  if not v5.IsEmpty then
    failwithf "all gone: %A" v5

[<Fact>]
let ``AMap groupBy feeds adaptive consumers per group``() =
  let m = CMap.empty<int, int>
  let grouped = AMap.groupBy (fun _ v -> v % 2) (CMap.value m)
  let counts = AMap.mapA (fun _ g -> AMap.count g) grouped

  CMap.addOrUpdate 1 1 m // group 1
  CMap.addOrUpdate 2 2 m // group 0
  let c1 = AMap.toMap counts

  if c1 <> Map.ofList [ 0, 1; 1, 1 ] then
    failwithf "counts init: %A" c1

  CMap.addOrUpdate 3 4 m // group 0 grows
  let c2 = AMap.toMap counts

  if c2 <> Map.ofList [ 0, 2; 1, 1 ] then
    failwithf "counts grow: %A" c2

  CMap.remove 1 m // group 1 empty: the group key disappears from the output
  let c3 = AMap.toMap counts

  if c3 <> Map.ofList [ 0, 2 ] then
    failwithf "counts drop: %A" c3

  CMap.addOrUpdate 1 5 m // group 1 reappears with the new member
  let c4 = AMap.toMap counts

  if c4 <> Map.ofList [ 0, 2; 1, 1 ] then
    failwithf "counts reappear: %A" c4

[<Fact>]
let ``AMap joinOn builds each key's subgraph once and swaps inputs in place``
  ()
  =
  let left = CMap.empty<int, int>
  let right = CMap.empty<int, int>
  CMap.addOrUpdate 0 100 right
  let mutable calls = 0

  let joined =
    AMap.joinOn
      (CMap.value left)
      (CMap.value right)
      (fun _ _ -> 0)
      (fun  // stable join key: the swap path is the hot path
           _
           lV
           rV ->
        calls <- calls + 1

        AVal.map2
          (fun l r ->
            match r with
            | ValueSome rv -> ValueSome(l + rv)
            | ValueNone -> ValueNone)
          lV
          rV)

  CMap.addOrUpdate 1 10 left
  let v1 = AMap.toMap joined
  let callsAfterInit = calls

  CMap.addOrUpdate 1 11 left // in-place swap: no mapping re-run
  let v2 = AMap.toMap joined
  let callsAfterUpdate = calls

  AMap.toMap joined |> ignore // clean re-read: nothing runs
  let callsAfterClean = calls

  CMap.remove 1 left // removal drops the entry and its subgraph
  let v3 = AMap.toMap joined
  CMap.addOrUpdate 2 12 left // new key: the mapping runs again
  let v4 = AMap.toMap joined
  let callsAfterReadd = calls

  if v1 <> Map.ofList [ 1, 110 ] then
    failwithf "init: %A" v1

  if v2 <> Map.ofList [ 1, 111 ] then
    failwithf "update: %A" v2

  if callsAfterInit <> 1 || callsAfterUpdate <> 1 || callsAfterClean <> 1 then
    failwithf
      "mapping re-ran: init=%d update=%d clean=%d"
      callsAfterInit
      callsAfterUpdate
      callsAfterClean

  if not v3.IsEmpty then
    failwithf "removal: %A" v3

  if v4 <> Map.ofList [ 2, 112 ] then
    failwithf "re-add: %A" v4

  if callsAfterReadd <> 2 then
    failwithf "re-add did not rebuild: %d" callsAfterReadd

[<Fact>]
let ``AMap joinOn regression: add-then-remove and remove-then-add between reads``
  ()
  =
  let left = CMap.empty<int, int>
  let right = CMap.empty<int, int>
  CMap.addOrUpdate 0 100 right

  let joined =
    AMap.joinOn
      (CMap.value left)
      (CMap.value right)
      (fun _ _ -> 0)
      (fun _ lV rV ->
        AVal.map2
          (fun l r -> ValueSome(l + (r |> ValueOption.defaultValue 0)))
          lV
          rV)

  // Add-then-remove between reads: the journal nets to nothing.
  CMap.addOrUpdate 1 10 left
  CMap.remove 1 left
  let afterNet = AMap.toMap joined

  if not afterNet.IsEmpty then
    failwithf "add-then-remove: %A" afterNet

  // Remove-then-add: the entry must be present with the last value.
  CMap.addOrUpdate 1 10 left
  CMap.remove 1 left
  CMap.addOrUpdate 1 11 left
  let afterReadd = AMap.toMap joined

  if afterReadd <> Map.ofList [ 1, 111 ] then
    failwithf "remove-then-add: %A" afterReadd

  // Update-then-remove: nothing remains.
  CMap.addOrUpdate 1 12 left
  CMap.remove 1 left
  let afterUpdateRemove = AMap.toMap joined

  if not afterUpdateRemove.IsEmpty then
    failwithf "update-then-remove: %A" afterUpdateRemove

[<Fact>]
let ``AMap joinOn re-joins when the join key changes``() =
  let left = CMap.empty<int, int>
  let right = CMap.empty<int, int>
  CMap.addOrUpdate 1 100 right // join key 1 only; join key 0 is missing until the catch-up

  let joined =
    AMap.joinOn
      (CMap.value left)
      (CMap.value right)
      (fun _ v -> v % 7)
      (fun  // value 1 -> key 1; value 8 -> key 1; value 14 -> key 0
           _
           lV
           rV ->
        AVal.map2
          (fun l r ->
            match r with
            | ValueSome rv -> ValueSome(l + rv)
            | ValueNone -> ValueNone)
          lV
          rV)

  CMap.addOrUpdate 1 1 left // join key 1: 1 + 100
  let v1 = AMap.toMap joined
  CMap.addOrUpdate 1 8 left // join key still 1: swap path, 8 + 100
  let v2 = AMap.toMap joined
  CMap.addOrUpdate 1 14 left // join key 0: right missing -> dropped
  let v3 = AMap.toMap joined
  CMap.addOrUpdate 0 21 right // right catch-up: the entry re-joins via the scan
  let v4 = AMap.toMap joined
  CMap.addOrUpdate 1 1 left // back to join key 1: re-point, 1 + 100
  let v5 = AMap.toMap joined

  if v1 <> Map.ofList [ 1, 101 ] then
    failwithf "init: %A" v1

  if v2 <> Map.ofList [ 1, 108 ] then
    failwithf "same-key update: %A" v2

  if not v3.IsEmpty then
    failwithf "key change to missing: %A" v3

  if v4 <> Map.ofList [ 1, 35 ] then
    failwithf "right catch-up: %A" v4

  if v5 <> Map.ofList [ 1, 101 ] then
    failwithf "key change back: %A" v5

[<Fact>]
let ``AMap joinOn defers transaction writes until commit``() =
  let left = CMap.empty<int, int>
  let right = CMap.empty<int, int>
  CMap.addOrUpdate 0 100 right
  CMap.addOrUpdate 1 10 left

  let joined =
    AMap.joinOn
      (CMap.value left)
      (CMap.value right)
      (fun _ _ -> 0)
      (fun _ lV rV ->
        AVal.map2
          (fun l r -> ValueSome(l + (r |> ValueOption.defaultValue 0)))
          lV
          rV)

  let before = AMap.toMap joined

  let inside =
    Transaction.run(fun () ->
      CMap.addOrUpdate 1 20 left
      AMap.toMap joined) // reads inside see pre-transaction values

  let after = AMap.toMap joined

  if before <> Map.ofList [ 1, 110 ] then
    failwithf "before: %A" before

  if inside <> Map.ofList [ 1, 110 ] then
    failwithf "inside: %A" inside

  if after <> Map.ofList [ 1, 120 ] then
    failwithf "after: %A" after

[<Fact>]
let ``AMap joinOn composes into a 3-way join``() =
  // The 3-way join shape: left entries -> (middle row) -> (outer row),
  // with the middle map's values updating every iteration.
  let left = CMap.empty<int, int> // left id -> middle key
  let middle = CMap.empty<int, int> // middle key -> value
  let outer = CMap.empty<int, int> // middle key -> outer id

  CMap.addOrUpdate 1 10 middle
  CMap.addOrUpdate 1 7 outer
  CMap.addOrUpdate 5 1 left // left 5 points at middle key 1

  let join1 =
    AMap.joinOn
      left // left: id -> middle key
      middle // middle: key -> value
      (fun _ key -> key) // join key: the middle key from the left value
      (fun _ keyV valueV ->
        AVal.map2 (fun t p -> ValueSome(struct (t, p))) keyV valueV)

  let joinedRows =
    AMap.joinOn
      join1
      outer
      (fun _ struct (key, _) -> key)
      (fun  // join key: the middle key carried by the first join
           _
           structV
           outerV ->
        AVal.map2
          (fun struct (key, value) c ->
            match c with
            | ValueSome cid -> ValueSome(struct (value, cid))
            | ValueNone -> ValueNone)
          structV
          outerV)

  let v1 = AMap.toMap joinedRows

  if v1 <> Map.ofList [ 5, struct (ValueSome 10, 7) ] then
    failwithf "init: %A" v1

  CMap.addOrUpdate 1 11 middle // per-iteration value update flows through both joins
  let v2 = AMap.toMap joinedRows

  if v2 <> Map.ofList [ 5, struct (ValueSome 11, 7) ] then
    failwithf "value update: %A" v2

  CMap.remove 1 middle // missing middle row: the second join keeps the row (outer still resolves)
  let v3 = AMap.toMap joinedRows

  if v3 <> Map.ofList [ 5, struct (ValueNone, 7) ] then
    failwithf "middle removed: %A" v3

  CMap.remove 1 outer // outer gone: the innermost join drops the row
  let v4 = AMap.toMap joinedRows

  if not v4.IsEmpty then
    failwithf "outer removed: %A" v4

[<Fact>]
let ``AMap mapA with cross-map lookup matches the model``() =
  let prop(sc: CrossScenario) =
    let entities = CMap.empty<int, int> // entity -> lookup key
    let lookups = CMap.empty<int, int> // key -> value

    for (k, v) in sc.initialEntities do
      CMap.addOrUpdate k v entities

    for (k, v) in sc.initialLookups do
      CMap.addOrUpdate k v lookups

    let contexts =
      entities
      |> AMap.mapA(fun _ key -> AMap.tryFind key (CMap.value lookups))
      |> AMap.choose(fun _ v -> ValueOption.toOption v)

    let modelEntities = Dictionary<int, int>()
    let modelLookups = Dictionary<int, int>()

    for (k, v) in sc.initialEntities do
      modelEntities[k] <- v

    for (k, v) in sc.initialLookups do
      modelLookups[k] <- v

    let apply(op: CrossOp) =
      match op with
      | EntityUpsert(key, value) -> // Upsert an entity with its lookup key.
        CMap.addOrUpdate key value entities
        modelEntities[key] <- value
      | EntityRemove key -> // Remove the entity.
        CMap.remove key entities
        modelEntities.Remove key |> ignore
      | LookupUpsert(key, value) -> // Upsert a lookup entry.
        CMap.addOrUpdate key value lookups
        modelLookups[key] <- value
      | LookupRemove key -> // Remove the lookup entry.
        CMap.remove key lookups
        modelLookups.Remove key |> ignore

    for op in sc.ops do
      apply op

      let actual = AMap.toMap contexts

      let expected =
        Map.ofSeq(
          seq {
            for KeyValue(id, lookupKey) in modelEntities do
              match modelLookups.TryGetValue lookupKey with
              | true, v -> id, v
              | false, _ -> ()
          }
        )

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``ASet mapA with cross-map lookup matches the model``() =
  let prop(sc: CrossScenario) =
    let entities = CSet.empty<int> // entity ids
    let lookups = CMap.empty<int, int> // key -> value

    for (k, _) in sc.initialEntities do
      CSet.add k entities

    for (k, v) in sc.initialLookups do
      CMap.addOrUpdate k v lookups

    let contexts =
      entities
      |> ASet.mapA(fun id -> AMap.tryFind id (CMap.value lookups))
      |> ASet.choose(fun v -> ValueOption.toOption v)

    let modelEntities = HashSet<int>(List.map fst sc.initialEntities)
    let modelLookups = Dictionary<int, int>()

    for (k, v) in sc.initialLookups do
      modelLookups[k] <- v

    let apply(op: CrossOp) =
      match op with
      | EntityUpsert(key, _) -> // Add an entity id.
        CSet.add key entities
        modelEntities.Add key |> ignore
      | EntityRemove key -> // Remove the entity.
        CSet.remove key entities
        modelEntities.Remove key |> ignore
      | LookupUpsert(key, value) -> // Upsert a lookup entry.
        CMap.addOrUpdate key value lookups
        modelLookups[key] <- value
      | LookupRemove key -> // Remove the lookup entry.
        CMap.remove key lookups
        modelLookups.Remove key |> ignore

    for op in sc.ops do
      apply op

      let actual = Set.ofSeq(ASet.toSet contexts)

      let expected =
        Set.ofSeq(
          seq {
            for id in modelEntities do
              match modelLookups.TryGetValue id with
              | true, v -> v
              | false, _ -> ()
          }
        )

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList mapA with cross-map lookup matches the model``() =
  let prop(sc: CrossScenario) =
    let entities = CList.empty<int> // entity ids in order
    let lookups = CMap.empty<int, int> // key -> value

    for (k, _) in sc.initialEntities do
      CList.append k entities

    for (k, v) in sc.initialLookups do
      CMap.addOrUpdate k v lookups

    let contexts =
      entities
      |> AList.mapA(fun id -> AMap.tryFind id (CMap.value lookups))
      |> AList.choose(fun v -> ValueOption.toOption v)

    let modelEntities = ResizeArray<int>(List.map fst sc.initialEntities)
    let modelLookups = Dictionary<int, int>()

    for (k, v) in sc.initialLookups do
      modelLookups[k] <- v

    let apply(op: CrossOp) =
      match op with
      | EntityUpsert(key, _) -> // Insert the entity id at a derived position.
        let position = key % (modelEntities.Count + 1)
        CList.insertAt position key entities
        modelEntities.Insert(position, key)
      | EntityRemove key -> // Remove the first occurrence of the entity id.
        let idx = modelEntities.IndexOf key

        if idx >= 0 then
          CList.removeAt idx entities
          modelEntities.RemoveAt idx
      | LookupUpsert(key, value) -> // Upsert a lookup entry.
        CMap.addOrUpdate key value lookups
        modelLookups[key] <- value
      | LookupRemove key -> // Remove the lookup entry.
        CMap.remove key lookups
        modelLookups.Remove key |> ignore

    for op in sc.ops do
      apply op

      let actual = AList.toList contexts

      let expected =
        List.ofSeq(
          seq {
            for id in modelEntities do
              match modelLookups.TryGetValue id with
              | true, v -> v
              | false, _ -> ()
          }
        )

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AMap filter with keys matches the model``() =
  let prop(ops: int list) =
    let resources = CMap.empty<int, bool> // entity -> alive flag

    let live = resources |> AMap.filter(fun _ alive -> alive) |> AMap.keys

    let model = Dictionary<int, bool>()

    let apply(op: int) =
      let kind = op % 2
      let rest = op / 2
      let key = rest % 10
      let alive = (rest / 10) % 2 = 0

      match kind with
      | 0 -> // Upsert the entity's alive flag.
        CMap.addOrUpdate key alive resources
        model[key] <- alive
      | _ -> // Remove the entity.
        CMap.remove key resources
        model.Remove key |> ignore

    for op in ops do
      apply op

      let actual = ASet.toSet live

      let expected =
        Set.ofSeq(
          seq {
            for KeyValue(k, alive) in model do
              if alive then
                k
          }
        )

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``physics cache refresh matches a fresh computation``() =
  let prop(ops: int list) =
    let positions = CMap.empty<int, float>
    let velocities = CMap.empty<int, float>
    let inScenario = CSet.empty<int>
    let dt = 0.5

    let modelPos = Dictionary<int, float>()
    let modelVel = Dictionary<int, float>()
    let modelMembers = HashSet<int>()

    // The cache shape: force the world maps, then compute the snapshot.
    let refresh() =
      let pos = AMap.force(CMap.value positions)
      let vel = AMap.force(CMap.value velocities)
      let members = ASet.force(CSet.value inScenario)
      let computed = Dictionary<int, float>()

      for id in members do
        let mutable p = 0.0
        pos.TryGetValue(id, &p) |> ignore
        let mutable v = 0.0

        if vel.TryGetValue(id, &v) then
          computed[id] <- p + v * dt
        else
          computed[id] <- p

      computed

    // The model: the same computation from the model state.
    let model() =
      let computed = Dictionary<int, float>()

      for id in modelMembers do
        let mutable p = 0.0
        modelPos.TryGetValue(id, &p) |> ignore
        let mutable v = 0.0

        if modelVel.TryGetValue(id, &v) then
          computed[id] <- p + v * dt
        else
          computed[id] <- p

      computed

    let apply(op: int) =
      let kind = op % 5
      let rest = op / 5
      let id = rest % 10
      let value = float((rest / 10) % 100)

      match kind with
      | 0 -> // Upsert the position and mark the entity in the scenario.
        CMap.addOrUpdate id value positions
        modelPos[id] <- value
        CSet.add id inScenario
        modelMembers.Add id |> ignore
      | 1 -> // Upsert the velocity.
        CMap.addOrUpdate id value velocities
        modelVel[id] <- value
      | 2 -> // Remove the entity from the scenario.
        CSet.remove id inScenario
        modelMembers.Remove id |> ignore
      | 3 -> // Remove the position (the entity keeps its velocity).
        CMap.remove id positions
        modelPos.Remove id |> ignore
      | _ -> // Remove the velocity.
        CMap.remove id velocities
        modelVel.Remove id |> ignore

    for op in ops do
      apply op

      let actual = refresh()
      let expected = model()

      if actual.Count <> expected.Count then
        failwithf
          "count mismatch after %A: actual=%A expected=%A"
          op
          actual.Count
          expected.Count
      else
        for KeyValue(id, v) in expected do
          let mutable a = 0.0

          if not(actual.TryGetValue(id, &a)) || a <> v then
            failwithf
              "value mismatch after %A for %A: actual=%A expected=%A"
              op
              id
              a
              v

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``physics cache snapshot agrees with the adaptive derivation``() =
  let prop(ops: int list) =
    let positions = CMap.empty<int, float>
    let velocities = CMap.empty<int, float>
    let inScenario = CSet.empty<int>
    let dt = 0.5

    // The original adaptive cache (before the snapshot switch): a derived
    // per-member position computed from the adaptive world.
    let derived =
      inScenario
      |> ASet.mapA(fun id ->
        AVal.map2
          (fun p v -> struct (id, p + v * dt))
          (AMap.tryFind id (CMap.value positions)
           |> AVal.map(ValueOption.defaultValue 0.0))
          (AMap.tryFind id (CMap.value velocities)
           |> AVal.map(ValueOption.defaultValue 0.0)))

    let modelPos = Dictionary<int, float>()
    let modelVel = Dictionary<int, float>()
    let modelMembers = HashSet<int>()

    // The snapshot cache shape: force the world maps, then compute.
    let snapshot() =
      let pos = AMap.force(CMap.value positions)
      let vel = AMap.force(CMap.value velocities)
      let members = ASet.force(CSet.value inScenario)
      let computed = Dictionary<int, float>()

      for id in members do
        let mutable p = 0.0
        pos.TryGetValue(id, &p) |> ignore
        let mutable v = 0.0

        if vel.TryGetValue(id, &v) then
          computed[id] <- p + v * dt
        else
          computed[id] <- p

      computed

    // The model: the same computation from the model state.
    let model() =
      let computed = Dictionary<int, float>()

      for id in modelMembers do
        let mutable p = 0.0
        modelPos.TryGetValue(id, &p) |> ignore
        let mutable v = 0.0

        if modelVel.TryGetValue(id, &v) then
          computed[id] <- p + v * dt
        else
          computed[id] <- p

      computed

    let apply(op: int) =
      let kind = op % 5
      let rest = op / 5
      let id = rest % 10
      let value = float((rest / 10) % 100)

      match kind with
      | 0 -> // Upsert the position and mark the entity in the scenario.
        CMap.addOrUpdate id value positions
        modelPos[id] <- value
        CSet.add id inScenario
        modelMembers.Add id |> ignore
      | 1 -> // Upsert the velocity.
        CMap.addOrUpdate id value velocities
        modelVel[id] <- value
      | 2 -> // Remove the entity from the scenario.
        CSet.remove id inScenario
        modelMembers.Remove id |> ignore
      | 3 -> // Remove the position (the entity keeps its velocity).
        CMap.remove id positions
        modelPos.Remove id |> ignore
      | _ -> // Remove the velocity.
        CMap.remove id velocities
        modelVel.Remove id |> ignore

    for op in ops do
      apply op

      // The adaptive derivation, forced, must equal the snapshot and
      // the model.
      let adaptive =
        ASet.force derived |> Seq.map(fun struct (id, v) -> id, v) |> Map.ofSeq

      let snap = snapshot()
      let expected = model()

      if Map.count adaptive <> expected.Count then
        failwithf
          "count mismatch after %A: actual=%A expected=%A"
          op
          (Map.count adaptive)
          expected.Count
      else
        for KeyValue(id, v) in expected do
          let mutable a = 0.0
          let mutable s = 0.0

          if
            (not(Map.tryFind id adaptive |> Option.exists(fun x -> x = v)))
            || (not(snap.TryGetValue(id, &s)) || s <> v)
          then
            failwithf
              "value mismatch after %A for %A: adaptive=%A snapshot=%A expected=%A"
              op
              id
              (Map.tryFind id adaptive)
              s
              v

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``spatial radius query matches the brute force``() =
  let prop(ops: int list) =
    let positions = CMap.empty<int, struct (float * float)>
    let live = CSet.empty<int>
    let cellSize = 4.0
    let radius = 5.0
    let center = struct (10.0, 10.0)

    let modelPos = Dictionary<int, struct (float * float)>()
    let modelLive = HashSet<int>()

    // The Kipo shape: cells in radius -> potential targets -> distance
    // filter over the forced snapshot.
    let query() =
      let pos = AMap.force(CMap.value positions)
      let members = ASet.force(CSet.value live)
      let cellRadius = int(radius / cellSize) + 1
      let struct (centerX, centerY) = center

      let struct (ccx, ccy) =
        struct (int(centerX / cellSize), int(centerY / cellSize))

      let results = ResizeArray<int>()

      for dx = -cellRadius to cellRadius do
        for dy = -cellRadius to cellRadius do
          let cell = struct (ccx + dx, ccy + dy)
          let struct (cellX, cellY) = cell

          let cellOrigin =
            struct (float cellX * cellSize, float cellY * cellSize)

          // Only cells whose origin is within radius + cellSize count.
          let struct (ox, oy) = cellOrigin
          let dxp = ox - centerX
          let dyp = oy - centerY

          if
            dxp * dxp + dyp * dyp <= (radius + cellSize) * (radius + cellSize)
          then
            for id in members do
              match pos.TryGetValue id with
              | true, p ->
                let struct (px, py) = p
                let ddx = px - centerX
                let ddy = py - centerY

                if ddx * ddx + ddy * ddy <= radius * radius then
                  results.Add id
              | false, _ -> ()

      results.ToArray() |> Array.sort |> Set.ofArray

    let model() =
      let struct (centerX, centerY) = center

      seq {
        for id in modelLive do
          match modelPos.TryGetValue id with
          | true, p ->
            let struct (px, py) = p
            let ddx = px - centerX
            let ddy = py - centerY

            if ddx * ddx + ddy * ddy <= radius * radius then
              id
          | false, _ -> ()
      }
      |> Set.ofSeq

    let apply(op: int) =
      let kind = op % 3
      let rest = op / 3
      let id = rest % 20
      let x = float((rest / 20) % 20)
      let y = float((rest / 400) % 20)

      match kind with
      | 0 -> // Upsert the position and mark the entity live.
        CMap.addOrUpdate id (struct (x, y)) positions
        modelPos[id] <- struct (x, y)
        CSet.add id live
        modelLive.Add id |> ignore
      | 1 -> // Remove the entity.
        CMap.remove id positions
        modelPos.Remove id |> ignore
        CSet.remove id live
        modelLive.Remove id |> ignore
      | _ -> // Toggle liveness only.
        CSet.add id live
        modelLive.Add id |> ignore

    for op in ops do
      apply op

      let actual = query()
      let expected = model()

      if actual <> expected then
        failwithf
          "query mismatch after %A: actual=%A expected=%A"
          op
          actual
          expected

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``AList choose matches the combat-status model``() =
  let prop(ops: int list) =
    let effects = CList.empty<int> // effect kind

    // Kipo: stun/silence map to a status, everything else is dropped.
    let effectKindToStatus(kind: int) =
      if kind % 4 = 0 then Some(kind * 10) else None

    let statuses = effects |> AList.choose effectKindToStatus
    let model = ResizeArray<int>()

    let apply(op: int) =
      let kind = op % 3
      let rest = op / 3
      let element = rest % 10

      let position =
        // F#'s % is signed: normalize to [0, Count + 1).
        let p = (rest / 10) % (model.Count + 1)
        if p < 0 then p + (model.Count + 1) else p

      match kind with
      | 0 -> // Insert an effect at the position.
        CList.insertAt position element effects
        model.Insert(position, element)
      | 1 -> // Remove the effect at the position.
        if model.Count > 0 && position < model.Count then
          CList.removeAt position effects
          model.RemoveAt position
      | _ -> // Update the effect at the position.
        if model.Count > 0 && position < model.Count then
          CList.updateAt position element effects
          model[position] <- element

    for op in ops do
      apply op

      let actual = AList.toList statuses
      let expected = model |> Seq.choose effectKindToStatus |> List.ofSeq

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.QuickThrowOnFailure prop

// =============================================================================
// Gap coverage: transactions, the external nodes, the AList positional and
// reduction families, bind/concat, mapiA positions, and physics removals.
// =============================================================================

[<Fact>]
let ``Transaction.run defers application until commit``() =
  let prop(ops: int list) =
    let m = CMap.empty<int, int>
    let model = Dictionary<int, int>()

    let apply(op: int) =
      let kind = op % 2
      let rest = op / 2
      let key = rest % 10
      let value = (rest / 10) % 100
      let key2 = (key + 5) % 10

      // The state visible inside the transaction is the PRE state.
      let expectedPre = Map.ofSeq(seq { for KeyValue(k, v) in model -> k, v })

      let actualPre =
        Transaction.run(fun () ->
          match kind with
          | 0 -> // Batch: two upserts.
            CMap.addOrUpdate key value m
            CMap.addOrUpdate key2 (value + 1) m
          | _ -> // Batch: an upsert and a remove.
            CMap.addOrUpdate key value m
            CMap.remove key2 m

          AMap.toMap(CMap.value m))

      match kind with
      | 0 ->
        model[key] <- value
        model[key2] <- value + 1
      | _ ->
        model[key] <- value
        model.Remove key2 |> ignore

      let expectedPost = Map.ofSeq(seq { for KeyValue(k, v) in model -> k, v })
      let actualPost = AMap.toMap(CMap.value m)
      actualPre = expectedPre && actualPost = expectedPost

    for op in ops do
      if not(apply op) then
        failwithf "mismatch after op %A" op

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``AMap ofExternal matches the reference model``() =
  let prop(sc: ExternalScenario) =
    let mutable snapshot = Map.empty<int, int>

    let ext, invalidate =
      AMap.ofExternal(fun () -> snapshot :> IReadOnlyDictionary<int, int>)

    // Model: whether an invalidate is pending, and the last read snapshot.
    let mutable dirty = true
    let mutable lastSeen = Map.empty<int, int>

    let apply(op: ExternalOp) =
      match op with
      | Replace value -> // Replace the external snapshot.
        snapshot <- Map.ofList [ value % 10, value ]

        if value % 4 = 0 then
          snapshot <- Map.add ((value + 1) % 10) (value + 1) snapshot
      | Invalidate -> // Invalidate: the next read re-reads the snapshot.
        invalidate()
        dirty <- true

      let expected = if dirty then snapshot else lastSeen
      let actual = AMap.toMap ext

      if actual <> expected then
        false
      else
        dirty <- false
        lastSeen <- expected
        true

    for op in sc.ops do
      if not(apply op) then
        failwithf "mismatch after %A" op

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList ofExternal matches the reference model``() =
  let prop(sc: ExternalScenario) =
    let mutable snapshot: int list = []

    let ext, invalidate =
      AList.ofExternal(fun () -> snapshot :> IReadOnlyList<int>)

    // Model: whether an invalidate is pending, and the last read snapshot.
    let mutable dirty = true
    let mutable lastSeen: int list = []

    let apply(op: ExternalOp) =
      match op with
      | Replace value -> // Replace the external snapshot.
        snapshot <- [ value; value * 2 ]
      | Invalidate -> // Invalidate: the next read re-reads the snapshot.
        invalidate()
        dirty <- true

      let expected = if dirty then snapshot else lastSeen
      let actual = AList.toList ext

      if actual <> expected then
        false
      else
        dirty <- false
        lastSeen <- expected
        true

    for op in sc.ops do
      if not(apply op) then
        failwithf "mismatch after %A" op

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList take matches the model``() =
  let prop(sc: ListScenario) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()

    for (e, _) in sc.initial do
      CList.append e l
      model.Add e

    let count = List.length sc.ops % 5
    let taken = AList.take count (CList.value l)

    for op in sc.ops do
      applyListChange op l model

      let actual = AList.toList taken
      let expected = model |> Seq.truncate count |> List.ofSeq

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList skip matches the model``() =
  let prop(sc: ListScenario) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()

    for (e, _) in sc.initial do
      CList.append e l
      model.Add e

    let count = List.length sc.ops % 5
    let skipped = AList.skip count (CList.value l)

    for op in sc.ops do
      applyListChange op l model

      let actual = AList.toList skipped

      let expected =
        if model.Count <= count then
          []
        else
          model |> Seq.skip count |> List.ofSeq

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList sub matches the model``() =
  let prop(sc: ListScenario) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()

    for (e, _) in sc.initial do
      CList.append e l
      model.Add e

    let offset = List.length sc.ops % 3
    let count = List.length sc.ops % 5
    let sliced = AList.sub offset count (CList.value l)

    for op in sc.ops do
      applyListChange op l model

      let actual = AList.toList sliced

      let expected =
        let from = min offset model.Count
        let take = max 0 (min count (model.Count - from))
        model |> Seq.skip from |> Seq.truncate take |> List.ofSeq

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList sort matches the model``() =
  let prop(sc: ListScenario) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()

    for (e, _) in sc.initial do
      CList.append e l
      model.Add e

    let sorted = AList.sort(CList.value l)

    for op in sc.ops do
      applyListChange op l model

      let actual = AList.toList sorted
      let expected = model |> Seq.sort |> List.ofSeq

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList rev matches the model``() =
  let prop(sc: ListScenario) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()

    for (e, _) in sc.initial do
      CList.append e l
      model.Add e

    let reversed = AList.rev(CList.value l)

    for op in sc.ops do
      applyListChange op l model

      let actual = AList.toList reversed
      let expected = model |> Seq.rev |> List.ofSeq

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList pairwise matches the model``() =
  let prop(sc: ListScenario) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()

    for (e, _) in sc.initial do
      CList.append e l
      model.Add e

    let pairs = AList.pairwise(CList.value l)

    for op in sc.ops do
      applyListChange op l model

      let actual = AList.toList pairs

      let expected =
        seq {
          for i in 0 .. model.Count - 2 do
            struct (model[i], model[i + 1])
        }
        |> List.ofSeq

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList sum matches the model``() =
  let prop(sc: ListScenario) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()

    for (e, _) in sc.initial do
      CList.append e l
      model.Add e

    let total = AList.sum(CList.value l)

    for op in sc.ops do
      applyListChange op l model

      let actual = AVal.getValue total
      let expected = Seq.sum model

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList countBy matches the model``() =
  let prop(sc: ListScenario) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()

    for (e, _) in sc.initial do
      CList.append e l
      model.Add e

    let evenCount = AList.countBy (fun x -> x % 2 = 0) (CList.value l)

    for op in sc.ops do
      applyListChange op l model

      let actual = AVal.getValue evenCount
      let expected = model |> Seq.filter(fun x -> x % 2 = 0) |> Seq.length

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList tryMin and tryMax match the model``() =
  let prop(sc: ListScenario) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()

    for (e, _) in sc.initial do
      CList.append e l
      model.Add e

    let mn = AList.tryMin(CList.value l)
    let mx = AList.tryMax(CList.value l)

    for op in sc.ops do
      applyListChange op l model

      let actualMin = AVal.getValue mn
      let actualMax = AVal.getValue mx

      let expectedMin =
        if model.Count = 0 then
          ValueNone
        else
          ValueSome(Seq.min model)

      let expectedMax =
        if model.Count = 0 then
          ValueNone
        else
          ValueSome(Seq.max model)

      if actualMin <> expectedMin || actualMax <> expectedMax then
        failwithf "mismatch after %A: min=%A max=%A" op actualMin actualMax

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList fold matches the model``() =
  let prop(sc: ListScenario) =
    let l = CList.empty<int>
    let model = ResizeArray<int>()

    for (e, _) in sc.initial do
      CList.append e l
      model.Add e

    let acc = AList.fold (fun s x -> s * 10 + x) 0 (CList.value l)

    for op in sc.ops do
      applyListChange op l model

      let actual = AVal.getValue acc
      let expected = model |> Seq.fold (fun s x -> s * 10 + x) 0

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList bind matches the model``() =
  let prop(sc: TwoListScenario) =
    let l1 = CList.empty<int>
    let l2 = CList.empty<int>
    let selector = CVal.create 0
    let model1 = ResizeArray<int>()
    let model2 = ResizeArray<int>()

    for (e, _) in sc.initialA do
      CList.append e l1
      model1.Add e

    for (e, _) in sc.initialB do
      CList.append e l2
      model2.Add e

    // The bind switches the whole output when the selector aval changes.
    let bound =
      AList.bind
        (fun n -> if n = 0 then CList.value l1 else CList.value l2)
        (CVal.value selector)

    let apply(op: TwoListOp) =
      match op with
      | InsertInto(element, payload, which) ->
        let target = if which = 0 then l1 else l2
        let model = if which = 0 then model1 else model2
        let position = payload % (model.Count + 1)
        CList.insertAt position element target
        model.Insert(position, element)
      | RemoveFrom(payload, which) ->
        let target = if which = 0 then l1 else l2
        let model = if which = 0 then model1 else model2
        let position = payload % (model.Count + 1)

        if model.Count > 0 && position < model.Count then
          CList.removeAt position target
          model.RemoveAt position
      | UpdateIn(element, payload, which) ->
        let target = if which = 0 then l1 else l2
        let model = if which = 0 then model1 else model2
        let position = payload % (model.Count + 1)

        if model.Count > 0 && position < model.Count then
          CList.updateAt position element target
          model[position] <- element
      | Switch which -> // Switch the selector.
        CVal.set which selector

    for op in sc.ops do
      apply op

      let actual = AList.toList bound

      let expected =
        if AVal.getValue(CVal.value selector) = 0 then
          List.ofSeq model1
        else
          List.ofSeq model2

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList concat matches the model``() =
  let prop(sc: TwoListScenario) =
    let l1 = CList.empty<int>
    let l2 = CList.empty<int>
    let model1 = ResizeArray<int>()
    let model2 = ResizeArray<int>()

    for (e, _) in sc.initialA do
      CList.append e l1
      model1.Add e

    for (e, _) in sc.initialB do
      CList.append e l2
      model2.Add e

    let concat = AList.concat [ CList.value l1; CList.value l2 ]

    let apply(op: TwoListOp) =
      match op with
      | InsertInto(element, payload, which) ->
        let target = if which = 0 then l1 else l2
        let model = if which = 0 then model1 else model2
        let position = payload % (model.Count + 1)
        CList.insertAt position element target
        model.Insert(position, element)
      | RemoveFrom(payload, which) ->
        let target = if which = 0 then l1 else l2
        let model = if which = 0 then model1 else model2
        let position = payload % (model.Count + 1)

        if model.Count > 0 && position < model.Count then
          CList.removeAt position target
          model.RemoveAt position
      | UpdateIn(element, payload, which) ->
        let target = if which = 0 then l1 else l2
        let model = if which = 0 then model1 else model2
        let position = payload % (model.Count + 1)

        if model.Count > 0 && position < model.Count then
          CList.updateAt position element target
          model[position] <- element
      | Switch _ -> () // No selector here: no-op.

    for op in sc.ops do
      apply op

      let actual = AList.toList concat
      let expected = Seq.append model1 model2 |> List.ofSeq

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AList mapiA passes the mapping-time position``() =
  let prop(sc: ListScenario) =
    let l = CList.empty<int>
    // Model: element -> the position its mapping ran at (positions stick).
    let model = ResizeArray<struct (int * int)>()

    for (e, _) in sc.initial do
      CList.append e l
      model.Add(struct (e, model.Count))

    // The mapping encodes the position it was invoked with into the value.
    let mapped =
      AList.mapiA (fun i v -> AVal.constant(v * 100 + i)) (CList.value l)

    // Establish the mapping positions at the initial state before any op:
    // a lazy first load after an op would map the initial elements at
    // their post-op positions.
    AList.force mapped |> ignore

    let apply(op: ListChange) =
      match op with
      | Insert(element, payload) ->
        let position = payload % (model.Count + 1)
        CList.insertAt position element l
        model.Insert(position, struct (element, position))
      | RemoveAt payload ->
        let position = payload % (model.Count + 1)

        if model.Count > 0 && position < model.Count then
          CList.removeAt position l
          model.RemoveAt position
      | UpdateAt(element, payload) ->
        let position = payload % (model.Count + 1)

        if model.Count > 0 && position < model.Count then
          // Equal values are a no-op (no re-map, the position sticks).
          let struct (oldE, _) = model[position]

          if oldE <> element then
            CList.updateAt position element l
            model[position] <- struct (element, position)
      | ListChange.SetValue _ -> ()

    for op in sc.ops do
      apply op

      let actual = AList.toList mapped

      let expected =
        model |> Seq.map(fun struct (e, i) -> e * 100 + i) |> List.ofSeq

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(scenarioConfig, prop)

// =============================================================================
// Gap coverage: AVal.ofExternal and the changeables' batch ops.
// =============================================================================

[<Fact>]
let ``AVal ofExternal matches the reference model``() =
  let prop(sc: ExternalScenario) =
    let mutable snapshot = 0
    let ext, invalidate = AVal.ofExternal(fun () -> snapshot)
    // Model: whether an invalidate is pending, and the last read snapshot.
    let mutable dirty = true
    let mutable lastSeen = 0

    let apply(op: ExternalOp) =
      match op with
      | Replace value -> // Replace the external snapshot.
        snapshot <- value
      | Invalidate -> // Invalidate: the next read re-reads the snapshot.
        invalidate()
        dirty <- true

      let expected = if dirty then snapshot else lastSeen
      let actual = AVal.getValue ext

      if actual <> expected then
        false
      else
        dirty <- false
        lastSeen <- expected
        true

    for op in sc.ops do
      if not(apply op) then
        failwithf "mismatch after %A" op

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``CSet updateTo and perform match the sequential model``() =
  let prop(ops: SetOp list) =
    let viaUpdate = CSet.empty<int>
    let viaPerform = CSet.empty<int>
    let model = HashSet<int>()
    let pendingRems = HashSet<int>()

    for op in ops do
      match op with
      | Add(e, _) -> model.Add e |> ignore
      | SetOp.Remove e -> pendingRems.Add e |> ignore
      | SetOp.SetValue _ -> ()

    // The batch semantics: adds and removes are separate phases (an add
    // and a remove of the same element cancel, regardless of order).
    for e in pendingRems do
      model.Remove e |> ignore

    CSet.updateTo model viaUpdate |> ignore

    let d = SetDeltaBuilder<int>()

    for op in ops do
      match op with
      | Add(e, _) -> d.Add e
      | SetOp.Remove e -> d.Remove e
      | SetOp.SetValue _ -> ()

    CSet.perform d viaPerform

    let expected = Set.ofSeq model

    let actualUpdate = viaUpdate |> CSet.value |> ASet.toSet
    let actualPerform = viaPerform |> CSet.value |> ASet.toSet
    actualUpdate = expected && actualPerform = expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``CMap updateTo and perform match the sequential model``() =
  let prop(ops: MapOp list) =
    let viaUpdate = CMap.empty<int, int>
    let viaPerform = CMap.empty<int, int>
    let model = Dictionary<int, int>()
    let pendingRems = HashSet<int>()

    for op in ops do
      match op with
      | Upsert(k, v) -> model[k] <- v
      | MapOp.Remove k -> pendingRems.Add k |> ignore
      | MapOp.SetValue _ -> ()

    // The batch semantics: sets and removes are separate phases (a set
    // and a remove of the same key cancel, regardless of order).
    for k in pendingRems do
      model.Remove k |> ignore

    CMap.updateTo (Seq.map (fun (KeyValue(k, v)) -> k, v) model) viaUpdate
    |> ignore

    let d = MapDeltaBuilder<int, int>()

    for op in ops do
      match op with
      | Upsert(k, v) -> d.Set(k, v)
      | MapOp.Remove k -> d.Remove k
      | MapOp.SetValue _ -> ()

    CMap.perform d viaPerform

    let expected = [ for KeyValue(k, v) in model -> k, v ] |> Map.ofList

    let actualUpdate = viaUpdate |> CMap.value |> AMap.toMap
    let actualPerform = viaPerform |> CMap.value |> AMap.toMap
    actualUpdate = expected && actualPerform = expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``CList updateTo and perform match the sequential model``() =
  let prop(ops: ListChange list) =
    let viaUpdate = CList.empty<int>
    let viaPerform = CList.empty<int>
    let model = ResizeArray<int>()
    let d = ListDeltaBuilder<int>()

    for op in ops do
      match op with
      | Insert(e, payload) ->
        let pos = payload % (model.Count + 1)
        model.Insert(pos, e)
        d.Insert(pos, e)
      | RemoveAt payload ->
        if model.Count > 0 then
          let pos = payload % model.Count
          model.RemoveAt pos
          d.Remove(pos)
      | UpdateAt(e, payload) ->
        if model.Count > 0 then
          let pos = payload % model.Count
          model[pos] <- e
          d.Update(pos, e)
      | ListChange.SetValue _ -> ()

    CList.updateTo (Array.ofSeq model) viaUpdate |> ignore
    CList.perform d viaPerform

    let expected = List.ofSeq model

    let actualUpdate = viaUpdate |> CList.value |> AList.toList
    let actualPerform = viaPerform |> CList.value |> AList.toList
    actualUpdate = expected && actualPerform = expected

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``CList addRange appends the items``() =
  let prop(xs: int list) =
    let l = CList.empty<int>
    CList.addRange xs l
    let actual = l |> CList.value |> AList.toList
    actual = xs

  Check.QuickThrowOnFailure prop

// =============================================================================
// Non-int element types: the same reference-model shape with struct, string,
// and Guid elements. The int tests cover the full op matrix; these cover the
// element-type paths (struct hashing, reference-type elements, and keys
// without an int representation).
// =============================================================================

type StructListChange =
  | Insert of element: struct (int * int) * payload: int
  | RemoveAt of payload: int

type StructListScenario = {
  initial: struct (int * int) list
  ops: StructListChange list
}

type StringSetOp =
  | Add of element: string * value: int
  | Remove of element: string

type StringSetScenario = {
  initial: (string * int) list
  ops: StringSetOp list
}

type GuidMapOp =
  | Upsert of key: Guid * value: int
  | Remove of key: Guid

type GuidMapScenario = {
  initial: (Guid * int) list
  ops: GuidMapOp list
}

let structPairGen = gen {
  let! a = Gen.choose(0, 19)
  let! b = Gen.choose(0, 19)
  return struct (a, b)
}

let structListChangeGen: Gen<StructListChange> =
  Gen.frequency [
    (3,
     gen {
       let! e = structPairGen
       let! p = Gen.choose(0, 200)
       return Insert(e, p)
     })
    (2,
     gen {
       let! p = Gen.choose(0, 200)
       return RemoveAt p
     })
  ]

let structListScenarioGen: Gen<StructListScenario> = gen {
  let! initial = Gen.listOf structPairGen
  let! ops = Gen.listOf structListChangeGen
  return { initial = initial; ops = ops }
}

let stringGen = Gen.elements [ "alpha"; "beta"; "gamma"; "delta"; "epsilon" ]

let stringSetOpGen: Gen<StringSetOp> =
  Gen.frequency [
    (3,
     gen {
       let! e = stringGen
       let! v = Gen.choose(0, 99)
       return Add(e, v)
     })
    (2,
     gen {
       let! e = stringGen
       return StringSetOp.Remove e
     })
  ]

let stringSetScenarioGen: Gen<StringSetScenario> = gen {
  let! initial =
    Gen.listOf(
      gen {
        let! e = stringGen
        let! v = Gen.choose(0, 99)
        return e, v
      }
    )

  let! ops = Gen.listOf stringSetOpGen

  return {
    initial = dedupPairs initial
    ops = ops
  }
}

let guidGen =
  Gen.elements [
    for i in 0..19 ->
      Guid("00000000-0000-0000-0000-" + i.ToString("000000000000"))
  ]

let guidMapOpGen: Gen<GuidMapOp> =
  Gen.frequency [
    (3,
     gen {
       let! k = guidGen
       let! v = Gen.choose(0, 99)
       return Upsert(k, v)
     })
    (2,
     gen {
       let! k = guidGen
       return GuidMapOp.Remove k
     })
  ]

let guidMapScenarioGen: Gen<GuidMapScenario> = gen {
  let! initial =
    Gen.listOf(
      gen {
        let! k = guidGen
        let! v = Gen.choose(0, 99)
        return k, v
      }
    )

  let! ops = Gen.listOf guidMapOpGen

  return {
    initial = dedupPairs initial
    ops = ops
  }
}

type NonIntArbs =
  static member StructListScenario() : Arbitrary<StructListScenario> =
    Arb.fromGen structListScenarioGen

  static member StringSetScenario() : Arbitrary<StringSetScenario> =
    Arb.fromGen stringSetScenarioGen

  static member GuidMapScenario() : Arbitrary<GuidMapScenario> =
    Arb.fromGen guidMapScenarioGen

let private nonIntConfig =
  Config.QuickThrowOnFailure.WithArbitrary([| typeof<NonIntArbs> |])

[<Fact>]
let ``AList sort over struct pairs matches the model``() =
  let prop(sc: StructListScenario) =
    let l = CList.empty<struct (int * int)>
    let model = ResizeArray<struct (int * int)>()

    for e in sc.initial do
      CList.append e l
      model.Add e

    let sorted = AList.sort(CList.value l)

    for op in sc.ops do
      match op with
      | Insert(element, payload) ->
        let position = payload % (model.Count + 1)
        CList.insertAt position element l
        model.Insert(position, element)
      | RemoveAt payload ->
        let position = payload % (model.Count + 1)

        if model.Count > 0 && position < model.Count then
          CList.removeAt position l
          model.RemoveAt position

      let actual = AList.toList sorted
      let expected = model |> Seq.sort |> List.ofSeq

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(nonIntConfig, prop)

[<Fact>]
let ``ASet mapA over strings matches the reference model``() =
  let prop(sc: StringSetScenario) =
    let source = CSet.empty<string>
    let values = Dictionary<string, cval<int>>()

    for (e, v) in sc.initial do
      CSet.add e source
      values[e] <- CVal.create v

    let mapped = ASet.mapA (fun v -> CVal.value values[v]) (CSet.value source)
    // Model: element -> current value; value -> occurrence count; the output.
    let elementValue = Dictionary<string, int>()
    let valueRefs = Dictionary<int, int>()
    let model = HashSet<int>()

    for (e, v) in sc.initial do
      elementValue[e] <- v

      match valueRefs.TryGetValue v with
      | true, r -> valueRefs[v] <- r + 1
      | false, _ ->
        valueRefs[v] <- 1
        model.Add v |> ignore

    for op in sc.ops do
      match op with
      | Add(element, value) -> // Add the element with a fresh aval holding the value.
        CSet.add element source

        if not(elementValue.ContainsKey element) then
          values[element] <- CVal.create value
          elementValue[element] <- value

          match valueRefs.TryGetValue value with
          | true, r -> valueRefs[value] <- r + 1
          | false, _ ->
            valueRefs[value] <- 1
            model.Add value |> ignore
      | StringSetOp.Remove element -> // Remove the element.
        CSet.remove element source

        if elementValue.ContainsKey element then
          let v = elementValue[element]
          elementValue.Remove element |> ignore
          values.Remove element |> ignore
          let r = valueRefs[v] - 1

          if r = 0 then
            valueRefs.Remove v |> ignore
            model.Remove v |> ignore
          else
            valueRefs[v] <- r

      let actual = Set.ofSeq(ASet.toSet mapped)
      let expected = Set.ofSeq model

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(nonIntConfig, prop)

[<Fact>]
let ``AMap mapA over Guid keys matches the reference model``() =
  let prop(sc: GuidMapScenario) =
    let source = CMap.empty<Guid, int>
    let values = Dictionary<Guid, cval<int>>()

    for (k, v) in sc.initial do
      CMap.addOrUpdate k v source
      values[k] <- CVal.create v

    let mapped = AMap.mapA (fun k _ -> CVal.value values[k]) (CMap.value source)
    let model = Dictionary<Guid, int>()

    for (k, v) in sc.initial do
      model[k] <- v

    for op in sc.ops do
      match op with
      | Upsert(key, value) -> // AddOrUpdate the key: a fresh key gets a fresh aval.
        CMap.addOrUpdate key value source

        if not(model.ContainsKey key) then
          values[key] <- CVal.create value
          model[key] <- value
      | GuidMapOp.Remove key -> // Remove the key.
        CMap.remove key source

        if model.ContainsKey key then
          model.Remove key |> ignore

      let actual = AMap.toMap mapped

      let expected = Map.ofSeq(seq { for KeyValue(k, v) in model -> k, v })

      if actual <> expected then
        failwithf "mismatch after %A: actual=%A expected=%A" op actual expected

  Check.One(nonIntConfig, prop)

// =============================================================================
// Cross-thread posting: a posted batch must match sequential application of
// the same operations (single producer, one drain).
// =============================================================================

[<Fact>]
let ``posted set ops match the sequential model``() =
  let prop(ops: (bool * int) list) =
    let source = CSet.empty<int>
    let model = HashSet<int>()

    for (isAdd, v) in ops do
      if isAdd then
        model.Add v |> ignore
      else
        model.Remove v |> ignore

    let worker =
      Task.Run(fun () ->
        for (isAdd, v) in ops do
          if isAdd then
            CSet.postAdd v source
          else
            CSet.postRemove v source)

    worker.Wait()
    Posting.pump()

    Assert.Equal<Set<int>>(Set.ofSeq model, ASet.toSet source)

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``posted map ops match the sequential model``() =
  let prop(ops: (int * int) list) =
    let source = CMap.empty<int, int>
    let model = Dictionary<int, int>()

    for (k, v) in ops do
      if v % 2 = 0 then
        model[k] <- v
      else
        model.Remove k |> ignore

    let worker =
      Task.Run(fun () ->
        for (k, v) in ops do
          if v % 2 = 0 then
            CMap.postAddOrUpdate k v source
          else
            CMap.postRemove k source)

    worker.Wait()
    Posting.pump()

    let expected = Map.ofSeq [ for KeyValue(k, v) in model -> k, v ]
    Assert.Equal<Map<int, int>>(expected, AMap.toMap source)

  Check.QuickThrowOnFailure prop

[<Fact>]
let ``posted list ops match the sequential model``() =
  let prop(values: int list) =
    let source = CList.empty<int>
    // Model: kind (0 = insert, 1 = removeAt), position, value.
    let model = ResizeArray<int>()
    let ops = ResizeArray<int * int * int>()

    for v in values do
      // abs of a bounded residue: Int32.MinValue is safe.
      let n = abs(v % 997)

      if v % 2 = 0 then
        let pos = n % (model.Count + 1)
        model.Insert(pos, v)
        ops.Add(0, pos, v)
      elif model.Count > 0 then
        let pos = n % model.Count
        model.RemoveAt pos
        ops.Add(1, pos, 0)
      else
        model.Add v
        ops.Add(0, model.Count - 1, v)

    let worker =
      Task.Run(fun () ->
        for (kind, pos, v) in ops do
          if kind = 0 then
            CList.postInsertAt pos v source
          else
            CList.postRemoveAt pos source)

    worker.Wait()
    Posting.pump()

    Assert.Equal<int[]>(model.ToArray(), AList.force source)

  Check.QuickThrowOnFailure prop

// =============================================================================
// Issue #113 regression class: a consumer that snapshots an upstream node's
// Version AFTER forcing it can record the dirty-indicator value (committed +
// 1); every later read-after-write then compares equal and skips the upstream
// node, freezing its output. All reads go through the TOP of each chain only;
// models are pure and iteration-order independent (integer sums).
// =============================================================================

[<Fact>]
let ``AMap joinOn chains revert an upstream removal seen through the outer join``
  ()
  =
  // Minimal deterministic shape of issue #113: cores -> groupBy ->
  // mapA(fold) -> join(a, b) -> join(s1, c), first read while the inner
  // group is absent.
  let cores: cmap<int, struct (int * string)> = CMap.empty
  let a: cmap<int, string> = CMap.empty
  let c: cmap<int, string> = CMap.empty

  let bySlot =
    cores
    |> AMap.groupBy(fun _ sc ->
      match sc with
      | struct (s, _) -> s)

  let b: amap<int, string> =
    bySlot
    |> AMap.mapA(fun _ g ->
      g
      |> AMap.fold
        (fun acc _ sc ->
          match sc with
          | struct (_, m) -> acc + m)
        "")

  let s1 =
    AMap.joinOn (CMap.value a) b (fun _ _ -> 1) (fun _ lV rV ->
      AVal.map2
        (fun l r ->
          ValueSome(l + "+" + defaultArg (Option.ofValueOption r) "-"))
        lV
        rV)

  let d =
    AMap.joinOn s1 (CMap.value c) (fun _ _ -> 1) (fun _ lV rV ->
      AVal.map2
        (fun l r ->
          ValueSome(l + "/" + defaultArg (Option.ofValueOption r) "-"))
        lV
        rV)

  let read() : string voption = AVal.getValue(AMap.tryFind 1 d)

  CMap.addOrUpdate 1 "A" a |> ignore

  if read() <> ValueSome "A+-/-" then
    failwithf "pre read: %A" (read())

  CMap.addOrUpdate 100 struct (1, "B") cores |> ignore

  if read() <> ValueSome "A+B/-" then
    failwithf "after core add: %A" (read())

  CMap.remove 100 cores |> ignore

  if read() <> ValueSome "A+-/-" then
    failwithf "after core remove: %A" (read())

[<Fact>]
let ``AMap joinOn chains match the model under core churn with top-only reads``
  ()
  =
  let prop(ops: ChainOp list) =
    // cores -> groupBy -> mapA(fold sum) -> join(a, b) -> join(s1, c).
    // The group reduction is a sum, so the model is iteration-order
    // independent. Every read goes through the outer join's tryFind.
    let cores: cmap<int, int> = CMap.empty
    let a: cmap<int, string> = CMap.empty
    let c: cmap<int, string> = CMap.empty
    CMap.addOrUpdate 1 "A" a |> ignore

    let bySlot = cores |> AMap.groupBy(fun _ v -> v % 3)

    let b: amap<int, int> =
      bySlot |> AMap.mapA(fun _ g -> g |> AMap.fold (fun acc _ v -> acc + v) 0)

    let s1 =
      AMap.joinOn (CMap.value a) b (fun _ _ -> 1) (fun _ lV rV ->
        AVal.map2
          (fun l r ->
            ValueSome(
              l
              + "."
              + (match r with
                 | ValueSome v -> string v
                 | ValueNone -> "x")
            ))
          lV
          rV)

    let d =
      AMap.joinOn s1 (CMap.value c) (fun _ _ -> 1) (fun _ lV rV ->
        AVal.map2
          (fun l r ->
            ValueSome(l + "/" + defaultArg (Option.ofValueOption r) ""))
          lV
          rV)

    let coreModel = Dictionary<int, int>()
    let mutable equip: string voption = ValueNone

    let expected() =
      let groups = Dictionary<int, int>()

      for KeyValue(_, v) in coreModel do
        let g = v % 3
        groups[g] <- groups.GetValueOrDefault(g) + v

      let mid =
        match groups.TryGetValue(1) with
        | true, sum -> string sum
        | false, _ -> "x"

      let left = "A." + mid

      match equip with
      | ValueSome e -> left + "/" + e
      | ValueNone -> left + "/"

    let apply(op: ChainOp) =
      match op with
      | CoreEdit(CoreUpsert(k, v)) ->
        CMap.addOrUpdate k v cores |> ignore
        coreModel[k] <- v
      | CoreEdit(CoreRemove k) ->
        CMap.remove k cores |> ignore
        coreModel.Remove k |> ignore
      | EquipEdit(EquipUpsert v) ->
        CMap.addOrUpdate 1 (string v) c |> ignore
        equip <- ValueSome(string v)
      | EquipEdit EquipRemove ->
        CMap.remove 1 c |> ignore
        equip <- ValueNone

    let actual() : string voption = AVal.getValue(AMap.tryFind 1 d)

    // The first read lands while no core exists yet: it builds the outer
    // entry against an absent right side, which is the issue trigger.
    if actual() <> ValueSome(expected()) then
      failwithf
        "mismatch before any op: actual=%A expected=%A"
        (actual())
        (expected())

    for op in ops do
      apply op

      if actual() <> ValueSome(expected()) then
        failwithf
          "mismatch after %A: actual=%A expected=%A"
          op
          (actual())
          (expected())

  Check.One(scenarioConfig, prop)

[<Fact>]
let ``AMap joinOn tail consumers track a cascading right side under core churn``
  ()
  =
  // Same chain without the outer join, read through two tail consumers
  // (tryFind and fold). The right side of the join cascades into lazy
  // drains (groupBy children + per-entry folds), which is what makes the
  // upstream node end its drain with the self-healing flag set.
  let prop(ops: ChainCoreOp list) =
    // Same chain without the outer join, read through two tail consumers
    // (tryFind and fold). The right side of the join cascades into lazy
    // drains (groupBy children + per-entry folds), which is what makes the
    // upstream node end its drain with the self-healing flag set.
    let cores: cmap<int, int> = CMap.empty
    let a: cmap<int, string> = CMap.empty
    CMap.addOrUpdate 1 "A" a |> ignore

    let bySlot = cores |> AMap.groupBy(fun _ v -> v % 3)

    let b: amap<int, int> =
      bySlot |> AMap.mapA(fun _ g -> g |> AMap.fold (fun acc _ v -> acc + v) 0)

    let j =
      AMap.joinOn (CMap.value a) b (fun _ _ -> 1) (fun _ lV rV ->
        AVal.map2
          (fun l r ->
            ValueSome(
              l
              + "."
              + (match r with
                 | ValueSome v -> string v
                 | ValueNone -> "x")
            ))
          lV
          rV)

    let found = AMap.tryFind 1 j

    let total = j |> AMap.fold (fun acc _ v -> acc + ";" + v) ""

    let coreModel = Dictionary<int, int>()

    let expected() =
      let groups = Dictionary<int, int>()

      for KeyValue(_, v) in coreModel do
        let g = v % 3
        groups[g] <- groups.GetValueOrDefault(g) + v

      match groups.TryGetValue(1) with
      | true, sum -> "A." + string sum
      | false, _ -> "A.x"

    let apply(op: ChainCoreOp) =
      match op with
      | CoreUpsert(k, v) ->
        CMap.addOrUpdate k v cores |> ignore
        coreModel[k] <- v
      | CoreRemove k ->
        CMap.remove k cores |> ignore
        coreModel.Remove k |> ignore

    for op in ops do
      apply op

      let exp = ValueSome(expected())

      if AVal.getValue found <> exp then
        failwithf
          "tryFind mismatch after %A: actual=%A expected=%A"
          op
          (AVal.getValue found)
          exp

      let expFold = ";" + expected()

      if AVal.getValue total <> expFold then
        failwithf
          "fold mismatch after %A: actual=%A expected=%A"
          op
          (AVal.getValue total)
          expFold

  Check.One(scenarioConfig, prop)

// =============================================================================
// Issue #113 follow-up: forced-scenario coverage for the remaining snapshot
// families (collect entries, set contains/count resyncs, list tails, ofAVal).
// Each test drives one lazy scalar (`total`) through a groupBy -> mapA(fold)
// chain so that the dependency under test ends its drain dirty; reads go
// through the top consumer only, and the first read happens before any core
// exists (the trigger).
// =============================================================================

[<Fact>]
let ``ASet collect tracks cascading inner sets under core churn``() =
  let cores: cmap<int, int> = CMap.empty

  let total: aval<int> =
    let bySlot = cores |> AMap.groupBy(fun _ v -> v % 3)

    let sums =
      bySlot |> AMap.mapA(fun _ g -> g |> AMap.fold (fun acc _ v -> acc + v) 0)

    sums |> AMap.fold (fun acc _ v -> acc + v) 0

  let buckets = CSet.ofSeq [ 1; 2 ]

  let innerFor x =
    ASet.ofSeq [ x ] |> ASet.mapA(fun v -> AVal.map (fun t -> v * 10 + t) total)

  let merged = ASet.collect innerFor (CSet.value buckets)

  // Model: sum of (10 * bucket + total) over the fixed buckets.
  let expected() =
    (10 * 1 + 10 * 2) + 2 * AVal.getValue total

  let apply(op: ChainCoreOp) =
    match op with
    | CoreUpsert(k, v) -> CMap.addOrUpdate k v cores |> ignore
    | CoreRemove k -> CMap.remove k cores |> ignore

  for op in
    [ CoreUpsert(1, 5); CoreRemove(1); CoreUpsert(2, 7); CoreUpsert(1, 2) ] do
    apply op

    let actual = merged |> ASet.fold (fun acc v -> acc + v) 0 |> AVal.getValue

    if actual <> expected() then
      failwithf
        "mismatch after %A: actual=%A expected=%A"
        op
        actual
        (expected())

[<Fact>]
let ``ASet contains and count track a filtered cascading source under core churn``
  ()
  =
  let cores: cmap<int, int> = CMap.empty

  let total: aval<int> =
    let bySlot = cores |> AMap.groupBy(fun _ v -> v % 3)

    let sums =
      bySlot |> AMap.mapA(fun _ g -> g |> AMap.fold (fun acc _ v -> acc + v) 0)

    sums |> AMap.fold (fun acc _ v -> acc + v) 0

  let baseSet = CSet.ofSeq [ 0..4 ]

  let filtered =
    CSet.value baseSet
    |> ASet.filterA(fun x -> AVal.map (fun t -> (x + t) % 2 = 0) total)

  let has3 = ASet.contains 3 filtered
  let cnt = ASet.count filtered

  let expectedCount() =
    let t = AVal.getValue total
    [ 0..4 ] |> List.filter(fun x -> (x + t) % 2 = 0) |> List.length

  let expectedHas3() = (3 + AVal.getValue total) % 2 = 0

  let apply(op: ChainCoreOp) =
    match op with
    | CoreUpsert(k, v) -> CMap.addOrUpdate k v cores |> ignore
    | CoreRemove k -> CMap.remove k cores |> ignore

  for op in
    [ CoreUpsert(1, 5); CoreUpsert(2, 7); CoreRemove(1); CoreUpsert(3, 6) ] do
    apply op

    if AVal.getValue cnt <> expectedCount() then
      failwithf
        "count mismatch after %A: actual=%A expected=%A"
        op
        (AVal.getValue cnt)
        (expectedCount())

    if AVal.getValue has3 <> expectedHas3() then
      failwithf
        "contains mismatch after %A: actual=%A expected=%A"
        op
        (AVal.getValue has3)
        (expectedHas3())

[<Fact>]
let ``AList tails track cascading element values under core churn``() =
  let cores: cmap<int, int> = CMap.empty

  let total: aval<int> =
    let bySlot = cores |> AMap.groupBy(fun _ v -> v % 3)

    let sums =
      bySlot |> AMap.mapA(fun _ g -> g |> AMap.fold (fun acc _ v -> acc + v) 0)

    sums |> AMap.fold (fun acc _ v -> acc + v) 0

  let cl = CList.ofSeq [ 10; 20; 30 ]

  let lst =
    CList.value cl |> AList.mapA(fun v -> AVal.map (fun t -> v + t) total)

  let srt = AList.sort lst

  let expected() =
    let t = AVal.getValue total
    [ 10; 20; 30 ] |> List.sum |> (+)(3 * t)

  let apply(op: ChainCoreOp) =
    match op with
    | CoreUpsert(k, v) -> CMap.addOrUpdate k v cores |> ignore
    | CoreRemove k -> CMap.remove k cores |> ignore

  for op in [ CoreUpsert(1, 5); CoreRemove(1); CoreUpsert(2, 7) ] do
    apply op

    let sumLst = lst |> AList.fold (fun acc v -> acc + v) 0 |> AVal.getValue
    let sumSrt = srt |> AList.fold (fun acc v -> acc + v) 0 |> AVal.getValue

    if sumLst <> expected() then
      failwithf
        "list fold mismatch after %A: actual=%A expected=%A"
        op
        sumLst
        (expected())

    if sumSrt <> expected() then
      failwithf
        "sorted list fold mismatch after %A: actual=%A expected=%A"
        op
        sumSrt
        (expected())

[<Fact>]
let ``AList ofAVal tracks scalar-driven list changes under core churn``() =
  let cores: cmap<int, int> = CMap.empty

  let total: aval<int> =
    let bySlot = cores |> AMap.groupBy(fun _ v -> v % 3)

    let sums =
      bySlot |> AMap.mapA(fun _ g -> g |> AMap.fold (fun acc _ v -> acc + v) 0)

    sums |> AMap.fold (fun acc _ v -> acc + v) 0

  let alst = AList.ofAVal(total |> AVal.map(fun t -> [ 10 + t; 20 + t ]))

  let expected() =
    let t = AVal.getValue total
    (10 + t) + (20 + t)

  let apply(op: ChainCoreOp) =
    match op with
    | CoreUpsert(k, v) -> CMap.addOrUpdate k v cores |> ignore
    | CoreRemove k -> CMap.remove k cores |> ignore

  for op in [ CoreUpsert(1, 5); CoreRemove(1); CoreUpsert(2, 7); CoreRemove(2) ] do
    apply op

    let cnt = AList.count alst |> AVal.getValue
    let sum = alst |> AList.fold (fun acc v -> acc + v) 0 |> AVal.getValue

    if cnt <> 2 then
      failwithf "count mismatch after %A: actual=%i" op cnt

    if sum <> expected() then
      failwithf
        "sum mismatch after %A: actual=%A expected=%A"
        op
        sum
        (expected())
