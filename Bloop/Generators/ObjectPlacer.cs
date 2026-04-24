using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Bloop.World;

namespace Bloop.Generators
{
    /// <summary>
    /// Enumerates all placeable world object types.
    /// Used by ObjectPlacer to record what to spawn, and by Level to instantiate them.
    /// </summary>
    public enum ObjectType
    {
        FallingStalactite,
        DisappearingPlatform,
        StunDamageObject,
        GlowVine,
        RootClump,
        VentFlower,
        CaveLichen,
        BlindFish,
        ResonanceShard,
        IonStone,
        PhosphorMoss,
        CrystalCluster,
        CrystalBridge,
        // ── Controllable entities ──────────────────────────────────────────────
        EchoBat,
        SilkWeaverSpider,
        ChainCentipede,
        LuminescentGlowworm,
        DeepBurrowWorm,
        BlindCaveSalamander,
        LuminousIsopod,
    }

    /// <summary>
    /// Rarity tiers for collectible items (CaveLichen, BlindFish, ResonanceShard).
    /// Common items are placed on main paths; Rare items are in remote dead-end alcoves.
    /// </summary>
    public enum ItemRarity
    {
        Common,
        Uncommon,
        Rare,
    }

    /// <summary>
    /// A single object placement record produced by ObjectPlacer.
    /// Level.cs reads these to instantiate the concrete WorldObject subclasses.
    /// </summary>
    public class ObjectPlacement
    {
        /// <summary>Type of object to spawn.</summary>
        public ObjectType Type { get; set; }

        /// <summary>Center position in pixel space.</summary>
        public Vector2 PixelPosition { get; set; }

        /// <summary>
        /// Chain ID for domino-linked disappearing platforms.
        /// -1 means standalone (no chain).
        /// </summary>
        public int ChainId { get; set; } = -1;

        /// <summary>
        /// Order within the domino chain (0 = first to trigger).
        /// -1 if not chained.
        /// </summary>
        public int ChainOrder { get; set; } = -1;

        /// <summary>
        /// Seed-derived poison flag for CaveLichen and BlindFish.
        /// True = 30% chance this item is poisonous.
        /// </summary>
        public bool IsPoisonous { get; set; } = false;

        /// <summary>
        /// Rarity tier for collectible items. Rare items are placed in remote
        /// dead-end alcoves and receive stronger visual tells.
        /// </summary>
        public ItemRarity Rarity { get; set; } = ItemRarity.Common;

        /// <summary>
        /// Width in tiles (for multi-tile objects like GlowVine, RootClump).
        /// 1 for single-tile objects.
        /// </summary>
        public int TileHeight { get; set; } = 1;

        /// <summary>
        /// Generic integer variant, currently used by CrystalCluster to pick color
        /// (0=Cyan, 1=Violet, 2=Red). Default 0.
        /// </summary>
        public int Variant { get; set; } = 0;

        /// <summary>
        /// Normalized growth direction for CrystalBridge objects.
        /// Ignored for all other types.
        /// </summary>
        public Vector2 GrowDirection { get; set; } = Vector2.UnitX;

        /// <summary>
        /// Number of crystal bridge segments (3–6).
        /// Ignored for all other types.
        /// </summary>
        public int SegmentCount { get; set; } = 4;
    }

    /// <summary>
    /// Controls which object types and entities are available at a given level (1–30).
    /// Early levels have low variety; later levels unlock more hazardous and exotic content.
    /// </summary>
    public readonly struct DifficultyProfile
    {
        public int Level { get; init; }

        // ── Object unlock flags ────────────────────────────────────────────────
        public bool HasRootClump        { get; init; }
        public bool HasStunDamage       { get; init; }
        public bool HasCaveLichen       { get; init; }
        public bool HasBlindFish        { get; init; }
        public bool HasFallingStalactite{ get; init; }
        public bool HasCrystalCluster   { get; init; }
        public bool HasIonStone         { get; init; }
        public bool HasCrystalBridge    { get; init; }

        // ── Entity unlock flags ────────────────────────────────────────────────
        public bool HasEchoBat              { get; init; }
        public bool HasSilkWeaverSpider     { get; init; }
        public bool HasChainCentipede       { get; init; }
        public bool HasDeepBurrowWorm       { get; init; }
        public bool HasBlindCaveSalamander  { get; init; }

        /// <summary>
        /// Overall density multiplier: 0.5 at level 1, 1.0 at level 15, 1.5 at level 30.
        /// Applied to all placement chances.
        /// </summary>
        public float DensityScale { get; init; }

        /// <summary>Build a DifficultyProfile for the given level (1–30).</summary>
        public static DifficultyProfile ForLevel(int level)
        {
            level = Math.Clamp(level, 1, 30);
            // Linear density ramp: 0.5 → 1.5 over levels 1–30
            float density = 0.5f + (level - 1) / 29f;

            return new DifficultyProfile
            {
                Level = level,
                DensityScale = density,

                // Objects unlocked progressively
                HasRootClump         = level >= 4,
                HasStunDamage        = level >= 7,
                HasCaveLichen        = level >= 4,
                HasBlindFish         = level >= 4,
                HasFallingStalactite = level >= 7,
                HasCrystalCluster    = level >= 7,
                HasIonStone          = level >= 11,
                HasCrystalBridge     = level >= 11,

                // Entities unlocked progressively
                HasEchoBat             = level >= 4,
                HasSilkWeaverSpider    = level >= 7,
                HasChainCentipede      = level >= 11,
                HasDeepBurrowWorm      = level >= 16,
                HasBlindCaveSalamander = level >= 4,
            };
        }
    }

    /// <summary>
    /// Context-aware world object placer.
    /// Uses CavityAnalyzer topology data to place objects in ecologically and
    /// gameplay-logically appropriate locations.
    ///
    /// Placement philosophy:
    ///   - DisappearingPlatforms: bridge gaps AND form shaft-crossing staircases
    ///   - StunDamageObjects:     floor spikes, ceiling stalactites, wall hazards
    ///                            clustered near narrow passages and shaft entrances
    ///   - GlowVines:             on both wall faces AND hanging from ceilings
    ///   - RootClumps:            on both wall faces AND hanging from ceilings
    ///   - VentFlowers:           prefer dead-end alcoves and shaft bottoms
    ///   - CaveLichen:            cluster in large caverns, ecological groupings
    ///   - BlindFish:             prefer wide floor areas and shaft bottoms
    ///
    /// Minimum spacing of 4 tiles between objects of the same type is enforced.
    /// </summary>
    public static class ObjectPlacer
    {
        // ── Placement density constants ────────────────────────────────────────
        // Base chance per eligible tile (before depth scaling)
        private const float ChanceDisappearingPlatform = 1f / 15f;
        private const float ChanceStunDamage           = 1f / 25f;
        private const float ChanceGlowVine             = 1f / 30f;
        private const float ChanceRootClump            = 1f / 35f;
        private const float ChanceVentFlower           = 1f / 40f;
        private const float ChanceCaveLichen           = 1f / 20f;
        private const float ChanceBlindFish            = 1f / 30f;

        // Depth scaling per level (additive fraction)
        private const float DepthScaleDisappearing = 0.05f;
        private const float DepthScaleStun         = 0.08f;
        private const float DepthScaleGlowVine     = 0.03f;
        private const float DepthScaleRootClump    = 0.04f;
        private const float DepthScaleLichen       = 0.06f;
        private const float DepthScaleFish         = 0.04f;

        // Minimum tile spacing between objects of the same type
        private const int MinSpacing = 4;

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Scan the tile map and return a list of object placements.
        /// Uses CavityAnalyzer for topology-aware placement decisions.
        /// seed and depth are used to derive the placement RNG.
        /// biome provides per-biome density multipliers.
        /// difficulty gates which object/entity types are available at this level.
        /// </summary>
        public static List<ObjectPlacement> PlaceObjects(TileMap map, int seed, int depth,
            BiomeProfile biome = default, DifficultyProfile difficulty = default)
        {
            // Build difficulty profile from depth if caller didn't supply one
            if (difficulty.Level == 0)
                difficulty = DifficultyProfile.ForLevel(depth);

            // Use a different prime multiplier than LevelGenerator to avoid correlation
            var rng        = new Random(seed + depth * 3571);
            var placements = new List<ObjectPlacement>();
            var analyzer   = new CavityAnalyzer(map);

            int w = map.Width;
            int h = map.Height;

            // Depth multiplier for density scaling (clamped to reasonable range)
            // Combined with difficulty density scale for progressive variety
            float depthMult = Math.Min(depth - 1, 20) * difficulty.DensityScale;

            // Biome density multipliers (default 1.0 if no biome provided)
            float biomePlatform   = biome.DensityPlatform   > 0f ? biome.DensityPlatform   : 1f;
            float biomeStun       = biome.DensityStun       > 0f ? biome.DensityStun       : 1f;
            float biomeGlowVine   = biome.DensityGlowVine   > 0f ? biome.DensityGlowVine   : 1f;
            float biomeRootClump  = biome.DensityRootClump  > 0f ? biome.DensityRootClump  : 1f;
            float biomeVentFlower = biome.DensityVentFlower > 0f ? biome.DensityVentFlower : 1f;
            float biomeLichen     = biome.DensityLichen     > 0f ? biome.DensityLichen     : 1f;

            // ── Disappearing platforms (always available — core traversal mechanic) ──
            PlaceDisappearingPlatforms(map, analyzer, rng, placements, w, h, depthMult,
                biomePlatform * difficulty.DensityScale);

            // ── Stun/damage objects (unlocked at level 7) ─────────────────────
            if (difficulty.HasStunDamage)
                PlaceStunObjects(map, analyzer, rng, placements, w, h, depthMult, biomeStun);

            // ── Glow vines (always available — ambient decoration) ────────────
            PlaceGlowVines(map, analyzer, rng, placements, w, h, depthMult, biomeGlowVine);

            // ── Root clumps (unlocked at level 4) ────────────────────────────
            if (difficulty.HasRootClump)
                PlaceRootClumps(map, analyzer, rng, placements, w, h, depthMult, biomeRootClump);

            // ── Vent flowers (always available — ambient decoration) ──────────
            PlaceVentFlowers(map, analyzer, rng, placements, w, h,
                biomeVentFlower * difficulty.DensityScale);

            // ── Cave lichen (unlocked at level 4) ─────────────────────────────
            if (difficulty.HasCaveLichen)
                PlaceCaveLichen(map, analyzer, rng, placements, w, h, depthMult, seed, biomeLichen);

            // ── Blind fish (unlocked at level 4) ──────────────────────────────
            if (difficulty.HasBlindFish)
                PlaceBlindFish(map, analyzer, rng, placements, w, h, depthMult, seed);

            // ── Falling stalactites (unlocked at level 7) ─────────────────────
            if (difficulty.HasFallingStalactite)
                PlaceFallingStalactites(map, analyzer, rng, placements, w, h, biomeStun);

            // ── Resonance shards (always available — required for exit) ────────
            PlaceResonanceShards(map, analyzer, rng, placements, w, h, seed);

            // ── Crystal bridges (unlocked at level 11) ────────────────────────
            if (difficulty.HasCrystalBridge)
                PlaceCrystalBridges(map, analyzer, rng, placements, w, h, seed, difficulty);

            // ── Glowing atmosphere objects (biome-specific, always available) ──
            PlaceGlowObjects(map, analyzer, rng, placements, w, h, biome.Tier);

            // ── Controllable entities (gated by difficulty flags) ──────────────
            PlaceControllableEntities(map, analyzer, rng, placements, w, h, depth, difficulty);

            return placements;
        }

        /// <summary>
        /// Post-placement passability validation pass.
        ///
        /// Builds a "blocked tile" overlay from the bounding boxes of wall-attached
        /// objects (GlowVine, RootClump, StunDamageObject) and re-runs a BFS from
        /// entry to exit. If the path is broken, objects are removed one by one
        /// (least important type first) until passability is restored.
        ///
        /// Also enforces a minimum clearance rule: no wall-attached object may occupy
        /// a tile in a passage that is only 2 tiles wide (would reduce it to 1).
        /// </summary>
        public static void ValidatePassability(
            TileMap map,
            List<ObjectPlacement> placements,
            Microsoft.Xna.Framework.Vector2 entryPixel,
            Microsoft.Xna.Framework.Vector2 exitPixel)
        {
            int entryTx = (int)(entryPixel.X / TileMap.TileSize);
            int entryTy = (int)(entryPixel.Y / TileMap.TileSize);
            int exitTx  = (int)(exitPixel.X  / TileMap.TileSize);
            int exitTy  = (int)(exitPixel.Y  / TileMap.TileSize);

            // ── Phase 1: Minimum clearance pre-filter ─────────────────────────
            // Remove any wall-attached object whose tile footprint sits in a passage
            // that is only 3 tiles wide (the object would reduce clearance below the
            // 3-tile minimum required for the 1.875-tile-tall player).
            placements.RemoveAll(p =>
            {
                if (!IsWallAttached(p.Type)) return false;

                int tx = (int)(p.PixelPosition.X / TileMap.TileSize);
                int ty = (int)(p.PixelPosition.Y / TileMap.TileSize);

                // Measure horizontal passage width at this tile
                int hWidth = MeasurePassageWidth(map, tx, ty, horizontal: true);
                // Measure vertical passage width at this tile
                int vWidth = MeasurePassageWidth(map, tx, ty, horizontal: false);

                // If the narrowest dimension is 3 or less, placing this object would
                // reduce clearance below the 3-tile minimum — reject it.
                return hWidth <= 3 || vWidth <= 3;
            });

            // ── Phase 2: BFS with object footprints as blocked tiles ──────────
            // Priority order for removal (least important first)
            var removalOrder = new[]
            {
                ObjectType.StunDamageObject,
                ObjectType.RootClump,
                ObjectType.GlowVine,
                ObjectType.VentFlower,
                ObjectType.CaveLichen,
                ObjectType.BlindFish,
                ObjectType.DisappearingPlatform,
                ObjectType.ResonanceShard,
            };

            // Iteratively remove objects until path is clear (or nothing left to remove)
            foreach (var typeToRemove in removalOrder)
            {
                var blocked = BuildBlockedTileSet(map, placements);
                var path = PathValidator.BFS(map, entryTx, entryTy, exitTx, exitTy, blocked);
                if (path != null) break; // path is clear — done

                // Remove all objects of this type that overlap the blocked set
                placements.RemoveAll(p => p.Type == typeToRemove && IsWallAttached(p.Type));
            }

            // Final check: if still no path, remove ALL wall-attached objects
            {
                var blocked = BuildBlockedTileSet(map, placements);
                var path = PathValidator.BFS(map, entryTx, entryTy, exitTx, exitTy, blocked);
                if (path == null)
                    placements.RemoveAll(p => IsWallAttached(p.Type));
            }
        }

        // ── Passability helpers ────────────────────────────────────────────────

        /// <summary>
        /// Returns true for object types that are physically attached to walls/ceilings
        /// and can potentially block narrow passages.
        /// </summary>
        private static bool IsWallAttached(ObjectType type) =>
            type == ObjectType.GlowVine
            || type == ObjectType.RootClump
            || type == ObjectType.StunDamageObject;

        /// <summary>
        /// Build a set of tile coordinates that are "blocked" by the footprints of
        /// wall-attached objects. Each object's pixel position is converted to a tile
        /// coordinate; objects with TileHeight > 1 block multiple tiles vertically.
        /// </summary>
        private static HashSet<(int, int)> BuildBlockedTileSet(
            TileMap map, List<ObjectPlacement> placements)
        {
            var blocked = new HashSet<(int, int)>();

            foreach (var p in placements)
            {
                if (!IsWallAttached(p.Type)) continue;

                int cx = (int)(p.PixelPosition.X / TileMap.TileSize);
                int cy = (int)(p.PixelPosition.Y / TileMap.TileSize);

                // Only block if the tile is actually passable (i.e. in open space)
                for (int dy = 0; dy < Math.Max(1, p.TileHeight); dy++)
                {
                    int ty = cy + dy - p.TileHeight / 2;
                    if (ty < 0 || ty >= map.Height) continue;

                    var tile = map.GetTile(cx, ty);
                    if (tile == TileType.Empty
                        || tile == TileType.Platform
                        || tile == TileType.SlopeLeft
                        || tile == TileType.SlopeRight
                        || tile == TileType.Climbable)
                    {
                        blocked.Add((cx, ty));
                    }
                }
            }

            return blocked;
        }

        /// <summary>
        /// Measures the width of the open passage at tile (tx, ty) in the given direction.
        /// horizontal=true measures left-right; horizontal=false measures up-down.
        /// Returns the count of consecutive passable tiles including (tx, ty).
        /// </summary>
        private static int MeasurePassageWidth(TileMap map, int tx, int ty, bool horizontal)
        {
            int count = 0;
            int dx = horizontal ? 1 : 0;
            int dy = horizontal ? 0 : 1;

            // Count in positive direction
            int x = tx, y = ty;
            while (x >= 0 && x < map.Width && y >= 0 && y < map.Height)
            {
                var tile = map.GetTile(x, y);
                if (tile != TileType.Empty && tile != TileType.Platform
                    && tile != TileType.SlopeLeft && tile != TileType.SlopeRight
                    && tile != TileType.Climbable)
                    break;
                count++;
                x += dx;
                y += dy;
            }

            // Count in negative direction (excluding origin already counted)
            x = tx - dx;
            y = ty - dy;
            while (x >= 0 && x < map.Width && y >= 0 && y < map.Height)
            {
                var tile = map.GetTile(x, y);
                if (tile != TileType.Empty && tile != TileType.Platform
                    && tile != TileType.SlopeLeft && tile != TileType.SlopeRight
                    && tile != TileType.Climbable)
                    break;
                count++;
                x -= dx;
                y -= dy;
            }

            return count;
        }

        // ── Private placement methods ──────────────────────────────────────────

        private static void PlaceDisappearingPlatforms(
            TileMap map, CavityAnalyzer analyzer, Random rng,
            List<ObjectPlacement> placements, int w, int h, float depthMult, float biomeMult = 1f)
        {
            float chance = (ChanceDisappearingPlatform + depthMult * DepthScaleDisappearing * ChanceDisappearingPlatform) * biomeMult;
            var lastPlaced = new Dictionary<int, int>(); // row → last placed column

            // ── A: Bridge horizontal gaps (existing logic, enhanced) ──────────
            for (int ty = 2; ty < h - 4; ty++)
            {
                int gapStart = -1;
                for (int tx = 2; tx < w - 2; tx++)
                {
                    bool isEmpty    = map.GetTile(tx, ty) == TileType.Empty;
                    bool solidAbove = TileProperties.IsSolid(map.GetTile(tx, ty - 1));
                    bool emptyBelow = map.GetTile(tx, ty + 1) == TileType.Empty ||
                                     map.GetTile(tx, ty + 1) == TileType.Platform;

                    if (isEmpty && solidAbove && emptyBelow)
                    {
                        if (gapStart < 0) gapStart = tx;
                    }
                    else
                    {
                        if (gapStart >= 0)
                        {
                            int gapWidth = tx - gapStart;
                            if (gapWidth >= 3 && gapWidth <= 8)
                            {
                                int lastCol = lastPlaced.TryGetValue(ty, out int lc) ? lc : -999;
                                if (gapStart - lastCol >= MinSpacing && rng.NextDouble() < chance)
                                {
                                    int midX = gapStart + gapWidth / 2;
                                    placements.Add(new ObjectPlacement
                                    {
                                        Type          = ObjectType.DisappearingPlatform,
                                        PixelPosition = TileMap.TileCenter(midX, ty),
                                    });
                                    lastPlaced[ty] = midX;
                                }
                            }
                            gapStart = -1;
                        }
                    }
                }
            }

            // ── B: Shaft-crossing staircase chains ────────────────────────────
            // Detect wide vertical shafts (3+ consecutive empty columns) and place
            // diagonal chains of disappearing platforms as temporary staircases.
            PlaceShaftStaircases(map, analyzer, rng, placements, w, h, chance);
        }

        private static void PlaceShaftStaircases(
            TileMap map, CavityAnalyzer analyzer, Random rng,
            List<ObjectPlacement> placements, int w, int h, float chance)
        {
            // Scan for wide vertical shafts: columns where 3+ consecutive tiles are empty
            // and the shaft is at least 8 tiles tall
            for (int tx = 3; tx < w - 6; tx++)
            {
                // Check if this column is part of a wide shaft
                int shaftWidth = 0;
                for (int dx = 0; dx < 5; dx++)
                {
                    int emptyCount = 0;
                    for (int ty = 5; ty < h - 5; ty++)
                        if (map.GetTile(tx + dx, ty) == TileType.Empty) emptyCount++;
                    if (emptyCount > h / 3) shaftWidth++;
                    else break;
                }

                if (shaftWidth < 3) continue;

                // Found a wide shaft — place a diagonal staircase chain
                if (rng.NextDouble() > chance * 2f) continue;

                int startY = rng.Next(h / 5, 2 * h / 5);
                int endY   = Math.Min(h - 5, startY + rng.Next(8, 20));
                int dir    = rng.Next(2) == 0 ? 1 : -1; // staircase direction

                int chainId    = placements.Count; // use current count as unique chain ID
                int chainOrder = 0;
                int curX       = tx + shaftWidth / 2;

                for (int ty = startY; ty < endY; ty += 2)
                {
                    curX = Math.Clamp(curX + dir, tx + 1, tx + shaftWidth - 2);

                    // Only place if this is an empty tile
                    if (map.GetTile(curX, ty) != TileType.Empty) continue;

                    placements.Add(new ObjectPlacement
                    {
                        Type          = ObjectType.DisappearingPlatform,
                        PixelPosition = TileMap.TileCenter(curX, ty),
                        ChainId       = chainId,
                        ChainOrder    = chainOrder++,
                    });
                }

                tx += shaftWidth; // skip past this shaft
            }
        }

        private static void PlaceStunObjects(
            TileMap map, CavityAnalyzer analyzer, Random rng,
            List<ObjectPlacement> placements, int w, int h, float depthMult, float biomeMult = 1f)
        {
            float chance = (ChanceStunDamage + depthMult * DepthScaleStun * ChanceStunDamage) * biomeMult;
            var lastPlacedRow = new Dictionary<int, int>();
            var lastPlacedCol = new Dictionary<int, int>();

            for (int ty = 1; ty < h - 1; ty++)
            {
                for (int tx = 1; tx < w - 1; tx++)
                {
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;

                    var surface = analyzer.SurfaceFlags[tx, ty];
                    if (surface == CavityAnalyzer.SurfaceType.None) continue;

                    // Spacing check
                    int lastRow = lastPlacedRow.TryGetValue(tx, out int lr) ? lr : -999;
                    int lastCol = lastPlacedCol.TryGetValue(ty, out int lc) ? lc : -999;
                    if (ty - lastRow < MinSpacing || tx - lastCol < MinSpacing) continue;

                    // Boost chance near narrow passages (hazard gauntlet design)
                    float localChance = chance;
                    if (analyzer.IsNarrowPassage[tx, ty]) localChance *= 1.8f;

                    if (rng.NextDouble() >= localChance) continue;

                    Vector2 pos;

                    // ── Floor spikes: on floor surface (solid with empty above) ──
                    if ((surface & CavityAnalyzer.SurfaceType.Floor) != 0)
                    {
                        // Place spike just above the solid tile (on the floor surface)
                        pos = new Vector2(
                            tx * TileMap.TileSize + TileMap.TileSize / 2f,
                            ty * TileMap.TileSize - 12f);
                    }
                    // ── Ceiling stalactites: on ceiling surface (solid with empty below) ──
                    else if ((surface & CavityAnalyzer.SurfaceType.Ceiling) != 0)
                    {
                        pos = new Vector2(
                            tx * TileMap.TileSize + TileMap.TileSize / 2f,
                            (ty + 1) * TileMap.TileSize + 12f);
                    }
                    // ── Wall hazards: on wall faces ──
                    else if ((surface & CavityAnalyzer.SurfaceType.WallLeft) != 0)
                    {
                        pos = new Vector2(
                            tx * TileMap.TileSize - 12f,
                            ty * TileMap.TileSize + TileMap.TileSize / 2f);
                    }
                    else // WallRight
                    {
                        pos = new Vector2(
                            (tx + 1) * TileMap.TileSize + 12f,
                            ty * TileMap.TileSize + TileMap.TileSize / 2f);
                    }

                    placements.Add(new ObjectPlacement
                    {
                        Type          = ObjectType.StunDamageObject,
                        PixelPosition = pos,
                    });
                    lastPlacedRow[tx] = ty;
                    lastPlacedCol[ty] = tx;
                }
            }
        }

        private static void PlaceGlowVines(
            TileMap map, CavityAnalyzer analyzer, Random rng,
            List<ObjectPlacement> placements, int w, int h, float depthMult, float biomeMult = 1f)
        {
            float chance = (ChanceGlowVine + depthMult * DepthScaleGlowVine * ChanceGlowVine) * biomeMult;

            // ── A: Wall-face vines (right-facing walls) ───────────────────────
            for (int tx = 1; tx < w - 1; tx++)
            {
                int runStart = -1;
                for (int ty = 1; ty < h - 1; ty++)
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
                            if (runLen >= 2 && runLen <= 8 && rng.NextDouble() < chance)
                            {
                                int vineHeight  = Math.Clamp(rng.Next(2, 6), 2, runLen);
                                int vineStartTy = runStart + rng.Next(0, runLen - vineHeight + 1);
                                int midTy       = vineStartTy + vineHeight / 2;

                                placements.Add(new ObjectPlacement
                                {
                                    Type          = ObjectType.GlowVine,
                                    PixelPosition = new Vector2(
                                        (tx + 1) * TileMap.TileSize + TileMap.TileSize / 2f,
                                        midTy * TileMap.TileSize + TileMap.TileSize / 2f),
                                    TileHeight    = vineHeight,
                                });
                            }
                            runStart = -1;
                        }
                    }
                }
            }

            // ── B: Wall-face vines (left-facing walls) ────────────────────────
            for (int tx = 1; tx < w - 1; tx++)
            {
                int runStart = -1;
                for (int ty = 1; ty < h - 1; ty++)
                {
                    bool isSolid   = TileProperties.IsSolid(map.GetTile(tx, ty));
                    bool emptyLeft = map.GetTile(tx - 1, ty) == TileType.Empty;

                    if (isSolid && emptyLeft)
                    {
                        if (runStart < 0) runStart = ty;
                    }
                    else
                    {
                        if (runStart >= 0)
                        {
                            int runLen = ty - runStart;
                            if (runLen >= 2 && runLen <= 8 && rng.NextDouble() < chance * 0.7f)
                            {
                                int vineHeight  = Math.Clamp(rng.Next(2, 6), 2, runLen);
                                int vineStartTy = runStart + rng.Next(0, runLen - vineHeight + 1);
                                int midTy       = vineStartTy + vineHeight / 2;

                                placements.Add(new ObjectPlacement
                                {
                                    Type          = ObjectType.GlowVine,
                                    PixelPosition = new Vector2(
                                        tx * TileMap.TileSize - TileMap.TileSize / 2f,
                                        midTy * TileMap.TileSize + TileMap.TileSize / 2f),
                                    TileHeight    = vineHeight,
                                });
                            }
                            runStart = -1;
                        }
                    }
                }
            }

            // ── C: Ceiling-hanging vines (solid with empty below) ─────────────
            // These hang downward and serve as vertical traversal aids in shafts
            for (int ty = 1; ty < h - 1; ty++)
            {
                int runStart = -1;
                for (int tx = 1; tx < w - 1; tx++)
                {
                    bool isSolid    = TileProperties.IsSolid(map.GetTile(tx, ty));
                    bool emptyBelow = map.GetTile(tx, ty + 1) == TileType.Empty;

                    if (isSolid && emptyBelow)
                    {
                        if (runStart < 0) runStart = tx;
                    }
                    else
                    {
                        if (runStart >= 0)
                        {
                            int runLen = tx - runStart;
                            if (runLen >= 2 && runLen <= 6 && rng.NextDouble() < chance * 0.5f)
                            {
                                // Place in the middle of the ceiling run
                                int midTx      = runStart + runLen / 2;
                                int vineHeight = rng.Next(2, 5); // hangs down this many tiles

                                placements.Add(new ObjectPlacement
                                {
                                    Type          = ObjectType.GlowVine,
                                    PixelPosition = new Vector2(
                                        midTx * TileMap.TileSize + TileMap.TileSize / 2f,
                                        (ty + 1) * TileMap.TileSize + TileMap.TileSize / 2f),
                                    TileHeight    = vineHeight,
                                });
                            }
                            runStart = -1;
                        }
                    }
                }
            }
        }

        private static void PlaceRootClumps(
            TileMap map, CavityAnalyzer analyzer, Random rng,
            List<ObjectPlacement> placements, int w, int h, float depthMult, float biomeMult = 1f)
        {
            float chance = (ChanceRootClump + depthMult * DepthScaleRootClump * ChanceRootClump) * biomeMult;

            // ── A: Left-facing wall roots ─────────────────────────────────────
            for (int tx = 1; tx < w - 1; tx++)
            {
                int runStart = -1;
                for (int ty = 1; ty < h - 1; ty++)
                {
                    bool isSolid   = TileProperties.IsSolid(map.GetTile(tx, ty));
                    bool emptyLeft = map.GetTile(tx - 1, ty) == TileType.Empty;

                    if (isSolid && emptyLeft)
                    {
                        if (runStart < 0) runStart = ty;
                    }
                    else
                    {
                        if (runStart >= 0)
                        {
                            int runLen = ty - runStart;
                            if (runLen >= 3 && runLen <= 10 && rng.NextDouble() < chance)
                            {
                                int clumpHeight  = Math.Clamp(rng.Next(3, 7), 3, runLen);
                                int clumpStartTy = runStart + rng.Next(0, runLen - clumpHeight + 1);
                                int midTy        = clumpStartTy + clumpHeight / 2;

                                placements.Add(new ObjectPlacement
                                {
                                    Type          = ObjectType.RootClump,
                                    PixelPosition = new Vector2(
                                        tx * TileMap.TileSize - TileMap.TileSize / 2f,
                                        midTy * TileMap.TileSize + TileMap.TileSize / 2f),
                                    TileHeight    = clumpHeight,
                                });
                            }
                            runStart = -1;
                        }
                    }
                }
            }

            // ── B: Right-facing wall roots ────────────────────────────────────
            for (int tx = 1; tx < w - 1; tx++)
            {
                int runStart = -1;
                for (int ty = 1; ty < h - 1; ty++)
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
                            if (runLen >= 3 && runLen <= 10 && rng.NextDouble() < chance * 0.7f)
                            {
                                int clumpHeight  = Math.Clamp(rng.Next(3, 7), 3, runLen);
                                int clumpStartTy = runStart + rng.Next(0, runLen - clumpHeight + 1);
                                int midTy        = clumpStartTy + clumpHeight / 2;

                                placements.Add(new ObjectPlacement
                                {
                                    Type          = ObjectType.RootClump,
                                    PixelPosition = new Vector2(
                                        (tx + 1) * TileMap.TileSize + TileMap.TileSize / 2f,
                                        midTy * TileMap.TileSize + TileMap.TileSize / 2f),
                                    TileHeight    = clumpHeight,
                                });
                            }
                            runStart = -1;
                        }
                    }
                }
            }

            // ── C: Ceiling-hanging roots (for vertical shaft descent chains) ──
            for (int ty = 1; ty < h - 1; ty++)
            {
                int runStart = -1;
                for (int tx = 1; tx < w - 1; tx++)
                {
                    bool isSolid    = TileProperties.IsSolid(map.GetTile(tx, ty));
                    bool emptyBelow = map.GetTile(tx, ty + 1) == TileType.Empty;

                    if (isSolid && emptyBelow)
                    {
                        if (runStart < 0) runStart = tx;
                    }
                    else
                    {
                        if (runStart >= 0)
                        {
                            int runLen = tx - runStart;
                            if (runLen >= 2 && runLen <= 5 && rng.NextDouble() < chance * 0.4f)
                            {
                                int midTx       = runStart + runLen / 2;
                                int clumpHeight = rng.Next(2, 5);

                                placements.Add(new ObjectPlacement
                                {
                                    Type          = ObjectType.RootClump,
                                    PixelPosition = new Vector2(
                                        midTx * TileMap.TileSize + TileMap.TileSize / 2f,
                                        (ty + 1) * TileMap.TileSize + TileMap.TileSize / 2f),
                                    TileHeight    = clumpHeight,
                                });
                            }
                            runStart = -1;
                        }
                    }
                }
            }
        }

        private static void PlaceVentFlowers(
            TileMap map, CavityAnalyzer analyzer, Random rng,
            List<ObjectPlacement> placements, int w, int h, float biomeMult = 1f)
        {
            var lastPlacedCol = new Dictionary<int, int>();

            for (int ty = 2; ty < h - 2; ty++)
            {
                for (int tx = 2; tx < w - 2; tx++)
                {
                    // Must be on solid ground with 3 empty tiles above
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                    if (map.GetTile(tx, ty - 1) != TileType.Empty) continue;
                    if (map.GetTile(tx, ty - 2) != TileType.Empty) continue;
                    if (map.GetTile(tx, ty - 3) != TileType.Empty) continue;

                    // Spacing check
                    int lastCol = lastPlacedCol.TryGetValue(ty, out int lc) ? lc : -999;
                    if (tx - lastCol < MinSpacing * 3) continue;

                    // Boost chance for dead-end alcoves (reward exploration)
                    float localChance = ChanceVentFlower * biomeMult;
                    if (analyzer.IsDeadEnd[tx, ty - 1])
                        localChance *= 3.0f; // strongly prefer alcoves

                    // Boost chance for shaft bottoms (reward descending)
                    if (analyzer.IsShaftBottom[tx, ty - 1])
                        localChance *= 2.0f;

                    if (rng.NextDouble() < localChance)
                    {
                        placements.Add(new ObjectPlacement
                        {
                            Type          = ObjectType.VentFlower,
                            PixelPosition = new Vector2(
                                tx * TileMap.TileSize + TileMap.TileSize / 2f,
                                ty * TileMap.TileSize - 24f),
                        });
                        lastPlacedCol[ty] = tx;
                    }
                }
            }
        }

        private static void PlaceCaveLichen(
            TileMap map, CavityAnalyzer analyzer, Random rng,
            List<ObjectPlacement> placements, int w, int h, float depthMult, int seed, float biomeMult = 1f)
        {
            float chance = (ChanceCaveLichen + depthMult * DepthScaleLichen * ChanceCaveLichen) * biomeMult;
            var lastPlacedCol = new Dictionary<int, int>();

            for (int ty = 1; ty < h - 1; ty++)
            {
                for (int tx = 1; tx < w - 1; tx++)
                {
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;

                    var surface = analyzer.SurfaceFlags[tx, ty];
                    if (surface == CavityAnalyzer.SurfaceType.None) continue;

                    int lastCol = lastPlacedCol.TryGetValue(ty, out int lc) ? lc : -999;
                    if (tx - lastCol < MinSpacing) continue;

                    // Boost chance in large caverns (ecological clustering)
                    float localChance = chance;
                    bool nearLargeCavern = false;
                    // Check adjacent empty tiles for cavern size
                    foreach (var (nx, ny) in new[] { (tx-1,ty),(tx+1,ty),(tx,ty-1),(tx,ty+1) })
                    {
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h
                            && map.GetTile(nx, ny) == TileType.Empty
                            && analyzer.IsLargeCavern(nx, ny))
                        {
                            nearLargeCavern = true;
                            break;
                        }
                    }
                    if (nearLargeCavern) localChance *= 2.5f;

                    if (rng.NextDouble() >= localChance) continue;

                    // Determine poison from seed + position hash (reproducible)
                    bool isPoisonous = new Random(seed + tx * 1000 + ty).NextDouble() < 0.3;

                    // Determine rarity by cavity remoteness (dead-end = higher rarity)
                    ItemRarity rarity;
                    int adjTy = (surface & CavityAnalyzer.SurfaceType.Floor) != 0 ? ty - 1 : ty;
                    if (analyzer.IsDeadEnd[tx, adjTy] && rng.NextDouble() < 0.35)
                        rarity = ItemRarity.Rare;
                    else if (analyzer.IsDeadEnd[tx, adjTy] || !nearLargeCavern)
                        rarity = ItemRarity.Uncommon;
                    else
                        rarity = ItemRarity.Common;

                    // Place in the empty space adjacent to the solid tile
                    // Prefer floor surface (ceiling of solid = floor of empty above)
                    Vector2 pos;
                    if ((surface & CavityAnalyzer.SurfaceType.Floor) != 0)
                        pos = TileMap.TileCenter(tx, ty - 1);
                    else if ((surface & CavityAnalyzer.SurfaceType.WallLeft) != 0)
                        pos = TileMap.TileCenter(tx - 1, ty);
                    else if ((surface & CavityAnalyzer.SurfaceType.WallRight) != 0)
                        pos = TileMap.TileCenter(tx + 1, ty);
                    else
                        pos = TileMap.TileCenter(tx, ty + 1); // ceiling surface

                    placements.Add(new ObjectPlacement
                    {
                        Type          = ObjectType.CaveLichen,
                        PixelPosition = pos,
                        IsPoisonous   = isPoisonous,
                        Rarity        = rarity,
                    });
                    lastPlacedCol[ty] = tx;
                }
            }
        }

        private static void PlaceResonanceShards(
            TileMap map, CavityAnalyzer analyzer, Random rng,
            List<ObjectPlacement> placements, int w, int h, int seed)
        {
            // Place 3–5 shards per level, exclusively in dead-end alcoves.
            // Shards are the primary collection goal; the exit is gated until all are found.
            int targetCount = 3 + rng.Next(3); // 3, 4, or 5

            // Gather all valid dead-end floor positions
            var candidates = new List<Vector2>();
            for (int ty = 5; ty < h - 5; ty++)
            {
                for (int tx = 3; tx < w - 3; tx++)
                {
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                    if ((analyzer.SurfaceFlags[tx, ty] & CavityAnalyzer.SurfaceType.Floor) == 0) continue;

                    int emptyTy = ty - 1;
                    if (emptyTy < 0 || emptyTy >= h) continue;
                    if (!analyzer.IsDeadEnd[tx, emptyTy]) continue;

                    candidates.Add(TileMap.TileCenter(tx, emptyTy));
                }
            }

            // Shuffle candidates deterministically
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            // Pick up to targetCount, enforcing minimum spacing between shards
            const float MinShardSpacingPx = 192f; // 6 tiles
            var placed = new List<Vector2>();

            foreach (var candidate in candidates)
            {
                if (placed.Count >= targetCount) break;

                bool tooClose = false;
                foreach (var p in placed)
                {
                    if (Vector2.DistanceSquared(candidate, p) < MinShardSpacingPx * MinShardSpacingPx)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                placed.Add(candidate);
                placements.Add(new ObjectPlacement
                {
                    Type          = ObjectType.ResonanceShard,
                    PixelPosition = candidate,
                    Rarity        = ItemRarity.Rare,
                });
            }

            // Fallback: if fewer than 3 candidates existed, place remaining shards on any floor
            if (placed.Count < 3)
            {
                for (int ty = 10; ty < h - 10 && placed.Count < 3; ty += 8)
                {
                    for (int tx = 5; tx < w - 5 && placed.Count < 3; tx += 10)
                    {
                        if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                        if ((analyzer.SurfaceFlags[tx, ty] & CavityAnalyzer.SurfaceType.Floor) == 0) continue;
                        var pos = TileMap.TileCenter(tx, ty - 1);

                        bool tooClose = placed.Any(p =>
                            Vector2.DistanceSquared(pos, p) < MinShardSpacingPx * MinShardSpacingPx);
                        if (tooClose) continue;

                        placed.Add(pos);
                        placements.Add(new ObjectPlacement
                        {
                            Type          = ObjectType.ResonanceShard,
                            PixelPosition = pos,
                            Rarity        = ItemRarity.Rare,
                        });
                    }
                }
            }
        }

        private static void PlaceBlindFish(
            TileMap map, CavityAnalyzer analyzer, Random rng,
            List<ObjectPlacement> placements, int w, int h, float depthMult, int seed)
        {
            float chance = ChanceBlindFish + depthMult * DepthScaleFish * ChanceBlindFish;
            var lastPlacedCol = new Dictionary<int, int>();

            for (int ty = 2; ty < h - 2; ty++)
            {
                for (int tx = 2; tx < w - 2; tx++)
                {
                    // Must be on solid ground with empty space above
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                    if (map.GetTile(tx, ty - 1) != TileType.Empty) continue;

                    int lastCol = lastPlacedCol.TryGetValue(ty, out int lc) ? lc : -999;
                    if (tx - lastCol < MinSpacing) continue;

                    // Boost chance in wide floor areas (simulates underground pools)
                    float localChance = chance;

                    // Count consecutive floor tiles in this row
                    int floorWidth = 0;
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int nx = tx + dx;
                        if (nx >= 0 && nx < w
                            && TileProperties.IsSolid(map.GetTile(nx, ty))
                            && map.GetTile(nx, ty - 1) == TileType.Empty)
                            floorWidth++;
                    }
                    if (floorWidth >= 5) localChance *= 2.0f; // wide floor = pool area

                    // Boost chance at shaft bottoms
                    if (analyzer.IsShaftBottom[tx, ty - 1])
                        localChance *= 1.8f;

                    if (rng.NextDouble() >= localChance) continue;

                    bool isPoisonous = new Random(seed + tx * 2000 + ty).NextDouble() < 0.3;

                    // Rarity: fish in wide pools are common; shaft-bottom fish are uncommon/rare
                    ItemRarity rarity;
                    if (analyzer.IsShaftBottom[tx, ty - 1] && rng.NextDouble() < 0.4)
                        rarity = ItemRarity.Rare;
                    else if (analyzer.IsShaftBottom[tx, ty - 1] || floorWidth < 5)
                        rarity = ItemRarity.Uncommon;
                    else
                        rarity = ItemRarity.Common;

                    placements.Add(new ObjectPlacement
                    {
                        Type          = ObjectType.BlindFish,
                        PixelPosition = TileMap.TileCenter(tx, ty - 1),
                        IsPoisonous   = isPoisonous,
                        Rarity        = rarity,
                    });
                    lastPlacedCol[ty] = tx;
                }
            }
        }

        private static void PlaceFallingStalactites(
            TileMap map, CavityAnalyzer analyzer, Random rng,
            List<ObjectPlacement> placements, int w, int h, float biomeMult = 1f)
        {
            // Base chance: 1 in 18 eligible ceiling tiles, scaled by biome stun density
            float chance = (1f / 18f) * biomeMult;
            var lastPlacedCol = new Dictionary<int, int>();

            for (int ty = 2; ty < h - 4; ty++)
            {
                for (int tx = 2; tx < w - 2; tx++)
                {
                    // Must be a solid ceiling tile (solid with empty below)
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                    if (map.GetTile(tx, ty + 1) != TileType.Empty) continue;

                    // Need at least 3 empty tiles below (room to fall)
                    int emptyBelow = 0;
                    for (int dy = 1; dy <= 4; dy++)
                    {
                        if (map.GetTile(tx, ty + dy) == TileType.Empty) emptyBelow++;
                        else break;
                    }
                    if (emptyBelow < 3) continue;

                    // Spacing check
                    int lastCol = lastPlacedCol.TryGetValue(ty, out int lc) ? lc : -999;
                    if (tx - lastCol < MinSpacing * 2) continue;

                    if (rng.NextDouble() >= chance) continue;

                    placements.Add(new ObjectPlacement
                    {
                        Type          = ObjectType.FallingStalactite,
                        PixelPosition = new Vector2(
                            tx * TileMap.TileSize + TileMap.TileSize / 2f,
                            (ty + 1) * TileMap.TileSize + 10f), // just below ceiling
                    });
                    lastPlacedCol[ty] = tx;
                }
            }
        }

        // ── Glow objects (IonStone / PhosphorMoss / CrystalCluster) ────────────

        /// <summary>
        /// Scatter biome-appropriate glowing decorations across the map.
        /// Placement rules per biome:
        ///   ShallowCaves   → sparse PhosphorMoss on exposed wall faces
        ///   FungalGrottos  → dense PhosphorMoss + occasional violet CrystalCluster
        ///   CrystalDepths  → CrystalClusters (mixed variants) on floor tiles
        ///   TheAbyss       → IonStones on floors, rare CrystalClusters
        /// </summary>
        private static void PlaceGlowObjects(
            TileMap map, CavityAnalyzer analyzer, Random rng,
            List<ObjectPlacement> placements, int w, int h, BiomeTier tier)
        {
            float mossChance    = tier switch
            {
                BiomeTier.ShallowCaves  => 1f / 22f,
                BiomeTier.FungalGrottos => 1f / 10f,
                _ => 0f,
            };
            float ionChance     = tier == BiomeTier.TheAbyss ? 1f / 18f : 0f;
            float crystalChance = tier switch
            {
                BiomeTier.CrystalDepths => 1f / 24f,
                BiomeTier.FungalGrottos => 1f / 90f,
                BiomeTier.TheAbyss      => 1f / 60f,
                _ => 0f,
            };

            var lastMossRow     = new Dictionary<int, int>();
            var lastIonCol      = new Dictionary<int, int>();
            var lastCrystalCol  = new Dictionary<int, int>();

            for (int ty = 2; ty < h - 2; ty++)
            {
                for (int tx = 2; tx < w - 2; tx++)
                {
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                    var surface = analyzer.SurfaceFlags[tx, ty];
                    if (surface == CavityAnalyzer.SurfaceType.None) continue;

                    // ── PhosphorMoss: any exposed surface ─────────────────────
                    if (mossChance > 0f)
                    {
                        int lastRow = lastMossRow.TryGetValue(tx, out int lr) ? lr : -999;
                        if (ty - lastRow >= 3 && rng.NextDouble() < mossChance)
                        {
                            Vector2 pos = SurfacePos(tx, ty, surface);
                            placements.Add(new ObjectPlacement
                            {
                                Type          = ObjectType.PhosphorMoss,
                                PixelPosition = pos,
                            });
                            lastMossRow[tx] = ty;
                            continue;
                        }
                    }

                    // IonStone and CrystalCluster prefer floor surfaces.
                    if ((surface & CavityAnalyzer.SurfaceType.Floor) == 0) continue;

                    int lastIon     = lastIonCol.TryGetValue(ty, out int li)   ? li : -999;
                    int lastCrystal = lastCrystalCol.TryGetValue(ty, out int lc) ? lc : -999;

                    // ── CrystalCluster: floor-standing, mixed variants ────────
                    if (crystalChance > 0f
                        && tx - lastCrystal >= MinSpacing * 2
                        && rng.NextDouble() < crystalChance)
                    {
                        int variant = rng.Next(3);
                        // Bias FungalGrottos → violet, TheAbyss → violet/red only
                        if (tier == BiomeTier.FungalGrottos) variant = 1;
                        else if (tier == BiomeTier.TheAbyss
                                 && variant == 0) variant = 2;

                        placements.Add(new ObjectPlacement
                        {
                            Type          = ObjectType.CrystalCluster,
                            PixelPosition = TileMap.TileCenter(tx, ty - 1),
                            Variant       = variant,
                        });
                        lastCrystalCol[ty] = tx;
                        continue;
                    }

                    // ── IonStone: Abyss-only flickering decoration ────────────
                    if (ionChance > 0f
                        && tx - lastIon >= MinSpacing
                        && rng.NextDouble() < ionChance)
                    {
                        placements.Add(new ObjectPlacement
                        {
                            Type          = ObjectType.IonStone,
                            PixelPosition = TileMap.TileCenter(tx, ty - 1),
                        });
                        lastIonCol[ty] = tx;
                    }
                }
            }
        }

        /// <summary>Choose a pixel position in the empty space adjacent to a solid tile.</summary>
        private static Vector2 SurfacePos(int tx, int ty, CavityAnalyzer.SurfaceType surface)
        {
            if ((surface & CavityAnalyzer.SurfaceType.Floor)     != 0) return TileMap.TileCenter(tx, ty - 1);
            if ((surface & CavityAnalyzer.SurfaceType.Ceiling)   != 0) return TileMap.TileCenter(tx, ty + 1);
            if ((surface & CavityAnalyzer.SurfaceType.WallLeft)  != 0) return TileMap.TileCenter(tx - 1, ty);
            return TileMap.TileCenter(tx + 1, ty);
        }

        // ── Crystal bridge placement ───────────────────────────────────────────

        /// <summary>
        /// Place 1–4 CrystalBridge anchors on wall surfaces adjacent to open space.
        /// Growth direction points away from the wall into the open area.
        /// Minimum 8-tile spacing between bridges.
        /// Unlocked at level 11 via DifficultyProfile.
        /// </summary>
        private static void PlaceCrystalBridges(
            TileMap map, CavityAnalyzer analyzer, Random rng,
            List<ObjectPlacement> placements, int w, int h,
            int seed, DifficultyProfile difficulty)
        {
            const int MinSpacingBridge = 8;
            const int MaxBridges       = 4;

            int count = 0;
            int maxCount = 1 + (int)((difficulty.Level - 11) / 5f); // 1 at L11, 2 at L16, 3 at L21, 4 at L26
            maxCount = Math.Clamp(maxCount, 1, MaxBridges);

            var placed = new List<(int tx, int ty)>();

            // Candidate growth directions: horizontal and diagonal-up
            var directions = new[]
            {
                new Vector2( 1f,  0f),  // right
                new Vector2(-1f,  0f),  // left
                new Vector2( 1f, -0.5f), // right-up diagonal
                new Vector2(-1f, -0.5f), // left-up diagonal
            };

            for (int tx = 3; tx < w - 3 && count < maxCount; tx++)
            {
                for (int ty = 4; ty < h - 4 && count < maxCount; ty++)
                {
                    // Must be a solid tile
                    if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;

                    // Check for open space to the right or left (wall surface)
                    bool openRight = map.GetTile(tx + 1, ty) == TileType.Empty
                                  && map.GetTile(tx + 1, ty - 1) == TileType.Empty;
                    bool openLeft  = map.GetTile(tx - 1, ty) == TileType.Empty
                                  && map.GetTile(tx - 1, ty - 1) == TileType.Empty;

                    if (!openRight && !openLeft) continue;

                    // Need enough open space for the bridge to grow (at least 4 tiles)
                    int dir = openRight ? 1 : -1;
                    int clearance = 0;
                    for (int dx = 1; dx <= 6; dx++)
                    {
                        int nx = tx + dir * dx;
                        if (nx < 1 || nx >= w - 1) break;
                        if (map.GetTile(nx, ty) != TileType.Empty) break;
                        clearance++;
                    }
                    if (clearance < 3) continue;

                    // Enforce minimum spacing
                    bool tooClose = false;
                    foreach (var (px, py) in placed)
                        if (Math.Abs(tx - px) < MinSpacingBridge && Math.Abs(ty - py) < MinSpacingBridge)
                        { tooClose = true; break; }
                    if (tooClose) continue;

                    // Random chance
                    if (rng.NextDouble() > 1.0 / 120.0) continue;

                    // Pick growth direction (prefer horizontal, occasionally diagonal)
                    Vector2 growDir = rng.NextDouble() < 0.7
                        ? new Vector2(dir, 0f)
                        : Vector2.Normalize(new Vector2(dir, -0.5f));

                    int segCount = 3 + rng.Next(4); // 3–6 segments
                    int bridgeSeed = seed ^ (tx * 1031 + ty * 7919);

                    // Anchor spawn position: empty tile adjacent to the wall
                    Vector2 anchorPos = TileMap.TileCenter(tx + dir, ty);

                    placements.Add(new ObjectPlacement
                    {
                        Type          = ObjectType.CrystalBridge,
                        PixelPosition = anchorPos,
                        GrowDirection = growDir,
                        SegmentCount  = segCount,
                        Variant       = bridgeSeed,
                    });

                    placed.Add((tx, ty));
                    count++;
                }
            }
        }

        // ── Controllable entity placement ──────────────────────────────────────

        /// <summary>
        /// Places all 7 controllable entity types across the level.
        ///
        /// Biome distribution:
        ///   EchoBat            — ceiling/open air tiles (any depth)
        ///   SilkWeaverSpider   — wall surfaces (depth ≥ 1)
        ///   ChainCentipede     — ceiling surfaces (depth ≥ 2)
        ///   LuminescentGlowworm— floor surfaces in dark alcoves (any depth)
        ///   DeepBurrowWorm     — wide floor areas (depth ≥ 2)
        ///   BlindCaveSalamander— floor near water / shaft bottoms (depth ≥ 1)
        ///   LuminousIsopod     — floor surfaces near entry (depth ≥ 1)
        ///
        /// Each type places 1–3 instances per level, spaced at least 20 tiles apart.
        /// </summary>
        private static void PlaceControllableEntities(
            TileMap map, CavityAnalyzer analyzer, Random rng,
            List<ObjectPlacement> placements, int w, int h, int depth,
            DifficultyProfile difficulty = default)
        {
            // Solitary entities: enforce minimum tile spacing between same-type placements
            const int SolitarySpacing   = 8;  // tiles — SilkWeaverSpider, DeepBurrowWorm
            const int GregariousSpacing = 20; // tiles — gregarious anchor spacing

            // ── Echo Bat: ceiling tiles — gregarious (unlocked at level 4) ─────
            if (difficulty.Level == 0 || difficulty.HasEchoBat)
            {
                int anchorCount = 0;
                int maxAnchors  = Math.Min(5, 3 + depth);
                int lastTx      = -999;
                for (int ty = 2; ty < h - 2 && anchorCount < maxAnchors; ty++)
                {
                    for (int tx = 3; tx < w - 3 && anchorCount < maxAnchors; tx++)
                    {
                        if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                        if (map.GetTile(tx, ty + 1) != TileType.Empty) continue;
                        if (map.GetTile(tx, ty + 2) != TileType.Empty) continue;
                        if (Math.Abs(tx - lastTx) < GregariousSpacing) continue;
                        if (rng.NextDouble() > 1.0 / 80.0) continue;

                        var anchor = TileMap.TileCenter(tx, ty + 1);
                        placements.Add(new ObjectPlacement
                        {
                            Type          = ObjectType.EchoBat,
                            PixelPosition = anchor,
                        });
                        lastTx = tx;
                        anchorCount++;

                        int clusterSize = 1 + rng.Next(3);
                        PlaceCluster(map, rng, placements, ObjectType.EchoBat,
                            anchor, clusterSize, radiusTiles: 3, w, h,
                            tileValidator: (cx, cy) =>
                                TileProperties.IsSolid(map.GetTile(cx, cy))
                                && map.GetTile(cx, cy + 1) == TileType.Empty,
                            spawnTileYOffset: +1);
                    }
                }
            }

            // ── Silk Weaver Spider: wall surfaces (unlocked at level 7) ─────────
            if (difficulty.Level == 0 || difficulty.HasSilkWeaverSpider)
            {
                int count    = 0;
                int maxCount = 1 + (depth >= 2 ? 1 : 0);
                var placed   = new List<(int tx, int ty)>();
                for (int tx = 2; tx < w - 2 && count < maxCount; tx++)
                {
                    for (int ty = 3; ty < h - 3 && count < maxCount; ty++)
                    {
                        if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                        if (map.GetTile(tx + 1, ty) != TileType.Empty) continue;
                        bool tooClose = false;
                        foreach (var (px, py) in placed)
                            if (Math.Abs(tx - px) < SolitarySpacing && Math.Abs(ty - py) < SolitarySpacing)
                            { tooClose = true; break; }
                        if (tooClose) continue;
                        if (rng.NextDouble() > 1.0 / 90.0) continue;

                        placements.Add(new ObjectPlacement
                        {
                            Type          = ObjectType.SilkWeaverSpider,
                            PixelPosition = TileMap.TileCenter(tx + 1, ty),
                        });
                        placed.Add((tx, ty));
                        count++;
                    }
                }
            }

            // ── Chain Centipede: ceiling surfaces (unlocked at level 11) ─────────
            if (difficulty.Level == 0 || difficulty.HasChainCentipede)
            {
                int anchorCount = 0;
                int maxAnchors  = 2 + (depth >= 4 ? 2 : 0);
                int lastTx      = -999;
                for (int ty = 2; ty < h - 2 && anchorCount < maxAnchors; ty++)
                {
                    for (int tx = 3; tx < w - 3 && anchorCount < maxAnchors; tx++)
                    {
                        if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                        if (map.GetTile(tx, ty + 1) != TileType.Empty) continue;
                        if (Math.Abs(tx - lastTx) < GregariousSpacing) continue;
                        if (rng.NextDouble() > 1.0 / 100.0) continue;

                        var anchor = TileMap.TileCenter(tx, ty + 1);
                        placements.Add(new ObjectPlacement
                        {
                            Type          = ObjectType.ChainCentipede,
                            PixelPosition = anchor,
                        });
                        lastTx = tx;
                        anchorCount++;

                        int clusterSize = 1 + rng.Next(2);
                        PlaceCluster(map, rng, placements, ObjectType.ChainCentipede,
                            anchor, clusterSize, radiusTiles: 3, w, h,
                            tileValidator: (cx, cy) =>
                                TileProperties.IsSolid(map.GetTile(cx, cy))
                                && map.GetTile(cx, cy + 1) == TileType.Empty,
                            spawnTileYOffset: +1);
                    }
                }
            }

            // ── Luminescent Glowworm: floor alcoves — always available ────────
            {
                int anchorCount = 0;
                int maxAnchors  = Math.Min(6, 4 + depth);
                int lastTx      = -999;
                for (int ty = 3; ty < h - 2 && anchorCount < maxAnchors; ty++)
                {
                    for (int tx = 3; tx < w - 3 && anchorCount < maxAnchors; tx++)
                    {
                        if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                        if (map.GetTile(tx, ty - 1) != TileType.Empty) continue;
                        bool hasWall = TileProperties.IsSolid(map.GetTile(tx - 1, ty - 1))
                                    || TileProperties.IsSolid(map.GetTile(tx + 1, ty - 1));
                        if (!hasWall) continue;
                        if (Math.Abs(tx - lastTx) < GregariousSpacing) continue;
                        if (rng.NextDouble() > 1.0 / 70.0) continue;

                        var anchor = TileMap.TileCenter(tx, ty - 1);
                        placements.Add(new ObjectPlacement
                        {
                            Type          = ObjectType.LuminescentGlowworm,
                            PixelPosition = anchor,
                        });
                        lastTx = tx;
                        anchorCount++;

                        int clusterSize = 2 + rng.Next(3);
                        PlaceCluster(map, rng, placements, ObjectType.LuminescentGlowworm,
                            anchor, clusterSize, radiusTiles: 2, w, h,
                            tileValidator: (cx, cy) =>
                                TileProperties.IsSolid(map.GetTile(cx, cy))
                                && map.GetTile(cx, cy - 1) == TileType.Empty);
                    }
                }
            }

            // ── Deep Burrow Worm: wide floor (unlocked at level 16) ───────────
            if (difficulty.Level == 0 || difficulty.HasDeepBurrowWorm)
            {
                int count    = 0;
                int maxCount = 1 + (depth >= 4 ? 1 : 0);
                var placed   = new List<(int tx, int ty)>();
                for (int ty = 3; ty < h - 2 && count < maxCount; ty++)
                {
                    for (int tx = 4; tx < w - 4 && count < maxCount; tx++)
                    {
                        if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                        if (map.GetTile(tx, ty - 1) != TileType.Empty) continue;
                        int floorW = 0;
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int nx = tx + dx;
                            if (nx >= 0 && nx < w
                                && TileProperties.IsSolid(map.GetTile(nx, ty))
                                && map.GetTile(nx, ty - 1) == TileType.Empty)
                                floorW++;
                        }
                        if (floorW < 4) continue;
                        bool tooClose = false;
                        foreach (var (px, py) in placed)
                            if (Math.Abs(tx - px) < SolitarySpacing && Math.Abs(ty - py) < SolitarySpacing)
                            { tooClose = true; break; }
                        if (tooClose) continue;
                        if (rng.NextDouble() > 1.0 / 110.0) continue;

                        placements.Add(new ObjectPlacement
                        {
                            Type          = ObjectType.DeepBurrowWorm,
                            PixelPosition = TileMap.TileCenter(tx, ty - 1),
                        });
                        placed.Add((tx, ty));
                        count++;
                    }
                }
            }

            // ── Blind Cave Salamander: floor near shaft bottoms (unlocked at level 4) ──
            if (difficulty.Level == 0 || difficulty.HasBlindCaveSalamander)
            {
                int count    = 0;
                int maxCount = 1 + (depth >= 3 ? 1 : 0);
                int lastTx   = -999;
                for (int ty = 3; ty < h - 2 && count < maxCount; ty++)
                {
                    for (int tx = 3; tx < w - 3 && count < maxCount; tx++)
                    {
                        if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                        if (map.GetTile(tx, ty - 1) != TileType.Empty) continue;
                        bool isShaftBottom = ty - 1 >= 0 && ty - 1 < h
                                          && analyzer.IsShaftBottom[tx, ty - 1];
                        if (!isShaftBottom && rng.NextDouble() > 0.3) continue;
                        if (Math.Abs(tx - lastTx) < GregariousSpacing) continue;
                        if (rng.NextDouble() > 1.0 / 85.0) continue;

                        var anchor = TileMap.TileCenter(tx, ty - 1);
                        placements.Add(new ObjectPlacement
                        {
                            Type          = ObjectType.BlindCaveSalamander,
                            PixelPosition = anchor,
                        });
                        lastTx = tx;
                        count++;

                        PlaceCluster(map, rng, placements, ObjectType.BlindCaveSalamander,
                            anchor, count: 1, radiusTiles: 4, w, h,
                            tileValidator: (cx, cy) =>
                                TileProperties.IsSolid(map.GetTile(cx, cy))
                                && map.GetTile(cx, cy - 1) == TileType.Empty);
                    }
                }
            }

            // ── Luminous Isopod: floor — always available ─────────────────────
            {
                int anchorCount = 0;
                int maxAnchors  = 1 + (depth >= 2 ? 2 : 0);
                int lastTx      = -999;
                for (int ty = 3; ty < h - 2 && anchorCount < maxAnchors; ty++)
                {
                    for (int tx = 3; tx < w - 3 && anchorCount < maxAnchors; tx++)
                    {
                        if (!TileProperties.IsSolid(map.GetTile(tx, ty))) continue;
                        if (map.GetTile(tx, ty - 1) != TileType.Empty) continue;
                        if (Math.Abs(tx - lastTx) < GregariousSpacing) continue;
                        if (rng.NextDouble() > 1.0 / 60.0) continue;

                        var anchor = TileMap.TileCenter(tx, ty - 1);
                        placements.Add(new ObjectPlacement
                        {
                            Type          = ObjectType.LuminousIsopod,
                            PixelPosition = anchor,
                        });
                        lastTx = tx;
                        anchorCount++;

                        int clusterSize = 1 + rng.Next(3);
                        PlaceCluster(map, rng, placements, ObjectType.LuminousIsopod,
                            anchor, clusterSize, radiusTiles: 3, w, h,
                            tileValidator: (cx, cy) =>
                                TileProperties.IsSolid(map.GetTile(cx, cy))
                                && map.GetTile(cx, cy - 1) == TileType.Empty);
                    }
                }
            }
        }

        /// <summary>
        /// Place up to <paramref name="count"/> entities of <paramref name="type"/> within
        /// <paramref name="radiusTiles"/> tiles of <paramref name="anchor"/>.
        /// <paramref name="tileValidator"/>(tx, ty) must return true for the solid tile;
        /// <paramref name="spawnTileYOffset"/> is added to ty to get the spawn tile
        /// (e.g. -1 for floor entities that spawn one tile above the solid tile,
        ///  +1 for ceiling entities that spawn one tile below the solid tile).
        /// </summary>
        private static void PlaceCluster(
            TileMap map, Random rng, List<ObjectPlacement> placements,
            ObjectType type, Vector2 anchor, int count, int radiusTiles,
            int mapW, int mapH, Func<int, int, bool> tileValidator,
            int spawnTileYOffset = -1)
        {
            int anchorTx = (int)(anchor.X / TileMap.TileSize);
            int anchorTy = (int)(anchor.Y / TileMap.TileSize);

            int placed = 0;
            int attempts = 0;
            int maxAttempts = count * 20;

            while (placed < count && attempts < maxAttempts)
            {
                attempts++;
                int dx = rng.Next(-radiusTiles, radiusTiles + 1);
                int dy = rng.Next(-radiusTiles, radiusTiles + 1);
                int cx = anchorTx + dx;
                int cy = anchorTy + dy;

                if (cx < 2 || cx >= mapW - 2 || cy < 2 || cy >= mapH - 2) continue;
                if (!tileValidator(cx, cy)) continue;

                // Avoid placing exactly on the anchor tile
                if (dx == 0 && dy == 0) continue;

                int spawnTy = cy + spawnTileYOffset;
                if (spawnTy < 0 || spawnTy >= mapH) continue;

                placements.Add(new ObjectPlacement
                {
                    Type          = type,
                    PixelPosition = TileMap.TileCenter(cx, spawnTy),
                });
                placed++;
            }
        }
    }
}
