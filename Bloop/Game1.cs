using Bloop.Core;
using Bloop.Lighting;
using Bloop.Screens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Bloop
{
    /// <summary>
    /// Root MonoGame game class. Initializes core systems and delegates all
    /// update/draw logic to the ScreenManager.
    ///
    /// Rendering pipeline (native resolution):
    ///   The game renders directly to the backbuffer at the actual window size.
    ///   A larger window shows more of the game world at 1:1 pixel scale.
    ///   No virtual render target or scaling is applied.
    /// </summary>
    public class Game1 : Game
    {
        // ── Singleton instance (for Window access from screens) ────────────────
        public static Game1 Instance { get; private set; } = null!;

        // ── MonoGame infrastructure ────────────────────────────────────────────
        private readonly GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch = null!;

        // ── Core systems ───────────────────────────────────────────────────────
        public static AssetManager      Assets     { get; private set; } = null!;
        public static ScreenManager     Screens    { get; private set; } = null!;
        public static ResolutionManager Resolution { get; private set; } = null!;
        public static LightingSystem?   Lighting   { get; private set; }

        // ── Taskbar margin (pixels reserved for OS taskbar in windowed mode) ───
        private const int TaskbarMargin = 72;

        // ── Constructor ────────────────────────────────────────────────────────
        public Game1()
        {
            Instance = this;

            // ── Detect display size and set backbuffer BEFORE base.Initialize() ──
            // base.Initialize() calls LoadContent() internally, so the backbuffer
            // must be the correct size before ResolutionManager is created there.
            var display = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            int targetW = display.Width;
            int targetH = display.Height - TaskbarMargin;

            // Ensure minimum size matches the design resolution
            if (targetW < ResolutionManager.VirtualWidth)  targetW = ResolutionManager.VirtualWidth;
            if (targetH < ResolutionManager.VirtualHeight) targetH = ResolutionManager.VirtualHeight;

            _graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth  = targetW,
                PreferredBackBufferHeight = targetH,
                IsFullScreen              = false
            };

            Content.RootDirectory = "Content";
            IsMouseVisible        = true;

            // Target 60 FPS
            IsFixedTimeStep         = true;
            TargetElapsedTime       = System.TimeSpan.FromSeconds(1.0 / 60.0);
            _graphics.SynchronizeWithVerticalRetrace = true;

            // Allow window resizing
            Window.AllowUserResizing = true;
        }

        // ── Initialize ─────────────────────────────────────────────────────────
        protected override void Initialize()
        {
            base.Initialize(); // calls LoadContent() internally

            // Wire up window resize event after base.Initialize() creates the window
            Window.ClientSizeChanged += OnWindowSizeChanged;

            // Position the window at the top-left so the taskbar is visible at the bottom
            Window.Position = new Microsoft.Xna.Framework.Point(0, 0);

            // Notify subsystems of the final backbuffer size
            Resolution?.OnWindowResize();
        }

        // ── LoadContent ────────────────────────────────────────────────────────
        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            // Initialize resolution manager (thin wrapper — no render target in native mode)
            Resolution = new ResolutionManager(GraphicsDevice, _graphics);

            // Initialize asset manager
            Assets = new AssetManager(GraphicsDevice);
            Assets.Initialize();

            // Load SpriteFonts (optional — UI degrades gracefully without them)
            SpriteFont? menuFont = null, gameFont = null;
            try { menuFont = Content.Load<SpriteFont>("Fonts/MenuFont"); } catch { }
            try { gameFont = Content.Load<SpriteFont>("Fonts/GameFont"); } catch { }
            Assets.LoadFonts(menuFont, gameFont);

            // Initialize lighting system at the actual backbuffer size
            int bbW = GraphicsDevice.PresentationParameters.BackBufferWidth;
            int bbH = GraphicsDevice.PresentationParameters.BackBufferHeight;
            Lighting = new LightingSystem(GraphicsDevice, Assets, bbW, bbH);

            // Subscribe LightingSystem to window resize events
            Resolution.WindowResized += (w, h) => Lighting?.OnResize(w, h);

            // Initialize screen manager and push the main menu
            Screens = new ScreenManager(GraphicsDevice, _spriteBatch);

            // Wire resolution manager into input manager (identity transform in native mode)
            Screens.Input.SetResolutionManager(Resolution);

            Screens.Push(new MainMenuScreen());
        }

        // ── Update ─────────────────────────────────────────────────────────────
        protected override void Update(GameTime gameTime)
        {
            // Handle fullscreen toggle (F11) — check before ScreenManager.Update()
            // so the key-press is detected before input state is advanced
            if (Resolution != null && Screens != null &&
                Screens.Input.IsFullscreenTogglePressed())
            {
                Resolution.ToggleFullscreen();
            }

            Screens?.Update(gameTime);
            base.Update(gameTime);
        }

        // ── Draw ───────────────────────────────────────────────────────────────
        protected override void Draw(GameTime gameTime)
        {
            // Native rendering: draw directly to the backbuffer at actual window size.
            // No virtual render target or scaling — a larger window shows more of the world.
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.Black);

            Screens.Draw(gameTime);

            base.Draw(gameTime);
        }

        // ── Cleanup ────────────────────────────────────────────────────────────
        protected override void UnloadContent()
        {
            Assets?.Dispose();
            Lighting?.Dispose();
            Resolution?.Dispose();
            base.UnloadContent();
        }

        // ── Window resize handler ──────────────────────────────────────────────
        private void OnWindowSizeChanged(object? sender, System.EventArgs e)
        {
            Resolution?.OnWindowResize();
        }
    }
}
