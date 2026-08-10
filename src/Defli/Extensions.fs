namespace Defli

// ─────────────────────────────────────────────────────────────
// Extensions — canonical dictionary access + iteration patterns.
// Mirrors Kimo's Extensions.fs (established convention across
// both codebases). Compiled FIRST (before all other files).
//
// Dictionary access: voption-returning, allocation-free (byref
// out param). Never `match dict.TryGetValue key with | true, v ->`
// — F# core's tuple form allocates.
//
// Iteration: `for KeyValueV(k, v) in dict do` yields a struct
// tuple — the F# core `KeyValue` pattern allocates a heap tuple.
// ─────────────────────────────────────────────────────────────

module Dictionary =
  open System.Collections.Generic

  let inline tryGetValue key (dictionary: IDictionary<_, _>) =
    let mutable value = Unchecked.defaultof<_>

    if dictionary.TryGetValue(key, &value) then
      ValueSome value
    else
      ValueNone

module ReadOnlyDict =
  open System.Collections.Generic

  let inline tryGetValue key (dictionary: IReadOnlyDictionary<_, _>) =
    let mutable value = Unchecked.defaultof<_>

    if dictionary.TryGetValue(key, &value) then
      ValueSome value
    else
      ValueNone

module FrozenDict =
  open System.Collections.Frozen

  let inline tryGetValue key (dictionary: FrozenDictionary<_, _>) =
    let mutable value = Unchecked.defaultof<_>

    if dictionary.TryGetValue(key, &value) then
      ValueSome value
    else
      ValueNone

module ValueOption =

  /// Flattens two voptions without nested matches (projection joins).
  let inline bind2 ([<InlineIfLambda>] binder) v1 v2 =
    match struct (v1, v2) with
    | ValueSome v1, ValueSome v2 -> binder v1 v2
    | _ -> ValueNone

  let inline iter2 ([<InlineIfLambda>] action) v1 v2 =
    match struct (v1, v2) with
    | ValueSome v1, ValueSome v2 -> action v1 v2
    | _ -> ()

[<AutoOpen>]
module Patterns =
  open System.Collections.Generic

  /// Struct-tuple iteration over dictionaries — no heap tuple.
  let inline (|KeyValueV|)(kvp: KeyValuePair<_, _>) =
    struct (kvp.Key, kvp.Value)
