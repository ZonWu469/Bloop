using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Bloop.Core
{
    /// <summary>
    /// Manages window resolution and fullscreen toggling.
    ///
    /// The game now renders at the native window resolution (no virtual render target).
    /// A larger window shows more of the game world at 1:1 pixel scale.
    ///
    /// Kept as a thin wrapper so existing call sites compile without changes.
    /// BeginDraw / EndDraw / ToVirtualCoords are now no-ops / identity transforms.
    /// </summary>
    public class ResolutionManager : IDisposable
    {
        // ── Virtual (design) resolution — kept for backward-compat references ──
        public const int VirtualWidth  = 1280;
        public const int VirtualHeight = 720;

        // ── References ─────────────────────────────────────────────────────────
        private readonly GraphicsDevice        _graphicsDevice;
        private readonly GraphicsDeviceManager _gdm;

        // ── Fullscreen state ───────────────────────────────────────────────────
        public bool IsFullscreen => _gdm.IsFullScreen;

        // ── Window resize event ────────────────────────────────────────────────
        /// <summary>
        /// Fired when the window is resized. Subscribers (e.g. LightingSystem)
        /// should recreate any render targets that depend on window dimensions.
        /// </summary>
        public event Action<int, int>? WindowResized;

        // ── Constructor ────────────────────────────────────────────────────────
        public ResolutionManager(GraphicsDevice graphicsDevice, GraphicsDeviceManager gdm)
        {
            _graphicsDevice = graphicsDevice;
            _gdm            = gdm;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Call when the window is resized. Fires WindowResized so subsystems
        /// (e.g. LightingSystem) can recreate their render targets.
        /// </summary>
        public void OnWindowResize()
        {
            int w = _graphicsDevice.PresentationParameters.BackBufferWidth;
            int h = _graphicsDevice.PresentationParameters.BackBufferHeight;
            WindowResized?.Invoke(w, h);
        }

        /// <summary>
        /// Toggle fullscreen mode.
        /// </summary>
        public void ToggleFullscreen()
        {
            _gdm.ToggleFullScreen();
            OnWindowResize();
        }

        /// <summary>
        /// No-op: game now renders directly to the backbuffer.
        /// Kept so Game1.Draw() compiles without changes.
        /// </summary>
        public void BeginDraw()
        {
            _graphicsDevice.SetRenderTarget(null);
            _graphicsDevice.Clear(Color.Black);
        }

        /// <summary>
        /// No-op: nothing to blit. Kept so Game1.Draw() compiles without changes.
        /// </summary>
        public void EndDraw(SpriteBatch spriteBatch)
        {
            // Native rendering: the backbuffer is already the final output.
        }

        /// <summary>
        /// Identity transform: mouse coordinates are already in native window space.
        /// Kept so InputManager compiles without changes.
        /// </summary>
        public Vector2 ToVirtualCoords(Vector2 screenPos) => screenPos;

        /// <summary>Scale factor (always 1.0 in native rendering mode).</summary>
        public float Scale => 1f;

        /// <summary>Offset (always zero in native rendering mode).</summary>
        public Vector2 Offset => Vector2.Zero;

        /// <summary>Current actual window width.</summary>
        public int ActualWidth  => _graphicsDevice.PresentationParameters.BackBufferWidth;

        /// <summary>Current actual window height.</summary>
        public int ActualHeight => _graphicsDevice.PresentationParameters.BackBufferHeight;

        // ── IDisposable ────────────────────────────────────────────────────────
        public void Dispose()
        {
            // Nothing to dispose — no render target in native mode.
        }
    }
}
