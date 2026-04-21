using Microsoft.Xna.Framework;

namespace Bloop.World
{
    /// <summary>
    /// Tile type enum. Each type has distinct visual and physics properties.
    /// </summary>
    public enum TileType
    {
        /// <summary>Empty air — no collision, no rendering.</summary>
        Empty = 0,

        /// <summary>Solid rock — full collision on all sides.</summary>
        Solid,

        /// <summary>One-way platform — collision only from above.</summary>
        Platform,

        /// <summary>Slope angled down-left (top-right to bottom-left).</summary>
        SlopeLeft,

        /// <summary>Slope angled down-right (top-left to bottom-right).</summary>
        SlopeRight,

        /// <summary>Climbable surface (C key to climb).</summary>
        Climbable,
    }

    /// <summary>
    /// Static helpers for tile properties: placeholder colors, solidity, slope detection.
    /// All rendering uses colored rectangles — no external art assets required.
    /// </summary>
    public static class TileProperties
    {
        // ── Placeholder colors per tile type ───────────────────────────────────
        private static readonly Color ColorSolid     = new Color( 80,  60,  40); // dark brown rock
        private static readonly Color ColorPlatform  = new Color(100, 140,  60); // mossy green
        private static readonly Color ColorSlopeLeft = new Color( 90,  70,  45); // slightly lighter rock
        private static readonly Color ColorSlopeRight= new Color( 90,  70,  45);
        private static readonly Color ColorClimbable = new Color( 40, 120,  60); // dark green vine

        /// <summary>Returns the placeholder draw color for a tile type.</summary>
        public static Color GetColor(TileType type) => type switch
        {
            TileType.Solid      => ColorSolid,
            TileType.Platform   => ColorPlatform,
            TileType.SlopeLeft  => ColorSlopeLeft,
            TileType.SlopeRight => ColorSlopeRight,
            TileType.Climbable  => ColorClimbable,
            _                   => Color.Transparent
        };

        /// <summary>Returns true if the tile blocks movement (has solid collision).</summary>
        public static bool IsSolid(TileType type) => type switch
        {
            TileType.Solid      => true,
            TileType.SlopeLeft  => true,
            TileType.SlopeRight => true,
            TileType.Climbable  => true,
            _                   => false
        };

        /// <summary>Returns true if the tile is a slope.</summary>
        public static bool IsSlope(TileType type) =>
            type == TileType.SlopeLeft || type == TileType.SlopeRight;

        /// <summary>Returns true if the tile is a one-way platform.</summary>
        public static bool IsPlatform(TileType type) =>
            type == TileType.Platform;

        /// <summary>Returns true if the tile can be climbed with C key.</summary>
        public static bool IsClimbable(TileType type) =>
            type == TileType.Climbable;

        /// <summary>Returns true if the tile should be rendered (not empty).</summary>
        public static bool IsVisible(TileType type) =>
            type != TileType.Empty;
    }
}
