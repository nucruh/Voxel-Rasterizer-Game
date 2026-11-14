#version 430 core

in vec2 vUV;
out vec4 FragColor;

uniform sampler2D screenTexture;   // GL_LINEAR + CLAMP_TO_EDGE, LDR gamma-space color
uniform sampler2D depthTexture;    // single-sample depth, compare mode OFF
uniform float inverseWidth;
uniform float inverseHeight;
uniform float nearPlane;
uniform float farPlane;

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

void main()
{
    vec2 texel = vec2(inverseWidth, inverseHeight);

    if(true)
    {
    FragColor = vec4(Sample(vUV), 1.0);
    return;
    }

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