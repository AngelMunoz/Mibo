namespace SpaceBattle

open System
open System.Diagnostics
open System.Numerics
open Microsoft.FSharp.NativeInterop
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Layout
open SpaceBattle.Types

#nowarn "9"

module FogOfWar =

  module private Uniform =
    let setFloat (shader: Shader) (loc: int) (value: float32) =
      use p = fixed &value

      Raylib.SetShaderValue(
        shader,
        loc,
        NativePtr.toVoidPtr p,
        ShaderUniformDataType.Float
      )

  let private vertexSource =
    """#version 330

in vec3 vertexPosition;
uniform mat4 mvp;

out vec2 fragWorldPos;

void main() {
  gl_Position = mvp * vec4(vertexPosition, 1.0);
  fragWorldPos = vertexPosition.xy;
}
"""

  let private fragmentSource =
    """#version 330

in vec2 fragWorldPos;

uniform float time;

out vec4 finalColor;

// ── Hash / Noise (same family as skybox) ───────────────────────

float hash(vec2 p) {
  p = fract(p * vec2(0.3183099, 0.3678794) + 0.1);
  p *= 17.0;
  return fract(p.x * p.y * (p.x + p.y));
}

float noise(vec2 x) {
  vec2 i = floor(x);
  vec2 f = fract(x);
  f = f * f * (3.0 - 2.0 * f);

  float a = hash(i);
  float b = hash(i + vec2(1.0, 0.0));
  float c = hash(i + vec2(0.0, 1.0));
  float d = hash(i + vec2(1.0, 1.0));

  return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 x) {
  float v = 0.0;
  float a = 0.5;
  vec2 shift = vec2(100.0);
  for (int i = 0; i < 4; i++) {
    v += a * noise(x);
    x = x * 2.0 + shift;
    a *= 0.5;
  }
  return v;
}

// ── Main ───────────────────────────────────────────────────────

void main() {
  // Space dust: animated FBM noise
  vec2 noiseCoord = fragWorldPos * 0.008 + time * 0.02;
  float n = fbm(noiseCoord);

  vec2 noiseCoord2 = fragWorldPos * 0.015 + time * 0.01 + vec2(50.0);
  float n2 = fbm(noiseCoord2);

  // Deep space base
  vec3 baseColor = vec3(0.01, 0.01, 0.03);

  // Purple nebula
  vec3 nebula1 = vec3(0.15, 0.02, 0.2);
  float mask1 = smoothstep(0.3, 0.7, n);
  baseColor += nebula1 * mask1 * 0.6;

  // Blue dust
  vec3 nebula2 = vec3(0.02, 0.08, 0.2);
  float mask2 = smoothstep(0.4, 0.8, n2);
  baseColor += nebula2 * mask2 * 0.4;

  finalColor = vec4(baseColor, 1.0);
}
"""

  type FogState = {
    Shader: Shader
    TimeLoc: int
    Timer: Stopwatch
  }

  let init() =
    let shader = Raylib.LoadShaderFromMemory(vertexSource, fragmentSource)

    {
      Shader = shader
      TimeLoc = Raylib.GetShaderLocation(shader, "time")
      Timer = Stopwatch.StartNew()
    }

  let render
    (state: FogState)
    (visible: Set<struct (int * int)>)
    (grid: HexGrid<Tile>)
    (camera: Camera2D)
    (vpWidth: float32)
    (vpHeight: float32)
    (buffer: RenderBuffer2D)
    =
    Uniform.setFloat
      state.Shader
      state.TimeLoc
      (float32 state.Timer.Elapsed.TotalSeconds)

    let topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera)

    let bottomRight =
      Raylib.GetScreenToWorld2D(Vector2(vpWidth, vpHeight), camera)

    buffer |> Draw.beginShader 0<RenderLayer> state.Shader |> Draw.drop

    grid
    |> HexGrid.iterVisible
      topLeft.X
      topLeft.Y
      bottomRight.X
      bottomRight.Y
      (fun col row _tile ->
        if not(visible.Contains(struct (col, row))) then
          let worldPos = grid |> HexGrid.getWorldPos col row

          buffer
          |> Draw.fillPoly
            (0<RenderLayer>, Color.White)
            (Vector2(worldPos.X, worldPos.Y), 6, Constants.CellSize, 0f)
          |> Draw.drop)

    buffer |> Draw.endShader 0<RenderLayer>
