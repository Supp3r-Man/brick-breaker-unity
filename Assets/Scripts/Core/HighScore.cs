using System.Globalization;

namespace BrickBurst.Core
{
    /// <summary>Parsing and comparing the stored best score. Storage itself (PlayerPrefs) lives in the Unity layer.</summary>
    public static class HighScore
    {
        /// <summary>Anything missing, malformed or negative reads as 0.</summary>
        public static int Parse(string stored)
        {
            int value;
            if (string.IsNullOrEmpty(stored)) return 0;
            if (!int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return 0;
            return value < 0 ? 0 : value;
        }

        public static string Format(int score)
        {
            return (score < 0 ? 0 : score).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Returns the new best; isNewBest is true only for a strictly higher score.</summary>
        public static int Update(int best, int score, out bool isNewBest)
        {
            isNewBest = score > best;
            return isNewBest ? score : best;
        }
    }
}
