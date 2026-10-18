// Dependency-free test runner for the engine-free game rules (Assets/Scripts/Core).
// Exit code 0 = all passed, 1 = at least one failed.
using System;
using System.Collections.Generic;
using BrickBurst.Core;

namespace LogicTests
{
    public static class Program
    {
        private static int passed;
        private static int failed;

        private static void Run(string name, Action body)
        {
            try
            {
                body();
                passed++;
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine("FAIL " + name + ": " + e.Message);
            }
        }

        private static void True(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Eq<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(what + ": expected " + expected + " but got " + actual);
        }

        private static void Near(float expected, float actual, float eps, string what)
        {
            if (Math.Abs(expected - actual) > eps)
                throw new Exception(what + ": expected " + expected + " but got " + actual);
        }

        // ---- helpers ----

        private const float Dt = Game.FixedDt;

        private static Brick MakeBrick(Aabb box, int hp, BrickKind kind)
        {
            Brick b = new Brick();
            b.Box = box;
            b.Hp = hp;
            b.MaxHp = hp;
            b.Kind = kind;
            return b;
        }

        /// <summary>A game with no bricks except a tough anchor in the top-left corner, so the level never clears by accident.</summary>
        private static Game Arena(int seed)
        {
            Game g = new Game(seed);
            g.Bricks.Clear();
            g.Bricks.Add(MakeBrick(new Aabb(0.6f, 10.9f, 1.2f, 11.4f), 100000, BrickKind.Normal));
            return g;
        }

        private static void StepN(Game g, int n, float dt)
        {
            for (int i = 0; i < n; i++) g.Step(dt);
        }

        private static bool HasEvent(Game g, SimEvent e)
        {
            return g.Events.Contains(e);
        }

        /// <summary>Step until an event shows up (or the budget runs out); returns whether it did.</summary>
        private static bool StepUntil(Game g, SimEvent e, int maxSteps, float dt)
        {
            for (int i = 0; i < maxSteps; i++)
            {
                g.Step(dt);
                if (HasEvent(g, e)) return true;
            }
            return false;
        }

        private static Ball First(Game g)
        {
            if (g.Balls.Count == 0) throw new Exception("no ball");
            return g.Balls[0];
        }

        private static void ForceClear(Game g)
        {
            g.Bricks.Clear();
            g.Bricks.Add(MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 1, BrickKind.Normal));
            g.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
            for (int i = 0; i < 600 && g.Status == GameStatus.Playing; i++) g.Step(Dt);
        }

        private static int CountPlayable(string[] rows)
        {
            int n = 0;
            foreach (string r in rows) foreach (char c in r) if (c != '.') n++;
            return n;
        }

        // Tracks per-step invariants of a long simulation.
        private sealed class Watch
        {
            private readonly Dictionary<Ball, Vec2> last = new Dictionary<Ball, Vec2>();
            public float MaxRatio;           // largest (displacement / allowed displacement)
            public float WorstBrickPenetration;
            public bool OutsideArena;

            public void Before(Game g)
            {
                last.Clear();
                if (g.Status != GameStatus.Playing) return;
                for (int i = 0; i < g.Balls.Count; i++) last[g.Balls[i]] = g.Balls[i].Pos;
            }

            public void After(Game g, float dt, float speedBefore)
            {
                float allowed = Math.Max(speedBefore, g.Speed) * dt * 1.001f + 1e-3f;
                for (int i = 0; i < g.Balls.Count; i++)
                {
                    Ball b = g.Balls[i];
                    Vec2 prev;
                    if (last.TryGetValue(b, out prev))
                    {
                        float ratio = Vec2.Distance(prev, b.Pos) / allowed;
                        if (ratio > MaxRatio) MaxRatio = ratio;
                    }
                    if (g.Status != GameStatus.Playing) continue;
                    if (b.Pos.X < Game.BallRadius - 1e-3f || b.Pos.X > Game.Width - Game.BallRadius + 1e-3f || b.Pos.Y > Game.Height - Game.BallRadius + 1e-3f)
                        OutsideArena = true;
                    for (int k = 0; k < g.Bricks.Count; k++)
                    {
                        if (!g.Bricks[k].Alive) continue;
                        Aabb bx = g.Bricks[k].Box;
                        float cx = Math.Max(bx.MinX, Math.Min(bx.MaxX, b.Pos.X));
                        float cy = Math.Max(bx.MinY, Math.Min(bx.MaxY, b.Pos.Y));
                        float dist = (float)Math.Sqrt((b.Pos.X - cx) * (b.Pos.X - cx) + (b.Pos.Y - cy) * (b.Pos.Y - cy));
                        float pen = Game.BallRadius - dist;
                        if (pen > WorstBrickPenetration) WorstBrickPenetration = pen;
                    }
                }
            }
        }

        private static readonly float[] Aims = { 0.7f, -0.7f, 0.35f, -0.35f, 0.9f, -0.9f, 0.15f, -0.15f };

        /// <summary>Plays a game with a scripted paddle. idle = never move it. Returns simulated seconds.</summary>
        private static float Play(Game g, float maxSeconds, bool idle, Watch watch)
        {
            int paddleHits = 0;
            int steps = (int)(maxSeconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                if (g.Status == GameStatus.Serve) g.Launch();
                if (g.Status == GameStatus.LevelClear || g.Status == GameStatus.GameOver) return i * Dt;

                if (!idle && g.Status == GameStatus.Playing && g.Balls.Count > 0)
                {
                    Ball low = g.Balls[0];
                    for (int k = 1; k < g.Balls.Count; k++) if (g.Balls[k].Pos.Y < low.Pos.Y) low = g.Balls[k];
                    float aim = Aims[paddleHits % Aims.Length];
                    g.SetPaddleTarget(low.Pos.X - aim * g.PaddleWidth / 2f);
                }

                float speedBefore = g.Speed;
                if (watch != null) watch.Before(g);
                g.Step(Dt);
                if (watch != null) watch.After(g, Dt, speedBefore);

                for (int e = 0; e < g.Events.Count; e++) if (g.Events[e] == SimEvent.PaddleHit) paddleHits++;
                g.ClearEvents();
            }
            return maxSeconds;
        }

        public static int Main()
        {
            // ======== Sweep: circle vs box ========
            Aabb box = new Aabb(0f, 0f, 2f, 1f);
            const float r = 0.25f;

            Run("sweep hits the top face with an upward normal at the right time", delegate
            {
                SweepHit h = Sweep.CircleVsAabb(new Vec2(1f, 3f), new Vec2(0f, -4f), r, box);
                True(h.Found, "no hit");
                Near(0.4375f, h.T, 1e-5f, "T");
                Near(0f, h.Normal.X, 1e-6f, "nx");
                Near(1f, h.Normal.Y, 1e-6f, "ny");
            });
            Run("sweep hits each of the four faces with the matching normal", delegate
            {
                SweepHit left = Sweep.CircleVsAabb(new Vec2(-3f, 0.5f), new Vec2(6f, 0f), r, box);
                SweepHit right = Sweep.CircleVsAabb(new Vec2(5f, 0.5f), new Vec2(-6f, 0f), r, box);
                SweepHit below = Sweep.CircleVsAabb(new Vec2(1f, -3f), new Vec2(0f, 6f), r, box);
                True(left.Found && left.Normal.X == -1f, "left");
                True(right.Found && right.Normal.X == 1f, "right");
                True(below.Found && below.Normal.Y == -1f, "below");
            });
            Run("sweep that stops short reports no hit", delegate
            {
                True(!Sweep.CircleVsAabb(new Vec2(1f, 3f), new Vec2(0f, -1f), r, box).Found, "hit although too short");
            });
            Run("sweep moving away reports no hit", delegate
            {
                True(!Sweep.CircleVsAabb(new Vec2(1f, 2f), new Vec2(0f, 5f), r, box).Found, "hit while leaving");
            });
            Run("sweep passing parallel and clear of the box reports no hit", delegate
            {
                True(!Sweep.CircleVsAabb(new Vec2(-3f, 1.4f), new Vec2(10f, 0f), r, box).Found, "grazed although clear");
            });
            Run("sweep skimming just inside the grown edge does hit", delegate
            {
                SweepHit h = Sweep.CircleVsAabb(new Vec2(-3f, 1.2f), new Vec2(10f, 0f), r, box);
                True(h.Found, "missed the rounded corner");
                True(h.Normal.X < 0f && h.Normal.Y > 0f, "corner normal should point up-left: " + h.Normal);
                Near(1f, h.Normal.Length, 1e-4f, "unit normal");
            });
            Run("corner hit has a diagonal unit normal and the right contact time", delegate
            {
                // Aimed straight at the top-left corner (0,1) along the diagonal.
                SweepHit h = Sweep.CircleVsAabb(new Vec2(-1f, 2f), new Vec2(2f, -2f), r, box);
                True(h.Found, "no hit");
                Near((float)((Math.Sqrt(2) - r) / (2 * Math.Sqrt(2))), h.T, 1e-4f, "T");
                Near(-0.7071f, h.Normal.X, 1e-3f, "nx");
                Near(0.7071f, h.Normal.Y, 1e-3f, "ny");
            });
            Run("a very fast sweep cannot tunnel through a thin box", delegate
            {
                Aabb thin = new Aabb(5f, 0f, 5.01f, 10f);
                SweepHit h = Sweep.CircleVsAabb(new Vec2(0f, 5f), new Vec2(1000f, 0f), r, thin);
                True(h.Found, "tunnelled through");
                Near(-1f, h.Normal.X, 1e-6f, "normal");
                True(h.T < 0.01f, "contact time too late");
            });
            Run("a circle penetrating the box is pushed out the nearest way, only if moving in", delegate
            {
                SweepHit into = Sweep.CircleVsAabb(new Vec2(1f, 0.9f), new Vec2(0f, -1f), r, box);
                True(into.Found && into.T == 0f && into.Normal.Y == 1f, "centre inside, moving down");
                True(!Sweep.CircleVsAabb(new Vec2(1f, 0.9f), new Vec2(0f, 1f), r, box).Found, "centre inside, moving up reported");
                SweepHit edge = Sweep.CircleVsAabb(new Vec2(1f, 1.1f), new Vec2(0f, -1f), r, box);
                True(edge.Found && edge.T == 0f && edge.Normal.Y == 1f, "overlapping top edge");
            });
            Run("a circle exactly touching a face and moving along it is not blocked", delegate
            {
                True(!Sweep.CircleVsAabb(new Vec2(-1f, 1f + r), new Vec2(5f, 0f), r, box).Found, "blocked while sliding");
            });
            Run("ray vs circle handles zero length, away and inside cases", delegate
            {
                True(!Sweep.RayVsCircle(new Vec2(0f, 0f), Vec2.Zero, new Vec2(5f, 0f), 1f).Found, "zero delta");
                True(!Sweep.RayVsCircle(new Vec2(0f, 0f), new Vec2(-1f, 0f), new Vec2(5f, 0f), 1f).Found, "moving away");
                SweepHit h = Sweep.RayVsCircle(new Vec2(0f, 0f), new Vec2(10f, 0f), new Vec2(5f, 0f), 1f);
                True(h.Found, "head-on");
                Near(0.4f, h.T, 1e-5f, "T");
            });

            // ======== Levels and the brick grid ========
            Run("level 1 builds its 40 bricks, laid out inside the arena without overlaps", delegate
            {
                Game g = new Game(1);
                Eq(40, g.Bricks.Count, "brick count");
                for (int i = 0; i < g.Bricks.Count; i++)
                {
                    Aabb a = g.Bricks[i].Box;
                    True(a.MinX >= BrickGrid.SideMargin - 1e-4f && a.MaxX <= Game.Width - BrickGrid.SideMargin + 1e-4f, "brick " + i + " outside side margins");
                    True(a.MaxY <= BrickGrid.TopY + 1e-4f, "brick " + i + " above the top line");
                    for (int k = i + 1; k < g.Bricks.Count; k++)
                    {
                        Aabb c = g.Bricks[k].Box;
                        bool overlap = a.MinX < c.MaxX && a.MaxX > c.MinX && a.MinY < c.MaxY && a.MaxY > c.MinY;
                        True(!overlap, "bricks " + i + " and " + k + " overlap");
                    }
                }
            });
            Run("every level definition is valid and leaves space above the paddle", delegate
            {
                for (int level = 0; level < Levels.Count * 2; level++)
                {
                    List<Brick> bricks = BrickGrid.Build(Levels.Rows(level), Levels.HpBonus(level));
                    Eq(CountPlayable(Levels.Rows(level)), bricks.Count, "bricks in level index " + level);
                    foreach (Brick b in bricks)
                    {
                        True(b.Box.MinY > 5f, "level " + level + " reaches down to the paddle zone");
                        True(b.Hp >= 1 && b.Hp <= Levels.MaxHp, "hp out of range");
                    }
                }
            });
            Run("level layouts repeat every Count levels", delegate
            {
                Eq(Levels.Rows(0), Levels.Rows(Levels.Count), "level index Count");
                Eq(Levels.Rows(1), Levels.Rows(2 * Levels.Count + 1), "level index 2*Count+1");
            });
            Run("bricks are separated by the configured gap so a ball cannot slip into a seam", delegate
            {
                List<Brick> bricks = BrickGrid.Build(new[] { "1111111111", "1111111111" }, 0);
                Near(BrickGrid.Gap, bricks[1].Box.MinX - bricks[0].Box.MaxX, 1e-4f, "column gap");
                Near(BrickGrid.Gap, bricks[0].Box.MinY - bricks[10].Box.MaxY, 1e-4f, "row gap");
                True(BrickGrid.Gap < Game.BallRadius * 2f, "gap wide enough for the ball");
            });
            Run("grid rejects a short row and an unknown character", delegate
            {
                bool shortRow = false, badChar = false;
                try { BrickGrid.Build(new[] { "111" }, 0); } catch (ArgumentException) { shortRow = true; }
                try { BrickGrid.Build(new[] { "11111x1111" }, 0); } catch (ArgumentException) { badChar = true; }
                True(shortRow, "short row accepted");
                True(badChar, "unknown character accepted");
            });
            Run("hit point bonus applies to plain bricks only and is capped", delegate
            {
                Eq(0, Levels.HpBonus(Levels.Count - 1), "last lap-one level");
                Eq(1, Levels.HpBonus(Levels.Count), "first lap-two level");
                List<Brick> bricks = BrickGrid.Build(new[] { "123WMS....", "..........", "..........", "..........", ".........." }, 1);
                Eq(2, bricks[0].Hp, "1 + bonus");
                Eq(3, bricks[1].Hp, "2 + bonus");
                Eq(3, bricks[2].Hp, "3 + bonus capped");
                for (int i = 3; i < 6; i++) Eq(1, bricks[i].Hp, "power brick " + i);
                Eq(BrickKind.Wide, bricks[3].Kind, "W");
                Eq(BrickKind.Multi, bricks[4].Kind, "M");
                Eq(BrickKind.Slow, bricks[5].Kind, "S");
            });
            Run("brick value is ten per starting hit point", delegate
            {
                Brick b = MakeBrick(new Aabb(0, 0, 1, 1), 3, BrickKind.Normal);
                b.Hp = 1;
                Eq(30, b.Value, "value of a damaged 3-hp brick");
            });

            // ======== Serve, launch, paddle ========
            Run("a new game starts in serve with the ball resting on the paddle", delegate
            {
                Game g = new Game(1);
                Eq(GameStatus.Serve, g.Status, "status");
                Eq(Game.StartLives, g.Lives, "lives");
                Eq(1, g.Level, "level");
                Eq(1, g.Balls.Count, "balls");
                Near(g.PaddleX, First(g).Pos.X, 1e-5f, "ball x");
                Near(Game.PaddleY + Game.PaddleHeight / 2f + Game.BallRadius, First(g).Pos.Y, 1e-3f, "ball y");
            });
            Run("launch only works in serve and sends the ball upward", delegate
            {
                Game g = new Game(1);
                True(g.Launch(), "launch failed");
                Eq(GameStatus.Playing, g.Status, "status");
                True(First(g).Dir.Y > 0.9f, "not mostly upward: " + First(g).Dir);
                Near(1f, First(g).Dir.Length, 1e-4f, "unit direction");
                True(HasEvent(g, SimEvent.Launch), "no launch event");
                True(!g.Launch(), "second launch accepted");
            });
            Run("the ball follows the paddle while waiting to launch", delegate
            {
                Game g = new Game(1);
                g.SetPaddleTarget(3f);
                StepN(g, 120, Dt);
                Near(3f, g.PaddleX, 1e-3f, "paddle");
                Near(g.PaddleX, First(g).Pos.X, 1e-5f, "ball follows");
            });
            Run("the paddle never moves faster than its speed limit", delegate
            {
                Game g = new Game(1);
                g.SetPaddleTarget(15f);
                float before = g.PaddleX;
                g.Step(Dt);
                True(g.PaddleX - before <= Game.PaddleSpeed * Dt + 1e-5f, "moved " + (g.PaddleX - before));
                True(g.PaddleX > before, "did not move");
            });
            Run("the paddle stays inside the arena for any target", delegate
            {
                Game g = new Game(1);
                g.SetPaddleTarget(-50f);
                StepN(g, 300, Dt);
                Near(Game.BasePaddleWidth / 2f, g.PaddleX, 1e-4f, "left clamp");
                g.SetPaddleTarget(500f);
                StepN(g, 600, Dt);
                Near(Game.Width - Game.BasePaddleWidth / 2f, g.PaddleX, 1e-4f, "right clamp");
            });

            // ======== Walls, paddle bounce, direction rules ========
            Run("a ball bounces off the left wall keeping its speed", delegate
            {
                Game g = Arena(1);
                g.StartRally(new Vec2(1f, 6f), new Vec2(-0.6f, -0.8f));
                True(StepUntil(g, SimEvent.WallHit, 60, Dt), "no wall hit");
                True(First(g).Dir.X > 0f, "still heading into the wall");
                Near(-0.8f, First(g).Dir.Y, 1e-4f, "vertical component untouched");
                Near(1f, First(g).Dir.Length, 1e-4f, "unit direction");
            });
            Run("a ball bounces off the top wall", delegate
            {
                Game g = Arena(1);
                g.StartRally(new Vec2(8f, 11f), new Vec2(0.3f, 0.95f));
                True(StepUntil(g, SimEvent.WallHit, 60, Dt), "no wall hit");
                True(First(g).Dir.Y < 0f, "still heading up");
            });
            Run("paddle bounce angle follows the hit offset", delegate
            {
                float[] offsets = { -0.99f, -0.5f, 0f, 0.5f, 0.99f };
                foreach (float off in offsets)
                {
                    Game g = Arena(1);
                    g.StartRally(new Vec2(g.PaddleX + off * g.PaddleWidth / 2f, 3f), new Vec2(0f, -1f));
                    True(StepUntil(g, SimEvent.PaddleHit, 120, Dt), "no paddle hit at " + off);
                    float expected = (float)Math.Sin(off * Game.MaxBounceAngle);
                    Near(expected, First(g).Dir.X, 0.02f, "dir x at offset " + off);
                    True(First(g).Dir.Y > 0f, "not heading up after the paddle");
                    Near(1f, First(g).Dir.Length, 1e-4f, "unit direction");
                }
            });
            Run("a ball dropped on the paddle edge bounces outward at the maximum angle", delegate
            {
                Game g = Arena(1);
                g.StartRally(new Vec2(g.PaddleX + g.PaddleWidth / 2f + 0.1f, 3f), new Vec2(0f, -1f));
                True(StepUntil(g, SimEvent.PaddleHit, 120, Dt), "corner hit missed");
                True(First(g).Dir.Y > 0f, "not heading up");
                // The offset is clamped, so an edge hit gives exactly the maximum angle, never more.
                Near((float)Math.Sin(Game.MaxBounceAngle), First(g).Dir.X, 0.01f, "dir x at the edge");
            });
            Run("minimum vertical component is enforced for every direction", delegate
            {
                float[] ys = { 0f, 0.001f, -0.001f, 0.2f, -0.2f, 0.25f, -0.9f };
                foreach (float y in ys)
                {
                    float x = (float)Math.Sqrt(1 - y * y);
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        Vec2 d = Game.EnforceMinVertical(new Vec2(sx * x, y));
                        True(Math.Abs(d.Y) >= Game.MinVertical - 1e-6f, "too flat: " + d);
                        True(d.X * sx > 0f, "x sign changed");
                        Near(1f, d.Length, 1e-4f, "unit length for y=" + y);
                        if (y != 0f) True((y > 0f) == (d.Y > 0f), "y sign changed for " + y);
                    }
                }
                Vec2 steep = Game.EnforceMinVertical(new Vec2(0.1f, 0.995f));
                Near(0.995f, steep.Y, 1e-6f, "steep directions are left alone");
            });
            Run("a flat ball cannot rattle sideways forever", delegate
            {
                Game g = Arena(1);
                g.SetPaddleTarget(15f);
                StepN(g, 60, Dt);
                g.StartRally(new Vec2(8f, 6f), new Vec2(1f, 0f));
                bool lost = false;
                for (int i = 0; i < 120 * 60 && !lost; i++)
                {
                    g.Step(Dt);
                    lost = HasEvent(g, SimEvent.BallLost);
                }
                True(lost, "ball still bouncing sideways after a minute");
            });
            Run("speed is preserved through a long rally of bounces", delegate
            {
                Game g = Arena(1);
                g.SetPaddleTarget(15f);
                g.StartRally(new Vec2(8f, 6f), new Vec2(0.7f, 0.7f));
                for (int i = 0; i < 120 * 6 && g.Balls.Count > 0; i++)
                {
                    g.Step(Dt);
                    if (g.Balls.Count > 0) Near(1f, First(g).Dir.Length, 1e-3f, "unit direction at step " + i);
                }
            });

            // ======== Tunnelling ========
            Run("a very fast ball cannot pass through a thin brick", delegate
            {
                Game g = Arena(1);
                Brick target = MakeBrick(new Aabb(7f, 6f, 9f, 6.1f), 5, BrickKind.Normal);
                g.Bricks.Add(target);
                g.StartRally(new Vec2(8f, 2f), new Vec2(0f, 1f));
                g.Speed = 400f;
                True(StepUntil(g, SimEvent.BrickHit, 20, Dt), "passed through the brick");
                Eq(4, target.Hp, "brick damage");
                True(First(g).Dir.Y < 0f, "not reflected");
                True(First(g).Pos.Y < 6f, "ball ended up beyond the brick");
            });
            Run("a huge time step cannot tunnel either", delegate
            {
                Game g = Arena(1);
                Brick target = MakeBrick(new Aabb(7f, 6f, 9f, 6.1f), 5, BrickKind.Normal);
                g.Bricks.Add(target);
                g.StartRally(new Vec2(8f, 2f), new Vec2(0f, 1f));
                g.Step(0.5f); // 9 units in one step
                Eq(4, target.Hp, "brick hit");
                True(First(g).Pos.Y < 6f, "tunnelled");
            });
            Run("a very fast ball cannot pass through the paddle", delegate
            {
                Game g = Arena(1);
                g.StartRally(new Vec2(g.PaddleX, 6f), new Vec2(0f, -1f));
                g.Speed = 500f;
                // 4.2 units per step: the second step crosses the paddle.
                True(StepUntil(g, SimEvent.PaddleHit, 5, Dt), "no paddle hit");
                True(!HasEvent(g, SimEvent.BallLost), "ball fell through");
                True(First(g).Dir.Y > 0f, "not bounced up");
            });
            Run("a very fast ball cannot leave through the side walls or the top", delegate
            {
                Game g = Arena(1);
                g.SetPaddleTarget(15f);
                g.StartRally(new Vec2(2f, 6f), new Vec2(-1f, 0.3f));
                g.Speed = 1000f;
                for (int i = 0; i < 300 && g.Balls.Count > 0; i++)
                {
                    g.Step(1f / 60f);
                    if (g.Balls.Count == 0) break;
                    Vec2 p = First(g).Pos;
                    True(p.X >= 0f && p.X <= Game.Width && p.Y <= Game.Height, "escaped to " + p);
                }
            });

            // ======== Corners ========
            Run("a diagonal hit on a brick corner bounces back diagonally", delegate
            {
                Game g = Arena(1);
                g.Bricks.Add(MakeBrick(new Aabb(5f, 5f, 7f, 5.5f), 3, BrickKind.Normal));
                // Heading down-right straight at the top-left corner (5, 5.5).
                g.StartRally(new Vec2(4f, 6.5f), new Vec2(1f, -1f));
                True(StepUntil(g, SimEvent.BrickHit, 200, Dt), "no hit");
                Near(-0.7071f, First(g).Dir.X, 0.02f, "dir x");
                Near(0.7071f, First(g).Dir.Y, 0.02f, "dir y");
            });
            Run("a ball driven into the inside corner of two bricks hits both and escapes", delegate
            {
                Game g = Arena(1);
                Brick floor = MakeBrick(new Aabb(4f, 6f, 8f, 6.5f), 3, BrickKind.Normal);
                Brick wall = MakeBrick(new Aabb(4f, 6.5f, 4.5f, 8f), 3, BrickKind.Normal);
                g.Bricks.Add(floor);
                g.Bricks.Add(wall);
                g.StartRally(new Vec2(6f, 8f), new Vec2(-1f, -1f));
                for (int i = 0; i < 60; i++) g.Step(Dt);
                Eq(2, floor.Hp, "floor hit once");
                Eq(2, wall.Hp, "wall hit once");
                True(First(g).Dir.X > 0f && First(g).Dir.Y > 0f, "not heading out of the pocket: " + First(g).Dir);
            });
            Run("a ball skimming along the top of a brick row is not trapped", delegate
            {
                Game g = Arena(1);
                g.Bricks.AddRange(BrickGrid.Build(new[] { "1111111111" }, 0));
                float y = BrickGrid.TopY + Game.BallRadius + 0.001f;
                g.StartRally(new Vec2(0.8f, y), new Vec2(1f, 0f));
                Run3(g);
            });

            // ======== Bricks, score, combo, speed ========
            Run("a hit that does not destroy scores 5 and damages the brick", delegate
            {
                Game g = Arena(1);
                Brick b = MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 2, BrickKind.Normal);
                g.Bricks.Add(b);
                g.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
                True(StepUntil(g, SimEvent.BrickHit, 200, Dt), "no brick hit");
                Eq(1, b.Hp, "hp");
                Eq(Game.HitPoints, g.Score, "score");
                True(!HasEvent(g, SimEvent.BrickDestroyed), "destroyed too early");
            });
            Run("destroying a brick scores its value times the multiplier", delegate
            {
                Game g = Arena(1);
                Brick b = MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 1, BrickKind.Normal);
                b.MaxHp = 3;
                g.Bricks.Add(b);
                g.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
                g.SetCombo(5); // the hit makes it 6 -> multiplier 3
                True(StepUntil(g, SimEvent.BrickDestroyed, 200, Dt), "not destroyed");
                Eq(30 * 3, g.Score, "score");
            });
            Run("combo multiplier steps up every three hits and caps at five", delegate
            {
                Game g = Arena(1);
                int[] combos = { 0, 1, 2, 3, 5, 6, 11, 12, 100 };
                int[] mult = { 1, 1, 1, 2, 2, 3, 4, 5, 5 };
                for (int i = 0; i < combos.Length; i++)
                {
                    g.SetCombo(combos[i]);
                    Eq(mult[i], g.Multiplier, "multiplier at combo " + combos[i]);
                }
            });
            Run("a paddle bounce resets the combo", delegate
            {
                Game g = Arena(1);
                g.SetCombo(7);
                g.StartRally(new Vec2(g.PaddleX, 3f), new Vec2(0f, -1f));
                True(StepUntil(g, SimEvent.PaddleHit, 120, Dt), "no paddle hit");
                Eq(0, g.Combo, "combo");
            });
            Run("each brick hit raises the ball speed until the cap", delegate
            {
                Game g = Arena(1);
                Brick b = MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 9, BrickKind.Normal);
                g.Bricks.Add(b);
                float s0 = g.Speed;
                g.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
                True(StepUntil(g, SimEvent.BrickHit, 200, Dt), "no hit");
                Near(s0 + Game.SpeedStep, g.Speed, 1e-5f, "speed after one hit");
            });
            Run("the speed ramp never exceeds the cap and never lowers a faster test speed", delegate
            {
                Game g = Arena(1);
                Brick b = MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 9, BrickKind.Normal);
                g.Bricks.Add(b);
                g.Speed = Game.MaxSpeed - 0.01f;
                g.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
                StepUntil(g, SimEvent.BrickHit, 200, Dt);
                Near(Game.MaxSpeed, g.Speed, 1e-5f, "capped");

                Game fast = Arena(1);
                Brick b2 = MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 9, BrickKind.Normal);
                fast.Bricks.Add(b2);
                fast.Speed = 40f;
                fast.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
                StepUntil(fast, SimEvent.BrickHit, 200, Dt);
                Near(40f, fast.Speed, 1e-5f, "not lowered");
            });
            Run("base speed grows by level and is capped", delegate
            {
                for (int l = 1; l < 60; l++) True(Game.BaseSpeed(l + 1) >= Game.BaseSpeed(l), "fell at " + l);
                Near(Game.BaseSpeedLevel1, Game.BaseSpeed(1), 1e-6f, "level 1");
                Near(Game.MaxSpeed, Game.BaseSpeed(500), 1e-6f, "cap");
            });

            Run("a non-destroying hit is also multiplied by the combo", delegate
            {
                Game g = Arena(1);
                g.Bricks.Add(MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 2, BrickKind.Normal));
                g.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
                g.SetCombo(5); // the hit makes it 6 -> multiplier 3
                True(StepUntil(g, SimEvent.BrickHit, 200, Dt), "no hit");
                Eq(Game.HitPoints * 3, g.Score, "score");
            });
            Run("when two bricks are in reach the nearer one is hit, not the one listed last", delegate
            {
                Game g = Arena(1);
                Brick near = MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 5, BrickKind.Normal);
                Brick far = MakeBrick(new Aabb(7f, 8f, 9f, 8.5f), 5, BrickKind.Normal);
                g.Bricks.Add(near);
                g.Bricks.Add(far);
                g.SetPaddleTarget(15f); // keep the paddle out of the way so the ball falls away after bouncing
                StepN(g, 120, Dt);
                g.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
                g.Speed = 1500f; // 12.5 units in one step: both bricks are inside a single sweep
                g.Step(Dt);
                Eq(4, near.Hp, "near brick");
                Eq(5, far.Hp, "far brick must be untouched");
            });
            Run("a ball keeps travelling after a bounce within the same step", delegate
            {
                Game g = Arena(1);
                g.SetPaddleTarget(15f);
                StepN(g, 60, Dt);
                g.StartRally(new Vec2(8f, 11f), new Vec2(0f, 1f));
                g.Step(0.5f); // 9 units: 0.78 up to the wall, then 8.22 back down
                float expected = Game.Height - Game.BallRadius - (g.Speed * 0.5f - (Game.Height - Game.BallRadius - 11f));
                Near(expected, First(g).Pos.Y, 0.05f, "y after bounce");
            });
            Run("after a bounce the ball is separated from the surface, not resting exactly on it", delegate
            {
                Game g = Arena(1);
                g.StartRally(new Vec2(1f, 6f), new Vec2(-0.6f, -0.8f));
                True(StepUntil(g, SimEvent.WallHit, 60, Dt), "no wall hit");
                True(First(g).Pos.X > Game.BallRadius + 1e-5f, "ball is still touching the wall: x = " + First(g).Pos.X);
            });
            Run("a bounce that ends a step right at contact still leaves the ball separated from the wall", delegate
            {
                Game g = Arena(1);
                // The ball reaches the wall with ~1e-7 of the step left, so nothing carries it away afterwards.
                g.StartRally(new Vec2(Game.BallRadius + 0.0749999f, 6f), new Vec2(-1f, 0f));
                g.Step(Dt);
                True(HasEvent(g, SimEvent.WallHit), "no wall hit");
                True(First(g).Pos.X >= Game.BallRadius + 5e-5f, "ball left resting on the wall: x = " + First(g).Pos.X);
            });
            Run("a ball marked inactive is ignored and dropped", delegate
            {
                Game g = Arena(1);
                Ball dead = new Ball();
                dead.Active = false;
                dead.Pos = new Vec2(8f, 5f);
                dead.Dir = new Vec2(0f, 1f);
                g.StartRally(new Vec2(12f, 9f), new Vec2(0f, 1f));
                g.Balls.Add(dead);
                g.Step(Dt);
                Near(5f, dead.Pos.Y, 1e-6f, "inactive ball moved");
                Eq(1, g.Balls.Count, "inactive ball not removed");
            });
            Run("starting a level directly resets the combo and power-up state", delegate
            {
                Game g = Arena(1);
                g.SetCombo(7);
                g.StartLevel(2);
                Eq(0, g.Combo, "combo");
                Eq(GameStatus.Serve, g.Status, "status");
            });
            Run("the combo is zero once the last ball is gone, including on game over", delegate
            {
                Game g = Arena(1);
                g.SetPaddleTarget(15f);
                StepN(g, 120, Dt);
                for (int life = 0; life < Game.StartLives; life++)
                {
                    g.StartRally(new Vec2(1f, 3f), new Vec2(0f, -1f));
                    g.SetCombo(5);
                    for (int i = 0; i < 600 && g.Status == GameStatus.Playing; i++) g.Step(Dt);
                    Eq(0, g.Combo, "combo after losing life " + (life + 1));
                }
                Eq(GameStatus.GameOver, g.Status, "status");
            });
            Run("the level bonus grows with the level number", delegate
            {
                Game g = Arena(1);
                g.StartLevel(3);
                ForceClear(g);
                Eq(10 + Game.LevelBonus * 3, g.Score, "score after clearing level 3");
            });

            // ======== Power-ups ========
            Run("a slow brick slows the ball for a while and the effect expires", delegate
            {
                Game g = Arena(1);
                g.Bricks.Add(MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 1, BrickKind.Slow));
                g.SetPaddleTarget(15f);
                g.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
                True(StepUntil(g, SimEvent.PowerUp, 200, Dt), "no power-up");
                True(g.SlowTimer > 7.9f, "timer " + g.SlowTimer);
                Near(g.Speed * Game.SlowFactor, g.EffectiveSpeed, 1e-4f, "effective speed");
                float s = g.Speed;
                // Park the paddle under the ball so the slow-down can run its course.
                for (int i = 0; i < 120 * 9 && g.SlowTimer > 0f; i++) { g.SetPaddleTarget(First(g).Pos.X); g.Step(Dt); }
                Near(0f, g.SlowTimer, 1e-6f, "timer expired");
                Near(g.Speed, g.EffectiveSpeed, 1e-6f, "back to full speed");
            });
            Run("a slow ball covers proportionally less distance per step", delegate
            {
                Game fastG = Arena(1);
                Game slowG = Arena(1);
                slowG.Bricks.Add(MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 1, BrickKind.Slow));
                slowG.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
                StepUntil(slowG, SimEvent.PowerUp, 200, Dt);
                float before = First(slowG).Pos.Y;
                slowG.ClearEvents();
                slowG.Step(Dt);
                float moved = Math.Abs(First(slowG).Pos.Y - before);
                Near(slowG.Speed * Game.SlowFactor * Dt, moved, 1e-3f, "distance in one slowed step");
            });
            Run("a wide brick widens the paddle, keeps it inside the arena, and expires", delegate
            {
                Game g = Arena(1);
                g.SetPaddleTarget(0f);
                StepN(g, 120, Dt);
                Near(Game.BasePaddleWidth / 2f, g.PaddleX, 1e-3f, "paddle at the wall");
                g.Bricks.Add(MakeBrick(new Aabb(2.5f, 6f, 3.5f, 6.5f), 1, BrickKind.Wide));
                g.StartRally(new Vec2(3f, 3f), new Vec2(0f, 1f));
                True(StepUntil(g, SimEvent.PowerUp, 200, Dt), "no power-up");
                Near(Game.BasePaddleWidth * Game.WidePaddleFactor, g.PaddleWidth, 1e-5f, "width");
                True(g.PaddleX >= g.PaddleWidth / 2f - 1e-4f, "paddle sticks out of the left wall");
                g.SetPaddleTarget(500f);
                StepN(g, 120 * 2, Dt);
                True(g.PaddleX <= Game.Width - g.PaddleWidth / 2f + 1e-4f, "paddle sticks out of the right wall");
                for (int i = 0; i < 120 * 13 && g.WideTimer > 0f; i++) g.Step(Dt);
                Near(Game.BasePaddleWidth, g.PaddleWidth, 1e-5f, "width after expiry");
            });
            Run("a multi brick adds two balls with distinct valid directions", delegate
            {
                Game g = Arena(1);
                g.Bricks.Add(MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 1, BrickKind.Multi));
                g.StartRally(new Vec2(8f, 3f), new Vec2(0.1f, 1f));
                True(StepUntil(g, SimEvent.PowerUp, 200, Dt), "no power-up");
                Eq(3, g.Balls.Count, "balls");
                foreach (Ball b in g.Balls)
                {
                    Near(1f, b.Dir.Length, 1e-3f, "unit direction");
                    True(Math.Abs(b.Dir.Y) >= Game.MinVertical - 1e-4f, "too flat");
                }
                True(g.Balls[0].Dir.X != g.Balls[1].Dir.X && g.Balls[1].Dir.X != g.Balls[2].Dir.X && g.Balls[0].Dir.X != g.Balls[2].Dir.X, "directions not distinct");
            });
            Run("multi-ball respects the ball cap", delegate
            {
                Game g = Arena(1);
                g.Bricks.Add(MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 1, BrickKind.Multi));
                g.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
                // One short of the cap, so only one of the two new balls fits.
                for (int i = 0; i < Game.MaxBalls - 2; i++)
                {
                    Ball extra = new Ball();
                    extra.Active = true;
                    extra.Pos = new Vec2(12f, 9f + i * 0.5f);
                    extra.Dir = new Vec2(0.3f, 0.95f).Normalized();
                    g.Balls.Add(extra);
                }
                StepUntil(g, SimEvent.PowerUp, 200, Dt);
                True(g.Balls.Count <= Game.MaxBalls, "over the cap: " + g.Balls.Count);
                Eq(Game.MaxBalls, g.Balls.Count, "filled up to the cap");
            });
            Run("a plain brick triggers no power-up", delegate
            {
                Game g = Arena(1);
                g.Bricks.Add(MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 1, BrickKind.Normal));
                g.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
                True(StepUntil(g, SimEvent.BrickDestroyed, 200, Dt), "not destroyed");
                True(!HasEvent(g, SimEvent.PowerUp), "power-up from a plain brick");
                Near(0f, g.SlowTimer, 1e-6f, "slow");
                Near(0f, g.WideTimer, 1e-6f, "wide");
            });

            // ======== Lives, game over, levels ========
            Run("losing a ball costs a life and returns to serve with a clean slate", delegate
            {
                Game g = Arena(1);
                g.Bricks.Add(MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 1, BrickKind.Slow));
                g.SetPaddleTarget(15f);
                StepN(g, 120, Dt);
                g.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
                True(StepUntil(g, SimEvent.LifeLost, 120 * 30, Dt), "never lost the ball");
                Eq(Game.StartLives - 1, g.Lives, "lives");
                Eq(GameStatus.Serve, g.Status, "status");
                Near(Game.BaseSpeed(1), g.Speed, 1e-6f, "speed reset");
                Near(0f, g.SlowTimer, 1e-6f, "power-up cleared");
                Eq(0, g.Combo, "combo");
                Eq(1, g.Balls.Count, "fresh ball");
                Near(g.PaddleX, First(g).Pos.X, 1e-4f, "ball back on the paddle");
            });
            Run("losing a ball ends the wide paddle effect too", delegate
            {
                Game g = Arena(1);
                g.Bricks.Add(MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 1, BrickKind.Wide));
                g.SetPaddleTarget(15f);
                StepN(g, 120, Dt);
                g.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
                True(StepUntil(g, SimEvent.PowerUp, 200, Dt), "no power-up");
                True(g.WideTimer > 0f, "wide never started");
                True(StepUntil(g, SimEvent.LifeLost, 120 * 30, Dt), "never lost the ball");
                Near(0f, g.WideTimer, 1e-6f, "wide effect survived the lost ball");
                Near(Game.BasePaddleWidth, g.PaddleWidth, 1e-5f, "paddle still wide");
            });
            Run("losing one of several balls costs nothing, losing the last costs a life", delegate
            {
                Game g = Arena(1);
                g.SetPaddleTarget(15f);
                StepN(g, 120, Dt);
                g.StartRally(new Vec2(8f, 0.1f), new Vec2(0f, -1f)); // about to fall out
                Ball keeper = new Ball();
                keeper.Active = true;
                keeper.Pos = new Vec2(8f, 9f);
                keeper.Dir = new Vec2(0f, 1f);
                g.Balls.Add(keeper);
                StepN(g, 30, Dt);
                Eq(Game.StartLives, g.Lives, "lives after losing one ball");
                Eq(1, g.Balls.Count, "balls");
                True(StepUntil(g, SimEvent.LifeLost, 120 * 20, Dt), "never lost the last ball");
                Eq(Game.StartLives - 1, g.Lives, "lives");
            });
            Run("three lost balls end the game and everything goes inert", delegate
            {
                Game g = Arena(1);
                g.SetPaddleTarget(15f);
                StepN(g, 120, Dt);
                for (int life = 0; life < Game.StartLives; life++)
                {
                    g.StartRally(new Vec2(1f, 3f), new Vec2(0f, -1f));
                    for (int i = 0; i < 600 && g.Status == GameStatus.Playing; i++) g.Step(Dt);
                }
                Eq(GameStatus.GameOver, g.Status, "status");
                Eq(0, g.Lives, "lives");
                True(HasEvent(g, SimEvent.GameOver), "no game over event");
                int score = g.Score;
                g.Step(Dt);
                True(!g.Launch(), "launch after game over");
                True(!g.NextLevel(), "next level after game over");
                Eq(score, g.Score, "score moved");
            });
            Run("destroying the last brick clears the level with a bonus", delegate
            {
                Game g = Arena(1);
                g.Bricks.Clear();
                g.Bricks.Add(MakeBrick(new Aabb(7f, 6f, 9f, 6.5f), 1, BrickKind.Normal));
                g.StartRally(new Vec2(8f, 3f), new Vec2(0f, 1f));
                True(StepUntil(g, SimEvent.LevelCleared, 200, Dt), "not cleared");
                Eq(GameStatus.LevelClear, g.Status, "status");
                Eq(10 + Game.LevelBonus * 1, g.Score, "score");
                Eq(0, g.Balls.Count, "balls left");
                int s = g.Score;
                g.Step(Dt);
                Eq(s, g.Score, "inert after clear");
            });
            Run("next level builds the new layout and keeps score and lives", delegate
            {
                Game g = Arena(1);
                ForceClear(g);
                Eq(GameStatus.LevelClear, g.Status, "cleared");
                int score = g.Score, lives = g.Lives;
                True(g.NextLevel(), "next level refused");
                Eq(2, g.Level, "level");
                Eq(CountPlayable(Levels.Rows(1)), g.Bricks.Count, "bricks of level 2");
                Eq(GameStatus.Serve, g.Status, "status");
                Eq(score, g.Score, "score kept");
                Eq(lives, g.Lives, "lives kept");
                Near(Game.BaseSpeed(2), g.Speed, 1e-6f, "level 2 speed");
                True(!g.NextLevel(), "next level accepted outside level clear");
            });
            Run("every third level restores a life, up to the maximum", delegate
            {
                Game g = Arena(1);
                g.StartLevel(3);
                int before = g.Lives;
                ForceClear(g);
                Eq(before + 1, g.Lives, "life at level 3");
                g.StartLevel(6);
                ForceClear(g);
                Eq(Game.MaxLives, g.Lives, "second extra life");
                g.StartLevel(9);
                ForceClear(g);
                Eq(Game.MaxLives, g.Lives, "capped");
            });
            Run("levels past the last repeat with tougher bricks", delegate
            {
                Game g = new Game(1);
                g.StartLevel(Levels.Count + 1);
                int total = 0;
                foreach (Brick b in g.Bricks) total += b.Hp;
                Game first = new Game(1);
                int totalFirst = 0;
                foreach (Brick b in first.Bricks) totalFirst += b.Hp;
                True(total > totalFirst, "second lap not tougher: " + total + " vs " + totalFirst);
            });

            // ======== Determinism and step independence ========
            Run("the same seed and inputs give an identical game", delegate
            {
                Game a = new Game(7), b = new Game(7);
                for (int i = 0; i < 120 * 20; i++)
                {
                    float target = 8f + 6f * (float)Math.Sin(i * 0.013);
                    a.SetPaddleTarget(target);
                    b.SetPaddleTarget(target);
                    if (a.Status == GameStatus.Serve) { a.Launch(); b.Launch(); }
                    a.Step(Dt);
                    b.Step(Dt);
                    a.ClearEvents();
                    b.ClearEvents();
                }
                Eq(a.Score, b.Score, "score");
                Eq(a.Lives, b.Lives, "lives");
                Eq(a.Balls.Count, b.Balls.Count, "balls");
                for (int i = 0; i < a.Balls.Count; i++)
                    True(a.Balls[i].Pos.X == b.Balls[i].Pos.X && a.Balls[i].Pos.Y == b.Balls[i].Pos.Y, "ball " + i + " diverged");
            });
            Run("different seeds launch in different directions", delegate
            {
                Game a = new Game(1), b = new Game(2);
                a.Launch();
                b.Launch();
                True(First(a).Dir.X != First(b).Dir.X, "same launch direction");
            });
            Run("free flight does not depend on the step size", delegate
            {
                Game a = Arena(1), b = Arena(1);
                a.StartRally(new Vec2(4f, 4f), new Vec2(1f, 0.5f));
                b.StartRally(new Vec2(4f, 4f), new Vec2(1f, 0.5f));
                StepN(a, 60, Dt);
                StepN(b, 30, 2f * Dt);
                Near(First(a).Pos.X, First(b).Pos.X, 1e-3f, "x");
                Near(First(a).Pos.Y, First(b).Pos.Y, 1e-3f, "y");
            });
            Run("events can be cleared", delegate
            {
                Game g = new Game(1);
                g.Launch();
                True(g.Events.Count > 0, "no events");
                g.ClearEvents();
                Eq(0, g.Events.Count, "events after clear");
            });

            // ======== Whole-game simulations ========
            Run("a tracking bot clears level 1 and the invariants hold the whole way", delegate
            {
                Game g = new Game(3);
                Watch w = new Watch();
                float t = Play(g, 600f, false, w);
                Console.WriteLine("[info] tracking bot, level 1: " + g.Status + " after " + t.ToString("0.0") + " s, score " + g.Score + ", lives " + g.Lives);
                Eq(GameStatus.LevelClear, g.Status, "level 1 not cleared in " + t + " s (score " + g.Score + ", lives " + g.Lives + ")");
                True(g.Lives > 0, "no lives left");
                True(w.MaxRatio <= 1f, "a ball moved further than its speed allows (ratio " + w.MaxRatio + ")");
                True(w.WorstBrickPenetration < 1e-3f, "ball sank into a brick by " + w.WorstBrickPenetration);
                True(!w.OutsideArena, "ball left the arena");
            });
            Run("a bot that never moves the paddle loses the game", delegate
            {
                Game g = new Game(3);
                Watch w = new Watch();
                float t = Play(g, 1800f, true, w);
                Console.WriteLine("[info] idle paddle: " + g.Status + " after " + t.ToString("0.0") + " s, score " + g.Score + ", bricks left " + g.BricksLeft);
                Eq(GameStatus.GameOver, g.Status, "still alive after " + t + " s (lives " + g.Lives + ")");
                True(w.MaxRatio <= 1f, "a ball moved further than its speed allows (ratio " + w.MaxRatio + ")");
                True(w.WorstBrickPenetration < 1e-3f, "ball sank into a brick by " + w.WorstBrickPenetration);
            });
            Run("the tracking bot also gets through level 2 with a multi-ball and slow brick in play", delegate
            {
                Game g = new Game(5);
                g.StartLevel(2);
                Watch w = new Watch();
                float t = Play(g, 900f, false, w);
                Console.WriteLine("[info] tracking bot, level 2: " + g.Status + " after " + t.ToString("0.0") + " s, score " + g.Score + ", lives " + g.Lives);
                Eq(GameStatus.LevelClear, g.Status, "level 2 not cleared in " + t + " s (lives " + g.Lives + ")");
                True(w.WorstBrickPenetration < 1e-3f, "ball sank into a brick by " + w.WorstBrickPenetration);
            });

            // ======== High score, Rng, Vec2 ========
            Run("high score parse handles junk", delegate
            {
                Eq(0, HighScore.Parse(null), "null");
                Eq(0, HighScore.Parse(""), "empty");
                Eq(0, HighScore.Parse("abc"), "text");
                Eq(0, HighScore.Parse("-5"), "negative");
                Eq(0, HighScore.Parse("99999999999999"), "overflow");
                Eq(1234, HighScore.Parse("1234"), "valid");
            });
            Run("high score updates only on a strictly higher score", delegate
            {
                bool isNew;
                Eq(100, HighScore.Update(100, 100, out isNew), "tie keeps best");
                True(!isNew, "tie flagged as new");
                Eq(150, HighScore.Update(100, 150, out isNew), "higher replaces");
                True(isNew, "higher not flagged");
                Eq(777, HighScore.Parse(HighScore.Format(777)), "round trip");
            });
            Run("rng is deterministic and in range", delegate
            {
                Rng a = new Rng(42), b = new Rng(42);
                for (int i = 0; i < 100; i++) Eq(a.NextUInt(), b.NextUInt(), "value " + i);
                Rng c = new Rng(8);
                for (int i = 0; i < 2000; i++)
                {
                    float f = c.Range(-0.35f, 0.35f);
                    True(f >= -0.35f && f < 0.35f, "out of range " + f);
                }
            });
            Run("rng survives the one seed that mixes to a zero state", delegate
            {
                uint k = 2654435761u, cc = 0x9E3779B9u, inv = k;
                for (int i = 0; i < 6; i++) inv = inv * (2u - k * inv);
                uint badSeed = (0u - cc) * inv;
                True(badSeed * k + cc == 0u, "test setup: seed does not mix to zero");
                Rng rr = new Rng((int)badSeed);
                uint x = rr.NextUInt(), y = rr.NextUInt();
                True(x != 0u && y != 0u && x != y, "generator is stuck at zero");
            });
            Run("vec2 normalises, measures and leaves zero alone", delegate
            {
                Near(5f, new Vec2(3f, 4f).Length, 1e-5f, "length");
                Near(1f, new Vec2(3f, 4f).Normalized().Length, 1e-5f, "normalised");
                Vec2 z = Vec2.Zero.Normalized();
                True(z.X == 0f && z.Y == 0f, "zero changed");
            });

            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        /// <summary>The skimming ball must keep moving (not stall in place) and eventually leave the row.</summary>
        private static void Run3(Game g)
        {
            Vec2 start = g.Balls[0].Pos;
            for (int i = 0; i < 120 * 3; i++) g.Step(Dt);
            if (g.Balls.Count > 0)
            {
                float moved = Vec2.Distance(start, g.Balls[0].Pos);
                True(moved > 1f, "ball stuck near " + g.Balls[0].Pos);
            }
        }
    }
}
