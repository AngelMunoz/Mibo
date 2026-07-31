// Depth-only shadow-pass shader — GROUPED-UNIFORM ISOLATE for DX12.
//
// This is the DepthShadow.fx shader with ONLY the grouped-uniform depth technique.
// Exists as a separate .fx for the same reason as ForwardPbrGrouped.fx: the DX12
// mgfx reflection parser drops bonePaletteGroup from the main DepthShadow.fx when
// all techniques are present. Loaded ONLY on DX12.
//
// §6.1: plain float4x4 (no row_major), mul(position, matrix) vector-LEFT.
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

float4x4 matModel;
float4x4 viewProj;

#define MAX_GROUP_PALETTES 320
float4x4 bonePaletteGroup[MAX_GROUP_PALETTES];
int groupBoneCount;

struct VS_INPUT_SKINNED_INSTANCED {
  float3 Position    : POSITION0;
  float4 BoneWeights : BLENDWEIGHT0;
  int4   BoneIndices : BLENDINDICES0;
  float4 Row0        : TEXCOORD1;
  float4 Row1        : TEXCOORD2;
  float4 Row2        : TEXCOORD3;
  float4 Row3        : TEXCOORD4;
  float PaletteOffset : TEXCOORD6;
};

struct VS_OUTPUT {
  float4 Position  : SV_POSITION;
  float2 Depth     : TEXCOORD0; // x = clip.z, y = clip.w (divide in PS)
};

VS_OUTPUT VS_SkinnedInstancedGrouped(VS_INPUT_SKINNED_INSTANCED input) {
  VS_OUTPUT output;

  int base = (int)input.PaletteOffset * groupBoneCount;

  float4x4 skin =
    input.BoneWeights.x * bonePaletteGroup[base + input.BoneIndices.x] +
    input.BoneWeights.y * bonePaletteGroup[base + input.BoneIndices.y] +
    input.BoneWeights.z * bonePaletteGroup[base + input.BoneIndices.z] +
    input.BoneWeights.w * bonePaletteGroup[base + input.BoneIndices.w];

  float4 skinnedPos = mul(float4(input.Position, 1.0), skin);
  float4x4 instanceWorld = float4x4(input.Row0, input.Row1, input.Row2, input.Row3);
  float4 world = mul(mul(skinnedPos, matModel), instanceWorld);
  float4 clip = mul(world, viewProj);
  output.Position = clip;
  output.Depth = clip.zw;
  return output;
}

float4 PS_Main(VS_OUTPUT input) : SV_TARGET {
  float d = input.Depth.x / input.Depth.y;
  return float4(d, d, d, 1.0);
}

technique DepthSkinnedInstancedGrouped {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_SkinnedInstancedGrouped();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};
