#version 430 core

in vec2 texCoord;

uniform sampler2D atlas;

void main()
{
    float alpha = texture(atlas, texCoord).a;

    if(alpha < 0.5)
        discard;
}