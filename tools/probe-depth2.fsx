#r "../AdaptiveSlop/src/AdaptiveSlop.Core/bin/Debug/net10.0/AdaptiveSlop.Core.dll"
open AdaptiveSlop.Core

let positions = CMap.ofSeq [ for i in 0 .. 99 -> i, float32 i ]
let healths = CMap.ofSeq [ for i in 0 .. 99 -> i, 100 ]

let mutable elementJoins = 0
let mutable joinMaps = 0

let views =
    positions
    |> AMap.mapA(fun eid pos ->
        elementJoins <- elementJoins + 1
        healths
        |> AMap.tryFind eid
        |> AVal.map(fun h ->
            joinMaps <- joinMaps + 1
            pos + float32(h |> ValueOption.defaultValue 0)))

AMap.getValue views |> ignore // warm
elementJoins <- 0; joinMaps <- 0

// A) position write ONLY
CMap.addOrUpdate 7 700f positions
AMap.getValue views |> ignore
printfn "A) pos write only : elementJoins=%d joinMaps=%d" elementJoins joinMaps

// B) health write ONLY (no pos write)
elementJoins <- 0; joinMaps <- 0
CMap.addOrUpdate 7 50 healths
AMap.getValue views |> ignore
printfn "B) health write    : elementJoins=%d joinMaps=%d" elementJoins joinMaps

// C) read views TWICE at the same generation (second read should be O(1))
elementJoins <- 0; joinMaps <- 0
AMap.getValue views |> ignore
AMap.getValue views |> ignore
printfn "C) settle re-reads : elementJoins=%d joinMaps=%d" elementJoins joinMaps
