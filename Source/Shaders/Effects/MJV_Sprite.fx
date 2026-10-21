// @name MJV_Sprite
// @queue 3117
// @texture main
// One cell of the packed Piloto texture sheet (pack_sheet.py), for the layers the composite effects place themselves:
// flares, rings, chevrons, dust and debris.
// p0: x opacity, y intensity, z mode (0 glow in the armor palette, 1 the cell's own colour, 2 the material colour: dust,
//     shadow), w unused
// p1: the cell rect (FxSheet, or one sprite of it)
vec4 MJV_Fx(vec2 uv, vec3 wpos, vec4 col, vec4 p0, vec4 p1)
{
    vec4 t = mjv_sheet(uv, p1);
    float opacity = mjv_sat(p0.x);
    vec4 glow = mjv_glow(t.a, p0.y, opacity, col);
    // Colour cells are straight alpha.
    vec4 solid = vec4(t.r * t.a * col.r, t.g * t.a * col.g, t.b * t.a * col.b, t.a) * (opacity * col.a);
    vec4 tinted = vec4(col.r * t.a, col.g * t.a, col.b * t.a, t.a) * (opacity * col.a);
    return p0.z < 0.5 ? glow : (p0.z < 1.5 ? solid : tinted);
}
