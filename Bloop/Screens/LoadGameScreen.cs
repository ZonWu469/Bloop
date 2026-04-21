using System.Collections.Generic;
using System.IO;
using Bloop.Core;
using Bloop.SaveLoad;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Bloop.Screens
{
    /// <summary>
    /// Screen that lists available JSON save files and lets the player
    /// select one to resume. Shows seed and current depth as a preview.
    /// </summary>
    public class LoadGameScreen : Screen
    {
        // ── State ──────────────────────────────────────────────────────────────
        private List<SaveFileEntry> _saves = new();
        private int   _selectedIndex = 0;
        private string _statusMessage = "";
        private float  _statusTimer   = 0f;

        // ── Layout ─────────────────────────────────────────────────────────────
        private const float ListStartY  = 200f;
        private const float EntryHeight = 70f;
        private const float EntryWidth  = 560f;

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color BgColor      = new Color(8,  10, 18);
        private static readonly Color TitleColor   = new Color(220, 180, 80);
        private static readonly Color EntryNormal  = new Color(20,  30,  50);
        private static readonly Color EntryHover   = new Color(40,  60,  90);
        private static readonly Color BorderColor  = new Color(60,  90, 140);
        private static readonly Color TextPrimary  = new Color(220, 240, 255);
        private static readonly Color TextSecondary= new Color(120, 150, 180);
        private static readonly Color HintColor    = new Color(100, 120, 140);
        private static readonly Color ErrorColor   = new Color(220,  80,  80);
        private static readonly Color SuccessColor = new Color( 80, 200, 120);

        public override bool BlocksDraw   => true;
        public override bool BlocksUpdate => true;

        public override void LoadContent()
        {
            RefreshSaveList();
        }

        public override void Update(GameTime gameTime)
        {
            float dt  = (float)gameTime.ElapsedGameTime.TotalSeconds;
            var input = ScreenManager.Input;

            if (_statusTimer > 0f) _statusTimer -= dt;

            // Back
            if (input.IsPausePressed())
            {
                ScreenManager.Pop();
                return;
            }

            if (_saves.Count == 0) return;

            // Navigate
            if (input.IsKeyPressed(Keys.Up) || input.IsKeyPressed(Keys.W))
                _selectedIndex = (_selectedIndex - 1 + _saves.Count) % _saves.Count;
            if (input.IsKeyPressed(Keys.Down) || input.IsKeyPressed(Keys.S))
                _selectedIndex = (_selectedIndex + 1) % _saves.Count;

            // Load selected
            if (input.IsKeyPressed(Keys.Enter))
                LoadSelected();

            // Delete selected (with Delete key)
            if (input.IsKeyPressed(Keys.Delete))
                DeleteSelected();

            // Mouse
            var mousePos = input.GetMousePosition();
            for (int i = 0; i < _saves.Count; i++)
            {
                var rect = GetEntryRect(i);
                if (rect.Contains((int)mousePos.X, (int)mousePos.Y))
                {
                    _selectedIndex = i;
                    if (input.IsLeftClickPressed()) LoadSelected();
                }
            }
        }

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            var assets = Game1.Assets;
            int vw     = GraphicsDevice.Viewport.Width;
            int vh     = GraphicsDevice.Viewport.Height;

            spriteBatch.Begin();

            assets.DrawRect(spriteBatch, new Rectangle(0, 0, vw, vh), BgColor);

            // Title
            assets.DrawMenuStringCentered(spriteBatch, "LOAD GAME", 60f, TitleColor, 1.8f);
            assets.DrawRect(spriteBatch, new Rectangle(vw / 2 - 200, 120, 400, 2), BorderColor);

            if (_saves.Count == 0)
            {
                assets.DrawMenuStringCentered(spriteBatch, "No save files found.", vh / 2f - 20f, TextSecondary, 1.0f);
                assets.DrawMenuStringCentered(spriteBatch, "Start a new game from the main menu.", vh / 2f + 20f, HintColor, 0.85f);
            }
            else
            {
                for (int i = 0; i < _saves.Count; i++)
                {
                    var  entry    = _saves[i];
                    var  rect     = GetEntryRect(i);
                    bool selected = i == _selectedIndex;

                    assets.DrawRect(spriteBatch, rect, selected ? EntryHover : EntryNormal);
                    assets.DrawRectOutline(spriteBatch, rect, BorderColor, 2);

                    if (selected)
                        assets.DrawRect(spriteBatch, new Rectangle(rect.X, rect.Y, 4, rect.Height), TitleColor);

                    // Save file name
                    assets.DrawMenuString(spriteBatch, entry.FileName,
                        new Vector2(rect.X + 16, rect.Y + 8), TextPrimary, 0.9f);

                    // Seed + depth info
                    string info = $"Seed: {entry.Seed}   |   Depth: {entry.CurrentDepth}   |   {entry.SaveDate}";
                    assets.DrawMenuString(spriteBatch, info,
                        new Vector2(rect.X + 16, rect.Y + 36), TextSecondary, 0.75f);
                }
            }

            // Status message
            if (_statusTimer > 0f)
            {
                Color col = _statusMessage.StartsWith("Error") ? ErrorColor : SuccessColor;
                assets.DrawMenuStringCentered(spriteBatch, _statusMessage, vh - 80f, col, 0.9f);
            }

            // Footer hints
            assets.DrawMenuStringCentered(spriteBatch,
                "Enter to load  |  Delete to remove  |  Escape to go back",
                vh - 40f, HintColor, 0.75f);

            spriteBatch.End();
        }

        // ── Private helpers ────────────────────────────────────────────────────
        private Rectangle GetEntryRect(int index)
        {
            int vw = GraphicsDevice.Viewport.Width;
            int x  = (int)(vw / 2f - EntryWidth / 2f);
            int y  = (int)(ListStartY + index * (EntryHeight + 8));
            return new Rectangle(x, y, (int)EntryWidth, (int)EntryHeight);
        }

        private void RefreshSaveList()
        {
            _saves.Clear();
            var files = SaveManager.GetSaveFiles();
            foreach (var file in files)
            {
                var data = SaveManager.Load(file);
                if (data != null)
                {
                    _saves.Add(new SaveFileEntry
                    {
                        FilePath     = file,
                        FileName     = Path.GetFileNameWithoutExtension(file),
                        Seed         = data.Seed,
                        CurrentDepth = data.CurrentDepth,
                        SaveDate     = data.SaveDate
                    });
                }
            }
            _selectedIndex = 0;
        }

        private void LoadSelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _saves.Count) return;
            var entry = _saves[_selectedIndex];
            var data  = SaveManager.Load(entry.FilePath);
            if (data == null)
            {
                _statusMessage = "Error: Could not load save file.";
                _statusTimer   = 3f;
                return;
            }

            // Replace stack with gameplay screen loaded from save
            ScreenManager.Pop(); // pop load screen
            ScreenManager.Pop(); // pop main menu
            ScreenManager.Push(new GameplayScreen(data.Seed, data.CurrentDepth, data));
        }

        private void DeleteSelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _saves.Count) return;
            var entry = _saves[_selectedIndex];
            try
            {
                File.Delete(entry.FilePath);
                _statusMessage = $"Deleted: {entry.FileName}";
                _statusTimer   = 2f;
                RefreshSaveList();
            }
            catch
            {
                _statusMessage = "Error: Could not delete save file.";
                _statusTimer   = 3f;
            }
        }

        // ── Inner types ────────────────────────────────────────────────────────
        private class SaveFileEntry
        {
            public string FilePath     = "";
            public string FileName     = "";
            public int    Seed         = 0;
            public int    CurrentDepth = 0;
            public string SaveDate     = "";
        }
    }
}
