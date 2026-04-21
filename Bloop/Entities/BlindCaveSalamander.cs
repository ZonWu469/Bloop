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
    /// Blind Cave Salamander (Olm) — controllable cave entity.
    ///
    /// Movement: Walk on surfaces; swim in water pools; stick to wet walls.
    /// Skill: Slime Trail Spit (4s CD) — shoots sticky trail or blob in aimed direction.
    ///   Same type: other salamanders follow slime trail at 2× speed.
    ///   Different type: enemies on slime glued 9s or slide on slopes.
    ///   Extra: can swim in underground pools (WaterPool integration).
    /// </summary>
    public class BlindCaveSalamander : ControllableEntity
    {
        public const float WidthPx  = 22f;
        public const float HeightPx = 10f;

        public override string DisplayName    => "Blind Cave Salamander";
        public override float  ControlDuration => 13f;
        public override float  MovementSpeed   => 90f;
        public override bool   CanSwim         => true;

        private readonly InputManager _input;
        private readonly Camera       _camera;

        // ── Idle AI ────────────────────────────────────────────────────────────
        private Vector2 _wanderTarget;
        private float   _wanderTimer;
        private const float WanderInterval = 3.5f;

        public BlindCaveSalamander(Vector2 pixelPosition, AetherWorld world,
            InputManager input, Camera camera)
            : base(ControllableEntityType.BlindCaveSalamander, pixelPosition, world)
        {
            _input  = input;
            _camera = camera;

            _wanderTarget = pixelPosition;

            Body = BodyFactory.CreateEntityBody(world, pixelPosition, WidthPx, HeightPx, canFly: false);
            Body.Tag = this;

            Skill = new SlimeTrailSpitSkill(this);
        }

        protected override void OnControlStart() { }
        protected override void OnControlEnd()
        {
            _wanderTarget = PixelPosition;
            _wanderTimer  = 0f;
        }

        protected override void UpdateControlled(GameTime gameTime)
        {
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
        }

        protected override void UpdateIdle(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (IsStuck) { SetVelocity(Vector2.Zero); return; }
            if (IsFleeing) { SetVelocity(FleeDirection * MovementSpeed * 0.5f); return; }

            if (IsFollowing && FollowTarget != null)
            {
                Vector2 toTarget = FollowTarget.PixelPosition - PixelPosition;
                if (toTarget.LengthSquared() > 4f)
                    SetVelocity(Vector2.Normalize(toTarget) * MovementSpeed * 1.5f); // 2× speed
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
                SetVelocity(Vector2.Normalize(toWander) * MovementSpeed * 0.4f);
            else
                SetVelocity(new Vector2(0f, GetVelocityPixels().Y));
        }

        public override void Draw(SpriteBatch spriteBatch, Bloop.Core.AssetManager assets)
        {
            if (IsDestroyed) return;
            EntityRenderer.DrawBlindCaveSalamander(spriteBatch, assets, this);
        }

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - WidthPx / 2f),
            (int)(PixelPosition.Y - HeightPx / 2f),
            (int)WidthPx, (int)HeightPx);

        public override float GetEffectRadius() => 140f;

        public override void ApplySameTypeEffect(List<ControllableEntity> sameType)
        {
            foreach (var e in sameType)
            {
                if (e == this) continue;
                e.IsFollowing  = true;
                e.FollowTarget = this;
                e.DisorientTimer = 9f;
            }
        }

        public override void ApplyDifferentTypeEffect(List<ControllableEntity> differentType)
        {
            foreach (var e in differentType)
            {
                e.IsStuck   = true;
                e.StuckTimer = 9f;
            }
        }
    }

    internal sealed class SlimeTrailSpitSkill : EntitySkill
    {
        private readonly BlindCaveSalamander _salamander;
        public SlimeTrailSpitSkill(BlindCaveSalamander s)
            : base("Slime Trail Spit", SkillActivationType.Instant, cooldown: 4f) => _salamander = s;
        protected override void OnActivate(float power)
        {
            // Effect applied via ApplySameTypeEffect / ApplyDifferentTypeEffect
            // triggered by EntityControlSystem when it detects skill fired
        }
    }
}
