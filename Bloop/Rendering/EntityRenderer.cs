using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Entities;

namespace Bloop.Rendering
{
    /// <summary>
    /// Static renderer for all 7 controllable cave entities.
    /// Uses EntitySpritesheet horizontal-strip PNGs loaded via the content pipeline.
    ///
    /// Entry point: <see cref="DrawEntity"/> — draws the spritesheet frame scaled to
    /// the entity's physics hitbox, flipped horizontally when FacingDirection &lt; 0,
    /// then draws all overlay helpers (highlight ring, effect state icons, timer bar,
    /// skill cooldown pip, danger aura).
    ///
    /// Entity-specific visual effects (pulse rings, web trails, flash, trajectory)
    /// are drawn by each entity's own Draw() method before calling DrawEntity().
    /// </summary>
    public static class EntityRenderer
    {
        // ── Shared colors ──────────────────────────────────────────────────────
        private static readonly Color ControlHighlightColor   = new Color(80, 220, 255, 200);
        private static readonly Color SelectionHighlightColor = new Color(255, 220, 60, 180);
        private static readonly Color InRangeHighlightColor   = new Color(180, 255, 180, 120);
        private static readonly Color TimerBarBg              = new Color(30, 30, 30, 180);
        private static readonly Color TimerBarFg              = new Color(80, 220, 255, 220);
        private static readonly Color SkillReadyColor         = new Color(255, 200, 60, 220);
        private static readonly Color SkillCooldownColor      = new Color(100, 100, 100, 180);
        private static readonly Color RangeCircleColor        = new Color(180, 255, 180, 60);
        private static readonly Color SkillRangeCircleColor   = new Color(255, 200, 60, 50);

        // ── Player position for danger proximity scaling ───────────────────────
        /// <summary>
        /// Set this each frame before drawing entities so danger indicators can
        /// scale their intensity based on distance to the player.
        /// </summary>
        public static Vector2 PlayerPositionForDanger { get; set; } = Vector2.Zero;

        // ══════════════════════════════════════════════════════════════════════
        // ── Main entry point ──────────────────────────────────────────────────
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draw a controllable entity using its spritesheet.
        /// Call this from each entity's Draw() method after drawing any
        /// entity-specific effects that should appear behind the sprite.
        ///
        /// Scale: sprite height is scaled to match hitboxH so the visual size
        /// matches the physics hitbox.
        ///
        /// Facing: sprites are authored facing right.
        /// SpriteEffects.FlipHorizontally is applied when FacingDirection &lt; 0.
        ///
        /// If <paramref name="sheet"/> is null (not yet loaded), falls back to
        /// a colored placeholder rectangle.
        /// </summary>
        public static void DrawEntity(SpriteBatch sb, AssetManager assets,
            ControllableEntity entity, float hitboxW, float hitboxH,
            EntitySpritesheet? sheet)
        {
            Vector2 pos = entity.PixelPosition;

            // ── Danger indicator (behind everything) ──────────────────────────
            DrawDangerIndicator(sb, assets, entity, hitboxW, hitboxH);

            // ── Selection / control highlight ring ────────────────────────────
            DrawEntityHighlight(sb, assets, entity, hitboxW, hitboxH);

            // ── Sprite ────────────────────────────────────────────────────────
            if (sheet != null)
            {
                int frameCount = Math.Max(1, sheet.FrameCount);
                int frameIndex = (int)(AnimationClock.Time * sheet.Fps) % frameCount;
                var srcRect    = sheet.GetSourceRect(frameIndex);

                // Scale sprite height to match hitbox height
                float scale  = sheet.FrameHeight > 0 ? hitboxH / sheet.FrameHeight : 1f;
                var   origin = new Vector2(sheet.FrameWidth / 2f, sheet.FrameHeight / 2f);

                SpriteEffects fx = entity.FacingDirection < 0
                    ? SpriteEffects.FlipHorizontally
                    : SpriteEffects.None;

                sb.Draw(sheet.Texture, pos, srcRect, Color.White, 0f, origin, scale, fx, 0f);
            }
            else
            {
                // Fallback: colored placeholder rectangle
                var fallbackColor = entity.IsControlled
                    ? new Color(80, 220, 255, 200)
                    : new Color(120, 120, 140, 180);
                assets.DrawRect(sb,
                    new Rectangle(
                        (int)(pos.X - hitboxW / 2f),
                        (int)(pos.Y - hitboxH / 2f),
                        (int)hitboxW,
                        (int)hitboxH),
                    fallbackColor);
            }

            // ── Effect state overlays ─────────────────────────────────────────
            DrawEffectState(sb, assets, entity, hitboxW, hitboxH);

            // ── Control timer bar + skill cooldown pip ────────────────────────
            if (entity.IsControlled)
            {
                DrawControlTimerBar(sb, assets, entity, hitboxW);
                DrawSkillCooldownPip(sb, assets, entity, hitboxW);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // ── World-space range circles (called from GameplayScreen) ────────────
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draws the selection-mode range circle in world space.
        /// </summary>
        public static void DrawSelectionRangeCircle(SpriteBatch sb, AssetManager assets,
            Vector2 center, float radius)
        {
            GeometryBatch.DrawCircleOutline(sb, assets, center, radius, RangeCircleColor, 16);
        }

        /// <summary>
        /// Draws the skill effect range circle around a controlled entity in world space.
        /// </summary>
        public static void DrawSkillRangeCircle(SpriteBatch sb, AssetManager assets,
            Vector2 center, float radius)
        {
            GeometryBatch.DrawCircleOutline(sb, assets, center, radius, SkillRangeCircleColor, 16);
        }

        // ══════════════════════════════════════════════════════════════════════
        // ── Shared overlay helpers ────────────────────────────────────────────
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draws a pulsing red aura around entities that are dangerous to the player.
        /// Intensity scales with proximity.
        /// </summary>
        private static void DrawDangerIndicator(SpriteBatch sb, AssetManager assets,
            ControllableEntity entity, float w, float h)
        {
            if (entity.IsControlled) return;

            float dist = Vector2.Distance(entity.PixelPosition, PlayerPositionForDanger);
            float dangerRadius = 80f;
            if (dist > dangerRadius) return;

            float proximity = 1f - dist / dangerRadius;
            float pulse = 0.5f + 0.5f * (float)Math.Sin(AnimationClock.Time * 4f);
            float alpha = proximity * pulse * 0.35f;

            GeometryBatch.DrawCircleApprox(sb, assets, entity.PixelPosition,
                w * 0.8f + pulse * 4f,
                new Color(255, 60, 30, (int)(alpha * 255)), 10);
        }

        /// <summary>
        /// Draws a small icon or overlay indicating the entity's current effect state
        /// (following, fleeing, stuck, disoriented, infighting).
        /// </summary>
        private static void DrawEffectState(SpriteBatch sb, AssetManager assets,
            ControllableEntity entity, float w, float h)
        {
            Vector2 pos = entity.PixelPosition;
            float iconY = pos.Y - h * 0.5f - 10f;
            float t = AnimationClock.Time;

            if (entity.IsFollowing)
            {
                // Green upward arrow (following)
                float pulse = 0.7f + 0.3f * MathF.Sin(t * 3f);
                var c = new Color(60, 220, 80, (int)(180 * pulse));
                GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X, iconY), new Vector2(pos.X, iconY - 5f), c, 1.5f);
                GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X - 2f, iconY - 3f), new Vector2(pos.X, iconY - 5f), c, 1.5f);
                GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X + 2f, iconY - 3f), new Vector2(pos.X, iconY - 5f), c, 1.5f);
            }
            else if (entity.IsFleeing)
            {
                // Orange outward arrows (fleeing)
                float pulse = 0.7f + 0.3f * MathF.Sin(t * 5f);
                var c = new Color(255, 160, 40, (int)(180 * pulse));
                GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X - 3f, iconY), new Vector2(pos.X - 6f, iconY - 3f), c, 1.5f);
                GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X + 3f, iconY), new Vector2(pos.X + 6f, iconY - 3f), c, 1.5f);
            }
            else if (entity.IsStuck)
            {
                // White X (stuck)
                float pulse = 0.6f + 0.4f * MathF.Sin(t * 6f);
                var c = new Color(220, 220, 220, (int)(200 * pulse));
                GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X - 3f, iconY - 3f), new Vector2(pos.X + 3f, iconY + 3f), c, 1.5f);
                GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X + 3f, iconY - 3f), new Vector2(pos.X - 3f, iconY + 3f), c, 1.5f);
            }
            else if (entity.IsDisoriented)
            {
                // Cyan spinning dots (disoriented)
                float angle = t * 4f;
                var c = new Color(80, 220, 255, 180);
                for (int d = 0; d < 3; d++)
                {
                    float a = angle + d * MathHelper.TwoPi / 3f;
                    Vector2 dp = new Vector2(MathF.Cos(a) * 4f, MathF.Sin(a) * 2f);
                    GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(pos.X + dp.X, iconY + dp.Y), 1.2f, c, 4);
                }
            }
            else if (entity.IsInfighting)
            {
                // Red crossed swords (infighting)
                float pulse = 0.7f + 0.3f * MathF.Sin(t * 7f);
                var c = new Color(220, 40, 40, (int)(200 * pulse));
                GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X - 4f, iconY - 4f), new Vector2(pos.X + 4f, iconY + 4f), c, 1.5f);
                GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X + 4f, iconY - 4f), new Vector2(pos.X - 4f, iconY + 4f), c, 1.5f);
                // Small sparks
                float sparkA = t * 8f;
                for (int s = 0; s < 2; s++)
                {
                    float sa = sparkA + s * MathHelper.Pi;
                    Vector2 sp = new Vector2(MathF.Cos(sa) * 3f, MathF.Sin(sa) * 3f);
                    GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(pos.X + sp.X, iconY + sp.Y), 0.8f,
                        new Color(255, 120, 60, (int)(160 * pulse)), 4);
                }
            }
        }

        /// <summary>
        /// Draws the selection / control highlight ring around an entity.
        /// </summary>
        private static void DrawEntityHighlight(SpriteBatch sb, AssetManager assets,
            ControllableEntity entity, float w, float h)
        {
            if (entity.IsControlled)
                DrawEntityOutline(sb, assets, entity.PixelPosition, w, h, ControlHighlightColor);
            else if (entity.IsHighlighted)
                DrawEntityOutline(sb, assets, entity.PixelPosition, w, h, SelectionHighlightColor);
            else if (entity.IsInRange)
                DrawEntityOutline(sb, assets, entity.PixelPosition, w, h, InRangeHighlightColor);
        }

        private static void DrawEntityOutline(SpriteBatch sb, AssetManager assets,
            Vector2 pos, float w, float h, Color color)
        {
            float r = Math.Max(w, h) * 0.55f;
            float pulse = 0.85f + 0.15f * (float)Math.Sin(AnimationClock.Time * 3f);
            GeometryBatch.DrawCircleOutline(sb, assets, pos, r * pulse, color, 2);
        }

        /// <summary>
        /// Draws the control-time-remaining bar above the entity.
        /// </summary>
        private static void DrawControlTimerBar(SpriteBatch sb, AssetManager assets,
            ControllableEntity entity, float w)
        {
            float fraction = entity.ControlDuration > 0f
                ? entity.ControlTimer / entity.ControlDuration
                : 0f;
            fraction = Math.Clamp(fraction, 0f, 1f);
            int barW = (int)(w * 1.2f);
            int barH = 3;
            int barX = (int)(entity.PixelPosition.X - barW / 2f);
            int barY = (int)(entity.PixelPosition.Y - entity.GetBounds().Height / 2f - 8);

            assets.DrawRect(sb, new Rectangle(barX, barY, barW, barH), TimerBarBg);
            if (fraction > 0f)
                assets.DrawRect(sb, new Rectangle(barX, barY, (int)(barW * fraction), barH), TimerBarFg);
        }

        /// <summary>
        /// Draws a small pip above the entity showing skill cooldown state.
        /// </summary>
        private static void DrawSkillCooldownPip(SpriteBatch sb, AssetManager assets,
            ControllableEntity entity, float w)
        {
            if (entity.Skill == null) return;

            // fraction = 1 when ready (cooldown elapsed), 0 when just used
            float fraction = entity.Skill.Cooldown > 0f
                ? 1f - MathHelper.Clamp(entity.Skill.CooldownTimer / entity.Skill.Cooldown, 0f, 1f)
                : 1f;
            var color = entity.Skill.IsReady ? SkillReadyColor : SkillCooldownColor;

            Vector2 pipPos = new Vector2(
                entity.PixelPosition.X + w * 0.6f + 4f,
                entity.PixelPosition.Y - entity.GetBounds().Height / 2f - 6f);

            GeometryBatch.DrawCircleApprox(sb, assets, pipPos, 3f, color, 6);
            if (fraction < 1f)
            {
                // Partial arc to show cooldown progress
                int arcSegs = 6;
                int filledSegs = (int)(fraction * arcSegs);
                for (int s = 0; s < filledSegs; s++)
                {
                    float a0 = -MathHelper.PiOver2 + (s / (float)arcSegs) * MathHelper.TwoPi;
                    float a1 = -MathHelper.PiOver2 + ((s + 0.9f) / arcSegs) * MathHelper.TwoPi;
                    Vector2 p0 = pipPos + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * 3f;
                    Vector2 p1 = pipPos + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * 3f;
                    GeometryBatch.DrawLine(sb, assets, p0, p1, SkillReadyColor, 1f);
                }
            }
        }
    }
}
