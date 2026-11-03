#version 430 core

out vec4 FragColor;

in vec2 vNdc;

uniform mat4 invProjection;
uniform mat4 invView;

vec3 SkyColor(vec3 rd)
{
    float t = clamp(rd.y, 0.0, 1.0);

    vec3 zenith  = vec3(0.31, 0.544, 0.792);
    vec3 horizon = vec3(0.533, 0.741, 0.945);

    vec3 sky = mix(horizon, zenith, pow(t, 0.6));

    float below = clamp(-rd.y, 0.0, 1.0);
    sky = mix(sky, vec3(0.55, 0.60, 0.62), pow(below, 0.4));

    float haze = pow(1.0 - abs(rd.y), 6.0);
    sky += vec3(0.5, 0.45, 0.4) * haze * 0.3;

    return sky;
}

void main()
{
    vec4 viewRay = invProjection * vec4(vNdc, 1.0, 1.0);
    vec3 viewDir = normalize(viewRay.xyz / viewRay.w);

    vec3 rayDir = normalize((invView * vec4(viewDir, 0.0)).xyz);

    FragColor = vec4(SkyColor(rayDir), 1.0);
}