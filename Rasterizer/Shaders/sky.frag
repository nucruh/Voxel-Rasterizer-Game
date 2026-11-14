#version 430 core

out vec4 FragColor;

in vec2 vNdc;

uniform mat4 invProjection;
uniform mat4 invView;
uniform vec3 lightDir;

vec3 SkyColor(vec3 rd)
{
    vec3 sunDir = normalize(-lightDir);

    float sunHeight = dot(sunDir, vec3(0.0, 1.0, 0.0));
    float elevation = clamp(rd.y, 0.0, 1.0);

    // Sky gradient
    vec3 zenith = vec3(0.20, 0.38, 0.65);
    vec3 horizon = vec3(0.55, 0.72, 0.88);

    vec3 sky = mix(
        horizon,
        zenith,
        pow(elevation, 0.55)
    );

    // Sunset coloration
    float sunset = 1.0 - smoothstep(-0.05, 0.35, sunHeight);

    vec3 sunsetColor = vec3(0.95, 0.48, 0.25);

    float horizonFactor = pow(1.0 - elevation, 3.0);

    sky = mix(
        sky,
        sunsetColor,
        sunset * horizonFactor * 0.65
    );

    // Atmospheric haze
    float haze = pow(1.0 - elevation, 5.0);

    vec3 hazeColor = mix(
        vec3(0.65, 0.78, 0.92),
        vec3(1.0, 0.65, 0.42),
        sunset
    );

    sky = mix(sky, hazeColor, haze * 0.3);

    // Sun glow
    float sunDot = max(dot(rd, sunDir), 0.0);

    // Sun disc
    float sunDisc = smoothstep(
        cos(radians(0.55)),
        cos(radians(0.45)),
        sunDot
    );

    // Inner glow
    float sunGlow = pow(sunDot, 256.0);

    // Outer atmospheric halo
    float sunHalo = pow(sunDot, 16.0);

    vec3 sunColor = mix(
        vec3(1.0, 0.82, 0.58),
        vec3(1.0, 0.96, 0.85),
        smoothstep(-0.1, 0.5, sunHeight)
    );

    sky += sunColor * sunDisc * 8.0;
    sky += sunColor * sunGlow * 0.8;
    sky += sunColor * sunHalo * 0.12;

    // Below horizon
    float below = clamp(-rd.y, 0.0, 1.0);

    vec3 groundHorizon = vec3(0.55, 0.60, 0.62);

    sky = mix(
        sky,
        groundHorizon,
        pow(below, 0.4)
    );

    return sky;
}

void main()
{
    vec4 viewRay = invProjection * vec4(vNdc, 1.0, 1.0);
    vec3 viewDir = normalize(viewRay.xyz / viewRay.w);

    vec3 rayDir = normalize((invView * vec4(viewDir, 0.0)).xyz);

    FragColor = vec4(SkyColor(rayDir), 1.0);
}