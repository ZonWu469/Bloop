using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Generators;
using Bloop.Objects;
using Bloop.Rendering;
using Bloop.World;

namespace Bloop.UI
{
    /// <summary>
    /// Minimap HUD element drawn in the bottom-right corner.
    /// Shows discovered tiles (fog-of-reveal), shard locations (always visible,
    /// pulsing so they stand out even on undiscovered terrain), and the exit.
    ///
    /// Rendering is CPU-side (DrawRect calls) — no render-target needed, keeping it simple.
    /// One screen pixel = one tile. The map is scaled by MapScale and padded by margin.
    /// </summary>
    public class Minimap
    {
        // ── Layout ─────────────────────────────────────────────────────────────
        private const int MapScale  = 2;   // pixels per tile on screen
        private const int Margin    = 12;
        private const int BorderPad = 2;

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color BgColor       = new Color( 4,  6, 10, 180);
        private static readonly Color SolidColor    = new Color(60, 70, 80);
        private static readonly Color EmptyColor    = new Color(20, 25, 30);
        private static readonly Color Undiscovered  = new Color( 0,  0,  0, 0);   // transparent = black
        private static readonly Color PlayerColor   = new Color(80, 220, 120);
        private static readonly Color ExitLocked    = new Color(80,  50, 120);
        private static readonly Color ExitUnlocked  = new Color(220, 180, 60);
        private static readonly Color ShardColor    = new Color(180, 140, 255);
        private static readonly Color ShardGlow     = new Color(220, 190, 255);
        private static readonly Color BorderColor   = new Color(50,  60,  80);

        // ── Draw ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw the minimap in the bottom-right corner of the screen.
        /// Call inside a SpriteBatch.Begin/End block in screen-space (no camera transform).
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, AssetManager assets,
            Level level, Vector2 playerPixelPos, int screenWidth, int screenHeight)
        {
            if (level == null) return;

            int mapW = level.TileMap.Width;
            int mapH = level.TileMap.Height;

            int displayW = mapW * MapScale;
            int displayH = mapH * MapScale;

            // Clamp display size so it never overwhelms the screen
            // Max 160×200 px on screen
            int maxW = 160;
            int maxH = 200;
            int scale = MapScale;
            while (displayW > maxW || displayH > maxH)
            {
                scale = 1;
                displayW = mapW * scale;
                displayH = mapH * scale;
                break;
            }

            int originX = screenWidth  - displayW - Margin - BorderPad * 2;
            int originY = screenHeight - displayH - Margin - BorderPad * 2;

            // Background + border
            assets.DrawRect(spriteBatch,
                new Rectangle(originX - BorderPad, originY - BorderPad,
                    displayW + BorderPad * 2, displayH + BorderPad * 2),
                BgColor);
            assets.DrawRectOutline(spriteBatch,
                new Rectangle(originX - BorderPad, originY - BorderPad,
                    displayW + BorderPad * 2, displayH + BorderPad * 2),
                BorderColor, 1);

            // Draw tiles: only draw discovered area
            bool[,] disc = level.Discovered;
            for (int ty = 0; ty < mapH; ty++)
            {
                for (int tx = 0; tx < mapW; tx++)
                {
                    if (!disc[tx, ty]) continue;

                    var tile   = level.TileMap.GetTile(tx, ty);
                    bool solid = TileProperties.IsSolid(tile);
                    Color col  = solid ? SolidColor : EmptyColor;

                    assets.DrawRect(spriteBatch,
                        new Rectangle(originX + tx * scale, originY + ty * scale, scale, scale),
                        col);
                }
            }

            // ── Draw resonance shards ──────────────────────────────────────────
            // Always visible (even on undiscovered tiles) — they pulse to stand out.
            // Collected shards are shown as a dim dot; uncollected pulse brightly.
            float pulse     = AnimationClock.Pulse(2.5f);   // 0→1→0 at 2.5 Hz
            float fastPulse = AnimationClock.Pulse(4.0f);   // faster secondary pulse

            foreach (var obj in level.Objects)
            {
                if (obj is ResonanceShard shard)
                {
                    int stx = (int)(shard.PixelPosition.X / TileMap.TileSize);
                    int sty = (int)(shard.PixelPosition.Y / TileMap.TileSize);
                    if (stx < 0 || stx >= mapW || sty < 0 || sty >= mapH) continue;

                    int dotX = originX + stx * scale;
                    int dotY = originY + sty * scale;

                    if (!shard.IsDestroyed)
                    {
                        // Uncollected shard: pulsing glow ring + bright core dot
                        // Outer glow ring (larger, lower alpha, pulses)
                        float glowAlpha = 0.25f + pulse * 0.35f;
                        assets.DrawRect(spriteBatch,
                            new Rectangle(dotX - 3, dotY - 3, scale + 6, scale + 6),
                            ShardGlow * glowAlpha);

                        // Mid ring
                        float midAlpha = 0.45f + pulse * 0.30f;
                        assets.DrawRect(spriteBatch,
                            new Rectangle(dotX - 1, dotY - 1, scale + 2, scale + 2),
                            ShardColor * midAlpha);

                        // Core dot (solid, always visible)
                        assets.DrawRect(spriteBatch,
                            new Rectangle(dotX, dotY, scale, scale),
                            Color.Lerp(ShardColor, ShardGlow, pulse));
                    }
                    // Collected shards are removed from level.Objects, so no else needed
                }
            }

            // Draw exit marker
            int exitTx = (int)(level.ExitPoint.X / TileMap.TileSize);
            int exitTy = (int)(level.ExitPoint.Y / TileMap.TileSize);
            Color exitCol = level.IsExitUnlocked ? ExitUnlocked : ExitLocked;
            if (exitTx >= 0 && exitTx < mapW && exitTy >= 0 && exitTy < mapH)
            {
                assets.DrawRect(spriteBatch,
                    new Rectangle(originX + exitTx * scale - 1, originY + exitTy * scale - 1,
                        scale + 2, scale + 2),
                    exitCol);
            }

            // Draw player dot (always on top)
            int ptx = (int)(playerPixelPos.X / TileMap.TileSize);
            int pty = (int)(playerPixelPos.Y / TileMap.TileSize);
            if (ptx >= 0 && ptx < mapW && pty >= 0 && pty < mapH)
            {
                assets.DrawRect(spriteBatch,
                    new Rectangle(originX + ptx * scale - 1, originY + pty * scale - 1,
                        scale + 2, scale + 2),
                    PlayerColor);
            }
        }
    }
}
