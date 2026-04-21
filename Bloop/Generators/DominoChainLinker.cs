using System;
using System.Collections.Generic;

namespace Bloop.Generators
{
    /// <summary>
    /// Groups nearby DisappearingPlatform placements into domino chains.
    /// Platforms within MaxChainDistance tiles of each other are linked.
    /// Each chain gets a unique ChainId; platforms within a chain get a ChainOrder
    /// (0 = first to trigger, ascending from there).
    ///
    /// Chains of 1 platform remain standalone (ChainId = -1, ChainOrder = -1).
    /// </summary>
    public static class DominoChainLinker
    {
        /// <summary>Maximum pixel distance between platforms to be considered for chaining.</summary>
        private const float MaxChainDistance = 6f * 32f; // 6 tiles

        /// <summary>
        /// Assign ChainId and ChainOrder to all DisappearingPlatform placements.
        /// Modifies the placements list in-place.
        /// </summary>
        public static void LinkChains(List<ObjectPlacement> placements, int seed)
        {
            // Collect only disappearing platforms
            var platforms = new List<ObjectPlacement>();
            foreach (var p in placements)
            {
                if (p.Type == ObjectType.DisappearingPlatform)
                    platforms.Add(p);
            }

            if (platforms.Count == 0) return;

            // Union-Find to group nearby platforms into chains
            int n = platforms.Count;
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    float dist = (platforms[i].PixelPosition - platforms[j].PixelPosition).Length();
                    if (dist <= MaxChainDistance)
                    {
                        Union(parent, i, j);
                    }
                }
            }

            // Group platforms by their root parent
            var groups = new Dictionary<int, List<int>>(); // root → list of indices
            for (int i = 0; i < n; i++)
            {
                int root = Find(parent, i);
                if (!groups.ContainsKey(root))
                    groups[root] = new List<int>();
                groups[root].Add(i);
            }

            // Assign chain IDs and orders
            // Use seed-derived RNG for reproducible chain ID assignment
            var rng = new Random(seed + 9973);
            int nextChainId = 0;

            foreach (var (root, indices) in groups)
            {
                // Standalone platforms (group of 1) stay unchained
                if (indices.Count < 2) continue;

                int chainId = nextChainId++;

                // Sort platforms within the chain spatially:
                // primarily by Y (top to bottom), secondarily by X (left to right)
                // This gives a natural cascade order for vertical descents
                indices.Sort((a, b) =>
                {
                    float ay = platforms[a].PixelPosition.Y;
                    float by2 = platforms[b].PixelPosition.Y;
                    if (Math.Abs(ay - by2) > 16f) // more than half a tile difference
                        return ay.CompareTo(by2);
                    return platforms[a].PixelPosition.X.CompareTo(platforms[b].PixelPosition.X);
                });

                // Assign chain ID and order
                for (int order = 0; order < indices.Count; order++)
                {
                    platforms[indices[order]].ChainId    = chainId;
                    platforms[indices[order]].ChainOrder = order;
                }
            }
        }

        // ── Union-Find helpers ─────────────────────────────────────────────────

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]]; // path compression
                i = parent[i];
            }
            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra != rb) parent[ra] = rb;
        }
    }
}
