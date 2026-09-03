namespace Mibo.Elmish

open Mibo.Windowing

/// <summary>
/// Configuration for game window and framerate settings.
/// </summary>
/// <remarks>
/// Config callbacks return a new GameConfig with desired changes applied.
/// Use <see cref="M:Mibo.Elmish.Program.withConfig"/> to register callbacks.
/// </remarks>
[<Struct>]
type GameConfig = {
  /// Window width in pixels. Default: 800.
  Width: int
  /// Window height in pixels. Default: 600.
  Height: int
  /// Window title.
  Title: string
  /// Target render rate. <c>ValueNone</c> = use the backend's default (no cap
  /// imposed); <c>ValueSome n</c> = cap at n FPS (MonoGame fixed timestep at
  /// 1/n, Raylib <c>SetTargetFPS</c>). Defaults to <c>ValueNone</c>.
  TargetFPS: int voption
  /// Minimum window width in pixels. When set, enables resizable window.
  MinWidth: int voption
  /// Minimum window height in pixels. When set, enables resizable window.
  MinHeight: int voption
  /// Whether the user can resize the window. Default: false. Setting
  /// <c>MinWidth</c> or <c>MinHeight</c> also enables it.
  Resizable: bool
  /// Window presentation mode at startup. Default: <c>Windowed</c>.
  WindowMode: WindowMode
}

module GameConfig =
  let defaultConfig = {
    Width = 800
    Height = 600
    Title = "Mibo F#"
    TargetFPS = ValueNone
    MinWidth = ValueNone
    MinHeight = ValueNone
    Resizable = false
    WindowMode = Windowed
  }

  let withWidth width config = { config with Width = width }
  let withHeight height config = { config with Height = height }

  let withMinWidth width config = {
    config with
        MinWidth = ValueSome width
  }

  let withMinHeight height config = {
    config with
        MinHeight = ValueSome height
  }

  let withTitle title config = { config with Title = title }

  /// Cap the render rate at the given FPS (MonoGame fixed timestep at 1/fps,
  /// Raylib <c>SetTargetFPS</c>). Omit it (the default) to leave the backend's
  /// framerate behavior untouched.
  let withTargetFPS fps config = {
    config with
        TargetFPS = ValueSome fps
  }

  /// Allow the user to resize the window.
  let withResizable config = { config with Resizable = true }

  /// Set the window presentation mode at startup (windowed, borderless
  /// fullscreen, or exclusive fullscreen).
  let withWindowMode mode config = { config with WindowMode = mode }
