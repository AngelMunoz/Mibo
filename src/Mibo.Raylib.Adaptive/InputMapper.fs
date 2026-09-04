namespace Mibo.Input

open Mibo.Elmish
open Mibo.Adaptive

// The adaptive input mapping surface for the raylib backend. The
// runtime-neutral delta-attachment logic lives in Mibo.Raylib (module
// InputMapperDeltas); the MVU counterparts of the subscriptions here live in
// Mibo.Raylib.Mvu. All members keep their home in the
// Mibo.Input.InputMapper module.

module InputMapper =

  /// <summary>
  /// Adaptive subscription that builds an <see cref="T:Mibo.Input.ActionState`1"/> from
  /// the registered <see cref="T:Mibo.Input.IInput"/> observables and the supplied map,
  /// and writes it into the <paramref name="actions"/> root. This is the
  /// adaptive counterpart of <see cref="M:Mibo.Input.InputMapper.subscribe"/> with
  /// the root as the sink (no <c>'Msg</c>, no dispatch). The state is built at
  /// event time (owner thread, during the host's input poll); the writes are
  /// deferred to framework-owned moments: each build is merged into the root
  /// at the step boundary, before <c>Update</c>, and the consumed edges are
  /// cleared after <c>Update</c>, before the frame is forced. Edge handling
  /// is shared with the MonoGame backend (see
  /// <see cref="M:Mibo.Input.AdaptiveInput.subscribe"/>).
  /// </summary>
  /// <remarks>
  /// <para>
  /// CONSUMING: <c>Held</c> is current truth — derive projections from it freely
  /// (<c>actions |&gt; AVal.map (fun s -&gt; s.Held.Contains Jump)</c>, or read it in the
  /// frame builder). <c>Started</c>/<c>Released</c> are EDGE EVENTS: read them in
  /// <c>Update</c> exactly once. The subscription clears them after
  /// <c>Update</c> (after each fixed sub-step's <c>Update</c>, before the
  /// frame is forced), so the next step reads fresh edges:
  /// <code>
  ///   let s = actions |&gt; AVal.getValue
  ///   for a in s.Started do handleStarted a
  /// </code>
  /// A manual <c>actions.Set(ActionState.nextFrame s)</c> write stays legal
  /// but is redundant; the root's equality gate makes it free. The clear
  /// runs before the frame force and before work posted from <c>Update</c>,
  /// so a projection over <c>Started</c> forced in the frame builder and an
  /// intent that reads <c>Started</c> both see the cleared state: read the
  /// edges (or materialize the derived value) in <c>Update</c> instead. One
  /// exception: a fixed-step frame with no sub-step runs no <c>Update</c>
  /// and no drain, so the clear waits and the edges stay in the root for
  /// the next sub-step's <c>Update</c>.
  /// </para>
  /// <para>
  /// EDGES ACCUMULATE between consumptions: every delta (keyboard, mouse,
  /// gamepad) builds a full state and the write MERGES its edges into the root's
  /// unread edges (<see cref="M:Mibo.Input.ActionState.mergeEdges"/>) — a
  /// mouse-move build between a key press and its release must not drop the key's
  /// edges. <c>Held</c>/<c>Values</c> stay last-wins (current truth).
  /// </para>
  /// <para>
  /// COST: the write is cheap (merging with empty edges reuses the existing sets;
  /// the changeable's equality gate skips no-op writes), but the BUILD is real
  /// per-event work — the same cost the Msg-dispatching <c>subscribe</c> pays, one
  /// build per delta. The edge clear costs one closure per state that has edges,
  /// and nothing on frames without input events. Do not skip empty-delta builds:
  /// the rebuild re-derives <c>Held</c> from live polling at event time, which is
  /// how a missed release heals for Held-based consumers.
  /// </para>
  /// <para>
  /// FRAME ONE: subscriptions attach at the first <c>Step</c>'s diff, which runs
  /// after the host's first input poll — input from that first poll is dropped.
  /// One startup frame; not observable in practice.
  /// </para>
  /// </remarks>
  let subscribeAdaptive
    (getMap: unit -> InputMap<'Action>)
    (actions: cval<ActionState<'Action>>)
    (ctx: GameContext)
    : AdaptiveSub =
    AdaptiveInput.subscribe (InputMapperDeltas.attachDeltas getMap ctx) actions

  /// <summary>
  /// Adaptive subscription variant for a fixed (non-changing) InputMap.
  /// </summary>
  let subscribeStaticAdaptive
    (map: InputMap<'Action>)
    (actions: cval<ActionState<'Action>>)
    (ctx: GameContext)
    : AdaptiveSub =
    subscribeAdaptive (fun () -> map) actions ctx
