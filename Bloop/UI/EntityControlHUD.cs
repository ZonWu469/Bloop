using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Entities;
using Bloop.Rendering;

namespace Bloop.UI
{
    /// <summary>
    /// HUD overlay for the entity control system.
    ///
    /// Draws (all in screen/HUD space, not world space):
    ///   - Control button (Q) at center-bottom of screen with cooldown ring
    ///   - "LMB: Select | RMB: Cancel" prompt during selection mode
    ///   - Control duration bar at top-center during active control
    ///   - Entity name + skill name display during active control
    ///   - Skill cooldown indicator during active control
    ///   - Isopod attachment icon + "T: Throw" prompt when isopod is attached
    ///
    /// The range circle (200px) is drawn in world space by EntityRenderer.DrawSelectionRangeCircle()
    /// and is called from GameplayScreen.DrawWorld() during selection mode.
    /// </summary>
    public class EntityControlHUD
    {
        // ── Layout constants ───────────────────────────────────────────────────
        private const float ButtonRadius    = 22f;
        private const float ButtonCenterX   = 640f; // virtual 1280×720 center
        private const float ButtonCenterY   = 680f; // near bottom
        private const float CooldownRingThickness = 3f;

        private const float ControlBarWidth  = 300f;
        private const float ControlBarHeight = 8f;
        private const float ControlBarY      = 20f;  // from top

        private const float PromptY = 650f;

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color ButtonReadyColor    = new Color(80, 220, 255, 220);
        private static readonly Color ButtonCooldownColor = new Color(50, 80, 100, 180);
        private static readonly Color ButtonLabelColor    = Color.White;
        private static readonly Color CooldownRingColor   = new Color(80, 220, 255, 200);
        private static readonly Color CooldownBgColor     = new Color(30, 30, 30, 160);
        private static readonly Color ControlBarBgColor   = new Color(20, 20, 20, 180);
        private static readonly Color ControlBarFgColor   = new Color(80, 220, 255, 220);
        private static readonly Color ControlBarLowColor  = new Color(255, 100, 60, 220);
        private static readonly Color EntityNameColor     = new Color(200, 240, 255, 220);
        private static readonly Color SkillNameColor      = new Color(255, 220, 100, 200);
        private static readonly Color PromptColor         = new Color(200, 200, 200, 180);
        private static readonly Color IsopodIconColor     = new Color(80, 200, 240, 220);
        private static readonly Color SelectingColor      = new Color(180, 255, 180, 200);

        // ── References ─────────────────────────────────────────────────────────
        private readonly EntityControlSystem _controlSystem;

        public EntityControlHUD(EntityControlSystem controlSystem)
        {
            _controlSystem = controlSystem;
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw all HUD elements. Call inside a SpriteBatch.Begin/End block
        /// WITHOUT camera transform (HUD is in screen space).
        /// </summary>
        public void Draw(SpriteBatch sb, AssetManager assets, int viewWidth, int viewHeight)
        {
            DrawControlButton(sb, assets, viewWidth, viewHeight);

            if (_controlSystem.IsSelecting)
                DrawSelectionPrompt(sb, assets, viewWidth, viewHeight);

            if (_controlSystem.IsControlling && _controlSystem.ActiveEntity != null)
                DrawControlOverlay(sb, assets, _controlSystem.ActiveEntity, viewWidth, viewHeight);

            if (_controlSystem.IsIsopodAttached && _controlSystem.ActiveEntity is LuminousIsopod isopod)
                DrawIsopodAttachedUI(sb, assets, isopod, viewWidth, viewHeight);
        }

        // ── Control button ─────────────────────────────────────────────────────

        private void DrawControlButton(SpriteBatch sb, AssetManager assets,
            int viewWidth, int viewHeight)
        {
            // Scale button position to actual view dimensions
            float scaleX = viewWidth  / 1280f;
            float scaleY = viewHeight / 720f;
            float cx = ButtonCenterX * scaleX;
            float cy = ButtonCenterY * scaleY;
            float r  = ButtonRadius;

            bool ready = _controlSystem.IsReady;

            // Background circle
            var bgColor = ready ? ButtonReadyColor : ButtonCooldownColor;
            GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(cx, cy), r, bgColor, 16);

            // Cooldown ring (arc showing remaining cooldown)
            if (!ready)
            {
                float fraction = 1f - _controlSystem.CooldownTimer / EntityControlSystem.GlobalCooldown;
                DrawCooldownArc(sb, assets, new Vector2(cx, cy), r + 4f, fraction, CooldownRingColor);
            }
            else
            {
                // Pulsing ready ring
                float pulse = 0.5f + 0.5f * (float)Math.Sin(AnimationClock.Pulse(2f));
                var readyRing = new Color(
                    CooldownRingColor.R,
                    CooldownRingColor.G,
                    CooldownRingColor.B,
                    (int)(CooldownRingColor.A * pulse));
                GeometryBatch.DrawCircleOutline(sb, assets, new Vector2(cx, cy), r + 4f, readyRing, 2);
            }

            // "Q" label
            assets.DrawStringCentered(sb, "Q", cy - 6f, ButtonLabelColor, 0.9f);

            // Cooldown timer text (when on cooldown)
            if (!ready && _controlSystem.CooldownTimer > 0f)
            {
                string timerText = $"{_controlSystem.CooldownTimer:F0}s";
                assets.DrawStringCentered(sb, timerText, cy + 8f,
                    new Color(180, 180, 180, 180), 0.6f);
            }

            // "Selecting" indicator
            if (_controlSystem.IsSelecting)
            {
                GeometryBatch.DrawCircleOutline(sb, assets, new Vector2(cx, cy),
                    r + 8f, SelectingColor, 2);
            }
        }

        // ── Selection mode prompt ──────────────────────────────────────────────

        private void DrawSelectionPrompt(SpriteBatch sb, AssetManager assets,
            int viewWidth, int viewHeight)
        {
            float scaleY = viewHeight / 720f;
            float y = PromptY * scaleY;

            string prompt = _controlSystem.HighlightedEntity != null
                ? $"LMB: Control {_controlSystem.HighlightedEntity.DisplayName}  |  RMB: Cancel"
                : "Move near an entity  |  RMB: Cancel";

            assets.DrawStringCentered(sb, prompt, y, PromptColor, 0.75f);
        }

        // ── Active control overlay ─────────────────────────────────────────────

        private void DrawControlOverlay(SpriteBatch sb, AssetManager assets,
            ControllableEntity entity, int viewWidth, int viewHeight)
        {
            float scaleX = viewWidth  / 1280f;
            float scaleY = viewHeight / 720f;

            // ── Control duration bar at top center ─────────────────────────────
            float barW = ControlBarWidth * scaleX;
            float barH = ControlBarHeight;
            float barX = (viewWidth - barW) / 2f;
            float barY = ControlBarY * scaleY;

            float fraction = entity.ControlDuration > 0f
                ? MathHelper.Clamp(entity.ControlTimer / entity.ControlDuration, 0f, 1f)
                : 0f;

            // Background
            assets.DrawRect(sb, new Vector2(barX, barY), new Vector2(barW, barH), ControlBarBgColor);

            // Foreground (turns red when < 20% remaining)
            var barColor = fraction < 0.2f ? ControlBarLowColor : ControlBarFgColor;
            if (fraction > 0f)
                assets.DrawRect(sb, new Vector2(barX, barY), new Vector2(barW * fraction, barH), barColor);

            // Entity name above bar
            assets.DrawStringCentered(sb, entity.DisplayName,
                barY - 14f * scaleY, EntityNameColor, 0.75f);

            // Skill name + cooldown below bar
            if (entity.Skill != null)
            {
                string skillText = entity.Skill.IsReady
                    ? $"[E] {entity.Skill.Name}"
                    : $"[E] {entity.Skill.Name}  ({entity.Skill.CooldownTimer:F1}s)";
                assets.DrawStringCentered(sb, skillText,
                    barY + barH + 4f * scaleY, SkillNameColor, 0.65f);
            }

            // RMB release prompt
            assets.DrawStringCentered(sb, "RMB: Release",
                barY + barH + 18f * scaleY, PromptColor, 0.6f);
        }

        // ── Isopod attached UI ─────────────────────────────────────────────────

        private void DrawIsopodAttachedUI(SpriteBatch sb, AssetManager assets,
            LuminousIsopod isopod, int viewWidth, int viewHeight)
        {
            float scaleX = viewWidth  / 1280f;
            float scaleY = viewHeight / 720f;

            // Isopod icon near the control button
            float cx = ButtonCenterX * scaleX;
            float cy = (ButtonCenterY - 50f) * scaleY;

            // Glowing oval icon
            float glowPulse = 0.6f + 0.4f * (float)Math.Sin(AnimationClock.Pulse(1.5f));
            var glowColor = new Color(60, 180, 220, (int)(80 * glowPulse));
            GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(cx, cy), 14f, glowColor, 12);
            GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(cx, cy), 8f, IsopodIconColor, 8);

            // Control timer bar
            float barW = 80f * scaleX;
            float barH = 4f;
            float barX = cx - barW / 2f;
            float barY = cy + 18f * scaleY;

            float fraction = isopod.ControlDuration > 0f
                ? MathHelper.Clamp(isopod.ControlTimer / isopod.ControlDuration, 0f, 1f)
                : 0f;

            assets.DrawRect(sb, new Vector2(barX, barY), new Vector2(barW, barH), ControlBarBgColor);
            if (fraction > 0f)
            {
                var barColor = fraction < 0.2f ? ControlBarLowColor : ControlBarFgColor;
                assets.DrawRect(sb, new Vector2(barX, barY), new Vector2(barW * fraction, barH), barColor);
            }

            // T: Throw prompt
            assets.DrawStringCentered(sb, "[T] Throw", cy + 26f * scaleY, PromptColor, 0.65f);

            // Glow Surge cooldown
            if (isopod.Skill != null)
            {
                string skillText = isopod.Skill.IsReady
                    ? "[E] Glow Surge"
                    : $"[E] Glow Surge ({isopod.Skill.CooldownTimer:F1}s)";
                assets.DrawStringCentered(sb, skillText, cy + 38f * scaleY, SkillNameColor, 0.6f);
            }
        }

        // ── Cooldown arc helper ────────────────────────────────────────────────

        /// <summary>
        /// Draw a partial arc (0–1 fraction of a full circle) as a cooldown indicator.
        /// Starts at the top (12 o'clock) and sweeps clockwise.
        /// </summary>
        private static void DrawCooldownArc(SpriteBatch sb, AssetManager assets,
            Vector2 center, float radius, float fraction, Color color)
        {
            if (fraction <= 0f) return;

            int segments = (int)(fraction * 32f);
            if (segments < 1) segments = 1;

            float startAngle = -MathHelper.PiOver2; // 12 o'clock
            float totalAngle = fraction * MathHelper.TwoPi;
            float step       = totalAngle / segments;

            for (int i = 0; i < segments; i++)
            {
                float a0 = startAngle + step * i;
                float a1 = startAngle + step * (i + 1);

                var p0 = center + new Vector2((float)Math.Cos(a0), (float)Math.Sin(a0)) * radius;
                var p1 = center + new Vector2((float)Math.Cos(a1), (float)Math.Sin(a1)) * radius;

                GeometryBatch.DrawLine(sb, assets, p0, p1, color, (int)CooldownRingThickness);
            }
        }
    }
}
