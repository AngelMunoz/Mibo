namespace Mibo.Input

open System
open System.Numerics
open Raylib_cs
open Mibo.Elmish

/// <summary>
/// Represents a physical hardware input trigger.
/// </summary>
type Trigger =
  | Key of KeyboardKey
  | KeyCombo of Set<KeyboardKey>
  | MouseBut of int
  | GamepadBut of player: int * button: GamepadButton

/// <summary>
/// Configuration mapping game actions to their trigger inputs.
/// </summary>
type InputMap<'Action when 'Action: comparison> = {
  ActionToTriggers: Map<'Action, Trigger list>
  TriggerToActions: Map<Trigger, 'Action list>
}

/// Functions for building InputMap configurations.
module InputMap =
  let empty = {
    ActionToTriggers = Map.empty
    TriggerToActions = Map.empty
  }

  let bind (action: 'Action) (trigger: Trigger) (map: InputMap<'Action>) =
    let existingTriggers =
      map.ActionToTriggers |> Map.tryFind action |> Option.defaultValue []

    let existingActions =
      map.TriggerToActions |> Map.tryFind trigger |> Option.defaultValue []

    {
      ActionToTriggers =
        map.ActionToTriggers |> Map.add action (trigger :: existingTriggers)
      TriggerToActions =
        map.TriggerToActions |> Map.add trigger (action :: existingActions)
    }

  let key (action: 'Action) (k: KeyboardKey) (map: InputMap<'Action>) =
    bind action (Key k) map

  let keyCombo
    (action: 'Action)
    (keys: Set<KeyboardKey>)
    (map: InputMap<'Action>)
    =
    bind action (KeyCombo keys) map

  let mouse (action: 'Action) (btn: int) (map: InputMap<'Action>) =
    bind action (MouseBut btn) map

  let gamepadButton
    (action: 'Action)
    (player: int)
    (btn: GamepadButton)
    (map: InputMap<'Action>)
    =
    bind action (GamepadBut(player, btn)) map

/// <summary>
/// Runtime state tracking which actions are currently active.
/// </summary>
/// <summary>
/// Runtime state tracking which actions are currently active.
/// </summary>
/// <remarks>
/// ActionState is the "output" of the input mapping system. It tells you
/// which actions are held, just started, or just released.
/// </remarks>
/// <example>
/// <code>
/// if actionState.Started.Contains Jump then
///     // Player just pressed jump this frame
///
/// if actionState.Held.Contains MoveLeft then
///     // Player is holding left
/// </code>
/// </example>
type ActionState<'Action when 'Action: comparison> = {
  Held: Set<'Action>
  Started: Set<'Action>
  Released: Set<'Action>
  Values: Map<'Action, float32>
  HeldTriggers: Set<Trigger>
}

module ActionState =
  let empty = {
    Held = Set.empty
    Started = Set.empty
    Released = Set.empty
    Values = Map.empty
    HeldTriggers = Set.empty
  }

  let update
    (map: InputMap<'Action>)
    (isDown: bool)
    (trigger: Trigger)
    (state: ActionState<'Action>)
    : ActionState<'Action> =
    let newHeldTriggers =
      if isDown then
        state.HeldTriggers |> Set.add trigger
      else
        state.HeldTriggers |> Set.remove trigger

    let actions =
      map.TriggerToActions |> Map.tryFind trigger |> Option.defaultValue []

    let mutable newHeld = state.Held
    let mutable newStarted = state.Started
    let mutable newReleased = state.Released
    let mutable newValues = state.Values

    for action in actions do
      let allTriggers =
        map.ActionToTriggers |> Map.tryFind action |> Option.defaultValue []

      let isActionHeld = allTriggers |> List.exists newHeldTriggers.Contains

      let wasHeld = state.Held.Contains action

      if isActionHeld && not wasHeld then
        newHeld <- newHeld |> Set.add action
        newStarted <- newStarted |> Set.add action
        newValues <- newValues |> Map.add action 1.0f
      elif not isActionHeld && wasHeld then
        newHeld <- newHeld |> Set.remove action
        newReleased <- newReleased |> Set.add action
        newValues <- newValues |> Map.remove action

    {
      Held = newHeld
      Started = newStarted
      Released = newReleased
      Values = newValues
      HeldTriggers = newHeldTriggers
    }

  let nextFrame(state: ActionState<'Action>) = {
    state with
        Started = Set.empty
        Released = Set.empty
  }

/// Service Interface for Input Mapping
type IInputMapper<'Action when 'Action: comparison> =
  abstract CurrentState: ActionState<'Action>
  abstract Update: unit -> unit

// ─────────────────────────────────────────────────────────────────────────────
// IInputMapper service (registered via Program.withInputMapper)
// ─────────────────────────────────────────────────────────────────────────────

module InputMapper =

  let subscribe
    (getMap: unit -> InputMap<'Action>)
    (toMsg: ActionState<'Action> -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/InputMapper/subscribe"

    let subscribeFn(dispatch: Dispatch<'Msg>) =
      let input = Input.getService ctx
      let mutable prevComboStates = Map.empty<Set<KeyboardKey>, bool>

      let buildActions (pressed: Trigger[]) (released: Trigger[]) =
        let map = getMap()
        let mutable started = Set.empty
        let mutable releasedSet = Set.empty
        let mutable held = Set.empty
        let mutable heldTriggers = Set.empty
        let mutable values = Map.empty

        for kv in map.TriggerToActions do
          let isDown =
            match kv.Key with
            | Key k -> Raylib.IsKeyDown(k).AsBool()
            | KeyCombo keys ->
              keys |> Set.forall(fun k -> Raylib.IsKeyDown(k).AsBool())
            | MouseBut b ->
              Raylib.IsMouseButtonDown(enum<MouseButton>(b)).AsBool()
            | GamepadBut(p, b) -> Raylib.IsGamepadButtonDown(p, b).AsBool()

          if isDown then
            heldTriggers <- heldTriggers |> Set.add kv.Key

            for a in kv.Value do
              held <- held |> Set.add a
              values <- values |> Map.add a 1.0f

        for t in pressed do
          match t with
          | KeyCombo keys ->
            let allHeld =
              keys |> Set.forall(fun k -> Raylib.IsKeyDown(k).AsBool())

            let wasHeld =
              prevComboStates |> Map.tryFind keys |> Option.defaultValue false

            prevComboStates <- prevComboStates |> Map.add keys allHeld

            if allHeld && not wasHeld then
              map.TriggerToActions
              |> Map.tryFind t
              |> Option.iter(fun actions ->
                for a in actions do
                  started <- started |> Set.add a)
          | _ ->
            map.TriggerToActions
            |> Map.tryFind t
            |> Option.iter(fun actions ->
              for a in actions do
                started <- started |> Set.add a)

        for t in released do
          match t with
          | KeyCombo keys ->
            let allHeld =
              keys |> Set.forall(fun k -> Raylib.IsKeyDown(k).AsBool())

            let wasHeld =
              prevComboStates |> Map.tryFind keys |> Option.defaultValue false

            prevComboStates <- prevComboStates |> Map.add keys allHeld

            if not allHeld && wasHeld then
              map.TriggerToActions
              |> Map.tryFind t
              |> Option.iter(fun actions ->
                for a in actions do
                  releasedSet <- releasedSet |> Set.add a)
          | _ ->
            map.TriggerToActions
            |> Map.tryFind t
            |> Option.iter(fun actions ->
              for a in actions do
                releasedSet <- releasedSet |> Set.add a)

        for a in releasedSet do
          held <- held |> Set.remove a
          values <- values |> Map.remove a

        {
          Held = held
          Started = started
          Released = releasedSet
          Values = values
          HeldTriggers = heldTriggers
        }

      let subKey: IDisposable =
        input.KeyboardDelta.Subscribe(fun (d: KeyboardDelta) ->
          buildActions
            (d.Pressed |> Array.map Key)
            (d.Released |> Array.map Key)
          |> toMsg
          |> dispatch)

      let subMouse: IDisposable =
        input.MouseDelta.Subscribe(fun (d: MouseDelta) ->
          buildActions
            (d.Buttons.Pressed |> Array.map(fun b -> MouseBut(int b)))
            (d.Buttons.Released |> Array.map(fun b -> MouseBut(int b)))
          |> toMsg
          |> dispatch)

      let subGamepad: IDisposable =
        input.GamepadDelta.Subscribe(fun (d: GamepadDelta) ->
          buildActions
            (d.Buttons.Pressed
             |> Array.map(fun b -> GamepadBut(d.PlayerIndex, b)))
            (d.Buttons.Released
             |> Array.map(fun b -> GamepadBut(d.PlayerIndex, b)))
          |> toMsg
          |> dispatch)

      { new IDisposable with
          member _.Dispose() =
            subKey.Dispose()
            subMouse.Dispose()
            subGamepad.Dispose()
      }

    Sub.Active(subId, subscribeFn)

  let subscribeStatic
    (map: InputMap<'Action>)
    (toMsg: ActionState<'Action> -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    subscribe (fun () -> map) toMsg ctx

  let tryGetService<'Action when 'Action: comparison>
    (ctx: GameContext)
    : IInputMapper<'Action> voption =
    GameContext.tryGetService<IInputMapper<'Action>> ctx

  let getService<'Action when 'Action: comparison>
    (ctx: GameContext)
    : IInputMapper<'Action> =
    match tryGetService<'Action> ctx with
    | ValueSome m -> m
    | ValueNone -> failwith "IInputMapper service not registered."

  let internal createService
    (initialMap: InputMap<'Action>)
    : IInputMapper<'Action> =
    let mutable map = initialMap
    let mutable state = ActionState.empty
    let mutable prevComboStates = Map.empty<Set<KeyboardKey>, bool>

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
                Raylib.IsKeyPressed(k).AsBool(),
                Raylib.IsKeyReleased(k).AsBool(),
                Raylib.IsKeyDown(k).AsBool()
              | KeyCombo keys ->
                let allHeld =
                  keys |> Set.forall(fun k -> Raylib.IsKeyDown(k).AsBool())

                let wasHeld =
                  prevComboStates
                  |> Map.tryFind keys
                  |> Option.defaultValue false

                prevComboStates <- prevComboStates |> Map.add keys allHeld
                (allHeld && not wasHeld), (not allHeld && wasHeld), allHeld
              | MouseBut b ->
                let btn = enum<MouseButton>(b)

                Raylib.IsMouseButtonPressed(btn).AsBool(),
                Raylib.IsMouseButtonReleased(btn).AsBool(),
                Raylib.IsMouseButtonDown(btn).AsBool()
              | GamepadBut(p, b) ->
                Raylib.IsGamepadButtonPressed(p, b).AsBool(),
                Raylib.IsGamepadButtonReleased(p, b).AsBool(),
                Raylib.IsGamepadButtonDown(p, b).AsBool()

            if isPressed then
              for a in kv.Value do
                started <- started |> Set.add a

            if isReleased then
              for a in kv.Value do
                releasedSet <- releasedSet |> Set.add a

            if isDown then
              heldTriggers <- heldTriggers |> Set.add kv.Key

              for a in kv.Value do
                held <- held |> Set.add a
                values <- values |> Map.add a 1.0f

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
