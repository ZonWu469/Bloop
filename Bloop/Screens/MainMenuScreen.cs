using Bloop.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Bloop.Screens
{
    /// <summary>
    /// Main menu screen. Shows the game title and navigation buttons:
    /// Start Game, Load Game, Options, and Quit.
    /// </summary>
    public class MainMenuScreen : Screen
    {
        // ── Layout constants ───────────────────────────────────────────────────
        private const float TitleY      = 140f;
        private const float SubtitleY   = 220f;
        private const float ButtonStartY = 320f;
        private const float ButtonSpacing = 70f;
        private const float ButtonWidth  = 320f;
        private const float ButtonHeight = 52f;

        // ── Menu items ─────────────────────────────────────────────────────────
        private readonly string[] _menuItems = { "Start Game", "Load Game", "Options", "Quit" };
        private int _selectedIndex = 0;

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color BackgroundColor  = new Color(8,  10, 18);
        private static readonly Color TitleColor       = new Color(220, 180, 80);
        private static readonly Color SubtitleColor    = new Color(120, 140, 160);
        private static readonly Color ButtonNormal     = new Color(30,  40,  60);
        private static readonly Color ButtonHover      = new Color(50,  70, 110);
        private static readonly Color ButtonBorder     = new Color(60,  90, 140);
        private static readonly Color TextNormal       = new Color(180, 200, 220);
        private static readonly Color TextSelected     = new Color(255, 255, 255);

        // ── Ambient flicker ────────────────────────────────────────────────────
        private float _flickerTimer;

        // ── Screen overrides ───────────────────────────────────────────────────
        public override bool BlocksDraw   => true;
        public override bool BlocksUpdate => true;

        public override void Update(GameTime gameTime)
        {
            var input = ScreenManager.Input;
            float dt  = (float)gameTime.ElapsedGameTime.TotalSeconds;

            _flickerTimer += dt;

            // Navigate menu
            if (input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.Up) ||
                input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.W))
            {
                _selectedIndex = (_selectedIndex - 1 + _menuItems.Length) % _menuItems.Length;
            }
            if (input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.Down) ||
                input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.S))
            {
                _selectedIndex = (_selectedIndex + 1) % _menuItems.Length;
            }

            // Confirm selection
            if (input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.Enter) ||
                input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.Space))
            {
                ActivateSelection();
            }

            // Mouse hover
            var mousePos = input.GetMousePosition();
            for (int i = 0; i < _menuItems.Length; i++)
            {
                var rect = GetButtonRect(i);
                if (rect.Contains((int)mousePos.X, (int)mousePos.Y))
                {
                    _selectedIndex = i;
                    if (input.IsLeftClickPressed())
                        ActivateSelection();
                }
            }
        }

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            var assets = Game1.Assets;
            int vw     = GraphicsDevice.Viewport.Width;
            int vh     = GraphicsDevice.Viewport.Height;

            spriteBatch.Begin();

            // Background
            assets.DrawRect(spriteBatch, new Rectangle(0, 0, vw, vh), BackgroundColor);

            // Subtle scanline effect (every other row slightly darker)
            for (int y = 0; y < vh; y += 4)
                assets.DrawRect(spriteBatch, new Rectangle(0, y, vw, 1), new Color(0, 0, 0, 30));

            // Title
            float flicker = 0.92f + 0.08f * (float)System.Math.Sin(_flickerTimer * 2.3f);
            Color titleCol = new Color(
                (int)(TitleColor.R * flicker),
                (int)(TitleColor.G * flicker),
                (int)(TitleColor.B * flicker));
            assets.DrawMenuStringCentered(spriteBatch, "DESCENT INTO THE DEEP", TitleY, titleCol, 2.0f);
            assets.DrawMenuStringCentered(spriteBatch, "A Cave Descent Survival Platformer", SubtitleY, SubtitleColor, 1.0f);

            // Decorative horizontal rule
            int ruleY = (int)SubtitleY + 36;
            assets.DrawRect(spriteBatch, new Rectangle(vw / 2 - 200, ruleY, 400, 2), ButtonBorder);

            // Menu buttons
            for (int i = 0; i < _menuItems.Length; i++)
            {
                var  rect     = GetButtonRect(i);
                bool selected = i == _selectedIndex;

                // Button background
                assets.DrawRect(spriteBatch, rect, selected ? ButtonHover : ButtonNormal);
                assets.DrawRectOutline(spriteBatch, rect, ButtonBorder, 2);

                // Selection indicator
                if (selected)
                {
                    assets.DrawRect(spriteBatch,
                        new Rectangle(rect.X, rect.Y, 4, rect.Height),
                        TitleColor);
                }

                // Button text
                Color textColor = selected ? TextSelected : TextNormal;
                Vector2 textSize = Game1.Assets.MenuFont != null
                    ? Game1.Assets.MenuFont.MeasureString(_menuItems[i])
                    : Vector2.Zero;
                Vector2 textPos = new Vector2(
                    rect.X + (rect.Width  - textSize.X) / 2f,
                    rect.Y + (rect.Height - textSize.Y) / 2f);
                assets.DrawMenuString(spriteBatch, _menuItems[i], textPos, textColor);
            }

            // Footer hint
            assets.DrawMenuStringCentered(spriteBatch,
                "Arrow Keys / WASD to navigate  |  Enter / Click to select",
                vh - 40f, SubtitleColor, 0.75f);

            spriteBatch.End();
        }

        // ── Private helpers ────────────────────────────────────────────────────
        private Rectangle GetButtonRect(int index)
        {
            int vw = GraphicsDevice.Viewport.Width;
            int x  = (int)(vw / 2f - ButtonWidth / 2f);
            int y  = (int)(ButtonStartY + index * ButtonSpacing);
            return new Rectangle(x, y, (int)ButtonWidth, (int)ButtonHeight);
        }

        private void ActivateSelection()
        {
            switch (_selectedIndex)
            {
                case 0: // Start Game → seed input
                    ScreenManager.Push(new SeedInputScreen());
                    break;
                case 1: // Load Game
                    ScreenManager.Push(new LoadGameScreen());
                    break;
                case 2: // Options
                    ScreenManager.Push(new OptionsScreen());
                    break;
                case 3: // Quit
                    System.Environment.Exit(0);
                    break;
            }
        }
    }
}
