// @name MJV_PsychicShield
// @queue 3105
// The Promachos armor's psychic shield: a plain dome of eltex glass seen from above, lit like a curved surface (soft light
// from the upper left, a glint and a sky reflection), with a clean rim, a few soft lines of energy drifting over it, and
// ripples that run over the surface where shots are caught. No lattice or cells.
// uv 0..1 spans the dome's diameter.
// p0: x energy (0..1, above 1 while Invictus overcharges it), y seed, z time in seconds, w absorb flash 0..1
// p1: xy direction of the last impact in uv space (unit), z seconds since that impact, w Invictus overdrive 0..1

vec4 MJV_Fx(vec2 uv, vec3 wpos, vec4 col, vec4 p0, vec4 p1)
{
    vec2 q = (uv - vec2(0.5, 0.5)) * 2.0;
    float r = length(q);
    float aa = mjv_aa(r);
    // No early return outside the dome: fwidth below must run in uniform control flow (HLSL rejects it otherwise).
    float inside = 1.0 - smoothstep(1.0 - aa * 1.5, 1.0, r);
    float time = p0.z;
    float seed = p0.y;
    float energy = p0.x;
    float over = mjv_sat(p1.w);

    // The dome's surface normal, z towards the camera.
    float rr = min(r, 0.999);
    float z = sqrt(1.0 - rr * rr);
    vec3 n = vec3(q.x, q.y, z);
    vec3 lightDir = normalize(vec3(-0.5, 0.6, 0.62));
    float lambert = dot(n, lightDir);
    float lit = smoothstep(-0.4, 0.9, lambert);
    float fres = pow(1.0 - z, 2.4);
    vec3 halfway = normalize(lightDir + vec3(0.0, 0.0, 1.0));
    float glint = pow(mjv_sat(dot(n, halfway)), 260.0);
    float sheen = pow(mjv_sat(dot(n, halfway)), 12.0);
    // A broad band of reflected sky across the far side of the dome.
    vec3 refl = vec3(2.0 * n.x * n.z, 2.0 * n.y * n.z, 2.0 * n.z * n.z - 1.0);
    float sky = pow(mjv_sat(dot(refl, normalize(vec3(0.62, -0.68, 0.2)))), 5.0);

    // Impact: where on the dome the shot was caught, and how far along the surface each point is from it.
    vec2 hitXY = vec2(p1.x, p1.y) * 0.86;
    vec3 hitN = vec3(hitXY.x, hitXY.y, sqrt(max(1.0 - dot(hitXY, hitXY), 0.0)));
    float geo = acos(clamp(dot(n, hitN), -1.0, 1.0));
    float age = p1.z;
    float rippleFront = geo - age * 3.2;
    float ripple = exp(-mjv_sq(rippleFront / 0.06)) * exp(-age * 3.0);
    float hitSpot = exp(-geo * geo * 45.0) * exp(-age * 7.0);
    float hitGlow = exp(-geo * geo * 6.0) * exp(-age * 4.0);

    // Energy drifting over the dome, in the dome's projected coordinates (compressed towards the rim like a sphere
    // seen from above): soft patches of light and a few thin lines where the flow field crosses its middle.
    vec2 sp = q / (0.45 + 0.55 * z);
    float flowSpeed = 1.0 + over * 1.5;
    vec2 fp = sp * 1.1 + vec2(time * 0.18, -time * 0.12) * flowSpeed;
    float warp = mjv_gnoise(fp * 0.7 + vec2(seed * 11.0, 0.0));
    float flow = mjv_gnoise(fp + vec2(warp * 1.6, -warp * 1.2));
    float drift = smoothstep(0.58, 0.85, flow) * (0.25 + 0.75 * fres);
    float vein = (1.0 - smoothstep(0.0, 0.01 + mjv_aa(flow), abs(flow - 0.5))) * (0.35 + 0.65 * over) * (0.3 + 0.7 * fres);

    // Low energy: the dome flickers and thins out in soft patches.
    float weak = 1.0 - smoothstep(0.1, 0.35, energy);
    float flicker = mix(1.0, 0.55 + 0.45 * mjv_noise(vec2(time * 18.0, seed * 7.0)), weak);
    float patches = mix(1.0, smoothstep(0.35, 0.6, mjv_gnoise(sp * 2.2 + vec2(time * 0.6, seed * 3.0)) + energy * 1.5), weak);
    float strength = (0.45 + 0.55 * mjv_sat(energy)) * flicker * patches;
    float flash = mjv_sat(p0.w);

    float rimLine = mjv_band(r - 0.985, 0.005, aa * 1.5) * (0.45 + 0.75 * lit);
    float glass = 0.03 + 0.06 * lit * z + 0.45 * fres * (0.5 + 0.7 * lit);
    vec3 deep = mjv_eltex(0.22);
    vec3 mint = mjv_eltex(0.55);
    vec3 pale = mjv_eltex(0.9);
    vec3 light = deep * glass
        + mint * (drift * 0.1 + vein * 0.35 + rimLine * 0.9 + hitGlow * 0.35 + ripple * 0.9)
        + pale * (glint * 1.2 + sheen * 0.1 + sky * 0.18 + hitSpot * 1.6 + ripple * 0.6)
        + mint * flash * (fres * 1.2 + 0.15);
    light *= strength;

    // Invictus overdrive shifts the dome towards white-violet and makes it hum brighter.
    vec3 overTint = mix(vec3(1.0, 1.0, 1.0), vec3(0.85, 0.72, 1.25), over * 0.8);
    light *= overTint * (1.0 + over * 0.35);
    float alpha = (0.04 + 0.1 * fres + 0.03 * vein + 0.05 * ripple) * strength * inside;
    return mjv_out(light * inside, alpha, col, wpos);
}
