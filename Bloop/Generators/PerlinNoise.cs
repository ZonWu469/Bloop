using System;

namespace Bloop.Generators
{
    /// <summary>
    /// Seed-based Perlin noise implementation.
    /// Produces smooth, organic-looking noise values in the range [0, 1].
    ///
    /// Uses the improved Perlin noise algorithm with:
    ///   - 256-entry permutation table shuffled from the seed
    ///   - 8 gradient vectors at 45-degree intervals
    ///   - Quintic fade function: 6t^5 - 15t^4 + 10t^3
    ///   - Multi-octave support via SampleOctaves() and GenerateGrid()
    /// </summary>
    public class PerlinNoise
    {
        // ── Permutation table ──────────────────────────────────────────────────
        private readonly int[] _perm = new int[512];

        // ── Gradient vectors (8 directions at 45° intervals) ──────────────────
        private static readonly float[,] Gradients = new float[8, 2]
        {
            {  1f,  0f },
            { -1f,  0f },
            {  0f,  1f },
            {  0f, -1f },
            {  0.7071f,  0.7071f },
            { -0.7071f,  0.7071f },
            {  0.7071f, -0.7071f },
            { -0.7071f, -0.7071f }
        };

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Initialize the noise generator with the given seed.
        /// The same seed always produces the same noise field.
        /// </summary>
        public PerlinNoise(int seed)
        {
            // Build a base permutation array [0..255]
            int[] source = new int[256];
            for (int i = 0; i < 256; i++)
                source[i] = i;

            // Fisher-Yates shuffle using the seed
            var rng = new Random(seed);
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (source[i], source[j]) = (source[j], source[i]);
            }

            // Double the permutation table to avoid index wrapping
            for (int i = 0; i < 512; i++)
                _perm[i] = source[i & 255];
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Sample the noise at a single (x, y) coordinate.
        /// Returns a value in approximately [-1, 1] before normalization.
        /// Use SampleOctaves() or GenerateGrid() for [0, 1] output.
        /// </summary>
        public float Sample(float x, float y)
        {
            // Integer cell coordinates
            int xi = (int)Math.Floor(x) & 255;
            int yi = (int)Math.Floor(y) & 255;

            // Fractional position within cell
            float xf = x - (float)Math.Floor(x);
            float yf = y - (float)Math.Floor(y);

            // Fade curves for smooth interpolation
            float u = Fade(xf);
            float v = Fade(yf);

            // Hash corner coordinates
            int aa = _perm[_perm[xi    ] + yi    ];
            int ab = _perm[_perm[xi    ] + yi + 1];
            int ba = _perm[_perm[xi + 1] + yi    ];
            int bb = _perm[_perm[xi + 1] + yi + 1];

            // Interpolate gradient contributions
            float x1 = Lerp(Grad(aa, xf,       yf    ),
                            Grad(ba, xf - 1f,   yf    ), u);
            float x2 = Lerp(Grad(ab, xf,       yf - 1f),
                            Grad(bb, xf - 1f,   yf - 1f), u);

            return Lerp(x1, x2, v);
        }

        /// <summary>
        /// Sample multi-octave (fractal) noise at (x, y).
        /// Layers multiple noise passes at increasing frequencies and decreasing amplitudes.
        ///
        /// octaves:     number of noise layers (2–6 typical)
        /// persistence: amplitude multiplier per octave (0.5 = each octave is half as strong)
        /// lacunarity:  frequency multiplier per octave (2.0 = each octave is twice as detailed)
        ///
        /// Returns a value in [0, 1].
        /// </summary>
        public float SampleOctaves(float x, float y,
            int octaves, float persistence, float lacunarity)
        {
            float value     = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float maxValue  = 0f; // for normalization

            for (int i = 0; i < octaves; i++)
            {
                value    += Sample(x * frequency, y * frequency) * amplitude;
                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            // Normalize to [0, 1]
            return (value / maxValue + 1f) * 0.5f;
        }

        /// <summary>
        /// Generate a full 2D noise grid of the given dimensions.
        /// Each cell is sampled at (tx * scale, ty * scale) with multi-octave noise.
        ///
        /// scale:       controls zoom level — lower values = larger, smoother features
        /// octaves:     number of noise layers
        /// persistence: amplitude falloff per octave
        /// lacunarity:  frequency multiplier per octave
        ///
        /// Returns a [width, height] array with values in [0, 1].
        /// </summary>
        public float[,] GenerateGrid(int width, int height,
            float scale, int octaves, float persistence, float lacunarity)
        {
            var grid = new float[width, height];

            for (int ty = 0; ty < height; ty++)
            {
                for (int tx = 0; tx < width; tx++)
                {
                    grid[tx, ty] = SampleOctaves(
                        tx * scale,
                        ty * scale,
                        octaves,
                        persistence,
                        lacunarity);
                }
            }

            return grid;
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>Quintic fade function: 6t^5 - 15t^4 + 10t^3.</summary>
        private static float Fade(float t)
            => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// <summary>Linear interpolation.</summary>
        private static float Lerp(float a, float b, float t)
            => a + t * (b - a);

        /// <summary>
        /// Compute the dot product of the gradient vector at hash h
        /// with the distance vector (x, y).
        /// </summary>
        private static float Grad(int hash, float x, float y)
        {
            int g = hash & 7; // select one of 8 gradient vectors
            return Gradients[g, 0] * x + Gradients[g, 1] * y;
        }
    }
}
