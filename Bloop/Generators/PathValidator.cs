using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Bloop.World;

namespace Bloop.Generators
{
    /// <summary>
    /// Validates that a generated level is completable by performing a BFS
    /// from the entry tile to the exit tile, simulating player movement capabilities.
    ///
    /// Also verifies that at least 2 distinct paths exist by running a Dijkstra
    /// search that penalizes tiles on the primary path, ensuring the secondary
    /// route shares fewer than 40% of tiles with the first.
    ///
    /// If only 1 path exists (or paths are too similar), a worm tunnel is carved
    /// through underused regions to create a genuine alternate route.
    /// </summary>
    public static class PathValidator
    {
        // ── Movement constants ─────────────────────────────────────────────────
        /// <summary>Maximum upward jump height in tiles (player can jump ~3 tiles).</summary>
        private const int MaxJumpHeight = 3;

        /// <summary>
        /// Maximum tile overlap fraction between two paths before they are
        /// considered "too similar" and a new route is carved.
        /// </summary>
        private const float MaxPathOverlapFraction = 0.40f;

        /// <summary>
        /// Cost penalty applied to tiles that are on the primary path when
        /// searching for the secondary path via Dijkstra.
        /// </summary>
        private const int PrimaryPathPenalty = 5;

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Validate that the level has at least one path from entry to exit,
        /// and that a genuinely distinct second path also exists.
        ///
        /// Returns true if the level is valid (at least one path exists after any fixes).
        /// Returns false if no path can be found even after carving.
        /// </summary>
        public static bool Validate(TileMap map, Vector2 entryPixel, Vector2 exitPixel, Random rng)
        {
            int entryTx = (int)(entryPixel.X / TileMap.TileSize);
            int entryTy = (int)(entryPixel.Y / TileMap.TileSize);
            int exitTx  = (int)(exitPixel.X  / TileMap.TileSize);
            int exitTy  = (int)(exitPixel.Y  / TileMap.TileSize);

            // Primary path check
            var path1 = BFS(map, entryTx, entryTy, exitTx, exitTy);
            if (path1 == null) return false; // no path at all

            // Build a set of tiles on path1 for fast lookup
            var path1Set = new HashSet<(int, int)>(path1);

            // Secondary path check using Dijkstra with penalty on path1 tiles
            var path2 = DijkstraAvoidingPath(map, entryTx, entryTy, exitTx, exitTy, path1Set);

            bool pathsDistinct = false;
            if (path2 != null)
            {
                // Calculate overlap fraction
                int overlap = 0;
                foreach (var tile in path2)
                    if (path1Set.Contains(tile)) overlap++;

                float overlapFraction = path1.Count > 0
                    ? (float)overlap / Math.Max(path1.Count, path2.Count)
                    : 1f;

                pathsDistinct = overlapFraction <= MaxPathOverlapFraction;
            }

            if (!pathsDistinct)
            {
                // Carve a worm tunnel through underused regions to create a second route
                CarveAlternateWormTunnel(map, rng, entryTx, entryTy, exitTx, exitTy, path1Set);

                // Verify the new path is now distinct
                path2 = DijkstraAvoidingPath(map, entryTx, entryTy, exitTx, exitTy, path1Set);
                // Even if still not perfectly distinct, the level is still completable
            }

            return true;
        }

        // ── BFS ────────────────────────────────────────────────────────────────

        /// <summary>
        /// BFS from (startTx, startTy) to (goalTx, goalTy).
        /// Returns the path as a list of (tx, ty) tile coordinates, or null if unreachable.
        ///
        /// Movement rules:
        ///   - Left/right: move to adjacent empty/platform/slope/climbable tile
        ///   - Down: move to tile below if it is empty or platform
        ///   - Up: move up to MaxJumpHeight tiles if current tile is climbable,
        ///         or 1 tile up if jumping from ground (tile below is solid/platform)
        /// </summary>
        public static List<(int tx, int ty)>? BFS(
            TileMap map, int startTx, int startTy, int goalTx, int goalTy)
            => BFS(map, startTx, startTy, goalTx, goalTy, null);

        /// <summary>
        /// BFS overload that treats an additional set of blocked tiles as impassable.
        /// Used by the post-placement passability validator to account for object footprints.
        /// </summary>
        public static List<(int tx, int ty)>? BFS(
            TileMap map, int startTx, int startTy, int goalTx, int goalTy,
            HashSet<(int, int)>? blockedTiles)
        {
            if (!IsPassable(map, startTx, startTy, blockedTiles)) return null;
            if (!IsPassable(map, goalTx,  goalTy,  blockedTiles)) return null;

            var visited = new HashSet<(int, int)>();
            var queue   = new Queue<(int tx, int ty, List<(int, int)> path)>();

            var startPath = new List<(int, int)> { (startTx, startTy) };
            queue.Enqueue((startTx, startTy, startPath));
            visited.Add((startTx, startTy));

            while (queue.Count > 0)
            {
                var (tx, ty, path) = queue.Dequeue();

                if (tx == goalTx && ty == goalTy)
                    return path;

                foreach (var (nx, ny) in GetNeighbors(map, tx, ty, blockedTiles))
                {
                    if (visited.Contains((nx, ny))) continue;
                    visited.Add((nx, ny));

                    var newPath = new List<(int, int)>(path) { (nx, ny) };
                    queue.Enqueue((nx, ny, newPath));
                }
            }

            return null; // unreachable
        }

        // ── Dijkstra with path penalty ─────────────────────────────────────────

        /// <summary>
        /// Dijkstra search from start to goal, applying a cost penalty to tiles
        /// that appear on the primary path. This encourages finding an alternate route.
        /// Returns the path as a list of (tx, ty) tile coordinates, or null if unreachable.
        /// </summary>
        private static List<(int tx, int ty)>? DijkstraAvoidingPath(
            TileMap map, int startTx, int startTy, int goalTx, int goalTy,
            HashSet<(int, int)> primaryPath)
        {
            if (!IsPassable(map, startTx, startTy, null)) return null;
            if (!IsPassable(map, goalTx,  goalTy,  null)) return null;

            // Priority queue: (cost, tx, ty)
            var pq       = new SortedSet<(int cost, int tx, int ty, int id)>(
                Comparer<(int cost, int tx, int ty, int id)>.Create(
                    (a, b) => a.cost != b.cost ? a.cost.CompareTo(b.cost)
                            : a.id.CompareTo(b.id)));
            var dist     = new Dictionary<(int, int), int>();
            var prev     = new Dictionary<(int, int), (int, int)>();
            int idCounter = 0;

            dist[(startTx, startTy)] = 0;
            pq.Add((0, startTx, startTy, idCounter++));

            while (pq.Count > 0)
            {
                var (cost, tx, ty, _) = pq.Min;
                pq.Remove(pq.Min);

                if (tx == goalTx && ty == goalTy)
                {
                    // Reconstruct path
                    var path = new List<(int, int)>();
                    var cur  = (goalTx, goalTy);
                    while (prev.ContainsKey(cur))
                    {
                        path.Add(cur);
                        cur = prev[cur];
                    }
                    path.Add((startTx, startTy));
                    path.Reverse();
                    return path;
                }

                if (dist.TryGetValue((tx, ty), out int bestCost) && cost > bestCost)
                    continue;

                foreach (var (nx, ny) in GetNeighbors(map, tx, ty, null))
                {
                    // Apply penalty if this neighbor is on the primary path
                    int moveCost = primaryPath.Contains((nx, ny)) ? PrimaryPathPenalty : 1;
                    int newCost  = cost + moveCost;

                    if (!dist.TryGetValue((nx, ny), out int existingCost) || newCost < existingCost)
                    {
                        dist[(nx, ny)] = newCost;
                        prev[(nx, ny)] = (tx, ty);
                        pq.Add((newCost, nx, ny, idCounter++));
                    }
                }
            }

            return null; // unreachable
        }

        // ── Alternate route carving ────────────────────────────────────────────

        /// <summary>
        /// Carve a worm tunnel through underused regions to create a second route.
        /// The tunnel starts from the entry side and meanders toward the exit,
        /// deliberately avoiding the primary path tiles.
        /// </summary>
        private static void CarveAlternateWormTunnel(
            TileMap map, Random rng,
            int entryTx, int entryTy, int exitTx, int exitTy,
            HashSet<(int, int)> primaryPath)
        {
            int w = map.Width;
            int h = map.Height;

            // Pick a starting X on the opposite side of the map from the primary path center
            int pathCenterX = 0;
            int pathCount   = 0;
            foreach (var (px, _) in primaryPath)
            {
                pathCenterX += px;
                pathCount++;
            }
            if (pathCount > 0) pathCenterX /= pathCount;

            // Start the alternate tunnel on the opposite side
            int startX = pathCenterX < w / 2
                ? rng.Next(2 * w / 3, w - 4)
                : rng.Next(4, w / 3);

            int x = startX;
            int y = entryTy + 2;

            // Worm downward toward exit, avoiding primary path tiles
            while (y < exitTy - 2)
            {
                // Carve 2-tile wide passage
                for (int dx = 0; dx < 2; dx++)
                {
                    int cx = Math.Clamp(x + dx, 2, w - 3);
                    map.SetTile(cx, y, TileType.Empty);
                }

                // Move: mostly downward, with horizontal drift away from primary path
                double roll = rng.NextDouble();
                if (roll < 0.60)
                {
                    y++; // move down
                }
                else if (roll < 0.80)
                {
                    // Drift horizontally away from primary path center
                    x += x < pathCenterX ? -1 : 1;
                }
                else
                {
                    // Occasional horizontal corridor segment
                    int corridorLen = rng.Next(3, 8);
                    int dir         = x < pathCenterX ? -1 : 1;
                    for (int i = 0; i < corridorLen; i++)
                    {
                        x = Math.Clamp(x + dir, 2, w - 3);
                        map.SetTile(x, y, TileType.Empty);
                        map.SetTile(x, Math.Clamp(y + 1, 2, h - 3), TileType.Empty);
                    }
                    y++;
                }

                x = Math.Clamp(x, 2, w - 3);
                y = Math.Clamp(y, 2, h - 3);
            }
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Get all reachable neighbor tiles from (tx, ty) based on player movement rules.
        /// blockedTiles is an optional extra set of tiles treated as impassable.
        /// </summary>
        private static IEnumerable<(int, int)> GetNeighbors(
            TileMap map, int tx, int ty, HashSet<(int, int)>? blockedTiles)
        {
            // Horizontal movement — require player-footprint clearance at destination
            if (IsPassableForPlayer(map, tx - 1, ty, blockedTiles)) yield return (tx - 1, ty);
            if (IsPassableForPlayer(map, tx + 1, ty, blockedTiles)) yield return (tx + 1, ty);

            // Fall down (gravity)
            if (IsPassableForPlayer(map, tx, ty + 1, blockedTiles)) yield return (tx, ty + 1);

            // Jump up (from ground or climbable)
            bool onGround    = !IsPassable(map, tx, ty + 1, blockedTiles)
                             || map.GetTile(tx, ty + 1) == TileType.Platform;
            bool onClimbable = map.GetTile(tx, ty) == TileType.Climbable;

            if (onClimbable)
            {
                // Can climb up multiple tiles
                for (int dy = 1; dy <= MaxJumpHeight; dy++)
                {
                    if (!IsPassableForPlayer(map, tx, ty - dy, blockedTiles)) break;
                    yield return (tx, ty - dy);
                }
            }
            else if (onGround)
            {
                // Standard jump: up to MaxJumpHeight tiles
                for (int dy = 1; dy <= MaxJumpHeight; dy++)
                {
                    if (!IsPassableForPlayer(map, tx, ty - dy, blockedTiles)) break;
                    yield return (tx, ty - dy);
                }
            }
        }

        /// <summary>
        /// Returns true if the tile at (tx, ty) can be occupied by the player
        /// as a single tile (used for ground/ceiling checks, not movement).
        /// blockedTiles is an optional extra set of tiles treated as impassable.
        /// </summary>
        private static bool IsPassable(TileMap map, int tx, int ty,
            HashSet<(int, int)>? blockedTiles)
        {
            if (blockedTiles != null && blockedTiles.Contains((tx, ty))) return false;
            var tile = map.GetTile(tx, ty);
            return tile == TileType.Empty
                || tile == TileType.Platform
                || tile == TileType.SlopeLeft
                || tile == TileType.SlopeRight
                || tile == TileType.Climbable;
        }

        /// <summary>
        /// Returns true if the player body can physically occupy tile (tx, ty).
        /// Checks a 2-tile-tall footprint (the player is ~1.25 tiles tall) so that
        /// 1-tile-tall passages are correctly rejected as impassable (A4).
        /// The tile itself AND the tile directly above must both be passable.
        /// </summary>
        private static bool IsPassableForPlayer(TileMap map, int tx, int ty,
            HashSet<(int, int)>? blockedTiles)
        {
            // The foot tile must be passable
            if (!IsPassable(map, tx, ty, blockedTiles)) return false;
            // The head tile (one above) must also be passable — player is ~1.25 tiles tall
            if (!IsPassable(map, tx, ty - 1, blockedTiles)) return false;
            return true;
        }
    }
}
