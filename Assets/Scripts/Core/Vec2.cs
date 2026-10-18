// Plain C# on purpose: no UnityEngine here, so the game rules can be compiled and
// tested outside the editor (see Tests/LogicTests). Keep this folder free of Unity types.
using System;

namespace BrickBurst.Core
{
    public struct Vec2
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);

        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.X + b.X, a.Y + b.Y); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.X - b.X, a.Y - b.Y); }
        public static Vec2 operator *(Vec2 a, float s) { return new Vec2(a.X * s, a.Y * s); }

        public float SqrLength { get { return X * X + Y * Y; } }
        public float Length { get { return (float)Math.Sqrt(SqrLength); } }

        public Vec2 Normalized()
        {
            float len = Length;
            if (len < 1e-6f) return Zero;
            return new Vec2(X / len, Y / len);
        }

        public static float Dot(Vec2 a, Vec2 b) { return a.X * b.X + a.Y * b.Y; }

        public static float Distance(Vec2 a, Vec2 b) { return (a - b).Length; }

        public override string ToString() { return "(" + X + ", " + Y + ")"; }
    }
}
