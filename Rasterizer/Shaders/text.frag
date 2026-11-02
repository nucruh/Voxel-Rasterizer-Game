#version 430 core

in vec2 vUV;

uniform sampler2D uTexture;
uniform vec4 uColor;

out vec4 fragColor;

void main()
{
    float mask = texture(uTexture, vUV).a;

    fragColor = vec4(uColor.rgb, uColor.a * mask);
}