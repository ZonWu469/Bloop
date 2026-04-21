using Microsoft.Xna.Framework;

namespace Bloop.Lighting
{
    /// <summary>
    /// A temporary light source spawned when a disappearing platform is triggered
    /// while illuminated by the player's lantern. Represents bioluminescent spores
    /// released from the fungal shelf as it crumbles.
    ///
    /// Behavior:
    ///   - 160px radius, pale purple color
    ///   - 15-second lifetime
    ///   - Intensity fades linearly from 0.7 → 0.0 over its lifetime
    ///   - After expiry, removed from LightingSystem automatically
    /// </summary>
    public class SporeLight : LightSource
    {
        // ── Constants ──────────────────────────────────────────────────────────
        public const float SporeLightRadius    = 160f;
        public const float SporeLightLifetime  = 15f;
        public const float SporeLightIntensity = 0.7f;

        private static readonly Color SporeLightColor = new Color(180, 160, 255);

        // ── State ──────────────────────────────────────────────────────────────
        private readonly float _initialLifetime;
        private readonly float _initialIntensity;

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Create a spore light at the given pixel-space position.
        /// Preset with spore-specific radius, color, and lifetime.
        /// </summary>
        public SporeLight(Vector2 pixelPosition)
            : base(pixelPosition, SporeLightRadius, SporeLightIntensity, SporeLightColor, SporeLightLifetime)
        {
            _initialLifetime  = SporeLightLifetime;
            _initialIntensity = SporeLightIntensity;
        }

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Tick lifetime and fade intensity linearly from initial → 0.
        /// </summary>
        public override void Update(float deltaSeconds)
        {
            base.Update(deltaSeconds);

            // Fade intensity proportionally to remaining lifetime
            if (_initialLifetime > 0f)
                Intensity = _initialIntensity * (Lifetime / _initialLifetime);
        }
    }
}
