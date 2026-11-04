#version 430 core

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aUV;
layout(location = 3) in vec4 aTangent;
layout(location = 4) in float windWeight;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
uniform float time;

out vec3 vertexNormal;
out vec2 texCoord;
out vec3 vWorldPos;
out vec3 vTangent;
out float vTangentHandedness;

void main()
{
    vec4 world = model * vec4(aPosition, 1.0);
    world.x += sin(time + world.y) * 0.1 * windWeight;
    vWorldPos = world.xyz;


    gl_Position = projection * view * world;

    mat3 normalMatrix = transpose(inverse(mat3(model)));

    vTangent = aTangent.xyz;
    vTangentHandedness = aTangent.w;

    vertexNormal = aNormal;
    texCoord = aUV;
}