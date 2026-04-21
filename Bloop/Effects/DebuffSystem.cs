using System;
using System.Collections.Generic;

namespace Bloop.Gameplay
{
    // ── Debuff type enum ───────────────────────────────────────────────────────

    /// <summary>All temporary debuff types that can affect the player.</summary>
    public enum DebuffType
    {
        /// <summary>Reduces horizontal movement force by 40%.</summary>
        SlowMovement,

        /// <summary>Swaps left/right input direction.</summary>
        InvertedControls,

        /// <summary>Reduces jump impulse by 50%.</summary>
        ReducedJump,

        /// <summary>Reduces lantern light radius by 30%.</summary>
        Blurred,

        /// <summary>Reduces maximum carry weight by 20kg.</summary>
        Weakened,
    }

    // ── Active debuff record ───────────────────────────────────────────────────

    /// <summary>A single active debuff instance with remaining duration.</summary>
    public class ActiveDebuff
    {
        public DebuffType Type          { get; }
        public float      TotalDuration { get; }
        public float      RemainingTime { get; set; }
        public bool       IsExpired     => RemainingTime <= 0f;

        /// <summary>Progress from 1.0 (just applied) to 0.0 (about to expire).</summary>
        public float Progress => TotalDuration > 0f ? RemainingTime / TotalDuration : 0f;

        public ActiveDebuff(DebuffType type, float duration)
        {
            Type          = type;
            TotalDuration = duration;
            RemainingTime = duration;
        }
    }

    // ── Debuff system ──────────────────────────────────────────────────────────

    /// <summary>
    /// Manages temporary negative effects applied to the player.
    /// Multiple debuffs can be active simultaneously, but the same type
    /// refreshes its duration rather than stacking.
    ///
    /// Gameplay systems query modifiers via GetModifier() and HasDebuff()
    /// before applying forces, impulses, or other effects.
    ///
    /// Debuff effects:
    ///   SlowMovement    — movement force × 0.6
    ///   InvertedControls — left/right input swapped
    ///   ReducedJump     — jump impulse × 0.5
    ///   Blurred         — lantern radius × 0.7
    ///   Weakened        — max carry weight -20kg
    /// </summary>
    public class DebuffSystem
    {
        // ── State ──────────────────────────────────────────────────────────────
        private readonly List<ActiveDebuff> _debuffs   = new();
        private readonly List<ActiveDebuff> _toRemove  = new();

        public IReadOnlyList<ActiveDebuff> ActiveDebuffs => _debuffs;

        // ── Events ─────────────────────────────────────────────────────────────
        /// <summary>Fired when a debuff is applied or refreshed.</summary>
        public event Action<DebuffType>? OnDebuffApplied;

        /// <summary>Fired when a debuff expires.</summary>
        public event Action<DebuffType>? OnDebuffExpired;

        // ── Query ──────────────────────────────────────────────────────────────

        /// <summary>Returns true if the given debuff type is currently active.</summary>
        public bool HasDebuff(DebuffType type)
        {
            foreach (var d in _debuffs)
                if (d.Type == type) return true;
            return false;
        }

        /// <summary>
        /// Returns the gameplay modifier for the given debuff type.
        /// Returns 1.0 if the debuff is not active (no effect).
        /// Returns the reduced value if active.
        ///
        /// Multiply movement/jump forces by this value before applying.
        /// </summary>
        public float GetModifier(DebuffType type)
        {
            if (!HasDebuff(type)) return 1f;

            return type switch
            {
                DebuffType.SlowMovement     => 0.6f,
                DebuffType.ReducedJump      => 0.5f,
                DebuffType.Blurred          => 0.7f,  // lantern radius multiplier
                DebuffType.Weakened         => 0.6f,  // max weight multiplier
                DebuffType.InvertedControls => -1f,   // direction flip
                _                           => 1f
            };
        }

        // ── Operations ─────────────────────────────────────────────────────────

        /// <summary>
        /// Apply a debuff of the given type for the given duration.
        /// If the same type is already active, refreshes its duration
        /// (takes the longer of the two durations).
        /// </summary>
        public void ApplyDebuff(DebuffType type, float duration)
        {
            // Check if already active — refresh duration if so
            foreach (var d in _debuffs)
            {
                if (d.Type == type)
                {
                    d.RemainingTime = Math.Max(d.RemainingTime, duration);
                    OnDebuffApplied?.Invoke(type);
                    return;
                }
            }

            // New debuff
            _debuffs.Add(new ActiveDebuff(type, duration));
            OnDebuffApplied?.Invoke(type);
        }

        /// <summary>Remove all active debuffs immediately.</summary>
        public void ClearAll()
        {
            _debuffs.Clear();
        }

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Tick all active debuffs. Remove expired ones.
        /// Call once per frame from GameplayScreen.Update().
        /// </summary>
        public void Update(float deltaSeconds)
        {
            foreach (var d in _debuffs)
            {
                d.RemainingTime -= deltaSeconds;
                if (d.IsExpired)
                    _toRemove.Add(d);
            }

            foreach (var d in _toRemove)
            {
                _debuffs.Remove(d);
                OnDebuffExpired?.Invoke(d.Type);
            }
            _toRemove.Clear();
        }

        // ── Display helpers ────────────────────────────────────────────────────

        /// <summary>Get a short display name for a debuff type (for HUD).</summary>
        public static string GetDisplayName(DebuffType type) => type switch
        {
            DebuffType.SlowMovement     => "SLOW",
            DebuffType.InvertedControls => "INVERTED",
            DebuffType.ReducedJump      => "WEAK JUMP",
            DebuffType.Blurred          => "BLURRED",
            DebuffType.Weakened         => "WEAKENED",
            _                           => type.ToString()
        };

        /// <summary>Get the HUD color for a debuff type.</summary>
        public static Microsoft.Xna.Framework.Color GetDisplayColor(DebuffType type) => type switch
        {
            DebuffType.SlowMovement     => new Microsoft.Xna.Framework.Color(200, 160,  60),
            DebuffType.InvertedControls => new Microsoft.Xna.Framework.Color(200,  60, 200),
            DebuffType.ReducedJump      => new Microsoft.Xna.Framework.Color(200,  80,  80),
            DebuffType.Blurred          => new Microsoft.Xna.Framework.Color(100, 140, 200),
            DebuffType.Weakened         => new Microsoft.Xna.Framework.Color(160, 120,  80),
            _                           => Microsoft.Xna.Framework.Color.White
        };
    }
}
