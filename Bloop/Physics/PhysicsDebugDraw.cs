using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Collision.Shapes;
using nkast.Aether.Physics2D.Common;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Physics
{
    /// <summary>
    /// Debug renderer that draws all Aether physics bodies as colored outlines.
    /// Enable during development to visualize collision shapes.
    /// Toggle with F1 key in gameplay.
    /// Only draws bodies whose AABB intersects the camera visible bounds (culled).
    /// </summary>
    public class PhysicsDebugDraw
    {
        // ── State ──────────────────────────────────────────────────────────────
        public bool Enabled { get; set; } = false;

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color StaticColor    = new Color(100, 200, 100, 180);
        private static readonly Color DynamicColor   = new Color(100, 150, 255, 180);
        private static readonly Color SensorColor    = new Color(255, 200,  50, 120);
        private static readonly Color KinematicColor = new Color(200, 100, 255, 180);

        // ── Draw ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw all bodies in the physics world that intersect the visible bounds.
        /// Call inside a SpriteBatch.Begin/End block with the camera transform applied.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, AetherWorld world,
            Bloop.Core.AssetManager assets, Rectangle visibleBounds)
        {
            if (!Enabled) return;

            // Expand visible bounds slightly to avoid pop-in at edges
            var cullBounds = new Rectangle(
                visibleBounds.X      - 32,
                visibleBounds.Y      - 32,
                visibleBounds.Width  + 64,
                visibleBounds.Height + 64);

            foreach (var body in world.BodyList)
            {
                // Cull: skip bodies whose pixel-space position is outside visible bounds
                Vector2 bodyPixelPos = PhysicsManager.ToPixels(body.Position);
                if (!IsNearBounds(bodyPixelPos, cullBounds, 200f))
                    continue;

                Color color = body.BodyType switch
                {
                    BodyType.Static    => StaticColor,
                    BodyType.Dynamic   => DynamicColor,
                    BodyType.Kinematic => KinematicColor,
                    _                  => StaticColor
                };

                foreach (var fixture in body.FixtureList)
                {
                    Color drawColor = fixture.IsSensor ? SensorColor : color;
                    DrawFixture(spriteBatch, assets, body, fixture, drawColor);
                }
            }
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the given pixel position is within the bounds (with margin).
        /// Used for quick culling before drawing debug shapes.
        /// </summary>
        private static bool IsNearBounds(Vector2 pixelPos, Rectangle bounds, float margin)
        {
            return pixelPos.X >= bounds.Left   - margin &&
                   pixelPos.X <= bounds.Right  + margin &&
                   pixelPos.Y >= bounds.Top    - margin &&
                   pixelPos.Y <= bounds.Bottom + margin;
        }

        private void DrawFixture(SpriteBatch sb, Bloop.Core.AssetManager assets,
            Body body, Fixture fixture, Color color)
        {
            switch (fixture.Shape.ShapeType)
            {
                case ShapeType.Polygon:
                    DrawPolygon(sb, assets, body, (PolygonShape)fixture.Shape, color);
                    break;
                case ShapeType.Circle:
                    DrawCircle(sb, assets, body, (CircleShape)fixture.Shape, color);
                    break;
                case ShapeType.Chain:
                    DrawChain(sb, assets, body, (ChainShape)fixture.Shape, color);
                    break;
                case ShapeType.Edge:
                    DrawEdge(sb, assets, body, (EdgeShape)fixture.Shape, color);
                    break;
            }
        }

        private void DrawPolygon(SpriteBatch sb, Bloop.Core.AssetManager assets,
            Body body, PolygonShape shape, Color color)
        {
            var verts = shape.Vertices;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector2 a = PhysicsManager.ToPixels(body.GetWorldPoint(verts[i]));
                Vector2 b = PhysicsManager.ToPixels(body.GetWorldPoint(verts[(i + 1) % verts.Count]));
                DrawLine(sb, assets, a, b, color);
            }
        }

        private void DrawCircle(SpriteBatch sb, Bloop.Core.AssetManager assets,
            Body body, CircleShape shape, Color color)
        {
            Vector2 center = PhysicsManager.ToPixels(body.GetWorldPoint(shape.Position));
            float   radius = PhysicsManager.ToPixels(shape.Radius);
            int     segs   = 16;
            for (int i = 0; i < segs; i++)
            {
                float a1 = i       * MathHelper.TwoPi / segs;
                float a2 = (i + 1) * MathHelper.TwoPi / segs;
                Vector2 p1 = center + new Vector2((float)Math.Cos(a1), (float)Math.Sin(a1)) * radius;
                Vector2 p2 = center + new Vector2((float)Math.Cos(a2), (float)Math.Sin(a2)) * radius;
                DrawLine(sb, assets, p1, p2, color);
            }
        }

        private void DrawChain(SpriteBatch sb, Bloop.Core.AssetManager assets,
            Body body, ChainShape shape, Color color)
        {
            // ChainShape.Vertices is a Vertices collection
            var verts = shape.Vertices;
            for (int i = 0; i < verts.Count - 1; i++)
            {
                Vector2 a = PhysicsManager.ToPixels(body.GetWorldPoint(verts[i]));
                Vector2 b = PhysicsManager.ToPixels(body.GetWorldPoint(verts[i + 1]));
                DrawLine(sb, assets, a, b, color);
            }
        }

        private void DrawEdge(SpriteBatch sb, Bloop.Core.AssetManager assets,
            Body body, EdgeShape shape, Color color)
        {
            // EdgeShape exposes Vertex1 and Vertex2 as the two endpoints
            Vector2 a = PhysicsManager.ToPixels(body.GetWorldPoint(shape.Vertex1));
            Vector2 b = PhysicsManager.ToPixels(body.GetWorldPoint(shape.Vertex2));
            DrawLine(sb, assets, a, b, color);
        }

        private void DrawLine(SpriteBatch sb, Bloop.Core.AssetManager assets,
            Vector2 a, Vector2 b, Color color)
        {
            Vector2 diff   = b - a;
            float   length = diff.Length();
            if (length < 0.5f) return;

            float angle = (float)Math.Atan2(diff.Y, diff.X);
            sb.Draw(assets.Pixel,
                new Rectangle((int)a.X, (int)a.Y, (int)length, 1),
                null, color, angle, Vector2.Zero, SpriteEffects.None, 0f);
        }
    }
}
