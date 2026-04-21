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
        public const float WidthPx  = 12f;
        public const float HeightPx = 8f;

        public override string DisplayName    => "Luminescent Glowworm";
        public override float  ControlDuration => 14f;
        public override float  MovementSpeed   => 60f;

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

        public LuminescentGlowworm(Vector2 pixelPosition, AetherWorld world, InputManager input)
            : base(ControllableEntityType.LuminescentGlowworm, pixelPosition, world)
        {
            _input        = input;
            _wanderTarget = pixelPosition;

            Body = BodyFactory.CreateEntityBody(world, pixelPosition, WidthPx, HeightPx, canFly: false);
            Body.Tag = this;

            Skill = new BioluminescenceFlashSkill(this);
        }

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
                SetVelocity(Vector2.Normalize(toWander) * MovementSpeed * 0.5f);
            else
                SetVelocity(new Vector2(0f, GetVelocityPixels().Y));
        }

        public override void Draw(SpriteBatch spriteBatch, Bloop.Core.AssetManager assets)
        {
            if (IsDestroyed) return;
            EntityRenderer.DrawLuminescentGlowworm(spriteBatch, assets, this);
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
            : base("Bioluminescence Flash", SkillActivationType.Charge, cooldown: 5f, maxChargeTime: 3f)
            => _worm = w;
        protected override void OnActivate(float power) => _worm.TriggerFlash();
    }
}
