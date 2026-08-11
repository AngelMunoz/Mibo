// Shares a collection with the property tests (Properties.fs): the adaptive
// graph is confined to one owner thread, so xUnit must not run this module's
// tests in parallel with the FsCheck properties.
[<global.Xunit.Collection("Mibo.Adaptive")>]
module Mibo.Adaptive.Tests

#nowarn "893"

open System
open System.Collections.Generic
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open global.Xunit
open Mibo.Adaptive


type private DepTree =
  | Leaf of ChangeableValue<int>
  | Node of DepTree list

let rec private collectLeaves tree =
  match tree with
  | Leaf leaf -> [ leaf ]
  | Node children -> children |> List.collect collectLeaves

let rec private buildAdaptive tree =
  match tree with
  | Leaf leaf -> leaf :> IAdaptiveValue<int>
  | Node children ->
    match children with
    | [] -> AVal.constant 0
    | first :: rest ->
      let initial = buildAdaptive first

      rest
      |> List.fold
        (fun acc child ->
          let right = buildAdaptive child

          AVal.map2
            (fun leftValue rightValue -> leftValue + rightValue)
            acc
            right)
        initial

let private buildTree depth (values: int list) =
  let mutable index = 0

  let nextValue() =
    if values.IsEmpty then
      0
    else
      let value = values[index % values.Length]
      index <- index + 1
      value

  let rec build currentDepth =
    if currentDepth <= 0 then
      Leaf(CVal.create(nextValue()))
    else
      let left = build(currentDepth - 1)
      let right = build(currentDepth - 1)
      Node [ left; right ]

  build depth

[<Fact>]
let ``AVal map reflects changes``() =
  let input = CVal.create 5
  let mapped = AVal.map (fun v -> v + 1) (CVal.value input)

  Assert.Equal(6, AVal.getValue mapped)
  input.Set(10)
  Assert.Equal(11, AVal.getValue mapped)

[<Fact>]
let ``AVal map avoids recompute when unchanged``() =
  let input = CVal.create 3
  let mutable recomputeCount = 0

  let mapped =
    AVal.map
      (fun v ->
        recomputeCount <- recomputeCount + 1
        v * 2)
      (CVal.value input)

  Assert.Equal(6, AVal.getValue mapped)
  Assert.Equal(1, recomputeCount)
  Assert.Equal(6, AVal.getValue mapped)
  Assert.Equal(1, recomputeCount)

  input.Set(4)
  Assert.Equal(8, AVal.getValue mapped)
  Assert.Equal(2, recomputeCount)
  Assert.Equal(8, AVal.getValue mapped)
  Assert.Equal(2, recomputeCount)

[<Fact>]
let ``AVal mapTask produces expected results``() =
  let input = CVal.create 2
  let mapped = AVal.mapTask (fun v -> Task.FromResult(v * 3)) (CVal.value input)

  let initial = AVal.getValue mapped
  Assert.Equal(6, initial.Result)
  input.Set(4)
  let updated = AVal.getValue mapped
  Assert.Equal(12, updated.Result)

[<Fact>]
let ``AVal mapValueTask produces expected results``() =
  let input = CVal.create 3

  let mapped =
    AVal.mapValueTask (fun v -> ValueTask<int>(v + 5)) (CVal.value input)

  let initial = AVal.getValue mapped
  Assert.Equal(8, initial.Result)
  input.Set(7)
  let updated = AVal.getValue mapped
  Assert.Equal(12, updated.Result)

[<Fact>]
let ``AVal bind chooses latest adaptive value``() =
  let selector = CVal.create true
  let left = CVal.create 1
  let right = CVal.create 5

  let bound =
    AVal.bind
      (fun useLeft -> if useLeft then CVal.value left else CVal.value right)
      (CVal.value selector)

  Assert.Equal(1, AVal.getValue bound)
  selector.Set(false)
  Assert.Equal(5, AVal.getValue bound)
  right.Set(9)
  Assert.Equal(9, AVal.getValue bound)

[<Fact>]
let ``AVal mapTaskResult and bindTaskResult``() =
  let source = CVal.create 2

  let taskValue =
    AVal.mapTask (fun v -> Task.FromResult(v + 1)) (CVal.value source)

  let mapped = AVal.mapTaskResult (fun v -> v * 3) taskValue
  let bound = AVal.bindTaskResult (fun v -> Task.FromResult(v - 1)) mapped

  let initial = AVal.getValue bound
  Assert.Equal(8, initial.Result)
  source.Set(4)
  let updated = AVal.getValue bound
  Assert.Equal(14, updated.Result)

[<Fact>]
let ``AVal mapValueTaskResult and bindValueTaskResult``() =
  let source = CVal.create 1

  let taskValue =
    AVal.mapValueTask (fun v -> ValueTask<int>(v + 2)) (CVal.value source)

  let mapped = AVal.mapValueTaskResult (fun v -> v * 4) taskValue
  let bound = AVal.bindValueTaskResult (fun v -> ValueTask<int>(v + 1)) mapped

  let initial = AVal.getValue bound
  Assert.Equal(13, initial.Result)
  source.Set(3)
  let updated = AVal.getValue bound
  Assert.Equal(21, updated.Result)

[<Fact>]
let ``ASet union matches expected output``() =
  let left = CSet.ofSeq [ 1; 2; 3 ]
  let right = CSet.ofSeq [ 3; 4 ]

  let unioned = ASet.union (CSet.value left) (CSet.value right)
  let expectedInitial: Set<int> = Set.ofList [ 1; 2; 3; 4 ]
  Assert.Equal<Set<int>>(expectedInitial, ASet.toSet unioned)

  right.Add(5)
  let expectedAfterAdd: Set<int> = Set.ofList [ 1; 2; 3; 4; 5 ]
  Assert.Equal<Set<int>>(expectedAfterAdd, ASet.toSet unioned)

[<Fact>]
let ``ASet map and filter``() =
  let source = CSet.ofSeq [ 1; 2; 3; 4 ]
  let mapped = ASet.map (fun v -> v * 2) (CSet.value source)
  let filtered = ASet.filter (fun v -> v > 4) mapped

  let expectedInitial: Set<int> = Set.ofList [ 6; 8 ]
  Assert.Equal<Set<int>>(expectedInitial, ASet.toSet filtered)

  source.Add(5)
  let expectedAfterAdd: Set<int> = Set.ofList [ 6; 8; 10 ]
  Assert.Equal<Set<int>>(expectedAfterAdd, ASet.toSet filtered)

  source.Remove(4)
  let expectedAfterRemove: Set<int> = Set.ofList [ 6; 10 ]
  Assert.Equal<Set<int>>(expectedAfterRemove, ASet.toSet filtered)

[<Fact>]
let ``ASet mapA follows element avals and structural edits``() =
  let s = CSet.ofSeq [ 1; 2; 3 ]
  let even = CVal.create 1
  let odd = CVal.create 0

  let result =
    s
    |> ASet.mapA(fun v ->
      if v % 2 = 0 then
        AVal.map (fun e -> v * 10 + e) (even :> aval<int>)
      else
        AVal.map (fun e -> v * 10 + e) (odd :> aval<int>))

  // (1,10) (2,21) (3,30)
  Assert.Equal<Set<int>>(Set.ofList [ 10; 21; 30 ], ASet.toSet result)

  CVal.set 2 odd
  // (1,12) (3,32)
  Assert.Equal<Set<int>>(Set.ofList [ 12; 21; 32 ], ASet.toSet result)

  s.Add 4
  // (4,41)
  Assert.Equal<Set<int>>(Set.ofList [ 12; 21; 32; 41 ], ASet.toSet result)

  CVal.set 5 even
  // (2,25) (4,45)
  Assert.Equal<Set<int>>(Set.ofList [ 12; 25; 32; 45 ], ASet.toSet result)

  s.Remove 2
  Assert.Equal<Set<int>>(Set.ofList [ 12; 32; 45 ], ASet.toSet result)

  CVal.set 1 even
  CVal.set 0 odd
  // element 2 was removed earlier: no 21
  Assert.Equal<Set<int>>(Set.ofList [ 10; 30; 41 ], ASet.toSet result)

[<Fact>]
let ``ASet chooseA survival flips``() =
  let s = CSet.ofSeq [ 1; 2; 3 ]
  let even = CVal.create(Some 1)
  let odd = CVal.create(Some 0)

  let result =
    s
    |> ASet.chooseA(fun v ->
      if v % 2 = 0 then
        even :> aval<int option>
      else
        odd :> aval<int option>)

  // (1,0) (2,1) (3,0)
  Assert.Equal<Set<int>>(Set.ofList [ 0; 1 ], ASet.toSet result)

  CVal.set (Some 2) odd
  // (1,2) (2,1) (3,2)
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2 ], ASet.toSet result)

  CVal.set None even
  // (2,None) (4 absent)
  Assert.Equal<Set<int>>(Set.ofList [ 2 ], ASet.toSet result)

  s.Add 4
  // (4,None)
  Assert.Equal<Set<int>>(Set.ofList [ 2 ], ASet.toSet result)

  CVal.set (Some 5) even
  // (2,5) (4,5)
  Assert.Equal<Set<int>>(Set.ofList [ 2; 5 ], ASet.toSet result)

[<Fact>]
let ``ASet filterA flips with predicate avals``() =
  let takeEven = CVal.create true
  let takeOdd = CVal.create true
  let set = ASet.ofArray(Array.init 5 id)

  let filtered =
    set
    |> ASet.filterA(fun i ->
      if i % 2 = 0 then
        takeEven :> aval<bool>
      else
        takeOdd :> aval<bool>)

  Assert.Equal<Set<int>>(Set.ofList [ 0; 1; 2; 3; 4 ], ASet.toSet filtered)

  CVal.set false takeEven
  Assert.Equal<Set<int>>(Set.ofList [ 1; 3 ], ASet.toSet filtered)

  CVal.set false takeOdd
  Assert.Equal<Set<int>>(Set.empty, ASet.toSet filtered)

  CVal.set true takeOdd
  CVal.set true takeEven
  Assert.Equal<Set<int>>(Set.ofList [ 0; 1; 2; 3; 4 ], ASet.toSet filtered)

[<Fact>]
let ``ASet mapA counts duplicate mapped values``() =
  let s = CSet.ofSeq [ 1; 2; 3 ]
  let v = CVal.create 0

  let result = s |> ASet.mapA(fun _ -> v :> aval<int>)

  Assert.Equal<Set<int>>(Set.ofList [ 0 ], ASet.toSet result)

  CVal.set 1 v
  Assert.Equal<Set<int>>(Set.ofList [ 1 ], ASet.toSet result)

  s.Remove 1 // two occurrences of v remain
  Assert.Equal<Set<int>>(Set.ofList [ 1 ], ASet.toSet result)

  s.Remove 2
  Assert.Equal<Set<int>>(Set.ofList [ 1 ], ASet.toSet result)

  s.Remove 3 // last occurrence leaves: the output empties
  Assert.Equal<Set<int>>(Set.empty, ASet.toSet result)

[<Fact>]
let ``ASet mapA: a mapping that writes to the source during load is applied``
  ()
  =
  let s = CSet.ofSeq [ 1 ]
  let mutable reenter = true

  let result =
    s
    |> ASet.mapA(fun x ->
      if reenter && x = 1 then
        reenter <- false
        s.Add 2 |> ignore
        AVal.constant x
      else
        AVal.constant x)

  Assert.Equal<Set<int>>(Set.ofList [ 1; 2 ], ASet.toSet result)
  // The journal must be clean: the next write applies normally.
  s.Add 3
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 3 ], ASet.toSet result)

[<Fact>]
let ``ASet mapA steady-state element writes allocate zero bytes``() =
  let s = CSet.ofSeq(List.init 1000 id)
  let v = CVal.create 0
  let result = s |> ASet.mapA(fun _ -> v :> aval<int>)
  ASet.getValue result |> ignore
  // Warm up: the first reads grow the internal buffers.
  CVal.set 1 v
  ASet.getValue result |> ignore

  let before = GC.GetAllocatedBytesForCurrentThread()

  for i in 1..1000 do
    CVal.set (i % 2) v
    ASet.getValue result |> ignore

  let allocated = GC.GetAllocatedBytesForCurrentThread() - before
  Assert.Equal(0L, allocated)

[<Fact>]
let ``ASet countByA counts predicate-aval matches``() =
  let s = CSet.ofSeq [ 1; 2; 3; 4 ]
  let flag = CVal.create true

  let count =
    s |> ASet.countByA(fun v -> flag |> AVal.map(fun f -> f && v % 2 = 0))

  Assert.Equal(2, AVal.getValue count) // 2,4
  CVal.set false flag
  Assert.Equal(0, AVal.getValue count)
  s.Add 6
  Assert.Equal(0, AVal.getValue count)
  CVal.set true flag
  Assert.Equal(3, AVal.getValue count) // 2,4,6
  s.Remove 2
  Assert.Equal(2, AVal.getValue count)

[<Fact>]
let ``ASet existsA and forallA follow predicate avals``() =
  let s = CSet.ofSeq [ 1; 2; 3 ]
  let flag = CVal.create true

  let exists = s |> ASet.existsA(fun v -> flag |> AVal.map(fun f -> f && v > 2))

  let forall = s |> ASet.forallA(fun v -> flag |> AVal.map(fun f -> f && v > 2))

  Assert.True(AVal.getValue exists) // 3 qualifies
  Assert.False(AVal.getValue forall) // 1,2 do not
  CVal.set false flag
  Assert.False(AVal.getValue exists)
  Assert.False(AVal.getValue forall)
  CVal.set true flag
  Assert.True(AVal.getValue exists)
  Assert.False(AVal.getValue forall)

[<Fact>]
let ``ASet sumByA sums mapped avals``() =
  let s = CSet.ofSeq [ 1; 2; 3 ]
  let k = CVal.create 10

  let sum = s |> ASet.sumByA(fun v -> k |> AVal.map(fun k -> v * k))

  Assert.Equal(60, AVal.getValue sum)
  CVal.set 2 k
  Assert.Equal(12, AVal.getValue sum)
  s.Add 4
  Assert.Equal(20, AVal.getValue sum)
  s.Remove 3
  Assert.Equal(14, AVal.getValue sum)

[<Fact>]
let ``ASet averageByA averages mapped avals``() =
  let s = CSet.ofSeq [ 1.0; 2.0; 3.0 ]
  let k = CVal.create 2.0

  let avg = s |> ASet.averageByA(fun v -> k |> AVal.map(fun k -> v * k))

  Assert.Equal(4.0, AVal.getValue avg) // (2+4+6)/3
  CVal.set 3.0 k
  Assert.Equal(6.0, AVal.getValue avg) // (3+6+9)/3
  s.Add 5.0
  Assert.Equal(8.25, AVal.getValue avg) // (3+6+9+15)/4
  s.Remove 1.0
  Assert.Equal(10.0, AVal.getValue avg) // (6+9+15)/3

[<Fact>]
let ``ASet reduceByA folds with a custom reduction``() =
  let s = CSet.ofSeq [ 1; 3; 5 ]
  let k = CVal.create 10

  let min =
    s
    |> ASet.reduceByA (AdaptiveReduction.tryMin()) (fun v ->
      k |> AVal.map(fun k -> v + k))

  Assert.Equal(ValueSome 11, AVal.getValue min)
  CVal.set 100 k
  Assert.Equal(ValueSome 101, AVal.getValue min)
  s.Add 200
  Assert.Equal(ValueSome 101, AVal.getValue min)
  s.Remove 1
  Assert.Equal(ValueSome 103, AVal.getValue min)

[<Fact>]
let ``AMap map and filter``() =
  let source = CMap.ofSeq [ 1, 10; 2, 20; 3, 30 ]
  let mapped = AMap.map (fun _ v -> v + 1) (CMap.value source)
  let filtered = AMap.filter (fun _ v -> v > 15) mapped

  let expectedInitial: Map<int, int> = Map.ofList [ 2, 21; 3, 31 ]
  Assert.Equal<Map<int, int>>(expectedInitial, AMap.toMap filtered)

  source.AddOrUpdate 4 40
  let expectedAfterAdd: Map<int, int> = Map.ofList [ 2, 21; 3, 31; 4, 41 ]
  Assert.Equal<Map<int, int>>(expectedAfterAdd, AMap.toMap filtered)

  source.Remove(3)
  let expectedAfterRemove: Map<int, int> = Map.ofList [ 2, 21; 4, 41 ]
  Assert.Equal<Map<int, int>>(expectedAfterRemove, AMap.toMap filtered)

[<Fact>]
let ``AMap mapA follows entry avals and structural edits``() =
  let m = CMap.ofSeq [ "A", 1; "B", 2; "C", 3 ]
  let flag = CVal.create true

  let res =
    m
    |> AMap.mapA(fun _ v ->
      flag
      |> AVal.map (function
        | true -> v
        | false -> -1))

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "A", 1; "B", 2; "C", 3 ],
    AMap.toMap res
  )

  CVal.set false flag

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "A", -1; "B", -1; "C", -1 ],
    AMap.toMap res
  )

  CMap.set (Map.ofList [ "A", 2; "B", 4; "C", 6 ]) m

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "A", -1; "B", -1; "C", -1 ],
    AMap.toMap res
  )

  CVal.set true flag

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "A", 2; "B", 4; "C", 6 ],
    AMap.toMap res
  )

  CMap.remove "B" m
  Assert.Equal<Map<string, int>>(Map.ofList [ "A", 2; "C", 6 ], AMap.toMap res)

  CMap.addOrUpdate "D" 8 m

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "A", 2; "C", 6; "D", 8 ],
    AMap.toMap res
  )

[<Fact>]
let ``AMap chooseA survival flips``() =
  let m = CMap.ofSeq [ "A", 1; "B", 2; "C", 3 ]
  let keep = CVal.create(Some true)

  let res =
    m
    |> AMap.chooseA(fun _ v ->
      keep |> AVal.map(fun b -> if b = Some true then Some(v * 10) else None))

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "A", 10; "B", 20; "C", 30 ],
    AMap.toMap res
  )

  CVal.set (Some false) keep
  Assert.Equal<Map<string, int>>(Map.empty, AMap.toMap res)

  CVal.set (Some true) keep

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "A", 10; "B", 20; "C", 30 ],
    AMap.toMap res
  )

[<Fact>]
let ``AMap filterA flips with predicate avals``() =
  let m = CMap.ofSeq [ "A", 1; "B", 2; "C", 3; "D", 4 ]
  let takeEven = CVal.create true

  let filtered =
    m
    |> AMap.filterA(fun _ v ->
      if v % 2 = 0 then
        takeEven :> aval<bool>
      else
        AVal.constant true)

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "A", 1; "B", 2; "C", 3; "D", 4 ],
    AMap.toMap filtered
  )

  CVal.set false takeEven

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "A", 1; "C", 3 ],
    AMap.toMap filtered
  )

  CVal.set true takeEven

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "A", 1; "B", 2; "C", 3; "D", 4 ],
    AMap.toMap filtered
  )

[<Fact>]
let ``AMap mapA steady-state entry writes allocate zero bytes``() =
  let m = CMap.ofSeq(List.init 1000 (fun i -> i, i))
  let flag = CVal.create true

  let res =
    m
    |> AMap.mapA(fun _ v ->
      flag
      |> AVal.map (function
        | true -> v
        | false -> -1))

  AMap.getValue res |> ignore
  // Warm up: the first reads grow the internal buffers.
  CVal.set false flag
  AMap.getValue res |> ignore

  let before = GC.GetAllocatedBytesForCurrentThread()

  for i in 1..1000 do
    CVal.set (i % 2 = 0) flag
    AMap.getValue res |> ignore

  let allocated = GC.GetAllocatedBytesForCurrentThread() - before
  Assert.Equal(0L, allocated)

[<Fact>]
let ``Transaction defers ChangeableValue updates``() =
  let value = CVal.create 1

  Transaction.run(fun () ->
    value.Set(5)
    Assert.Equal(1, AVal.getValue(CVal.value value)))
  |> ignore

  Assert.Equal(5, AVal.getValue(CVal.value value))

[<Fact>]
let ``Transaction nesting defers updates until outer commit``() =
  let value = CVal.create 1

  Transaction.run(fun () ->
    value.Set(2)
    Transaction.run(fun () -> value.Set(3)) |> ignore
    Assert.Equal(1, AVal.getValue(CVal.value value)))
  |> ignore

  Assert.Equal(3, AVal.getValue(CVal.value value))

[<Fact>]
let ``Transaction rollback on exception``() =
  let value = CVal.create 1

  Assert.Throws<exn>(fun () ->
    Transaction.run(fun () ->
      value.Set(5)
      failwith "boom")
    |> ignore)
  |> ignore

  Assert.Equal(1, AVal.getValue(CVal.value value))

[<Fact>]
let ``Transaction batches set updates``() =
  let setValue = CSet.ofSeq [ 1; 2 ]

  Transaction.run(fun () ->
    setValue.Add(3)
    setValue.Remove(1)
    let expectedDuring: Set<int> = Set.ofList [ 1; 2 ]
    Assert.Equal<Set<int>>(expectedDuring, ASet.toSet(CSet.value setValue)))
  |> ignore

  let expectedAfter: Set<int> = Set.ofList [ 2; 3 ]
  Assert.Equal<Set<int>>(expectedAfter, ASet.toSet(CSet.value setValue))

[<Fact>]
let ``Transaction batches map updates``() =
  let mapValue = CMap.ofSeq [ 1, 10; 2, 20 ]

  Transaction.run(fun () ->
    mapValue.AddOrUpdate 3 30
    mapValue.Remove(1)
    let expectedDuring: Map<int, int> = Map.ofList [ 1, 10; 2, 20 ]

    Assert.Equal<Map<int, int>>(
      expectedDuring,
      AMap.toMap(CMap.value mapValue)
    ))
  |> ignore

  let expectedAfter: Map<int, int> = Map.ofList [ 2, 20; 3, 30 ]
  Assert.Equal<Map<int, int>>(expectedAfter, AMap.toMap(CMap.value mapValue))

[<Fact>]
let ``AVal mapTask tracks latest value``() =
  let input = CVal.create 1

  let mapped =
    AVal.mapTask (fun v -> Task.FromResult(v + 10)) (CVal.value input)

  let initial = AVal.getValue mapped
  Assert.Equal(11, initial.Result)

  input.Set(4)
  input.Set(7)
  let updated = AVal.getValue mapped
  Assert.Equal(17, updated.Result)

[<Fact>]
let ``AVal mapValueTask tracks latest value``() =
  let input = CVal.create 2

  let mapped =
    AVal.mapValueTask (fun (v: int) -> ValueTask<int>(v * v)) (CVal.value input)

  let initial = AVal.getValue mapped
  Assert.Equal(4, initial.Result)

  input.Set(3)
  input.Set(5)
  let updated = AVal.getValue mapped
  Assert.Equal(25, updated.Result)

[<Fact>]
let ``ASet union updates with add/remove``() =
  let left = CSet.ofSeq [ 1; 2 ]
  let right = CSet.ofSeq [ 2; 3 ]

  let unioned = ASet.union (CSet.value left) (CSet.value right)
  let expectedInitial: Set<int> = Set.ofList [ 1; 2; 3 ]
  Assert.Equal<Set<int>>(expectedInitial, ASet.toSet unioned)

  left.Remove(2)
  right.Add(4)
  let expectedAfterFirst: Set<int> = Set.ofList [ 1; 2; 3; 4 ]
  Assert.Equal<Set<int>>(expectedAfterFirst, ASet.toSet unioned)

  right.Remove(2)
  left.Add(5)
  let expectedAfterSecond: Set<int> = Set.ofList [ 1; 3; 4; 5 ]
  Assert.Equal<Set<int>>(expectedAfterSecond, ASet.toSet unioned)

[<Fact>]
let ``AMap map and filter respond to updates``() =
  let source = CMap.ofSeq [ 1, 10; 2, 20 ]
  let mapped = AMap.map (fun _ v -> v + 5) (CMap.value source)
  let filtered = AMap.filter (fun _ v -> v > 20) mapped

  let expectedInitial: Map<int, int> = Map.ofList [ 2, 25 ]
  Assert.Equal<Map<int, int>>(expectedInitial, AMap.toMap filtered)

  source.AddOrUpdate 2 12
  let expectedAfterUpdate: Map<int, int> = Map.empty
  Assert.Equal<Map<int, int>>(expectedAfterUpdate, AMap.toMap filtered)

  source.AddOrUpdate 1 30
  let expectedAfterSecond: Map<int, int> = Map.ofList [ 1, 35 ]
  Assert.Equal<Map<int, int>>(expectedAfterSecond, AMap.toMap filtered)

[<Fact>]
let ``Transaction applies last value update``() =
  let value = CVal.create 1

  Transaction.run(fun () ->
    value.Set(2)
    value.Set(5)
    Assert.Equal(1, AVal.getValue(CVal.value value)))
  |> ignore

  Assert.Equal(5, AVal.getValue(CVal.value value))

[<Fact>]
let ``AVal getValueTask reflects updates``() =
  let value = CVal.create 10

  let initial = AVal.getValueTask(CVal.value value)
  Assert.Equal(10, initial.Result)

  value.Set(14)
  let updated = AVal.getValueTask(CVal.value value)
  Assert.Equal(14, updated.Result)

[<Fact>]
let ``AVal getValueValueTask reflects updates``() =
  let value = CVal.create 4

  let initial = AVal.getValueValueTask(CVal.value value)
  Assert.Equal(4, initial.Result)

  value.Set(9)
  let updated = AVal.getValueValueTask(CVal.value value)
  Assert.Equal(9, updated.Result)

[<Fact>]
let ``ASet map responds to CSet.set``() =
  let source = CSet.ofSeq [ 1; 2 ]
  let mapped = ASet.map (fun v -> v + 1) (CSet.value source)

  let expectedInitial: Set<int> = Set.ofList [ 2; 3 ]
  Assert.Equal<Set<int>>(expectedInitial, ASet.toSet mapped)

  CSet.set (Set.ofList [ 3; 4 ]) source
  let expectedAfter: Set<int> = Set.ofList [ 4; 5 ]
  Assert.Equal<Set<int>>(expectedAfter, ASet.toSet mapped)

[<Fact>]
let ``AMap map responds to CMap.set``() =
  let source = CMap.ofSeq [ 1, 10; 2, 20 ]
  let mapped = AMap.map (fun key value -> value + key) (CMap.value source)

  let expectedInitial: Map<int, int> = Map.ofList [ 1, 11; 2, 22 ]
  Assert.Equal<Map<int, int>>(expectedInitial, AMap.toMap mapped)

  CMap.set (Map.ofList [ 2, 5; 3, 7 ]) source
  let expectedAfter: Map<int, int> = Map.ofList [ 2, 7; 3, 10 ]
  Assert.Equal<Map<int, int>>(expectedAfter, AMap.toMap mapped)

[<Fact>]
let ``Transaction defers CSet.set in unions``() =
  let left = CSet.ofSeq [ 1; 2 ]
  let right = CSet.ofSeq [ 2; 3 ]
  let unioned = ASet.union (CSet.value left) (CSet.value right)

  Transaction.run(fun () ->
    CSet.set (Set.ofList [ 5 ]) left
    CSet.set (Set.ofList [ 6 ]) right
    let expectedDuring: Set<int> = Set.ofList [ 1; 2; 3 ]
    Assert.Equal<Set<int>>(expectedDuring, ASet.toSet unioned))
  |> ignore

  let expectedAfter: Set<int> = Set.ofList [ 5; 6 ]
  Assert.Equal<Set<int>>(expectedAfter, ASet.toSet unioned)

[<Fact>]
let ``ASet union preserves duplicates until fully removed``() =
  let left = CSet.ofSeq [ 1; 2 ]
  let right = CSet.ofSeq [ 2; 3 ]
  let unioned = ASet.union (CSet.value left) (CSet.value right)

  let expectedInitial: Set<int> = Set.ofList [ 1; 2; 3 ]
  Assert.Equal<Set<int>>(expectedInitial, ASet.toSet unioned)

  left.Remove(2)
  let expectedAfterLeft: Set<int> = Set.ofList [ 1; 2; 3 ]
  Assert.Equal<Set<int>>(expectedAfterLeft, ASet.toSet unioned)

  right.Remove(2)
  let expectedAfterRight: Set<int> = Set.ofList [ 1; 3 ]
  Assert.Equal<Set<int>>(expectedAfterRight, ASet.toSet unioned)

[<Fact>]
let ``AMap filter ignores non-matching updates``() =
  let source = CMap.ofSeq [ 1, 5; 2, 20 ]
  let filtered = AMap.filter (fun _ value -> value > 10) (CMap.value source)

  let expectedInitial: Map<int, int> = Map.ofList [ 2, 20 ]
  Assert.Equal<Map<int, int>>(expectedInitial, AMap.toMap filtered)

  source.AddOrUpdate 1 8
  source.AddOrUpdate 3 9
  let expectedAfter: Map<int, int> = Map.ofList [ 2, 20 ]
  Assert.Equal<Map<int, int>>(expectedAfter, AMap.toMap filtered)

[<Fact>]
let ``Transaction defers updates across multiple values``() =
  let first = CVal.create 1
  let second = CVal.create 10

  Transaction.run(fun () ->
    first.Set(2)
    second.Set(20)
    Assert.Equal(1, AVal.getValue(CVal.value first))
    Assert.Equal(10, AVal.getValue(CVal.value second)))
  |> ignore

  Assert.Equal(2, AVal.getValue(CVal.value first))
  Assert.Equal(20, AVal.getValue(CVal.value second))

[<Fact>]
let ``AVal map stays stable on idempotent updates``() =
  let source = CVal.create 3
  let mapped = AVal.map (fun value -> value * 2) (CVal.value source)

  Assert.Equal(6, AVal.getValue mapped)
  source.Set(3)
  Assert.Equal(6, AVal.getValue mapped)

[<Fact>]
let ``AVal chained maps reflect updates``() =
  let source = CVal.create 2

  let mapped =
    source
    |> CVal.value
    |> AVal.map(fun value -> value + 1)
    |> AVal.map(fun value -> value * 2)

  Assert.Equal(6, AVal.getValue mapped)
  source.Set(4)
  Assert.Equal(10, AVal.getValue mapped)

[<Fact>]
let ``ASet union with empty set behaves``() =
  let left = CSet.ofSeq [ 1; 2 ]
  let right = CSet.ofSeq []
  let unioned = ASet.union (CSet.value left) (CSet.value right)

  let expectedInitial: Set<int> = Set.ofList [ 1; 2 ]
  Assert.Equal<Set<int>>(expectedInitial, ASet.toSet unioned)

  right.Add(3)
  let expectedAfterAdd: Set<int> = Set.ofList [ 1; 2; 3 ]
  Assert.Equal<Set<int>>(expectedAfterAdd, ASet.toSet unioned)

  left.Remove(1)
  let expectedAfterRemove: Set<int> = Set.ofList [ 2; 3 ]
  Assert.Equal<Set<int>>(expectedAfterRemove, ASet.toSet unioned)

[<Fact>]
let ``AMap filter updates on removals``() =
  let source = CMap.ofSeq [ 1, 10; 2, 20; 3, 30 ]
  let filtered = AMap.filter (fun _ value -> value >= 20) (CMap.value source)

  let expectedInitial: Map<int, int> = Map.ofList [ 2, 20; 3, 30 ]
  Assert.Equal<Map<int, int>>(expectedInitial, AMap.toMap filtered)

  source.Remove(3)
  let expectedAfterRemove: Map<int, int> = Map.ofList [ 2, 20 ]
  Assert.Equal<Map<int, int>>(expectedAfterRemove, AMap.toMap filtered)

  source.AddOrUpdate 1 25
  let expectedAfterUpdate: Map<int, int> = Map.ofList [ 1, 25; 2, 20 ]
  Assert.Equal<Map<int, int>>(expectedAfterUpdate, AMap.toMap filtered)

[<Fact>]
let ``Transaction defers set and map together``() =
  let setValue = CSet.ofSeq [ 1 ]
  let mapValue = CMap.ofSeq [ 1, 1 ]

  Transaction.run(fun () ->
    setValue.Add(2)
    mapValue.AddOrUpdate 2 2

    let expectedSet: Set<int> = Set.ofList [ 1 ]
    let expectedMap: Map<int, int> = Map.ofList [ 1, 1 ]
    Assert.Equal<Set<int>>(expectedSet, ASet.toSet(CSet.value setValue))
    Assert.Equal<Map<int, int>>(expectedMap, AMap.toMap(CMap.value mapValue)))
  |> ignore

  let expectedSetAfter: Set<int> = Set.ofList [ 1; 2 ]
  let expectedMapAfter: Map<int, int> = Map.ofList [ 1, 1; 2, 2 ]
  Assert.Equal<Map<int, int>>(expectedMapAfter, AMap.toMap(CMap.value mapValue))

[<Fact>]
let ``AVal constant returns stable value``() =
  let constant = AVal.constant 42
  Assert.Equal(42, AVal.getValue constant)
  Assert.Equal(42, AVal.getValue constant)

[<Fact>]
let ``AVal getValueTask and getValueValueTask match``() =
  let value = CVal.create 7
  let taskValue = AVal.getValueTask(CVal.value value)
  let valueTaskValue = AVal.getValueValueTask(CVal.value value)

  Assert.Equal(7, taskValue.Result)
  Assert.Equal(7, valueTaskValue.Result)

  value.Set(9)
  Assert.Equal(9, (AVal.getValueTask(CVal.value value)).Result)
  Assert.Equal(9, (AVal.getValueValueTask(CVal.value value)).Result)

[<Fact>]
let ``ASet map responds to multiple updates``() =
  let source = CSet.ofSeq [ 1; 3 ]
  let mapped = ASet.map (fun v -> v * 10) (CSet.value source)

  let expectedInitial: Set<int> = Set.ofList [ 10; 30 ]
  Assert.Equal<Set<int>>(expectedInitial, ASet.toSet mapped)

  source.Add(2)
  source.Remove(1)
  let expectedAfter: Set<int> = Set.ofList [ 20; 30 ]
  Assert.Equal<Set<int>>(expectedAfter, ASet.toSet mapped)

[<Fact>]
let ``AMap filter removes when threshold increases``() =
  let source = CMap.ofSeq [ 1, 5; 2, 15; 3, 25 ]
  let filtered = AMap.filter (fun _ value -> value >= 10) (CMap.value source)

  let expectedInitial: Map<int, int> = Map.ofList [ 2, 15; 3, 25 ]
  Assert.Equal<Map<int, int>>(expectedInitial, AMap.toMap filtered)

  source.AddOrUpdate 2 8
  let expectedAfter: Map<int, int> = Map.ofList [ 3, 25 ]
  Assert.Equal<Map<int, int>>(expectedAfter, AMap.toMap filtered)

[<Fact>]
let ``Transaction defers map set updates``() =
  let mapValue = CMap.ofSeq [ 1, 1; 2, 2 ]

  Transaction.run(fun () ->
    CMap.set (Map.ofList [ 3, 3 ]) mapValue
    let expectedDuring: Map<int, int> = Map.ofList [ 1, 1; 2, 2 ]

    Assert.Equal<Map<int, int>>(
      expectedDuring,
      AMap.toMap(CMap.value mapValue)
    ))
  |> ignore

  let expectedAfter: Map<int, int> = Map.ofList [ 3, 3 ]
  Assert.Equal<Map<int, int>>(expectedAfter, AMap.toMap(CMap.value mapValue))

[<FsCheck.Xunit.Property(MaxTest = 200)>]
let ``Deep dependency trees propagate updates``
  (depth: FsCheck.PositiveInt)
  (updates: int list)
  =
  let actualDepth = min 6 depth.Get
  let values = if updates.IsEmpty then [ 0 ] else updates
  let tree = buildTree actualDepth values
  let root = buildAdaptive tree
  let leaves = collectLeaves tree

  let sumLeaves() =
    leaves |> List.sumBy(fun leaf -> AVal.getValue(leaf :> IAdaptiveValue<int>))

  let mutable ok = AVal.getValue root = sumLeaves()

  if ok then
    let mutable idx = 0

    for value in values do
      let leaf = leaves[idx % leaves.Length]
      leaf.Set(value)
      idx <- idx + 1

      if AVal.getValue root <> sumLeaves() then
        ok <- false

  ok
// =============================================================================
// N-ary Node Tests (map3, map4, mapN, reduce, sum)
// =============================================================================

[<Fact>]
let ``AVal map3 combines three values correctly``() =
  let a = CVal.create 1
  let b = CVal.create 2
  let c = CVal.create 3

  let combined =
    AVal.map3
      (fun x y z -> x + y + z)
      (CVal.value a)
      (CVal.value b)
      (CVal.value c)

  Assert.Equal(6, AVal.getValue combined)

  a.Set(10)
  Assert.Equal(15, AVal.getValue combined)

  b.Set(20)
  Assert.Equal(33, AVal.getValue combined)

  c.Set(30)
  Assert.Equal(60, AVal.getValue combined)

[<Fact>]
let ``AVal map3 avoids recompute when unchanged``() =
  let a = CVal.create 1
  let b = CVal.create 2
  let c = CVal.create 3
  let mutable computeCount = 0

  let combined =
    AVal.map3
      (fun x y z ->
        computeCount <- computeCount + 1
        x * y * z)
      (CVal.value a)
      (CVal.value b)
      (CVal.value c)

  Assert.Equal(6, AVal.getValue combined)
  Assert.Equal(1, computeCount)

  // Reading again should not recompute
  Assert.Equal(6, AVal.getValue combined)
  Assert.Equal(1, computeCount)

  // Changing a value should recompute
  a.Set(2)
  Assert.Equal(12, AVal.getValue combined)
  Assert.Equal(2, computeCount)

[<Fact>]
let ``AVal map4 combines four values correctly``() =
  let a = CVal.create 1
  let b = CVal.create 2
  let c = CVal.create 3
  let d = CVal.create 4

  let combined =
    AVal.map4
      (fun w x y z -> w + x + y + z)
      (CVal.value a)
      (CVal.value b)
      (CVal.value c)
      (CVal.value d)

  Assert.Equal(10, AVal.getValue combined)

  a.Set(10)
  Assert.Equal(19, AVal.getValue combined)

  d.Set(40)
  Assert.Equal(55, AVal.getValue combined)

[<Fact>]
let ``AVal map4 avoids recompute when unchanged``() =
  let a = CVal.create 1
  let b = CVal.create 2
  let c = CVal.create 3
  let d = CVal.create 4
  let mutable computeCount = 0

  let combined =
    AVal.map4
      (fun w x y z ->
        computeCount <- computeCount + 1
        w * x * y * z)
      (CVal.value a)
      (CVal.value b)
      (CVal.value c)
      (CVal.value d)

  Assert.Equal(24, AVal.getValue combined)
  Assert.Equal(1, computeCount)

  // Reading again should not recompute
  Assert.Equal(24, AVal.getValue combined)
  Assert.Equal(1, computeCount)

  // Changing a value should recompute
  b.Set(3)
  Assert.Equal(36, AVal.getValue combined)
  Assert.Equal(2, computeCount)

[<Fact>]
let ``AVal mapN combines array of values correctly``() =
  let sources = [|
    CVal.create 1
    CVal.create 2
    CVal.create 3
    CVal.create 4
    CVal.create 5
  |]

  let deps = sources |> Array.map CVal.value
  let combined = AVal.mapN (fun arr -> arr |> Array.sum) deps

  Assert.Equal(15, AVal.getValue combined)

  sources.[0].Set(10)
  Assert.Equal(24, AVal.getValue combined)

  sources.[4].Set(50)
  Assert.Equal(69, AVal.getValue combined)

[<Fact>]
let ``AVal mapN avoids recompute when unchanged``() =
  let sources = [| CVal.create 1; CVal.create 2; CVal.create 3 |]
  let deps = sources |> Array.map CVal.value
  let mutable computeCount = 0

  let combined =
    AVal.mapN
      (fun arr ->
        computeCount <- computeCount + 1
        arr |> Array.fold (*) 1)
      deps

  Assert.Equal(6, AVal.getValue combined)
  Assert.Equal(1, computeCount)

  // Reading again should not recompute
  Assert.Equal(6, AVal.getValue combined)
  Assert.Equal(1, computeCount)

  // Changing a value should recompute
  sources.[1].Set(5)
  Assert.Equal(15, AVal.getValue combined)
  Assert.Equal(2, computeCount)

[<Fact>]
let ``AVal mapN handles empty array``() =
  let deps: IAdaptiveValue<int>[] = [||]
  let combined = AVal.mapN (fun arr -> arr.Length) deps

  Assert.Equal(0, AVal.getValue combined)

[<Fact>]
let ``AVal mapN handles single element``() =
  let source = CVal.create 42
  let deps = [| CVal.value source |]
  let combined = AVal.mapN (fun arr -> arr.[0] * 2) deps

  Assert.Equal(84, AVal.getValue combined)

  source.Set(10)
  Assert.Equal(20, AVal.getValue combined)

[<Fact>]
let ``AVal reduce combines values with binary operation``() =
  let sources = [| CVal.create 1; CVal.create 2; CVal.create 3; CVal.create 4 |]
  let deps = sources |> Array.map CVal.value
  let reduced = AVal.reduce 0 (+) deps

  Assert.Equal(10, AVal.getValue reduced)

  sources.[0].Set(10)
  Assert.Equal(19, AVal.getValue reduced)

  sources.[3].Set(40)
  Assert.Equal(55, AVal.getValue reduced)

[<Fact>]
let ``AVal reduce handles empty array with init value``() =
  let deps: IAdaptiveValue<int>[] = [||]
  let reduced = AVal.reduce 42 (+) deps

  Assert.Equal(42, AVal.getValue reduced)

[<Fact>]
let ``AVal reduce handles single element``() =
  let source = CVal.create 10
  let deps = [| CVal.value source |]
  let reduced = AVal.reduce 5 (+) deps

  Assert.Equal(15, AVal.getValue reduced)

  source.Set(20)
  Assert.Equal(25, AVal.getValue reduced)

[<Fact>]
let ``AVal reduce works with multiplication``() =
  let sources = [| CVal.create 2; CVal.create 3; CVal.create 4 |]
  let deps = sources |> Array.map CVal.value
  let reduced = AVal.reduce 1 (*) deps

  Assert.Equal(24, AVal.getValue reduced)

  sources.[1].Set(5)
  Assert.Equal(40, AVal.getValue reduced)

[<Fact>]
let ``AVal sum sums integer values``() =
  let sources = [| CVal.create 10; CVal.create 20; CVal.create 30 |]
  let deps = sources |> Array.map CVal.value
  let summed = AVal.sum deps

  Assert.Equal(60, AVal.getValue summed)

  sources.[0].Set(100)
  Assert.Equal(150, AVal.getValue summed)

  sources.[2].Set(300)
  Assert.Equal(420, AVal.getValue summed)

[<Fact>]
let ``AVal sum handles empty array``() =
  let deps: IAdaptiveValue<int>[] = [||]
  let summed = AVal.sum deps

  Assert.Equal(0, AVal.getValue summed)

[<Fact>]
let ``AVal sum handles single element``() =
  let source = CVal.create 42
  let deps = [| CVal.value source |]
  let summed = AVal.sum deps

  Assert.Equal(42, AVal.getValue summed)

  source.Set(100)
  Assert.Equal(100, AVal.getValue summed)

[<Fact>]
let ``AVal sum avoids recompute when unchanged``() =
  let sources = [| CVal.create 1; CVal.create 2; CVal.create 3 |]
  let deps = sources |> Array.map CVal.value

  // Note: We can't easily count recomputes here without exposing internals,
  // but we can verify version doesn't change on repeated reads
  let summed = AVal.sum deps
  let v1 = (summed :> IAdaptiveObject).Version
  let _ = AVal.getValue summed
  let v2 = (summed :> IAdaptiveObject).Version

  Assert.Equal(v1, v2)

  // After change, version should increase
  sources.[0].Set(10)
  let _ = AVal.getValue summed
  let v3 = (summed :> IAdaptiveObject).Version
  Assert.True(v3 > v2)

[<Fact>]
let ``N-ary nodes work in chains with other adaptive operations``() =
  let a = CVal.create 1
  let b = CVal.create 2
  let c = CVal.create 3

  // map3 -> map -> map2
  let sum3 =
    AVal.map3
      (fun x y z -> x + y + z)
      (CVal.value a)
      (CVal.value b)
      (CVal.value c)

  let doubled = AVal.map (fun x -> x * 2) sum3
  let final = AVal.map2 (fun x y -> x + y) doubled (CVal.value a)

  // (1+2+3)*2 + 1 = 13
  Assert.Equal(13, AVal.getValue final)

  a.Set(10)
  // (10+2+3)*2 + 10 = 40
  Assert.Equal(40, AVal.getValue final)

// =============================================================================
// Phase 0 — Characterization Tests
// =============================================================================

[<Fact>]
let ``Deep chain propagates one update end to end``() =
  let depth = 500
  let source = CVal.create 1
  let mutable recomputeCount = 0

  let root =
    (CVal.value source, [ 1..depth ])
    ||> List.fold(fun acc _ ->
      AVal.map
        (fun v ->
          recomputeCount <- recomputeCount + 1
          v + 1)
        acc)

  Assert.Equal(1 + depth, AVal.getValue root)
  Assert.Equal(depth, recomputeCount)

  // Repeated clean reads must not recompute.
  Assert.Equal(1 + depth, AVal.getValue root)
  Assert.Equal(depth, recomputeCount)

  // One write must recompute each node at most once.
  source.Set(2)
  Assert.Equal(2 + depth, AVal.getValue root)
  Assert.Equal(2 * depth, recomputeCount)

[<Fact>]
let ``Diamond recomputes the join node at most once per change``() =
  let source = CVal.create 1
  let left = AVal.map (fun v -> v + 1) (CVal.value source)
  let right = AVal.map (fun v -> v * 2) (CVal.value source)
  let mutable joinCount = 0

  let join =
    AVal.map2
      (fun l r ->
        joinCount <- joinCount + 1
        l + r)
      left
      right

  Assert.Equal(4, AVal.getValue join) // (1+1) + (1*2)
  Assert.Equal(1, joinCount)

  source.Set(2)
  Assert.Equal(7, AVal.getValue join) // (2+1) + (2*2)
  Assert.Equal(2, joinCount)

  source.Set(3)
  Assert.Equal(10, AVal.getValue join) // (3+1) + (3*2)
  Assert.Equal(3, joinCount)

[<Fact>]
let ``Bind stops tracking the dropped branch``() =
  let selector = CVal.create true
  let left = CVal.create 1
  let right = CVal.create 10
  let mutable recomputeCount = 0

  let bound =
    AVal.bind
      (fun useLeft ->
        recomputeCount <- recomputeCount + 1
        if useLeft then CVal.value left else CVal.value right)
      (CVal.value selector)

  Assert.Equal(1, AVal.getValue bound)
  Assert.Equal(1, recomputeCount)

  // Switch to the right branch.
  selector.Set(false)
  Assert.Equal(10, AVal.getValue bound)
  Assert.Equal(2, recomputeCount)

  // Writes to the dropped branch must not recompute the bound node.
  left.Set(99)
  Assert.Equal(10, AVal.getValue bound)
  Assert.Equal(2, recomputeCount)

  // Writes to the live branch must propagate.
  right.Set(20)
  Assert.Equal(20, AVal.getValue bound)
  Assert.Equal(3, recomputeCount)

  // Switching back re-establishes tracking of the left branch.
  selector.Set(true)
  Assert.Equal(99, AVal.getValue bound)
  Assert.Equal(4, recomputeCount)

  right.Set(30)
  Assert.Equal(99, AVal.getValue bound)
  Assert.Equal(4, recomputeCount)

[<Fact>]
let ``Bind nested in bind switches inner graphs``() =
  let outer = CVal.create true
  let inner = CVal.create true
  let a = CVal.create 1
  let b = CVal.create 2
  let c = CVal.create 3

  let innerBound =
    AVal.bind
      (fun pick -> if pick then CVal.value a else CVal.value b)
      (CVal.value inner)

  let outerBound =
    AVal.bind
      (fun pick -> if pick then innerBound else CVal.value c)
      (CVal.value outer)

  Assert.Equal(1, AVal.getValue outerBound)

  inner.Set(false)
  Assert.Equal(2, AVal.getValue outerBound)

  // outer switches away from innerBound; inner writes must not matter.
  outer.Set(false)
  Assert.Equal(3, AVal.getValue outerBound)
  inner.Set(true)
  a.Set(100)
  Assert.Equal(3, AVal.getValue outerBound)

  // Switching back picks up the current inner value.
  outer.Set(true)
  Assert.Equal(100, AVal.getValue outerBound)

[<Fact>]
let ``Computed reads inside a transaction see pre-transaction values``() =
  let source = CVal.create 1
  let mutable recomputeCount = 0

  let mapped =
    AVal.map
      (fun v ->
        recomputeCount <- recomputeCount + 1
        v * 10)
      (CVal.value source)

  Assert.Equal(10, AVal.getValue mapped)
  Assert.Equal(1, recomputeCount)

  Transaction.run(fun () ->
    source.Set(5)
    // The computed node must still see the pre-transaction value.
    Assert.Equal(10, AVal.getValue mapped))
  |> ignore

  // No recompute happened inside the transaction.
  Assert.Equal(1, recomputeCount)

  // After commit the next read recomputes exactly once.
  Assert.Equal(50, AVal.getValue mapped)
  Assert.Equal(2, recomputeCount)

[<Fact>]
let ``Reads inside a transaction see values committed before it``() =
  let source = CVal.create 1

  source.Set(7)

  Transaction.run(fun () -> Assert.Equal(7, AVal.getValue(CVal.value source)))
  |> ignore

[<Fact>]
let ``Several writes between reads yield the last value``() =
  let source = CVal.create 0
  let mutable recomputeCount = 0

  let mapped =
    AVal.map
      (fun v ->
        recomputeCount <- recomputeCount + 1
        v + 1)
      (CVal.value source)

  Assert.Equal(1, AVal.getValue mapped)

  source.Set(1)
  source.Set(2)
  source.Set(3)
  Assert.Equal(4, AVal.getValue mapped)
  Assert.Equal(2, recomputeCount)

[<Fact>]
let ``Arbitrary interleaving of writes and reads stays correct``() =
  let a = CVal.create 0
  let b = CVal.create 0
  let sum = AVal.map2 (+) (CVal.value a) (CVal.value b)
  let doubled = AVal.map (fun v -> v * 2) sum
  let rng = Random(42)

  for _ in 1..200 do
    if rng.NextDouble() < 0.5 then
      a.Set(rng.Next(100))
    else
      b.Set(rng.Next(100))

    let expected =
      2 * (AVal.getValue(CVal.value a) + AVal.getValue(CVal.value b))

    Assert.Equal(expected, AVal.getValue doubled)

[<Fact>]
let ``Collection delta sequences match a reference model``() =
  let source = CSet.empty<int>
  let mapped = ASet.map (fun v -> v * 3) (CSet.value source)
  let filtered = ASet.filter (fun v -> v % 2 = 0) mapped
  let mutable model = Set.empty<int>
  let rng = Random(7)

  for i in 1..300 do
    let v = rng.Next(50)

    if rng.NextDouble() < 0.6 then
      source.Add(v)
      model <- model.Add(v)
    else
      source.Remove(v)
      model <- model.Remove(v)

    let expected =
      model |> Set.map(fun v -> v * 3) |> Set.filter(fun v -> v % 2 = 0)

    Assert.Equal<Set<int>>(expected, ASet.toSet filtered)

  // Bulk replace must match as well.
  let replacement = Set.ofList [ 1; 2; 3; 60 ]
  CSet.set replacement source

  let expected =
    replacement |> Set.map(fun v -> v * 3) |> Set.filter(fun v -> v % 2 = 0)

  Assert.Equal<Set<int>>(expected, ASet.toSet filtered)

[<Fact>]
let ``Map delta sequences match a reference model``() =
  let source = CMap.empty<int, int>
  let mapped = AMap.map (fun k v -> v + k) (CMap.value source)
  let mutable model = Map.empty<int, int>
  let rng = Random(11)

  for _ in 1..300 do
    let k = rng.Next(30)

    if rng.NextDouble() < 0.6 then
      let v = rng.Next(100)
      source.AddOrUpdate k v
      model <- model.Add(k, v)
    else
      source.Remove(k)
      model <- model.Remove k

    let expected = model |> Map.map(fun k v -> v + k)
    Assert.Equal<Map<int, int>>(expected, AMap.toMap mapped)

[<Fact>]
let ``Union delta sequences preserve duplicate semantics``() =
  let left = CSet.empty<int>
  let right = CSet.empty<int>
  let unioned = ASet.union (CSet.value left) (CSet.value right)
  let rng = Random(13)

  for _ in 1..200 do
    let v = rng.Next(20)

    match rng.Next(4) with
    | 0 -> left.Add(v)
    | 1 -> right.Add(v)
    | 2 -> left.Remove(v)
    | _ -> right.Remove(v)

    let expected =
      Set.union (ASet.toSet(CSet.value left)) (ASet.toSet(CSet.value right))

    Assert.Equal<Set<int>>(expected, ASet.toSet unioned)


// =============================================================================
// Phase 3/4 — Dependency tracking and transactions
// =============================================================================

[<Fact>]
let ``Several writes between reads produce the last value``() =
  let a = CVal.create 0
  let m = AVal.map (fun v -> v + 1) (CVal.value a)

  a.Set(1)
  a.Set(2)
  a.Set(3)
  Assert.Equal(4, AVal.getValue m)

[<Fact>]
let ``Arbitrary interleaving stays correct``() =
  let a = CVal.create 0
  let b = CVal.create 0
  let sum = AVal.map2 (+) (CVal.value a) (CVal.value b)
  let doubled = AVal.map (fun v -> v * 2) sum
  let rng = Random(43)

  for _ in 1..200 do
    if rng.NextDouble() < 0.5 then
      a.Set(rng.Next 100)
    else
      b.Set(rng.Next 100)

    let expected =
      2 * (AVal.getValue(CVal.value a) + AVal.getValue(CVal.value b))

    Assert.Equal(expected, AVal.getValue doubled)

[<Fact>]
let ``Writing an equal value does not mark``() =
  let a = CVal.create 5
  let m = AVal.map (fun v -> v + 1) (CVal.value a)
  let _ = AVal.getValue m

  let versionBefore = (CVal.value a :> IAdaptiveObject).Version
  a.Set(5)

  Assert.Equal(versionBefore, (CVal.value a :> IAdaptiveObject).Version)

[<Fact>]
let ``Transaction rollback resets collection journals``() =
  let s = CSet.ofSeq [ 1 ]

  Assert.Throws<exn>(fun () ->
    Transaction.run(fun () ->
      s.Add(2)
      failwith "boom")
    |> ignore)
  |> ignore

  Assert.Equal<Set<int>>(Set.ofList [ 1 ], ASet.toSet(CSet.value s))
  // The journal must be clean: the next write applies normally.
  s.Add(3)
  Assert.Equal<Set<int>>(Set.ofList [ 1; 3 ], ASet.toSet(CSet.value s))

[<Fact>]
let ``Transaction run returns the function result``() =
  let a = CVal.create 1

  let result =
    Transaction.run(fun () ->
      a.Set(2)
      40 + 2)

  Assert.Equal(42, result)

[<Fact>]
let ``Writes during an evaluation are visible to re-reads in the same evaluation``
  ()
  =
  let a = CVal.create 1
  let m = AVal.map (fun v -> v + 1) (CVal.value a)

  let trigger =
    AVal.map
      (fun s ->
        if s > 100 then
          a.Set(0)

        // Re-read m after the write, inside the same evaluation.
        AVal.getValue m + s)
      m

  Assert.Equal(4, AVal.getValue trigger) // (1+1) + (1+1)

  a.Set(101)

  // m is 102 after the write; the re-read inside the same evaluation must see 1.
  Assert.Equal(103, AVal.getValue trigger) // 102 + 1, not 102 + 102

[<Fact>]
let ``mapN recompute allocates zero bytes``() =
  let deps = Array.init 10 (fun _ -> CVal.create 1)

  let node =
    AVal.mapN
      (fun values -> values |> Array.fold (+) 0)
      (deps |> Array.map CVal.value)

  deps[0].Set(2)
  AVal.getValue node |> ignore // warm up: first recompute

  let before = GC.GetAllocatedBytesForCurrentThread()

  for i in 1..1000 do
    deps[0].Set(i)
    AVal.getValue node |> ignore

  let allocated = GC.GetAllocatedBytesForCurrentThread() - before
  Assert.Equal(0L, allocated)

// =============================================================================
// Phase 5 — Cross-thread posting
// =============================================================================

[<Fact>]
let ``Posted values apply at the next graph operation without pump``() =
  let a = CVal.create 1
  let m = AVal.map (fun v -> v * 10) (CVal.value a)

  a.Post(5)
  // No pump needed: the next owner read drains and applies the post.
  Assert.Equal(50, AVal.getValue m)

  // Posting.pump remains an explicit batch point.
  a.Post(7)
  Posting.pump()
  Assert.Equal(70, AVal.getValue m)

[<Fact>]
let ``Posts from a foreign thread apply at the next graph operation``() =
  let a = CVal.create 0

  let producer =
    Task.Run(fun () ->
      for i in 1..100 do
        a.Post(i))

  producer.Wait()
  // No owner operation happened yet: posts are still pending, version untouched.
  Assert.Equal(0L, (CVal.value a :> IAdaptiveObject).Version)

  // The first owner read drains: one application of the last posted value.
  Assert.Equal(100, AVal.getValue(CVal.value a))
  Assert.Equal(1L, (CVal.value a :> IAdaptiveObject).Version)

[<Fact>]
let ``Posts from several threads collapse to one application per batch``() =
  let a = CVal.create 0
  let mutable recomputeCount = 0

  let m =
    AVal.map
      (fun v ->
        recomputeCount <- recomputeCount + 1
        v)
      (CVal.value a)

  Assert.Equal(0, AVal.getValue m) // initial: 1 recompute
  Assert.Equal(1, recomputeCount)

  let producers = [
    for _ in 1..4 ->
      Task.Run(fun () ->
        for i in 1..250 do
          a.Post(i))
  ]

  Task.WaitAll(Array.ofList producers)
  // The first read drains: all 1000 posts collapsed into one application.
  Assert.Equal(250, AVal.getValue(CVal.value a))
  Assert.Equal(250, AVal.getValue m) // lazy: this read triggers the recompute
  Assert.Equal(2, recomputeCount)
  // An explicit pump with nothing pending must not recompute anything.
  Posting.pump()
  Assert.Equal(2, recomputeCount)

[<Fact>]
let ``Posting an equal value does not mark``() =
  let a = CVal.create 5
  let versionBefore = (CVal.value a :> IAdaptiveObject).Version

  a.Post(5)
  Posting.pump()

  Assert.Equal(versionBefore, (CVal.value a :> IAdaptiveObject).Version)

[<Fact>]
let ``Post and pump allocate zero bytes``() =
  let a = CVal.create 0
  let m = AVal.map (fun v -> v + 1) (CVal.value a)
  AVal.getValue m |> ignore // warm up

  let before = GC.GetAllocatedBytesForCurrentThread()

  for i in 1..1000 do
    a.Post(i)
    Posting.pump()
    AVal.getValue m |> ignore

  let allocated = GC.GetAllocatedBytesForCurrentThread() - before
  Assert.Equal(0L, allocated)

[<Fact>]
let ``Stress: posts interleaved with pumps and reads stay consistent``() =
  let a = CVal.create 0
  let m = AVal.map (fun v -> v + 1) (CVal.value a)
  let rng = Random(1)

  let producer =
    Task.Run(fun () ->
      for _ in 1..5000 do
        a.Post(rng.Next(1000)))

  // Pump and read while the producer runs.
  while not producer.IsCompleted do
    Posting.pump()
    let v = AVal.getValue m

    if v < 1 || v > 1000 then
      failwithf "read an out-of-range value: %d" v

    Thread.Yield() |> ignore

  producer.Wait()
  Posting.pump()
  let final = AVal.getValue(CVal.value a)
  Assert.InRange(final, 0, 999)
  Assert.Equal(final + 1, AVal.getValue m)


// =============================================================================
// Phase 6 — Collections lifecycle
// =============================================================================

[<Fact>]
let ``AMap map on top of filter receives updates``() =
  // Regression: FilterMapNode used to register by concrete type dispatch,
  // so a map node on top of a filter node never received deltas.
  let source = CMap.ofSeq [ 1, 10; 2, 20 ]
  let filtered = AMap.filter (fun _ v -> v > 15) (CMap.value source)
  let mapped = AMap.map (fun _ v -> v + 1) filtered
  Assert.Equal<Map<int, int>>(Map.ofList [ 2, 21 ], AMap.toMap mapped)

  CMap.addOrUpdate 3 30 source
  CMap.addOrUpdate 1 11 source // still filtered out
  Assert.Equal<Map<int, int>>(Map.ofList [ 2, 21; 3, 31 ], AMap.toMap mapped)

[<Fact>]
let ``force materializes a checkpoint the library never touches again``() =
  let source = CSet.ofSeq [ 1; 2; 3 ]
  let mapped = ASet.map (fun v -> v * 2) (CSet.value source)
  let snapshot = ASet.force mapped
  Assert.Equal(3, snapshot.Count)
  Assert.True(snapshot.Contains 6)

  // The live chain keeps working; the checkpoint is decoupled.
  CSet.add 4 source
  Assert.Equal(4, (ASet.force mapped).Count)
  Assert.Equal(3, snapshot.Count)
  Assert.True(snapshot.Contains 6)

[<Fact>]
let ``force on a map materializes FrozenDictionary``() =
  let source = CMap.ofSeq [ 1, 10; 2, 20 ]
  let mapped = AMap.map (fun _ v -> v * 10) (CMap.value source)
  let snapshot = AMap.force mapped
  Assert.Equal(100, snapshot[1])
  Assert.Equal(200, snapshot[2])
  CMap.addOrUpdate 3 30 source
  Assert.Equal(3, (AMap.force mapped).Count)
  Assert.Equal(2, snapshot.Count)

[<Fact>]
let ``toSet and toMap materialize F# counterparts``() =
  let source = CSet.ofSeq [ 3; 1; 2 ]
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 3 ], ASet.toSet(CSet.value source))
  let m = CMap.ofSeq [ 2, 20; 1, 10 ]

  Assert.Equal<Map<int, int>>(
    Map.ofList [ 1, 10; 2, 20 ],
    AMap.toMap(CMap.value m)
  )

[<Fact>]
let ``collections accept element types without comparison``() =
  // Regression: the collection interfaces used to require ': comparison'.
  let s = ASet.ofSeq [ typeof<int>; typeof<string> ]
  Assert.Equal(2, (ASet.force s).Count)
  let m = AMap.ofSeq [ typeof<int>, 1 ]
  Assert.Equal(1, (AMap.force m).Count)

[<Fact>]
let ``derived collections register lazily and dispose cleanly``() =
  let source = CSet.ofSeq [ 1; 2 ]
  let mapped = ASet.map (fun v -> v * 2) (CSet.value source)
  let cs = source
  Assert.Equal(0, cs.SinkCount) // not read yet: no registration

  let _ = ASet.toSet mapped // first read: registers
  Assert.Equal(1, cs.SinkCount)

  (mapped :> IDisposable).Dispose()
  Assert.Equal(0, cs.SinkCount)

  // A disposed node throws on read and processes nothing.
  Assert.Throws<InvalidOperationException>(fun () ->
    ASet.toSet mapped |> ignore)
  |> ignore

  CSet.add 3 source
  Assert.Equal(0, cs.SinkCount)

[<Fact>]
let ``add and remove of one element in a transaction collapse to no delta``() =
  let source = CSet.ofSeq [ 1; 2 ]
  let mutable processed = 0

  let mapped =
    ASet.map
      (fun v ->
        processed <- processed + 1
        v * 2)
      (CSet.value source)

  let _ = ASet.toSet mapped // warm up
  let before = processed

  Transaction.run(fun () ->
    CSet.add 5 source
    CSet.remove 5 source)
  |> ignore

  Assert.Equal<Set<int>>(Set.ofList [ 1; 2 ], ASet.toSet(CSet.value source))
  Assert.Equal(before, processed) // no delta reached the derived node

[<Fact>]
let ``remove then add of an existing element in a transaction keeps it``() =
  let source = CSet.ofSeq [ 1; 2 ]

  Transaction.run(fun () ->
    CSet.remove 2 source
    CSet.add 2 source)
  |> ignore

  Assert.Equal<Set<int>>(Set.ofList [ 1; 2 ], ASet.toSet(CSet.value source))

[<Fact>]
let ``N-element delta delivery allocates zero bytes``() =
  let source = CSet.ofSeq [ 1..100 ]
  let mapped = ASet.map (fun v -> v * 2) (CSet.value source)
  let filtered = ASet.filter (fun v -> v % 4 = 0) mapped
  let _ = ASet.toSet filtered // warm up: initial load, buffers, JIT

  // Warm up the write path and grow every journal/out buffer past 16.
  for i in 101..300 do
    CSet.add i source

  let _ = ASet.toSet filtered

  for i in 101..300 do
    CSet.remove i source

  let _ = ASet.toSet filtered

  GC.Collect()
  GC.WaitForPendingFinalizers()
  GC.Collect()
  let before = GC.GetAllocatedBytesForCurrentThread()

  // Measured: writes (journal append) plus one drain read (transient view).
  // The remaining cost is a small constant per draining node (24 B, an F#
  // codegen artifact of byref struct passing); it does not scale with the
  // delta size. Materialization (force/toSet) is not part of the contract.
  for i in 301..310 do
    CSet.add i source

  ASet.getValue filtered |> ignore
  let after = GC.GetAllocatedBytesForCurrentThread()

  Assert.True(
    after - before < 128L,
    sprintf "drain allocated %d bytes" (after - before)
  )

  Assert.Equal(55, (ASet.toSet filtered).Count)

[<Fact>]
let ``joinOn swap path: updates and one drain read allocate nothing``() =
  let left = CMap.empty<int, int>
  let right = CMap.empty<int, int>

  for i in 0..99 do
    CMap.addOrUpdate i 100 right

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

  for i in 0..49 do
    CMap.addOrUpdate i i left

  // Extra warm cycles: tiered JIT promotion must settle BEFORE the window.
  for _ in 1..3 do
    for i in 0..49 do
      CMap.addOrUpdate i (i + 1000) left

    AMap.getValue joined |> ignore

  GC.Collect()
  GC.WaitForPendingFinalizers()
  GC.Collect()
  let before = GC.GetAllocatedBytesForCurrentThread()

  for i in 0..49 do
    CMap.addOrUpdate i (i + 1000) left

  AMap.getValue joined |> ignore
  let after = GC.GetAllocatedBytesForCurrentThread()

  Assert.True(
    after - before < 128L,
    sprintf "swap drain allocated %d bytes" (after - before)
  )

[<Fact>]
let ``derived chain processes nothing when never read``() =
  let source = CSet.ofSeq [ 1 ]
  let mutable processed = 0

  let mapped =
    ASet.map
      (fun v ->
        processed <- processed + 1
        v)
      (CSet.value source)

  for i in 2..100 do
    CSet.add i source

  // No read happened: the mapping never ran (write is journal-append only).
  Assert.Equal(0, processed)

  let _ = ASet.toSet mapped
  Assert.Equal(100, processed)

[<Fact>]
let ``sequential transactions each apply their collection writes``() =
  // Regression: CommitJournal never reset flushEnqueued, so the flush of a
  // second sequential transaction was never re-enqueued and its writes were
  // silently lost.
  let setSource = CSet.empty<int>
  Transaction.run(fun () -> CSet.add 1 setSource)
  Transaction.run(fun () -> CSet.add 2 setSource)
  Assert.Equal(2, (setSource |> CSet.value |> ASet.force).Count)

  let mapSource = CMap.empty<string, int>
  Transaction.run(fun () -> CMap.addOrUpdate "a" 1 mapSource)
  Transaction.run(fun () -> CMap.addOrUpdate "b" 2 mapSource)
  Assert.Equal(2, (mapSource |> CMap.value |> AMap.force).Count)

// =============================================================================
// Phase 7.2 — Incremental reductions and derived checks
// =============================================================================

[<Fact>]
let ``ASet count isEmpty contains track the state``() =
  let source = CSet.empty<int>
  let count = ASet.count(CSet.value source)
  let isEmpty = ASet.isEmpty(CSet.value source)
  let hasFive = ASet.contains 5 (CSet.value source)

  Assert.Equal(0, AVal.getValue count)
  Assert.True(AVal.getValue isEmpty)
  Assert.False(AVal.getValue hasFive)

  CSet.add 5 source
  Assert.Equal(1, AVal.getValue count)
  Assert.False(AVal.getValue isEmpty)
  Assert.True(AVal.getValue hasFive)

  CSet.add 7 source
  CSet.remove 5 source
  Assert.Equal(1, AVal.getValue count)
  Assert.False(AVal.getValue hasFive)
  Assert.True(source |> CSet.value |> ASet.contains 7 |> AVal.getValue)

[<Fact>]
let ``ASet exists forall countBy are delta-driven``() =
  let source = CSet.empty<int>
  let existsEven = ASet.exists (fun v -> v % 2 = 0) (CSet.value source)
  let forallEven = ASet.forall (fun v -> v % 2 = 0) (CSet.value source)
  let evenCount = ASet.countBy (fun v -> v % 2 = 0) (CSet.value source)

  Assert.False(AVal.getValue existsEven)
  Assert.True(AVal.getValue forallEven)
  Assert.Equal(0, AVal.getValue evenCount)

  CSet.add 2 source
  Assert.True(AVal.getValue existsEven)
  Assert.True(AVal.getValue forallEven)
  Assert.Equal(1, AVal.getValue evenCount)

  CSet.add 3 source
  Assert.True(AVal.getValue existsEven)
  Assert.False(AVal.getValue forallEven)
  Assert.Equal(1, AVal.getValue evenCount)

  CSet.add 4 source
  CSet.remove 2 source
  Assert.True(AVal.getValue existsEven)
  Assert.Equal(1, AVal.getValue evenCount)

  CSet.remove 4 source
  Assert.False(AVal.getValue existsEven)
  Assert.False(AVal.getValue forallEven)
  Assert.Equal(0, AVal.getValue evenCount)

[<Fact>]
let ``ASet fold recomputes on removals``() =
  let source = CSet.empty<int>
  let folded = ASet.fold (+) 0 (CSet.value source)

  CSet.add 10 source
  CSet.add 20 source
  Assert.Equal(30, AVal.getValue folded)

  // fold has no inverse: the removal triggers a full recompute.
  CSet.remove 10 source
  Assert.Equal(20, AVal.getValue folded)

[<Fact>]
let ``ASet foldGroup inverts removals``() =
  let source = CSet.empty<int>
  let folded = ASet.foldGroup (+) (-) 0 (CSet.value source)

  CSet.add 10 source
  CSet.add 20 source
  Assert.Equal(30, AVal.getValue folded)

  CSet.remove 10 source
  Assert.Equal(20, AVal.getValue folded)

  CSet.add 5 source
  CSet.add 15 source
  CSet.remove 20 source
  Assert.Equal(20, AVal.getValue folded)

[<Fact>]
let ``ASet sum sumBy tryMin tryMax``() =
  let source = CSet.empty<int>
  let total = ASet.sum(CSet.value source)
  let totalBy = ASet.sumBy (fun v -> v * 2) (CSet.value source)
  let min = ASet.tryMin(CSet.value source)
  let max = ASet.tryMax(CSet.value source)

  Assert.Equal(ValueNone, AVal.getValue min)
  CSet.add 3 source
  CSet.add 1 source
  CSet.add 5 source
  Assert.Equal(9, AVal.getValue total)
  Assert.Equal(18, AVal.getValue totalBy)
  Assert.Equal(ValueSome 1, AVal.getValue min)
  Assert.Equal(ValueSome 5, AVal.getValue max)
  CSet.remove 5 source
  Assert.Equal(4, AVal.getValue total)
  Assert.Equal(ValueSome 3, AVal.getValue max)

[<Fact>]
let ``ASet reduceBy with a mapping``() =
  let source = CSet.empty<string>
  // Count the strings longer than one character, via reduceBy.
  // The pipe resolves the element type before the lambda is checked (F#
  // checks InlineIfLambda arguments eagerly; the subject-last order makes
  // member access work).
  let longCount =
    CSet.value source
    |> ASet.reduceBy AdaptiveReduction.countPositive (fun s -> s.Length > 1)

  Assert.Equal(0, AVal.getValue longCount)
  CSet.add "a" source
  CSet.add "bb" source
  CSet.add "ccc" source
  Assert.Equal(2, AVal.getValue longCount)
  CSet.remove "bb" source
  Assert.Equal(1, AVal.getValue longCount)

[<Fact>]
let ``AMap tryFind find track entries``() =
  let source = CMap.empty<string, int>
  let lookup = AMap.tryFind "a" (CMap.value source)

  Assert.Equal(ValueNone, AVal.getValue lookup)

  CMap.addOrUpdate "a" 1 source
  Assert.Equal(ValueSome 1, AVal.getValue lookup)

  CMap.addOrUpdate "a" 2 source
  Assert.Equal(ValueSome 2, AVal.getValue lookup)

  CMap.remove "a" source
  Assert.Equal(ValueNone, AVal.getValue lookup)

  CMap.addOrUpdate "a" 9 source
  let findValue = AMap.find "a" (CMap.value source)
  Assert.Equal(9, AVal.getValue findValue)

  Assert.Throws<System.Collections.Generic.KeyNotFoundException>(fun () ->
    source |> CMap.value |> AMap.find "missing" |> AVal.getValue |> ignore)

[<Fact>]
let ``AMap tryFind gates downstream recomputes to the watched key``() =
  let source = CMap.ofSeq [ "a", 1; "b", 2 ]
  let mutable evals = 0

  let branch =
    source
    |> CMap.value
    |> AMap.tryFind "a"
    |> AVal.map(fun v ->
      evals <- evals + 1
      v)

  Assert.Equal(ValueSome 1, AVal.getValue branch)
  Assert.Equal(1, evals)

  // Lazy re-sync contract: every write makes the immediate consumer
  // re-read exactly once (the dirty indicator); the read-time gate then
  // stops propagation when the watched output did not move. Downstream
  // nodes see only real changes.
  CMap.addOrUpdate "b" 20 source
  Assert.Equal(ValueSome 1, AVal.getValue branch)

  CMap.addOrUpdate "c" 3 source
  Assert.Equal(ValueSome 1, AVal.getValue branch)

  CMap.remove "b" source
  Assert.Equal(ValueSome 1, AVal.getValue branch)
  Assert.Equal(4, evals)

  // The watched key.
  CMap.addOrUpdate "a" 10 source
  Assert.Equal(ValueSome 10, AVal.getValue branch)
  Assert.Equal(5, evals)

  CMap.remove "a" source
  Assert.Equal(ValueNone, AVal.getValue branch)
  Assert.Equal(6, evals)

[<Fact>]
let ``AMap tryFind on a derived map tracks the watched key``() =
  let source = CMap.ofSeq [ "a", 1; "b", 2 ]
  let mutable evals = 0

  let branch =
    source
    |> CMap.value
    |> AMap.map(fun _ v -> v * 2)
    |> AMap.tryFind "a"
    |> AVal.map(fun v ->
      evals <- evals + 1
      v)

  Assert.Equal(ValueSome 2, AVal.getValue branch)
  Assert.Equal(1, evals)

  // Conservative on derived sources: the output delta of AMap.map exists
  // only after its drain (pull-lazy), so the per-key gate runs at the next
  // read. An unrelated upstream change costs the branch one recompute; the
  // value stays correct.
  CMap.addOrUpdate "b" 20 source
  Assert.Equal(ValueSome 2, AVal.getValue branch)
  Assert.Equal(2, evals)

  CMap.addOrUpdate "a" 5 source
  Assert.Equal(ValueSome 10, AVal.getValue branch)
  Assert.Equal(3, evals)

  CMap.remove "a" source
  Assert.Equal(ValueNone, AVal.getValue branch)
  Assert.Equal(4, evals)

[<Fact>]
let ``AMap count gates downstream recomputes to count changes``() =
  let source = CMap.ofSeq [ "a", 1; "b", 2 ]
  let mutable evals = 0

  let branch =
    source
    |> CMap.value
    |> AMap.count
    |> AVal.map(fun v ->
      evals <- evals + 1
      v)

  Assert.Equal(2, AVal.getValue branch)
  Assert.Equal(1, evals)

  // Update of an existing key: the count does not move, so the re-read
  // (the lazy re-sync contract) does not propagate.
  CMap.addOrUpdate "a" 10 source
  Assert.Equal(2, AVal.getValue branch)
  Assert.Equal(2, evals)

  CMap.addOrUpdate "c" 3 source
  Assert.Equal(3, AVal.getValue branch)
  Assert.Equal(3, evals)

  CMap.remove "c" source
  Assert.Equal(2, AVal.getValue branch)
  Assert.Equal(4, evals)

[<Fact>]
let ``AMap isEmpty gates downstream recomputes to the empty boundary``() =
  let source = CMap.ofSeq [ "a", 1 ]
  let mutable evals = 0

  let branch =
    source
    |> CMap.value
    |> AMap.isEmpty
    |> AVal.map(fun v ->
      evals <- evals + 1
      v)

  Assert.Equal(false, AVal.getValue branch)
  Assert.Equal(1, evals)

  // Non-empty to non-empty: the flag does not move, so the re-read (the
  // lazy re-sync contract) does not propagate.
  CMap.addOrUpdate "b" 2 source
  Assert.Equal(false, AVal.getValue branch)
  Assert.Equal(2, evals)

  CMap.remove "a" source
  CMap.remove "b" source
  Assert.Equal(true, AVal.getValue branch)
  Assert.Equal(3, evals)

[<Fact>]
let ``ASet contains gates downstream recomputes to the watched element``() =
  let source = CSet.ofSeq [ "a"; "b" ]
  let mutable evals = 0

  let branch =
    source
    |> CSet.value
    |> ASet.contains "x"
    |> AVal.map(fun v ->
      evals <- evals + 1
      v)

  Assert.Equal(false, AVal.getValue branch)
  Assert.Equal(1, evals)

  // Unrelated writes: the immediate consumer re-reads once (lazy re-sync
  // contract), the gate stops propagation when membership did not move.
  CSet.add "c" source
  Assert.Equal(false, AVal.getValue branch)

  CSet.remove "a" source
  Assert.Equal(false, AVal.getValue branch)
  Assert.Equal(3, evals)

  CSet.add "x" source
  Assert.Equal(true, AVal.getValue branch)
  Assert.Equal(4, evals)

  CSet.remove "x" source
  Assert.Equal(false, AVal.getValue branch)
  Assert.Equal(5, evals)

[<Fact>]
let ``ASet count and isEmpty gate downstream recomputes to their output changes``
  ()
  =
  let source = CSet.ofSeq [ "a" ]
  let mutable countEvals = 0
  let mutable emptyEvals = 0

  let countBranch =
    source
    |> CSet.value
    |> ASet.count
    |> AVal.map(fun v ->
      countEvals <- countEvals + 1
      v)

  let emptyBranch =
    source
    |> CSet.value
    |> ASet.isEmpty
    |> AVal.map(fun v ->
      emptyEvals <- emptyEvals + 1
      v)

  Assert.Equal(1, AVal.getValue countBranch)
  Assert.Equal(false, AVal.getValue emptyBranch)
  Assert.Equal(1, countEvals)
  Assert.Equal(1, emptyEvals)

  // The empty projection collapses (1 -> 2 under isEmpty): the immediate
  // consumer re-reads once (lazy re-sync contract), the gate stops the
  // propagation when the output did not move.
  CSet.add "b" source
  Assert.Equal(2, AVal.getValue countBranch)
  Assert.Equal(false, AVal.getValue emptyBranch)
  Assert.Equal(2, countEvals)
  Assert.Equal(2, emptyEvals)

  CSet.remove "a" source
  CSet.remove "b" source
  Assert.Equal(0, AVal.getValue countBranch)
  Assert.Equal(true, AVal.getValue emptyBranch)
  Assert.Equal(3, countEvals)
  Assert.Equal(3, emptyEvals)

[<Fact>]
let ``AList tryAt gates downstream recomputes to the watched position``() =
  let source = CList.ofSeq [ 1; 2; 3 ]
  let mutable evals = 0

  let branch =
    source
    |> CList.value
    |> AList.tryAt 1
    |> AVal.map(fun v ->
      evals <- evals + 1
      v)

  Assert.Equal(ValueSome 2, AVal.getValue branch)
  Assert.Equal(1, evals)

  // Update and insert after the watched position: the element there does
  // not move, so the re-reads (lazy re-sync contract) do not propagate.
  CList.updateAt 2 30 source
  Assert.Equal(ValueSome 2, AVal.getValue branch)

  CList.insertAt 3 4 source
  Assert.Equal(ValueSome 2, AVal.getValue branch)
  Assert.Equal(3, evals)

  // Insert before the watched position: the position shifts.
  CList.insertAt 0 0 source
  Assert.Equal(ValueSome 1, AVal.getValue branch)
  Assert.Equal(4, evals)

  // Update at the watched position.
  CList.updateAt 1 99 source
  Assert.Equal(ValueSome 99, AVal.getValue branch)
  Assert.Equal(5, evals)

[<Fact>]
let ``AList tryLast gates downstream recomputes to the last element``() =
  let source = CList.ofSeq [ 1; 2; 3 ]
  let mutable evals = 0

  let branch =
    source
    |> CList.value
    |> AList.tryLast
    |> AVal.map(fun v ->
      evals <- evals + 1
      v)

  Assert.Equal(ValueSome 3, AVal.getValue branch)
  Assert.Equal(1, evals)

  // Insert and update away from the end: the last element is untouched,
  // so the re-reads (lazy re-sync contract) do not propagate.
  CList.insertAt 0 0 source
  Assert.Equal(ValueSome 3, AVal.getValue branch)

  CList.updateAt 0 99 source
  Assert.Equal(ValueSome 3, AVal.getValue branch)
  Assert.Equal(3, evals)

  CList.append 4 source
  Assert.Equal(ValueSome 4, AVal.getValue branch)
  Assert.Equal(4, evals)

  CList.removeAt 4 source
  Assert.Equal(ValueSome 3, AVal.getValue branch)
  Assert.Equal(5, evals)

[<Fact>]
let ``AList count and isEmpty gate downstream recomputes to their output changes``
  ()
  =
  let source = CList.ofSeq [ 1 ]
  let mutable countEvals = 0
  let mutable emptyEvals = 0

  let countBranch =
    source
    |> CList.value
    |> AList.count
    |> AVal.map(fun v ->
      countEvals <- countEvals + 1
      v)

  let emptyBranch =
    source
    |> CList.value
    |> AList.isEmpty
    |> AVal.map(fun v ->
      emptyEvals <- emptyEvals + 1
      v)

  Assert.Equal(1, AVal.getValue countBranch)
  Assert.Equal(false, AVal.getValue emptyBranch)
  Assert.Equal(1, countEvals)
  Assert.Equal(1, emptyEvals)

  // Update: the count does not move, so the re-reads (lazy re-sync
  // contract) do not propagate.
  CList.updateAt 0 10 source
  Assert.Equal(1, AVal.getValue countBranch)
  Assert.Equal(false, AVal.getValue emptyBranch)
  Assert.Equal(2, countEvals)
  Assert.Equal(2, emptyEvals)

  CList.append 2 source
  Assert.Equal(2, AVal.getValue countBranch)
  Assert.Equal(false, AVal.getValue emptyBranch)
  Assert.Equal(3, countEvals)
  Assert.Equal(3, emptyEvals)

  CList.removeAt 1 source
  CList.removeAt 0 source
  Assert.Equal(0, AVal.getValue countBranch)
  Assert.Equal(true, AVal.getValue emptyBranch)
  Assert.Equal(4, countEvals)
  Assert.Equal(4, emptyEvals)


[<Fact>]
let ``derived map stays consistent after set and remove between reads``() =
  // Regression (pre-existing journal-order divergence): set k then remove k
  // with no drain between used to leave k present in the derived map (the
  // journal replayed rems before sets regardless of arrival order). The
  // append-time coalescing keeps the journal net.
  let source = CMap.ofSeq [ "a", 1 ]
  let derived = source |> CMap.value |> AMap.map(fun _ v -> v)

  let lookup = derived |> AMap.tryFind "b" |> AVal.map id

  Assert.Equal(ValueNone, AVal.getValue lookup)
  CMap.addOrUpdate "b" 2 source
  CMap.remove "b" source
  Assert.False((AMap.force derived).ContainsKey "b")
  Assert.Equal(ValueNone, AVal.getValue lookup)
  // The reverse order (rem then set) keeps the set.
  CMap.addOrUpdate "c" 3 source
  CMap.remove "c" source
  CMap.addOrUpdate "c" 4 source
  Assert.True((AMap.force derived).ContainsKey "c")
  Assert.Equal(ValueSome 4, AVal.getValue(derived |> AMap.tryFind "c"))

[<Fact>]
let ``derived set stays consistent after add and remove between reads``() =
  // The set analogue: add x then remove x with no drain between. The
  // cancelled pair must not replay the add alone (the refcount would
  // ratchet and the mapped output would keep a ghost element).
  let source = CSet.ofSeq [ 1 ]
  let derived = source |> CSet.value |> ASet.map(fun x -> x + 1)

  let contains = derived |> ASet.contains 3 |> AVal.map id

  Assert.Equal(false, AVal.getValue contains)
  CSet.add 2 source |> ignore
  CSet.remove 2 source |> ignore
  Assert.False((ASet.force derived).Contains 3)
  Assert.Equal(false, AVal.getValue contains)
  // And the reverse order: rem then add cancels too (the element never
  // left the derived state, so the refcount must not grow).
  CSet.remove 1 source |> ignore
  CSet.add 1 source |> ignore
  Assert.True((ASet.force derived).Contains 2)

  Assert.Equal(
    1,
    ASet.force derived |> Seq.filter(fun x -> x = 2) |> Seq.length
  )

[<Fact>]
let ``AMap reduce updates subtract the old value``() =
  let source = CMap.empty<string, int>
  let total = AMap.reduce (AdaptiveReduction.sum()) (CMap.value source)

  CMap.addOrUpdate "a" 10 source
  CMap.addOrUpdate "b" 20 source
  Assert.Equal(30, AVal.getValue total)

  // Update: the old value must be subtracted before the new one is added.
  CMap.addOrUpdate "a" 15 source
  Assert.Equal(35, AVal.getValue total)

  CMap.remove "b" source
  Assert.Equal(15, AVal.getValue total)

[<Fact>]
let ``AMap fold recomputes on removals``() =
  let source = CMap.empty<int, int>
  let folded = AMap.fold (fun s k v -> s + k * v) 0 (CMap.value source)

  CMap.addOrUpdate 2 3 source
  CMap.addOrUpdate 4 5 source
  Assert.Equal(26, AVal.getValue folded)

  CMap.remove 2 source
  Assert.Equal(20, AVal.getValue folded)

[<Fact>]
let ``AMap foldGroup inverts removals``() =
  let source = CMap.empty<int, int>

  let folded =
    AMap.foldGroup
      (fun s k v -> s + k * v)
      (fun s k v -> s - k * v)
      0
      (CMap.value source)

  CMap.addOrUpdate 2 3 source
  CMap.addOrUpdate 4 5 source
  Assert.Equal(26, AVal.getValue folded)

  CMap.remove 2 source
  Assert.Equal(20, AVal.getValue folded)

  CMap.addOrUpdate 4 1 source // update: subtract old (20), add new (4)
  Assert.Equal(4, AVal.getValue folded)

[<Fact>]
let ``AMap exists forall countBy``() =
  let source = CMap.empty<int, int>
  let hasBig = AMap.exists (fun _ v -> v > 100) (CMap.value source)
  let allBig = AMap.forall (fun _ v -> v > 100) (CMap.value source)
  let bigCount = AMap.countBy (fun _ v -> v > 100) (CMap.value source)

  Assert.False(AVal.getValue hasBig)
  Assert.True(AVal.getValue allBig)
  Assert.Equal(0, AVal.getValue bigCount)

  CMap.addOrUpdate 1 50 source
  Assert.False(AVal.getValue hasBig)
  Assert.False(AVal.getValue allBig)
  Assert.Equal(0, AVal.getValue bigCount)

  CMap.addOrUpdate 2 200 source
  Assert.True(AVal.getValue hasBig)
  Assert.Equal(1, AVal.getValue bigCount)

  CMap.addOrUpdate 2 5 source // update flips the group
  Assert.False(AVal.getValue hasBig)
  Assert.Equal(0, AVal.getValue bigCount)

[<Fact>]
let ``toAVal materializes stable snapshots``() =
  let source = CSet.empty<int>
  let snap = ASet.toAVal(CSet.value source)
  CSet.add 1 source
  CSet.add 2 source

  let first = AVal.getValue snap
  Assert.Equal(2, first.Count)
  Assert.True(first.Contains 1)

  CSet.add 3 source
  Assert.Equal(3, (AVal.getValue snap).Count)
  // The earlier snapshot is untouched by later writes.
  Assert.Equal(2, first.Count)

  let mapSource = CMap.empty<string, int>
  let mapSnap = AMap.toAVal(CMap.value mapSource)
  CMap.addOrUpdate "x" 1 mapSource
  Assert.Equal(1, (AVal.getValue mapSnap).Count)

[<Fact>]
let ``single builds constant singletons``() =
  let set = ASet.single 42
  Assert.Equal(1, (ASet.force set).Count)
  Assert.True((ASet.force set).Contains 42)

  let mapValue = AMap.single "k" 7
  Assert.Equal(7, (AMap.force mapValue)["k"])

[<Fact>]
let ``reduction over a derived set receives deltas``() =
  let source = CSet.empty<int>
  let doubled = ASet.map (fun v -> v * 2) (CSet.value source)
  let total = ASet.sum doubled

  CSet.add 1 source
  CSet.add 3 source
  Assert.Equal(8, AVal.getValue total)

  CSet.remove 1 source
  Assert.Equal(6, AVal.getValue total)

[<Fact>]
let ``reduction matches a reference model under random churn``() =
  let source = CSet.empty<int>
  let folded = ASet.foldGroup (+) (-) 0 (CSet.value source)
  let evenCount = ASet.countBy (fun v -> v % 2 = 0) (CSet.value source)
  let mutable model = Set.empty<int>
  let rng = Random(11)

  for _ in 1..500 do
    let v = rng.Next(100)

    if rng.NextDouble() < 0.6 then
      CSet.add v source
      model <- model.Add v
    else
      CSet.remove v source
      model <- model.Remove v

    Assert.Equal(Set.fold (+) 0 model, AVal.getValue folded)

    Assert.Equal(
      Set.count(Set.filter (fun x -> x % 2 = 0) model),
      AVal.getValue evenCount
    )

[<Fact>]
let ``reduce node steady-state drain allocates zero``() =
  let source = CSet.empty<int>
  let total = ASet.foldGroup (+) (-) 0 (CSet.value source)

  for i in 1..50 do
    CSet.add i source

  for i in 1..25 do
    CSet.remove i source

  // Warm up the first read: the initial load enumerates the view once
  // (one-time cost, like every node's initial load).
  AVal.getValue total |> ignore

  let before = GC.GetAllocatedBytesForCurrentThread()

  for i in 1..100 do
    CSet.remove ((i % 25) + 26) source
    CSet.add ((i % 25) + 26) source

    if AVal.getValue total <> 950 then
      failwith "sum drifted"

  let allocated = GC.GetAllocatedBytesForCurrentThread() - before
  Assert.Equal(950, AVal.getValue total)
  Assert.Equal(0L, allocated)

// =============================================================================
// Phase 7.3 — collection algebra
// =============================================================================

[<Fact>]
let ``ASet difference/intersect/xor match a reference model under random churn``
  ()
  =
  let left = CSet.empty<int>
  let right = CSet.empty<int>
  let diff = ASet.difference (CSet.value left) (CSet.value right)
  let inter = ASet.intersect (CSet.value left) (CSet.value right)
  let xored = ASet.xor (CSet.value left) (CSet.value right)

  for i in 1..30 do
    CSet.add i left
    CSet.add (i + 20) right

  let rng = Random 1234

  for _ in 1..500 do
    let target = if rng.NextDouble() < 0.5 then left else right
    let v = rng.Next(1, 60)

    if rng.NextDouble() < 0.5 then
      CSet.add v target
    else
      CSet.remove v target

    let l = Set.ofSeq(ASet.force(CSet.value left))
    let r = Set.ofSeq(ASet.force(CSet.value right))
    Assert.Equal<Set<int>>(Set.difference l r, Set.ofSeq(ASet.force diff))
    Assert.Equal<Set<int>>(Set.intersect l r, Set.ofSeq(ASet.force inter))

    Assert.Equal<Set<int>>(
      Set.union (Set.difference l r) (Set.difference r l),
      Set.ofSeq(ASet.force xored)
    )

  // A write that changes nothing (removing an absent element) must not mark.
  // The nodes' journals stay empty; the versions do not advance.
  let v0 = diff.Version
  CSet.remove 1000 left
  Assert.Equal(v0, diff.Version)

[<Fact>]
let ``ASet unionMany folds a static sequence of sets``() =
  let a = CSet.ofSeq [ 1; 2 ]
  let b = CSet.ofSeq [ 2; 3 ]
  let c = CSet.ofSeq [ 4 ]
  let all = ASet.unionMany [ CSet.value a; CSet.value b; CSet.value c ]
  Assert.Equal<Set<_>>(Set.ofList [ 1; 2; 3; 4 ], Set.ofSeq(ASet.force all))

  CSet.add 5 a
  Assert.Equal<Set<_>>(Set.ofList [ 1; 2; 3; 4; 5 ], Set.ofSeq(ASet.force all))
  CSet.remove 2 b // 2 is still contributed by a
  Assert.Equal<Set<_>>(Set.ofList [ 1; 2; 3; 4; 5 ], Set.ofSeq(ASet.force all))
  CSet.remove 2 a
  Assert.Equal<Set<_>>(Set.ofList [ 1; 3; 4; 5 ], Set.ofSeq(ASet.force all))

  let empty = ASet.unionMany []
  Assert.True(Set.empty = Set.ofSeq(ASet.force empty))

[<Fact>]
let ``ASet choose keeps only mapped values``() =
  let source = CSet.ofSeq [ 1; 2; 3; 4 ]

  let chosen =
    ASet.chooseV
      (fun x -> if x % 2 = 0 then ValueSome(x * 10) else ValueNone)
      (CSet.value source)

  Assert.Equal<Set<_>>(Set.ofList [ 20; 40 ], Set.ofSeq(ASet.force chosen))

  CSet.add 6 source
  CSet.add 7 source
  Assert.Equal<Set<_>>(Set.ofList [ 20; 40; 60 ], Set.ofSeq(ASet.force chosen))
  CSet.remove 2 source
  Assert.Equal<Set<_>>(Set.ofList [ 40; 60 ], Set.ofSeq(ASet.force chosen))

[<Fact>]
let ``ASet ofAVal rebuilds on every value change``() =
  let v = CVal.create [| 1; 2; 3 |]
  let set = ASet.ofAVal(CVal.value v)
  Assert.Equal<Set<_>>(Set.ofList [ 1; 2; 3 ], Set.ofSeq(ASet.force set))

  CVal.set [| 3; 4 |] v
  Assert.Equal<Set<_>>(Set.ofList [ 3; 4 ], Set.ofSeq(ASet.force set))

  CVal.set [| 3; 4 |] v // same content: elided, no mark
  let v0 = set.Version
  CVal.set [| 3; 4 |] v
  Assert.Equal(v0, set.Version)

  CVal.set [||] v
  Assert.Equal<Set<int>>(Set.empty, Set.ofSeq(ASet.force set))

[<Fact>]
let ``ASet ofReader and custom are poll-driven``() =
  let mutable state = HashSet([ 1; 2 ])
  let read = ASet.ofReader(fun () -> HashSet(state))
  Assert.Equal<Set<_>>(Set.ofList [ 1; 2 ], Set.ofSeq(ASet.force read))

  state.Add 3 |> ignore
  state.Remove 1 |> ignore
  Assert.Equal<Set<_>>(Set.ofList [ 2; 3 ], Set.ofSeq(ASet.force read))

  let mutable customState = HashSet([ 10 ])
  let mutable pending = ResizeArray<SetDelta<int>>()

  let custom =
    ASet.custom(fun view delta ->
      if pending.Count > 0 then
        let d = pending[0]
        pending.RemoveAt 0

        for a in d.Added.ToArray() do
          delta.Add a

        for r in d.Removed.ToArray() do
          delta.Remove r

      ())

  Assert.Equal<Set<_>>(Set.empty, Set.ofSeq(ASet.force custom))
  let mutable d = SetDelta<int>()
  d.Add 42
  pending.Add d
  Assert.Equal<Set<_>>(Set.ofList [ 42 ], Set.ofSeq(ASet.force custom))

[<Fact>]
let ``ASet constant and delay evaluate once, lazily``() =
  let mutable calls = 0

  let c =
    ASet.constant(fun () ->
      calls <- calls + 1
      HashSet([ 1; 2 ]))

  Assert.Equal(0, calls)
  Assert.Equal<Set<_>>(Set.ofList [ 1; 2 ], Set.ofSeq(ASet.force c))
  Assert.Equal<Set<_>>(Set.ofList [ 1; 2 ], Set.ofSeq(ASet.force c))
  Assert.Equal(1, calls)

  let d = ASet.delay(fun () -> HashSet([ 7 ]))
  Assert.Equal<Set<_>>(Set.ofList [ 7 ], Set.ofSeq(ASet.force d))

[<Fact>]
let ``AMap union is right-biased and unionWith resolves``() =
  let a = CMap.empty<string, int>
  let b = CMap.empty<string, int>
  let u = AMap.union (CMap.value a) (CMap.value b)
  let uw = AMap.unionWith (fun _ l r -> l + r) (CMap.value a) (CMap.value b)

  CMap.addOrUpdate "x" 1 a
  CMap.addOrUpdate "y" 2 a
  CMap.addOrUpdate "x" 10 b
  CMap.addOrUpdate "z" 3 b

  // FDA parity: union prefers the RIGHT value on collisions.
  Assert.Equal<Map<string, int>>(
    Map.ofList [ "x", 10; "y", 2; "z", 3 ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force u -> k, v })
  )

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "x", 11; "y", 2; "z", 3 ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force uw -> k, v })
  )

  // Updating the non-winning side of a conflict does not change the output.
  CMap.addOrUpdate "x" 5 a

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "x", 10; "y", 2; "z", 3 ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force u -> k, v })
  )

  CMap.remove "x" b // now the left value surfaces

  Assert.Equal<Map<_, _>>(
    Map.ofList [ "x", 5; "y", 2; "z", 3 ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force u -> k, v })
  )

[<Fact>]
let ``AMap intersect pairs values and intersectWith combines``() =
  let a = CMap.empty<string, int>
  let b = CMap.empty<string, int>
  let i = AMap.intersect (CMap.value a) (CMap.value b)
  let iw = AMap.intersectWith (fun _ l r -> l * r) (CMap.value a) (CMap.value b)

  CMap.addOrUpdate "x" 3 a
  CMap.addOrUpdate "y" 5 a
  CMap.addOrUpdate "x" 4 b
  CMap.addOrUpdate "z" 6 b

  Assert.Equal<Map<string, int * int>>(
    Map.ofList [ "x", (3, 4) ],
    Map.ofSeq(
      seq { for KeyValue(k, struct (l, r)) in AMap.force i -> k, (l, r) }
    )
  )

  Assert.Equal<Map<_, _>>(
    Map.ofList [ "x", 12 ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force iw -> k, v })
  )

  CMap.addOrUpdate "y" 5 b
  Assert.Equal(2, AMap.count i |> AVal.getValue)
  CMap.remove "x" a

  Assert.Equal<Map<_, _>>(
    Map.ofList [ "y", 25 ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force iw -> k, v })
  )

[<Fact>]
let ``AMap choose2 matches a reference model under random churn``() =
  let a = CMap.empty<int, int>
  let b = CMap.empty<int, int>

  // The mapping: union of both sides; on conflict the right value wins.
  let m =
    AMap.choose2V
      (fun _ l r ->
        match l, r with
        | ValueSome lv, ValueSome rv -> ValueSome rv
        | ValueSome lv, ValueNone -> ValueSome lv
        | ValueNone, ValueSome rv -> ValueSome rv
        | ValueNone, ValueNone -> ValueNone)
      (CMap.value a)
      (CMap.value b)

  let rng = Random 99

  for _ in 1..400 do
    let target = if rng.NextDouble() < 0.5 then a else b
    let k = rng.Next(1, 25)
    let v = rng.Next(1, 100)

    if rng.NextDouble() < 0.6 then
      CMap.addOrUpdate k v target
    else
      CMap.remove k target

    let ma = Map.ofSeq(seq { for KeyValue(k, v) in CMap.force a -> k, v })
    let mb = Map.ofSeq(seq { for KeyValue(k, v) in CMap.force b -> k, v })

    let expected = Map.fold (fun acc k v -> Map.add k v acc) ma mb

    let actual = Map.ofSeq(seq { for KeyValue(k, v) in AMap.force m -> k, v })
    Assert.Equal<Map<int, int>>(expected, actual)

  // The mapping is not called when both sides are absent (FDA parity):
  // a removal of a key that has no value on either side must not crash even
  // for a mapping that pattern-matches only Some/Some.
  let strict =
    AMap.choose2V
      (fun _ l r ->
        match l, r with
        | ValueSome lv, ValueSome rv -> ValueSome(struct (lv, rv))
        | ValueSome _, ValueNone
        | ValueNone, ValueSome _ -> ValueNone
        | ValueNone, ValueNone -> failwith "mapping called with no side value")
      (CMap.value a)
      (CMap.value b)

  // A removal of a key with no value on either side must not call the mapping
  // (FDA parity), and keys present on one side only are dropped without error.
  CMap.remove 1000 a

  let expectedStrict =
    Set.intersect
      (Set.ofSeq(seq { for KeyValue(k, _) in CMap.force a -> k }))
      (Set.ofSeq(seq { for KeyValue(k, _) in CMap.force b -> k }))
    |> Set.count

  Assert.Equal(expectedStrict, AMap.count strict |> AVal.getValue)

[<Fact>]
let ``AMap ofASet keeps all values per key; IgnoreDuplicates keeps the last``
  ()
  =
  let source = CSet.empty<int * string>
  let all = AMap.ofASet(CSet.value source)
  let last = AMap.ofASetIgnoreDuplicates(CSet.value source)

  CSet.add (1, "a") source
  CSet.add (1, "b") source
  CSet.add (2, "c") source

  Assert.Equal<Map<int, Set<string>>>(
    Map.ofList [ 1, Set.ofList [ "a"; "b" ]; 2, Set.ofList [ "c" ] ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force all -> k, Set.ofSeq v })
  )

  Assert.Equal<Map<_, _>>(
    Map.ofList [ 1, "b"; 2, "c" ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force last -> k, v })
  )

  CSet.remove (1, "a") source

  Assert.Equal<Map<int, Set<string>>>(
    Map.ofList [ 1, Set.ofList [ "b" ]; 2, Set.ofList [ "c" ] ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force all -> k, Set.ofSeq v })
  )

  CSet.remove (1, "b") source

  Assert.Equal<Map<_, _>>(
    Map.ofList [ 2, "c" ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force last -> k, v })
  )

  // ofASetMapped: deriving the key from the values.
  let mapped =
    AMap.ofASetMapped
      (fun (s: string) -> s.Length)
      (CSet.value(CSet.ofSeq [ "ab"; "cd"; "e" ]))

  Assert.Equal<Map<int, Set<string>>>(
    Map.ofList [ 2, Set.ofList [ "ab"; "cd" ]; 1, Set.ofList [ "e" ] ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force mapped -> k, Set.ofSeq v })
  )

[<Fact>]
let ``AMap mapSet, toASet and toASetValues``() =
  let source = CSet.ofSeq [ 1; 2; 3 ]
  let ms = AMap.mapSet (fun k -> k * 10) (CSet.value source)

  Assert.Equal<Map<_, _>>(
    Map.ofList [ 1, 10; 2, 20; 3, 30 ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force ms -> k, v })
  )

  let m = CMap.empty<string, int>
  let keys = AMap.keys(CMap.value m)
  let values = AMap.toASetValues(CMap.value m)

  CMap.addOrUpdate "a" 1 m
  CMap.addOrUpdate "b" 1 m
  CMap.addOrUpdate "c" 2 m

  Assert.Equal<Set<_>>(Set.ofList [ "a"; "b"; "c" ], Set.ofSeq(ASet.force keys))
  Assert.Equal<Set<_>>(Set.ofList [ 1; 2 ], Set.ofSeq(ASet.force values))

  // Distinct values share one reference: removing one contributor keeps the value.
  CMap.remove "a" m
  Assert.Equal<Set<_>>(Set.ofList [ 1; 2 ], Set.ofSeq(ASet.force values))
  CMap.remove "b" m
  Assert.Equal<Set<_>>(Set.ofList [ 2 ], Set.ofSeq(ASet.force values))

[<Fact>]
let ``AMap ofAVal rebuilds on every value change``() =
  let v = CVal.create [ "a", 1; "b", 2 ]
  let m = AMap.ofAVal(CVal.value v)

  Assert.Equal<Map<_, _>>(
    Map.ofList [ "a", 1; "b", 2 ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force m -> k, v })
  )

  CVal.set [ "b", 3; "c", 4 ] v

  Assert.Equal<Map<_, _>>(
    Map.ofList [ "b", 3; "c", 4 ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force m -> k, v })
  )

[<Fact>]
let ``AMap custom drives content from a compute``() =
  let mutable pending = ResizeArray<MapDelta<string, int>>()

  let m =
    AMap.custom(fun view delta ->
      if pending.Count > 0 then
        let d = pending[0]
        pending.RemoveAt 0

        for struct (k, v) in d.SetEntries.ToArray() do
          delta.Set(k, v)

        for k in d.RemovedKeys.ToArray() do
          delta.Remove k

      ())

  Assert.Equal(0, AMap.count m |> AVal.getValue)
  let mutable d = MapDelta<string, int>()
  d.Set("x", 1)
  pending.Add d

  Assert.Equal<Map<_, _>>(
    Map.ofList [ "x", 1 ],
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force m -> k, v })
  )

[<Fact>]
let ``dirty derived source at first read does not double-apply``() =
  // A two-source node whose derived source is dirty (pending journal) at the
  // first read: the initial load drains the source, which pushes the delta
  // into the node's journal. If the journal is then applied on top of the
  // loaded view, the refcount double-counts and a later removal leaves a
  // phantom in the output.
  let src = CSet.empty<int>
  let a = ASet.map (fun x -> x * 10) (CSet.value src)
  let b = CSet.ofSeq [ 100 ]
  let u = ASet.union a (CSet.value b)

  // a is registered with src but unread by u; a is dirty when u is first
  // read.
  ASet.toSet a |> ignore
  CSet.add 1 src

  Assert.Equal<Set<int>>(Set.ofList [ 10; 100 ], ASet.toSet u)

  // The double-apply would leave a phantom refcount: removing 10 from a's
  // source would fail to remove 10 from the union.
  CSet.remove 1 src
  Assert.Equal<Set<int>>(Set.ofList [ 100 ], ASet.toSet u)

  // Map node over a dirty derived source (MapMapNode).
  let msrc = CMap.empty<int, int>
  let ma = AMap.map (fun k v -> k, v * 10) (CMap.value msrc)
  let mm = AMap.map (fun _ (_, v) -> v) ma

  (ma :> IAdaptiveMap<_, _>).GetValue() |> ignore
  CMap.addOrUpdate 2 20 msrc
  Assert.Equal<Map<int, int>>(Map.ofList [ 2, 200 ], AMap.toMap mm)
  CMap.remove 2 msrc
  Assert.Equal<Map<int, int>>(Map.empty, AMap.toMap mm)

  // Reduction over a dirty derived source (ASet.count).
  let rsrc = CSet.empty<int>
  let ra = ASet.map (fun x -> x * 10) (CSet.value rsrc)
  let rc = ASet.count ra

  AVal.getValue rc |> ignore
  CSet.add 3 rsrc
  Assert.Equal(1, AVal.getValue rc)
  CSet.remove 3 rsrc
  Assert.Equal(0, AVal.getValue rc)

[<Fact>]
let ``two-source node drains allocate zero in steady state``() =
  let left = CSet.empty<int>
  let right = CSet.empty<int>
  let inter = ASet.intersect (CSet.value left) (CSet.value right)
  let u = ASet.unionMany [ CSet.value left; CSet.value right ]
  let interCount = ASet.count inter
  let unionCount = ASet.count u

  for i in 1..50 do
    CSet.add i left
    CSet.add i right

  // Warm up: the initial load enumerates both source views once.
  AVal.getValue interCount |> ignore
  AVal.getValue unionCount |> ignore

  let before = GC.GetAllocatedBytesForCurrentThread()

  for i in 1..100 do
    CSet.remove ((i % 25) + 26) left
    CSet.add ((i % 25) + 26) left
    CSet.remove ((i % 25) + 26) right
    CSet.add ((i % 25) + 26) right

    if AVal.getValue interCount <> 50 then
      failwith "intersect drifted"

    if AVal.getValue unionCount <> 50 then
      failwith "union drifted"

  let allocated = GC.GetAllocatedBytesForCurrentThread() - before
  Assert.Equal(0L, allocated)

[<Fact>]
let ``choose2 node drains allocate zero in steady state``() =
  let a = CMap.empty<int, int>
  let b = CMap.empty<int, int>

  let u = AMap.unionWith (fun _ l r -> l + r) (CMap.value a) (CMap.value b)
  let unionCount = AMap.count u

  for i in 1..50 do
    CMap.addOrUpdate i i a
    CMap.addOrUpdate i (i * 2) b

  AVal.getValue unionCount |> ignore

  let before = GC.GetAllocatedBytesForCurrentThread()

  for i in 1..100 do
    let k = (i % 25) + 26
    CMap.remove k a
    CMap.addOrUpdate k i a
    CMap.remove k b
    CMap.addOrUpdate k (i * 2) b

    if u.GetValue().Count <> 50 then
      failwith "union drifted"


  let allocated = GC.GetAllocatedBytesForCurrentThread() - before
  Assert.Equal(0L, allocated)

// =============================================================================
// PLAN.md Section 7.4 — dynamic dependencies (collect / bind)
// =============================================================================

[<Fact>]
let ``ASet.collect unions the inner sets and follows their changes``() =
  let src = CSet.empty<int>
  let inner = CSet.empty<int>
  let collected = ASet.collect (fun _ -> CSet.value inner) (CSet.value src)

  CSet.add 1 src
  CSet.add 2 src
  CSet.add 3 inner
  CSet.add 4 inner

  // Two source elements map to the same inner set: 3 and 4 each get two
  // references, the output still holds each element once.
  Assert.Equal<Set<int>>(Set.ofList [ 3; 4 ], ASet.toSet collected)

  // Inner changes propagate to the output.
  CSet.add 5 inner
  Assert.Equal<Set<int>>(Set.ofList [ 3; 4; 5 ], ASet.toSet collected)
  CSet.remove 3 inner
  Assert.Equal<Set<int>>(Set.ofList [ 4; 5 ], ASet.toSet collected)

[<Fact>]
let ``ASet.collect refcounts shared output elements``() =
  // Two distinct source elements map to two distinct inner sets that share
  // an element. Removing one contribution keeps the element in the output.
  let buckets = CSet.empty<int>
  let odd = CSet.ofSeq [ 1; 3 ]
  let even = CSet.ofSeq [ 2; 4 ]
  let shared = CSet.ofSeq [ 9 ]

  let collected =
    ASet.collect
      (fun b ->
        match b with
        | 1 -> CSet.value odd
        | 2 -> CSet.value even
        | _ -> CSet.value shared)
      (CSet.value buckets)

  CSet.add 1 buckets
  CSet.add 2 buckets
  CSet.add 3 buckets

  // 1 -> odd {1,3}, 2 -> even {2,4}, 3 -> shared {9}
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 3; 4; 9 ], ASet.toSet collected)

  // Remove bucket 1: odd's elements {1,3} leave (nobody else has them).
  CSet.remove 1 buckets
  Assert.Equal<Set<int>>(Set.ofList [ 2; 4; 9 ], ASet.toSet collected)

  // Now bucket 2 also maps to shared: 9 has two references.
  CSet.remove 2 buckets
  Assert.Equal<Set<int>>(Set.ofList [ 9 ], ASet.toSet collected)

  // Removing bucket 3 drops the last reference: 9 leaves.
  CSet.remove 3 buckets
  Assert.Equal(0, (ASet.force collected).Count)

[<Fact>]
let ``ASet.collect id is the dynamic unionMany``() =
  // The outer set is itself adaptive: inner sets enter and leave the union.
  let outer = CSet.empty<Mibo.Adaptive.ChangeableSet<int>>
  let a = CSet.ofSeq [ 1; 2 ]
  let b = CSet.ofSeq [ 2; 3 ]

  let u = ASet.collect (fun s -> CSet.value s) (CSet.value outer)
  CSet.add a outer
  CSet.add b outer

  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 3 ], ASet.toSet u)

  // The inner sets keep contributing while they are in the outer set.
  CSet.add 4 a
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 3; 4 ], ASet.toSet u)

  // Removing an inner set removes its (refcounted) contribution.
  CSet.remove a outer
  Assert.Equal<Set<int>>(Set.ofList [ 2; 3 ], ASet.toSet u)

  // The removed inner set's later changes do not leak (eager unregister).
  CSet.add 5 a
  Assert.Equal<Set<int>>(Set.ofList [ 2; 3 ], ASet.toSet u)

[<Fact>]
let ``ASet.collect handles source churn and inner churn in one batch``() =
  let outer = CSet.empty<int>
  let inner1 = CSet.ofSeq [ 1; 2 ]
  let inner2 = CSet.ofSeq [ 3 ]

  let u =
    ASet.collect
      (fun x -> if x = 1 then CSet.value inner1 else CSet.value inner2)
      (CSet.value outer)

  CSet.add 1 outer
  CSet.add 2 outer
  CSet.add 3 inner1
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 3 ], ASet.toSet u)

  // One batch: drop element 1 (inner1 leaves), element 2 stays on inner2.
  // inner2 gains 3 in the same batch.
  Transaction.run(fun () ->
    CSet.remove 1 outer
    CSet.add 3 inner2)
  |> ignore

  Assert.Equal<Set<int>>(Set.ofList [ 3 ], ASet.toSet u)

  // Re-add element 1: a fresh entry maps it again.
  CSet.add 1 outer
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 3 ], ASet.toSet u)

[<Fact>]
let ``ASet.collect accepts poll inner sets (ofReader)``() =
  let mutable current = HashSet<int>()
  current.Add 1 |> ignore
  current.Add 2 |> ignore

  let reader =
    ASet.ofReader(fun () ->
      let next = HashSet<int>(current)
      next)

  let src = CSet.ofSeq [ 10; 20 ]
  let u = ASet.collect (fun _ -> reader) (CSet.value src)

  Assert.Equal<Set<int>>(Set.ofList [ 1; 2 ], ASet.toSet u)

  // The poll inner changes; the version check pulls it on the next read.
  current.Remove 1 |> ignore
  current.Add 3 |> ignore
  Assert.Equal<Set<int>>(Set.ofList [ 2; 3 ], ASet.toSet u)

  // Drop one source element: its contribution (with the fresh polled
  // content) leaves, the other element still contributes.
  CSet.remove 10 src
  Assert.Equal<Set<int>>(Set.ofList [ 2; 3 ], ASet.toSet u)

[<Fact>]
let ``ASet.collect with a derived inner settles a tail-only count``() =
  // A derived inner (map|>filter) does not deliver at write time: it
  // delivers only when the collect is pulled. The collect's dirty
  // indicator must therefore scan the inner entries, or a tail-only
  // count/contains over the collect serves the stale value forever.
  let innerSrc = CSet.empty<int>

  let derived =
    innerSrc |> ASet.map(fun v -> v * 2) |> ASet.filter(fun v -> v > 10)

  let buckets = CSet.ofSeq [ 1 ]
  let collected = ASet.collect (fun _ -> derived) (CSet.value buckets)
  let cnt = collected |> ASet.count
  let has84 = collected |> ASet.contains 84

  Assert.Equal(0, AVal.getValue cnt)
  Assert.False(AVal.getValue has84)

  // 42*2 = 84 > 10: the derived inner gains it, the tail must settle.
  CSet.add 42 innerSrc |> ignore
  Assert.Equal(1, AVal.getValue cnt)
  Assert.True(AVal.getValue has84)

  // A second derived-inner change with no intermediate read.
  CSet.add 30 innerSrc |> ignore
  // 30*2 = 60 > 10: count rises to 2.
  Assert.Equal(2, AVal.getValue cnt)

  // A filtered-out element (5*2 = 10, not > 10) does not move the count.
  CSet.add 5 innerSrc |> ignore
  Assert.Equal(2, AVal.getValue cnt)

  // Removing a contributing element drops it.
  CSet.remove 42 innerSrc |> ignore
  Assert.Equal(1, AVal.getValue cnt)

[<Fact>]
let ``ASet.collect with a derived inner settles a tail-only reader of the collect``
  ()
  =
  // Same gap, exercised by reading the collect itself (not just a scalar
  // escape): the whole-collection view is served by GetValue, gated by the
  // version a downstream map consumer recorded.
  let innerSrc = CSet.empty<int>

  let derived =
    innerSrc |> ASet.map(fun v -> v * 2) |> ASet.filter(fun v -> v > 10)

  let buckets = CSet.ofSeq [ 1 ]
  let collected = ASet.collect (fun _ -> derived) (CSet.value buckets)
  let cnt = collected |> ASet.toAVal |> AVal.map(fun s -> s.Count)

  Assert.Equal(0, AVal.getValue cnt)
  CSet.add 42 innerSrc |> ignore
  Assert.Equal(1, AVal.getValue cnt)

[<Fact>]
let ``ASet.bind swaps the inner set and unregisters the old one eagerly``() =
  let selected = CVal.create 0
  let buckets = [| CSet.ofSeq [ 1; 2 ]; CSet.ofSeq [ 3; 4 ] |]

  let visible = ASet.bind (fun i -> CSet.value buckets[i]) (CVal.value selected)
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2 ], ASet.toSet visible)

  // The bound inner set's changes propagate.
  CSet.add 5 (buckets[0])
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 5 ], ASet.toSet visible)

  // Swap: the old content leaves, the new content enters.
  CVal.set 1 selected
  Assert.Equal<Set<int>>(Set.ofList [ 3; 4 ], ASet.toSet visible)

  // The old inner set is unregistered: its later changes do not leak.
  CSet.add 6 (buckets[0])
  CSet.remove 3 (buckets[1])
  Assert.Equal<Set<int>>(Set.ofList [ 4 ], ASet.toSet visible)

  // Swapping back re-reads the current content of bucket 0 (with 6).
  CVal.set 0 selected
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 5; 6 ], ASet.toSet visible)

[<Fact>]
let ``AMap.bind swaps the inner map and unregisters the old one eagerly``() =
  let selected = CVal.create 0
  let tables = [| CMap.ofSeq [ "a", 1; "b", 2 ]; CMap.ofSeq [ "c", 3 ] |]

  let visible = AMap.bind (fun i -> CMap.value tables[i]) (CVal.value selected)

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "a", 1; "b", 2 ],
    AMap.toMap visible
  )

  CMap.addOrUpdate "b" 20 tables[0]

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "a", 1; "b", 20 ],
    AMap.toMap visible
  )

  CVal.set 1 selected
  Assert.Equal<Map<string, int>>(Map.ofList [ "c", 3 ], AMap.toMap visible)

  // Old inner unregistered: later changes do not leak.
  CMap.addOrUpdate "b" 999 tables[0]
  CMap.remove "c" tables[1]
  Assert.Equal<Map<string, int>>(Map.empty, AMap.toMap visible)

  CVal.set 0 selected

  Assert.Equal<Map<string, int>>(
    Map.ofList [ "a", 1; "b", 999 ],
    AMap.toMap visible
  )

[<Fact>]
let ``collect and bind dispose cleanly``() =
  let source = CSet.ofSeq [ 1; 2 ]
  let inner = CSet.ofSeq [ 10 ]
  let collected = ASet.collect (fun _ -> CSet.value inner) (CSet.value source)

  ASet.toSet collected |> ignore
  let cs = source
  let ci = inner
  Assert.Equal(1, cs.SinkCount)
  Assert.Equal(2, ci.SinkCount) // one sink per source element

  (collected :> IDisposable).Dispose()
  Assert.Equal(0, cs.SinkCount)
  Assert.Equal(0, ci.SinkCount)

  Assert.Throws<InvalidOperationException>(fun () ->
    ASet.toSet collected |> ignore)
  |> ignore

  // Writes after disposal process nothing.
  CSet.add 3 source
  CSet.add 11 inner
  Assert.Equal(0, cs.SinkCount)
  Assert.Equal(0, ci.SinkCount)

  // Bind disposal unregisters the value edge and the inner sink.
  let selected = CVal.create 0
  let buckets = [| CSet.ofSeq [ 1 ]; CSet.ofSeq [ 2 ] |]
  let bound = ASet.bind (fun i -> CSet.value buckets[i]) (CVal.value selected)
  ASet.toSet bound |> ignore
  Assert.Equal(1, buckets[0].SinkCount)

  (bound :> IDisposable).Dispose()
  Assert.Equal(0, buckets[0].SinkCount)

  Assert.Throws<InvalidOperationException>(fun () -> ASet.toSet bound |> ignore)
  |> ignore

[<Fact>]
let ``collect drain allocates zero in steady state``() =
  let outer = CSet.empty<int>
  let inner1 = CSet.ofSeq [ 1; 2; 3 ]
  let inner2 = CSet.ofSeq [ 4; 5 ]

  let u =
    ASet.collect
      (fun x -> if x % 2 = 0 then CSet.value inner1 else CSet.value inner2)
      (CSet.value outer)

  for i in 1..20 do
    CSet.add i outer

  // Warm up: initial load + one full drain cycle grows all buffers.
  u.GetValue().Count |> ignore

  for i in 1..20 do
    CSet.add (i + 100) outer

  u.GetValue().Count |> ignore

  let before = GC.GetAllocatedBytesForCurrentThread()

  for i in 1..200 do
    // Churn an element that is actually in inner1 (1..3): the output
    // must stay {1..5} through every remove/add cycle.
    CSet.remove ((i % 3) + 1) inner1
    CSet.add ((i % 3) + 1) inner1

    if u.GetValue().Count <> 5 then
      failwith "collect drifted"

  let allocated = GC.GetAllocatedBytesForCurrentThread() - before
  Assert.Equal(0L, allocated)

[<Fact>]
let ``write during a drain is deferred to the next read``() =
  // The mapping runs during the drain; a write to a source of the same node
  // appends to the journal mid-processing. The compaction markers keep the
  // reentrant entries for the next read: nothing is lost, nothing double-applies.
  let src = CSet.ofSeq [ 1; 2 ]
  let mutable reentrant = false

  let u =
    ASet.map
      (fun x ->
        if x = 2 && not reentrant then
          reentrant <- true
          CSet.add 3 src

        x * 10)
      (CSet.value src)

  // The reentrant write lands in the journal during the first read's load;
  // the first drain applies it before that read returns (the write is part
  // of the read's execution). Later reads see it exactly once.
  Assert.Equal<Set<int>>(Set.ofList [ 10; 20; 30 ], ASet.toSet u)
  Assert.Equal<Set<int>>(Set.ofList [ 10; 20; 30 ], ASet.toSet u)

  // And the removal works: no phantom refcounts.
  CSet.remove 3 src
  Assert.Equal<Set<int>>(Set.ofList [ 10; 20 ], ASet.toSet u)

// NOTE: reading a node from inside its own mapping is out of contract
// (undefined in FDA as well). Writes during a drain are supported: they
// land in the journal via the compaction markers and apply exactly once.

// =============================================================================
// FDA public API parity (docs/PARITY-FDA.md)
// =============================================================================

[<Fact>]
let ``type aliases are usable in signature positions``() =
  // FDA parity: aval/cval/aset/cset/amap/cmap abbreviations resolve to our
  // interfaces and changeable types.
  let v: aval<int> = AVal.constant 1
  let c: cval<int> = CVal.create 2
  let s: aset<int> = ASet.empty
  let cs: cset<int> = CSet.empty
  let m: amap<string, int> = AMap.empty
  let cm: cmap<string, int> = CMap.empty

  // The aliases are the same types as the long names: cross-assignment works.
  let v2: IAdaptiveValue<int> = v
  let c2: ChangeableValue<int> = c
  let s2: IAdaptiveSet<int> = s
  let cs2: ChangeableSet<int> = cs
  let m2: IAdaptiveMap<string, int> = m
  let cm2: ChangeableMap<string, int> = cm

  Assert.Equal(1, v2.GetValue())
  Assert.Equal(2, c2.GetValue()) // concrete cval exposes GetValue (FDA parity)
  Assert.Equal(0, s2.GetValue().Count)
  Assert.Equal(0, (ASet.getValue cs2).Count)
  Assert.Equal(0, m2.GetValue().Count)
  Assert.Equal(0, (AMap.getValue cm2).Count)

[<Fact>]
let ``AVal force and init match FDA``() =
  let c = AVal.init 5
  Assert.Equal(5, AVal.force c)
  CVal.set 6 c
  Assert.Equal(6, AVal.force c)
  // force is getValue under another name.
  Assert.Equal(AVal.getValue c, AVal.force c)

[<Fact>]
let ``AVal delay computes once on first read``() =
  let mutable count = 0

  let v: aval<int> =
    AVal.delay(fun () ->
      count <- count + 1
      42)

  Assert.Equal(0, count) // not computed at construction
  Assert.Equal(42, AVal.getValue v)
  Assert.Equal(42, AVal.getValue v)
  Assert.Equal(1, count) // computed exactly once

[<Fact>]
let ``AVal bind2 and bind3 switch inner values``() =
  let a = CVal.create 1
  let b = CVal.create 10
  let i1 = CVal.create 100
  let i2 = CVal.create 200
  let i3 = CVal.create 300

  let v2 =
    AVal.bind2
      (fun x y -> if x + y > 15 then CVal.value i2 else CVal.value i1)
      (CVal.value a)
      (CVal.value b)

  Assert.Equal(100, AVal.getValue v2) // 1 + 10 <= 15 -> i1
  CVal.set 6 a // 6 + 10 > 15 -> switch to i2
  Assert.Equal(200, AVal.getValue v2)
  CVal.set 1 a // switch back
  Assert.Equal(100, AVal.getValue v2)

  let v3 =
    AVal.bind3
      (fun x y z -> if x + y + z > 25 then CVal.value i3 else CVal.value i1)
      (CVal.value a)
      (CVal.value b)
      (CVal.value a)

  Assert.Equal(100, AVal.getValue v3) // 1 + 10 + 1 <= 25 -> i1
  CVal.set 8 a // 8 + 10 + 8 > 25 -> switch to i3
  Assert.Equal(300, AVal.getValue v3)

[<Fact>]
let ``AVal custom computes lazily and caches``() =
  let mutable counter = 0

  let v: aval<int> =
    AVal.custom(fun () ->
      counter <- counter + 1
      counter)

  Assert.Equal(0, counter) // not computed at construction
  Assert.Equal(1, AVal.getValue v)
  Assert.Equal(1, AVal.getValue v)
  Assert.Equal(1, counter) // cached: computes once per change

[<Fact>]
let ``cval Value property and UpdateTo``() =
  let c = CVal.create 1
  Assert.Equal(1, c.Value)
  c.Value <- 2
  Assert.Equal(2, AVal.getValue(CVal.value c))
  // UpdateTo returns whether the value changed.
  Assert.True(c.UpdateTo 3)
  Assert.Equal(3, c.Value)
  Assert.False(c.UpdateTo 3)
  Assert.False(c.UpdateTo 3)

[<Fact>]
let ``cval Value set inside a transaction defers``() =
  let c = CVal.create 1

  Transaction.run(fun () ->
    c.Value <- 2
    // Reads inside the transaction see the pre-transaction value.
    Assert.Equal(1, AVal.getValue(CVal.value c)))
  |> ignore

  Assert.Equal(2, AVal.getValue(CVal.value c))

[<Fact>]
let ``ASet empty and AMap empty are empty and never mark``() =
  let s: aset<int> = ASet.empty
  let m: amap<string, int> = AMap.empty
  Assert.Equal(0, s.GetValue().Count)
  Assert.Equal(0, m.GetValue().Count)
  Assert.Equal(0L, (s :> IAdaptiveObject).Version)
  Assert.Equal(0L, (m :> IAdaptiveObject).Version)

[<Fact>]
let ``AMap constant and delay compute the creator once``() =
  let mutable count = 0

  let mk() =
    count <- count + 1
    Dictionary(dict [ "a", 1 ])

  let m: amap<string, int> = AMap.constant mk
  Assert.Equal(0, count)
  Assert.Equal(1, m.GetValue().Count)
  Assert.Equal(1, m.GetValue().Count)
  Assert.Equal(1, count)

  let mutable count2 = 0

  let d: amap<string, int> =
    AMap.delay(fun () ->
      count2 <- count2 + 1
      Dictionary())

  Assert.Equal(0, count2)
  Assert.Equal(0, d.GetValue().Count)
  Assert.Equal(1, count2)

// =============================================================================
// AList prototype (docs/ALIST-DESIGN.md §7)
// =============================================================================

[<Fact>]
let ``AList map reflects changes``() =
  let src = CList.ofList [ 1; 2; 3 ]
  let mapped = AList.map (fun x -> x * 10) (CList.value src)
  Assert.Equal<int list>([ 10; 20; 30 ], AList.toList mapped)
  CList.append 4 src
  Assert.Equal<int list>([ 10; 20; 30; 40 ], AList.toList mapped)
  CList.insertAt 0 0 src
  Assert.Equal<int list>([ 0; 10; 20; 30; 40 ], AList.toList mapped)
  CList.updateAt 2 99 src
  Assert.Equal<int list>([ 0; 10; 990; 30; 40 ], AList.toList mapped)
  CList.removeAt 0 src
  Assert.Equal<int list>([ 10; 990; 30; 40 ], AList.toList mapped)
  CList.clear src
  Assert.Equal<int list>([], AList.toList mapped)

[<Fact>]
let ``AList mapA follows element avals and structural edits``() =
  let l = CList.ofList [ 1; 2; 3 ]
  let even = CVal.create 1
  let odd = CVal.create 0

  let result =
    l
    |> AList.mapA(fun v ->
      if v % 2 = 0 then even :> aval<int> else odd :> aval<int>)

  // (1,0) (2,1) (3,0)
  Assert.Equal<int list>([ 0; 1; 0 ], AList.toList result)

  CVal.set 2 odd
  // (1,2) (2,1) (3,2)
  Assert.Equal<int list>([ 2; 1; 2 ], AList.toList result)

  CList.append 4 l
  // (4,1)
  Assert.Equal<int list>([ 2; 1; 2; 1 ], AList.toList result)

  CVal.set 5 even
  // (2,5) (4,5)
  Assert.Equal<int list>([ 2; 5; 2; 5 ], AList.toList result)

  CList.removeAt 0 l
  // the first element leaves with its aval
  Assert.Equal<int list>([ 5; 2; 5 ], AList.toList result)

  CVal.set 1 even
  CVal.set 0 odd
  Assert.Equal<int list>([ 1; 0; 1 ], AList.toList result)

[<Fact>]
let ``AList chooseA survival flips``() =
  let l = CList.ofList [ 1; 2; 3 ]
  let even = CVal.create(Some 1)
  let odd = CVal.create(Some 0)

  let result =
    l
    |> AList.chooseA(fun v ->
      if v % 2 = 0 then
        even :> aval<int option>
      else
        odd :> aval<int option>)

  // (1,0) (2,1) (3,0)
  Assert.Equal<int list>([ 0; 1; 0 ], AList.toList result)

  CVal.set (Some 2) odd
  Assert.Equal<int list>([ 2; 1; 2 ], AList.toList result)

  CVal.set None even
  // (2,None) (4 absent): the middle element leaves the output
  Assert.Equal<int list>([ 2; 2 ], AList.toList result)

  CList.append 4 l
  // (4,None)
  Assert.Equal<int list>([ 2; 2 ], AList.toList result)

  CVal.set (Some 5) even
  // (2,5) (4,5): both enter the output
  Assert.Equal<int list>([ 2; 5; 2; 5 ], AList.toList result)

[<Fact>]
let ``AList filterA flips with predicate avals``() =
  let takeEven = CVal.create true
  let takeOdd = CVal.create true
  let l = CList.ofList [ 0; 1; 2; 3; 4 ]

  let filtered =
    l
    |> AList.filterA(fun i ->
      if i % 2 = 0 then
        takeEven :> aval<bool>
      else
        takeOdd :> aval<bool>)

  Assert.Equal<int list>([ 0; 1; 2; 3; 4 ], AList.toList filtered)

  CVal.set false takeEven
  Assert.Equal<int list>([ 1; 3 ], AList.toList filtered)

  CVal.set false takeOdd
  Assert.Equal<int list>([], AList.toList filtered)

  CVal.set true takeOdd
  CVal.set true takeEven
  Assert.Equal<int list>([ 0; 1; 2; 3; 4 ], AList.toList filtered)

[<Fact>]
let ``AList mapA steady-state element writes allocate zero bytes``() =
  let l = CList.ofList(List.init 1000 id)
  let v = CVal.create 0
  let result = l |> AList.mapA(fun _ -> v :> aval<int>)
  AList.getValue result |> ignore
  // Warm up: the first reads grow the internal buffers.
  CVal.set 1 v
  AList.getValue result |> ignore

  let before = GC.GetAllocatedBytesForCurrentThread()

  for i in 1..1000 do
    CVal.set (i % 2) v
    AList.getValue result |> ignore

  let allocated = GC.GetAllocatedBytesForCurrentThread() - before
  Assert.Equal(0L, allocated)

[<Fact>]
let ``AList mapiA passes the position at mapping time``() =
  let l = CList.ofList [ 10; 20; 30 ]

  let result = l |> AList.mapiA(fun i _ -> AVal.constant i)

  Assert.Equal<int list>([ 0; 1; 2 ], AList.toList result)

  CList.insertAt 0 5 l
  // The new element maps at position 0; shifted elements keep their aval
  // (the mapping does not re-run for shifted elements, docs/2026-08-05-
  // MAPA-DESIGN.md §4: FDA's stable-Index equivalent).
  Assert.Equal<int list>([ 0; 0; 1; 2 ], AList.toList result)

  CList.removeAt 2 l // removes 20 (mapped 1); 30 keeps its mapped 2
  Assert.Equal<int list>([ 0; 0; 2 ], AList.toList result)

  CList.append 6 l // the new element maps at position 3
  Assert.Equal<int list>([ 0; 0; 2; 3 ], AList.toList result)

  CList.updateAt 1 99 l // the updated element re-maps at position 1
  Assert.Equal<int list>([ 0; 1; 2; 3 ], AList.toList result)

[<Fact>]
let ``AList mapiA inner change (mapping depends on another adaptive set)``() =
  let map = CList.ofList [ 1; 2; 3; 4; 5 ]
  let keys = CSet.ofSeq [ 0; 2; 4 ]

  let res =
    map
    |> AList.mapiA(fun k v ->
      keys
      |> ASet.contains k
      |> AVal.map (function
        | true -> v
        | false -> -1))

  Assert.Equal<int list>([ 1; -1; 3; -1; 5 ], AList.toList res)

  CList.set [ 2; 4; 6; 8; 10 ] map
  Assert.Equal<int list>([ 2; -1; 6; -1; 10 ], AList.toList res)

  CSet.set (Set.ofList [ 0; 2; 3; 4 ]) keys
  Assert.Equal<int list>([ 2; -1; 6; 8; 10 ], AList.toList res)

[<Fact>]
let ``AList filteriA flips by position``() =
  let map = CList.ofList [ 1; 2; 3; 4; 5 ]
  let keys = CSet.ofSeq [ 0; 2; 4 ]

  let res = map |> AList.filteriA(fun k _ -> keys |> ASet.contains k)

  Assert.Equal<int list>([ 1; 3; 5 ], AList.toList res)

  CList.set [ 2; 4; 6; 8; 10 ] map
  Assert.Equal<int list>([ 2; 6; 10 ], AList.toList res)

  CSet.set (Set.ofList [ 0; 2; 3; 4 ]) keys
  Assert.Equal<int list>([ 2; 6; 8; 10 ], AList.toList res)

[<Fact>]
let ``AList chooseiA survival flips by position``() =
  let l = CList.ofList [ 1; 2; 3 ]
  let keepEven = CVal.create true

  let result =
    l
    |> AList.chooseiA(fun i v ->
      keepEven
      |> AVal.map(fun k -> if k && i % 2 = 1 then Some(v * 10) else None))

  Assert.Equal<int list>([ 20 ], AList.toList result)

  CVal.set false keepEven
  Assert.Equal<int list>([], AList.toList result)

  CList.append 4 l
  Assert.Equal<int list>([], AList.toList result)

  CVal.set true keepEven
  Assert.Equal<int list>([ 20; 40 ], AList.toList result)

[<Fact>]
let ``AList filter and choose update semantics``() =
  let src = CList.ofList [ 1; 2; 3; 4; 5 ]
  let evens = AList.filter (fun x -> x % 2 = 0) (CList.value src)
  Assert.Equal<int list>([ 2; 4 ], AList.toList evens)
  CList.prepend 0 src
  Assert.Equal<int list>([ 0; 2; 4 ], AList.toList evens)
  // update of a filtered-out element that now passes -> inserted
  CList.updateAt 3 6 src
  Assert.Equal<int list>([ 0; 2; 6; 4 ], AList.toList evens)
  // update of a passing element that now fails -> removed
  CList.updateAt 3 7 src
  Assert.Equal<int list>([ 0; 2; 4 ], AList.toList evens)
  // remove of a filtered-out element -> the later survivors shift position
  CList.removeAt 1 src
  Assert.Equal<int list>([ 0; 2; 4 ], AList.toList evens)
  // update of a surviving element -> update in place (4 at position 3 -> 6)
  CList.updateAt 3 6 src
  Assert.Equal<int list>([ 0; 2; 6 ], AList.toList evens)

  let chosen =
    AList.choose
      (fun x -> if x % 2 = 0 then Some(x * 100) else None)
      (CList.value src)

  Assert.Equal<int list>([ 0; 200; 600 ], AList.toList chosen)

[<Fact>]
let ``AList append concatenates with cross-source ordering``() =
  let l = CList.ofList [ 1; 2 ]
  let r = CList.ofList [ 3; 4 ]
  let all = AList.append (CList.value l) (CList.value r)
  Assert.Equal<int list>([ 1; 2; 3; 4 ], AList.toList all)

  // The tricky case: a right insert and a left append in ONE batch. The ops
  // arrive in write order; the right op's absolute position uses leftCount
  // at its application point (docs/ALIST-DESIGN.md §3.4).
  Transaction.run(fun () ->
    CList.insertAt 0 9 r
    CList.append 5 l)

  Assert.Equal<int list>([ 1; 2; 5; 9; 3; 4 ], AList.toList all)

  // left removal shifts the right base offset
  CList.removeAt 0 l
  Assert.Equal<int list>([ 2; 5; 9; 3; 4 ], AList.toList all)

  Transaction.run(fun () ->
    CList.append 6 l
    CList.removeAt 0 r)

  Assert.Equal<int list>([ 2; 5; 6; 3; 4 ], AList.toList all)

[<Fact>]
let ``ChangeableList transactions replay in order``() =
  let src = CList.ofList [ 1; 2; 3 ]

  Transaction.run(fun () ->
    CList.append 4 src
    CList.removeAt 0 src)
  |> ignore

  Assert.Equal<int list>([ 2; 3; 4 ], AList.toList(CList.value src))

[<Fact>]
let ``ChangeableList Set and Clear inside transactions``() =
  let src = CList.ofList [ 1; 2; 3 ]

  // Set is last-wins over the whole batch.
  Transaction.run(fun () ->
    CList.append 4 src
    CList.set [ 9; 8 ] src)

  Assert.Equal<int list>([ 9; 8 ], AList.toList(CList.value src))

  // Set then Clear -> empty.
  Transaction.run(fun () ->
    CList.set [ 1 ] src
    CList.clear src)

  Assert.Equal<int list>([], AList.toList(CList.value src))

  // Clear then Set -> the set value.
  Transaction.run(fun () ->
    CList.clear src
    CList.set [ 5; 6 ] src)

  Assert.Equal<int list>([ 5; 6 ], AList.toList(CList.value src))

  // Appends after the Set are superseded (last-wins, docs/ALIST-DESIGN.md §3.3).
  Transaction.run(fun () ->
    CList.set [ 7 ] src
    CList.append 8 src)

  Assert.Equal<int list>([ 7 ], AList.toList(CList.value src))

[<Fact>]
let ``ChangeableList no-op writes do not mark``() =
  let src = CList.ofList [ 1; 2 ]
  let versionBefore = (CList.value src :> IAdaptiveObject).Version
  CList.updateAt 0 1 src // equal value
  CList.remove 99 src // absent
  Assert.Equal(versionBefore, (CList.value src :> IAdaptiveObject).Version)
  CList.updateAt 0 2 src
  Assert.Equal(versionBefore + 1L, (CList.value src :> IAdaptiveObject).Version)

[<Fact>]
let ``AList count and isEmpty``() =
  let src = CList.ofList [ 1; 2; 3 ]
  let c = AList.count(CList.value src)
  let e = AList.isEmpty(CList.value src)
  Assert.Equal(3, AVal.getValue c)
  Assert.False(AVal.getValue e)
  CList.append 4 src
  Assert.Equal(4, AVal.getValue c)
  CList.clear src
  Assert.Equal(0, AVal.getValue c)
  Assert.True(AVal.getValue e)

[<Fact>]
let ``AList constructors``() =
  Assert.Equal<int list>([], AList.toList(AList.empty: alist<int>))
  Assert.Equal<int list>([ 1 ], AList.toList(AList.single 1))

  Assert.Equal<int list>(
    [ 1; 2; 3 ],
    [ 1; 2; 3 ] |> AList.ofSeq |> AList.toList
  )

  Assert.Equal<int list>([ 1; 2 ], [| 1; 2 |] |> AList.ofArray |> AList.toList)
  Assert.Equal<int list>([ 1; 2 ], [ 1; 2 ] |> AList.ofList |> AList.toList)

  Assert.Equal<int list>(
    [ 1; 2 ],
    ResizeArray [ 1; 2 ] |> AList.ofResizeArray |> AList.toList
  )

  Assert.Equal<int[]>([| 1; 2 |], [ 1; 2 ] |> AList.ofSeq |> AList.force)

  let mutable count = 0

  let c =
    AList.constant(fun () ->
      count <- count + 1
      ResizeArray [ 1 ])

  Assert.Equal(0, count)
  Assert.Equal<int list>([ 1 ], AList.toList c)
  Assert.Equal(1, count)
  Assert.Equal<int list>([ 1 ], AList.toList c)
  Assert.Equal(1, count) // computed once

[<Fact>]
let ``dirty derived list at first read does not double-apply``() =
  let src = CList.ofList [ 1; 2; 3 ]
  let doubled = AList.map (fun x -> x * 2) (CList.value src)

  // Write to the source BEFORE doubled is ever read: the write cannot reach
  // its journal (registration is lazy), so the load must see it exactly once.
  CList.append 4 src

  let filtered = AList.filter (fun x -> x > 5) doubled
  Assert.Equal<int list>([ 6; 8 ], AList.toList filtered)

  // Now doubled is registered: writes push through the chain.
  CList.append 5 src
  Assert.Equal<int list>([ 6; 8; 10 ], AList.toList filtered)

[<Fact>]
let ``AList derived node disposal unregisters``() =
  let src = CList.ofList [ 1; 2; 3 ]
  let mapped = AList.map id (CList.value src)
  Assert.Equal(0, src.SinkCount) // registration is lazy until the first read
  Assert.Equal<int list>([ 1; 2; 3 ], AList.toList mapped)
  Assert.Equal(1, src.SinkCount)
  (mapped :> IDisposable).Dispose()
  Assert.Equal(0, src.SinkCount)
  CList.append 4 src // no throw, no delivery to the disposed node
  Assert.Equal(0, src.SinkCount)

[<Fact>]
let ``AList drains allocate zero in steady state``() =
  // Permanent allocation test (ALIST-DESIGN.md §7): an N-op batch
  // (write + drain + delivery) allocates 0 bytes after warmup.
  //
  // Chain-depth semantics: the version check in a node read reaches its
  // direct source only (depth 2 works; deeper unobserved chains are not
  // drained by a single read). The read here targets `filtered` (depth 2),
  // which drains the chain.
  //
  // The filter predicate passes every value (mapped = x*2 is always even),
  // so every write produces an output delta. The read each iteration drains
  // both writes as one batch.
  let src = CList.ofList [ for i in 1..100 -> i ]
  let mapped = AList.map (fun x -> x * 2) (CList.value src)
  let filtered = AList.filter (fun x -> x % 2 = 0) mapped

  // Warm up: read once (registers the chain and grows every buffer).
  AList.toList filtered |> ignore

  let step() =
    for i in 1..100 do
      CList.append i src
      CList.removeAt 0 src
      AList.getValue filtered |> ignore

  step()
  let before = GC.GetAllocatedBytesForCurrentThread()
  step()
  let allocated = GC.GetAllocatedBytesForCurrentThread() - before
  Assert.Equal(0L, allocated)

[<Fact>]
let ``transaction appends land in write order``() =
  // Regression: several appends in one transaction journaled the same
  // pre-transaction position; the sequential replay reversed them.
  let s = CList.ofList [ 1; 2; 3 ]

  Transaction.run(fun () ->
    CList.append 4 s
    CList.append 5 s
    CList.append 6 s)

  Assert.Equal<int list>([ 1; 2; 3; 4; 5; 6 ], AList.toList(CList.value s))

  // append after a clear inside a transaction
  let s2 = CList.ofList [ 1; 2; 3 ]

  Transaction.run(fun () ->
    CList.clear s2
    CList.append 7 s2
    CList.append 8 s2)

  Assert.Equal<int list>([ 7; 8 ], AList.toList(CList.value s2))

  // append and prepend interleaved in one transaction
  let s3 = CList.ofList [ 1; 2; 3 ]

  Transaction.run(fun () ->
    CList.prepend 0 s3
    CList.append 4 s3
    CList.append 5 s3)

  Assert.Equal<int list>([ 0; 1; 2; 3; 4; 5 ], AList.toList(CList.value s3))

  // Positions are replay-relative (docs/ALIST-DESIGN.md §3.3): the second
  // insertAt 2 targets the state after the first insert (the element 3),
  // so it lands before it. An insert at the replay-time end is an append.
  let s4 = CList.ofList [ 1; 2 ]

  Transaction.run(fun () ->
    CList.insertAt 2 3 s4
    CList.insertAt 2 4 s4)

  Assert.Equal<int list>([ 1; 2; 4; 3 ], AList.toList(CList.value s4))

  // A positional op that is valid against the pre-transaction list but not
  // against the replay state throws at write time (all-or-nothing commit):
  // removeAt 0 shrinks the replay, so removeAt 1 is out of range.
  let s5 = CList.ofList [ 1; 2 ]

  Assert.Throws<System.ArgumentOutOfRangeException>(fun () ->
    Transaction.run(fun () ->
      CList.removeAt 0 s5
      CList.removeAt 1 s5)
    |> ignore)
  |> ignore

  Assert.Equal<int list>([ 1; 2 ], AList.toList(CList.value s5))

// =============================================================================
// Hostile-review fixes (docs/2026-08-05-GLM_REVIEW_FINDINGS.md,
// docs/2026-08-05-KIMI_REVIEW_FINDINGS.md) — regression tests
// =============================================================================

[<Fact>]
let ``ASet ofAVal feeds derived nodes after the first read``() =
  // KIMI 1: OfAvalSetNode never advanced its version, so downstream nodes
  // stopped re-pulling the source after the first read.
  let v = CVal.create [| 1; 2; 3 |]
  let mapped = ASet.map (fun x -> x * 10) (ASet.ofAVal(CVal.value v))
  Assert.Equal<Set<_>>(Set.ofList [ 10; 20; 30 ], Set.ofSeq(ASet.force mapped))
  CVal.set [| 3; 4 |] v
  Assert.Equal<Set<_>>(Set.ofList [ 30; 40 ], Set.ofSeq(ASet.force mapped))
  CVal.set [||] v
  Assert.Equal<Set<int>>(Set.empty, Set.ofSeq(ASet.force mapped))

[<Fact>]
let ``AMap ofAVal feeds derived nodes after the first read``() =
  // KIMI 1, map side.
  let v = CVal.create [ "a", 1 ]
  let mapped = AMap.map (fun k x -> x * 10) (AMap.ofAVal(CVal.value v))

  let view() =
    Map.ofSeq(seq { for KeyValue(k, x) in AMap.force mapped -> k, x })

  Assert.Equal<Map<string, int>>(Map.ofList [ "a", 10 ], view())
  CVal.set [ "b", 2 ] v
  Assert.Equal<Map<string, int>>(Map.ofList [ "b", 20 ], view())

[<Fact>]
let ``same-element add and remove in one batch net to nothing downstream``() =
  // KIMI 2: producers must deliver net deltas (one op per element per
  // batch); consumers apply the buffers order-free.
  // xor: both sides gain the element in one transaction -> absent before
  // and after, so nothing may reach the downstream map.
  let l = CSet.empty<int>
  let r = CSet.empty<int>
  let x = ASet.xor (CSet.value l) (CSet.value r)
  let mapped = ASet.map (fun y -> y * 2) x

  Transaction.run(fun () ->
    CSet.add 1 l
    CSet.add 1 r)

  Assert.Equal<Set<int>>(Set.empty, Set.ofSeq(ASet.force mapped))
  // custom: the compute adds and removes the same element every poll
  // (adds apply first by convention) -> net nothing.
  let c =
    ASet.custom(fun _ d ->
      d.Add 5
      d.Remove 5)

  let cm = ASet.map string c
  Assert.Equal<Set<string>>(Set.empty, Set.ofSeq(ASet.force cm))

[<Fact>]
let ``AList filter keeps positions after an update that changes survival``() =
  // KIMI 3: the Update branch shifted inputPositions as if the input had
  // grown or shrunk; later removes were then ignored.
  let src = CList.ofList [ 1; 2; 3; 4 ]
  let evens = AList.filter (fun x -> x % 2 = 0) (CList.value src)
  Assert.Equal<int list>([ 2; 4 ], AList.toList evens)
  // 2 -> 5 (stops surviving): the tail keeps its input positions.
  CList.updateAt 1 5 src
  Assert.Equal<int list>([ 4 ], AList.toList evens)
  // Remove the element at input position 3 (the 4): must be seen.
  CList.removeAt 3 src
  Assert.Empty(AList.toList evens)

[<Fact>]
let ``ChangeableList transaction update equality checks the replay state``() =
  // KIMI 5: an update that restores the committed value of a position
  // touched earlier in the batch must still journal.
  let s = CList.ofList [ 2; 2; 3 ]

  Transaction.run(fun () ->
    CList.updateAt 0 5 s
    CList.updateAt 0 2 s)

  Assert.Equal<int list>([ 2; 2; 3 ], AList.toList(CList.value s))

[<Fact>]
let ``ReduceNode recomputes when a write lands mid-reduce``() =
  // GLM 1 / KIMI 6: a write from user code inside the reduce callback moves
  // the write generation; the node caches its verdict at the start
  // generation, so the next read version-checks and sees the moved
  // dependency version instead of serving the stale value.
  let a = CVal.create 1
  let b = CVal.create 100
  let mutable n = 0
  // Compute 2 writes a while processing it (a was already read): the
  // computed value is stale for a.
  let r =
    AVal.reduce
      0
      (fun acc x ->
        n <- n + 1

        if n = 3 then
          CVal.set 10 a

        acc + x)
      [| CVal.value a; CVal.value b |]

  // Compute 1: 1 + 100.
  Assert.Equal(101, AVal.getValue r)
  // A write to b forces a recompute; compute 2 writes a mid-compute (after
  // a was read), so the result (1 + 200) is stale for a ...
  CVal.set 200 b
  Assert.Equal(201, AVal.getValue r)
  // ... and the next read must recompute and see a = 10: 10 + 200.
  Assert.Equal(210, AVal.getValue r)

[<Fact>]
let ``node initialization retries after a throwing mapping``() =
  // KIMI 7: initialized <- true ran before the work; a throw left the node
  // permanently half-initialized (later reads returned partial state).
  let s = CSet.ofSeq [ 1; 2 ]
  let mutable calls = 0

  let mapped =
    ASet.map
      (fun x ->
        calls <- calls + 1

        if calls = 1 then
          failwith "boom"

        x * 10)
      (CSet.value s)

  Assert.Throws<System.Exception>(fun () -> ASet.force mapped |> ignore)
  |> ignore

  Assert.Equal<Set<_>>(Set.ofList [ 10; 20 ], Set.ofSeq(ASet.force mapped))

[<Fact>]
let ``reduction drain does not re-apply consumed entries after a throw``() =
  // KIMI 8: a throwing mapping must not leave consumed journal entries for
  // the next drain (double subtract corrupts the reduction). The entry that
  // threw survives, so the reduction still converges after the mapping is
  // fixed.
  let s = CSet.ofSeq [ 1; 2 ]
  let mutable fail = false

  let sum =
    ASet.sumBy (fun x -> if fail then failwith "boom" else x) (CSet.value s)

  Assert.Equal(3, AVal.getValue sum)
  fail <- true

  Transaction.run(fun () ->
    CSet.remove 1 s
    CSet.add 3 s)

  Assert.Throws<System.Exception>(fun () -> AVal.getValue sum |> ignore)
  |> ignore

  fail <- false
  // {2,3} -> sum 5: the consumed Rem 1 is not re-applied (which would give
  // 5 - 1 = 4 with a double subtract... the exact hazard: without compaction
  // the Rem would apply twice, 5 - 1 - 1 = 3, and the Add twice, 3 + 3 = 6).
  Assert.Equal(5, AVal.getValue sum)

[<Fact>]
let ``ChangeableSet.Set supersedes the whole batch``() =
  // KIMI 16: Set meant "replace first, later journaled ops apply" for set
  // and map but "supersedes the whole batch" for list. Unified on the list
  // semantic (docs/ALIST-DESIGN.md §3.3).
  let s = CSet.ofSeq [ 1 ]

  Transaction.run(fun () ->
    CSet.set (Set.ofList [ 2; 3 ]) s
    CSet.add 4 s)

  Assert.Equal<Set<_>>(
    Set.ofList [ 2; 3 ],
    s |> CSet.value |> ASet.force |> Set.ofSeq
  )

  let m = CMap.ofSeq [ "a", 1 ]

  Transaction.run(fun () ->
    CMap.set (Map.ofList [ "b", 2 ]) m
    CMap.addOrUpdate "c" 3 m)

  let view() =
    Map.ofSeq(seq { for KeyValue(k, v) in AMap.force(CMap.value m) -> k, v })

  Assert.Equal<Map<string, int>>(Map.ofList [ "b", 2 ], view())

[<Fact>]
let ``changeable dispose detaches sinks``() =
  // KIMI 14: Dispose was a no-op on all changeables; `use` silently leaked
  // the derived graph.
  let s = CSet.ofSeq [ 1; 2 ]
  let mapped = ASet.map (fun x -> x * 10) (CSet.value s)
  ASet.force mapped |> ignore
  Assert.Equal(1, s.SinkCount)
  (s :> IDisposable).Dispose()
  Assert.Equal(0, s.SinkCount)

[<Fact>]
let ``dropped derived collection nodes are collected``() =
  // Weak sink references (docs/2026-08-05-DESIGN-WEAK-SINK-REFERENCES.md):
  // a derived node that was read (sink registered) and then dropped is
  // collectible; the source keeps only a weak entry. The node is created in
  // a separate function so the JIT cannot extend its lifetime.
  let src = CSet.ofSeq [ 1; 2 ]

  let makeWeak() =
    let mapped = ASet.map (fun x -> x * 10) (CSet.value src)
    ASet.force mapped |> ignore // first read registers the sink
    WeakReference(mapped)

  let weak = makeWeak()
  GC.Collect()
  GC.WaitForPendingFinalizers()
  GC.Collect()
  Assert.False(weak.IsAlive)
  // The source still works, and a live sink still receives deltas (delivery
  // skips and compacts the dead entry).
  CSet.add 3 src
  let m2 = ASet.map (fun x -> x * 2) (CSet.value src)
  Assert.Equal<Set<_>>(Set.ofList [ 2; 4; 6 ], Set.ofSeq(ASet.force m2))

[<Fact>]
let ``a dropped derived node is collected and its sink entry dies``() =
  // Weak sinks: a derived node the user dropped (and never reads) is
  // collected, and delivery skips its dead entry. Created in a separate
  // function so the JIT cannot extend lifetimes.
  let src = CSet.ofSeq [ 1 ]

  let makeWeak() =
    let mapped = ASet.map (fun x -> x * 10) (CSet.value src)
    ASet.force mapped |> ignore // first read: registers the sink
    WeakReference(mapped)

  let mappedWeak = makeWeak()
  GC.Collect()
  GC.WaitForPendingFinalizers()
  GC.Collect()
  Assert.False(mappedWeak.IsAlive)
  CSet.add 2 src // the delivery compacts the dead entry
  Assert.Equal(0, src.SinkCount)

// =============================================================================
// Extension points (MAPA-DESIGN §1): ofExternal ×4, AList.custom.
// Public API level only. Semantics per §1.1: the snapshot runs at most once
// per invalidate, on the next read; not invalidated → zero cost.
// =============================================================================

[<Fact>]
let ``AVal ofExternal: first read takes the snapshot; invalidate re-reads``() =
  let mutable current = 0
  let value, invalidate = AVal.ofExternal(fun () -> current)

  Assert.Equal(0, AVal.getValue value)

  current <- 42
  Assert.Equal(0, AVal.getValue value) // not invalidated: cached
  invalidate()
  Assert.Equal(42, AVal.getValue value)

[<Fact>]
let ``AVal ofExternal: reads without invalidate do not re-run the function``() =
  let mutable calls = 0

  let value, _ =
    AVal.ofExternal(fun () ->
      calls <- calls + 1
      calls)

  AVal.getValue value |> ignore
  AVal.getValue value |> ignore
  Assert.Equal(1, calls)

[<Fact>]
let ``AVal ofExternal: derived values recompute after invalidate``() =
  let mutable current = 1
  let value, invalidate = AVal.ofExternal(fun () -> current)
  let doubled = AVal.map (fun v -> v * 2) value

  Assert.Equal(2, AVal.getValue doubled)

  current <- 5
  invalidate()
  Assert.Equal(10, AVal.getValue doubled)

[<Fact>]
let ``AVal ofExternal: foreign-thread invalidate applies at the next read``() =
  let mutable current = 1
  let value, invalidate = AVal.ofExternal(fun () -> current)

  Assert.Equal(1, AVal.getValue value)

  current <- 7
  Task.Run(fun () -> invalidate()).Wait()
  Assert.Equal(7, AVal.getValue value) // the post drains at the next graph op

[<Fact>]
let ``ASet mapA consumes an AVal ofExternal element source``() =
  // The invalidate moves the write generation, so the *A scan gate fires.
  let s = CSet.ofSeq [ 1; 2; 3 ]
  let mutable current = 0
  let ext, invalidate = AVal.ofExternal(fun () -> current)
  let mapped = s |> ASet.mapA(fun _ -> ext)

  Assert.Equal<Set<int>>(Set.ofList [ 0 ], ASet.toSet mapped)

  current <- 9
  invalidate()
  Assert.Equal<Set<int>>(Set.ofList [ 9 ], ASet.toSet mapped)

[<Fact>]
let ``ASet ofExternal: materializes on first read and diffs on invalidate``() =
  let mutable current = HashSet<int>([ 1; 2; 3 ])
  let s, invalidate = ASet.ofExternal(fun () -> current :> IReadOnlySet<int>)

  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 3 ], ASet.toSet s)

  current <- HashSet<int>([ 2; 3; 4 ])
  invalidate()
  Assert.Equal<Set<int>>(Set.ofList [ 2; 3; 4 ], ASet.toSet s)

  invalidate() // unchanged snapshot: no visible change
  Assert.Equal<Set<int>>(Set.ofList [ 2; 3; 4 ], ASet.toSet s)

[<Fact>]
let ``ASet ofExternal: reads without invalidate do not re-run the snapshot``() =
  let mutable calls = 0

  let s, _ =
    ASet.ofExternal(fun () ->
      calls <- calls + 1
      HashSet<int>([ 1 ]) :> IReadOnlySet<int>)

  ASet.force s |> ignore
  ASet.force s |> ignore
  Assert.Equal(1, calls)

[<Fact>]
let ``AMap ofExternal: materializes on first read and diffs on invalidate``() =
  let mutable current = Dictionary<int, string>()
  current[1] <- "a"
  current[2] <- "b"

  let m, invalidate =
    AMap.ofExternal(fun () -> current :> IReadOnlyDictionary<int, string>)

  Assert.Equal<Map<int, string>>(Map.ofList [ 1, "a"; 2, "b" ], AMap.toMap m)

  current[1] <- "a" // unchanged value: elided
  current[3] <- "c"
  invalidate()

  Assert.Equal<Map<int, string>>(
    Map.ofList [ 1, "a"; 2, "b"; 3, "c" ],
    AMap.toMap m
  )

  current.Remove 2 |> ignore
  invalidate()
  Assert.Equal<Map<int, string>>(Map.ofList [ 1, "a"; 3, "c" ], AMap.toMap m)

[<Fact>]
let ``AList ofExternal: materializes on first read and diffs positionally``() =
  let mutable current = ResizeArray [ 1; 2; 3 ]
  let l, invalidate = AList.ofExternal(fun () -> current :> IReadOnlyList<int>)

  Assert.Equal<int[]>([| 1; 2; 3 |], AList.toArray l)

  current.RemoveAt 0 // [ 2; 3 ]
  invalidate()
  Assert.Equal<int[]>([| 2; 3 |], AList.toArray l)

  current.Insert(1, 9) // [ 2; 9; 3 ]
  invalidate()
  Assert.Equal<int[]>([| 2; 9; 3 |], AList.toArray l)

  current[0] <- 7 // [ 7; 9; 3 ]
  invalidate()
  Assert.Equal<int[]>([| 7; 9; 3 |], AList.toArray l)

[<Fact>]
let ``AList ofExternal: reads without invalidate do not re-run the snapshot``
  ()
  =
  let mutable calls = 0

  let l, _ =
    AList.ofExternal(fun () ->
      calls <- calls + 1
      ResizeArray [ 1 ] :> IReadOnlyList<int>)

  AList.force l |> ignore
  AList.force l |> ignore
  Assert.Equal(1, calls)

[<Fact>]
let ``AList custom: the compute drains an event queue into the list``() =
  let events = ResizeArray<int>()

  let list =
    AList.custom(fun view (delta: ListDeltaBuilder<int>) ->
      for i in 0 .. events.Count - 1 do
        delta.Insert(view.Count + i, events[i])

      events.Clear())

  Assert.Equal<int[]>([||], AList.toArray list)

  events.Add 1
  events.Add 2
  Assert.Equal<int[]>([| 1; 2 |], AList.toArray list) // one poll drains the queue

  events.Add 3
  Assert.Equal<int[]>([| 1; 2; 3 |], AList.toArray list)

[<Fact>]
let ``ofExternal: reads without invalidate allocate nothing``() =
  let mutable current = 1
  let v, invalidateV = AVal.ofExternal(fun () -> current)

  let s, invalidateS =
    ASet.ofExternal(fun () -> HashSet<int>([ 1 ]) :> IReadOnlySet<int>)

  let map = Dictionary<int, string>()
  map[1] <- "a"

  let m, invalidateM =
    AMap.ofExternal(fun () -> map :> IReadOnlyDictionary<int, string>)

  let l, invalidateL =
    AList.ofExternal(fun () -> ResizeArray [ 1 ] :> IReadOnlyList<int>)

  // Settle: first reads, an invalidate round, settled reads.
  AVal.getValue v |> ignore
  ASet.getValue s |> ignore
  AMap.getValue m |> ignore
  AList.getValue l |> ignore
  invalidateV()
  invalidateS()
  invalidateM()
  invalidateL()
  AVal.getValue v |> ignore
  ASet.getValue s |> ignore
  AMap.getValue m |> ignore
  AList.getValue l |> ignore

  let before = GC.GetAllocatedBytesForCurrentThread()

  for _ in 1..1000 do
    AVal.getValue v |> ignore
    ASet.getValue s |> ignore
    AMap.getValue m |> ignore
    AList.getValue l |> ignore

  let allocated = GC.GetAllocatedBytesForCurrentThread() - before
  Assert.Equal(0L, allocated)

// =============================================================================
// Bring list — ASet group (docs/2026-08-05-FDA-API-GAPS.md §3): range,
// bind2/bind3, collect', mapUse, average/averageBy.
// =============================================================================

[<Fact>]
let ``ASet range follows its bounds``() =
  let lo = CVal.create 1
  let hi = CVal.create 3
  let r = ASet.range (CVal.value lo) (CVal.value hi)

  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 3 ], ASet.toSet r)

  CVal.set 0 lo
  Assert.Equal<Set<int>>(Set.ofList [ 0; 1; 2; 3 ], ASet.toSet r)

  CVal.set 2 hi
  Assert.Equal<Set<int>>(Set.ofList [ 0; 1; 2 ], ASet.toSet r)

[<Fact>]
let ``ASet bind2 and bind3 remap when any input changes``() =
  let a = CVal.create 0
  let b = CVal.create 0
  let buckets = [| CSet.empty<int>; CSet.empty<int>; CSet.empty<int> |]

  let combined =
    ASet.bind2 (fun av bv -> buckets[av + bv]) (CVal.value a) (CVal.value b)

  CSet.add 1 (buckets[0])
  Assert.Equal<Set<int>>(Set.ofList [ 1 ], ASet.toSet combined)

  CVal.set 1 b // a+b = 1: bucket 1, still empty
  Assert.Equal<Set<int>>(Set.empty, ASet.toSet combined)

  CSet.add 2 (buckets[1])
  Assert.Equal<Set<int>>(Set.ofList [ 2 ], ASet.toSet combined)

  CVal.set 1 a // a+b = 2: bucket 2, empty
  Assert.Equal<Set<int>>(Set.empty, ASet.toSet combined)

  let c = CVal.create 0

  let combined3 =
    ASet.bind3
      (fun av bv cv -> buckets[av + bv + cv])
      (CVal.value a)
      (CVal.value b)
      (CVal.value c)

  Assert.Equal<Set<int>>(Set.empty, ASet.toSet combined3)

  CVal.set 0 a
  CVal.set 0 b
  CVal.set 1 c // a+b+c = 1: bucket 1 holds { 2 }
  Assert.Equal<Set<int>>(Set.ofList [ 2 ], ASet.toSet combined3)

[<Fact>]
let ``ASet collect' expands statically``() =
  let s = CSet.ofSeq [ 1; 2; 3 ]
  let expanded = s |> ASet.collect'(fun v -> [ v; v * 10 ])

  Assert.Equal<Set<int>>(
    Set.ofList [ 1; 10; 2; 20; 3; 30 ],
    ASet.toSet expanded
  )

  CSet.remove 1 s
  Assert.Equal<Set<int>>(Set.ofList [ 2; 20; 3; 30 ], ASet.toSet expanded)

[<Fact>]
let ``ASet mapUse disposes on removal and on dispose``() =
  let input = CSet.ofSeq [ 1; 2; 3; 4 ]
  let refCount = ref 0

  let newDisposable() =
    incr refCount

    { new IDisposable with
        member _.Dispose() = decr refCount
    }

  let disp, set = input |> ASet.mapUse(fun v -> newDisposable())

  Assert.Equal(0, !refCount) // mapped lazily: nothing before the first read

  ASet.force set |> ignore
  Assert.Equal(4, !refCount)

  CSet.remove 1 input
  ASet.force set |> ignore
  Assert.Equal(3, !refCount)

  CSet.add 7 input
  ASet.force set |> ignore
  Assert.Equal(4, !refCount)

  disp.Dispose()
  Assert.Equal(0, !refCount)

[<Fact>]
let ``ASet mapUse refcounts duplicate mapped values``() =
  let input = CSet.ofSeq [ 1; 2 ]
  let mutable disposes = 0

  let shared =
    { new IDisposable with
        member _.Dispose() = disposes <- disposes + 1
    }

  let disp, set = input |> ASet.mapUse(fun _ -> shared)

  ASet.force set |> ignore
  Assert.Equal(1, (ASet.force set).Count) // both elements map to one output value
  Assert.Equal(0, disposes)

  CSet.remove 1 input
  ASet.force set |> ignore
  Assert.Equal(1, (ASet.force set).Count) // one occurrence remains
  Assert.Equal(0, disposes)

  CSet.remove 2 input
  ASet.force set |> ignore
  Assert.Equal(0, (ASet.force set).Count)
  Assert.Equal(1, disposes) // last occurrence left: disposed exactly once

[<Fact>]
let ``ASet average and averageBy``() =
  let s = CSet.ofSeq [ 1.0; 2.0; 3.0 ]
  let source = CSet.value s

  Assert.Equal(2.0, source |> ASet.average |> AVal.getValue)
  Assert.Equal(4.0, source |> ASet.averageBy(fun v -> v * 2.0) |> AVal.getValue)

  CSet.add 5.0 s
  Assert.Equal(2.75, source |> ASet.average |> AVal.getValue)

// =============================================================================
// Bring list — AMap group (docs/2026-08-05-FDA-API-GAPS.md §4): map',
// choose', filter', intersectV, bind2/bind3, mapUse, foldHalfGroup,
// sumBy/averageBy, toASet pairs + keys.
// =============================================================================

[<Fact>]
let ``AMap mapV and filterV are value-only``() =
  let m = CMap.ofSeq [ 1, 2; 2, 4; 3, 5 ]

  let doubled = AMap.mapV (fun v -> v * 2) (CMap.value m)

  Assert.Equal<Map<int, int>>(
    Map.ofList [ 1, 4; 2, 8; 3, 10 ],
    AMap.toMap doubled
  )

  let big = AMap.filterV (fun v -> v > 3) (CMap.value m)
  Assert.Equal<Map<int, int>>(Map.ofList [ 2, 4; 3, 5 ], AMap.toMap big)

[<Fact>]
let ``AMap intersectV pairs the values as struct pairs``() =
  let a = CMap.ofSeq [ 1, "a"; 2, "b"; 3, "c" ]
  let b = CMap.ofSeq [ 2, 20; 3, 30; 4, 40 ]

  let paired = AMap.intersectV (CMap.value a) (CMap.value b)

  let expected = Map.ofList [ 2, struct ("b", 20); 3, struct ("c", 30) ]

  Assert.Equal<Map<int, struct (string * int)>>(expected, AMap.toMap paired)

[<Fact>]
let ``AMap bind2 and bind3 remap when any input changes``() =
  let a = CVal.create 0
  let b = CVal.create 0

  let tables = [|
    CMap.empty<int, string>
    CMap.empty<int, string>
    CMap.empty<int, string>
  |]

  let combined =
    AMap.bind2 (fun av bv -> tables[av + bv]) (CVal.value a) (CVal.value b)

  CMap.addOrUpdate 1 "x" (tables[0])
  Assert.Equal<Map<int, string>>(Map.ofList [ 1, "x" ], AMap.toMap combined)

  CVal.set 1 b // a+b = 1: table 1, still empty
  Assert.Equal<Map<int, string>>(Map.empty, AMap.toMap combined)

  CMap.addOrUpdate 2 "y" (tables[1])
  Assert.Equal<Map<int, string>>(Map.ofList [ 2, "y" ], AMap.toMap combined)

  CVal.set 1 a // a+b = 2: table 2, empty
  Assert.Equal<Map<int, string>>(Map.empty, AMap.toMap combined)

  let c = CVal.create 0

  let combined3 =
    AMap.bind3
      (fun av bv cv -> tables[av + bv + cv])
      (CVal.value a)
      (CVal.value b)
      (CVal.value c)

  Assert.Equal<Map<int, string>>(Map.empty, AMap.toMap combined3)

  CVal.set 0 a
  CVal.set 0 b
  CVal.set 1 c // a+b+c = 1: table 1 holds { 2 -> y }
  Assert.Equal<Map<int, string>>(Map.ofList [ 2, "y" ], AMap.toMap combined3)

[<Fact>]
let ``AMap mapUse disposes on removal and on dispose``() =
  let input = CMap.ofSeq [ 1, "a"; 2, "b"; 3, "c"; 4, "d" ]
  let refCount = ref 0

  let newDisposable _ =
    incr refCount

    { new IDisposable with
        member _.Dispose() = decr refCount
    }

  let disp, mapValue = input |> AMap.mapUse(fun _ _ -> newDisposable())

  Assert.Equal(0, !refCount) // mapped lazily: nothing before the first read

  AMap.force mapValue |> ignore
  Assert.Equal(4, !refCount)

  CMap.remove 1 input
  AMap.force mapValue |> ignore
  Assert.Equal(3, !refCount)

  CMap.addOrUpdate 7 "g" input
  AMap.force mapValue |> ignore
  Assert.Equal(4, !refCount)

  disp.Dispose()
  Assert.Equal(0, !refCount)

[<Fact>]
let ``AMap foldHalfGroup, sumBy and averageBy``() =
  let m = CMap.ofSeq [ 1, 10; 2, 20; 3, 30 ]

  // Fully invertible: removals subtract without a full recompute.
  let sum =
    AMap.foldHalfGroup
      (fun s k v -> s + v)
      (fun s k v -> ValueSome(s - v))
      0
      (CMap.value m)

  Assert.Equal(60, AVal.getValue sum)

  CMap.remove 2 m
  Assert.Equal(40, AVal.getValue sum)

  CMap.addOrUpdate 4 40 m
  Assert.Equal(80, AVal.getValue sum)

  // Non-invertible trySubtract: removals recompute the whole fold; the
  // result stays correct.
  let always =
    AMap.foldHalfGroup
      (fun s k v -> s + v)
      (fun _ _ _ -> ValueNone)
      0
      (CMap.value m)

  Assert.Equal(80, AVal.getValue always)

  CMap.remove 4 m
  Assert.Equal(40, AVal.getValue always)

  let sums = AMap.sumBy (fun k v -> v) (CMap.value m)
  Assert.Equal(40, AVal.getValue sums)

  let mf = CMap.ofSeq [ 1, 1.0; 2, 2.0 ]
  let avg = AMap.averageBy (fun k v -> v) (CMap.value mf)
  Assert.Equal(1.5, AVal.getValue avg)

[<Fact>]
let ``AMap toASet returns pairs and keys returns keys``() =
  let m = CMap.ofSeq [ 1, "a"; 2, "b" ]

  let pairs = AMap.toASet(CMap.value m)
  let keys = AMap.keys(CMap.value m)

  Assert.Equal<Set<struct (int * string)>>(
    Set.ofList [ struct (1, "a"); struct (2, "b") ],
    ASet.toSet pairs
  )

  Assert.Equal<Set<int>>(Set.ofList [ 1; 2 ], ASet.toSet keys)

  CMap.remove 1 m

  Assert.Equal<Set<struct (int * string)>>(
    Set.ofList [ struct (2, "b") ],
    ASet.toSet pairs
  )

  Assert.Equal<Set<int>>(Set.ofList [ 2 ], ASet.toSet keys)

// =============================================================================
// Bring list — AList simple group (gap sheet §5.1, §5.5, §5.6, §5.7):
// mapi/choosei/filteri, indexed, ofAVal, toAVal, range, init, tryAt/tryGet/
// tryFirst/tryLast, rev, sort family, pairwise, mapUse/mapUsei.
// =============================================================================

[<Fact>]
let ``AList mapi choosei and filteri pass the input position``() =
  let l = CList.ofSeq [ 10; 20; 30 ]

  let withPos = l |> AList.mapi(fun i v -> (i, v))
  Assert.Equal<(int * int)[]>([| 0, 10; 1, 20; 2, 30 |], AList.toArray withPos)

  let chosen =
    l |> AList.choosei(fun i v -> if i % 2 = 0 then Some(v * 10) else None)

  Assert.Equal<int[]>([| 100; 300 |], AList.toArray chosen)

  let filtered = l |> AList.filteri(fun i v -> i = 1 || v = 30)
  Assert.Equal<int[]>([| 20; 30 |], AList.toArray filtered)

  CList.insertAt 0 5 l // [ 5; 10; 20; 30 ]
  // Mapping-time positions stick (the mapiA convention): shifted elements
  // keep the position the mapping saw (FDA stable-Index equivalent).
  Assert.Equal<(int * int)[]>(
    [| 0, 5; 0, 10; 1, 20; 2, 30 |],
    AList.toArray withPos
  )

  let chosen2 =
    l |> AList.choosei(fun i v -> if i % 2 = 0 then Some(v * 10) else None)

  Assert.Equal<int[]>([| 50; 200 |], AList.toArray chosen2) // 5@0, 20@2

[<Fact>]
let ``AList indexed pairs elements with their positions``() =
  let l = CList.ofSeq [ "a"; "b" ]
  let indexed = AList.indexed(CList.value l)

  Assert.Equal<struct (int * string)[]>(
    [| struct (0, "a"); struct (1, "b") |],
    AList.toArray indexed
  )

  CList.insertAt 1 "x" l
  // Mapping-time positions stick (the mapiA convention).
  Assert.Equal<struct (int * string)[]>(
    [| struct (0, "a"); struct (1, "x"); struct (1, "b") |],
    AList.toArray indexed
  )

[<Fact>]
let ``AList ofAVal rebuilds on value change``() =
  let v = CVal.create [| 1; 2; 3 |]
  let l = AList.ofAVal(CVal.value v)

  Assert.Equal<int[]>([| 1; 2; 3 |], AList.toArray l)

  CVal.set [| 3; 4 |] v
  Assert.Equal<int[]>([| 3; 4 |], AList.toArray l)

[<Fact>]
let ``AList toAVal materializes snapshots``() =
  let l = CList.ofSeq [ 1; 2 ]
  let snap = AList.toAVal(CList.value l)

  Assert.Equal<int[]>([| 1; 2 |], AVal.getValue snap)

  CList.append 3 l
  Assert.Equal<int[]>([| 1; 2; 3 |], AVal.getValue snap)

[<Fact>]
let ``AList range and init follow their inputs``() =
  let lo = CVal.create 1
  let hi = CVal.create 3
  let r = AList.range (CVal.value lo) (CVal.value hi)
  Assert.Equal<int[]>([| 1; 2; 3 |], AList.toArray r)

  CVal.set 5 hi
  Assert.Equal<int[]>([| 1; 2; 3; 4; 5 |], AList.toArray r)

  let count = CVal.create 2
  let gen = AList.init (fun i -> i * 10) (CVal.value count)
  Assert.Equal<int[]>([| 0; 10 |], AList.toArray gen)

  CVal.set 4 count
  Assert.Equal<int[]>([| 0; 10; 20; 30 |], AList.toArray gen)

[<Fact>]
let ``AList tryAt tryGet tryFirst tryLast``() =
  let l = CList.ofSeq [ 10; 20; 30 ]
  let a = AList.tryAt 1 (CList.value l)
  let g = AList.tryGet 5 (CList.value l)
  let f = AList.tryFirst(CList.value l)
  let t = AList.tryLast(CList.value l)

  Assert.Equal(ValueSome 20, AVal.getValue a)
  Assert.Equal(ValueNone, AVal.getValue g)
  Assert.Equal(ValueSome 10, AVal.getValue f)
  Assert.Equal(ValueSome 30, AVal.getValue t)

  CList.removeAt 0 l
  Assert.Equal(ValueSome 20, AVal.getValue f)
  Assert.Equal(ValueSome 30, AVal.getValue t)

[<Fact>]
let ``AList rev follows the source``() =
  let l = CList.ofSeq [ 1; 2; 3 ]
  let r = AList.rev(CList.value l)

  Assert.Equal<int[]>([| 3; 2; 1 |], AList.toArray r)

  CList.append 4 l
  Assert.Equal<int[]>([| 4; 3; 2; 1 |], AList.toArray r)

[<Fact>]
let ``AList sort family is stable``() =
  let l = CList.ofSeq [ 3; 1; 2; 1 ]
  let source = CList.value l

  Assert.Equal<int[]>([| 1; 1; 2; 3 |], source |> AList.sort |> AList.toArray)

  Assert.Equal<int[]>(
    [| 3; 2; 1; 1 |],
    source |> AList.sortDescending |> AList.toArray
  )

  Assert.Equal<int[]>(
    [| 1; 1; 2; 3 |],
    source |> AList.sortBy(fun v -> v) |> AList.toArray
  )

  Assert.Equal<int[]>(
    [| 1; 1; 2; 3 |],
    source |> AList.sortWith(fun a b -> compare a b) |> AList.toArray
  )

  // sortByi: the projection sees the input position at poll time; stable.
  let bySum = l |> CList.value |> AList.sortByi(fun i v -> i + v)
  Assert.Equal<int[]>([| 1; 3; 2; 1 |], AList.toArray bySum) // keys: 3, 2, 4, 4

  let l2 = CList.ofSeq [ 1; 2; 3 ]
  let byDesc = l2 |> CList.value |> AList.sortByDescending(fun v -> v)
  Assert.Equal<int[]>([| 3; 2; 1 |], AList.toArray byDesc)

  let byDesci = l2 |> CList.value |> AList.sortByDescendingi(fun i v -> i + v)
  Assert.Equal<int[]>([| 3; 2; 1 |], AList.toArray byDesci) // keys: 1, 3, 5

[<Fact>]
let ``AList pairwise and pairwiseCyclic``() =
  let l = CList.ofSeq [ 1; 2; 3 ]

  let p = AList.pairwise(CList.value l)

  Assert.Equal<struct (int * int)[]>(
    [| struct (1, 2); struct (2, 3) |],
    AList.toArray p
  )

  let pc = AList.pairwiseCyclic(CList.value l)

  Assert.Equal<struct (int * int)[]>(
    [| struct (1, 2); struct (2, 3); struct (3, 1) |],
    AList.toArray pc
  )

  CList.insertAt 1 9 l // [ 1; 9; 2; 3 ]

  Assert.Equal<struct (int * int)[]>(
    [| struct (1, 9); struct (9, 2); struct (2, 3) |],
    AList.toArray p
  )

[<Fact>]
let ``AList mapUse disposes on removal and on dispose``() =
  let input = CList.ofSeq [ 1; 2; 3 ]
  let refCount = ref 0

  let newDisposable _ =
    incr refCount

    { new IDisposable with
        member _.Dispose() = decr refCount
    }

  let disp, list = input |> AList.mapUse newDisposable

  Assert.Equal(0, !refCount) // mapped lazily: nothing before the first read

  AList.force list |> ignore
  Assert.Equal(3, !refCount)

  CList.removeAt 0 input
  AList.force list |> ignore
  Assert.Equal(2, !refCount)

  CList.append 7 input
  AList.force list |> ignore
  Assert.Equal(3, !refCount)

  disp.Dispose()
  Assert.Equal(0, !refCount)

[<Fact>]
let ``AList mapUsei disposes the replaced value on update``() =
  let input = CList.ofSeq [ 10 ]
  let mutable disposes = 0

  let disp, list =
    input
    |> AList.mapUsei(fun i v ->
      { new IDisposable with
          member _.Dispose() = disposes <- disposes + 1
      })

  AList.force list |> ignore

  CList.updateAt 0 20 input // the mapped value at position 0 is replaced
  AList.force list |> ignore
  Assert.Equal(1, disposes)

  disp.Dispose()
  Assert.Equal(2, disposes)

// =============================================================================
// Bring list — AList bind/slices/concat group (gap sheet §5.2, §5.4, §5.1.5).
// =============================================================================

[<Fact>]
let ``AList bind follows the value and the inner list``() =
  let selected = CVal.create 0
  let lists = [| CList.ofSeq [ 1; 2 ]; CList.ofSeq [ 3 ] |]

  let bound = AList.bind (fun i -> CList.value lists[i]) (CVal.value selected)

  Assert.Equal<int[]>([| 1; 2 |], AList.toArray bound)

  CList.append 9 (lists[0]) // the bound inner's changes propagate
  Assert.Equal<int[]>([| 1; 2; 9 |], AList.toArray bound)

  CVal.set 1 selected // swap to the second inner
  Assert.Equal<int[]>([| 3 |], AList.toArray bound)

  CList.append 8 (lists[0]) // the unbound inner no longer leaks
  Assert.Equal<int[]>([| 3 |], AList.toArray bound)

[<Fact>]
let ``AList bind2 and bind3 remap when any input changes``() =
  let a = CVal.create 0
  let b = CVal.create 0
  let lists = [| CList.ofSeq [ 1 ]; CList.ofSeq [ 2 ]; CList.ofSeq [ 3 ] |]

  let combined =
    AList.bind2
      (fun av bv -> CList.value lists[av + bv])
      (CVal.value a)
      (CVal.value b)

  Assert.Equal<int[]>([| 1 |], AList.toArray combined)

  CVal.set 1 b
  Assert.Equal<int[]>([| 2 |], AList.toArray combined)

  CVal.set 1 a
  Assert.Equal<int[]>([| 3 |], AList.toArray combined)

  let c = CVal.create 0

  let combined3 =
    AList.bind3
      (fun av bv cv -> CList.value lists[av + bv + cv])
      (CVal.value a)
      (CVal.value b)
      (CVal.value c)

  Assert.Equal<int[]>([| 3 |], AList.toArray combined3)

  CVal.set 0 c // a+b+c = 2: the third list
  CVal.set 0 a
  Assert.Equal<int[]>([| 2 |], AList.toArray combined3)

[<Fact>]
let ``AList concat concatenates the inner lists``() =
  let a = CList.ofSeq [ 1; 2 ]
  let b = CList.ofSeq [ 3 ]
  let c = CList.ofSeq [ 4; 5 ]

  let all = AList.concat [ CList.value a; CList.value b; CList.value c ]

  Assert.Equal<int[]>([| 1; 2; 3; 4; 5 |], AList.toArray all)

  CList.append 9 b
  Assert.Equal<int[]>([| 1; 2; 3; 9; 4; 5 |], AList.toArray all)

  CList.append 6 c
  Assert.Equal<int[]>([| 1; 2; 3; 9; 4; 5; 6 |], AList.toArray all)

[<Fact>]
let ``AList take skip and sub follow adaptive bounds``() =
  let l = CList.ofSeq [ 0; 1; 2; 3; 4 ]

  let t = AList.take 2 (CList.value l)
  Assert.Equal<int[]>([| 0; 1 |], AList.toArray t)

  let s = AList.skip 2 (CList.value l)
  Assert.Equal<int[]>([| 2; 3; 4 |], AList.toArray s)

  let sub = AList.sub 1 2 (CList.value l)
  Assert.Equal<int[]>([| 1; 2 |], AList.toArray sub)

  let count = CVal.create 2
  let ta = AList.takeA (CVal.value count) (CList.value l)
  Assert.Equal<int[]>([| 0; 1 |], AList.toArray ta)

  CVal.set 4 count
  Assert.Equal<int[]>([| 0; 1; 2; 3 |], AList.toArray ta)

  let offset = CVal.create 1
  let c = CVal.create 2
  let sa = AList.subA (CVal.value offset) (CVal.value c) (CList.value l)
  Assert.Equal<int[]>([| 1; 2 |], AList.toArray sa)

  CVal.set 3 offset
  Assert.Equal<int[]>([| 3; 4 |], AList.toArray sa)

  CVal.set 1 c
  Assert.Equal<int[]>([| 3 |], AList.toArray sa)

// =============================================================================
// Bring list — AList reductions group (gap sheet §5.8): reduce/reduceBy,
// fold/foldGroup/foldHalfGroup, forall/exists, tryMin/tryMax, sum/sumBy,
// average/averageBy, countBy.
// =============================================================================

[<Fact>]
let ``AList reduce and reduceBy follow the deltas``() =
  let l = CList.ofSeq [ 1; 2; 3 ]

  let sum = AList.sum(CList.value l)
  Assert.Equal(6, AVal.getValue sum)

  let sums = AList.sumBy (fun v -> v * 10) (CList.value l)
  Assert.Equal(60, AVal.getValue sums)

  CList.append 4 l
  Assert.Equal(10, AVal.getValue sum)
  Assert.Equal(100, AVal.getValue sums)

  CList.removeAt 0 l // [ 2; 3; 4 ]
  Assert.Equal(9, AVal.getValue sum)
  Assert.Equal(90, AVal.getValue sums)

  CList.updateAt 1 30 l // [ 2; 30; 4 ]
  Assert.Equal(36, AVal.getValue sum)

  CList.updateAt 1 30 l // no-op update: no rebuild, same value
  Assert.Equal(36, AVal.getValue sum)

[<Fact>]
let ``AList fold family``() =
  let l = CList.ofSeq [ 1; 2; 3 ]

  let f = AList.fold (fun s v -> s + v) 0 (CList.value l)
  Assert.Equal(6, AVal.getValue f)

  CList.append 4 l // fold recomputes on removals; appends add
  Assert.Equal(10, AVal.getValue f)

  CList.removeAt 0 l // fold cannot invert: full recompute
  Assert.Equal(9, AVal.getValue f)

  let l2 = CList.ofSeq [ 1; 2; 3 ]

  let g =
    AList.foldGroup (fun s v -> s + v) (fun s v -> s - v) 0 (CList.value l2)

  Assert.Equal(6, AVal.getValue g)

  CList.removeAt 1 l2 // invertible: subtract without recompute
  Assert.Equal(4, AVal.getValue g)

  let l3 = CList.ofSeq [ 1; 2; 3 ]

  let h =
    AList.foldHalfGroup
      (fun s v -> s + v)
      (fun s v -> ValueSome(s - v))
      0
      (CList.value l3)

  Assert.Equal(6, AVal.getValue h)

  CList.removeAt 0 l3
  Assert.Equal(5, AVal.getValue h)

  let l4 = CList.ofSeq [ 1; 2; 3 ]

  let nonInv =
    AList.foldHalfGroup
      (fun s v -> s + v)
      (fun _ _ -> ValueNone)
      0
      (CList.value l4)

  Assert.Equal(6, AVal.getValue nonInv)

  CList.removeAt 0 l4 // cannot invert: recompute
  Assert.Equal(5, AVal.getValue nonInv)

[<Fact>]
let ``AList exists forall and countBy``() =
  let l = CList.ofSeq [ 1; 2; 3 ]

  let hasEven = AList.exists (fun v -> v % 2 = 0) (CList.value l)
  let allPos = AList.forall (fun v -> v > 0) (CList.value l)
  let evens = AList.countBy (fun v -> v % 2 = 0) (CList.value l)

  Assert.Equal(true, AVal.getValue hasEven)
  Assert.Equal(true, AVal.getValue allPos)
  Assert.Equal(1, AVal.getValue evens)

  CList.append 4 l
  Assert.Equal(2, AVal.getValue evens)

  CList.removeAt 0 l // [ 2; 3; 4 ]: still has evens
  Assert.Equal(true, AVal.getValue hasEven)

  CList.updateAt 0 7 l // [ 7; 3; 4 ]
  Assert.Equal(true, AVal.getValue hasEven)

  CList.updateAt 2 5 l // [ 7; 3; 5 ]: no evens left
  Assert.Equal(false, AVal.getValue hasEven)
  Assert.Equal(0, AVal.getValue evens)

[<Fact>]
let ``AList tryMin tryMax sum average``() =
  let l = CList.ofSeq [ 3; 1; 2 ]
  let source = CList.value l

  Assert.Equal(ValueSome 1, source |> AList.tryMin |> AVal.getValue)
  Assert.Equal(ValueSome 3, source |> AList.tryMax |> AVal.getValue)
  Assert.Equal(6, source |> AList.sum |> AVal.getValue)

  let lf = CList.ofSeq [ 1.0; 2.0; 3.0 ]
  let sourceF = CList.value lf
  Assert.Equal(2.0, sourceF |> AList.average |> AVal.getValue)

  Assert.Equal(
    4.0,
    sourceF |> AList.averageBy(fun v -> v * 2.0) |> AVal.getValue
  )

  CList.removeAt 1 l // [ 3; 2 ]
  Assert.Equal(ValueSome 2, source |> AList.tryMin |> AVal.getValue)
  Assert.Equal(ValueSome 3, source |> AList.tryMax |> AVal.getValue)
  Assert.Equal(5, source |> AList.sum |> AVal.getValue)

  CList.removeAt 0 lf // [ 2.0; 3.0 ]
  Assert.Equal(2.5, sourceF |> AList.average |> AVal.getValue)

  CList.removeAt 0 l
  CList.removeAt 0 l // empty
  Assert.Equal(ValueNone, source |> AList.tryMin |> AVal.getValue)
  Assert.Equal(ValueNone, source |> AList.tryMax |> AVal.getValue)
  Assert.Equal(0, source |> AList.sum |> AVal.getValue)

// =============================================================================
// Bring list — Changeables group (gap sheet §6): cset UpdateTo/Perform/
// UnionWith/ExceptWith/IntersectWith; cmap ContainsKey/TryGetValue/Item/
// UpdateTo/Perform/Clear; clist UpdateTo/Perform/AddRange.
// =============================================================================

[<Fact>]
let ``CSet updateTo and perform``() =
  let s = CSet.ofSeq [ 1; 2; 3 ]

  Assert.Equal(true, CSet.updateTo (seq [ 2; 3; 4 ]) s)
  Assert.Equal<Set<int>>(Set.ofList [ 2; 3; 4 ], CSet.toSet s)

  Assert.Equal(false, CSet.updateTo (seq [ 2; 3; 4 ]) s) // equal: no-op
  Assert.Equal<Set<int>>(Set.ofList [ 2; 3; 4 ], CSet.toSet s)

  let delta = SetDeltaBuilder<int>()
  delta.Add 5
  delta.Remove 2
  CSet.perform delta s
  Assert.Equal<Set<int>>(Set.ofList [ 3; 4; 5 ], CSet.toSet s)

[<Fact>]
let ``CSet unionWith exceptWith intersectWith are atomic batches``() =
  let s = CSet.ofSeq [ 1; 2; 3 ]
  let versionBefore = (CSet.value s :> IAdaptiveObject).Version

  CSet.unionWith (seq [ 3; 4 ]) s
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 3; 4 ], CSet.toSet s)
  Assert.Equal(versionBefore + 1L, (CSet.value s :> IAdaptiveObject).Version) // one batch, one flush

  CSet.exceptWith (seq [ 1; 5 ]) s
  Assert.Equal<Set<int>>(Set.ofList [ 2; 3; 4 ], CSet.toSet s)

  CSet.intersectWith (seq [ 2; 9; 4 ]) s
  Assert.Equal<Set<int>>(Set.ofList [ 2; 4 ], CSet.toSet s)

[<Fact>]
let ``CMap containsKey tryGetValue item``() =
  let m = CMap.ofSeq [ 1, "a"; 2, "b" ]

  Assert.Equal(true, CMap.containsKey 1 m)
  Assert.Equal(false, CMap.containsKey 3 m)
  Assert.Equal(ValueSome "a", CMap.tryGetValue 1 m)
  Assert.Equal(ValueNone, CMap.tryGetValue 3 m)
  Assert.Equal("b", CMap.item 2 m)

  CMap.remove 1 m
  Assert.Equal(false, CMap.containsKey 1 m)
  Assert.Equal(ValueNone, CMap.tryGetValue 1 m)

[<Fact>]
let ``CMap updateTo perform and clear``() =
  let m = CMap.ofSeq [ 1, "a"; 2, "b" ]

  Assert.Equal(true, CMap.updateTo (seq [ 2, "B"; 3, "c" ]) m)
  Assert.Equal<Map<int, string>>(Map.ofList [ 2, "B"; 3, "c" ], CMap.toMap m)

  Assert.Equal(false, CMap.updateTo (seq [ 2, "B"; 3, "c" ]) m) // equal: no-op

  let delta = MapDeltaBuilder<int, string>()
  delta.Set(4, "d")
  delta.Set(2, "b2")
  delta.Remove 3
  CMap.perform delta m
  Assert.Equal<Map<int, string>>(Map.ofList [ 2, "b2"; 4, "d" ], CMap.toMap m)

  CMap.clear m
  Assert.Equal<Map<int, string>>(Map.empty, CMap.toMap m)

[<Fact>]
let ``CList updateTo perform and addRange``() =
  let l = CList.ofSeq [ 1; 2; 3 ]

  Assert.Equal(true, CList.updateTo [| 2; 3; 4 |] l)
  Assert.Equal<int[]>([| 2; 3; 4 |], CList.force l)

  Assert.Equal(false, CList.updateTo [| 2; 3; 4 |] l) // equal: no-op

  let delta = ListDeltaBuilder<int>()
  delta.Remove 0
  delta.Insert(1, 9)
  delta.Update(2, 8)
  CList.perform delta l // [ 3; 9; 8 ]
  Assert.Equal<int[]>([| 3; 9; 8 |], CList.force l)

  CList.addRange (seq [ 5; 6 ]) l
  Assert.Equal<int[]>([| 3; 9; 8; 5; 6 |], CList.force l)

// =============================================================================
// Bring list — §10 conversions + slicing + ASet.sort; §11 AdaptiveReduction
// par/structpar/mapIn/count.
// =============================================================================

[<Fact>]
let ``AList toASet dedups and toIndexedASet pairs positions``() =
  let l = CList.ofSeq [ 1; 2; 2; 3 ]

  let s = AList.toASet(CList.value l)
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 3 ], ASet.toSet s)

  CList.append 2 l
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 3 ], ASet.toSet s) // still one 2

  CList.removeAt 0 l // [ 2; 2; 3 ]: one 2 remains
  Assert.Equal<Set<int>>(Set.ofList [ 2; 3 ], ASet.toSet s)

  CList.removeAt 0 l // [ 2; 3 ]
  Assert.Equal<Set<int>>(Set.ofList [ 2; 3 ], ASet.toSet s)

  CList.removeAt 0 l // [ 3; 2 ]
  Assert.Equal<Set<int>>(Set.ofList [ 2; 3 ], ASet.toSet s)

  CList.removeAt 0 l // [ 2 ]
  Assert.Equal<Set<int>>(Set.ofList [ 2 ], ASet.toSet s)

  CList.removeAt 0 l // []
  Assert.Equal<Set<int>>(Set.empty, ASet.toSet s)

  let l2 = CList.ofSeq [ 10; 20 ]
  let indexed = AList.toIndexedASet(CList.value l2)

  Assert.Equal<Set<struct (int * int)>>(
    Set.ofList [ struct (0, 10); struct (1, 20) ],
    ASet.toSet indexed
  )

[<Fact>]
let ``AList ofASet follows the set``() =
  let s = CSet.ofSeq [ 3; 1; 2 ]
  let l = AList.ofASet(CSet.value s)

  Assert.Equal<Set<int>>(Set.ofList [ 1; 2; 3 ], ASet.toSet(AList.toASet l))
  Assert.Equal(3, (AList.force l).Length)

  CSet.add 9 s
  CSet.remove 1 s
  Assert.Equal<Set<int>>(Set.ofList [ 2; 3; 9 ], ASet.toSet(AList.toASet l))

[<Fact>]
let ``AMap toAList and ofAList``() =
  let m = CMap.ofSeq [ 1, "a"; 2, "b" ]
  let l = AMap.toAList(CMap.value m)

  Assert.Equal<Set<int * string>>(
    Set.ofList [ 1, "a"; 2, "b" ],
    AList.toArray l |> Set.ofArray
  )

  CMap.addOrUpdate 3 "c" m

  Assert.Equal<Set<int * string>>(
    Set.ofList [ 1, "a"; 2, "b"; 3, "c" ],
    AList.toArray l |> Set.ofArray
  )

  let src = CList.ofSeq [ 1, "x"; 2, "y"; 1, "z" ] // duplicate key: last wins
  let m2 = AMap.ofAList(CList.value src)
  Assert.Equal<Map<int, string>>(Map.ofList [ 1, "z"; 2, "y" ], AMap.toMap m2)

  CList.append (3, "w") src

  Assert.Equal<Map<int, string>>(
    Map.ofList [ 1, "z"; 2, "y"; 3, "w" ],
    AMap.toMap m2
  )

[<Fact>]
let ``ASet sort family returns sorted lists``() =
  let s = CSet.ofSeq [ 3; 1; 2; 1 ] // the set dedups to { 1; 2; 3 }
  let source = CSet.value s

  Assert.Equal<int[]>([| 1; 2; 3 |], source |> ASet.sort |> AList.toArray)

  Assert.Equal<int[]>(
    [| 3; 2; 1 |],
    source |> ASet.sortDescending |> AList.toArray
  )

  Assert.Equal<int[]>(
    [| 1; 2; 3 |],
    source |> ASet.sortBy(fun v -> v) |> AList.toArray
  )

  Assert.Equal<int[]>(
    [| 3; 2; 1 |],
    source |> ASet.sortByDescending(fun v -> v) |> AList.toArray
  )

  CSet.add 0 s
  Assert.Equal<int[]>([| 0; 1; 2; 3 |], source |> ASet.sort |> AList.toArray)

[<Fact>]
let ``AList slicing syntax``() =
  let items = CList.ofSeq [ 0; 1; 2; 3; 4 ]
  let l = CList.value items

  Assert.Equal<int[]>([| 1; 2; 3 |], AList.toArray l.[1..3])
  Assert.Equal<int[]>([| 2; 3; 4 |], AList.toArray l.[2..])
  Assert.Equal<int[]>([| 0; 1 |], AList.toArray l.[..1])

[<Fact>]
let ``AdaptiveReduction par structpar mapIn count``() =
  let l = CList.ofSeq [ 1; 2; 3 ]

  // count and sum in parallel over the same elements
  let par =
    AdaptiveReduction.par AdaptiveReduction.count (AdaptiveReduction.sum())

  let both = AList.reduceBy par (fun v -> v) (CList.value l)
  let (c, s) = AVal.getValue both
  Assert.Equal(3, c)
  Assert.Equal(6, s)

  CList.append 4 l
  let (c2, s2) = AVal.getValue both
  Assert.Equal(4, c2)
  Assert.Equal(10, s2)

  let spar =
    AdaptiveReduction.structpar
      AdaptiveReduction.count
      (AdaptiveReduction.sum())

  let sboth = AList.reduceBy spar (fun v -> v) (CList.value l)
  let struct (c3, s3) = AVal.getValue sboth
  Assert.Equal(4, c3)
  Assert.Equal(10, s3)

  // mapIn: map the element side before reducing
  let sumOfSquares =
    AdaptiveReduction.sum() |> AdaptiveReduction.mapIn(fun v -> v * v)

  let sq = AList.reduceBy sumOfSquares (fun v -> v) (CList.value l)
  Assert.Equal(30, AVal.getValue sq) // 1 + 4 + 9 + 16

  let c = AList.reduceBy AdaptiveReduction.count (fun v -> v) (CList.value l)
  Assert.Equal(4, AVal.getValue c)

// =============================================================================
// Cross-thread posting (the cval.Post handoff pattern) on the changeable
// collections. A post from any thread lands in a per-node pending-op ring;
// the owner applies it at the next graph operation (auto-pump) or at
// Posting.pump, as one batch with one notification delivery.
// =============================================================================

[<Fact>]
let ``CSet posts apply on the next read``() =
  let items = CSet.empty<int>
  let doneSignal = new ManualResetEventSlim(false)

  let worker =
    Task.Run(fun () ->
      CSet.postAdd 1 items
      CSet.postAdd 2 items
      doneSignal.Set())

  doneSignal.Wait()
  worker.Wait()

  // The read's claim auto-drains the pending posts before it returns.
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2 ], ASet.toSet items)

[<Fact>]
let ``posted set batch delivers one net delta``() =
  let items = CSet.empty<int>
  let versionBefore = (CSet.value items :> IAdaptiveObject).Version
  let doneSignal = new ManualResetEventSlim(false)

  let worker =
    Task.Run(fun () ->
      CSet.postAdd 1 items
      CSet.postAdd 2 items
      CSet.postRemove 1 items // cancels the add of 1
      doneSignal.Set())

  doneSignal.Wait()
  worker.Wait()
  Posting.pump()

  // One net flush: the version advances once.
  Assert.Equal(
    versionBefore + 1L,
    (CSet.value items :> IAdaptiveObject).Version
  )

  Assert.Equal<Set<int>>(Set.ofList [ 2 ], ASet.toSet items)

[<Fact>]
let ``posted set batch with no net change marks nothing``() =
  let items = CSet.empty<int>
  let versionBefore = (CSet.value items :> IAdaptiveObject).Version
  let doneSignal = new ManualResetEventSlim(false)

  let worker =
    Task.Run(fun () ->
      CSet.postAdd 1 items
      CSet.postRemove 1 items
      doneSignal.Set())

  doneSignal.Wait()
  worker.Wait()
  Posting.pump()

  Assert.Equal(versionBefore, (CSet.value items :> IAdaptiveObject).Version)
  Assert.Equal<Set<int>>(Set.empty, ASet.toSet items)

[<Fact>]
let ``posted replace supersedes the other ops of the batch``() =
  let items = CSet.empty<int>
  let doneSignal = new ManualResetEventSlim(false)

  let worker =
    Task.Run(fun () ->
      CSet.postAdd 1 items
      CSet.postSet (Set.ofList [ 9; 10 ]) items
      doneSignal.Set())

  doneSignal.Wait()
  worker.Wait()
  Posting.pump()

  Assert.Equal<Set<int>>(Set.ofList [ 9; 10 ], ASet.toSet items)

[<Fact>]
let ``CMap posts apply as one net delta``() =
  let mapValue = CMap.empty<int, string>
  let versionBefore = (CMap.value mapValue :> IAdaptiveObject).Version
  let doneSignal = new ManualResetEventSlim(false)

  let worker =
    Task.Run(fun () ->
      CMap.postAddOrUpdate 1 "a" mapValue
      CMap.postAddOrUpdate 2 "b" mapValue
      CMap.postRemove 1 mapValue // cancels the add of 1
      doneSignal.Set())

  doneSignal.Wait()
  worker.Wait()
  Posting.pump()

  // One net flush: the version advances once.
  Assert.Equal(
    versionBefore + 1L,
    (CMap.value mapValue :> IAdaptiveObject).Version
  )

  Assert.Equal<Map<int, string>>(Map.ofList [ 2, "b" ], AMap.toMap mapValue)

[<Fact>]
let ``CMap postSet and postClear replace the content``() =
  let mapValue = CMap.ofSeq [ 1, "a"; 2, "b" ]
  let doneSignal = new ManualResetEventSlim(false)

  let worker =
    Task.Run(fun () ->
      CMap.postAddOrUpdate 3 "c" mapValue
      CMap.postSet (Map.toSeq(Map.ofList [ 9, "z" ])) mapValue // supersedes the batch
      doneSignal.Set())

  doneSignal.Wait()
  worker.Wait()
  Posting.pump()

  Assert.Equal<Map<int, string>>(Map.ofList [ 9, "z" ], AMap.toMap mapValue)

  let doneSignal2 = new ManualResetEventSlim(false)

  let worker2 =
    Task.Run(fun () ->
      CMap.postClear mapValue
      doneSignal2.Set())

  doneSignal2.Wait()
  worker2.Wait()
  Posting.pump()

  Assert.Equal<Map<int, string>>(Map.empty, AMap.toMap mapValue)

[<Fact>]
let ``CList posts preserve order and apply-time positions``() =
  let items = CList.empty<int>
  let doneSignal = new ManualResetEventSlim(false)

  let worker =
    Task.Run(fun () ->
      CList.postAppend 1 items
      CList.postAppend 2 items
      CList.postAppend 3 items
      doneSignal.Set())

  doneSignal.Wait()
  worker.Wait()
  Posting.pump()

  Assert.Equal<int[]>([| 1; 2; 3 |], AList.force items)

  // Positions refer to the state built by the earlier ops of the batch:
  // both inserts land at position 0 of the evolving list.
  let doneSignal2 = new ManualResetEventSlim(false)

  let worker2 =
    Task.Run(fun () ->
      CList.postInsertAt 0 9 items
      CList.postInsertAt 0 8 items
      doneSignal2.Set())

  doneSignal2.Wait()
  worker2.Wait()
  Posting.pump()

  Assert.Equal<int[]>([| 8; 9; 1; 2; 3 |], AList.force items)

  let doneSignal3 = new ManualResetEventSlim(false)

  let worker3 =
    Task.Run(fun () ->
      CList.postRemoveAt 0 items
      CList.postRemoveAt 0 items
      doneSignal3.Set())

  doneSignal3.Wait()
  worker3.Wait()
  Posting.pump()

  Assert.Equal<int[]>([| 1; 2; 3 |], AList.force items)

[<Fact>]
let ``CList postRemove removes the first occurrence``() =
  let items = CList.empty<int>
  let doneSignal = new ManualResetEventSlim(false)

  let worker =
    Task.Run(fun () ->
      CList.postAppend 1 items
      CList.postAppend 2 items
      CList.postAppend 1 items
      CList.postRemove 1 items
      doneSignal.Set())

  doneSignal.Wait()
  worker.Wait()
  Posting.pump()

  Assert.Equal<int[]>([| 2; 1 |], AList.force items)

[<Fact>]
let ``CList postClear and postSet replace the content``() =
  let items = CList.ofSeq [ 1; 2; 3 ]
  let doneSignal = new ManualResetEventSlim(false)

  let worker =
    Task.Run(fun () ->
      CList.postAppend 4 items
      CList.postClear items // the replace supersedes the whole batch
      doneSignal.Set())

  doneSignal.Wait()
  worker.Wait()
  Posting.pump()

  // The replace (clear) supersedes the whole batch.
  Assert.Equal<int[]>(Array.empty, AList.force items)

  let doneSignal2 = new ManualResetEventSlim(false)

  let worker2 =
    Task.Run(fun () ->
      CList.postSet [ 7; 8 ] items
      doneSignal2.Set())

  doneSignal2.Wait()
  worker2.Wait()
  Posting.pump()

  Assert.Equal<int[]>([| 7; 8 |], AList.force items)

[<Fact>]
let ``posted list positions are validated at apply time``() =
  let items = CList.empty<int>
  let doneSignal = new ManualResetEventSlim(false)

  let worker =
    Task.Run(fun () ->
      CList.postRemoveAt 5 items // invalid when the batch applies
      doneSignal.Set())

  doneSignal.Wait()
  worker.Wait()

  Assert.Throws<ArgumentOutOfRangeException>(fun () -> Posting.pump()) |> ignore

  // The failed batch applies nothing and the graph stays usable: a later
  // valid batch applies normally (the drain failure unwinds the owner
  // claim before propagating).
  let doneSignal2 = new ManualResetEventSlim(false)

  let worker2 =
    Task.Run(fun () ->
      CList.postAppend 1 items
      doneSignal2.Set())

  doneSignal2.Wait()
  worker2.Wait()
  Posting.pump()

  Assert.Equal<int[]>([| 1 |], AList.force items)

  // Strictness matches the transaction path: an invalid positional op
  // aborts the batch even when a later replace would supersede it (the
  // transaction validates positional ops at write time and throws there).
  let items3 = CList.empty<int>
  let doneSignal3 = new ManualResetEventSlim(false)

  let worker3 =
    Task.Run(fun () ->
      CList.postRemoveAt 0 items3
      CList.postSet [ 1; 2; 3 ] items3
      doneSignal3.Set())

  doneSignal3.Wait()
  worker3.Wait()
  Assert.Throws<ArgumentOutOfRangeException>(fun () -> Posting.pump()) |> ignore
  Assert.Equal<int[]>(Array.empty, AList.force items3)

[<Fact>]
let ``posts inside a transaction apply after commit``() =
  let items = CSet.empty<int>
  let started = new ManualResetEventSlim(false)
  let posted = new ManualResetEventSlim(false)

  Transaction.run(fun () ->
    // A post lands while the transaction is open: the auto-pump is
    // skipped (reads inside a transaction see the pre-transaction state).
    let worker =
      Task.Run(fun () ->
        started.Wait()
        CSet.postAdd 1 items
        posted.Set())

    started.Set()
    posted.Wait()
    worker.Wait()

    Assert.Equal<Set<int>>(Set.empty, ASet.toSet items)

    CSet.add 2 items)

  // The post applies at the next graph operation, after the commit.
  Assert.Equal<Set<int>>(Set.ofList [ 1; 2 ], ASet.toSet items)

[<Fact>]
let ``concurrent producers post to one set without loss``() =
  let items = CSet.empty<int>
  let n = 500

  let workers =
    [| 1..2 |]
    |> Array.map(fun w ->
      Task.Run(fun () ->
        for i in 1..n do
          CSet.postAdd (w * 100000 + i) items))

  workers |> Array.iter(fun w -> w.Wait())
  Posting.pump()

  Assert.Equal(2 * n, (ASet.getValue items).Count)

[<Fact>]
let ``posting allocates nothing after the first post``() =
  let items = CSet.empty<int>
  // Warm up: the first post allocates the per-node ring lazily.
  CSet.postAdd 0 items
  Posting.pump()

  let workerAlloc =
    Task
      .Run(fun () ->
        let before = GC.GetAllocatedBytesForCurrentThread()

        for i in 1..1000 do
          CSet.postAdd i items

        GC.GetAllocatedBytesForCurrentThread() - before)
      .Result

  Posting.pump()
  Assert.Equal(1001, (ASet.getValue items).Count)

  Assert.True(
    workerAlloc < 1024,
    sprintf "worker allocated %d bytes per 1000 posts" workerAlloc
  )

// =============================================================================
// Phase 8 — Per-thread graphs
//
// Every thread owns its ambient graph, created lazily on first use. A node
// belongs to the graph of its creating thread. Direct operations on another
// thread's nodes are rejected in debug builds; cross-thread changes go
// through the post machinery and land in the node's own graph ring.
//
// A dedicated Thread is used for the foreign thread (Task.Run may inline the
// work on the caller's own thread-pool thread, which would not be foreign).
// =============================================================================

/// Runs f on a dedicated thread and rethrows any exception on the caller.
/// The caller must not need to interleave with f (no shared barriers).
let private runOnThread(f: unit -> unit) : unit =
  let mutable error: exn option = None

  let t =
    Thread(fun () ->
      try
        f()
      with ex ->
        error <- Some ex)

  t.Start()
  t.Join()

  match error with
  | None -> ()
  | Some ex -> raise ex

/// Starts a dedicated thread that runs f, captures its exception, and joins
/// it on dispose, rethrowing the captured exception.
let private startThread(f: unit -> unit) : IDisposable =
  let mutable error: exn option = None

  let t =
    Thread(fun () ->
      try
        f()
      with ex ->
        error <- Some ex)

  t.Start()

  { new IDisposable with
      member _.Dispose() =
        t.Join()

        match error with
        | None -> ()
        | Some ex -> raise ex
  }

[<Fact>]
let ``two threads own independent graphs``() =
  // The exact scenario: main and a background thread each create their own
  // cval and write it concurrently. With per-thread graphs the writes never
  // contend; with a shared graph the debug check would throw.
  let value = CVal.create 10
  let barrier = new Barrier(2)

  let worker =
    startThread(fun () ->
      let valueb = CVal.create 10 // the worker's own graph
      barrier.SignalAndWait()
      valueb.Set 20
      barrier.SignalAndWait()

      if AVal.getValue valueb <> 20 then
        failwith "background value wrong"

      valueb.Set 21

      if AVal.getValue valueb <> 21 then
        failwith "background value wrong after second write")

  barrier.SignalAndWait()
  value.Set 15
  barrier.SignalAndWait()

  if AVal.getValue value <> 15 then
    failwith "main value wrong"

  value.Set 16

  if AVal.getValue value <> 16 then
    failwith "main value wrong after second write"

  // Joins the worker and rethrows any worker failure.
  worker.Dispose()

[<Fact>]
let ``collections on different threads are independent``() =
  let items = CSet.empty<int> // main's graph
  let barrier = new Barrier(2)

  let worker =
    startThread(fun () ->
      let workerItems = CSet.empty<int> // the worker's own graph
      CSet.add 1 workerItems
      barrier.SignalAndWait()
      CSet.add 2 workerItems
      barrier.SignalAndWait()

      if ASet.toSet workerItems <> Set.ofList [ 1; 2 ] then
        failwith "worker set wrong")

  barrier.SignalAndWait()
  CSet.add 10 items
  barrier.SignalAndWait()

  // Joins the worker and rethrows any worker failure before the asserts.
  worker.Dispose()

  Assert.Equal<Set<int>>(Set.ofList [ 10 ], ASet.toSet items)

[<Fact>]
let ``a thread with its own graph posts into the owner's ring``() =
  let value = CVal.create 0 // main's graph
  let posted = new ManualResetEventSlim(false)

  runOnThread(fun () ->
    let own = CVal.create 1 // the worker's own graph
    own.Set 2 // worker operates its own graph freely
    value.Post 7 // cross-thread: lands in MAIN's ring

    // The worker's pump must not apply the post: it sits in the node's
    // own ring. If it landed in the worker's ring, applying it would
    // claim the node's graph from a foreign thread (debug owner check)
    // and throw.
    Posting.pump()
    posted.Set())

  posted.Wait()

  // The owner's next graph operation drains and applies the post.
  Assert.Equal(7, AVal.getValue value)

// =============================================================================
// Tail-only reads of 2+ level chains (the stale-count report). A write
// journals the first transform level at write time; a transform pushes
// downstream only at its own read. The transform versions must therefore
// indicate upstream dirt (the dirty-indicator Version), or a gated tail read
// (count, tryFind, contains, tryLast, fold, a scalar consumer) of a chain with
// two or more transform levels serves the stale value forever.
// =============================================================================

[<Fact>]
let ``map: tail-only count over chooseA + filter settles``() =
  // The reported repro, verbatim.
  let src = CMap.empty<int, int>
  let chosen = src |> AMap.chooseA(fun _ v -> AVal.constant(Some v))
  let filtered = chosen |> AMap.filter(fun _ v -> v > 10)
  let cnt = filtered |> AMap.count

  Assert.Equal(0, AVal.getValue cnt)
  CMap.addOrUpdate 1 42 src
  Assert.Equal(1, AVal.getValue cnt)

[<Fact>]
let ``map: tail-only count over map + filter settles after batched writes``() =
  let src = CMap.empty<int, int>
  let mapped = src |> AMap.map(fun _ v -> v * 2)
  let filtered = mapped |> AMap.filter(fun _ v -> v > 10)
  let cnt = filtered |> AMap.count

  Assert.Equal(0, AVal.getValue cnt)
  // Three writes with no intermediate reads anywhere in the chain.
  CMap.addOrUpdate 1 42 src
  CMap.addOrUpdate 2 100 src
  CMap.remove 1 src |> ignore
  Assert.Equal(1, AVal.getValue cnt)

[<Fact>]
let ``map: tail-only tryFind over map + filter settles``() =
  let src = CMap.empty<int, int>
  let mapped = src |> AMap.map(fun _ v -> v + 1)
  let filtered = mapped |> AMap.filter(fun _ v -> v > 0)
  let lookup = filtered |> AMap.tryFind 7

  Assert.Equal(ValueNone, AVal.getValue lookup)
  CMap.addOrUpdate 7 41 src
  Assert.Equal(ValueSome 42, AVal.getValue lookup)

[<Fact>]
let ``map: tail-only isEmpty over map + filter settles``() =
  let src = CMap.empty<int, int>
  let mapped = src |> AMap.map(fun _ v -> v)
  let filtered = mapped |> AMap.filter(fun _ v -> v > 10)
  let empty = filtered |> AMap.isEmpty

  Assert.True(AVal.getValue empty)
  CMap.addOrUpdate 1 42 src
  Assert.False(AVal.getValue empty)
  CMap.remove 1 src |> ignore
  Assert.True(AVal.getValue empty)

[<Fact>]
let ``map: tail-only fold over map + filter settles``() =
  let src = CMap.empty<int, int>
  let mapped = src |> AMap.map(fun _ v -> v)
  let filtered = mapped |> AMap.filter(fun _ v -> v > 10)
  let sum = filtered |> AMap.fold (fun acc _ v -> acc + v) 0

  Assert.Equal(0, AVal.getValue sum)
  CMap.addOrUpdate 1 42 src
  Assert.Equal(42, AVal.getValue sum)
  CMap.addOrUpdate 2 8 src
  // 8 does not pass the v > 10 filter: the sum must stay 42.
  Assert.Equal(42, AVal.getValue sum)

[<Fact>]
let ``map: tail-only count over three transform levels settles``() =
  let src = CMap.empty<int, int>
  let a = src |> AMap.map(fun _ v -> v)
  let b = a |> AMap.filter(fun _ v -> v > 10)
  let c = b |> AMap.choose(fun _ v -> Some v)
  let cnt = c |> AMap.count

  Assert.Equal(0, AVal.getValue cnt)
  CMap.addOrUpdate 1 42 src
  Assert.Equal(1, AVal.getValue cnt)

[<Fact>]
let ``map: tail-only read of a 3-level chain's last transform settles``() =
  let src = CMap.empty<int, int>
  let a = src |> AMap.map(fun _ v -> v * 2)
  let b = a |> AMap.filter(fun _ v -> v > 10)
  let c = b |> AMap.map(fun _ v -> v + 1)

  AMap.getValue c |> ignore
  CMap.addOrUpdate 1 42 src
  Assert.Equal(85, (AMap.getValue c)[1])

[<Fact>]
let ``map: scalar consumer over map + filter settles``() =
  let src = CMap.empty<int, int>
  let mapped = src |> AMap.map(fun _ v -> v)
  let filtered = mapped |> AMap.filter(fun _ v -> v > 10)
  let scalar = filtered |> AMap.toAVal |> AVal.map(fun d -> d.Count)

  Assert.Equal(0, AVal.getValue scalar)
  CMap.addOrUpdate 1 42 src
  Assert.Equal(1, AVal.getValue scalar)

[<Fact>]
let ``map: bind over a 2-level inner chain settles``() =
  let src = CMap.empty<int, int>
  let mapped = src |> AMap.map(fun _ v -> v)
  let filtered = mapped |> AMap.filter(fun _ v -> v > 10)
  let pick = CVal.create true
  let bound = AMap.bind (fun _ -> filtered) pick
  let cnt = bound |> AMap.count

  Assert.Equal(0, AVal.getValue cnt)
  CMap.addOrUpdate 1 42 src
  Assert.Equal(1, AVal.getValue cnt)

[<Fact>]
let ``set: tail-only count and contains over map + filter settle``() =
  let src = CSet.empty<int>
  let mapped = src |> ASet.map(fun v -> v * 2)
  let filtered = mapped |> ASet.filter(fun v -> v > 10)
  let cnt = filtered |> ASet.count
  let has = filtered |> ASet.contains 84

  Assert.Equal(0, AVal.getValue cnt)
  Assert.False(AVal.getValue has)
  CSet.add 42 src |> ignore
  Assert.Equal(1, AVal.getValue cnt)
  Assert.True(AVal.getValue has)

[<Fact>]
let ``set: union with a 2-level side settles``() =
  let left = CSet.empty<int>
  let right = CSet.empty<int>
  let mapped = left |> ASet.map(fun v -> v * 2)
  let filtered = mapped |> ASet.filter(fun v -> v > 10)
  let union = ASet.union filtered (right :> aset<int>)
  let cnt = union |> ASet.count

  Assert.Equal(0, AVal.getValue cnt)
  CSet.add 42 left |> ignore
  Assert.Equal(1, AVal.getValue cnt)
  CSet.add 7 right |> ignore
  Assert.Equal(2, AVal.getValue cnt)

[<Fact>]
let ``list: tail-only count and tryLast over map + filter settle``() =
  let src = CList.empty<int>
  let mapped = src |> AList.map(fun v -> v * 2)
  let filtered = mapped |> AList.filter(fun v -> v > 10)
  let cnt = filtered |> AList.count
  let last = filtered |> AList.tryLast

  Assert.Equal(0, AVal.getValue cnt)
  Assert.Equal(ValueNone, AVal.getValue last)
  CList.append 42 src |> ignore
  Assert.Equal(1, AVal.getValue cnt)
  Assert.Equal(ValueSome 84, AVal.getValue last)

#if DEBUG
[<Fact>]
let ``direct access to another thread's node throws in debug``() =
  let value = CVal.create 10
  let mutable threw = false

  runOnThread(fun () ->
    try
      value.Set 99 // foreign thread, direct access
    with :? InvalidOperationException ->
      threw <- true)

  Assert.True(threw)
  Assert.Equal(10, AVal.getValue value)

[<Fact>]
let ``direct read of another thread's node throws in debug``() =
  let value = CVal.create 10
  let mutable threw = false

  runOnThread(fun () ->
    try
      AVal.getValue value |> ignore
    with :? InvalidOperationException ->
      threw <- true)

  Assert.True(threw)
#endif

// ---- System.Text.Json round-trip of changeable nodes (see ChangeableConverterFactory) ----

[<Fact>]
let ``cval round-trips through System.Text.Json``() =
  let v = CVal.create 10
  let json = JsonSerializer.Serialize v

  Assert.Equal("10", json)

  let v' = JsonSerializer.Deserialize<cval<int>> json
  Assert.Equal(10, AVal.getValue(CVal.value v'))

  CVal.set 12 v'
  Assert.Equal(12, AVal.getValue(CVal.value v'))

[<Fact>]
let ``cset round-trips through System.Text.Json``() =
  let s = CSet.ofSeq [ 1; 2; 3 ]
  let json = JsonSerializer.Serialize s

  let s' = JsonSerializer.Deserialize<cset<int>> json
  Assert.Equal(3, (ASet.count(CSet.value s') |> AVal.getValue))

  CSet.add 4 s'
  Assert.Equal(4, (ASet.count(CSet.value s') |> AVal.getValue))

[<Fact>]
let ``cmap round-trips through System.Text.Json``() =
  let m = CMap.ofSeq [ "a", 1; "b", 2 ]
  let json = JsonSerializer.Serialize m

  Assert.Equal("""{"a":1,"b":2}""", json)

  let m' = JsonSerializer.Deserialize<cmap<string, int>> json
  Assert.Equal(2, (AMap.getValue(CMap.value m')).Count)

  CMap.addOrUpdate "c" 3 m'
  Assert.Equal(3, (AMap.getValue(CMap.value m')).Count)

[<Fact>]
let ``clist round-trips through System.Text.Json``() =
  let l = CList.ofSeq [ 1; 2; 3 ]
  let json = JsonSerializer.Serialize l

  Assert.Equal("[1,2,3]", json)

  let l' = JsonSerializer.Deserialize<clist<int>> json
  Assert.Equal(3, (AList.getValue(CList.value l')).Count)

  CList.append 4 l'
  Assert.Equal(4, (AList.getValue(CList.value l')).Count)

type JsonDoc = {
  Selected: cval<string>
  Prices: cmap<string, int>
}

[<Fact>]
let ``changeable nodes round-trip nested in a record``() =
  let doc: JsonDoc = {
    Selected = CVal.create "apple"
    Prices = CMap.ofSeq [ "apple", 10 ]
  }

  let json = JsonSerializer.Serialize doc

  let doc' = JsonSerializer.Deserialize<JsonDoc> json
  Assert.Equal("apple", AVal.getValue(CVal.value doc'.Selected))
  Assert.Equal(10, (AMap.getValue(CMap.value doc'.Prices))["apple"])

  CVal.set "pear" doc'.Selected
  Assert.Equal("pear", AVal.getValue(CVal.value doc'.Selected))

[<Fact>]
let ``cval of a plain F# collection round-trips``() =
  let v = CVal.create [ 1; 2; 3 ]
  let json = JsonSerializer.Serialize v

  Assert.Equal("[1,2,3]", json)

  let v' = JsonSerializer.Deserialize<cval<int list>> json
  Assert.Equal<int list>([ 1; 2; 3 ], AVal.getValue(CVal.value v'))

[<Fact>]
let ``nodes nested in nodes round-trip``() =
  let v = CVal.create(CVal.create 7)
  let json = JsonSerializer.Serialize v

  Assert.Equal("7", json)

  let v' = JsonSerializer.Deserialize<cval<cval<int>>> json
  Assert.Equal(7, AVal.getValue(CVal.value v'.Value))

[<Fact>]
let ``collections of nodes round-trip``() =
  let s = CSet.ofSeq [ CVal.create 1; CVal.create 2 ]
  let json = JsonSerializer.Serialize s

  let s' = JsonSerializer.Deserialize<cset<cval<int>>> json
  Assert.Equal(2, (ASet.count(CSet.value s') |> AVal.getValue))

  let m = CMap.ofSeq [ "x", CVal.create 5 ]
  let jsonM = JsonSerializer.Serialize m

  let m' = JsonSerializer.Deserialize<cmap<string, cval<int>>> jsonM

  Assert.Equal(
    5,
    ((AMap.getValue(CMap.value m'))["x"] |> fun n -> AVal.getValue(CVal.value n))
  )
