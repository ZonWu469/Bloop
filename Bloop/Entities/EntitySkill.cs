using Microsoft.Xna.Framework;

namespace Bloop.Entities
{
    /// <summary>
    /// Activation style for an entity skill.
    /// </summary>
    public enum SkillActivationType
    {
        /// <summary>Fires once on press, then goes on cooldown.</summary>
        Instant,
        /// <summary>Active while the button is held; deactivates on release.</summary>
        Hold,
        /// <summary>Charges while held; fires on release with power proportional to charge time.</summary>
        Charge
    }

    /// <summary>
    /// Base class for a controllable entity's special skill.
    /// Handles cooldown tracking, activation gating, and hold/charge logic.
    /// Subclasses override <see cref="OnActivate"/> and <see cref="OnDeactivate"/>
    /// to implement the actual effect.
    /// </summary>
    public abstract class EntitySkill
    {
        // ── Identity ───────────────────────────────────────────────────────────
        /// <summary>Display name shown in the HUD (e.g. "Sonic Pulse").</summary>
        public string Name { get; }

        /// <summary>How this skill is triggered.</summary>
        public SkillActivationType ActivationType { get; }

        // ── Cooldown ───────────────────────────────────────────────────────────
        /// <summary>Seconds between uses (0 = no cooldown).</summary>
        public float Cooldown { get; }

        /// <summary>Remaining cooldown in seconds. 0 means ready.</summary>
        public float CooldownTimer { get; private set; }

        /// <summary>True when the skill can be activated (cooldown elapsed).</summary>
        public bool IsReady => CooldownTimer <= 0f;

        // ── Active state (for Hold/Charge skills) ──────────────────────────────
        /// <summary>True while a Hold skill is being held, or a Charge skill is charging.</summary>
        public bool IsActive { get; private set; }

        // ── Charge (for Charge skills) ─────────────────────────────────────────
        /// <summary>Maximum charge time in seconds (Charge skills only).</summary>
        public float MaxChargeTime { get; }

        /// <summary>Current charge accumulated in seconds (Charge skills only).</summary>
        public float ChargeTimer { get; private set; }

        /// <summary>Charge fraction 0–1 (Charge skills only).</summary>
        public float ChargeFraction => MaxChargeTime > 0f
            ? MathHelper.Clamp(ChargeTimer / MaxChargeTime, 0f, 1f)
            : 0f;

        // ── Constructor ────────────────────────────────────────────────────────
        protected EntitySkill(string name, SkillActivationType activationType,
            float cooldown, float maxChargeTime = 0f)
        {
            Name           = name;
            ActivationType = activationType;
            Cooldown       = cooldown;
            MaxChargeTime  = maxChargeTime;
        }

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Tick cooldown and charge timers. Call once per frame.
        /// </summary>
        public void Update(float dt)
        {
            if (CooldownTimer > 0f)
                CooldownTimer = MathHelper.Max(0f, CooldownTimer - dt);

            if (IsActive && ActivationType == SkillActivationType.Charge)
            {
                ChargeTimer = MathHelper.Min(ChargeTimer + dt, MaxChargeTime);
            }
        }

        // ── Activation ─────────────────────────────────────────────────────────

        /// <summary>
        /// Attempt to activate the skill. Returns true if activation succeeded.
        /// For Instant skills: fires immediately and starts cooldown.
        /// For Hold skills: begins the active state (no cooldown until released).
        /// For Charge skills: begins charging (cooldown starts on release).
        /// </summary>
        public bool TryActivate()
        {
            if (!IsReady) return false;
            if (IsActive)  return false; // already active (Hold/Charge)

            IsActive = true;

            if (ActivationType == SkillActivationType.Instant)
            {
                OnActivate(1f);
                IsActive      = false;
                CooldownTimer = Cooldown;
            }
            else if (ActivationType == SkillActivationType.Charge)
            {
                ChargeTimer = 0f;
                // OnActivate called on release via Deactivate()
            }
            else // Hold
            {
                OnActivate(1f);
            }

            return true;
        }

        /// <summary>
        /// Deactivate a Hold or Charge skill (called when the button is released).
        /// For Charge skills, fires the effect with the accumulated charge fraction.
        /// </summary>
        public void Deactivate()
        {
            if (!IsActive) return;

            if (ActivationType == SkillActivationType.Charge)
            {
                float power = ChargeFraction;
                ChargeTimer = 0f;
                OnActivate(power);
            }
            else if (ActivationType == SkillActivationType.Hold)
            {
                OnDeactivate();
            }

            IsActive      = false;
            CooldownTimer = Cooldown;
        }

        /// <summary>
        /// Force-reset the skill (e.g. when control ends mid-hold).
        /// Does NOT fire OnDeactivate — just clears state.
        /// </summary>
        public void ForceReset()
        {
            IsActive    = false;
            ChargeTimer = 0f;
            // Cooldown is intentionally NOT reset — partial use still costs cooldown
        }

        // ── Abstract hooks ─────────────────────────────────────────────────────

        /// <summary>
        /// Called when the skill fires. <paramref name="power"/> is 1.0 for Instant/Hold,
        /// or the charge fraction (0–1) for Charge skills.
        /// </summary>
        protected abstract void OnActivate(float power);

        /// <summary>
        /// Called when a Hold skill is released. Override to clean up hold effects.
        /// Default implementation does nothing.
        /// </summary>
        protected virtual void OnDeactivate() { }
    }
}
