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
    /// Luminous Isopod — controllable cave entity (special case).
    ///
    /// Unlike other entities, the Isopod ATTACHES to the player body rather than
    /// freezing the player. The player moves normally while the isopod rides along.
    ///
    /// Passive: Constant blue-green glow (medium radius light source).
    /// Skill: Glow Surge (instant, 6s CD) — bright pulse.
    ///   Same type: nearby isopods drawn to glow, form convoy following player path.
    ///   Different type: enemies flee for 8s; passive glow mildly repels.
    ///   Extra: can crawl on any surface; squeeze through tiny cracks.
    ///
    /// Throw mechanic: Press T to throw in mouse direction.
    ///   - Show trajectory arc while T is held.
    ///   - On T release: isopod launches as a projectile.
    ///   - Lands and becomes a temporary world light source.
    ///   - Crawls back toward player after landing (or stops if timer expires).
    /// </summary>
    public class LuminousIsopod : ControllableEntity
    {
        public const float WidthPx  = 28f; // 2× larger
        public const float HeightPx = 16f; // 2× larger

        public override string DisplayName    => "Luminous Isopod";
        public override float  ControlDuration => 30f;
        public override float  MovementSpeed   => 80f;

        // ── Light reaction ─────────────────────────────────────────────────────
        public override LightReactionType LightReaction => LightReactionType.AttractedToLight;
        /// <summary>Isopods are the most tolerant — only react at 55% perceived intensity.</summary>
        public override float LightTolerance => 0.55f;

        // ── Glow Surge state ───────────────────────────────────────────────────
        public bool  GlowSurgeActive { get; private set; }
        public float GlowSurgeRadius { get; private set; }
        public const float GlowSurgeMaxRadius = 200f;
        private const float GlowSurgeExpandSpeed = 450f;

        // ── Throw mechanic ─────────────────────────────────────────────────────
        /// <summary>True while T is held — show trajectory arc.</summary>
        public bool ShowTrajectory { get; private set; }
        /// <summary>Precomputed trajectory arc points for rendering.</summary>
        public Vector2[]? TrajectoryPoints { get; private set; }
        private const float ThrowSpeed    = 300f;
        private const float MaxThrowDist  = 300f;
        private const int   TrajectorySteps = 20;

        // ── Attach / throw state ───────────────────────────────────────────────
        private enum IsopodState { Attached, Thrown, CrawlingBack, Idle }
        private IsopodState _state = IsopodState.Idle;

        /// <summary>The player the isopod is attached to (set when control begins).</summary>
        private Bloop.Gameplay.Player? _attachedPlayer;
        private Vector2 _attachOffset = new Vector2(-12f, 8f); // relative to player center

        // ── Crawl-back state ───────────────────────────────────────────────────
        private const float CrawlBackSpeed = 60f;

        // ── References ─────────────────────────────────────────────────────────
        private readonly InputManager _input;
        private readonly Camera       _camera;
        private LightSource? _ambientLight;

        // ── Idle AI ────────────────────────────────────────────────────────────
        private Vector2 _wanderTarget;
        private float   _wanderTimer;
        private const float WanderInterval = 4f;

        // ── Scatter + regroup ──────────────────────────────────────────────────
        private bool    _isScattered;
        private float   _scatterTimer;
        private Vector2 _scatterDirection;
        private float   _regroupTimer;
        private Vector2 _regroupTarget;
        private const float ScatterRange    = 60f;   // px — player detection
        private const float ScatterDuration = 2f;    // seconds of scatter
        private const float RegroupDuration = 5f;    // seconds to regroup
        private const float ScatterSpeed    = 90f;   // px/s while scattering
        private const float RegroupSpeed    = 40f;   // px/s while regrouping

        public LuminousIsopod(Vector2 pixelPosition, AetherWorld world,
            InputManager input, Camera camera)
            : base(ControllableEntityType.LuminousIsopod, pixelPosition, world)
        {
            _input  = input;
            _camera = camera;

            _wanderTarget = pixelPosition;

            Body = BodyFactory.CreateEntityBody(world, pixelPosition, WidthPx, HeightPx, canFly: false);
            Body.Tag = this;

            Skill = new GlowSurgeSkill(this);
        }

        public override (string description, string? actionHint) GetTooltipInfo()
            => ("Glowing cave crustacean. Scatters when threatened.", "[Q] Control — attaches to player, emits light");

        public void SetLightSource(LightSource light)
        {
            _ambientLight = light;
            light.Position = PixelPosition;
        }

        // ── Control lifecycle ──────────────────────────────────────────────────

        protected override void OnControlStart()
        {
            // Isopod attaches to the player — player reference is set by EntityControlSystem
            // via SetAttachedPlayer() before BeginControl() is called.
            _state = IsopodState.Attached;
            ShowTrajectory = false;
            TrajectoryPoints = null;

            if (Body != null)
            {
                Body.IgnoreGravity = true;
                Body.LinearDamping = 99f;
            }
        }

        protected override void OnControlEnd()
        {
            _state         = IsopodState.Idle;
            _attachedPlayer = null;
            ShowTrajectory  = false;
            TrajectoryPoints = null;

            if (Body != null)
            {
                Body.IgnoreGravity = false;
                Body.LinearDamping = 0f;
            }

            _wanderTarget = PixelPosition;
            _wanderTimer  = 0f;
        }

        /// <summary>
        /// Called by EntityControlSystem to wire the player reference before control begins.
        /// </summary>
        public void SetAttachedPlayer(Bloop.Gameplay.Player player)
        {
            _attachedPlayer = player;
        }

        // ── Update ─────────────────────────────────────────────────────────────

        protected override void UpdateControlled(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_ambientLight != null) _ambientLight.Position = PixelPosition;

            // Tick Glow Surge ring
            if (GlowSurgeActive)
            {
                GlowSurgeRadius += GlowSurgeExpandSpeed * dt;
                if (GlowSurgeRadius >= GlowSurgeMaxRadius)
                {
                    GlowSurgeActive = false;
                    GlowSurgeRadius = 0f;
                }
            }

            switch (_state)
            {
                case IsopodState.Attached:
                    UpdateAttached(dt);
                    break;

                case IsopodState.Thrown:
                    UpdateThrown(dt);
                    break;

                case IsopodState.CrawlingBack:
                    UpdateCrawlingBack(dt);
                    break;
            }
        }

        protected override void UpdateIdle(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_ambientLight != null) _ambientLight.Position = PixelPosition;

            if (GlowSurgeActive)
            {
                GlowSurgeRadius += GlowSurgeExpandSpeed * dt;
                if (GlowSurgeRadius >= GlowSurgeMaxRadius)
                {
                    GlowSurgeActive = false;
                    GlowSurgeRadius = 0f;
                }
            }

            // ── Skill-triggered flee (different-type effect) ───────────────────
            if (IsFleeing) { SetVelocity(FleeDirection * MovementSpeed * 0.5f); return; }

            if (IsFollowing && _attachedPlayer != null)
            {
                Vector2 toPlayer = _attachedPlayer.PixelPosition - PixelPosition;
                if (toPlayer.LengthSquared() > 4f)
                    SetVelocity(Vector2.Normalize(toPlayer) * MovementSpeed);
                DisorientTimer -= dt;
                if (DisorientTimer <= 0f) { IsFollowing = false; FollowTarget = null; }
                return;
            }

            // ── Light attraction: bias wander toward light source ─────────────
            // Only when not in scatter/regroup state (preserve existing behavior)
            if (LightReactionStrength > 0f && LightSourceDirection.LengthSquared() > 0.0001f
                && !_isScattered && _regroupTimer <= 0f)
            {
                float attractSpeed = MovementSpeed * 0.4f * LightReactionStrength;
                SetVelocity(new Vector2(LightSourceDirection.X * attractSpeed, GetVelocityPixels().Y));
                return;
            }

            // ── Scatter when player approaches ────────────────────────────────
            if (_isScattered)
            {
                _scatterTimer -= dt;
                float scatterVx = _scatterDirection.X * ScatterSpeed;
                SetVelocity(new Vector2(scatterVx, GetVelocityPixels().Y));
                if (scatterVx != 0f) FacingDirection = MathF.Sign(scatterVx);

                if (_scatterTimer <= 0f)
                {
                    _isScattered  = false;
                    _regroupTimer = RegroupDuration;
                    // Regroup toward original wander target
                    _regroupTarget = _wanderTarget;
                }
                return;
            }

            if (_regroupTimer > 0f)
            {
                _regroupTimer -= dt;
                Vector2 toRegroup = _regroupTarget - PixelPosition;
                if (toRegroup.LengthSquared() > 4f)
                {
                    var vel = Vector2.Normalize(toRegroup) * RegroupSpeed;
                    SetVelocity(vel);
                    if (vel.X != 0f) FacingDirection = MathF.Sign(vel.X);
                }
                else
                    SetVelocity(new Vector2(0f, GetVelocityPixels().Y));
                return;
            }

            // Detect player proximity to trigger scatter
            if (HasPlayerPosition && !_isScattered)
            {
                float distToPlayer = Vector2.Distance(PixelPosition, PlayerPosition);
                if (distToPlayer < ScatterRange)
                {
                    _isScattered = true;
                    _scatterTimer = ScatterDuration;
                    // Scatter away from player
                    var rng = new Random();
                    float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                    _scatterDirection = new Vector2((float)Math.Cos(angle), 0f);
                    return;
                }
            }

            // ── Normal wander ─────────────────────────────────────────────────
            _wanderTimer -= dt;
            if (_wanderTimer <= 0f)
            {
                _wanderTimer = WanderInterval;
                var rng2 = new Random();
                float a = (float)(rng2.NextDouble() * Math.PI * 2.0);
                _wanderTarget = PixelPosition + new Vector2(
                    (float)Math.Cos(a) * 40f, (float)Math.Sin(a) * 20f);
            }

            Vector2 toWander = _wanderTarget - PixelPosition;
            if (toWander.LengthSquared() > 4f)
            {
                var vel = Vector2.Normalize(toWander) * MovementSpeed * 0.4f;
                SetVelocity(vel);
                if (vel.X != 0f) FacingDirection = MathF.Sign(vel.X);
            }
            else
                SetVelocity(new Vector2(0f, GetVelocityPixels().Y));
        }

        // ── Attached state ─────────────────────────────────────────────────────

        private void UpdateAttached(float dt)
        {
            if (_attachedPlayer == null) return;

            // Track player position with offset — move the physics body directly.
            // PixelPosition reads from Body.Position when Body != null, so updating
            // the body is sufficient; no need to set the backing field separately.
            Vector2 targetPos = _attachedPlayer.PixelPosition + _attachOffset;
            if (Body != null)
                Body.Position = PhysicsManager.ToMeters(targetPos);

            // E key: Glow Surge
            if (_input.IsInteractPressed())
                Skill?.TryActivate();

            // T key: show trajectory / throw
            if (_input.IsThrowHeld())
            {
                ShowTrajectory = true;
                Vector2 mouseScreen = _input.GetMouseWorldPosition();
                Vector2 mouseWorld  = _camera.ScreenToWorld(mouseScreen);
                UpdateTrajectoryPreview(mouseWorld);
            }
            else if (_input.IsThrowReleased() && ShowTrajectory)
            {
                ShowTrajectory = false;
                Vector2 mouseScreen = _input.GetMouseWorldPosition();
                Vector2 mouseWorld  = _camera.ScreenToWorld(mouseScreen);
                ThrowIsopod(mouseWorld);
            }
            else
            {
                ShowTrajectory   = false;
                TrajectoryPoints = null;
            }
        }

        // ── Thrown state ───────────────────────────────────────────────────────

        private void UpdateThrown(float dt)
        {
            if (Body == null) return;

            // Physics handles the projectile arc (gravity is re-enabled on throw)
            // Check if we've landed (very low vertical velocity + grounded approximation)
            float speed = GetVelocityPixels().Length();
            if (speed < 20f)
            {
                // Landed — become a light source and start crawling back
                _state = IsopodState.CrawlingBack;
                if (Body != null)
                {
                    Body.IgnoreGravity = false;
                    Body.LinearDamping = 4f;
                }
            }
        }

        // ── Crawling back state ────────────────────────────────────────────────

        private void UpdateCrawlingBack(float dt)
        {
            if (_attachedPlayer == null)
            {
                // Control ended while crawling — just stop
                _state = IsopodState.Idle;
                return;
            }

            Vector2 toPlayer = _attachedPlayer.PixelPosition - PixelPosition;
            float dist = toPlayer.Length();

            if (dist < 16f)
            {
                // Reached player — re-attach
                _state = IsopodState.Attached;
                if (Body != null)
                {
                    Body.IgnoreGravity = true;
                    Body.LinearDamping = 99f;
                }
                return;
            }

            var crawlVel = Vector2.Normalize(toPlayer) * CrawlBackSpeed;
            SetVelocity(crawlVel);
            if (crawlVel.X != 0f) FacingDirection = MathF.Sign(crawlVel.X);
        }

        // ── Throw helpers ──────────────────────────────────────────────────────

        private void ThrowIsopod(Vector2 mouseWorldPos)
        {
            Vector2 dir = mouseWorldPos - PixelPosition;
            if (dir.LengthSquared() < 0.01f) dir = Vector2.UnitX;
            dir = Vector2.Normalize(dir);

            // Clamp to max throw distance
            float dist = Vector2.Distance(mouseWorldPos, PixelPosition);
            if (dist > MaxThrowDist)
                mouseWorldPos = PixelPosition + dir * MaxThrowDist;

            _state = IsopodState.Thrown;

            if (Body != null)
            {
                Body.IgnoreGravity = false;
                Body.LinearDamping = 0.5f;
                Body.LinearVelocity = PhysicsManager.ToMeters(dir * ThrowSpeed);
            }

            TrajectoryPoints = null;
        }

        private void UpdateTrajectoryPreview(Vector2 mouseWorldPos)
        {
            Vector2 dir = mouseWorldPos - PixelPosition;
            if (dir.LengthSquared() < 0.01f) { TrajectoryPoints = null; return; }
            dir = Vector2.Normalize(dir);

            float dist = Math.Min(Vector2.Distance(mouseWorldPos, PixelPosition), MaxThrowDist);
            Vector2 launchVel = dir * ThrowSpeed;

            // Simulate parabolic arc (gravity ≈ 9.8 m/s² = 980 px/s² at 100px/m)
            const float gravity = 980f; // px/s²
            const float simDt   = 0.05f;
            int steps = TrajectorySteps;

            var pts = new Vector2[steps + 1];
            Vector2 pos = PixelPosition;
            Vector2 vel = launchVel;

            for (int i = 0; i <= steps; i++)
            {
                pts[i] = pos;
                vel += new Vector2(0f, gravity * simDt);
                pos += vel * simDt;

                // Stop if we've gone past max distance
                if (Vector2.Distance(pts[i], PixelPosition) >= MaxThrowDist)
                {
                    // Trim array
                    var trimmed = new Vector2[i + 1];
                    Array.Copy(pts, trimmed, i + 1);
                    TrajectoryPoints = trimmed;
                    return;
                }
            }

            TrajectoryPoints = pts;
        }

        // ── WorldObject overrides ──────────────────────────────────────────────

        public override void Draw(SpriteBatch spriteBatch, Bloop.Core.AssetManager assets)
        {
            if (IsDestroyed) return;

            // Glow surge ring drawn before sprite
            if (GlowSurgeActive)
            {
                float surgeAlpha = 1f - GlowSurgeRadius / GlowSurgeMaxRadius;
                var surgeColor = new Color(100, 220, 255, (int)(surgeAlpha * 180));
                GeometryBatch.DrawCircleOutline(spriteBatch, assets, PixelPosition, GlowSurgeRadius, surgeColor, 2);
            }

            // Trajectory preview drawn before sprite
            if (ShowTrajectory)
            {
                var pts = TrajectoryPoints;
                if (pts != null && pts.Length >= 2)
                {
                    var trailColor = new Color(80, 200, 220, 100);
                    for (int i = 0; i < pts.Length - 1; i++)
                    {
                        float alpha = (i / (float)pts.Length) * 0.6f;
                        GeometryBatch.DrawLine(spriteBatch, assets, pts[i], pts[i + 1],
                            new Color(trailColor.R, trailColor.G, trailColor.B, (int)(alpha * 255)), 1f);
                    }
                }
            }

            EntityRenderer.DrawEntity(spriteBatch, assets, this,
                WidthPx, HeightPx, assets.EntityLuminousIsopod);
        }

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - WidthPx / 2f),
            (int)(PixelPosition.Y - HeightPx / 2f),
            (int)WidthPx, (int)HeightPx);

        public override float GetEffectRadius() => GlowSurgeMaxRadius;

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
                e.FleeTimer   = 8f;
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                e.FleeDirection = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            }
        }

        internal void TriggerGlowSurge()
        {
            GlowSurgeActive = true;
            GlowSurgeRadius = 0f;
        }
    }

    internal sealed class GlowSurgeSkill : EntitySkill
    {
        private readonly LuminousIsopod _isopod;
        public GlowSurgeSkill(LuminousIsopod i)
            : base("Glow Surge", "Glow surge disorients all nearby creatures", SkillActivationType.Instant, cooldown: 6f) => _isopod = i;
        protected override void OnActivate(float power) => _isopod.TriggerGlowSurge();
    }
}
