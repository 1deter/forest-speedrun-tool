using System;

// ------------------------------------------------------------------
// Minimal stand-ins for the handful of UnityEngine types the testable
// logic touches.
//
// WHY A SHIM AND NOT THE REAL ASSEMBLY
// The plugin targets net35 and the only UnityEngine available is either
// the game's own (copyrighted, cannot be in CI) or BepInEx's stub. The
// stub is a net35 assembly whose method bodies are empty - so
// Vector3.Distance would return 0 rather than compute, which is worse
// than useless for testing distance-based logic.
//
// So these are real implementations, and they are deliberately TINY.
// Every member here is arithmetic with a single unambiguous definition,
// which is what makes the shim safe: there is no behaviour to get
// subtly wrong.
//
// THE LIMIT. If this file ever needs Quaternion, Transform, or anything
// with Unity-specific semantics, that is the signal that the logic under
// test is not actually pure and should be refactored - not that the shim
// should grow. Keep it boring.
//
// Only compiled into the test project; the plugin never sees it.
// ------------------------------------------------------------------
namespace UnityEngine
{
    public struct Vector3 : IEquatable<Vector3>
    {
        public float x, y, z;

        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        public static Vector3 zero { get { return new Vector3(0f, 0f, 0f); } }

        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }

        public static Vector3 operator -(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        public static Vector3 operator +(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        public static Vector3 operator *(Vector3 a, float f)
        {
            return new Vector3(a.x * f, a.y * f, a.z * f);
        }

        public static float Distance(Vector3 a, Vector3 b)
        {
            return (a - b).magnitude;
        }

        public bool Equals(Vector3 other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        public override bool Equals(object o) { return o is Vector3 && Equals((Vector3)o); }
        public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode(); }
        public override string ToString() { return "(" + x + ", " + y + ", " + z + ")"; }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float magnitude { get { return (float)Math.Sqrt(x * x + y * y); } }
    }

    public static class Mathf
    {
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float Abs(float v) { return Math.Abs(v); }

        public static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
