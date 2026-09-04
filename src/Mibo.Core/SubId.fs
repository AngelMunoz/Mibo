namespace Mibo.Elmish

open System
open FSharp.UMX

// Subscription identifiers are runtime-neutral: the MVU runtime uses them to
// diff subscriptions, and the adaptive runner uses them as identity keys for
// its adaptive subscriptions. They live in the kernel so neither runtime
// package depends on the other.

/// <summary>
/// Subscription identifier used as the key for subscription diffing.
/// </summary>
/// <remarks>
/// The runtimes use SubIds to determine which subscriptions to start, stop,
/// or keep running across frames. Use stable, unique IDs for each subscription.
/// Keep this allocation-free in hot paths (avoid list-based IDs).
/// </remarks>
[<Measure>]
type subId

/// A typed string wrapper for subscription identifiers.
type SubId = string<subId>

/// Functions for creating and manipulating subscription identifiers.
module SubId =
  /// <summary>Wraps a raw string into a <see cref="T:Mibo.Elmish.SubId"/>.</summary>
  let inline ofString(value: string) : SubId = UMX.tag<subId> value

  /// <summary>Extracts the raw string value from a <see cref="T:Mibo.Elmish.SubId"/>.</summary>
  let inline value(id: SubId) : string = UMX.untag id

  /// <summary>
  /// Prefixes a SubId with a namespace for parent-child subscription composition.
  /// </summary>
  /// <example>
  /// <code>
  /// // Creates "Player/moveInput"
  /// SubId.prefix "Player" (SubId.ofString "moveInput")
  /// </code>
  /// </example>
  let inline prefix (prefix: string) (id: SubId) : SubId =
    if String.IsNullOrEmpty(prefix) then
      id
    else
      let idStr = value id

      if String.IsNullOrEmpty(idStr) then
        ofString prefix
      else
        ofString(prefix + "/" + idStr)
