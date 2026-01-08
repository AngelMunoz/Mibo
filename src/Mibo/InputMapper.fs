namespace Mibo.Input

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Mibo.Elmish

/// Represents a physical hardware input trigger.
type Trigger =
  | Key of Keys
  | MouseBut of int // 0=Left, 1=Right, 2=Middle
  | GamepadBut of PlayerIndex * Buttons

/// Configuration: Mappings from Actions to Triggers.
type InputMap<'Action when 'Action: comparison> = {
  ActionToTriggers: Map<'Action, Trigger list>
  TriggerToActions: Map<Trigger, 'Action list>
}

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

  let key (action: 'Action) (k: Keys) (map: InputMap<'Action>) =
    bind action (Key k) map

  let mouse (action: 'Action) (btn: int) (map: InputMap<'Action>) =
    bind action (MouseBut btn) map

  let gamepadButton
    (action: 'Action)
    (player: PlayerIndex)
    (btn: Buttons)
    (map: InputMap<'Action>)
    =
    bind action (GamepadBut(player, btn)) map

/// Runtime State
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

  /// Internal update logic (pure)
  let update map isDown trigger state =
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

  let nextFrame state = {
    state with
        Started = Set.empty
        Released = Set.empty
  }

/// Subscription-based input mapping.
///
/// This is intentionally "push" driven: it listens to raw IInput deltas and dispatches a
/// user message that contains the mapped `ActionState<'Action>`.
///
/// Why this exists:
/// - keeps the user's update signature unchanged (no context parameter)
/// - user can opt into any model strategy by handling a single message
/// - supports dynamic remapping via a `getMap` callback (e.g., backed by a ref/agent)
module InputMapper =

  /// Subscribe to mapped action state changes.
  ///
  /// Notes on semantics:
  /// - `Started`/`Released` are one-shot sets relative to the most recent hardware delta batch.
  /// - If you store the state in your model, you typically want to clear one-shots each frame
  ///   (or just treat them as event-like).
  let subscribe
    (getMap: unit -> InputMap<'Action>)
    (toMsg: ActionState<'Action> -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    let subId = SubId.ofString "Mibo/Input/InputMapper/subscribe"

    let subscribeFn(dispatch: Dispatch<'Msg>) =
      let input = Input.getService ctx
      let mutable state = ActionState.empty

      let apply(isDown: bool, trigger: Trigger) =
        let map = getMap()
        state <- ActionState.update map isDown trigger state

      let subKey =
        input.KeyboardDelta.Subscribe(fun d ->
          // Treat each delta batch as a logical "tick" for one-shot fields.
          state <- ActionState.nextFrame state

          for k in d.Pressed do
            apply(true, Key k)

          for k in d.Released do
            apply(false, Key k)

          dispatch(toMsg state))

      let subMouse =
        input.MouseDelta.Subscribe(fun d ->
          state <- ActionState.nextFrame state

          if d.Buttons.LeftPressed then
            apply(true, MouseBut 0)

          if d.Buttons.LeftReleased then
            apply(false, MouseBut 0)

          if d.Buttons.RightPressed then
            apply(true, MouseBut 1)

          if d.Buttons.RightReleased then
            apply(false, MouseBut 1)

          if d.Buttons.MiddlePressed then
            apply(true, MouseBut 2)

          if d.Buttons.MiddleReleased then
            apply(false, MouseBut 2)

          dispatch(toMsg state))

      let subGamepad =
        input.GamepadDelta.Subscribe(fun d ->
          state <- ActionState.nextFrame state

          for btn in d.Buttons.Pressed do
            apply(true, GamepadBut(d.PlayerIndex, btn))

          for btn in d.Buttons.Released do
            apply(false, GamepadBut(d.PlayerIndex, btn))

          dispatch(toMsg state))

      { new IDisposable with
          member _.Dispose() =
            subKey.Dispose()
            subMouse.Dispose()
            subGamepad.Dispose()
      }

    Sub.Active(subId, subscribeFn)

  /// Convenience overload for static mappings.
  let subscribeStatic
    (map: InputMap<'Action>)
    (toMsg: ActionState<'Action> -> 'Msg)
    (ctx: GameContext)
    : Sub<'Msg> =
    subscribe (fun () -> map) toMsg ctx

/// Service Interface for Input Mapping
type IInputMapper<'Action when 'Action: comparison> =
  abstract CurrentState: ActionState<'Action>
  abstract Update: unit -> unit

/// Service Implementation
type InputMapperService<'Action when 'Action: comparison>
  (input: IInput, initialMap: InputMap<'Action>) =
  let mutable map = initialMap
  let mutable state = ActionState.empty

  // Internal buffer for incoming events this frame
  let pendingEvents = ResizeArray<(bool * Trigger)>()

  let subKey =
    input.KeyboardDelta.Subscribe(fun d ->
      for k in d.Pressed do
        pendingEvents.Add(true, Key k)

      for k in d.Released do
        pendingEvents.Add(false, Key k))

  let subMouse =
    input.MouseDelta.Subscribe(fun d ->
      if d.Buttons.LeftPressed then
        pendingEvents.Add(true, MouseBut 0)

      if d.Buttons.LeftReleased then
        pendingEvents.Add(false, MouseBut 0)

      if d.Buttons.RightPressed then
        pendingEvents.Add(true, MouseBut 1)

      if d.Buttons.RightReleased then
        pendingEvents.Add(false, MouseBut 1))

  let subGamepad =
    input.GamepadDelta.Subscribe(fun d ->
      for btn in d.Buttons.Pressed do
        pendingEvents.Add(true, GamepadBut(d.PlayerIndex, btn))

      for btn in d.Buttons.Released do
        pendingEvents.Add(false, GamepadBut(d.PlayerIndex, btn)))

  interface IDisposable with
    member _.Dispose() =
      subKey.Dispose()
      subMouse.Dispose()
      subGamepad.Dispose()

  interface IInputMapper<'Action> with
    member _.CurrentState = state

    /// Called at start of frame to process pending hardware events
    member _.Update() =
      // Reset one-shot states
      state <- ActionState.nextFrame state

      // Process pending
      for (isDown, trigger) in pendingEvents do
        state <- ActionState.update map isDown trigger state

      pendingEvents.Clear()
