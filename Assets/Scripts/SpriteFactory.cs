using UnityEngine;

namespace BrickBurst
{
    /// <summary>
    /// The game ships no art. Sprites are generated at start-up as white shapes and tinted with
    /// SpriteRenderer.color, so there are no image assets to import or commit.
    /// </summary>
    public static class SpriteFactory
    {
        private static Sprite circle;
        private static Sprite square;

        /// <summary>A soft-edged white circle exactly one world unit across.</summary>
        public static Sprite Circle()
        {
            if (circle != null) return circle;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            float r = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r, dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // One pixel of anti-aliasing at the rim.
                    float a = Mathf.Clamp01(r - d);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            circle.name = "BrickBurst_Circle";
            return circle;
        }

        /// <summary>A plain white square one world unit across.</summary>
        public static Sprite Square()
        {
            if (square != null) return square;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply();
            square = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
            square.name = "BrickBurst_Square";
            return square;
        }
    }
}
