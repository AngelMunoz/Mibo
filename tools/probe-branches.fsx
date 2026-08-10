// ─────────────────────────────────────────────────────────────
// Empirical probe: branch isolation — do non-recomputed paths
// (branches whose sources did NOT change) recompute anyway?
// Analysis only — no production code involved.
// ─────────────────────────────────────────────────────────────
#r "../AdaptiveSlop/src/AdaptiveSlop.Core/bin/Debug/net10.0/AdaptiveSlop.Core.dll"
open AdaptiveSlop.Core

// ── Probe A: a branching aval DAG ─────────────────────────────
let a = CVal.create 1
let b = CVal.create 10
let mutable aComputes = 0
let mutable bComputes = 0
let mutable sumComputes = 0

let xa = a |> CVal.value |> AVal.map(fun v -> aComputes <- aComputes + 1; v * 2)
let xb = b |> CVal.value |> AVal.map(fun v -> bComputes <- bComputes + 1; v * 3)
let sum = AVal.map2(fun x y -> sumComputes <- sumComputes + 1; x + y) xa xb

AVal.getValue sum |> ignore // warm: a=1, b=1, sum=1
aComputes <- 0; bComputes <- 0; sumComputes <- 0

printfn "── Probe A: branch isolation (two independent branches) ──"
AVal.getValue sum |> ignore // settled
printfn "settled read                 : a=%d b=%d sum=%d (expect 0/0/0)" aComputes bComputes sumComputes

a.Set 2
AVal.getValue sum |> ignore
printfn "write a only                 : a=%d b=%d sum=%d (expect 1/0/1)" aComputes bComputes sumComputes

aComputes <- 0; bComputes <- 0; sumComputes <- 0
b.Set 20
AVal.getValue xa |> ignore // read the OTHER branch only
printfn "write b, read xa (sibling)   : a=%d b=%d (expect 0/0 — b's mark never reaches xa)" aComputes bComputes

// ── Probe B: game-shaped projection set over a shared source ──
printfn ""
printfn "── Probe B: game-shaped projections over 100 enemies ──"

let positions = CMap.ofSeq [ for i in 0 .. 99 -> i, float32 i ]
let healths = CMap.ofSeq [ for i in 0 .. 99 -> i, 100 ]
let hover = CVal.create 0

let mutable viewComputes = 0
let mutable alivePredicates = 0
let mutable countComputes = 0
let mutable bannerComputes = 0
let mutable rangeComputes = 0

// Views: per-element join (mapA) — like Enemies.Views
let views =
    positions
    |> AMap.mapA(fun eid pos ->
        viewComputes <- viewComputes + 1
        AVal.constant(pos + 1f))

// Alive: per-element filter (filterA) — like Enemies.Alive
let alive =
    views
    |> AMap.filterA(fun _ v ->
        alivePredicates <- alivePredicates + 1
        AVal.constant(v < 1000f))

// Count + banner chain — like Waves' banner over the count
let count =
    alive
    |> AMap.count
    |> AVal.map(fun n -> countComputes <- countComputes + 1; n)

let banner =
    count
    |> AVal.map(fun n -> bannerComputes <- bannerComputes + 1; $"Wave %d{n}")

// An INDEPENDENT branch over a DIFFERENT source — like RangeRing
let range =
    hover
    |> CVal.value
    |> AVal.map(fun h -> rangeComputes <- rangeComputes + 1; h * 2)

AMap.getValue views |> ignore
AMap.getValue alive |> ignore
AVal.getValue banner |> ignore
AVal.getValue range |> ignore // warm all
viewComputes <- 0; alivePredicates <- 0; countComputes <- 0; bannerComputes <- 0; rangeComputes <- 0

// 1) ONE enemy position changes
CMap.addOrUpdate 7 700f positions
AMap.getValue views |> ignore
AMap.getValue alive |> ignore
AVal.getValue banner |> ignore
AVal.getValue range |> ignore
printfn "one position write, read all: views=%d alivePred=%d count=%d banner=%d range=%d (expect 1/1/1/1/0)"
    viewComputes alivePredicates countComputes bannerComputes rangeComputes

// 2) hover (the independent branch's source) changes
viewComputes <- 0; alivePredicates <- 0; countComputes <- 0; bannerComputes <- 0; rangeComputes <- 0
CVal.set 5 hover
AMap.getValue views |> ignore
AMap.getValue alive |> ignore
AVal.getValue banner |> ignore
AVal.getValue range |> ignore
printfn "hover write, read all       : views=%d alivePred=%d count=%d banner=%d range=%d (expect 0/0/0/0/1)"
    viewComputes alivePredicates countComputes bannerComputes rangeComputes
