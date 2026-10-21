using System;
using Color = UnityEngine.Color;
using Matrix4x4 = UnityEngine.Matrix4x4;
using Vector3 = UnityEngine.Vector3;
using Vector4 = UnityEngine.Vector4;

namespace JusticiarValor
{
    // Vector, colour and matrix helpers that only construct RimWorld's boundary types and read their fields.
    // On RimWorld 1.7 these types come from the Unity compatibility layer (UnityStubs), whose operators and helpers
    // are not used (see BannedSymbols.txt); the same code builds unchanged for 1.6.
    // Angles follow RimWorld: degrees, 0 = north (+z), increasing clockwise (90 = east, +x).
    public static class JVMath
    {
        public const float Deg2Rad = MathF.PI / 180f;
        public const float Rad2Deg = 180f / MathF.PI;

        public static Vector3 Add(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);

        public static Vector3 Sub(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);

        public static Vector3 Mul(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);

        public static Vector3 WithY(Vector3 a, float y) => new Vector3(a.x, y, a.z);

        public static Vector3 Offset(Vector3 a, float dx, float dz) => new Vector3(a.x + dx, a.y, a.z + dz);

        // Horizontal unit vector of a RimWorld angle.
        public static Vector3 Direction(float angle) => new Vector3(MathF.Sin(angle * Deg2Rad), 0f, MathF.Cos(angle * Deg2Rad));

        public static Vector3 AlongAngle(Vector3 from, float angle, float distance)
        {
            return new Vector3(from.x + MathF.Sin(angle * Deg2Rad) * distance, from.y, from.z + MathF.Cos(angle * Deg2Rad) * distance);
        }

        // RimWorld angle of the horizontal direction from a to b, in [0, 360).
        public static float AngleFlat(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x;
            float dz = to.z - from.z;
            if (dx == 0f && dz == 0f)
            {
                return 0f;
            }
            float angle = MathF.Atan2(dx, dz) * Rad2Deg;
            return angle < 0f ? angle + 360f : angle;
        }

        public static float DistanceFlat(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x;
            float dz = b.z - a.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        public static float DistanceFlatSquared(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x;
            float dz = b.z - a.z;
            return dx * dx + dz * dz;
        }

        // RimWorld angle -> the shader's uv-space angle (radians, counter-clockwise from +x, as atan2(v, u)).
        public static float ToUvRadians(float angle) => (90f - angle) * Deg2Rad;

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        // Unity's clamped lerp: a + (b - a) * t with t in [0, 1].
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static float InverseLerp(float a, float b, float v) => a == b ? 0f : Clamp01((v - a) / (b - a));

        public static Color Color(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Clamp01(t);
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        }

        public static readonly Color White = new Color(1f, 1f, 1f, 1f);

        public static Vector4 V4(float x, float y, float z, float w) => new Vector4(x, y, z, w);

        public static readonly Vector4 Zero4 = new Vector4(0f, 0f, 0f, 0f);

        // Translation * rotation about +y * scale, laid out in Unity's convention (translation in m03, m13, m23), as
        // Matrix4x4.TRS(pos, Quaternion.AngleAxis(angle, Vector3.up), new Vector3(scaleX, 1, scaleZ)) builds it.
        public static Matrix4x4 TRS(Vector3 pos, float angle, float scaleX, float scaleZ)
        {
            float rad = angle * Deg2Rad;
            float c = MathF.Cos(rad);
            float s = MathF.Sin(rad);
            var m = new Matrix4x4();
            m.m00 = c * scaleX;
            m.m01 = 0f;
            m.m02 = s * scaleZ;
            m.m03 = pos.x;
            m.m10 = 0f;
            m.m11 = 1f;
            m.m12 = 0f;
            m.m13 = pos.y;
            m.m20 = -s * scaleX;
            m.m21 = 0f;
            m.m22 = c * scaleZ;
            m.m23 = pos.z;
            m.m30 = 0f;
            m.m31 = 0f;
            m.m32 = 0f;
            m.m33 = 1f;
            return m;
        }
    }
}
