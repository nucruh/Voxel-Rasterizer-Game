#version 430 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;
layout(location = 3) in vec4 aTangent;
layout(location = 4) in float windWeight;

uniform mat4 model;
uniform mat4 lightSpace;

out vec2 texCoord;
uniform float time;

void main()
{
    vec4 world = model * vec4(aPosition, 1.0);
    world.x += sin(time + world.y) * 0.1 * windWeight;
    texCoord = aTexCoord;

    gl_Position = lightSpace * world;
}