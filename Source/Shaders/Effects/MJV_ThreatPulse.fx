// @name MJV_ThreatPulse
// @queue 3095
// @texture main
// The Promachos helmet's scan when it senses new murderous intent, composed from Piloto sheet cells: a sight closes on
// the wearer, a sonar ping (Ring_Shockwave) races outward with a soft glow behind it and reveals the HUD ring
// (Tech_DecalRing2) around the wearer. The chevrons towards each threat and the lock-on on the
// threats themselves are separate quads (FxEffects.ThreatPulseFx).
// uv 0..1 spans the scan's full reach, centred on the wearer.
// p0: x progress 0..1, y seed, z strength (number of threats), w intensity
vec4 MJV_Fx(vec2 uv, vec3 wpos, vec4 col, vec4 p0, vec4 p1)
{
    vec2 q = (uv - vec2(0.5, 0.5)) * 2.0;
    float r = length(q);
    float p = mjv_sat(p0.x);
    float seed = p0.y;

    // The ping: Ring_Shockwave's ring sits at 0.43 of its cell, scaled so it lies on the front.
    float front = mix(0.05, 1.0, mjv_easeOut(p));
    float ring = mjv_sheetMask(vec2(0.5, 0.5) + q * (0.43 / front), MJV_SHEET_PINGRING) * mjv_sq(1.0 - p);

    // A soft glow just behind the front.
    float behind = front - r;
    float wake = step(0.0, behind) * exp(-behind / 0.1) * (1.0 - smoothstep(0.5, 1.0, p)) * (1.0 - smoothstep(0.9, 1.0, r));

    // HUD ring at 0.36 of the reach (Tech_DecalRing2's circle at 0.37 of its cell), shown once the ping has passed it.
    float hudR = 0.36;
    vec2 hudq = mjv_rot(q, seed * 2.0 + p * 0.5);
    float hud = mjv_sheetMask(vec2(0.5, 0.5) + hudq * (0.37 / hudR), MJV_SHEET_HUDRING)
        * smoothstep(hudR - 0.04, hudR + 0.1, front) * (1.0 - smoothstep(0.45, 1.0, p));

    // The sight closes on the wearer: its brackets (0.23 of the cell) snap from 0.3 to 0.17 of the reach.
    float sightR = mix(0.3, 0.17, mjv_easeOut5(p / 0.22));
    vec2 sq = mjv_rot(q, (1.0 - mjv_easeOut(p / 0.25)) * 0.8);
    float sight = mjv_sheetMask(vec2(0.5, 0.5) + sq * (0.23 / sightR), MJV_SHEET_SIGHTINNER)
        * smoothstep(0.0, 0.06, p) * (1.0 - smoothstep(0.5, 0.9, p));

    float light = (ring * 1.15 + wake * 0.3 + hud * 1.6 + sight * 1.1) * p0.z;
    return mjv_glow(light, p0.w, 1.0, col);
}
