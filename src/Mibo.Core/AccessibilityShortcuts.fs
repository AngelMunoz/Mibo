namespace Mibo.Windowing

open System
open System.Runtime.InteropServices
open Mibo.Elmish

// ─────────────────────────────────────────────────────────────────────────────
// Windows accessibility shortcuts: pressing Shift five times (Sticky Keys),
// holding right Shift for eight seconds (Filter Keys) or holding Num Lock for
// five seconds (Toggle Keys) opens a system dialog that takes the focus from
// the game. While the game window is in front, the guard clears the shortcut
// flags of each feature that is off; it puts the saved settings back when the
// window goes to the background and when the game ends. A feature that is on
// is never touched, because the player uses it. The change is never written
// to the user profile, so a killed process leaves it only until the next
// sign-in.
// ─────────────────────────────────────────────────────────────────────────────

/// One accessibility feature: its SystemParametersInfo get/set actions and the
/// size of its struct, which starts with cbSize and then dwFlags.
[<Struct>]
type internal ShortcutFeature = { Get: uint32; Set: uint32; Size: int }

/// The operating system side of the guard; tests supply a fake.
type internal ShortcutPlatform = {
  /// Reads a feature's struct; <c>ValueNone</c> when the system refuses.
  Read: ShortcutFeature -> byte[] voption
  /// Writes a feature's struct for this session only.
  Write: ShortcutFeature -> byte[] -> unit
  /// True when this process owns the foreground window.
  OwnsForeground: unit -> bool
}

module internal ShortcutFeature =

  /// SPI_GETSTICKYKEYS / SPI_SETSTICKYKEYS, STICKYKEYS.
  let stickyKeys = {
    Get = 0x003Au
    Set = 0x003Bu
    Size = 8
  }

  /// SPI_GETFILTERKEYS / SPI_SETFILTERKEYS, FILTERKEYS.
  let filterKeys = {
    Get = 0x0032u
    Set = 0x0033u
    Size = 24
  }

  /// SPI_GETTOGGLEKEYS / SPI_SETTOGGLEKEYS, TOGGLEKEYS.
  let toggleKeys = {
    Get = 0x0034u
    Set = 0x0035u
    Size = 8
  }

  let all = [| stickyKeys; filterKeys; toggleKeys |]

  /// The feature is on (SKF_STICKYKEYSON, FKF_FILTERKEYSON, TKF_TOGGLEKEYSON).
  [<Literal>]
  let FeatureOn = 0x1

  /// The shortcut is active and asks for confirmation (the *_HOTKEYACTIVE and
  /// *_CONFIRMHOTKEY flags).
  [<Literal>]
  let HotkeyFlags = 0xC

  /// The dwFlags field of a feature's struct.
  let flags(data: byte[]) = BitConverter.ToInt32(data, 4)

module internal Win32Shortcuts =

  [<DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")>]
  extern bool private SystemParametersInfo(
    uint32 action,
    uint32 param,
    nativeint data,
    uint32 winIni
  )

  [<DllImport("user32.dll")>]
  extern nativeint private GetForegroundWindow()

  [<DllImport("user32.dll")>]
  extern uint32 private GetWindowThreadProcessId(
    nativeint window,
    uint32& processId
  )

  let private processId = uint32 Environment.ProcessId

  let private read(f: ShortcutFeature) : byte[] voption =
    let buf = Marshal.AllocHGlobal f.Size

    try
      Marshal.Copy(Array.zeroCreate<byte> f.Size, 0, buf, f.Size)
      Marshal.WriteInt32(buf, 0, f.Size)

      if SystemParametersInfo(f.Get, uint32 f.Size, buf, 0u) then
        let data = Array.zeroCreate<byte> f.Size
        Marshal.Copy(buf, data, 0, f.Size)
        ValueSome data
      else
        ValueNone
    finally
      Marshal.FreeHGlobal buf

  // No SPIF_UPDATEINIFILE: the change lasts for this sign-in session only.
  let private write (f: ShortcutFeature) (data: byte[]) =
    let buf = Marshal.AllocHGlobal f.Size

    try
      Marshal.Copy(data, 0, buf, f.Size)
      SystemParametersInfo(f.Set, uint32 f.Size, buf, 0u) |> ignore
    finally
      Marshal.FreeHGlobal buf

  let private ownsForeground() =
    let mutable pid = 0u
    GetWindowThreadProcessId(GetForegroundWindow(), &pid) |> ignore
    pid = processId

  let platform = {
    Read = read
    Write = write
    OwnsForeground = ownsForeground
  }

/// Holds the saved settings while the shortcuts are off. Focus checks run
/// every frame; reads and writes happen only when the focus changes.
type internal ShortcutGuard(platform: ShortcutPlatform) =
  let gate = obj()
  let mutable saved: struct (ShortcutFeature * byte[])[] = [||]
  let mutable suppressed = false
  let mutable closed = false

  let suppressOne(f: ShortcutFeature) =
    match platform.Read f with
    | ValueSome data ->
      let flags = ShortcutFeature.flags data

      if
        flags &&& ShortcutFeature.FeatureOn = 0
        && flags &&& ShortcutFeature.HotkeyFlags <> 0
      then
        let off = Array.copy data

        BitConverter
          .GetBytes(flags &&& ~~~ShortcutFeature.HotkeyFlags)
          .CopyTo(off, 4)

        platform.Write f off
        ValueSome(struct (f, data))
      else
        ValueNone
    | ValueNone -> ValueNone

  /// True while the shortcuts are off.
  member _.IsSuppressed = suppressed

  /// Turns the shortcuts off. Does nothing when they are already off or the
  /// guard is closed.
  member _.Suppress() =
    lock gate (fun () ->
      if not suppressed && not closed then
        suppressed <- true

        saved <-
          ShortcutFeature.all
          |> Array.choose(suppressOne >> ValueOption.toOption))

  /// Puts the saved settings back. Does nothing when nothing is saved.
  member _.Restore() =
    lock gate (fun () ->
      if suppressed then
        suppressed <- false

        for struct (f, data) in saved do
          platform.Write f data

        saved <- [||])

  /// Puts the saved settings back and never turns the shortcuts off again.
  /// The process is ending: a frame that still runs on another thread must
  /// not undo the restore.
  member this.Close() =
    lock gate (fun () -> closed <- true)
    this.Restore()

  /// Shortcuts off while this process owns the foreground window, back on
  /// while it does not.
  member this.Sync() =
    let owns = platform.OwnsForeground()

    if owns <> suppressed && not closed then
      if owns then this.Suppress() else this.Restore()

/// <summary>
/// Turns off the Windows accessibility shortcuts (Sticky Keys, Filter Keys,
/// Toggle Keys) while the game window has focus.
/// </summary>
/// <remarks>
/// <para>
/// Pressing Shift five times, holding right Shift for eight seconds or holding
/// Num Lock opens a Windows dialog that takes the focus from the game. Games
/// that use Shift as a game key can turn these shortcuts off. Use
/// <see cref="M:Mibo.Elmish.Program.withAccessibilityShortcutsSuppressed"/>
/// or <see cref="M:Mibo.Adaptive.AdaptiveProgram.withAccessibilityShortcutsSuppressed"/>;
/// they add the renderer from this module.
/// </para>
/// <para>
/// A feature that the player has turned on stays as it is. The saved settings
/// come back when the game window goes to the background, when the host
/// disposes its renderers, and when the process exits. The change is never
/// written to the user profile, so a killed process leaves it only until the
/// next sign-in. On other operating systems the renderer does nothing.
/// </para>
/// </remarks>
module AccessibilityShortcuts =

  // One guard per process: the settings are global to the sign-in session.
  let private shared =
    lazy
      (let guard = ShortcutGuard(Win32Shortcuts.platform)
       AppDomain.CurrentDomain.ProcessExit.Add(fun _ -> guard.Close())

       AppDomain.CurrentDomain.UnhandledException.Add(fun _ -> guard.Close())

       guard)

  /// A renderer that drives the given guard: Draw syncs it, Dispose restores.
  let internal rendererWith<'Model>(guard: ShortcutGuard) : IRenderer<'Model> =
    { new IRenderer<'Model> with
        member _.Draw(_, _, _) = guard.Sync()
      interface IDisposable with
        member _.Dispose() = guard.Restore()
    }

  /// <summary>
  /// A renderer that draws nothing. Each frame it turns the shortcuts off
  /// while this process owns the foreground window, and back on while it does
  /// not. Disposing it restores the saved settings.
  /// </summary>
  /// <remarks>
  /// The raylib hosts dispose their renderers when the loop ends, and the
  /// MonoGame hosts when the game is disposed. When neither happens, a
  /// process exit or an unhandled exception restores the settings. On other
  /// operating systems the renderer does nothing.
  /// </remarks>
  let renderer<'Model>() : IRenderer<'Model> =
    if OperatingSystem.IsWindows() then
      rendererWith shared.Value
    else
      { new IRenderer<'Model> with
          member _.Draw(_, _, _) = ()
      }
