using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;

namespace Bloop.Rendering
{
    /// <summary>
    /// Organic / procedural drawing primitives that extend GeometryBatch.
    /// All helpers here are composed from GeometryBatch primitives and the
    /// AssetManager.Pixel texture, so everything still batches into a single
    /// SpriteBatch draw call per frame.
    ///
    /// Intended for world objects that want to feel life-like: breathing
    /// blobs, gradient halos, bezier stems, jittered living edges, vein
    /// networks, faceted gems.
    /// </summary>
    public static class OrganicPrimitives
    {
        // ── Blob (non-circular, breathing) ─────────────────────────────────────

        /// <summary>
        /// Filled non-circular blob — a circle whose radius is modulated per-angle
        /// by a small rotating sine (driven by <paramref name="time"/>) and a
        /// deterministic seed offset. Looks fleshy / breathing when animated.
        ///
        /// segments  — number of triangle slices (12–16 is usually enough).
        /// lobeCount — how many high/low points the outline has (2–4 typical).
        /// wobbleAmp — per-angle radius variation, in fraction of radius (0–0.25).
        /// </summary>
        public static void DrawBlob(SpriteBatch sb, AssetManager assets,
            Vector2 center, float radius, Color color,
            int lobeCount, float time, float wobbleAmp, int seed,
            int segments = 14)
        {
            if (radius < 1f) return;

            float step = MathHelper.TwoPi / segments;
            float seedPhase = NoiseHelpers.Hash01(seed) * MathHelper.TwoPi;

            // Build outline points first
            Span<Vector2> pts = stackalloc Vector2[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = i * step;
                float wobble = MathF.Sin(a * lobeCount + seedPhase + time) * wobbleAmp
                             + MathF.Sin(a * (lobeCount + 1) + seedPhase * 1.7f - time * 0.6f) * wobbleAmp * 0.5f;
                float r = radius * (1f + wobble);
                pts[i] = center + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
            }

            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                GeometryBatch.DrawTriangleSolid(sb, assets, center, pts[i], pts[j], color);
            }
        }

        // ── Radial gradient disk ───────────────────────────────────────────────

        /// <summary>
        /// Soft radial gradient: a disk of radius <paramref name="rOut"/> whose
        /// color lerps from innerColor (center / rIn) to outerColor (edge / rOut).
        /// Built from concentric ring steps; cheap and pretty.
        /// </summary>
        public static void DrawGradientDisk(SpriteBatch sb, AssetManager assets,
            Vector2 center, float rIn, float rOut, Color innerColor, Color outerColor,
            int rings = 6, int segments = 12)
        {
            if (rOut < 1f) return;
            if (rings < 2) rings = 2;

            // Outer-to-inner so inner draws on top
            for (int i = rings - 1; i >= 0; i--)
            {
                float t = i / (float)(rings - 1);
                float r = MathHelper.Lerp(rIn, rOut, t);
                Color c = Color.Lerp(innerColor, outerColor, t);
                GeometryBatch.DrawCircleApprox(sb, assets, center, r, c, segments);
            }
        }

        // ── Bezier curve ───────────────────────────────────────────────────────

        /// <summary>
        /// Cubic bezier from p0 to p3 with control points p1, p2.
        /// Drawn as N short connected line pieces.
        /// </summary>
        public static void DrawBezier(SpriteBatch sb, AssetManager assets,
            Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
            Color color, float thickness = 1f, int segments = 12)
        {
            Vector2 prev = p0;
            for (int i = 1; i <= segments; i++)
            {
                float t  = i / (float)segments;
                float it = 1f - t;
                Vector2 pt =
                      p0 * (it * it * it)
                    + p1 * (3f * it * it * t)
                    + p2 * (3f * it * t * t)
                    + p3 * (t  * t  * t);
                GeometryBatch.DrawLine(sb, assets, prev, pt, color, thickness);
                prev = pt;
            }
        }

        /// <summary>
        /// Quadratic bezier convenience overload.
        /// </summary>
        public static void DrawBezierQuad(SpriteBatch sb, AssetManager assets,
            Vector2 p0, Vector2 p1, Vector2 p2,
            Color color, float thickness = 1f, int segments = 10)
        {
            Vector2 prev = p0;
            for (int i = 1; i <= segments; i++)
            {
                float t  = i / (float)segments;
                float it = 1f - t;
                Vector2 pt = p0 * (it * it) + p1 * (2f * it * t) + p2 * (t * t);
                GeometryBatch.DrawLine(sb, assets, prev, pt, color, thickness);
                prev = pt;
            }
        }

        // ── Noisy line (jittered living edge) ──────────────────────────────────

        /// <summary>
        /// Line from a→b whose midpoints are offset perpendicular by sine-noise.
        /// Useful for twitching filaments, lightning arcs, crack patterns.
        /// </summary>
        public static void DrawNoisyLine(SpriteBatch sb, AssetManager assets,
            Vector2 a, Vector2 b, Color color, float thickness,
            float amplitude, float frequency, float time, int seed,
            int segments = 8)
        {
            Vector2 diff = b - a;
            float len = diff.Length();
            if (len < 1f) return;
            Vector2 dir  = diff / len;
            Vector2 perp = new Vector2(-dir.Y, dir.X);

            Vector2 prev = a;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                // Noise along the line, fading at endpoints so a/b stay anchored.
                float edgeFade = MathF.Sin(t * MathF.PI);
                float n = MathF.Sin(time * frequency + t * MathF.PI * 2f + NoiseHelpers.Hash01(seed) * 10f)
                        + MathF.Sin(time * frequency * 1.7f + t * MathF.PI * 5f + seed) * 0.5f;
                Vector2 pt = a + dir * (t * len) + perp * (n * amplitude * edgeFade);
                GeometryBatch.DrawLine(sb, assets, prev, pt, color, thickness);
                prev = pt;
            }
            // Ensure endpoint exact
            GeometryBatch.DrawLine(sb, assets, prev, b, color, thickness);
        }

        // ── Vein network ───────────────────────────────────────────────────────

        /// <summary>
        /// Draw a small root / vein / nerve pattern radiating from an origin.
        /// Depth-2 branches. Deterministic for a given seed.
        ///
        /// branchCount — primary branches off the origin.
        /// length      — primary branch length in pixels.
        /// </summary>
        public static void DrawVeinNetwork(SpriteBatch sb, AssetManager assets,
            Vector2 origin, Color color, int branchCount, float length,
            float thickness, float time, int seed)
        {
            for (int i = 0; i < branchCount; i++)
            {
                float a = (i / (float)branchCount) * MathHelper.TwoPi
                        + NoiseHelpers.HashSigned(seed + i * 17) * 0.25f;
                Vector2 dir = new Vector2(MathF.Cos(a), MathF.Sin(a));

                // Primary branch — noisy line
                Vector2 tip = origin + dir * length;
                DrawNoisyLine(sb, assets, origin, tip, color, thickness,
                    amplitude: length * 0.12f, frequency: 1.3f,
                    time: time, seed: seed + i, segments: 6);

                // One or two sub-branches near the tip
                int subCount = 1 + ((seed + i) & 1);
                for (int s = 0; s < subCount; s++)
                {
                    float sa = a + NoiseHelpers.HashSigned(seed + i * 31 + s * 7) * 0.9f;
                    Vector2 sdir = new Vector2(MathF.Cos(sa), MathF.Sin(sa));
                    Vector2 start = origin + dir * (length * 0.55f);
                    Vector2 end   = start + sdir * (length * 0.45f);
                    DrawNoisyLine(sb, assets, start, end,
                        color * 0.8f, MathF.Max(1f, thickness - 0.5f),
                        amplitude: length * 0.08f, frequency: 1.1f,
                        time: time, seed: seed + i * 41 + s, segments: 5);
                }
            }
        }

        // ── Faceted gem ────────────────────────────────────────────────────────

        /// <summary>
        /// Polygonal gem with a lighter highlight on a rotating specular facet.
        /// Good for crystals, shards, geodes.
        /// </summary>
        public static void DrawFacetedGem(SpriteBatch sb, AssetManager assets,
            Vector2 center, float radius, int facetCount,
            Color baseColor, Color highlight, float time, int seed = 0)
        {
            if (facetCount < 3) facetCount = 3;
            float step = MathHelper.TwoPi / facetCount;
            float rotBase = NoiseHelpers.Hash01(seed) * MathHelper.TwoPi;

            // Precompute rim vertices (with slight per-vertex seed jitter for irregular facets)
            Span<Vector2> rim = stackalloc Vector2[facetCount];
            for (int i = 0; i < facetCount; i++)
            {
                float jitter = 1f + NoiseHelpers.HashSigned(seed + i * 11) * 0.12f;
                float a = rotBase + i * step;
                rim[i] = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius * jitter;
            }

            // Rotating specular facet index
            int specIdx = ((int)(time * 2f) % facetCount + facetCount) % facetCount;

            // Fill triangles from center
            for (int i = 0; i < facetCount; i++)
            {
                int j = (i + 1) % facetCount;
                Color c = (i == specIdx) ? highlight : baseColor;
                GeometryBatch.DrawTriangleSolid(sb, assets, center, rim[i], rim[j], c);
            }

            // Rim highlight polygon outline
            for (int i = 0; i < facetCount; i++)
            {
                int j = (i + 1) % facetCount;
                GeometryBatch.DrawLine(sb, assets, rim[i], rim[j],
                    Color.Lerp(baseColor, highlight, 0.5f), 1f);
            }
        }
    }
}
