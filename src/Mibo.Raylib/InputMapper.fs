namespace Mibo.Input

open System
open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Adaptive

// Trigger, InputMap<'Action>, ActionState<'Action>, IInputMapper<'Action>, and
// the InputMapper service accessors (getService/tryGetService) all live in Core
// now. This file contains only the raylib-specific factory + polling logic that
// evaluates whether a Core Trigger is currently held, by translating to the
// native raylib key/button and calling Raylib.IsKeyDown etc.

module InputMapper =

  let internal buildActions
    (getMap: unit -> InputMap<'Action>)
    (prevComboStates: Map<Set<KeyCode>, bool>)
    (prevHeldActions: Set<'Action>)
    (pressed: Trigger[])
    (released: Trigger[])
    (isKeyDown: KeyCode -> bool)
    (isMouseButtonDown: MouseButtonCode -> bool)
    (isGamepadButtonDown: int -> GamepadButtonCode -> bool)
    : ActionState<'Action> * Map<Set<KeyCode>, bool> =
    let map = getMap()
    let mutable started = Set.empty
    let mutable releasedSet = Set.empty
    let mutable held = Set.empty
    let mutable heldTriggers = Set.empty
    let mutable values = Map.empty
    let mutable comboStates = prevComboStates

    // Started is a TRANSITION, matching core ActionState.update: it
    // fires only when the action was NOT already held. Pressing a
    // synonym binding (Left while A already holds the action) must not
    // re-fire Started — Released only fires when the action FULLY
    // releases, so a per-trigger Started unbalances edge-arithmetic
    // consumers (add on Started, subtract on Released): one add per
    // binding press against a single subtract at full release leaves
    // them stuck.
    let startIfNew(a: 'Action) =
      if not(prevHeldActions.Contains a) then
        started <- started |> Set.add a

    for kv in map.TriggerToActions do
      let isDown =
        match kv.Key with
        | Key k -> isKeyDown k
        | KeyCombo keys -> keys |> Set.forall isKeyDown
        | MouseButton b -> isMouseButtonDown b
        | GamepadButton(p, b) -> isGamepadButtonDown p b

      if isDown then
        heldTriggers <- heldTriggers |> Set.add kv.Key

        for a in kv.Value do
          held <- held |> Set.add a
          values <- values |> Map.add a 1.0f

      match kv.Key with
      | KeyCombo keys ->
        let wasHeld =
          comboStates |> Map.tryFind keys |> Option.defaultValue false

        comboStates <- comboStates |> Map.add keys isDown

        if isDown && not wasHeld then
          for a in kv.Value do
            startIfNew a
        elif not isDown && wasHeld then
          for a in kv.Value do
            releasedSet <- releasedSet |> Set.add a
      | _ -> ()

    for t in pressed do
      map.TriggerToActions
      |> Map.tryFind t
      |> Option.iter(fun actions ->
        for a in actions do
          startIfNew a)

    for t in released do
      map.TriggerToActions
      |> Map.tryFind t
      |> Option.iter(fun actions ->
        for a in actions do
          releasedSet <- releasedSet |> Set.add a)

    releasedSet <- releasedSet |> Set.filter(fun a -> not(held.Contains a))

    ({
      Held = held
      Started = started
      Released = releasedSet
      Values = values
      HeldTriggers = heldTriggers
     },
     comboStates)

  /// Attaches the three delta subscriptions; every keyboard/mouse/gamepad
  /// delta builds a fresh <see cref="T:Mibo.Input.ActionState`1"/> (event time,
  /// owner thread — during the host's input poll) and hands it to
  /// <paramref name="emit"/>. Shared by <see cref="M:Mibo.Input.InputMapper.subscribe"/>
  /// (emits a message) and <see cref="M:Mibo.Input.InputMapper.subscribeAdaptive"/>
  /// (emits a deferred root write) — the emit step is the only difference.
  let private attachDeltas
    (getMap: unit -> InputMap<'Action>)
    (ctx: GameContext)
    (emit: ActionState<'Action> -> unit)
    : IDisposable =
    let input = Input.getService ctx
    let mutable prevComboStates = Map.empty<Set<KeyCode>, bool>
    let mutable prevHeldActions = Set.empty<'Action>

    let doBuild (pressed: Trigger[]) (released: Trigger[]) =
      let state, newComboStates =
        buildActions
          getMap
          prevComboStates
          prevHeldActions
          pressed
          released
          (fun k -> Raylib.IsKeyDown(KeyCode.toRaylibKey k).AsBool())
          (fun b ->
            Raylib.IsMouseButtonDown(MouseButtonCode.toRaylibButton b).AsBool())
          (fun p b ->
            Raylib
              .IsGamepadButtonDown(p, GamepadButtonCode.toRaylibButton b)
              .AsBool())

      prevComboStates <- newComboStates
      prevHeldActions <- state.Held
      state

    let subKey: IDisposable =
      input.KeyboardDelta.Subscribe(fun (d: KeyboardDelta) ->
        let pressed = d.Pressed |> Array.map Key
        let released = d.Released |> Array.map Key
        emit(doBuild pressed released))

    let subMouse: IDisposable =
      input.MouseDelta.Subscribe(fun (d: MouseDelta) ->
        let pressed = d.Buttons.Pressed |> Array.map Trigger.MouseButton
        let released = d.Buttons.Released |> Array.map Trigger.MouseButton

        emit(doBuild pressed released))

    let subGamepad: IDisposable =
      input.GamepadDelta.Subscribe(fun (d: GamepadDelta) ->
        let pressed =
          d.Buttons.Pressed
          |> Array.map(fun b -> Trigger.GamepadButton(d.PlayerIndex, b))

        let released =
          d.Buttons.Released
          |> Array.map(fun b -> Trigger.GamepadButton(d.PlayerIndex, b))

        emit(doBuild pressed released))

    { new IDisposable with
        member _.Dispose() =
          subKey.Dispose()
          subMouse.Dispose()
          subGamepad.Dispose()
    }

  /// <summary>
  /// Elmish subscription that builds an <see cref="T:Mibo.Input.ActionState`1"/> from
  /// the registered <see cref="T:Mibo.Input.IInput"/> observables and the supplied map.
  /// Backend-neutral apart from the "is key down" poller, which calls raylib.
  /// </summary>
  let subscribe
    (getMap: unit -> InputMap<'Action>)
    (toMsg: ActionState<'Action> -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/InputMapper/subscribe"

    let subscribeFn(dispatch: Dispatch<'Msg>) =
      attachDeltas getMap ctx (fun state -> dispatch(toMsg state))

    Sub.Active(subId, subscribeFn)

  /// <summary>
  /// Elmish subscription variant for a fixed (non-changing) InputMap.
  /// </summary>
  let subscribeStatic
    (map: InputMap<'Action>)
    (toMsg: ActionState<'Action> -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    subscribe (fun () -> map) toMsg ctx

  /// <summary>
  /// Adaptive subscription that builds an <see cref="T:Mibo.Input.ActionState`1"/> from
  /// the registered <see cref="T:Mibo.Input.IInput"/> observables and the supplied map,
  /// writing it into the <paramref name="actions"/> root through the pre-step lane —
  /// the adaptive counterpart of <see cref="M:Mibo.Input.InputMapper.subscribe"/> with
  /// the root as the sink (no <c>'Msg</c>, no dispatch). The state is built at event
  /// time (owner thread, during the host's input poll); only the root write is
  /// deferred, applied at the step boundary before <c>Update</c>, so the update phase
  /// and the frame force read the settled state.
  /// </summary>
  /// <remarks>
  /// <para>
  /// CONSUMING: <c>Held</c> is current truth — derive projections from it freely
  /// (<c>actions |&gt; AVal.map (fun s -> s.Held.Contains Jump)</c>, or read it in the
  /// frame builder). <c>Started</c>/<c>Released</c> are EDGE EVENTS and must be
  /// consumed exactly once, in <c>Update</c>, read-then-clear — unlike the MVU
  /// subscribe, where every delta dispatches and "Started" means "pressed this
  /// frame", the root keeps the edges until they are cleared:
  /// <code>
  ///   let s = actions |&gt; AVal.getValue
  ///   for a in s.Started do handleStarted a
  ///   actions.Set(ActionState.nextFrame s)   // clear the consumed edges
  /// </code>
  /// Skip the clear and the edges stay for the whole session (a <c>Contains</c>
  /// check would fire forever after the first press); derive a projection from
  /// <c>Started</c> instead of reading it in <c>Update</c> and the clear hides the
  /// events from that projection. Read the edges in <c>Update</c>, clear, done.
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
  /// build per delta. Do not skip empty-delta builds: the rebuild re-derives
  /// <c>Held</c> from live polling at event time, which is how a missed release
  /// heals for Held-based consumers.
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
    let subId = SubId.ofString "Mibo/Input/InputMapper/subscribeAdaptive"

    let attach(post: (unit -> unit) -> unit) =
      attachDeltas getMap ctx (fun state ->
        post(fun () ->
          actions.Set(ActionState.mergeEdges (actions.GetValue()) state)))

    { Id = subId; Attach = attach }

  /// <summary>
  /// Adaptive subscription variant for a fixed (non-changing) InputMap.
  /// </summary>
  let subscribeStaticAdaptive
    (map: InputMap<'Action>)
    (actions: cval<ActionState<'Action>>)
    (ctx: GameContext)
    : AdaptiveSub =
    subscribeAdaptive (fun () -> map) actions ctx

  /// <summary>
  /// Creates the backend-specific <see cref="T:Mibo.Input.IInputMapper`1"/> service.
  /// Polls raylib directly on each <c>Update()</c>. Registered into GameContext
  /// by the runtime host when <c>Program.withInputMapper</c> is set.
  /// </summary>
  let internal createService
    (initialMap: InputMap<'Action>)
    : IInputMapper<'Action> =
    let mutable map = initialMap
    let mutable state = ActionState.empty
    let mutable prevComboStates = Map.empty<Set<KeyCode>, bool>
    let mutable prevHeldActions = Set.empty<'Action>

    { new IInputMapper<'Action> with
        member _.CurrentState = state

        member _.Update() =
          let mutable started = Set.empty
          let mutable releasedSet = Set.empty
          let mutable held = Set.empty
          let mutable heldTriggers = Set.empty
          let mutable values = Map.empty

          for kv in map.TriggerToActions do
            let isPressed, isReleased, isDown =
              match kv.Key with
              | Key k ->
                let rk = KeyCode.toRaylibKey k

                Raylib.IsKeyPressed(rk).AsBool(),
                Raylib.IsKeyReleased(rk).AsBool(),
                Raylib.IsKeyDown(rk).AsBool()
              | KeyCombo keys ->
                let allHeld =
                  keys
                  |> Set.forall(fun k ->
                    Raylib.IsKeyDown(KeyCode.toRaylibKey k).AsBool())

                let wasHeld =
                  prevComboStates
                  |> Map.tryFind keys
                  |> Option.defaultValue false

                prevComboStates <- prevComboStates |> Map.add keys allHeld
                (allHeld && not wasHeld), (not allHeld && wasHeld), allHeld
              | MouseButton b ->
                let btn = MouseButtonCode.toRaylibButton b

                Raylib.IsMouseButtonPressed(btn).AsBool(),
                Raylib.IsMouseButtonReleased(btn).AsBool(),
                Raylib.IsMouseButtonDown(btn).AsBool()
              | GamepadButton(p, b) ->
                let btn = GamepadButtonCode.toRaylibButton b

                Raylib.IsGamepadButtonPressed(p, btn).AsBool(),
                Raylib.IsGamepadButtonReleased(p, btn).AsBool(),
                Raylib.IsGamepadButtonDown(p, btn).AsBool()

            if isPressed then
              // Transition semantics, as in buildActions: Started fires
              // only when the action was NOT already held.
              for a in kv.Value do
                if not(prevHeldActions.Contains a) then
                  started <- started |> Set.add a

            if isReleased then
              for a in kv.Value do
                releasedSet <- releasedSet |> Set.add a

            if isDown then
              heldTriggers <- heldTriggers |> Set.add kv.Key

              for a in kv.Value do
                held <- held |> Set.add a
                values <- values |> Map.add a 1.0f

          // An action may appear in releasedSet via a KeyCombo release while
          // still held by another trigger. Filter rather than mutate held —
          // matches buildActions. Mutating held here would
          // incorrectly deactivate an action still held by another binding.
          releasedSet <-
            releasedSet |> Set.filter(fun a -> not(held.Contains a))

          prevHeldActions <- held

          state <- {
            Held = held
            Started = started
            Released = releasedSet
            Values = values
            HeldTriggers = heldTriggers
          }
    }
