#version 430 core

in vec2 texCoord;
in vec4 vTileRect;

uniform sampler2D atlas;

void main()
{
    vec2 uv = texCoord;
    if (vTileRect.z > 0.0)
        uv = vTileRect.xy + fract(texCoord) * vTileRect.zw;

    if (textureLod(atlas, uv, 0.0).a < 0.5)
        discard;
}