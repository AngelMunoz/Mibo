// ─────────────────────────────────────────────────────────────
// Empirical probe: does a collection recompute incrementally
// (only the changed entries) or fully (all entries)?
// Analysis only — no production code involved.
// ─────────────────────────────────────────────────────────────
#r "../AdaptiveSlop/src/AdaptiveSlop.Core/bin/Debug/net10.0/AdaptiveSlop.Core.dll"

open System
open AdaptiveSlop.Core

let m = CMap.ofSeq [ for i in 0..999 -> i, i ]

// A mapA join shaped like the game's Views: one per-element compute.
let mutable computes = 0

let res =
  m
  |> AMap.mapA(fun _ v ->
    computes <- computes + 1
    AVal.constant(v * 2))

// 1) Initial load
AMap.getValue res |> ignore
printfn "initial load           : %d element computes (expect 1000)" computes

// 2) Settled read — no writes
computes <- 0
AMap.getValue res |> ignore
printfn "settled read (no write): %d element computes (expect 0)" computes

// 3) ONE entry write → read
computes <- 0
CMap.addOrUpdate 7 700 m
AMap.getValue res |> ignore

printfn
  "one-entry write        : %d element computes (expect 1; full = 1000)"
  computes

// 4) 100 entry writes (one batch) → read
computes <- 0

Transaction.run(fun () ->
  for i in 0..99 do
    CMap.addOrUpdate i (i * 3) m)

AMap.getValue res |> ignore
printfn "100-entry write (batch): %d element computes (expect 100)" computes

// 5) Allocation proportionality: 1 write vs 1000 writes
GC.Collect()
let before1 = GC.GetAllocatedBytesForCurrentThread()
CMap.addOrUpdate 3 999 m
AMap.getValue res |> ignore
let alloc1 = GC.GetAllocatedBytesForCurrentThread() - before1

GC.Collect()
let before2 = GC.GetAllocatedBytesForCurrentThread()

Transaction.run(fun () ->
  for i in 0..999 do
    CMap.addOrUpdate i (i * 7) m)

AMap.getValue res |> ignore
let allocN = GC.GetAllocatedBytesForCurrentThread() - before2

printfn "alloc one write         : %d B" alloc1

printfn
  "alloc 1000 writes       : %d B (ratio %.1fx of one write)"
  allocN
  (float allocN / float(max alloc1 1))
