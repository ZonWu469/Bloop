using Bloop.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Bloop.Screens
{
    /// <summary>
    /// Options screen: volume slider and controls reference.
    /// Settings are stored in a simple static class for global access.
    /// </summary>
    public class OptionsScreen : Screen
    {
        // ── Layout ─────────────────────────────────────────────────────────────
        private const float PanelW = 600f;
        private const float PanelH = 480f;

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color BgColor      = new Color(8,  10, 18);
        private static readonly Color TitleColor   = new Color(220, 180, 80);
        private static readonly Color BorderColor  = new Color(60,  90, 140);
        private static readonly Color TextPrimary  = new Color(220, 240, 255);
        private static readonly Color TextSecondary= new Color(120, 150, 180);
        private static readonly Color SliderBg     = new Color(20,  30,  50);
        private static readonly Color SliderFill   = new Color(60, 120, 200);
        private static readonly Color HintColor    = new Color(100, 120, 140);

        // ── Volume slider drag state ───────────────────────────────────────────
        private bool _draggingVolume = false;

        public override bool BlocksDraw   => true;
        public override bool BlocksUpdate => true;

        public override void Update(GameTime gameTime)
        {
            var input    = ScreenManager.Input;
            var mousePos = input.GetMousePosition();

            if (input.IsPausePressed() || input.IsKeyPressed(Keys.Enter))
            {
                ScreenManager.Pop();
                return;
            }

            // Volume slider interaction
            var sliderRect = GetVolumeSliderRect();
            if (input.IsLeftClickPressed() && sliderRect.Contains((int)mousePos.X, (int)mousePos.Y))
                _draggingVolume = true;
            if (input.IsLeftClickReleased())
                _draggingVolume = false;

            if (_draggingVolume)
            {
                float t = MathHelper.Clamp(
                    (mousePos.X - sliderRect.X) / sliderRect.Width, 0f, 1f);
                GameSettings.MasterVolume = t;
            }

            // Keyboard volume adjustment
            if (input.IsKeyHeld(Keys.Left))
                GameSettings.MasterVolume = MathHelper.Clamp(GameSettings.MasterVolume - 0.01f, 0f, 1f);
            if (input.IsKeyHeld(Keys.Right))
                GameSettings.MasterVolume = MathHelper.Clamp(GameSettings.MasterVolume + 0.01f, 0f, 1f);
        }

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            var assets = Game1.Assets;
            int vw     = GraphicsDevice.Viewport.Width;
            int vh     = GraphicsDevice.Viewport.Height;

            spriteBatch.Begin();

            assets.DrawRect(spriteBatch, new Rectangle(0, 0, vw, vh), BgColor);

            // Panel
            int px = (int)(vw / 2f - PanelW / 2f);
            int py = (int)(vh / 2f - PanelH / 2f);
            assets.DrawRect(spriteBatch, new Rectangle(px, py, (int)PanelW, (int)PanelH), new Color(12, 16, 28));
            assets.DrawRectOutline(spriteBatch, new Rectangle(px, py, (int)PanelW, (int)PanelH), BorderColor, 2);

            // Title
            assets.DrawMenuStringCentered(spriteBatch, "OPTIONS", py + 28f, TitleColor, 1.6f);
            assets.DrawRect(spriteBatch, new Rectangle(px + 20, py + 72, (int)PanelW - 40, 2), BorderColor);

            // ── Volume ──────────────────────────────────────────────────────────
            int sectionY = py + 90;
            assets.DrawMenuString(spriteBatch, "AUDIO", new Vector2(px + 24, sectionY), TitleColor, 0.9f);

            assets.DrawMenuString(spriteBatch, "Master Volume",
                new Vector2(px + 24, sectionY + 30), TextPrimary, 0.85f);

            var sliderRect = GetVolumeSliderRect();
            assets.DrawRect(spriteBatch, sliderRect, SliderBg);
            assets.DrawRectOutline(spriteBatch, sliderRect, BorderColor, 1);
            int fillW = (int)(sliderRect.Width * GameSettings.MasterVolume);
            if (fillW > 0)
                assets.DrawRect(spriteBatch, new Rectangle(sliderRect.X, sliderRect.Y, fillW, sliderRect.Height), SliderFill);

            string volPct = $"{(int)(GameSettings.MasterVolume * 100)}%";
            assets.DrawMenuString(spriteBatch, volPct,
                new Vector2(sliderRect.Right + 12, sliderRect.Y + 2), TextPrimary, 0.85f);

            // ── Controls reference ──────────────────────────────────────────────
            int ctrlY = sectionY + 90;
            assets.DrawRect(spriteBatch, new Rectangle(px + 20, ctrlY - 8, (int)PanelW - 40, 2), BorderColor);
            assets.DrawMenuString(spriteBatch, "CONTROLS", new Vector2(px + 24, ctrlY + 4), TitleColor, 0.9f);

            var controls = new (string key, string action)[]
            {
                ("A / D  or  ← →",  "Move left / right"),
                ("Space",            "Jump (when grounded)"),
                ("Down + Space",     "Rappel down (rope)"),
                ("Left Click",       "Fire grappling hook (aim with mouse)"),
                ("C  (hold)",        "Climb climbable surfaces"),
                ("E",                "Interact / eat foraged item"),
                ("Escape",           "Pause game"),
            };

            int lineY = ctrlY + 34;
            foreach (var (key, action) in controls)
            {
                assets.DrawMenuString(spriteBatch, key,
                    new Vector2(px + 24, lineY), TextSecondary, 0.78f);
                assets.DrawMenuString(spriteBatch, action,
                    new Vector2(px + 220, lineY), TextPrimary, 0.78f);
                lineY += 26;
            }

            // Footer
            assets.DrawMenuStringCentered(spriteBatch,
                "Escape or Enter to go back",
                py + PanelH - 30f, HintColor, 0.75f);

            spriteBatch.End();
        }

        // ── Private helpers ────────────────────────────────────────────────────
        private Rectangle GetVolumeSliderRect()
        {
            int vw = GraphicsDevice.Viewport.Width;
            int vh = GraphicsDevice.Viewport.Height;
            int px = (int)(vw / 2f - PanelW / 2f);
            int py = (int)(vh / 2f - PanelH / 2f);
            return new Rectangle(px + 24, py + 90 + 56, 300, 20);
        }
    }

    /// <summary>
    /// Global game settings accessible from any screen.
    /// </summary>
    public static class GameSettings
    {
        public static float MasterVolume { get; set; } = 0.8f;
    }
}
