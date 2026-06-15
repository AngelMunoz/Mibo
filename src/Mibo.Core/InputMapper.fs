namespace Mibo.Input

open System
open Mibo.Elmish

// ─────────────────────────────────────────────────────────────────────────────
// Trigger: a physical hardware input that an action can be bound to.
//
// Uses the Core-neutral KeyCode / MouseButtonCode / GamepadButtonCode, so an
// InputMap can be authored and persisted without any backend reference. The
// backend's IInputMapper implementation translates "is this trigger held?" via
// its native API.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Represents a physical hardware input trigger, expressed in backend-neutral
/// codes so an <see cref="T:Mibo.Input.InputMap`1"/> is portable across backends.
/// </summary>
[<Struct>]
type Trigger =
  | Key of keyCode: KeyCode
  | KeyCombo of keyCombo: Set<KeyCode>
  | MouseButton of mouseButton: MouseButtonCode
  | GamepadButton of player: int * gamepadButton: GamepadButtonCode

/// <summary>
/// Configuration mapping game actions to their trigger inputs.
/// </summary>
/// <remarks>
/// An InputMap is backend-neutral: it stores <see cref="T:Mibo.Input.Trigger"/>
/// values rather than native key/button types, so the same map works on every
/// backend. Build maps with the <see cref="M:Mibo.Input.InputMap"/> module helpers.
/// </remarks>
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

  let key (action: 'Action) (k: KeyCode) (map: InputMap<'Action>) =
    bind action (Key k) map

  let keyCombo (action: 'Action) (keys: Set<KeyCode>) (map: InputMap<'Action>) =
    bind action (KeyCombo keys) map

  let mouse (action: 'Action) (btn: MouseButtonCode) (map: InputMap<'Action>) =
    bind action (MouseButton btn) map

  let gamepadButton
    (action: 'Action)
    (player: int)
    (btn: GamepadButtonCode)
    (map: InputMap<'Action>)
    =
    bind action (GamepadButton(player, btn)) map

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

  /// <summary>
  /// Pure state-update for a single trigger transition. Backend-agnostic: the
  /// caller supplies <c>isDown</c>, so this function never touches a native API.
  /// </summary>
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

/// <summary>
/// Service interface for input mapping. The contract lives in Core; each backend
/// supplies an implementation that polls its native API to evaluate whether each
/// <see cref="T:Mibo.Input.Trigger"/> is held.
/// </summary>
type IInputMapper<'Action when 'Action: comparison> =
  abstract CurrentState: ActionState<'Action>
  abstract Update: unit -> unit

/// <summary>Service accessors for the registered <see cref="T:Mibo.Input.IInputMapper`1"/>.</summary>
module InputMapper =

  /// <summary>Attempts to get the registered <see cref="T:Mibo.Input.IInputMapper`1"/> service.</summary>
  let tryGetService<'Action when 'Action: comparison>
    (ctx: GameContext)
    : IInputMapper<'Action> voption =
    GameContext.tryGetService<IInputMapper<'Action>> ctx

  /// <summary>Gets the registered <see cref="T:Mibo.Input.IInputMapper`1"/> service.</summary>
  /// <exception cref="T:System.Exception">Thrown when no IInputMapper is registered (use Program.withInputMapper).</exception>
  let getService<'Action when 'Action: comparison>
    (ctx: GameContext)
    : IInputMapper<'Action> =
    match tryGetService<'Action> ctx with
    | ValueSome m -> m
    | ValueNone ->
      failwith
        "IInputMapper service not registered. Add RaylibProgram.withInputMapper or MonoGameProgram.withInputMapper to your program."
