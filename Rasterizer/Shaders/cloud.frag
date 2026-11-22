#version 430 core

in vec3 vWorldNormal;
in vec3 vWorldPos;

uniform vec3 lightDir;
uniform vec3 uCamPos;
uniform float time;

out vec4 FragColor;

// --- same as main.frag ---
vec3 ApplyAtmosphere(vec3 color, vec3 viewDir, float depth)
{
	vec3 sunDir = normalize(-lightDir);
	vec3 up = vec3(0.0, 1.0, 0.0);

	float sunHeight = dot(sunDir, up);
	float sunset = 1.0 - smoothstep(0.0, 0.35, sunHeight);

	vec3 rayleighColor = vec3(0.20, 0.42, 0.85);
	vec3 sunsetColor = vec3(1.0, 0.38, 0.12);
	vec3 atmosphereColor = mix(rayleighColor, sunsetColor, sunset);

	float cosTheta = dot(viewDir, sunDir);
	float rayleighPhase = 0.75 * (1.0 + cosTheta * cosTheta);

	float horizon = 1.0 - abs(dot(viewDir, up));
	horizon = pow(horizon, 2.0);

	float opticalDepth = 1.0 - exp(-depth * 0.0025);

	float scattering = opticalDepth * (0.12 + horizon * 0.65) * rayleighPhase;
	scattering = clamp(scattering, 0.0, 0.65);

	return mix(color, atmosphereColor, scattering);
}

// --- blocky noise: no interpolation, one random value per grid cell ---
float hash(vec3 p)
{
	p = fract(p * 0.3183099 + vec3(0.1, 0.2, 0.3));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float blockNoise(vec3 p, float cell)
{
	return hash(floor(p / cell));
}

// three octaves of blocks: big, medium, small
float blockyFbm(vec3 p)
{
	float v = 0.0;
	v += 0.50 * blockNoise(p, 16.0);
	v += 0.30 * blockNoise(p + 31.7, 8.0);
	v += 0.20 * blockNoise(p + 73.1, 4.0);
	return v;
}

void main()
{
	vec3 N = normalize(vWorldNormal);
	vec3 sunDir = normalize(-lightDir);
	vec3 viewDir = normalize(vWorldPos - uCamPos);   // camera -> fragment, like main.frag
	float depth = length(vWorldPos - uCamPos);
	float sunHeight = dot(sunDir, vec3(0.0, 1.0, 0.0));
	// Keep a small amount of ambient light at night, while making the
	// directional contribution follow the sun's elevation.
	float sunIntensity = mix(0.08, 1.0, smoothstep(-0.2, 0.12, sunHeight)) * 1.5;

	// nudge the sample point slightly inside the box so faces lying exactly on a
	// grid line don't flicker between two cells
	vec3 samplePos = vWorldPos - N * 0.01;
	//samplePos += vec3(time * 0.8, 0.0, time * 0.3);   // drift, in world units/sec

	float density = blockyFbm(samplePos);

	// lighting
	float ndl = dot(N, sunDir);
	float wrap = clamp((ndl + 0.6) / 1.6, 0.0, 1.0);
	float underside = mix(0.62, 1.0, N.y * 0.5 + 0.5);

	float light = 0.12 + (wrap * underside + (density - 0.5) * 0.35) * sunIntensity;

	vec3 shadowColor = vec3(0.50, 0.57, 0.68);
	vec3 litColor    = vec3(1.00, 0.98, 0.95);
	vec3 color = mix(shadowColor, litColor, clamp(light, 0.0, 1.0));

	// silver lining toward the sun
	float sunView = pow(max(dot(viewDir, sunDir), 0.0), 6.0);
	color += vec3(1.0, 0.95, 0.85) * sunView * 0.25 * sunIntensity;

	// same atmosphere as the terrain
	color = ApplyAtmosphere(color, viewDir, depth);

	// alpha
	float facing = abs(dot(N, viewDir));
	float edgeFade = smoothstep(0.0, 0.45, facing);
	float distFade = 1.0 - smoothstep(600.0, 1400.0, depth);

	float alpha = mix(0.55, 0.92, density) * mix(0.5, 1.0, edgeFade) * distFade;

	FragColor = vec4(color, alpha);
}