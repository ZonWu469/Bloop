using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Bloop.Core
{
    /// <summary>
    /// Abstract base class for all game screens (menus, gameplay, pause, etc.).
    /// Each screen manages its own update and draw logic.
    /// </summary>
    public abstract class Screen
    {
        /// <summary>Reference to the screen manager that owns this screen.</summary>
        protected ScreenManager ScreenManager { get; private set; } = null!;

        /// <summary>Reference to the MonoGame GraphicsDevice.</summary>
        protected GraphicsDevice GraphicsDevice { get; private set; } = null!;

        /// <summary>
        /// Whether this screen is active (receives Update calls).
        /// Screens below the top of the stack may be inactive.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Whether this screen is visible (receives Draw calls).
        /// Useful for transparent overlays (e.g., pause screen over gameplay).
        /// </summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// Whether this screen blocks screens below it from updating.
        /// True for full-screen screens; false for overlays.
        /// </summary>
        public virtual bool BlocksUpdate => true;

        /// <summary>
        /// Whether this screen blocks screens below it from drawing.
        /// True for opaque full-screen screens; false for transparent overlays.
        /// </summary>
        public virtual bool BlocksDraw => true;

        /// <summary>Called once when the screen is pushed onto the stack.</summary>
        public virtual void Initialize(ScreenManager screenManager, GraphicsDevice graphicsDevice)
        {
            ScreenManager  = screenManager;
            GraphicsDevice = graphicsDevice;
        }

        /// <summary>Called once after Initialize to load content.</summary>
        public virtual void LoadContent() { }

        /// <summary>Called once when the screen is popped from the stack.</summary>
        public virtual void UnloadContent() { }

        /// <summary>Called every frame when the screen is active.</summary>
        public abstract void Update(GameTime gameTime);

        /// <summary>Called every frame when the screen is visible.</summary>
        public abstract void Draw(GameTime gameTime, SpriteBatch spriteBatch);
    }
}
