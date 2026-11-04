#version 430 core

in vec3 vWorldPos;
in vec3 vertexNormal;
in vec3 vTangent;
in float vTangentHandedness;
in vec2 texCoord;

out vec4 FragColor;

uniform sampler2D atlas;
uniform sampler2D normalAtlas;
uniform sampler2DShadow shadowMap;
uniform vec3 uCamPos;

uniform mat4 lightSpace;
uniform vec3 lightDir;
uniform float texelWorld;

const float NORMAL_OFFSET = 1.5;
const float CONST_BIAS = 0.005;
const float DENSITY = 0.008;

const vec3 ambient = vec3(1.0, 1.0, 1.0);
const float ambient_strength = 0.15;
const float direct_strength = 0.4;
const float NORMAL_STRENGTH = 0.2;

vec3 ACESFilm(vec3 color, float exposure)
{
    color *= exp2(exposure);
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((color * (a * color + b)) / (color * (c * color + d) + e), 0.0, 1.0);
}

float ShadowFactor(vec3 worldPos, vec3 N)
{
    float cosT = clamp(dot(N, -lightDir), 0.0, 1.0);
    float sinT = sqrt(1.0 - cosT * cosT);

    vec3 offsetPos = worldPos + N * texelWorld * NORMAL_OFFSET * sinT;

    vec4 lp = lightSpace * vec4(offsetPos, 1.0);
    vec3 p = lp.xyz / lp.w * 0.5 + 0.5;

    if (p.z > 1.0) return 1.0;

    float shadow = 0.0;
    vec2 texelSize = 1.0 / textureSize(shadowMap, 0);

    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            vec2 offset = vec2(x, y) * texelSize;
            shadow += texture(shadowMap, vec3(p.xy + offset, p.z - CONST_BIAS));
        }
    }

    return shadow / 9.0;
}

vec3 ApplyFog(vec3 color, float depth)
{
    const vec3 fogColor = vec3(0.65, 0.75, 0.9);
    const float density = 0.012;

    float fogFactor = 1.0 - exp(-depth * density);

    return mix(color, fogColor, fogFactor);
}

void main()
{
    vec4 color = texture(atlas, texCoord);

    float depth = length(vWorldPos - uCamPos);

    if (color.a < 0.5)
        discard;

    // normals
    vec3 N = normalize(vertexNormal);
    vec3 T = normalize(vTangent - dot(vTangent, N) * N);
    vec3 B = cross(N, T) * vTangentHandedness;

    mat3 TBN = mat3(T, B, N);

    vec3 normalSample = texture(normalAtlas, texCoord).rgb;
    normalSample = normalize(normalSample * 2.0 - 1.0);

    normalSample = normalize(mix(vec3(0.0, 0.0, 1.0), normalSample, NORMAL_STRENGTH));

    N = normalize(TBN * normalSample);

    // Lighting
    float ndl = max(dot(N, -lightDir), 0.0);
    float shadow = ShadowFactor(vWorldPos, N);

    vec3 lighting = ambient * ambient_strength + direct_strength * ndl * shadow;

    color.rgb *= lighting;
    color.rgb = ACESFilm(color.rgb, 0.713);
    color.rgb = ApplyFog(color.rgb, depth);


    //FragColor = vec4(texture(normalAtlas, texCoord).rgb, 1.0);

    FragColor = color;
}