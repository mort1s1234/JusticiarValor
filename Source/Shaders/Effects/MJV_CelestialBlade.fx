// @name MJV_CelestialBlade
// @queue 3115
// @texture main
// The actual equipped sword: retain its silhouette, fuller and hilt in a translucent silver-green echo. Celestial Pierce
// grows it out of the wielder's blade and drives it into the ground: the part past the ground line is hidden and the
// blade glows where it enters; it flashes on impact and dissolves at the end.
// p0: x opacity, y emission, z buried fraction (from the quad's top edge, the blade's tip), w flash 0..1
// p1: xy source texel size, z dissolve 0..1, w seed; no generated weapon silhouette or animation atlas.
vec4 MJV_Fx(vec2 uv, vec3 wpos, vec4 col, vec4 p0, vec4 p1)
{
    // Coverage as the game's cutout weapon shader shows it: faint pixels (some weapon textures carry a 25% border) are
    // not part of the blade.
    vec4 sword = MJV_TEX(MJV_MAINTEX_UV(uv));
    sword.a = smoothstep(0.35, 0.65, sword.a);
    vec2 dx = vec2(p1.x * 1.5, 0.0);
    vec2 dy = vec2(0.0, p1.y * 1.5);
    float neighbors = smoothstep(0.35, 0.65, MJV_TEX(MJV_MAINTEX_UV(uv + dx)).a) + smoothstep(0.35, 0.65, MJV_TEX(MJV_MAINTEX_UV(uv - dx)).a)
        + smoothstep(0.35, 0.65, MJV_TEX(MJV_MAINTEX_UV(uv + dy)).a) + smoothstep(0.35, 0.65, MJV_TEX(MJV_MAINTEX_UV(uv - dy)).a);
    float edge = abs(sword.a - neighbors * 0.25);
    float light = dot(sword.rgb, vec3(0.30, 0.59, 0.11));
    float opacity = mjv_sat(p0.x) * col.a;

    // Ground line: past it the blade is in the ground.
    float buried = mjv_sat(p0.z);
    float groundV = 1.0 - buried;
    float above = 1.0 - smoothstep(groundV - 0.006, groundV + 0.002, uv.y) * step(0.001, buried);
    float entry = exp(-abs(uv.y - groundV) / 0.015) * step(0.001, buried) * sword.a;

    // Dissolve from scattered points, their edges burning brighter as they eat into the blade.
    float n = mjv_gnoise(uv * vec2(6.0, 13.0) + vec2(p1.w * 17.0, p1.w * 5.0));
    float d = mjv_sat(p1.z) * 1.1;
    float kept = smoothstep(d - 0.05, d, n);
    float burn = (1.0 - smoothstep(d, d + 0.04, n)) * kept * step(0.001, p1.z) * sword.a;

    float flash = mjv_sat(p0.w);
    vec3 body = mjv_sage(0.62 + light * 0.36) * (0.42 + light * 0.55) * sword.a;
    vec3 rim = vec3(0.82, 0.97, 0.92) * edge * 1.8;
    vec3 hot = vec3(0.86, 1.0, 0.95) * (sword.a * flash * 0.6 + burn * 0.9 + entry * 2.0);
    float coverage = (sword.a * 0.68 + edge * 0.12) * (1.0 - flash * 0.25) + entry * 0.4;
    float shown = above * kept;
    return vec4(((body + rim) * p0.y + hot) * opacity * shown * col.rgb, mjv_sat(coverage) * opacity * shown);
}
