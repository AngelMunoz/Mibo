# Mibo.Adaptive — Agent Guide

Guidance for AI agents working on the Mibo.Adaptive library (adopted from
AdaptiveSlop). For API/usage documentation see README.md and the docs/ folder.
Mibo repo-wide rules (push policy, formatting, PRs) live in the repo root
AGENTS.md — the imperatives below are the library-specific ones.

## Commands

```bash
dotnet build Mibo.slnx
dotnet test src/Mibo.Adaptive.Tests/Mibo.Adaptive.Tests.fsproj
dotnet test src/Mibo.Adaptive.Tests/Mibo.Adaptive.Tests.fsproj -c Release   # before release claims
cd src/Mibo.Adaptive.Benchmarks && dotnet run -c Release -- --filter "*" --job short
```

## Layout

- `src/Mibo.Adaptive/Core/Library.fs` — scalar core: `ChangeableValue`, `AdaptiveNode`
  (generic), `Map3/4/N/ReduceNode`, `Transaction`, `DependencyCollector`, `AVal`/`CVal`
- `src/Mibo.Adaptive/Core/Collections/` — the collection layer, in compile order:
  `Shared.fs` (delta buffers, `Collections` internal module), `Changeable.fs`
  (`ChangeableSet/Map/List`), `ElementSetNode.fs`, `ElementMapNode.fs`,
  `ElementListNode.fs` (the per-element mapA/filterA/chooseA nodes), `SetNodes.fs`,
  `MapNodes.fs`, `ListNodes.fs`, `Reductions.fs`, `ExternalNodes.fs`
  (ofExternal/custom), `Api.fs` (the public modules)
- `src/Mibo.Adaptive.Tests/Tests.fs` — xUnit unit tests
- `src/Mibo.Adaptive.Tests/Properties.fs` — the FsCheck property suite (reference
  models, algebraic laws, incremental laws, scenario generators)
- `src/Mibo.Adaptive.Benchmarks/` — BenchmarkDotNet; run before/after any perf change;
  raw run output goes to `benchmark-results/` (gitignored), summaries to `docs/BENCHMARKS.md`
- `src/Mibo.Adaptive/docs/` — design research and planning documents, when present
- `src/Mibo.Core/AdaptiveProgram.fs` + `Adaptive.Headless.fs` — the Mibo integration
  (the adaptive counterpart of the MVU shell); it references this library, never
  the other way around — Mibo.Adaptive stays dependency-free.

## Invariants

Hold these in all core changes; violating any of them breaks the design:

1. **Pull-lazy only.** Writes mark/notify; nothing recomputes on write. Recomputation
   happens exclusively on read, per dirty node, at most once per change.
2. **Recompute = re-read all dependencies.** Never cache the dep set without re-reading;
   dynamic dependencies (`bind`) and edge self-healing depend on it.
3. **No evaluation during marking.** Notifications are deferred until marking/transaction
   completes. Do not add level/topological-ordering machinery; this rule replaces it.
4. **Owner-thread confinement.** Core code must not add locks; `Interlocked`/`Volatile`
   use stays inside the explicit handoff structure (the post rings). Shared state
   lives on a graph context object. Every thread owns its ambient graph (a
   `[<ThreadStatic>]` pointer to it — the pointer only, all state stays on the
   context); a node belongs to the graph of its creating thread. Cross-thread
   interaction goes through explicit handoff (post/drain), never shared mutable
   access.
5. **Zero library-side allocation on hot paths** (clean read, mark, static recompute,
   delta delivery). Prove allocation claims with `GC.GetAllocatedBytesForCurrentThread`.
6. **Transactions defer application**: writes inside `Transaction.run` apply at commit;
   reads inside see pre-transaction values.

If a change requires breaking an invariant, that is a design change — get explicit sign-off
from the user before proceeding.

## Working in this repo

- **Verify behavior by reading the code, not prose.** Comments and docs may describe
  intended rather than actual behavior; when they disagree, the code wins — and fix the
  comment.
- Structural changes to edges, observation, threading, or node types are architectural
  work: discuss the approach with the user before implementing.
- XML doc comments on all public types/functions, with `<example>` blocks.
- `[<InlineIfLambda>]` on function parameters in hot paths.
- No new dependencies without asking; the core currently depends only on the BCL.
- Performance claims require a BenchmarkDotNet before/after; allocation claims require
  measurement evidence.
- **Test at the public API level only.** Never test node internals. Property tests use the
  DSL (module functions: `CVal.set`, `CSet.add`, `CList.insertAt`, `CMap.addOrUpdate`,
  `AVal.map`), never instance methods, and live in `Properties.fs` (100 iterations;
  the op models must mirror the real semantics, not the library's implementation).
