using System;
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
    /// How this entity reacts to player-owned light sources (lantern + flares).
    /// </summary>
    public enum LightReactionType
    {
        /// <summary>No reaction to light.</summary>
        None,
        /// <summary>Flees from light; cancels aggro when illuminated.</summary>
        ScaredOfLight,
        /// <summary>Wanders toward light sources (biased movement).</summary>
        AttractedToLight
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

        // ── Facing direction ───────────────────────────────────────────────────
        /// <summary>
        /// +1 = facing right (sprite default), -1 = facing left.
        /// Updated each frame by subclasses based on horizontal velocity.
        /// Used by EntityRenderer to flip the spritesheet horizontally.
        /// </summary>
        public float FacingDirection { get; protected set; } = 1f;

        // ── Contact damage ─────────────────────────────────────────────────────
        /// <summary>Whether this entity damages the player on contact when idle (not controlled).</summary>
        public virtual bool DamagesPlayerOnContact => false;
        /// <summary>Damage dealt to the player on contact. Only used when DamagesPlayerOnContact is true.</summary>
        public virtual float ContactDamage => 0f;
        /// <summary>Stun duration in seconds applied to the player on contact. 0 = no stun.</summary>
        public virtual float ContactStunDuration => 0f;

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

        // ── Tooltip info (Task 11) ─────────────────────────────────────────────
        /// <summary>
        /// Returns a (description, actionHint) tuple for the hover tooltip.
        /// Override in each subclass to provide specific information.
        /// </summary>
        public virtual (string description, string? actionHint) GetTooltipInfo()
            => ("A cave creature.", null);

        // ── Player proximity reference (set each frame by Level.Update) ────────
        /// <summary>
        /// Last known player position in pixel space. Updated each frame by Level.Update().
        /// Used by idle AI for aggro/flee proximity checks without a direct player reference.
        /// </summary>
        protected Vector2 PlayerPosition { get; private set; } = Vector2.Zero;

        /// <summary>True if a player position has been set this session.</summary>
        protected bool HasPlayerPosition { get; private set; }

        /// <summary>
        /// Called by Level.Update() each frame to provide the player's current position.
        /// </summary>
        public void SetPlayerReference(Vector2 playerPixelPosition)
        {
            PlayerPosition    = playerPixelPosition;
            HasPlayerPosition = true;
        }

        // ── Light reaction (set by subclass, driven by Level.Update) ──────────

        /// <summary>
        /// How this entity reacts to player-owned light sources (lantern + flares).
        /// Override in subclasses to set ScaredOfLight or AttractedToLight.
        /// </summary>
        public virtual LightReactionType LightReaction => LightReactionType.None;

        /// <summary>
        /// Minimum perceived light intensity [0..1] before this entity reacts.
        /// Below this threshold the entity ignores the light entirely.
        /// Above it, reaction strength scales linearly from 0 → 1.
        /// Override in subclasses to tune sensitivity.
        /// </summary>
        public virtual float LightTolerance => 0.5f;

        /// <summary>
        /// Perceived light strength [0..1] from all reaction-triggering sources combined.
        /// Computed each frame by Level.Update() via SetLightPerception().
        /// 0 = no light perceived; 1 = fully illuminated.
        /// </summary>
        public float PerceivedLight { get; private set; }

        /// <summary>
        /// Reaction strength after applying the tolerance gradient [0..1].
        /// 0 = below tolerance (no reaction); 1 = maximum reaction.
        /// </summary>
        public float LightReactionStrength { get; private set; }

        /// <summary>
        /// Direction toward the brightest perceived light source (pixel space, normalised).
        /// Zero vector when no light is perceived.
        /// </summary>
        public Vector2 LightSourceDirection { get; private set; }

        /// <summary>
        /// Called by Level.Update() each frame with the summed light perception data
        /// from all player-owned reaction lights (lantern + active flares).
        /// </summary>
        public void SetLightPerception(float perceivedIntensity, Vector2 towardLight)
        {
            PerceivedLight = MathF.Min(perceivedIntensity, 1f);

            // Gradient: reaction_strength = clamp((perceived - tolerance) / (1 - tolerance), 0, 1)
            float tol = LightTolerance;
            LightReactionStrength = tol >= 1f
                ? 0f
                : MathF.Max(0f, MathF.Min(1f, (PerceivedLight - tol) / (1f - tol)));

            LightSourceDirection = towardLight.LengthSquared() > 0.0001f
                ? Vector2.Normalize(towardLight)
                : Vector2.Zero;
        }

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
