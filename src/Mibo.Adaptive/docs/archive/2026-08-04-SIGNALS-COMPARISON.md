# Adaptive Computation: FDA vs JS Signals — and the Research to Leverage

> Companion to `ANALYSIS-FDA.md`. This doc answers: how does FDA differ from the JS signals
> ecosystem, how do JS signals actually do recomputation, is either "more novel," and what
> research can a zero-allocation reimplementation steal from. External claims verified
> against the TC39 proposal text, Solid's `signal.ts`, MobX docs, and the cited papers
> (August 2026).

---

## 0. The one-paragraph answer

FDA and JS signals are **two practical descendants of the same two research traditions**:
*Functional Reactive Programming* (FRP — Elliott & Hudak's Fran, 1997) and *Self-Adjusting
Computation* (SAC — Acar, early 2000s; refined by Adapton, 2014–2015). They share the same
skeleton: dirty flags, dependency edges discovered during evaluation, batched/topological
propagation, glitch-freedom. The differences are in **emphasis and capabilities**, not
fundamental novelty:

- **JS signals** optimized for *UI ergonomics and fine-grained DOM updates*: ownership/
  disposal scopes, a rich scheduling vocabulary, and — since ~2022 — convergence on
  **push-marking + pull-evaluation, exactly FDA's hybrid model**. They are
  **scalar-only** — collections are opaque values.
- **FDA** optimized for *incremental data transformation*: pull-lazy evaluation, weak-reference
  GC, and — its real differentiator — **element-level incremental collections** via the
  Traceable/Reader/History machinery.

For a zero-allocation game-loop library, the most valuable things to steal are: **element-level
deltas from FDA itself**, **edge representation and disposal from the modern JS libs**
(alien-signals/Solid), and **the theory from SAC/Adapton, incremental λ-calculus, and
differential dataflow** (see §4).

---

## 1. How JS signals do recomputation (the mechanism)

Mature signal libraries converged on one design; the TC39 proposal
(`github.com/tc39/proposal-signals`, Stage 1 since April 2024, actively developed with input
from Solid/Vue/Preact/MobX/Angular/Svelte/Ember authors) is its written-down form.

### 1.1 The node + state machine

A computed node carries a state (TC39 proposal names):

| State | Meaning |
|---|---|
| `clean` | value is current; return cache |
| `checked` | an *indirect* source changed; must verify immediate sources before deciding to recompute |
| `dirty` | an immediate source changed (or never computed); recompute on next read |
| `computing` | re-entrancy guard while the callback runs |

On `.set()`, the **immediate** sinks go `dirty`; everything **transitively** downstream goes
`checked`. On read, a `checked` node re-evaluates its sources; if none actually changed value
(per the `equals` predicate), it flips back to `clean` without recomputing. This is the
equality/elision tier: "marked dirty but value didn't change → don't propagate."

Note: AdaptiveSlop already has a three-state version as `DirtyState.Clean/Dirty/MaybeDirty`
(`src/AdaptiveSlop.Core/Library.fs:22-28`) — the right model. FDA performs the equivalent
elision inside compute via `cheapEqual`.

### 1.2 The edge model: strong, bidirectional, lazily-built

- Reading a signal inside an active computation registers a **strong** edge both ways
  (`signal.observers`/`sinks` ← `computation`; `computation.sources` → `signal`).
  Solid stores them as parallel arrays with slot indices for O(1) removal.
- Contrast FDA: edges point one way (child→parent, in `Outputs`) and are **weak**.
- Both worlds **tear down and rebuild edges on every recompute** of a node (Solid's
  `cleanNode`, TC39's "recalculate dirty computed Signal", FDA's `EvaluateAlways`).

### 1.3 Write → mark → settle → evaluate

A write does not immediately recompute the world:

1. **Push-marking (synchronous):** the write marks transitive dependents `dirty`/`checked`
   (TC39), or queues them (Solid's `Updates`, MobX's pending derivations).
2. **Glitch-free settling:** queues drain so each node settles only after its sources did —
   Solid's `runTop`/`lookUpstream`, MobX's two-pass observer counting, FDA's `Level`-ordered
   priority queue. Same idea everywhere: no node reads from the future.
3. **Evaluation:** depends on the library's eager/lazy stance (§1.4). Side effects
   (`createEffect`, MobX reactions, TC39 `Watcher.notify`) are the exception — they are
   always scheduled by the marking phase, deferred per framework policy (Solid's `Effects`
   queue runs after `Updates`; TC39 `notify` runs synchronously inside `.set()` but may not
   read signals, forcing schedule-later).

### 1.4 Eager vs lazy: the ecosystem converged on FDA's model

The honest split, by derived-value behavior:

| Library | Derived recompute policy |
|---|---|
| **S.js** | Eager, unconditional: every computation re-runs synchronously when a source changes |
| **Solid (`createMemo`)** | Eager: memos run at creation and re-run on every source change **regardless of observers** (verified in `signal.ts`: `writeSignal` pushes all subscribed pure computations into `Updates`; no observer-count check exists) |
| **MobX (`computed`)** | **Lazy**: recomputes only when read while dirty; unobserved computeds are fully suspended (stop tracking). Only *reactions* are eager |
| **Preact Signals, Vue `computed`, Angular signals, Svelte 5 runes** | **Lazy**: push-marking on write, re-evaluate on `.value` read if dirty |
| **TC39 proposal** | **Lazy, explicitly**: "Computed Signals are lazy, i.e., pull-based: they are only re-evaluated when they are accessed" |

So the modern consensus core is **push-mark + pull-evaluate — precisely FDA's hybrid**.
Solid and S.js are the eager outliers, not the norm. The remaining real difference from FDA
is the side-effect tier: JS libs give effects/watchers a first-class synchronous notification
during marking (TC39 `Watcher`, Solid `Effects`); FDA's equivalent is `AddMarkingCallback` +
transaction finalizers (`AddCallback`), same shape, less ergonomic.

**Implication for a game loop:** pull-lazy derivation is what you want — you read N things
per frame and only pay for those N. The JS ecosystem independently arrived at the same
answer; this is a strong cross-validation of FDA's (and AdaptiveSlop's) default.

### 1.5 Ownership / disposal scopes (Solid's real contribution)

Solid (following S.js) maintains an **ownership tree**: every computation created inside
`createRoot`/`createEffect` is registered in its owner's `owned` array; disposing the owner
recursively tears down children and detaches all edges (`cleanNode`). This gives **leak-free
lifecycle without weak references**.

FDA instead uses weak references in `Outputs` so unreachable graph fragments get GC'd. More
automatic, but pays weak-ref resolution on every mark plus cleanup heuristics. **For a
bounded game-loop graph, strong edges + explicit disposal is simpler and faster** — and is
what AdaptiveSlop's `IObservation` model already reaches toward.

---

## 2. The six concrete differences, ranked by how much they matter to you

1. **Collection-level incrementalism.** FDA: `aset/alist/amap` process *changed elements
   only* via delta-stream readers. JS signals: a "list" is a signal holding an array; any
   derived array recomputes wholesale. **This is FDA's genuine, large advantage** — no JS
   reactivity core has it. (Frameworks add keyed-list reconcilers on top, but that's a
   rendering concern, not a reactivity-core one.)

2. **Effect scheduling.** JS libs treat side effects as first-class scheduled nodes
   (`Updates` vs `Effects` queues, `Watcher.notify`). FDA treats observation as an add-on
   callback. A game loop wants a smaller vocabulary: sync-only, frame-batched.

3. **Weak-ref DAG (FDA) vs ownership-tree + strong edges (JS).** Determines leak strategy
   and per-mark cost. See §1.5.

4. **Scheduling vocabulary.** Solid offers `createMemo` (sync), `createEffect` (deferred),
   `createComputed`, `on(deps, fn, {defer})`, `untrack`, `batch`. FDA offers `transact`,
   `AddMarkingCallback`, `AddCallback`. FDA's is sufficient; JS's is UI-tuned.

5. **Edge representation and reuse.** Modern JS cores store edges as **flat arrays or
   doubly-linked lists with slot indices** (Solid's `sources`/`sourceSlots`; Preact's and
   alien-signals' linked `Link` nodes) — cheaper to build, diff, and GC than set-based
   edges. Experimental cores (Reactively, alien-signals — the design that became Vue 3.4's
   reactivity) additionally **reuse edge-link objects across recomputes** instead of
   reallocating. **Stealable for the zero-alloc goal.** (Note: no shipping Solid version
   skips edge teardown for stable dependency sets — `cleanNode` is unconditional. The reuse
   optimization lives in Reactively/alien-signals, not Solid.)

6. **Equality/elision discipline.** TC39's `checked` tier, FDA's `cheapEqual`, Solid's
   `equals` option all express "marked dirty but value unchanged → stop propagation."
   Same idea at different pipeline points.

---

## 3. Is either side "more novel"?

**No.** Both are engineering distillations of the same research.

- The **glitch-free topological propagation** in both descends from FRP (Fran 1997; Yampa;
  Elliott's *Push-pull FRP*, 2009).
- The **changeable-cell + change-propagation** model (`cval`/`modref`, `transact`) in FDA is
  straight from Self-Adjusting Computation (Acar).
- The **fine-grained auto-tracking** pattern in JS (S.js → Solid → TC39) is the practical
  reactivity lineage, itself citing FRP.

Where each side is *genuinely ahead*:

- **FDA ahead:** element-level collection incrementalism (Traceable/Reader/History) and
  level-ordered glitch-freedom formalized for dynamic deps (`LevelChangedException`).
- **JS ahead:** ownership/disposal ergonomics, effect scheduling, edge-representation
  micro-optimization (linked-list edges, link reuse), and deployment at scale.

---

## 4. Research to leverage (ranked by relevance to your zero-alloc goal)

### ★★★ Self-Adjusting Computation — Acar et al. (POPL 2002; Acar PhD 2005)
The theoretical source of FDA's `cval`/`aval`/`transact`:
- **Modifiable (modref)** = `cval`. **Change propagation** = the mark+recompute cycle.
- **Complexity bounds**: change propagation costs `O(|δ| · work-per-node)` where `|δ|` is the
  changed region — the formal statement of "recompute only the dirty subtree," i.e. the
  perf target your implementation must provably meet.
- "Adaptive Functional Programming" (POPL 2002) is the readable entry point.

### ★★★ Adapton — Hammer, Phang, Hicks, Foster (PLDI 2014); Hammer, Dunfield et al. (OOPSLA 2015)
http://adapton.org — composable, demand-driven incremental computation.

Key ideas directly usable:
- **Lazy vs eager strategies as explicit, interchangeable engines** (its implementations ship
  both) — a formal framing of the lazy/eager axis you're choosing on.
- **Reference-counted DCG nodes** — eager reclamation of graph nodes without weak refs; the
  principled version of the strong-edge + disposal model.
- **Nominal memoization** ("Incremental Computation with Names", OOPSLA 2015 — *not* the 2014
  paper): giving computations **names** enables reusing results across divergent
  input-change histories (e.g. a value oscillating A→B→A). Neither FDA nor JS signals do
  this. Optional for a game loop, but a real capability gap.
- The Rust implementation (adapton-lab.rust) is instructive for arena/pooled node
  representation.

### ★★ Reactively (Milo M., 2022) → alien-signals → Vue 3.4 — the practical JS algorithm
The most directly useful modern reference for the *scalar* core:
- **Graph coloring** instead of version counters: mark with a color/epoch, no per-node
  counters. (`milomg.dev/2022-12-01/reactivity`)
- **Doubly-linked-list edges with slot reuse** — no arrays reallocated per recompute; this is
  the closest existing art to your zero-alloc edge goal, and it's battle-tested (it became
  Vue 3.4's reactivity system).
- The TC39 proposal's algorithm section is the spec-quality write-up of the same design,
  including the two-tier lazy-computed + synchronous-`Watcher` split.

### ★★ Incremental λ-calculus — Cai, Giarrusso, Rendel, Ostermann (PLDI 2014)
The cleanest theory for FDA-style collection deltas:
- **First-class change values**: every type has a change type; functions have function-changes.
  This is what `Traceable<'State,'Delta>` is, stated as a calculus, with a correctness
  theorem (derivatives compute the difference).
- Read it to know exactly what laws your pooled flat-array delta monoid must satisfy —
  especially when you hand-roll `tapplyDelta`/`tcomputeDelta` for BCL collections.

### ★★ Differential dataflow (McSherry et al., Naiad/SOSP 2013) and DBSP (Budiu et al.)
The theory behind FDA's collection model, at production scale:
- **Difference collections as a monoid indexed by time** — exactly FDA's `tmonoid`, with a
  rigorous version algebra. DBSP restates this as a streaming algebra with a `z⁻¹` delay
  operator; either is a good formal basis for collection combinators.
- **Arrangements**: key-indexed, version-stamped collection state making incremental
  joins/group-by `O(δ)`. Relevant only if you want joins/group-by beyond map/filter —
  probably out of scope for a game loop, but know it exists before you design `cmap`
  combinators.

### ★★ Push-Pull FRP — Elliott (Haskell Symposium 2009)
The explicit articulation of the hybrid push/pull model. Read for the glitch-freedom argument
and the justification of pull-lazy defaults — it's the FRP position, not an FDA accident.

### ★ S.js source (~1k LOC, Adam Haile) + Solid internals (Carniato's blog series)
The practical eager-side references. S.js is the cleanest minimal synchronous engine; the
"SolidJS: Reactivity to Rendering" series explains ownership trees and the Updates/Effects
split. Steal: ownership disposal. Skip: unconditional eagerness.

---

## 5. Concrete techniques to lift into the reimplementation

Mapped to the gaps identified in `ANALYSIS-FDA.md`:

| Gap (from ANALYSIS-FDA) | Steal from | What it gives you |
|---|---|---|
| Weak-ref `Outputs` resolution cost | Solid ownership tree + Adapton ref-counted DCG | Strong edges, explicit `Dispose`, no weak-ref churn |
| Edge arrays reallocated/rebuilt every recompute | alien-signals / Reactively linked-list edges with slot reuse | O(1) edge teardown, reusable link objects — kills per-recompute edge allocation |
| `OutOfDate` boolean too coarse | TC39 `checked` tier (AdaptiveSlop already has `MaybeDirty`) | Cheap "maybe" push; escalate to recompute only on verified value change |
| Per-mark queue entry alloc | Level-bucketed queue (justified by SAC's `O(δ)` bound: only the dirty frontier is ever enqueued) | No per-node entry alloc; correctness target stays intact |
| Observation ergonomics | TC39 `Watcher` two-tier split | Lazy computeds + synchronous mark-notification for the frame's effect tier — same shape as FDA's `AddMarkingCallback`, cleaner contract |
| Collection recomputation not `O(δ)` for joins/group-by | Differential dataflow arrangements / DBSP (only if needed) | Key-indexed versioned state for `O(δ)` beyond map/filter |
| Correctness contract for hand-rolled BCL deltas | Incremental λ-calculus change types | The exact laws your delta monoid/`tapplyDelta` must satisfy |
| Recompute of unchanged-but-oscillating values (A→B→A) | Adapton nominal memoization (2015) — optional | Cross-history memo hits; likely skip for a game loop |

---

## 6. TL;DR

- FDA ≈ "SAC (scalar) + differential-dataflow-lite (collections), pull-lazy, weak-ref GC."
- Modern JS signals ≈ "FRP (scalar only), **the same push-mark/pull-evaluate hybrid**, strong
  edges + ownership-tree disposal, first-class effect scheduling." Solid/S.js are the eager
  outliers.
- Neither is more novel; they're co-descendants. FDA's real edge is **collection
  incrementalism**; JS's real edge is **lifecycle ergonomics and edge-representation
  micro-optimization**.
- Decision inputs for AdaptiveSlop: keep FDA's pull-lazy default (the JS ecosystem
  independently converged on it) and its collection deltas; take disposal + edge
  representation from Solid/alien-signals; take the correctness contracts from SAC,
  incremental λ-calculus, and (only if you need joins) differential dataflow/DBSP.
