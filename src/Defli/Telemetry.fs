module Defli.Telemetry

// ── Demo instrumentation ─────────────────────────────────────────────────────
// Recompute counters. Each projection bumps its counter when it actually
// recomputes — not when it is forced and found clean. Per-key counters
// (mapA joins) count element recomputes; whole-node counters (filters,
// scalar maps) count node recomputes. The sim output prints these so the
// dirty tracking is visible in numbers at game scale.

let mutable viewsJoin = 0
let mutable aliveFilter = 0
let mutable bossPositions = 0
let mutable effectiveDef = 0
let mutable banner = 0
let mutable gameOver = 0
let mutable homingJoin = 0
let mutable suppression = 0
let mutable rangeRing = 0
let mutable placementPreview = 0

let print (totalFrames: int) (pausedFrames: int) (allocatedPerFrame: int64) =
  printfn "\n═══ telemetry: %d frames forced ═══" totalFrames

  printfn
    "  Views join        recomputed %6dx  per-enemy element recomputes"
    viewsJoin

  printfn
    "  Alive filter      recomputed %6dx  — enemies spawn/die/move"
    aliveFilter

  printfn
    "  BossPositions     recomputed %6dx  — boss-only chooseA"
    bossPositions

  printfn
    "  Homing join       recomputed %6dx  per-projectile element recomputes"
    homingJoin

  printfn
    "  Suppression       recomputed %6dx  — boss aura spatial join"
    suppression

  printfn
    "  EffectiveDef      recomputed %6dx  — tower upgrades only"
    effectiveDef

  printfn "  Banner            recomputed %6dx  — wave banner text" banner
  printfn "  GameOver          recomputed %6dx  — lives <= 0" gameOver
  printfn "  RangeRing         recomputed %6dx  — hover-only" rangeRing
  printfn "  PlacementPreview  recomputed %6dx  — hover-only" placementPreview

  printfn
    "\n═══ paused phase: %d frames forced, 0 recomputes, %d B/frame allocated ═══"
    pausedFrames
    allocatedPerFrame

  printfn
    "   (nothing in the world depends on the time root, so a paused frame\n    forces the graph to pure version checks — the whole game settles)"
