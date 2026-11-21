#version 430 core

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aUV;
layout(location = 3) in vec4 aTangent;
layout(location = 4) in float windWeight;
layout(location = 5) in vec4 aTileRect;
layout(location = 6) in uint aBiomeData;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

out vec3 vertexNormal;
out vec2 texCoord;
out vec3 vWorldPos;
out vec3 vTangent;
out float vTangentHandedness;
out vec4 vTileRect;
out vec2 vBiomeData;
out float vViewDepth;

void main()
{
    vec4 world = model * vec4(aPosition, 1.0);
    vWorldPos = world.xyz;
    vViewDepth = -(view * world).z;

    gl_Position = projection * view * world;

    mat3 normalMatrix = transpose(inverse(mat3(model)));

    vTangent = aTangent.xyz;
    vTangentHandedness = aTangent.w;
    vTileRect = aTileRect;
    vBiomeData = vec2(float(aBiomeData & 255u),
                      float((aBiomeData >> 8u) & 255u)) / 255.0;

    vertexNormal = aNormal;
    texCoord = aUV;
}