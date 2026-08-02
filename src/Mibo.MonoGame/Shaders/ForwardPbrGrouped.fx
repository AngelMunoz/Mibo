// Forward PBR pipeline (Cook-Torrance) — GROUPED-UNIFORM ISOLATE for DX12.
//
// This is the full ForwardPbr.fx shader with ONLY the grouped-uniform skinned +
// instanced techniques. It exists as a separate .fx file because the MonoGame DX12
// mgfx reflection parser (ShaderProfile.DirectX12.cs) drops bonePaletteGroup,
// groupBoneCount, and paletteTexSize from the compiled effect when all 8 techniques
// are present in one file. Isolating the grouped techniques into this file makes
// the params survive reflection on DX12.
//
// On DX11/Vulkan/OpenGL the grouped techniques in ForwardPbr.fx are used directly;
// this file is loaded ONLY on DX12.
//
// §6 compliance: same as ForwardPbr.fx (plain float4x4, mul(position, matrix)
// vector-LEFT, world->clip = mul(mul(position, matModel), viewProj)).
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

#define MAX_POINT_LIGHTS 8
#define MAX_SPOT_LIGHTS 4

// ------------------------------------------------------------------
// Cross-profile texture/sampler declarations.
// ------------------------------------------------------------------
#if OPENGL
  #define DECLARE_TEX(name, slot) sampler2D name : register(s##slot)
  #define SAMPLE_TEX(name, uv) tex2D(name, uv)
  #define SAMPLE_TEX_LOD(name, uv, lod) tex2Dlod(name, float4(uv, 0.0, lod))
#else
  #define DECLARE_TEX(name, slot) Texture2D name : register(t##slot); SamplerState name##Sampler : register(s##slot)
  #define SAMPLE_TEX(name, uv) name.Sample(name##Sampler, uv)
  #define SAMPLE_TEX_LOD(name, uv, lod) name.SampleLevel(name##Sampler, uv, lod)
#endif

DECLARE_TEX(texture0, 0); // albedo
DECLARE_TEX(texture1, 1); // roughness
DECLARE_TEX(texture2, 2); // normal
DECLARE_TEX(texture3, 3); // metallic
DECLARE_TEX(texture4, 4); // emission

float4 albedoColor;
float roughness;
float metallic;
float4 emissionColor;
float opacity;
float2 tiling;
int useNormalMap;
int useEmissionMap;

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
// Shadow atlas (same as ForwardPbr.fx)
// ------------------------------------------------------------------

#define MAX_SHADOW_CASTERS 16

#if defined(SM6)
Texture2D shadowAtlas : register(t5);
SamplerState shadowSampler : register(s5);
#define FETCH_DEPTH(uv) shadowAtlas.SampleLevel(shadowSampler, uv, 0.0).r
#elif OPENGL
sampler2D shadowAtlas : register(s5) = sampler_state {
  AddressU = Clamp;
  AddressV = Clamp;
  MinFilter = Point;
  MagFilter = Point;
  MipFilter = Point;
};
#define FETCH_DEPTH(uv) tex2Dlod(shadowAtlas, float4(uv, 0.0, 0.0)).r
#else
DECLARE_TEX(shadowAtlas, 5);
#define FETCH_DEPTH(uv) shadowAtlas.SampleLevel(shadowAtlasSampler, uv, 0.0).r
#endif
float4x4 shadowViewProjs[MAX_SHADOW_CASTERS];
float4 shadowUVOffsets[MAX_SHADOW_CASTERS];
float2 shadowTexelSize;
float shadowBiases[MAX_SHADOW_CASTERS];
int pointLightShadowIdx[MAX_POINT_LIGHTS];
int spotLightShadowIdx[MAX_SPOT_LIGHTS];

float computeShadowAt(float3 worldPos, int casterIdx) {
  if (casterIdx < 0)
    return 1.0;

  float4 sc = mul(float4(worldPos, 1.0), shadowViewProjs[casterIdx]);
  float3 ndc = sc.xyz / sc.w;

  if (ndc.z > 1.0)
    return 1.0;

  if (ndc.x < -1.0 || ndc.x > 1.0 || ndc.y < -1.0 || ndc.y > 1.0)
    return 1.0;

  float4 uvOff = shadowUVOffsets[casterIdx];
  float2 atlasUV = float2(ndc.x * 0.5 + 0.5, -ndc.y * 0.5 + 0.5) * uvOff.zw + uvOff.xy;

  float2 tileMin = uvOff.xy;
  float2 tileMax = uvOff.xy + uvOff.zw - shadowTexelSize * 0.5;

  float bias = shadowBiases[casterIdx];
  float recvZ = ndc.z - bias;

  float shadow = 0.0;
  [unroll]
  for (int x = -1; x <= 1; x++) {
    [unroll]
    for (int y = -1; y <= 1; y++) {
      float2 sampleUV =
        clamp(atlasUV + float2(float(x), float(y)) * shadowTexelSize, tileMin, tileMax);
      float d = FETCH_DEPTH(sampleUV);
      shadow += (recvZ > d) ? 0.0 : 1.0;
    }
  }
  return shadow / 9.0;
}

float computeDirShadow(float3 worldPos) {
  if (dirLightCastsShadows == 0)
    return 1.0;

  return computeShadowAt(worldPos, 0);
}

// ------------------------------------------------------------------
// Grouped-uniform bone palette (the reason this file exists)
// ------------------------------------------------------------------

float4x4 matModel;
float4x4 viewProj;
float4x4 normalMatrix;

#define MAX_GROUP_PALETTES 448
float4x4 bonePaletteGroup[MAX_GROUP_PALETTES];
int groupBoneCount;

float4x4 groupBoneMatrix(int boneIndex, float instance) {
  return bonePaletteGroup[(int)instance * groupBoneCount + boneIndex];
}

// ------------------------------------------------------------------
// Vertex shaders — grouped-uniform skinned + instanced
// ------------------------------------------------------------------

struct VS_OUTPUT {
  float4 Position  : SV_POSITION;
  float2 TexCoord  : TEXCOORD0;
  float3 Normal    : TEXCOORD1;
  float3 WorldPos  : TEXCOORD2;
};

struct VS_OUTPUT_COLOR {
  float4 Position  : SV_POSITION;
  float2 TexCoord  : TEXCOORD0;
  float3 Normal    : TEXCOORD1;
  float3 WorldPos  : TEXCOORD2;
  float4 InstanceColor : TEXCOORD3;
};

struct VS_INPUT_SKINNED_INSTANCED {
  // Stream 0 (per-vertex skinned mesh)
  float3 Position   : POSITION0;
  float2 TexCoord   : TEXCOORD0;
  float3 Normal     : NORMAL0;
  float4 BoneWeights: BLENDWEIGHT0;
  int4   BoneIndices: BLENDINDICES0;
  // Stream 1 (per-instance) — 4 rows composing a 4x4 world matrix + the palette row.
  float4 Row0         : TEXCOORD1;
  float4 Row1         : TEXCOORD2;
  float4 Row2         : TEXCOORD3;
  float4 Row3         : TEXCOORD4;
  float PaletteOffset : TEXCOORD6;
};

struct VS_INPUT_SKINNED_INSTANCED_COLOR {
  // Stream 0 (per-vertex skinned mesh)
  float3 Position   : POSITION0;
  float2 TexCoord   : TEXCOORD0;
  float3 Normal     : NORMAL0;
  float4 BoneWeights: BLENDWEIGHT0;
  int4   BoneIndices: BLENDINDICES0;
  // Stream 1 (per-instance) — 4 world rows + a color + the palette row.
  float4 Row0         : TEXCOORD1;
  float4 Row1         : TEXCOORD2;
  float4 Row2         : TEXCOORD3;
  float4 Row3         : TEXCOORD4;
  float4 InstanceColor : TEXCOORD5;
  float PaletteOffset : TEXCOORD6;
};

VS_OUTPUT VS_SkinnedInstancedGrouped(VS_INPUT_SKINNED_INSTANCED input) {
  VS_OUTPUT output;
  float inst = input.PaletteOffset;

  float4x4 skin =
    input.BoneWeights.x * groupBoneMatrix(input.BoneIndices.x, inst) +
    input.BoneWeights.y * groupBoneMatrix(input.BoneIndices.y, inst) +
    input.BoneWeights.z * groupBoneMatrix(input.BoneIndices.z, inst) +
    input.BoneWeights.w * groupBoneMatrix(input.BoneIndices.w, inst);

  float4 skinnedPos = mul(float4(input.Position, 1.0), skin);
  float3 skinnedN = mul(input.Normal, (float3x3) skin);

  float4x4 instanceWorld = float4x4(input.Row0, input.Row1, input.Row2, input.Row3);
  float4 world = mul(mul(skinnedPos, matModel), instanceWorld);
  output.Position = mul(world, viewProj);
  output.TexCoord = input.TexCoord;
  output.Normal = mul(mul(skinnedN, (float3x3) matModel), (float3x3) instanceWorld);
  output.WorldPos = world.xyz;
  return output;
}

VS_OUTPUT_COLOR VS_SkinnedInstancedGroupedColor(VS_INPUT_SKINNED_INSTANCED_COLOR input) {
  VS_OUTPUT_COLOR output;
  float inst = input.PaletteOffset;

  float4x4 skin =
    input.BoneWeights.x * groupBoneMatrix(input.BoneIndices.x, inst) +
    input.BoneWeights.y * groupBoneMatrix(input.BoneIndices.y, inst) +
    input.BoneWeights.z * groupBoneMatrix(input.BoneIndices.z, inst) +
    input.BoneWeights.w * groupBoneMatrix(input.BoneIndices.w, inst);

  float4 skinnedPos = mul(float4(input.Position, 1.0), skin);
  float3 skinnedN = mul(input.Normal, (float3x3) skin);

  float4x4 instanceWorld = float4x4(input.Row0, input.Row1, input.Row2, input.Row3);
  float4 world = mul(mul(skinnedPos, matModel), instanceWorld);
  output.Position = mul(world, viewProj);
  output.TexCoord = input.TexCoord;
  output.Normal = mul(mul(skinnedN, (float3x3) matModel), (float3x3) instanceWorld);
  output.WorldPos = world.xyz;
  output.InstanceColor = input.InstanceColor;
  return output;
}

// ------------------------------------------------------------------
// Fragment shader (full PBR — identical to ForwardPbr.fx shadePBR)
// ------------------------------------------------------------------

static const float PI = 3.14159265359;

float3 getNormal(float3 fragNormal, float2 uv) {
  if (useNormalMap == 0)
    return normalize(fragNormal);

  float3 tangentNormal = SAMPLE_TEX(texture2, uv).xyz * 2.0 - 1.0;
  float3 N = normalize(fragNormal);
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

float4 shadePBR(float2 texCoord, float3 fragNormal, float3 worldPos, float4 instanceColor) {
  float2 uv = texCoord * tiling;
  float4 texColor = SAMPLE_TEX(texture0, uv) * albedoColor;
  float3 albedo = texColor.rgb * instanceColor.rgb;
  float3 normal = getNormal(fragNormal, uv);

  float r = clamp(roughness, 0.04, 1.0);
  float m = clamp(metallic, 0.0, 1.0);

  float3 V = normalize(cameraPos - worldPos);

  float3 ambient = ambientColor * albedo * ambientIntensity;

  float3 L = normalize(-dirLightDir);
  float3 radiance = dirLightColor * dirLightIntensity;
  // Fragments facing away from the sun get zero directional contribution from
  // calcPBR anyway — skip the shadow matrix multiply and the 9-tap PCF for them.
  float dirShadow = 0.0;
  if (dot(normal, L) > 0.0)
    dirShadow = computeDirShadow(worldPos);
  float3 dirResult = calcPBR(V, normal, L, radiance, albedo, r, m) * dirShadow;

  float3 pointResult = float3(0.0, 0.0, 0.0);
  [loop]
  for (int i = 0; i < MAX_POINT_LIGHTS; i++) {
    if (i >= pointLightCount) break;
    float3 toLight = pointLightPos[i] - worldPos;
    float dist = length(toLight);
    if (dist < pointLightRadius[i]) {
      float3 pL = normalize(toLight);
      float atten = pow(clamp(1.0 - dist / pointLightRadius[i], 0.0, 1.0), pointLightFalloff[i]);
      float3 pRad = pointLightColor[i] * pointLightIntensity[i] * atten;
      float pShadow = computeShadowAt(worldPos, pointLightShadowIdx[i]);
      pointResult += calcPBR(V, normal, pL, pRad, albedo, r, m) * pShadow;
    }
  }

  float3 spotResult = float3(0.0, 0.0, 0.0);
  [loop]
  for (int j = 0; j < MAX_SPOT_LIGHTS; j++) {
    if (j >= spotLightCount) break;
    float3 toLight = spotLightPos[j] - worldPos;
    float dist = length(toLight);
    if (dist < spotLightRadius[j]) {
      float3 sL = normalize(toLight);
      float theta = dot(sL, normalize(-spotLightDir[j]));
      float epsilon = spotLightInnerCutoff[j] - spotLightOuterCutoff[j];
      float intensity = clamp((theta - spotLightOuterCutoff[j]) / max(epsilon, 0.0001), 0.0, 1.0);
      float distAtten = 1.0 - (dist / spotLightRadius[j]);
      float3 sRad = spotLightColor[j] * spotLightIntensity[j] * intensity * distAtten;
      float sShadow = computeShadowAt(worldPos, spotLightShadowIdx[j]);
      spotResult += calcPBR(V, normal, sL, sRad, albedo, r, m) * sShadow;
    }
  }

  // Emission map modulation — sampled only when the material binds one
  // (black-emission materials, the common case, skip the tap entirely).
  float3 emission = emissionColor.rgb;
  if (useEmissionMap == 1)
    emission *= SAMPLE_TEX(texture4, uv).rgb;
  float3 result = ambient + dirResult + pointResult + spotResult + emission;
  float alpha = texColor.a * opacity * instanceColor.a;
  return float4(result, alpha);
}

float4 PS_Main(VS_OUTPUT input) : SV_TARGET {
  return shadePBR(input.TexCoord, input.Normal, input.WorldPos, float4(1.0, 1.0, 1.0, 1.0));
}

float4 PS_MainColor(VS_OUTPUT_COLOR input) : SV_TARGET {
  return shadePBR(input.TexCoord, input.Normal, input.WorldPos, input.InstanceColor);
}

// ------------------------------------------------------------------
// Techniques — only the grouped-uniform skinned + instanced variants.
// ------------------------------------------------------------------

technique SkinnedInstancedGrouped {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_SkinnedInstancedGrouped();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};

technique SkinnedInstancedGroupedColor {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_SkinnedInstancedGroupedColor();
    PixelShader = compile PS_SHADERMODEL PS_MainColor();
  }
};
