#version 430 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;
layout(location = 3) in vec4 aTangent;
layout(location = 4) in float windWeight;
layout(location = 5) in vec4 aTileRect;


uniform mat4 model;
uniform mat4 lightSpace;

out vec2 texCoord;
out vec4 vTileRect;

uniform float time;

void main()
{
    vec4 world = model * vec4(aPosition, 1.0);
    float height = clamp(aPosition.y, 0.0, 1.0);

    // Per-instance variation
    float phase = dot(world.xz, vec2(0.1741, 0.1381));

    // Layered wind
    float wind =
        sin(time * 1.2 + phase * 10.0) * 0.7 +
        sin(time * 0.7 + phase * 1.8) * 0.3;

    // Stronger bending towards the tip
    float bend = height * height;

    world.x += wind * 0.35 * bend * windWeight;
    world.z += sin(time * 0.9 + phase + height * 2.0)
             * 0.12 * bend * windWeight;
    texCoord = aTexCoord;
    vTileRect = aTileRect;

    gl_Position = lightSpace * world;
}