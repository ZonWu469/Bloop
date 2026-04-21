using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using Bloop.Core;
using Bloop.Gameplay;
using Bloop.Physics;
using Bloop.Rendering;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Objects
{
    /// <summary>
    /// A standalone climbable surface object placed by the procedural generator.
    /// Provides a static Aether body with CollisionCategories.Climbable so the
    /// existing PlayerController climbing logic (C key, IgnoreGravity) works automatically.
    ///
    /// This complements the TileType.Climbable tiles already in the TileMap —
    /// it is used for procedurally-placed climbable patches on walls that are
    /// NOT part of the tile grid.
    ///
    /// Visual: dark green rectangle with vertical stripe pattern.
    /// </summary>
    public class ClimbableSurface : WorldObject
    {
        // ── Dimensions ─────────────────────────────────────────────────────────
        private const int TileSize = 32;

        // ── Dimensions (set from TileHeight) ──────────────────────────────────
        private readonly int _heightPx;

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Create a climbable surface centered at pixelPosition.
        /// tileHeight: number of tiles tall (1–8).
        /// </summary>
        public ClimbableSurface(Vector2 pixelPosition, AetherWorld world, int tileHeight = 1)
            : base(pixelPosition, world)
        {
            _heightPx = tileHeight * TileSize;

            Body = BodyFactory.CreateStaticRect(
                world, pixelPosition,
                TileSize, _heightPx,
                CollisionCategories.Climbable);

            Body.Tag = this;
        }

        // ── Update ─────────────────────────────────────────────────────────────

        public override void Update(GameTime gameTime)
        {
            // Static object — no update logic needed
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        public override void Draw(SpriteBatch spriteBatch, AssetManager assets)
        {
            int tileHash = (int)(PixelPosition.X * 3 + PixelPosition.Y * 7);
            WorldObjectRenderer.DrawClimbableSurface(spriteBatch, assets, PixelPosition, _heightPx, tileHash);
        }

        // ── Bounds ─────────────────────────────────────────────────────────────

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - TileSize / 2f),
            (int)(PixelPosition.Y - _heightPx / 2f),
            TileSize, _heightPx);
    }
}
