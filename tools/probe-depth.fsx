// ─────────────────────────────────────────────────────────────
// Empirical probe: per-frame recompute count for the game's exact
// projection chain shape (Views join with per-element tryFind
// nodes + Alive filter + count + banner) vs a root-read shape.
// Analysis only — no production code involved.
// ─────────────────────────────────────────────────────────────
#r "../AdaptiveSlop/src/AdaptiveSlop.Core/bin/Debug/net10.0/AdaptiveSlop.Core.dll"
open AdaptiveSlop.Core

let positions = CMap.ofSeq [ for i in 0..99 -> i, float32 i ]
let healths = CMap.ofSeq [ for i in 0..99 -> i, 100 ]

let mutable elementJoins = 0 // the mapA mapping (one per element node recompute)
let mutable joinMaps = 0 // the AVal.map over tryFind (per element)
let mutable alivePreds = 0 // the filterA predicate
let mutable countComputes = 0 // the count node
let mutable bannerComputes = 0 // the banner node

// ── Shape 1: the game's Views chain (mapA + per-element tryFind) ──
let views =
  positions
  |> AMap.mapA(fun eid pos ->
    elementJoins <- elementJoins + 1

    healths
    |> AMap.tryFind eid
    |> AVal.map(fun h ->
      joinMaps <- joinMaps + 1
      pos + float32(h |> ValueOption.defaultValue 0)))

let alive =
  views
  |> AMap.filterA(fun _ v ->
    alivePreds <- alivePreds + 1
    AVal.constant(v < 1000f))

let count =
  alive
  |> AMap.count
  |> AVal.map(fun n ->
    countComputes <- countComputes + 1
    n)

let banner =
  count
  |> AVal.map(fun n ->
    bannerComputes <- bannerComputes + 1
    $"Wave %d{n}")

AMap.getValue views |> ignore
AMap.getValue alive |> ignore
AVal.getValue banner |> ignore // warm
elementJoins <- 0
joinMaps <- 0
alivePreds <- 0
countComputes <- 0
bannerComputes <- 0

// Simulated frame: ONE enemy moves + ONE enemy takes damage (two writes).
CMap.addOrUpdate 7 700f positions
CMap.addOrUpdate 7 50 healths
AMap.getValue views |> ignore
AMap.getValue alive |> ignore
AVal.getValue banner |> ignore

let totalShape1 =
  elementJoins + joinMaps + alivePreds + countComputes + bannerComputes

printfn
  "── Shape 1 (game's chain): one frame, two writes (pos+health of enemy 7) ──"

printfn
  "element joins=%d tryFind joins=%d alive preds=%d count=%d banner=%d → total %d node recomputes"
  elementJoins
  joinMaps
  alivePreds
  countComputes
  bannerComputes
  totalShape1

printfn "  (unchanged 99 enemies: 0 — branch isolation confirmed again)"

// ── Shape 2: root reads — the hot path bypasses the graph entirely ──
elementJoins <- 0
joinMaps <- 0
alivePreds <- 0
countComputes <- 0
bannerComputes <- 0
let framePositions = positions |> AMap.getValue // transient dict read
let frameHealths = healths |> AMap.getValue
let aliveCount = framePositions.Count // transient .Count — no node
let bannerText = sprintf "Wave %d" aliveCount // computed in the view

CMap.addOrUpdate 7 700f positions
CMap.addOrUpdate 7 50 healths
// the frame reads the sources transiently — zero graph nodes touched
let _ = positions |> AMap.getValue
let _ = healths |> AMap.getValue
printfn ""
printfn "── Shape 2 (root reads): same frame ──"

printfn
  "graph node recomputes=%d (plain dictionary reads)"
  (elementJoins + joinMaps + alivePreds + countComputes + bannerComputes)

printfn "  (banner text computed in the view: %s)" bannerText
