using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Gameplay;
using Bloop.Physics;
using Bloop.Rendering;

namespace Bloop.Effects
{
    /// <summary>
    /// Renders a motion trail behind the player based on their current state.
    ///
    /// Trail types (3.5):
    ///   Sliding   — orange sparks spraying backward along the slide direction
    ///   Swinging  — blue rope-arc ghost trail (fading copies of the rope line)
    ///   Launching — white/cyan speed-burst streaks (complements PlayerRenderer speed lines)
    ///   WallJump  — brief yellow flash on the wall contact point
    ///   Falling   — subtle grey dust motes trailing upward
    /// </summary>
    public class TrailEffect
    {
        // ── Trail particle pool ────────────────────────────────────────────────
        private const int PoolSize = 256;

        private struct TrailParticle
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public Color   Color;
            public float   Size;
            public float   Life;      // remaining lifetime (seconds)
            public float   MaxLife;   // total lifetime (seconds)
            public bool    Active;
        }

        private readonly TrailParticle[] _pool = new TrailParticle[PoolSize];
        private int _poolHead = 0;

        // ── Spawn timers ───────────────────────────────────────────────────────
        private float _sparkTimer    = 0f;
        private float _dustTimer     = 0f;
        private float _launchTimer   = 0f;

        // ── Wall-jump flash ────────────────────────────────────────────────────
        private Vector2 _wallFlashPos;
        private float   _wallFlashTimer = 0f;
        private const float WallFlashDuration = 0.18f;

        // ── Palette ────────────────────────────────────────────────────────────
        private static readonly Color ColSpark1  = new Color(255, 160,  40);
        private static readonly Color ColSpark2  = new Color(255, 220,  80);
        private static readonly Color ColDust    = new Color(180, 180, 180);
        private static readonly Color ColLaunch1 = new Color(160, 220, 255);
        private static readonly Color ColLaunch2 = new Color(255, 255, 255);
        private static readonly Color ColWallFlash = new Color(255, 240, 120);

        // ── Random ────────────────────────────────────────────────────────────
        private readonly Random _rng = new Random();

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Call once per frame to update all active trail particles and spawn new ones.
        /// </summary>
        public void Update(float dt, Player player)
        {
            // Tick existing particles
            for (int i = 0; i < PoolSize; i++)
            {
                ref var p = ref _pool[i];
                if (!p.Active) continue;

                p.Life -= dt;
                if (p.Life <= 0f)
                {
                    p.Active = false;
                    continue;
                }

                p.Position += p.Velocity * dt;
                // Gravity on sparks
                p.Velocity.Y += 120f * dt;
            }

            // Tick wall flash
            if (_wallFlashTimer > 0f)
                _wallFlashTimer -= dt;

            // Spawn new particles based on player state
            Vector2 pos = player.PixelPosition;
            Vector2 vel = player.PixelVelocity;

            switch (player.State)
            {
                case PlayerState.Sliding:
                    _sparkTimer -= dt;
                    if (_sparkTimer <= 0f)
                    {
                        _sparkTimer = 0.025f; // 40 sparks/sec
                        SpawnSlidingSparks(pos, vel);
                    }
                    break;

                case PlayerState.Falling:
                    _dustTimer -= dt;
                    if (_dustTimer <= 0f)
                    {
                        _dustTimer = 0.06f; // ~16 dust/sec
                        SpawnFallingDust(pos, vel);
                    }
                    break;

                case PlayerState.Launching:
                    _launchTimer -= dt;
                    if (_launchTimer <= 0f)
                    {
                        _launchTimer = 0.018f; // ~55 streaks/sec
                        SpawnLaunchStreaks(pos, vel, player.FacingDirection);
                    }
                    break;

                case PlayerState.WallJumping:
                    // Trigger wall flash once on state entry (detected by timer being 0)
                    if (_wallFlashTimer <= 0f)
                    {
                        _wallFlashPos   = pos + new Vector2(
                            player.IsTouchingWallRight ? 12f : -12f, 0f);
                        _wallFlashTimer = WallFlashDuration;
                        SpawnWallJumpBurst(pos, player.IsTouchingWallRight ? -1 : 1);
                    }
                    break;

                default:
                    // Reset timers when not in trail-emitting states
                    _sparkTimer  = 0f;
                    _dustTimer   = 0f;
                    _launchTimer = 0f;
                    break;
            }
        }

        /// <summary>
        /// Draw all active trail particles. Call inside the world SpriteBatch block.
        /// </summary>
        public void Draw(SpriteBatch sb, AssetManager assets)
        {
            // Wall flash
            if (_wallFlashTimer > 0f)
            {
                float alpha = _wallFlashTimer / WallFlashDuration;
                int flashSize = (int)(20f * alpha);
                assets.DrawRect(sb,
                    new Rectangle(
                        (int)_wallFlashPos.X - flashSize / 2,
                        (int)_wallFlashPos.Y - flashSize / 2,
                        flashSize, flashSize),
                    ColWallFlash * alpha);
            }

            // Particles
            for (int i = 0; i < PoolSize; i++)
            {
                ref var p = ref _pool[i];
                if (!p.Active) continue;

                float t = p.Life / p.MaxLife; // 1 = fresh, 0 = dying
                int size = Math.Max(1, (int)(p.Size * t));
                assets.DrawRect(sb,
                    new Rectangle((int)p.Position.X - size / 2,
                                  (int)p.Position.Y - size / 2,
                                  size, size),
                    p.Color * t);
            }
        }

        // ── Spawn helpers ──────────────────────────────────────────────────────

        private void SpawnSlidingSparks(Vector2 pos, Vector2 vel)
        {
            // 2–3 sparks per burst, spraying backward and slightly upward
            int count = 2 + _rng.Next(2);
            for (int i = 0; i < count; i++)
            {
                float vx = -vel.X * 0.3f + (float)(_rng.NextDouble() - 0.5) * 80f;
                float vy = -60f - (float)_rng.NextDouble() * 80f;
                Emit(
                    pos + new Vector2((float)(_rng.NextDouble() - 0.5) * 8f, 8f),
                    new Vector2(vx, vy),
                    _rng.NextDouble() < 0.5 ? ColSpark1 : ColSpark2,
                    size: 3f,
                    life: 0.25f + (float)_rng.NextDouble() * 0.2f);
            }
        }

        private void SpawnFallingDust(Vector2 pos, Vector2 vel)
        {
            // 1 dust mote drifting upward relative to fall speed
            float vy = -Math.Abs(vel.Y) * 0.08f - 20f;
            Emit(
                pos + new Vector2((float)(_rng.NextDouble() - 0.5) * 10f, -8f),
                new Vector2((float)(_rng.NextDouble() - 0.5) * 15f, vy),
                ColDust,
                size: 2f,
                life: 0.4f + (float)_rng.NextDouble() * 0.3f);
        }

        private void SpawnLaunchStreaks(Vector2 pos, Vector2 vel, int facing)
        {
            // 2 streaks per burst, shooting backward from player
            for (int i = 0; i < 2; i++)
            {
                float vx = -facing * (150f + (float)_rng.NextDouble() * 100f);
                float vy = (float)(_rng.NextDouble() - 0.5) * 40f;
                Emit(
                    pos + new Vector2(-facing * 8f, (float)(_rng.NextDouble() - 0.5) * 12f),
                    new Vector2(vx, vy),
                    _rng.NextDouble() < 0.5 ? ColLaunch1 : ColLaunch2,
                    size: 4f,
                    life: 0.12f + (float)_rng.NextDouble() * 0.08f);
            }
        }

        private void SpawnWallJumpBurst(Vector2 pos, int kickDir)
        {
            // 6 burst particles spraying away from the wall
            for (int i = 0; i < 6; i++)
            {
                float vx = kickDir * (80f + (float)_rng.NextDouble() * 120f);
                float vy = -60f - (float)_rng.NextDouble() * 80f;
                Emit(
                    pos + new Vector2(-kickDir * 10f, (float)(_rng.NextDouble() - 0.5) * 16f),
                    new Vector2(vx, vy),
                    ColWallFlash,
                    size: 3f,
                    life: 0.20f + (float)_rng.NextDouble() * 0.15f);
            }
        }

        private void Emit(Vector2 position, Vector2 velocity, Color color, float size, float life)
        {
            ref var p = ref _pool[_poolHead % PoolSize];
            _poolHead++;

            p.Position = position;
            p.Velocity = velocity;
            p.Color    = color;
            p.Size     = size;
            p.Life     = life;
            p.MaxLife  = life;
            p.Active   = true;
        }
    }
}
