using Bloop.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Bloop.Screens
{
    /// <summary>
    /// Game over / death screen. Shows cause of death, depth reached,
    /// and offers Restart (same seed) or Return to Main Menu.
    /// </summary>
    public class GameOverScreen : Screen
    {
        // ── State ──────────────────────────────────────────────────────────────
        private readonly int    _seed;
        private readonly int    _depthReached;
        private readonly string _causeOfDeath;
        private int             _selectedIndex = 0;
        private float           _fadeIn        = 0f;

        private readonly string[] _menuItems = { "Try Again (same seed)", "New Seed", "Main Menu" };

        // ── Layout ─────────────────────────────────────────────────────────────
        private const float PanelW      = 480f;
        private const float PanelH      = 360f;
        private const float ButtonW     = 340f;
        private const float ButtonH     = 48f;
        private const float ButtonStart = 180f;
        private const float ButtonGap   = 58f;

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color BgColor      = new Color(4,   4,   8);
        private static readonly Color PanelColor   = new Color(12,  8,  16);
        private static readonly Color BorderColor  = new Color(120, 30,  30);
        private static readonly Color TitleColor   = new Color(220,  60,  60);
        private static readonly Color SubColor     = new Color(180, 140, 100);
        private static readonly Color ButtonNormal = new Color(30,  15,  20);
        private static readonly Color ButtonHover  = new Color(60,  25,  35);
        private static readonly Color TextNormal   = new Color(180, 160, 160);
        private static readonly Color TextSelected = new Color(255, 220, 220);
        private static readonly Color HintColor    = new Color(80,  70,  70);

        // ── Constructor ────────────────────────────────────────────────────────
        public GameOverScreen(int seed, int depthReached, string causeOfDeath = "Unknown")
        {
            _seed         = seed;
            _depthReached = depthReached;
            _causeOfDeath = causeOfDeath;
        }

        public override bool BlocksDraw   => true;
        public override bool BlocksUpdate => true;

        public override void Update(GameTime gameTime)
        {
            float dt  = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _fadeIn   = MathHelper.Clamp(_fadeIn + dt * 1.5f, 0f, 1f);

            var input = ScreenManager.Input;

            // Navigate
            if (input.IsKeyPressed(Keys.Up) || input.IsKeyPressed(Keys.W))
                _selectedIndex = (_selectedIndex - 1 + _menuItems.Length) % _menuItems.Length;
            if (input.IsKeyPressed(Keys.Down) || input.IsKeyPressed(Keys.S))
                _selectedIndex = (_selectedIndex + 1) % _menuItems.Length;

            // Confirm
            if (input.IsKeyPressed(Keys.Enter) || input.IsKeyPressed(Keys.Space))
                ActivateSelection();

            // Mouse
            var mousePos = input.GetMousePosition();
            for (int i = 0; i < _menuItems.Length; i++)
            {
                var rect = GetButtonRect(i);
                if (rect.Contains((int)mousePos.X, (int)mousePos.Y))
                {
                    _selectedIndex = i;
                    if (input.IsLeftClickPressed()) ActivateSelection();
                }
            }
        }

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            var assets = Game1.Assets;
            int vw     = GraphicsDevice.Viewport.Width;
            int vh     = GraphicsDevice.Viewport.Height;

            byte alpha = (byte)(_fadeIn * 255);

            spriteBatch.Begin();

            // Background
            assets.DrawRect(spriteBatch, new Rectangle(0, 0, vw, vh), BgColor);

            // Vignette effect (dark corners)
            for (int i = 0; i < 8; i++)
            {
                int margin = i * 20;
                byte a = (byte)(40 - i * 5);
                assets.DrawRectOutline(spriteBatch,
                    new Rectangle(margin, margin, vw - margin * 2, vh - margin * 2),
                    new Color((byte)80, (byte)0, (byte)0, a), 20);
            }

            // Panel
            int px = (int)(vw / 2f - PanelW / 2f);
            int py = (int)(vh / 2f - PanelH / 2f);
            assets.DrawRect(spriteBatch, new Rectangle(px, py, (int)PanelW, (int)PanelH),
                new Color((byte)PanelColor.R, (byte)PanelColor.G, (byte)PanelColor.B, alpha));
            assets.DrawRectOutline(spriteBatch, new Rectangle(px, py, (int)PanelW, (int)PanelH),
                new Color((byte)BorderColor.R, (byte)BorderColor.G, (byte)BorderColor.B, alpha), 2);

            // Title
            assets.DrawMenuStringCentered(spriteBatch, "YOU DIED", py + 24f,
                new Color((byte)TitleColor.R, (byte)TitleColor.G, (byte)TitleColor.B, alpha), 2.0f);

            // Cause of death
            assets.DrawMenuStringCentered(spriteBatch, _causeOfDeath, py + 80f,
                new Color((byte)SubColor.R, (byte)SubColor.G, (byte)SubColor.B, alpha), 0.9f);

            // Stats
            assets.DrawMenuStringCentered(spriteBatch, $"Depth Reached: {_depthReached}", py + 112f,
                new Color((byte)TextNormal.R, (byte)TextNormal.G, (byte)TextNormal.B, alpha), 0.85f);
            assets.DrawMenuStringCentered(spriteBatch, $"Seed: {_seed}", py + 138f,
                new Color((byte)HintColor.R, (byte)HintColor.G, (byte)HintColor.B, alpha), 0.8f);

            assets.DrawRect(spriteBatch,
                new Rectangle(px + 20, py + 162, (int)PanelW - 40, 1),
                new Color((byte)BorderColor.R, (byte)BorderColor.G, (byte)BorderColor.B, alpha));

            // Buttons
            for (int i = 0; i < _menuItems.Length; i++)
            {
                var  rect     = GetButtonRect(i);
                bool selected = i == _selectedIndex;

                Color btnCol = selected ? ButtonHover : ButtonNormal;
                assets.DrawRect(spriteBatch, rect,
                    new Color((byte)btnCol.R, (byte)btnCol.G, (byte)btnCol.B, alpha));
                assets.DrawRectOutline(spriteBatch, rect,
                    new Color((byte)BorderColor.R, (byte)BorderColor.G, (byte)BorderColor.B, alpha), 1);

                if (selected)
                    assets.DrawRect(spriteBatch,
                        new Rectangle(rect.X, rect.Y, 3, rect.Height),
                        new Color((byte)TitleColor.R, (byte)TitleColor.G, (byte)TitleColor.B, alpha));

                Color textColor = selected ? TextSelected : TextNormal;
                Vector2 textSize = Game1.Assets.MenuFont != null
                    ? Game1.Assets.MenuFont.MeasureString(_menuItems[i]) : Vector2.Zero;
                Vector2 textPos = new Vector2(
                    rect.X + (rect.Width  - textSize.X) / 2f,
                    rect.Y + (rect.Height - textSize.Y) / 2f);
                assets.DrawMenuString(spriteBatch, _menuItems[i], textPos,
                    new Color((byte)textColor.R, (byte)textColor.G, (byte)textColor.B, alpha));
            }

            spriteBatch.End();
        }

        // ── Private helpers ────────────────────────────────────────────────────
        private Rectangle GetButtonRect(int index)
        {
            int vw = GraphicsDevice.Viewport.Width;
            int vh = GraphicsDevice.Viewport.Height;
            int px = (int)(vw / 2f - PanelW / 2f);
            int py = (int)(vh / 2f - PanelH / 2f);
            int bx = (int)(vw / 2f - ButtonW / 2f);
            int by = (int)(py + ButtonStart + index * ButtonGap);
            return new Rectangle(bx, by, (int)ButtonW, (int)ButtonH);
        }

        private void ActivateSelection()
        {
            switch (_selectedIndex)
            {
                case 0: // Try again — same seed, depth 1
                    ScreenManager.Replace(new GameplayScreen(_seed, startDepth: 1));
                    break;
                case 1: // New seed
                    ScreenManager.Replace(new MainMenuScreen());
                    ScreenManager.Push(new SeedInputScreen());
                    break;
                case 2: // Main menu
                    ScreenManager.Replace(new MainMenuScreen());
                    break;
            }
        }
    }
}
