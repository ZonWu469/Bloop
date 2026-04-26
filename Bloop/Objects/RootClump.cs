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
    /// A climbable root clump attached to a wall. The player can climb it with C key.
    /// If the player stops moving on it for 8 seconds, it retracts into the wall
    /// (shrinks visually over 1 second, then destroys itself).
    ///
    /// Visual: brown-green organic rectangle that shrinks horizontally during retraction.
    /// </summary>
    public class RootClump : WorldObject
    {
        // ── Dimensions ─────────────────────────────────────────────────────────
        private const int TileSize = 32;

        // ── Tuning ─────────────────────────────────────────────────────────────
        private const float IdleTimeout      = 8f;  // seconds before retraction
        private const float RetractDuration  = 1f;  // seconds to fully retract
        private const float MovementThreshold = 20f; // pixels/second — below this = idle

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color ColorFill    = new Color( 90,  70,  40);
        private static readonly Color ColorHighlight = new Color( 60, 100,  50);
        private static readonly Color ColorOutline  = new Color( 50,  40,  20);
        private static readonly Color ColorRetract  = new Color(120,  90,  50);

        // ── State ──────────────────────────────────────────────────────────────
        private float _idleTimer;
        private bool  _playerOnSurface;
        private bool  _isRetracting;
        private float _retractTimer;
        private float _retractProgress; // 0 = full, 1 = fully retracted

        private readonly ObjectParticleEmitter _dust = new ObjectParticleEmitter(24);
        private float _dustTimer;
        private static readonly Color DustColor = new Color(105, 82, 52);

        // ── Dimensions ─────────────────────────────────────────────────────────
        private readonly int _heightPx;

        public override bool WantsPlayerContact => true;

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Create a root clump centered at pixelPosition.
        /// tileHeight: number of tiles tall (3–6).
        /// </summary>
        public RootClump(Vector2 pixelPosition, AetherWorld world, int tileHeight = 3)
            : base(pixelPosition, world)
        {
            _heightPx = tileHeight * TileSize;

            Body = BodyFactory.CreateStaticRect(
                world, pixelPosition,
                TileSize, _heightPx,
                CollisionCategories.Climbable);

            Body.Tag = this;

            // Sensor for player contact detection
            var sensorBody = BodyFactory.CreateSensorRect(
                world, pixelPosition,
                TileSize + 8, _heightPx + 8);
            sensorBody.Tag = this;

            foreach (var fixture in sensorBody.FixtureList)
            {
                fixture.OnCollision  += OnSensorCollision;
                fixture.OnSeparation += OnSensorSeparation;
            }
        }

        // ── Update ─────────────────────────────────────────────────────────────

        public override void Update(GameTime gameTime)
        {
            if (IsDestroyed) return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _dust.Update(dt);

            if (_isRetracting)
            {
                _retractTimer    += dt;
                _retractProgress  = Math.Min(_retractTimer / RetractDuration, 1f);

                // Emit dust puffs during retraction
                _dustTimer -= dt;
                if (_dustTimer <= 0f)
                {
                    _dustTimer = 0.12f;
                    int s = (int)(PixelPosition.X * 3 + PixelPosition.Y * 7 + _dust.ActiveCount * 29);
                    float dx2 = NoiseHelpers.HashSigned(s) * 14f;
                    float dy2 = PixelPosition.Y - _heightPx / 2f + _retractProgress * _heightPx;
                    _dust.Emit(new Vector2(PixelPosition.X + dx2, dy2),
                        new Vector2(dx2 * 0.5f, -12f),
                        DustColor, life: 0.5f, size: 2f, gravity: 22f, drag: 1.5f);
                }

                if (_retractProgress >= 1f)
                    Destroy();

                return;
            }

            // Idle timer: only counts when player is on the surface
            if (_playerOnSurface)
            {
                _idleTimer += dt;
                if (_idleTimer >= IdleTimeout)
                    StartRetraction();
            }
            else
            {
                // Slowly drain idle timer when player is not on surface
                _idleTimer = Math.Max(0f, _idleTimer - dt * 0.5f);
            }
        }

        /// <summary>
        /// Reset the idle timer when the player is actively moving on this surface.
        /// Called by Level.Update() when the player is climbing and moving.
        /// </summary>
        public void ResetIdleTimer(Player player)
        {
            if (!_playerOnSurface) return;

            float speed = Physics.PhysicsManager.ToPixels(player.Body.LinearVelocity).Length();
            if (speed > MovementThreshold)
                _idleTimer = 0f;
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        public override void Draw(SpriteBatch spriteBatch, AssetManager assets)
        {
            if (IsDestroyed) return;

            _dust.Draw(spriteBatch, assets);

            var sheet = assets.ObjectRootClump;
            if (sheet == null) return;

            float visibleH = _heightPx * (1f - _retractProgress);
            if (visibleH <= 0f) return;

            int frame  = (int)(AnimationClock.Time * sheet.Fps) % Math.Max(1, sheet.FrameCount);
            var src    = sheet.GetSourceRect(frame);
            float scale = sheet.FrameHeight > 0 ? visibleH / sheet.FrameHeight : 1f;
            var origin  = new Vector2(sheet.FrameWidth / 2f, sheet.FrameHeight / 2f);
            spriteBatch.Draw(sheet.Texture, PixelPosition, src, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
        }

        // ── Bounds ─────────────────────────────────────────────────────────────

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - TileSize / 2f),
            (int)(PixelPosition.Y - _heightPx / 2f),
            TileSize, _heightPx);

        // ── Player contact ─────────────────────────────────────────────────────

        public override void OnPlayerContact(Player player)
        {
            _playerOnSurface = true;
        }

        public override void OnPlayerSeparate(Player player)
        {
            _playerOnSurface = false;
        }

        // ── Collision callbacks ────────────────────────────────────────────────

        private bool OnSensorCollision(Fixture sender, Fixture other, Contact contact)
        {
            if (other.Body?.Tag is Player player)
                OnPlayerContact(player);
            return true;
        }

        private void OnSensorSeparation(Fixture sender, Fixture other, Contact contact)
        {
            if (other.Body?.Tag is Player player)
                OnPlayerSeparate(player);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private void StartRetraction()
        {
            _isRetracting = true;
            _retractTimer = 0f;

            // Remove the climbable body immediately so player can't climb during retraction
            if (Body != null)
            {
                World.Remove(Body);
                Body = null;
            }

            // TODO: play root retraction sound effect
        }
    }
}
