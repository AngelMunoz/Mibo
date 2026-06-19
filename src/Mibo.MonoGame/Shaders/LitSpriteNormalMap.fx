#if OPENGL
  #define VS_SHADERMODEL vs_3_0
  #define PS_SHADERMODEL ps_3_0
#else
  #define VS_SHADERMODEL vs_4_0_level_9_1
  #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

#define MAX_DIR_LIGHTS 4
#define MAX_POINT_LIGHTS 16
#ifndef MAX_OCCLUDERS
  #define MAX_OCCLUDERS 128
#endif

float4x4 MatrixTransform;
sampler2D Texture : register(s0);
sampler2D NormalMap : register(s1);

float3 AmbientColor;

int DirLightCount;
float2 DirLightDirs[MAX_DIR_LIGHTS];
float3 DirLightColors[MAX_DIR_LIGHTS];
float DirLightIntensities[MAX_DIR_LIGHTS];
int DirLightShadowIdx[MAX_DIR_LIGHTS];

int PointLightCount;
float2 PointLightPos[MAX_POINT_LIGHTS];
float3 PointLightColors[MAX_POINT_LIGHTS];
float PointLightIntensities[MAX_POINT_LIGHTS];
float PointLightRadii[MAX_POINT_LIGHTS];
float PointLightFalloffs[MAX_POINT_LIGHTS];
int PointLightShadowIdx[MAX_POINT_LIGHTS];

float4 Occluders[MAX_OCCLUDERS];
int OccluderCount;
float ShadowSoftness;
float ShadowMaxDistance;

struct VS_INPUT {
  float4 Position : POSITION0;
  float4 Color    : COLOR0;
  float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT {
  float4 Position : POSITION0;
  float4 Color    : COLOR0;
  float2 TexCoord : TEXCOORD0;
  float2 WorldPos : TEXCOORD1;
};

VS_OUTPUT VS_Main(VS_INPUT input) {
  VS_OUTPUT output;
  output.Position = mul(input.Position, MatrixTransform);
  output.Color = input.Color;
  output.TexCoord = input.TexCoord;
  output.WorldPos = input.Position.xy;
  return output;
}

float sdSegment(float2 p, float2 a, float2 b) {
  float2 pa = p - a;
  float2 ba = b - a;
  float baLen2 = dot(ba, ba);
  float h = (baLen2 < 0.0001) ? 0.0 : clamp(dot(pa, ba) / baLen2, 0.0, 1.0);
  return length(pa - ba * h);
}

float sceneSDF(float2 p) {
  float d = 1e10;
  [loop]
  for (int i = 0; i < MAX_OCCLUDERS; i++) {
    if (i >= OccluderCount) break;
    d = min(d, sdSegment(p, Occluders[i].xy, Occluders[i].zw));
  }
  return d;
}

float sampleShadow(float2 worldPos, float2 lightDirOrPos, bool isDirectional, float softness) {
  float2 ro = worldPos;
  float2 rd = isDirectional ? -normalize(lightDirOrPos) : normalize(lightDirOrPos - worldPos);
  float maxt = isDirectional ? ShadowMaxDistance : distance(worldPos, lightDirOrPos);
  if (maxt < 0.01 || OccluderCount < 1) return 1.0;
  float k = 1.0 / max(softness, 0.0001);
  float res = 1.0;
  float t = 0.01;
  [loop]
  for (int j = 0; j < 64; j++) {
    if (t > maxt) break;
    float2 p = ro + rd * t;
    float h = sceneSDF(p);
    if (h < 0.001) return 0.0;
    res = min(res, k * h / t);
    if (res < 0.001) return 0.0;
    t += h;
  }
  return clamp(res, 0.0, 1.0);
}

float4 PS_Main(VS_OUTPUT input) : COLOR0 {
  float4 texColor = tex2D(Texture, input.TexCoord) * input.Color;
  float3 normal = normalize(tex2D(NormalMap, input.TexCoord).rgb * 2.0 - 1.0);
  float3 lighting = AmbientColor;

  [loop]
  for (int di = 0; di < MAX_DIR_LIGHTS; di++) {
    if (di >= DirLightCount) break;
    float shadow = 1.0;
    if (DirLightShadowIdx[di] >= 0)
      shadow = sampleShadow(input.WorldPos, DirLightDirs[di], true, ShadowSoftness);
    float2 L = -normalize(DirLightDirs[di]);
    float NdotL = max(1.0 + dot(normal.xy, L), 0.0);
    lighting += DirLightColors[di] * DirLightIntensities[di] * NdotL * shadow;
  }

  [loop]
  for (int pi = 0; pi < MAX_POINT_LIGHTS; pi++) {
    if (pi >= PointLightCount) break;
    float dist = length(input.WorldPos - PointLightPos[pi]);
    if (dist < PointLightRadii[pi]) {
      float atten = pow(abs(1.0 - dist / PointLightRadii[pi]), PointLightFalloffs[pi]);
      float shadow = 1.0;
      if (PointLightShadowIdx[pi] >= 0)
        shadow = sampleShadow(input.WorldPos, PointLightPos[pi], false, ShadowSoftness);
      float2 toLight = PointLightPos[pi] - input.WorldPos;
      float2 L = length(toLight) > 0.001 ? normalize(toLight) : float2(0.0, 0.0);
      float NdotL = max(1.0 + dot(normal.xy, L), 0.0);
      lighting += PointLightColors[pi] * PointLightIntensities[pi] * atten * NdotL * shadow;
    }
  }

  return float4(texColor.rgb * lighting, texColor.a);
}

technique LitSpriteNormalMap {
  pass P0 {
    VertexShader = compile VS_SHADERMODEL VS_Main();
    PixelShader = compile PS_SHADERMODEL PS_Main();
  }
}
