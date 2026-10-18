using System;
using System.Collections.Generic;

namespace BrickBurst.Core
{
    public enum BrickKind
    {
        Normal,
        Wide,   // paddle gets wider for a while
        Multi,  // two extra balls
        Slow    // ball slows down for a while
    }

    public sealed class Brick
    {
        public Aabb Box;
        public int Hp;
        public int MaxHp;
        public BrickKind Kind;

        public bool Alive { get { return Hp > 0; } }

        /// <summary>Points for destroying it (before the combo multiplier): 10 per starting hit point.</summary>
        public int Value { get { return 10 * MaxHp; } }
    }

    /// <summary>
    /// Level layouts. Each row is exactly Columns characters:
    /// '.' empty, '1' '2' '3' hit points, 'W' wide paddle, 'M' multi-ball, 'S' slow ball (power bricks have 1 hit point).
    /// </summary>
    public static class Levels
    {
        public const int Columns = 10;
        public const int MaxHp = 3;

        private static readonly string[][] Defs = new string[][]
        {
            new[] { "1111111111", "1111111111", "11M1111W11", "1111111111" },
            new[] { "2222222222", "1111111111", "1S11111S11", "1111111111" },
            new[] { "1.1.1.1.1.", ".2.2.2.2.2", "1.1.M.1.1.", ".1.1.1.1.1" },
            new[] { "3333333333", "2222222222", "11W1111W11", "1111111111", "11S1111S11" },
            new[] { "3.3.3.3.3.", "2222222222", "1M1.11.1M1", "3333333333", ".1.1.1.1.1" }
        };

        public static int Count { get { return Defs.Length; } }

        /// <summary>Rows for level index 0..; levels past the last repeat with one more hit point per lap (capped).</summary>
        public static string[] Rows(int levelIndex)
        {
            if (levelIndex < 0) throw new ArgumentOutOfRangeException("levelIndex");
            return Defs[levelIndex % Defs.Length];
        }

        public static int HpBonus(int levelIndex)
        {
            return levelIndex / Defs.Length;
        }
    }

    public static class BrickGrid
    {
        public const float SideMargin = 0.5f;
        public const float Gap = 0.08f;
        public const float BrickHeight = 0.5f;

        public static float BrickWidth
        {
            get { return (Game.Width - 2f * SideMargin - (Levels.Columns - 1) * Gap) / Levels.Columns; }
        }

        /// <summary>Y of the top edge of the first row.</summary>
        public static float TopY { get { return Game.Height - 1.5f; } }

        public static List<Brick> Build(string[] rows, int hpBonus)
        {
            if (rows == null) throw new ArgumentNullException("rows");
            List<Brick> bricks = new List<Brick>();
            float w = BrickWidth;
            for (int row = 0; row < rows.Length; row++)
            {
                string line = rows[row];
                if (line.Length != Levels.Columns)
                    throw new ArgumentException("row " + row + " has " + line.Length + " columns, expected " + Levels.Columns);
                for (int col = 0; col < line.Length; col++)
                {
                    char ch = line[col];
                    if (ch == '.') continue;

                    int hp;
                    BrickKind kind = BrickKind.Normal;
                    switch (ch)
                    {
                        case '1': hp = 1; break;
                        case '2': hp = 2; break;
                        case '3': hp = 3; break;
                        case 'W': hp = 1; kind = BrickKind.Wide; break;
                        case 'M': hp = 1; kind = BrickKind.Multi; break;
                        case 'S': hp = 1; kind = BrickKind.Slow; break;
                        default: throw new ArgumentException("unknown brick character '" + ch + "' in row " + row);
                    }
                    if (kind == BrickKind.Normal) hp = Math.Min(Levels.MaxHp, hp + hpBonus);

                    float minX = SideMargin + col * (w + Gap);
                    float maxY = TopY - row * (BrickHeight + Gap);
                    Brick brick = new Brick();
                    brick.Box = new Aabb(minX, maxY - BrickHeight, minX + w, maxY);
                    brick.Hp = hp;
                    brick.MaxHp = hp;
                    brick.Kind = kind;
                    bricks.Add(brick);
                }
            }
            return bricks;
        }
    }
}
