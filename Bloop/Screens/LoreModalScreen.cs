using System;
using System.Collections.Generic;
using Bloop.Core;
using Bloop.Lore;
using Bloop.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Bloop.Screens
{
    /// <summary>
    /// Gothic diary-entry modal shown when the player collects a Resonance Shard.
    /// Blocks gameplay updates but renders the world behind it.
    /// Pops itself and invokes onDismissed(sanityDelta) when any key is pressed.
    /// </summary>
    public class LoreModalScreen : Screen
    {
        // ── Layout ─────────────────────────────────────────────────────────────
        private const float PanelW       = 560f;
        private const float PanelH       = 360f;
        private const float FadeInTime   = 0.5f;
        private const float SlideOffset  = 20f;  // starts below, slides up
        private const float LineHeight   = 20f;

        // ── Colors (gothic dark palette) ───────────────────────────────────────
        private static readonly Color OverlayColor  = new Color(0,   0,   0,   190);
        private static readonly Color PanelColor    = new Color(8,   5,   18,  255);
        private static readonly Color BorderColor   = new Color(80,  45,  120, 255);
        private static readonly Color BorderPulse   = new Color(160, 100, 220, 255);
        private static readonly Color TitleColor    = new Color(200, 160, 255, 255);
        private static readonly Color AuthorColor   = new Color(130,  90, 170, 255);
        private static readonly Color DividerColor  = new Color(60,  35,  90,  255);
        private static readonly Color ContentColor  = new Color(185, 175, 195, 255);
        private static readonly Color HintColor     = new Color(110,  90, 150, 255);
        private static readonly Color SanityNeg     = new Color(210,  55,  55, 255);
        private static readonly Color SanityPos     = new Color( 80, 195, 100, 255);
        private static readonly Color DismissColor  = new Color( 90, 110, 130, 255);
        private static readonly Color ShardIcon     = new Color(160, 120, 240, 255);

        // ── State ──────────────────────────────────────────────────────────────
        private readonly LoreEntry   _entry;
        private readonly Action<int> _onDismissed;
        private float                _fadeIn = 0f;
        private bool                 _dismissed = false;

        // Pre-split content sentences for rendering
        private readonly List<string> _contentLines;

        public LoreModalScreen(LoreEntry entry, Action<int> onDismissed)
        {
            _entry       = entry;
            _onDismissed = onDismissed;
            _contentLines = SplitContent(entry.Content);
        }

        public override bool BlocksDraw   => false;
        public override bool BlocksUpdate => true;

        // ── Update ─────────────────────────────────────────────────────────────

        public override void Update(GameTime gameTime)
        {
            if (_dismissed) return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _fadeIn = MathHelper.Clamp(_fadeIn + dt / FadeInTime, 0f, 1f);

            // Allow dismiss only after fade is substantially complete
            if (_fadeIn >= 0.9f)
            {
                var input = ScreenManager.Input;
                if (input.IsAnyKeyPressed() || input.IsLeftClickPressed())
                {
                    _dismissed = true;
                    ScreenManager.Pop();
                    _onDismissed?.Invoke(_entry.SanityDelta);
                }
            }
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            var assets = Game1.Assets;
            int vw     = GraphicsDevice.Viewport.Width;
            int vh     = GraphicsDevice.Viewport.Height;

            // Smooth ease-in alpha
            float alpha = Ease(_fadeIn);

            spriteBatch.Begin();

            // Full-screen dim overlay
            assets.DrawRect(spriteBatch, new Rectangle(0, 0, vw, vh),
                Multiply(OverlayColor, alpha));

            // Panel position — slides up as it fades in
            int px = (int)(vw / 2f - PanelW / 2f);
            int py = (int)(vh / 2f - PanelH / 2f) + (int)(SlideOffset * (1f - alpha));

            // Panel background
            assets.DrawRect(spriteBatch,
                new Rectangle(px, py, (int)PanelW, (int)PanelH),
                Multiply(PanelColor, alpha));

            // Pulsing border
            float pulse = AnimationClock.Pulse(1.2f);
            Color border = Color.Lerp(BorderColor, BorderPulse, pulse * 0.35f);
            assets.DrawRectOutline(spriteBatch,
                new Rectangle(px, py, (int)PanelW, (int)PanelH),
                Multiply(border, alpha), 2);

            // Small shard diamond icon (top-left corner accent)
            DrawShardIcon(spriteBatch, assets, px + 18, py + 18, alpha);

            // Title (centered, MenuFont)
            assets.DrawMenuStringCentered(spriteBatch, _entry.Title,
                py + 14f, Multiply(TitleColor, alpha), 1.0f);

            // Author (right-aligned, GameFont small)
            string authorLine = $"— {_entry.Author}";
            assets.DrawString(spriteBatch, authorLine,
                new Vector2(px + PanelW - MeasureGameFont(authorLine, 0.75f) - 14f, py + 18f),
                Multiply(AuthorColor, alpha), 0.75f);

            // Divider
            assets.DrawRect(spriteBatch,
                new Rectangle(px + 16, py + 50, (int)PanelW - 32, 1),
                Multiply(Color.Lerp(DividerColor, BorderPulse, pulse * 0.4f), alpha));

            // Content body
            float contentY = py + 62f;
            foreach (string line in _contentLines)
            {
                assets.DrawString(spriteBatch, line,
                    new Vector2(px + 20f, contentY),
                    Multiply(ContentColor, alpha), 0.78f);
                contentY += LineHeight;
            }

            // Divider before hint
            float hintY = py + 200f;
            assets.DrawRect(spriteBatch,
                new Rectangle(px + 16, (int)hintY - 8, (int)PanelW - 32, 1),
                Multiply(DividerColor, alpha * 0.6f));

            // Portal hint (italic-ish smaller text)
            assets.DrawString(spriteBatch, $"\"{_entry.PortalHint}\"",
                new Vector2(px + 20f, hintY),
                Multiply(HintColor, alpha), 0.72f);

            // Sanity indicator
            float sanityY = py + PanelH - 72f;
            string sanityLabel = _entry.SanityDelta >= 0
                ? $"Sanity  +{_entry.SanityDelta}"
                : $"Sanity  {_entry.SanityDelta}";
            Color sanityColor = _entry.SanityDelta >= 0 ? SanityPos : SanityNeg;

            // Fast pulse for negative, gentle for positive
            if (_entry.SanityDelta < 0)
            {
                float sanPulse = AnimationClock.Pulse(4f);
                sanityColor = Color.Lerp(sanityColor, new Color(255, 150, 150), sanPulse * 0.3f);
            }
            assets.DrawString(spriteBatch, sanityLabel,
                new Vector2(px + 20f, sanityY),
                Multiply(sanityColor, alpha), 0.85f);

            // Small colored pip next to sanity text
            Color pipColor = _entry.SanityDelta >= 0 ? SanityPos : SanityNeg;
            assets.DrawRect(spriteBatch,
                new Rectangle(px + 12, (int)sanityY + 3, 5, 10),
                Multiply(pipColor, alpha * 0.9f));

            // Dismiss prompt (fades in after modal is mostly visible)
            if (_fadeIn >= 0.85f)
            {
                float promptAlpha = (_fadeIn - 0.85f) / 0.15f;
                float dimPulse    = 0.6f + 0.4f * AnimationClock.Pulse(1.5f);
                assets.DrawMenuStringCentered(spriteBatch, "Press any key to continue",
                    py + PanelH - 26f,
                    Multiply(DismissColor, alpha * promptAlpha * dimPulse), 0.72f);
            }

            spriteBatch.End();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static float Ease(float t) => t * t * (3f - 2f * t); // smoothstep

        private static Color Multiply(Color c, float alpha)
            => new Color(c.R, c.G, c.B, (byte)(c.A * alpha));

        private float MeasureGameFont(string text, float scale)
        {
            if (Game1.Assets.GameFont == null) return text.Length * 8f * scale;
            return Game1.Assets.GameFont.MeasureString(text).X * scale;
        }

        private static List<string> SplitContent(string content)
        {
            // Split on sentence boundaries, keeping each sentence as a separate line.
            // Also hard-wrap at ~65 characters to fit inside the panel.
            const int MaxChars = 65;
            var lines = new List<string>();

            string[] sentences = content.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string sentence in sentences)
            {
                string s = sentence.TrimEnd('.');
                if (s.Length == 0) continue;

                // Hard-wrap if needed
                while (s.Length > MaxChars)
                {
                    int breakAt = s.LastIndexOf(' ', MaxChars);
                    if (breakAt <= 0) breakAt = MaxChars;
                    lines.Add(s.Substring(0, breakAt));
                    s = s.Substring(breakAt).TrimStart();
                }
                if (s.Length > 0) lines.Add(s);
            }
            return lines;
        }

        private static void DrawShardIcon(SpriteBatch sb, AssetManager assets, int x, int y, float alpha)
        {
            float pulse = 0.6f + 0.4f * AnimationClock.Pulse(2f);
            Color c = Multiply(new Color((int)(ShardIcon.R * pulse), (int)(ShardIcon.G * pulse), (int)(ShardIcon.B * pulse)), alpha);
            // Diamond: 4 triangles meeting at center (approximated as rotated rect)
            assets.DrawRect(sb, new Rectangle(x + 4, y,     4, 4), c);
            assets.DrawRect(sb, new Rectangle(x,     y + 4, 4, 4), c);
            assets.DrawRect(sb, new Rectangle(x + 8, y + 4, 4, 4), c);
            assets.DrawRect(sb, new Rectangle(x + 4, y + 8, 4, 4), c);
            assets.DrawRect(sb, new Rectangle(x + 4, y + 4, 4, 4), Multiply(new Color(220, 190, 255), alpha)); // bright center
        }
    }
}
