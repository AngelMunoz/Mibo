module SpaceBattle.Shaders

open System.Diagnostics
open System.Numerics
open Microsoft.FSharp.NativeInterop
open Mibo.Elmish
open Mibo.Elmish.Next.Graphics2D
open Raylib_cs

#nowarn "9"

// ─────────────────────────────────────────────────────────────
// Uniform helpers (DisableRuntimeMarshalling-safe)
// ─────────────────────────────────────────────────────────────

module private Uniform =
  let setFloat (shader: Shader) (loc: int) (value: float32) =
    use p = fixed &value

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Float
    )

  let setVec2 (shader: Shader) (loc: int) (value: Vector2) =
    use p = fixed &value

    Raylib.SetShaderValue(
      shader,
      loc,
      NativePtr.toVoidPtr p,
      ShaderUniformDataType.Vec2
    )

// ─────────────────────────────────────────────────────────────
// Deep-space procedural skybox
//
// Based on NightSky.fx — uses a custom vertex shader that passes
// vertex positions as screen coordinates, then the fragment shader
// derives a 3D direction vector for procedural noise.
// ─────────────────────────────────────────────────────────────

let private skyboxVertex =
  """
#version 330

in vec3 vertexPosition;
uniform mat4 mvp;

out vec3 fragScreenPos;

void main() {
  gl_Position = mvp * vec4(vertexPosition, 1.0);
  fragScreenPos = vertexPosition;
}
"""

let private skyboxFragment =
  """
#version 330

in vec3 fragScreenPos;

uniform float time;
uniform vec2 cameraPos;
uniform vec2 resolution;

out vec4 finalColor;

// ── 3D Hash / Noise (from NightSky.fx) ─────────────────────────

float hash(vec3 p) {
  p = fract(p * 0.3183099 + 0.1);
  p *= 17.0;
  return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise(vec3 x) {
  vec3 i = floor(x);
  vec3 f = fract(x);
  f = f * f * (3.0 - 2.0 * f);

  float a = hash(i + vec3(0, 0, 0));
  float b = hash(i + vec3(1, 0, 0));
  float c = hash(i + vec3(0, 1, 0));
  float d = hash(i + vec3(1, 1, 0));
  float e = hash(i + vec3(0, 0, 1));
  float fv = hash(i + vec3(1, 0, 1));
  float g = hash(i + vec3(0, 1, 1));
  float h = hash(i + vec3(1, 1, 1));

  return mix(mix(mix(a, b, f.x), mix(c, d, f.x), f.y),
             mix(mix(e, fv, f.x), mix(g, h, f.x), f.y),
             f.z);
}

float fbm(vec3 x) {
  float v = 0.0;
  float a = 0.5;
  vec3 shift = vec3(100.0);
  for (int i = 0; i < 4; i++) {
    v += a * noise(x);
    x = x * 2.0 + shift;
    a *= 0.5;
  }
  return v;
}

// ── Star layer (threshold-based, like NightSky.fx) ─────────────

float starLayer(vec3 dir, float scale, float threshold) {
  vec3 cell = floor(dir * scale);
  float h = hash(cell);
  float brightness = smoothstep(threshold, 1.0, h);
  // twinkle
  brightness *= 0.7 + 0.3 * sin(time * (1.0 + h * 3.0) + h * 60.0);
  return brightness;
}

// ── Main ───────────────────────────────────────────────────────

void main() {
  // Derive a 3D direction from screen position + camera parallax
  vec2 uv = fragScreenPos.xy / resolution;
  vec2 cam = cameraPos * 0.001;
  vec3 dir = normalize(vec3((uv - 0.5) * 2.0 + cam * 0.5, 1.0));

  // 1. Deep space background
  vec3 deepSpace = vec3(0.0, 0.02, 0.05);
  vec3 horizon   = vec3(0.05, 0.1, 0.25);
  float horizonFactor = pow(max(0.0, dir.y + 0.2), 3.0);
  vec3 sky = mix(deepSpace, horizon, horizonFactor);

  // 2. Nebulae — two-tone clouds via 3D FBM
  float cloudNoise = fbm(dir * 2.0);
  vec3 nebula1 = vec3(0.3, 0.0, 0.4); // purple
  vec3 nebula2 = vec3(0.0, 0.3, 0.4); // teal
  float nebulaMask = smoothstep(0.4, 0.8, cloudNoise);
  vec3 nebula = mix(nebula1, nebula2, noise(dir * 5.0)) * nebulaMask * 0.6;
  sky += nebula;

  // 3. Multi-layer stars with parallax
  //    Offset direction per layer — deeper layers move less
  vec3 farDir  = dir + vec3(cam * 0.05, 0.0);
  vec3 midDir  = dir + vec3(cam * 0.2,  0.0);
  vec3 nearDir = dir + vec3(cam * 0.5,  0.0);

  float stars = 0.0;
  stars += starLayer(farDir,  100.0, 0.995) * 0.9; // big, bright, rare
  stars += starLayer(midDir,  250.0, 0.985) * 0.6; // medium
  stars += starLayer(nearDir, 500.0, 0.96)  * 0.3; // small dust

  sky += stars;

  finalColor = vec4(sky, 1.0);
}
"""

// ─────────────────────────────────────────────────────────────
// Skybox model
// ─────────────────────────────────────────────────────────────

type SkyboxModel = {
  Shader: Shader
  TimeLoc: int
  CamLoc: int
  ResLoc: int
  Timer: Stopwatch
}

module Skybox =

  let init(vpWidth: float32, vpHeight: float32) =
    let shader = Raylib.LoadShaderFromMemory(skyboxVertex, skyboxFragment)

    let sky = {
      Shader = shader
      TimeLoc = Raylib.GetShaderLocation(shader, "time")
      CamLoc = Raylib.GetShaderLocation(shader, "cameraPos")
      ResLoc = Raylib.GetShaderLocation(shader, "resolution")
      Timer = Stopwatch.StartNew()
    }

    Uniform.setVec2 sky.Shader sky.ResLoc (Vector2(vpWidth, vpHeight))
    sky

  let render
    (cameraTarget: Vector2, vpWidth: float32, vpHeight: float32)
    (sky: SkyboxModel)
    (buffer: RenderBuffer2D)
    =
    Uniform.setFloat
      sky.Shader
      sky.TimeLoc
      (float32 sky.Timer.Elapsed.TotalSeconds)

    Uniform.setVec2 sky.Shader sky.CamLoc cameraTarget
    Uniform.setVec2 sky.Shader sky.ResLoc (Vector2(vpWidth, vpHeight))

    buffer
    |> Draw.beginShader -1000<RenderLayer> sky.Shader
    |> Draw.fillRect
      (-1000<RenderLayer>, Color.White)
      (Rectangle(0f, 0f, vpWidth, vpHeight))
    |> Draw.endShader -1000<RenderLayer>
