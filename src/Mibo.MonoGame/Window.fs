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
//
// The fork has no minimum-window-size API, so the minimum is enforced through
// the backbuffer: when the client area drops below it, SyncBackBuffer applies
// the minimum as the backbuffer size, and the window's presentation-changed
// handler sizes the client area to the backbuffer — the window snaps back.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The MonoGame <see cref="T:Mibo.Windowing.IWindow"/>: drives the host's
/// GraphicsDeviceManager. Registered by the runtime hosts.
/// </summary>
/// <param name="graphics">The host's GraphicsDeviceManager.</param>
/// <param name="minWidth">Minimum client width in pixels; 0 means no minimum.</param>
/// <param name="minHeight">Minimum client height in pixels; 0 means no minimum.</param>
type MonoGameWindow
  (graphics: GraphicsDeviceManager, minWidth: int, minHeight: int) =
  // Read back the mode ApplyConfig resolved (this is constructed after it ran).
  let mutable mode =
    if graphics.IsFullScreen then
      if graphics.HardwareModeSwitch then
        Fullscreen
      else
        BorderlessFullscreen
    else
      Windowed

  // The size the window returns to when leaving fullscreen. Updated by the
  // windowed sync, so a resized window restores at its resized dimensions.
  let mutable windowedSize =
    graphics.PreferredBackBufferWidth, graphics.PreferredBackBufferHeight

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
  /// Per-frame window bookkeeping: outside exclusive fullscreen, resizes the
  /// backbuffer when the client area changes (clamped to the minimum size),
  /// then tracks the backbuffer size in the context dimensions.
  /// </summary>
  /// <remarks>
  /// The context tracks the BACKBUFFER, not the client area: in exclusive
  /// fullscreen the two differ, and the backbuffer is what the pipelines draw
  /// to. Exclusive fullscreen owns its backbuffer size, so the resize step is
  /// skipped there. No-op while minimized (a zero client area is not a valid
  /// backbuffer).
  /// </remarks>
  member _.SyncBackBuffer(game: Game, ctx: GameContext) =
    let bounds = game.Window.ClientBounds
    let isExclusive = graphics.IsFullScreen && graphics.HardwareModeSwitch

    if not isExclusive && bounds.Width > 0 && bounds.Height > 0 then
      let targetW =
        if graphics.IsFullScreen then
          bounds.Width
        else
          max bounds.Width minWidth

      let targetH =
        if graphics.IsFullScreen then
          bounds.Height
        else
          max bounds.Height minHeight

      let pp = game.GraphicsDevice.PresentationParameters

      // The bounds check re-applies while the user holds a sub-minimum drag:
      // the backbuffer is already clamped, but the window needs the snap-back.
      if
        bounds.Width <> targetW
        || bounds.Height <> targetH
        || pp.BackBufferWidth <> targetW
        || pp.BackBufferHeight <> targetH
      then
        graphics.PreferredBackBufferWidth <- targetW
        graphics.PreferredBackBufferHeight <- targetH
        graphics.ApplyChanges()

      if not graphics.IsFullScreen then
        windowedSize <- targetW, targetH

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
        | Windowed ->
          let w, h = windowedSize
          graphics.PreferredBackBufferWidth <- w
          graphics.PreferredBackBufferHeight <- h
          graphics.IsFullScreen <- false
        | BorderlessFullscreen ->
          // Pre-size the backbuffer to the desktop mode: the GDM does not
          // adjust it for borderless, so it would stretch the old size.
          let dm = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode
          graphics.PreferredBackBufferWidth <- dm.Width
          graphics.PreferredBackBufferHeight <- dm.Height
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
        let w = max width minWidth
        let h = max height minHeight
        graphics.PreferredBackBufferWidth <- w
        graphics.PreferredBackBufferHeight <- h
        graphics.ApplyChanges()
        windowedSize <- w, h
