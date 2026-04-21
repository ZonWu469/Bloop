using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using Bloop.Core;
using Bloop.Physics;
using Bloop.Rendering;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Entities
{
    /// <summary>
    /// Echo Bat — controllable cave entity.
    ///
    /// Movement: Free flight in any direction via WASD. No gravity while controlled.
    /// Skill: Sonic Pulse (instant, 3s cooldown) — radial pulse that:
    ///   - Same type: nearby bats flock and mirror flight path for 6s
    ///   - Different type: ground enemies reverse/jump randomly for 4s
    ///   - Extra: shatters fragile stalactites in range
    ///
    /// Idle AI: Wanders in a slow figure-eight pattern near its roost position,
    /// occasionally reversing direction. Hangs upside-down when stationary.
    /// </summary>
    public class EchoBat : ControllableEntity
    {
        // ── Dimensions ─────────────────────────────────────────────────────────
        public const float WidthPx  = 16f;
        public const float HeightPx = 10f;

        // ── Identity ───────────────────────────────────────────────────────────
        public override string DisplayName    => "Echo Bat";
        public override float  ControlDuration => 9f;
        public override float  MovementSpeed   => 200f;
        public override bool   CanFly          => true;

        // ── Sonic Pulse state (shared with renderer) ───────────────────────────
        /// <summary>True while the pulse ring is expanding (visual effect).</summary>
        public bool  PulseActive   { get; private set; }
        /// <summary>Current pulse ring radius in pixels (0 → PulseMaxRadius).</summary>
        public float PulseRadius   { get; private set; }
        /// <summary>Maximum pulse ring radius in pixels.</summary>
        public const float PulseMaxRadius = 180f;
        private const float PulseExpandSpeed = 400f; // px/s

        // ── Wing animation (shared with renderer) ──────────────────────────────
        /// <summary>Wing flap phase 0–1 (driven by velocity magnitude).</summary>
        public float WingPhase { get; private set; }
        private float _wingTimer;

        // ── Idle AI state ──────────────────────────────────────────────────────
        private Vector2 _roostPosition;      // where the bat hangs when idle
        private Vector2 _idleWanderTarget;   // current wander destination
        private float   _idleWanderTimer;    // time until next wander target
        private const float IdleWanderInterval = 3f;
        private const float IdleWanderSpeed    = 60f;
        private const float IdleWanderRadius   = 80f;

        // ── Controlled flight state ────────────────────────────────────────────
        private readonly InputManager _input;
        private readonly Camera       _camera;

        // ── Path recording for same-type followers ─────────────────────────────
        private readonly Queue<Vector2> _flightPath = new Queue<Vector2>();
        private float _pathRecordTimer;
        private const float PathRecordInterval = 0.1f; // record position every 100ms
        private const int   MaxPathPoints      = 60;   // 6 seconds of path at 100ms intervals

        // ── Constructor ────────────────────────────────────────────────────────
        public EchoBat(Vector2 pixelPosition, AetherWorld world,
            InputManager input, Camera camera)
            : base(ControllableEntityType.EchoBat, pixelPosition, world)
        {
            _input  = input;
            _camera = camera;

            _roostPosition    = pixelPosition;
            _idleWanderTarget = pixelPosition;

            // Create physics body — flying, so no gravity
            Body = BodyFactory.CreateEntityBody(world, pixelPosition, WidthPx, HeightPx, canFly: true);
            Body.Tag = this;

            // Assign skill
            Skill = new SonicPulseSkill(this);
        }

        // ── ControllableEntity overrides ───────────────────────────────────────

        protected override void OnControlStart()
        {
            // Disable gravity while controlled (already set by canFly, but ensure it)
            if (Body != null)
            {
                Body.IgnoreGravity = true;
                Body.LinearDamping = 3f;
            }
            _flightPath.Clear();
            _pathRecordTimer = 0f;
        }

        protected override void OnControlEnd()
        {
            // Resume idle wandering from current position
            _roostPosition    = PixelPosition;
            _idleWanderTarget = PixelPosition;
            _idleWanderTimer  = 0f;

            if (Body != null)
                Body.LinearDamping = 2f;
        }

        protected override void UpdateControlled(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // ── WASD free flight ───────────────────────────────────────────────
            float horiz = _input.GetHorizontalAxis();
            float vert  = _input.GetVerticalAxis();

            var dir = new Vector2(horiz, vert);
            if (dir.LengthSquared() > 0.01f)
            {
                dir = Vector2.Normalize(dir);
                SetVelocity(dir * MovementSpeed);
            }
            else
            {
                // Dampen to a stop
                var vel = GetVelocityPixels();
                SetVelocity(vel * 0.85f);
            }

            // ── Wing animation ─────────────────────────────────────────────────
            float speed = GetVelocityPixels().Length();
            float flapRate = 0.5f + speed / MovementSpeed * 2.5f; // 0.5–3 Hz
            _wingTimer += dt * flapRate;
            WingPhase = (_wingTimer % 1f);

            // ── Path recording for same-type followers ─────────────────────────
            _pathRecordTimer -= dt;
            if (_pathRecordTimer <= 0f)
            {
                _pathRecordTimer = PathRecordInterval;
                _flightPath.Enqueue(PixelPosition);
                while (_flightPath.Count > MaxPathPoints)
                    _flightPath.Dequeue();
            }

            // ── Skill: E key activates Sonic Pulse ────────────────────────────
            if (_input.IsInteractPressed())
                Skill?.TryActivate();

            // ── Tick pulse ring ────────────────────────────────────────────────
            if (PulseActive)
            {
                PulseRadius += PulseExpandSpeed * dt;
                if (PulseRadius >= PulseMaxRadius)
                {
                    PulseActive = false;
                    PulseRadius = 0f;
                }
            }
        }

        protected override void UpdateIdle(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Tick pulse ring even when idle (effect may still be expanding)
            if (PulseActive)
            {
                PulseRadius += PulseExpandSpeed * dt;
                if (PulseRadius >= PulseMaxRadius)
                {
                    PulseActive = false;
                    PulseRadius = 0f;
                }
            }

            // Handle following behavior (same-type effect)
            if (IsFollowing && FollowTarget != null)
            {
                UpdateFollowing(dt);
                return;
            }

            // Handle fleeing behavior (different-type effect)
            if (IsFleeing)
            {
                SetVelocity(FleeDirection * IdleWanderSpeed * 2f);
                return;
            }

            // Handle disorientation (random direction changes)
            if (IsDisoriented)
            {
                _idleWanderTimer -= dt;
                if (_idleWanderTimer <= 0f)
                {
                    _idleWanderTimer = 0.5f;
                    float angle = (float)(new Random().NextDouble() * Math.PI * 2.0);
                    _idleWanderTarget = PixelPosition + new Vector2(
                        (float)Math.Cos(angle), (float)Math.Sin(angle)) * 40f;
                }
                MoveTowardTarget(_idleWanderTarget, IdleWanderSpeed * 0.5f, dt);
                return;
            }

            // Normal idle: wander near roost
            _idleWanderTimer -= dt;
            if (_idleWanderTimer <= 0f)
            {
                _idleWanderTimer = IdleWanderInterval;
                PickNewWanderTarget();
            }

            MoveTowardTarget(_idleWanderTarget, IdleWanderSpeed, dt);

            // Wing animation (slow idle flap)
            _wingTimer += dt * 0.8f;
            WingPhase = (_wingTimer % 1f);
        }

        // ── WorldObject overrides ──────────────────────────────────────────────

        public override void Draw(SpriteBatch spriteBatch, AssetManager assets)
        {
            if (IsDestroyed) return;
            EntityRenderer.DrawEchoBat(spriteBatch, assets, this);
        }

        public override Rectangle GetBounds()
        {
            return new Rectangle(
                (int)(PixelPosition.X - WidthPx  / 2f),
                (int)(PixelPosition.Y - HeightPx / 2f),
                (int)WidthPx,
                (int)HeightPx);
        }

        // ── Inter-entity effects ───────────────────────────────────────────────

        public override float GetEffectRadius() => PulseMaxRadius;

        public override void ApplySameTypeEffect(List<ControllableEntity> sameType)
        {
            // Same-type bats flock and mirror flight path for 6s
            foreach (var entity in sameType)
            {
                if (entity == this) continue;
                entity.IsFollowing  = true;
                entity.FollowTarget = this;
                entity.FleeTimer    = 0f;

                // Copy current flight path to follower
                entity.FollowPath.Clear();
                foreach (var pt in _flightPath)
                    entity.FollowPath.Enqueue(pt);

                // Set disorientation timer to 6s (repurposed as follow duration)
                entity.DisorientTimer = 6f;
                entity.IsDisoriented  = false;
            }
        }

        public override void ApplyDifferentTypeEffect(List<ControllableEntity> differentType)
        {
            // Different-type entities reverse/jump randomly for 4s
            var rng = new Random();
            foreach (var entity in differentType)
            {
                entity.IsDisoriented  = true;
                entity.DisorientTimer = 4f;
                // Random flee direction
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                entity.FleeDirection = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void PickNewWanderTarget()
        {
            var rng = new Random();
            float angle  = (float)(rng.NextDouble() * Math.PI * 2.0);
            float radius = (float)(rng.NextDouble() * IdleWanderRadius);
            _idleWanderTarget = _roostPosition + new Vector2(
                (float)Math.Cos(angle) * radius,
                (float)Math.Sin(angle) * radius);
        }

        private void MoveTowardTarget(Vector2 target, float speed, float dt)
        {
            Vector2 toTarget = target - PixelPosition;
            float dist = toTarget.Length();
            if (dist < 4f)
            {
                SetVelocity(Vector2.Zero);
                return;
            }
            SetVelocity(Vector2.Normalize(toTarget) * speed);
        }

        private void UpdateFollowing(float dt)
        {
            // Follow the recorded path of the controlled bat
            if (FollowPath.Count == 0)
            {
                IsFollowing  = false;
                FollowTarget = null;
                return;
            }

            Vector2 nextPoint = FollowPath.Peek();
            float dist = Vector2.Distance(PixelPosition, nextPoint);
            if (dist < 8f)
                FollowPath.Dequeue();

            MoveTowardTarget(nextPoint, IdleWanderSpeed * 1.5f, dt);

            // Stop following when disorientation timer (follow duration) expires
            DisorientTimer -= dt;
            if (DisorientTimer <= 0f)
            {
                IsFollowing  = false;
                FollowTarget = null;
                FollowPath.Clear();
            }
        }

        // ── Trigger pulse (called by SonicPulseSkill) ──────────────────────────
        internal void TriggerPulse()
        {
            PulseActive = true;
            PulseRadius = 0f;
        }
    }

    // ── Sonic Pulse Skill ──────────────────────────────────────────────────────

    /// <summary>
    /// Sonic Pulse: instant radial pulse with 3s cooldown.
    /// Affects same-type bats (flock) and different-type entities (disorient).
    /// Also shatters nearby FallingStalactite objects.
    /// </summary>
    internal sealed class SonicPulseSkill : EntitySkill
    {
        private readonly EchoBat _bat;

        public SonicPulseSkill(EchoBat bat)
            : base("Sonic Pulse", SkillActivationType.Instant, cooldown: 3f)
        {
            _bat = bat;
        }

        protected override void OnActivate(float power)
        {
            _bat.TriggerPulse();
            // Inter-entity effects are applied by EntityControlSystem when it detects
            // the pulse fired (via PulseActive flag). The bat's ApplySameTypeEffect
            // and ApplyDifferentTypeEffect are called from GameplayScreen.
        }
    }
}
