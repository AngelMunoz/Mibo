namespace Mibo.Elmish

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Windowing

// ─────────────────────────────────────────────────────────────────────────────
// Window management for the MonoGame host.
//
// Startup: ApplyConfig translates the GameConfig window fields to
// GraphicsDeviceManager/GameWindow settings, before the DeviceConfig callbacks
// so a game can still override.
//
// Runtime: MonoGameWindow implements IWindow over the GraphicsDeviceManager.
// MonoGame never resizes the backbuffer on its own: a resized window shows the
// old backbuffer stretched, and gd.Viewport (which the 3D pipelines read for
// the camera aspect) disagrees with the context dimensions (which picking and
// HUD code read). SyncBackBuffer closes that gap once per frame.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The MonoGame <see cref="T:Mibo.Windowing.IWindow"/>: drives the host's
/// GraphicsDeviceManager. Registered by the runtime hosts.
/// </summary>
type MonoGameWindow(graphics: GraphicsDeviceManager) =
  // Read back the mode ApplyConfig resolved (this is constructed after it ran).
  let mutable mode =
    if graphics.IsFullScreen then
      if graphics.HardwareModeSwitch then
        Fullscreen
      else
        BorderlessFullscreen
    else
      Windowed

  /// <summary>
  /// Translates the window-management fields of a
  /// <see cref="T:Mibo.Elmish.GameConfig"/> to window and device settings.
  /// Runs before the DeviceConfig callbacks, which can still override.
  /// </summary>
  static member ApplyConfig
    (config: GameConfig, game: Game, graphics: GraphicsDeviceManager)
    =
    game.Window.AllowUserResizing <-
      config.Resizable || config.MinWidth.IsSome || config.MinHeight.IsSome

    match config.WindowMode with
    | Windowed -> ()
    | BorderlessFullscreen ->
      graphics.HardwareModeSwitch <- false
      graphics.IsFullScreen <- true
    | Fullscreen ->
      graphics.HardwareModeSwitch <- true
      graphics.IsFullScreen <- true

  /// <summary>
  /// Per-frame window bookkeeping: in windowed mode, resizes the backbuffer to
  /// the client area when it changes, then tracks the backbuffer size in the
  /// context dimensions.
  /// </summary>
  /// <remarks>
  /// The context tracks the BACKBUFFER, not the client area: in exclusive
  /// fullscreen the two differ, and the backbuffer is what the pipelines draw
  /// to. In fullscreen the GraphicsDeviceManager owns the backbuffer size, so
  /// the resize step is skipped. No-op while minimized (a zero client area is
  /// not a valid backbuffer).
  /// </remarks>
  static member SyncBackBuffer
    (game: Game, graphics: GraphicsDeviceManager, ctx: GameContext)
    =
    let bounds = game.Window.ClientBounds

    if not graphics.IsFullScreen && bounds.Width > 0 && bounds.Height > 0 then
      let pp = game.GraphicsDevice.PresentationParameters

      if
        bounds.Width <> pp.BackBufferWidth
        || bounds.Height <> pp.BackBufferHeight
      then
        graphics.PreferredBackBufferWidth <- bounds.Width
        graphics.PreferredBackBufferHeight <- bounds.Height
        graphics.ApplyChanges()

    let pp = game.GraphicsDevice.PresentationParameters

    if
      pp.BackBufferWidth <> ctx.WindowWidth
      || pp.BackBufferHeight <> ctx.WindowHeight
    then
      ctx.UpdateDimensions(pp.BackBufferWidth, pp.BackBufferHeight)

  interface IWindow with
    member _.Mode = mode
    member _.IsFullscreen = graphics.IsFullScreen

    member _.SetMode target =
      if target <> mode then
        match target with
        | Windowed -> graphics.IsFullScreen <- false
        | BorderlessFullscreen ->
          graphics.HardwareModeSwitch <- false
          graphics.IsFullScreen <- true
        | Fullscreen ->
          graphics.HardwareModeSwitch <- true
          graphics.IsFullScreen <- true

        graphics.ApplyChanges()
        mode <- target

    member this.ToggleFullscreen() =
      let self = this :> IWindow

      if self.IsFullscreen then
        self.SetMode Windowed
      else
        // A windowed startup has no configured fullscreen flavor; borderless
        // keeps the desktop mode and avoids the video-mode switch.
        match mode with
        | Windowed -> self.SetMode BorderlessFullscreen
        | fullscreen -> self.SetMode fullscreen

    member _.SetSize(width, height) =
      if not graphics.IsFullScreen then
        graphics.PreferredBackBufferWidth <- width
        graphics.PreferredBackBufferHeight <- height
        graphics.ApplyChanges()
