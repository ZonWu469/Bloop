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

        // ── Light reaction ─────────────────────────────────────────────────────
        public override LightReactionType LightReaction => LightReactionType.AttractedToLight;
        /// <summary>Salamanders are curious — react at 40% perceived intensity.</summary>
        public override float LightTolerance => 0.40f;

        private readonly InputManager _input;
        private readonly Camera       _camera;

        // ── Idle AI ────────────────────────────────────────────────────────────
        private Vector2 _wanderTarget;
        private float   _wanderTimer;
        private const float WanderInterval = 3.5f;

        // ── Flee from player ───────────────────────────────────────────────────
        private bool  _fleeFromPlayer;
        private float _fleeFromPlayerTimer;
        private const float PlayerFleeRange    = 100f;  // px — detection radius
        private const float PlayerFleeDuration = 2f;    // seconds of flee
        private const float FleeFromPlayerSpeed = 70f;  // px/s while fleeing player

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

        public override (string description, string? actionHint) GetTooltipInfo()
            => ("Timid cave amphibian. Flees from light and movement.", "[Q] Control — 12s scout");

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

            // ── Skill-triggered flee (different-type effect) ───────────────────
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

            // ── Light attraction: bias wander toward light source ─────────────
            if (LightReactionStrength > 0f && LightSourceDirection.LengthSquared() > 0.0001f)
            {
                // Move toward the light, speed scales with reaction strength
                float attractSpeed = MovementSpeed * 0.4f * (0.5f + LightReactionStrength * 0.5f);
                SetVelocity(new Vector2(LightSourceDirection.X * attractSpeed, GetVelocityPixels().Y));
                return;
            }

            // ── Flee from player when nearby ───────────────────────────────────
            if (_fleeFromPlayer)
            {
                _fleeFromPlayerTimer -= dt;
                if (_fleeFromPlayerTimer <= 0f)
                    _fleeFromPlayer = false;
                else
                {
                    // Flee away from player
                    if (HasPlayerPosition)
                    {
                        Vector2 awayFromPlayer = Vector2.Normalize(PixelPosition - PlayerPosition);
                        SetVelocity(new Vector2(awayFromPlayer.X * FleeFromPlayerSpeed, GetVelocityPixels().Y));
                    }
                    return;
                }
            }

            if (HasPlayerPosition && !_fleeFromPlayer)
            {
                float distToPlayer = Vector2.Distance(PixelPosition, PlayerPosition);
                if (distToPlayer < PlayerFleeRange)
                {
                    _fleeFromPlayer      = true;
                    _fleeFromPlayerTimer = PlayerFleeDuration;
                    return;
                }
            }

            // ── Normal wander (bias toward horizontal — water edge behavior) ───
            _wanderTimer -= dt;
            if (_wanderTimer <= 0f)
            {
                _wanderTimer = WanderInterval;
                var rng = new Random();
                // Bias strongly horizontal to simulate hugging water edges
                float angle = (float)(rng.NextDouble() * Math.PI * 0.5 - Math.PI * 0.25); // ±45°
                float r     = 50f + (float)(rng.NextDouble() * 30f);
                _wanderTarget = PixelPosition + new Vector2(
                    (float)Math.Cos(angle) * r, (float)Math.Sin(angle) * 10f);
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
            : base("Slime Trail Spit", "Slime trail immobilizes enemies, draws salamanders near", SkillActivationType.Instant, cooldown: 4f) => _salamander = s;
        protected override void OnActivate(float power)
        {
            // Effect applied via ApplySameTypeEffect / ApplyDifferentTypeEffect
            // triggered by EntityControlSystem when it detects skill fired
        }
    }
}
