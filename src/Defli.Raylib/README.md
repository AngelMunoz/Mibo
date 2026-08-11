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

## 6. Post-joinOn assessment (2026-08-10, second trace)

**Scope:** a second long windowed session over the same wave range (20–35,
576.3 s, game fully warm) captured AFTER the projections moved from
`mapA` + `tryFind` to `AMap.joinOn` (commit `f74239d`, AdaptiveSlop PR #19
branch `feat/joinon-groupby-reductions`). Same capture method, same census —
directly comparable to sections 2–4.

### 6.1 Macroscopic — the same census, before → after

| Metric | Pre-joinOn (518.3 s) | Post-joinOn (576.3 s) |
| --- | --- | --- |
| Busy samples (≈ ms busy) | 37 080 (7.2 % wall, 1.19 ms/frame) | 19 167 (3.3 % wall, 0.55 ms/frame) |
| AdaptiveSlop samples | 19 410 (52.3 % busy, 3.7 % wall, 0.62 ms/frame) | 6 297 (32.9 % busy, **1.1 % wall, 0.18 ms/frame**) |
| zeroCreate samples | 3 827 (10.3 % of busy) | 1 219 (3.2 % of busy) |
| GC frames / pushMapDelta / OnDeltas | 0 / 0 / 0 | 0 / 0 / 0 |
| Densest minute (busy share) | 10.5 % | 5.0 % |
| AdaptiveSlop share per minute | flat 45–57 % | flat 31–37 % |

Session-mix note: the post-joinOn session was NOT lighter — it was heavier on
the sim side (more towers placed: `Towers.tick` absolute samples grew 4 528 →
5 492; more projectiles in flight: the homing-updates loop, measured as the
`List<ValueTuple<int, ProjectileRow>>.AddWithResize` leaf, grew ~1 520 →
3 930). Total busy still halved. The adaptive collapse is therefore not a
lighter-session artifact.

### 6.2 Microscopic — per node, before → after

| Chain | Pre (samples, % busy) | Post (samples, % busy) |
| --- | --- | --- |
| **Homing join** (all machinery) | 10 570 (28.5 %) | **~184 (0.7 %)** |
| ├─ projection lambda (game code) | 7 761 (20.9 %) | 40 (0.2 %) |
| └─ per-key `voption<HomingView>` reads | 1 926 (5.2 %) | 19 (0.1 %) |
| **Views join** (3-way, two JoinMapNodes) | 2 310 (6.2 %) | 674 (3.5 %) |
| **BossPositions** | 1 907 (5.1 %) | 70 (0.4 %) |
| **Suppression** (filter + count chain) | 1 961 (5.3 %) | ~244 (1.3 %) |
| **Alive chain** (filter + count) | 4 539 (12.2 %) | 813 (4.2 %) |
| **EffectiveDef** | 1 | 1 |
| Join right-side lookups (MapLookupNode) | 93 (0.2 %) | 473 (2.5 %) |

Reading: the joinOn swap removed the per-key subgraph rebuild — the measured
2.1 MB/op allocation sink. The Homing join, the #1 cost line and the one
scaling with the session's hottest entity, collapsed 40× (10 570 → 184
samples) while the projectile load grew. What replaced the rebuild: the swap
(cell re-apply, ~0 samples) and the read-time gate (the right-side lookup
re-syncs — Health 79, Motion 326, EnemyDef 68 samples; the Motion lookup
pays a voption equality per enemy per frame). The scan/equality work in the
Views join-2 (ScanElements 29, `GenericEqualityComparer` on the join-1 struct
89) is the new fixed cost of the 3-way composition — an order of magnitude
below the old rebuild.

### 6.3 The occasional slowliness — investigated

The FPS counter stayed at 60; the user felt rare sluggishness. The CPU trace
answers:

- **No long busy period exists on the game thread.** The longest busy
  cluster (consecutive samples with < 25 ms gaps) is 10 samples ≈ 10 ms of
  busy, at t ≈ 545.9 s (the wave-35 climax). Average busy per frame is
  0.55 ms.
- **The 733.5 ms "CPU_TIME span" is a sampling artifact** — it contains
  exactly 1 sample. The same artifact shape (650.8 ms span) exists in the
  pre-joinOn trace. Span durations are unusable; only samples are truth.
- **The 413 idle gaps ≥ 120 ms** (50 of them ≥ 250 ms, 2 ≥ 500 ms) are gaps
  between BUSY samples — the thread was waiting, not computing. They cluster
  in the boot minutes (115 in minute 0 — asset load / shader compile) and
  decline to 2–18 per minute by minute 7. Consistent with the trace
  collector's event-writing pauses (the user's hypothesis) — this session
  showed no FPS dip, unlike earlier collector interference.
- The occasional heavy frames that DO exist are game-side: the dense-minute
  clusters sit in `Towers.tick` (O(towers × alive) target acquisition,
  CPU_TIME leaf 3 108 samples) and the Projectiles homing loop (3 930) —
  the sim's own scans at the wave climax, not adaptive nodes.

Verdict on the slowliness: no stall in the adaptive machinery — its total is
0.18 ms/frame. The felt sluggishness is either the dense sim frames (5–10 ms
busy at the climax, still inside the 16.7 ms budget) or the collector, and
the two are distinguishable with a short no-collector session or a
per-frame-max timer in the shell loop.

### 6.3.1 GC — measured from the GC events, not the sample census

The "GC frames: 0" rows above come from the sample census
(`probe-structure.fsx` greps the frame-name table for `System.GC`/
`PollGC`/`WriteBarrier`/`Garbage`). That check only proves no GC *code* ever
appeared on a game-thread stack. It does NOT measure GC pauses: a blocking
(stop-the-world) GC **suspends** the game thread, a suspended thread produces
no samples, and the GC work runs on the GC threads (which have no profile in
this trace) — a GC pause looks like an idle gap, indistinguishable from
vsync/collector waits.

The direct measurement reads the gc-verbose GC events from the nettrace
(`tools/gcprobe`, a TraceEvent console app — GCStart/GCStop/SuspendEEStart/
RestartEEStop):

| | count | total | avg | max |
| --- | --- | --- | --- | --- |
| GCs (186 blocking, 2 background) | 188 | — | one per ~3 s | — |
| GC work (Start→Stop) | 186 | 52.9 ms | 0.28 ms | 0.54 ms |
| STW pause (SuspendEEStart→RestartEEStop) | 188 | 62.6 ms | 0.333 ms | **0.69 ms** |

All 188 pauses are < 1 ms; the worst pause is 0.69 ms = 4 % of one frame
budget. Total stop-the-world across the 576 s session is 62.6 ms (0.01 % of
wall). **The GC cannot cause the felt slowliness** — the pauses are two
orders of magnitude below what the frame budget would notice, and three
below the 250–1000 ms sampling gaps. The allocation rate that drives the GCs
is the ~1.2 k zeroCreate samples (~3.2 % of busy) — the frequency is normal
for a small-heap game.

**Did GC increase vs the original? No — it decreased.** Same probe, run over
the seven original Defli nettrace files in `E:\Defli`:

| Trace (E:\Defli) | wall | GCs | GC/s | STW total | STW max |
| --- | --- | --- | --- | --- | --- |
| 210309 (Phase 3 era) | 222.0 s | 17 | 0.08/s | 5.4 ms | 1.07 ms |
| 211933 (Phase 4 era) | 247.7 s | 20 | 0.08/s | 6.9 ms | 1.16 ms |
| 093206 | 201.3 s | 22 | 0.11/s | 8.2 ms | 0.97 ms |
| 111124 (Phase 5 era) | 321.4 s | 185 | 0.58/s | 69.7 ms | 0.89 ms |
| **092915 (Trace A, regression peak)** | 368.7 s | 859 | 2.33/s | 2 800 ms | **37.2 ms** |
| 102819 (Trace B) | 208.9 s | 35 | 0.17/s | 12.2 ms | 0.65 ms |
| **230909 (waves 33–35 baseline)** | 50.25 s | 60 | 1.19/s | 31.0 ms | 2.05 ms |
| **Port (post-joinOn)** | 576.3 s | 188 | **0.33/s** | 62.6 ms | **0.69 ms** |

Reading: the port collects 3.6× less often than the same-wave baseline
(0.33/s vs 1.19/s) with a 3× smaller worst pause (0.69 vs 2.05 ms) — GC
activity is driven by allocation, and the port allocates less (the dead
write side + the joinOn swap: no per-key rebuild, the original's 2.1 MB/op).
Trace A (the pre-lazy regression-peak build) shows what allocation-driven GC
looks like when it IS a problem: 859 GCs, 2.8 s of stop-the-world, and
**37 ms pauses** — visible stutter, three orders above the port's worst. The
port's 0.69 ms worst pause is below every original trace's worst except the
two lightest early sessions.

Note: the pre-joinOn Mibo nettrace was overwritten by this recording, so the
pre/post joinOn GC delta cannot be measured directly; the architecture-level
comparison above is the available evidence, and it shows no GC increase.

### 6.4 Verdict — did the PR help?

**Yes, decisively.** Adaptive wall-share dropped 3.7 % → 1.1 % (0.62 →
0.18 ms/frame) with a heavier session, the allocation drip dropped 3.8 k →
1.2 k zeroCreate samples, and the Homing join — the profiled hot spot the PR
was written for — collapsed from 28.5 % of busy to 0.7 % while projectile
volume grew. The remaining adaptive cost is the read-time gate (lookups +
scan equality) at ~1–2 orders of magnitude below the rebuild it replaced.

Watch items (unchanged, now smaller): Suppression's O(towers × bosses)
re-scan (1.3 %, by design), the per-tower transient reads (cooldownA/targetA,
grew with tower count), view-side strings (TowersView level labels 665
samples + `AssetsService.resolvePath` 330 samples — texture paths resolved
per frame), and the dense sim frames at the wave climax (game-side target
acquisition + homing loops).
