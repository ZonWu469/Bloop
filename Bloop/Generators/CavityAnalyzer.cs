using System;
using System.Collections.Generic;
using Bloop.World;

namespace Bloop.Generators
{
    /// <summary>
    /// Classifies every tile in a TileMap by its surface exposure and spatial context.
    /// Used by ObjectPlacer to make contextually appropriate placement decisions.
    ///
    /// Analysis results:
    ///   - SurfaceFlags[tx, ty]  — bitmask of exposed faces for each solid tile
    ///   - OpenRadius[tx, ty]    — count of empty tiles within a 5-tile radius (cavity size)
    ///   - IsDeadEnd[tx, ty]     — true if this empty tile is in a dead-end alcove
    ///   - IsShaftBottom[tx, ty] — true if this is the lowest empty tile in a vertical shaft
    ///   - IsJunction[tx, ty]    — true if 3+ passages meet at this empty tile
    ///   - IsNarrowPassage[tx, ty] — true if passage width is 2–3 tiles
    /// </summary>
    public class CavityAnalyzer
    {
        // ── Surface flags ──────────────────────────────────────────────────────
        [Flags]
        public enum SurfaceType
        {
            None      = 0,
            Floor     = 1,  // solid tile with empty directly above
            Ceiling   = 2,  // solid tile with empty directly below
            WallLeft  = 4,  // solid tile with empty to the left
            WallRight = 8,  // solid tile with empty to the right
        }

        // ── Analysis results ───────────────────────────────────────────────────
        /// <summary>Surface exposure flags for each tile (meaningful only for solid tiles).</summary>
        public SurfaceType[,] SurfaceFlags { get; }

        /// <summary>
        /// Count of empty tiles within a 5-tile radius.
        /// High values = large open cavern. Low values = tight passage.
        /// </summary>
        public int[,] OpenRadius { get; }

        /// <summary>True if this empty tile is in a dead-end alcove (passage ends within 10 tiles).</summary>
        public bool[,] IsDeadEnd { get; }

        /// <summary>True if this is the lowest empty tile in a vertical shaft column.</summary>
        public bool[,] IsShaftBottom { get; }

        /// <summary>True if 3 or more passages meet at this empty tile.</summary>
        public bool[,] IsJunction { get; }

        /// <summary>True if the passage at this empty tile is 2–3 tiles wide (narrow).</summary>
        public bool[,] IsNarrowPassage { get; }

        // ── Thresholds ─────────────────────────────────────────────────────────
        /// <summary>Minimum open-radius count to be considered a "large cavern".</summary>
        public const int LargeCavernThreshold = 40;

        /// <summary>Minimum open-radius count to be considered a "medium cavity".</summary>
        public const int MediumCavernThreshold = 15;

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Analyze the given TileMap and populate all analysis arrays.
        /// Call once after level generation, before object placement.
        /// </summary>
        public CavityAnalyzer(TileMap map)
        {
            int w = map.Width;
            int h = map.Height;

            SurfaceFlags    = new SurfaceType[w, h];
            OpenRadius      = new int[w, h];
            IsDeadEnd       = new bool[w, h];
            IsShaftBottom   = new bool[w, h];
            IsJunction      = new bool[w, h];
            IsNarrowPassage = new bool[w, h];

            AnalyzeSurfaces(map, w, h);
            AnalyzeOpenRadius(map, w, h);
            AnalyzeDeadEnds(map, w, h);
            AnalyzeShaftBottoms(map, w, h);
            AnalyzeJunctions(map, w, h);
            AnalyzeNarrowPassages(map, w, h);
        }

        // ── Convenience queries ────────────────────────────────────────────────

        /// <summary>Returns true if the solid tile at (tx, ty) has a floor surface (empty above).</summary>
        public bool IsFloor(int tx, int ty)    => (SurfaceFlags[tx, ty] & SurfaceType.Floor)     != 0;

        /// <summary>Returns true if the solid tile at (tx, ty) has a ceiling surface (empty below).</summary>
        public bool IsCeiling(int tx, int ty)  => (SurfaceFlags[tx, ty] & SurfaceType.Ceiling)   != 0;

        /// <summary>Returns true if the solid tile at (tx, ty) has a left-wall surface (empty left).</summary>
        public bool IsWallLeft(int tx, int ty) => (SurfaceFlags[tx, ty] & SurfaceType.WallLeft)  != 0;

        /// <summary>Returns true if the solid tile at (tx, ty) has a right-wall surface (empty right).</summary>
        public bool IsWallRight(int tx, int ty)=> (SurfaceFlags[tx, ty] & SurfaceType.WallRight) != 0;

        /// <summary>Returns true if the empty tile at (tx, ty) is in a large open cavern.</summary>
        public bool IsLargeCavern(int tx, int ty) => OpenRadius[tx, ty] >= LargeCavernThreshold;

        /// <summary>Returns true if the empty tile at (tx, ty) is in a medium-sized cavity.</summary>
        public bool IsMediumCavern(int tx, int ty) => OpenRadius[tx, ty] >= MediumCavernThreshold;

        // ── Private analysis passes ────────────────────────────────────────────

        private void AnalyzeSurfaces(TileMap map, int w, int h)
        {
            for (int ty = 1; ty < h - 1; ty++)
            {
                for (int tx = 1; tx < w - 1; tx++)
                {
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;

                    var flags = SurfaceType.None;

                    // Floor: solid tile with empty directly above
                    if (map.GetTile(tx, ty - 1) == TileType.Empty)
                        flags |= SurfaceType.Floor;

                    // Ceiling: solid tile with empty directly below
                    if (map.GetTile(tx, ty + 1) == TileType.Empty)
                        flags |= SurfaceType.Ceiling;

                    // WallLeft: solid tile with empty to the left
                    if (map.GetTile(tx - 1, ty) == TileType.Empty)
                        flags |= SurfaceType.WallLeft;

                    // WallRight: solid tile with empty to the right
                    if (map.GetTile(tx + 1, ty) == TileType.Empty)
                        flags |= SurfaceType.WallRight;

                    SurfaceFlags[tx, ty] = flags;
                }
            }
        }

        private void AnalyzeOpenRadius(TileMap map, int w, int h)
        {
            const int Radius = 5;

            for (int ty = 0; ty < h; ty++)
            {
                for (int tx = 0; tx < w; tx++)
                {
                    if (map.GetTile(tx, ty) != TileType.Empty) continue;

                    int count = 0;
                    for (int dy = -Radius; dy <= Radius; dy++)
                    {
                        for (int dx = -Radius; dx <= Radius; dx++)
                        {
                            if (dx * dx + dy * dy > Radius * Radius) continue;
                            int nx = tx + dx;
                            int ny = ty + dy;
                            if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                            if (map.GetTile(nx, ny) == TileType.Empty)
                                count++;
                        }
                    }
                    OpenRadius[tx, ty] = count;
                }
            }
        }

        private void AnalyzeDeadEnds(TileMap map, int w, int h)
        {
            // A dead-end is an empty tile from which you can only reach a small
            // connected region (< 30 tiles) before hitting solid walls on all sides.
            // We use a limited BFS capped at 30 steps.
            const int DeadEndMaxSize = 30;

            for (int ty = 2; ty < h - 2; ty++)
            {
                for (int tx = 2; tx < w - 2; tx++)
                {
                    if (map.GetTile(tx, ty) != TileType.Empty) continue;

                    // Count reachable empty tiles from this position (capped)
                    int reachable = CountReachable(map, tx, ty, DeadEndMaxSize);
                    IsDeadEnd[tx, ty] = reachable < DeadEndMaxSize;
                }
            }
        }

        private static int CountReachable(TileMap map, int startX, int startY, int cap)
        {
            var visited = new HashSet<(int, int)>();
            var queue   = new Queue<(int, int)>();
            queue.Enqueue((startX, startY));
            visited.Add((startX, startY));

            while (queue.Count > 0 && visited.Count < cap)
            {
                var (x, y) = queue.Dequeue();
                foreach (var (nx, ny) in new[] { (x-1,y),(x+1,y),(x,y-1),(x,y+1) })
                {
                    if (nx < 0 || nx >= map.Width || ny < 0 || ny >= map.Height) continue;
                    if (visited.Contains((nx, ny))) continue;
                    if (map.GetTile(nx, ny) != TileType.Empty) continue;
                    visited.Add((nx, ny));
                    queue.Enqueue((nx, ny));
                }
            }

            return visited.Count;
        }

        private void AnalyzeShaftBottoms(TileMap map, int w, int h)
        {
            // A shaft bottom is the lowest empty tile in a column that has
            // at least 6 consecutive empty tiles above it (vertical shaft).
            for (int tx = 1; tx < w - 1; tx++)
            {
                int consecutiveEmpty = 0;
                for (int ty = 1; ty < h - 1; ty++)
                {
                    if (map.GetTile(tx, ty) == TileType.Empty)
                    {
                        consecutiveEmpty++;
                    }
                    else
                    {
                        // This solid tile ends a run of empty tiles
                        if (consecutiveEmpty >= 6)
                        {
                            // Mark the tile just above this solid tile as shaft bottom
                            int bottomTy = ty - 1;
                            if (bottomTy >= 1 && bottomTy < h - 1)
                                IsShaftBottom[tx, bottomTy] = true;
                        }
                        consecutiveEmpty = 0;
                    }
                }
            }
        }

        private void AnalyzeJunctions(TileMap map, int w, int h)
        {
            // A junction is an empty tile where 3 or more of the 4 cardinal
            // directions lead to open passages (not immediately blocked by solid).
            for (int ty = 1; ty < h - 1; ty++)
            {
                for (int tx = 1; tx < w - 1; tx++)
                {
                    if (map.GetTile(tx, ty) != TileType.Empty) continue;

                    int openDirs = 0;
                    if (map.GetTile(tx - 1, ty) == TileType.Empty) openDirs++;
                    if (map.GetTile(tx + 1, ty) == TileType.Empty) openDirs++;
                    if (map.GetTile(tx, ty - 1) == TileType.Empty) openDirs++;
                    if (map.GetTile(tx, ty + 1) == TileType.Empty) openDirs++;

                    IsJunction[tx, ty] = openDirs >= 3;
                }
            }
        }

        private void AnalyzeNarrowPassages(TileMap map, int w, int h)
        {
            // A narrow passage is an empty tile where the passage is 2–3 tiles wide
            // in at least one axis (horizontal or vertical).
            for (int ty = 1; ty < h - 1; ty++)
            {
                for (int tx = 1; tx < w - 1; tx++)
                {
                    if (map.GetTile(tx, ty) != TileType.Empty) continue;

                    // Measure horizontal width at this row
                    int hWidth = MeasurePassageWidth(map, tx, ty, 1, 0);
                    // Measure vertical height at this column
                    int vHeight = MeasurePassageWidth(map, tx, ty, 0, 1);

                    // Narrow if either dimension is 2–3 tiles
                    IsNarrowPassage[tx, ty] = (hWidth >= 2 && hWidth <= 3)
                                           || (vHeight >= 2 && vHeight <= 3);
                }
            }
        }

        /// <summary>
        /// Measure the width of a passage at (tx, ty) in the given direction (dx, dy).
        /// Counts consecutive empty tiles in both the positive and negative direction.
        /// </summary>
        private static int MeasurePassageWidth(TileMap map, int tx, int ty, int dx, int dy)
        {
            // Measure perpendicular to the given direction
            int perpDx = dy;  // perpendicular to (dx, dy)
            int perpDy = dx;

            int count = 1; // count the tile itself

            // Count in positive perpendicular direction
            int nx = tx + perpDx;
            int ny = ty + perpDy;
            while (nx >= 0 && nx < map.Width && ny >= 0 && ny < map.Height
                   && map.GetTile(nx, ny) == TileType.Empty)
            {
                count++;
                nx += perpDx;
                ny += perpDy;
            }

            // Count in negative perpendicular direction
            nx = tx - perpDx;
            ny = ty - perpDy;
            while (nx >= 0 && nx < map.Width && ny >= 0 && ny < map.Height
                   && map.GetTile(nx, ny) == TileType.Empty)
            {
                count++;
                nx -= perpDx;
                ny -= perpDy;
            }

            return count;
        }
    }
}
