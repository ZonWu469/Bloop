using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Contacts;
using Bloop.Core;
using Bloop.Effects;
using Bloop.Gameplay;
using Bloop.Physics;
using Bloop.Rendering;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Objects
{
    /// <summary>
    /// An air pocket vent flower. Standing inside its sensor area for 5 seconds
    /// fully refills the player's breath meter AND lantern fuel.
    ///
    /// Behavior:
    ///   - Sensor area: 64×64 pixels
    ///   - Tracks standing time while player is in zone
    ///   - After 5 seconds: calls PlayerStats.RefillFromVent(), resets timer
    ///   - 30-second cooldown before it can refill again
    ///   - Emits a soft ambient glow (drawn as a radial gradient overlay)
    ///
    /// Visual: bright cyan-green layered rectangles with pulsing glow.
    /// </summary>
    public class VentFlower : WorldObject
    {
        // ── Dimensions ─────────────────────────────────────────────────────────
        private const int VisualWidth  = 32;
        private const int VisualHeight = 48;
        private const int SensorSize   = 64;

        // ── Tuning ─────────────────────────────────────────────────────────────
        private const float RefillTime = 5f;   // seconds standing to trigger refill
        private const float Cooldown   = 30f;  // seconds before next refill

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color ColorBase   = new Color( 40, 180, 120);
        private static readonly Color ColorGlow   = new Color( 80, 255, 180);
        private static readonly Color ColorPetal  = new Color(100, 220, 160);
        private static readonly Color ColorCenter = new Color(200, 255, 220);
        private static readonly Color ColorAura   = new Color( 60, 200, 140);

        // ── State ──────────────────────────────────────────────────────────────
        private float            _standingTime;
        private float            _cooldownTimer;
        private bool             _playerInZone;
        private Gameplay.Player? _currentPlayer;

        private readonly ObjectParticleEmitter _heat = new ObjectParticleEmitter(24);
        private float _heatTimer;
        private static readonly Color HeatColor = new Color(180, 255, 210);

        // ── Constructor ────────────────────────────────────────────────────────
        public VentFlower(Vector2 pixelPosition, AetherWorld world)
            : base(pixelPosition, world)
        {
            Body = BodyFactory.CreateSensorRect(world, pixelPosition, SensorSize, SensorSize);
            Body.Tag = this;

            foreach (var fixture in Body.FixtureList)
            {
                fixture.OnCollision  += OnCollision;
                fixture.OnSeparation += OnSeparation;
            }
        }

        public override bool WantsPlayerContact => true;

        // ── Update ─────────────────────────────────────────────────────────────

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _heat.Update(dt);

            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= dt;
                return;
            }

            // Heat shimmer emitter — rate doubles when player inside
            _heatTimer -= dt;
            float heatRate = _playerInZone ? 8f : 4f;
            if (_heatTimer <= 0f)
            {
                _heatTimer = 1f / heatRate;
                int s = (int)(PixelPosition.X * 3 + PixelPosition.Y * 7 + _heat.ActiveCount * 13);
                float hx = PixelPosition.X + NoiseHelpers.HashSigned(s) * 8f;
                _heat.Emit(new Vector2(hx, PixelPosition.Y - 4f),
                    new Vector2(NoiseHelpers.HashSigned(s + 3) * 5f, -18f - NoiseHelpers.Hash01(s + 7) * 10f),
                    HeatColor, life: 0.7f, size: 2f, gravity: -5f, drag: 1f);
            }

            if (_playerInZone)
            {
                _standingTime += dt;

                if (_standingTime >= RefillTime)
                {
                    _currentPlayer?.Stats.RefillFromVent();
                    _standingTime  = 0f;
                    _cooldownTimer = Cooldown;
                    // TODO: play vent flower refill sound effect
                }
            }
            else
            {
                // Drain standing time slowly when player leaves
                _standingTime = Math.Max(0f, _standingTime - dt * 2f);
            }
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        public override void Draw(SpriteBatch spriteBatch, AssetManager assets)
        {
            _heat.Draw(spriteBatch, assets);
            WorldObjectRenderer.DrawVentFlower(
                spriteBatch, assets,
                PixelPosition,
                _cooldownTimer > 0f,
                _standingTime / RefillTime,
                _playerInZone);
        }

        // ── Bounds ─────────────────────────────────────────────────────────────

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - SensorSize / 2f),
            (int)(PixelPosition.Y - SensorSize / 2f),
            SensorSize, SensorSize);

        // ── Player contact ─────────────────────────────────────────────────────

        public override void OnPlayerContact(Player player)
        {
            _playerInZone  = true;
            _currentPlayer = player;
        }

        public override void OnPlayerSeparate(Player player)
        {
            _playerInZone = false;
        }

        // ── Collision callbacks ────────────────────────────────────────────────

        private bool OnCollision(Fixture sender, Fixture other, Contact contact)
        {
            if (other.Body?.Tag is Player player)
                OnPlayerContact(player);
            return true;
        }

        private void OnSeparation(Fixture sender, Fixture other, Contact contact)
        {
            if (other.Body?.Tag is Player player)
                OnPlayerSeparate(player);
        }
    }
}
