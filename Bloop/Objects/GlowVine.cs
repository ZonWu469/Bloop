using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using Bloop.Core;
using Bloop.Effects;
using Bloop.Gameplay;
using Bloop.Lighting;
using Bloop.Physics;
using Bloop.Rendering;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Objects
{
    /// <summary>
    /// A vine that becomes climbable only after being illuminated by the player's
    /// lantern for 2+ cumulative seconds.
    ///
    /// Behavior:
    ///   - Initially: sensor-only body (no climbing collision), dim visual
    ///   - While lit: accumulates illumination time
    ///   - After 2s cumulative illumination: activates — creates climbable body,
    ///     changes to bright glowing visual
    ///   - Once activated, stays climbable permanently for this level
    ///
    /// Visual:
    ///   - Inactive: dim teal rectangle, alpha 0.5
    ///   - Activating: brightens as illumination accumulates
    ///   - Active: bright green with pulsing glow
    /// </summary>
    public class GlowVine : WorldObject
    {
        // ── Dimensions ─────────────────────────────────────────────────────────
        private const int TileSize = 32;

        // ── Tuning ─────────────────────────────────────────────────────────────
        private const float RequiredIllumination = 2f;  // seconds
        private const float LanternRadius        = 200f; // pixels

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color ColorInactive  = new Color( 20,  60,  50);
        private static readonly Color ColorActivating = new Color( 40, 140,  80);
        private static readonly Color ColorActive    = new Color( 60, 220, 120);
        private static readonly Color ColorGlow      = new Color(120, 255, 160);
        private static readonly Color ColorOutline   = new Color( 20,  80,  40);

        // ── State ──────────────────────────────────────────────────────────────
        private float        _illuminationTime;
        private bool         _isActivated;
        private LightSource? _light;
        private Body? _climbableBody;
        private float _climbProgress01;
        private float _sporeTimer;
        private readonly ObjectParticleEmitter _spores = new ObjectParticleEmitter(24);

        // ── Dimensions ─────────────────────────────────────────────────────────
        private readonly int _heightPx;

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Create a glow vine centered at pixelPosition.
        /// tileHeight: number of tiles tall (2–5).
        /// </summary>
        public GlowVine(Vector2 pixelPosition, AetherWorld world, int tileHeight = 2)
            : base(pixelPosition, world)
        {
            _heightPx = tileHeight * TileSize;

            // Start as a sensor only — no climbing collision yet
            Body = BodyFactory.CreateSensorRect(world, pixelPosition, TileSize, _heightPx);
            Body.Tag = this;
        }

        public void SetLightSource(LightSource light)
        {
            _light = light;
            light.Intensity = 0f;
            light.Position  = PixelPosition;
        }

        // ── Update ─────────────────────────────────────────────────────────────

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _spores.Update(dt);

            // Decay climb-chase wave
            _climbProgress01 = MathHelper.Clamp(_climbProgress01 - dt * 0.6f, 0f, 1f);

            if (_isActivated)
            {
                _sporeTimer -= dt;
                if (_sporeTimer <= 0f)
                {
                    _sporeTimer = 0.4f;
                    int seed = (int)(PixelPosition.X * 11 + PixelPosition.Y * 7 + _spores.ActiveCount);
                    float fx = PixelPosition.X + NoiseHelpers.HashSigned(seed) * 10f;
                    float fy = PixelPosition.Y + _heightPx / 2f - NoiseHelpers.Hash01(seed + 3) * _heightPx;
                    _spores.Emit(new Vector2(fx, fy),
                        new Vector2(NoiseHelpers.HashSigned(seed + 9) * 4f, -6f - NoiseHelpers.Hash01(seed + 5) * 5f),
                        new Color(170, 255, 200), life: 2f, size: 2f, gravity: -4f, drag: 0.3f);
                }
            }
        }

        /// <summary>
        /// Update illumination state from the player reference.
        /// Called by Level.Update() each frame.
        /// </summary>
        public void UpdateIllumination(Player player)
        {
            if (!_isActivated)
            {
                bool lit = IsLitByLantern(player, PixelPosition, LanternRadius);
                if (lit)
                {
                    float dt = 1f / 60f;
                    _illuminationTime += dt;

                    if (_illuminationTime >= RequiredIllumination)
                        Activate();
                }
                return;
            }

            // Activated: track player climbing to drive photophore-chase animation.
            if (player.State == PlayerState.Climbing)
            {
                float top = PixelPosition.Y - _heightPx / 2f;
                float bot = PixelPosition.Y + _heightPx / 2f;
                float dx = MathF.Abs(player.PixelPosition.X - PixelPosition.X);
                if (dx < 24f && player.PixelPosition.Y >= top - 8f && player.PixelPosition.Y <= bot + 8f)
                {
                    _climbProgress01 = MathHelper.Clamp((bot - player.PixelPosition.Y) / _heightPx, 0f, 1f);
                }
            }
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        public override void Draw(SpriteBatch spriteBatch, AssetManager assets)
        {
            float progress = _illuminationTime / RequiredIllumination;
            _spores.Draw(spriteBatch, assets);
            WorldObjectRenderer.DrawGlowVine(
                spriteBatch, assets,
                PixelPosition, _heightPx,
                _isActivated, progress,
                _climbProgress01);
        }

        // ── Bounds ─────────────────────────────────────────────────────────────

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - TileSize / 2f - 8),
            (int)(PixelPosition.Y - _heightPx / 2f - 8),
            TileSize + 16, _heightPx + 16);

        // ── Private helpers ────────────────────────────────────────────────────

        private void Activate()
        {
            if (_isActivated) return;
            _isActivated = true;

            // Remove the sensor body
            if (Body != null)
            {
                World.Remove(Body);
                Body = null;
            }

            // Create a proper climbable collision body
            _climbableBody = BodyFactory.CreateStaticRect(
                World, PixelPosition,
                TileSize, _heightPx,
                CollisionCategories.Climbable);
            _climbableBody.Tag = this;

            // Store as the main body so PixelPosition reads correctly
            Body = _climbableBody;

            if (_light != null)
            {
                _light.Intensity = 1.2f;
                _light.Radius    = 100f;
            }
        }
    }
}
