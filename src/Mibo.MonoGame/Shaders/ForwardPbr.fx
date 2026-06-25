// Forward PBR pipeline (Cook-Torrance). MonoGame port of the canonical
// Mibo.Raylib/Graphics3D/Pipelines/Shaders.fs forward shaders.
//
// §6 compliance:
//  - §6.1: plain float4x4 (no row_major), mul(position, matrix) vector-LEFT,
//          world->clip = mul(mul(position, matModel), viewProj).
//  - §6.2: normal-map TBN re-derived in right-handed convention; XNA/MonoGame
//          Vector3.Cross and HLSL cross use the same RH formula as GLSL cross,
//          so the canonical derivation ports without a sign flip.
//  - §6.3: OpenGL is capped at SM3.0. Pure PBR lighting is SM3.0-clean.
//          Shadow sampling (dFdx/dFdy/textureSize) is deferred to B10.
//          Light loops use [loop] + if (i >= Count) break; (OGL SM3.0 requirement).
#if OPENGL
  #define VS_SHADERMODEL vs_3_0
  #define PS_SHADERMODEL ps_3_0
#else
  #define VS_SHADERMODEL vs_5_0
  #define PS_SHADERMODEL ps_5_0
#endif

#define MAX_POINT_LIGHTS 8
#define MAX_SPOT_LIGHTS 4
#define MAX_BONES 128

// ------------------------------------------------------------------
// Samplers + scalars (mirror canonical uniform names; F# uploads by name)
// ------------------------------------------------------------------

sampler2D texture0 : register(s0); // albedo
sampler2D texture1 : register(s1); // roughness
sampler2D texture2 : register(s2); // normal
sampler2D texture3 : register(s3); // metallic
sampler2D texture4 : register(s4); // emission

float4 albedoColor;
float roughness;
float metallic;
float4 emissionColor;
float opacity;
float2 tiling;
int useNormalMap;

float3 ambientColor;
float ambientIntensity;

float3 dirLightDir;
float3 dirLightColor;
float dirLightIntensity;
int dirLightCastsShadows;

int pointLightCount;
float3 pointLightPos[MAX_POINT_LIGHTS];
float3 pointLightColor[MAX_POINT_LIGHTS];
float pointLightIntensity[MAX_POINT_LIGHTS];
float pointLightRadius[MAX_POINT_LIGHTS];
float pointLightFalloff[MAX_POINT_LIGHTS];

int spotLightCount;
float3 spotLightPos[MAX_SPOT_LIGHTS];
float3 spotLightDir[MAX_SPOT_LIGHTS];
float3 spotLightColor[MAX_SPOT_LIGHTS];
float spotLightIntensity[MAX_SPOT_LIGHTS];
float spotLightRadius[MAX_SPOT_LIGHTS];
float spotLightInnerCutoff[MAX_SPOT_LIGHTS];
float spotLightOuterCutoff[MAX_SPOT_LIGHTS];

float3 cameraPos;

// ------------------------------------------------------------------
// Shadow atlas (B10 directional; B11 adds point/spot). Multi-caster.
//
// SM3.0 approach (works identically on OGL and DX):
//  - Regular sampler2D + manual PCF (hardware SamplerComparisonState / SampleCmp
//    isn't reliably portable through mgfxc on both profiles).
//  - Slope-scale bias via RasterizerState.SlopeScaleDepthBias on the shadow pass
//    (no dFdx/dFdy shader math — applied during caster rasterization).
//  - Texel size passed as a uniform (replaces textureSize, which has no SM3.0 equivalent).
//  - Per-light shadow caster index (-1 = none): O(1) lookup, replaces the canonical
//    raylib O(N*M) caster-matching scan.
// ------------------------------------------------------------------

#define MAX_SHADOW_CASTERS 16

sampler2D shadowAtlas : register(s5);
float4x4 shadowViewProjs[MAX_SHADOW_CASTERS];
float4 shadowUVOffsets[MAX_SHADOW_CASTERS]; // xy = offset, zw = scale (atlas region remap)
float2 shadowTexelSize;                      // 1.0 / atlas resolution (replaces textureSize)
int pointLightShadowIdx[MAX_POINT_LIGHTS];   // -1 = no shadow
int spotLightShadowIdx[MAX_SPOT_LIGHTS];     // -1 = no shadow

// Generic shadow lookup for a caster slot. Manual 3x3 PCF. Bias is baked into the shadow
// map via RasterizerState on the shadow pass, so the comparison threshold is the receiver
// depth directly.
float computeShadowAt(float3 worldPos, int casterIdx) {
  if (casterIdx < 0)
    return 1.0;

  float4 sc = mul(float4(worldPos, 1.0), shadowViewProjs[casterIdx]);
  float3 ndc = sc.xyz / sc.w;

  // Outside the shadow frustum → fully lit (no shadow).
  if (ndc.z > 1.0)
    return 1.0;

  ndc = ndc * 0.5 + 0.5; // to [0,1]

  if (ndc.x < 0.0 || ndc.x > 1.0 || ndc.y < 0.0 || ndc.y > 1.0)
    return 1.0;

  float4 uvOff = shadowUVOffsets[casterIdx];
  float2 atlasUV = ndc.xy * uvOff.zw + uvOff.xy;

  float shadow = 0.0;
  [unroll]
  for (int x = -1; x <= 1; x++) {
    [unroll]
    for (int y = -1; y <= 1; y++) {
      // tex2Dlod (explicit LOD 0 — the shadow atlas has no mipmaps) instead of tex2D:
      // tex2D is a gradient instruction, which SM3.0 forbids inside loops with break
      // (the point/spot light loops break on count). tex2Dlod is gradient-free.
      float2 sampleUV = atlasUV + float2(float(x), float(y)) * shadowTexelSize;
      float d = tex2Dlod(shadowAtlas, float4(sampleUV, 0.0, 0.0)).r;
      shadow += (ndc.z > d) ? 0.0 : 1.0;
    }
  }
  return shadow / 9.0;
}

float computeDirShadow(float3 worldPos) {
  if (dirLightCastsShadows == 0)
    return 1.0;

  // Directional caster is registered first (slot 0 by convention — see runShadowPass).
  return computeShadowAt(worldPos, 0);
}

// ------------------------------------------------------------------
// Standard (non-instanced, non-skinned) vertex shader
// ------------------------------------------------------------------

float4x4 matModel;
float4x4 viewProj;
float4x4 normalMatrix;

struct VS_INPUT {
  float3 Position  : POSITION0;
  float2 TexCoord  : TEXCOORD0;
  float3 Normal    : NORMAL0;
};

struct VS_OUTPUT {
  float4 Position  : SV_POSITION;
  float2 TexCoord  : TEXCOORD0;
  float3 Normal    : TEXCOORD1;
  float3 WorldPos  : TEXCOORD2;
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
// Instanced vertex shader (dual stream; per-instance world matrix
// arrives as 4 Vector4 rows on TEXCOORD1..4, matching VertexInstanceWorld)
// ------------------------------------------------------------------

struct VS_INPUT_INSTANCED {
  // Stream 0 (per-vertex mesh)
  float3 Position  : POSITION0;
  float2 TexCoord  : TEXCOORD0;
  float3 Normal    : NORMAL0;
  // Stream 1 (per-instance) — 4 rows composing a 4x4 world matrix.
  float4 Row0      : TEXCOORD1;
  float4 Row1      : TEXCOORD2;
  float4 Row2      : TEXCOORD3;
  float4 Row3      : TEXCOORD4;
};

VS_OUTPUT VS_Instanced(VS_INPUT_INSTANCED input) {
  VS_OUTPUT output;
  float4x4 world = float4x4(input.Row0, input.Row1, input.Row2, input.Row3);
  float4 wp = mul(float4(input.Position, 1.0), world);
  output.Position = mul(wp, viewProj);
  output.TexCoord = input.TexCoord;
  // Transform normal by the per-instance world matrix directly: instances are
  // uniform-scale, so the rotation block is orthogonal and inverse-transpose == world.
  output.Normal = mul(input.Normal, (float3x3) world);
  output.WorldPos = wp.xyz;
  return output;
}

// ------------------------------------------------------------------
// Skinned vertex shader (forward-declared; F# side does not bind in B9,
// B12 wires bone-matrix upload). 4-bone linear blend skinning.
// ------------------------------------------------------------------

float4x4 boneMatrices[MAX_BONES];

// VS_INPUT_SKINNED mirrors MonoGame's SkinnedEffect.fx VSInputNmTxWeights: bone
// indices arrive as int4 (the pipeline bakes them as Byte4, which binds to int4
// exactly — NOT float4, whose (int) cast would misinterpret the byte encoding).
struct VS_INPUT_SKINNED {
  float3 Position   : POSITION0;
  float2 TexCoord   : TEXCOORD0;
  float3 Normal     : NORMAL0;
  float4 BoneWeights: BLENDWEIGHT0;
  int4   BoneIndices: BLENDINDICES0;
};

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
  float3 skinnedN = mul(input.Normal, (float3x3) skin);

  float4 world = mul(skinnedPos, matModel);
  output.Position = mul(world, viewProj);
  output.TexCoord = input.TexCoord;
  output.Normal = mul(skinnedN, (float3x3) normalMatrix);
  output.WorldPos = world.xyz;
  return output;
}

// ------------------------------------------------------------------
// Fragment shader (shared by all three techniques)
// ------------------------------------------------------------------

static const float PI = 3.14159265359;

float3 getNormal(float3 fragNormal, float2 uv) {
  if (useNormalMap == 0)
    return normalize(fragNormal);

  float3 tangentNormal = tex2D(texture2, uv).xyz * 2.0 - 1.0;
  float3 N = normalize(fragNormal);
  // RH derivation (§6.2): XNA/HLSL cross matches GLSL cross, so the
  // canonical tangent fallback ports without a sign flip.
  float3 up = float3(0.0, 1.0, 0.0);
  float3 c = cross(N, up);
  float3 T = normalize(c);
  if (length(c) < 0.001)
    T = normalize(cross(N, float3(1.0, 0.0, 0.0)));
  float3 B = cross(N, T);
  float3x3 TBN = float3x3(T, B, N);
  return normalize(mul(tangentNormal, TBN));
}

float distributionGGX(float3 N, float3 H, float r) {
  float a = r * r;
  float a2 = a * a;
  float NdotH = max(dot(N, H), 0.0);
  float NdotH2 = NdotH * NdotH;
  float denom = NdotH2 * (a2 - 1.0) + 1.0;
  return a2 / max(PI * denom * denom, 0.0001);
}

float geometrySchlickGGX(float NdotV, float r) {
  float k = ((r + 1.0) * (r + 1.0)) / 8.0;
  return NdotV / max(NdotV * (1.0 - k) + k, 0.0001);
}

float geometrySmith(float3 N, float3 V, float3 L, float r) {
  float NdotV = max(dot(N, V), 0.0);
  float NdotL = max(dot(N, L), 0.0);
  return geometrySchlickGGX(NdotV, r) * geometrySchlickGGX(NdotL, r);
}

float3 fresnelSchlick(float cosTheta, float3 F0) {
  return F0 + (1.0 - F0) * pow(max(1.0 - cosTheta, 0.0), 5.0);
}

float3 calcPBR(float3 V, float3 N, float3 L, float3 radiance, float3 albedo, float r, float m) {
  float3 H = normalize(V + L);
  float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, m);
  float D = distributionGGX(N, H, r);
  float G = geometrySmith(N, V, L, r);
  float3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);

  float3 num = D * G * F;
  float denom = 4.0 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0);
  float3 spec = num / max(denom, 0.0001);

  float3 kS = F;
  float3 kD = (float3(1.0, 1.0, 1.0) - kS) * (1.0 - m);
  float NdotL = max(dot(N, L), 0.0);
  return (kD * albedo / PI + spec) * radiance * NdotL;
}

float4 PS_Main(VS_OUTPUT input) : SV_TARGET {
  float2 uv = input.TexCoord * tiling;
  float4 texColor = tex2D(texture0, uv) * albedoColor;
  float3 albedo = texColor.rgb;
  float3 normal = getNormal(input.Normal, uv);

  float r = clamp(roughness, 0.04, 1.0);
  float m = clamp(metallic, 0.0, 1.0);

  float3 V = normalize(cameraPos - input.WorldPos);

  // Ambient
  float3 ambient = ambientColor * albedo * ambientIntensity;

  // Directional (L points toward the light; dirLightDir points along travel)
  float3 L = normalize(-dirLightDir);
  float3 radiance = dirLightColor * dirLightIntensity;
  float dirShadow = computeDirShadow(input.WorldPos);
  float3 dirResult = calcPBR(V, normal, L, radiance, albedo, r, m) * dirShadow;

  // Point lights ([loop]+break for OGL SM3.0; §6.3)
  float3 pointResult = float3(0.0, 0.0, 0.0);
  [loop]
  for (int i = 0; i < MAX_POINT_LIGHTS; i++) {
    if (i >= pointLightCount) break;
    float3 toLight = pointLightPos[i] - input.WorldPos;
    float dist = length(toLight);
    if (dist < pointLightRadius[i]) {
      float3 pL = normalize(toLight);
      float atten = pow(clamp(1.0 - dist / pointLightRadius[i], 0.0, 1.0), pointLightFalloff[i]);
      float3 pRad = pointLightColor[i] * pointLightIntensity[i] * atten;
      float pShadow = computeShadowAt(input.WorldPos, pointLightShadowIdx[i]);
      pointResult += calcPBR(V, normal, pL, pRad, albedo, r, m) * pShadow;
    }
  }

  // Spot lights
  float3 spotResult = float3(0.0, 0.0, 0.0);
  [loop]
  for (int j = 0; j < MAX_SPOT_LIGHTS; j++) {
    if (j >= spotLightCount) break;
    float3 toLight = spotLightPos[j] - input.WorldPos;
    float dist = length(toLight);
    if (dist < spotLightRadius[j]) {
      float3 sL = normalize(toLight);
      float theta = dot(sL, normalize(-spotLightDir[j]));
      float epsilon = spotLightInnerCutoff[j] - spotLightOuterCutoff[j];
      float intensity = clamp((theta - spotLightOuterCutoff[j]) / max(epsilon, 0.0001), 0.0, 1.0);
      float distAtten = 1.0 - (dist / spotLightRadius[j]);
      float3 sRad = spotLightColor[j] * spotLightIntensity[j] * intensity * distAtten;
      float sShadow = computeShadowAt(input.WorldPos, spotLightShadowIdx[j]);
      spotResult += calcPBR(V, normal, sL, sRad, albedo, r, m) * sShadow;
    }
  }

  float3 emission = emissionColor.rgb * tex2D(texture4, uv).rgb;
  float3 result = ambient + dirResult + pointResult + spotResult + emission;
  float alpha = texColor.a * opacity;
  return float4(result, alpha);
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

technique Instanced {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Instanced();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};

technique Skinned {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Skinned();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};
