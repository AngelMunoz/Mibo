module Mibo.Adaptive.Demo.Program

open System
open System.Text
open AdaptiveSlop.Core
open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Adaptive.Demo.Types
open Mibo.Adaptive.Demo.World

// ─────────────────────────────────────────────────────────────────────────────
// Two frontends for the same world.
//
//   dotnet run --project src/Mibo.Adaptive.Demo -- sim     (default)
//   dotnet run --project src/Mibo.Adaptive.Demo -- raylib
//
// Both write the `Input` root and step the runner; the renderer (ASCII court
// or raylib window) is a pure function of the forced frame. The simulation
// never knows which frontend is attached.
// ─────────────────────────────────────────────────────────────────────────────

// ── The renderer: a pure function of the frame ───────────────────────────────

let renderCourt(frame: RenderFrame) : string =
  let cols, rows = 58, 28
  let grid = Array2D.create rows cols ' '

  let xToCol(x: float32) =
    int(x / courtWidth * float32(cols - 1)) |> min(cols - 1) |> max 0

  let yToRow(y: float32) =
    int(y / courtHeight * float32(rows - 1)) |> min(rows - 1) |> max 0

  // center line and borders
  for r = 0 to rows - 1 do
    grid[r, cols / 2] <- if r % 2 = 0 then ':' else ' '

    for c = 0 to cols - 1 do
      if r = 0 || r = rows - 1 || c = 0 || c = cols - 1 then
        grid[r, c] <- '#'

  // paddles (3 rows tall) and ball — clamped inside the border
  let lp = frame.LeftPaddleRect

  for r = yToRow(lp.Y - 30f) to yToRow(lp.Y + 30f) do
    grid[r, xToCol lp.X |> max 1 |> min(cols - 2)] <- '|'

  let rp = frame.RightPaddleRect

  for r = yToRow(rp.Y - 30f) to yToRow(rp.Y + 30f) do
    grid[r, xToCol rp.X |> max 1 |> min(cols - 2)] <- '|'

  let ballCol = xToCol(frame.BallRect.X + ballRadius) |> max 1 |> min(cols - 2)

  let ballRow = yToRow(frame.BallRect.Y + ballRadius) |> max 1 |> min(rows - 2)

  grid[ballRow, ballCol] <- 'o'

  if frame.Threat then
    grid[1, 1] <- '!'

  let sb = StringBuilder()

  for r = 0 to rows - 1 do
    for c = 0 to cols - 1 do
      sb.Append(grid[r, c]) |> ignore

    sb.AppendLine() |> ignore

  sb.ToString()

// ── sim: headless simulation with telemetry ─────────────────────────────────

let runSim() =
  let liveFrames = 360 // 6 seconds at 60 fps
  let pausedFrames = 60 // 1 second with the simulation frozen

  printfn "AdaptivePong — headless simulation (AI vs AI, 60 fps, %d s)"
  <| ((liveFrames + pausedFrames) / 60)

  let world = World.create()
  use runner = new AdaptiveHeadless<RenderFrame>(World.adaptiveWorld world)

  let mutable lastScore = "0   -   0"

  for frame = 1 to liveFrames do
    world.Input.Set(
      {
        LeftMove = World.aiMove world true * World.aiSpeedFactor
        RightMove = World.aiMove world false * World.aiSpeedFactor
      }
    )

    let f = runner.Step(TimeSpan.FromMilliseconds 16.0)

    if f.ScoreLabel <> lastScore then
      printfn "GOAL!  score is now %s" f.ScoreLabel
      lastScore <- f.ScoreLabel

    if frame % 30 = 1 then
      printfn
        "\nframe %4d   ball (%4.0f, %4.0f)   left %4.0f right %4.0f   %s   %s"
        frame
        (f.BallRect.X + ballRadius)
        (f.BallRect.Y + ballRadius)
        (f.LeftPaddleRect.Y + paddleHeight / 2f)
        (f.RightPaddleRect.Y + paddleHeight / 2f)
        f.ScoreLabel
        f.ClockLabel

      printf "%s" (renderCourt f)

  // The paused phase: no input, the router writes nothing, so forcing the
  // frame is pure version checks. Measure it.
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

// ── raylib: the same world, windowed ─────────────────────────────────────────

/// Convert a raylib-cs CBool wrapper to a native F# bool (same extension as
/// Mibo.Raylib's RaylibExtensions — the demo uses raw Raylib-cs on purpose).
[<System.Runtime.CompilerServices.Extension>]
type RaylibExtensions =
  [<System.Runtime.CompilerServices.Extension>]
  static member inline AsBool(c: Raylib_cs.CBool) : bool =
    Raylib_cs.CBool.op_Implicit(c)

let toRaylibRect(r: Rect) =
  Raylib_cs.Rectangle(r.X, r.Y, r.Width, r.Height)

let runRaylib() =
  Raylib_cs.Raylib.InitWindow(
    int courtWidth,
    int courtHeight,
    "AdaptivePong — AdaptiveHeadless"
  )

  Raylib_cs.Raylib.SetTargetFPS(60)

  let world = World.create()
  use runner = new AdaptiveHeadless<RenderFrame>(World.adaptiveWorld world)

  while not runner.ShouldQuit
        && not(Raylib_cs.Raylib.WindowShouldClose().AsBool()) do
    let dt = Raylib_cs.Raylib.GetFrameTime()

    let leftMove =
      (if Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.W).AsBool() then
         -1f
       else
         0f)
      + if Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.S).AsBool() then
          1f
        else
          0f

    world.Input.Set
      {
        LeftMove = leftMove
        RightMove = World.aiMove world false * World.aiSpeedFactor
      }

    if Raylib_cs.Raylib.IsKeyPressed(Raylib_cs.KeyboardKey.P).AsBool() then
      world.Paused.Set(not(AVal.getValue world.Paused))

    // One Step = the whole frame: pump, time root, router, force.
    let frame = runner.Step(TimeSpan.FromSeconds(float dt))

    Raylib_cs.Raylib.BeginDrawing()
    Raylib_cs.Raylib.ClearBackground Raylib_cs.Color.Black

    let leftColor =
      if frame.Threat then
        Raylib_cs.Color.Orange
      else
        Raylib_cs.Color.White

    Raylib_cs.Raylib.DrawRectangleRec(
      toRaylibRect frame.LeftPaddleRect,
      leftColor
    )

    Raylib_cs.Raylib.DrawRectangleRec(
      toRaylibRect frame.RightPaddleRect,
      Raylib_cs.Color.White
    )

    Raylib_cs.Raylib.DrawRectangleRec(
      toRaylibRect frame.BallRect,
      Raylib_cs.Color.Red
    )

    Raylib_cs.Raylib.DrawText(
      frame.ScoreLabel,
      340,
      20,
      32,
      Raylib_cs.Color.White
    )

    Raylib_cs.Raylib.DrawText(
      frame.ClockLabel,
      20,
      20,
      20,
      Raylib_cs.Color.Gray
    )

    if AVal.getValue world.Paused then
      Raylib_cs.Raylib.DrawText("PAUSED", 340, 400, 40, Raylib_cs.Color.Gray)

    Raylib_cs.Raylib.EndDrawing()

  runner.Dispose()
  Raylib_cs.Raylib.CloseWindow()

// ── entry ────────────────────────────────────────────────────────────────────

[<EntryPoint>]
let main args =
  match args with
  | [| "raylib" |] -> runRaylib()
  | _ -> runSim()

  0
