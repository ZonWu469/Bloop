using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Lighting;
using Bloop.Physics;
using Bloop.Rendering;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Entities
{
    /// <summary>
    /// Luminescent Glowworm — controllable cave entity.
    ///
    /// Movement: Slow crawl on surfaces; can squeeze through 1-tile gaps.
    /// Skill: Bioluminescence Flash (charge 5s) — bright pulse.
    ///   Same type: all glowworms sync glow and follow in single-file line.
    ///   Different type: light-averse creatures flee for 6s.
    ///   Extra: flash reveals entire screen (hidden ledges, glyphs).
    /// Light source: Emits constant ambient light.
    /// </summary>
    public class LuminescentGlowworm : ControllableEntity
    {
        public const float WidthPx  = 24f; // 2× larger
        public const float HeightPx = 16f; // 2× larger

        public override string DisplayName    => "Luminescent Glowworm";
        public override float  ControlDuration => 14f;
        public override float  MovementSpeed   => 60f;

        // ── Light reaction ─────────────────────────────────────────────────────
        public override LightReactionType LightReaction => LightReactionType.AttractedToLight;
        /// <summary>Glowworms are tolerant — only react at 50% perceived intensity.</summary>
        public override float LightTolerance => 0.50f;

        // ── Flash state ────────────────────────────────────────────────────────
        public bool  FlashActive  { get; private set; }
        public float FlashRadius  { get; private set; }
        public const float FlashMaxRadius  = 300f;
        private const float FlashExpandSpeed = 600f;

        private readonly InputManager _input;
        private LightSource? _ambientLight;

        // ── Idle AI ────────────────────────────────────────────────────────────
        private Vector2 _wanderTarget;
        private float   _wanderTimer;
        private const float WanderInterval = 5f;

        // ── Cluster drift + sync glow ──────────────────────────────────────────
        /// <summary>Shared cluster center updated by the first glowworm each frame.</summary>
        public static Vector2 ClusterCenter { get; set; } = Vector2.Zero;
        public static int     ClusterCount  { get; set; } = 0;
        /// <summary>Per-instance sync phase offset for staggered glow pulsing.</summary>
        public float SyncPhase { get; private set; }
        private static readonly Random _syncRng = new Random();
        private const float ClusterDriftSpeed = 20f;  // px/s toward cluster center
        private const float ClusterRadius     = 60f;  // px — cluster cohesion radius

        public LuminescentGlowworm(Vector2 pixelPosition, AetherWorld world, InputManager input)
            : base(ControllableEntityType.LuminescentGlowworm, pixelPosition, world)
        {
            _input        = input;
            _wanderTarget = pixelPosition;

            Body = BodyFactory.CreateEntityBody(world, pixelPosition, WidthPx, HeightPx, canFly: false);
            Body.Tag = this;

            Skill = new BioluminescenceFlashSkill(this);

            // Randomize sync phase so glowworms pulse at slightly different times
            SyncPhase = (float)(_syncRng.NextDouble() * Math.PI * 2.0);
        }

        public override (string description, string? actionHint) GetTooltipInfo()
            => ("Bioluminescent cave worm. Drifts in clusters.", "[Q] Control — 10s light carrier");

        public void SetLightSource(LightSource light)
        {
            _ambientLight = light;
            light.Position = PixelPosition;
        }

        protected override void OnControlStart() { }
        protected override void OnControlEnd()
        {
            _wanderTarget = PixelPosition;
            _wanderTimer  = 0f;
        }

        protected override void UpdateControlled(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_ambientLight != null) _ambientLight.Position = PixelPosition;

            float horiz  = _input.GetHorizontalAxis();
            float vert   = _input.GetVerticalAxis();
            float physVY = GetVelocityPixels().Y;
            var dir = new Vector2(horiz, vert);
            if (dir.LengthSquared() > 0.01f)
            {
                var n  = Vector2.Normalize(dir);
                float vy = MathF.Abs(vert) > 0.01f ? n.Y * MovementSpeed : physVY;
                SetVelocity(new Vector2(n.X * MovementSpeed, vy));
                if (horiz != 0f) FacingDirection = MathF.Sign(horiz);
            }
            else
                SetVelocity(new Vector2(GetVelocityPixels().X * 0.6f, physVY));

            // E key charges/releases flash
            if (_input.IsInteractPressed())
            {
                if (Skill?.IsActive == true)
                    Skill.Deactivate();
                else
                    Skill?.TryActivate();
            }

            if (FlashActive)
            {
                FlashRadius += FlashExpandSpeed * dt;
                if (FlashRadius >= FlashMaxRadius) { FlashActive = false; FlashRadius = 0f; }
            }
        }

        protected override void UpdateIdle(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_ambientLight != null) _ambientLight.Position = PixelPosition;

            if (FlashActive)
            {
                FlashRadius += FlashExpandSpeed * dt;
                if (FlashRadius >= FlashMaxRadius) { FlashActive = false; FlashRadius = 0f; }
            }

            if (IsFleeing) { SetVelocity(FleeDirection * MovementSpeed * 0.5f); return; }

            if (IsFollowing && FollowTarget != null)
            {
                Vector2 toTarget = FollowTarget.PixelPosition - PixelPosition;
                if (toTarget.LengthSquared() > 4f)
                    SetVelocity(Vector2.Normalize(toTarget) * MovementSpeed * 0.8f);
                DisorientTimer -= dt;
                if (DisorientTimer <= 0f) { IsFollowing = false; FollowTarget = null; }
                return;
            }

            // ── Light attraction: bias wander toward light source ─────────────
            if (LightReactionStrength > 0f && LightSourceDirection.LengthSquared() > 0.0001f)
            {
                // Gentle drift toward light — glowworms are slow and peaceful
                float attractSpeed = MovementSpeed * 0.5f * LightReactionStrength;
                SetVelocity(new Vector2(LightSourceDirection.X * attractSpeed, GetVelocityPixels().Y));
                return;
            }

            // ── Cluster drift: drift toward shared cluster center ──────────────
            // ClusterCenter is updated externally by Level or GameplayScreen each frame
            // by averaging all glowworm positions. Here we just drift toward it.
            if (ClusterCount > 1)
            {
                float distToCluster = Vector2.Distance(PixelPosition, ClusterCenter);
                if (distToCluster > ClusterRadius)
                {
                    Vector2 toCluster = Vector2.Normalize(ClusterCenter - PixelPosition);
                    // Blend cluster drift with normal wander
                    Vector2 driftVel = toCluster * ClusterDriftSpeed;
                    SetVelocity(driftVel);
                    return;
                }
            }

            // ── Normal wander (slow, peaceful) ────────────────────────────────
            _wanderTimer -= dt;
            if (_wanderTimer <= 0f)
            {
                _wanderTimer = WanderInterval;
                var rng = new Random();
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                _wanderTarget = PixelPosition + new Vector2(
                    (float)Math.Cos(angle) * 40f, (float)Math.Sin(angle) * 20f);
            }

            Vector2 toWander = _wanderTarget - PixelPosition;
            if (toWander.LengthSquared() > 4f)
            {
                var vel = Vector2.Normalize(toWander) * MovementSpeed * 0.5f;
                SetVelocity(vel);
                if (vel.X != 0f) FacingDirection = MathF.Sign(vel.X);
            }
            else
                SetVelocity(new Vector2(0f, GetVelocityPixels().Y));
        }

        public override void Draw(SpriteBatch spriteBatch, Bloop.Core.AssetManager assets)
        {
            if (IsDestroyed) return;

            // Flash effect drawn before sprite
            if (FlashActive)
            {
                float flashAlpha = 1f - FlashRadius / FlashMaxRadius;
                var flashColor = new Color(220, 255, 180, (int)(flashAlpha * 200));
                GeometryBatch.DrawCircleApprox(spriteBatch, assets, PixelPosition, FlashRadius, flashColor, 16);
            }

            EntityRenderer.DrawEntity(spriteBatch, assets, this,
                WidthPx, HeightPx, assets.EntityLuminescentGlowworm);
        }

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - WidthPx / 2f),
            (int)(PixelPosition.Y - HeightPx / 2f),
            (int)WidthPx, (int)HeightPx);

        public override float GetEffectRadius() => FlashMaxRadius;

        public override void ApplySameTypeEffect(List<ControllableEntity> sameType)
        {
            foreach (var e in sameType)
            {
                if (e == this) continue;
                e.IsFollowing  = true;
                e.FollowTarget = this;
                e.DisorientTimer = 8f;
            }
        }

        public override void ApplyDifferentTypeEffect(List<ControllableEntity> differentType)
        {
            var rng = new Random();
            foreach (var e in differentType)
            {
                e.IsFleeing   = true;
                e.FleeTimer   = 6f;
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                e.FleeDirection = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            }
        }

        internal void TriggerFlash() { FlashActive = true; FlashRadius = 0f; }
    }

    internal sealed class BioluminescenceFlashSkill : EntitySkill
    {
        private readonly LuminescentGlowworm _worm;
        public BioluminescenceFlashSkill(LuminescentGlowworm w)
            : base("Bioluminescence Flash", "Flash disorients all nearby creatures", SkillActivationType.Charge, cooldown: 5f, maxChargeTime: 3f)
            => _worm = w;
        protected override void OnActivate(float power) => _worm.TriggerFlash();
    }
}
