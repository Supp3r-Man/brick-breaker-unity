using System;
using System.Collections.Generic;

namespace BrickBurst.Core
{
    public enum GameStatus
    {
        Serve,      // ball sits on the paddle, waiting for Launch()
        Playing,
        LevelClear, // call NextLevel()
        GameOver
    }

    /// <summary>Things that happened during Step(); the engine layer drains them to play sounds.</summary>
    public enum SimEvent
    {
        Launch,
        WallHit,
        PaddleHit,
        BrickHit,
        BrickDestroyed,
        PowerUp,
        BallLost,
        LifeLost,
        LevelCleared,
        GameOver
    }

    public sealed class Ball
    {
        public Vec2 Pos;
        public Vec2 Dir;   // always a unit vector while the ball is moving
        public bool Active;
    }

    /// <summary>
    /// The whole game, minus the engine: a deterministic fixed-step simulation. World units: the arena is
    /// Width x Height with y up; the bottom is open and a ball whose centre falls below it is lost.
    /// Every ball is swept (not teleported) against walls, paddle and bricks, so no speed or step size can tunnel.
    /// </summary>
    public sealed class Game
    {
        public const float Width = 16f;
        public const float Height = 12f;
        public const float BallRadius = 0.22f;

        public const float PaddleY = 1.0f;
        public const float PaddleHeight = 0.35f;
        public const float BasePaddleWidth = 2.4f;
        public const float WidePaddleFactor = 1.5f;
        public const float PaddleSpeed = 28f;
        public const float MaxBounceAngle = 1.0471976f; // 60 degrees from vertical
        public const float MinVertical = 0.25f;         // |Dir.Y| never stays below this after a bounce

        public const float FixedDt = 1f / 120f;
        public const float BaseSpeedLevel1 = 9f;
        public const float SpeedPerLevel = 0.5f;
        public const float MaxSpeed = 18f;
        public const float SpeedStep = 0.05f;

        public const int StartLives = 3;
        public const int MaxLives = 5;
        public const int MaxBalls = 5;
        public const float WideSeconds = 12f;
        public const float SlowSeconds = 8f;
        public const float SlowFactor = 0.65f;
        public const int HitPoints = 5;
        public const int LevelBonus = 100;

        private const int MaxIterations = 12;
        private const float Epsilon = 1e-4f;

        private readonly Rng rng;
        private readonly Aabb[] walls;

        public readonly List<Brick> Bricks = new List<Brick>();
        public readonly List<Ball> Balls = new List<Ball>();
        public readonly List<SimEvent> Events = new List<SimEvent>();

        public GameStatus Status { get; private set; }
        public int Score { get; private set; }
        public int Lives { get; private set; }
        public int Level { get; private set; }
        public int Combo { get; private set; }
        public float Speed { get; set; }
        public float WideTimer { get; private set; }
        public float SlowTimer { get; private set; }
        public float PaddleX { get; private set; }
        public float PaddleTargetX { get; private set; }

        public Game(int seed)
        {
            rng = new Rng(seed);
            // Thick walls on three sides; the bottom is open.
            walls = new Aabb[]
            {
                new Aabb(-20f, -20f, 0f, Height + 20f),
                new Aabb(Width, -20f, Width + 20f, Height + 20f),
                new Aabb(-20f, Height, Width + 20f, Height + 20f)
            };
            NewGame();
        }

        public void NewGame()
        {
            Score = 0;
            Lives = StartLives;
            PaddleX = Width / 2f;
            PaddleTargetX = PaddleX;
            Events.Clear();
            StartLevel(1);
        }

        public static float BaseSpeed(int level)
        {
            return Math.Min(MaxSpeed, BaseSpeedLevel1 + SpeedPerLevel * (level - 1));
        }

        public float PaddleWidth { get { return BasePaddleWidth * (WideTimer > 0f ? WidePaddleFactor : 1f); } }

        public Aabb PaddleBox { get { return Aabb.FromCenter(PaddleX, PaddleY, PaddleWidth, PaddleHeight); } }

        public float EffectiveSpeed { get { return Speed * (SlowTimer > 0f ? SlowFactor : 1f); } }

        /// <summary>Score multiplier from the combo (bricks hit since the ball last touched the paddle): 1 to 5.</summary>
        public int Multiplier { get { return 1 + Math.Min(Combo / 3, 4); } }

        public int BricksLeft
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Bricks.Count; i++) if (Bricks[i].Alive) n++;
                return n;
            }
        }

        public void ClearEvents() { Events.Clear(); }

        public void StartLevel(int level)
        {
            Level = level;
            Bricks.Clear();
            Bricks.AddRange(BrickGrid.Build(Levels.Rows(level - 1), Levels.HpBonus(level - 1)));
            ResetServe();
        }

        private void ResetServe()
        {
            Balls.Clear();
            Ball b = new Ball();
            b.Active = true;
            b.Dir = new Vec2(0f, 1f);
            Balls.Add(b);
            Speed = BaseSpeed(Level);
            WideTimer = 0f;
            SlowTimer = 0f;
            Combo = 0;
            Status = GameStatus.Serve;
            PaddleX = ClampX(PaddleX);
            PaddleTargetX = ClampX(PaddleTargetX);
            Attach();
        }

        private void Attach()
        {
            Balls[0].Pos = new Vec2(PaddleX, PaddleY + PaddleHeight / 2f + BallRadius + Epsilon);
        }

        /// <summary>Test/tool hook: drop straight into play with a single ball at pos moving along dir.</summary>
        public void StartRally(Vec2 pos, Vec2 dir)
        {
            Balls.Clear();
            Ball b = new Ball();
            b.Active = true;
            b.Pos = pos;
            b.Dir = dir.Normalized();
            Balls.Add(b);
            Status = GameStatus.Playing;
        }

        /// <summary>Test/tool hook: set the combo counter directly.</summary>
        public void SetCombo(int combo)
        {
            Combo = Math.Max(0, combo);
        }

        public void SetPaddleTarget(float x)
        {
            PaddleTargetX = ClampX(x);
        }

        private float ClampX(float x)
        {
            float half = PaddleWidth / 2f;
            return Math.Max(half, Math.Min(Width - half, x));
        }

        public bool Launch()
        {
            if (Status != GameStatus.Serve) return false;
            Balls[0].Dir = new Vec2(rng.Range(-0.35f, 0.35f), 1f).Normalized();
            Status = GameStatus.Playing;
            Events.Add(SimEvent.Launch);
            return true;
        }

        public bool NextLevel()
        {
            if (Status != GameStatus.LevelClear) return false;
            StartLevel(Level + 1);
            return true;
        }

        public void Step(float dt)
        {
            if (Status == GameStatus.GameOver || Status == GameStatus.LevelClear) return;

            TickTimers(dt);
            MovePaddle(dt);

            if (Status == GameStatus.Serve)
            {
                Attach();
                return;
            }

            int count = Balls.Count; // balls spawned this step start moving next step
            for (int i = 0; i < count; i++)
                if (Balls[i].Active) MoveBall(Balls[i], dt);
            Balls.RemoveAll(delegate(Ball b) { return !b.Active; });

            if (BricksLeft == 0) ClearLevel();
            else if (Balls.Count == 0) LoseLife();
        }

        private void TickTimers(float dt)
        {
            if (WideTimer > 0f) WideTimer = Math.Max(0f, WideTimer - dt);
            if (SlowTimer > 0f) SlowTimer = Math.Max(0f, SlowTimer - dt);
        }

        private void MovePaddle(float dt)
        {
            float target = ClampX(PaddleTargetX);
            float max = PaddleSpeed * dt;
            float delta = target - PaddleX;
            if (delta > max) delta = max;
            if (delta < -max) delta = -max;
            PaddleX = ClampX(PaddleX + delta);
        }

        private void MoveBall(Ball ball, float dt)
        {
            float remaining = EffectiveSpeed * dt;
            Aabb paddle = PaddleBox;

            for (int iter = 0; iter < MaxIterations && remaining > 1e-7f; iter++)
            {
                Vec2 delta = ball.Dir * remaining;
                SweepHit best = new SweepHit();
                int bestKind = -1, bestIndex = -1;

                for (int w = 0; w < walls.Length; w++) Consider(ball.Pos, delta, walls[w], 0, w, ref best, ref bestKind, ref bestIndex);
                Consider(ball.Pos, delta, paddle, 1, 0, ref best, ref bestKind, ref bestIndex);
                for (int i = 0; i < Bricks.Count; i++)
                    if (Bricks[i].Alive) Consider(ball.Pos, delta, Bricks[i].Box, 2, i, ref best, ref bestKind, ref bestIndex);

                if (!best.Found)
                {
                    ball.Pos = ball.Pos + delta;
                    break;
                }

                ball.Pos = ball.Pos + delta * best.T;
                remaining *= 1f - best.T;
                Respond(ball, best.Normal, bestKind, bestIndex);
                ball.Pos = ball.Pos + best.Normal * Epsilon;
            }

            if (ball.Pos.Y < -BallRadius)
            {
                ball.Active = false;
                Events.Add(SimEvent.BallLost);
            }
        }

        private static void Consider(Vec2 pos, Vec2 delta, Aabb box, int kind, int index, ref SweepHit best, ref int bestKind, ref int bestIndex)
        {
            SweepHit h = Sweep.CircleVsAabb(pos, delta, BallRadius, box);
            if (!h.Found) return;
            if (best.Found && h.T >= best.T) return; // ties keep the earlier candidate
            best = h;
            bestKind = kind;
            bestIndex = index;
        }

        private void Respond(Ball ball, Vec2 n, int kind, int index)
        {
            if (kind == 0)
            {
                Reflect(ball, n);
                Events.Add(SimEvent.WallHit);
            }
            else if (kind == 1)
            {
                if (n.Y > 0.1f && ball.Dir.Y < 0f)
                {
                    float offset = (ball.Pos.X - PaddleX) / (PaddleWidth / 2f);
                    offset = Math.Max(-1f, Math.Min(1f, offset));
                    double a = offset * MaxBounceAngle;
                    ball.Dir = EnforceMinVertical(new Vec2((float)Math.Sin(a), (float)Math.Cos(a)));
                    Combo = 0;
                }
                else
                {
                    Reflect(ball, n); // side or underside of the paddle: plain bounce
                }
                Events.Add(SimEvent.PaddleHit);
            }
            else
            {
                Reflect(ball, n);
                HitBrick(index, ball);
            }
        }

        private static void Reflect(Ball ball, Vec2 n)
        {
            float d = Vec2.Dot(ball.Dir, n);
            Vec2 r = d < 0f ? ball.Dir - n * (2f * d) : ball.Dir;
            ball.Dir = EnforceMinVertical(r.Normalized());
        }

        /// <summary>Keep |Dir.Y| >= MinVertical (same signs), so a ball can never rattle sideways forever.</summary>
        public static Vec2 EnforceMinVertical(Vec2 d)
        {
            if (Math.Abs(d.Y) >= MinVertical) return d;
            float sy = d.Y > 0f ? 1f : -1f;
            float sx = d.X < 0f ? -1f : 1f;
            return new Vec2(sx * (float)Math.Sqrt(1f - MinVertical * MinVertical), sy * MinVertical);
        }

        private void HitBrick(int index, Ball ball)
        {
            Brick brick = Bricks[index];
            brick.Hp--;
            Combo++;
            int mult = Multiplier;
            if (Speed < MaxSpeed) Speed = Math.Min(MaxSpeed, Speed + SpeedStep);

            if (brick.Hp > 0)
            {
                Score += HitPoints * mult;
                Events.Add(SimEvent.BrickHit);
                return;
            }

            Score += brick.Value * mult;
            Events.Add(SimEvent.BrickDestroyed);
            ApplyPower(brick, ball);
        }

        private void ApplyPower(Brick brick, Ball source)
        {
            switch (brick.Kind)
            {
                case BrickKind.Wide:
                    WideTimer = WideSeconds;
                    PaddleX = ClampX(PaddleX);
                    PaddleTargetX = ClampX(PaddleTargetX);
                    break;
                case BrickKind.Slow:
                    SlowTimer = SlowSeconds;
                    break;
                case BrickKind.Multi:
                    SpawnExtra(source, -0.35f);
                    SpawnExtra(source, 0.35f);
                    break;
                default:
                    return;
            }
            Events.Add(SimEvent.PowerUp);
        }

        private void SpawnExtra(Ball source, float radians)
        {
            if (Balls.Count >= MaxBalls) return;
            double c = Math.Cos(radians), s = Math.Sin(radians);
            Vec2 d = new Vec2((float)(source.Dir.X * c - source.Dir.Y * s), (float)(source.Dir.X * s + source.Dir.Y * c));
            Ball b = new Ball();
            b.Active = true;
            b.Pos = source.Pos;
            b.Dir = EnforceMinVertical(d.Normalized());
            Balls.Add(b);
        }

        private void LoseLife()
        {
            Lives--;
            Combo = 0;
            Events.Add(SimEvent.LifeLost);
            if (Lives <= 0)
            {
                Lives = 0;
                Status = GameStatus.GameOver;
                Events.Add(SimEvent.GameOver);
                return;
            }
            ResetServe();
        }

        private void ClearLevel()
        {
            Status = GameStatus.LevelClear;
            Combo = 0;
            Balls.Clear();
            Score += LevelBonus * Level;
            if (Level % 3 == 0 && Lives < MaxLives) Lives++;
            Events.Add(SimEvent.LevelCleared);
        }
    }
}
