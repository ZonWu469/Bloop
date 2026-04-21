using Bloop.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Bloop.Screens
{
    /// <summary>
    /// Screen that prompts the player to enter a numeric seed for procedural generation.
    /// Validates input and launches the gameplay screen with the chosen seed.
    /// Uses MonoGame's Window.TextInput event for reliable text capture.
    /// </summary>
    public class SeedInputScreen : Screen
    {
        // ── State ──────────────────────────────────────────────────────────────
        private string _inputText    = "";
        private string _errorMessage = "";
        private float  _errorTimer   = 0f;
        private bool   _confirmed    = false;

        // ── Layout ─────────────────────────────────────────────────────────────
        private const float BoxY      = 280f;
        private const float BoxWidth  = 400f;
        private const float BoxHeight = 60f;

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color BgColor      = new Color(8,  10, 18);
        private static readonly Color TitleColor   = new Color(220, 180, 80);
        private static readonly Color BoxColor     = new Color(20,  30,  50);
        private static readonly Color BoxBorder    = new Color(60,  90, 140);
        private static readonly Color TextColor    = new Color(220, 240, 255);
        private static readonly Color HintColor    = new Color(100, 120, 140);
        private static readonly Color ErrorColor   = new Color(220,  80,  80);
        private static readonly Color ButtonColor  = new Color(40,  80, 120);
        private static readonly Color ButtonHover  = new Color(60, 110, 160);

        // ── Cursor blink ───────────────────────────────────────────────────────
        private float _cursorTimer;
        private bool  _cursorVisible = true;

        // ── TextInput event subscription ───────────────────────────────────────
        private bool _textInputSubscribed = false;

        public override bool BlocksDraw   => false; // show menu behind
        public override bool BlocksUpdate => true;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public override void LoadContent()
        {
            // Subscribe to the Window.TextInput event for reliable character capture.
            // This handles key repeat, IME, and all keyboard layouts correctly.
            if (!_textInputSubscribed)
            {
                Game1.Instance.Window.TextInput += OnTextInput;
                _textInputSubscribed = true;
            }
        }

        public override void UnloadContent()
        {
            if (_textInputSubscribed)
            {
                Game1.Instance.Window.TextInput -= OnTextInput;
                _textInputSubscribed = false;
            }
        }

        // ── TextInput handler ──────────────────────────────────────────────────

        private void OnTextInput(object? sender, TextInputEventArgs e)
        {
            // Guard: ignore input if this screen has already been confirmed or
            // if it is no longer the active screen (prevents bleed-through to
            // other screens after popping).
            if (_confirmed || !_textInputSubscribed) return;

            char c = e.Character;

            // Backspace: remove last character
            if (c == '\b')
            {
                if (_inputText.Length > 0)
                    _inputText = _inputText[..^1];
                return;
            }

            // Enter: confirm
            if (c == '\r' || c == '\n')
            {
                TryConfirm();
                return;
            }

            // Only accept digit characters, up to 10 digits
            if (char.IsDigit(c) && _inputText.Length < 10)
                _inputText += c;
        }

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Cursor blink
            _cursorTimer += dt;
            if (_cursorTimer >= 0.5f) { _cursorTimer = 0f; _cursorVisible = !_cursorVisible; }

            // Error message timeout
            if (_errorTimer > 0f) _errorTimer -= dt;

            var input = ScreenManager.Input;

            // Back / Cancel
            if (input.IsPausePressed())
            {
                ScreenManager.Pop();
                return;
            }

            // Confirm via Enter key (also handled in OnTextInput, but belt-and-suspenders)
            if (input.IsKeyPressed(Keys.Enter))
            {
                TryConfirm();
                return;
            }

            // Mouse click on Start button
            var mousePos = input.GetMousePosition();
            var btnRect  = GetStartButtonRect();
            if (btnRect.Contains((int)mousePos.X, (int)mousePos.Y) && input.IsLeftClickPressed())
                TryConfirm();
        }

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            var assets = Game1.Assets;
            int vw     = GraphicsDevice.Viewport.Width;
            int vh     = GraphicsDevice.Viewport.Height;

            spriteBatch.Begin();

            // Semi-transparent overlay over main menu
            assets.DrawRect(spriteBatch, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 200));

            // Panel
            int panelW = 500, panelH = 340;
            int panelX = (vw - panelW) / 2;
            int panelY = (vh - panelH) / 2;
            assets.DrawRect(spriteBatch, new Rectangle(panelX, panelY, panelW, panelH), BgColor);
            assets.DrawRectOutline(spriteBatch, new Rectangle(panelX, panelY, panelW, panelH), BoxBorder, 2);

            // Title
            assets.DrawMenuStringCentered(spriteBatch, "ENTER SEED", panelY + 30f, TitleColor, 1.5f);
            assets.DrawMenuStringCentered(spriteBatch, "Type any number (or leave blank for random)", panelY + 80f, HintColor, 0.85f);

            // Input box
            int boxX = (vw - (int)BoxWidth) / 2;
            int boxY = panelY + 120;
            assets.DrawRect(spriteBatch, new Rectangle(boxX, boxY, (int)BoxWidth, (int)BoxHeight), BoxColor);
            assets.DrawRectOutline(spriteBatch, new Rectangle(boxX, boxY, (int)BoxWidth, (int)BoxHeight), BoxBorder, 2);

            // Input text + cursor
            string display = _inputText + (_cursorVisible ? "|" : " ");
            Vector2 textSize = assets.MenuFont != null
                ? assets.MenuFont.MeasureString(display) : Vector2.Zero;
            Vector2 textPos = new Vector2(
                boxX + (BoxWidth - textSize.X) / 2f,
                boxY + (BoxHeight - textSize.Y) / 2f);
            assets.DrawMenuString(spriteBatch, display, textPos, TextColor, 1.2f);

            // Hint below box
            assets.DrawMenuStringCentered(spriteBatch, "Same seed = same world every run", boxY + (int)BoxHeight + 14f, HintColor, 0.8f);

            // Error message
            if (_errorTimer > 0f)
                assets.DrawMenuStringCentered(spriteBatch, _errorMessage, boxY + (int)BoxHeight + 40f, ErrorColor, 0.9f);

            // Start button
            var btnRect = GetStartButtonRect();
            var mousePos = ScreenManager.Input.GetMousePosition();
            bool hover = btnRect.Contains((int)mousePos.X, (int)mousePos.Y);
            assets.DrawRect(spriteBatch, btnRect, hover ? ButtonHover : ButtonColor);
            assets.DrawRectOutline(spriteBatch, btnRect, BoxBorder, 2);
            assets.DrawMenuStringCentered(spriteBatch, "START DESCENT", btnRect.Y + 12f, TextColor, 1.0f);

            // Back hint
            assets.DrawMenuStringCentered(spriteBatch, "Escape to go back", panelY + panelH - 30f, HintColor, 0.75f);

            spriteBatch.End();
        }

        // ── Private helpers ────────────────────────────────────────────────────
        private Rectangle GetStartButtonRect()
        {
            int vw = GraphicsDevice.Viewport.Width;
            int vh = GraphicsDevice.Viewport.Height;
            int panelY = (vh - 340) / 2;
            int btnW = 240, btnH = 48;
            return new Rectangle((vw - btnW) / 2, panelY + 230, btnW, btnH);
        }

        private void TryConfirm()
        {
            if (_confirmed) return;

            int seed;
            if (string.IsNullOrWhiteSpace(_inputText))
            {
                // Random seed
                seed = new System.Random().Next(100000, 999999);
            }
            else if (!int.TryParse(_inputText, out seed))
            {
                _errorMessage = "Please enter a valid number.";
                _errorTimer   = 3f;
                return;
            }

            _confirmed = true;
            // Replace the entire stack with the gameplay screen
            ScreenManager.Pop(); // pop seed input
            ScreenManager.Pop(); // pop main menu
            ScreenManager.Push(new GameplayScreen(seed, startDepth: 1));
        }

    }
}
