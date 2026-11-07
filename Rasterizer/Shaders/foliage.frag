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
    const float density = 0.006;

    float fogFactor = 1.0 - exp(-depth * density);

    return mix(color, fogColor, fogFactor);
}

float Hash01(ivec3 p)
{
    uvec3 v = uvec3(p);
    uint h = v.x * 1597334673u ^ v.y * 3812015801u ^ v.z * 2798796415u;
    h ^= h >> 16; h *= 0x7feb352du;
    h ^= h >> 15; h *= 0x846ca68bu;
    h ^= h >> 16;
    return float(h) * (1.0 / 4294967295.0);
}

float ValueNoise(vec3 p)
{
    ivec3 i = ivec3(floor(p));
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);

    float n000 = Hash01(i);
    float n100 = Hash01(i + ivec3(1, 0, 0));
    float n010 = Hash01(i + ivec3(0, 1, 0));
    float n110 = Hash01(i + ivec3(1, 1, 0));
    float n001 = Hash01(i + ivec3(0, 0, 1));
    float n101 = Hash01(i + ivec3(1, 0, 1));
    float n011 = Hash01(i + ivec3(0, 1, 1));
    float n111 = Hash01(i + ivec3(1, 1, 1));

    return mix(mix(mix(n000, n100, f.x), mix(n010, n110, f.x), f.y),
               mix(mix(n001, n101, f.x), mix(n011, n111, f.x), f.y), f.z);
}

vec3 FoliageTint(vec3 worldPos, vec3 geoNormal)
{
    // Nudge into the block so all faces of a voxel hash to the same cell
    ivec3 cell = ivec3(floor(worldPos - geoNormal * 0.01));

    float perBlock = Hash01(cell);                    // speckle
    float patch_    = ValueNoise(worldPos * (1.0 / 8.0)); // large-scale variation

    float t = clamp(patch_ * 0.75 + perBlock * 0.25, 0.0, 1.0);

    const vec3 cool = vec3(0.88, 1.00, 1.06); // bluer green
    const vec3 warm = vec3(1.08, 1.04, 0.82); // yellower green

    float brightness = 1.0 + (perBlock - 0.5) * 0.12;
    return mix(cool, warm, t) * brightness;
}

void main()
{
    vec4 color = texture(atlas, texCoord);

    float depth = length(vWorldPos - uCamPos);

    if (color.a < 0.5)
        discard;

    // normals
    vec3 N = normalize(vertexNormal);
    vec3 geoN = N; // keep the geometric normal for the foliage hash
    vec3 T = normalize(vTangent - dot(vTangent, N) * N);
    vec3 B = cross(N, T) * vTangentHandedness;

    mat3 TBN = mat3(T, B, N);

    vec3 normalSample = texture(normalAtlas, texCoord).rgb;
    normalSample = normalize(normalSample * 2.0 - 1.0);
    normalSample = normalize(mix(vec3(0.0, 0.0, 1.0), normalSample, NORMAL_STRENGTH));

    N = normalize(TBN * normalSample);

    // Foliage variation
    color.rgb *= FoliageTint(vWorldPos, geoN);

    // Lighting
    float ndl = max(dot(N, -lightDir), 0.0);
    float shadow = ShadowFactor(vWorldPos, N);

    vec3 lighting = ambient * ambient_strength + direct_strength * ndl * shadow;

    color.rgb *= lighting;
    color.rgb = ACESFilm(color.rgb, 0.713);
    color.rgb = ApplyFog(color.rgb, depth);

    FragColor = color;
}