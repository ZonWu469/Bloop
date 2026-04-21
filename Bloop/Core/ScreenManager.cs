using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Bloop.Core
{
    /// <summary>
    /// Manages a stack of game screens. The top screen is active and visible.
    /// Supports pushing new screens, popping the current screen, and replacing the stack.
    /// Screens below the top may still draw (for transparent overlays like pause menus).
    /// </summary>
    public class ScreenManager
    {
        // ── Fields ─────────────────────────────────────────────────────────────
        private readonly List<Screen> _screens     = new();
        private readonly List<Screen> _toAdd       = new();
        private readonly List<Screen> _toRemove    = new();

        private readonly GraphicsDevice _graphicsDevice;
        private readonly SpriteBatch    _spriteBatch;

        /// <summary>Shared input manager accessible to all screens.</summary>
        public InputManager Input { get; } = new InputManager();

        // ── Constructor ────────────────────────────────────────────────────────
        public ScreenManager(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch)
        {
            _graphicsDevice = graphicsDevice;
            _spriteBatch    = spriteBatch;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Push a new screen on top of the stack.</summary>
        public void Push(Screen screen)
        {
            _toAdd.Add(screen);
        }

        /// <summary>Pop the top screen from the stack.</summary>
        public void Pop()
        {
            if (_screens.Count > 0)
                _toRemove.Add(_screens[^1]);
        }

        /// <summary>Replace the entire stack with a single new screen.</summary>
        public void Replace(Screen screen)
        {
            // Queue all current screens for removal
            foreach (var s in _screens)
                _toRemove.Add(s);
            _toAdd.Add(screen);
        }

        /// <summary>The screen currently on top of the stack (or null if empty).</summary>
        public Screen? Current => _screens.Count > 0 ? _screens[^1] : null;

        // ── Update ─────────────────────────────────────────────────────────────
        public void Update(GameTime gameTime)
        {
            // Snapshot input once per frame
            Input.Update();

            // Apply pending additions and removals
            ApplyPendingChanges();

            // Update screens from top to bottom, stopping when a blocking screen is hit
            for (int i = _screens.Count - 1; i >= 0; i--)
            {
                var screen = _screens[i];
                if (!screen.IsActive) continue;

                screen.Update(gameTime);

                if (screen.BlocksUpdate) break;
            }
        }

        // ── Draw ───────────────────────────────────────────────────────────────
        public void Draw(GameTime gameTime)
        {
            // Find the lowest screen that should be drawn
            int startIndex = _screens.Count - 1;
            for (int i = _screens.Count - 1; i >= 0; i--)
            {
                if (_screens[i].BlocksDraw)
                {
                    startIndex = i;
                    break;
                }
            }

            // Draw from bottom to top so overlays appear on top
            for (int i = startIndex; i < _screens.Count; i++)
            {
                var screen = _screens[i];
                if (!screen.IsVisible) continue;

                screen.Draw(gameTime, _spriteBatch);
            }
        }

        // ── Private helpers ────────────────────────────────────────────────────
        private void ApplyPendingChanges()
        {
            // Remove screens first
            foreach (var screen in _toRemove)
            {
                screen.UnloadContent();
                _screens.Remove(screen);
            }
            _toRemove.Clear();

            // Then add new screens
            foreach (var screen in _toAdd)
            {
                screen.Initialize(this, _graphicsDevice);
                screen.LoadContent();
                _screens.Add(screen);
            }
            _toAdd.Clear();
        }
    }
}
