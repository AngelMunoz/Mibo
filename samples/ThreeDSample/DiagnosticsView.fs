module ThreeDSample.Diagnostics

open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Next.Graphics2D
open Raylib_cs
open ThreeDSample.Types

// ── System ──

let inline system(model: DiagnosticsModel) : unit = model.Fps <- Raylib.GetFPS()

let view (ctx: GameContext) (model: GameModel) (buffer: RenderBuffer2D) =
  let diag = model.Diagnostics

  buffer
  |> Draw.text {
    Font = diag.Font
    Text = $"FPS: {diag.Fps}  Chunks: {diag.ChunkCount}  Score: {diag.Score}"
    Position = Vector2(10.0f, 10.0f)
    FontSize = 20.0f
    Spacing = 1.0f
    Color = Color.Yellow
    Layer = 0<RenderLayer>
  }
  |> Draw.text {
    Font = diag.Font
    Text =
      $"Time: {diag.TimeOfDay:F1}h  Pos: ({diag.PlayerX:F0},{diag.PlayerY:F0},{diag.PlayerZ:F0})  Grounded: {diag.IsGrounded}  Particles: {diag.ParticleCount}"
    Position = Vector2(10.0f, 35.0f)
    FontSize = 20.0f
    Spacing = 1.0f
    Color = Color.Yellow
    Layer = 0<RenderLayer>
  }
  |> Draw.drop
