using Bloop.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Bloop.Screens
{
    /// <summary>
    /// Pause overlay drawn on top of the gameplay screen.
    /// Offers Resume, Save, Options, and Quit to Main Menu.
    /// </summary>
    public class PauseScreen : Screen
    {
        // ── State ──────────────────────────────────────────────────────────────
        private int _selectedIndex = 0;
        private readonly string[] _menuItems = { "Resume", "Save Game", "Options", "Quit to Menu" };

        // ── Layout ─────────────────────────────────────────────────────────────
        private const float PanelW      = 360f;
        private const float PanelH      = 320f;
        private const float ButtonW     = 280f;
        private const float ButtonH     = 48f;
        private const float ButtonStart = 120f;
        private const float ButtonGap   = 58f;

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color OverlayColor  = new Color(0, 0, 0, 160);
        private static readonly Color PanelColor    = new Color(10, 14, 24);
        private static readonly Color BorderColor   = new Color(60, 90, 140);
        private static readonly Color TitleColor    = new Color(220, 180, 80);
        private static readonly Color ButtonNormal  = new Color(25, 38, 60);
        private static readonly Color ButtonHover   = new Color(45, 68, 110);
        private static readonly Color TextNormal    = new Color(180, 200, 220);
        private static readonly Color TextSelected  = new Color(255, 255, 255);
        private static readonly Color HintColor     = new Color(100, 120, 140);

        // ── Callbacks ──────────────────────────────────────────────────────────
        private readonly System.Action? _onSave;

        public PauseScreen(System.Action? onSave = null)
        {
            _onSave = onSave;
        }

        // Overlay — does NOT block the gameplay screen from drawing
        public override bool BlocksDraw   => false;
        public override bool BlocksUpdate => true;

        public override void Update(GameTime gameTime)
        {
            var input = ScreenManager.Input;

            // Resume on Escape
            if (input.IsPausePressed())
            {
                ScreenManager.Pop();
                return;
            }

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

            spriteBatch.Begin();

            // Dim overlay over gameplay
            assets.DrawRect(spriteBatch, new Rectangle(0, 0, vw, vh), OverlayColor);

            // Panel
            int px = (int)(vw / 2f - PanelW / 2f);
            int py = (int)(vh / 2f - PanelH / 2f);
            assets.DrawRect(spriteBatch, new Rectangle(px, py, (int)PanelW, (int)PanelH), PanelColor);
            assets.DrawRectOutline(spriteBatch, new Rectangle(px, py, (int)PanelW, (int)PanelH), BorderColor, 2);

            // Title
            assets.DrawMenuStringCentered(spriteBatch, "PAUSED", py + 28f, TitleColor, 1.5f);
            assets.DrawRect(spriteBatch, new Rectangle(px + 20, py + 70, (int)PanelW - 40, 2), BorderColor);

            // Buttons
            for (int i = 0; i < _menuItems.Length; i++)
            {
                var  rect     = GetButtonRect(i);
                bool selected = i == _selectedIndex;

                assets.DrawRect(spriteBatch, rect, selected ? ButtonHover : ButtonNormal);
                assets.DrawRectOutline(spriteBatch, rect, BorderColor, 1);

                if (selected)
                    assets.DrawRect(spriteBatch, new Rectangle(rect.X, rect.Y, 3, rect.Height), TitleColor);

                Color textColor = selected ? TextSelected : TextNormal;
                Vector2 textSize = Game1.Assets.MenuFont != null
                    ? Game1.Assets.MenuFont.MeasureString(_menuItems[i]) : Vector2.Zero;
                Vector2 textPos = new Vector2(
                    rect.X + (rect.Width  - textSize.X) / 2f,
                    rect.Y + (rect.Height - textSize.Y) / 2f);
                assets.DrawMenuString(spriteBatch, _menuItems[i], textPos, textColor);
            }

            // Hint
            assets.DrawMenuStringCentered(spriteBatch, "Escape to resume", py + PanelH - 24f, HintColor, 0.72f);

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
                case 0: // Resume
                    ScreenManager.Pop();
                    break;
                case 1: // Save Game
                    _onSave?.Invoke();
                    break;
                case 2: // Options
                    ScreenManager.Push(new OptionsScreen());
                    break;
                case 3: // Quit to Menu
                    // Pop pause + gameplay, push main menu
                    ScreenManager.Pop();
                    ScreenManager.Pop();
                    ScreenManager.Push(new MainMenuScreen());
                    break;
            }
        }
    }
}
