using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Generators;
using Bloop.World;

namespace Bloop.Rendering
{
    /// <summary>
    /// Renders each TileType as a complex geometric shape using GeometryBatch primitives.
    /// Neighbor-aware: exposed edges get organic deformation; interior tiles use a fast path.
    ///
    /// All positions are in pixel space. TileSize = 32.
    /// Animation uses AnimationClock.Time for ambient effects.
    ///
    /// Draw call budget per tile:
    ///   Interior Solid  : 1  (plain rect)
    ///   Peripheral Solid: 4–6
    ///   Platform        : 5–7
    ///   Slope           : 4–5
    ///   Climbable       : 5–7
    /// </summary>
    public static class TileRenderer
    {
        // ── Tile size constant ─────────────────────────────────────────────────
        private const int TS = TileMap.TileSize; // 32

        // ── Active biome (set once per level load) ─────────────────────────────
        private static BiomeTier _currentBiome = BiomeTier.ShallowCaves;

        /// <summary>Call once when a new level is loaded to switch the color palette.</summary>
        public static void SetBiome(BiomeTier biome) => _currentBiome = biome;

        // ── Biome color palettes ───────────────────────────────────────────────

        // ShallowCaves — warm brown/amber
        private static readonly Color SC_SolidBase      = new Color( 72,  54,  36);
        private static readonly Color SC_SolidDark      = new Color( 50,  36,  22);
        private static readonly Color SC_SolidHighlight = new Color( 98,  76,  52);
        private static readonly Color SC_SolidCrack     = new Color( 40,  28,  16);
        private static readonly Color SC_SlopeBase      = new Color( 80,  62,  42);
        private static readonly Color SC_SlopeDark      = new Color( 54,  40,  24);
        private static readonly Color SC_SlopeCrack     = new Color( 44,  32,  18);

        // FungalGrottos — cool green/teal
        private static readonly Color FG_SolidBase      = new Color( 38,  62,  48);
        private static readonly Color FG_SolidDark      = new Color( 24,  44,  32);
        private static readonly Color FG_SolidHighlight = new Color( 56,  90,  68);
        private static readonly Color FG_SolidCrack     = new Color( 18,  34,  24);
        private static readonly Color FG_SlopeBase      = new Color( 44,  70,  54);
        private static readonly Color FG_SlopeDark      = new Color( 28,  48,  36);
        private static readonly Color FG_SlopeCrack     = new Color( 20,  38,  28);

        // CrystalDepths — cold blue/purple
        private static readonly Color CD_SolidBase      = new Color( 42,  48,  80);
        private static readonly Color CD_SolidDark      = new Color( 26,  30,  58);
        private static readonly Color CD_SolidHighlight = new Color( 68,  76, 120);
        private static readonly Color CD_SolidCrack     = new Color( 20,  24,  48);
        private static readonly Color CD_SlopeBase      = new Color( 50,  56,  90);
        private static readonly Color CD_SlopeDark      = new Color( 32,  36,  64);
        private static readonly Color CD_SlopeCrack     = new Color( 24,  28,  52);

        // TheAbyss — deep red/black
        private static readonly Color TA_SolidBase      = new Color( 58,  22,  22);
        private static readonly Color TA_SolidDark      = new Color( 36,  12,  12);
        private static readonly Color TA_SolidHighlight = new Color( 82,  34,  34);
        private static readonly Color TA_SolidCrack     = new Color( 28,   8,   8);
        private static readonly Color TA_SlopeBase      = new Color( 64,  26,  26);
        private static readonly Color TA_SlopeDark      = new Color( 40,  14,  14);
        private static readonly Color TA_SlopeCrack     = new Color( 30,  10,  10);

        // ── Palette accessors ──────────────────────────────────────────────────
        private static Color SolidBase      => _currentBiome switch {
            BiomeTier.FungalGrottos => FG_SolidBase,
            BiomeTier.CrystalDepths => CD_SolidBase,
            BiomeTier.TheAbyss      => TA_SolidBase,
            _                       => SC_SolidBase };
        private static Color SolidDark      => _currentBiome switch {
            BiomeTier.FungalGrottos => FG_SolidDark,
            BiomeTier.CrystalDepths => CD_SolidDark,
            BiomeTier.TheAbyss      => TA_SolidDark,
            _                       => SC_SolidDark };
        private static Color SolidHighlight => _currentBiome switch {
            BiomeTier.FungalGrottos => FG_SolidHighlight,
            BiomeTier.CrystalDepths => CD_SolidHighlight,
            BiomeTier.TheAbyss      => TA_SolidHighlight,
            _                       => SC_SolidHighlight };
        private static Color SolidCrack    => _currentBiome switch {
            BiomeTier.FungalGrottos => FG_SolidCrack,
            BiomeTier.CrystalDepths => CD_SolidCrack,
            BiomeTier.TheAbyss      => TA_SolidCrack,
            _                       => SC_SolidCrack };
        private static Color SlopeBase     => _currentBiome switch {
            BiomeTier.FungalGrottos => FG_SlopeBase,
            BiomeTier.CrystalDepths => CD_SlopeBase,
            BiomeTier.TheAbyss      => TA_SlopeBase,
            _                       => SC_SlopeBase };
        private static Color SlopeDark     => _currentBiome switch {
            BiomeTier.FungalGrottos => FG_SlopeDark,
            BiomeTier.CrystalDepths => CD_SlopeDark,
            BiomeTier.TheAbyss      => TA_SlopeDark,
            _                       => SC_SlopeDark };
        private static Color SlopeCrack    => _currentBiome switch {
            BiomeTier.FungalGrottos => FG_SlopeCrack,
            BiomeTier.CrystalDepths => CD_SlopeCrack,
            BiomeTier.TheAbyss      => TA_SlopeCrack,
            _                       => SC_SlopeCrack };

        // ── Platform tile colors (shared across biomes, slight tint) ───────────
        private static readonly Color PlatformWood   = new Color( 90,  68,  42);
        private static readonly Color PlatformGrain  = new Color( 72,  54,  32);
        private static readonly Color PlatformMoss   = new Color( 58, 128,  48);
        private static readonly Color PlatformMossHi = new Color( 80, 160,  60);
        private static readonly Color PlatformDrip   = new Color( 60, 100, 140);

        // ── Climbable tile colors ──────────────────────────────────────────────
        private static readonly Color ClimbBg        = new Color( 28,  72,  38);
        private static readonly Color ClimbVine      = new Color( 38, 118,  58);
        private static readonly Color ClimbLeaf      = new Color( 56, 152,  70);
        private static readonly Color ClimbGlow      = new Color( 90, 210, 100);

        // ── Main entry point ──────────────────────────────────────────────────

        /// <summary>
        /// Draw a crack overlay for a damaged tile.
        /// damage: 0 = pristine, 255 = critical. Call after DrawTile to composite on top.
        /// Crack pattern is deterministic from (tx, ty).
        /// </summary>
        public static void DrawDamageOverlay(SpriteBatch sb, AssetManager assets,
            int tx, int ty, byte damage)
        {
            if (damage == 0) return;

            int px = tx * TS;
            int py = ty * TS;
            int seed = tx * 7 + ty * 13;

            // Number of crack lines scales with damage: 1 at low, 4 at near-collapse.
            int cracks = 1 + damage / 64;
            float alpha = 0.35f + (damage / 255f) * 0.55f;
            Color crackColor = SolidCrack * alpha;

            for (int c = 0; c < cracks; c++)
            {
                int s = seed + c * 29;
                float x0 = px + 3 + (s       & 0x1F) % (TS - 6);
                float y0 = py + 3 + ((s * 3) & 0x1F) % (TS - 6);
                float x1 = x0 + ((s * 5) % 18) - 9;
                float y1 = y0 + ((s * 7) % 18) - 9;
                GeometryBatch.DrawLine(sb, assets,
                    new Vector2(x0, y0), new Vector2(x1, y1),
                    crackColor, 1f);
            }

            // Near-critical: add a darker flake in one corner.
            if (damage > 192)
            {
                int flakeX = px + (seed & 1) * (TS - 5);
                int flakeY = py + ((seed >> 1) & 1) * (TS - 5);
                assets.DrawRect(sb, new Rectangle(flakeX, flakeY, 5, 5),
                    SolidCrack * 0.6f);
            }
        }

        /// <summary>
        /// Draw a single tile at grid position (tx, ty).
        /// neighborCache must have been refreshed for this frame before calling.
        /// </summary>
        public static void DrawTile(SpriteBatch sb, AssetManager assets,
            TileType type, int tx, int ty, TileNeighborCache neighborCache)
        {
            int px = tx * TS;
            int py = ty * TS;
            var rect = new Rectangle(px, py, TS, TS);

            switch (type)
            {
                case TileType.Solid:
                    DrawSolid(sb, assets, tx, ty, px, py, rect, neighborCache);
                    break;
                case TileType.Platform:
                    DrawPlatform(sb, assets, tx, ty, px, py);
                    break;
                case TileType.SlopeLeft:
                    DrawSlope(sb, assets, tx, ty, px, py, slopeRight: false);
                    break;
                case TileType.SlopeRight:
                    DrawSlope(sb, assets, tx, ty, px, py, slopeRight: true);
                    break;
                case TileType.Climbable:
                    DrawClimbable(sb, assets, tx, ty, px, py);
                    break;
            }
        }

        // ── Solid tile ─────────────────────────────────────────────────────────

        private static void DrawSolid(SpriteBatch sb, AssetManager assets,
            int tx, int ty, int px, int py, Rectangle rect, TileNeighborCache neighborCache)
        {
            // Fast path: fully interior tile — just a plain rect
            if (neighborCache.IsInterior(tx, ty))
            {
                assets.DrawRect(sb, rect, SolidBase);
                return;
            }

            bool topExp    = neighborCache.IsTopExposed(tx, ty);
            bool rightExp  = neighborCache.IsRightExposed(tx, ty);
            bool bottomExp = neighborCache.IsBottomExposed(tx, ty);
            bool leftExp   = neighborCache.IsLeftExposed(tx, ty);

            // ── 1. Base fill ───────────────────────────────────────────────────
            assets.DrawRect(sb, rect, SolidBase);

            // ── 2. Edge erosion bites (triangular notches on exposed edges) ────
            // Positions are deterministic from tile coords so they're stable
            int seed = tx * 7 + ty * 13;

            if (topExp)
                DrawEdgeBites(sb, assets, px, py, TS, true, seed, SolidDark);
            if (bottomExp)
                DrawEdgeBites(sb, assets, px, py + TS - 4, TS, true, seed + 3, SolidDark);
            if (leftExp)
                DrawEdgeBites(sb, assets, px, py, TS, false, seed + 5, SolidDark);
            if (rightExp)
                DrawEdgeBites(sb, assets, px + TS - 4, py, TS, false, seed + 7, SolidDark);

            // ── 3. Surface crack lines ─────────────────────────────────────────
            int crackCount = 1 + (seed % 2); // 1 or 2 cracks
            for (int c = 0; c < crackCount; c++)
            {
                int crackSeed = seed + c * 17;
                float crackX0 = px + 4 + (crackSeed % (TS - 8));
                float crackY0 = py + 4 + ((crackSeed * 3) % (TS - 8));
                float crackX1 = crackX0 + ((crackSeed * 5) % 12) - 6;
                float crackY1 = crackY0 + ((crackSeed * 7) % 10) + 4;
                GeometryBatch.DrawLine(sb, assets,
                    new Vector2(crackX0, crackY0),
                    new Vector2(crackX1, crackY1),
                    SolidCrack, 1f);
            }

            // ── 4. Depth shading strips ────────────────────────────────────────
            if (topExp)
                assets.DrawRect(sb, new Rectangle(px, py, TS, 2), SolidHighlight);
            if (bottomExp)
                assets.DrawRect(sb, new Rectangle(px, py + TS - 2, TS, 2), SolidDark);
            if (leftExp)
                assets.DrawRect(sb, new Rectangle(px, py, 2, TS), SolidHighlight);
            if (rightExp)
                assets.DrawRect(sb, new Rectangle(px + TS - 2, py, 2, TS), SolidDark);

            // ── 5. Corner erosion for exposed corners ──────────────────────────
            if (topExp && rightExp)
                DrawCornerErosion(sb, assets, px + TS - 6, py, 6, 6, SolidDark);
            if (topExp && leftExp)
                DrawCornerErosion(sb, assets, px, py, 6, 6, SolidDark);
            if (bottomExp && rightExp)
                DrawCornerErosion(sb, assets, px + TS - 6, py + TS - 6, 6, 6, SolidDark);
            if (bottomExp && leftExp)
                DrawCornerErosion(sb, assets, px, py + TS - 6, 6, 6, SolidDark);

            // ── 6. Organic silhouettes (3.4) ───────────────────────────────────
            // Stalactite drips: hang from the bottom face of ceiling tiles
            // (tile has empty below = bottomExp, and solid above = !topExp)
            if (bottomExp && !topExp)
                DrawStalactiteDrips(sb, assets, px, py, seed);

            // Stalagmite bumps: rise from the top face of floor tiles
            // (tile has empty above = topExp, and solid below = !bottomExp)
            if (topExp && !bottomExp)
                DrawStalagmiteBumps(sb, assets, px, py, seed);

            // Rough wall protrusions: small nubs on exposed vertical faces
            if (leftExp && !rightExp)
                DrawWallProtrusions(sb, assets, px, py, seed + 11, facingRight: false);
            if (rightExp && !leftExp)
                DrawWallProtrusions(sb, assets, px, py, seed + 19, facingRight: true);
        }

        /// <summary>
        /// Draw 1–2 stalactite drips hanging downward from the bottom of a ceiling tile.
        /// Each drip is a narrow tapered rectangle extending below the tile boundary.
        /// </summary>
        private static void DrawStalactiteDrips(SpriteBatch sb, AssetManager assets,
            int px, int py, int seed)
        {
            int count = 1 + (seed % 2); // 1 or 2 drips per tile
            for (int i = 0; i < count; i++)
            {
                int s = seed + i * 23;
                int dripX  = px + 4 + (s % (TS - 8));
                int dripW  = 2 + (s * 3 % 3);          // 2–4 px wide
                int dripH  = 4 + (s * 7 % 8);          // 4–11 px tall
                // Draw the drip body
                assets.DrawRect(sb,
                    new Rectangle(dripX, py + TS - 2, dripW, dripH),
                    SolidDark);
                // Tip: 1px darker point
                assets.DrawRect(sb,
                    new Rectangle(dripX + dripW / 2, py + TS - 2 + dripH, 1, 2),
                    SolidCrack);
            }
        }

        /// <summary>
        /// Draw 1–2 stalagmite bumps rising upward from the top of a floor tile.
        /// </summary>
        private static void DrawStalagmiteBumps(SpriteBatch sb, AssetManager assets,
            int px, int py, int seed)
        {
            int count = 1 + ((seed * 3) % 2); // 1 or 2 bumps
            for (int i = 0; i < count; i++)
            {
                int s = seed + i * 31;
                int bumpX = px + 3 + (s % (TS - 6));
                int bumpW = 2 + (s * 5 % 4);   // 2–5 px wide
                int bumpH = 3 + (s * 9 % 7);   // 3–9 px tall
                // Draw the bump body (extends upward from tile top)
                assets.DrawRect(sb,
                    new Rectangle(bumpX, py - bumpH + 2, bumpW, bumpH),
                    SolidBase);
                // Highlight on the left face
                assets.DrawRect(sb,
                    new Rectangle(bumpX, py - bumpH + 2, 1, bumpH),
                    SolidHighlight);
            }
        }

        /// <summary>
        /// Draw 1–2 small rocky protrusions on an exposed vertical wall face.
        /// </summary>
        private static void DrawWallProtrusions(SpriteBatch sb, AssetManager assets,
            int px, int py, int seed, bool facingRight)
        {
            int count = 1 + (seed % 2);
            for (int i = 0; i < count; i++)
            {
                int s = seed + i * 41;
                int nubY = py + 3 + (s % (TS - 6));
                int nubW = 2 + (s * 3 % 3);  // 2–4 px
                int nubH = 2 + (s * 7 % 3);  // 2–4 px
                int nubX = facingRight ? px + TS - 2 : px - nubW + 2;
                assets.DrawRect(sb,
                    new Rectangle(nubX, nubY, nubW, nubH),
                    SolidDark);
            }
        }

        /// <summary>
        /// Draw 2–3 small triangular "bite" notches along an edge to create an irregular silhouette.
        /// horizontal=true for top/bottom edges, false for left/right edges.
        /// </summary>
        private static void DrawEdgeBites(SpriteBatch sb, AssetManager assets,
            int edgeX, int edgeY, int edgeLength, bool horizontal, int seed, Color biteColor)
        {
            int biteCount = 2 + (seed % 2); // 2 or 3 bites
            for (int b = 0; b < biteCount; b++)
            {
                int bSeed  = seed + b * 11;
                int offset = 4 + (bSeed % (edgeLength - 8));
                int size   = 2 + (bSeed % 3); // 2–4 px

                if (horizontal)
                    assets.DrawRect(sb, new Rectangle(edgeX + offset, edgeY, size, 4), biteColor);
                else
                    assets.DrawRect(sb, new Rectangle(edgeX, edgeY + offset, 4, size), biteColor);
            }
        }

        /// <summary>Draw a small triangle-like erosion at a corner using background color.</summary>
        private static void DrawCornerErosion(SpriteBatch sb, AssetManager assets,
            int cx, int cy, int w, int h, Color color)
        {
            // Approximate a triangle with two overlapping rects
            assets.DrawRect(sb, new Rectangle(cx, cy, w, 2), color);
            assets.DrawRect(sb, new Rectangle(cx, cy, 2, h), color);
        }

        // ── Platform tile ──────────────────────────────────────────────────────

        private static void DrawPlatform(SpriteBatch sb, AssetManager assets,
            int tx, int ty, int px, int py)
        {
            float t = AnimationClock.Time;

            // ── 1. Plank body (thin, centered vertically) ──────────────────────
            int plankY = py + TS / 2 - 3;
            assets.DrawRect(sb, new Rectangle(px + 1, plankY, TS - 2, 6), PlatformWood);

            // ── 2. Wood grain lines ────────────────────────────────────────────
            assets.DrawRect(sb, new Rectangle(px + 2, plankY + 2, TS - 4, 1), PlatformGrain);
            assets.DrawRect(sb, new Rectangle(px + 2, plankY + 4, TS - 4, 1), PlatformGrain);

            // ── 3. Moss tufts on top (animated sway) ──────────────────────────
            int tileHash = tx * 7 + ty * 13;
            int tuftCount = 3 + (tileHash % 3); // 3–5 tufts
            for (int i = 0; i < tuftCount; i++)
            {
                int tuftSeed = tileHash + i * 11;
                int baseX    = px + 3 + (tuftSeed % (TS - 6));
                int tuftW    = 4 + (tuftSeed % 3);
                int tuftH    = 3 + (tuftSeed % 3);

                // Sway: ±1px horizontal oscillation
                float swayX = AnimationClock.Sway(1f, 1.5f, i * 1.1f);
                int drawX = baseX + (int)swayX;

                Color tuftColor = (i % 2 == 0) ? PlatformMoss : PlatformMossHi;
                assets.DrawRect(sb, new Rectangle(drawX, plankY - tuftH, tuftW, tuftH), tuftColor);
            }

            // ── 4. Drip drop (every 3rd tile, slow falling drop) ───────────────
            if (tileHash % 3 == 0)
            {
                float dropPhase = (tileHash % 7) * 0.4f;
                float dropT     = AnimationClock.Loop(2.5f, dropPhase);
                int   dropY     = plankY + 6 + (int)(dropT * 10f);
                int   dropAlpha = dropT < 0.8f ? 200 : (int)((1f - (dropT - 0.8f) / 0.2f) * 200);
                if (dropAlpha > 0)
                    assets.DrawRect(sb, new Rectangle(px + TS / 2 - 1, dropY, 2, 3),
                        PlatformDrip * (dropAlpha / 255f));
            }
        }

        // ── Slope tile ─────────────────────────────────────────────────────────

        private static void DrawSlope(SpriteBatch sb, AssetManager assets,
            int tx, int ty, int px, int py, bool slopeRight)
        {
            int seed = tx * 7 + ty * 13;

            // ── 1. Fill the triangular area with horizontal scanlines ──────────
            // SlopeRight: top-left corner is the apex, fill lower-right triangle
            // SlopeLeft:  top-right corner is the apex, fill lower-left triangle
            for (int row = 0; row < TS; row++)
            {
                int fillStart, fillWidth;
                if (slopeRight)
                {
                    // Row 0 (top): 1px wide at left. Row 31 (bottom): full width.
                    fillWidth = 1 + row;
                    fillStart = px + TS - fillWidth;
                }
                else
                {
                    // Row 0 (top): 1px wide at right. Row 31 (bottom): full width.
                    fillWidth = 1 + row;
                    fillStart = px;
                }
                assets.DrawRect(sb, new Rectangle(fillStart, py + row, fillWidth, 1), SlopeBase);
            }

            // ── 2. Diagonal edge line (slightly irregular) ─────────────────────
            Vector2 diagA, diagB;
            if (slopeRight)
            {
                diagA = new Vector2(px,      py);
                diagB = new Vector2(px + TS, py + TS);
            }
            else
            {
                diagA = new Vector2(px + TS, py);
                diagB = new Vector2(px,      py + TS);
            }

            // Main diagonal
            GeometryBatch.DrawLine(sb, assets, diagA, diagB, SlopeDark, 2f);

            // Slight bump on the diagonal for irregularity
            float midX = (diagA.X + diagB.X) / 2f + ((seed % 5) - 2);
            float midY = (diagA.Y + diagB.Y) / 2f + ((seed % 3) - 1);
            GeometryBatch.DrawLine(sb, assets, diagA, new Vector2(midX, midY), SlopeDark, 1f);

            // ── 3. Rock crack parallel to slope ───────────────────────────────
            float crackOffset = 4f + (seed % 6);
            Vector2 crackA, crackB;
            if (slopeRight)
            {
                crackA = new Vector2(px + crackOffset,      py + crackOffset + 4);
                crackB = new Vector2(px + TS - crackOffset, py + TS - crackOffset + 4);
            }
            else
            {
                crackA = new Vector2(px + TS - crackOffset, py + crackOffset + 4);
                crackB = new Vector2(px + crackOffset,      py + TS - crackOffset + 4);
            }
            GeometryBatch.DrawLine(sb, assets, crackA, crackB, SlopeCrack, 1f);

            // ── 4. Shadow strip along bottom edge ─────────────────────────────
            assets.DrawRect(sb, new Rectangle(px, py + TS - 2, TS, 2), SlopeDark);
        }

        // ── Climbable tile ─────────────────────────────────────────────────────

        private static void DrawClimbable(SpriteBatch sb, AssetManager assets,
            int tx, int ty, int px, int py)
        {
            float t    = AnimationClock.Time;
            int   seed = tx * 7 + ty * 13;

            // ── 1. Background fill ─────────────────────────────────────────────
            assets.DrawRect(sb, new Rectangle(px, py, TS, TS), ClimbBg * 0.75f);

            // ── 2. Vine strands (2–3 vertical lines with sine-wave X offset) ───
            int strandCount = 2 + (seed % 2); // 2 or 3 strands
            for (int s = 0; s < strandCount; s++)
            {
                int strandSeed = seed + s * 17;
                int baseX      = px + 6 + (strandSeed % (TS - 12));
                float phase    = s * 1.5f + (strandSeed % 10) * 0.3f;

                // Draw the strand as a series of short segments with sine-wave X
                int segCount = 8;
                for (int seg = 0; seg < segCount; seg++)
                {
                    float y0 = py + seg * (TS / segCount);
                    float y1 = py + (seg + 1) * (TS / segCount);
                    float x0 = baseX + AnimationClock.Sway(2f, 2.0f, phase + seg * 0.2f);
                    float x1 = baseX + AnimationClock.Sway(2f, 2.0f, phase + (seg + 1) * 0.2f);

                    GeometryBatch.DrawLine(sb, assets,
                        new Vector2(x0, y0), new Vector2(x1, y1),
                        ClimbVine, 2f);
                }

                // ── 3. Leaf nodes along each strand ───────────────────────────
                int leafCount = 2 + (strandSeed % 2);
                for (int l = 0; l < leafCount; l++)
                {
                    int leafSeed = strandSeed + l * 13;
                    float leafY  = py + 4 + (leafSeed % (TS - 8));
                    float leafX  = baseX + AnimationClock.Sway(2f, 2.0f, phase + leafY * 0.05f);
                    int   leafW  = 4 + (leafSeed % 3);
                    int   leafH  = 3 + (leafSeed % 2);

                    // Alternate left/right
                    float leafOffX = (l % 2 == 0) ? leafW * 0.5f : -leafW * 1.5f;
                    assets.DrawRect(sb,
                        new Rectangle((int)(leafX + leafOffX), (int)leafY, leafW, leafH),
                        ClimbLeaf);
                }
            }

            // ── 4. Bioluminescent glow dots (pulse independently) ──────────────
            int glowCount = 1 + (seed % 2); // 1 or 2 glow dots
            for (int g = 0; g < glowCount; g++)
            {
                int glowSeed = seed + g * 23;
                float glowX  = px + 4 + (glowSeed % (TS - 8));
                float glowY  = py + 4 + ((glowSeed * 3) % (TS - 8));
                float pulse  = AnimationClock.Pulse(3f, g * 2.1f);
                float alpha  = 0.4f + pulse * 0.6f;

                assets.DrawRect(sb,
                    new Rectangle((int)glowX, (int)glowY, 2, 2),
                    ClimbGlow * alpha);
            }
        }
    }
}
