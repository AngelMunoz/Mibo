namespace Mibo.Elmish

open Raylib_cs
open Mibo.Windowing

// ─────────────────────────────────────────────────────────────────────────────
// Window management for the raylib host.
//
// Startup: ApplyConfig translates the GameConfig window fields to raylib
// window flags. SetConfigFlags only takes effect before InitWindow (raylib
// warns and ignores it after), so the hosts call ApplyConfig first.
//
// Runtime: RaylibWindow implements IWindow over the native toggles. raylib has
// no direct "set mode" call; transitions compose ToggleFullscreen (exclusive)
// and ToggleBorderlessWindowed. Both restore the windowed size and position
// when leaving fullscreen, so no size bookkeeping is needed here.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The raylib <see cref="T:Mibo.Windowing.IWindow"/>: tracks the presentation
/// mode and drives the native window calls. Registered by the runtime hosts.
/// </summary>
type RaylibWindow(startupMode: WindowMode) =
  let mutable mode = startupMode

  let enter(target: WindowMode) =
    match target with
    | Windowed -> ()
    | BorderlessFullscreen -> Raylib.ToggleBorderlessWindowed()
    | Fullscreen -> Raylib.ToggleFullscreen()

  let exit() =
    match mode with
    | Windowed -> ()
    | BorderlessFullscreen -> Raylib.ToggleBorderlessWindowed()
    | Fullscreen -> Raylib.ToggleFullscreen()

  /// <summary>
  /// Translates the window-management fields of a
  /// <see cref="T:Mibo.Elmish.GameConfig"/> to raylib window flags. Must be
  /// called before <c>InitWindow</c>.
  /// </summary>
  static member ApplyConfig(config: GameConfig) =
    if
      config.Resizable || config.MinWidth.IsSome || config.MinHeight.IsSome
    then
      Raylib.SetConfigFlags(ConfigFlags.ResizableWindow)

    match config.WindowMode with
    | Windowed -> ()
    | BorderlessFullscreen ->
      Raylib.SetConfigFlags(ConfigFlags.BorderlessWindowMode)
    | Fullscreen -> Raylib.SetConfigFlags(ConfigFlags.FullscreenMode)

  interface IWindow with
    member _.Mode = mode

    member _.IsFullscreen =
      match mode with
      | Windowed -> false
      | _ -> true

    member _.SetMode target =
      if target <> mode then
        exit()
        enter target
        mode <- target

    member this.ToggleFullscreen() =
      let self = this :> IWindow

      if self.IsFullscreen then
        self.SetMode Windowed
      else
        // A windowed startup has no configured fullscreen flavor; borderless
        // keeps the desktop mode and avoids the video-mode switch.
        match startupMode with
        | Windowed -> self.SetMode BorderlessFullscreen
        | fullscreen -> self.SetMode fullscreen

    member _.SetSize(width, height) =
      match mode with
      | Windowed -> Raylib.SetWindowSize(width, height)
      | _ -> ()
