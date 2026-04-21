using System;
using Microsoft.Xna.Framework;

namespace Bloop.Lighting
{
    /// <summary>
    /// Data class representing a single light source in the world.
    /// Used by LightingSystem to render radial gradient circles onto the light map.
    ///
    /// Light types used in the game:
    ///   - Player lantern:  radius 200px, warm yellow, permanent (tied to fuel)
    ///   - VentFlower glow: radius 120px, cyan-green, permanent (while flower exists)
    ///   - SporeLight:      radius 160px, pale purple, 15-second fade-out
    ///   - GlowVine active: radius  80px, green, permanent (while activated)
    ///
    /// Flicker support (3.6):
    ///   Set FlickerAmplitude > 0 to enable radius variation at FlickerFrequency Hz.
    ///   SputterChance > 0 adds occasional brief dim-outs (simulates low fuel).
    ///   SwayOffset adds a velocity-driven position offset for lantern sway.
    /// </summary>
    public class LightSource
    {
        // ── Position ───────────────────────────────────────────────────────────
        /// <summary>World-space pixel position of the light center.</summary>
        public Vector2 Position { get; set; }

        // ── Light properties ───────────────────────────────────────────────────
        /// <summary>Base radius of the light circle in pixels (before flicker).</summary>
        public float Radius { get; set; }

        /// <summary>Brightness multiplier in [0, 1]. 0 = off, 1 = full brightness.</summary>
        public float Intensity { get; set; }

        /// <summary>Tint color of the light. White = neutral white light.</summary>
        public Color Color { get; set; }

        // ── Lifetime ───────────────────────────────────────────────────────────
        /// <summary>
        /// Remaining lifetime in seconds.
        /// -1f = permanent (never expires).
        /// </summary>
        public float Lifetime { get; protected set; }

        /// <summary>Whether this light has expired and should be removed.</summary>
        public bool IsExpired => Lifetime >= 0f && Lifetime <= 0f;

        // ── Flicker parameters (3.6) ───────────────────────────────────────────
        /// <summary>
        /// Radius variation amplitude as a fraction of Radius (e.g. 0.05 = ±5%).
        /// 0 = no flicker.
        /// </summary>
        public float FlickerAmplitude { get; set; } = 0f;

        /// <summary>Flicker oscillation frequency in Hz (cycles per second).</summary>
        public float FlickerFrequency { get; set; } = 10f;

        /// <summary>
        /// Probability per second of a "sputter" event (brief dim-out to 30% intensity).
        /// 0 = no sputtering. Typical value for low-fuel: 0.3.
        /// </summary>
        public float SputterChance { get; set; } = 0f;

        /// <summary>
        /// Additional pixel offset applied to Position (for lantern sway).
        /// Set each frame by GameplayScreen.UpdateLighting() based on player velocity.
        /// </summary>
        public Vector2 SwayOffset { get; set; } = Vector2.Zero;

        // ── Flicker runtime state ──────────────────────────────────────────────
        private float _flickerPhase    = 0f;
        private float _sputterTimer    = 0f;   // remaining sputter dim-out time
        private float _sputterCooldown = 0f;   // cooldown before next sputter check
        private readonly Random _rng;

        /// <summary>
        /// Effective radius after flicker is applied. Use this for rendering.
        /// </summary>
        public float EffectiveRadius { get; private set; }

        /// <summary>
        /// Effective intensity after sputter is applied. Use this for rendering.
        /// </summary>
        public float EffectiveIntensity { get; private set; }

        /// <summary>
        /// Effective position including sway offset. Use this for rendering.
        /// </summary>
        public Vector2 EffectivePosition => Position + SwayOffset;

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Create a permanent light source (Lifetime = -1).
        /// </summary>
        public LightSource(Vector2 position, float radius, float intensity, Color color)
        {
            Position          = position;
            Radius            = radius;
            Intensity         = intensity;
            Color             = color;
            Lifetime          = -1f; // permanent
            EffectiveRadius   = radius;
            EffectiveIntensity = intensity;
            _rng              = new Random(position.GetHashCode());
        }

        /// <summary>
        /// Create a temporary light source with a finite lifetime.
        /// </summary>
        public LightSource(Vector2 position, float radius, float intensity, Color color, float lifetime)
        {
            Position          = position;
            Radius            = radius;
            Intensity         = intensity;
            Color             = color;
            Lifetime          = lifetime;
            EffectiveRadius   = radius;
            EffectiveIntensity = intensity;
            _rng              = new Random(position.GetHashCode() ^ (int)(lifetime * 1000));
        }

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Tick the lifetime and update flicker/sputter state.
        /// Call once per frame from LightingSystem.Update().
        /// </summary>
        public virtual void Update(float deltaSeconds)
        {
            // Lifetime tick
            if (Lifetime >= 0f)
            {
                Lifetime -= deltaSeconds;
                if (Lifetime < 0f) Lifetime = 0f;
            }

            // ── Flicker: sinusoidal radius variation ──────────────────────────
            _flickerPhase += deltaSeconds * FlickerFrequency * MathF.Tau;
            float flickerMod = FlickerAmplitude > 0f
                ? 1f + FlickerAmplitude * MathF.Sin(_flickerPhase)
                : 1f;
            EffectiveRadius = Radius * flickerMod;

            // ── Sputter: occasional brief dim-out ─────────────────────────────
            if (_sputterTimer > 0f)
            {
                _sputterTimer -= deltaSeconds;
                EffectiveIntensity = Intensity * 0.30f; // dim during sputter
            }
            else
            {
                EffectiveIntensity = Intensity;

                if (SputterChance > 0f)
                {
                    _sputterCooldown -= deltaSeconds;
                    if (_sputterCooldown <= 0f)
                    {
                        // Roll for sputter
                        if (_rng.NextDouble() < SputterChance * deltaSeconds)
                        {
                            _sputterTimer    = 0.08f + (float)_rng.NextDouble() * 0.06f; // 80–140ms
                            _sputterCooldown = 0.5f;  // min 0.5s between sputters
                        }
                        else
                        {
                            _sputterCooldown = 0.1f;
                        }
                    }
                }
            }
        }
    }
}
