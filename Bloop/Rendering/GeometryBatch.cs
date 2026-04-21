using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;

namespace Bloop.Rendering
{
    /// <summary>
    /// Static helper class providing geometric drawing primitives built on top of
    /// AssetManager's 1×1 Pixel texture and SpriteBatch.
    ///
    /// All methods use only the Pixel texture so SpriteBatch can batch everything
    /// into a single GPU draw call per Begin/End block.
    ///
    /// Coordinate system: pixel space, Y-down (MonoGame default).
    /// </summary>
    public static class GeometryBatch
    {
        // ── Line ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw a line from point a to point b with the given pixel thickness.
        /// Uses a rotated rectangle for correct angle and length.
        /// </summary>
        public static void DrawLine(SpriteBatch sb, AssetManager assets,
            Vector2 a, Vector2 b, Color color, float thickness = 1f)
        {
            Vector2 diff   = b - a;
            float   length = diff.Length();
            if (length < 0.5f) return;

            float angle = (float)Math.Atan2(diff.Y, diff.X);
            sb.Draw(assets.Pixel,
                new Rectangle((int)a.X, (int)a.Y, (int)Math.Ceiling(length), (int)Math.Max(1, thickness)),
                null, color, angle, Vector2.Zero, SpriteEffects.None, 0f);
        }

        // ── Triangle ───────────────────────────────────────────────────────────

        /// <summary>
        /// Draw a filled triangle approximated by three overlapping rotated rectangles
        /// (one per edge, filled inward). Sufficient for small decorative triangles.
        /// For larger triangles, use DrawTriangleSolid.
        /// </summary>
        public static void DrawTriangle(SpriteBatch sb, AssetManager assets,
            Vector2 v0, Vector2 v1, Vector2 v2, Color color)
        {
            // Draw the three edges as thick lines to approximate a filled triangle
            Vector2 centroid = (v0 + v1 + v2) / 3f;

            // Thickness proportional to the triangle's approximate size
            float size = Math.Max(
                Math.Max(Vector2.Distance(v0, v1), Vector2.Distance(v1, v2)),
                Vector2.Distance(v2, v0));
            float thickness = Math.Max(1f, size * 0.5f);

            DrawLine(sb, assets, v0, v1, color, thickness);
            DrawLine(sb, assets, v1, v2, color, thickness);
            DrawLine(sb, assets, v2, v0, color, thickness);
        }

        /// <summary>
        /// Draw a filled triangle by rasterizing horizontal scanlines.
        /// More accurate than DrawTriangle for larger shapes.
        /// Vertices are sorted top-to-bottom internally.
        /// </summary>
        public static void DrawTriangleSolid(SpriteBatch sb, AssetManager assets,
            Vector2 v0, Vector2 v1, Vector2 v2, Color color)
        {
            // Sort vertices by Y (top to bottom)
            if (v0.Y > v1.Y) { var tmp = v0; v0 = v1; v1 = tmp; }
            if (v0.Y > v2.Y) { var tmp = v0; v0 = v2; v2 = tmp; }
            if (v1.Y > v2.Y) { var tmp = v1; v1 = v2; v2 = tmp; }

            int yTop    = (int)Math.Ceiling(v0.Y);
            int yMid    = (int)Math.Ceiling(v1.Y);
            int yBottom = (int)Math.Ceiling(v2.Y);

            // Upper half: v0 → v1 (left/right) and v0 → v2 (long edge)
            RasterizeTriangleHalf(sb, assets, v0, v1, v0, v2, yTop, yMid, color);
            // Lower half: v1 → v2 and v0 → v2
            RasterizeTriangleHalf(sb, assets, v1, v2, v0, v2, yMid, yBottom, color);
        }

        // ── Circle approximation ───────────────────────────────────────────────

        /// <summary>
        /// Draw a filled circle approximated by N thin pie-slice rectangles.
        /// segments=8 is visually sufficient for radii up to ~16px.
        /// </summary>
        public static void DrawCircleApprox(SpriteBatch sb, AssetManager assets,
            Vector2 center, float radius, Color color, int segments = 8)
        {
            if (radius < 1f) return;

            float angleStep = MathHelper.TwoPi / segments;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * angleStep;
                float a1 = a0 + angleStep;

                Vector2 p0 = center + new Vector2((float)Math.Cos(a0), (float)Math.Sin(a0)) * radius;
                Vector2 p1 = center + new Vector2((float)Math.Cos(a1), (float)Math.Sin(a1)) * radius;

                // Draw a filled triangle from center to the two arc points
                DrawTriangleSolid(sb, assets, center, p0, p1, color);
            }
        }

        /// <summary>
        /// Draw a circle outline (ring) using N line segments.
        /// </summary>
        public static void DrawCircleOutline(SpriteBatch sb, AssetManager assets,
            Vector2 center, float radius, Color color, int segments = 12, float thickness = 1f)
        {
            if (radius < 1f) return;

            float angleStep = MathHelper.TwoPi / segments;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * angleStep;
                float a1 = a0 + angleStep;

                Vector2 p0 = center + new Vector2((float)Math.Cos(a0), (float)Math.Sin(a0)) * radius;
                Vector2 p1 = center + new Vector2((float)Math.Cos(a1), (float)Math.Sin(a1)) * radius;

                DrawLine(sb, assets, p0, p1, color, thickness);
            }
        }

        // ── Rounded rectangle ──────────────────────────────────────────────────

        /// <summary>
        /// Draw a filled rectangle with softened corners.
        /// cornerRadius: how many pixels to cut from each corner.
        /// </summary>
        public static void DrawRoundedRect(SpriteBatch sb, AssetManager assets,
            Rectangle rect, int cornerRadius, Color color)
        {
            if (cornerRadius <= 0)
            {
                assets.DrawRect(sb, rect, color);
                return;
            }

            int cr = Math.Min(cornerRadius, Math.Min(rect.Width / 2, rect.Height / 2));

            // Center cross
            assets.DrawRect(sb, new Rectangle(rect.X + cr, rect.Y, rect.Width - cr * 2, rect.Height), color);
            assets.DrawRect(sb, new Rectangle(rect.X, rect.Y + cr, rect.Width, rect.Height - cr * 2), color);

            // Four corner circles
            DrawCircleApprox(sb, assets, new Vector2(rect.X + cr,              rect.Y + cr),              cr, color, 6);
            DrawCircleApprox(sb, assets, new Vector2(rect.Right - cr,          rect.Y + cr),              cr, color, 6);
            DrawCircleApprox(sb, assets, new Vector2(rect.X + cr,              rect.Bottom - cr),         cr, color, 6);
            DrawCircleApprox(sb, assets, new Vector2(rect.Right - cr,          rect.Bottom - cr),         cr, color, 6);
        }

        // ── Polygon outline ────────────────────────────────────────────────────

        /// <summary>
        /// Draw a closed polygon outline from an array of vertices.
        /// </summary>
        public static void DrawPolygonOutline(SpriteBatch sb, AssetManager assets,
            Vector2[] vertices, Color color, float thickness = 1f)
        {
            if (vertices.Length < 2) return;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector2 a = vertices[i];
                Vector2 b = vertices[(i + 1) % vertices.Length];
                DrawLine(sb, assets, a, b, color, thickness);
            }
        }

        // ── Diamond (rotated square) ───────────────────────────────────────────

        /// <summary>
        /// Draw a filled diamond (square rotated 45°) centered at the given position.
        /// </summary>
        public static void DrawDiamond(SpriteBatch sb, AssetManager assets,
            Vector2 center, float halfSize, Color color)
        {
            var verts = new Vector2[]
            {
                center + new Vector2(0,         -halfSize),
                center + new Vector2(halfSize,   0),
                center + new Vector2(0,          halfSize),
                center + new Vector2(-halfSize,  0),
            };

            // Fill with two triangles
            DrawTriangleSolid(sb, assets, verts[0], verts[1], verts[2], color);
            DrawTriangleSolid(sb, assets, verts[0], verts[2], verts[3], color);
        }

        /// <summary>
        /// Draw a diamond outline.
        /// </summary>
        public static void DrawDiamondOutline(SpriteBatch sb, AssetManager assets,
            Vector2 center, float halfSize, Color color, float thickness = 1f)
        {
            var verts = new Vector2[]
            {
                center + new Vector2(0,         -halfSize),
                center + new Vector2(halfSize,   0),
                center + new Vector2(0,          halfSize),
                center + new Vector2(-halfSize,  0),
            };
            DrawPolygonOutline(sb, assets, verts, color, thickness);
        }

        // ── Rotated rectangle ──────────────────────────────────────────────────

        /// <summary>
        /// Draw a filled rectangle rotated around its center by the given angle (radians).
        /// </summary>
        public static void DrawRotatedRect(SpriteBatch sb, AssetManager assets,
            Vector2 center, float width, float height, float angle, Color color)
        {
            sb.Draw(assets.Pixel,
                new Rectangle((int)(center.X - width / 2f), (int)(center.Y - height / 2f),
                               (int)width, (int)height),
                null, color, angle,
                new Vector2(width / 2f, height / 2f),
                SpriteEffects.None, 0f);
        }

        // ── Dashed line ────────────────────────────────────────────────────────

        /// <summary>
        /// Draw a dashed line from a to b.
        /// dashLength: pixels per dash. gapLength: pixels per gap.
        /// </summary>
        public static void DrawDashedLine(SpriteBatch sb, AssetManager assets,
            Vector2 a, Vector2 b, Color color, float dashLength = 4f, float gapLength = 4f,
            float thickness = 1f)
        {
            Vector2 diff      = b - a;
            float   totalLen  = diff.Length();
            if (totalLen < 0.5f) return;

            Vector2 dir       = diff / totalLen;
            float   cycleLen  = dashLength + gapLength;
            float   traveled  = 0f;

            while (traveled < totalLen)
            {
                float dashEnd = Math.Min(traveled + dashLength, totalLen);
                Vector2 p0 = a + dir * traveled;
                Vector2 p1 = a + dir * dashEnd;
                DrawLine(sb, assets, p0, p1, color, thickness);
                traveled += cycleLen;
            }
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private static void RasterizeTriangleHalf(SpriteBatch sb, AssetManager assets,
            Vector2 edgeA0, Vector2 edgeA1,
            Vector2 edgeB0, Vector2 edgeB1,
            int yStart, int yEnd, Color color)
        {
            float heightA = edgeA1.Y - edgeA0.Y;
            float heightB = edgeB1.Y - edgeB0.Y;

            for (int y = yStart; y < yEnd; y++)
            {
                float tA = heightA == 0f ? 0f : (y - edgeA0.Y) / heightA;
                float tB = heightB == 0f ? 0f : (y - edgeB0.Y) / heightB;

                float xA = edgeA0.X + tA * (edgeA1.X - edgeA0.X);
                float xB = edgeB0.X + tB * (edgeB1.X - edgeB0.X);

                int xLeft  = (int)Math.Min(xA, xB);
                int xRight = (int)Math.Max(xA, xB);
                int width  = xRight - xLeft + 1;

                if (width > 0)
                    sb.Draw(assets.Pixel, new Rectangle(xLeft, y, width, 1), color);
            }
        }
    }
}
