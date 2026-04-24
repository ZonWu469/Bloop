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
    /// A platform that starts a 5-second countdown when touched by the player,
    /// then vanishes (Aether body removed). Supports domino chain linking:
    /// when triggered, it can notify a DominoPlatformChain to cascade to the next platform.
    ///
    /// Visual: orange-brown rectangle (64×8 pixels) that fades out during countdown.
    /// If the player's lantern is active when triggered, sets SpawnSporeLight = true
    /// so the Level can create a temporary spore light (Phase 9 integration point).
    /// </summary>
    public class DisappearingPlatform : WorldObject
    {
        // ── Dimensions ─────────────────────────────────────────────────────────
        public const int PlatformWidth  = 128; // 2× larger
        public const int PlatformHeight = 16;  // 2× larger

        // ── Tuning ─────────────────────────────────────────────────────────────
        private const float CountdownDuration = 5f;

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color ColorNormal    = new Color(180, 120, 60);
        private static readonly Color ColorTriggered = new Color(220, 80,  40);
        private static readonly Color ColorOutline   = new Color(100, 60,  20);

        // ── State ──────────────────────────────────────────────────────────────
        private float _countdownTimer;
        private bool  _isTriggered;
        private float _alpha = 1f;

        // ── Domino chain ───────────────────────────────────────────────────────
        /// <summary>Chain ID (-1 = standalone).</summary>
        public int ChainId    { get; set; } = -1;
        /// <summary>Order within the chain (0 = first to trigger).</summary>
        public int ChainOrder { get; set; } = -1;

        /// <summary>
        /// Callback invoked when this platform is triggered.
        /// DominoPlatformChain wires this up to cascade to the next platform.
        /// </summary>
        public Action<DisappearingPlatform>? OnTriggered;

        // ── Spore light flag ───────────────────────────────────────────────────
        /// <summary>
        /// Set to true when triggered while lit by the player's lantern.
        /// Level.cs checks this flag to spawn a temporary spore light (Phase 9).
        /// </summary>
        public bool SpawnSporeLight { get; private set; }

        private readonly ObjectParticleEmitter _spores = new ObjectParticleEmitter(32);
        private float _sporeTimer;
        private static readonly Color SporeColor = new Color(220, 175, 110);

        // ── Sensor fixture ─────────────────────────────────────────────────────
        private Fixture? _sensorFixture;

        // ── Contact tracking ──────────────────────────────────────────────────
        // Remember the player touching this platform so we can forcibly clear
        // their ground contacts the instant our body is removed — Aether may
        // not fire OnSeparation when a body is disposed mid-contact, which
        // would leave the player floating on invisible ground for a frame.
        private Gameplay.Player? _contactingPlayer;

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Create a disappearing platform centered at pixelPosition.
        /// </summary>
        public DisappearingPlatform(Vector2 pixelPosition, AetherWorld world)
            : base(pixelPosition, world)
        {
            // Static collision body (platform top edge)
            Body = BodyFactory.CreateStaticRect(
                world, pixelPosition,
                PlatformWidth, PlatformHeight,
                CollisionCategories.DisappearingPlatform);

            Body.Tag = this;

            // Sensor fixture slightly larger than the platform for touch detection
            _sensorFixture = BodyFactory.CreateSensorRect(
                world, pixelPosition,
                PlatformWidth + 8, PlatformHeight + 16).FixtureList[0];

            // Wire collision callback on the sensor
            _sensorFixture.OnCollision += OnSensorCollision;
        }

        public override bool WantsPlayerContact => true;

        public override void OnPlayerContact(Gameplay.Player player)
        {
            _contactingPlayer = player;
            if (_isTriggered || IsDestroyed) return;
            StartCountdown(spawnSporeLight: IsLitByLantern(player, PixelPosition));
        }

        public override void OnPlayerSeparate(Gameplay.Player player)
        {
            if (ReferenceEquals(_contactingPlayer, player))
                _contactingPlayer = null;
        }

        // ── Trigger from chain ─────────────────────────────────────────────────

        /// <summary>
        /// Start the countdown without requiring direct player contact.
        /// Called by DominoPlatformChain to cascade the effect.
        /// </summary>
        public void TriggerFromChain()
        {
            if (_isTriggered || IsDestroyed) return;
            StartCountdown(spawnSporeLight: false);
        }

        // ── Update ─────────────────────────────────────────────────────────────

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _spores.Update(dt);

            if (!_isTriggered || IsDestroyed) return;

            _countdownTimer -= dt;

            // Fade alpha from 1 → 0 over the countdown
            _alpha = Math.Max(0f, _countdownTimer / CountdownDuration);

            // Spore drift emission — rate spikes as dissolution approaches
            _sporeTimer -= dt;
            float sporeRate = _alpha > 0.5f ? 1.2f : 0.3f + (1f - _alpha) * 0.8f;
            if (_sporeTimer <= 0f)
            {
                _sporeTimer = 1f / sporeRate;
                int tileHash = (int)(PixelPosition.X * 3 + PixelPosition.Y * 7);
                float fx = PixelPosition.X - PlatformWidth / 2f + 6
                           + NoiseHelpers.Hash01(tileHash + _spores.ActiveCount) * (PlatformWidth - 12);
                _spores.Emit(new Vector2(fx, PixelPosition.Y + 4f),
                    new Vector2(NoiseHelpers.HashSigned(tileHash + _spores.ActiveCount * 7) * 4f, 6f + _alpha * 8f),
                    SporeColor, life: 0.9f + _alpha * 0.5f, size: 2f, gravity: 15f, drag: 0.8f);
            }

            // Dissolution fragments when nearly gone
            if (_alpha < 0.3f && (int)(AnimationClock.Time * 12f) % 2 == 0)
            {
                int th = (int)(PixelPosition.X * 3 + PixelPosition.Y * 7);
                float fx = PixelPosition.X + NoiseHelpers.HashSigned(th + _spores.ActiveCount * 3) * PlatformWidth * 0.4f;
                _spores.Emit(new Vector2(fx, PixelPosition.Y),
                    new Vector2(NoiseHelpers.HashSigned(th + _spores.ActiveCount) * 20f, -18f),
                    new Color(200, 140, 80), life: 0.5f, size: 3f, gravity: 50f, drag: 1f);
            }

            if (_countdownTimer <= 0f)
            {
                // If the player is plausibly standing on top of us, force-clear
                // their ground contacts so the fall begins this frame. Aether's
                // OnSeparation is unreliable when a body is disposed live.
                if (_contactingPlayer != null)
                {
                    float px = _contactingPlayer.PixelPosition.X;
                    float py = _contactingPlayer.PixelPosition.Y;
                    bool xAligned = Math.Abs(px - PixelPosition.X) <= PlatformWidth / 2f + 4f;
                    bool above    = py <= PixelPosition.Y;
                    if (xAligned && above)
                        _contactingPlayer.ResetGroundContacts();
                }

                // Remove the physics body and mark for destruction
                if (Body != null)
                {
                    World.Remove(Body);
                    Body = null;
                }
                Destroy();
            }
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        public override void Draw(SpriteBatch spriteBatch, AssetManager assets)
        {
            if (IsDestroyed) return;

            _spores.Draw(spriteBatch, assets);
            int tileHash = (int)(PixelPosition.X * 3 + PixelPosition.Y * 7);
            WorldObjectRenderer.DrawDisappearingPlatform(
                spriteBatch, assets,
                PixelPosition,
                _isTriggered,
                _alpha,
                _countdownTimer,
                tileHash);
        }

        // ── Bounds ─────────────────────────────────────────────────────────────

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - PlatformWidth  / 2f),
            (int)(PixelPosition.Y - PlatformHeight / 2f - 16),
            PlatformWidth, PlatformHeight + 32);

        // ── Collision callback ─────────────────────────────────────────────────

        private bool OnSensorCollision(Fixture sender, Fixture other, Contact contact)
        {
            if (_isTriggered || IsDestroyed) return true;

            // Check if the colliding body is the player
            if (other.Body?.Tag is Player player)
            {
                bool litByLantern = IsLitByLantern(player, PixelPosition);
                StartCountdown(spawnSporeLight: litByLantern);
            }

            return true;
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private void StartCountdown(bool spawnSporeLight)
        {
            _isTriggered    = true;
            _countdownTimer = CountdownDuration;
            SpawnSporeLight = spawnSporeLight;

            // Notify the domino chain
            OnTriggered?.Invoke(this);

            // TODO: play disappearing platform trigger sound effect
        }
    }
}
