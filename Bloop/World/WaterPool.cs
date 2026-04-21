using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Gameplay;
using Bloop.Generators;
using Bloop.Physics;
using Bloop.Rendering;

namespace Bloop.World
{
    /// <summary>
    /// Represents a single water pool occupying a horizontal run of shaft-bottom tiles.
    /// </summary>
    public readonly struct WaterPoolRect
    {
        /// <summary>Pixel-space rectangle of the pool surface (1 tile tall).</summary>
        public Rectangle PixelRect { get; init; }
        /// <summary>Depth of the pool in tiles (1–3).</summary>
        public int DepthTiles { get; init; }
    }

    /// <summary>
    /// Manages all water pools in a level (1.5).
    ///
    /// Features:
    ///   - Detects shaft-bottom tiles via CavityAnalyzer.IsShaftBottom
    ///   - Merges adjacent shaft-bottom tiles into pool rectangles
    ///   - Renders animated water surface (ripple wave + translucent fill)
    ///   - Applies gameplay effects when player overlaps a pool:
    ///       • Horizontal speed capped at 60% of normal
    ///       • Fall damage negated (peak velocity reset)
    ///       • Vertical velocity damped on entry (splash deceleration)
    /// </summary>
    public class WaterPoolSystem
    {
        // ── Pool data ──────────────────────────────────────────────────────────
        private readonly List<WaterPoolRect> _pools = new();

        // ── Player state tracking ──────────────────────────────────────────────
        private bool _playerWasInWater = false;

        // ── Palette ────────────────────────────────────────────────────────────
        private static readonly Color WaterDeep    = new Color( 20,  60, 120, 160);
        private static readonly Color WaterSurface = new Color( 40, 100, 200, 200);
        private static readonly Color WaterShine   = new Color(120, 180, 255, 100);
        private static readonly Color WaterFoam    = new Color(200, 230, 255, 180);
        private static readonly Color WaterSplash  = new Color( 80, 160, 255, 140);

        // ── Splash particles ───────────────────────────────────────────────────
        private struct SplashParticle
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float   Life;
            public bool    Active;
        }
        private readonly SplashParticle[] _splashPool = new SplashParticle[32];
        private int _splashHead = 0;
        private readonly Random _rng = new Random();

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Scan the tile map for shaft-bottom tiles and build pool rectangles.
        /// Call once after level generation.
        /// </summary>
        public void BuildPools(TileMap map, int seed)
        {
            _pools.Clear();
            int w = map.Width;
            int h = map.Height;
            var rng = new Random(seed ^ 0xB00B5);

            // Use CavityAnalyzer to find shaft bottoms
            var analyzer = new CavityAnalyzer(map);

            int ts = TileMap.TileSize;

            // Scan row by row; merge adjacent shaft-bottom tiles into runs
            for (int ty = 1; ty < h - 1; ty++)
            {
                int runStart = -1;
                int runLen   = 0;

                for (int tx = 0; tx < w; tx++)
                {
                    bool isShaftBottom = analyzer.IsShaftBottom[tx, ty] &&
                                         map.GetTile(tx, ty) == TileType.Empty;

                    if (isShaftBottom)
                    {
                        if (runStart < 0) runStart = tx;
                        runLen++;
                    }
                    else
                    {
                        if (runLen >= 2) // minimum 2 tiles wide to form a pool
                        {
                            TryAddContainedPool(map, rng, _pools, runStart, runLen, ty, ts, w, h);
                        }
                        runStart = -1;
                        runLen   = 0;
                    }
                }

                // Flush trailing run
                if (runLen >= 2)
                {
                    TryAddContainedPool(map, rng, _pools, runStart, runLen, ty, ts, w, h);
                }
            }
        }

        /// <summary>
        /// Update water gameplay effects and splash particles.
        /// Call once per frame in Level.Update().
        /// </summary>
        public void Update(float dt, Player? player)
        {
            // Tick splash particles
            for (int i = 0; i < _splashPool.Length; i++)
            {
                ref var p = ref _splashPool[i];
                if (!p.Active) continue;
                p.Life -= dt;
                if (p.Life <= 0f) { p.Active = false; continue; }
                p.Position += p.Velocity * dt;
                p.Velocity.Y += 200f * dt; // gravity
            }

            if (player == null || player.State == PlayerState.Dead) return;

            bool inWater = false;
            WaterPoolRect activePool = default;

            // Check if player overlaps any pool
            var playerBounds = player.PixelBounds;
            foreach (var pool in _pools)
            {
                // Expand pool rect downward by depth
                var fullRect = new Rectangle(
                    pool.PixelRect.X,
                    pool.PixelRect.Y,
                    pool.PixelRect.Width,
                    pool.PixelRect.Height * pool.DepthTiles);

                if (playerBounds.Intersects(fullRect))
                {
                    inWater     = true;
                    activePool  = pool;
                    break;
                }
            }

            if (inWater)
            {
                // ── Entry splash ───────────────────────────────────────────────
                if (!_playerWasInWater)
                {
                    float entrySpeed = Math.Abs(PhysicsManager.ToPixels(player.Body.LinearVelocity.Y));
                    if (entrySpeed > 80f)
                        SpawnSplash(player.PixelPosition, activePool.PixelRect.Y, entrySpeed);

                    // Negate fall damage: reset peak fall velocity via vertical damp
                    // We do this by clamping downward velocity to a safe value
                    var vel = player.Body.LinearVelocity;
                    if (vel.Y > 0f)
                        player.Body.LinearVelocity = new Vector2(vel.X, vel.Y * 0.25f);
                }

                // ── Continuous water drag ──────────────────────────────────────
                // Cap horizontal speed at 60% of normal max (180 px/s → 108 px/s)
                const float WaterSpeedCap = 108f; // px/s
                var v = player.Body.LinearVelocity;
                float vxPx = PhysicsManager.ToPixels(v.X);
                if (Math.Abs(vxPx) > WaterSpeedCap)
                {
                    float capped = Math.Sign(vxPx) * WaterSpeedCap;
                    player.Body.LinearVelocity = new Vector2(
                        PhysicsManager.ToMeters(capped), v.Y);
                }

                // Damp vertical velocity (water resistance)
                if (v.Y > 0f)
                {
                    player.Body.LinearVelocity = new Vector2(
                        player.Body.LinearVelocity.X,
                        v.Y * (1f - 3f * dt)); // exponential drag
                }
            }

            _playerWasInWater = inWater;
        }

        /// <summary>
        /// Draw all water pools and splash particles.
        /// Call inside the world SpriteBatch block.
        /// </summary>
        public void Draw(SpriteBatch sb, AssetManager assets, Rectangle visibleBounds)
        {
            float t = AnimationClock.Time;

            foreach (var pool in _pools)
            {
                var r = pool.PixelRect;

                var expandedR = new Rectangle(r.X - 4, r.Y - 4, r.Width + 8, r.Height + 8);
                if (!visibleBounds.Intersects(expandedR)) continue;

                // ── Depth gradient bands ───────────────────────────────────────
                int totalDepthPx = pool.DepthTiles * TileMap.TileSize;
                int bands = Math.Max(2, pool.DepthTiles * 2);
                for (int b = 0; b < bands; b++)
                {
                    float bt = b / (float)bands;
                    int by = r.Y + (int)(bt * totalDepthPx);
                    int bh = Math.Max(1, totalDepthPx / bands);
                    Color bc = Color.Lerp(WaterSurface, WaterDeep, bt);
                    assets.DrawRect(sb, new Rectangle(r.X, by, r.Width, bh), bc);
                }

                // ── Noisy surface crest ────────────────────────────────────────
                int seed = r.X * 7 + r.Y * 13;
                OrganicPrimitives.DrawNoisyLine(sb, assets,
                    new Vector2(r.X, r.Y + 1),
                    new Vector2(r.Right, r.Y + 1),
                    WaterFoam * 0.85f, 2f,
                    amplitude: 1.5f, frequency: 1.4f, time: t, seed: seed, segments: 10);

                // ── Foam edge ─────────────────────────────────────────────────
                assets.DrawRect(sb, new Rectangle(r.X, r.Y, r.Width, 1), WaterFoam);

                // ── Caustic light streaks ──────────────────────────────────────
                for (int c = 0; c < 2; c++)
                {
                    float cp = AnimationClock.Loop(5f, c * 1.7f);
                    float cx  = r.X + (c * r.Width / 2) + MathF.Sin(t * 0.6f + c) * r.Width * 0.2f;
                    float cy2 = r.Y + 3 + MathF.Sin(t * 1.1f + c * 2f) * 2f;
                    float cw  = 18f + MathF.Sin(t * 0.9f + c) * 6f;
                    float ca  = (MathF.Sin(cp * MathF.PI) * 0.5f + 0.1f) * 0.55f;
                    OrganicPrimitives.DrawBezierQuad(sb, assets,
                        new Vector2(cx, cy2),
                        new Vector2(cx + cw * 0.5f, cy2 - 1.5f),
                        new Vector2(cx + cw, cy2 + 0.5f),
                        WaterShine * ca, 2f, 6);
                }

                // ── Shimmer dots ───────────────────────────────────────────────
                for (int i = 0; i < 3; i++)
                {
                    int s = seed + i * 17;
                    float shimPhase = t * 2.5f + i * 1.1f;
                    float alpha = (MathF.Sin(shimPhase) + 1f) * 0.5f * 0.55f;
                    int sx = r.X + 4 + (s % Math.Max(1, r.Width - 8));
                    int sy = r.Y + 4 + ((s * 3) % Math.Max(1, r.Height - 4));
                    assets.DrawRect(sb, new Rectangle(sx, sy, 2, 2), WaterShine * alpha);
                }
            }

            // ── Splash particles ───────────────────────────────────────────────
            for (int i = 0; i < _splashPool.Length; i++)
            {
                ref var p = ref _splashPool[i];
                if (!p.Active) continue;
                // Velocity-driven alpha: fades with both life and vertical slowdown
                float speed = p.Velocity.Length();
                float velFade = MathHelper.Clamp(speed / 80f, 0f, 1f);
                float alpha = Math.Min(1f, p.Life * 4f) * (0.5f + velFade * 0.5f);
                int size = Math.Max(1, (int)(3f * Math.Min(1f, p.Life * 3f)));
                assets.DrawRect(sb,
                    new Rectangle((int)p.Position.X - size / 2,
                                  (int)p.Position.Y - size / 2,
                                  size, size),
                    WaterSplash * alpha);
            }
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Validates that a candidate pool run has solid tile walls on both left and right
        /// sides, then trims the run inward until containment is satisfied.
        /// Also limits depth to the number of rows that remain fully contained.
        /// Discards the pool if the trimmed run is narrower than 2 tiles.
        /// </summary>
        private static void TryAddContainedPool(
            TileMap map, Random rng, List<WaterPoolRect> pools,
            int runStart, int runLen, int ty, int ts, int w, int h)
        {
            // ── Trim left boundary until a solid wall is immediately to the left ──
            int left  = runStart;
            int right = runStart + runLen - 1; // inclusive

            // Shrink left edge rightward until tile at (left-1, ty) is solid
            while (left <= right)
            {
                int leftNeighbor = left - 1;
                if (leftNeighbor >= 0 && TileProperties.IsSolid(map.GetTile(leftNeighbor, ty)))
                    break; // solid wall found on the left
                left++;
            }

            // Shrink right edge leftward until tile at (right+1, ty) is solid
            while (right >= left)
            {
                int rightNeighbor = right + 1;
                if (rightNeighbor < w && TileProperties.IsSolid(map.GetTile(rightNeighbor, ty)))
                    break; // solid wall found on the right
                right--;
            }

            int trimmedLen = right - left + 1;
            if (trimmedLen < 2) return; // too narrow after trimming — discard

            // ── Determine maximum contained depth ─────────────────────────────
            // For each depth row below the surface, check that solid walls still
            // exist at the same left/right boundaries.
            int maxDepth = 1 + rng.Next(3); // desired depth 1–3
            int actualDepth = 0;
            for (int d = 0; d < maxDepth; d++)
            {
                int depthRow = ty + d;
                if (depthRow >= h - 1) break;

                // Check left wall at this depth row
                int leftNeighbor  = left - 1;
                int rightNeighbor = right + 1;
                bool leftOk  = leftNeighbor  >= 0 && TileProperties.IsSolid(map.GetTile(leftNeighbor,  depthRow));
                bool rightOk = rightNeighbor < w  && TileProperties.IsSolid(map.GetTile(rightNeighbor, depthRow));

                if (!leftOk || !rightOk) break; // walls missing at this depth — stop
                actualDepth++;
            }

            if (actualDepth < 1) actualDepth = 1; // always at least 1 tile deep

            pools.Add(new WaterPoolRect
            {
                PixelRect  = new Rectangle(left * ts, ty * ts, trimmedLen * ts, ts),
                DepthTiles = actualDepth,
            });
        }

        private void SpawnSplash(Vector2 playerPos, int surfaceY, float entrySpeed)
        {
            int count = Math.Min(8, 3 + (int)(entrySpeed / 100f));
            for (int i = 0; i < count; i++)
            {
                ref var p = ref _splashPool[_splashHead % _splashPool.Length];
                _splashHead++;

                float angle = (float)(_rng.NextDouble() * Math.PI); // upward hemisphere
                float speed = 60f + (float)_rng.NextDouble() * entrySpeed * 0.4f;
                p.Position = new Vector2(
                    playerPos.X + (float)(_rng.NextDouble() - 0.5) * 16f,
                    surfaceY);
                p.Velocity = new Vector2(
                    MathF.Cos(angle) * speed * (float)(_rng.NextDouble() - 0.5) * 2f,
                    -MathF.Sin(angle) * speed);
                p.Life   = 0.25f + (float)_rng.NextDouble() * 0.2f;
                p.Active = true;
            }
        }
    }
}
