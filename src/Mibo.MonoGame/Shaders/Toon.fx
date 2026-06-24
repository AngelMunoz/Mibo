// Toon (cel) shader — a minimal custom shading effect for the MonoThreeD sample,
// exercised via Draw3D.beginEffect / Draw3D.endEffect (use case 2 of the v2 pipeline
// staging). Proves a user effect can inherit scene DATA (camera, lights, material,
// bones, shadows) by declaring the matching uniforms — it does NOT inherit the PBR
// shader itself (v2 spec §3).
//
// The shading model is banded N·L + a rim term (cheap, reads clearly as "toon"). It
// declares the directional + ambient lights, the albedo map/colour, the bone palette,
// and the shadow-sampling uniforms — so a toon-scoped draw inherits lighting,
// animation, and shadows by name, with absent uniforms no-op'd by SceneUpload.
//
// §6 compliance (same as ForwardPbr.fx):
//  - §6.1: plain float4x4, mul(position, matrix) vector-LEFT.
//  - §6.3: OpenGL capped at SM3.0. Shadow sampling uses tex2Dlod (gradient-free) so it
//          composes with [loop]+break light loops; texel size comes in as a uniform.
#if OPENGL
  #define VS_SHADERMODEL vs_3_0
  #define PS_SHADERMODEL ps_3_0
#else
  #define VS_SHADERMODEL vs_5_0
  #define PS_SHADERMODEL ps_5_0
#endif

#define MAX_BONES 128
#define MAX_SHADOW_CASTERS 16

// ------------------------------------------------------------------
// Samplers + material (SceneUpload uploads these by name when declared)
// ------------------------------------------------------------------

sampler2D texture0 : register(s0); // albedo

float4 albedoColor;
float opacity;
float2 tiling;

// ------------------------------------------------------------------
// Lights (ambient + 1 directional — the toon model only uses these)
// ------------------------------------------------------------------

float3 ambientColor;
float ambientIntensity;

float3 dirLightDir;
float3 dirLightColor;
float dirLightIntensity;
int dirLightCastsShadows;

// ------------------------------------------------------------------
// Shadow atlas (opt-in by declaration). Manual 3x3 PCF, gradient-free
// (tex2Dlod), matching ForwardPbr.fx so a toon-scoped draw can sample shadows.
// ------------------------------------------------------------------

sampler2D shadowAtlas : register(s5);
float4x4 shadowViewProjs[MAX_SHADOW_CASTERS];
float4 shadowUVOffsets[MAX_SHADOW_CASTERS];
float2 shadowTexelSize;

float computeDirShadow(float3 worldPos) {
  if (dirLightCastsShadows == 0)
    return 1.0;

  // Directional caster is registered first (slot 0 by convention).
  float4 sc = mul(float4(worldPos, 1.0), shadowViewProjs[0]);
  float3 ndc = sc.xyz / sc.w;

  if (ndc.z > 1.0)
    return 1.0;

  ndc = ndc * 0.5 + 0.5; // to [0,1]

  if (ndc.x < 0.0 || ndc.x > 1.0 || ndc.y < 0.0 || ndc.y > 1.0)
    return 1.0;

  float4 uvOff = shadowUVOffsets[0];
  float2 atlasUV = ndc.xy * uvOff.zw + uvOff.xy;

  float shadow = 0.0;
  [unroll]
  for (int x = -1; x <= 1; x++) {
    [unroll]
    for (int y = -1; y <= 1; y++) {
      float2 sampleUV = atlasUV + float2(float(x), float(y)) * shadowTexelSize;
      float d = tex2Dlod(shadowAtlas, float4(sampleUV, 0.0, 0.0)).r;
      shadow += (ndc.z > d) ? 0.0 : 1.0;
    }
  }
  return shadow / 9.0;
}

// ------------------------------------------------------------------
// Matrices + camera
// ------------------------------------------------------------------

float4x4 matModel;
float4x4 viewProj;
float4x4 normalMatrix;
float3 cameraPos;

struct VS_INPUT {
  float3 Position : POSITION0;
  float2 TexCoord : TEXCOORD0;
  float3 Normal   : NORMAL0;
};

struct VS_OUTPUT {
  float4 Position : SV_POSITION;
  float2 TexCoord : TEXCOORD0;
  float3 Normal   : TEXCOORD1;
  float3 WorldPos : TEXCOORD2;
};

VS_OUTPUT VS_Standard(VS_INPUT input) {
  VS_OUTPUT output;
  float4 world = mul(float4(input.Position, 1.0), matModel);
  output.Position = mul(world, viewProj);
  output.TexCoord = input.TexCoord;
  output.Normal = mul(input.Normal, (float3x3) normalMatrix);
  output.WorldPos = world.xyz;
  return output;
}

// ------------------------------------------------------------------
// Skinned vertex shader — 4-bone linear blend skinning, mirrors ForwardPbr.fx
// VS_Skinned so a toon-scoped AnimatedModel inherits its bone palette.
// ------------------------------------------------------------------

float4x4 boneMatrices[MAX_BONES];

struct VS_INPUT_SKINNED {
  float3 Position   : POSITION0;
  float2 TexCoord   : TEXCOORD0;
  float3 Normal     : NORMAL0;
  float4 BoneWeights: BLENDWEIGHT0;
  int4   BoneIndices: BLENDINDICES0;
};

VS_OUTPUT VS_Skinned(VS_INPUT_SKINNED input) {
  VS_OUTPUT output;

  float4x4 skin =
    input.BoneWeights.x * boneMatrices[input.BoneIndices.x] +
    input.BoneWeights.y * boneMatrices[input.BoneIndices.y] +
    input.BoneWeights.z * boneMatrices[input.BoneIndices.z] +
    input.BoneWeights.w * boneMatrices[input.BoneIndices.w];

  float4 skinnedPos = mul(float4(input.Position, 1.0), skin);
  float3 skinnedN = mul(input.Normal, (float3x3) skin);

  float4 world = mul(skinnedPos, matModel);
  output.Position = mul(world, viewProj);
  output.TexCoord = input.TexCoord;
  output.Normal = mul(skinnedN, (float3x3) normalMatrix);
  output.WorldPos = world.xyz;
  return output;
}

// ------------------------------------------------------------------
// Fragment: banded N·L toon shading + rim term.
// ------------------------------------------------------------------

// Quantise the diffuse term into discrete bands → the cel-shaded look.
float toonBand(float NdotL) {
  // 3 bands: shadow / mid / lit. Smoothstep softens the step edges.
  float b = smoothstep(0.0, 0.05, NdotL) * 0.4;       // mid band
  b += smoothstep(0.5, 0.55, NdotL) * 0.6;            // lit band
  return b;
}

float4 PS_Main(VS_OUTPUT input) : SV_TARGET {
  float2 uv = input.TexCoord * tiling;
  float4 texColor = tex2D(texture0, uv) * albedoColor;
  float3 albedo = texColor.rgb;

  float3 N = normalize(input.Normal);
  float3 V = normalize(cameraPos - input.WorldPos);

  // Ambient base.
  float3 ambient = ambientColor * albedo * ambientIntensity;

  // Directional (L points toward the light; dirLightDir points along travel).
  float3 L = normalize(-dirLightDir);
  float NdotL = dot(N, L);
  float band = toonBand(max(NdotL, 0.0));
  float shadow = computeDirShadow(input.WorldPos);
  float3 dir = dirLightColor * dirLightIntensity * albedo * band * shadow;

  // Rim: brighten edges facing away from the camera for a toon outline feel.
  float rim = 1.0 - max(dot(N, V), 0.0);
  rim = smoothstep(0.6, 1.0, rim);
  float3 rimColor = dirLightColor * rim * 0.4;

  float3 result = ambient + dir + rimColor;
  return float4(result, texColor.a * opacity);
}

// ------------------------------------------------------------------
// Techniques
// ------------------------------------------------------------------

technique Standard {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Standard();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};

technique Skinned {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Skinned();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};
