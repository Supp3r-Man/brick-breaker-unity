using System;

namespace BrickBurst.Core
{
    public struct Aabb
    {
        public float MinX;
        public float MinY;
        public float MaxX;
        public float MaxY;

        public Aabb(float minX, float minY, float maxX, float maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public static Aabb FromCenter(float cx, float cy, float width, float height)
        {
            return new Aabb(cx - width / 2f, cy - height / 2f, cx + width / 2f, cy + height / 2f);
        }

        public float CenterX { get { return (MinX + MaxX) / 2f; } }
        public float CenterY { get { return (MinY + MaxY) / 2f; } }
    }

    /// <summary>Result of sweeping a circle along a segment. T is the fraction (0..1) of the segment travelled at first contact.</summary>
    public struct SweepHit
    {
        public bool Found;
        public float T;
        public Vec2 Normal;
    }

    public static class Sweep
    {
        /// <summary>A circle already closer than this to a box (inside by more than Slop) counts as penetrating.</summary>
        public const float Slop = 1e-5f;

        /// <summary>
        /// Sweep a circle (centre start, radius r) along delta against a box. Exact: flat faces use the
        /// box grown by r, corners use a circle of radius r around the corner, so a fast ball cannot tunnel
        /// and a corner hit gets a diagonal normal. A circle that already penetrates the box is reported at
        /// T = 0 (pushed out along the shortest way) but only when it is moving into the box.
        /// </summary>
        public static SweepHit CircleVsAabb(Vec2 start, Vec2 delta, float r, Aabb b)
        {
            SweepHit none = new SweepHit();

            // Closest point of the box to the circle centre.
            float cx = Math.Max(b.MinX, Math.Min(b.MaxX, start.X));
            float cy = Math.Max(b.MinY, Math.Min(b.MaxY, start.Y));
            float dx = start.X - cx, dy = start.Y - cy;
            float distSq = dx * dx + dy * dy;

            if (distSq < (r - Slop) * (r - Slop))
            {
                // Penetrating (or centre inside the box).
                Vec2 n;
                if (distSq > 1e-12f)
                {
                    n = new Vec2(dx, dy).Normalized();
                }
                else
                {
                    // Centre inside: leave through the nearest face.
                    float left = start.X - b.MinX, right = b.MaxX - start.X, down = start.Y - b.MinY, up = b.MaxY - start.Y;
                    float m = Math.Min(Math.Min(left, right), Math.Min(down, up));
                    if (m == left) n = new Vec2(-1f, 0f);
                    else if (m == right) n = new Vec2(1f, 0f);
                    else if (m == down) n = new Vec2(0f, -1f);
                    else n = new Vec2(0f, 1f);
                }
                if (Vec2.Dot(delta, n) >= 0f) return none; // already leaving
                SweepHit hit = new SweepHit();
                hit.Found = true;
                hit.T = 0f;
                hit.Normal = n;
                return hit;
            }

            // Slab test against the box grown by r.
            float exMin = b.MinX - r, exMax = b.MaxX + r, eyMin = b.MinY - r, eyMax = b.MaxY + r;
            float txMin, txMax, tyMin, tyMax;
            if (!Slab(start.X, delta.X, exMin, exMax, out txMin, out txMax)) return none;
            if (!Slab(start.Y, delta.Y, eyMin, eyMax, out tyMin, out tyMax)) return none;

            float tEntry = Math.Max(txMin, tyMin);
            float tExit = Math.Min(txMax, tyMax);
            if (tEntry > tExit || tExit < 0f || tEntry > 1f) return none;

            bool alreadyInsideGrown = tEntry < 0f;
            float t = alreadyInsideGrown ? 0f : tEntry;
            Vec2 q = new Vec2(start.X + delta.X * t, start.Y + delta.Y * t);
            bool xAxis = txMin > tyMin;

            bool flat;
            if (alreadyInsideGrown) flat = false; // not penetrating but inside the grown box: a corner zone
            else if (xAxis) flat = q.Y >= b.MinY && q.Y <= b.MaxY;
            else flat = q.X >= b.MinX && q.X <= b.MaxX;

            if (flat)
            {
                SweepHit hit = new SweepHit();
                hit.Found = true;
                hit.T = t;
                hit.Normal = xAxis ? new Vec2(delta.X > 0f ? -1f : 1f, 0f) : new Vec2(0f, delta.Y > 0f ? -1f : 1f);
                return hit;
            }

            // Corner: sweep against a circle of radius r around the nearest corner.
            float cornerX = q.X < b.MinX ? b.MinX : (q.X > b.MaxX ? b.MaxX : (delta.X > 0f ? b.MinX : b.MaxX));
            float cornerY = q.Y < b.MinY ? b.MinY : (q.Y > b.MaxY ? b.MaxY : (delta.Y > 0f ? b.MinY : b.MaxY));
            return RayVsCircle(start, delta, new Vec2(cornerX, cornerY), r);
        }

        /// <summary>Intersect the segment start + t*delta (t in [0,1]) with a circle; first entry only.</summary>
        public static SweepHit RayVsCircle(Vec2 start, Vec2 delta, Vec2 center, float r)
        {
            SweepHit none = new SweepHit();
            Vec2 m = start - center;
            float a = Vec2.Dot(delta, delta);
            if (a < 1e-12f) return none;
            float bq = Vec2.Dot(m, delta);
            float c = Vec2.Dot(m, m) - r * r;
            if (c < 0f && bq >= 0f) return none; // inside and leaving
            if (c > 0f && bq > 0f) return none;  // outside and moving away
            float disc = bq * bq - a * c;
            if (disc < 0f) return none;
            float t = (-bq - (float)Math.Sqrt(disc)) / a;
            if (t < 0f) t = 0f;
            if (t > 1f) return none;
            Vec2 p = new Vec2(start.X + delta.X * t, start.Y + delta.Y * t);
            Vec2 normal = (p - center).Normalized();
            // A tangent graze (moving along the surface, not into it) is not a hit.
            if (Vec2.Dot(delta, normal) >= 0f) return none;
            SweepHit hit = new SweepHit();
            hit.Found = true;
            hit.T = t;
            hit.Normal = normal;
            return hit;
        }

        private static bool Slab(float s, float d, float min, float max, out float tMin, out float tMax)
        {
            if (Math.Abs(d) < 1e-12f)
            {
                tMin = float.NegativeInfinity;
                tMax = float.PositiveInfinity;
                return s >= min && s <= max;
            }
            float t1 = (min - s) / d, t2 = (max - s) / d;
            tMin = Math.Min(t1, t2);
            tMax = Math.Max(t1, t2);
            return true;
        }
    }
}
