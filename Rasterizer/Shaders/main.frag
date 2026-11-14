#version 430 core

in vec3 vWorldPos;
in vec3 vertexNormal;
in vec3 vTangent;
in float vTangentHandedness;
in vec2 texCoord;
in vec4 vTileRect;

out vec4 FragColor;

uniform sampler2D atlas;
uniform sampler2D normalAtlas;
uniform sampler2D tintAtlas;
uniform sampler2D whittakerAtlas;
uniform sampler2DShadow shadowMap;
uniform vec3 uCamPos;

uniform mat4 lightSpace;
uniform vec3 lightDir;
uniform float texelWorld;

const float NORMAL_OFFSET = 1.5;
const float CONST_BIAS = 0.0002;
const float DENSITY = 0.008;

const vec3 ambient = vec3(1.0, 1.0, 1.0);
const float ambient_strength = 0.35;
const float direct_strength = 0.75;
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

vec3 ColorCorrection(
    vec3 color,
    float exposure,
    float contrast,
    float saturation,
    float vibrance,
    float gamma
)
{
    // Exposure
    color *= exp2(exposure);

    // Contrast
    color = (color - 0.5) * contrast + 0.5;

    // Saturation
    float luminance = dot(color, vec3(0.2126, 0.7152, 0.0722));
    color = mix(vec3(luminance), color, saturation);

    // Vibrance
    float maxColor = max(max(color.r, color.g), color.b);
    float minColor = min(min(color.r, color.g), color.b);
    float chroma = maxColor - minColor;

    float vibranceFactor = 1.0 - chroma;
    color = mix(vec3(luminance), color,
                1.0 + vibrance * vibranceFactor);

    // Gamma
    color = pow(max(color, 0.0), vec3(1.0 / gamma));

    return clamp(color, 0.0, 1.0);
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

vec3 ApplyAtmosphere(vec3 color, vec3 viewDir, float depth)
{
    vec3 sunDir = normalize(-lightDir);
    vec3 up = vec3(0.0, 1.0, 0.0);

    // Rayleigh phase function
    float cosTheta = dot(viewDir, sunDir);
    float rayleighPhase = 0.75 * (1.0 + cosTheta * cosTheta);

    // Stronger scattering near the horizon
    float horizon = 1.0 - abs(dot(viewDir, up));
    horizon = pow(horizon, 2.0);

    // Atmospheric optical depth approximation
    float opticalDepth = 1.0 - exp(-depth * 0.0025);

    // Approximate Rayleigh scattering color
    vec3 rayleighColor = vec3(0.20, 0.42, 0.85);

    float scattering = opticalDepth
                     * (0.12 + horizon * 0.65)
                     * rayleighPhase;

    scattering = clamp(scattering, 0.0, 0.65);

    return mix(color, rayleighColor, scattering);
}

float Hash(vec3 p)
{
    p = fract(p * 0.3183099 + vec3(0.1, 0.2, 0.3));
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float WorldNoise(vec3 p)
{
    vec3 i = floor(p);
    vec3 f = fract(p);

    f = f * f * (3.0 - 2.0 * f);

    float n000 = Hash(i + vec3(0, 0, 0));
    float n100 = Hash(i + vec3(1, 0, 0));
    float n010 = Hash(i + vec3(0, 1, 0));
    float n110 = Hash(i + vec3(1, 1, 0));

    float n001 = Hash(i + vec3(0, 0, 1));
    float n101 = Hash(i + vec3(1, 0, 1));
    float n011 = Hash(i + vec3(0, 1, 1));
    float n111 = Hash(i + vec3(1, 1, 1));

    float x0 = mix(
        mix(n000, n100, f.x),
        mix(n010, n110, f.x),
        f.y
    );

    float x1 = mix(
        mix(n001, n101, f.x),
        mix(n011, n111, f.x),
        f.y
    );

    return mix(x0, x1, f.z);
}

vec3 RotateHue(vec3 color, float angle)
{
    const vec3 axis = normalize(vec3(1.0));

    float s = sin(angle);
    float c = cos(angle);

    return color * c
        + cross(axis, color) * s
        + axis * dot(axis, color) * (1.0 - c);
}

void main()
{
    // derivatives must be taken before any discard / branching
    vec2 dx = dFdx(texCoord);
    vec2 dy = dFdy(texCoord);

    // greedy quads: tile the single atlas cell across the merged face
    vec2 uv = texCoord;
    if (vTileRect.z > 0.0)
    {
        uv = vTileRect.xy + fract(texCoord) * vTileRect.zw;
        dx *= vTileRect.zw;
        dy *= vTileRect.zw;
    }

    vec4 color = textureGrad(atlas, uv, dx, dy);
    float depth = length(vWorldPos - uCamPos);

    if (color.a < 0.5)
        discard;

    vec4 tintMask = textureGrad(tintAtlas, uv, dx, dy);

    // normals
    vec3 N = normalize(vertexNormal);
    vec3 T = normalize(vTangent - dot(vTangent, N) * N);
    vec3 B = cross(N, T) * vTangentHandedness;

    mat3 TBN = mat3(T, B, N);

    vec3 normalSample = textureGrad(normalAtlas, uv, dx, dy).rgb;
    normalSample = normalize(normalSample * 2.0 - 1.0);

    normalSample = normalize(mix(vec3(0.0, 0.0, 1.0), normalSample, NORMAL_STRENGTH));

    N = normalize(TBN * normalSample);

    // Lighting
    float ndl = max(dot(N, -lightDir), 0.0);
    float shadow = ShadowFactor(vWorldPos, N);
    vec3 lighting = ambient * ambient_strength + direct_strength * ndl * shadow;

    // Tinting
    float tintStrength = tintMask.r;

    vec3 biomeTint = texture(whittakerAtlas, vec2(0.5, 0.66)).rgb;

    // Biome tint
    color.rgb *= mix(vec3(1.0), biomeTint, tintStrength);

    // Procedural hue variation
    float largeNoise = WorldNoise(vWorldPos / 48.0);
    float smallNoise = WorldNoise(vWorldPos / 6.0);

    float hueVariation = (largeNoise - 0.5) * 0.15
                       + (smallNoise - 0.5) * 0.10;

    color.rgb = mix(
        color.rgb,
        RotateHue(color.rgb, hueVariation),
        tintStrength
    );
    //

    color.rgb *= lighting;
    //color.rgb = ACESFilm(color.rgb, 0.0113);
    color.rgb = ColorCorrection(color.rgb, 0.15, 0.95, 0.9, 0.0, 1.0);
    vec3 viewDir = normalize(vWorldPos - uCamPos);

    color.rgb = ApplyAtmosphere(color.rgb, viewDir, depth);
    //color.rgb = vec3(direct_strength);


    //color.rgb = vec3(tintStrength);


    FragColor = color;
}