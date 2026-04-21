using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Physics;
using Bloop.Rendering;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Entities
{
    /// <summary>
    /// Chain Centipede — controllable cave entity.
    ///
    /// Movement: WASD on surfaces; climbs walls and ceilings at high speed.
    /// Skill: Aggression Pheromone Burst (instant) — aura pulse.
    ///   Same type: up to 4 centipedes lock into train formation, follow path.
    ///   Different type: triggers infighting for 7s.
    ///   Extra: can push heavy objects when in train formation.
    /// </summary>
    public class ChainCentipede : ControllableEntity
    {
        public const float WidthPx  = 30f;
        public const float HeightPx = 8f;

        public override string DisplayName    => "Chain Centipede";
        public override float  ControlDuration => 11f;
        public override float  MovementSpeed   => 180f;
        public override bool   CanWallClimb    => true;
        public override bool   CanCeilingClimb => true;

        // ── Pulse state ────────────────────────────────────────────────────────
        public bool  PulseActive  { get; private set; }
        public float PulseRadius  { get; private set; }
        public const float PulseMaxRadius  = 150f;
        private const float PulseExpandSpeed = 350f;

        private readonly InputManager _input;

        // ── Idle AI ────────────────────────────────────────────────────────────
        private Vector2 _wanderTarget;
        private float   _wanderTimer;
        private const float WanderInterval = 2.5f;

        public ChainCentipede(Vector2 pixelPosition, AetherWorld world, InputManager input)
            : base(ControllableEntityType.ChainCentipede, pixelPosition, world)
        {
            _input        = input;
            _wanderTarget = pixelPosition;

            Body = BodyFactory.CreateEntityBody(world, pixelPosition, WidthPx, HeightPx, canFly: false);
            Body.Tag = this;

            Skill = new AggressionPheromoneSkill(this);
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
                SetVelocity(new Vector2(GetVelocityPixels().X * 0.7f, physVY));

            if (_input.IsInteractPressed())
                Skill?.TryActivate();

            if (PulseActive)
            {
                PulseRadius += PulseExpandSpeed * dt;
                if (PulseRadius >= PulseMaxRadius) { PulseActive = false; PulseRadius = 0f; }
            }
        }

        protected override void UpdateIdle(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (PulseActive)
            {
                PulseRadius += PulseExpandSpeed * dt;
                if (PulseRadius >= PulseMaxRadius) { PulseActive = false; PulseRadius = 0f; }
            }

            if (IsStuck) { SetVelocity(Vector2.Zero); return; }
            if (IsInfighting) { SetVelocity(FleeDirection * MovementSpeed * 0.6f); return; }

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
                    (float)Math.Cos(angle) * 50f, (float)Math.Sin(angle) * 20f);
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
            EntityRenderer.DrawChainCentipede(spriteBatch, assets, this);
        }

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - WidthPx / 2f),
            (int)(PixelPosition.Y - HeightPx / 2f),
            (int)WidthPx, (int)HeightPx);

        public override float GetEffectRadius() => PulseMaxRadius;

        public override void ApplySameTypeEffect(List<ControllableEntity> sameType)
        {
            int count = 0;
            foreach (var e in sameType)
            {
                if (e == this || count >= 4) break;
                e.IsFollowing  = true;
                e.FollowTarget = this;
                e.DisorientTimer = 7f;
                count++;
            }
        }

        public override void ApplyDifferentTypeEffect(List<ControllableEntity> differentType)
        {
            var rng = new Random();
            foreach (var e in differentType)
            {
                e.IsInfighting  = true;
                e.InfightTimer  = 7f;
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                e.FleeDirection = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            }
        }

        internal void TriggerPulse() { PulseActive = true; PulseRadius = 0f; }
    }

    internal sealed class AggressionPheromoneSkill : EntitySkill
    {
        private readonly ChainCentipede _centipede;
        public AggressionPheromoneSkill(ChainCentipede c)
            : base("Aggression Pheromone", SkillActivationType.Instant, cooldown: 0f) => _centipede = c;
        protected override void OnActivate(float power) => _centipede.TriggerPulse();
    }
}
