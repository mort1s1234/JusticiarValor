// @name MJV_PsyOrb
// @queue 3110
// @texture main
// Original Piloto animation; material selects the atlas, FxDraw.Clip supplies playback parameters.
vec4 MJV_Fx(vec2 uv, vec3 wpos, vec4 col, vec4 p0, vec4 p1)
{
    return mjv_clip(uv, col, p0);
}
