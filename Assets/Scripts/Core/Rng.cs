namespace BrickBurst.Core
{
    /// <summary>
    /// Small xorshift generator. UnityEngine.Random and System.Random are not guaranteed to
    /// give the same sequence across platforms/versions; this one does, so spawn tests are stable.
    /// </summary>
    public sealed class Rng
    {
        private uint state;

        public Rng(int seed)
        {
            // Mix the seed so that 0 and small seeds do not start in a weak state.
            state = (uint)seed * 2654435761u + 0x9E3779B9u;
            if (state == 0u) state = 2463534242u;
            for (int i = 0; i < 4; i++) NextUInt();
        }

        public uint NextUInt()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat()
        {
            return (NextUInt() & 0xFFFFFFu) / 16777216f;
        }

        /// <summary>Uniform float in [min, max).</summary>
        public float Range(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }

        /// <summary>Uniform int in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            return min + (int)(NextUInt() % (uint)(maxExclusive - min));
        }
    }
}
