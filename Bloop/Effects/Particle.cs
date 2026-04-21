using Microsoft.Xna.Framework;

namespace Bloop.Effects
{
    /// <summary>
    /// Lightweight value-type particle used by ParticleSystem.
    /// Kept as a struct to avoid GC pressure in the pooled emitter.
    /// </summary>
    public struct Particle
    {
        // ── Position / motion ──────────────────────────────────────────────────
        public Vector2 Position;
        public Vector2 Velocity;

        // ── Appearance ─────────────────────────────────────────────────────────
        public Color   Color;
        /// <summary>Width of the particle rectangle in pixels.</summary>
        public float   Width;
        /// <summary>Height of the particle rectangle in pixels.</summary>
        public float   Height;
        /// <summary>Current alpha (0–1). Fades over lifetime.</summary>
        public float   Alpha;

        // ── Lifetime ───────────────────────────────────────────────────────────
        /// <summary>Total lifetime in seconds.</summary>
        public float   Lifetime;
        /// <summary>Remaining lifetime in seconds.</summary>
        public float   Age;

        // ── Type tag ───────────────────────────────────────────────────────────
        public ParticleKind Kind;

        /// <summary>True while the particle is alive and should be updated/drawn.</summary>
        public bool IsAlive => Age > 0f;
    }

    /// <summary>Identifies the visual behaviour of a particle.</summary>
    public enum ParticleKind
    {
        /// <summary>Tiny dust mote drifting slowly downward.</summary>
        DustMote,
        /// <summary>Thin vertical rain streak falling fast.</summary>
        RainStreak,
        /// <summary>Small splash droplet spawned on rain impact.</summary>
        RainSplash,
        /// <summary>Waterfall column particle flowing downward.</summary>
        WaterfallDrop,
        /// <summary>Mist particle spreading horizontally at waterfall base.</summary>
        WaterfallMist,
        /// <summary>Ceiling water drip falling from wet ceiling tiles.</summary>
        WaterDrip,
        /// <summary>Splash spawned when a drip hits the floor.</summary>
        DripSplash,
        /// <summary>Floating cave spore rising from GlowVines / VentFlowers.</summary>
        CaveSpore,
    }
}
