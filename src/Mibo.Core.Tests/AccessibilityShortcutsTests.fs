module Mibo.Core.Tests.AccessibilityShortcuts

open System
open System.Collections.Generic
open Expecto
open Mibo.Elmish
open Mibo.Windowing

/// Windows defaults: the feature is off, its shortcut is active and confirms.
let private shortcutActive = 0x1FE

/// A struct of the feature's size with the given flags and a marker in every
/// other byte, so a test sees when a write changes more than the flags.
let private structOf (f: ShortcutFeature) (flags: int) =
  let data = Array.init f.Size (fun i -> byte(0xA0 + i))
  BitConverter.GetBytes(f.Size).CopyTo(data, 0)
  BitConverter.GetBytes(flags).CopyTo(data, 4)
  data

/// An in-memory system: the current struct of each feature, the foreground
/// owner, and a count of writes.
type private FakeSystem(initial: (ShortcutFeature * int) list) =
  let current = Dictionary<uint32, byte[]>()

  do
    for f, flags in initial do
      current[f.Get] <- structOf f flags

  member val Foreground = false with get, set
  member val Writes = 0 with get, set
  member val Refused: Set<uint32> = Set.empty with get, set

  member _.Flags(f: ShortcutFeature) = ShortcutFeature.flags current[f.Get]
  member _.Struct(f: ShortcutFeature) = Array.copy current[f.Get]

  member this.Platform: ShortcutPlatform = {
    Read =
      fun f ->
        if this.Refused.Contains f.Get then
          ValueNone
        else
          ValueSome(Array.copy current[f.Get])
    Write =
      fun f data ->
        this.Writes <- this.Writes + 1
        current[f.Get] <- Array.copy data
    OwnsForeground = fun () -> this.Foreground
  }

let private allActive() =
  FakeSystem [
    ShortcutFeature.stickyKeys, shortcutActive
    ShortcutFeature.filterKeys, shortcutActive
    ShortcutFeature.toggleKeys, shortcutActive
  ]

let private gameTime = {
  TotalTime = TimeSpan.Zero
  ElapsedGameTime = TimeSpan.Zero
}

[<Tests>]
let tests =
  testList "AccessibilityShortcuts" [

    testCase "focus clears the shortcut flags and only them"
    <| fun _ ->
      let system = allActive()
      let before = system.Struct ShortcutFeature.filterKeys
      let guard = ShortcutGuard(system.Platform)

      system.Foreground <- true
      guard.Sync()

      for f in ShortcutFeature.all do
        Expect.equal
          (system.Flags f)
          (shortcutActive &&& ~~~ShortcutFeature.HotkeyFlags)
          "hotkey flags cleared, other flags kept"

      let after = system.Struct ShortcutFeature.filterKeys
      Expect.equal after[8..] before[8..] "bytes after dwFlags unchanged"
      Expect.isTrue guard.IsSuppressed "guard reports suppressed"

    testCase "losing focus puts every saved struct back"
    <| fun _ ->
      let system = allActive()
      let before = ShortcutFeature.all |> Array.map system.Struct
      let guard = ShortcutGuard(system.Platform)

      system.Foreground <- true
      guard.Sync()
      system.Foreground <- false
      guard.Sync()

      let after = ShortcutFeature.all |> Array.map system.Struct
      Expect.equal after before "structs restored byte for byte"
      Expect.isFalse guard.IsSuppressed "guard reports restored"

    testCase "a feature the player has on is never written"
    <| fun _ ->
      let system =
        FakeSystem [
          ShortcutFeature.stickyKeys,
          shortcutActive ||| ShortcutFeature.FeatureOn
          ShortcutFeature.filterKeys, 0x2
          ShortcutFeature.toggleKeys, shortcutActive
        ]

      let guard = ShortcutGuard(system.Platform)

      system.Foreground <- true
      guard.Sync()

      Expect.equal system.Writes 1 "only toggle keys written on focus"

      Expect.equal
        (system.Flags ShortcutFeature.stickyKeys)
        (shortcutActive ||| ShortcutFeature.FeatureOn)
        "sticky keys in use stays on with its shortcut"

      Expect.equal
        (system.Flags ShortcutFeature.filterKeys)
        0x2
        "filter keys without a shortcut stays as it is"

      guard.Restore()
      Expect.equal system.Writes 2 "only toggle keys written on restore"

    testCase "a refused read leaves the feature alone"
    <| fun _ ->
      let system = allActive()
      system.Refused <- Set.singleton ShortcutFeature.filterKeys.Get
      let guard = ShortcutGuard(system.Platform)

      system.Foreground <- true
      guard.Sync()
      guard.Restore()

      Expect.equal system.Writes 4 "two features written twice each"

      Expect.equal
        (system.Flags ShortcutFeature.filterKeys)
        shortcutActive
        "refused feature unchanged"

    testCase "frames without a focus change write nothing"
    <| fun _ ->
      let system = allActive()
      let guard = ShortcutGuard(system.Platform)

      system.Foreground <- true

      for _ in 1..5 do
        guard.Sync()

      Expect.equal system.Writes 3 "one write per feature on focus"

      system.Foreground <- false

      for _ in 1..5 do
        guard.Sync()

      Expect.equal system.Writes 6 "one write per feature on focus loss"

    testCase "restore without suppress, and a second restore, write nothing"
    <| fun _ ->
      let system = allActive()
      let guard = ShortcutGuard(system.Platform)

      guard.Restore()
      Expect.equal system.Writes 0 "nothing saved, nothing written"

      system.Foreground <- true
      guard.Sync()
      guard.Restore()
      guard.Restore()
      Expect.equal system.Writes 6 "second restore writes nothing"

    testCase "a closed guard restores and never suppresses again"
    <| fun _ ->
      let system = allActive()
      let before = ShortcutFeature.all |> Array.map system.Struct
      let guard = ShortcutGuard(system.Platform)

      system.Foreground <- true
      guard.Sync()
      guard.Close()

      let after = ShortcutFeature.all |> Array.map system.Struct
      Expect.equal after before "close restores the settings"

      guard.Sync()
      guard.Suppress()
      Expect.equal system.Writes 6 "no writes after close"
      Expect.isFalse guard.IsSuppressed "guard stays restored"

    testCase "the renderer syncs on draw and restores on dispose"
    <| fun _ ->
      let system = allActive()
      let before = ShortcutFeature.all |> Array.map system.Struct
      let guard = ShortcutGuard(system.Platform)
      let renderer = AccessibilityShortcuts.rendererWith<unit> guard
      let ctx = GameContext.create(800, 600)

      system.Foreground <- true
      renderer.Draw(ctx, (), gameTime)
      Expect.isTrue guard.IsSuppressed "draw with focus suppresses"

      match renderer with
      | :? IDisposable as d -> d.Dispose()
      | _ -> failtest "renderer is not disposable"

      let after = ShortcutFeature.all |> Array.map system.Struct
      Expect.equal after before "dispose restores the settings"

    testCase "the renderer does nothing off Windows"
    <| fun _ ->
      if OperatingSystem.IsWindows() then
        skiptest "the Windows renderer changes real session settings"

      let renderer = AccessibilityShortcuts.renderer<unit>()
      renderer.Draw(GameContext.create(800, 600), (), gameTime)

      Expect.isFalse
        (renderer :? IDisposable)
        "no guard behind the renderer off Windows"
  ]
