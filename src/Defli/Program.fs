module Defli.Program

open System
open System.Numerics
open Mibo.Adaptive
open Mibo.Elmish
open Defli.World

// ─────────────────────────────────────────────────────────────
// Defli on AdaptiveHeadless — headless simulation first.
//
//   dotnet run --project src/Defli -- sim
//
// The autonomous policy (Application.policy) drives the game; the
// runner steps the world at 60 fps virtual time; the frame is
// forced once per Step and the console reads it. A windowed
// raylib frontend is milestone 2.
// ─────────────────────────────────────────────────────────────

let runSim() =
  let simSeconds = 120
  let liveFrames = 60 * simSeconds
  let pausedFrames = 60

  printfn
    "Defli — headless tower defense on AdaptiveHeadless (autonomous policy, %d s, %d fps)"
    simSeconds
    60

  let cfg = WorldConfig.defaults
  let world = World.init cfg

  use runner =
    new AdaptiveHeadless<Frame.RenderFrame>(Frame.adaptiveWorld world)

  let mutable lastWave = 0
  let mutable gameOverReported = false

  for frame = 1 to liveFrames do
    Application.policy world frame

    let f = runner.Step(TimeSpan.FromMilliseconds 16.0)

    if f.WaveNumber <> lastWave then
      printfn "Wave %d started   %s" f.WaveNumber f.Banner
      lastWave <- f.WaveNumber

    if f.GameOver && not gameOverReported then
      printfn "GAME OVER — the base fell during wave %d" f.WaveNumber
      gameOverReported <- true

    if frame % 60 = 1 then
      printfn
        "t=%4.0fs  wave %2d  enemies %3d  towers %2d  projectiles %3d  gold %3d  lives %2d   %s"
        (float frame / 60.0)
        f.WaveNumber
        f.EnemyCount
        f.TowerCount
        f.ProjectileCount
        f.Gold
        f.Lives
        f.Banner

  // The paused phase: the router writes nothing, so forcing the frame
  // is pure version checks. Measure it.
  world.Paused.Set(true)
  printfn "\n--- simulation paused (the router stops writing roots) ---"

  GC.Collect()
  GC.WaitForPendingFinalizers()
  GC.Collect()
  let before = GC.GetAllocatedBytesForCurrentThread()

  for _ = 1 to pausedFrames do
    runner.Step(TimeSpan.FromMilliseconds 16.0) |> ignore

  let allocatedPerFrame =
    (GC.GetAllocatedBytesForCurrentThread() - before) / int64 pausedFrames

  Telemetry.print (liveFrames + pausedFrames) pausedFrames allocatedPerFrame
  runner.Dispose()

[<EntryPoint>]
let main args =
  match args with
  | _ -> runSim()

  0
