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
    /// Deep Burrow Worm — controllable cave entity.
    ///
    /// Movement: Slow surface crawl; Skill dives underground for fast travel.
    /// Skill: Seismic Burrow (6s CD) — dive underground, move short distance, erupt.
    ///   Same type: 2-3 worms erupt at random nearby spots (worm elevators).
    ///   Different type: surface enemies above burrow path stunned 5s.
    ///   Extra: can travel under gaps/pits while burrowed.
    /// </summary>
    public class DeepBurrowWorm : ControllableEntity
    {
        public const float WidthPx  = 20f; // 2× larger
        public const float HeightPx = 48f; // 2× larger

        public override string DisplayName    => "Deep Burrow Worm";
        public override float  ControlDuration => 20f;
        public override float  MovementSpeed   => 50f;
        public override bool   CanBurrow       => true;

        // ── Contact damage ─────────────────────────────────────────────────────
        public override bool  DamagesPlayerOnContact => true;
        public override float ContactDamage          => 10f;
        public override float ContactStunDuration    => 0.5f;

        // ── Light reaction ─────────────────────────────────────────────────────
        public override LightReactionType LightReaction => LightReactionType.AttractedToLight;
        /// <summary>Worms react at 35% perceived intensity.</summary>
        public override float LightTolerance => 0.35f;

        // ── Burrow state ───────────────────────────────────────────────────────
        public bool IsBurrowing { get; private set; }
        private float _burrowTimer;
        private Vector2 _burrowTarget;
        private const float BurrowDuration = 1.5f;
        private const float BurrowSpeed    = 100f;

        private readonly InputManager _input;

        // ── Idle AI ────────────────────────────────────────────────────────────
        private Vector2 _wanderTarget;
        private float   _wanderTimer;
        private const float WanderInterval = 4f;

        // ── Idle burrow + ambush ───────────────────────────────────────────────
        private bool  _isBurrowed;           // hiding underground between ambushes
        private float _idleBurrowTimer;      // time to stay burrowed
        private float _ambushCooldown;       // cooldown after emerging
        private const float IdleBurrowInterval = 6f;   // seconds between burrow cycles
        private const float IdleBurrowDuration = 3f;   // seconds spent underground
        private const float AmbushRange        = 40f;  // px — player above detection
        private const float AmbushCooldownTime = 4f;   // seconds before next ambush

        public DeepBurrowWorm(Vector2 pixelPosition, AetherWorld world, InputManager input)
            : base(ControllableEntityType.DeepBurrowWorm, pixelPosition, world)
        {
            _input        = input;
            _wanderTarget = pixelPosition;

            Body = BodyFactory.CreateEntityBody(world, pixelPosition, WidthPx, HeightPx, canFly: false);
            Body.Tag = this;

            Skill = new SeismicBurrowSkill(this);
        }

        protected override void OnControlStart() { IsBurrowing = false; }

        public override (string description, string? actionHint) GetTooltipInfo()
            => ("Subterranean ambush predator. Burrows to surprise prey.", "[Q] Control — 8s tunnel dig");

        protected override void OnControlEnd()
        {
            IsBurrowing   = false;
            _wanderTarget = PixelPosition;
            _wanderTimer  = 0f;
            if (Body != null) Body.IgnoreGravity = false;
        }

        protected override void UpdateControlled(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (IsBurrowing)
            {
                _burrowTimer -= dt;
                Vector2 toTarget = _burrowTarget - PixelPosition;
                if (toTarget.LengthSquared() > 4f)
                    SetVelocity(Vector2.Normalize(toTarget) * BurrowSpeed);
                else
                    SetVelocity(Vector2.Zero);

                if (_burrowTimer <= 0f)
                    EndBurrow();
                return;
            }

            float horiz  = _input.GetHorizontalAxis();
            float physVY = GetVelocityPixels().Y;
            var dir = new Vector2(horiz, 0f);
            if (dir.LengthSquared() > 0.01f)
            {
                SetVelocity(new Vector2(horiz * MovementSpeed, physVY));
                FacingDirection = MathF.Sign(horiz);
            }
            else
                SetVelocity(new Vector2(GetVelocityPixels().X * 0.7f, physVY));

            if (_input.IsInteractPressed())
                Skill?.TryActivate();
        }

        protected override void UpdateIdle(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Tick ambush cooldown
            if (_ambushCooldown > 0f) _ambushCooldown -= dt;

            // ── Skill-triggered burrow (from TriggerBurrow) ───────────────────
            if (IsBurrowing)
            {
                _burrowTimer -= dt;
                if (_burrowTimer <= 0f) EndBurrow();
                return;
            }

            if (IsStuck) { SetVelocity(Vector2.Zero); return; }

            // ── Light attraction: bias wander toward light source ─────────────
            if (LightReactionStrength > 0f && LightSourceDirection.LengthSquared() > 0.0001f)
            {
                // Emerge from idle burrow if currently hiding
                if (_isBurrowed)
                {
                    _isBurrowed = false;
                    if (Body != null) Body.IgnoreGravity = false;
                    _wanderTimer = WanderInterval;
                }

                // Crawl toward the light
                float attractSpeed = MovementSpeed * 0.5f * (0.5f + LightReactionStrength * 0.5f);
                SetVelocity(new Vector2(
                    LightSourceDirection.X > 0f ? attractSpeed : -attractSpeed,
                    GetVelocityPixels().Y));
                return;
            }

            // ── Idle burrow cycle: periodically hide underground ───────────────
            if (_isBurrowed)
            {
                _idleBurrowTimer -= dt;
                SetVelocity(Vector2.Zero);

                // While burrowed, check if player is directly above within AmbushRange
                if (HasPlayerPosition && _ambushCooldown <= 0f)
                {
                    float dx = MathF.Abs(PlayerPosition.X - PixelPosition.X);
                    float dy = PixelPosition.Y - PlayerPosition.Y; // positive = player above
                    if (dx < 20f && dy > 0f && dy < AmbushRange)
                    {
                        // Ambush: erupt upward
                        _isBurrowed      = false;
                        _ambushCooldown  = AmbushCooldownTime;
                        if (Body != null) Body.IgnoreGravity = false;
                        SetVelocity(new Vector2(0f, -120f)); // burst upward
                        return;
                    }
                }

                if (_idleBurrowTimer <= 0f)
                {
                    // Emerge from idle burrow
                    _isBurrowed = false;
                    if (Body != null) Body.IgnoreGravity = false;
                    _wanderTimer = WanderInterval;
                }
                return;
            }

            // ── Periodically burrow to hide ────────────────────────────────────
            _wanderTimer -= dt;
            if (_wanderTimer <= 0f)
            {
                _wanderTimer = WanderInterval;
                var rng = new Random();
                if (rng.NextDouble() < 0.35 && _ambushCooldown <= 0f)
                {
                    // Enter idle burrow
                    _isBurrowed      = true;
                    _idleBurrowTimer = IdleBurrowDuration;
                    if (Body != null) Body.IgnoreGravity = true;
                    SetVelocity(Vector2.Zero);
                    return;
                }
                float dir = rng.NextDouble() > 0.5 ? 1f : -1f;
                _wanderTarget = PixelPosition + new Vector2(dir * 60f, 0f);
            }

            Vector2 toWander = _wanderTarget - PixelPosition;
            if (toWander.LengthSquared() > 4f)
            {
                float vx = Math.Sign(toWander.X) * MovementSpeed * 0.5f;
                SetVelocity(new Vector2(vx, GetVelocityPixels().Y));
                FacingDirection = MathF.Sign(toWander.X);
            }
            else
                SetVelocity(new Vector2(0f, GetVelocityPixels().Y));
        }

        public override void Draw(SpriteBatch spriteBatch, Bloop.Core.AssetManager assets)
        {
            if (IsDestroyed) return;

            // Burrowing dust cloud drawn before sprite (or instead of sprite when fully burrowed)
            if (IsBurrowing)
            {
                var dirtColor = new Color(120, 80, 40, 180);
                GeometryBatch.DrawCircleApprox(spriteBatch, assets, PixelPosition,
                    WidthPx * 0.6f, dirtColor, 8);
                return; // don't draw sprite while underground
            }

            EntityRenderer.DrawEntity(spriteBatch, assets, this,
                WidthPx, HeightPx, assets.EntityDeepBurrowWorm);
        }

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - WidthPx / 2f),
            (int)(PixelPosition.Y - HeightPx / 2f),
            (int)WidthPx, (int)HeightPx);

        public override float GetEffectRadius() => 120f;

        public override void ApplySameTypeEffect(List<ControllableEntity> sameType)
        {
            int count = 0;
            foreach (var e in sameType)
            {
                if (e == this || count >= 3) break;
                if (e is DeepBurrowWorm worm)
                    worm.TriggerBurrow(PixelPosition + new Vector2(
                        (float)(new Random().NextDouble() * 80 - 40), 0f));
                count++;
            }
        }

        public override void ApplyDifferentTypeEffect(List<ControllableEntity> differentType)
        {
            foreach (var e in differentType)
            {
                e.IsDisoriented  = true;
                e.DisorientTimer = 5f;
            }
        }

        internal void TriggerBurrow(Vector2 target)
        {
            if (IsBurrowing) return;
            IsBurrowing  = true;
            _burrowTimer = BurrowDuration;
            _burrowTarget = target;
            if (Body != null) Body.IgnoreGravity = true;
        }

        private void EndBurrow()
        {
            IsBurrowing = false;
            if (Body != null) Body.IgnoreGravity = false;
            SetVelocity(new Vector2(0f, -80f)); // erupt upward
        }
    }

    internal sealed class SeismicBurrowSkill : EntitySkill
    {
        private readonly DeepBurrowWorm _worm;
        public SeismicBurrowSkill(DeepBurrowWorm w)
            : base("Seismic Burrow", "Burrow through soft terrain", SkillActivationType.Instant, cooldown: 6f) => _worm = w;
        protected override void OnActivate(float power)
        {
            // Burrow toward a point 80px ahead in facing direction
            _worm.TriggerBurrow(_worm.PixelPosition + new Vector2(80f, 0f));
        }
    }
}
