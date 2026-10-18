using System;
using System.Collections.Generic;
using UnityEngine;
using BrickBurst.Core;

namespace BrickBurst
{
    /// <summary>
    /// Unity side of Brick Burst: builds the camera, arena and sprites, turns input into a paddle target,
    /// steps the Core simulation with a fixed-timestep accumulator, mirrors the result onto sprites and
    /// draws the HUD. All rules and all collision maths live in BrickBurst.Core (engine-free, tested in
    /// Tests/LogicTests); nothing here uses Unity physics.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        private const float MaxFrameTime = 0.1f; // spiral-of-death guard: never try to catch up more than this
        private const int MaxStepsPerFrame = 10;
        private const string BestKey = "brickburst.best";

        private static readonly Color BackgroundColor = new Color(0.04f, 0.07f, 0.10f);
        private static readonly Color WallColor = new Color(0.25f, 0.33f, 0.45f);
        private static readonly Color PaddleColor = new Color(0.80f, 0.90f, 1.00f);
        private static readonly Color PaddleWideColor = new Color(0.55f, 1.00f, 0.45f);
        private static readonly Color BallColor = new Color(1.00f, 0.95f, 0.80f);
        private static readonly Color BallSlowColor = new Color(0.60f, 0.70f, 1.00f);
        private static readonly Color Hp1Color = new Color(0.20f, 0.80f, 0.75f);
        private static readonly Color Hp2Color = new Color(1.00f, 0.70f, 0.20f);
        private static readonly Color Hp3Color = new Color(0.95f, 0.30f, 0.55f);
        private static readonly Color WideBrickColor = new Color(0.50f, 1.00f, 0.30f);
        private static readonly Color MultiBrickColor = new Color(0.95f, 0.95f, 1.00f);
        private static readonly Color SlowBrickColor = new Color(0.45f, 0.50f, 1.00f);

        private sealed class BrickView
        {
            public SpriteRenderer Body;
            public SpriteRenderer Marker; // small dot that tells power bricks apart from plain ones
        }

        private readonly List<BrickView> brickViews = new List<BrickView>();
        private readonly List<SpriteRenderer> ballViews = new List<SpriteRenderer>();

        private Game game;
        private Sfx sfx;
        private Camera cam;
        private SpriteRenderer paddleView;
        private float accumulator;
        private bool needsRebuild;
        private bool paused;
        private bool launchRequested;
        private bool usingPointer;
        private bool pointerSeen;
        private float lastPointerX;
        private int best;
        private bool newBest;
        private float sinceStart;
        private GUIStyle bigStyle, midStyle, smallStyle;

        private void Awake()
        {
            game = new Game(Environment.TickCount);
            sfx = new Sfx(gameObject);
            SetupCamera();
            SetupWalls();
            paddleView = MakeSprite("Paddle", SpriteFactory.Square(), PaddleColor, 10);
            best = HighScore.Parse(PlayerPrefs.GetString(BestKey, ""));
            needsRebuild = true;
        }

        private void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            if (FindFirstObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BackgroundColor;
            cam.transform.rotation = Quaternion.identity;
            FitCamera();
        }

        /// <summary>Keep the whole arena visible at any aspect ratio.</summary>
        private void FitCamera()
        {
            float halfH = Game.Height / 2f;
            float halfW = Game.Width / 2f;
            cam.orthographicSize = Mathf.Max(halfH, halfW / cam.aspect);
            cam.transform.position = new Vector3(halfW, halfH, -10f);
        }

        private void SetupWalls()
        {
            const float t = 0.2f;
            Bar("WallLeft", -t / 2f, Game.Height / 2f, t, Game.Height + t * 2f);
            Bar("WallRight", Game.Width + t / 2f, Game.Height / 2f, t, Game.Height + t * 2f);
            Bar("WallTop", Game.Width / 2f, Game.Height + t / 2f, Game.Width + t * 2f, t);
        }

        private void Bar(string objectName, float x, float y, float w, float h)
        {
            var sr = MakeSprite(objectName, SpriteFactory.Square(), WallColor, -10);
            sr.transform.position = new Vector3(x, y, 0f);
            sr.transform.localScale = new Vector3(w, h, 1f);
        }

        private SpriteRenderer MakeSprite(string objectName, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>One sprite per brick of the current level (and a marker dot on power bricks).</summary>
        private void RebuildBricks()
        {
            foreach (var v in brickViews)
            {
                Destroy(v.Body.gameObject);
                if (v.Marker != null) Destroy(v.Marker.gameObject);
            }
            brickViews.Clear();

            foreach (Brick b in game.Bricks)
            {
                var view = new BrickView();
                view.Body = MakeSprite("Brick", SpriteFactory.Square(), Hp1Color, 5);
                view.Body.transform.position = new Vector3(b.Box.CenterX, b.Box.CenterY, 0f);
                view.Body.transform.localScale = new Vector3(b.Box.MaxX - b.Box.MinX, b.Box.MaxY - b.Box.MinY, 1f);
                if (b.Kind != BrickKind.Normal)
                {
                    view.Marker = MakeSprite("Marker", SpriteFactory.Circle(), new Color(0.05f, 0.07f, 0.10f, 0.8f), 6);
                    view.Marker.transform.position = new Vector3(b.Box.CenterX, b.Box.CenterY, 0f);
                    view.Marker.transform.localScale = Vector3.one * 0.22f;
                }
                brickViews.Add(view);
            }
            needsRebuild = false;
        }

        private static Color BrickColor(Brick b)
        {
            switch (b.Kind)
            {
                case BrickKind.Wide: return WideBrickColor;
                case BrickKind.Multi: return MultiBrickColor;
                case BrickKind.Slow: return SlowBrickColor;
            }
            if (b.Hp >= 3) return Hp3Color;
            if (b.Hp == 2) return Hp2Color;
            return Hp1Color;
        }

        private void NewGame()
        {
            game.NewGame();
            newBest = false;
            paused = false;
            accumulator = 0f;
            needsRebuild = true;
        }

        private void Update()
        {
            FitCamera();
            sinceStart += Time.deltaTime;

            if (InputReader.PausePressed() && (game.Status == GameStatus.Playing || game.Status == GameStatus.Serve)) paused = !paused;
            if (InputReader.LaunchPressed()) launchRequested = true;

            if (game.Status == GameStatus.GameOver && (InputReader.RestartPressed() || launchRequested))
            {
                launchRequested = false;
                NewGame();
            }
            else if (game.Status == GameStatus.LevelClear && launchRequested)
            {
                launchRequested = false;
                game.NextLevel();
                needsRebuild = true;
                accumulator = 0f;
            }

            if (!paused) Simulate();
            if (needsRebuild) RebuildBricks();
            Render();
        }

        private void Simulate()
        {
            // Mouse and keyboard both work: whichever was used last drives the paddle.
            Vector2 pointer;
            if (InputReader.TryGetPointer(out pointer))
            {
                // The first reading is only a baseline; a "move" needs a later reading that differs.
                if (pointerSeen && Mathf.Abs(pointer.x - lastPointerX) > 0.5f) usingPointer = true;
                lastPointerX = pointer.x;
                pointerSeen = true;
            }
            float axis = InputReader.PaddleAxis();
            if (axis != 0f) usingPointer = false;
            if (usingPointer) game.SetPaddleTarget(PointerWorldX(pointer.x));

            accumulator += Mathf.Min(Time.deltaTime, MaxFrameTime);
            int steps = 0;
            while (accumulator >= Game.FixedDt && steps < MaxStepsPerFrame)
            {
                if (!usingPointer && axis != 0f)
                    game.SetPaddleTarget(game.PaddleTargetX + axis * Game.PaddleSpeed * Game.FixedDt);
                if (launchRequested && game.Status == GameStatus.Serve)
                {
                    game.Launch();
                    launchRequested = false;
                }
                game.Step(Game.FixedDt);
                accumulator -= Game.FixedDt;
                steps++;
            }
            if (steps == MaxStepsPerFrame) accumulator = 0f;
            if (game.Status != GameStatus.Serve) launchRequested = false;

            DrainEvents();
        }

        private float PointerWorldX(float screenX)
        {
            return cam.ScreenToWorldPoint(new Vector3(screenX, 0f, Mathf.Abs(cam.transform.position.z))).x;
        }

        private void DrainEvents()
        {
            foreach (SimEvent e in game.Events)
            {
                switch (e)
                {
                    case SimEvent.Launch: sfx.Launch(); break;
                    case SimEvent.WallHit: sfx.Wall(); break;
                    case SimEvent.PaddleHit: sfx.Paddle(); break;
                    case SimEvent.BrickHit: sfx.Brick(); break;
                    case SimEvent.BrickDestroyed: sfx.Burst(); break;
                    case SimEvent.PowerUp: sfx.Power(); break;
                    case SimEvent.LifeLost: sfx.Lose(); break;
                    case SimEvent.LevelCleared: sfx.Clear(); SaveBest(); break;
                    case SimEvent.GameOver: sfx.GameOver(); SaveBest(); break;
                }
            }
            game.ClearEvents();
        }

        private void SaveBest()
        {
            bool isNew;
            best = HighScore.Update(best, game.Score, out isNew);
            if (isNew) newBest = true;
            PlayerPrefs.SetString(BestKey, HighScore.Format(best));
            PlayerPrefs.Save();
        }

        private void Render()
        {
            // Paddle
            paddleView.transform.position = new Vector3(game.PaddleX, Game.PaddleY, 0f);
            paddleView.transform.localScale = new Vector3(game.PaddleWidth, Game.PaddleHeight, 1f);
            paddleView.color = game.WideTimer > 0f ? PaddleWideColor : PaddleColor;

            // Bricks (same order as game.Bricks, which is never reordered)
            int n = Mathf.Min(brickViews.Count, game.Bricks.Count);
            for (int i = 0; i < n; i++)
            {
                Brick b = game.Bricks[i];
                BrickView v = brickViews[i];
                v.Body.enabled = b.Alive;
                if (v.Marker != null) v.Marker.enabled = b.Alive;
                if (b.Alive) v.Body.color = BrickColor(b);
            }

            // Balls: grow/shrink the pool to match the simulation
            while (ballViews.Count < game.Balls.Count) ballViews.Add(MakeSprite("Ball", SpriteFactory.Circle(), BallColor, 20));
            while (ballViews.Count > game.Balls.Count)
            {
                Destroy(ballViews[ballViews.Count - 1].gameObject);
                ballViews.RemoveAt(ballViews.Count - 1);
            }
            for (int i = 0; i < ballViews.Count; i++)
            {
                Ball ball = game.Balls[i];
                ballViews[i].transform.position = new Vector3(ball.Pos.X, ball.Pos.Y, 0f);
                ballViews[i].transform.localScale = Vector3.one * (Game.BallRadius * 2f);
                ballViews[i].color = game.SlowTimer > 0f ? BallSlowColor : BallColor;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && game != null && game.Status == GameStatus.Playing) paused = true;
        }

        // ---- HUD (IMGUI: no UI package or canvas needed) ----

        private void EnsureStyles()
        {
            int h = Screen.height;
            if (bigStyle == null)
            {
                bigStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                midStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
                smallStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            }
            bigStyle.fontSize = Mathf.Max(24, h / 12);
            midStyle.fontSize = Mathf.Max(14, h / 30);
            smallStyle.fontSize = Mathf.Max(12, h / 38);
        }

        private static void Shadowed(Rect r, string text, GUIStyle style, Color color)
        {
            Color old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        private void OnGUI()
        {
            if (game == null) return;
            EnsureStyles();

            float w = Screen.width, h = Screen.height;
            int shownBest = Mathf.Max(best, game.Score);
            Shadowed(new Rect(16f, 8f, w, h * 0.06f),
                "Score " + game.Score + "    Level " + game.Level + "    Best " + shownBest, midStyle, Color.white);
            Shadowed(new Rect(16f, 8f + h * 0.055f, w, h * 0.06f),
                "Lives " + game.Lives, midStyle, new Color(1f, 0.5f, 0.5f));
            if (game.Combo >= 3)
                Shadowed(new Rect(16f, 8f + h * 0.11f, w, h * 0.06f),
                    "Combo " + game.Combo + "  (x" + game.Multiplier + ")", midStyle, new Color(1f, 0.85f, 0.2f));

            string effects = "";
            if (game.WideTimer > 0f) effects += "WIDE " + Mathf.CeilToInt(game.WideTimer) + "s   ";
            if (game.SlowTimer > 0f) effects += "SLOW " + Mathf.CeilToInt(game.SlowTimer) + "s";
            if (effects.Length > 0)
                Shadowed(new Rect(16f, h - h * 0.07f, w, h * 0.06f), effects, midStyle, new Color(0.6f, 1f, 0.6f));

            if (game.Status == GameStatus.Serve && !paused)
            {
                Shadowed(new Rect(0f, h * 0.62f, w, h * 0.07f), "Click or press Space to launch", smallStyle, Color.white);
                if (sinceStart < 20f)
                    Shadowed(new Rect(0f, h * 0.68f, w, h * 0.07f),
                        "Mouse or A/D to move.  Green = wide paddle, white = multi-ball, blue = slow ball.  P pauses.",
                        smallStyle, new Color(1f, 1f, 1f, 0.8f));
            }

            if (paused)
            {
                Shadowed(new Rect(0f, h * 0.35f, w, h * 0.2f), "PAUSED", bigStyle, Color.white);
                Shadowed(new Rect(0f, h * 0.55f, w, h * 0.06f), "Press P to resume", smallStyle, Color.white);
            }

            if (game.Status == GameStatus.LevelClear)
            {
                Shadowed(new Rect(0f, h * 0.30f, w, h * 0.2f), "LEVEL " + game.Level + " CLEAR", bigStyle, new Color(0.5f, 1f, 0.5f));
                Shadowed(new Rect(0f, h * 0.50f, w, h * 0.06f), "Click or press Space for the next level", smallStyle, Color.white);
            }

            if (game.Status == GameStatus.GameOver)
            {
                Shadowed(new Rect(0f, h * 0.28f, w, h * 0.2f), "GAME OVER", bigStyle, new Color(1f, 0.35f, 0.35f));
                Shadowed(new Rect(0f, h * 0.48f, w, h * 0.07f),
                    "Score " + game.Score + (newBest ? "   NEW BEST!" : "   Best " + best), smallStyle, Color.white);
                Shadowed(new Rect(0f, h * 0.56f, w, h * 0.06f), "Press R, Space or click to play again", smallStyle, Color.white);
            }
        }
    }
}
