namespace Mibo.Input

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Mibo.Elmish

/// <summary>
/// Represents a physical hardware input trigger.
/// </summary>
/// <remarks>
/// Triggers are the "source" side of input mapping - they represent actual
/// hardware inputs that can be detected.
/// </remarks>
type Trigger =
  /// A keyboard key.
  | Key of Keys
  /// A mouse button (0=Left, 1=Right, 2=Middle).
  | MouseBut of int
  /// A gamepad button for a specific player.
  | GamepadBut of PlayerIndex * Buttons

/// <summary>
/// Configuration mapping game actions to their trigger inputs.
/// </summary>
/// <remarks>
/// InputMap is immutable and can be stored in your model. Use the <see cref="T:Mibo.Input.InputMap"/>
/// module functions to build mappings.
/// </remarks>
/// <example>
/// <code>
/// type Action = MoveLeft | MoveRight | Jump | Fire
///
/// let inputMap =
///     InputMap.empty
///     |&gt; InputMap.key MoveLeft Keys.A
///     |&gt; InputMap.key MoveLeft Keys.Left
///     |&gt; InputMap.key MoveRight Keys.D
///     |&gt; InputMap.key MoveRight Keys.Right
///     |&gt; InputMap.key Jump Keys.Space
///     |&gt; InputMap.mouse Fire 0  // Left click
///     |&gt; InputMap.gamepadButton Jump PlayerIndex.One Buttons.A
/// </code>
/// </example>
type InputMap<'Action when 'Action: comparison> = {

  /// Map from action to all triggers that can activate it.
  ActionToTriggers: Map<'Action, Trigger list>
  /// Reverse lookup: map from trigger to all actions it activates.
  TriggerToActions: Map<Trigger, 'Action list>
}

/// Functions for building InputMap configurations.
module InputMap =
  /// An empty input map with no bindings.
  let empty = {
    ActionToTriggers = Map.empty
    TriggerToActions = Map.empty
  }

  /// Binds a trigger to an action. Multiple triggers can map to the same action.
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

  /// Binds a keyboard key to an action.
  let key (action: 'Action) (k: Keys) (map: InputMap<'Action>) =
    bind action (Key k) map

  /// Binds a mouse button to an action (0=Left, 1=Right, 2=Middle).
  let mouse (action: 'Action) (btn: int) (map: InputMap<'Action>) =
    bind action (MouseBut btn) map

  /// Binds a gamepad button for a specific player to an action.
  let gamepadButton
    (action: 'Action)
    (player: PlayerIndex)
    (btn: Buttons)
    (map: InputMap<'Action>)
    =
    bind action (GamepadBut(player, btn)) map

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
  /// Actions currently being held down.
  Held: Set<'Action>
  /// Actions that started (pressed) this frame.
  Started: Set<'Action>
  /// Actions that ended (released) this frame.
  Released: Set<'Action>
  /// Analog values for actions (0.0 to 1.0). Used for triggers/thumbsticks.
  Values: Map<'Action, float32>
  /// Internal: tracks which raw triggers are currently held.
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

/// Service Interface for Input Mapping
type IInputMapper<'Action when 'Action: comparison> =
  abstract CurrentState: ActionState<'Action>
  abstract Update: unit -> unit

/// <summary>
/// Subscription-based input mapping.
/// </summary>
/// <remarks>
/// <para>This is intentionally "push" driven: it listens to raw <see cref="T:Mibo.Input.IInput"/> deltas and dispatches a
/// user message that contains the mapped <see cref="T:Mibo.Input.ActionState`1"/>.</para>
/// <para>Why this exists:</para>
/// <ul>
/// <li>keeps the user's update signature unchanged (no context parameter)</li>
/// <li>user can opt into any model strategy by handling a single message</li>
/// <li>supports dynamic remapping via a <c>getMap</c> callback (e.g., backed by a ref/agent)</li>
/// </ul>
/// </remarks>
module InputMapper =

  /// <summary>
  /// Subscribe to mapped action state changes.
  /// </summary>
  /// <remarks>
  /// <para><see cref="F:Mibo.Input.ActionState`1.Started"/>/<see cref="F:Mibo.Input.ActionState`1.Released"/> are one-shot sets relative to the most recent hardware delta batch.</para>
  /// <para>If you store the state in your model, you typically want to clear one-shots each frame
  /// (or just treat them as event-like).</para>
  /// </remarks>
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

  /// Attempts to get the IInputMapper service from the game context.
  ///
  /// Returns ValueNone if the service is not registered (i.e., Program.withInputMapper wasn't used).
  let tryGetService<'Action when 'Action: comparison>
    (ctx: GameContext)
    : IInputMapper<'Action> voption =
    let svc = ctx.Game.Services.GetService(typeof<IInputMapper<'Action>>)

    match svc with
    | null -> ValueNone
    | :? IInputMapper<'Action> as m -> ValueSome m
    | _ -> ValueNone

  /// Gets the IInputMapper service from the game context.
  ///
  /// Throws if the service is not registered. Use Program.withInputMapper to ensure registration.
  let getService<'Action when 'Action: comparison>
    (ctx: GameContext)
    : IInputMapper<'Action> =
    match tryGetService<'Action> ctx with
    | ValueSome m -> m
    | ValueNone ->
      failwith
        "IInputMapper service not registered. Add Program.withInputMapper to your program."

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
