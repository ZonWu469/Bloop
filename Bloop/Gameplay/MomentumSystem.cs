using System;
using Microsoft.Xna.Framework;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Contacts;
using Bloop.Physics;

namespace Bloop.Gameplay
{
    /// <summary>
    /// Tracks kinetic charge built up from sliding on slopes.
    /// At maximum charge the player can:
    ///   - Slingshot: release to launch upward with a large impulse
    ///   - Zip-drop: combine with rappel to pierce through disappearing platforms
    ///
    /// Slope detection uses contact normals from Aether collision callbacks.
    /// </summary>
    public class MomentumSystem
    {
        // ── Screen shake callback (3.2) ────────────────────────────────────────
        /// <summary>
        /// Optional callback invoked when a screen shake should be triggered.
        /// Parameters: (amplitude in pixels, duration in seconds).
        /// Wired up by GameplayScreen after construction.
        /// </summary>
        public Action<float, float>? OnShakeRequested { get; set; }

        // ── Tuning ─────────────────────────────────────────────────────────────
        /// <summary>Minimum slope angle (degrees from vertical) to count as a slide.</summary>
        private const float MinSlopeAngleDeg   = 15f;
        /// <summary>Kinetic charge gained per second while sliding.</summary>
        private const float ChargeRatePerSecond = 30f;
        /// <summary>Slingshot vertical impulse in pixel-space units.</summary>
        private const float SlingshotImpulse    = 280f;
        /// <summary>Slingshot horizontal impulse multiplier (uses current velocity direction).</summary>
        private const float SlingshotHorizMult  = 1.5f;

        // ── State ──────────────────────────────────────────────────────────────
        private bool  _isOnSlope;
        private float _currentSlopeAngle; // degrees from vertical

        // ── Zip-drop state ─────────────────────────────────────────────────────
        private bool  _zipDropActive;
        private float _zipDropTimer;
        private const float ZipDropDuration = 0.8f; // seconds of platform pass-through

        public bool ZipDropActive => _zipDropActive;

        // ── Constructor ────────────────────────────────────────────────────────
        public MomentumSystem()
        {
        }

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Update kinetic charge accumulation and zip-drop timer.
        /// Called by PlayerController each frame.
        /// </summary>
        public void Update(GameTime gameTime, Player player)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Zip-drop countdown
            if (_zipDropActive)
            {
                _zipDropTimer -= dt;
                if (_zipDropTimer <= 0f)
                    EndZipDrop(player);
            }

            // Detect sliding: player is grounded, moving horizontally, on a slope
            bool sliding = player.IsGrounded &&
                           System.Math.Abs(PhysicsManager.ToPixels(player.Body.LinearVelocity.X)) > 20f &&
                           _isOnSlope &&
                           _currentSlopeAngle >= MinSlopeAngleDeg;

            if (sliding)
            {
                player.SetState(PlayerState.Sliding);
                player.Stats.AddKineticCharge(ChargeRatePerSecond * dt);
            }
            else
            {
                // Drain charge slowly when not sliding
                if (player.State == PlayerState.Sliding)
                    player.SetState(PlayerState.Walking);

                player.Stats.DrainKineticCharge(dt);
            }
        }

        // ── Slingshot ──────────────────────────────────────────────────────────

        /// <summary>
        /// Consume all kinetic charge and launch the player upward.
        /// Called by PlayerController when jump is pressed at max charge.
        /// </summary>
        public void TriggerSlingshot(Player player)
        {
            if (player.Stats.KineticCharge < PlayerStats.MaxKineticCharge) return;

            // Upward impulse + horizontal boost in current movement direction
            float horizVel = PhysicsManager.ToPixels(player.Body.LinearVelocity.X);
            float horizBoost = horizVel * SlingshotHorizMult;

            Vector2 impulse = new Vector2(horizBoost, -SlingshotImpulse);
            player.Body.ApplyLinearImpulse(PhysicsManager.ToMeters(impulse));
            player.Stats.ConsumeKineticCharge();
            player.SetState(PlayerState.Jumping);

            // ── Screen shake on slingshot launch (3.2) ────────────────────────
            // Quick upward jolt: 4px amplitude, 0.15s duration, upward direction
            OnShakeRequested?.Invoke(4f, 0.15f);

            // TODO: play slingshot sound effect
        }

        // ── Zip-drop ───────────────────────────────────────────────────────────

        /// <summary>
        /// Activate zip-drop: temporarily disable collision with disappearing platforms
        /// so the player can pierce through them in one motion.
        /// </summary>
        public void TriggerZipDrop(Player player)
        {
            if (_zipDropActive) return;
            if (player.Stats.KineticCharge < PlayerStats.MaxKineticCharge * 0.5f) return;

            _zipDropActive = true;
            _zipDropTimer  = ZipDropDuration;

            // Disable collision with disappearing platforms
            foreach (var fixture in player.Body.FixtureList)
            {
                fixture.CollidesWith &= ~Bloop.Physics.CollisionCategories.DisappearingPlatform;
            }

            player.Stats.ConsumeKineticCharge();

            // TODO: play zip-drop sound effect
        }

        private void EndZipDrop(Player player)
        {
            _zipDropActive = false;

            // Re-enable collision with disappearing platforms
            foreach (var fixture in player.Body.FixtureList)
            {
                fixture.CollidesWith |= Bloop.Physics.CollisionCategories.DisappearingPlatform;
            }
        }

        // ── Slope contact tracking ─────────────────────────────────────────────

        /// <summary>
        /// Call this from a collision callback to register slope contact.
        /// contactNormal: the collision normal from Aether (world space).
        /// </summary>
        public void RegisterSlopeContact(Vector2 contactNormal)
        {
            // Normal pointing up = flat ground (angle = 0)
            // Normal pointing sideways = wall (angle = 90)
            // We want slopes between 15° and 75° from vertical
            float angleFromVertical = MathHelper.ToDegrees(
                (float)System.Math.Acos(MathHelper.Clamp(
                    System.Math.Abs(contactNormal.Y), 0f, 1f)));

            if (angleFromVertical >= MinSlopeAngleDeg && angleFromVertical < 75f)
            {
                _isOnSlope         = true;
                _currentSlopeAngle = angleFromVertical;
            }
        }

        /// <summary>Clear slope state when player leaves ground contact.</summary>
        public void ClearSlopeContact()
        {
            _isOnSlope         = false;
            _currentSlopeAngle = 0f;
        }
    }
}
