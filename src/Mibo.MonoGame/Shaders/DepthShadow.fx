// Depth-only shadow-pass shader. Writes non-linear depth (position.z after the
// w-divide, mapped to [0,1]) to the .r channel of an R32F color render target.
//
// MonoGame cannot create a sampleable depth-only RenderTarget2D (depth buffers are
// non-sampleable on both backends), so the shadow depth is written into a color
// attachment. The forward pass samples this with a comparison sampler for hardware PCF.
//
// §6.1: plain float4x4 (no row_major), mul(position, matrix) vector-LEFT.
// §6.3: SM3.0-clean profile split.
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

#define MAX_BONES 128

float4x4 matModel;
float4x4 viewProj;
float4x4 boneMatrices[MAX_BONES];

struct VS_INPUT {
  float3 Position : POSITION0;
};

struct VS_INPUT_SKINNED {
  float3 Position    : POSITION0;
  float4 BoneWeights : BLENDWEIGHT0;
  int4   BoneIndices : BLENDINDICES0;
};

struct VS_OUTPUT {
  float4 Position  : SV_POSITION;
  float2 Depth     : TEXCOORD0; // x = clip.z, y = clip.w (divide in PS)
};

VS_OUTPUT VS_Main(VS_INPUT input) {
  VS_OUTPUT output;
  float4 world = mul(float4(input.Position, 1.0), matModel);
  float4 clip = mul(world, viewProj);
  output.Position = clip;
  output.Depth = clip.zw;
  return output;
}

VS_OUTPUT VS_Skinned(VS_INPUT_SKINNED input) {
  VS_OUTPUT output;

  int ids0 = input.BoneIndices.x;
  int ids1 = input.BoneIndices.y;
  int ids2 = input.BoneIndices.z;
  int ids3 = input.BoneIndices.w;

  float4x4 skin =
    input.BoneWeights.x * boneMatrices[ids0] +
    input.BoneWeights.y * boneMatrices[ids1] +
    input.BoneWeights.z * boneMatrices[ids2] +
    input.BoneWeights.w * boneMatrices[ids3];

  float4 skinnedPos = mul(float4(input.Position, 1.0), skin);
  float4 world = mul(skinnedPos, matModel);
  float4 clip = mul(world, viewProj);
  output.Position = clip;
  output.Depth = clip.zw;
  return output;
}

// ── Instanced: per-instance world matrix arrives as 4 Vector4 rows on stream 1
// (TEXCOORD1..4), matching VertexInstanceWorld and ForwardPbr.fx's VS_Instanced.
// matModel is Identity for instanced depth (the per-instance world IS the model).
struct VS_INPUT_INSTANCED {
  float3 Position : POSITION0;
  float4 Row0     : TEXCOORD1;
  float4 Row1     : TEXCOORD2;
  float4 Row2     : TEXCOORD3;
  float4 Row3     : TEXCOORD4;
};

VS_OUTPUT VS_Instanced(VS_INPUT_INSTANCED input) {
  VS_OUTPUT output;
  float4x4 world = float4x4(input.Row0, input.Row1, input.Row2, input.Row3);
  float4 clip = mul(float4(input.Position, 1.0), world);
  clip = mul(clip, viewProj);
  output.Position = clip;
  output.Depth = clip.zw;
  return output;
}

float4 PS_Main(VS_OUTPUT input) : SV_TARGET {
  // Depth in [0,1] on both backends (matches the forward shader's raw ndc.z).
  // The projection matrix already maps view z to [0,1] on both DX and OpenGL, so
  // clip.z/clip.w is directly comparable with the forward shader's ndc.z. Clear
  // color is white (1.0 = far = lit).
  float d = input.Depth.x / input.Depth.y;
  return float4(d, d, d, 1.0);
}

technique Depth {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Main();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};

technique DepthSkinned {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Skinned();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};

technique DepthInstanced {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Instanced();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};
