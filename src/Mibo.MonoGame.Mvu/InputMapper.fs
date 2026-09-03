namespace Mibo.Input

open Microsoft.Xna.Framework.Input
open Mibo.Elmish

// The MVU input mapping surface for the MonoGame backend. The runtime-neutral
// delta-attachment and polling logic lives in Mibo.MonoGame (module
// InputMapperDeltas); the adaptive counterparts of the subscriptions here live
// in Mibo.MonoGame.Adaptive. All members keep their home in the
// Mibo.Input.InputMapper module.

module InputMapper =

  /// <summary>
  /// Elmish subscription that builds an <see cref="T:Mibo.Input.ActionState`1"/> from
  /// the registered <see cref="T:Mibo.Input.IInput"/> observables and the supplied map.
  /// Backend-neutral apart from the "is key down" poller, which reads MonoGame state.
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
  /// Polls MonoGame state on each <c>Update()</c>. Registered into GameContext
  /// by the runtime host when <c>MonoGameProgram.withInputMapper</c> is set.
  /// </summary>
  let internal createService
    (initialMap: InputMap<'Action>)
    : IInputMapper<'Action> =
    let mutable map = initialMap
    let mutable state = ActionState.empty
    let mutable prevComboStates = Map.empty<Set<KeyCode>, bool>

    { new IInputMapper<'Action> with
        member _.CurrentState = state

        member _.Update() =
          let kb = Keyboard.GetState()
          let ms = Mouse.GetState()
          let g0 = GamePad.GetState(0)
          let g1 = GamePad.GetState(1)
          let g2 = GamePad.GetState(2)
          let g3 = GamePad.GetState(3)

          let isGpDown (p: int) (b: GamepadButtonCode) =
            match p with
            | 0 -> InputMapperDeltas.isGamepadButtonDownFor g0 0 b
            | 1 -> InputMapperDeltas.isGamepadButtonDownFor g1 1 b
            | 2 -> InputMapperDeltas.isGamepadButtonDownFor g2 2 b
            | _ -> InputMapperDeltas.isGamepadButtonDownFor g3 3 b

          // For the poll-driven service, derive Held/Started/Released from the
          // current "is this trigger held?" snapshot, diffing Held against the
          // previous frame's Held to get edges (the standard polling-mapper
          // pattern). KeyCombo edge tracking needs its own prev-state map.
          let mutable started = Set.empty
          let mutable releasedSet = Set.empty
          let mutable held = Set.empty
          let mutable heldTriggers = Set.empty
          let mutable values = Map.empty

          // The previous Held is the transition baseline for every
          // started edge, including the KeyCombo branch below.
          let prevHeld = state.Held

          for kv in map.TriggerToActions do
            let isDown =
              match kv.Key with
              | Key k -> InputMapperDeltas.isKeyDownFor kb k
              | KeyCombo keys ->
                keys |> Set.forall(InputMapperDeltas.isKeyDownFor kb)
              | MouseButton b -> InputMapperDeltas.isMouseButtonDownFor ms b
              | GamepadButton(p, b) -> isGpDown p b

            if isDown then
              heldTriggers <- heldTriggers |> Set.add kv.Key

              for a in kv.Value do
                held <- held |> Set.add a
                values <- values |> Map.add a 1.0f

            match kv.Key with
            | KeyCombo keys ->
              let wasHeld =
                prevComboStates |> Map.tryFind keys |> Option.defaultValue false

              prevComboStates <- prevComboStates |> Map.add keys isDown

              if isDown && not wasHeld then
                // Transition semantics, as in buildActions: Started
                // fires only when the action was NOT already held.
                for a in kv.Value do
                  if not(prevHeld.Contains a) then
                    started <- started |> Set.add a
              elif not isDown && wasHeld then
                for a in kv.Value do
                  releasedSet <- releasedSet |> Set.add a
            | _ -> ()

          // Derived edges for single keys/buttons: a trigger that is held now
          // but wasn't last frame → Started; was held but no longer → Released.
          for a in held do
            if not(prevHeld.Contains a) then
              started <- started |> Set.add a

          for a in prevHeld do
            if not(held.Contains a) then
              releasedSet <- releasedSet |> Set.add a

          // An action may appear in releasedSet via a KeyCombo release while
          // still held by another trigger. Filter rather than mutate held —
          // matches buildActions (line ~81). Mutating held here would
          // incorrectly deactivate an action still held by another binding.
          releasedSet <-
            releasedSet |> Set.filter(fun a -> not(held.Contains a))

          state <- {
            Held = held
            Started = started
            Released = releasedSet
            Values = values
            HeldTriggers = heldTriggers
          }
    }
