namespace Mibo.Elmish.Graphics3D.Pipelines

open Raylib_cs

/// <summary>
/// Built-in GLSL shader generators for the Forward PBR pipeline.
/// </summary>
/// <remarks>
/// Fragment shaders use uniform arrays for point lights. The array size is
/// determined at pipeline creation time (default 8) because GLSL requires
/// compile-time constants for array declarations.
/// </remarks>
module Shaders =

  let forwardVertex =
    """#version 330

in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec3 vertexNormal;
in vec4 vertexColor;

out vec2 fragTexCoord;
out vec4 fragColor;
out vec3 fragNormal;
out vec3 fragWorldPos;

uniform mat4 mvp;
uniform mat4 matModel;
uniform mat4 normalMatrix;

void main()
{
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    fragNormal = mat3(normalMatrix) * vertexNormal;
    fragWorldPos = (matModel * vec4(vertexPosition, 1.0)).xyz;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
"""

  /// <summary>
  /// Instanced variant of the forward vertex shader.
  /// Uses <c>in mat4 instanceTransform</c> (vertex attribute) instead of
  /// <c>uniform mat4 matModel</c>. The <c>mvp</c> uniform must be
  /// view-projection only (without model) — the per-instance model
  /// transform comes from the <c>instanceTransform</c> attribute.
  /// </summary>
  let forwardVertexInstanced =
    """#version 330

in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec3 vertexNormal;
in vec4 vertexColor;

in mat4 instanceTransform;

out vec2 fragTexCoord;
out vec4 fragColor;
out vec3 fragNormal;
out vec3 fragWorldPos;

uniform mat4 mvp;
uniform mat4 normalMatrix;

void main()
{
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    fragNormal = mat3(normalMatrix) * vertexNormal;
    fragWorldPos = (instanceTransform * vec4(vertexPosition, 1.0)).xyz;
    gl_Position = mvp * instanceTransform * vec4(vertexPosition, 1.0);
}
"""

  let depthShadowVertex =
    """#version 330

in vec3 vertexPosition;
in vec3 vertexNormal;
in vec2 vertexTexCoord;
in vec4 vertexColor;

uniform mat4 mvp;
uniform mat4 matModel;
uniform mat4 normalMatrix;

out vec3 fragPosition;
out vec2 fragTexCoord;
out vec4 fragColor;
out vec3 fragNormal;

void main()
{
    fragPosition = vec3(matModel * vec4(vertexPosition, 1.0));
    fragTexCoord = vertexTexCoord;
    fragColor    = vertexColor;
    fragNormal   = normalize(mat3(normalMatrix) * vertexNormal);
    gl_Position  = mvp * vec4(vertexPosition, 1.0);
}
"""

  let depthShadowFragment =
    """#version 330

out vec4 finalColor;

void main()
{
    finalColor = vec4(1.0);
}
"""

  let forwardVertexSkinned =
    """#version 330

in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec3 vertexNormal;
in vec4 vertexColor;
in vec4 vertexBoneIndices;
in vec4 vertexBoneWeights;

out vec2 fragTexCoord;
out vec4 fragColor;
out vec3 fragNormal;
out vec3 fragWorldPos;

uniform mat4 mvp;
uniform mat4 matModel;
uniform mat4 normalMatrix;
uniform mat4 boneMatrices[128];

void main()
{
    int ids0 = int(vertexBoneIndices.x);
    int ids1 = int(vertexBoneIndices.y);
    int ids2 = int(vertexBoneIndices.z);
    int ids3 = int(vertexBoneIndices.w);

    mat4 skinMatrix =
        vertexBoneWeights.x * boneMatrices[ids0] +
        vertexBoneWeights.y * boneMatrices[ids1] +
        vertexBoneWeights.z * boneMatrices[ids2] +
        vertexBoneWeights.w * boneMatrices[ids3];

    vec4 skinnedPos = skinMatrix * vec4(vertexPosition, 1.0);
    vec3 skinnedNormal = mat3(skinMatrix) * vertexNormal;

    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    fragNormal = mat3(normalMatrix) * skinnedNormal;
    fragWorldPos = (matModel * skinnedPos).xyz;
    gl_Position = mvp * skinnedPos;
}
"""

  let depthShadowVertexSkinned =
    """#version 330

in vec3 vertexPosition;
in vec3 vertexNormal;
in vec2 vertexTexCoord;
in vec4 vertexColor;
in vec4 vertexBoneIndices;
in vec4 vertexBoneWeights;

uniform mat4 mvp;
uniform mat4 matModel;
uniform mat4 normalMatrix;
uniform mat4 boneMatrices[128];

out vec3 fragPosition;
out vec2 fragTexCoord;
out vec4 fragColor;
out vec3 fragNormal;

void main()
{
    int ids0 = int(vertexBoneIndices.x);
    int ids1 = int(vertexBoneIndices.y);
    int ids2 = int(vertexBoneIndices.z);
    int ids3 = int(vertexBoneIndices.w);

    mat4 skinMatrix =
        vertexBoneWeights.x * boneMatrices[ids0] +
        vertexBoneWeights.y * boneMatrices[ids1] +
        vertexBoneWeights.z * boneMatrices[ids2] +
        vertexBoneWeights.w * boneMatrices[ids3];

    vec4 skinnedPos = skinMatrix * vec4(vertexPosition, 1.0);
    vec3 skinnedNormal = mat3(skinMatrix) * vertexNormal;

    fragPosition = vec3(matModel * skinnedPos);
    fragTexCoord = vertexTexCoord;
    fragColor    = vertexColor;
    fragNormal   = normalize(mat3(normalMatrix) * skinnedNormal);
    gl_Position  = mvp * skinnedPos;
}
"""

  let postProcessVertex =
    """#version 330

in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec4 vertexColor;

out vec2 fragTexCoord;

uniform mat4 mvp;

void main()
{
    fragTexCoord = vertexTexCoord;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
"""

  let postProcessFragment =
    """#version 330

in vec2 fragTexCoord;
out vec4 finalColor;

uniform sampler2D texture0;

void main()
{
    finalColor = texture(texture0, fragTexCoord);
}
"""

  let forwardFragmentFmt
    (maxPointLights: int)
    (maxSpotLights: int)
    (maxShadowCasters: int)
    =
    $"""#version 330

const float PI = 3.14159265359;
const int MAX_SHADOW_CASTERS = {maxShadowCasters};

in vec2 fragTexCoord;
in vec4 fragColor;
in vec3 fragNormal;
in vec3 fragWorldPos;

out vec4 finalColor;

uniform sampler2D texture0; // albedo
uniform sampler2D texture1; // metalness
uniform sampler2D texture2; // normal map
uniform sampler2D texture3; // roughness
uniform sampler2D texture4; // emission

uniform vec4 albedoColor;
uniform float roughness;
uniform float metallic;
uniform vec4 emissionColor;
uniform float opacity;
uniform vec2 tiling;
uniform int useNormalMap;

uniform vec3 ambientColor;
uniform float ambientIntensity;

uniform vec3 dirLightDir;
uniform vec3 dirLightColor;
uniform float dirLightIntensity;
uniform int dirLightCastsShadows;

uniform int pointLightCount;
uniform vec3 pointLightPos[{maxPointLights}];
uniform vec3 pointLightColor[{maxPointLights}];
uniform float pointLightIntensity[{maxPointLights}];
uniform float pointLightRadius[{maxPointLights}];
uniform float pointLightFalloff[{maxPointLights}];

uniform int spotLightCount;
uniform vec3 spotLightPos[{maxSpotLights}];
uniform vec3 spotLightDir[{maxSpotLights}];
uniform vec3 spotLightColor[{maxSpotLights}];
uniform float spotLightIntensity[{maxSpotLights}];
uniform float spotLightRadius[{maxSpotLights}];
uniform float spotLightInnerCutoff[{maxSpotLights}];
uniform float spotLightOuterCutoff[{maxSpotLights}];

uniform vec3 cameraPos;
uniform sampler2D shadowAtlas;
uniform int shadowCasterCount;
uniform mat4 shadowViewProjs[MAX_SHADOW_CASTERS];
uniform vec4 shadowUVOffsets[MAX_SHADOW_CASTERS]; // xy=offset, zw=scale
uniform vec3 shadowLightPositions[MAX_SHADOW_CASTERS];
uniform float shadowBiases[MAX_SHADOW_CASTERS];
uniform int shadowTypes[MAX_SHADOW_CASTERS]; // 0=directional, 1=point, 2=spot
uniform int shadowPass;

vec3 getNormal()
{{
    if (useNormalMap == 0)
        return normalize(fragNormal);

    vec3 tangentNormal = texture(texture2, fragTexCoord * tiling).xyz * 2.0 - 1.0;
    vec3 N = normalize(fragNormal);
    vec3 T = normalize(cross(N, vec3(0.0, 1.0, 0.0)));
    if (length(cross(N, vec3(0.0, 1.0, 0.0))) < 0.001)
        T = normalize(cross(N, vec3(1.0, 0.0, 0.0)));
    vec3 B = cross(N, T);
    mat3 TBN = mat3(T, B, N);
    return normalize(TBN * tangentNormal);
}}

float distributionGGX(vec3 N, vec3 H, float r)
{{
    float a = r * r;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    float denom = NdotH2 * (a2 - 1.0) + 1.0;
    return a2 / max(PI * denom * denom, 0.0001);
}}

float geometrySchlickGGX(float NdotV, float r)
{{
    float k = ((r + 1.0) * (r + 1.0)) / 8.0;
    return NdotV / max(NdotV * (1.0 - k) + k, 0.0001);
}}

float geometrySmith(vec3 N, vec3 V, vec3 L, float r)
{{
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    return geometrySchlickGGX(NdotV, r) * geometrySchlickGGX(NdotL, r);
}}

vec3 fresnelSchlick(float cosTheta, vec3 F0)
{{
    return F0 + (1.0 - F0) * pow(max(1.0 - cosTheta, 0.0), 5.0);
}}

float computeShadowFromAtlas(vec3 worldPos, int casterIndex)
{{
    if (casterIndex < 0 || casterIndex >= shadowCasterCount)
        return 1.0;

    vec4 shadowCoord = shadowViewProjs[casterIndex] * vec4(worldPos, 1.0);
    vec3 projCoord = shadowCoord.xyz / shadowCoord.w;
    projCoord = projCoord * 0.5 + 0.5;

    // Outside shadow frustum → fully lit (no shadow)
    if (projCoord.z > 1.0) return 1.0;
    if (projCoord.x < 0.0 || projCoord.x > 1.0 || projCoord.y < 0.0 || projCoord.y > 1.0)
        return 1.0;

    // Apply UV offset/scale for atlas region
    vec2 atlasUV = projCoord.xy * shadowUVOffsets[casterIndex].zw + shadowUVOffsets[casterIndex].xy;

    // Slope-scale bias: increases bias at steep angles to prevent shadow acne
    float baseBias = shadowBiases[casterIndex];
    float dzdx = dFdx(projCoord.z);
    float dzdy = dFdy(projCoord.z);
    float bias = baseBias + length(vec2(dzdx, dzdy)) * 3.0;

    float shadow = 0.0;
    vec2 texel = 1.0 / vec2(textureSize(shadowAtlas, 0));
    for (int x = -1; x <= 1; x++) {{
        for (int y = -1; y <= 1; y++) {{
            float d = texture(shadowAtlas, atlasUV + vec2(float(x), float(y)) * texel).r;
            shadow += (projCoord.z - bias > d) ? 0.0 : 1.0;
        }}
    }}
    return shadow / 9.0;
}}

float computePointShadow(vec3 worldPos, int casterIndex)
{{
    if (casterIndex < 0 || casterIndex >= shadowCasterCount)
        return 1.0;

    // Single forward-facing shadow map per point light
    // Uses standard projective shadow mapping (same as spot lights)
    vec4 shadowCoord = shadowViewProjs[casterIndex] * vec4(worldPos, 1.0);
    vec3 projCoord = shadowCoord.xyz / shadowCoord.w;
    projCoord = projCoord * 0.5 + 0.5;

    // Outside shadow frustum → fully lit (no shadow)
    if (projCoord.z > 1.0) return 1.0;
    if (projCoord.x < 0.0 || projCoord.x > 1.0 || projCoord.y < 0.0 || projCoord.y > 1.0)
        return 1.0;

    vec2 atlasUV = projCoord.xy * shadowUVOffsets[casterIndex].zw + shadowUVOffsets[casterIndex].xy;

    // Slope-scale bias
    float baseBias = shadowBiases[casterIndex];
    float dzdx = dFdx(projCoord.z);
    float dzdy = dFdy(projCoord.z);
    float bias = baseBias + length(vec2(dzdx, dzdy)) * 3.0;

    float shadow = 0.0;
    vec2 texel = 1.0 / vec2(textureSize(shadowAtlas, 0));
    for (int x = -1; x <= 1; x++) {{
        for (int y = -1; y <= 1; y++) {{
            float d = texture(shadowAtlas, atlasUV + vec2(float(x), float(y)) * texel).r;
            shadow += (projCoord.z - bias > d) ? 0.0 : 1.0;
        }}
    }}
    return shadow / 9.0;
}}

float computeSpotShadow(vec3 worldPos, int casterIndex)
{{
    if (casterIndex < 0 || casterIndex >= shadowCasterCount)
        return 1.0;

    vec4 shadowCoord = shadowViewProjs[casterIndex] * vec4(worldPos, 1.0);
    vec3 projCoord = shadowCoord.xyz / shadowCoord.w;
    projCoord = projCoord * 0.5 + 0.5;

    // Outside shadow frustum → fully lit (no shadow)
    if (projCoord.z > 1.0) return 1.0;
    if (projCoord.x < 0.0 || projCoord.x > 1.0 || projCoord.y < 0.0 || projCoord.y > 1.0)
        return 1.0;

    vec2 atlasUV = projCoord.xy * shadowUVOffsets[casterIndex].zw + shadowUVOffsets[casterIndex].xy;

    // Slope-scale bias
    float baseBias = shadowBiases[casterIndex];
    float dzdx = dFdx(projCoord.z);
    float dzdy = dFdy(projCoord.z);
    float bias = baseBias + length(vec2(dzdx, dzdy)) * 3.0;

    float shadow = 0.0;
    vec2 texel = 1.0 / vec2(textureSize(shadowAtlas, 0));
    for (int x = -1; x <= 1; x++) {{
        for (int y = -1; y <= 1; y++) {{
            float d = texture(shadowAtlas, atlasUV + vec2(float(x), float(y)) * texel).r;
            shadow += (projCoord.z - bias > d) ? 0.0 : 1.0;
        }}
    }}
    return shadow / 9.0;
}}

float computeDirShadow(vec3 worldPos)
{{
    if (dirLightCastsShadows == 0)
        return 1.0;

    // Find the first directional light caster
    for (int i = 0; i < shadowCasterCount; i++) {{
        if (shadowTypes[i] == 0) {{ // Directional
            return computeShadowFromAtlas(worldPos, i);
        }}
    }}
    return 1.0;
}}

vec3 calcPBR(vec3 V, vec3 N, vec3 L, vec3 radiance, vec3 albedo, float r, float m)
{{
    vec3 H = normalize(V + L);

    vec3 F0 = mix(vec3(0.04), albedo, m);
    float D = distributionGGX(N, H, r);
    float G = geometrySmith(N, V, L, r);
    vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);

    vec3 num = D * G * F;
    float denom = 4.0 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0);
    vec3 spec = num / max(denom, 0.0001);

    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - m);
    float NdotL = max(dot(N, L), 0.0);

    return (kD * albedo / PI + spec) * radiance * NdotL;
}}

void main()
{{
    if (shadowPass == 1) {{ finalColor = vec4(1.0); return; }}

    vec2 uv = fragTexCoord * tiling;
    vec4 texColor = texture(texture0, uv) * albedoColor * fragColor;
    vec3 albedo = texColor.rgb;
    vec3 normal = getNormal();

    float r = clamp(roughness, 0.04, 1.0);
    float m = clamp(metallic, 0.0, 1.0);

    vec3 V = normalize(cameraPos - fragWorldPos);

    // Ambient
    vec3 ambient = ambientColor * albedo * ambientIntensity;

    // Directional light (PBR)
    float dirShadow = computeDirShadow(fragWorldPos);
    vec3 L = normalize(-dirLightDir);
    vec3 radiance = dirLightColor * dirLightIntensity;
    vec3 dirResult = calcPBR(V, normal, L, radiance, albedo, r, m) * dirShadow;

    // Point lights (PBR)
    vec3 pointResult = vec3(0.0);
    int count = min(pointLightCount, {maxPointLights});
    for (int i = 0; i < count; i++)
    {{
        vec3 toLight = pointLightPos[i] - fragWorldPos;
        float dist = length(toLight);
        if (dist < pointLightRadius[i])
        {{
            vec3 pL = normalize(toLight);
            float atten = pow(clamp(1.0 - dist / pointLightRadius[i], 0.0, 1.0), pointLightFalloff[i]);
            vec3 pRadiance = pointLightColor[i] * pointLightIntensity[i] * atten;
            
            // Find matching shadow caster for this point light
            float ptShadow = 1.0;
            for (int si = 0; si < shadowCasterCount; si++) {{
                if (shadowTypes[si] == 1 && shadowLightPositions[si] == pointLightPos[i]) {{
                    ptShadow = computePointShadow(fragWorldPos, si);
                    break;
                }}
            }}
            
            pointResult += calcPBR(V, normal, pL, pRadiance, albedo, r, m) * ptShadow;
        }}
    }}

    // Spot lights (PBR)
    vec3 spotResult = vec3(0.0);
    int sCount = min(spotLightCount, {maxSpotLights});
    for (int i = 0; i < sCount; i++)
    {{
        vec3 toLight = spotLightPos[i] - fragWorldPos;
        float dist = length(toLight);
        if (dist < spotLightRadius[i])
        {{
            vec3 sL = normalize(toLight);
            float theta = dot(sL, normalize(-spotLightDir[i]));
            float epsilon = spotLightInnerCutoff[i] - spotLightOuterCutoff[i];
            float intensity = clamp((theta - spotLightOuterCutoff[i]) / max(epsilon, 0.0001), 0.0, 1.0);
            float distAtten = 1.0 - (dist / spotLightRadius[i]);
            vec3 sRadiance = spotLightColor[i] * spotLightIntensity[i] * intensity * distAtten;
            
            // Find matching shadow caster for this spot light
            float spShadow = 1.0;
            for (int si = 0; si < shadowCasterCount; si++) {{
                if (shadowTypes[si] == 2 && shadowLightPositions[si] == spotLightPos[i]) {{
                    spShadow = computeSpotShadow(fragWorldPos, si);
                    break;
                }}
            }}
            
            spotResult += calcPBR(V, normal, sL, sRadiance, albedo, r, m) * spShadow;
        }}
    }}

    vec3 emission = emissionColor.rgb;
    // Emission map modulation
    vec4 emTex = texture(texture4, uv);
    emission *= emTex.rgb;

    vec3 result = ambient + dirResult + pointResult + spotResult + emission;
    float alpha = texColor.a * opacity;
    finalColor = vec4(result, alpha);
}}
"""

  /// <summary>
  /// Loads the built-in forward PBR vertex + fragment shader.
  /// The fragment shader is generated with the specified light and shadow array sizes.
  /// </summary>
  let loadForwardShader
    (maxPointLights: int)
    (maxSpotLights: int)
    (maxShadowCasters: int)
    : Shader =
    Raylib.LoadShaderFromMemory(
      forwardVertex,
      forwardFragmentFmt maxPointLights maxSpotLights maxShadowCasters
    )

  /// <summary>
  /// Loads the instanced forward PBR vertex + fragment shader.
  /// Uses <c>in mat4 instanceTransform</c> for per-instance model transforms.
  /// The <c>mvp</c> uniform must be view-projection only (without model).
  /// </summary>
  let loadForwardInstancedShader
    (maxPointLights: int)
    (maxSpotLights: int)
    (maxShadowCasters: int)
    : Shader =
    Raylib.LoadShaderFromMemory(
      forwardVertexInstanced,
      forwardFragmentFmt maxPointLights maxSpotLights maxShadowCasters
    )

  /// <summary>Loads the depth-only shadow pass vertex + fragment shader (C example compatible).</summary>
  let loadDepthShadowShader() : Shader =
    Raylib.LoadShaderFromMemory(depthShadowVertex, depthShadowFragment)

  /// <summary>Loads the skinned forward PBR vertex + fragment shader.</summary>
  /// <remarks>
  /// Uses <c>vertexBoneIndices</c> and <c>vertexBoneWeights</c> vertex attributes plus
  /// a <c>boneMatrices[128]</c> uniform array for GPU skeletal animation.
  /// Raylib binds these attribute/uniform names automatically during shader linking.
  /// </remarks>
  let loadForwardSkinnedShader
    (maxPointLights: int)
    (maxSpotLights: int)
    (maxShadowCasters: int)
    : Shader =
    Raylib.LoadShaderFromMemory(
      forwardVertexSkinned,
      forwardFragmentFmt maxPointLights maxSpotLights maxShadowCasters
    )

  /// <summary>Loads the skinned depth-only shadow pass vertex + fragment shader.</summary>
  let loadDepthShadowSkinnedShader() : Shader =
    Raylib.LoadShaderFromMemory(depthShadowVertexSkinned, depthShadowFragment)

  /// <summary>Loads the built-in fullscreen post-process vertex + fragment shader.</summary>
  let loadPostProcessShader() : Shader =
    Raylib.LoadShaderFromMemory(postProcessVertex, postProcessFragment)
