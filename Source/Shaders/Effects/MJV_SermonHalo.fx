// @name MJV_SermonHalo
// @queue 3092
// Imperial missions: the halo at a preaching missionary's feet. Imperial gilt rather than eltex. (A convert gets the
// game's own ideoligion conversion feedback, see Sermon.Conclude.)
// uv 0..1 spans the quad, centred on the pawn.
// p0: x unused, y seed, z time in seconds, w strength 0..1

// Distance from a point of a glyph cell (physical units) to a stroke between two nodes of the glyph's grid, or a large
// number when the glyph has no such stroke.
float mjv_stroke(vec2 p, vec2 a, vec2 b, float present)
{
    vec2 ab = b - a;
    float h = mjv_sat(dot(p - a, ab) / dot(ab, ab));
    return present > 0.5 ? length(p - a - ab * h) : 1.0;
}

// The band of imperial script: 44 glyphs around the circle, each a few strokes on a 3 x 4 grid of nodes, with gaps
// between words. Returns the glyphs' coverage.
float mjv_script(float r, float ang, float turn, float seed, float aa)
{
    float r0 = 0.535;
    float bandW = 0.16;
    float cells = 44.0;
    float u = ((ang + MJV_PI) / MJV_TAU + turn) * cells;
    float id = floor(u);
    float cellArc = MJV_TAU * r / cells;
    vec2 p = vec2((fract(u) - 0.5) * cellArc, r - r0);
    float word = step(0.13, mjv_hash11(id * 0.77 + seed * 29.0));

    // The strokes of the nearest row and of the nearest column, enough for lines this thin.
    float gy = (r - r0) / bandW;
    float row = clamp(floor((gy - 0.14) / 0.24 + 0.5), 0.0, 3.0);
    float gx = fract(u);
    float column = clamp(floor((gx - 0.27) / 0.23 + 0.5), 0.0, 2.0);
    float colX0 = -0.23 * cellArc;
    float colX1 = 0.0;
    float colX2 = 0.23 * cellArc;
    float rowY = (0.14 + row * 0.24) * bandW;
    float colX = (column - 1.0) * 0.23 * cellArc;
    float hs = id * 17.0 + seed * 61.0;
    float d = 1.0;
    d = min(d, mjv_stroke(p, vec2(colX0, rowY), vec2(colX1, rowY), step(0.5, mjv_hash11(hs + row * 2.0))));
    d = min(d, mjv_stroke(p, vec2(colX1, rowY), vec2(colX2, rowY), step(0.55, mjv_hash11(hs + row * 2.0 + 1.0))));
    for (int j = 0; j < 3; j++)
    {
        float fj = float(j);
        float y0 = (0.14 + fj * 0.24) * bandW;
        float on = step(0.42, mjv_hash11(hs + 20.0 + column * 3.0 + fj));
        d = min(d, mjv_stroke(p, vec2(colX, y0), vec2(colX, y0 + 0.24 * bandW), on));
    }
    float strokes = 1.0 - smoothstep(0.0021, 0.0021 + aa, d);
    // A dot beside some glyphs, like a diacritic.
    float dotY = mix(0.25, 0.75, mjv_hash11(hs + 40.0)) * bandW;
    float dotMark = (1.0 - smoothstep(0.004, 0.004 + aa, length(p - vec2(0.36 * cellArc, dotY)))) * step(0.7, mjv_hash11(hs + 41.0));
    float inBand = step(0.0, gy) * step(gy, 1.0);
    return mjv_sat(strokes + dotMark) * word * inBand;
}

// The halo: gilded rings strung with beads, a band of scripture turning slowly between them, soft irregular rays and
// motes of light drifting outward. The middle stays clear so the preacher is not washed out.
vec3 mjv_sermonHalo(vec2 q, vec4 p0, out float alpha)
{
    float seed = p0.y;
    float time = p0.z;
    float strength = mjv_sat(p0.w);
    float r = length(q);
    vec2 dir = q / max(r, 0.0001);
    float ang = MJV_ATAN2(q.y, q.x);
    float aa = mjv_aa(r);
    float breath = 0.9 + 0.1 * sin(time * 1.3 + seed * MJV_TAU);

    float rings = mjv_glowLine(r - 0.47, 0.003, aa, 0.01)
        + 0.5 * mjv_band(r - 0.522, 0.0015, aa) + 0.5 * mjv_band(r - 0.708, 0.0015, aa)
        + mjv_glowLine(r - 0.79, 0.0035, aa, 0.012) + 0.55 * mjv_band(r - 0.84, 0.002, aa);
    // Beads strung between the outer rings, turning against the script.
    float beadU = (ang - time * 0.012 + MJV_PI) / MJV_TAU * 96.0;
    float beadArc = (fract(beadU) - 0.5) * MJV_TAU / 96.0 * r;
    float beads = 1.0 - smoothstep(0.0038, 0.0038 + aa, length(vec2(beadArc, r - 0.815)));
    float ringGlow = exp(-mjv_sq((r - 0.47) / 0.04)) * 0.16 + exp(-mjv_sq((r - 0.81) / 0.06)) * 0.14;

    // Light passes slowly over the script, as over letters cut in gold.
    float shimmer = 0.45 + 0.55 * pow(0.5 + 0.5 * sin(ang * 2.0 - time * 0.7 + seed * 5.0), 3.0);
    float script = mjv_script(r, ang, time * 0.02 + seed, seed, aa) * shimmer;

    // Soft, irregular rays, slowly turning.
    vec2 rayDir = mjv_rot(dir, time * 0.025);
    float rays = pow(mjv_gnoise(rayDir * 5.0 + vec2(seed * 9.0, time * 0.08)), 3.0) * 2.2;
    rays *= smoothstep(0.42, 0.5, r) * (1.0 - smoothstep(0.62, 0.98, r)) * 0.2;

    // Motes drifting outward from the inner ring, twinkling, each on its own slow course.
    vec4 slot = mjv_ringSlot(ang, r, 38.0, seed);
    float phase = fract(slot.z * 3.7 + time * (0.05 + 0.05 * slot.w));
    float moteR = mix(0.46, 0.96, phase);
    float twinkle = 0.55 + 0.45 * sin(time * (2.0 + 3.0 * slot.w) + slot.z * 20.0);
    float motes = exp(-(mjv_sq(slot.x) + mjv_sq(r - moteR)) / 0.000035) * mjv_window(phase, 0.0, 0.15, 0.6, 1.0) * twinkle;

    float floorGlow = exp(-r * r * 2.6) * smoothstep(0.12, 0.42, r) * 0.16;
    float edge = 1.0 - smoothstep(0.93, 1.0, r);

    float k = breath * strength * edge;
    alpha = mjv_sat(floorGlow * 0.3 + ringGlow * 0.12) * strength * edge;
    return (mjv_gilt(0.68) * (rings * 0.85 + beads * 0.6 + script * 0.85)
        + mjv_gilt(0.45) * (ringGlow + rays * 0.8 + floorGlow * 0.7)
        + mjv_gilt(0.95) * motes * 1.8) * k;
}

vec4 MJV_Fx(vec2 uv, vec3 wpos, vec4 col, vec4 p0, vec4 p1)
{
    vec2 q = (uv - vec2(0.5, 0.5)) * 2.0;
    float haloAlpha;
    vec3 halo = mjv_sermonHalo(q, p0, haloAlpha);
    return mjv_out(halo, haloAlpha, col, wpos);
}
