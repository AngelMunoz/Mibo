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
#elif defined(SM6)
  #define VS_SHADERMODEL vs_6_0
  #define PS_SHADERMODEL ps_6_0
#else
  #define VS_SHADERMODEL vs_5_0
  #define PS_SHADERMODEL ps_5_0
#endif

#define MAX_POINT_LIGHTS 8
#define MAX_SPOT_LIGHTS 4
#define MAX_BONES 128

// ------------------------------------------------------------------
// Cross-profile texture/sampler declarations.
// mgfxc defines: OPENGL (OGL), HLSL (DX11), HLSL+SM6 (DX12), VULKAN+SM6 (Vulkan).
// OpenGL uses legacy sampler2D + tex2D/tex2Dlod (mojo shader pipeline).
// DX11/DX12/Vulkan use Texture2D + SamplerState + .Sample()/.SampleLevel().
// The texture parameter name stays constant so F# EffectParameter lookups work everywhere.
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
// Shadow atlas (directional + point/spot). Multi-caster.
//
// Profile-gated sampling — see the FETCH_DEPTH block below for the full rationale. Both
// profiles do the same manual comparison (recvZ > d ? shadowed : lit) with a per-caster
// receiver-side bias (shadowBiases) so a flat caster/receiver surface doesn't shadow
// itself across the frustum. Slope-scale bias is applied via RasterizerState on the shadow
// pass. Texel size is passed as a uniform (textureSize has no SM3.0 equivalent). Per-light
// shadow caster index (-1 = none): O(1) lookup.
// ------------------------------------------------------------------

#define MAX_SHADOW_CASTERS 16

// Shadow atlas sampling. The atlas is an R32F COLOR target holding a packed depth value
// in .r (MonoGame cannot make a sampleable depth-only RT), so hardware comparison samplers
// (SamplerComparisonState / SampleCmp) don't apply — they expect a true depth-stencil
// resource. All profiles point-sample the depth value and do the comparison in-shader
// (recvZ > d ? shadowed : lit), matching the raylib backend. Bilinear-filtered depth was
// tried on SM6 and rejected: interpolating depth across a caster silhouette blends two
// unrelated depths into a value no real surface has, which biases the comparison and
// smears/bleeds edges — PCF must filter the comparison, not the depth.
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
float4 shadowUVOffsets[MAX_SHADOW_CASTERS]; // xy = offset, zw = scale (atlas region remap)
float2 shadowTexelSize;                      // 1.0 / atlas resolution (replaces textureSize)
float shadowBiases[MAX_SHADOW_CASTERS];      // per-caster receiver-side bias (prevents self-shadow acne)
int pointLightShadowIdx[MAX_POINT_LIGHTS];   // -1 = no shadow
int spotLightShadowIdx[MAX_SPOT_LIGHTS];     // -1 = no shadow

// Generic shadow lookup for a caster slot. 3x3 PCF, point-sampled on every profile
// (same kernel as the raylib backend): each tap fetches a depth value and the binary
// comparisons are averaged. A per-caster receiver-side bias is subtracted from recvZ
// so a flat caster/receiver surface doesn't shadow itself across the frustum.
float computeShadowAt(float3 worldPos, int casterIdx) {
  if (casterIdx < 0)
    return 1.0;

  float4 sc = mul(float4(worldPos, 1.0), shadowViewProjs[casterIdx]);
  float3 ndc = sc.xyz / sc.w;

  // Outside the shadow frustum → fully lit (no shadow).
  if (ndc.z > 1.0)
    return 1.0;

  // ndc.z stays in clip space [-1,1] on both backends because we write raw clip.z/clip.w
  // into the atlas color target (no viewport transform is applied to color values).
  // Only xy is remapped to [0,1] for atlas UV lookup.
  if (ndc.x < -1.0 || ndc.x > 1.0 || ndc.y < -1.0 || ndc.y > 1.0)
    return 1.0;

  float4 uvOff = shadowUVOffsets[casterIdx];
  // DirectX viewports map clip.y=1 to the top of the render target, while texture v
  // increases downward. Flip y so the atlas lookup matches the viewport transform.
  float2 atlasUV = float2(ndc.x * 0.5 + 0.5, -ndc.y * 0.5 + 0.5) * uvOff.zw + uvOff.xy;

  // Clamp PCF taps to this caster's atlas tile. Tiles are flush (no guard padding), so a
  // tap stepping outside the tile would bleed into a neighbor caster's region and read its
  // depth. The max is pulled back half a texel: with point sampling, a tap clamped to
  // exactly tileMax would land on the first texel of the neighboring tile, so clamp to the
  // last texel center inside this tile instead.
  float2 tileMin = uvOff.xy;
  float2 tileMax = uvOff.xy + uvOff.zw - shadowTexelSize * 0.5;

  // Receiver-side bias: shrink the receiver depth before comparing so a surface doesn't
  // shadow itself.
  float bias = shadowBiases[casterIdx];
  float recvZ = ndc.z - bias;

  // 3×3 PCF kernel (9 taps), identical on every profile and matching the raylib backend.
  // Wider kernels (a 5×5 grid was tried on SM6) cost ~3× the fetches for no quality win
  // at this texel density — the 9-step gradient reads sharper. Taps are clamped to this
  // caster's atlas tile: tiles are flush (no guard padding), so a tap stepping outside
  // the tile would bleed into a neighbor caster's region and read its depth.
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
// Colored instanced vertex shader (dual stream; per-instance world matrix
// on TEXCOORD1..4 plus a per-instance color on TEXCOORD5, matching
// VertexInstanceWorldColor)
// ------------------------------------------------------------------

struct VS_INPUT_INSTANCED_COLOR {
  // Stream 0 (per-vertex mesh)
  float3 Position  : POSITION0;
  float2 TexCoord  : TEXCOORD0;
  float3 Normal    : NORMAL0;
  // Stream 1 (per-instance) — 4 rows composing a 4x4 world matrix + a color.
  float4 Row0      : TEXCOORD1;
  float4 Row1      : TEXCOORD2;
  float4 Row2      : TEXCOORD3;
  float4 Row3      : TEXCOORD4;
  float4 InstanceColor : TEXCOORD5;
};

struct VS_OUTPUT_COLOR {
  float4 Position  : SV_POSITION;
  float2 TexCoord  : TEXCOORD0;
  float3 Normal    : TEXCOORD1;
  float3 WorldPos  : TEXCOORD2;
  float4 InstanceColor : TEXCOORD3;
};

VS_OUTPUT_COLOR VS_InstancedColor(VS_INPUT_INSTANCED_COLOR input) {
  VS_OUTPUT_COLOR output;
  float4x4 world = float4x4(input.Row0, input.Row1, input.Row2, input.Row3);
  float4 wp = mul(float4(input.Position, 1.0), world);
  output.Position = mul(wp, viewProj);
  output.TexCoord = input.TexCoord;
  // Same uniform-scale assumption as VS_Instanced.
  output.Normal = mul(input.Normal, (float3x3) world);
  output.WorldPos = wp.xyz;
  output.InstanceColor = input.InstanceColor;
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
// Skinned + instanced vertex shaders (bone palettes from a texture).
// The palette texture is RGBA32F, width = boneCount*4 texels, height = the chunk's
// instance count; a bone's 4x4 matrix occupies 4 consecutive texels. XNA Matrix is
// row-major, so texel r holds row r and float4x4(r0,r1,r2,r3) rebuilds it for the
// file's mul(position, matrix) vector-LEFT convention. Sampler slot 6 (slots 0-4 are
// the material maps, 5 the shadow atlas). The OpenGL profile (vs_3_0) has no vertex
// texture fetch, so these shaders and their techniques are excluded there — the
// pipeline falls back to per-instance Skinned draws.
// ------------------------------------------------------------------
#if !OPENGL
DECLARE_TEX(paletteTex, 6);
float2 paletteTexSize;

float4 paletteBoneRow(int boneIndex, int row, float instance) {
  float2 uv = float2(
    (float(boneIndex * 4 + row) + 0.5) / paletteTexSize.x,
    (instance + 0.5) / paletteTexSize.y);
  return SAMPLE_TEX_LOD(paletteTex, uv, 0);
}

float4x4 paletteBoneMatrix(int boneIndex, float instance) {
  return float4x4(
    paletteBoneRow(boneIndex, 0, instance),
    paletteBoneRow(boneIndex, 1, instance),
    paletteBoneRow(boneIndex, 2, instance),
    paletteBoneRow(boneIndex, 3, instance));
}

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

VS_OUTPUT VS_SkinnedInstanced(VS_INPUT_SKINNED_INSTANCED input) {
  VS_OUTPUT output;
  float inst = input.PaletteOffset;

  float4x4 skin =
    input.BoneWeights.x * paletteBoneMatrix(input.BoneIndices.x, inst) +
    input.BoneWeights.y * paletteBoneMatrix(input.BoneIndices.y, inst) +
    input.BoneWeights.z * paletteBoneMatrix(input.BoneIndices.z, inst) +
    input.BoneWeights.w * paletteBoneMatrix(input.BoneIndices.w, inst);

  float4 skinnedPos = mul(float4(input.Position, 1.0), skin);
  float3 skinnedN = mul(input.Normal, (float3x3) skin);

  // matModel carries the mesh's parent-bone world (as in VS_Skinned, minus the draw
  // transform); the per-instance world arrives on stream 1. Normals use the raw 3x3
  // (uniform-scale assumption, same as VS_Instanced) — a per-instance
  // inverse-transpose would defeat the point of instancing.
  float4x4 instanceWorld = float4x4(input.Row0, input.Row1, input.Row2, input.Row3);
  float4 world = mul(mul(skinnedPos, matModel), instanceWorld);
  output.Position = mul(world, viewProj);
  output.TexCoord = input.TexCoord;
  output.Normal = mul(mul(skinnedN, (float3x3) matModel), (float3x3) instanceWorld);
  output.WorldPos = world.xyz;
  return output;
}

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

VS_OUTPUT_COLOR VS_SkinnedInstancedColor(VS_INPUT_SKINNED_INSTANCED_COLOR input) {
  VS_OUTPUT_COLOR output;
  float inst = input.PaletteOffset;

  float4x4 skin =
    input.BoneWeights.x * paletteBoneMatrix(input.BoneIndices.x, inst) +
    input.BoneWeights.y * paletteBoneMatrix(input.BoneIndices.y, inst) +
    input.BoneWeights.z * paletteBoneMatrix(input.BoneIndices.z, inst) +
    input.BoneWeights.w * paletteBoneMatrix(input.BoneIndices.w, inst);

  float4 skinnedPos = mul(float4(input.Position, 1.0), skin);
  float3 skinnedN = mul(input.Normal, (float3x3) skin);

  // Same matModel / uniform-scale conventions as VS_SkinnedInstanced.
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
// Grouped-uniform skinned + instanced variant (DX12 fallback). The native
// DX12 runtime never delivers vertex-stage textures to the VS (the palette
// SRV samples zeros there regardless of slot or content — the PS reads the
// same SRV fine), so on DX12 bone palettes ride a constant array instead:
// bonePaletteGroup holds ONE GROUP of instances (groupBoneCount matrices
// each), indexed by the group-local PaletteOffset from stream 1. The pipeline
// chunks draws to 320/boneCount instances per group (see the sizing note at
// the declaration). The vertex layout is the same
// VS_INPUT_SKINNED_INSTANCED(+COLOR) — only the palette source differs.
// ------------------------------------------------------------------
#define MAX_GROUP_PALETTES 320
// Declared as plain globals (NOT an explicit cbuffer block): mgfx packs all
// globals into one shared $Globals CB whose size is stored as a signed Int16
// (32,767 cap). The effect's other uniforms take ~11KB, so 320 matrices
// (20KB) is the group-array budget (total ~32KB). A named cbuffer would
// dodge the cap on DX12 but the Vulkan mgfx profile rejects multi-CB
// effects outright.
float4x4 bonePaletteGroup[MAX_GROUP_PALETTES];
int groupBoneCount;

float4x4 groupBoneMatrix(int boneIndex, float instance) {
  return bonePaletteGroup[(int)instance * groupBoneCount + boneIndex];
}

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

  // Same matModel / instance-world / uniform-scale conventions as
  // VS_SkinnedInstanced.
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
#endif // !OPENGL

// ------------------------------------------------------------------
// Fragment shader (shared by all three techniques)
// ------------------------------------------------------------------

static const float PI = 3.14159265359;

float3 getNormal(float3 fragNormal, float2 uv) {
  if (useNormalMap == 0)
    return normalize(fragNormal);

  float3 tangentNormal = SAMPLE_TEX(texture2, uv).xyz * 2.0 - 1.0;
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

// Shared PBR lighting body for all pixel-shader entry points. instanceColor is the
// per-instance multiplier (float4(1,1,1,1) on the non-colored paths): albedo scales by
// instanceColor.rgb, the final alpha by instanceColor.a.
float4 shadePBR(float2 texCoord, float3 fragNormal, float3 worldPos, float4 instanceColor) {
  float2 uv = texCoord * tiling;
  float4 texColor = SAMPLE_TEX(texture0, uv) * albedoColor;
  float3 albedo = texColor.rgb * instanceColor.rgb;
  float3 normal = getNormal(fragNormal, uv);

  float r = clamp(roughness, 0.04, 1.0);
  float m = clamp(metallic, 0.0, 1.0);

  float3 V = normalize(cameraPos - worldPos);

  // Ambient
  float3 ambient = ambientColor * albedo * ambientIntensity;

  // Directional (L points toward the light; dirLightDir points along travel)
  float3 L = normalize(-dirLightDir);
  float3 radiance = dirLightColor * dirLightIntensity;
  float dirShadow = computeDirShadow(worldPos);
  float3 dirResult = calcPBR(V, normal, L, radiance, albedo, r, m) * dirShadow;

  // Point lights ([loop]+break for OGL SM3.0; §6.3)
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

  // Spot lights
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

  float3 emission = emissionColor.rgb * SAMPLE_TEX(texture4, uv).rgb;
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

technique InstancedColor {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_InstancedColor();
    PixelShader = compile PS_SHADERMODEL PS_MainColor();
  }
};

technique Skinned {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Skinned();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};

// Skinned + instanced relies on vertex texture fetch — unavailable on the OpenGL
// (vs_3_0) profile, so the GL .mgfx must not contain these techniques.
#if !OPENGL
technique SkinnedInstanced {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_SkinnedInstanced();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
};

technique SkinnedInstancedColor {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_SkinnedInstancedColor();
    PixelShader = compile PS_SHADERMODEL PS_MainColor();
  }
};

// Grouped-uniform variants — the DX12 backend's skinned + instanced path (no
// working vertex texture fetch there); unused on DX11/Vulkan.
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
#endif // !OPENGL
