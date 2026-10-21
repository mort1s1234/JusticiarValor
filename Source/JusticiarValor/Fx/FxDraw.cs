using System;
using Verse;
using Color = UnityEngine.Color;
using Material = UnityEngine.Material;
using Matrix4x4 = UnityEngine.Matrix4x4;
using Mesh = UnityEngine.Mesh;
using Vector3 = UnityEngine.Vector3;
using Vector4 = UnityEngine.Vector4;

namespace JusticiarValor
{
    public static class FxDraw
    {
        // Seeds for the shaders' variation. A random of its own keeps visuals from consuming the game's Rand.
        private static readonly Random random = new Random();

        public static float Seed() => (float)random.NextDouble();

        // Eltex light is drawn above the map's lighting overlay (as the game's psycast skip flashes are, AltitudeLayer
        // VisEffects): below it, the overlay tints every glow with the sky's colour and darkens it at night. Fog of war
        // still covers it.
        public static float AltitudeGlow => AltitudeLayer.VisEffects.AltitudeFor();

        // Marks on the ground (cracks, shadows) lie with the filth: pawns and buildings cover them, the map's light
        // tints them.
        public static float AltitudeGround => AltitudeLayer.Filth.AltitudeFor();

        // A cell of the packed Piloto sheet (FxSheet) as a sprite. mode: 0 glow in the armor palette, 1 the cell's own
        // colour, 2 the given colour (dust, shadow).
        public static void Sprite(FxDrawContext ctx, Material mat, Vector3 pos, float angle, float sizeX, float sizeZ,
            Vector4 cell, float opacity, float intensity = 1f, int mode = 0, Color? color = null)
        {
            Quad(ctx, mat, pos, angle, sizeX, sizeZ, color ?? JVMath.White, JVMath.V4(opacity, intensity, mode, 0f), cell);
        }

        // One sprite of a sheet cell holding a cols x rows grid of them, numbered from the top left.
        public static Vector4 SheetSprite(Vector4 cell, int index, int cols, int rows)
        {
            int i = ((index % (cols * rows)) + cols * rows) % (cols * rows);
            float w = cell.z / cols;
            float h = cell.w / rows;
            return JVMath.V4(cell.x + i % cols * w, cell.y + (rows - 1 - i / cols) * h, w, h);
        }

        public static float EaseOut(float t)
        {
            float k = 1f - JVMath.Clamp01(t);
            return 1f - k * k * k;
        }

        public static float EaseInOut(float t)
        {
            t = JVMath.Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - (2f - 2f * t) * (2f - 2f * t) * (2f - 2f * t) * 0.5f;
        }

        // Each vendor animation has 64 frames in an 8x8 atlas. Sampling and frame interpolation happen
        // in the shader, so this adds no per-frame materials, textures or particle objects.
        public static void Clip(FxDrawContext ctx, Material mat, Vector3 pos, float angle, float sizeX, float sizeZ,
            float progress, float opacity = 1f, float intensity = 1f, bool loop = false)
        {
            Quad(ctx, mat, pos, angle, sizeX, sizeZ, JVMath.White,
                JVMath.V4(progress, loop ? 1f : 0f, intensity, opacity), JVMath.Zero4);
        }

        public static float LoopPhase(double now, float seconds, float seed = 0f)
            => (float)((now / seconds + seed) % 1.0);

        // A quad of the given size centred on pos, rotated by a RimWorld angle, with the effect parameters.
        public static void Quad(FxDrawContext ctx, Material mat, Vector3 pos, float angle, float sizeX, float sizeZ, Color color, Vector4 p0, Vector4 p1)
        {
            if (mat is null || !ctx.view.Contains(pos.ToIntVec3()))
            {
                return;
            }
            DrawBatchPropertyBlock block = ctx.batch.GetPropertyBlock();
            block.SetVector("_JVParams", p0);
            block.SetVector("_JVParams2", p1);
            ctx.batch.DrawMesh(MeshPool.plane10, JVMath.TRS(pos, angle, sizeX, sizeZ), mat, 0, color, false, block);
        }

        public static void Mesh(FxDrawContext ctx, Mesh mesh, Material mat, Matrix4x4 matrix, Color color, Vector4 p0, Vector4 p1)
        {
            if (mat is null || mesh is null)
            {
                return;
            }
            DrawBatchPropertyBlock block = ctx.batch.GetPropertyBlock();
            block.SetVector("_JVParams", p0);
            block.SetVector("_JVParams2", p1);
            ctx.batch.DrawMesh(mesh, matrix, mat, 0, color, false, block);
        }
    }
}
