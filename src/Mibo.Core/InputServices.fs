namespace Mibo.Input

open System
open Mibo.Elmish

// ─────────────────────────────────────────────────────────────────────────────
// IInput service accessors (runtime-neutral).
//
// The MVU subscription helpers over IInput (Keyboard/Mouse/Touch/Gamepad/
// Gesture) live in Mibo.Mvu; the adaptive counterparts live in the adaptive
// runtime package. Both retrieve the service through the accessors here.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Service accessors and the concrete <c>IInput</c> factory hook.
/// </summary>
/// <remarks>
/// <c>create</c> stays backend-local (it polls the native API). The accessors
/// here let user and framework code retrieve the registered <see cref="T:Mibo.Input.IInput"/>
/// from a <see cref="T:Mibo.Elmish.GameContext"/> without referencing a backend.
/// </remarks>
module Input =

  /// <summary>Attempts to get the registered <see cref="T:Mibo.Input.IInput"/> service.</summary>
  let tryGetService(ctx: GameContext) : IInput voption =
    GameContext.tryGetService<IInput> ctx

  /// <summary>Gets the registered <see cref="T:Mibo.Input.IInput"/> service.</summary>
  /// <exception cref="T:System.Exception">Thrown when no IInput is registered (use <see cref="M:Mibo.Elmish.Program.withInput"/>).</exception>
  let getService(ctx: GameContext) : IInput =
    match tryGetService ctx with
    | ValueSome i -> i
    | ValueNone ->
      failwith
        "IInput service not registered. Add Program.withInput to your program."
