using nkast.Aether.Physics2D.Dynamics;

namespace Bloop.Physics
{
    /// <summary>
    /// Collision category bitmask flags used by Aether.Physics2D fixtures.
    /// Uses Aether's own Category enum directly to avoid casting issues.
    /// Set a fixture's CollisionCategories to what it IS, and CollidesWith to what it should HIT.
    /// </summary>
    public static class CollisionCategories
    {
        /// <summary>No collision.</summary>
        public const Category None = Category.None;

        /// <summary>Static terrain tiles (solid ground, walls, ceilings).</summary>
        public const Category Terrain = Category.Cat1;

        /// <summary>One-way platforms (player can pass through from below).</summary>
        public const Category Platform = Category.Cat2;

        /// <summary>The player character body.</summary>
        public const Category Player = Category.Cat3;

        /// <summary>Sensor-only trigger zones (vent flowers, item pickups, etc.).</summary>
        public const Category Trigger = Category.Cat4;

        /// <summary>Disappearing platform bodies.</summary>
        public const Category DisappearingPlatform = Category.Cat5;

        /// <summary>Stun/damage hazard objects.</summary>
        public const Category Hazard = Category.Cat6;

        /// <summary>Climbable surface bodies (glow vines, root clumps, etc.).</summary>
        public const Category Climbable = Category.Cat7;

        /// <summary>Grapple hook projectile.</summary>
        public const Category GrappleHook = Category.Cat8;

        /// <summary>Collectible item bodies (cave lichen, blind fish).</summary>
        public const Category Collectible = Category.Cat9;

        /// <summary>Dynamic world objects (falling stalactites, etc.).</summary>
        public const Category WorldObject = Category.Cat10;

        /// <summary>
        /// Controllable cave entity bodies (bats, spiders, centipedes, etc.).
        /// Sensor-only for selection detection; entities do not physically block the player.
        /// </summary>
        public const Category Entity = Category.Cat11;

        /// <summary>
        /// Crystal bridge segment bodies — walkable platforms that grow/retract on a timer.
        /// The grappling hook can attach to these surfaces.
        /// </summary>
        public const Category CrystalBridge = Category.Cat12;

        // ── Collision masks (what each category collides WITH) ─────────────────

        /// <summary>Player collides with terrain, platforms, disappearing platforms, hazards,
        /// and crystal bridge segments. Does NOT include Entity.</summary>
        public const Category PlayerCollidesWith =
            Terrain | Platform | DisappearingPlatform | Hazard | Climbable | Collectible | CrystalBridge;

        /// <summary>Grapple hook collides with terrain, climbable surfaces, and crystal bridges.</summary>
        public const Category GrappleCollidesWith = Terrain | Climbable | CrystalBridge;

        /// <summary>Triggers are sensors — they detect player overlap but don't block movement.</summary>
        public const Category TriggerCollidesWith = Player;

        /// <summary>Entity bodies collide with terrain and platforms for movement/gravity.</summary>
        public const Category EntityCollidesWith = Terrain | Platform | DisappearingPlatform;
    }
}
