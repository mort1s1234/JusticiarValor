// @name MJV_GroundFissure
// @queue 3088
// @texture main
// The ground Celestial Pierce breaks, composed from Piloto sheet cells: radial cracks where the blade went in
// (GroundCracks_Radial) and the fissure it splits along the line (TentacleCrack, whose white core and grey walls are kept
// apart). The opened ground is dark; its seams glow while hot, and the running front of the crack is brightest.
// Drawn at floor altitude, so pawns standing on it cover it.
// Radial mode: uv spans the hub, centred on the blade. Line mode: uv.y runs from the blade (0) to the end of the line (1).
// p0: x mode (0 radial, 1 line), y reveal 0..1 (how far the crack has run), z heat 0..1, w opacity
// p1: x mirror (0/1), y seconds since impact, zw unused
vec4 MJV_Fx(vec2 uv, vec3 wpos, vec4 col, vec4 p0, vec4 p1)
{
    bool isLine = p0.x > 0.5;
    float reveal = p0.y;
    float heat = mjv_sat(p0.z);

    float radialM = mjv_sheetMask(uv, MJV_SHEET_RADIALCRACK);
    float along = uv.y;
    float mirror = p1.x > 0.5 ? -1.0 : 1.0;
    // The fissure narrows and thins out towards the end of the line.
    float lineM = mjv_sheetMask(vec2(along, 0.5 + mirror * (uv.x - 0.5) / mix(1.0, 0.6, along)), MJV_SHEET_FISSURE)
        * (1.0 - smoothstep(0.88, 1.0, along));
    float m = isLine ? lineM : radialM;
    float pos = isLine ? along : length(uv - vec2(0.5, 0.5)) * 2.0;

    float shown = 1.0 - smoothstep(reveal - 0.05, reveal, pos);
    float running = 1.0 - smoothstep(0.95, 1.05, reveal);
    float tip = exp(-mjv_sq((pos - reveal) / 0.06)) * running;

    float walls = smoothstep(0.08, 0.4, m);
    float seam = smoothstep(0.5, 0.95, m);
    float flicker = 0.82 + 0.18 * sin(p1.y * 21.0 + pos * 15.0);
    float light = ((seam * 1.6 * flicker + walls * 0.3) * heat) * shown + tip * walls * 1.8;
    vec4 glow = mjv_glow(light, 1.5, 1.0, col);
    float dark = walls * shown * 0.6;
    return vec4(glow.r, glow.g, glow.b, max(dark, glow.a)) * mjv_sat(p0.w);
}
