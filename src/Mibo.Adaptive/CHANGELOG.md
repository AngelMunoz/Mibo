# Changelog

## [Unreleased]

### Fixed

- Chained derived maps no longer freeze after certain read and write orders: an upstream removal or update now propagates to chained `AMap.joinOn` stages, `tryFind`/`fold` tails, and other derived maps on the next read.

## [1.0.0] - 2026-08-11

### Added

- Mibo.Adaptive — the adaptive-data library, adopted from AdaptiveSlop in its entirety (dependency-free; the Mibo integration lives in Mibo.Core).
- Pull-evaluate incremental computation: push-mark, pull-evaluate core with per-thread ambient graphs and cross-thread posting for the changeable collections.
- `CVal`/`AVal` value model with lazy scalar escapes — version-bump writes and read-time gates (per-key/per-position precise escapes for the collections).
- Adaptive collections with FDA parity: `ASet`/`CSet`, `AMap`/`CMap`, `AList`/`CList`, including `AMap.joinOn`, `groupBy`, `difference`, the `*A` reduction family, and the `mapA` family with extension points.
- Zero-allocation two-source drains and the collection algebra (dirty cache, journal-and-drain nodes with a correct dirty-indicator version).
- Changeable nodes round-trip through `System.Text.Json` with zero options (self-registering converters).
- `Transaction` and `Posting` primitives for batched writes and cross-thread effects.
- Mibo.Core — `AdaptiveProgram`/`AdaptiveHeadless`: the adaptive counterpart of the MVU `Program` shell (graph-building context, frame builders, restart/exit requests, fixed-step and headless runners).
- Mibo.Raylib/Mibo.MonoGame — `AdaptiveRaylibGame` and `AdaptiveMonoGameGame` hosts that drive an `AdaptiveHeadless` runner from the backend's frame loop.
- Tests and benchmarks: `Mibo.Adaptive.Tests` (xunit + FsCheck property suite) and `Mibo.Adaptive.Benchmarks` (BenchmarkDotNet, compared against FSharp.Data.Adaptive).
