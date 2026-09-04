namespace Mibo.Input

open Raylib_cs
open Mibo.Elmish

// The MVU input mapping surface for the raylib backend. The runtime-neutral
// delta-attachment logic lives in Mibo.Raylib (module InputMapperDeltas); the
// adaptive counterparts of the subscriptions here live in Mibo.Raylib.Adaptive.
// All members keep their home in the Mibo.Input.InputMapper module.

module InputMapper =

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
      InputMapperDeltas.attachDeltas getMap ctx (fun state ->
        dispatch(toMsg state))

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
