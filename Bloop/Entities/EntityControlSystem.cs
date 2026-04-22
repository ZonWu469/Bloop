using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Bloop.Core;
using Bloop.Gameplay;
using Bloop.World;

namespace Bloop.Entities
{
    /// <summary>
    /// Central manager for the entity possession mechanic.
    ///
    /// Lifecycle:
    ///   Normal → (Q pressed, cooldown ready) → Selecting → (LMB on entity in range) → Controlling
    ///   Controlling → (timer expires OR RMB pressed) → Normal (cooldown starts)
    ///   Selecting → (RMB pressed OR Q pressed) → Normal (no cooldown consumed)
    ///
    /// Special case — Luminous Isopod:
    ///   When an Isopod is selected the player does NOT enter PlayerState.Controlling.
    ///   Instead the isopod attaches to the player body and the player moves normally.
    ///   The system tracks this via <see cref="IsIsopodAttached"/>.
    /// </summary>
    public class EntityControlSystem
    {
        // ── Constants ──────────────────────────────────────────────────────────
        /// <summary>Seconds between uses of the control ability.</summary>
        public const float GlobalCooldown = 30f;

        /// <summary>Maximum pixel distance from player to selectable entity.</summary>
        public const float SelectionRange = 200f;

        // ── References ─────────────────────────────────────────────────────────
        private readonly Player       _player;
        private readonly Camera       _camera;
        private readonly InputManager _input;

        // ── State ──────────────────────────────────────────────────────────────
        /// <summary>Remaining global cooldown in seconds.</summary>
        public float CooldownTimer { get; private set; }

        /// <summary>True when the global cooldown has elapsed and the ability can be used.</summary>
        public bool IsReady => CooldownTimer <= 0f;

        /// <summary>True while the player is in entity-selection mode (range circle visible).</summary>
        public bool IsSelecting { get; private set; }

        /// <summary>True while the player is actively possessing an entity.</summary>
        public bool IsControlling { get; private set; }

        /// <summary>True when a Luminous Isopod is attached to the player.</summary>
        public bool IsIsopodAttached { get; private set; }

        /// <summary>The entity currently being controlled (or null).</summary>
        public ControllableEntity? ActiveEntity { get; private set; }

        /// <summary>The entity nearest to the mouse cursor during selection (highlighted).</summary>
        public ControllableEntity? HighlightedEntity { get; private set; }

        // ── Effect dispatch state ──────────────────────────────────────────────
        /// <summary>
        /// True once inter-entity effects have been dispatched for the current skill activation.
        /// Prevents re-dispatching the same skill fire on subsequent frames.
        /// Reset when a new control session begins.
        /// </summary>
        private bool _effectsDispatched = false;

        /// <summary>
        /// Tracks whether the active entity's skill was active last frame.
        /// Used to detect the rising edge of skill activation.
        /// </summary>
        private bool _skillWasActive = false;

        // ── Events ─────────────────────────────────────────────────────────────
        /// <summary>Fired when possession of an entity begins.</summary>
        public event Action<ControllableEntity>? OnControlStarted;

        /// <summary>Fired when possession ends (timer, RMB, or entity destroyed).</summary>
        public event Action<ControllableEntity>? OnControlEnded;

        // ── Constructor ────────────────────────────────────────────────────────
        public EntityControlSystem(Player player, Camera camera, InputManager input)
        {
            _player = player;
            _camera = camera;
            _input  = input;
        }

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Main update. Call once per frame after InputManager.Update() and before
        /// PlayerController.Update() so input mode is set before the controller reads it.
        /// </summary>
        public void Update(GameTime gameTime, Level level)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Tick global cooldown
            if (CooldownTimer > 0f)
                CooldownTimer = MathF.Max(0f, CooldownTimer - dt);

            // ── Handle active control ──────────────────────────────────────────
            if (IsControlling)
            {
                UpdateControlling(level);
                return;
            }

            // ── Handle isopod attached ─────────────────────────────────────────
            if (IsIsopodAttached)
            {
                UpdateIsopodAttached(gameTime, level);
                return;
            }

            // ── Handle selection mode ──────────────────────────────────────────
            if (IsSelecting)
            {
                UpdateSelecting(level);
                return;
            }

            // ── Normal mode: listen for Q ──────────────────────────────────────
            if (_input.IsControlEntityPressed() && IsReady &&
                _player.State != PlayerState.Dead &&
                _player.State != PlayerState.Stunned)
            {
                BeginSelecting();
            }
        }

        // ── Selection mode ─────────────────────────────────────────────────────

        private void BeginSelecting()
        {
            IsSelecting = true;
            _input.CurrentMode = InputMode.EntitySelecting;
            RefreshEntityHighlights((ControllableEntity?)null); // clear highlights initially
        }

        private void UpdateSelecting(Level level)
        {
            // RMB or Q cancels selection
            if (_input.IsRightClickPressed() || _input.IsControlEntityPressed())
            {
                CancelSelection();
                return;
            }

            // Get mouse world position for highlight
            Vector2 mouseScreen = _input.GetMouseWorldPosition();
            Vector2 mouseWorld  = _camera.ScreenToWorld(mouseScreen);

            // Update entity highlights
            ControllableEntity? nearest = FindNearestEntityInRange(level, mouseWorld);
            RefreshEntityHighlights(nearest);
            HighlightedEntity = nearest;

            // LMB selects the highlighted entity
            if (_input.IsLeftClickPressed() && nearest != null)
            {
                BeginControl(nearest);
            }
        }

        private void CancelSelection()
        {
            IsSelecting       = false;
            HighlightedEntity = null;
            _input.CurrentMode = InputMode.Normal;
            ClearAllHighlights(null);
        }

        // ── Control mode ───────────────────────────────────────────────────────

        private void BeginControl(ControllableEntity entity)
        {
            IsSelecting       = false;
            HighlightedEntity = null;
            ClearAllHighlights(null);

            ActiveEntity  = entity;
            IsControlling = true;

            // Reset effect dispatch state for new control session
            _effectsDispatched = false;
            _skillWasActive    = false;

            // Special case: Luminous Isopod attaches to player instead of freezing them
            if (entity.EntityType == ControllableEntityType.LuminousIsopod
                && entity is LuminousIsopod isopod)
            {
                IsIsopodAttached = true;
                IsControlling    = false; // isopod uses its own tracking
                _input.CurrentMode = InputMode.EntityControlling;
                isopod.SetAttachedPlayer(_player);
                entity.BeginControl();
                OnControlStarted?.Invoke(entity);
                return;
            }

            // Normal entities: freeze the player
            _player.SetState(PlayerState.Controlling);
            _input.CurrentMode = InputMode.EntityControlling;
            entity.BeginControl();
            OnControlStarted?.Invoke(entity);
        }

        private void UpdateControlling(Level level)
        {
            // Check if entity ended control on its own (timer expired)
            if (ActiveEntity == null || !ActiveEntity.IsControlled || ActiveEntity.IsDestroyed)
            {
                EndControl();
                return;
            }

            // RMB releases control early
            if (_input.IsRightClickPressed())
            {
                EndControl();
                return;
            }

            // ── Inter-entity effect dispatch ───────────────────────────────────
            // Detect when the active entity's skill fires and dispatch effects to
            // nearby entities. Only dispatches once per skill activation.
            if (ActiveEntity != null && !_effectsDispatched)
            {
                bool skillJustFired = DetectSkillFired(ActiveEntity);
                if (skillJustFired)
                {
                    _effectsDispatched = true;
                    var nearby = GetEntitiesInRadius(level, ActiveEntity.PixelPosition,
                        ActiveEntity.GetEffectRadius());
                    var (same, diff) = PartitionByType(nearby, ActiveEntity.EntityType);
                    ActiveEntity.ApplySameTypeEffect(same);
                    ActiveEntity.ApplyDifferentTypeEffect(diff);
                }
            }

            // Reset dispatch flag when skill is no longer active (allows re-use on cooldown reset)
            if (ActiveEntity?.Skill != null && !ActiveEntity.Skill.IsActive && _effectsDispatched)
            {
                // Only reset if the skill is on cooldown (meaning it fired and completed)
                if (!ActiveEntity.Skill.IsReady)
                    _effectsDispatched = false;
            }
        }

        /// <summary>
        /// Detect the rising edge of a skill activation for the given entity.
        /// Returns true on the first frame the skill fires (pulse/flash/surge starts,
        /// or hold-skill transitions from inactive to active).
        /// </summary>
        private bool DetectSkillFired(ControllableEntity entity)
        {
            // Entity-specific detection via public state flags
            switch (entity)
            {
                case EchoBat bat:
                    // Pulse just started: active and radius is near zero
                    if (bat.PulseActive && bat.PulseRadius < 20f)
                        return true;
                    break;

                case ChainCentipede centipede:
                    if (centipede.PulseActive && centipede.PulseRadius < 20f)
                        return true;
                    break;

                case LuminescentGlowworm worm:
                    if (worm.FlashActive && worm.FlashRadius < 20f)
                        return true;
                    break;

                case LuminousIsopod isopod:
                    if (isopod.GlowSurgeActive && isopod.GlowSurgeRadius < 20f)
                        return true;
                    break;

                default:
                    // For hold-type skills (spider web trail, salamander slime):
                    // detect rising edge — skill just became active this frame
                    if (entity.Skill != null)
                    {
                        bool isActiveNow = entity.Skill.IsActive;
                        bool justActivated = isActiveNow && !_skillWasActive;
                        _skillWasActive = isActiveNow;
                        return justActivated;
                    }
                    break;
            }

            // Track skill active state for next frame (for non-pulse entities)
            _skillWasActive = entity.Skill?.IsActive ?? false;
            return false;
        }

        private void EndControl()
        {
            var entity = ActiveEntity;

            if (entity != null && entity.IsControlled)
                entity.EndControl();

            IsControlling = false;
            ActiveEntity  = null;

            // Unfreeze player
            if (_player.State == PlayerState.Controlling)
                _player.SetState(PlayerState.Idle);

            _input.CurrentMode = InputMode.Normal;
            CooldownTimer      = GlobalCooldown;

            if (entity != null)
                OnControlEnded?.Invoke(entity);
        }

        // ── Isopod attached mode ───────────────────────────────────────────────

        private void UpdateIsopodAttached(GameTime gameTime, Level level)
        {
            if (ActiveEntity == null || !ActiveEntity.IsControlled || ActiveEntity.IsDestroyed)
            {
                EndIsopodControl();
                return;
            }

            // Update isopod position to track player
            // (LuminousIsopod.UpdateControlled handles this internally)

            // ── Inter-entity effect dispatch for isopod ────────────────────────
            if (!_effectsDispatched)
            {
                bool skillJustFired = DetectSkillFired(ActiveEntity);
                if (skillJustFired)
                {
                    _effectsDispatched = true;
                    var nearby = GetEntitiesInRadius(level, ActiveEntity.PixelPosition,
                        ActiveEntity.GetEffectRadius());
                    var (same, diff) = PartitionByType(nearby, ActiveEntity.EntityType);
                    ActiveEntity.ApplySameTypeEffect(same);
                    ActiveEntity.ApplyDifferentTypeEffect(diff);
                }
            }

            // Reset dispatch flag when glow surge ends (allows re-use after cooldown)
            if (ActiveEntity is LuminousIsopod iso && !iso.GlowSurgeActive && _effectsDispatched)
                _effectsDispatched = false;

            // T key throw is handled inside LuminousIsopod.UpdateControlled
            // RMB releases the isopod early
            if (_input.IsRightClickPressed())
            {
                EndIsopodControl();
            }
        }

        private void EndIsopodControl()
        {
            var entity = ActiveEntity;

            if (entity != null && entity.IsControlled)
                entity.EndControl();

            IsIsopodAttached = false;
            ActiveEntity     = null;
            _input.CurrentMode = InputMode.Normal;
            CooldownTimer      = GlobalCooldown;

            if (entity != null)
                OnControlEnded?.Invoke(entity);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Find the entity nearest to <paramref name="mouseWorldPos"/> that is also
        /// within <see cref="SelectionRange"/> pixels of the player.
        /// Returns null if no eligible entity is found.
        /// </summary>
        private ControllableEntity? FindNearestEntityInRange(Level level, Vector2 mouseWorldPos)
        {
            ControllableEntity? best     = null;
            float               bestDist = float.MaxValue;

            foreach (var obj in level.Objects)
            {
                if (obj is not ControllableEntity entity) continue;
                if (entity.IsDestroyed || entity.IsControlled)  continue;

                // Must be within 200px of the player
                float distToPlayer = Vector2.Distance(entity.PixelPosition, _player.PixelPosition);
                if (distToPlayer > SelectionRange) continue;

                // Among those in range, pick the one nearest to the mouse cursor
                float distToMouse = Vector2.Distance(entity.PixelPosition, mouseWorldPos);
                if (distToMouse < bestDist)
                {
                    bestDist = distToMouse;
                    best     = entity;
                }
            }

            return best;
        }

        /// <summary>
        /// Update IsInRange and IsHighlighted flags on all entities in the level.
        /// </summary>
        private void RefreshEntityHighlights(Level? level)
        {
            if (level == null) return;

            foreach (var obj in level.Objects)
            {
                if (obj is not ControllableEntity entity) continue;

                float dist = Vector2.Distance(entity.PixelPosition, _player.PixelPosition);
                entity.IsInRange     = dist <= SelectionRange;
                entity.IsHighlighted = entity == HighlightedEntity;
            }
        }

        private void RefreshEntityHighlights(ControllableEntity? highlighted)
        {
            HighlightedEntity = highlighted;
        }

        private void ClearAllHighlights(Level? level)
        {
            if (level == null) return;
            foreach (var obj in level.Objects)
            {
                if (obj is not ControllableEntity entity) continue;
                entity.IsInRange     = false;
                entity.IsHighlighted = false;
            }
        }

        // ── Public queries ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns all ControllableEntity instances in the level within the given
        /// pixel radius of the specified world position.
        /// </summary>
        public static List<ControllableEntity> GetEntitiesInRadius(
            Level level, Vector2 centerPixels, float radiusPixels)
        {
            var result = new List<ControllableEntity>();
            foreach (var obj in level.Objects)
            {
                if (obj is not ControllableEntity entity) continue;
                if (entity.IsDestroyed) continue;
                if (Vector2.Distance(entity.PixelPosition, centerPixels) <= radiusPixels)
                    result.Add(entity);
            }
            return result;
        }

        /// <summary>
        /// Partition a list of entities into same-type and different-type lists
        /// relative to <paramref name="sourceType"/>.
        /// </summary>
        public static (List<ControllableEntity> sameType, List<ControllableEntity> differentType)
            PartitionByType(List<ControllableEntity> entities, ControllableEntityType sourceType)
        {
            var same = new List<ControllableEntity>();
            var diff = new List<ControllableEntity>();
            foreach (var e in entities)
            {
                if (e.EntityType == sourceType) same.Add(e);
                else                            diff.Add(e);
            }
            return (same, diff);
        }
    }
}
