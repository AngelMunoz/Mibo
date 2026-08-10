// ─────────────────────────────────────────────────────────────
// EXACT probe-depth environment + shape B side by side.
// ─────────────────────────────────────────────────────────────
#r "../AdaptiveSlop/src/AdaptiveSlop.Core/bin/Debug/net10.0/AdaptiveSlop.Core.dll"
open AdaptiveSlop.Core

let positions = CMap.ofSeq [ for i in 0 .. 99 -> i, float32 i ]
let healths = CMap.ofSeq [ for i in 0 .. 99 -> i, 100 ]

let mutable elementJoins = 0
let mutable joinMaps = 0
let mutable elementJoinsB = 0
let mutable joinMapsB = 0

let views =
    positions
    |> AMap.mapA(fun eid pos ->
        elementJoins <- elementJoins + 1
        healths
        |> AMap.tryFind eid
        |> AVal.map(fun h ->
            joinMaps <- joinMaps + 1
            pos + float32(h |> ValueOption.defaultValue 0)))

let viewsB =
    positions
    |> AMap.mapA(fun eid pos ->
        elementJoinsB <- elementJoinsB + 1
        let hp = healths |> CMap.tryGetValue eid |> ValueOption.defaultValue 0
        AVal.constant(pos + float32 hp))

let alive = views |> AMap.filterA(fun _ v -> AVal.constant(v < 1000f))
let aliveB = viewsB |> AMap.filterA(fun _ v -> AVal.constant(v < 1000f))
let count = alive |> AMap.count
let countB = aliveB |> AMap.count
let banner = count |> AVal.map(fun n -> $"Wave %d{n}")
let bannerB = countB |> AVal.map(fun n -> $"Wave %d{n}")

AMap.getValue views |> ignore
AMap.getValue alive |> ignore
AVal.getValue banner |> ignore
AMap.getValue viewsB |> ignore
AMap.getValue aliveB |> ignore
AVal.getValue bannerB |> ignore
elementJoins <- 0; joinMaps <- 0; elementJoinsB <- 0; joinMapsB <- 0

// Frame 1: one enemy moves + one takes damage (the normal game frame)
CMap.addOrUpdate 7 700f positions
CMap.addOrUpdate 7 50 healths
AMap.getValue views |> ignore
AMap.getValue alive |> ignore
AVal.getValue banner |> ignore
AMap.getValue viewsB |> ignore
AMap.getValue aliveB |> ignore
AVal.getValue bannerB |> ignore
printfn "frame pos+damage: A: element=%d join=%d | B: element=%d join=%d"
    elementJoins joinMaps elementJoinsB joinMapsB

// Frame 2: damage only
elementJoins <- 0; joinMaps <- 0; elementJoinsB <- 0; joinMapsB <- 0
CMap.addOrUpdate 7 40 healths
AMap.getValue views |> ignore
AMap.getValue alive |> ignore
AVal.getValue banner |> ignore
AMap.getValue viewsB |> ignore
AMap.getValue aliveB |> ignore
AVal.getValue bannerB |> ignore
printfn "damage only    : A: element=%d join=%d | B: element=%d join=%d"
    elementJoins joinMaps elementJoinsB joinMapsB
