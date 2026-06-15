namespace Mibo.Input

open System
open System.Collections.Generic
open System.Numerics
open Microsoft.Xna.Framework.Input
open Mibo.Elmish

// Trigger, InputMap<'Action>, ActionState<'Action>, IInputMapper<'Action>, and
// the InputMapper service accessors (getService/tryGetService) all live in Core.
// This file contains only the MonoGame-specific factory + polling logic that
// evaluates whether a Core Trigger is currently held, by translating to the
// native MonoGame key/button and reading Keyboard/Mouse/GamePad state.
//
// Mirrors the raylib InputMapper.fs shape: buildActions (shared logic) +
// subscribe/subscribeStatic (observable-driven) + createService (poll-driven).

module InputMapper =

  let internal buildActions
    (getMap: unit -> InputMap<'Action>)
    (prevComboStates: Map<Set<KeyCode>, bool>)
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
            started <- started |> Set.add a
        elif not isDown && wasHeld then
          for a in kv.Value do
            releasedSet <- releasedSet |> Set.add a
      | _ -> ()

    for t in pressed do
      map.TriggerToActions
      |> Map.tryFind t
      |> Option.iter(fun actions ->
        for a in actions do
          started <- started |> Set.add a)

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

  // MonoGame whole-state pollers for the "is this held right now?" queries.
  // MonoGame gives whole-state snapshots, so these read Keyboard/Mouse/GamePad
  // state on demand. Used by both subscribe (per-event) and createService
  // (per-Update).
  let private isKeyDownFor (kb: KeyboardState) (k: KeyCode) : bool =
    kb.IsKeyDown(KeyCode.toMonoGameKey k)

  let private isMouseButtonDownFor
    (ms: MouseState)
    (b: MouseButtonCode)
    : bool =
    match b with
    | MouseButtonCode.Left -> ms.LeftButton = ButtonState.Pressed
    | MouseButtonCode.Right -> ms.RightButton = ButtonState.Pressed
    | MouseButtonCode.Middle -> ms.MiddleButton = ButtonState.Pressed
    | MouseButtonCode.Extra1 -> ms.XButton1 = ButtonState.Pressed
    | MouseButtonCode.Extra2 -> ms.XButton2 = ButtonState.Pressed
    | _ -> false

  let private isGamepadButtonDownFor
    (gp: GamePadState)
    (playerIndex: int)
    (b: GamepadButtonCode)
    : bool =
    // playerIndex is carried for API parity with the Trigger DU (which is
    // player-tagged); the caller has already routed to this player's state,
    // so no cross-check is needed (and GamePadState has no PlayerIndex field).
    if not gp.IsConnected then
      false
    else
      let btn = GamepadButtonCode.toMonoGameButton b

      match btn with
      | Buttons.DPadUp -> gp.DPad.Up = ButtonState.Pressed
      | Buttons.DPadRight -> gp.DPad.Right = ButtonState.Pressed
      | Buttons.DPadDown -> gp.DPad.Down = ButtonState.Pressed
      | Buttons.DPadLeft -> gp.DPad.Left = ButtonState.Pressed
      | Buttons.LeftTrigger -> gp.Triggers.Left >= 0.5f
      | Buttons.RightTrigger -> gp.Triggers.Right >= 0.5f
      | _ -> gp.IsButtonDown(btn)

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
      let input = Input.getService ctx
      let mutable prevComboStates = Map.empty<Set<KeyCode>, bool>

      let doBuild (pressed: Trigger[]) (released: Trigger[]) =
        // Snapshot MonoGame state for the "held right now?" queries.
        let kb = Keyboard.GetState()
        let ms = Mouse.GetState()
        let g0 = GamePad.GetState(0)
        let g1 = GamePad.GetState(1)
        let g2 = GamePad.GetState(2)
        let g3 = GamePad.GetState(3)

        let isGpDown (p: int) (b: GamepadButtonCode) =
          match p with
          | 0 -> isGamepadButtonDownFor g0 0 b
          | 1 -> isGamepadButtonDownFor g1 1 b
          | 2 -> isGamepadButtonDownFor g2 2 b
          | _ -> isGamepadButtonDownFor g3 3 b

        let state, newComboStates =
          buildActions
            getMap
            prevComboStates
            pressed
            released
            (isKeyDownFor kb)
            (isMouseButtonDownFor ms)
            isGpDown

        prevComboStates <- newComboStates
        state

      let subKey: IDisposable =
        input.KeyboardDelta.Subscribe(fun (d: KeyboardDelta) ->
          let pressed = d.Pressed |> Array.map Key
          let released = d.Released |> Array.map Key
          doBuild pressed released |> toMsg |> dispatch)

      let subMouse: IDisposable =
        input.MouseDelta.Subscribe(fun (d: MouseDelta) ->
          let pressed = d.Buttons.Pressed |> Array.map Trigger.MouseButton
          let released = d.Buttons.Released |> Array.map Trigger.MouseButton
          doBuild pressed released |> toMsg |> dispatch)

      let subGamepad: IDisposable =
        input.GamepadDelta.Subscribe(fun (d: GamepadDelta) ->
          let pressed =
            d.Buttons.Pressed
            |> Array.map(fun b -> Trigger.GamepadButton(d.PlayerIndex, b))

          let released =
            d.Buttons.Released
            |> Array.map(fun b -> Trigger.GamepadButton(d.PlayerIndex, b))

          doBuild pressed released |> toMsg |> dispatch)

      { new IDisposable with
          member _.Dispose() =
            subKey.Dispose()
            subMouse.Dispose()
            subGamepad.Dispose()
      }

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
            | 0 -> isGamepadButtonDownFor g0 0 b
            | 1 -> isGamepadButtonDownFor g1 1 b
            | 2 -> isGamepadButtonDownFor g2 2 b
            | _ -> isGamepadButtonDownFor g3 3 b

          // For the poll-driven service, derive Held/Started/Released from the
          // current "is this trigger held?" snapshot, diffing Held against the
          // previous frame's Held to get edges (the standard polling-mapper
          // pattern). KeyCombo edge tracking needs its own prev-state map.
          let mutable started = Set.empty
          let mutable releasedSet = Set.empty
          let mutable held = Set.empty
          let mutable heldTriggers = Set.empty
          let mutable values = Map.empty

          for kv in map.TriggerToActions do
            let isDown =
              match kv.Key with
              | Key k -> isKeyDownFor kb k
              | KeyCombo keys -> keys |> Set.forall(isKeyDownFor kb)
              | MouseButton b -> isMouseButtonDownFor ms b
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
                for a in kv.Value do
                  started <- started |> Set.add a
              elif not isDown && wasHeld then
                for a in kv.Value do
                  releasedSet <- releasedSet |> Set.add a
            | _ -> ()

          // Derived edges for single keys/buttons: a trigger that is held now
          // but wasn't last frame → Started; was held but no longer → Released.
          let prevHeld = state.Held

          for a in held do
            if not(prevHeld.Contains a) then
              started <- started |> Set.add a

          for a in prevHeld do
            if not(held.Contains a) then
              releasedSet <- releasedSet |> Set.add a

          for a in releasedSet do
            held <- held |> Set.remove a
            values <- values |> Map.remove a

          state <- {
            Held = held
            Started = started
            Released = releasedSet
            Values = values
            HeldTriggers = heldTriggers
          }
    }
