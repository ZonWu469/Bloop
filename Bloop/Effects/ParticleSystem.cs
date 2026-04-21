using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.World;

namespace Bloop.Effects
{
    /// <summary>
    /// Pooled ambient particle system for cave atmosphere effects.
    ///
    /// Emitter types:
    ///   • Dust Motes    — tiny drifting specks throughout the viewport
    ///   • Rain Streaks  — vertical streaks in large open caverns
    ///   • Waterfalls    — column drops + base mist on exposed wall faces
    ///   • Water Drips   — ceiling drips from wet tiles
    ///   • Cave Spores   — rising glowing dots near GlowVines / VentFlowers
    ///
    /// All particles are viewport-culled. Emitter positions are computed once
    /// per level load and stored in lists for efficient per-frame spawning.
    /// </summary>
    public class ParticleSystem
    {
        // ── Pool ───────────────────────────────────────────────────────────────
        private const int PoolSize = 1200;
        private readonly Particle[] _pool = new Particle[PoolSize];
        private int _poolHead = 0; // next slot to try (ring buffer)

        // ── Emitter data (computed once per level) ─────────────────────────────
        /// <summary>Pixel X positions of rain column emitters (top of large caverns).</summary>
        private readonly List<Vector2> _rainEmitters      = new();
        /// <summary>Pixel positions of waterfall emitters (exposed wall tops).</summary>
        private readonly List<Vector2> _waterfallEmitters = new();
        /// <summary>Pixel positions of ceiling drip emitters.</summary>
        private readonly List<Vector2> _dripEmitters      = new();
        /// <summary>Pixel positions of spore emitters (GlowVine / VentFlower tiles).</summary>
        private readonly List<Vector2> _sporeEmitters     = new();

        // ── Spawn timers ───────────────────────────────────────────────────────
        private float _dustTimer;
        private float _rainTimer;
        private float _waterfallTimer;
        private float _dripTimer;
        private float _sporeTimer;

        // ── RNG ────────────────────────────────────────────────────────────────
        private readonly Random _rng;

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color DustColor      = new Color(200, 190, 170);
        private static readonly Color RainColor      = new Color(140, 180, 220);
        private static readonly Color WaterfallColor = new Color(120, 170, 220);
        private static readonly Color MistColor      = new Color(160, 200, 230);
        private static readonly Color DripColor      = new Color(100, 150, 200);
        private static readonly Color SporeColor     = new Color(120, 240, 160);

        // ── Constructor ────────────────────────────────────────────────────────

        public ParticleSystem(int seed)
        {
            _rng = new Random(seed ^ 0xFACE);
        }

        // ── Level setup ────────────────────────────────────────────────────────

        /// <summary>
        /// Scan the tile map and compute emitter positions.
        /// Call once after a new level is loaded.
        /// </summary>
        public void SetupEmitters(TileMap map)
        {
            _rainEmitters.Clear();
            _waterfallEmitters.Clear();
            _dripEmitters.Clear();
            _sporeEmitters.Clear();

            int w = map.Width;
            int h = map.Height;
            int ts = TileMap.TileSize;

            for (int tx = 1; tx < w - 1; tx++)
            {
                for (int ty = 1; ty < h - 1; ty++)
                {
                    var tile = map.GetTile(tx, ty);

                    // ── Rain emitters: top of tall open columns ────────────────
                    // Solid tile with 6+ consecutive empty tiles below it
                    if (TileProperties.IsSolid(tile) && map.GetTile(tx, ty + 1) == TileType.Empty)
                    {
                        int emptyBelow = 0;
                        for (int dy = 1; dy <= 8 && ty + dy < h; dy++)
                        {
                            if (map.GetTile(tx, ty + dy) == TileType.Empty) emptyBelow++;
                            else break;
                        }
                        if (emptyBelow >= 6 && _rng.NextDouble() < 0.25)
                        {
                            _rainEmitters.Add(new Vector2(
                                tx * ts + ts / 2f,
                                (ty + 1) * ts));
                        }
                    }

                    // ── Waterfall emitters: exposed wall top with open space ───
                    // Solid tile with empty to the right AND 3+ empty tiles below
                    if (TileProperties.IsSolid(tile) &&
                        map.GetTile(tx + 1, ty) == TileType.Empty &&
                        map.GetTile(tx, ty - 1) == TileType.Empty)
                    {
                        int emptyBelow = 0;
                        for (int dy = 1; dy <= 5 && ty + dy < h; dy++)
                        {
                            if (map.GetTile(tx + 1, ty + dy) == TileType.Empty) emptyBelow++;
                            else break;
                        }
                        if (emptyBelow >= 3 && _rng.NextDouble() < 0.12)
                        {
                            _waterfallEmitters.Add(new Vector2(
                                (tx + 1) * ts,
                                ty * ts + ts / 2f));
                        }
                    }

                    // ── Drip emitters: solid ceiling with empty below ──────────
                    if (TileProperties.IsSolid(tile) &&
                        map.GetTile(tx, ty + 1) == TileType.Empty &&
                        _rng.NextDouble() < 0.04)
                    {
                        _dripEmitters.Add(new Vector2(
                            tx * ts + ts / 2f,
                            (ty + 1) * ts));
                    }

                    // ── Spore emitters: climbable tiles (GlowVine surfaces) ────
                    if (tile == TileType.Climbable && _rng.NextDouble() < 0.3)
                    {
                        _sporeEmitters.Add(new Vector2(
                            tx * ts + ts / 2f,
                            ty * ts + ts / 2f));
                    }
                }
            }
        }

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Advance all live particles and spawn new ones from emitters.
        /// visibleBounds is used to cull spawning to the viewport area.
        /// playerVelocity is used to push dust motes away from the player.
        /// </summary>
        public void Update(float dt, Rectangle visibleBounds, Vector2 playerPixelPos, Vector2 playerVelocity)
        {
            // ── Tick live particles ────────────────────────────────────────────
            for (int i = 0; i < PoolSize; i++)
            {
                ref var p = ref _pool[i];
                if (!p.IsAlive) continue;

                p.Age -= dt;
                if (p.Age <= 0f) { p.Age = 0f; continue; }

                p.Position += p.Velocity * dt;

                // Fade out in last 30% of lifetime
                float lifeRatio = p.Age / p.Lifetime;
                p.Alpha = lifeRatio < 0.3f ? lifeRatio / 0.3f : 1f;

                // Kind-specific behaviour
                switch (p.Kind)
                {
                    case ParticleKind.DustMote:
                        // Slight horizontal sway
                        p.Velocity.X += MathF.Sin(p.Age * 3f) * 0.5f * dt;
                        // Push away from player
                        Vector2 toPlayer = playerPixelPos - p.Position;
                        if (toPlayer.LengthSquared() < 80f * 80f && toPlayer.LengthSquared() > 0.01f)
                        {
                            toPlayer.Normalize();
                            p.Velocity -= toPlayer * (playerVelocity.Length() * 0.002f);
                        }
                        break;

                    case ParticleKind.WaterfallMist:
                        // Spread horizontally, slow down
                        p.Velocity.X *= 0.96f;
                        p.Velocity.Y *= 0.94f;
                        break;

                    case ParticleKind.RainSplash:
                    case ParticleKind.DripSplash:
                        // Gravity on splash droplets
                        p.Velocity.Y += 120f * dt;
                        break;
                }
            }

            // ── Spawn dust motes ───────────────────────────────────────────────
            _dustTimer -= dt;
            if (_dustTimer <= 0f)
            {
                _dustTimer = 0.08f + (float)_rng.NextDouble() * 0.12f;
                SpawnDustMote(visibleBounds);
            }

            // ── Spawn rain ─────────────────────────────────────────────────────
            if (_rainEmitters.Count > 0)
            {
                _rainTimer -= dt;
                if (_rainTimer <= 0f)
                {
                    _rainTimer = 0.04f + (float)_rng.NextDouble() * 0.06f;
                    SpawnRain(visibleBounds);
                }
            }

            // ── Spawn waterfall ────────────────────────────────────────────────
            if (_waterfallEmitters.Count > 0)
            {
                _waterfallTimer -= dt;
                if (_waterfallTimer <= 0f)
                {
                    _waterfallTimer = 0.02f + (float)_rng.NextDouble() * 0.03f;
                    SpawnWaterfall(visibleBounds);
                }
            }

            // ── Spawn drips ────────────────────────────────────────────────────
            if (_dripEmitters.Count > 0)
            {
                _dripTimer -= dt;
                if (_dripTimer <= 0f)
                {
                    _dripTimer = 0.3f + (float)_rng.NextDouble() * 0.7f;
                    SpawnDrip(visibleBounds);
                }
            }

            // ── Spawn spores ───────────────────────────────────────────────────
            if (_sporeEmitters.Count > 0)
            {
                _sporeTimer -= dt;
                if (_sporeTimer <= 0f)
                {
                    _sporeTimer = 0.15f + (float)_rng.NextDouble() * 0.25f;
                    SpawnSpore(visibleBounds);
                }
            }
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw all live particles. Call inside a SpriteBatch.Begin/End block
        /// with the camera transform applied.
        /// </summary>
        public void Draw(SpriteBatch sb, AssetManager assets)
        {
            for (int i = 0; i < PoolSize; i++)
            {
                ref var p = ref _pool[i];
                if (!p.IsAlive) continue;

                int pw = Math.Max(1, (int)p.Width);
                int ph = Math.Max(1, (int)p.Height);
                var rect = new Rectangle((int)p.Position.X, (int)p.Position.Y, pw, ph);
                assets.DrawRect(sb, rect, p.Color * p.Alpha);
            }
        }

        // ── Spawn helpers ──────────────────────────────────────────────────────

        private void SpawnDustMote(Rectangle vis)
        {
            float x = vis.X + (float)_rng.NextDouble() * vis.Width;
            float y = vis.Y + (float)_rng.NextDouble() * vis.Height;
            float vx = ((float)_rng.NextDouble() - 0.5f) * 6f;
            float vy = 4f + (float)_rng.NextDouble() * 8f;
            float life = 4f + (float)_rng.NextDouble() * 4f;

            Emit(new Particle
            {
                Kind     = ParticleKind.DustMote,
                Position = new Vector2(x, y),
                Velocity = new Vector2(vx, vy),
                Color    = DustColor,
                Width    = 1f,
                Height   = 1f,
                Alpha    = 1f,
                Lifetime = life,
                Age      = life,
            });
        }

        private void SpawnRain(Rectangle vis)
        {
            if (_rainEmitters.Count == 0) return;
            var emitter = _rainEmitters[_rng.Next(_rainEmitters.Count)];
            if (!vis.Contains((int)emitter.X, (int)emitter.Y)) return;

            float x    = emitter.X + ((float)_rng.NextDouble() - 0.5f) * 24f;
            float vy   = 180f + (float)_rng.NextDouble() * 80f;
            float life = 1.2f + (float)_rng.NextDouble() * 0.8f;
            float alpha = 0.35f + (float)_rng.NextDouble() * 0.3f;

            Emit(new Particle
            {
                Kind     = ParticleKind.RainStreak,
                Position = new Vector2(x, emitter.Y),
                Velocity = new Vector2(((float)_rng.NextDouble() - 0.5f) * 4f, vy),
                Color    = RainColor * alpha,
                Width    = 1f,
                Height   = 10f + (float)_rng.NextDouble() * 6f,
                Alpha    = 1f,
                Lifetime = life,
                Age      = life,
            });
        }

        private void SpawnWaterfall(Rectangle vis)
        {
            if (_waterfallEmitters.Count == 0) return;
            var emitter = _waterfallEmitters[_rng.Next(_waterfallEmitters.Count)];
            if (!vis.Contains((int)emitter.X, (int)emitter.Y)) return;

            float x    = emitter.X + ((float)_rng.NextDouble() - 0.5f) * 5f;
            float vy   = 120f + (float)_rng.NextDouble() * 60f;
            float life = 0.8f + (float)_rng.NextDouble() * 0.5f;

            Emit(new Particle
            {
                Kind     = ParticleKind.WaterfallDrop,
                Position = new Vector2(x, emitter.Y),
                Velocity = new Vector2(((float)_rng.NextDouble() - 0.5f) * 3f, vy),
                Color    = WaterfallColor,
                Width    = 2f,
                Height   = 4f + (float)_rng.NextDouble() * 4f,
                Alpha    = 1f,
                Lifetime = life,
                Age      = life,
            });

            // Spawn mist at base (slightly below emitter)
            if (_rng.NextDouble() < 0.3f)
            {
                float mistLife = 0.6f + (float)_rng.NextDouble() * 0.4f;
                Emit(new Particle
                {
                    Kind     = ParticleKind.WaterfallMist,
                    Position = new Vector2(emitter.X + ((float)_rng.NextDouble() - 0.5f) * 8f,
                                           emitter.Y + 20f + (float)_rng.NextDouble() * 10f),
                    Velocity = new Vector2(((float)_rng.NextDouble() - 0.5f) * 20f, -5f),
                    Color    = MistColor,
                    Width    = 3f,
                    Height   = 3f,
                    Alpha    = 0.5f,
                    Lifetime = mistLife,
                    Age      = mistLife,
                });
            }
        }

        private void SpawnDrip(Rectangle vis)
        {
            if (_dripEmitters.Count == 0) return;
            var emitter = _dripEmitters[_rng.Next(_dripEmitters.Count)];
            if (!vis.Contains((int)emitter.X, (int)emitter.Y)) return;

            float life = 1.5f + (float)_rng.NextDouble() * 1.0f;
            Emit(new Particle
            {
                Kind     = ParticleKind.WaterDrip,
                Position = new Vector2(emitter.X, emitter.Y),
                Velocity = new Vector2(0f, 40f + (float)_rng.NextDouble() * 20f),
                Color    = DripColor,
                Width    = 2f,
                Height   = 4f,
                Alpha    = 1f,
                Lifetime = life,
                Age      = life,
            });
        }

        private void SpawnSpore(Rectangle vis)
        {
            if (_sporeEmitters.Count == 0) return;
            var emitter = _sporeEmitters[_rng.Next(_sporeEmitters.Count)];
            if (!vis.Contains((int)emitter.X, (int)emitter.Y)) return;

            float life = 3f + (float)_rng.NextDouble() * 3f;
            Emit(new Particle
            {
                Kind     = ParticleKind.CaveSpore,
                Position = new Vector2(
                    emitter.X + ((float)_rng.NextDouble() - 0.5f) * 16f,
                    emitter.Y + ((float)_rng.NextDouble() - 0.5f) * 8f),
                Velocity = new Vector2(
                    ((float)_rng.NextDouble() - 0.5f) * 6f,
                    -(4f + (float)_rng.NextDouble() * 8f)),
                Color    = SporeColor,
                Width    = 2f,
                Height   = 2f,
                Alpha    = 1f,
                Lifetime = life,
                Age      = life,
            });
        }

        // ── Pool management ────────────────────────────────────────────────────

        private void Emit(Particle p)
        {
            // Find a dead slot starting from _poolHead (ring buffer)
            for (int i = 0; i < PoolSize; i++)
            {
                int idx = (_poolHead + i) % PoolSize;
                if (!_pool[idx].IsAlive)
                {
                    _pool[idx] = p;
                    _poolHead  = (idx + 1) % PoolSize;
                    return;
                }
            }
            // Pool full: overwrite oldest (just advance head)
            _pool[_poolHead] = p;
            _poolHead = (_poolHead + 1) % PoolSize;
        }
    }
}
