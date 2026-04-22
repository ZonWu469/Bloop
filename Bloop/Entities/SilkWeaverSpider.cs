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
    /// Silk Weaver Spider — controllable cave entity.
    ///
    /// Movement: WASD on surfaces; can wall-climb; hold Space to swing on silk thread.
    /// Skill: Pheromone Web Trail (hold LMB) — sprays glowing trail.
    ///   Same type: other spiders follow the trail, cluster at end.
    ///   Different type: enemies touching trail stuck for 8s.
    ///   Extra: creates web platforms mid-air (temporary solid surfaces).
    /// </summary>
    public class SilkWeaverSpider : ControllableEntity
    {
        public const float WidthPx  = 20f;
        public const float HeightPx = 14f;

        public override string DisplayName    => "Silk Weaver Spider";
        public override float  ControlDuration => 16f;
        public override float  MovementSpeed   => 120f;
        public override bool   CanWallClimb    => true;

        // ── Contact damage ─────────────────────────────────────────────────────
        public override bool  DamagesPlayerOnContact => true;
        public override float ContactDamage          => 8f;
        public override float ContactStunDuration    => 0.5f;

        // ── Light reaction ─────────────────────────────────────────────────────
        public override LightReactionType LightReaction => LightReactionType.ScaredOfLight;
        /// <summary>Spiders react at 25% perceived intensity.</summary>
        public override float LightTolerance => 0.25f;

        // ── Web trail ──────────────────────────────────────────────────────────
        public List<Vector2> WebTrailPoints { get; } = new List<Vector2>();
        private float _trailRecordTimer;
        private const float TrailRecordInterval = 0.05f;

        private readonly InputManager _input;
        private readonly Camera       _camera;

        // ── Idle AI ────────────────────────────────────────────────────────────
        private Vector2 _wanderTarget;
        private float   _wanderTimer;
        private const float WanderInterval = 4f;
        private const float WanderRadius   = 60f;

        // ── Wall patrol + lunge ────────────────────────────────────────────────
        private float _patrolDirection = 1f;   // +1 right, -1 left
        private float _patrolPauseTimer;        // pause between patrol legs
        private bool  _isLunging;
        private float _lungeTimer;
        private Vector2 _lungeTarget;
        private const float LungeRange    = 60f;   // px — player detection for lunge
        private const float LungeSpeed    = 200f;  // px/s during lunge
        private const float LungeDuration = 0.3f;  // seconds
        private const float PatrolSpeed   = 50f;   // px/s during wall patrol
        private const float PatrolPauseMax = 2f;   // max pause seconds

        public SilkWeaverSpider(Vector2 pixelPosition, AetherWorld world,
            InputManager input, Camera camera)
            : base(ControllableEntityType.SilkWeaverSpider, pixelPosition, world)
        {
            _input  = input;
            _camera = camera;

            _wanderTarget = pixelPosition;

            Body = BodyFactory.CreateEntityBody(world, pixelPosition, WidthPx, HeightPx, canFly: false);
            Body.Tag = this;

            Skill = new PheromoneWebTrailSkill(this);
        }

        public override (string description, string? actionHint) GetTooltipInfo()
            => ("Venomous wall-crawler. Spins sticky webs.", "[Q] Control — 8s wall climb");

        protected override void OnControlStart()
        {
            WebTrailPoints.Clear();
            _trailRecordTimer = 0f;
        }

        protected override void OnControlEnd()
        {
            _wanderTarget = PixelPosition;
            _wanderTimer  = 0f;
        }

        protected override void UpdateControlled(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

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
                SetVelocity(GetVelocityPixels() * 0.7f);
            }

            // E key activates/deactivates web trail
            if (_input.IsInteractPressed())
            {
                if (Skill?.IsActive == true)
                    Skill.Deactivate();
                else
                    Skill?.TryActivate();
            }

            // Record web trail when skill is active
            if (Skill?.IsActive == true)
            {
                _trailRecordTimer -= dt;
                if (_trailRecordTimer <= 0f)
                {
                    _trailRecordTimer = TrailRecordInterval;
                    WebTrailPoints.Add(PixelPosition);
                    if (WebTrailPoints.Count > 200)
                        WebTrailPoints.RemoveAt(0);
                }
            }
        }

        protected override void UpdateIdle(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (IsStuck) { SetVelocity(Vector2.Zero); return; }
            if (IsFleeing) { SetVelocity(FleeDirection * MovementSpeed * 0.5f); return; }

            // ── Light reaction: scared spiders flee and cancel lunge ───────────
            if (LightReactionStrength > 0f)
            {
                // Cancel any ongoing lunge
                if (_isLunging)
                {
                    _isLunging    = false;
                    _lungeTimer   = 0f;
                    _wanderTarget = PixelPosition;
                    _wanderTimer  = WanderInterval;
                }

                // Flee away from the light source
                Vector2 fleeDir = -LightSourceDirection;
                float fleeSpeed = PatrolSpeed * (1f + LightReactionStrength * 2f);
                SetVelocity(new Vector2(
                    fleeDir.LengthSquared() > 0.0001f ? MathF.Sign(fleeDir.X) * fleeSpeed : fleeSpeed,
                    GetVelocityPixels().Y));
                return;
            }

            // ── Lunge at player when close ─────────────────────────────────────
            if (_isLunging)
            {
                _lungeTimer -= dt;
                Vector2 toLunge = _lungeTarget - PixelPosition;
                if (toLunge.LengthSquared() > 4f)
                    SetVelocity(Vector2.Normalize(toLunge) * LungeSpeed);
                else
                    SetVelocity(Vector2.Zero);

                if (_lungeTimer <= 0f)
                {
                    _isLunging    = false;
                    _wanderTarget = PixelPosition; // retreat to current spot
                    _wanderTimer  = WanderInterval;
                }
                return;
            }

            // ── Detect player for lunge ────────────────────────────────────────
            if (HasPlayerPosition)
            {
                float distToPlayer = Vector2.Distance(PixelPosition, PlayerPosition);
                if (distToPlayer < LungeRange)
                {
                    _isLunging   = true;
                    _lungeTimer  = LungeDuration;
                    _lungeTarget = PlayerPosition;
                    return;
                }
            }

            // ── Wall patrol: move horizontally, pause occasionally ─────────────
            if (_patrolPauseTimer > 0f)
            {
                _patrolPauseTimer -= dt;
                SetVelocity(Vector2.Zero);
                return;
            }

            _wanderTimer -= dt;
            if (_wanderTimer <= 0f)
            {
                _wanderTimer = WanderInterval;
                // Occasionally reverse patrol direction or pause
                var rng = new Random();
                if (rng.NextDouble() < 0.3)
                    _patrolDirection = -_patrolDirection;
                else if (rng.NextDouble() < 0.2)
                    _patrolPauseTimer = (float)(rng.NextDouble() * PatrolPauseMax);
                // Update wander target along patrol direction
                _wanderTarget = PixelPosition + new Vector2(_patrolDirection * WanderRadius, 0f);
            }

            Vector2 toTarget = _wanderTarget - PixelPosition;
            if (toTarget.LengthSquared() > 4f)
                SetVelocity(new Vector2(MathF.Sign(toTarget.X) * PatrolSpeed, GetVelocityPixels().Y));
            else
            {
                SetVelocity(Vector2.Zero);
                _patrolDirection = -_patrolDirection; // reverse at end of patrol leg
                _wanderTimer = 0f;
            }
        }

        public override void Draw(SpriteBatch spriteBatch, Bloop.Core.AssetManager assets)
        {
            if (IsDestroyed) return;
            EntityRenderer.DrawSilkWeaverSpider(spriteBatch, assets, this);
        }

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - WidthPx / 2f),
            (int)(PixelPosition.Y - HeightPx / 2f),
            (int)WidthPx, (int)HeightPx);

        public override float GetEffectRadius() => 160f;

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
            foreach (var e in differentType)
            {
                e.IsStuck   = true;
                e.StuckTimer = 8f;
            }
        }
    }

    internal sealed class PheromoneWebTrailSkill : EntitySkill
    {
        private readonly SilkWeaverSpider _spider;
        public PheromoneWebTrailSkill(SilkWeaverSpider spider)
            : base("Pheromone Web Trail", "Web trail snares enemies, coordinates nearby spiders", SkillActivationType.Hold, cooldown: 0f) => _spider = spider;
        protected override void OnActivate(float power) { }
        protected override void OnDeactivate() { _spider.WebTrailPoints.Clear(); }
    }
}
