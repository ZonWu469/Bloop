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
    /// effect state overlays, and the selection-mode range circle (drawn in world space).
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
        private static readonly Color DangerAuraColor         = new Color(255, 80, 40, 60);
        private static readonly Color DangerSparkColor        = new Color(255, 120, 60);

        // ── Player position for danger proximity scaling ───────────────────────
        /// <summary>
        /// Set this each frame before drawing entities so danger indicators can
        /// scale their intensity based on distance to the player.
        /// </summary>
        public static Vector2 PlayerPositionForDanger { get; set; } = Vector2.Zero;

        // ══════════════════════════════════════════════════════════════════════
        // ── Echo Bat ──────────────────────────────────────────────────────────
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draw an Echo Bat. Body: small dark ellipse with pointed ears.
        /// Wings: two angular triangles that flap. Fanged mouth when idle.
        /// Pulse ring: expanding circle when Sonic Pulse fires.
        /// </summary>
        public static void DrawEchoBat(SpriteBatch sb, AssetManager assets, EchoBat bat)
        {
            Vector2 pos = bat.PixelPosition;

            DrawDangerIndicator(sb, assets, bat, EchoBat.WidthPx, EchoBat.HeightPx);
            DrawEntityHighlight(sb, assets, bat, EchoBat.WidthPx, EchoBat.HeightPx);

            // Pulse ring
            if (bat.PulseActive && bat.PulseRadius > 0f)
            {
                float alpha = 1f - bat.PulseRadius / EchoBat.PulseMaxRadius;
                var pulseColor = new Color(180, 220, 255, (int)(alpha * 160));
                GeometryBatch.DrawCircleOutline(sb, assets, pos, bat.PulseRadius, pulseColor, 2);
            }

            // Wings
            float flapAngle = (float)Math.Sin(bat.WingPhase * Math.PI * 2.0) * 0.4f;
            float wingSpan  = EchoBat.WidthPx * 0.9f;
            float wingDrop  = EchoBat.HeightPx * 0.3f + flapAngle * EchoBat.HeightPx * 0.5f;
            var wingTipL = new Vector2(pos.X - wingSpan, pos.Y + wingDrop);
            var wingTipR = new Vector2(pos.X + wingSpan, pos.Y + wingDrop);
            var wingColor = bat.IsControlled ? new Color(60, 100, 160) : new Color(40, 40, 60);

            GeometryBatch.DrawTriangleSolid(sb, assets,
                pos, wingTipL, new Vector2(pos.X - EchoBat.WidthPx * 0.4f, pos.Y), wingColor);
            GeometryBatch.DrawTriangleSolid(sb, assets,
                pos, wingTipR, new Vector2(pos.X + EchoBat.WidthPx * 0.4f, pos.Y), wingColor);

            // Body
            var bodyColor = bat.IsControlled ? new Color(80, 130, 200) : new Color(50, 50, 70);
            GeometryBatch.DrawCircleApprox(sb, assets, pos, EchoBat.WidthPx * 0.35f, bodyColor, 8);

            // Pointed ears (two small triangles on top of head)
            var earColor = bat.IsControlled ? new Color(100, 150, 220) : new Color(60, 60, 80);
            float earOff = EchoBat.WidthPx * 0.18f;
            float earH   = EchoBat.HeightPx * 0.7f;
            float earBaseY = pos.Y - EchoBat.HeightPx * 0.3f;
            GeometryBatch.DrawTriangleSolid(sb, assets,
                new Vector2(pos.X - earOff - 2f, earBaseY),
                new Vector2(pos.X - earOff + 2f, earBaseY),
                new Vector2(pos.X - earOff, earBaseY - earH), earColor);
            GeometryBatch.DrawTriangleSolid(sb, assets,
                new Vector2(pos.X + earOff - 2f, earBaseY),
                new Vector2(pos.X + earOff + 2f, earBaseY),
                new Vector2(pos.X + earOff, earBaseY - earH), earColor);

            // Eyes
            var eyeColor = bat.IsControlled ? new Color(200, 240, 255) : new Color(180, 60, 60);
            float eyeOff = EchoBat.WidthPx * 0.12f;
            GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(pos.X - eyeOff, pos.Y - 1f), 1.5f, eyeColor, 4);
            GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(pos.X + eyeOff, pos.Y - 1f), 1.5f, eyeColor, 4);

            // Fanged mouth (V-shape below body, idle only)
            if (!bat.IsControlled)
            {
                var fangColor = new Color(200, 80, 80, 160);
                float mouthY = pos.Y + EchoBat.HeightPx * 0.3f;
                GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X - 2f, mouthY), new Vector2(pos.X, mouthY + 2f), fangColor, 1f);
                GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X + 2f, mouthY), new Vector2(pos.X, mouthY + 2f), fangColor, 1f);
            }

            // Ambient aura: echolocation arcs (idle only)
            if (!bat.IsControlled)
            {
                float t = AnimationClock.Time;
                for (int arc = 0; arc < 3; arc++)
                {
                    float arcPhase = (t * 0.8f + arc * 0.4f) % 1f;
                    float arcR     = 8f + arcPhase * 28f;
                    float arcAlpha = (1f - arcPhase) * 0.22f;
                    if (arcAlpha > 0.02f)
                    {
                        var arcColor = new Color(140, 200, 255, (int)(arcAlpha * 255));
                        int arcSegs = 8;
                        for (int s = 0; s < arcSegs; s++)
                        {
                            float a0 = -MathHelper.PiOver2 + (s / (float)arcSegs) * MathHelper.Pi;
                            float a1 = -MathHelper.PiOver2 + ((s + 0.8f) / arcSegs) * MathHelper.Pi;
                            Vector2 p0 = pos + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * arcR;
                            Vector2 p1 = pos + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * arcR;
                            GeometryBatch.DrawLine(sb, assets, p0, p1, arcColor, 1f);
                        }
                    }
                }
            }

            DrawEffectState(sb, assets, bat, EchoBat.WidthPx, EchoBat.HeightPx);

            if (bat.IsControlled)
            {
                DrawControlTimerBar(sb, assets, bat, EchoBat.WidthPx);
                DrawSkillCooldownPip(sb, assets, bat, EchoBat.WidthPx);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // ── Silk Weaver Spider ────────────────────────────────────────────────
        // ══════════════════════════════════════════════════════════════════════

        public static void DrawSilkWeaverSpider(SpriteBatch sb, AssetManager assets,
            SilkWeaverSpider spider)
        {
            Vector2 pos = spider.PixelPosition;
            DrawDangerIndicator(sb, assets, spider, SilkWeaverSpider.WidthPx, SilkWeaverSpider.HeightPx);
            DrawEntityHighlight(sb, assets, spider, SilkWeaverSpider.WidthPx, SilkWeaverSpider.HeightPx);

            var bodyColor = spider.IsControlled ? new Color(160, 100, 200) : new Color(80, 50, 100);
            var legColor  = spider.IsControlled ? new Color(130, 80, 170)  : new Color(60, 40, 80);

            // 8 two-segment legs with knee joints
            for (int i = 0; i < 4; i++)
            {
                float t      = (i / 3f) * 0.8f + 0.1f;
                float legY   = pos.Y - SilkWeaverSpider.HeightPx * 0.3f + t * SilkWeaverSpider.HeightPx * 0.6f;
                float legXL  = pos.X - SilkWeaverSpider.WidthPx * 0.45f;
                float legXR  = pos.X + SilkWeaverSpider.WidthPx * 0.45f;
                float kneeOff = SilkWeaverSpider.WidthPx * 0.5f;
                float tipOff  = SilkWeaverSpider.WidthPx * 0.75f;
                float tipY    = legY + SilkWeaverSpider.HeightPx * 0.5f;
                float kneeY   = legY + SilkWeaverSpider.HeightPx * 0.2f;

                GeometryBatch.DrawLine(sb, assets, new Vector2(legXL, legY), new Vector2(legXL - kneeOff, kneeY), legColor, 1);
                GeometryBatch.DrawLine(sb, assets, new Vector2(legXL - kneeOff, kneeY), new Vector2(legXL - tipOff, tipY), legColor, 1);
                GeometryBatch.DrawLine(sb, assets, new Vector2(legXR, legY), new Vector2(legXR + kneeOff, kneeY), legColor, 1);
                GeometryBatch.DrawLine(sb, assets, new Vector2(legXR + kneeOff, kneeY), new Vector2(legXR + tipOff, tipY), legColor, 1);
            }

            // Diamond-shaped cephalothorax
            GeometryBatch.DrawDiamond(sb, assets, pos, SilkWeaverSpider.WidthPx * 0.22f, bodyColor);

            // Round abdomen (below)
            GeometryBatch.DrawCircleApprox(sb, assets,
                new Vector2(pos.X, pos.Y + SilkWeaverSpider.HeightPx * 0.4f),
                SilkWeaverSpider.WidthPx * 0.32f, bodyColor, 8);

            // Fangs (two small downward lines from head)
            var fangColor = spider.IsControlled ? new Color(200, 140, 255) : new Color(120, 60, 140);
            float fangY = pos.Y - SilkWeaverSpider.HeightPx * 0.15f;
            GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X - 3f, fangY), new Vector2(pos.X - 4f, fangY + 4f), fangColor, 1.5f);
            GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X + 3f, fangY), new Vector2(pos.X + 4f, fangY + 4f), fangColor, 1.5f);

            // Spinnerets (two tiny dots at rear)
            var spinnColor = new Color(bodyColor.R, bodyColor.G, bodyColor.B, (byte)180);
            GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(pos.X - 2f, pos.Y + SilkWeaverSpider.HeightPx * 0.7f), 1.5f, spinnColor, 4);
            GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(pos.X + 2f, pos.Y + SilkWeaverSpider.HeightPx * 0.7f), 1.5f, spinnColor, 4);

            // Ambient aura: drifting silk dots
            {
                float t = AnimationClock.Time;
                for (int d = 0; d < 4; d++)
                {
                    float dPhase = (t * 0.4f + d * 0.25f) % 1f;
                    float dAlpha = MathF.Sin(dPhase * MathF.PI) * 0.22f;
                    if (dAlpha > 0.02f)
                    {
                        float dx = pos.X + MathF.Sin(t * 0.7f + d * 1.5f) * SilkWeaverSpider.WidthPx * 0.6f;
                        float dy = pos.Y + SilkWeaverSpider.HeightPx * 0.5f + dPhase * 10f;
                        GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(dx, dy), 1f,
                            new Color(200, 200, 255, (int)(dAlpha * 255)), 4);
                    }
                }
            }

            // Web trail (if skill active)
            if (spider.IsControlled && spider.Skill is { IsActive: true })
                DrawWebTrail(sb, assets, spider);

            DrawEffectState(sb, assets, spider, SilkWeaverSpider.WidthPx, SilkWeaverSpider.HeightPx);

            if (spider.IsControlled)
            {
                DrawControlTimerBar(sb, assets, spider, SilkWeaverSpider.WidthPx);
                DrawSkillCooldownPip(sb, assets, spider, SilkWeaverSpider.WidthPx);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // ── Chain Centipede ───────────────────────────────────────────────────
        // ══════════════════════════════════════════════════════════════════════

        public static void DrawChainCentipede(SpriteBatch sb, AssetManager assets,
            ChainCentipede centipede)
        {
            Vector2 pos = centipede.PixelPosition;
            DrawDangerIndicator(sb, assets, centipede, ChainCentipede.WidthPx, ChainCentipede.HeightPx);
            DrawEntityHighlight(sb, assets, centipede, ChainCentipede.WidthPx, ChainCentipede.HeightPx);

            var segColor = centipede.IsControlled ? new Color(220, 140, 60) : new Color(140, 90, 40);
            var legColor = centipede.IsControlled ? new Color(200, 120, 40) : new Color(120, 70, 30);

            // 7 alternating-size body segments
            int segments = 7;
            float segW = ChainCentipede.WidthPx / segments;
            for (int i = 0; i < segments; i++)
            {
                float sx = pos.X - ChainCentipede.WidthPx * 0.5f + segW * (i + 0.5f);
                float segRadius = (i % 2 == 0) ? segW * 0.48f : segW * 0.36f;
                GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(sx, pos.Y), segRadius, segColor, 6);

                float legLen = ChainCentipede.HeightPx * 0.7f;
                GeometryBatch.DrawLine(sb, assets, new Vector2(sx, pos.Y), new Vector2(sx - 2f, pos.Y - legLen), legColor, 1);
                GeometryBatch.DrawLine(sb, assets, new Vector2(sx, pos.Y), new Vector2(sx - 2f, pos.Y + legLen), legColor, 1);
            }

            // Head (larger, with mandibles)
            float headX = pos.X + ChainCentipede.WidthPx * 0.5f;
            GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(headX, pos.Y), segW * 0.65f, segColor, 6);

            var mandibleColor = centipede.IsControlled ? new Color(255, 180, 80) : new Color(160, 100, 40);
            GeometryBatch.DrawLine(sb, assets, new Vector2(headX + segW * 0.5f, pos.Y), new Vector2(headX + segW * 0.9f, pos.Y - 3f), mandibleColor, 1.5f);
            GeometryBatch.DrawLine(sb, assets, new Vector2(headX + segW * 0.5f, pos.Y), new Vector2(headX + segW * 0.9f, pos.Y + 3f), mandibleColor, 1.5f);

            // Antennae
            var antennaColor = new Color(legColor.R, legColor.G, legColor.B, (byte)180);
            GeometryBatch.DrawLine(sb, assets, new Vector2(headX + segW * 0.4f, pos.Y - 2f), new Vector2(headX + segW * 1.2f, pos.Y - 6f), antennaColor, 1f);
            GeometryBatch.DrawLine(sb, assets, new Vector2(headX + segW * 0.4f, pos.Y + 2f), new Vector2(headX + segW * 1.2f, pos.Y + 6f), antennaColor, 1f);

            // Aggression pheromone pulse
            if (centipede.PulseActive)
            {
                float alpha = 1f - centipede.PulseRadius / ChainCentipede.PulseMaxRadius;
                var pulseColor = new Color(255, 160, 40, (int)(alpha * 140));
                GeometryBatch.DrawCircleOutline(sb, assets, pos, centipede.PulseRadius, pulseColor, 2);
            }

            // Ambient aura: heat shimmer lines above (idle only)
            if (!centipede.IsControlled)
            {
                float t = AnimationClock.Time;
                for (int v = 0; v < 3; v++)
                {
                    float vPhase = (t * 2f + v * 0.33f) % 1f;
                    float vAlpha = MathF.Sin(vPhase * MathF.PI) * 0.18f;
                    if (vAlpha > 0.02f)
                    {
                        float vx = pos.X - ChainCentipede.WidthPx * 0.3f + v * (ChainCentipede.WidthPx * 0.3f);
                        float vy = pos.Y - ChainCentipede.HeightPx * 0.5f - vPhase * 8f;
                        float vLen = 2f + MathF.Sin(t * 5f + v) * 1f;
                        GeometryBatch.DrawLine(sb, assets,
                            new Vector2(vx - vLen, vy), new Vector2(vx + vLen, vy),
                            new Color(220, 160, 80, (int)(vAlpha * 255)), 1f);
                    }
                }
            }

            DrawEffectState(sb, assets, centipede, ChainCentipede.WidthPx, ChainCentipede.HeightPx);

            if (centipede.IsControlled)
            {
                DrawControlTimerBar(sb, assets, centipede, ChainCentipede.WidthPx);
                DrawSkillCooldownPip(sb, assets, centipede, ChainCentipede.WidthPx);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // ── Luminescent Glowworm ──────────────────────────────────────────────
        // ══════════════════════════════════════════════════════════════════════

        public static void DrawLuminescentGlowworm(SpriteBatch sb, AssetManager assets,
            LuminescentGlowworm worm)
        {
            Vector2 pos = worm.PixelPosition;
            DrawEntityHighlight(sb, assets, worm, LuminescentGlowworm.WidthPx, LuminescentGlowworm.HeightPx);

            // Subtle glow aura
            float glowPulse = 0.7f + 0.3f * (float)Math.Sin(AnimationClock.Pulse(2f));
            var glowColor = new Color(180, 255, 120, (int)(32 * glowPulse));
            GeometryBatch.DrawCircleApprox(sb, assets, pos, LuminescentGlowworm.WidthPx * 1.1f, glowColor, 12);

            var bodyColor = worm.IsControlled ? new Color(160, 255, 100) : new Color(100, 200, 60);

            // Elongated oval body — 5 tapering segments (distinct from centipede)
            int segs = 5;
            float segW = LuminescentGlowworm.WidthPx / segs;
            for (int i = 0; i < segs; i++)
            {
                float sx = pos.X - LuminescentGlowworm.WidthPx * 0.5f + segW * (i + 0.5f);
                float taper = (i == 0 || i == segs - 1) ? 0.35f : 0.45f;
                GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(sx, pos.Y), segW * taper, bodyColor, 8);
            }

            // Glowing tail tip (brighter last segment)
            float tailX = pos.X - LuminescentGlowworm.WidthPx * 0.5f + segW * 0.5f;
            var tailColor = worm.IsControlled ? new Color(220, 255, 160, 200) : new Color(160, 255, 80, 180);
            GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(tailX, pos.Y), segW * 0.4f, tailColor, 8);

            // Tiny antennae at head
            float headX = pos.X + LuminescentGlowworm.WidthPx * 0.5f - segW * 0.5f;
            var antennaColor = new Color(bodyColor.R, bodyColor.G, bodyColor.B, (byte)160);
            GeometryBatch.DrawLine(sb, assets, new Vector2(headX, pos.Y - 2f), new Vector2(headX + 3f, pos.Y - 5f), antennaColor, 1f);
            GeometryBatch.DrawLine(sb, assets, new Vector2(headX, pos.Y - 2f), new Vector2(headX + 4f, pos.Y - 3f), antennaColor, 1f);

            // Flash effect
            if (worm.FlashActive)
            {
                float flashAlpha = 1f - worm.FlashRadius / LuminescentGlowworm.FlashMaxRadius;
                var flashColor = new Color(220, 255, 180, (int)(flashAlpha * 200));
                GeometryBatch.DrawCircleApprox(sb, assets, pos, worm.FlashRadius, flashColor, 16);
            }

            // Ambient aura: synchronized pulse rings (idle only)
            if (!worm.IsControlled)
            {
                float t = AnimationClock.Time;
                float syncT = (t * 0.5f + worm.SyncPhase / (MathF.PI * 2f)) % 1f;
                float ringR  = syncT * 18f;
                float ringA  = (1f - syncT) * 0.16f;
                if (ringA > 0.01f)
                {
                    GeometryBatch.DrawCircleOutline(sb, assets, pos, ringR,
                        new Color(160, 255, 100, (int)(ringA * 255)), 8);
                }
            }

            DrawEffectState(sb, assets, worm, LuminescentGlowworm.WidthPx, LuminescentGlowworm.HeightPx);

            if (worm.IsControlled)
            {
                DrawControlTimerBar(sb, assets, worm, LuminescentGlowworm.WidthPx);
                DrawSkillCooldownPip(sb, assets, worm, LuminescentGlowworm.WidthPx);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // ── Deep Burrow Worm ──────────────────────────────────────────────────
        // ══════════════════════════════════════════════════════════════════════

        public static void DrawDeepBurrowWorm(SpriteBatch sb, AssetManager assets,
            DeepBurrowWorm worm)
        {
            Vector2 pos = worm.PixelPosition;
            DrawDangerIndicator(sb, assets, worm, DeepBurrowWorm.WidthPx, DeepBurrowWorm.HeightPx);
            DrawEntityHighlight(sb, assets, worm, DeepBurrowWorm.WidthPx, DeepBurrowWorm.HeightPx);

            if (worm.IsBurrowing)
            {
                var dirtColor = new Color(120, 80, 40, 180);
                GeometryBatch.DrawCircleApprox(sb, assets, pos, DeepBurrowWorm.WidthPx * 0.6f, dirtColor, 8);
                // Dust particles rising while burrowing
                float t2 = AnimationClock.Time;
                for (int d = 0; d < 3; d++)
                {
                    float dp = (t2 * 1.5f + d * 0.33f) % 1f;
                    float da = MathF.Sin(dp * MathF.PI) * 0.3f;
                    if (da > 0.02f)
                    {
                        float dx = pos.X + MathF.Sin(t2 * 2f + d) * 4f;
                        float dy = pos.Y - dp * 12f;
                        GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(dx, dy), 1.5f,
                            new Color(160, 120, 60, (int)(da * 255)), 4);
                    }
                }
                return;
            }

            var bodyColor = worm.IsControlled ? new Color(160, 120, 80) : new Color(100, 70, 40);

            // Tapering segmented body (vertical orientation, distinct from glowworm)
            int segs = 5;
            float segH = DeepBurrowWorm.HeightPx / segs;
            for (int i = 0; i < segs; i++)
            {
                float sy = pos.Y - DeepBurrowWorm.HeightPx * 0.5f + segH * (i + 0.5f);
                float taper = 0.45f - i * 0.04f;
                GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(pos.X, sy), DeepBurrowWorm.WidthPx * taper, bodyColor, 6);

                // Ring markings between segments
                if (i < segs - 1)
                {
                    float ringY = pos.Y - DeepBurrowWorm.HeightPx * 0.5f + segH * (i + 1f);
                    var ringColor = new Color((int)(bodyColor.R * 0.7f), (int)(bodyColor.G * 0.7f), (int)(bodyColor.B * 0.7f), 180);
                    GeometryBatch.DrawLine(sb, assets,
                        new Vector2(pos.X - DeepBurrowWorm.WidthPx * 0.35f, ringY),
                        new Vector2(pos.X + DeepBurrowWorm.WidthPx * 0.35f, ringY),
                        ringColor, 1f);
                }
            }

            // Jaw mandibles at head (top)
            float headY = pos.Y - DeepBurrowWorm.HeightPx * 0.5f;
            var mandibleColor = worm.IsControlled ? new Color(200, 160, 100) : new Color(130, 90, 50);
            GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X - 3f, headY), new Vector2(pos.X - 5f, headY - 5f), mandibleColor, 1.5f);
            GeometryBatch.DrawLine(sb, assets, new Vector2(pos.X + 3f, headY), new Vector2(pos.X + 5f, headY - 5f), mandibleColor, 1.5f);

            // Ambient aura: dust motes rising (idle only)
            if (!worm.IsControlled)
            {
                float t = AnimationClock.Time;
                for (int d = 0; d < 3; d++)
                {
                    float dp = (t * 0.6f + d * 0.33f) % 1f;
                    float da = MathF.Sin(dp * MathF.PI) * 0.15f;
                    if (da > 0.01f)
                    {
                        float dx = pos.X + MathF.Sin(t * 1.2f + d * 2.1f) * DeepBurrowWorm.WidthPx * 0.4f;
                        float dy = pos.Y + DeepBurrowWorm.HeightPx * 0.5f - dp * 14f;
                        GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(dx, dy), 1.2f,
                            new Color(140, 100, 60, (int)(da * 255)), 4);
                    }
                }
            }

            DrawEffectState(sb, assets, worm, DeepBurrowWorm.WidthPx, DeepBurrowWorm.HeightPx);

            if (worm.IsControlled)
            {
                DrawControlTimerBar(sb, assets, worm, DeepBurrowWorm.WidthPx);
                DrawSkillCooldownPip(sb, assets, worm, DeepBurrowWorm.WidthPx);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // ── Blind Cave Salamander ─────────────────────────────────────────────
        // ══════════════════════════════════════════════════════════════════════

        public static void DrawBlindCaveSalamander(SpriteBatch sb, AssetManager assets,
            BlindCaveSalamander salamander)
        {
            Vector2 pos = salamander.PixelPosition;
            DrawDangerIndicator(sb, assets, salamander, BlindCaveSalamander.WidthPx, BlindCaveSalamander.HeightPx);
            DrawEntityHighlight(sb, assets, salamander, BlindCaveSalamander.WidthPx, BlindCaveSalamander.HeightPx);

            var bodyColor = salamander.IsControlled ? new Color(80, 200, 160) : new Color(50, 130, 100);
            var legColor  = salamander.IsControlled ? new Color(60, 170, 130) : new Color(40, 110, 80);

            // 4 stubby legs (wide stance, distinct from spider)
            float legSpanX = BlindCaveSalamander.WidthPx * 0.38f;
            float legSpanY = BlindCaveSalamander.HeightPx * 0.55f;
            // Front legs
            GeometryBatch.DrawLine(sb, assets,
                new Vector2(pos.X - legSpanX * 0.5f, pos.Y - BlindCaveSalamander.HeightPx * 0.1f),
                new Vector2(pos.X - legSpanX, pos.Y + legSpanY), legColor, 2f);
            GeometryBatch.DrawLine(sb, assets,
                new Vector2(pos.X + legSpanX * 0.5f, pos.Y - BlindCaveSalamander.HeightPx * 0.1f),
                new Vector2(pos.X + legSpanX, pos.Y + legSpanY), legColor, 2f);
            // Rear legs
            GeometryBatch.DrawLine(sb, assets,
                new Vector2(pos.X - legSpanX * 0.4f, pos.Y + BlindCaveSalamander.HeightPx * 0.1f),
                new Vector2(pos.X - legSpanX * 0.9f, pos.Y + legSpanY), legColor, 2f);
            GeometryBatch.DrawLine(sb, assets,
                new Vector2(pos.X + legSpanX * 0.4f, pos.Y + BlindCaveSalamander.HeightPx * 0.1f),
                new Vector2(pos.X + legSpanX * 0.9f, pos.Y + legSpanY), legColor, 2f);

            // Wide flat body (rounded rect, landscape orientation)
            GeometryBatch.DrawRoundedRect(sb, assets,
                new Rectangle(
                    (int)(pos.X - BlindCaveSalamander.WidthPx * 0.45f),
                    (int)(pos.Y - BlindCaveSalamander.HeightPx * 0.22f),
                    (int)(BlindCaveSalamander.WidthPx * 0.9f),
                    (int)(BlindCaveSalamander.HeightPx * 0.44f)),
                4, bodyColor);

            // Wide flat head (distinct from body — wider, flatter)
            float headW = BlindCaveSalamander.WidthPx * 0.38f;
            float headH = BlindCaveSalamander.HeightPx * 0.28f;
            float headX = pos.X + BlindCaveSalamander.WidthPx * 0.38f;
            GeometryBatch.DrawRoundedRect(sb, assets,
                new Rectangle(
                    (int)(headX - headW * 0.5f),
                    (int)(pos.Y - headH * 0.5f),
                    (int)headW, (int)headH),
                3, bodyColor);

            // Gill fronds (3 feathery lines on each side of neck)
            var gillColor = salamander.IsControlled ? new Color(120, 240, 180, 180) : new Color(80, 160, 120, 160);
            float neckX = pos.X + BlindCaveSalamander.WidthPx * 0.2f;
            for (int g = 0; g < 3; g++)
            {
                float gOff = g * 3f;
                float gLen = 5f - g * 1f;
                GeometryBatch.DrawLine(sb, assets,
                    new Vector2(neckX, pos.Y - BlindCaveSalamander.HeightPx * 0.18f + gOff),
                    new Vector2(neckX + gLen, pos.Y - BlindCaveSalamander.HeightPx * 0.35f + gOff),
                    gillColor, 1f);
                GeometryBatch.DrawLine(sb, assets,
                    new Vector2(neckX, pos.Y - BlindCaveSalamander.HeightPx * 0.18f + gOff),
                    new Vector2(neckX - gLen, pos.Y - BlindCaveSalamander.HeightPx * 0.35f + gOff),
                    gillColor, 1f);
            }

            // Long tapering tail (to the left)
            float tailStartX = pos.X - BlindCaveSalamander.WidthPx * 0.45f;
            float tailEndX   = pos.X - BlindCaveSalamander.WidthPx * 0.85f;
            GeometryBatch.DrawLine(sb, assets,
                new Vector2(tailStartX, pos.Y),
                new Vector2(tailEndX, pos.Y + BlindCaveSalamander.HeightPx * 0.15f),
                bodyColor, 2.5f);
            GeometryBatch.DrawLine(sb, assets,
                new Vector2(tailEndX, pos.Y + BlindCaveSalamander.HeightPx * 0.15f),
                new Vector2(tailEndX - 4f, pos.Y + BlindCaveSalamander.HeightPx * 0.25f),
                bodyColor, 1.5f);

            // Ambient aura: moisture droplets (idle only)
            if (!salamander.IsControlled)
            {
                float t = AnimationClock.Time;
                for (int d = 0; d < 4; d++)
                {
                    float dp = (t * 0.7f + d * 0.25f) % 1f;
                    float da = MathF.Sin(dp * MathF.PI) * 0.18f;
                    if (da > 0.01f)
                    {
                        float dx = pos.X + MathF.Sin(t * 0.9f + d * 1.6f) * BlindCaveSalamander.WidthPx * 0.5f;
                        float dy = pos.Y - BlindCaveSalamander.HeightPx * 0.3f - dp * 10f;
                        GeometryBatch.DrawCircleApprox(sb, assets, new Vector2(dx, dy), 1f,
                            new Color(100, 220, 180, (int)(da * 255)), 4);
                    }
                }
            }

            DrawEffectState(sb, assets, salamander, BlindCaveSalamander.WidthPx, BlindCaveSalamander.HeightPx);

            if (salamander.IsControlled)
            {
                DrawControlTimerBar(sb, assets, salamander, BlindCaveSalamander.WidthPx);
                DrawSkillCooldownPip(sb, assets, salamander, BlindCaveSalamander.WidthPx);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // ── Luminous Isopod ───────────────────────────────────────────────────
        // ══════════════════════════════════════════════════════════════════════

        public static void DrawLuminousIsopod(SpriteBatch sb, AssetManager assets,
            LuminousIsopod isopod)
        {
            Vector2 pos = isopod.PixelPosition;
            DrawEntityHighlight(sb, assets, isopod, LuminousIsopod.WidthPx, LuminousIsopod.HeightPx);

            var bodyColor  = isopod.IsControlled ? new Color(80, 200, 220) : new Color(50, 130, 150);
            var plateColor = isopod.IsControlled ? new Color(60, 170, 190) : new Color(40, 110, 130);
            var legColor   = isopod.IsControlled ? new Color(50, 150, 170) : new Color(35, 100, 120);

            // 7 pairs of tiny legs (isopod has many legs, distinct from salamander)
            int legPairs = 7;
            float legSpacing = LuminousIsopod.WidthPx / legPairs;
            for (int i = 0; i < legPairs; i++)
            {
                float lx = pos.X - LuminousIsopod.WidthPx * 0.5f + legSpacing * (i + 0.5f);
                float legLen = LuminousIsopod.HeightPx * 0.55f;
                GeometryBatch.DrawLine(sb, assets,
                    new Vector2(lx, pos.Y),
                    new Vector2(lx - 1f, pos.Y + legLen), legColor, 1f);
                GeometryBatch.DrawLine(sb, assets,
                    new Vector2(lx, pos.Y),
                    new Vector2(lx + 1f, pos.Y - legLen), legColor, 1f);
            }

            // Armored shell plates (5 overlapping rounded rects — distinct from salamander's smooth body)
            int plates = 5;
            float plateW = LuminousIsopod.WidthPx / plates;
            for (int i = 0; i < plates; i++)
            {
                float px = pos.X - LuminousIsopod.WidthPx * 0.5f + plateW * i;
                float plateH = LuminousIsopod.HeightPx * (0.7f - Math.Abs(i - plates / 2f) * 0.08f);
                GeometryBatch.DrawRoundedRect(sb, assets,
                    new Rectangle(
                        (int)px,
                        (int)(pos.Y - plateH * 0.5f),
                        (int)(plateW + 1f),
                        (int)plateH),
                    2, i % 2 == 0 ? bodyColor : plateColor);
            }

            // Antennae (two long, from head)
            float isopodHeadX = pos.X + LuminousIsopod.WidthPx * 0.5f;
            var antennaColor = new Color(bodyColor.R, bodyColor.G, bodyColor.B, (byte)180);
            GeometryBatch.DrawLine(sb, assets,
                new Vector2(isopodHeadX, pos.Y - 2f),
                new Vector2(isopodHeadX + LuminousIsopod.WidthPx * 0.4f, pos.Y - 6f),
                antennaColor, 1f);
            GeometryBatch.DrawLine(sb, assets,
                new Vector2(isopodHeadX, pos.Y + 2f),
                new Vector2(isopodHeadX + LuminousIsopod.WidthPx * 0.4f, pos.Y + 6f),
                antennaColor, 1f);

            // Tail fan (3 short lines at rear)
            float isopodTailX = pos.X - LuminousIsopod.WidthPx * 0.5f;
            var tailFanColor = new Color(plateColor.R, plateColor.G, plateColor.B, (byte)200);
            for (int f = -1; f <= 1; f++)
            {
                GeometryBatch.DrawLine(sb, assets,
                    new Vector2(isopodTailX, pos.Y + f * 2f),
                    new Vector2(isopodTailX - 5f, pos.Y + f * 5f),
                    tailFanColor, 1.5f);
            }

            // Glow surge effect
            if (isopod.GlowSurgeActive)
            {
                float surgeAlpha = 1f - isopod.GlowSurgeRadius / LuminousIsopod.GlowSurgeMaxRadius;
                var surgeColor = new Color(100, 220, 255, (int)(surgeAlpha * 180));
                GeometryBatch.DrawCircleOutline(sb, assets, pos, isopod.GlowSurgeRadius, surgeColor, 2);
            }

            // Ambient aura: shimmer (idle only)
            if (!isopod.IsControlled)
            {
                float t = AnimationClock.Time;
                float shimmerA = 0.08f + 0.06f * MathF.Sin(t * 3f);
                GeometryBatch.DrawCircleApprox(sb, assets, pos,
                    LuminousIsopod.WidthPx * 0.9f,
                    new Color(80, 200, 220, (int)(shimmerA * 255)), 12);
            }

            // Trajectory preview when attached
            if (isopod.ShowTrajectory)
                DrawIsopodTrajectory(sb, assets, isopod);

            DrawEffectState(sb, assets, isopod, LuminousIsopod.WidthPx, LuminousIsopod.HeightPx);

            if (isopod.IsControlled)
            {
                DrawControlTimerBar(sb, assets, isopod, LuminousIsopod.WidthPx);
                DrawSkillCooldownPip(sb, assets, isopod, LuminousIsopod.WidthPx);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // ── Shared helpers ────────────────────────────────────────────────────
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

        // ── Private helpers ───────────────────────────────────────────────────

        private static void DrawIsopodTrajectory(SpriteBatch sb, AssetManager assets,
            LuminousIsopod isopod)
        {
            var pts = isopod.TrajectoryPoints;
            if (pts == null || pts.Length < 2) return;

            var trailColor = new Color(80, 200, 220, 100);
            for (int i = 0; i < pts.Length - 1; i++)
            {
                float alpha = (i / (float)pts.Length) * 0.6f;
                GeometryBatch.DrawLine(sb, assets, pts[i], pts[i + 1],
                    new Color(trailColor.R, trailColor.G, trailColor.B, (int)(alpha * 255)), 1f);
            }
        }

        private static void DrawWebTrail(SpriteBatch sb, AssetManager assets,
            SilkWeaverSpider spider)
        {
            var pts = spider.WebTrailPoints;
            if (pts == null || pts.Count < 2) return;

            var webColor = new Color(200, 200, 255, 80);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float alpha = (i / (float)pts.Count) * 0.5f;
                GeometryBatch.DrawLine(sb, assets, pts[i], pts[i + 1],
                    new Color(webColor.R, webColor.G, webColor.B, (int)(alpha * 255)), 1f);
            }
        }
    }
}
