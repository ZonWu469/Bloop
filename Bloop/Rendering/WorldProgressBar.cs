using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;

namespace Bloop.Rendering
{
    /// <summary>
    /// Reusable world-space progress bar drawn above objects.
    /// All methods are static — no state. Call from world-space SpriteBatch blocks.
    /// </summary>
    public static class WorldProgressBar
    {
        private static readonly Color DefaultBg  = new Color(10, 14, 20, 200);
        private static readonly Color DefaultFg  = new Color(80, 200, 140);
        private static readonly Color LabelColor = new Color(180, 210, 200);

        /// <summary>
        /// Draw a horizontal progress bar in world space above <paramref name="worldPos"/>.
        /// </summary>
        /// <param name="worldPos">Center of the object (bar is drawn above it).</param>
        /// <param name="progress01">Fill fraction 0..1.</param>
        /// <param name="width">Bar width in pixels.</param>
        /// <param name="height">Bar height in pixels.</param>
        /// <param name="fgColor">Fill color.</param>
        /// <param name="bgColor">Background color.</param>
        /// <param name="label">Optional label drawn above the bar.</param>
        /// <param name="yOffset">Vertical offset above worldPos (default -28).</param>
        public static void Draw(SpriteBatch sb, AssetManager assets,
            Vector2 worldPos, float progress01, float width, float height,
            Color fgColor, Color bgColor,
            string? label = null, float yOffset = -28f)
        {
            float clampedP = Math.Clamp(progress01, 0f, 1f);

            int bx = (int)(worldPos.X - width / 2f);
            int by = (int)(worldPos.Y + yOffset);
            int bw = (int)width;
            int bh = (int)height;

            // Background
            assets.DrawRect(sb, new Rectangle(bx, by, bw, bh), bgColor);

            // Fill
            int fillW = (int)(bw * clampedP);
            if (fillW > 0)
            {
                // Pulse effect when full
                Color drawColor = fgColor;
                if (clampedP >= 1f)
                {
                    float pulse = AnimationClock.Pulse(3f);
                    drawColor = Color.Lerp(fgColor, Color.White, pulse * 0.35f);
                }
                assets.DrawRect(sb, new Rectangle(bx, by, fillW, bh), drawColor);
            }

            // Border
            assets.DrawRectOutline(sb, new Rectangle(bx, by, bw, bh),
                new Color(60, 80, 70, 180), 1);

            // Optional label
            if (label != null && assets.GameFont != null)
            {
                Vector2 labelSize = assets.GameFont.MeasureString(label) * 0.6f;
                assets.DrawString(sb, label,
                    new Vector2(worldPos.X - labelSize.X / 2f, by - labelSize.Y - 2f),
                    LabelColor, 0.6f);
            }
        }

        /// <summary>
        /// Draw a circular cooldown sweep overlay in world space.
        /// Useful for showing recharge progress on objects.
        /// </summary>
        /// <param name="worldPos">Center of the object.</param>
        /// <param name="cooldownProgress01">0 = just started, 1 = fully recharged.</param>
        /// <param name="radius">Radius of the sweep circle.</param>
        /// <param name="overlayColor">Color of the grey-out overlay.</param>
        /// <param name="remainingTime">Remaining seconds to display (shown as text if > 0).</param>
        public static void DrawCooldownOverlay(SpriteBatch sb, AssetManager assets,
            Vector2 worldPos, float cooldownProgress01, float radius,
            Color overlayColor, float remainingTime = 0f)
        {
            float clampedP = Math.Clamp(cooldownProgress01, 0f, 1f);

            // Grey-out disk (full when cooldown just started, shrinks as it recharges)
            float overlayAlpha = (1f - clampedP) * 0.55f;
            if (overlayAlpha > 0.02f)
            {
                OrganicPrimitives.DrawGradientDisk(sb, assets, worldPos,
                    rIn: 0f, rOut: radius,
                    innerColor: overlayColor * overlayAlpha,
                    outerColor: overlayColor * (overlayAlpha * 0.3f),
                    rings: 3, segments: 10);
            }

            // Segmented arc showing remaining cooldown
            int arcSegs = 12;
            int litSegs = (int)((1f - clampedP) * arcSegs);
            for (int i = 0; i < arcSegs; i++)
            {
                float a0 = (i / (float)arcSegs) * MathHelper.TwoPi - MathHelper.PiOver2;
                float a1 = ((i + 0.7f) / arcSegs) * MathHelper.TwoPi - MathHelper.PiOver2;
                Vector2 p0 = worldPos + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius;
                Vector2 p1 = worldPos + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius;
                Color segColor = (i < litSegs)
                    ? overlayColor * (0.6f + AnimationClock.Pulse(2f, i * 0.3f) * 0.4f)
                    : new Color(30, 40, 35, 80);
                GeometryBatch.DrawLine(sb, assets, p0, p1, segColor, 2f);
            }

            // Remaining time text
            if (remainingTime > 0f && assets.GameFont != null)
            {
                string timeText = $"{remainingTime:0.0}s";
                Vector2 textSize = assets.GameFont.MeasureString(timeText) * 0.65f;
                assets.DrawString(sb, timeText,
                    new Vector2(worldPos.X - textSize.X / 2f, worldPos.Y - textSize.Y / 2f),
                    overlayColor * 0.9f, 0.65f);
            }
        }
    }
}
