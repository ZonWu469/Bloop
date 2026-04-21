using Bloop.World;

namespace Bloop.Rendering
{
    /// <summary>
    /// Computes and caches 8-bit neighbor bitmasks for every visible tile each frame.
    ///
    /// Bit layout (viewed from above, X right, Y down):
    ///   7  0  1
    ///   6  X  2
    ///   5  4  3
    ///
    /// Bit is SET (1) when the neighbor in that direction is a solid/same-type tile.
    /// Bit is CLEAR (0) when the neighbor is empty/air (i.e. the edge is exposed).
    ///
    /// Usage:
    ///   cache.Refresh(tileMap, minTx, maxTx, minTy, maxTy);
    ///   byte mask = cache.GetMask(tx, ty);
    ///   bool topExposed    = (mask & TileNeighborCache.Top)    == 0;
    ///   bool rightExposed  = (mask & TileNeighborCache.Right)  == 0;
    /// </summary>
    public sealed class TileNeighborCache
    {
        // ── Bit constants ──────────────────────────────────────────────────────
        public const byte Top         = 1 << 0;  // bit 0
        public const byte TopRight    = 1 << 1;  // bit 1
        public const byte Right       = 1 << 2;  // bit 2
        public const byte BottomRight = 1 << 3;  // bit 3
        public const byte Bottom      = 1 << 4;  // bit 4
        public const byte BottomLeft  = 1 << 5;  // bit 5
        public const byte Left        = 1 << 6;  // bit 6
        public const byte TopLeft     = 1 << 7;  // bit 7

        /// <summary>All four cardinal directions set.</summary>
        public const byte AllCardinals = Top | Right | Bottom | Left;

        // ── Storage ────────────────────────────────────────────────────────────
        private byte[,] _masks;
        private int     _mapWidth;
        private int     _mapHeight;

        // ── Constructor ────────────────────────────────────────────────────────
        public TileNeighborCache(int mapWidth, int mapHeight)
        {
            _mapWidth  = mapWidth;
            _mapHeight = mapHeight;
            _masks     = new byte[mapWidth, mapHeight];
        }

        // ── Refresh ────────────────────────────────────────────────────────────

        /// <summary>
        /// Recompute neighbor masks for all tiles in the given visible range.
        /// Call once per frame before drawing tiles.
        /// A neighbor is considered "solid" if it is any non-Empty tile type.
        /// </summary>
        public void Refresh(TileMap tileMap, int minTx, int maxTx, int minTy, int maxTy)
        {
            // Clamp to map bounds
            minTx = System.Math.Max(0, minTx);
            maxTx = System.Math.Min(_mapWidth  - 1, maxTx);
            minTy = System.Math.Max(0, minTy);
            maxTy = System.Math.Min(_mapHeight - 1, maxTy);

            for (int ty = minTy; ty <= maxTy; ty++)
            {
                for (int tx = minTx; tx <= maxTx; tx++)
                {
                    _masks[tx, ty] = ComputeMask(tileMap, tx, ty);
                }
            }
        }

        /// <summary>
        /// Get the cached neighbor mask for tile (tx, ty).
        /// Returns 0 (all exposed) if out of bounds or not yet refreshed.
        /// </summary>
        public byte GetMask(int tx, int ty)
        {
            if (tx < 0 || tx >= _mapWidth || ty < 0 || ty >= _mapHeight)
                return 0;
            return _masks[tx, ty];
        }

        // ── Convenience queries ────────────────────────────────────────────────

        /// <summary>Returns true if the top edge of this tile is exposed (no solid neighbor above).</summary>
        public bool IsTopExposed(int tx, int ty)    => (GetMask(tx, ty) & Top)    == 0;
        /// <summary>Returns true if the right edge is exposed.</summary>
        public bool IsRightExposed(int tx, int ty)  => (GetMask(tx, ty) & Right)  == 0;
        /// <summary>Returns true if the bottom edge is exposed.</summary>
        public bool IsBottomExposed(int tx, int ty) => (GetMask(tx, ty) & Bottom) == 0;
        /// <summary>Returns true if the left edge is exposed.</summary>
        public bool IsLeftExposed(int tx, int ty)   => (GetMask(tx, ty) & Left)   == 0;

        /// <summary>Returns true if ALL four cardinal edges are solid (fully interior tile).</summary>
        public bool IsInterior(int tx, int ty) => (GetMask(tx, ty) & AllCardinals) == AllCardinals;

        /// <summary>Returns the number of exposed cardinal edges (0–4).</summary>
        public int ExposedCardinalCount(int tx, int ty)
        {
            byte mask = GetMask(tx, ty);
            int count = 0;
            if ((mask & Top)    == 0) count++;
            if ((mask & Right)  == 0) count++;
            if ((mask & Bottom) == 0) count++;
            if ((mask & Left)   == 0) count++;
            return count;
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private static byte ComputeMask(TileMap tileMap, int tx, int ty)
        {
            byte mask = 0;

            if (IsSolidNeighbor(tileMap, tx,     ty - 1)) mask |= Top;
            if (IsSolidNeighbor(tileMap, tx + 1, ty - 1)) mask |= TopRight;
            if (IsSolidNeighbor(tileMap, tx + 1, ty    )) mask |= Right;
            if (IsSolidNeighbor(tileMap, tx + 1, ty + 1)) mask |= BottomRight;
            if (IsSolidNeighbor(tileMap, tx,     ty + 1)) mask |= Bottom;
            if (IsSolidNeighbor(tileMap, tx - 1, ty + 1)) mask |= BottomLeft;
            if (IsSolidNeighbor(tileMap, tx - 1, ty    )) mask |= Left;
            if (IsSolidNeighbor(tileMap, tx - 1, ty - 1)) mask |= TopLeft;

            return mask;
        }

        /// <summary>
        /// A neighbor counts as "solid" for masking purposes if it is any non-Empty tile.
        /// Out-of-bounds tiles are treated as solid (map edges don't expose).
        /// </summary>
        private static bool IsSolidNeighbor(TileMap tileMap, int tx, int ty)
        {
            // Out-of-bounds → treat as solid wall (no exposed edge at map boundary)
            if (tx < 0 || tx >= tileMap.Width || ty < 0 || ty >= tileMap.Height)
                return true;

            return tileMap.GetTile(tx, ty) != TileType.Empty;
        }
    }
}
