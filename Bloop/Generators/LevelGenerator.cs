using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Bloop.World;

namespace Bloop.Generators
{
    /// <summary>
    /// Result of a level generation pass.
    /// Contains the populated TileMap, entry/exit points, and object placements.
    /// </summary>
    public class GenerationResult
    {
        /// <summary>The fully populated tile grid.</summary>
        public TileMap TileMap { get; set; } = null!;

        /// <summary>Player spawn position in pixel space.</summary>
        public Vector2 EntryPoint { get; set; }

        /// <summary>Level exit position in pixel space.</summary>
        public Vector2 ExitPoint { get; set; }

        /// <summary>All world objects to be instantiated in this level.</summary>
        public List<ObjectPlacement> ObjectPlacements { get; set; } = new();

        /// <summary>Biome tier for this level (used by TileRenderer for color palette).</summary>
        public BiomeTier Biome { get; set; } = BiomeTier.ShallowCaves;
    }

    // ── Biome tiers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Biome tier determines the visual palette and structural parameters of a level.
    /// Depth 1–5: ShallowCaves, 6–12: FungalGrottos, 13–20: CrystalDepths, 21+: TheAbyss.
    /// </summary>
    public enum BiomeTier
    {
        ShallowCaves  = 0,
        FungalGrottos = 1,
        CrystalDepths = 2,
        TheAbyss      = 3,
    }

    /// <summary>
    /// All generation parameters that vary by biome.
    /// Passed through the pipeline so every step can adapt to the current biome.
    /// </summary>
    public readonly struct BiomeProfile
    {
        // ── Identification ─────────────────────────────────────────────────────
        public BiomeTier Tier { get; init; }

        // ── Noise / density ────────────────────────────────────────────────────
        /// <summary>Primary noise scale (higher = more fragmented caves).</summary>
        public float NoiseScale { get; init; }
        /// <summary>Threshold offset added on top of the depth-scaled base threshold.</summary>
        public float ThresholdOffset { get; init; }
        /// <summary>Cavity threshold offset (larger = bigger open caverns).</summary>
        public float CavityOffset { get; init; }

        // ── Shaft / worm counts ────────────────────────────────────────────────
        public int MinShafts { get; init; }
        public int MaxShafts { get; init; }
        public int MinWorms  { get; init; }
        public int MaxWorms  { get; init; }
        public int WormMinWidth { get; init; }
        public int WormMaxWidth { get; init; }

        // ── Object density multipliers ─────────────────────────────────────────
        public float DensityGlowVine    { get; init; }
        public float DensityRootClump   { get; init; }
        public float DensityStun        { get; init; }
        public float DensityVentFlower  { get; init; }
        public float DensityLichen      { get; init; }
        public float DensityPlatform    { get; init; }

        // ── Factory ────────────────────────────────────────────────────────────
        public static BiomeProfile ForDepth(int depth)
        {
            if (depth <= 5)
                return new BiomeProfile
                {
                    Tier            = BiomeTier.ShallowCaves,
                    NoiseScale      = 0.08f,
                    ThresholdOffset = 0f,
                    CavityOffset    = 0.10f,
                    MinShafts = 2, MaxShafts = 4,
                    MinWorms  = 4, MaxWorms  = 8,
                    WormMinWidth = 2, WormMaxWidth = 4,
                    DensityGlowVine   = 0.8f,
                    DensityRootClump  = 0.7f,
                    DensityStun       = 0.6f,
                    DensityVentFlower = 1.4f,
                    DensityLichen     = 0.8f,
                    DensityPlatform   = 1.3f,
                };
            if (depth <= 12)
                return new BiomeProfile
                {
                    Tier            = BiomeTier.FungalGrottos,
                    NoiseScale      = 0.09f,
                    ThresholdOffset = 0.01f,
                    CavityOffset    = 0.08f,
                    MinShafts = 2, MaxShafts = 4,
                    MinWorms  = 5, MaxWorms  = 9,
                    WormMinWidth = 2, WormMaxWidth = 3,
                    DensityGlowVine   = 1.5f,
                    DensityRootClump  = 1.3f,
                    DensityStun       = 0.9f,
                    DensityVentFlower = 0.9f,
                    DensityLichen     = 1.6f,
                    DensityPlatform   = 1.0f,
                };
            if (depth <= 20)
                return new BiomeProfile
                {
                    Tier            = BiomeTier.CrystalDepths,
                    NoiseScale      = 0.10f,
                    ThresholdOffset = 0.02f,
                    CavityOffset    = 0.06f,
                    MinShafts = 3, MaxShafts = 5,
                    MinWorms  = 3, MaxWorms  = 6,
                    WormMinWidth = 2, WormMaxWidth = 3,
                    DensityGlowVine   = 0.6f,
                    DensityRootClump  = 0.5f,
                    DensityStun       = 1.6f,
                    DensityVentFlower = 0.5f,
                    DensityLichen     = 0.7f,
                    DensityPlatform   = 0.8f,
                };
            // TheAbyss (depth 21+)
            return new BiomeProfile
            {
                Tier            = BiomeTier.TheAbyss,
                NoiseScale      = 0.11f,
                ThresholdOffset = 0.03f,
                CavityOffset    = 0.04f,
                MinShafts = 3, MaxShafts = 5,
                MinWorms  = 2, MaxWorms  = 5,
                WormMinWidth = 2, WormMaxWidth = 2,
                DensityGlowVine   = 0.3f,
                DensityRootClump  = 0.4f,
                DensityStun       = 2.0f,
                DensityVentFlower = 0.2f,
                DensityLichen     = 0.5f,
                DensityPlatform   = 0.6f,
            };
        }
    }

    /// <summary>
    /// Master procedural level generator.
    ///
    /// Pipeline:
    ///   1.  Initialize two PerlinNoise instances (primary + low-freq cavity)
    ///   2.  Generate dual noise grids at level dimensions (80 × 120 tiles)
    ///   3.  Apply combined threshold: primary noise + cavity noise create organic spaces
    ///   4.  Force border walls and floor
    ///   5.  Carve 2–4 branching vertical shafts with horizontal alcoves
    ///   6.  Run 4–8 horizontal worm tunnels for cross-cave connectivity
    ///   7.  Smooth edges (2 cellular automata passes)
    ///   8.  Flood-fill connectivity: connect isolated regions to main cave
    ///   9.  Inject pillar bands to disrupt vertical drops
    ///  10.  Enforce minimum passage width (2 tiles)
    ///  11.  Detect and place slope tiles
    ///  12.  Place climbable wall sections
    ///  13.  Find entry (top-center) and exit (bottom-center)
    ///  14.  Validate with PathValidator (retry up to 5 times if invalid)
    ///  15.  Place world objects via ObjectPlacer (context-aware)
    ///  16.  Link domino chains via DominoChainLinker
    /// </summary>
    public static class LevelGenerator
    {
        // ── Level dimensions ───────────────────────────────────────────────────
        public const int LevelWidth  = 80;
        public const int LevelHeight = 120;

        // ── Primary noise parameters ───────────────────────────────────────────
        private const float NoiseScale       = 0.08f;
        private const int   NoiseOctaves     = 4;
        private const float NoisePersistence = 0.5f;
        private const float NoiseLacunarity  = 2.0f;

        // ── Cavity noise parameters (low-frequency for large open spaces) ──────
        private const float CavityNoiseScale       = 0.03f;
        private const int   CavityNoiseOctaves     = 2;
        private const float CavityNoisePersistence = 0.6f;
        private const float CavityNoiseLacunarity  = 2.0f;
        /// <summary>How much lower the cavity threshold is vs primary threshold.
        /// Larger = bigger open caverns.</summary>
        private const float CavityThresholdOffset = 0.10f;

        // ── Threshold ─────────────────────────────────────────────────────────
        /// <summary>Base density threshold at depth 1. Higher = more solid rock.</summary>
        private const float BaseThreshold  = 0.44f;
        /// <summary>Threshold increase per depth level (reduced 0.004→0.003 for gentler curve).</summary>
        private const float ThresholdDepthScale = 0.003f;
        /// <summary>Threshold adjustment per retry (loosen if too dense).</summary>
        private const float ThresholdRetryAdjust = -0.02f;
        private const int   MaxRetries = 5;

        // ── Shaft parameters ──────────────────────────────────────────────────
        private const int MinShafts = 2;
        private const int MaxShafts = 4;
        private const int ShaftWidth = 3; // tiles wide

        // ── Worm tunnel parameters ─────────────────────────────────────────────
        private const int MinWorms = 4;
        private const int MaxWorms = 8;
        private const int WormMinSteps = 20;
        private const int WormMaxSteps = 50;
        private const int WormMinWidth = 2;
        private const int WormMaxWidth = 4;

        // ── Climbable wall chance ──────────────────────────────────────────────
        /// <summary>Base chance per eligible wall run to become climbable.</summary>
        private const float ClimbableChance = 0.15f;
        /// <summary>Climbable chance in deep biomes (CrystalDepths, TheAbyss) where terrain is denser.</summary>
        private const float ClimbableChanceDeep = 0.25f;
        /// <summary>Minimum shaft height (tiles) that triggers guaranteed escape route injection.</summary>
        private const int ShaftEscapeMinHeight = 8;
        /// <summary>Maximum vertical gap (tiles) between escape mechanisms in a shaft.</summary>
        private const int ShaftEscapeInterval = 3;

        // ── Large prime for seed derivation ───────────────────────────────────
        private const int SeedPrime = 7919;

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Generate a complete level for the given seed and depth.
        /// Retries up to MaxRetries times if PathValidator rejects the layout.
        /// </summary>
        public static GenerationResult Generate(int seed, int depth)
        {
            int effectiveSeed = seed + depth * SeedPrime;
            float threshold   = BaseThreshold + (depth - 1) * ThresholdDepthScale;

            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                float t = threshold + attempt * ThresholdRetryAdjust;
                var result = TryGenerate(effectiveSeed + attempt, depth, t);
                if (result != null) return result;
            }

            // Fallback: generate with very loose threshold (guaranteed passable)
            return TryGenerate(effectiveSeed, depth, 0.35f)
                ?? GenerateFallback(depth, seed);
        }

        // ── Private generation pipeline ────────────────────────────────────────

        private static GenerationResult? TryGenerate(int effectiveSeed, int depth, float threshold)
        {
            var biome = BiomeProfile.ForDepth(depth);
            var rng   = new Random(effectiveSeed);
            var map   = new TileMap(LevelWidth, LevelHeight);
            var noise = new PerlinNoise(effectiveSeed);

            // ── Step 1: Generate primary noise grid (biome-scaled) ─────────────
            float[,] grid = noise.GenerateGrid(
                LevelWidth, LevelHeight,
                biome.NoiseScale, NoiseOctaves, NoisePersistence, NoiseLacunarity);

            // ── Step 2: Generate secondary low-frequency cavity noise ──────────
            var cavityNoise = new PerlinNoise(effectiveSeed ^ 0xA3F1);
            float[,] cavityGrid = cavityNoise.GenerateGrid(
                LevelWidth, LevelHeight,
                CavityNoiseScale, CavityNoiseOctaves,
                CavityNoisePersistence, CavityNoiseLacunarity);

            // ── Step 3: Apply combined threshold (biome offset applied) ────────
            float effectiveThreshold = threshold + biome.ThresholdOffset;
            float cavityThreshold    = effectiveThreshold - biome.CavityOffset;
            for (int ty = 0; ty < LevelHeight; ty++)
            {
                for (int tx = 0; tx < LevelWidth; tx++)
                {
                    bool primarySolid = grid[tx, ty] >= effectiveThreshold;
                    bool cavityEmpty  = cavityGrid[tx, ty] < cavityThreshold;

                    // If cavity noise says open, override primary noise
                    map.SetTile(tx, ty, (primarySolid && !cavityEmpty)
                        ? TileType.Solid
                        : TileType.Empty);
                }
            }

            // ── Step 4: Force borders ──────────────────────────────────────────
            ForceBorders(map);

            // ── Step 5: Carve branching vertical shafts (biome shaft counts) ───
            int shaftCount = rng.Next(biome.MinShafts, biome.MaxShafts + 1);
            CarveBranchingShafts(map, rng, shaftCount);

            // ── Step 6: Carve horizontal worm tunnels (biome worm params) ──────
            int wormCount = rng.Next(biome.MinWorms, biome.MaxWorms + 1);
            CarveWormTunnels(map, rng, wormCount, biome.WormMinWidth, biome.WormMaxWidth);

            // ── Step 7: Inject landmark rooms BEFORE smoothing (1.3) ─────────
            // Rooms are carved before the cellular automata pass so their edges
            // get naturally softened. The connectivity pass afterwards ensures
            // they remain reachable.
            RoomTemplates.InjectRooms(map, rng, depth);

            // ── Step 7b: Inject pillar bands BEFORE smoothing ─────────────────
            // Moved here so cellular automata can soften pillar edges, and so
            // the subsequent connectivity pass can fix any sealed passages.
            InjectPillarBands(map, rng, effectiveSeed);

            // ── Step 8: Smooth (3 cellular automata passes for more organic caves) ──
            SmoothPass(map);
            SmoothPass(map);
            SmoothPass(map);

            // ── Step 9: Flood-fill connectivity ───────────────────────────────
            ConnectIsolatedRegions(map, rng);

            // ── Step 10: Enforce minimum passage width (A1, A2) ───────────────
            EnforceMinPassageWidth(map);

            // ── Step 10b: Remove orphan/peninsula solid blocks (A3) ───────────
            RemoveOrphanBlocks(map);

            // ── Step 10c: Erosion pass — remove thin spikes (C2) ─────────────
            ErosionPass(map);

            // ── Step 11: Detect slopes ─────────────────────────────────────────
            DetectSlopes(map);

            // ── Step 12: Place climbable wall sections (biome-scaled chance) ──
            float climbChance = (biome.Tier == BiomeTier.CrystalDepths ||
                                 biome.Tier == BiomeTier.TheAbyss)
                ? ClimbableChanceDeep
                : ClimbableChance;
            PlaceClimbableWalls(map, rng, climbChance);

            // ── Step 12b: Guarantee escape routes in tall vertical shafts ─────
            EnsureShaftEscapeRoutes(map, rng);

            // ── Step 13: Find entry and exit ───────────────────────────────────
            var (entryTx, entryTy) = FindEntryPoint(map);
            var (exitTx,  exitTy)  = FindExitPoint(map);

            if (entryTx < 0 || exitTx < 0) return null; // no valid entry/exit

            Vector2 entryPixel = TileMap.TileCenter(entryTx, entryTy);
            Vector2 exitPixel  = TileMap.TileCenter(exitTx,  exitTy);

            // ── Step 14: Validate paths ────────────────────────────────────────
            bool valid = PathValidator.Validate(map, entryPixel, exitPixel, rng);
            if (!valid) return null;

            // ── Step 15: Place objects (biome density multipliers) ─────────────
            var placements = ObjectPlacer.PlaceObjects(map, effectiveSeed, depth, biome);

            // ── Step 15b: Post-placement passability clearance check ───────────
            // Removes wall-attached objects that block narrow passages, ensuring
            // the BFS path from entry to exit remains clear after object placement.
            ObjectPlacer.ValidatePassability(map, placements, entryPixel, exitPixel);

            // ── Step 16: Link domino chains ────────────────────────────────────
            DominoChainLinker.LinkChains(placements, effectiveSeed);

            return new GenerationResult
            {
                TileMap          = map,
                EntryPoint       = entryPixel,
                ExitPoint        = exitPixel,
                ObjectPlacements = placements,
                Biome            = biome.Tier,
            };
        }

        // ── Border enforcement ─────────────────────────────────────────────────

        private static void ForceBorders(TileMap map)
        {
            int w = map.Width;
            int h = map.Height;

            // Left and right walls
            for (int ty = 0; ty < h; ty++)
            {
                map.SetTile(0,     ty, TileType.Solid);
                map.SetTile(w - 1, ty, TileType.Solid);
            }

            // Ceiling (top 2 rows)
            for (int tx = 0; tx < w; tx++)
            {
                map.SetTile(tx, 0, TileType.Solid);
                map.SetTile(tx, 1, TileType.Solid);
            }

            // Floor (bottom 2 rows)
            for (int tx = 0; tx < w; tx++)
            {
                map.SetTile(tx, h - 1, TileType.Solid);
                map.SetTile(tx, h - 2, TileType.Solid);
            }
        }

        // ── Branching shaft carving ────────────────────────────────────────────

        /// <summary>
        /// Carve vertical shafts that branch and reconnect, with horizontal alcoves.
        /// Each shaft can fork into two narrower branches that diverge and may merge.
        /// </summary>
        private static void CarveBranchingShafts(TileMap map, Random rng, int count)
        {
            int w = map.Width;
            int h = map.Height;

            // Distribute shafts evenly across the width
            int sectionWidth = (w - 4) / count;

            for (int s = 0; s < count; s++)
            {
                int baseX  = 2 + s * sectionWidth;
                int shaftX = baseX + rng.Next(1, Math.Max(2, sectionWidth - ShaftWidth - 1));
                shaftX     = Math.Clamp(shaftX, 2, w - ShaftWidth - 2);

                int currentX = shaftX;
                int ty       = 2;

                // Track if we're in a forked state
                bool forked      = false;
                int  forkX1      = currentX;
                int  forkX2      = currentX;
                int  forkEndY    = -1;

                while (ty < h - 2)
                {
                    // ── Vertical segment 6–12 rows tall ───────────────────────
                    int segmentHeight = rng.Next(6, 13);
                    int segmentEndY   = Math.Min(h - 2, ty + segmentHeight);

                    // Decide whether to fork this segment (20% chance if not already forked)
                    bool shouldFork = !forked && segmentHeight >= 10 && rng.NextDouble() < 0.20;

                    if (shouldFork)
                    {
                        // Fork: carve two narrower branches diverging from currentX
                        int diverge = rng.Next(4, 10);
                        forkX1   = Math.Clamp(currentX - diverge / 2, 2, w - ShaftWidth - 2);
                        forkX2   = Math.Clamp(currentX + diverge / 2, 2, w - ShaftWidth - 2);
                        forkEndY = Math.Min(h - 2, ty + rng.Next(15, 30));
                        forked   = true;
                    }

                    for (int y = ty; y < segmentEndY; y++)
                    {
                        // Minor meander ±1 every 3–5 rows
                        if (!forked && (y - ty) > 0 && (y - ty) % rng.Next(3, 6) == 0)
                            currentX = Math.Clamp(currentX + rng.Next(-1, 2), 2, w - ShaftWidth - 2);

                        if (forked && y < forkEndY)
                        {
                            // Carve both fork branches (2 tiles wide each)
                            for (int dx = 0; dx < 2; dx++)
                            {
                                map.SetTile(Math.Clamp(forkX1 + dx, 1, w - 2), y, TileType.Empty);
                                map.SetTile(Math.Clamp(forkX2 + dx, 1, w - 2), y, TileType.Empty);
                            }
                        }
                        else
                        {
                            if (forked && y >= forkEndY)
                            {
                                // Reconnect: merge back to center
                                currentX = (forkX1 + forkX2) / 2;
                                forked   = false;
                            }
                            // Normal carve: ShaftWidth tiles wide
                            for (int dx = 0; dx < ShaftWidth; dx++)
                                map.SetTile(currentX + dx, y, TileType.Empty);
                        }

                        // Carve horizontal alcove (short dead-end passage) every 15–25 rows
                        if (!forked && (y - 2) % rng.Next(15, 26) == 0 && y > 5 && y < h - 5)
                        {
                            int alcoveDir    = rng.Next(2) == 0 ? -1 : 1;
                            int alcoveLength = rng.Next(4, 10);
                            int alcoveHeight = 2;

                            for (int ax = 1; ax <= alcoveLength; ax++)
                            {
                                int alcoveX = currentX + (alcoveDir > 0 ? ShaftWidth - 1 + ax : -ax);
                                alcoveX = Math.Clamp(alcoveX, 2, w - 3);
                                for (int ay = 0; ay < alcoveHeight; ay++)
                                    map.SetTile(alcoveX, Math.Clamp(y + ay, 2, h - 3), TileType.Empty);
                            }
                        }
                    }

                    ty = segmentEndY;
                    if (ty >= h - 4) break;

                    // ── Horizontal corridor connecting to next segment ────────
                    int offset = rng.Next(4, 11) * (rng.Next(0, 2) == 0 ? -1 : 1);
                    int nextX  = Math.Clamp(currentX + offset, 2, w - ShaftWidth - 2);

                    int corridorLeft  = Math.Min(currentX, nextX);
                    int corridorRight = Math.Max(currentX + ShaftWidth - 1, nextX + ShaftWidth - 1);
                    int corridorTop   = ty;
                    int corridorBot   = Math.Min(h - 2, ty + 3); // 3 tiles tall

                    for (int cy = corridorTop; cy < corridorBot; cy++)
                        for (int cx = corridorLeft; cx <= corridorRight; cx++)
                            map.SetTile(cx, cy, TileType.Empty);

                    currentX = nextX;
                    ty       = corridorBot;
                }
            }
        }

        // ── Horizontal worm tunnels ────────────────────────────────────────────

        /// <summary>
        /// Carve horizontal worm tunnels that meander across the map.
        /// Each worm starts from a random edge position and walks mostly horizontally,
        /// creating organic cross-cave passages that break up vertical linearity.
        /// </summary>
        private static void CarveWormTunnels(TileMap map, Random rng, int count,
            int wormMinWidth = WormMinWidth, int wormMaxWidth = WormMaxWidth)
        {
            int w = map.Width;
            int h = map.Height;

            for (int i = 0; i < count; i++)
            {
                // Start from left or right edge at a random Y in the middle 60% of the map
                bool fromLeft = rng.Next(2) == 0;
                int  x        = fromLeft ? 3 : w - 4;
                int  y        = rng.Next(h / 5, 4 * h / 5);
                int  wormW    = rng.Next(wormMinWidth, wormMaxWidth + 1);
                int  steps    = rng.Next(WormMinSteps, WormMaxSteps + 1);

                for (int s = 0; s < steps; s++)
                {
                    // Carve a roughly circular blob at current position
                    int r = wormW / 2;
                    for (int dy = -r; dy <= r; dy++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (dx * dx + dy * dy <= r * r + 1)
                            {
                                int tx = Math.Clamp(x + dx, 2, w - 3);
                                int ty = Math.Clamp(y + dy, 2, h - 3);
                                map.SetTile(tx, ty, TileType.Empty);
                            }
                        }
                    }

                    // Move: 70% horizontal (toward opposite edge), 30% vertical drift
                    double roll = rng.NextDouble();
                    if (roll < 0.70)
                    {
                        // Horizontal: move toward opposite edge
                        x += fromLeft ? 1 : -1;
                    }
                    else if (roll < 0.85)
                    {
                        y -= 1; // drift up
                    }
                    else
                    {
                        y += 1; // drift down
                    }

                    // Occasional direction reversal for more organic shape
                    if (rng.NextDouble() < 0.05)
                        fromLeft = !fromLeft;

                    x = Math.Clamp(x, 2, w - 3);
                    y = Math.Clamp(y, 2, h - 3);

                    // Stop if we've crossed the map
                    if (x <= 2 || x >= w - 3) break;
                }
            }
        }

        // ── Flood-fill connectivity ────────────────────────────────────────────

        /// <summary>
        /// Ensure all large empty regions are connected to the main cave.
        /// Flood-fills from the top-center to find the main region, then
        /// connects any isolated region larger than MinRegionSize tiles
        /// by carving a short tunnel to the nearest main-region tile.
        /// Small isolated pockets are filled solid to avoid confusing dead-ends.
        /// </summary>
        private static void ConnectIsolatedRegions(TileMap map, Random rng)
        {
            int w = map.Width;
            int h = map.Height;
            const int MinRegionSize = 20; // regions smaller than this are filled solid

            var visited = new bool[w, h];

            // Find the main region by flood-filling from the top-center area
            int startX = w / 2;
            int startY = 3;
            // Find first empty tile near top-center
            for (int ty = 3; ty < h / 4; ty++)
            {
                for (int tx = w / 3; tx < 2 * w / 3; tx++)
                {
                    if (map.GetTile(tx, ty) == TileType.Empty)
                    {
                        startX = tx;
                        startY = ty;
                        goto foundStart;
                    }
                }
            }
            foundStart:

            // Flood-fill the main region
            var mainRegion = FloodFill(map, startX, startY, visited);

            // Find all unvisited empty tiles (isolated regions)
            var isolatedRegions = new List<List<(int x, int y)>>();
            for (int ty = 2; ty < h - 2; ty++)
            {
                for (int tx = 2; tx < w - 2; tx++)
                {
                    if (map.GetTile(tx, ty) == TileType.Empty && !visited[tx, ty])
                    {
                        var region = FloodFill(map, tx, ty, visited);
                        isolatedRegions.Add(region);
                    }
                }
            }

            // Process isolated regions
            foreach (var region in isolatedRegions)
            {
                if (region.Count < MinRegionSize)
                {
                    // Fill small isolated pockets solid
                    foreach (var (rx, ry) in region)
                        map.SetTile(rx, ry, TileType.Solid);
                }
                else
                {
                    // Connect large isolated regions to the main cave
                    // Find the closest pair of tiles between this region and the main region
                    var (rx, ry) = region[rng.Next(region.Count)];
                    ConnectToMainRegion(map, rx, ry, mainRegion);
                }
            }
        }

        /// <summary>
        /// Flood-fill from (startX, startY), marking visited tiles.
        /// Returns the list of all tiles in the connected region.
        /// </summary>
        private static List<(int x, int y)> FloodFill(
            TileMap map, int startX, int startY, bool[,] visited)
        {
            var region = new List<(int x, int y)>();
            if (map.GetTile(startX, startY) != TileType.Empty) return region;
            if (visited[startX, startY]) return region;

            var queue = new Queue<(int x, int y)>();
            queue.Enqueue((startX, startY));
            visited[startX, startY] = true;

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                region.Add((x, y));

                // 4-directional flood fill
                foreach (var (nx, ny) in new[] { (x-1,y),(x+1,y),(x,y-1),(x,y+1) })
                {
                    if (nx < 0 || nx >= map.Width || ny < 0 || ny >= map.Height) continue;
                    if (visited[nx, ny]) continue;
                    if (map.GetTile(nx, ny) != TileType.Empty) continue;
                    visited[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }

            return region;
        }

        /// <summary>
        /// Carve a straight tunnel from (fromX, fromY) toward the nearest tile
        /// in the main region, connecting the isolated region to the main cave.
        /// </summary>
        private static void ConnectToMainRegion(
            TileMap map, int fromX, int fromY,
            List<(int x, int y)> mainRegion)
        {
            if (mainRegion.Count == 0) return;

            // Find the closest main-region tile
            int bestX = mainRegion[0].x;
            int bestY = mainRegion[0].y;
            int bestDist = int.MaxValue;

            // Sample a subset for performance (check every 10th tile)
            for (int i = 0; i < mainRegion.Count; i += Math.Max(1, mainRegion.Count / 100))
            {
                var (mx, my) = mainRegion[i];
                int dist = (mx - fromX) * (mx - fromX) + (my - fromY) * (my - fromY);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestX    = mx;
                    bestY    = my;
                }
            }

            // Carve an L-shaped tunnel: horizontal then vertical.
            // Width is 3 tiles (C4) so the player can navigate the L-bend without
            // getting stuck at the corner.
            int cx = fromX;
            while (cx != bestX)
            {
                map.SetTile(cx, fromY, TileType.Empty);
                map.SetTile(cx, Math.Clamp(fromY + 1, 0, map.Height - 1), TileType.Empty);
                map.SetTile(cx, Math.Clamp(fromY - 1, 0, map.Height - 1), TileType.Empty);
                cx += cx < bestX ? 1 : -1;
            }
            int cy = fromY;
            while (cy != bestY)
            {
                map.SetTile(bestX, cy, TileType.Empty);
                map.SetTile(Math.Clamp(bestX + 1, 0, map.Width - 1), cy, TileType.Empty);
                map.SetTile(Math.Clamp(bestX - 1, 0, map.Width - 1), cy, TileType.Empty);
                cy += cy < bestY ? 1 : -1;
            }
        }

        // ── Pillar band injection ──────────────────────────────────────────────

        /// <summary>
        /// Sample a second low-frequency Perlin layer and drop solid tiles in
        /// periodic vertical bands. This creates column-like pillars that
        /// interrupt long vertical drops and force horizontal detours.
        /// Skips the top/bottom 4 rows so entry/exit stay reachable.
        ///
        /// Improvement (1.4): After injecting each pillar tile, a local flood-fill
        /// check verifies that the tile above and below the pillar are still
        /// connected within a small radius. If the pillar seals a passage, a 2-tile
        /// gap is carved through it to restore connectivity.
        /// </summary>
        private static void InjectPillarBands(TileMap map, Random rng, int seed)
        {
            var pillarNoise = new PerlinNoise(seed ^ 0x51ED);
            int w = map.Width;
            int h = map.Height;

            const int BandPeriod        = 12;   // pillar band every 12 tiles
            const int BandWidth         = 2;    // 2-tile-wide pillar column
            const float PillarThreshold = 0.58f;
            const int LocalCheckRadius  = 6;    // flood-fill radius for connectivity check

            for (int tx = 3; tx < w - 3; tx++)
            {
                if (tx % BandPeriod >= BandWidth) continue;

                for (int ty = 4; ty < h - 4; ty++)
                {
                    if (map.GetTile(tx, ty) != TileType.Empty) continue;

                    float n = pillarNoise.SampleOctaves(
                        tx * NoiseScale * 0.25f,
                        ty * NoiseScale * 0.25f,
                        2, 0.5f, 2.0f);

                    if (n <= PillarThreshold) continue;

                    // Tentatively place the pillar tile
                    map.SetTile(tx, ty, TileType.Solid);

                    // ── Local connectivity check ──────────────────────────────
                    // Find an empty tile above and below within the local radius.
                    // If they exist but are no longer connected after the pillar,
                    // carve a 2-tile gap through the pillar band at this row.
                    int aboveY = -1, belowY = -1;
                    for (int dy = 1; dy <= LocalCheckRadius; dy++)
                    {
                        if (aboveY < 0 && ty - dy >= 2
                            && map.GetTile(tx, ty - dy) == TileType.Empty)
                            aboveY = ty - dy;
                        if (belowY < 0 && ty + dy < h - 2
                            && map.GetTile(tx, ty + dy) == TileType.Empty)
                            belowY = ty + dy;
                        if (aboveY >= 0 && belowY >= 0) break;
                    }

                    if (aboveY >= 0 && belowY >= 0)
                    {
                        // Check if above and below are still connected via a local BFS
                        // (capped at LocalCheckRadius*LocalCheckRadius tiles for speed)
                        bool connected = LocalBFSConnected(
                            map, tx, aboveY, tx, belowY,
                            LocalCheckRadius * LocalCheckRadius);

                        if (!connected)
                        {
                            // Carve a 2-tile gap through the pillar band at this column
                            map.SetTile(tx, ty, TileType.Empty);
                            if (tx + 1 < w - 2)
                                map.SetTile(tx + 1, ty, TileType.Empty);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Lightweight BFS that checks whether (startTx, startTy) can reach
        /// (goalTx, goalTy) using only empty tiles, capped at maxTiles visited.
        /// Used by InjectPillarBands for fast local connectivity checks.
        /// </summary>
        private static bool LocalBFSConnected(
            TileMap map, int startTx, int startTy, int goalTx, int goalTy, int maxTiles)
        {
            if (map.GetTile(startTx, startTy) != TileType.Empty) return false;
            if (map.GetTile(goalTx,  goalTy)  != TileType.Empty) return false;

            var visited = new HashSet<(int, int)>();
            var queue   = new Queue<(int, int)>();
            queue.Enqueue((startTx, startTy));
            visited.Add((startTx, startTy));

            while (queue.Count > 0 && visited.Count <= maxTiles)
            {
                var (tx, ty) = queue.Dequeue();
                if (tx == goalTx && ty == goalTy) return true;

                foreach (var (nx, ny) in new[]
                    { (tx-1,ty),(tx+1,ty),(tx,ty-1),(tx,ty+1) })
                {
                    if (visited.Contains((nx, ny))) continue;
                    if (nx < 0 || nx >= map.Width || ny < 0 || ny >= map.Height) continue;
                    if (map.GetTile(nx, ny) != TileType.Empty) continue;
                    visited.Add((nx, ny));
                    queue.Enqueue((nx, ny));
                }
            }

            return false;
        }

        // ── Cellular automata smoothing ────────────────────────────────────────

        private static void SmoothPass(TileMap map)
        {
            int w = map.Width;
            int h = map.Height;

            // Work on a copy to avoid order-dependent updates
            var copy = new TileType[w, h];
            for (int ty = 0; ty < h; ty++)
                for (int tx = 0; tx < w; tx++)
                    copy[tx, ty] = map.GetTile(tx, ty);

            for (int ty = 1; ty < h - 1; ty++)
            {
                for (int tx = 1; tx < w - 1; tx++)
                {
                    // Skip border tiles
                    if (tx == 0 || tx == w - 1 || ty == 0 || ty == h - 1) continue;

                    int solidCount = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (TileProperties.IsSolid(copy[tx + dx, ty + dy]))
                                solidCount++;

                    // Standard cellular automata rule
                    if (solidCount >= 5)
                        map.SetTile(tx, ty, TileType.Solid);
                    else if (solidCount <= 2)
                        map.SetTile(tx, ty, TileType.Empty);
                    // else: keep current tile
                }
            }
        }

        // ── Minimum passage width ──────────────────────────────────────────────

        /// <summary>
        /// Ensure all passages are wide enough for the player body (~0.75×1.25 tiles).
        ///
        /// Pass 1 — Vertical clearance: any empty tile with solid directly above AND
        ///   below is a 1-tile-tall horizontal passage. Clear the tile above so the
        ///   player (1.25 tiles tall) can pass through.
        ///
        /// Pass 2 — Horizontal clearance: any empty tile with solid directly left AND
        ///   right is a 1-tile-wide vertical passage. Clear the tile to the right so
        ///   the player (0.75 tiles wide) has room.
        ///
        /// Pass 3 — Player-footprint check: scan every empty tile and verify that a
        ///   2-wide × 2-tall rectangle centred on it fits without overlapping solid
        ///   tiles. If it doesn't, clear the most constrained neighbour.
        ///
        /// Pass 4 — Diagonal pinch elimination (A2): two diagonally-adjacent solid
        ///   tiles with both orthogonal neighbours empty create a gap the player can
        ///   slip into but not escape. Clear one of the pair.
        /// </summary>
        private static void EnforceMinPassageWidth(TileMap map)
        {
            int w = map.Width;
            int h = map.Height;

            // ── Pass 1: Widen 1-tile-tall horizontal passages ─────────────────
            for (int ty = 2; ty < h - 2; ty++)
            {
                for (int tx = 1; tx < w - 1; tx++)
                {
                    if (map.GetTile(tx, ty) != TileType.Empty) continue;

                    bool solidAbove = TileProperties.IsSolid(map.GetTile(tx, ty - 1));
                    bool solidBelow = TileProperties.IsSolid(map.GetTile(tx, ty + 1));

                    // 1-tile-tall gap — clear the tile above to give 2-tile headroom
                    if (solidAbove && solidBelow && ty > 2)
                        map.SetTile(tx, ty - 1, TileType.Empty);
                }
            }

            // ── Pass 2: Widen 1-tile-wide vertical passages ───────────────────
            for (int tx = 2; tx < w - 2; tx++)
            {
                for (int ty = 1; ty < h - 1; ty++)
                {
                    if (map.GetTile(tx, ty) != TileType.Empty) continue;

                    bool solidLeft  = TileProperties.IsSolid(map.GetTile(tx - 1, ty));
                    bool solidRight = TileProperties.IsSolid(map.GetTile(tx + 1, ty));

                    // 1-tile-wide gap — clear the tile to the right
                    if (solidLeft && solidRight && tx < w - 2)
                        map.SetTile(tx + 1, ty, TileType.Empty);
                }
            }

            // ── Pass 3: Player-footprint check (A1) ───────────────────────────
            // The player body is ~0.75 tiles wide × 1.25 tiles tall.
            // Require every empty tile to have at least one empty tile above it
            // (2-tile vertical clearance) so the player can stand there.
            for (int ty = 2; ty < h - 2; ty++)
            {
                for (int tx = 1; tx < w - 1; tx++)
                {
                    if (map.GetTile(tx, ty) != TileType.Empty) continue;

                    // Need the tile above to be empty for standing headroom
                    if (TileProperties.IsSolid(map.GetTile(tx, ty - 1)))
                    {
                        // Clear the tile above — prefer this over clearing below
                        // (floor tiles are more structurally important)
                        if (ty - 1 > 1)
                            map.SetTile(tx, ty - 1, TileType.Empty);
                    }
                }
            }

            // ── Pass 4: Diagonal pinch elimination (A2) ───────────────────────
            // Pattern: solid at (x,y) and (x+1,y+1) with empty at (x+1,y) and (x,y+1)
            // — or the mirror — creates a diagonal gap the player can enter but not exit.
            // Fix: clear one of the two solid tiles (prefer the one with fewer solid
            // cardinal neighbours so we remove the less-connected block).
            for (int ty = 1; ty < h - 2; ty++)
            {
                for (int tx = 1; tx < w - 2; tx++)
                {
                    bool s00 = TileProperties.IsSolid(map.GetTile(tx,     ty));
                    bool s10 = TileProperties.IsSolid(map.GetTile(tx + 1, ty));
                    bool s01 = TileProperties.IsSolid(map.GetTile(tx,     ty + 1));
                    bool s11 = TileProperties.IsSolid(map.GetTile(tx + 1, ty + 1));

                    // Pattern A: solid diagonal (0,0)↔(1,1), empty anti-diagonal
                    if (s00 && s11 && !s10 && !s01)
                    {
                        // Clear the tile with fewer solid cardinal neighbours
                        int n00 = CountSolidCardinalNeighbours(map, tx,     ty,     w, h);
                        int n11 = CountSolidCardinalNeighbours(map, tx + 1, ty + 1, w, h);
                        if (n00 <= n11)
                            map.SetTile(tx,     ty,     TileType.Empty);
                        else
                            map.SetTile(tx + 1, ty + 1, TileType.Empty);
                    }
                    // Pattern B: solid anti-diagonal (1,0)↔(0,1), empty diagonal
                    else if (s10 && s01 && !s00 && !s11)
                    {
                        int n10 = CountSolidCardinalNeighbours(map, tx + 1, ty,     w, h);
                        int n01 = CountSolidCardinalNeighbours(map, tx,     ty + 1, w, h);
                        if (n10 <= n01)
                            map.SetTile(tx + 1, ty,     TileType.Empty);
                        else
                            map.SetTile(tx,     ty + 1, TileType.Empty);
                    }
                }
            }
        }

        /// <summary>Count how many of the 4 cardinal neighbours of (tx,ty) are solid.</summary>
        private static int CountSolidCardinalNeighbours(TileMap map, int tx, int ty, int w, int h)
        {
            int count = 0;
            if (tx > 0     && TileProperties.IsSolid(map.GetTile(tx - 1, ty))) count++;
            if (tx < w - 1 && TileProperties.IsSolid(map.GetTile(tx + 1, ty))) count++;
            if (ty > 0     && TileProperties.IsSolid(map.GetTile(tx, ty - 1))) count++;
            if (ty < h - 1 && TileProperties.IsSolid(map.GetTile(tx, ty + 1))) count++;
            return count;
        }

        // ── Slope detection ────────────────────────────────────────────────────

        private static void DetectSlopes(TileMap map)
        {
            int w = map.Width;
            int h = map.Height;

            for (int ty = 1; ty < h - 1; ty++)
            {
                for (int tx = 1; tx < w - 1; tx++)
                {
                    // Only process solid tiles that have empty space above them
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                    if (map.GetTile(tx, ty - 1) != TileType.Empty) continue;

                    bool solidLeft  = TileProperties.IsSolid(map.GetTile(tx - 1, ty));
                    bool solidRight = TileProperties.IsSolid(map.GetTile(tx + 1, ty));
                    bool emptyLeft  = map.GetTile(tx - 1, ty) == TileType.Empty;
                    bool emptyRight = map.GetTile(tx + 1, ty) == TileType.Empty;

                    // SlopeRight: solid on left, empty on right, solid below
                    if (solidLeft && emptyRight &&
                        TileProperties.IsSolid(map.GetTile(tx, ty + 1)) &&
                        map.GetTile(tx - 1, ty - 1) == TileType.Empty)
                    {
                        map.SetTile(tx, ty, TileType.SlopeRight);
                    }
                    // SlopeLeft: empty on left, solid on right, solid below
                    else if (emptyLeft && solidRight &&
                             TileProperties.IsSolid(map.GetTile(tx, ty + 1)) &&
                             map.GetTile(tx + 1, ty - 1) == TileType.Empty)
                    {
                        map.SetTile(tx, ty, TileType.SlopeLeft);
                    }
                }
            }
        }

        // ── Climbable wall placement ───────────────────────────────────────────

        /// <summary>
        /// Mark random wall runs as climbable. chance controls probability per eligible run.
        /// </summary>
        private static void PlaceClimbableWalls(TileMap map, Random rng, float chance = ClimbableChance)
        {
            int w = map.Width;
            int h = map.Height;

            for (int tx = 1; tx < w - 1; tx++)
            {
                int runStart = -1;
                for (int ty = 2; ty < h - 2; ty++)
                {
                    bool isSolid    = TileProperties.IsSolid(map.GetTile(tx, ty));
                    bool emptyRight = map.GetTile(tx + 1, ty) == TileType.Empty;

                    if (isSolid && emptyRight)
                    {
                        if (runStart < 0) runStart = ty;
                    }
                    else
                    {
                        if (runStart >= 0)
                        {
                            int runLen = ty - runStart;
                            if (runLen >= 3 && rng.NextDouble() < chance)
                            {
                                // Mark a run of 3–8 tiles as climbable
                                int climbLen   = Math.Min(runLen, rng.Next(3, 9));
                                int climbStart = runStart + rng.Next(0, runLen - climbLen + 1);
                                for (int cy = climbStart; cy < climbStart + climbLen; cy++)
                                    map.SetTile(tx, cy, TileType.Climbable);
                            }
                            runStart = -1;
                        }
                    }
                }
            }
        }

        // ── Orphan block removal ───────────────────────────────────────────────

        /// <summary>
        /// Remove floating solid blocks that are isolated from the main terrain mass (A3).
        ///
        /// Pass 1 — 1×1 orphans: solid tile with all 4 cardinal neighbours empty.
        /// Pass 2 — 1×2 vertical orphan pairs: two vertically-adjacent solid tiles
        ///   with no other solid cardinal neighbours outside the pair.
        /// Pass 3 — 2×1 horizontal orphan pairs: same for horizontal pairs.
        /// Pass 4 — Thin peninsulas: solid tiles with ≤1 solid cardinal neighbour
        ///   that protrude into open space and create snag points.
        /// </summary>
        private static void RemoveOrphanBlocks(TileMap map)
        {
            int w = map.Width;
            int h = map.Height;

            // ── Pass 1: 1×1 orphans ───────────────────────────────────────────
            for (int ty = 1; ty < h - 1; ty++)
            {
                for (int tx = 1; tx < w - 1; tx++)
                {
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;

                    bool leftEmpty  = !TileProperties.IsSolid(map.GetTile(tx - 1, ty));
                    bool rightEmpty = !TileProperties.IsSolid(map.GetTile(tx + 1, ty));
                    bool aboveEmpty = !TileProperties.IsSolid(map.GetTile(tx, ty - 1));
                    bool belowEmpty = !TileProperties.IsSolid(map.GetTile(tx, ty + 1));

                    if (leftEmpty && rightEmpty && aboveEmpty && belowEmpty)
                        map.SetTile(tx, ty, TileType.Empty);
                }
            }

            // ── Pass 2: 1×2 vertical orphan pairs ────────────────────────────
            for (int ty = 1; ty < h - 2; ty++)
            {
                for (int tx = 1; tx < w - 1; tx++)
                {
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty + 1))) continue;

                    bool isolated =
                        !TileProperties.IsSolid(map.GetTile(tx - 1, ty))     &&
                        !TileProperties.IsSolid(map.GetTile(tx + 1, ty))     &&
                        !TileProperties.IsSolid(map.GetTile(tx,     ty - 1)) &&
                        !TileProperties.IsSolid(map.GetTile(tx - 1, ty + 1)) &&
                        !TileProperties.IsSolid(map.GetTile(tx + 1, ty + 1)) &&
                        !TileProperties.IsSolid(map.GetTile(tx,     ty + 2));

                    if (isolated)
                    {
                        map.SetTile(tx, ty,     TileType.Empty);
                        map.SetTile(tx, ty + 1, TileType.Empty);
                    }
                }
            }

            // ── Pass 3: 2×1 horizontal orphan pairs ──────────────────────────
            for (int ty = 1; ty < h - 1; ty++)
            {
                for (int tx = 1; tx < w - 2; tx++)
                {
                    if (!TileProperties.IsSolid(map.GetTile(tx,     ty))) continue;
                    if (!TileProperties.IsSolid(map.GetTile(tx + 1, ty))) continue;

                    bool isolated =
                        !TileProperties.IsSolid(map.GetTile(tx - 1, ty))     &&
                        !TileProperties.IsSolid(map.GetTile(tx,     ty - 1)) &&
                        !TileProperties.IsSolid(map.GetTile(tx,     ty + 1)) &&
                        !TileProperties.IsSolid(map.GetTile(tx + 1, ty - 1)) &&
                        !TileProperties.IsSolid(map.GetTile(tx + 1, ty + 1)) &&
                        !TileProperties.IsSolid(map.GetTile(tx + 2, ty));

                    if (isolated)
                    {
                        map.SetTile(tx,     ty, TileType.Empty);
                        map.SetTile(tx + 1, ty, TileType.Empty);
                    }
                }
            }

            // ── Pass 4: Thin peninsulas (≤1 solid cardinal neighbour) ─────────
            // A solid tile with only 1 solid cardinal neighbour is a spike that
            // protrudes into open space and can snag the player hitbox.
            for (int ty = 1; ty < h - 1; ty++)
            {
                for (int tx = 1; tx < w - 1; tx++)
                {
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                    if (tx <= 1 || tx >= w - 2 || ty <= 1 || ty >= h - 2) continue;

                    int solidNeighbours = CountSolidCardinalNeighbours(map, tx, ty, w, h);
                    if (solidNeighbours <= 1)
                        map.SetTile(tx, ty, TileType.Empty);
                }
            }
        }

        /// <summary>
        /// Post-smooth erosion pass (C2): remove solid tiles that have only 1 solid
        /// cardinal neighbour. These are thin spikes left by cellular automata that
        /// serve no structural purpose and create collision snag points.
        /// Run once after all smoothing and passage-width passes.
        /// </summary>
        private static void ErosionPass(TileMap map)
        {
            int w = map.Width;
            int h = map.Height;

            // Collect tiles to erode first, then apply — avoids order-dependent results
            var toErode = new System.Collections.Generic.List<(int tx, int ty)>();

            for (int ty = 1; ty < h - 1; ty++)
            {
                for (int tx = 1; tx < w - 1; tx++)
                {
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                    // Skip border tiles — they must stay solid
                    if (tx <= 1 || tx >= w - 2 || ty <= 1 || ty >= h - 2) continue;

                    int solidNeighbours = CountSolidCardinalNeighbours(map, tx, ty, w, h);
                    if (solidNeighbours <= 1)
                        toErode.Add((tx, ty));
                }
            }

            foreach (var (tx, ty) in toErode)
                map.SetTile(tx, ty, TileType.Empty);
        }

        // ── Shaft escape route guarantee ───────────────────────────────────────

        /// <summary>
        /// Scans every column for vertical empty runs taller than ShaftEscapeMinHeight.
        /// For each such shaft, ensures that within every ShaftEscapeInterval-tile window
        /// there is at least one escape mechanism (climbable wall, ledge, or horizontal exit).
        /// If none exists, injects climbable tiles on the nearest solid wall face.
        ///
        /// This guarantees the player can ALWAYS climb out of any shaft, even if the
        /// grappling hook cannot reach the top.
        /// </summary>
        private static void EnsureShaftEscapeRoutes(TileMap map, Random rng)
        {
            int w = map.Width;
            int h = map.Height;

            for (int tx = 1; tx < w - 1; tx++)
            {
                // Find vertical empty runs in this column
                int runStart = -1;

                for (int ty = 2; ty <= h - 2; ty++)
                {
                    bool isEmpty = map.GetTile(tx, ty) == TileType.Empty;

                    if (isEmpty)
                    {
                        if (runStart < 0) runStart = ty;
                    }
                    else
                    {
                        if (runStart >= 0)
                        {
                            int runLen = ty - runStart;
                            if (runLen >= ShaftEscapeMinHeight)
                                EnsureEscapeInShaft(map, tx, runStart, ty - 1, rng);
                            runStart = -1;
                        }
                    }
                }

                // Flush trailing run
                if (runStart >= 0)
                {
                    int runLen = (h - 2) - runStart;
                    if (runLen >= ShaftEscapeMinHeight)
                        EnsureEscapeInShaft(map, tx, runStart, h - 3, rng);
                }
            }
        }

        /// <summary>
        /// For a detected shaft in column tx from topY to bottomY, scan every
        /// ShaftEscapeInterval-tile window and inject climbable tiles if no escape exists.
        /// </summary>
        private static void EnsureEscapeInShaft(TileMap map, int tx, int topY, int bottomY, Random rng)
        {
            int w = map.Width;

            // Walk down the shaft in intervals
            for (int windowTop = topY; windowTop <= bottomY; windowTop += ShaftEscapeInterval)
            {
                int windowBot = Math.Min(bottomY, windowTop + ShaftEscapeInterval - 1);

                // Check if any escape mechanism exists in this window
                bool hasEscape = false;
                for (int ty = windowTop; ty <= windowBot && !hasEscape; ty++)
                {
                    // Horizontal exit: empty tile to the left or right
                    if (map.GetTile(tx - 1, ty) == TileType.Empty ||
                        map.GetTile(tx + 1, ty) == TileType.Empty)
                    {
                        hasEscape = true;
                        break;
                    }

                    // Climbable wall on either side
                    if (map.GetTile(tx - 1, ty) == TileType.Climbable ||
                        map.GetTile(tx + 1, ty) == TileType.Climbable)
                    {
                        hasEscape = true;
                        break;
                    }

                    // Platform or ledge to stand on
                    var below = map.GetTile(tx, ty + 1);
                    if (TileProperties.IsSolid(below) || below == TileType.Platform)
                    {
                        hasEscape = true;
                        break;
                    }
                }

                if (hasEscape) continue;

                // No escape found — inject climbable tiles on the nearest solid wall
                // Prefer left wall, fall back to right wall
                int midY = (windowTop + windowBot) / 2;
                int climbLen = Math.Min(ShaftEscapeInterval, windowBot - windowTop + 1);

                bool injected = false;

                // Try left wall (tx-1 must be solid)
                if (tx - 1 >= 1 && TileProperties.IsSolid(map.GetTile(tx - 1, midY)))
                {
                    for (int cy = windowTop; cy <= windowBot; cy++)
                    {
                        if (TileProperties.IsSolid(map.GetTile(tx - 1, cy)))
                            map.SetTile(tx - 1, cy, TileType.Climbable);
                    }
                    injected = true;
                }

                // Try right wall (tx+1 must be solid)
                if (!injected && tx + 1 < w - 1 && TileProperties.IsSolid(map.GetTile(tx + 1, midY)))
                {
                    for (int cy = windowTop; cy <= windowBot; cy++)
                    {
                        if (TileProperties.IsSolid(map.GetTile(tx + 1, cy)))
                            map.SetTile(tx + 1, cy, TileType.Climbable);
                    }
                    injected = true;
                }

                // Last resort: carve a small 2-tile ledge into the nearest wall
                if (!injected)
                {
                    int ledgeY = midY;
                    // Try to carve a ledge to the left
                    if (tx - 2 >= 1)
                    {
                        map.SetTile(tx - 1, ledgeY, TileType.Empty);
                        map.SetTile(tx - 2, ledgeY, TileType.Empty);
                    }
                    else if (tx + 2 < w - 1)
                    {
                        map.SetTile(tx + 1, ledgeY, TileType.Empty);
                        map.SetTile(tx + 2, ledgeY, TileType.Empty);
                    }
                }
            }
        }

        // ── Entry / exit finding ───────────────────────────────────────────────

        private static (int tx, int ty) FindEntryPoint(TileMap map)
        {
            int w = map.Width;
            int h = map.Height;

            // Search in the center third of the map width, from top down
            int searchLeft  = w / 3;
            int searchRight = 2 * w / 3;

            for (int ty = 2; ty < h / 4; ty++)
            {
                for (int tx = searchLeft; tx < searchRight; tx++)
                {
                    if (IsOpenArea(map, tx, ty))
                        return (tx, ty);
                }
            }

            // Fallback: find any open tile near the top
            for (int ty = 2; ty < h / 3; ty++)
                for (int tx = 2; tx < w - 2; tx++)
                    if (map.GetTile(tx, ty) == TileType.Empty)
                        return (tx, ty);

            return (-1, -1); // failed
        }

        private static (int tx, int ty) FindExitPoint(TileMap map)
        {
            int w = map.Width;
            int h = map.Height;

            // Search in the center third of the map width, from bottom up
            int searchLeft  = w / 3;
            int searchRight = 2 * w / 3;

            for (int ty = h - 3; ty > 3 * h / 4; ty--)
            {
                for (int tx = searchLeft; tx < searchRight; tx++)
                {
                    if (IsOpenArea(map, tx, ty))
                        return (tx, ty);
                }
            }

            // Fallback: find any open tile near the bottom
            for (int ty = h - 3; ty > 2 * h / 3; ty--)
                for (int tx = 2; tx < w - 2; tx++)
                    if (map.GetTile(tx, ty) == TileType.Empty)
                        return (tx, ty);

            return (-1, -1); // failed
        }

        /// <summary>
        /// Returns true if the tile at (tx, ty) is empty and has at least
        /// a 3×3 open area around it (C3 — enough room for the player to spawn/exit
        /// without immediately being adjacent to a wall).
        /// Requires: the tile itself, the tile above, and the tile two above are all
        /// empty (2-tile headroom + 1 extra), and the tile below is solid/platform.
        /// Also checks the immediate left and right neighbours are not solid.
        /// </summary>
        private static bool IsOpenArea(TileMap map, int tx, int ty)
        {
            if (map.GetTile(tx, ty) != TileType.Empty) return false;
            if (map.GetTile(tx, ty - 1) != TileType.Empty) return false; // need headroom
            if (map.GetTile(tx, ty - 2) != TileType.Empty) return false; // extra clearance (C3)
            // Need solid ground below (or platform)
            var below = map.GetTile(tx, ty + 1);
            if (!(TileProperties.IsSolid(below) || below == TileType.Platform)) return false;
            // Need horizontal clearance — at least one side open (C3)
            bool leftClear  = !TileProperties.IsSolid(map.GetTile(tx - 1, ty));
            bool rightClear = !TileProperties.IsSolid(map.GetTile(tx + 1, ty));
            return leftClear || rightClear;
        }

        // ── Fallback level ─────────────────────────────────────────────────────

        /// <summary>
        /// Emergency fallback: generate a simple open level with guaranteed paths.
        /// Used only if all retries fail (should be extremely rare).
        /// </summary>
        private static GenerationResult GenerateFallback(int depth, int seed)
        {
            var map = new TileMap(LevelWidth, LevelHeight);
            int w   = LevelWidth;
            int h   = LevelHeight;

            // Solid borders
            for (int ty = 0; ty < h; ty++)
            {
                map.SetTile(0,     ty, TileType.Solid);
                map.SetTile(w - 1, ty, TileType.Solid);
            }
            for (int tx = 0; tx < w; tx++)
            {
                map.SetTile(tx, 0,     TileType.Solid);
                map.SetTile(tx, 1,     TileType.Solid);
                map.SetTile(tx, h - 1, TileType.Solid);
                map.SetTile(tx, h - 2, TileType.Solid);
            }

            // Open interior with scattered platforms
            var rng = new Random(seed + depth);
            for (int ty = 5; ty < h - 5; ty += 8)
            {
                int platStart = rng.Next(5, w / 2);
                int platEnd   = rng.Next(w / 2, w - 5);
                for (int tx = platStart; tx < platEnd; tx++)
                    map.SetTile(tx, ty, TileType.Platform);
            }

            Vector2 entry = TileMap.TileCenter(w / 2, 3);
            Vector2 exit  = TileMap.TileCenter(w / 2, h - 4);

            return new GenerationResult
            {
                TileMap          = map,
                EntryPoint       = entry,
                ExitPoint        = exit,
                ObjectPlacements = new List<ObjectPlacement>(),
            };
        }
    }
}
