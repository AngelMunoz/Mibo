module Mibo.Adaptive.Demo.Telemetry

// ── Demo instrumentation ─────────────────────────────────────────────────────
// Recompute counters. Each projection bumps its counter when it actually
// recomputes — not when it is forced and found clean. The sim output prints
// these so the dirty tracking is visible in numbers.

let mutable ballRect = 0
let mutable leftPaddle = 0
let mutable rightPaddle = 0
let mutable scoreLabel = 0
let mutable clockLabel = 0
let mutable threat = 0

let print (totalFrames: int) (pausedFrames: int) (allocatedPerFrame: int64) =
  printfn "\n═══ telemetry: %d frames forced ═══" totalFrames

  printfn
    "  ballRect          recomputed %3dx  — the ball moved every live frame"
    ballRect

  printfn
    "  leftPaddleRect    recomputed %3dx  — only while the paddle moved"
    leftPaddle

  printfn
    "  rightPaddleRect   recomputed %3dx  — only while the paddle moved"
    rightPaddle

  printfn
    "  scoreLabel        recomputed %3dx  — only when the score changed"
    scoreLabel

  printfn "  threat            recomputed %3dx  — follows the ball" threat

  printfn
    "  clockLabel        recomputed %3dx  — depends on the time root: every frame"
    clockLabel

  printfn
    "\n═══ paused phase: %d frames forced, 0 sim recomputes, %d B/frame allocated ═══"
    pausedFrames
    allocatedPerFrame

  printfn
    "   (the allocation is the clock label's string — a time-dependent projection\n    recomputes every frame, because the runner keeps advancing the time root;\n    the sim's data projections recompute 0x while paused and allocate nothing)"
