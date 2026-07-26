#if OPENGL
  #define VS_SHADERMODEL vs_3_0
  #define PS_SHADERMODEL ps_3_0
#elif defined(SM6)
  #define VS_SHADERMODEL vs_6_0
  #define PS_SHADERMODEL ps_6_0
#else
  #define VS_SHADERMODEL vs_5_0
  #define PS_SHADERMODEL ps_5_0
#endif

// Minimal instanced effect for B7: vertex shader composes a per-instance world
// matrix (read from stream 1, TEXCOORD1..4) with the shared view+projection, and
// the pixel shader applies ambient + a single directional light.
//
// Convention (per AGENTS.md §6.1 / the existing LitSprite.fx): row-vector,
// vector-LEFT — `mul(position, matrix)`. The instance world rows come in as
// Vector4 TEXCOORD slots; reassembling them into a float4x4 takes four muls.
//
// B9 will extend this into the full PBR HLSL (Cook-Torrance, normal maps, etc.);
// B7 ships only what's needed to validate native hardware instancing.

float4x4 ViewProj;

float3 AmbientColor;
float3 DirLightDir;
float3 DirLightColor;
float3 AlbedoColor;

// Stream 1: per-instance world matrix, row-major. Each row is a Vector4.
// Usage indices 1..4 so they don't collide with mesh VertexPositionNormalTexture's
// own TEXCOORD0 (texture coords) on stream 0.
struct VS_INPUT {
  float4 Position : POSITION0;
  float3 Normal   : NORMAL0;
  float2 TexCoord : TEXCOORD0;
  // Per-instance (stream 1, instanceFrequency = 1)
  float4 WorldRow0 : TEXCOORD1;
  float4 WorldRow1 : TEXCOORD2;
  float4 WorldRow2 : TEXCOORD3;
  float4 WorldRow3 : TEXCOORD4;
};

struct VS_OUTPUT {
  float4 Position : SV_POSITION;
  float3 Normal   : TEXCOORD0;
};

VS_OUTPUT VS_Main(VS_INPUT input) {
  VS_OUTPUT output;
  float4x4 world = float4x4(input.WorldRow0, input.WorldRow1, input.WorldRow2, input.WorldRow3);
  output.Position = mul(mul(input.Position, world), ViewProj);
  // Normal: transform by world (ignore translation/skew for this flat-lit pass).
  output.Normal = mul(input.Normal, (float3x3)world);
  return output;
}

float4 PS_Main(VS_OUTPUT input) : SV_TARGET {
  float3 N = normalize(input.Normal);
  float3 L = normalize(-DirLightDir);
  float diffuse = max(dot(N, L), 0.0);
  float3 lighting = AmbientColor + DirLightColor * diffuse;
  return float4(AlbedoColor * lighting, 1.0);
}

technique Instanced {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Main();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
}

// Colored variant: stream 1 additionally carries a per-instance color on TEXCOORD5
// (VertexInstanceWorldColor). Albedo scales by InstanceColor.rgb, alpha by InstanceColor.a.
struct VS_INPUT_COLOR {
  float4 Position : POSITION0;
  float3 Normal   : NORMAL0;
  float2 TexCoord : TEXCOORD0;
  // Per-instance (stream 1, instanceFrequency = 1)
  float4 WorldRow0 : TEXCOORD1;
  float4 WorldRow1 : TEXCOORD2;
  float4 WorldRow2 : TEXCOORD3;
  float4 WorldRow3 : TEXCOORD4;
  float4 InstanceColor : TEXCOORD5;
};

struct VS_OUTPUT_COLOR {
  float4 Position : SV_POSITION;
  float3 Normal   : TEXCOORD0;
  float4 InstanceColor : TEXCOORD1;
};

VS_OUTPUT_COLOR VS_MainColor(VS_INPUT_COLOR input) {
  VS_OUTPUT_COLOR output;
  float4x4 world = float4x4(input.WorldRow0, input.WorldRow1, input.WorldRow2, input.WorldRow3);
  output.Position = mul(mul(input.Position, world), ViewProj);
  output.Normal = mul(input.Normal, (float3x3)world);
  output.InstanceColor = input.InstanceColor;
  return output;
}

float4 PS_MainColor(VS_OUTPUT_COLOR input) : SV_TARGET {
  float3 N = normalize(input.Normal);
  float3 L = normalize(-DirLightDir);
  float diffuse = max(dot(N, L), 0.0);
  float3 lighting = AmbientColor + DirLightColor * diffuse;
  return float4(AlbedoColor * input.InstanceColor.rgb * lighting, input.InstanceColor.a);
}

technique InstancedColor {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_MainColor();
    PixelShader = compile PS_SHADERMODEL PS_MainColor();
  }
}
