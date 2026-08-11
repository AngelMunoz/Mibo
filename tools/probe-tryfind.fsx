#r "../AdaptiveSlop/src/AdaptiveSlop.Core/bin/Debug/net10.0/AdaptiveSlop.Core.dll"
open AdaptiveSlop.Core

let data = CMap.ofSeq [ for i in 0..9 -> i, i * 10 ]
let mutable transforms = 0

let t =
  data
  |> AMap.tryFind 3
  |> AVal.map(fun v ->
    transforms <- transforms + 1
    v)

AVal.getValue t |> ignore // warm
transforms <- 0

// 1) an UNRELATED key changes (key 7, we watch key 3)
CMap.addOrUpdate 7 999 data
AVal.getValue t |> ignore

printfn
  "unrelated key write → transform ran %d time(s) (naive expectation: 0)"
  transforms

// 2) the WATCHED key written with an EQUAL value (source equality filter)
transforms <- 0
CMap.addOrUpdate 3 30 data // 30 == 3*10, equal
AVal.getValue t |> ignore

printfn
  "equal-value write   → transform ran %d time(s) (expect 0 — source filter)"
  transforms

// 3) the watched key REMOVED (presence flips)
transforms <- 0
CMap.remove 3 data
AVal.getValue t |> ignore

printfn
  "key removed          → transform ran %d time(s) (expect 1 — presence changed)"
  transforms

// 4) settled re-read
transforms <- 0
AVal.getValue t |> ignore
printfn "settled re-read      → transform ran %d time(s) (expect 0)" transforms
