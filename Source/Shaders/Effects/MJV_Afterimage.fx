// @name MJV_Afterimage
// @queue 3090
// @texture main
// The only non-vendor shape: a snapshot of the actual wearer, so armor and equipment remain recognizable.
// Piloto's GaleWind_Woosh supplies the accompanying wake. No chromatic split or rainbow cycling.
vec4 MJV_Fx(vec2 uv, vec3 wpos, vec4 col, vec4 p0, vec4 p1)
{
    vec4 pawn = MJV_TEX(MJV_MAINTEX_UV(uv));
    float light = dot(pawn.rgb, vec3(0.30, 0.59, 0.11));
    float coverage = pawn.a * mjv_sat(p0.x) * mjv_sat(p0.x) * col.a;
    // Clear enough to read the wearer's silhouette at a glance, still a ghost over the ground.
    vec3 ink = col.rgb * (0.45 + light * 0.75);
    return vec4(ink * coverage * 1.05, coverage * 0.62);
}
