using System;

namespace Bloop.Rendering
{
    /// <summary>
    /// Small deterministic helpers for smooth procedural variation.
    /// Used by world objects that want breathing / drift animation that
    /// doesn't look like pure sine — or for seeded jitter / position hashes.
    /// </summary>
    public static class NoiseHelpers
    {
        /// <summary>Deterministic hash in [0, 1] from an integer seed.</summary>
        public static float Hash01(int seed)
        {
            unchecked
            {
                uint s = (uint)seed;
                s ^= s << 13;
                s ^= s >> 17;
                s ^= s << 5;
                return (s & 0x00FFFFFF) / (float)0x01000000;
            }
        }

        /// <summary>Deterministic signed hash in [-1, 1] from an integer seed.</summary>
        public static float HashSigned(int seed) => Hash01(seed) * 2f - 1f;

        /// <summary>
        /// 1-D value noise — smooth cosine-interpolated pseudo-random curve.
        /// t is continuous; the curve passes through Hash01(k) at every integer k.
        /// Used for slow organic drift (breathing, tendril writhe).
        /// </summary>
        public static float ValueNoise1D(float t, int seed)
        {
            int   i = (int)MathF.Floor(t);
            float f = t - i;
            float a = Hash01(seed + i);
            float b = Hash01(seed + i + 1);
            // Cosine (smoothstep-ish) interpolation between a and b
            float u = (1f - MathF.Cos(f * MathF.PI)) * 0.5f;
            return a * (1f - u) + b * u;
        }

        /// <summary>
        /// Signed 1-D value noise in [-1, 1].
        /// </summary>
        public static float ValueNoise1DSigned(float t, int seed)
            => ValueNoise1D(t, seed) * 2f - 1f;
    }
}
