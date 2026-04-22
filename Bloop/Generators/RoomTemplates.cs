using System;
using System.Collections.Generic;
using Bloop.World;

namespace Bloop.Generators
{
    /// <summary>
    /// Landmark room types. Each has a distinct visual identity and gameplay purpose.
    /// </summary>
    public enum LandmarkKind
    {
        CrystalGrove,    // Wide chamber with central pillar cluster — safe rest area
        AbyssalShaft,    // Tall narrow vertical shaft with ledge platforms
        CollapseHall,    // Wide horizontal corridor with low ceiling and rubble pillars
        ShrineAlcove,    // Small niche with symmetrical walls — resonance shard spawn
        FungalChamber,   // Rounded oval chamber — glow vine / lichen spawn
        // ── Large rooms (added for 160-wide maps) ─────────────────────────────
        GrandCavern,     // 30×16 wide open chamber with scattered pillars
        CrystalGallery,  // 26×14 long horizontal gallery with crystal alcoves
        ColossalShaft,   // 12×30 very tall vertical shaft with ledge platforms
    }

    /// <summary>
    /// Describes a placed landmark room: its tile-space bounding box and kind.
    /// </summary>
    public readonly struct LandmarkRoom
    {
        public LandmarkKind Kind   { get; init; }
        public int          TileX  { get; init; }  // top-left tile X
        public int          TileY  { get; init; }  // top-left tile Y
        public int          Width  { get; init; }  // in tiles
        public int          Height { get; init; }  // in tiles
    }

    /// <summary>
    /// Carves 1–3 pre-designed landmark rooms into a generated TileMap.
    /// Rooms are placed after the main generation pass but before smoothing,
    /// so their edges get softened by the cellular automata pass.
    ///
    /// Each room template is defined as a 2-D char array:
    ///   '.' = force empty (carve)
    ///   '#' = force solid (fill)
    ///   ' ' = leave as-is (don't touch)
    /// </summary>
    public static class RoomTemplates
    {
        // ── Template definitions ───────────────────────────────────────────────

        // CrystalGrove: 18×12 — wide chamber, central pillar cluster
        private static readonly string[] CrystalGroveTemplate =
        {
            "..................",
            "..................",
            "...##.....##......",
            "...##.....##......",
            "..................",
            "..................",
            "....##...##.......",
            "....##...##.......",
            "..................",
            "..................",
            "..................",
            "..................",
        };

        // AbyssalShaft: 8×20 — tall narrow shaft with ledge platforms
        private static readonly string[] AbyssalShaftTemplate =
        {
            "........",
            "........",
            "....##..",
            "....##..",
            "........",
            "........",
            "..##....",
            "..##....",
            "........",
            "........",
            "....##..",
            "....##..",
            "........",
            "........",
            "..##....",
            "..##....",
            "........",
            "........",
            "........",
            "........",
        };

        // CollapseHall: 22×8 — wide corridor with rubble pillars
        private static readonly string[] CollapseHallTemplate =
        {
            "......................",
            "......................",
            "......................",
            "...##....##....##.....",
            "...##....##....##.....",
            "......................",
            "......................",
            "......................",
        };

        // ShrineAlcove: 10×10 — symmetrical niche
        private static readonly string[] ShrineAlcoveTemplate =
        {
            "..........",
            ".#......#.",
            ".#......#.",
            "..........",
            "..........",
            "..........",
            ".#......#.",
            ".#......#.",
            "..........",
            "..........",
        };

        // FungalChamber: 16×10 — rounded oval chamber
        private static readonly string[] FungalChamberTemplate =
        {
            "....##########....",
            "..##..........##..",
            ".#..............#.",
            ".................",
            ".................",
            ".................",
            ".................",
            ".#..............#.",
            "..##..........##..",
            "....##########....",
        };

        // ── Large room templates (for 160-wide maps) ───────────────────────────

        // GrandCavern: 30×16 — wide open chamber with scattered pillars
        private static readonly string[] GrandCavernTemplate =
        {
            "..............................",
            "..............................",
            "..............................",
            "....##.......##.......##......",
            "....##.......##.......##......",
            "..............................",
            "..............................",
            "..............................",
            "..............................",
            "....##.......##.......##......",
            "....##.......##.......##......",
            "..............................",
            "..............................",
            "..............................",
            "..............................",
            "..............................",
        };

        // CrystalGallery: 26×14 — long horizontal gallery with alcoves
        private static readonly string[] CrystalGalleryTemplate =
        {
            "..........................",
            "..........................",
            "##....##....##....##....##",
            "##....##....##....##....##",
            "..........................",
            "..........................",
            "..........................",
            "..........................",
            "##....##....##....##....##",
            "##....##....##....##....##",
            "..........................",
            "..........................",
            "..........................",
            "..........................",
        };

        // ColossalShaft: 12×30 — very tall vertical shaft with ledge platforms
        private static readonly string[] ColossalShaftTemplate =
        {
            "............",
            "............",
            "......####..",
            "......####..",
            "............",
            "............",
            "..####......",
            "..####......",
            "............",
            "............",
            "......####..",
            "......####..",
            "............",
            "............",
            "..####......",
            "..####......",
            "............",
            "............",
            "......####..",
            "......####..",
            "............",
            "............",
            "..####......",
            "..####......",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
        };

        // ── Template registry ──────────────────────────────────────────────────

        private static (string[] template, int w, int h) GetTemplate(LandmarkKind kind)
        {
            return kind switch
            {
                LandmarkKind.CrystalGrove   => (CrystalGroveTemplate,   18, 12),
                LandmarkKind.AbyssalShaft   => (AbyssalShaftTemplate,    8, 20),
                LandmarkKind.CollapseHall   => (CollapseHallTemplate,   22,  8),
                LandmarkKind.ShrineAlcove   => (ShrineAlcoveTemplate,   10, 10),
                LandmarkKind.FungalChamber  => (FungalChamberTemplate,  16, 10),
                LandmarkKind.GrandCavern    => (GrandCavernTemplate,    30, 16),
                LandmarkKind.CrystalGallery => (CrystalGalleryTemplate, 26, 14),
                LandmarkKind.ColossalShaft  => (ColossalShaftTemplate,  12, 30),
                _                           => (CrystalGroveTemplate,   18, 12),
            };
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Inject 2–5 landmark rooms into the map.
        /// Rooms are placed in the middle vertical band (20%–80% of height)
        /// and spread horizontally to avoid clustering.
        /// Returns the list of placed rooms for downstream use (e.g. object placement).
        /// </summary>
        public static List<LandmarkRoom> InjectRooms(TileMap map, Random rng, int depth)
        {
            var placed = new List<LandmarkRoom>();

            // Number of rooms scales with depth (2 at depth 1, up to 5 at depth 7+)
            int roomCount = Math.Min(2 + (depth - 1) / 2, 5);

            // Candidate kinds — pick without replacement, include large rooms
            var kinds = new List<LandmarkKind>
            {
                LandmarkKind.CrystalGrove,
                LandmarkKind.AbyssalShaft,
                LandmarkKind.CollapseHall,
                LandmarkKind.ShrineAlcove,
                LandmarkKind.FungalChamber,
                LandmarkKind.GrandCavern,
                LandmarkKind.CrystalGallery,
                LandmarkKind.ColossalShaft,
            };
            Shuffle(kinds, rng);

            int mapW = map.Width;
            int mapH = map.Height;

            // Divide the map into horizontal bands for room placement
            int bandW = mapW / (roomCount + 1);

            for (int i = 0; i < roomCount && i < kinds.Count; i++)
            {
                var kind = kinds[i];
                var (template, tw, th) = GetTemplate(kind);

                // Horizontal center of this band
                int bandCenterX = bandW * (i + 1);

                // Try up to 8 random positions within the band
                bool success = false;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    // X: within ±bandW/3 of band center, clamped to map interior
                    int tx = bandCenterX + rng.Next(-bandW / 3, bandW / 3 + 1) - tw / 2;
                    tx = Math.Clamp(tx, 2, mapW - tw - 2);

                    // Y: in the middle 20%–80% vertical band
                    int minY = (int)(mapH * 0.20f);
                    int maxY = (int)(mapH * 0.80f) - th;
                    if (maxY <= minY) continue;
                    int ty = rng.Next(minY, maxY);

                    // Check no overlap with already-placed rooms (with 2-tile margin)
                    if (OverlapsPlaced(placed, tx, ty, tw, th, 2)) continue;

                    // Carve the room
                    CarveTemplate(map, template, tx, ty, tw, th);

                    placed.Add(new LandmarkRoom
                    {
                        Kind   = kind,
                        TileX  = tx,
                        TileY  = ty,
                        Width  = tw,
                        Height = th,
                    });
                    success = true;
                    break;
                }

                // If placement failed after 8 attempts, skip this room
                _ = success;
            }

            return placed;
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private static void CarveTemplate(TileMap map, string[] template,
            int originX, int originY, int tw, int th)
        {
            for (int row = 0; row < th && row < template.Length; row++)
            {
                string line = template[row];
                for (int col = 0; col < tw; col++)
                {
                    int mx = originX + col;
                    int my = originY + row;

                    if (mx < 0 || mx >= map.Width || my < 0 || my >= map.Height) continue;

                    char c = col < line.Length ? line[col] : ' ';
                    switch (c)
                    {
                        case '.':
                            map.SetTile(mx, my, TileType.Empty);
                            break;
                        case '#':
                            map.SetTile(mx, my, TileType.Solid);
                            break;
                        // ' ' = leave as-is
                    }
                }
            }
        }

        private static bool OverlapsPlaced(List<LandmarkRoom> placed,
            int tx, int ty, int tw, int th, int margin)
        {
            foreach (var r in placed)
            {
                bool xOverlap = tx < r.TileX + r.Width  + margin &&
                                tx + tw + margin > r.TileX;
                bool yOverlap = ty < r.TileY + r.Height + margin &&
                                ty + th + margin > r.TileY;
                if (xOverlap && yOverlap) return true;
            }
            return false;
        }

        private static void Shuffle<T>(List<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
