module Mibo.Adaptive.Benchmarks

open System.Threading
open System.Threading.Tasks
open System.Collections.Generic
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Running
open FSharp.Data.Adaptive

// =============================================================================
// Basic Value Benchmarks
// =============================================================================

[<MemoryDiagnoser>]
type ValueBenchmarks() =
  let mutable slopInput: Mibo.Adaptive.ChangeableValue<int> =
    Unchecked.defaultof<_>

  let mutable slopMapped: Mibo.Adaptive.IAdaptiveValue<int> =
    Unchecked.defaultof<_>

  let mutable fdaInput: cval<int> = Unchecked.defaultof<_>
  let mutable fdaMapped: aval<int> = Unchecked.defaultof<_>

  [<Params(1000)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member _.Setup() =
    slopInput <- Mibo.Adaptive.CVal.create 0

    slopMapped <-
      Mibo.Adaptive.AVal.map
        (fun value -> value + 1)
        (Mibo.Adaptive.CVal.value slopInput)

    fdaInput <- cval 0
    fdaMapped <- AVal.map (fun value -> value + 1) fdaInput

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    for i in 1 .. this.Iterations do
      slopInput.Set(i)
      let _ = Mibo.Adaptive.AVal.getValue slopMapped
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.Iterations do
      transact(fun () -> fdaInput.Value <- i)
      let _ = AVal.force fdaMapped
      ()

// =============================================================================
// Deep Dependency Chain Benchmark
// =============================================================================

[<MemoryDiagnoser>]
type DeepChainBenchmarks() =
  let mutable slopInput: Mibo.Adaptive.ChangeableValue<int> =
    Unchecked.defaultof<_>

  let mutable slopChain: Mibo.Adaptive.IAdaptiveValue<int> =
    Unchecked.defaultof<_>

  let mutable fdaInput: cval<int> = Unchecked.defaultof<_>
  let mutable fdaChain: aval<int> = Unchecked.defaultof<_>

  [<Params(5, 10, 20, 100, 1000)>]
  member val Depth = 0 with get, set

  [<Params(100)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member this.Setup() =
    // AdaptiveSlop chain
    slopInput <- Mibo.Adaptive.CVal.create 0

    let mutable current: Mibo.Adaptive.IAdaptiveValue<int> =
      Mibo.Adaptive.CVal.value slopInput

    for _ in 1 .. this.Depth do
      current <- Mibo.Adaptive.AVal.map (fun v -> v + 1) current

    slopChain <- current

    // FDA chain
    fdaInput <- cval 0
    let mutable fdaCurrent: aval<int> = fdaInput

    for _ in 1 .. this.Depth do
      fdaCurrent <- AVal.map (fun v -> v + 1) fdaCurrent

    fdaChain <- fdaCurrent

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    for i in 1 .. this.Iterations do
      slopInput.Set(i)
      let _ = Mibo.Adaptive.AVal.getValue slopChain
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.Iterations do
      transact(fun () -> fdaInput.Value <- i)
      let _ = AVal.force fdaChain
      ()

// =============================================================================
// Map2/Combine Benchmark
// =============================================================================

[<MemoryDiagnoser>]
type Map2Benchmarks() =
  let mutable slopLeft: Mibo.Adaptive.ChangeableValue<int> =
    Unchecked.defaultof<_>

  let mutable slopRight: Mibo.Adaptive.ChangeableValue<int> =
    Unchecked.defaultof<_>

  let mutable slopCombined: Mibo.Adaptive.IAdaptiveValue<int> =
    Unchecked.defaultof<_>

  let mutable fdaLeft: cval<int> = Unchecked.defaultof<_>
  let mutable fdaRight: cval<int> = Unchecked.defaultof<_>
  let mutable fdaCombined: aval<int> = Unchecked.defaultof<_>

  [<Params(1000)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member _.Setup() =
    slopLeft <- Mibo.Adaptive.CVal.create 0
    slopRight <- Mibo.Adaptive.CVal.create 0

    slopCombined <-
      Mibo.Adaptive.AVal.map2
        (+)
        (Mibo.Adaptive.CVal.value slopLeft)
        (Mibo.Adaptive.CVal.value slopRight)

    fdaLeft <- cval 0
    fdaRight <- cval 0
    fdaCombined <- AVal.map2 (+) fdaLeft fdaRight

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    for i in 1 .. this.Iterations do
      slopLeft.Set(i)
      slopRight.Set(i * 2)
      let _ = Mibo.Adaptive.AVal.getValue slopCombined
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.Iterations do
      transact(fun () ->
        fdaLeft.Value <- i
        fdaRight.Value <- i * 2)

      let _ = AVal.force fdaCombined
      ()

// =============================================================================
// Bind/Dynamic Graph Benchmark
// =============================================================================

[<MemoryDiagnoser>]
type BindBenchmarks() =
  let mutable slopSelector: Mibo.Adaptive.ChangeableValue<bool> =
    Unchecked.defaultof<_>

  let mutable slopLeft: Mibo.Adaptive.ChangeableValue<int> =
    Unchecked.defaultof<_>

  let mutable slopRight: Mibo.Adaptive.ChangeableValue<int> =
    Unchecked.defaultof<_>

  let mutable slopBound: Mibo.Adaptive.IAdaptiveValue<int> =
    Unchecked.defaultof<_>

  let mutable fdaSelector: cval<bool> = Unchecked.defaultof<_>
  let mutable fdaLeft: cval<int> = Unchecked.defaultof<_>
  let mutable fdaRight: cval<int> = Unchecked.defaultof<_>
  let mutable fdaBound: aval<int> = Unchecked.defaultof<_>

  [<Params(1000)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member _.Setup() =
    slopSelector <- Mibo.Adaptive.CVal.create true
    slopLeft <- Mibo.Adaptive.CVal.create 1
    slopRight <- Mibo.Adaptive.CVal.create 2

    slopBound <-
      Mibo.Adaptive.AVal.bind
        (fun sel ->
          if sel then
            Mibo.Adaptive.CVal.value slopLeft
          else
            Mibo.Adaptive.CVal.value slopRight)
        (Mibo.Adaptive.CVal.value slopSelector)

    fdaSelector <- cval true
    fdaLeft <- cval 1
    fdaRight <- cval 2

    fdaBound <-
      AVal.bind
        (fun sel -> if sel then fdaLeft :> aval<_> else fdaRight :> aval<_>)
        fdaSelector

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    for i in 1 .. this.Iterations do
      slopSelector.Set(i % 2 = 0)
      slopLeft.Set(i)
      slopRight.Set(i * 2)
      let _ = Mibo.Adaptive.AVal.getValue slopBound
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.Iterations do
      transact(fun () ->
        fdaSelector.Value <- (i % 2 = 0)
        fdaLeft.Value <- i
        fdaRight.Value <- i * 2)

      let _ = AVal.force fdaBound
      ()

// =============================================================================
// Transaction Batching Benchmark
// =============================================================================

[<MemoryDiagnoser>]
type TransactionBenchmarks() =
  let mutable slopValues: Mibo.Adaptive.ChangeableValue<int>[] = [||]

  let mutable slopSum: Mibo.Adaptive.IAdaptiveValue<int> =
    Unchecked.defaultof<_>

  let mutable fdaValues: cval<int>[] = [||]
  let mutable fdaSum: aval<int> = Unchecked.defaultof<_>

  [<Params(10)>]
  member val ValueCount = 0 with get, set

  [<Params(500)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member this.Setup() =
    // AdaptiveSlop
    slopValues <-
      Array.init this.ValueCount (fun _ -> Mibo.Adaptive.CVal.create 0)

    let mutable sum: Mibo.Adaptive.IAdaptiveValue<int> =
      Mibo.Adaptive.AVal.constant 0

    for v in slopValues do
      sum <- Mibo.Adaptive.AVal.map2 (+) sum (Mibo.Adaptive.CVal.value v)

    slopSum <- sum

    // FDA
    fdaValues <- Array.init this.ValueCount (fun _ -> cval 0)
    let mutable fdaSumVal: aval<int> = AVal.constant 0

    for v in fdaValues do
      fdaSumVal <- AVal.map2 (+) fdaSumVal v

    fdaSum <- fdaSumVal

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop_Batched() =
    for i in 1 .. this.Iterations do
      Mibo.Adaptive.Transaction.run(fun () ->
        for j in 0 .. slopValues.Length - 1 do
          slopValues[j].Set(i + j))
      |> ignore

      let _ = Mibo.Adaptive.AVal.getValue slopSum
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive_Batched() =
    for i in 1 .. this.Iterations do
      transact(fun () ->
        for j in 0 .. fdaValues.Length - 1 do
          fdaValues[j].Value <- i + j)

      let _ = AVal.force fdaSum
      ()

// =============================================================================
// Set Benchmarks
// =============================================================================

[<MemoryDiagnoser>]
type SetBenchmarks() =
  let mutable slopSet: Mibo.Adaptive.ChangeableSet<int> = Unchecked.defaultof<_>

  let mutable slopASet: Mibo.Adaptive.IAdaptiveSet<int> = Unchecked.defaultof<_>

  let mutable fdaSet: cset<int> = Unchecked.defaultof<_>
  let mutable fdaASet: aset<int> = Unchecked.defaultof<_>

  [<Params(1000)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member _.Setup() =
    slopSet <- Mibo.Adaptive.CSet.empty<int>
    slopASet <- Mibo.Adaptive.CSet.value slopSet
    fdaSet <- cset<int> []
    fdaASet <- fdaSet :> aset<int>

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    for i in 1 .. this.Iterations do
      slopSet.Add(i)
      slopSet.Remove(i)
      let _ = Mibo.Adaptive.ASet.getValue slopASet
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.Iterations do
      transact(fun () ->
        fdaSet.Add(i) |> ignore
        fdaSet.Remove(i) |> ignore)

      let _ = ASet.force fdaASet
      ()

// =============================================================================
// Set Filter/Map Benchmarks
// =============================================================================

[<MemoryDiagnoser>]
type SetTransformBenchmarks() =
  let mutable slopSet: Mibo.Adaptive.ChangeableSet<int> = Unchecked.defaultof<_>

  let mutable slopFiltered: Mibo.Adaptive.IAdaptiveSet<int> =
    Unchecked.defaultof<_>

  let mutable fdaSet: cset<int> = Unchecked.defaultof<_>
  let mutable fdaFiltered: aset<int> = Unchecked.defaultof<_>

  [<Params(500)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member _.Setup() =
    slopSet <- Mibo.Adaptive.CSet.ofSeq(seq { 1..100 })

    let mapped =
      Mibo.Adaptive.ASet.map (fun v -> v * 2) (Mibo.Adaptive.CSet.value slopSet)

    slopFiltered <- Mibo.Adaptive.ASet.filter (fun v -> v % 4 = 0) mapped

    fdaSet <- cset(seq { 1..100 })
    let fdaMapped = ASet.map (fun v -> v * 2) fdaSet
    fdaFiltered <- ASet.filter (fun v -> v % 4 = 0) fdaMapped

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    for i in 1 .. this.Iterations do
      slopSet.Add(1000 + i)
      slopSet.Remove(1000 + i - 1)
      let _ = Mibo.Adaptive.ASet.getValue slopFiltered
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.Iterations do
      transact(fun () ->
        fdaSet.Add(1000 + i) |> ignore
        fdaSet.Remove(1000 + i - 1) |> ignore)

      let _ = ASet.force fdaFiltered
      ()

// =============================================================================
// Map Benchmarks
// =============================================================================

[<MemoryDiagnoser>]
type MapBenchmarks() =
  let mutable slopMap: Mibo.Adaptive.ChangeableMap<int, int> =
    Unchecked.defaultof<_>

  let mutable slopAMap: Mibo.Adaptive.IAdaptiveMap<int, int> =
    Unchecked.defaultof<_>

  let mutable fdaMap: cmap<int, int> = Unchecked.defaultof<_>
  let mutable fdaAMap: amap<int, int> = Unchecked.defaultof<_>

  [<Params(1000)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member _.Setup() =
    slopMap <- Mibo.Adaptive.CMap.empty<int, int>
    slopAMap <- Mibo.Adaptive.CMap.value slopMap
    fdaMap <- cmap(Seq.empty<int * int>)
    fdaAMap <- fdaMap :> amap<int, int>

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    for i in 1 .. this.Iterations do
      slopMap.AddOrUpdate i (i * 2)
      slopMap.Remove(i)
      let _ = Mibo.Adaptive.AMap.getValue slopAMap
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.Iterations do
      transact(fun () ->
        fdaMap.[i] <- i * 2
        fdaMap.Remove(i) |> ignore)

      let _ = AMap.force fdaAMap
      ()

// =============================================================================
// Map Filter/Transform Benchmarks
// =============================================================================

[<MemoryDiagnoser>]
type MapTransformBenchmarks() =
  let mutable slopMap: Mibo.Adaptive.ChangeableMap<int, int> =
    Unchecked.defaultof<_>

  let mutable slopFiltered: Mibo.Adaptive.IAdaptiveMap<int, int> =
    Unchecked.defaultof<_>

  let mutable fdaMap: cmap<int, int> = Unchecked.defaultof<_>
  let mutable fdaFiltered: amap<int, int> = Unchecked.defaultof<_>

  [<Params(500)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member _.Setup() =
    slopMap <- Mibo.Adaptive.CMap.ofSeq(seq { for i in 1..100 -> i, i * 10 })

    let mapped =
      Mibo.Adaptive.AMap.map
        (fun _ v -> v + 1)
        (Mibo.Adaptive.CMap.value slopMap)

    slopFiltered <- Mibo.Adaptive.AMap.filter (fun _ v -> v > 50) mapped

    fdaMap <- cmap(seq { for i in 1..100 -> i, i * 10 })
    let fdaMapped = AMap.map (fun _ v -> v + 1) fdaMap
    fdaFiltered <- AMap.filter (fun _ v -> v > 50) fdaMapped

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    for i in 1 .. this.Iterations do
      slopMap.AddOrUpdate (1000 + i) ((1000 + i) * 10)
      slopMap.Remove(1000 + i - 1)
      let _ = Mibo.Adaptive.AMap.getValue slopFiltered
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.Iterations do
      transact(fun () ->
        fdaMap.[1000 + i] <- (1000 + i) * 10
        fdaMap.Remove(1000 + i - 1) |> ignore)

      let _ = AMap.force fdaFiltered
      ()

// =============================================================================
// List Benchmarks (docs/ALIST-DESIGN.md)
//
// The write/read benchmark mirrors FDA's CollectionUpdate.CList_Map_GetValue
// (100 appends in one transaction, then force the mapped list). The transform
// and append benchmarks mirror the set benchmarks above. FDA's IndexList/
// Index/ListDelta benchmarks do not apply: those measure the persistent
// structures we deliberately do not have (docs/ALIST-DESIGN.md §2).
// =============================================================================

[<MemoryDiagnoser>]
type ListWriteReadBenchmarks() =
  let mutable slopList: Mibo.Adaptive.ChangeableList<int> =
    Unchecked.defaultof<_>

  let mutable slopMapped: Mibo.Adaptive.IAdaptiveList<int> =
    Unchecked.defaultof<_>

  let mutable fdaList: clist<int> = Unchecked.defaultof<_>
  let mutable fdaMapped: alist<int> = Unchecked.defaultof<_>

  [<Params(0, 1000, 10000, 100000)>]
  member val Count = 0 with get, set

  // FDA's CollectionUpdate rebuilds per iteration: the measured op must be
  // stationary (our first run grew the list by 101 appends per iteration,
  // so the force array grew unbounded and the allocation column was
  // meaningless).
  [<IterationSetup>]
  member this.Setup() =
    let data = Array.init this.Count (fun i -> i)
    slopList <- Mibo.Adaptive.CList.ofArray data

    slopMapped <-
      Mibo.Adaptive.AList.map
        (fun i -> i * 2)
        (Mibo.Adaptive.CList.value slopList)

    Mibo.Adaptive.AList.force slopMapped |> ignore
    fdaList <- clist data
    fdaMapped <- AList.map (fun i -> i * 2) fdaList
    AList.force fdaMapped |> ignore

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    Mibo.Adaptive.Transaction.run(fun () ->
      for i in 0..100 do
        slopList.Append(i) |> ignore)

    Mibo.Adaptive.AList.force slopMapped |> ignore

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    transact(fun () ->
      for i in 0..100 do
        fdaList.Append(i) |> ignore)

    AList.force fdaMapped |> ignore

[<MemoryDiagnoser>]
type ListTransformBenchmarks() =
  let mutable slopList: Mibo.Adaptive.ChangeableList<int> =
    Unchecked.defaultof<_>

  let mutable slopFiltered: Mibo.Adaptive.IAdaptiveList<int> =
    Unchecked.defaultof<_>

  let mutable fdaList: clist<int> = Unchecked.defaultof<_>
  let mutable fdaFiltered: alist<int> = Unchecked.defaultof<_>

  [<Params(500)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member _.Setup() =
    slopList <- Mibo.Adaptive.CList.ofSeq(seq { 1..100 })

    let mapped =
      Mibo.Adaptive.AList.map
        (fun v -> v * 2)
        (Mibo.Adaptive.CList.value slopList)

    slopFiltered <- Mibo.Adaptive.AList.filter (fun v -> v % 4 = 0) mapped

    fdaList <- clist(seq { 1..100 })
    let fdaMapped = AList.map (fun v -> v * 2) fdaList
    fdaFiltered <- AList.filter (fun v -> v % 4 = 0) fdaMapped

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    for i in 1 .. this.Iterations do
      Mibo.Adaptive.CList.append (1000 + i) slopList
      Mibo.Adaptive.CList.removeAt 0 slopList
      let _ = Mibo.Adaptive.AList.getValue slopFiltered
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.Iterations do
      transact(fun () ->
        fdaList.Append(1000 + i) |> ignore
        fdaList.RemoveAt(0) |> ignore)

      let _ = AList.force fdaFiltered
      ()

[<MemoryDiagnoser>]
type ListAppendBenchmarks() =
  let mutable slopLeft: Mibo.Adaptive.ChangeableList<int> =
    Unchecked.defaultof<_>

  let mutable slopRight: Mibo.Adaptive.ChangeableList<int> =
    Unchecked.defaultof<_>

  let mutable slopAppended: Mibo.Adaptive.IAdaptiveList<int> =
    Unchecked.defaultof<_>

  let mutable fdaLeft: clist<int> = Unchecked.defaultof<_>
  let mutable fdaRight: clist<int> = Unchecked.defaultof<_>
  let mutable fdaAppended: alist<int> = Unchecked.defaultof<_>

  [<Params(500)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member _.Setup() =
    slopLeft <- Mibo.Adaptive.CList.ofSeq(seq { 1..50 })
    slopRight <- Mibo.Adaptive.CList.ofSeq(seq { 51..100 })

    slopAppended <-
      Mibo.Adaptive.AList.append
        (Mibo.Adaptive.CList.value slopLeft)
        (Mibo.Adaptive.CList.value slopRight)

    fdaLeft <- clist(seq { 1..50 })
    fdaRight <- clist(seq { 51..100 })
    fdaAppended <- AList.append fdaLeft fdaRight

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    for i in 1 .. this.Iterations do
      Mibo.Adaptive.CList.append (1000 + i) slopLeft
      Mibo.Adaptive.CList.removeAt 0 slopLeft
      let _ = Mibo.Adaptive.AList.getValue slopAppended
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.Iterations do
      transact(fun () ->
        fdaLeft.Append(1000 + i) |> ignore
        fdaLeft.RemoveAt(0) |> ignore)

      let _ = AList.force fdaAppended
      ()

// =============================================================================
// Large Collection Benchmark
// =============================================================================

[<MemoryDiagnoser>]
type LargeCollectionBenchmarks() =
  let mutable slopSet: Mibo.Adaptive.ChangeableSet<int> = Unchecked.defaultof<_>

  let mutable slopASet: Mibo.Adaptive.IAdaptiveSet<int> = Unchecked.defaultof<_>

  let mutable fdaSet: cset<int> = Unchecked.defaultof<_>
  let mutable fdaASet: aset<int> = Unchecked.defaultof<_>

  [<Params(10000)>]
  member val InitialSize = 0 with get, set

  [<Params(200)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member this.Setup() =
    slopSet <- Mibo.Adaptive.CSet.ofSeq(seq { 1 .. this.InitialSize })
    slopASet <- Mibo.Adaptive.CSet.value slopSet

    fdaSet <- cset(seq { 1 .. this.InitialSize })
    fdaASet <- fdaSet :> aset<int>

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    let baseIdx = this.InitialSize

    for i in 1 .. this.Iterations do
      slopSet.Add(baseIdx + i)
      let _ = Mibo.Adaptive.ASet.getValue slopASet
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    let baseIdx = this.InitialSize

    for i in 1 .. this.Iterations do
      transact(fun () -> fdaSet.Add(baseIdx + i) |> ignore)
      let _ = ASet.force fdaASet
      ()

// =============================================================================
// Read-Heavy Benchmark (many reads, few writes)
// =============================================================================

[<MemoryDiagnoser>]
type ReadHeavyBenchmarks() =
  let mutable slopInput: Mibo.Adaptive.ChangeableValue<int> =
    Unchecked.defaultof<_>

  let mutable slopMapped: Mibo.Adaptive.IAdaptiveValue<int> =
    Unchecked.defaultof<_>

  let mutable fdaInput: cval<int> = Unchecked.defaultof<_>
  let mutable fdaMapped: aval<int> = Unchecked.defaultof<_>

  [<Params(100)>]
  member val WriteCount = 0 with get, set

  [<Params(50)>]
  member val ReadsPerWrite = 0 with get, set

  [<GlobalSetup>]
  member _.Setup() =
    slopInput <- Mibo.Adaptive.CVal.create 0

    slopMapped <-
      Mibo.Adaptive.AVal.map
        (fun v -> v * 2)
        (Mibo.Adaptive.CVal.value slopInput)

    fdaInput <- cval 0
    fdaMapped <- AVal.map (fun v -> v * 2) fdaInput

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    for i in 1 .. this.WriteCount do
      slopInput.Set(i)

      for _ in 1 .. this.ReadsPerWrite do
        let _ = Mibo.Adaptive.AVal.getValue slopMapped
        ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.WriteCount do
      transact(fun () -> fdaInput.Value <- i)

      for _ in 1 .. this.ReadsPerWrite do
        let _ = AVal.force fdaMapped
        ()

// =============================================================================
// Diamond Dependency Graph Benchmark
// =============================================================================

[<MemoryDiagnoser>]
type DiamondGraphBenchmarks() =
  // Diamond pattern: A -> B, A -> C, B -> D, C -> D
  let mutable slopA: Mibo.Adaptive.ChangeableValue<int> = Unchecked.defaultof<_>

  let mutable slopD: Mibo.Adaptive.IAdaptiveValue<int> = Unchecked.defaultof<_>

  let mutable fdaA: cval<int> = Unchecked.defaultof<_>
  let mutable fdaD: aval<int> = Unchecked.defaultof<_>

  [<Params(1000)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member _.Setup() =
    // AdaptiveSlop diamond
    slopA <- Mibo.Adaptive.CVal.create 0
    let aVal = Mibo.Adaptive.CVal.value slopA
    let slopB = Mibo.Adaptive.AVal.map (fun v -> v + 1) aVal
    let slopC = Mibo.Adaptive.AVal.map (fun v -> v * 2) aVal
    slopD <- Mibo.Adaptive.AVal.map2 (+) slopB slopC

    // FDA diamond
    fdaA <- cval 0
    let fdaB = AVal.map (fun v -> v + 1) fdaA
    let fdaC = AVal.map (fun v -> v * 2) fdaA
    fdaD <- AVal.map2 (+) fdaB fdaC

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    for i in 1 .. this.Iterations do
      slopA.Set(i)
      let _ = Mibo.Adaptive.AVal.getValue slopD
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.Iterations do
      transact(fun () -> fdaA.Value <- i)
      let _ = AVal.force fdaD
      ()

// =============================================================================
// Wide Tree Benchmark (fan-in: single output depending on N inputs)
// =============================================================================

[<MemoryDiagnoser>]
type WideTreeBenchmarks() =
  // Wide pattern: N inputs all feeding into one output via map2 chain
  // input1 --\
  // input2 ---\
  // ...        --> sum
  // inputN ---/
  let mutable slopInputs: Mibo.Adaptive.ChangeableValue<int>[] = [||]

  let mutable slopSum: Mibo.Adaptive.IAdaptiveValue<int> =
    Unchecked.defaultof<_>

  let mutable fdaInputs: cval<int>[] = [||]
  let mutable fdaSum: aval<int> = Unchecked.defaultof<_>

  [<Params(10, 50, 100, 500)>]
  member val Width = 0 with get, set

  [<Params(100)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member this.Setup() =
    // AdaptiveSlop wide tree
    slopInputs <- Array.init this.Width (fun i -> Mibo.Adaptive.CVal.create i)

    let mutable sum: Mibo.Adaptive.IAdaptiveValue<int> =
      Mibo.Adaptive.CVal.value slopInputs.[0]

    for i in 1 .. this.Width - 1 do
      sum <-
        Mibo.Adaptive.AVal.map2
          (+)
          sum
          (Mibo.Adaptive.CVal.value slopInputs.[i])

    slopSum <- sum

    // FDA wide tree
    fdaInputs <- Array.init this.Width (fun i -> cval i)
    let mutable fdaSumVal: aval<int> = fdaInputs.[0]

    for i in 1 .. this.Width - 1 do
      fdaSumVal <- AVal.map2 (+) fdaSumVal fdaInputs.[i]

    fdaSum <- fdaSumVal

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    for i in 1 .. this.Iterations do
      // Change one input in the middle
      slopInputs.[this.Width / 2].Set(i)
      let _ = Mibo.Adaptive.AVal.getValue slopSum
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.Iterations do
      transact(fun () -> fdaInputs.[this.Width / 2].Value <- i)
      let _ = AVal.force fdaSum
      ()

// =============================================================================
// Optimized Wide Tree Benchmark using reduce (single node instead of map2 chain)
// =============================================================================

[<MemoryDiagnoser>]
type OptimizedWideTreeBenchmarks() =
  // Compares: map2 chain vs reduce (single node) vs FDA
  let mutable slopInputsMap2: Mibo.Adaptive.ChangeableValue<int>[] = [||]

  let mutable slopSumMap2: Mibo.Adaptive.IAdaptiveValue<int> =
    Unchecked.defaultof<_>

  let mutable slopInputsReduce: Mibo.Adaptive.ChangeableValue<int>[] = [||]

  let mutable slopSumReduce: Mibo.Adaptive.IAdaptiveValue<int> =
    Unchecked.defaultof<_>

  let mutable fdaInputs: cval<int>[] = [||]
  let mutable fdaSum: aval<int> = Unchecked.defaultof<_>

  [<Params(10, 50, 100, 500)>]
  member val Width = 0 with get, set

  [<Params(100)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member this.Setup() =
    // AdaptiveSlop with map2 chain (baseline)
    slopInputsMap2 <-
      Array.init this.Width (fun i -> Mibo.Adaptive.CVal.create i)

    let mutable sum: Mibo.Adaptive.IAdaptiveValue<int> =
      Mibo.Adaptive.CVal.value slopInputsMap2.[0]

    for i in 1 .. this.Width - 1 do
      sum <-
        Mibo.Adaptive.AVal.map2
          (+)
          sum
          (Mibo.Adaptive.CVal.value slopInputsMap2.[i])

    slopSumMap2 <- sum

    // AdaptiveSlop with reduce (optimized - single node)
    slopInputsReduce <-
      Array.init this.Width (fun i -> Mibo.Adaptive.CVal.create i)

    let deps = slopInputsReduce |> Array.map Mibo.Adaptive.CVal.value
    slopSumReduce <- Mibo.Adaptive.AVal.reduce 0 (+) deps

    // FDA with map2 chain
    fdaInputs <- Array.init this.Width (fun i -> cval i)
    let mutable fdaSumVal: aval<int> = fdaInputs.[0]

    for i in 1 .. this.Width - 1 do
      fdaSumVal <- AVal.map2 (+) fdaSumVal fdaInputs.[i]

    fdaSum <- fdaSumVal

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop_Map2Chain() =
    for i in 1 .. this.Iterations do
      slopInputsMap2.[this.Width / 2].Set(i)
      let _ = Mibo.Adaptive.AVal.getValue slopSumMap2
      ()

  [<Benchmark>]
  member this.AdaptiveSlop_Reduce() =
    for i in 1 .. this.Iterations do
      slopInputsReduce.[this.Width / 2].Set(i)
      let _ = Mibo.Adaptive.AVal.getValue slopSumReduce
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.Iterations do
      transact(fun () -> fdaInputs.[this.Width / 2].Value <- i)
      let _ = AVal.force fdaSum
      ()

// =============================================================================
// Deep+Wide Tree Benchmark (depth with branching factor)
// =============================================================================

[<MemoryDiagnoser>]
type DeepWideBenchmarks() =
  // Tree with depth D and branching factor B
  // Each level has B children, creating B^D leaf nodes
  let mutable slopInputs: Mibo.Adaptive.ChangeableValue<int>[] = [||]

  let mutable slopRoot: Mibo.Adaptive.IAdaptiveValue<int> =
    Unchecked.defaultof<_>

  let mutable fdaInputs: cval<int>[] = [||]
  let mutable fdaRoot: aval<int> = Unchecked.defaultof<_>

  [<Params(3, 5, 7)>]
  member val Depth = 0 with get, set

  [<Params(2, 3, 4)>]
  member val BranchingFactor = 0 with get, set

  [<Params(50)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member this.Setup() =
    let leafCount = pown this.BranchingFactor this.Depth

    // AdaptiveSlop tree
    slopInputs <- Array.init leafCount (fun i -> Mibo.Adaptive.CVal.create i)

    // Build tree bottom-up by combining nodes at each level
    let rec buildLevel(nodes: Mibo.Adaptive.IAdaptiveValue<int>[]) =
      if nodes.Length = 1 then
        nodes.[0]
      else
        let parentCount =
          (nodes.Length + this.BranchingFactor - 1) / this.BranchingFactor

        let parents =
          Array.init parentCount (fun i ->
            let start = i * this.BranchingFactor
            let endIdx = min (start + this.BranchingFactor) nodes.Length
            let mutable combined = nodes.[start]

            for j in (start + 1) .. (endIdx - 1) do
              combined <- Mibo.Adaptive.AVal.map2 (+) combined nodes.[j]

            combined)

        buildLevel parents

    slopRoot <- buildLevel(slopInputs |> Array.map Mibo.Adaptive.CVal.value)

    // FDA tree
    fdaInputs <- Array.init leafCount (fun i -> cval i)

    let rec buildFdaLevel(nodes: aval<int>[]) =
      if nodes.Length = 1 then
        nodes.[0]
      else
        let parentCount =
          (nodes.Length + this.BranchingFactor - 1) / this.BranchingFactor

        let parents =
          Array.init parentCount (fun i ->
            let start = i * this.BranchingFactor
            let endIdx = min (start + this.BranchingFactor) nodes.Length
            let mutable combined = nodes.[start]

            for j in (start + 1) .. (endIdx - 1) do
              combined <- AVal.map2 (+) combined nodes.[j]

            combined)

        buildFdaLevel parents

    fdaRoot <- buildFdaLevel(fdaInputs |> Array.map(fun x -> x :> aval<int>))

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    for i in 1 .. this.Iterations do
      // Change a leaf node
      slopInputs.[slopInputs.Length / 2].Set(i)
      let _ = Mibo.Adaptive.AVal.getValue slopRoot
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    for i in 1 .. this.Iterations do
      transact(fun () -> fdaInputs.[fdaInputs.Length / 2].Value <- i)
      let _ = AVal.force fdaRoot
      ()

// =============================================================================
// Kipo PhysicsCache Benchmark (Pomo.Core Projections.fs PhysicsCache module)
// The real 60 Hz update / render read shape: per frame the sim advances every
// entity position (amap writes, the shape that was abandoned in Kipo because
// FDA's allocations were unbearable), and the render side forces the entity
// maps and rebuilds the movement snapshot: interpolated positions
// (start + v * dt), velocities-derived rotations (Atan2), and a spatial grid.
// =============================================================================

[<MemoryDiagnoser>]
type KipoPhysicsBenchmarks() =
  let rng = System.Random 42
  let cellSize = 4.0f

  let mutable slopTime: Mibo.Adaptive.ChangeableValue<float32> =
    Unchecked.defaultof<_>

  let mutable slopPositions
    : Mibo.Adaptive.ChangeableMap<int, System.Numerics.Vector3> =
    Unchecked.defaultof<_>

  let mutable slopVelocities
    : Mibo.Adaptive.ChangeableMap<int, System.Numerics.Vector3> =
    Unchecked.defaultof<_>

  let mutable slopModelConfig: Mibo.Adaptive.ChangeableMap<int, string> =
    Unchecked.defaultof<_>

  let mutable slopEntityScenario: Mibo.Adaptive.ChangeableMap<int, int> =
    Unchecked.defaultof<_>

  let mutable slopScenarios: Mibo.Adaptive.ChangeableMap<int, int> =
    Unchecked.defaultof<_>

  let mutable slopDerivedPositions
    : Mibo.Adaptive.IAdaptiveMap<int, System.Numerics.Vector3> =
    Unchecked.defaultof<_>

  let mutable slopDerivedRotations: Mibo.Adaptive.IAdaptiveMap<int, float32> =
    Unchecked.defaultof<_>

  let mutable fdaTime: cval<float32> = Unchecked.defaultof<_>

  let mutable fdaPositions: cmap<int, System.Numerics.Vector3> =
    Unchecked.defaultof<_>

  let mutable fdaVelocities: cmap<int, System.Numerics.Vector3> =
    Unchecked.defaultof<_>

  let mutable fdaModelConfig: cmap<int, string> = Unchecked.defaultof<_>
  let mutable fdaEntityScenario: cmap<int, int> = Unchecked.defaultof<_>
  let mutable fdaScenarios: cmap<int, int> = Unchecked.defaultof<_>

  let mutable fdaDerivedPositions: amap<int, System.Numerics.Vector3> =
    Unchecked.defaultof<_>

  let mutable fdaDerivedRotations: amap<int, float32> = Unchecked.defaultof<_>

  [<Params(250, 1000)>]
  member val EntityCount = 0 with get, set

  [<Params(50)>]
  member val Iterations = 0 with get, set

  member private this.BuildSnapshot
    (dt: float32)
    (positions: seq<int * System.Numerics.Vector3>)
    (getVelocity: int -> voption<System.Numerics.Vector3>)
    (getModelConfig: int -> voption<string>)
    (getScenario: int -> voption<int>)
    =
    // Faithful clone of PhysicsCache.calculateSnapshot: interpolated
    // positions, velocity-derived rotations, per-cell spatial grid.
    let positionsBuilder = Dictionary<int, System.Numerics.Vector3>()
    let rotationsBuilder = Dictionary<int, float32>()
    let gridBuilder = Dictionary<int, ResizeArray<int>>()

    for (id, startPos) in positions do
      match getScenario id with
      | ValueSome _ ->
        let v =
          getVelocity id
          |> ValueOption.defaultValue System.Numerics.Vector3.Zero

        let currentPos = startPos + v * dt
        positionsBuilder[id] <- currentPos

        let rotation =
          if v <> System.Numerics.Vector3.Zero then
            float32(System.Math.Atan2(float v.X, float v.Z))
          else
            0.0f

        rotationsBuilder[id] <- rotation

        match getModelConfig id with
        | ValueSome _ ->
          let cell =
            (int(currentPos.X / cellSize)) * 100000
            + int(currentPos.Z / cellSize)

          match gridBuilder.TryGetValue cell with
          | true, list -> list.Add id
          | _ -> gridBuilder[cell] <- ResizeArray([| id |])
        | _ -> ()
      | _ -> ()

    positionsBuilder.Count + rotationsBuilder.Count + gridBuilder.Count

  [<GlobalSetup>]
  member this.Setup() =
    // ---- AdaptiveSlop world ----
    slopTime <- Mibo.Adaptive.CVal.create 0.0f
    slopPositions <- Mibo.Adaptive.CMap.empty
    slopVelocities <- Mibo.Adaptive.CMap.empty
    slopModelConfig <- Mibo.Adaptive.CMap.empty
    slopEntityScenario <- Mibo.Adaptive.CMap.empty
    slopScenarios <- Mibo.Adaptive.CMap.empty

    for i in 0 .. this.EntityCount - 1 do
      slopPositions.AddOrUpdate
        i
        (System.Numerics.Vector3(float32 i, 0.0f, 0.0f))

      slopVelocities.AddOrUpdate
        i
        (System.Numerics.Vector3(rng.NextSingle(), 0.0f, rng.NextSingle()))

      slopModelConfig.AddOrUpdate i ("config-" + string(i % 8))
      slopEntityScenario.AddOrUpdate i 0

    slopScenarios.AddOrUpdate 0 0

    // ---- FDA world ----
    fdaTime <- cval 0.0f
    fdaPositions <- cmap<int, System.Numerics.Vector3>()
    fdaVelocities <- cmap<int, System.Numerics.Vector3>()
    fdaModelConfig <- cmap<int, string>()
    fdaEntityScenario <- cmap<int, int>()
    fdaScenarios <- cmap<int, int>()

    transact(fun () ->
      for i in 0 .. this.EntityCount - 1 do
        fdaPositions.[i] <- System.Numerics.Vector3(float32 i, 0.0f, 0.0f)

        fdaVelocities.[i] <-
          System.Numerics.Vector3(rng.NextSingle(), 0.0f, rng.NextSingle())

        fdaModelConfig.[i] <- "config-" + string(i % 8)
        fdaEntityScenario.[i] <- 0

      fdaScenarios.[0] <- 0)

    // Derived nodes: the graph holds the interpolated positions and the
    // velocity-derived rotations. The velocity lookup inside the mapping is a
    // dynamic read (valid while velocities never change; the fully dynamic
    // dependency is Phase 7 work).
    let velView = Mibo.Adaptive.CMap.value slopVelocities |> _.GetValue()

    slopDerivedPositions <-
      Mibo.Adaptive.AMap.map
        (fun id startPos ->
          let v =
            match velView.TryGetValue id with
            | true, v -> v
            | _ -> System.Numerics.Vector3.Zero

          startPos + v * 0.016f)
        (Mibo.Adaptive.CMap.value slopPositions)

    slopDerivedRotations <-
      Mibo.Adaptive.AMap.map
        (fun _id v ->
          if v <> System.Numerics.Vector3.Zero then
            float32(System.Math.Atan2(float v.X, float v.Z))
          else
            0.0f)
        (Mibo.Adaptive.CMap.value slopVelocities)

    fdaDerivedPositions <-
      fdaPositions
      |> AMap.mapA(fun id startPos -> adaptive {
        let! v = AMap.tryFind id fdaVelocities

        return
          startPos
          + (v |> Option.defaultValue System.Numerics.Vector3.Zero) * 0.016f
      })

    fdaDerivedRotations <-
      AMap.map
        (fun _id v ->
          if v <> System.Numerics.Vector3.Zero then
            float32(System.Math.Atan2(float v.X, float v.Z))
          else
            0.0f)
        fdaVelocities

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    let mutable acc = 0

    for _ in 1 .. this.Iterations do
      // Sim side: advance time and every entity position (journal appends).
      Mibo.Adaptive.CVal.set 0.016f slopTime

      for i in 0 .. this.EntityCount - 1 do
        let positions = Mibo.Adaptive.CMap.value slopPositions
        let velocities = Mibo.Adaptive.CMap.value slopVelocities
        let v = velocities.GetValue().[i]
        slopPositions.AddOrUpdate i (positions.GetValue().[i] + v * 0.016f)

      // Render side: force the maps and rebuild the movement snapshot.
      let time = Mibo.Adaptive.AVal.getValue slopTime
      let positions = Mibo.Adaptive.CMap.value slopPositions |> _.GetValue()

      let velocities = Mibo.Adaptive.CMap.value slopVelocities |> _.GetValue()

      let positionSeq = positions |> Seq.map(fun (KeyValue(k, v)) -> k, v)

      let modelConfigs =
        Mibo.Adaptive.AMap.force(Mibo.Adaptive.CMap.value slopModelConfig)

      let entityScenarios =
        Mibo.Adaptive.AMap.force(Mibo.Adaptive.CMap.value slopEntityScenario)

      let scenarios =
        Mibo.Adaptive.AMap.force(Mibo.Adaptive.CMap.value slopScenarios)

      let getVelocity id =
        match velocities.TryGetValue id with
        | true, v -> ValueSome v
        | _ -> ValueNone

      let getModelConfig id =
        match modelConfigs.TryGetValue id with
        | true, c -> ValueSome c
        | _ -> ValueNone

      let getScenario id =
        match entityScenarios.TryGetValue id with
        | true, s -> ValueSome s
        | _ -> ValueNone

      acc <-
        acc
        + this.BuildSnapshot
            time
            positionSeq
            getVelocity
            getModelConfig
            getScenario
        + scenarios.Count

    if acc = -1 then
      failwith "unreachable"

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    let mutable acc = 0

    for _ in 1 .. this.Iterations do
      // Sim side: advance time and every entity position in one transact.
      transact(fun () ->
        fdaTime.Value <- 0.016f

        for i in 0 .. this.EntityCount - 1 do
          let v = fdaVelocities.[i]
          fdaPositions.[i] <- fdaPositions.[i] + v * 0.016f)

      // Render side: force the maps and rebuild the movement snapshot.
      let time = AVal.force fdaTime
      let positions = AMap.force fdaPositions
      let velocities = AMap.force fdaVelocities
      let positionSeq = positions |> HashMap.toSeq
      let modelConfigs = AMap.force fdaModelConfig
      let entityScenarios = AMap.force fdaEntityScenario
      let scenarios = AMap.force fdaScenarios

      let getVelocity id = HashMap.tryFindV id velocities
      let getModelConfig id = HashMap.tryFindV id modelConfigs
      let getScenario id = HashMap.tryFindV id entityScenarios

      acc <-
        acc
        + this.BuildSnapshot
            time
            positionSeq
            getVelocity
            getModelConfig
            getScenario
        + scenarios.Count

    if acc = -1 then
      failwith "unreachable"

  /// The graph-as-cache variant: derived nodes hold the interpolated positions
  /// and rotations; the render reads transient views only (no force), and the
  /// spatial grid is the only per-frame user-code rebuild.
  [<Benchmark>]
  member this.AdaptiveSlop_GraphDirect() =
    let mutable acc = 0

    for _ in 1 .. this.Iterations do
      // Sim side: advance time and every entity position (journal appends).
      Mibo.Adaptive.CVal.set 0.016f slopTime

      for i in 0 .. this.EntityCount - 1 do
        let positions = Mibo.Adaptive.CMap.value slopPositions
        let velocities = Mibo.Adaptive.CMap.value slopVelocities
        let v = velocities.GetValue().[i]
        slopPositions.AddOrUpdate i (positions.GetValue().[i] + v * 0.016f)

      // Render side: read the graph directly. The derived positions drain
      // the pending deltas in place (0 alloc); every view is transient.
      let positionsView = Mibo.Adaptive.AMap.getValue slopDerivedPositions
      let rotationsView = Mibo.Adaptive.AMap.getValue slopDerivedRotations

      let velocitiesView =
        Mibo.Adaptive.CMap.value slopVelocities |> _.GetValue()

      let modelConfigsView =
        Mibo.Adaptive.CMap.value slopModelConfig |> _.GetValue()

      let entityScenariosView =
        Mibo.Adaptive.CMap.value slopEntityScenario |> _.GetValue()

      let scenariosView = Mibo.Adaptive.CMap.value slopScenarios |> _.GetValue()

      let positionSeq = positionsView |> Seq.map(fun (KeyValue(k, v)) -> k, v)

      let getVelocity id =
        match velocitiesView.TryGetValue id with
        | true, v -> ValueSome v
        | _ -> ValueNone

      let getModelConfig id =
        match modelConfigsView.TryGetValue id with
        | true, c -> ValueSome c
        | _ -> ValueNone

      let getScenario id =
        match entityScenariosView.TryGetValue id with
        | true, s -> ValueSome s
        | _ -> ValueNone

      let getScenario id =
        match entityScenariosView.TryGetValue id with
        | true, s -> ValueSome s
        | _ -> ValueNone

      acc <-
        acc
        + this.BuildSnapshot
            0.016f
            positionSeq
            getVelocity
            getModelConfig
            getScenario
        + rotationsView.Count
        + scenariosView.Count

    if acc = -1 then
      failwith "unreachable"

  /// The graph-as-cache variant for FDA: per-element adaptive blocks over the
  /// derived maps, forced per frame (their materialization idiom).
  [<Benchmark>]
  member this.FSharpDataAdaptive_GraphDirect() =
    let mutable acc = 0

    for _ in 1 .. this.Iterations do
      transact(fun () ->
        fdaTime.Value <- 0.016f

        for i in 0 .. this.EntityCount - 1 do
          fdaPositions.[i] <- fdaPositions.[i] + fdaVelocities.[i] * 0.016f)

      // Render side: force the derived maps (FDA's materialization idiom).
      let positions = AMap.force fdaDerivedPositions
      let rotations = AMap.force fdaDerivedRotations
      let velocities = AMap.force fdaVelocities
      let modelConfigs = AMap.force fdaModelConfig
      let entityScenarios = AMap.force fdaEntityScenario
      let scenarios = AMap.force fdaScenarios
      let positionSeq = positions |> HashMap.toSeq

      let getVelocity id = HashMap.tryFindV id velocities
      let getModelConfig id = HashMap.tryFindV id modelConfigs
      let getScenario id = HashMap.tryFindV id entityScenarios

      acc <-
        acc
        + this.BuildSnapshot
            0.016f
            positionSeq
            getVelocity
            getModelConfig
            getScenario
        + rotations.Count
        + scenarios.Count

    if acc = -1 then
      failwith "unreachable"

// =============================================================================
// Unbalanced Tree Benchmark (asymmetric structure)
// =============================================================================

[<MemoryDiagnoser>]
type UnbalancedTreeBenchmarks() =
  // Unbalanced: One deep branch + many shallow branches
  // Deep branch: input -> map -> map -> ... -> map (depth levels)
  // Shallow branches: input -> map (1 level each)
  // All combine at the end
  let mutable slopDeepInput: Mibo.Adaptive.ChangeableValue<int> =
    Unchecked.defaultof<_>

  let mutable slopShallowInputs: Mibo.Adaptive.ChangeableValue<int>[] = [||]

  let mutable slopResult: Mibo.Adaptive.IAdaptiveValue<int> =
    Unchecked.defaultof<_>

  let mutable fdaDeepInput: cval<int> = Unchecked.defaultof<_>
  let mutable fdaShallowInputs: cval<int>[] = [||]
  let mutable fdaResult: aval<int> = Unchecked.defaultof<_>

  [<Params(10, 50, 100)>]
  member val DeepBranchDepth = 0 with get, set

  [<Params(5, 20, 50)>]
  member val ShallowBranchCount = 0 with get, set

  [<Params(50)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member this.Setup() =
    // AdaptiveSlop unbalanced tree
    slopDeepInput <- Mibo.Adaptive.CVal.create 0

    let mutable deepChain: Mibo.Adaptive.IAdaptiveValue<int> =
      Mibo.Adaptive.CVal.value slopDeepInput

    for _ in 1 .. this.DeepBranchDepth do
      deepChain <- Mibo.Adaptive.AVal.map (fun v -> v + 1) deepChain

    slopShallowInputs <-
      Array.init this.ShallowBranchCount (fun i -> Mibo.Adaptive.CVal.create i)

    let shallowMapped =
      slopShallowInputs
      |> Array.map(fun cv ->
        Mibo.Adaptive.AVal.map (fun v -> v * 2) (Mibo.Adaptive.CVal.value cv))

    // Combine deep chain with all shallow branches
    let mutable combined = deepChain

    for shallow in shallowMapped do
      combined <- Mibo.Adaptive.AVal.map2 (+) combined shallow

    slopResult <- combined

    // FDA unbalanced tree
    fdaDeepInput <- cval 0
    let mutable fdaDeepChain: aval<int> = fdaDeepInput

    for _ in 1 .. this.DeepBranchDepth do
      fdaDeepChain <- AVal.map (fun v -> v + 1) fdaDeepChain

    fdaShallowInputs <- Array.init this.ShallowBranchCount (fun i -> cval i)

    let fdaShallowMapped =
      fdaShallowInputs |> Array.map(fun cv -> AVal.map (fun v -> v * 2) cv)

    let mutable fdaCombined = fdaDeepChain

    for shallow in fdaShallowMapped do
      fdaCombined <- AVal.map2 (+) fdaCombined shallow

    fdaResult <- fdaCombined

  [<Benchmark(Baseline = true, Description = "AdaptiveSlop_DeepChange")>]
  member this.AdaptiveSlop_ChangeDeep() =
    for i in 1 .. this.Iterations do
      slopDeepInput.Set(i)
      let _ = Mibo.Adaptive.AVal.getValue slopResult
      ()

  [<Benchmark(Description = "FDA_DeepChange")>]
  member this.FSharpDataAdaptive_ChangeDeep() =
    for i in 1 .. this.Iterations do
      transact(fun () -> fdaDeepInput.Value <- i)
      let _ = AVal.force fdaResult
      ()

  [<Benchmark(Description = "AdaptiveSlop_ShallowChange")>]
  member this.AdaptiveSlop_ChangeShallow() =
    for i in 1 .. this.Iterations do
      slopShallowInputs.[0].Set(i)
      let _ = Mibo.Adaptive.AVal.getValue slopResult
      ()

  [<Benchmark(Description = "FDA_ShallowChange")>]
  member this.FSharpDataAdaptive_ChangeShallow() =
    for i in 1 .. this.Iterations do
      transact(fun () -> fdaShallowInputs.[0].Value <- i)
      let _ = AVal.force fdaResult
      ()

// =============================================================================
// Incremental Delta Propagation Benchmark
// Tests mutations through a map→filter transform chain
// =============================================================================

[<MemoryDiagnoser>]
type IncrementalChainBenchmarks() =
  // AdaptiveSlop: source → *2 → filter even numbers
  let mutable slopSource: Mibo.Adaptive.ChangeableSet<int> =
    Unchecked.defaultof<_>

  let mutable slopChain: Mibo.Adaptive.IAdaptiveSet<int> =
    Unchecked.defaultof<_>
  // FDA: same chain
  let mutable fdaSource: cset<int> = Unchecked.defaultof<_>
  let mutable fdaChain: aset<int> = Unchecked.defaultof<_>

  [<Params(100, 1000, 10000)>]
  member val InitialSize = 0 with get, set

  [<Params(200)>]
  member val Mutations = 0 with get, set

  [<GlobalSetup>]
  member this.Setup() =
    // AdaptiveSlop
    slopSource <- Mibo.Adaptive.CSet.ofSeq(seq { 1 .. this.InitialSize })
    let mapped = Mibo.Adaptive.ASet.map (fun x -> x * 2) slopSource
    slopChain <- Mibo.Adaptive.ASet.filter (fun x -> x % 4 = 0) mapped

    // FDA
    fdaSource <- cset(seq { 1 .. this.InitialSize })
    let fdaMapped = ASet.map (fun x -> x * 2) fdaSource
    fdaChain <- ASet.filter (fun x -> x % 4 = 0) fdaMapped

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    let offset = this.InitialSize

    for i in 1 .. this.Mutations do
      slopSource.Add(offset + i)
      slopSource.Remove(offset + i - 1)
      let _ = Mibo.Adaptive.ASet.getValue slopChain
      ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    let offset = this.InitialSize

    for i in 1 .. this.Mutations do
      transact(fun () ->
        fdaSource.Add(offset + i) |> ignore
        fdaSource.Remove(offset + i - 1) |> ignore)

      let _ = ASet.force fdaChain
      ()

// =============================================================================
// Concurrent Post/Pump Benchmark
// =============================================================================
// AdaptiveSlop: foreign threads only Post; the owner thread pumps and reads.
// FDA: threads write and read concurrently (its locked model).
[<MemoryDiagnoser>]
type ConcurrentBenchmarks() =
  let mutable slopInput: Mibo.Adaptive.ChangeableValue<int> =
    Unchecked.defaultof<_>

  let mutable slopMapped: Mibo.Adaptive.IAdaptiveValue<int> =
    Unchecked.defaultof<_>

  let mutable fdaInput: cval<int> = Unchecked.defaultof<_>
  let mutable fdaMapped: aval<int> = Unchecked.defaultof<_>

  [<Params(4)>]
  member val ThreadCount = 0 with get, set

  [<Params(500)>]
  member val IterationsPerThread = 0 with get, set

  [<GlobalSetup>]
  member _.Setup() =
    slopInput <- Mibo.Adaptive.CVal.create 0

    slopMapped <-
      Mibo.Adaptive.AVal.map
        (fun v -> v + 1)
        (Mibo.Adaptive.CVal.value slopInput)

    fdaInput <- cval 0
    fdaMapped <- AVal.map (fun v -> v + 1) fdaInput

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlop() =
    let tasks =
      Array.init this.ThreadCount (fun threadId ->
        Task.Run(fun () ->
          for i in 1 .. this.IterationsPerThread do
            slopInput.Post(threadId * 10000 + i)))

    // Owner thread: pump and read while the producers run.
    while not(Task.WaitAll(tasks, 1)) do
      Mibo.Adaptive.Posting.pump()
      let _ = Mibo.Adaptive.AVal.getValue slopMapped
      ()

    Mibo.Adaptive.Posting.pump()
    let _ = Mibo.Adaptive.AVal.getValue slopMapped
    ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    let tasks =
      Array.init this.ThreadCount (fun threadId ->
        Task.Run(fun () ->
          for i in 1 .. this.IterationsPerThread do
            transact(fun () -> fdaInput.Value <- threadId * 10000 + i)

            let _ = AVal.force fdaMapped
            ()))

    Task.WaitAll(tasks)

// =============================================================================
// Per-element adaptive map benchmark (docs/2026-08-05-MAPA-DESIGN.md §13.6)
//
// ASet.mapA: one element-aval write per iteration, targeted delta. The naive
// composition (ASet.map + AVal.getValue) cannot react to an aval write at all
// (its mapping runs only on source deltas), so its workload is a full source
// replace: every element re-mapped per iteration — the brute-force baseline
// the mapA design avoids.
//
// FDA benchmark pattern (src/Test/.../Benchmarks/Map.fs): the measured
// method is ONE operation (one write + one read); the IterationSetup restores
// the pre-change state and settles the graph, so the Mean is directly the
// per-operation cost.
// =============================================================================

[<MemoryDiagnoser>]
type MapABenchmarks() =
  let mutable slopElements: Mibo.Adaptive.ChangeableValue<int>[] = [||]

  let mutable slopSet: Mibo.Adaptive.ChangeableSet<int> = Unchecked.defaultof<_>

  let mutable slopMapped: Mibo.Adaptive.aset<int> = Unchecked.defaultof<_>
  let mutable slopNaive: Mibo.Adaptive.aset<int> = Unchecked.defaultof<_>
  let mutable fdaElements: cval<int>[] = [||]
  let mutable fdaSet: cset<int> = Unchecked.defaultof<_>
  let mutable fdaMapped: aset<int> = Unchecked.defaultof<_>
  let mutable counter = 0

  [<Params(100, 1000)>]
  member val ElementCount = 0 with get, set

  [<GlobalSetup>]
  member this.Setup() =
    counter <- 0

    slopElements <-
      Array.init this.ElementCount (fun i -> Mibo.Adaptive.CVal.create(i * 10))

    slopSet <- Mibo.Adaptive.CSet.ofSeq [ 0 .. this.ElementCount - 1 ]

    slopMapped <-
      slopSet
      |> Mibo.Adaptive.ASet.mapA(fun v ->
        Mibo.Adaptive.CVal.value slopElements[v % this.ElementCount])

    slopNaive <-
      slopSet
      |> Mibo.Adaptive.ASet.map(fun v ->
        Mibo.Adaptive.AVal.getValue(
          Mibo.Adaptive.CVal.value slopElements[v % this.ElementCount]
        ))

    fdaElements <- Array.init this.ElementCount (fun i -> cval(i * 10))
    fdaSet <- cset [ 0 .. this.ElementCount - 1 ]

    fdaMapped <-
      fdaSet
      |> ASet.mapA(fun v -> fdaElements[v % this.ElementCount] :> aval<int>)
    // Settle: initialize the derived nodes outside the measurement.
    Mibo.Adaptive.ASet.getValue slopMapped |> ignore
    Mibo.Adaptive.ASet.getValue slopNaive |> ignore
    transact(fun () -> fdaElements[0].Value <- 0)
    ASet.force fdaMapped |> ignore

  [<IterationSetup>]
  member this.IterationSetup() =
    // Restore the pre-change state and settle the graph.
    slopElements[0].Set(0)
    slopSet.Set(seq { 0 .. this.ElementCount - 1 })
    Mibo.Adaptive.ASet.getValue slopMapped |> ignore
    Mibo.Adaptive.ASet.getValue slopNaive |> ignore
    transact(fun () -> fdaElements[0].Value <- 0)
    ASet.force fdaMapped |> ignore

  [<Benchmark(Baseline = true)>]
  member this.AdaptiveSlopMapA() =
    counter <- counter + 1
    slopElements[0].Set(counter)
    let _ = Mibo.Adaptive.ASet.getValue slopMapped
    ()

  [<Benchmark>]
  member this.NaiveMapForcesOnFullReplace() =
    counter <- counter + 1
    // Full replace with a disjoint range: the naive composition re-maps
    // every element (the delta is N removes + N adds).
    let start = 100000 + this.ElementCount + counter
    slopSet.Set(seq { start .. start + this.ElementCount - 1 })
    let _ = Mibo.Adaptive.ASet.getValue slopNaive
    ()

  [<Benchmark>]
  member this.FSharpDataAdaptive() =
    counter <- counter + 1
    transact(fun () -> fdaElements[0].Value <- counter)
    let _ = ASet.force fdaMapped
    ()

// =============================================================================
// Scalar Escape Benchmarks (per-key/per-position precise nodes)
//
// The branch is: source |> tryFind watched |> AVal.map x3. One write plus one
// root read per iteration. The unrelated-write benchmarks are the precision
// case: the per-key gate must keep them at write cost only (no branch
// recompute); the watched-write benchmarks pay the recompute by design.
// FDA is shown for context: its tryFind re-evaluates on every map change.
// =============================================================================

[<MemoryDiagnoser>]
type ScalarEscapeBenchmarks() =
  let mutable slopMap: Mibo.Adaptive.ChangeableMap<int, int> =
    Unchecked.defaultof<_>

  let mutable slopBranch: Mibo.Adaptive.IAdaptiveValue<int voption> =
    Unchecked.defaultof<_>

  let mutable slopCountBranch: Mibo.Adaptive.IAdaptiveValue<int> =
    Unchecked.defaultof<_>

  let mutable slopSet: Mibo.Adaptive.ChangeableSet<int> = Unchecked.defaultof<_>

  let mutable slopContainsBranch: Mibo.Adaptive.IAdaptiveValue<bool> =
    Unchecked.defaultof<_>

  let mutable fdaMap: cmap<int, int> = Unchecked.defaultof<_>
  let mutable fdaBranch: aval<int option> = Unchecked.defaultof<_>

  [<Params(1000)>]
  member val Iterations = 0 with get, set

  [<GlobalSetup>]
  member _.Setup() =
    slopMap <- Mibo.Adaptive.CMap.ofSeq(seq { for i in 1..100 -> i, i * 10 })

    slopBranch <-
      slopMap
      |> Mibo.Adaptive.CMap.value
      |> Mibo.Adaptive.AMap.tryFind 50
      |> Mibo.Adaptive.AVal.map(ValueOption.map((+) 1))
      |> Mibo.Adaptive.AVal.map(ValueOption.map((+) 1))
      |> Mibo.Adaptive.AVal.map(ValueOption.map((+) 1))

    slopCountBranch <-
      slopMap
      |> Mibo.Adaptive.CMap.value
      |> Mibo.Adaptive.AMap.count
      |> Mibo.Adaptive.AVal.map((+) 1)
      |> Mibo.Adaptive.AVal.map((+) 1)
      |> Mibo.Adaptive.AVal.map((+) 1)

    slopSet <- Mibo.Adaptive.CSet.ofSeq(seq { 1..100 })

    slopContainsBranch <-
      slopSet
      |> Mibo.Adaptive.CSet.value
      |> Mibo.Adaptive.ASet.contains 50
      |> Mibo.Adaptive.AVal.map not
      |> Mibo.Adaptive.AVal.map not
      |> Mibo.Adaptive.AVal.map not

    fdaMap <- cmap(seq { for i in 1..100 -> i, i * 10 })

    fdaBranch <-
      fdaMap
      |> AMap.tryFind 50
      |> AVal.map(Option.map((+) 1))
      |> AVal.map(Option.map((+) 1))
      |> AVal.map(Option.map((+) 1))

  [<Benchmark(Baseline = true)>]
  member this.SlopTryFindUnrelatedWrite() =
    for i in 1 .. this.Iterations do
      slopMap.AddOrUpdate 1 i
      let _ = Mibo.Adaptive.AVal.getValue slopBranch
      ()

  [<Benchmark>]
  member this.SlopTryFindWatchedWrite() =
    for i in 1 .. this.Iterations do
      slopMap.AddOrUpdate 50 i
      let _ = Mibo.Adaptive.AVal.getValue slopBranch
      ()

  [<Benchmark>]
  member this.FdaTryFindUnrelatedWrite() =
    for i in 1 .. this.Iterations do
      transact(fun () -> fdaMap.[1] <- i)
      let _ = AVal.force fdaBranch
      ()

  [<Benchmark>]
  member this.SlopCountUpdateWrite() =
    for i in 1 .. this.Iterations do
      slopMap.AddOrUpdate 2 i
      let _ = Mibo.Adaptive.AVal.getValue slopCountBranch
      ()

  [<Benchmark>]
  member this.SlopCountAddWrite() =
    // Add then remove the same fresh key: both are real writes (absent at
    // the add, present at the remove), the count never moves, and the map
    // stays bounded — the per-key gate must keep the branch at write cost.
    for i in 1 .. this.Iterations do
      slopMap.AddOrUpdate (1000 + i) i
      slopMap.Remove(1000 + i)
      let _ = Mibo.Adaptive.AVal.getValue slopCountBranch
      ()

  [<Benchmark>]
  member this.SlopContainsUnrelatedWrite() =
    for i in 1 .. this.Iterations do
      // Add and remove the same unrelated element: both are real writes
      // (the element is absent at the add, present at the remove), the
      // watched element never moves, and the set stays bounded.
      slopSet.Add(2000 + i) |> ignore
      slopSet.Remove(2000 + i) |> ignore
      let _ = Mibo.Adaptive.AVal.getValue slopContainsBranch
      ()

  [<Benchmark>]
  member this.SlopContainsWatchedWrite() =
    for i in 1 .. this.Iterations do
      if i % 2 = 0 then
        slopSet.Add 50 |> ignore
      else
        slopSet.Remove 50 |> ignore

      let _ = Mibo.Adaptive.AVal.getValue slopContainsBranch
      ()

  // ── Churn-shaped benchmarks (the game regime) ────────────────────────────
  // A FRESH lookup node per read — created, read once, never touched again
  // — while an unrelated key is written every iteration. The persistent
  // benchmarks above cannot see the regression: with write-time delivery,
  // every write dispatched to every node created since the last GC
  // (O(writes × churn × GC-interval)); with lazy re-sync the write is a
  // version bump and the fresh node's read is one O(1) re-sync.

  [<Benchmark>]
  member this.SlopTryFindChurnWrite() =
    for i in 1 .. this.Iterations do
      slopMap.AddOrUpdate 1 i

      let branch =
        slopMap |> Mibo.Adaptive.CMap.value |> Mibo.Adaptive.AMap.tryFind 50

      let _ = Mibo.Adaptive.AVal.getValue branch
      ()

  [<Benchmark>]
  member this.SlopCountChurnWrite() =
    for i in 1 .. this.Iterations do
      slopMap.AddOrUpdate 1 i

      let branch =
        slopMap |> Mibo.Adaptive.CMap.value |> Mibo.Adaptive.AMap.count

      let _ = Mibo.Adaptive.AVal.getValue branch
      ()

  [<Benchmark>]
  member this.SlopContainsChurnWrite() =
    for i in 1 .. this.Iterations do
      slopSet.Add(2000 + i) |> ignore
      slopSet.Remove(2000 + i) |> ignore

      let branch =
        slopSet |> Mibo.Adaptive.CSet.value |> Mibo.Adaptive.ASet.contains 50

      let _ = Mibo.Adaptive.AVal.getValue branch
      ()
// Entry Point
// =============================================================================

/// The join churn regime: the left map's entries are updated every iteration
/// and joined by a computed key to a right map. JoinOnUpdateAll is the
/// per-key swappable-input join (no subgraph rebuild on updates);
/// MapATryFindUpdateAll is the pre-joinOn idiom (mapA + tryFind) that
/// rebuilds every per-key subgraph on every update (the measured ~5% of
/// busy time as AdaptiveNode ZeroCreate in a profiled join projection).
[<MemoryDiagnoser>]
type JoinBenchmarks() =
  let mutable enemies = Unchecked.defaultof<Mibo.Adaptive.cmap<int, int>>

  let mutable projectiles = Unchecked.defaultof<Mibo.Adaptive.cmap<int, int>>

  let mutable joinDerived =
    Unchecked.defaultof<Mibo.Adaptive.amap<int, struct (int * int voption)>>

  let mutable mapADerived =
    Unchecked.defaultof<Mibo.Adaptive.amap<int, struct (int * int voption)>>

  [<Params(200)>]
  member val Enemies = 0 with get, set

  [<GlobalSetup>]
  member this.Setup() =
    enemies <- Mibo.Adaptive.CMap.empty<int, int>
    projectiles <- Mibo.Adaptive.CMap.empty<int, int>

    for i in 1 .. this.Enemies do
      Mibo.Adaptive.CMap.addOrUpdate i (i * 10) enemies

    for i in 1 .. (this.Enemies / 2) do
      Mibo.Adaptive.CMap.addOrUpdate i i projectiles

    joinDerived <-
      Mibo.Adaptive.AMap.joinOn
        (Mibo.Adaptive.CMap.value projectiles)
        (Mibo.Adaptive.CMap.value enemies)
        (fun k _ -> k % this.Enemies + 1) // stable join key from the key
        (fun _ pV tV ->
          Mibo.Adaptive.AVal.map2 (fun p t -> ValueSome(struct (p, t))) pV tV)

    mapADerived <-
      Mibo.Adaptive.CMap.value projectiles
      |> Mibo.Adaptive.AMap.mapA(fun k p ->
        Mibo.Adaptive.AMap.tryFind
          (k % this.Enemies + 1)
          (Mibo.Adaptive.CMap.value enemies)
        |> Mibo.Adaptive.AVal.map(fun t -> struct (p, t)))

    // Warm-up: subgraphs built, buffers grown, JIT settled.
    for i in 1 .. (this.Enemies / 2) do
      Mibo.Adaptive.CMap.addOrUpdate i (i + 1) projectiles

    Mibo.Adaptive.AMap.getValue joinDerived |> ignore
    Mibo.Adaptive.AMap.getValue mapADerived |> ignore

  [<Benchmark>]
  member this.JoinOnUpdateAll() =
    for i in 1..50 do
      for p in 1 .. (this.Enemies / 2) do
        Mibo.Adaptive.CMap.addOrUpdate p (p * 10 + i) projectiles

      Mibo.Adaptive.AMap.getValue joinDerived |> ignore

  [<Benchmark>]
  member this.MapATryFindUpdateAll() =
    for i in 1..50 do
      for p in 1 .. (this.Enemies / 2) do
        Mibo.Adaptive.CMap.addOrUpdate p (p * 10 + i) projectiles

      Mibo.Adaptive.AMap.getValue mapADerived |> ignore

[<EntryPoint>]
let main args =
  BenchmarkSwitcher.FromAssembly(typeof<ValueBenchmarks>.Assembly).Run(args)
  |> ignore

  0
