# Defli.Raylib — Adaptive Architecture Trace Assessment

**Date:** 2026-08-10
**Status:** Analysis only. No code changes.
**Scope:** The adaptive port of the Defli tower-defense game (windowed raylib
frontend) at end-game load. Trace `defli-adaptive-sim.speedscope.json`
(518.3 s, waves 20–35, game fully warm, towers maxed, enemies active). The
session is the windowed `AdaptiveRaylibGame` loop — the same loop shape the
original MVU game used, so the comparison is apples-to-apples at the shell
level.

This document answers two questions:

1. **Microscopic** — how much does the adaptive machinery cost the game?
2. **Macroscopic** — what is the whole game doing, and what share of that
   whole is adaptive?

The reference is the original Defli evaluation, `2026-08-09-wave-33-35-trace.md`
in `E:\Defli\docs` (the MVU original at waves 33–35, Trace C).

---

## 1. Method note — the CPU_TIME frame in this trace

The speedscope file uses the evented format with a `CPU_TIME` pseudo-frame
(dotnet-trace run markers). The exclusive-interval tables of
`analyze-trace.fsx` attribute 100 % of the wall to `CPU_TIME` and are NOT
usable for this trace. The **sample census** (one sample per distinct event
timestamp, each ≈ 1 ms of busy time) is the valid measurement — it is the
same method the Defli baseline docs used and it reconstructs the full stack
per sample, immune to the `CPU_TIME` marker. All percentages below are
sample-based unless stated otherwise.

Structure probe (`tools/probe-structure.fsx`), both traces:

| | Baseline (waves 33–35) | Adaptive (waves 20–35) |
| --- | --- | --- |
| Game thread events | 72 392 | 322 524 |
| Distinct timestamps (samples) | 7 038 | 37 080 |
| Wall time | 50.25 s | 518.3 s |
| Busy share of wall | 14.0 % | 7.2 % |
| GC-related frames | 0 | 0 |

Both traces share the same event structure, so the census is directly
comparable. The adaptive session is 10× longer and includes the load ramp
waves 20 → 35 (the per-minute load grows from ~2 000 to ~6 300 samples/min),
which makes the 7.2 % busy-share a mixed-session average, not a peak slice.

## 2. Microscopic — what adaptive costs the game

**Headline: 3.7 % of wall time is AdaptiveSlop — 0.62 ms/frame at 60 FPS.**
At the game's own densest minute (minute 6, ~10.5 % busy) the adaptive wall
share is ≈ 5.2 %.

| Node / chain | % of busy | samples | Notes |
| --- | --- | --- | --- |
| **Homing join** `ElementMapNode<ProjectileRow, HomingView>` | 28.5 % | 10 570 | the #1 consumer, same as baseline |
| ├─ projection lambda (`$Projections+-ctor@49-3`) | 20.9 % | 7 761 | game code, linear in projectiles |
| └─ per-key `voption<HomingView>` reads | 5.2 % | 1 926 | node reads, O(1)/key |
| **Alive chain** `FilterMapNode` + `MapCountNode` | ~6.3 % | 2 318 + 2 221 | `Waves.tick` count read |
| **Views join** `ElementMapNode<Vector2, EnemyView>` | 6.2 % | 2 310 | per-enemy lambda 4.4 % + reads 1.4 % |
| **BossPositions** `ElementMapNode<Vector2, Vector2>` | 5.1 % | 1 907 | per-enemy lambda 3.7 % + reads 1.3 % |
| **Suppression** chain (per-tower filter/count over BossPositions) | 5.3 % | 1 961 | the O(towers × bosses) spatial re-scan, by design |
| **EffectiveDef** `ElementMapNode<TowerStatic, TowerDef>` | 0.0 % | 1 | upgrades only, dormant at end-game |
| **MapLookupNode** scalar reads (Health/Motion) | 0.2 % | 93 | the lazy scalar escape, O(1)/key |
| **Towers.tick** (total) | 12.2 % | 4 528 | own sim 7.9 % (CPU_TIME leaf), cooldownA 1.7 %, targetA 0.9 % |
| **Enemies.tick** | 4.2 % | 1 556 | mostly game-side `List.AddWithResize` (4.1 %) |
| **Renderer2D.Draw** (view pass) | 6.5 % | 2 406 | reads the precomputed frame, O(1) |

Microscopic verdict:

- The adaptive machinery is ~half of the busy time but the busy time is
  small. AdaptiveSlop is 52.3 % of busy samples (19 410 of 37 080) = 3.7 % of
  wall = 0.62 ms/frame. The game has ~14× headroom in the 16.7 ms budget
  (1.19 ms/frame total busy).
- The #1 line item — the Homing join at 28.5 % — decomposes into 20.9 %
  **game lambda code** (would cost the same under any evaluation strategy)
  and 5.2 % per-key node reads, leaving ~2.4 % of busy for the join machinery
  (drain/process) itself. The game is paying for projectile volume, not for
  reactive plumbing.
- **The write side stays dead**: 0 `pushMapDelta` / `OnDeltas` samples — the
  Trace-A regression shape never reappears. The lazy design holds at
  end-game load.
- **No GC**: 0 GC frames in the busy profile, same as the baseline.
- **The allocation drip is flat and linear in entities**: 3 827 zeroCreate
  samples ≈ 10.3 % of busy (HomingView 935, voption<HomingView> 919, Single
  617, voption<int> 340, EnemyView 295, voption<Vector2> 246, __Canon 244,
  voption<EnemyView> 231) — the per-node `Recompute` arrays, ~103 /s per
  busy-second (baseline ~85 /s). Grows only with entities.
- **Suppression is the only chain that got relatively bigger vs the
  baseline** (5.3 % vs ~1 %): the port forces BossPositions → Suppression in
  router order every frame (the documented lazy-settle ordering rule), and
  this session has more maxed towers. Still 0.06 ms/frame — a watch item,
  not a cost problem.

### 2.1 vs the original (per busy-second, same census)

| Node / chain | Baseline waves 33–35 | Adaptive waves 20–35 |
| --- | --- | --- |
| Homing join | 19.0 % (1 336) | 28.5 % (10 570) |
| Alive chain | 11.7 % (823) | ~6.3 % (2 318+2 221) |
| Views join | 6.0 % (421) | 6.2 % (2 310) |
| BossPositions | 5.0 % (353) | 5.1 % (1 907) |
| Suppression aura | ~1 % (72) | 5.3 % (1 961) |
| Towers.tick | 10.3 % (723) | 12.2 % (4 528) |
| Enemies.tick | 3.4 % (243) | 4.2 % (1 556) |
| MapLookupNode reads | 0.2 % (~19) | 0.2 % (93) |
| zeroCreate/Create | ~590–610 (8.7 %) | 3 827 top-8 (10.3 %) |

Reading: the shapes are the same. The Homing join is the top line item in
both (the lambda dominates); the Alive chain is cheaper in the port because
the count node is hoisted (the original rebuilt a fresh node per frame). The
per-busy-second absolute rates are the same order of magnitude everywhere.
No quadratic term appears at 15 waves of warm end-game load.

## 3. Macroscopic — what the whole game is doing

| Activity | % of busy (samples) |
| --- | --- |
| `AdaptiveHeadless.Step` (router + frame force) | 88.9 % (32 963) |
| ├─ `Router.step` (systems in Kimo order) | 46.6 % (17 278) |
| ├─ `buildFrame` (Force — the Homing drain is 28.5 % of it) | 29.0 % (10 766) |
| `Renderer2D.Draw` (view pass over the packed frame) | 6.5 % (2 406) |
| `Input.Poll` | 0.1 % (34) |
| Strings (HUD $"..." + AssetsService.resolvePath) | ~2.8 % (1 043) |
| GC / write-dispatch / idle-wait | 0 / 0 / ~1 |

vs the original: `ElmishLoop.TickFrame` 57.3 % + `Renderer2D.Draw` 23.8 %.
The port's view pass is 4× cheaper (6.5 % vs 23.8 %) because the frame is
forced once and drawn from a packed struct — the draw path is no longer a
per-frame projection rebuild. The sim+force still dominate the frame, which
is the design.

### 3.1 The whole-game cost table (wall-share, apples-to-apples)

| | Baseline (waves 33–35) | Adaptive (waves 20–35) |
| --- | --- | --- |
| Game busy (wall) | 14.0 % (2.33 ms/frame) | 7.2 % (1.19 ms/frame, mixed session) |
| AdaptiveSlop busy-share | 40.8 % | 52.3 % |
| **AdaptiveSlop wall-share** | **5.7 % (0.95 ms/frame)** | **3.7 % (0.62 ms/frame)** |
| GC frames | 0 | 0 |
| Frame budget used | ~14 % of 16.7 ms (~7× headroom) | ~7 % of 16.7 ms (~14× headroom) |
| Adaptive budget used | 5.7 % (~17× headroom) | 3.7 % (~27× headroom) |

Reading: the adaptive architecture in a non-MVU shell is **cheaper per
frame than the original MVU shell at comparable warm end-game load** —
0.62 vs 0.95 ms/frame of adaptive work, with zero GC and a dead write side.
The busy-share of wall (52.3 %) looks alarming only because the port removed
most of the non-adaptive shell overhead (no dispatch machinery, no view
projection rebuilds); the denominator shrank, the numerator did not grow.

Session caveat: the adaptive trace is a 518 s mixed ramp (waves 20–35) vs
the baseline's homogeneous 50 s peak slice (waves 33–35). The per-minute
census shows AdaptiveSlop share stays flat at 45–57 % across the whole
session (minute 0 → 8), so the conclusion is not an artifact of wave mixing.

## 4. Verdict

- **Microscopic**: adaptive data drags the game 0.62 ms/frame at warm
  end-game load. The biggest single line is the Homing projection lambda
  (game code), not the library. No GC, no write dispatch, flat allocation
  drip. Suppression re-scan is the only chain that grew (still 0.06 ms/frame).
- **Macroscopic**: the game is 7.2 % busy of wall; adaptive is 3.7 % of wall.
  The port's whole-game cost is lower than the original's at comparable load,
  and the view pass is 4× cheaper because the frame is precomputed.
- **The architecture is worth it**: the adaptive shell costs less than the
  MVU shell it replaces at the heaviest load captured so far, with the same
  per-frame cost shape the original's assessment documented.

Watch items, in order:

1. **Homing lambda (20.9 % of busy)** — game code, linear in projectiles in
   flight; the first line item to grow if projectile volume grows. Fix would
   be cheaper projection math, not library work (same as baseline watch #1).
2. **Suppression (5.3 %, 0.06 ms/frame)** — the per-tower spatial re-scan;
   grows with towers × bosses. The skip-when-no-boss gate stays the cheap
   option if boss-free waves ever matter.
3. **The allocation drip (10.3 % of busy)** — flat per unit of work, linear
   in entities; no action needed.
4. **HUD strings** — the per-frame `$"Gold: ..."` line plus
   `AssetsService.resolvePath` (Path.Combine per asset access) ≈ 2.8 % of
   busy; a view-side micro-optimization candidate, not an adaptive issue.

## 5. Reproduction

```
dotnet fsi tools/analyze-trace.fsx defli-adaptive-sim.speedscope.json
dotnet fsi tools/analyze-subtree.fsx defli-adaptive-sim.speedscope.json
dotnet fsi tools/probe-nodes.fsx defli-adaptive-sim.speedscope.json '<NodeQuery>'
dotnet fsi tools/probe-structure.fsx defli-adaptive-sim.speedscope.json
```

(Tools copied from `E:\Defli\tools` to `E:\Mibo\tools`; `probe-structure.fsx`
and `probe-nodes.fsx` are new. Trace collected with
`dotnet-trace collect --profile gc-verbose -o out.nettrace --name Defli.Raylib`,
converted with `dotnet-trace convert --format Speedscope`.)
