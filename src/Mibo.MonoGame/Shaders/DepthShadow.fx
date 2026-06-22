// Depth-only shadow-pass shader. Stub for B10 (directional shadow atlas);
// B9 creates the file but does not bind it in any dispatch path.
//
// §6.1: plain float4x4, mul(position, matrix) vector-LEFT.
#if OPENGL
  #define VS_SHADERMODEL vs_3_0
  #define PS_SHADERMODEL ps_3_0
#else
  #define VS_SHADERMODEL vs_5_0
  #define PS_SHADERMODEL ps_5_0
#endif

float4x4 matModel;
float4x4 viewProj;

struct VS_INPUT {
  float3 Position : POSITION0;
};

struct VS_OUTPUT {
  float4 Position : SV_POSITION;
};

VS_OUTPUT VS_Main(VS_INPUT input) {
  VS_OUTPUT output;
  float4 world = mul(float4(input.Position, 1.0), matModel);
  output.Position = mul(world, viewProj);
  return output;
}

float4 PS_Main(VS_OUTPUT input) : SV_TARGET {
  return float4(1.0, 1.0, 1.0, 1.0);
}

technique Depth {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Main();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};
