namespace Mibo.Elmish

open System.Collections.Generic

// ── Internal zero-allocation collection helpers ────────────────────────
// Assembly-visible only; not part of the public API.

[<AutoOpen>]
module KeyValuePatterns =

  /// Struct-returning `KeyValuePair` active pattern. Unlike FSharp.Core's
  /// `KeyValue`, it does not allocate a reference tuple per enumeration item.
  let inline (|KeyValueV|)(kvp: KeyValuePair<'K, 'V>) =
    struct (kvp.Key, kvp.Value)

module Dictionary =

  /// Zero-allocation lookup — avoids the `bool * 'T` reference tuple that
  /// F# allocates for the single-argument `IDictionary.TryGetValue` extension.
  let inline tryGetValue key (dictionary: IDictionary<'K, 'V>) : 'V voption =
    let mutable value = Unchecked.defaultof<'V>

    if dictionary.TryGetValue(key, &value) then
      ValueSome value
    else
      ValueNone

  /// Add-if-absent; returns true when the key was added.
  let inline tryAdd key value (dictionary: IDictionary<'K, 'V>) =
    dictionary.TryAdd(key, value)

module ReadOnlyDict =

  let inline tryGetValue
    key
    (dictionary: IReadOnlyDictionary<'K, 'V>)
    : 'V voption =
    let mutable value = Unchecked.defaultof<'V>

    if dictionary.TryGetValue(key, &value) then
      ValueSome value
    else
      ValueNone
