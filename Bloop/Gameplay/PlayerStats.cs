using Microsoft.Xna.Framework;

namespace Bloop.Gameplay
{
    /// <summary>
    /// Holds all numeric stats for the player: health, breath, lantern fuel,
    /// and kinetic charge. Provides drain/refill helpers with depth scaling.
    /// </summary>
    public class PlayerStats
    {
        // ── Constants ──────────────────────────────────────────────────────────
        public const float MaxHealth       = 100f;
        public const float MaxBreath       = 100f;
        public const float MaxLanternFuel  = 100f;
        public const float MaxKineticCharge = 100f;
        public const int   MaxFlareCount   = 3;
        public const float MaxSanity       = 100f;

        /// <summary>Base breath drain per second at depth 1.</summary>
        private const float BaseBreathDrain  = 2f;
        /// <summary>Base lantern drain per second at depth 1.</summary>
        private const float BaseLanternDrain = 0.5f;
        /// <summary>Damage per second when breath hits zero.</summary>
        private const float SuffocationDamage = 5f;

        // ── Properties ─────────────────────────────────────────────────────────
        public float Health        { get; private set; } = MaxHealth;
        public float Breath        { get; private set; } = MaxBreath;
        public float LanternFuel   { get; private set; } = MaxLanternFuel;
        public float KineticCharge { get; private set; } = 0f;
        public int   FlareCount    { get; private set; } = MaxFlareCount;
        public float Sanity        { get; private set; } = MaxSanity;

        public bool IsAlive        => Health > 0f;
        public bool HasBreath      => Breath > 0f;
        public bool HasLanternFuel => LanternFuel > 0f;
        public bool HasFlares      => FlareCount > 0;

        /// <summary>
        /// The source of the last damage taken. Used for death recap display.
        /// Null until damage has been received.
        /// </summary>
        public string? LastDamageSource { get; private set; }

        // ── Drain / Refill ─────────────────────────────────────────────────────

        /// <summary>
        /// Tick all passive drains. Call once per Update frame.
        /// depth: current level depth (1-based), used to scale drain rates.
        /// </summary>
        public void Tick(float deltaSeconds, int depth)
        {
            float depthMult = 1f + (depth - 1) * 0.08f; // +8% per depth level

            // Lantern fuel drains faster at depth
            if (HasLanternFuel)
            {
                LanternFuel -= BaseLanternDrain * depthMult * deltaSeconds;
                LanternFuel  = MathHelper.Clamp(LanternFuel, 0f, MaxLanternFuel);
            }

            // Breath drains at depth (starts draining from depth 3 onward)
            if (depth >= 3)
            {
                Breath -= BaseBreathDrain * depthMult * deltaSeconds;
                Breath  = MathHelper.Clamp(Breath, 0f, MaxBreath);
            }

            // Suffocation damage when breath is empty
            if (Breath <= 0f)
            {
                TakeDamage(SuffocationDamage * deltaSeconds);
            }
        }

        /// <summary>Apply damage to health. Optionally records the damage source for death recap.</summary>
        public void TakeDamage(float amount, string? source = null)
        {
            Health = MathHelper.Clamp(Health - amount, 0f, MaxHealth);
            if (source != null) LastDamageSource = source;
        }

        /// <summary>Restore health by amount.</summary>
        public void HealHealth(float amount)
        {
            Health = MathHelper.Clamp(Health + amount, 0f, MaxHealth);
        }

        /// <summary>Fully refill breath and lantern fuel (vent flower effect).</summary>
        public void RefillFromVent()
        {
            Breath      = MaxBreath;
            LanternFuel = MaxLanternFuel;
        }

        /// <summary>Add kinetic charge from sliding. Clamped to max.</summary>
        public void AddKineticCharge(float amount)
        {
            KineticCharge = MathHelper.Clamp(KineticCharge + amount, 0f, MaxKineticCharge);
        }

        /// <summary>Consume all kinetic charge (after slingshot or zip-drop).</summary>
        public void ConsumeKineticCharge()
        {
            KineticCharge = 0f;
        }

        /// <summary>Consume one flare. Returns true if successful.</summary>
        public bool UseFlare()
        {
            if (FlareCount <= 0) return false;
            FlareCount--;
            return true;
        }

        /// <summary>Reset flares to max. Call on new level load.</summary>
        public void RefillFlares()
        {
            FlareCount = MaxFlareCount;
        }

        /// <summary>Drain kinetic charge passively when not sliding.</summary>
        public void DrainKineticCharge(float deltaSeconds)
        {
            KineticCharge = MathHelper.Clamp(KineticCharge - 20f * deltaSeconds, 0f, MaxKineticCharge);
        }

        /// <summary>Apply a sanity change (positive or negative). Clamped to [0, MaxSanity].</summary>
        public void ApplySanityDelta(int delta)
        {
            Sanity = MathHelper.Clamp(Sanity + delta, 0f, MaxSanity);
        }

        /// <summary>Passive sanity drain per second (entity proximity, world effects).</summary>
        public void DrainSanity(float amountPerSecond, float deltaSeconds)
        {
            Sanity = MathHelper.Clamp(Sanity - amountPerSecond * deltaSeconds, 0f, MaxSanity);
        }

        /// <summary>Returns true if sanity is below the given threshold (0–100).</summary>
        public bool SanityBelow(float threshold) => Sanity < threshold;

        /// <summary>Set stats directly (used when loading from save).</summary>
        public void SetFromSave(float health, float breath, float lanternFuel, float sanity = MaxSanity)
        {
            Health      = MathHelper.Clamp(health,      0f, MaxHealth);
            Breath      = MathHelper.Clamp(breath,      0f, MaxBreath);
            LanternFuel = MathHelper.Clamp(lanternFuel, 0f, MaxLanternFuel);
            Sanity      = MathHelper.Clamp(sanity,      0f, MaxSanity);
        }
    }
}
