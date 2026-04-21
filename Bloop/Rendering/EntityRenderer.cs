using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Entities;

namespace Bloop.Rendering
{
    /// <summary>
    /// Static renderer for all 7 controllable cave entities.
    /// Uses GeometryBatch primitives exclusively — no sprites.
    ///
    /// Each entity type has a dedicated DrawXxx() method.
    /// Shared helpers draw the control highlight, selection highlight, timer bar,
    /// and the selection-mode range circle (drawn in world space).
    /// </summary>
    public static class EntityRenderer
    {
        // ── Shared colors ──────────────────────────────────────────────────────
        private static readonly Color ControlHighlightColor  = new Color(80, 220, 255, 200);
        private static readonly Color SelectionHighlightColor = new Color(255, 220, 60, 180);
        private static readonly Color InRangeHighlightColor  = new Color(180, 255, 180, 120);
        private static readonly Color TimerBarBg             = new Color(30, 30, 30, 180);
        private static readonly Color TimerBarFg             = new Color(80, 220, 255, 220);
        private static readonly Color SkillReadyColor        = new Color(255, 200, 60, 220);
        private static readonly Color SkillCooldownColor     = new Color(100, 100, 100, 180);
        private static readonly Color RangeCircleColor       = new Color(180, 255, 180, 60);

        // ── Echo Bat ───────────────────────────────────────────────────────────

        /// <summary>
        /// Draw an Echo Bat at its current position.
        /// Body: small dark ellipse. Wings: two angular triangles that flap.
        /// Pulse ring: expanding circle when Sonic Pulse fires.
        /// </summary>
        public static void DrawEchoBat(SpriteBatch sb, AssetManager assets, EchoBat bat)
        {
            Vector2 pos = bat.PixelPosition;

            // ── Selection / control highlights ─────────────────────────────────
            DrawEntityHighlight(sb, assets, bat, EchoBat.WidthPx, EchoBat.HeightPx);

            // ── Pulse ring ─────────────────────────────────────────────────────
            if (bat.PulseActive && bat.PulseRadius > 0f)
            {
                float alpha = 1f - bat.PulseRadius / EchoBat.PulseMaxRadius;
                var pulseColor = new Color(180, 220, 255, (int)(alpha * 160));
                GeometryBatch.DrawCircleOutline(sb, assets, pos, bat.PulseRadius, pulseColor, 2);
            }

            // ── Wings ──────────────────────────────────────────────────────────
            // Wing flap: phase 0 = fully open, 0.5 = folded
            float flapAngle = (float)Math.Sin(bat.WingPhase * Math.PI * 2.0) * 0.4f;
            float wingSpan  = EchoBat.WidthPx * 0.9f;
            float wingDrop  = EchoBat.HeightPx * 0.3f + flapAngle * EchoBat.HeightPx * 0.5f;

            // Left wing tip
            var wingTipL = new Vector2(pos.X - wingSpan, pos.Y + wingDrop);
            // Right wing tip
            var wingTipR = new Vector2(pos.X + wingSpan, pos.Y + wingDrop);
            // Wing root (body center)
            var wingRoot = pos;

            var wingColor = bat.IsControlled
                ? new Color(60, 100, 160)
                : new Color(40, 40, 60);

            // Left wing: triangle from root to tip to body-left
            GeometryBatch.DrawTriangleSolid(sb, assets,
                wingRoot,
                wingTipL,
                new Vector2(pos.X - EchoBat.WidthPx * 0.4f, pos.Y),
                wingColor);

            // Right wing: triangle from root to tip to body-right
            GeometryBatch.DrawTriangleSolid(sb, assets,
                wingRoot,
                wingTipR,
                new Vector2(pos.X + EchoBat.WidthPx * 0.4f, pos.Y),
                wingColor);

            // ── Body ───────────────────────────────────────────────────────────
            var bodyColor = bat.IsControlled
                ? new Color(80, 130, 200)
                : new Color(50, 50, 70);

            GeometryBatch.DrawCircleApprox(sb, assets, pos,
                EchoBat.WidthPx * 0.35f, bodyColor, 8);

            // ── Eyes (two tiny bright dots) ────────────────────────────────────
            var eyeColor = bat.IsControlled
                ? new Color(200, 240, 255)
                : new Color(180, 60, 60);

            float eyeOff = EchoBat.WidthPx * 0.12f;
            GeometryBatch.DrawCircleApprox(sb, assets,
                new Vector2(pos.X - eyeOff, pos.Y - 1f), 1.5f, eyeColor, 4);
            GeometryBatch.DrawCircleApprox(sb, assets,
                new Vector2(pos.X + eyeOff, pos.Y - 1f), 1.5f, eyeColor, 4);

            // ── Control timer bar ──────────────────────────────────────────────
            if (bat.IsControlled)
                DrawControlTimerBar(sb, assets, bat, EchoBat.WidthPx);

            // ── Skill cooldown pip ─────────────────────────────────────────────
            if (bat.IsControlled && bat.Skill != null)
                DrawSkillCooldownPip(sb, assets, bat, EchoBat.WidthPx);
        }

        // ── Silk Weaver Spider ─────────────────────────────────────────────────

        public static void DrawSilkWeaverSpider(SpriteBatch sb, AssetManager assets,
            SilkWeaverSpider spider)
        {
            Vector2 pos = spider.PixelPosition;
            DrawEntityHighlight(sb, assets, spider, SilkWeaverSpider.WidthPx, SilkWeaverSpider.HeightPx);

            var bodyColor = spider.IsControlled
                ? new Color(160, 100, 200)
                : new Color(80, 50, 100);
            var legColor = spider.IsControlled
                ? new Color(130, 80, 170)
                : new Color(60, 40, 80);

            // 8 legs — 4 per side, angled outward
            for (int i = 0; i < 4; i++)
            {
                float t     = (i / 3f) * 0.8f + 0.1f; // 0.1 to 0.9
                float legY  = pos.Y - SilkWeaverSpider.HeightPx * 0.3f + t * SilkWeaverSpider.HeightPx * 0.6f;
                float legXL = pos.X - SilkWeaverSpider.WidthPx * 0.5f;
                float legXR = pos.X + SilkWeaverSpider.WidthPx * 0.5f;
                float tipOff = SilkWeaverSpider.WidthPx * 0.7f;
                float tipY   = legY + SilkWeaverSpider.HeightPx * 0.4f;

                GeometryBatch.DrawLine(sb, assets, new Vector2(legXL, legY),
                    new Vector2(legXL - tipOff, tipY), legColor, 1);
                GeometryBatch.DrawLine(sb, assets, new Vector2(legXR, legY),
                    new Vector2(legXR + tipOff, tipY), legColor, 1);
            }

            // Round body
            GeometryBatch.DrawCircleApprox(sb, assets, pos,
                SilkWeaverSpider.WidthPx * 0.4f, bodyColor, 10);

            // Abdomen (slightly behind)
            GeometryBatch.DrawCircleApprox(sb, assets,
                new Vector2(pos.X, pos.Y + SilkWeaverSpider.HeightPx * 0.35f),
                SilkWeaverSpider.WidthPx * 0.3f, bodyColor, 8);

            // Web trail (if skill active)
            if (spider.IsControlled && spider.Skill is { IsActive: true })
                DrawWebTrail(sb, assets, spider);

            if (spider.IsControlled)
            {
                DrawControlTimerBar(sb, assets, spider, SilkWeaverSpider.WidthPx);
                DrawSkillCooldownPip(sb, assets, spider, SilkWeaverSpider.WidthPx);
            }
        }

        // ── Chain Centipede ────────────────────────────────────────────────────

        public static void DrawChainCentipede(SpriteBatch sb, AssetManager assets,
            ChainCentipede centipede)
        {
            Vector2 pos = centipede.PixelPosition;
            DrawEntityHighlight(sb, assets, centipede, ChainCentipede.WidthPx, ChainCentipede.HeightPx);

            var segColor = centipede.IsControlled
                ? new Color(220, 140, 60)
                : new Color(140, 90, 40);
            var legColor = centipede.IsControlled
                ? new Color(200, 120, 40)
                : new Color(120, 70, 30);

            // 7 body segments
            int segments = 7;
            float segW = ChainCentipede.WidthPx / segments;
            for (int i = 0; i < segments; i++)
            {
                float sx = pos.X - ChainCentipede.WidthPx * 0.5f + segW * (i + 0.5f);
                float sy = pos.Y;
                GeometryBatch.DrawCircleApprox(sb, assets,
                    new Vector2(sx, sy), segW * 0.45f, segColor, 6);

                // Tiny legs
                float legLen = ChainCentipede.HeightPx * 0.6f;
                GeometryBatch.DrawLine(sb, assets,
                    new Vector2(sx, sy),
                    new Vector2(sx - 2f, sy - legLen), legColor, 1);
                GeometryBatch.DrawLine(sb, assets,
                    new Vector2(sx, sy),
                    new Vector2(sx - 2f, sy + legLen), legColor, 1);
            }

            // Head (slightly larger)
            GeometryBatch.DrawCircleApprox(sb, assets,
                new Vector2(pos.X + ChainCentipede.WidthPx * 0.5f, pos.Y),
                segW * 0.6f, segColor, 6);

            // Aggression pheromone pulse
            if (centipede.PulseActive)
            {
                float alpha = 1f - centipede.PulseRadius / ChainCentipede.PulseMaxRadius;
                var pulseColor = new Color(255, 160, 40, (int)(alpha * 140));
                GeometryBatch.DrawCircleOutline(sb, assets, pos, centipede.PulseRadius, pulseColor, 2);
            }

            if (centipede.IsControlled)
            {
                DrawControlTimerBar(sb, assets, centipede, ChainCentipede.WidthPx);
                DrawSkillCooldownPip(sb, assets, centipede, ChainCentipede.WidthPx);
            }
        }

        // ── Luminescent Glowworm ───────────────────────────────────────────────

        public static void DrawLuminescentGlowworm(SpriteBatch sb, AssetManager assets,
            LuminescentGlowworm worm)
        {
            Vector2 pos = worm.PixelPosition;
            DrawEntityHighlight(sb, assets, worm, LuminescentGlowworm.WidthPx, LuminescentGlowworm.HeightPx);

            // Glow aura
            float glowPulse = 0.7f + 0.3f * (float)Math.Sin(AnimationClock.Pulse(2f));
            var glowColor = new Color(180, 255, 120, (int)(60 * glowPulse));
            GeometryBatch.DrawCircleApprox(sb, assets, pos,
                LuminescentGlowworm.WidthPx * 1.2f, glowColor, 12);

            var bodyColor = worm.IsControlled
                ? new Color(160, 255, 100)
                : new Color(100, 200, 60);

            // Segmented body (5 segments)
            int segs = 5;
            float segW = LuminescentGlowworm.WidthPx / segs;
            for (int i = 0; i < segs; i++)
            {
                float sx = pos.X - LuminescentGlowworm.WidthPx * 0.5f + segW * (i + 0.5f);
                GeometryBatch.DrawCircleApprox(sb, assets,
                    new Vector2(sx, pos.Y), segW * 0.45f, bodyColor, 6);
            }

            // Flash effect
            if (worm.FlashActive)
            {
                float flashAlpha = 1f - worm.FlashRadius / LuminescentGlowworm.FlashMaxRadius;
                var flashColor = new Color(220, 255, 180, (int)(flashAlpha * 200));
                GeometryBatch.DrawCircleApprox(sb, assets, pos, worm.FlashRadius, flashColor, 16);
            }

            if (worm.IsControlled)
            {
                DrawControlTimerBar(sb, assets, worm, LuminescentGlowworm.WidthPx);
                DrawSkillCooldownPip(sb, assets, worm, LuminescentGlowworm.WidthPx);
            }
        }

        // ── Deep Burrow Worm ───────────────────────────────────────────────────

        public static void DrawDeepBurrowWorm(SpriteBatch sb, AssetManager assets,
            DeepBurrowWorm worm)
        {
            Vector2 pos = worm.PixelPosition;
            DrawEntityHighlight(sb, assets, worm, DeepBurrowWorm.WidthPx, DeepBurrowWorm.HeightPx);

            if (worm.IsBurrowing)
            {
                // Show only the tail end disappearing into the ground
                var dirtColor = new Color(120, 80, 40, 180);
                GeometryBatch.DrawCircleApprox(sb, assets, pos,
                    DeepBurrowWorm.WidthPx * 0.6f, dirtColor, 8);
                return;
            }

            var bodyColor = worm.IsControlled
                ? new Color(160, 120, 80)
                : new Color(100, 70, 40);

            // Thick segmented body (vertical orientation)
            int segs = 5;
            float segH = DeepBurrowWorm.HeightPx / segs;
            for (int i = 0; i < segs; i++)
            {
                float sy = pos.Y - DeepBurrowWorm.HeightPx * 0.5f + segH * (i + 0.5f);
                GeometryBatch.DrawCircleApprox(sb, assets,
                    new Vector2(pos.X, sy), DeepBurrowWorm.WidthPx * 0.45f, bodyColor, 6);
            }

            // Head (top, slightly larger)
            GeometryBatch.DrawCircleApprox(sb, assets,
                new Vector2(pos.X, pos.Y - DeepBurrowWorm.HeightPx * 0.5f),
                DeepBurrowWorm.WidthPx * 0.55f, bodyColor, 8);

            if (worm.IsControlled)
            {
                DrawControlTimerBar(sb, assets, worm, DeepBurrowWorm.WidthPx);
                DrawSkillCooldownPip(sb, assets, worm, DeepBurrowWorm.WidthPx);
            }
        }

        // ── Blind Cave Salamander ──────────────────────────────────────────────

        public static void DrawBlindCaveSalamander(SpriteBatch sb, AssetManager assets,
            BlindCaveSalamander salamander)
        {
            Vector2 pos = salamander.PixelPosition;
            DrawEntityHighlight(sb, assets, salamander,
                BlindCaveSalamander.WidthPx, BlindCaveSalamander.HeightPx);

            var bodyColor = salamander.IsControlled
                ? new Color(220, 200, 180)
                : new Color(180, 160, 140);
            var legColor = salamander.IsControlled
                ? new Color(200, 180, 160)
                : new Color(160, 140, 120);

            // 4 tiny legs
            float legY = pos.Y + BlindCaveSalamander.HeightPx * 0.3f;
            for (int i = 0; i < 4; i++)
            {
                float lx = pos.X - BlindCaveSalamander.WidthPx * 0.4f +
                           (i / 3f) * BlindCaveSalamander.WidthPx * 0.8f;
                GeometryBatch.DrawLine(sb, assets,
                    new Vector2(lx, pos.Y),
                    new Vector2(lx + (i % 2 == 0 ? -3f : 3f), legY), legColor, 1);
            }

            // Elongated body
            {
                int bw = (int)BlindCaveSalamander.WidthPx;
                int bh = (int)(BlindCaveSalamander.HeightPx * 0.6f);
                GeometryBatch.DrawRoundedRect(sb, assets,
                    new Rectangle((int)pos.X - bw / 2, (int)pos.Y - bh / 2, bw, bh),
                    4, bodyColor);
            }

            // Tail
            GeometryBatch.DrawLine(sb, assets,
                new Vector2(pos.X - BlindCaveSalamander.WidthPx * 0.5f, pos.Y),
                new Vector2(pos.X - BlindCaveSalamander.WidthPx * 0.8f, pos.Y + 2f),
                bodyColor, 2);

            // Slime trail (if skill active)
            if (salamander.IsControlled && salamander.Skill is { IsActive: true })
            {
                var slimeColor = new Color(100, 200, 80, 120);
                GeometryBatch.DrawCircleApprox(sb, assets,
                    new Vector2(pos.X - BlindCaveSalamander.WidthPx * 0.5f, pos.Y),
                    4f, slimeColor, 6);
            }

            if (salamander.IsControlled)
            {
                DrawControlTimerBar(sb, assets, salamander, BlindCaveSalamander.WidthPx);
                DrawSkillCooldownPip(sb, assets, salamander, BlindCaveSalamander.WidthPx);
            }
        }

        // ── Luminous Isopod ────────────────────────────────────────────────────

        public static void DrawLuminousIsopod(SpriteBatch sb, AssetManager assets,
            LuminousIsopod isopod)
        {
            Vector2 pos = isopod.PixelPosition;
            DrawEntityHighlight(sb, assets, isopod, LuminousIsopod.WidthPx, LuminousIsopod.HeightPx);

            // Passive glow aura
            float glowPulse = 0.6f + 0.4f * (float)Math.Sin(AnimationClock.Pulse(1.5f));
            var glowColor = new Color(60, 180, 220, (int)(80 * glowPulse));
            GeometryBatch.DrawCircleApprox(sb, assets, pos,
                LuminousIsopod.WidthPx * 1.5f, glowColor, 12);

            var bodyColor = isopod.IsControlled
                ? new Color(80, 200, 240)
                : new Color(40, 140, 180);
            var legColor = isopod.IsControlled
                ? new Color(60, 170, 210)
                : new Color(30, 110, 150);

            // Many tiny legs (7 per side)
            for (int i = 0; i < 7; i++)
            {
                float lx = pos.X - LuminousIsopod.WidthPx * 0.45f +
                           (i / 6f) * LuminousIsopod.WidthPx * 0.9f;
                float legLen = LuminousIsopod.HeightPx * 0.5f;
                GeometryBatch.DrawLine(sb, assets,
                    new Vector2(lx, pos.Y),
                    new Vector2(lx, pos.Y + legLen), legColor, 1);
                GeometryBatch.DrawLine(sb, assets,
                    new Vector2(lx, pos.Y),
                    new Vector2(lx, pos.Y - legLen), legColor, 1);
            }

            // Oval segmented body
            {
                int iw = (int)LuminousIsopod.WidthPx;
                int ih = (int)(LuminousIsopod.HeightPx * 0.7f);
                GeometryBatch.DrawRoundedRect(sb, assets,
                    new Rectangle((int)pos.X - iw / 2, (int)pos.Y - ih / 2, iw, ih),
                    3, bodyColor);
            }

            // Glow Surge pulse
            if (isopod.GlowSurgeActive)
            {
                float alpha = 1f - isopod.GlowSurgeRadius / LuminousIsopod.GlowSurgeMaxRadius;
                var surgeColor = new Color(100, 220, 255, (int)(alpha * 180));
                GeometryBatch.DrawCircleOutline(sb, assets, pos, isopod.GlowSurgeRadius, surgeColor, 2);
            }

            // Throw trajectory preview (when T is held)
            if (isopod.IsControlled && isopod.ShowTrajectory)
                DrawIsopodTrajectory(sb, assets, isopod);

            if (isopod.IsControlled)
            {
                DrawControlTimerBar(sb, assets, isopod, LuminousIsopod.WidthPx);
                DrawSkillCooldownPip(sb, assets, isopod, LuminousIsopod.WidthPx);
            }
        }

        // ── Shared helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Draw the selection/control highlight outline around an entity.
        /// </summary>
        private static void DrawEntityHighlight(SpriteBatch sb, AssetManager assets,
            ControllableEntity entity, float widthPx, float heightPx)
        {
            if (entity.IsControlled)
            {
                // Pulsing cyan outline while controlled
                float pulse = 0.6f + 0.4f * (float)Math.Sin(AnimationClock.Pulse(3f));
                var color = new Color(
                    ControlHighlightColor.R,
                    ControlHighlightColor.G,
                    ControlHighlightColor.B,
                    (int)(ControlHighlightColor.A * pulse));
                DrawEntityOutline(sb, assets, entity.PixelPosition, widthPx + 6f, heightPx + 6f, color, 2);
            }
            else if (entity.IsHighlighted)
            {
                // Bright yellow outline when nearest to cursor in selection mode
                DrawEntityOutline(sb, assets, entity.PixelPosition, widthPx + 8f, heightPx + 8f,
                    SelectionHighlightColor, 2);
            }
            else if (entity.IsInRange)
            {
                // Dim green outline when in selection range
                DrawEntityOutline(sb, assets, entity.PixelPosition, widthPx + 4f, heightPx + 4f,
                    InRangeHighlightColor, 1);
            }
        }

        private static void DrawEntityOutline(SpriteBatch sb, AssetManager assets,
            Vector2 center, float w, float h, Color color, int thickness)
        {
            // Draw as a rounded rectangle outline
            var rect = new Microsoft.Xna.Framework.Rectangle(
                (int)(center.X - w / 2f),
                (int)(center.Y - h / 2f),
                (int)w, (int)h);
            assets.DrawRectOutline(sb, rect, color, thickness);
        }

        /// <summary>
        /// Draw the control duration timer bar above the entity.
        /// </summary>
        private static void DrawControlTimerBar(SpriteBatch sb, AssetManager assets,
            ControllableEntity entity, float entityWidthPx)
        {
            float barW  = Math.Max(entityWidthPx + 8f, 30f);
            float barH  = 3f;
            float barX  = entity.PixelPosition.X - barW / 2f;
            float barY  = entity.PixelPosition.Y - entityWidthPx - 10f;

            float fraction = entity.ControlDuration > 0f
                ? MathHelper.Clamp(entity.ControlTimer / entity.ControlDuration, 0f, 1f)
                : 0f;

            // Background
            assets.DrawRect(sb,
                new Vector2(barX, barY),
                new Vector2(barW, barH),
                TimerBarBg);

            // Foreground
            if (fraction > 0f)
                assets.DrawRect(sb,
                    new Vector2(barX, barY),
                    new Vector2(barW * fraction, barH),
                    TimerBarFg);
        }

        /// <summary>
        /// Draw a small skill cooldown pip below the timer bar.
        /// Green = ready, grey = on cooldown with fill showing progress.
        /// </summary>
        private static void DrawSkillCooldownPip(SpriteBatch sb, AssetManager assets,
            ControllableEntity entity, float entityWidthPx)
        {
            if (entity.Skill == null) return;

            float pipSize = 5f;
            float pipX    = entity.PixelPosition.X - pipSize / 2f;
            float pipY    = entity.PixelPosition.Y - entityWidthPx - 6f;

            if (entity.Skill.IsReady)
            {
                assets.DrawRect(sb,
                    new Vector2(pipX, pipY),
                    new Vector2(pipSize, pipSize),
                    SkillReadyColor);
            }
            else
            {
                assets.DrawRect(sb,
                    new Vector2(pipX, pipY),
                    new Vector2(pipSize, pipSize),
                    SkillCooldownColor);

                float fraction = entity.Skill.Cooldown > 0f
                    ? 1f - entity.Skill.CooldownTimer / entity.Skill.Cooldown
                    : 1f;
                assets.DrawRect(sb,
                    new Vector2(pipX, pipY + pipSize * (1f - fraction)),
                    new Vector2(pipSize, pipSize * fraction),
                    SkillReadyColor);
            }
        }

        /// <summary>
        /// Draw the 200px selection range circle around the player in world space.
        /// Call during selection mode before drawing entities.
        /// </summary>
        public static void DrawSelectionRangeCircle(SpriteBatch sb, AssetManager assets,
            Vector2 playerPixelPos)
        {
            GeometryBatch.DrawCircleOutline(sb, assets, playerPixelPos,
                EntityControlSystem.SelectionRange, RangeCircleColor, 1);
        }

        /// <summary>
        /// Draw the isopod throw trajectory arc (parabolic dotted line).
        /// </summary>
        private static void DrawIsopodTrajectory(SpriteBatch sb, AssetManager assets,
            LuminousIsopod isopod)
        {
            var points = isopod.TrajectoryPoints;
            if (points == null || points.Length < 2) return;

            var arcColor = new Color(80, 200, 240, 160);
            for (int i = 0; i < points.Length - 1; i++)
            {
                // Dashed: draw every other segment
                if (i % 2 == 0)
                    GeometryBatch.DrawLine(sb, assets, points[i], points[i + 1], arcColor, 1);
            }

            // Landing point marker
            var landColor = new Color(80, 200, 240, 200);
            GeometryBatch.DrawCircleOutline(sb, assets, points[^1], 4f, landColor, 1);
        }

        // ── Web trail helper ───────────────────────────────────────────────────

        private static void DrawWebTrail(SpriteBatch sb, AssetManager assets,
            SilkWeaverSpider spider)
        {
            var trailColor = new Color(200, 200, 255, 100);
            var trail = spider.WebTrailPoints;
            if (trail == null || trail.Count < 2) return;

            var pts = new System.Collections.Generic.List<Vector2>(trail);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                if (i % 2 == 0)
                    GeometryBatch.DrawLine(sb, assets, pts[i], pts[i + 1], trailColor, 1);
            }
        }
    }
}
