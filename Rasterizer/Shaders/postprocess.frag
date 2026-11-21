#version 430 core

in vec2 vUV;
out vec4 FragColor;

uniform sampler2D screenTexture;   // GL_LINEAR + CLAMP_TO_EDGE, LDR gamma-space color
uniform sampler2D depthTexture;    // single-sample depth, compare mode OFF
uniform sampler2D foliageMask;     // 1 where the visible pixel is foliage
uniform float inverseWidth;
uniform float inverseHeight;
uniform float nearPlane;
uniform float farPlane;
uniform mat4 projection;
uniform mat4 inverseProjection;

const float SSAO_RADIUS = 0.5;
const float SSAO_BIAS = 0.08;
const float SSAO_STRENGTH = 0.35;
const int SSAO_SAMPLES = 12;

const vec3 SSAO_KERNEL[SSAO_SAMPLES] = vec3[](
    vec3( 0.000,  0.000, 1.000),
    vec3( 0.276,  0.000, 0.961),
    vec3(-0.276,  0.000, 0.961),
    vec3( 0.000,  0.276, 0.961),
    vec3( 0.000, -0.276, 0.961),
    vec3( 0.530,  0.530, 0.662),
    vec3(-0.530,  0.530, 0.662),
    vec3( 0.530, -0.530, 0.662),
    vec3(-0.530, -0.530, 0.662),
    vec3( 0.707,  0.000, 0.707),
    vec3( 0.000,  0.707, 0.707),
    vec3(-0.707,  0.000, 0.707)
);

// --- base FXAA settings ---
#define EDGE_THRESHOLD_MIN 0.0312  // skip very dark areas
#define EDGE_THRESHOLD_MAX 0.125   // relative contrast needed to treat as edge
#define SUBPIXEL_QUALITY   0.75    // 0 = off, 1 = max blur of thin features
#define ITERATIONS         12

// --- depth-aware settings ---
#define NEAR_START          4.0    // world units: fully "close" below this
#define NEAR_END            40.0   // fully "far" above this
#define NEAR_THRESHOLD_MUL  3.0    // how much less aggressive up close
#define DEPTH_EDGE_REL      0.015  // relative depth jump = geometry edge

const float STEP_QUALITY[ITERATIONS] = float[](
    1.0, 1.0, 1.0, 1.0, 1.0, 1.5, 2.0, 2.0, 2.0, 2.0, 4.0, 8.0
);

float Luma(vec3 c)
{
    return dot(c, vec3(0.299, 0.587, 0.114));
}

vec3 Sample(vec2 uv)
{
    return textureLod(screenTexture, uv, 0.0).rgb;
}

float LinearDepth(float d)
{
    float z = d * 2.0 - 1.0;
    return (2.0 * nearPlane * farPlane) / (farPlane + nearPlane - z * (farPlane - nearPlane));
}

float DepthAt(vec2 o)
{
    vec2 texel = vec2(inverseWidth, inverseHeight);
    return LinearDepth(textureLod(depthTexture, vUV + o * texel, 0.0).r);
}

vec3 ViewPosition(vec2 uv, float depth)
{
    vec4 clip = vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    vec4 view = inverseProjection * clip;
    return view.xyz / view.w;
}

float Hash12(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

bool HasNearbyFoliage(vec2 uv)
{
    vec2 texel = vec2(inverseWidth, inverseHeight);

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            if (textureLod(foliageMask, uv + vec2(x, y) * texel, 0.0).r > 0.5)
                return true;
        }
    }

    return false;
}

bool HasNearbyDepthEdge(vec2 uv, float centerDepth)
{
    vec2 texel = vec2(inverseWidth, inverseHeight);
    float allowedDifference = max(0.25, centerDepth * 0.08);

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0)
                continue;

            float neighborDepth = LinearDepth(
                textureLod(depthTexture, uv + vec2(x, y) * texel, 0.0).r);

            if (abs(neighborDepth - centerDepth) > allowedDifference)
                return true;
        }
    }

    return false;
}

float ScreenSpaceAo(vec2 uv)
{
    float depth = textureLod(depthTexture, uv, 0.0).r;
    if (depth >= 0.999999)
        return 1.0;

    // Foliage should retain its original color and should not contribute
    // occlusion to nearby terrain either.
    if (textureLod(foliageMask, uv, 0.0).r > 0.5)
        return 1.0;

    // Avoid reconstructing terrain normals across foliage depth edges. The
    // foliage depth belongs to a different surface and would create a dark
    // halo on the terrain immediately behind or beside alpha-tested leaves.
    if (HasNearbyFoliage(uv))
        return 1.0;

    float centerDepth = LinearDepth(depth);
    if (HasNearbyDepthEdge(uv, centerDepth))
        return 1.0;

    vec2 texel = vec2(inverseWidth, inverseHeight);
    vec3 position = ViewPosition(uv, depth);
    vec3 positionRight = ViewPosition(uv + vec2(texel.x, 0.0),
                                      textureLod(depthTexture, uv + vec2(texel.x, 0.0), 0.0).r);
    vec3 positionUp = ViewPosition(uv + vec2(0.0, texel.y),
                                   textureLod(depthTexture, uv + vec2(0.0, texel.y), 0.0).r);

    vec3 normal = normalize(cross(positionRight - position, positionUp - position));
    if (dot(normal, -position) < 0.0)
        normal = -normal;

    vec3 random = normalize(vec3(
        Hash12(gl_FragCoord.xy),
        Hash12(gl_FragCoord.yx + 17.0),
        Hash12(gl_FragCoord.xy + 41.0)) * 2.0 - 1.0);

    vec3 tangent = normalize(random - normal * dot(random, normal));
    vec3 bitangent = cross(normal, tangent);
    mat3 basis = mat3(tangent, bitangent, normal);

    float occlusion = 0.0;
    for (int i = 0; i < SSAO_SAMPLES; i++)
    {
        vec3 samplePosition = position + basis * SSAO_KERNEL[i] * SSAO_RADIUS;
        vec4 projected = projection * vec4(samplePosition, 1.0);
        vec2 sampleUv = projected.xy / projected.w * 0.5 + 0.5;

        if (sampleUv.x <= 0.0 || sampleUv.x >= 1.0 ||
            sampleUv.y <= 0.0 || sampleUv.y >= 1.0)
            continue;

        float sampleDepth = textureLod(depthTexture, sampleUv, 0.0).r;
        if (sampleDepth >= 0.999999)
            continue;

        if (textureLod(foliageMask, sampleUv, 0.0).r > 0.5)
            continue;

        float sceneZ = ViewPosition(sampleUv, sampleDepth).z;
        float range = smoothstep(0.0, 1.0,
                                 SSAO_RADIUS / abs(position.z - sceneZ));

        // View-space Z is negative; a larger value is closer to the camera.
        occlusion += step(samplePosition.z + SSAO_BIAS, sceneZ) * range;
    }

    float ao = 1.0 - occlusion / float(SSAO_SAMPLES);
    return pow(clamp(ao, 0.0, 1.0), SSAO_STRENGTH);
}

void main()
{
    vec2 texel = vec2(inverseWidth, inverseHeight);

    float ao = ScreenSpaceAo(vUV);
    FragColor = vec4(Sample(vUV) * ao, 1.0);
    return;

    vec3  colorCenter = Sample(vUV);
    float lumaCenter  = Luma(colorCenter);

    float lumaDown  = Luma(textureLodOffset(screenTexture, vUV, 0.0, ivec2( 0, -1)).rgb);
    float lumaUp    = Luma(textureLodOffset(screenTexture, vUV, 0.0, ivec2( 0,  1)).rgb);
    float lumaLeft  = Luma(textureLodOffset(screenTexture, vUV, 0.0, ivec2(-1,  0)).rgb);
    float lumaRight = Luma(textureLodOffset(screenTexture, vUV, 0.0, ivec2( 1,  0)).rgb);

    float lumaMin = min(lumaCenter, min(min(lumaDown, lumaUp), min(lumaLeft, lumaRight)));
    float lumaMax = max(lumaCenter, max(max(lumaDown, lumaUp), max(lumaLeft, lumaRight)));
    float lumaRange = lumaMax - lumaMin;

    // --- depth-aware aggressiveness ---
    float depthC = LinearDepth(textureLod(depthTexture, vUV, 0.0).r);

    float depthJump = max(
        max(abs(DepthAt(vec2( 1.0,  0.0)) - depthC), abs(DepthAt(vec2(-1.0,  0.0)) - depthC)),
        max(abs(DepthAt(vec2( 0.0,  1.0)) - depthC), abs(DepthAt(vec2( 0.0, -1.0)) - depthC))
    ) / depthC;

    bool  geometryEdge = depthJump > DEPTH_EDGE_REL;
    float closeness    = 1.0 - smoothstep(NEAR_START, NEAR_END, depthC); // 1 = close, 0 = far

    // Close + no depth edge = probably texture detail -> be much stricter
    float thresholdMul = geometryEdge ? 1.0 : mix(1.0, NEAR_THRESHOLD_MUL, closeness);

    // Early exit: not an edge -> keep the pixel perfectly sharp
    if (lumaRange < max(EDGE_THRESHOLD_MIN, lumaMax * EDGE_THRESHOLD_MAX * thresholdMul))
    {
        FragColor = vec4(colorCenter, 1.0);
        return;
    }

    float lumaDownLeft  = Luma(textureLodOffset(screenTexture, vUV, 0.0, ivec2(-1, -1)).rgb);
    float lumaUpRight   = Luma(textureLodOffset(screenTexture, vUV, 0.0, ivec2( 1,  1)).rgb);
    float lumaUpLeft    = Luma(textureLodOffset(screenTexture, vUV, 0.0, ivec2(-1,  1)).rgb);
    float lumaDownRight = Luma(textureLodOffset(screenTexture, vUV, 0.0, ivec2( 1, -1)).rgb);

    float lumaDownUp       = lumaDown + lumaUp;
    float lumaLeftRight    = lumaLeft + lumaRight;
    float lumaLeftCorners  = lumaDownLeft + lumaUpLeft;
    float lumaDownCorners  = lumaDownLeft + lumaDownRight;
    float lumaRightCorners = lumaDownRight + lumaUpRight;
    float lumaUpCorners    = lumaUpRight + lumaUpLeft;

    // Is the edge horizontal or vertical?
    float edgeHorizontal =
        abs(-2.0 * lumaLeft   + lumaLeftCorners)  +
        abs(-2.0 * lumaCenter + lumaDownUp) * 2.0 +
        abs(-2.0 * lumaRight  + lumaRightCorners);

    float edgeVertical =
        abs(-2.0 * lumaUp     + lumaUpCorners)    +
        abs(-2.0 * lumaCenter + lumaLeftRight) * 2.0 +
        abs(-2.0 * lumaDown   + lumaDownCorners);

    bool isHorizontal = edgeHorizontal >= edgeVertical;

    // Pick the side of the edge with the steepest gradient
    float luma1 = isHorizontal ? lumaDown : lumaLeft;
    float luma2 = isHorizontal ? lumaUp   : lumaRight;
    float gradient1 = luma1 - lumaCenter;
    float gradient2 = luma2 - lumaCenter;

    bool  is1Steepest    = abs(gradient1) >= abs(gradient2);
    float gradientScaled = 0.25 * max(abs(gradient1), abs(gradient2));

    float stepLength = isHorizontal ? texel.y : texel.x;
    float lumaLocalAverage;

    if (is1Steepest)
    {
        stepLength = -stepLength;
        lumaLocalAverage = 0.5 * (luma1 + lumaCenter);
    }
    else
    {
        lumaLocalAverage = 0.5 * (luma2 + lumaCenter);
    }

    // Move to the edge boundary (half a pixel across)
    vec2 currentUV = vUV;
    if (isHorizontal) currentUV.y += stepLength * 0.5;
    else              currentUV.x += stepLength * 0.5;

    // Walk along the edge in both directions to find its ends
    vec2 offset = isHorizontal ? vec2(texel.x, 0.0) : vec2(0.0, texel.y);
    vec2 uv1 = currentUV - offset;
    vec2 uv2 = currentUV + offset;

    float lumaEnd1 = Luma(Sample(uv1)) - lumaLocalAverage;
    float lumaEnd2 = Luma(Sample(uv2)) - lumaLocalAverage;

    bool reached1 = abs(lumaEnd1) >= gradientScaled;
    bool reached2 = abs(lumaEnd2) >= gradientScaled;
    bool reachedBoth = reached1 && reached2;

    if (!reached1) uv1 -= offset;
    if (!reached2) uv2 += offset;

    if (!reachedBoth)
    {
        for (int i = 2; i < ITERATIONS; i++)
        {
            if (!reached1) lumaEnd1 = Luma(Sample(uv1)) - lumaLocalAverage;
            if (!reached2) lumaEnd2 = Luma(Sample(uv2)) - lumaLocalAverage;

            reached1 = abs(lumaEnd1) >= gradientScaled;
            reached2 = abs(lumaEnd2) >= gradientScaled;
            reachedBoth = reached1 && reached2;

            if (!reached1) uv1 -= offset * STEP_QUALITY[i];
            if (!reached2) uv2 += offset * STEP_QUALITY[i];

            if (reachedBoth) break;
        }
    }

    // Distances to both ends of the edge
    float distance1 = isHorizontal ? (vUV.x - uv1.x) : (vUV.y - uv1.y);
    float distance2 = isHorizontal ? (uv2.x - vUV.x) : (uv2.y - vUV.y);

    bool  isDirection1  = distance1 < distance2;
    float distanceFinal = min(distance1, distance2);
    float edgeThickness = distance1 + distance2;

    float pixelOffset = -distanceFinal / edgeThickness + 0.5;

    // Only blend if the edge end is consistent with the center pixel
    bool isLumaCenterSmaller = lumaCenter < lumaLocalAverage;
    bool correctVariation = ((isDirection1 ? lumaEnd1 : lumaEnd2) < 0.0) != isLumaCenterSmaller;
    float finalOffset = correctVariation ? pixelOffset : 0.0;

    // Sub-pixel aliasing (thin lines, isolated pixels)
    float lumaAverage = (1.0 / 12.0) *
        (2.0 * (lumaDownUp + lumaLeftRight) + lumaLeftCorners + lumaRightCorners);

    float subPixelOffset1 = clamp(abs(lumaAverage - lumaCenter) / lumaRange, 0.0, 1.0);
    float subPixelOffset2 = (-2.0 * subPixelOffset1 + 3.0) * subPixelOffset1 * subPixelOffset1;
    float subPixelOffsetFinal = subPixelOffset2 * subPixelOffset2 * SUBPIXEL_QUALITY
                              * (geometryEdge ? 1.0 : (1.0 - closeness));

    finalOffset = max(finalOffset, subPixelOffsetFinal);

    // Final sample, shifted perpendicular to the edge (bilinear filtering does the blend)
    vec2 finalUV = vUV;
    if (isHorizontal) finalUV.y += finalOffset * stepLength;
    else              finalUV.x += finalOffset * stepLength;

    FragColor = vec4(Sample(finalUV), 1.0);
}