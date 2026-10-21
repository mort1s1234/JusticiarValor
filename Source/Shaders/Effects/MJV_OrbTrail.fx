// @name MJV_OrbTrail
// @queue 3108
// @texture main
// A psy-orb's wake, composed from Piloto sheet cells: two ribbons (Trail13, Trail_Starry_2) anchored to the ground the
// orb passed over and drifting slowly back, a hot core line, and sparkles (SparklesPickbook) shed along the path that
// spread and fade as the orb leaves them behind.
// The quad lies along the flight: uv.y 0 at the end of the wake, 1 at the orb; uv.x across.
// p0: x distance flown (world), y wake length (world), z time in seconds, w seed
// p1: x opacity, y wake width (world), z consumed 0..1 (the tail catching up with the orb after it struck), w unused

// One ribbon cell scrolled along the path. The cell is clamped, not tiled: two taps half a period apart, each weighted
// to zero where it wraps, hide the seam.
float mjv_ribbon(float x, float v, vec4 rect)
{
    float sum = 0.0;
    for (int i = 0; i < 2; i++)
    {
        float f = fract(x + float(i) * 0.5);
        sum += mjv_sheetMask(vec2(f, v), rect) * (1.0 - abs(f * 2.0 - 1.0));
    }
    return sum;
}

vec4 MJV_Fx(vec2 uv, vec3 wpos, vec4 col, vec4 p0, vec4 p1)
{
    float a = uv.y;
    float len = max(p0.y, 0.001);
    float width = max(p1.y, 0.001);
    float s = p0.x - (1.0 - a) * len;
    float across = (uv.x - 0.5) * width;
    float time = p0.z;
    float seed = p0.w;

    // From the orb's width down to a thin tail.
    float halfW = mix(0.1, 0.5, pow(a, 0.75));
    float v = (uv.x - 0.5) / halfW;
    float inside = 1.0 - smoothstep(0.8, 1.0, abs(v));
    float ribbons = mjv_ribbon(s / 1.7 - time * 0.3 + seed, 0.5 + v * 0.42, MJV_SHEET_TRAILWISP) * 0.42
        + mjv_ribbon(s / 1.2 + time * 0.15 + seed * 3.0, 0.5 - v * 0.46, MJV_SHEET_TRAILSTARRY) * 0.3;
    float core = exp(-v * v * 9.0) * pow(a, 2.2) * 0.65;
    float tail = smoothstep(0.0, 0.5, a) * smoothstep(mjv_sat(p1.z), mjv_sat(p1.z) + 0.3, a);
    float wake = (ribbons * inside + core) * tail;

    // Sparkles, fixed where they were shed.
    float spacing = 0.27;
    float slot = floor(s / spacing);
    float sparkles = 0.0;
    for (int j = -1; j < 2; j++)
    {
        float id = slot + float(j);
        float h1 = mjv_hash11(id * 1.37 + seed * 71.0);
        float h2 = mjv_hash11(id * 2.71 + seed * 13.0 + 5.0);
        float h3 = mjv_hash11(id * 0.93 + seed * 37.0 + 9.0);
        float shedAt = (id + h1) * spacing;
        float age = (p0.x - shedAt) / len;
        float side = (h2 - 0.5) * width * (0.35 + age * 0.35);
        float size = mix(0.1, 0.22, h3) * (1.0 - age * 0.45);
        vec2 local = vec2((s - shedAt) / size, (across - side) / size) + vec2(0.5, 0.5);
        float life = smoothstep(0.0, 0.06, age) * (1.0 - smoothstep(0.5, 1.0, age)) * step(0.3, h2 * h3 + h1 * 0.5);
        float twinkle = 0.55 + 0.45 * sin(time * (8.0 + h2 * 9.0) + h1 * 31.0);
        sparkles += mjv_sheetSprite(local, MJV_SHEET_SPARKLES, floor(h3 * 8.0), 4.0, 2.0).a * life * twinkle;
    }

    return mjv_glow(wake + sparkles, 1.15, mjv_sat(p1.x), col);
}
