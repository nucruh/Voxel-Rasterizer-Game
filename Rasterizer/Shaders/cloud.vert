#version 430 core

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aNormal;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
uniform float time;

out vec3 vWorldNormal;
out vec3 vWorldPos;

void main()
{
	vec4 worldPosition = model * vec4(aPosition, 1.0);
	vWorldNormal = normalize(mat3(model) * aNormal);
	gl_Position = projection * view * (worldPosition + vec4(time * 2.0, 0.0, 0.0, 0.0));

	vWorldPos = worldPosition.xyz;
}
