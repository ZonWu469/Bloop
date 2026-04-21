using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using Bloop.Core;
using Bloop.Gameplay;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.World
{
    /// <summary>
    /// Abstract base class for all interactive world objects (platforms, hazards,
    /// collectibles, vines, etc.). Each object owns an optional Aether body/sensor
    /// and handles its own update and draw logic.
    ///
    /// The AetherWorld reference is stored in the base class so subclasses can
    /// call Destroy() without needing to pass the world each time.
    /// </summary>
    public abstract class WorldObject
    {
        // ── Physics ────────────────────────────────────────────────────────────
        /// <summary>The Aether body for this object (may be null for purely visual objects).</summary>
        protected Body? Body { get; set; }

        /// <summary>Reference to the physics world, used for body removal on Destroy().</summary>
        protected readonly AetherWorld World;

        // ── State ──────────────────────────────────────────────────────────────
        /// <summary>Whether this object is active and should update/draw.</summary>
        public bool IsActive { get; protected set; } = true;

        /// <summary>Whether this object has been marked for removal from the level.</summary>
        public bool IsDestroyed { get; private set; } = false;

        // ── Position ───────────────────────────────────────────────────────────
        /// <summary>
        /// World-space pixel position of the object center.
        /// If a Body is present, reads from it; otherwise uses the stored position.
        /// </summary>
        public Vector2 PixelPosition
        {
            get => Body != null
                ? Bloop.Physics.PhysicsManager.ToPixels(Body.Position)
                : _pixelPosition;
            protected set => _pixelPosition = value;
        }
        private Vector2 _pixelPosition;

        // ── Constructor ────────────────────────────────────────────────────────
        protected WorldObject(Vector2 pixelPosition, AetherWorld world)
        {
            _pixelPosition = pixelPosition;
            World          = world;
        }

        // ── Abstract interface ─────────────────────────────────────────────────

        /// <summary>Update this object's logic. Called once per frame.</summary>
        public abstract void Update(GameTime gameTime);

        /// <summary>
        /// Draw this object as a placeholder colored rectangle.
        /// Called inside a SpriteBatch.Begin/End block with camera transform applied.
        /// </summary>
        public abstract void Draw(SpriteBatch spriteBatch, AssetManager assets);

        /// <summary>
        /// Returns the pixel-space bounding rectangle for viewport culling.
        /// Override in subclasses to return a tighter bounds.
        /// Returns Rectangle.Empty if the object has no meaningful bounds (always drawn).
        /// </summary>
        public virtual Rectangle GetBounds()
        {
            // Default: 64×64 box centered on pixel position
            return new Rectangle(
                (int)(PixelPosition.X - 32),
                (int)(PixelPosition.Y - 32),
                64, 64);
        }

        // ── Collision callbacks ────────────────────────────────────────────────

        /// <summary>
        /// If true, Level.Update performs an AABB overlap test with the player
        /// each frame and invokes OnPlayerContact when they intersect.
        /// Reliable pickup path for collectibles whose sensor callbacks don't fire.
        /// </summary>
        public virtual bool WantsPlayerContact => false;

        /// <summary>Called when the player body begins overlapping this object's sensor.</summary>
        public virtual void OnPlayerContact(Player player) { }

        /// <summary>Called when the player body stops overlapping this object's sensor.</summary>
        public virtual void OnPlayerSeparate(Player player) { }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        /// <summary>
        /// Mark this object for removal. The Level will clean it up next frame.
        /// Also removes the Aether body from the world if present.
        /// </summary>
        protected void Destroy()
        {
            if (IsDestroyed) return;
            IsDestroyed = true;
            IsActive    = false;

            if (Body != null)
            {
                World.Remove(Body);
                Body = null;
            }
        }

        /// <summary>
        /// Overload kept for backward compatibility — calls Destroy() ignoring the world parameter.
        /// Prefer the no-argument Destroy() in new code.
        /// </summary>
        protected void Destroy(AetherWorld _)
        {
            Destroy();
        }

        // ── Helper: draw a placeholder rectangle ───────────────────────────────
        protected void DrawPlaceholder(SpriteBatch spriteBatch, AssetManager assets,
            int widthPx, int heightPx, Color fillColor, Color? outlineColor = null)
        {
            var rect = new Rectangle(
                (int)(PixelPosition.X - widthPx  / 2f),
                (int)(PixelPosition.Y - heightPx / 2f),
                widthPx, heightPx);

            assets.DrawRect(spriteBatch, rect, fillColor);

            if (outlineColor.HasValue)
                assets.DrawRectOutline(spriteBatch, rect, outlineColor.Value, 1);
        }

        // ── Helper: check if player is within a given pixel radius ─────────────
        protected static bool IsPlayerNearby(Player player, Vector2 objectPixelPos, float radiusPx)
        {
            return Vector2.DistanceSquared(player.PixelPosition, objectPixelPos) <= radiusPx * radiusPx;
        }

        // ── Helper: check if player's lantern is illuminating this object ──────
        /// <summary>
        /// Returns true if the player is within lantern radius AND has fuel.
        /// Used as a placeholder until the Phase 9 HLSL lighting system is in place.
        /// </summary>
        protected static bool IsLitByLantern(Player player, Vector2 objectPixelPos,
            float lanternRadiusPx = 200f)
        {
            return player.Stats.HasLanternFuel &&
                   IsPlayerNearby(player, objectPixelPos, lanternRadiusPx);
        }
    }
}
