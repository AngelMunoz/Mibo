# Bisect notes — choose2 / two-source zero-allocation failures (7.3) — RESOLVED

Final state: 140/140 tests pass, Debug and Release, after `dotnet fantomas .`.
The two zero-allocation tests pass. The probe tests are removed. The choose2
test is restored to 4 writes per iteration + `Assert.Equal(0L, allocated)`.

## Root causes (measured, IL-verified)

### Cause 1 — reference tuple in the two-source mapping bodies (map side)

`AMap.unionWith`, `AMap.intersect`, `AMap.intersectWith` wrapped the user
function in `fun k lv rv -> match lv, rv with ...`. The two-value match
compiles to a REFERENCE tuple: IL `newobj instance void class
[System.Runtime]System.Tuple`2<voption<'V1>, voption<'V2>>::.ctor` in the
closure `Invoke` (example: `intersectWith@392::Invoke`, IL offset 0x02).

Measured (isolated probe, one node per fresh graph, journals pre-grown):
unionWith drain of 100 entries = 3200 B (32 B per mapping call = one Tuple).
100 x (write + read) = 3200 B. This was the whole choose2-test failure.

Fix: `match struct (lv, rv) with` at the three sites (ValueTuple, no heap).
After: drain = 0 B, cycles = 0 B.

### Cause 2 — generalized class-level lambda materialized per drain (set side)

`UnionSetNode<'T>` had `let identity = fun x -> ValueSome x` in the class
body. The binding generalizes to `'a -> 'a voption`, so F# did not store it
as a field: IL shows `newobj GetValue@234-1(this)` (24 B) inside
`UnionSetNode.GetValue` at EVERY dirty drain.

Measured: 24 B per unionCount read cycle (sU = 2400 B per 100 iterations);
the intersect path (`TwoSourceSetNode`, no lambda) measured 0 B.

Fix: module-level `module private Id = let inline identityV x = ValueSome x`
in SetNodes.fs.

### Cause 3 — residual one-time 24 B

After causes 1+2, the two-source test still measured 24 B once. The Shared.fs
drain/load functions took the mapping as a plain `FSharpFunc` parameter, so
the lambda materialized a closure at the boundary. Making the drain/load
functions `let inline` with `[<InlineIfLambda>]` on the mapping parameter
(loadRefSet, loadPlainSet, loadMap, drainRefSet, drainPlainSet, drainMap,
drainSetPush, drainPlainSetPush, drainMapPush, processChoose2Side,
drainChoose2, drainChoose2Push, loadChoose2) inlines the lambda into the
drain loop: no FSharpFunc object at all. After: 0 B, Debug and Release.
Note: `let inline` with a `byref` parameter compiles on the current SDK —
the old header comment in Shared.fs (FS0412) was stale; modern F# accepts it.

## Discarded hypotheses (do not re-investigate)

1. **The `when`-guard / match-structure hypothesis** (previous session).
   REFUTED: rewriting `match newOut with | ValueSome v when ...` to explicit
   ifs changed nothing (pB/pD/pE byte-identical).
2. **The 24 B `match dict.TryGetValue key with | true, v ->` pattern.**
   REFUTED: the pattern does not occur in any hot path; all call sites use
   the out-param form.
3. **"~16 B/write in the delivery path".** REFUTED: probe pF (raw source,
   100 writes) = 0 B. `GC.GetAllocatedBytesForCurrentThread` calibration
   confirmed the counter works (4024 B for a 1000-int array).
4. **"Journal growth ~256 B".** Wrong: 16 -> 32 -> 64 -> 128 growth for 100
   single-entry appends = 1792 B.
5. **The pA-vs-pE candidate list** (`Out.TryGetValue` hit, `ValueSome` flow,
   `EqualityComparer<'V3>.Default` evaluation). ALL REFUTED:
   `applyChoose2Out` and `processChoose2Side` IL contain no `box` and no heap
   `newobj`; the isolated probe shows the elided drain costs 0 B when the
   mapping body has no reference tuple (`fun _ _ rv -> rv`: W=0 D=0 R=0 C=0).
6. **"EqualityComparer.Default evaluation allocates".** REFUTED: IL clean
   (cached singleton + virtual `Equals(int,int)`), and the ident-mapping
   probe measured 0 B.
7. **The old sequential-probe per-entry numbers (pB=7136, pD=9920, pE=8056).**
   Contaminated methodology: every probe node stayed attached to the shared
   source `a`, so later probes paid for earlier nodes. Per-entry costs
   derived from them (52-80 B/entry) are invalid. Replaced by isolated
   probes: one fresh graph per scenario.

## Methodology that worked

- Isolated probes: fresh sources + ONE derived node per scenario, journals
  and out-deltas pre-grown by a warm cycle, then W (writes only), D (one
  drain + read), R (clean read), C (100 write+read cycles) measured
  separately with `GC.GetAllocatedBytesForCurrentThread`.
- IL verification with the local tool: `dotnet ilspycmd <dll> -o <dir> -il`,
  then grep the method for `box ` and `newobj instance void class`.
- The mapping call site uses `FSharpFunc.InvokeFast` (IL-verified): no
  partial-application closures when the closure extends
  `OptimizedClosures.FSharpFunc`N. The per-call cost was the tuple in the
  body, not the dispatch.

## Rules confirmed by this bisect (keep)

- No reference-tuple matches (`match a, b with`) in hot paths. Use
  `match struct (a, b) with`.
- No class-level `let f = fun ...` bindings used as function arguments in
  hot paths: generalization materializes a closure per use. Module-level
  (inline) functions only.
- Drain/load functions that take a mapping: `let inline` + `[<InlineIfLambda>]`.
- `[<InlineIfLambda>]` stays on stored lambdas (user directive).

## Next (7.4, 7.5)

- 7.4: `ASet.bind`/`AMap.bind` and `collect` (dynamic `unionMany`).
- 7.5: hardening; reentrant write during drain investigation; benchmarks
  (`cd benchmarks/AdaptiveSlop.Benchmarks && dotnet run -c Release`).
