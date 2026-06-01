namespace Mibo.Elmish.Graphics2D.Lighting

open Raylib_cs

/// <summary>Embedded GLSL shaders for the built-in lit-sprite pipeline with SDF shadow raymarching.</summary>
module LitShader =

  let vertexSource =
    """#version 330
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec4 vertexColor;

uniform mat4 mvp;
uniform mat4 matModel;

out vec2 fragTexCoord;
out vec4 fragColor;
out vec2 fragWorldPos;

void main()
{
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    fragWorldPos = (matModel * vec4(vertexPosition, 1.0)).xy;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
"""

  let fragmentSource =
    """#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
in vec2 fragWorldPos;

out vec4 finalColor;

uniform sampler2D texture0;

#define MAX_DIR_LIGHTS 4
#define MAX_POINT_LIGHTS 16
#define MAX_OCCLUDERS 128

uniform vec3 ambientColor;

uniform int dirLightCount;
uniform vec2 dirLightDirs[MAX_DIR_LIGHTS];
uniform vec3 dirLightColors[MAX_DIR_LIGHTS];
uniform float dirLightIntensities[MAX_DIR_LIGHTS];
uniform int dirLightShadowIdx[MAX_DIR_LIGHTS];

uniform int pointLightCount;
uniform vec2 pointLightPos[MAX_POINT_LIGHTS];
uniform vec3 pointLightColors[MAX_POINT_LIGHTS];
uniform float pointLightIntensities[MAX_POINT_LIGHTS];
uniform float pointLightRadii[MAX_POINT_LIGHTS];
uniform float pointLightFalloffs[MAX_POINT_LIGHTS];
uniform int pointLightShadowIdx[MAX_POINT_LIGHTS];

uniform vec4 occluders[MAX_OCCLUDERS];
uniform int occluderCount;
uniform float shadowSoftness;
uniform float shadowMaxDistance;

float sdSegment(vec2 p, vec2 a, vec2 b)
{
    vec2 pa = p - a;
    vec2 ba = b - a;
    float baLen2 = dot(ba, ba);
    float h = (baLen2 < 0.0001) ? 0.0 : clamp(dot(pa, ba) / baLen2, 0.0, 1.0);
    return length(pa - ba * h);
}

float sceneSDF(vec2 p)
{
    float d = 1e10;
    for (int i = 0; i < occluderCount; i++)
    {
        d = min(d, sdSegment(p, occluders[i].xy, occluders[i].zw));
    }
    return d;
}

float sampleShadow(vec2 worldPos, vec2 lightDirOrPos, bool isDirectional, float softness)
{
    vec2 ro = worldPos;
    vec2 rd = isDirectional ? -normalize(lightDirOrPos) : normalize(lightDirOrPos - worldPos);
    float maxt = isDirectional ? shadowMaxDistance : distance(worldPos, lightDirOrPos);

    if (maxt < 0.01 || occluderCount < 1)
        return 1.0;

    float k = 1.0 / max(softness, 0.0001);
    float res = 1.0;
    float t = 0.01;

    for (int i = 0; i < 64; i++)
    {
        if (t > maxt) break;
        vec2 p = ro + rd * t;
        float h = sceneSDF(p);
        if (h < 0.001) return 0.0;
        res = min(res, k * h / t);
        if (res < 0.001) return 0.0;
        t += h;
    }
    return clamp(res, 0.0, 1.0);
}

void main()
{
    vec4 texColor = texture(texture0, fragTexCoord) * fragColor;
    vec3 lighting = ambientColor;

    for (int i = 0; i < dirLightCount && i < MAX_DIR_LIGHTS; i++)
    {
        float shadow = 1.0;
        if (dirLightShadowIdx[i] >= 0)
            shadow = sampleShadow(fragWorldPos, dirLightDirs[i], true, shadowSoftness);
        lighting += dirLightColors[i] * dirLightIntensities[i] * shadow;
    }

    for (int i = 0; i < pointLightCount && i < MAX_POINT_LIGHTS; i++)
    {
        float dist = length(fragWorldPos - pointLightPos[i]);
        if (dist < pointLightRadii[i])
        {
            float atten = pow(1.0 - dist / pointLightRadii[i], pointLightFalloffs[i]);
            float shadow = 1.0;
            if (pointLightShadowIdx[i] >= 0)
                shadow = sampleShadow(fragWorldPos, pointLightPos[i], false, shadowSoftness);
            lighting += pointLightColors[i] * pointLightIntensities[i] * atten * shadow;
        }
    }

    finalColor = vec4(texColor.rgb * lighting, texColor.a);
}
"""

  /// <summary>
  /// Fragment shader variant for normal-mapped sprites.
  /// Uses a 2D-compatible Half-Lambert lighting model where
  /// <c>NdotL = max(1.0 + dot(normal.xy, L), 0.0)</c>, so flat normals
  /// produce the same result as the non-NM shader while the normal map's
  /// XY perturbation adds directional variation.
  /// </summary>
  let fragmentSourceNormalMap =
    """#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
in vec2 fragWorldPos;

out vec4 finalColor;

uniform sampler2D texture0;

#define MAX_DIR_LIGHTS 4
#define MAX_POINT_LIGHTS 16
#define MAX_OCCLUDERS 128

uniform vec3 ambientColor;

uniform int dirLightCount;
uniform vec2 dirLightDirs[MAX_DIR_LIGHTS];
uniform vec3 dirLightColors[MAX_DIR_LIGHTS];
uniform float dirLightIntensities[MAX_DIR_LIGHTS];
uniform int dirLightShadowIdx[MAX_DIR_LIGHTS];

uniform int pointLightCount;
uniform vec2 pointLightPos[MAX_POINT_LIGHTS];
uniform vec3 pointLightColors[MAX_POINT_LIGHTS];
uniform float pointLightIntensities[MAX_POINT_LIGHTS];
uniform float pointLightRadii[MAX_POINT_LIGHTS];
uniform float pointLightFalloffs[MAX_POINT_LIGHTS];
uniform int pointLightShadowIdx[MAX_POINT_LIGHTS];

uniform vec4 occluders[MAX_OCCLUDERS];
uniform int occluderCount;
uniform float shadowSoftness;
uniform float shadowMaxDistance;

uniform sampler2D normalMap;

float sdSegment(vec2 p, vec2 a, vec2 b)
{
    vec2 pa = p - a;
    vec2 ba = b - a;
    float baLen2 = dot(ba, ba);
    float h = (baLen2 < 0.0001) ? 0.0 : clamp(dot(pa, ba) / baLen2, 0.0, 1.0);
    return length(pa - ba * h);
}

float sceneSDF(vec2 p)
{
    float d = 1e10;
    for (int i = 0; i < occluderCount; i++)
    {
        d = min(d, sdSegment(p, occluders[i].xy, occluders[i].zw));
    }
    return d;
}

float sampleShadow(vec2 worldPos, vec2 lightDirOrPos, bool isDirectional, float softness)
{
    vec2 ro = worldPos;
    vec2 rd = isDirectional ? -normalize(lightDirOrPos) : normalize(lightDirOrPos - worldPos);
    float maxt = isDirectional ? shadowMaxDistance : distance(worldPos, lightDirOrPos);

    if (maxt < 0.01 || occluderCount < 1)
        return 1.0;

    float k = 1.0 / max(softness, 0.0001);
    float res = 1.0;
    float t = 0.01;

    for (int i = 0; i < 64; i++)
    {
        if (t > maxt) break;
        vec2 p = ro + rd * t;
        float h = sceneSDF(p);
        if (h < 0.001) return 0.0;
        res = min(res, k * h / t);
        if (res < 0.001) return 0.0;
        t += h;
    }
    return clamp(res, 0.0, 1.0);
}

vec3 getNormal()
{
    vec3 n = texture(normalMap, fragTexCoord).rgb * 2.0 - 1.0;
    return normalize(n);
}

void main()
{
    vec4 texColor = texture(texture0, fragTexCoord) * fragColor;
    vec3 normal = getNormal();
    vec3 lighting = ambientColor;

    for (int i = 0; i < dirLightCount && i < MAX_DIR_LIGHTS; i++)
    {
        float shadow = 1.0;
        if (dirLightShadowIdx[i] >= 0)
            shadow = sampleShadow(fragWorldPos, dirLightDirs[i], true, shadowSoftness);
        vec2 L = -normalize(dirLightDirs[i]);
        float NdotL = max(1.0 + dot(normal.xy, L), 0.0);
        lighting += dirLightColors[i] * dirLightIntensities[i] * NdotL * shadow;
    }

    for (int i = 0; i < pointLightCount && i < MAX_POINT_LIGHTS; i++)
    {
        float dist = length(fragWorldPos - pointLightPos[i]);
        if (dist < pointLightRadii[i])
        {
            float atten = pow(1.0 - dist / pointLightRadii[i], pointLightFalloffs[i]);
            float shadow = 1.0;
            if (pointLightShadowIdx[i] >= 0)
                shadow = sampleShadow(fragWorldPos, pointLightPos[i], false, shadowSoftness);
            vec2 toLight = pointLightPos[i] - fragWorldPos;
            vec2 L = length(toLight) > 0.001 ? normalize(toLight) : vec2(0.0, 0.0);
            float NdotL = max(1.0 + dot(normal.xy, L), 0.0);
            lighting += pointLightColors[i] * pointLightIntensities[i] * atten * NdotL * shadow;
        }
    }

    finalColor = vec4(texColor.rgb * lighting, texColor.a);
}
"""

  /// <summary>Loads the built-in lit-sprite shader from embedded GLSL sources.</summary>
  let load() =
    Raylib.LoadShaderFromMemory(vertexSource, fragmentSource)

  /// <summary>Loads the normal-mapped lit-sprite shader from embedded GLSL sources.</summary>
  let loadNormalMap() =
    Raylib.LoadShaderFromMemory(vertexSource, fragmentSourceNormalMap)
