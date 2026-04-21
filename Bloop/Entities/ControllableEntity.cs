using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using Bloop.Core;
using Bloop.Gameplay;
using Bloop.Physics;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Entities
{
    /// <summary>
    /// Identifies which type of controllable entity this is.
    /// Used for same-type / different-type effect discrimination.
    /// </summary>
    public enum ControllableEntityType
    {
        EchoBat,
        SilkWeaverSpider,
        ChainCentipede,
        LuminescentGlowworm,
        DeepBurrowWorm,
        BlindCaveSalamander,
        LuminousIsopod
    }

    /// <summary>
    /// Abstract base class for all 7 controllable cave entities.
    /// Extends <see cref="WorldObject"/> with possession mechanics, skill routing,
    /// and inter-entity effect state (following, fleeing, stuck, etc.).
    ///
    /// Subclasses must implement:
    ///   - <see cref="UpdateControlled"/> — movement + skill while possessed
    ///   - <see cref="UpdateIdle"/>       — AI behaviour when not possessed
    ///   - <see cref="OnControlStart"/>   — setup when possession begins
    ///   - <see cref="OnControlEnd"/>     — cleanup when possession ends
    ///   - <see cref="Draw"/>             — primitive rendering (delegates to EntityRenderer)
    ///   - <see cref="GetBounds"/>        — pixel-space AABB for culling
    /// </summary>
    public abstract class ControllableEntity : WorldObject
    {
        // ── Identity ───────────────────────────────────────────────────────────
        /// <summary>Which entity type this is (used for effect discrimination).</summary>
        public ControllableEntityType EntityType { get; }

        /// <summary>Human-readable name shown in the HUD.</summary>
        public abstract string DisplayName { get; }

        // ── Control duration ───────────────────────────────────────────────────
        /// <summary>How many seconds the player can possess this entity.</summary>
        public abstract float ControlDuration { get; }

        /// <summary>Remaining possession time in seconds.</summary>
        public float ControlTimer { get; private set; }

        /// <summary>True while the player is possessing this entity.</summary>
        public bool IsControlled { get; private set; }

        // ── Skill ──────────────────────────────────────────────────────────────
        /// <summary>The entity's special skill. Null until subclass assigns it.</summary>
        public EntitySkill? Skill { get; protected set; }

        // ── Movement capabilities ──────────────────────────────────────────────
        public virtual bool CanFly         => false;
        public virtual bool CanWallClimb   => false;
        public virtual bool CanCeilingClimb => false;
        public virtual bool CanBurrow      => false;
        public virtual bool CanSwim        => false;

        /// <summary>Movement speed in pixels per second while controlled.</summary>
        public abstract float MovementSpeed { get; }

        // ── Selection highlight ────────────────────────────────────────────────
        /// <summary>True when the entity is highlighted during selection mode (nearest to cursor).</summary>
        public bool IsHighlighted { get; set; }

        /// <summary>True when the entity is within the 200px selection range.</summary>
        public bool IsInRange { get; set; }

        // ── Inter-entity effect state ──────────────────────────────────────────

        /// <summary>True when following a controlled same-type entity.</summary>
        public bool IsFollowing { get; set; }
        /// <summary>The entity this one is following (same-type effect).</summary>
        public ControllableEntity? FollowTarget { get; set; }
        /// <summary>Recorded path points for the follower to replay.</summary>
        public Queue<Vector2> FollowPath { get; } = new Queue<Vector2>();

        /// <summary>True when disoriented by a different-type skill effect.</summary>
        public bool IsDisoriented { get; set; }
        /// <summary>Remaining disorientation time in seconds.</summary>
        public float DisorientTimer { get; set; }

        /// <summary>True when immobilized by web or slime.</summary>
        public bool IsStuck { get; set; }
        /// <summary>Remaining stuck time in seconds.</summary>
        public float StuckTimer { get; set; }

        /// <summary>True when fleeing from a light source or different-type effect.</summary>
        public bool IsFleeing { get; set; }
        /// <summary>Direction to flee toward.</summary>
        public Vector2 FleeDirection { get; set; }
        /// <summary>Remaining flee time in seconds.</summary>
        public float FleeTimer { get; set; }

        /// <summary>True when infighting with nearby non-same-type entities.</summary>
        public bool IsInfighting { get; set; }
        /// <summary>Remaining infight time in seconds.</summary>
        public float InfightTimer { get; set; }

        // ── Constructor ────────────────────────────────────────────────────────

        protected ControllableEntity(ControllableEntityType entityType,
            Vector2 pixelPosition, AetherWorld world)
            : base(pixelPosition, world)
        {
            EntityType = entityType;
        }

        // ── Control lifecycle ──────────────────────────────────────────────────

        /// <summary>
        /// Begin possession. Called by <see cref="EntityControlSystem"/>.
        /// Sets <see cref="IsControlled"/> = true and starts the control timer.
        /// </summary>
        public void BeginControl()
        {
            IsControlled  = true;
            ControlTimer  = ControlDuration;
            IsHighlighted = false;
            IsInRange     = false;
            Skill?.ForceReset();
            OnControlStart();
        }

        /// <summary>
        /// End possession. Called by <see cref="EntityControlSystem"/> when the
        /// timer expires, the player presses RMB, or the entity is destroyed.
        /// </summary>
        public void EndControl()
        {
            if (!IsControlled) return;
            IsControlled = false;
            ControlTimer = 0f;
            Skill?.ForceReset();
            OnControlEnd();
        }

        // ── WorldObject.Update override ────────────────────────────────────────

        /// <summary>
        /// Main update. Routes to <see cref="UpdateControlled"/> or <see cref="UpdateIdle"/>
        /// and ticks all effect timers.
        /// </summary>
        public override void Update(GameTime gameTime)
        {
            if (IsDestroyed) return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Tick skill cooldown
            Skill?.Update(dt);

            // Tick control timer
            if (IsControlled)
            {
                ControlTimer -= dt;
                if (ControlTimer <= 0f)
                {
                    EndControl();
                    // EntityControlSystem will detect IsControlled == false and clean up
                }
            }

            // Tick effect timers
            TickEffectTimers(dt);

            // Route to controlled or idle update
            if (IsControlled)
                UpdateControlledInternal(gameTime);
            else
                UpdateIdle(gameTime);
        }

        // ── Abstract / virtual hooks ───────────────────────────────────────────

        /// <summary>
        /// Called every frame while the player is possessing this entity.
        /// Subclasses handle WASD movement, skill activation, and physics impulses here.
        /// </summary>
        protected abstract void UpdateControlled(GameTime gameTime);

        /// <summary>
        /// Called every frame when the entity is NOT possessed.
        /// Subclasses implement idle AI (wandering, roosting, following, fleeing, etc.).
        /// </summary>
        protected abstract void UpdateIdle(GameTime gameTime);

        /// <summary>Called once when possession begins. Override to set up control state.</summary>
        protected abstract void OnControlStart();

        /// <summary>Called once when possession ends. Override to clean up control state.</summary>
        protected abstract void OnControlEnd();

        /// <summary>
        /// Apply the same-type skill effect to a list of nearby same-type entities.
        /// Called by the skill's OnActivate when it fires.
        /// </summary>
        public virtual void ApplySameTypeEffect(List<ControllableEntity> sameType) { }

        /// <summary>
        /// Apply the different-type skill effect to a list of nearby different-type entities.
        /// Called by the skill's OnActivate when it fires.
        /// </summary>
        public virtual void ApplyDifferentTypeEffect(List<ControllableEntity> differentType) { }

        /// <summary>
        /// Returns the pixel radius within which this entity's skill affects other entities.
        /// </summary>
        public virtual float GetEffectRadius() => 200f;

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Apply a physics impulse to the entity body in pixel-space direction.
        /// No-op if the body is null.
        /// </summary>
        protected void ApplyImpulse(Vector2 pixelDirection, float pixelSpeed)
        {
            if (Body == null) return;
            var vel = pixelDirection * pixelSpeed;
            Body.LinearVelocity = PhysicsManager.ToMeters(vel);
        }

        /// <summary>
        /// Set the entity body velocity directly in pixel-space.
        /// No-op if the body is null.
        /// </summary>
        protected void SetVelocity(Vector2 pixelVelocity)
        {
            if (Body == null) return;
            Body.LinearVelocity = PhysicsManager.ToMeters(pixelVelocity);
        }

        /// <summary>
        /// Get the current entity velocity in pixel-space.
        /// Returns Vector2.Zero if the body is null.
        /// </summary>
        protected Vector2 GetVelocityPixels()
        {
            if (Body == null) return Vector2.Zero;
            return PhysicsManager.ToPixels(Body.LinearVelocity);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private void UpdateControlledInternal(GameTime gameTime)
        {
            // Guard: if control timer just expired this frame, skip
            if (!IsControlled) return;
            UpdateControlled(gameTime);
        }

        private void TickEffectTimers(float dt)
        {
            if (IsDisoriented)
            {
                DisorientTimer -= dt;
                if (DisorientTimer <= 0f) { IsDisoriented = false; DisorientTimer = 0f; }
            }

            if (IsStuck)
            {
                StuckTimer -= dt;
                if (StuckTimer <= 0f) { IsStuck = false; StuckTimer = 0f; }
            }

            if (IsFleeing)
            {
                FleeTimer -= dt;
                if (FleeTimer <= 0f) { IsFleeing = false; FleeTimer = 0f; }
            }

            if (IsInfighting)
            {
                InfightTimer -= dt;
                if (InfightTimer <= 0f) { IsInfighting = false; InfightTimer = 0f; }
            }
        }
    }
}
