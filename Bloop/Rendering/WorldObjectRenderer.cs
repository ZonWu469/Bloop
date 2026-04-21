using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Generators;

namespace Bloop.Rendering
{
    /// <summary>
    /// Static renderer for world objects whose Draw() is delegated here.
    /// Each public Draw* method renders one object type using GeometryBatch
    /// and OrganicPrimitives. All animation uses AnimationClock.Time — no
    /// per-call GameTime needed. pixelPos is always the object center.
    ///
    /// The visual language is context-driven:
    ///   - Hazards  → scary / menacing (organic blobs, veins, warning pulses)
    ///   - Lights   → bioluminescent (photophores, drifting spores)
    ///   - Pickups  → precious / mysterious (ghostly, faceted, phase halos)
    /// </summary>
    public static class WorldObjectRenderer
    {
        // ══════════════════════════════════════════════════════════════════════
        // DISAPPEARING PLATFORM — Living Fungal Shelf
        // ══════════════════════════════════════════════════════════════════════

        private static readonly Color DPBase      = new Color(165, 105,  55);
        private static readonly Color DPDark      = new Color( 90,  55,  25);
        private static readonly Color DPHighlight = new Color(215, 160,  90);
        private static readonly Color DPGill      = new Color(120,  75,  35);
        private static readonly Color DPVein      = new Color(205,  50,  30);
        private static readonly Color DPThread    = new Color(140,  95,  50);
        private static readonly Color DPCrack     = new Color( 30,  10,   5);

        public static void DrawDisappearingPlatform(SpriteBatch sb, AssetManager assets,
            Vector2 pixelPos, bool isTriggered, float alpha, float countdownTimer, int tileHash)
        {
            if (alpha <= 0f) return;
            const int W = 64;
            const int H = 8;

            float t = AnimationClock.Time;
            // Breath accelerates when triggered; subtle otherwise.
            float breathFreq = isTriggered ? 4.5f : 1.2f;
            float breath = AnimationClock.Pulse(breathFreq, tileHash * 0.003f);

            float shakeX = isTriggered
                ? (float)Math.Sin(t * 20f) * (1f - alpha) * 3f : 0f;
            int cx = (int)(pixelPos.X + shakeX);
            int cy = (int)pixelPos.Y;

            Color baseColor = isTriggered
                ? Color.Lerp(DPBase, new Color(200, 65, 40), (1f - alpha) * 0.7f)
                : DPBase;

            // 1. Main shelf body — with breath-modulated vertical thickness
            int bh = H + (int)(breath * 2f);
            var body = new Rectangle(cx - W / 2, cy - bh / 2, W, bh);
            assets.DrawRect(sb, body, baseColor * alpha);

            // 2. Top highlight ridge — slight wave along the top
            for (int i = 0; i < W - 2; i += 2)
            {
                float wy = MathF.Sin(i * 0.25f + t * 1.3f + tileHash * 0.1f) * 0.6f;
                assets.DrawRect(sb,
                    new Rectangle(cx - W / 2 + 1 + i, cy - bh / 2 + (int)wy, 2, 2),
                    DPHighlight * alpha);
            }

            // 3. Bottom shadow
            assets.DrawRect(sb, new Rectangle(cx - W / 2 + 1, cy + bh / 2 - 2, W - 2, 2),
                DPDark * alpha);

            // 4. Gill ridges rising/falling with breath
            int gillCount = 4 + (tileHash & 3);
            for (int g = 0; g < gillCount; g++)
            {
                int gs = tileHash + g * 13;
                int gx = cx - W / 2 + 4 + (gs % (W - 8));
                float gPhase = (g * 0.6f) + breath * 0.4f;
                int gy = cy - bh / 2 - 1 - (int)(MathF.Sin(t * breathFreq + gPhase) * 1.5f + 1f);
                int gh = 2 + (gs & 1);
                assets.DrawRect(sb, new Rectangle(gx, gy, 1, gh), DPGill * alpha * 0.85f);
            }

            // 5. Hanging spore threads from the underside
            int threadCount = 3 + (tileHash % 3);
            for (int f = 0; f < threadCount; f++)
            {
                int fs = tileHash + f * 11;
                int fx = cx - W / 2 + 6 + (fs % (W - 12));
                float sway = AnimationClock.Sway(1.5f, 0.9f, fs * 0.13f);
                int baseY = cy + bh / 2;
                int tipY  = baseY + 4 + (fs & 3);
                OrganicPrimitives.DrawNoisyLine(sb, assets,
                    new Vector2(fx, baseY),
                    new Vector2(fx + sway, tipY),
                    DPThread * alpha * 0.7f, 1f,
                    amplitude: 0.8f, frequency: 1.2f, time: t, seed: fs, segments: 4);
                // Dot at tip (bead of spore)
                assets.DrawRect(sb, new Rectangle(fx + (int)sway - 0, tipY, 1, 1),
                    new Color(220, 180, 120) * alpha * 0.8f);
            }

            // 6. Triggered state — cracks + veins running across the surface
            if (isTriggered && alpha < 0.95f)
            {
                float dissolveT = 1f - alpha;

                // Red vein flush — grows outward from center over time
                float veinLen = dissolveT * (W * 0.5f);
                OrganicPrimitives.DrawNoisyLine(sb, assets,
                    new Vector2(cx, cy),
                    new Vector2(cx - veinLen, cy),
                    DPVein * alpha * 0.9f, 1f,
                    amplitude: 1.2f, frequency: 2.1f, time: t, seed: tileHash, segments: 6);
                OrganicPrimitives.DrawNoisyLine(sb, assets,
                    new Vector2(cx, cy),
                    new Vector2(cx + veinLen, cy),
                    DPVein * alpha * 0.9f, 1f,
                    amplitude: 1.2f, frequency: 2.1f, time: t, seed: tileHash + 31, segments: 6);

                // Cracks — short dark lines forming as the platform dies
                int cracks = (int)(dissolveT * 6f);
                for (int k = 0; k < cracks; k++)
                {
                    int ks = tileHash + k * 29;
                    float cxx = cx - W / 2 + 4 + (ks % (W - 8));
                    float cyy = cy - bh / 2 + 2 + (ks % Math.Max(1, bh - 4));
                    float ang = (ks & 0xFF) / 255f * MathHelper.TwoPi;
                    float len = 2f + (ks % 4);
                    GeometryBatch.DrawLine(sb, assets,
                        new Vector2(cxx, cyy),
                        new Vector2(cxx + MathF.Cos(ang) * len, cyy + MathF.Sin(ang) * len),
                        DPCrack * alpha * 0.9f, 1f);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // STUN DAMAGE OBJECT — Pulsing Barnacle Eye
        // ══════════════════════════════════════════════════════════════════════

        private static readonly Color SDFlesh   = new Color( 95,  18,  48);
        private static readonly Color SDFleshIn = new Color(140,  30,  65);
        private static readonly Color SDIris    = new Color(255,  95, 130);
        private static readonly Color SDIrisHot = new Color(255, 190, 210);
        private static readonly Color SDPupil   = new Color( 15,   4,   8);
        private static readonly Color SDVein    = new Color(200,  40,  55);
        private static readonly Color SDGlow    = new Color(255, 110, 150);
        private static readonly Color SDGhost   = new Color( 50,  15,  28);
        private static readonly Color SDSparkle = new Color(255, 200, 220);

        /// <summary>
        /// Draw the stun/damage hazard.
        /// proximity01: 0 when player is far, 1 when very close. Drives iris dilation.
        /// </summary>
        public static void DrawStunDamageObject(SpriteBatch sb, AssetManager assets,
            Vector2 pixelPos, bool isLit, float proximity01)
        {
            float t = AnimationClock.Time;
            int seed = (int)(pixelPos.X * 13 + pixelPos.Y * 7);

            if (!isLit)
            {
                // Unlit: faint flesh silhouette + rare blink.
                OrganicPrimitives.DrawBlob(sb, assets, pixelPos, 10f,
                    SDGhost * 0.18f, lobeCount: 3, time: t * 0.5f,
                    wobbleAmp: 0.08f, seed: seed);

                float gc = t % 3.4f;
                if (gc < 0.09f)
                {
                    assets.DrawRect(sb,
                        new Rectangle((int)pixelPos.X - 1, (int)pixelPos.Y - 1, 2, 2),
                        SDSparkle * (1f - gc / 0.09f));
                }
                return;
            }

            // Lit state — slow heartbeat; faster when proximity high.
            float heartFreq = 1.4f + proximity01 * 2.6f;
            float heart = AnimationClock.Pulse(heartFreq, seed * 0.001f);
            float clampProx = MathHelper.Clamp(proximity01, 0f, 1f);

            // 1. Outer glow halo (larger when heart pulses)
            float haloR = 14f + heart * 6f + clampProx * 3f;
            OrganicPrimitives.DrawGradientDisk(sb, assets, pixelPos,
                rIn: 4f, rOut: haloR,
                innerColor: SDGlow * (0.38f + heart * 0.3f),
                outerColor: SDGlow * 0f,
                rings: 5, segments: 10);

            // 2. Fleshy outer blob (breathing)
            OrganicPrimitives.DrawBlob(sb, assets, pixelPos, 11f,
                Color.Lerp(SDFlesh, SDFleshIn, heart * 0.5f),
                lobeCount: 3, time: t * 1.1f, wobbleAmp: 0.12f, seed: seed);
            // Inner flesh highlight
            OrganicPrimitives.DrawBlob(sb, assets, pixelPos, 8f,
                SDFleshIn, lobeCount: 4, time: -t * 0.9f, wobbleAmp: 0.08f, seed: seed + 17);

            // 3. Vein network pulsing with heartbeat
            OrganicPrimitives.DrawVeinNetwork(sb, assets, pixelPos,
                SDVein * (0.55f + heart * 0.45f),
                branchCount: 5, length: 11f,
                thickness: 1f, time: t, seed: seed);

            // 4. Iris (gradient disk that constricts when proximity high)
            float irisR = 4.5f - clampProx * 1.8f + heart * 0.5f;
            OrganicPrimitives.DrawGradientDisk(sb, assets, pixelPos,
                rIn: 1f, rOut: MathF.Max(1.5f, irisR),
                innerColor: Color.Lerp(SDIris, SDIrisHot, heart),
                outerColor: SDIris * 0.4f,
                rings: 4, segments: 10);

            // 5. Pupil — dilates slightly with heartbeat
            float pupilR = MathF.Max(1f, 1.2f + heart * 0.6f - clampProx * 0.3f);
            GeometryBatch.DrawCircleApprox(sb, assets, pixelPos, pupilR, SDPupil, 6);

            // 6. Bright specular dot
            assets.DrawRect(sb,
                new Rectangle((int)(pixelPos.X - pupilR * 0.4f), (int)(pixelPos.Y - pupilR * 0.8f), 1, 1),
                SDIrisHot * (0.7f + heart * 0.3f));

            // 7. Orbiting blood sparkles — faster/closer when proximity high
            int sparkCount = clampProx > 0.5f ? 4 : 3;
            for (int s = 0; s < sparkCount; s++)
            {
                float oa = t * (1.8f + clampProx * 2f) + s * MathHelper.TwoPi / sparkCount;
                float orbit = 13f + heart * 2f;
                Vector2 sp = pixelPos + new Vector2(MathF.Cos(oa) * orbit, MathF.Sin(oa) * orbit);
                assets.DrawRect(sb,
                    new Rectangle((int)sp.X - 1, (int)sp.Y - 1, 2, 2),
                    SDSparkle * AnimationClock.Pulse(3.2f, s * 1.1f));
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // GLOW VINE — Deep-Sea Photophore Vine
        // ══════════════════════════════════════════════════════════════════════

        private static readonly Color GVStemOff = new Color( 18,  55,  45);
        private static readonly Color GVStemOn  = new Color( 60, 200, 155);
        private static readonly Color GVStemHot = new Color(140, 250, 200);
        private static readonly Color GVLeafOff = new Color( 25,  70,  50);
        private static readonly Color GVLeafOn  = new Color( 80, 200, 110);
        private static readonly Color GVNode    = new Color(160, 255, 200);
        private static readonly Color GVNodeHot = new Color(240, 255, 220);
        private static readonly Color GVSpore   = new Color(170, 255, 200);
        private static readonly Color GVAura    = new Color( 50, 200, 160);

        /// <summary>
        /// Draw a glow vine.
        /// climbProgress01: if > 0, a photophore-chase ripples downward (player climbing).
        /// </summary>
        public static void DrawGlowVine(SpriteBatch sb, AssetManager assets,
            Vector2 pixelPos, int heightPx, bool isActivated, float illuminationProgress,
            float climbProgress01)
        {
            int halfH = heightPx / 2;
            float t = AnimationClock.Time;
            int seed = (int)(pixelPos.X * 11 + pixelPos.Y * 7);

            // 1. Aura (wider, softer for activated)
            if (isActivated)
            {
                OrganicPrimitives.DrawGradientDisk(sb, assets,
                    pixelPos + new Vector2(0, 0),
                    rIn: 6f, rOut: 28f,
                    innerColor: GVAura * 0.22f,
                    outerColor: GVAura * 0f,
                    rings: 5, segments: 12);
            }

            // 2. Stem as 3 stacked cubic beziers swaying via ValueNoise1D
            Color stemColor = isActivated
                ? Color.Lerp(GVStemOn, GVStemHot, AnimationClock.Pulse(0.8f) * 0.3f)
                : Color.Lerp(GVStemOff, GVStemOn, illuminationProgress);
            float stemThick = isActivated ? 2.5f : 2f;
            float swayAmp   = isActivated ? 4f : 2f;

            int stemSections = 3;
            float sectionH = heightPx / (float)stemSections;
            Vector2 prevBottom = new Vector2(pixelPos.X, pixelPos.Y + halfH);
            for (int s = 0; s < stemSections; s++)
            {
                float y0 = pixelPos.Y + halfH - s * sectionH;
                float y1 = y0 - sectionH;
                // Sway positions follow ValueNoise for organic motion
                float n0 = NoiseHelpers.ValueNoise1DSigned(t * 0.6f + s * 0.7f, seed);
                float n1 = NoiseHelpers.ValueNoise1DSigned(t * 0.6f + s * 0.7f + 0.5f, seed + 97);
                Vector2 p0 = new Vector2(pixelPos.X + n0 * swayAmp, y0);
                Vector2 p3 = new Vector2(pixelPos.X + n1 * swayAmp, y1);
                Vector2 c1 = p0 + new Vector2(NoiseHelpers.HashSigned(seed + s * 31) * swayAmp * 2f, -sectionH / 3f);
                Vector2 c2 = p3 + new Vector2(NoiseHelpers.HashSigned(seed + s * 53) * swayAmp * 2f,  sectionH / 3f);
                OrganicPrimitives.DrawBezier(sb, assets, p0, c1, c2, p3,
                    stemColor, stemThick, segments: 10);
                // Save the top of this section for placement reference
                if (s == stemSections - 1) prevBottom = p3;
                else prevBottom = p3;
            }

            // 3. Leaf fronds — jittered noisy lines
            int leafCount = 4 + heightPx / 32;
            for (int l = 0; l < leafCount; l++)
            {
                float lf = (l + 0.5f) / leafCount;
                float ly = pixelPos.Y + halfH - lf * heightPx;
                float nx = NoiseHelpers.ValueNoise1DSigned(t * 0.6f + lf * 2.1f, seed) * swayAmp;
                float attachX = pixelPos.X + nx;
                int side = (l & 1) == 0 ? 1 : -1;
                Color lc = isActivated ? GVLeafOn : Color.Lerp(GVLeafOff, GVLeafOn, illuminationProgress);
                Vector2 a = new Vector2(attachX, ly);
                Vector2 b = new Vector2(attachX + side * (6 + (l % 3)), ly + side * 2);
                OrganicPrimitives.DrawNoisyLine(sb, assets, a, b, lc, 2f,
                    amplitude: 1.2f, frequency: 1.8f + l * 0.3f, time: t, seed: seed + l * 13, segments: 4);
            }

            // 4. Photophore nodes (gradient disks pulsing)
            int nc = Math.Max(3, 3 + heightPx / 32);
            float climbWave = climbProgress01; // 0..1
            for (int n = 0; n < nc; n++)
            {
                float nf = (n + 0.5f) / nc;
                float ny = pixelPos.Y + halfH - nf * heightPx;
                float nx = pixelPos.X + NoiseHelpers.ValueNoise1DSigned(t * 0.6f + nf * 2.1f, seed) * swayAmp;
                float np = AnimationClock.Pulse(1.8f, n * 0.9f);

                // Activated: full glow. Else partially lit based on illuminationProgress.
                float litFrac = isActivated ? 1f : MathHelper.Clamp(illuminationProgress * (1f + 0.3f * n) - n * 0.1f, 0f, 1f);
                if (litFrac <= 0.05f) continue;

                // Climb-chase flash: a wave of brightness travels through nodes when climbing.
                float chase = 0f;
                if (climbWave > 0f)
                {
                    float expected = climbWave; // 0 top, 1 bottom? Actually nf is 0 at bottom
                    float d = MathF.Abs(nf - expected);
                    chase = MathF.Max(0f, 1f - d * 3f);
                }

                float r = 2.5f + np * 1.2f + chase * 2f;
                Color nodeCol = Color.Lerp(GVNode, GVNodeHot, 0.35f * np + chase);
                OrganicPrimitives.DrawGradientDisk(sb, assets, new Vector2(nx, ny),
                    rIn: 0.5f, rOut: r,
                    innerColor: Color.Lerp(new Color(255, 255, 255), nodeCol, 0.3f) * litFrac,
                    outerColor: nodeCol * 0f,
                    rings: 4, segments: 10);
                // Bright core dot
                assets.DrawRect(sb, new Rectangle((int)nx - 1, (int)ny - 1, 2, 2),
                    nodeCol * (0.7f + np * 0.3f) * litFrac);
            }

            // 5. Drifting spore cloud (activated only)
            if (isActivated)
            {
                for (int sp = 0; sp < 4; sp++)
                {
                    float sph = sp * 0.65f;
                    float spT = AnimationClock.Loop(3.2f, sph);
                    float spY = pixelPos.Y + halfH - spT * (heightPx + 24f);
                    float spX = pixelPos.X + AnimationClock.Sway(7f, 0.9f, sph + spT);
                    float spa = spT < 0.8f ? spT / 0.8f : (1f - (spT - 0.8f) / 0.2f);
                    if (spa > 0.05f)
                    {
                        assets.DrawRect(sb, new Rectangle((int)spX - 1, (int)spY - 1, 2, 2),
                            GVSpore * spa * 0.9f);
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // ROOT CLUMP — Sinewy Writhing Tendrils
        // ══════════════════════════════════════════════════════════════════════

        private static readonly Color RCTorso = new Color( 78,  58,  32);
        private static readonly Color RCDark  = new Color( 40,  28,  14);
        private static readonly Color RCHi    = new Color(118,  90,  55);
        private static readonly Color RCRet   = new Color(110,  82,  45);
        private static readonly Color RCNode  = new Color(150, 120,  80);
        private static readonly Color RCWarn  = new Color(230,  60,  35);
        private static readonly Color RCWarnHot = new Color(255, 180, 100);

        public static void DrawRootClump(SpriteBatch sb, AssetManager assets,
            Vector2 pixelPos, int heightPx, bool isRetracting, float retractProgress,
            float idleWarningFraction, int tileHash)
        {
            if (retractProgress >= 1f) return;

            float t = AnimationClock.Time;
            int halfH = heightPx / 2;
            int dw    = isRetracting ? Math.Max(2, (int)(30 * (1f - retractProgress))) : 30;

            // Warning shake — increasing with timer
            float shakeAmp = idleWarningFraction * 2f + (isRetracting ? 2f : 0f);
            float shakeX   = MathF.Sin(t * (10f + idleWarningFraction * 30f) + tileHash) * shakeAmp;

            int cx = (int)(pixelPos.X + shakeX);
            int cy = (int)pixelPos.Y;

            Color fill = isRetracting ? RCRet : RCTorso;

            // 1. Torso — stacked blobs (tapering) forming a sinewy column
            int blobsY = Math.Max(2, heightPx / 14);
            for (int i = 0; i < blobsY; i++)
            {
                float tt = (i + 0.5f) / blobsY;
                float by = cy - halfH + tt * heightPx;
                float bw = dw * 0.5f * (1f - MathF.Abs(tt - 0.5f) * 0.3f);
                int seed = tileHash + i * 29;
                OrganicPrimitives.DrawBlob(sb, assets,
                    new Vector2(cx, by), bw,
                    fill * (1f - 0.2f * (i & 1)),
                    lobeCount: 3, time: t * 0.9f, wobbleAmp: 0.12f, seed: seed);
            }

            // 2. Bark vein lines on torso (deterministic)
            if (!isRetracting)
            {
                for (int l = 0; l < 2 + (tileHash % 3); l++)
                {
                    int ls = tileHash + l * 41;
                    float lx = cx + NoiseHelpers.HashSigned(ls) * dw * 0.35f;
                    Vector2 a = new Vector2(lx, cy - halfH + 3);
                    Vector2 b = new Vector2(lx + NoiseHelpers.HashSigned(ls + 7) * 3f, cy + halfH - 3);
                    OrganicPrimitives.DrawNoisyLine(sb, assets, a, b, RCDark * 0.8f, 1.5f,
                        amplitude: 1.2f, frequency: 0.9f, time: t * 0.4f, seed: ls, segments: 7);
                }
            }

            // 3. Writhing tendrils — bezier curves with noise-driven control points
            if (!isRetracting)
            {
                int tendrils = 4 + (tileHash % 3);
                for (int tr = 0; tr < tendrils; tr++)
                {
                    int ts = tileHash + tr * 23;
                    float ty2 = cy - halfH + 10 + (ts % Math.Max(1, heightPx - 20));
                    float dx  = (tr & 1) == 0 ? 1f : -1f;
                    Vector2 p0 = new Vector2(cx + dx * dw / 2f, ty2);
                    float baseLen = 10f + (ts % 8);
                    float n1 = NoiseHelpers.ValueNoise1DSigned(t * 1.1f + tr * 0.7f, ts);
                    float n2 = NoiseHelpers.ValueNoise1DSigned(t * 1.1f + tr * 0.7f + 0.5f, ts + 37);
                    Vector2 p1 = p0 + new Vector2(dx * baseLen * 0.4f, n1 * 5f);
                    Vector2 p3 = p0 + new Vector2(dx * baseLen,        n2 * 8f);
                    Vector2 p2 = p1 + new Vector2(dx * baseLen * 0.4f, n2 * 4f);
                    OrganicPrimitives.DrawBezier(sb, assets, p0, p1, p2, p3, RCHi, 2f, segments: 9);

                    // Grip-node bump along the tendril
                    Vector2 node = p0 + (p3 - p0) * 0.65f;
                    OrganicPrimitives.DrawGradientDisk(sb, assets, node,
                        rIn: 0.5f, rOut: 2.2f + AnimationClock.Pulse(2f, tr * 0.4f),
                        innerColor: RCNode, outerColor: RCNode * 0f, rings: 3, segments: 8);
                }
            }

            // 4. Warning veins pulsing red when idle timer is high
            if (idleWarningFraction > 0.25f && !isRetracting)
            {
                float wa = (idleWarningFraction - 0.25f) / 0.75f;
                float vpulse = AnimationClock.Pulse(3f + wa * 6f);
                Color vc = Color.Lerp(RCWarn, RCWarnHot, vpulse) * (0.5f + wa * 0.5f);
                for (int v = 0; v < (int)(wa * 4) + 1; v++)
                {
                    int vs = tileHash + v * 19;
                    float vy = cy - halfH + 6 + (vs % Math.Max(1, heightPx - 12));
                    OrganicPrimitives.DrawNoisyLine(sb, assets,
                        new Vector2(cx - dw / 2f + 2, vy),
                        new Vector2(cx + dw / 2f - 2, vy + (vs % 4) - 1),
                        vc, 1f,
                        amplitude: 1.5f, frequency: 2f + wa * 3f, time: t, seed: vs, segments: 6);
                }
            }

            // 5. Retraction dust — rising puffs as it curls into the wall
            if (isRetracting)
            {
                for (int d = 0; d < 4; d++)
                {
                    int ds = tileHash + d * 23;
                    float dxd = cx + NoiseHelpers.HashSigned(ds) * dw * 0.5f;
                    float ft  = (retractProgress + d * 0.25f) % 1f;
                    float dyd = cy + halfH + ft * 18f;
                    float da  = (1f - ft) * retractProgress;
                    if (da > 0.05f)
                        assets.DrawRect(sb, new Rectangle((int)dxd - 1, (int)dyd - 1, 2, 2),
                            RCNode * da);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // VENT FLOWER — Anemone Bloom
        // ══════════════════════════════════════════════════════════════════════

        private static readonly Color VFStem     = new Color( 36, 162, 112);
        private static readonly Color VFLeaf     = new Color( 55, 150,  90);
        private static readonly Color VFPetalOut = new Color( 65, 170, 120);
        private static readonly Color VFPetalIn  = new Color(130, 240, 180);
        private static readonly Color VFCtr      = new Color(205, 255, 225);
        private static readonly Color VFAura     = new Color( 60, 200, 150);
        private static readonly Color VFCool     = new Color( 30,  90,  60);
        private static readonly Color VFProg     = new Color( 80, 255, 180);

        public static void DrawVentFlower(SpriteBatch sb, AssetManager assets,
            Vector2 pixelPos, bool onCooldown, float standingProgress, bool playerInZone)
        {
            float t = AnimationClock.Time;
            // Slow breath — petals open and close
            float breath = AnimationClock.Pulse(0.45f);
            float breathAmp = onCooldown ? 0.15f : (playerInZone ? 1f : 0.55f);
            float open = 0.4f + breath * breathAmp;  // 0..1.4

            float dim = onCooldown ? 0.35f : 1f;

            int seed = (int)(pixelPos.X * 11 + pixelPos.Y * 13);

            // 1. Aura (softer; grows when player inside)
            float auraR = 20f + (playerInZone ? 12f * breath : 4f * breath);
            OrganicPrimitives.DrawGradientDisk(sb, assets, pixelPos,
                rIn: 6f, rOut: auraR,
                innerColor: VFAura * (onCooldown ? 0.04f : 0.18f + breath * 0.08f),
                outerColor: VFAura * 0f,
                rings: 5, segments: 12);

            // 2. Stem (bezier with slow sway)
            float stemSway = AnimationClock.Sway(2f, 0.6f);
            Vector2 stemBase = new Vector2(pixelPos.X, pixelPos.Y + 22);
            Vector2 stemTop  = new Vector2(pixelPos.X + stemSway * 0.3f, pixelPos.Y - 2);
            Vector2 stemCtrl = new Vector2(pixelPos.X + stemSway, pixelPos.Y + 10);
            OrganicPrimitives.DrawBezierQuad(sb, assets, stemBase, stemCtrl, stemTop,
                (onCooldown ? VFCool : VFStem) * dim, 3f, 10);

            // 3. Stem leaves
            Color lc = (onCooldown ? VFCool : VFLeaf) * dim;
            Vector2 leafPivot1 = new Vector2(pixelPos.X + stemSway * 0.5f + 1, pixelPos.Y + 8);
            Vector2 leafPivot2 = new Vector2(pixelPos.X + stemSway * 0.7f - 1, pixelPos.Y + 16);
            OrganicPrimitives.DrawBezierQuad(sb, assets,
                leafPivot1,
                leafPivot1 + new Vector2(5, -1),
                leafPivot1 + new Vector2(9, 2),
                lc, 2.5f, 6);
            OrganicPrimitives.DrawBezierQuad(sb, assets,
                leafPivot2,
                leafPivot2 + new Vector2(-5, -1),
                leafPivot2 + new Vector2(-9, 2),
                lc, 2.5f, 6);

            // 4. Petals — 6 blob petals breathing open
            Vector2 flowerCtr = stemTop + new Vector2(0, -2);
            int petalCount = 6;
            for (int i = 0; i < petalCount; i++)
            {
                float a = (i / (float)petalCount) * MathHelper.TwoPi + t * 0.15f;
                float gap = 5f + open * 6f;
                Vector2 pc = flowerCtr + new Vector2(MathF.Cos(a), MathF.Sin(a)) * gap;
                Color pcol = Color.Lerp(VFPetalOut, VFPetalIn, open * 0.6f) * dim;
                float petalR = 4f + open * 1.6f;
                OrganicPrimitives.DrawBlob(sb, assets, pc, petalR,
                    pcol, lobeCount: 2, time: t * 0.8f + a,
                    wobbleAmp: 0.18f, seed: seed + i * 11);
            }

            // 5. Center — gradient disk (dilates)
            float centerR = 4.5f + open * 1.2f;
            OrganicPrimitives.DrawGradientDisk(sb, assets, flowerCtr,
                rIn: 0.5f, rOut: centerR,
                innerColor: (onCooldown ? VFCool : VFCtr) * dim,
                outerColor: (onCooldown ? VFCool : VFPetalIn) * dim * 0.3f,
                rings: 4, segments: 10);

            // 6. Heat shimmer — handled by emitter on the object; draw small rising streaks here too
            if (!onCooldown)
            {
                int streams = playerInZone ? 3 : 2;
                for (int s = 0; s < streams; s++)
                {
                    float sph  = s * 0.5f;
                    float stT  = AnimationClock.Loop(1.4f, sph);
                    float stX  = flowerCtr.X + (s - (streams - 1) * 0.5f) * 5f
                                 + MathF.Sin(stT * MathF.PI * 2f + s) * 1.5f;
                    float stYb = flowerCtr.Y - 4f;
                    float stYt = stYb - 22f;
                    float yOff = stT * 22f;
                    float sa   = (stT < 0.85f ? 1f : (1f - (stT - 0.85f) / 0.15f)) * 0.35f;
                    GeometryBatch.DrawLine(sb, assets,
                        new Vector2(stX, stYb - yOff),
                        new Vector2(stX, stYt - yOff + 3f),
                        VFPetalIn * sa, 1f);
                }
            }

            // 7. Progress ring — segmented around the center
            if (playerInZone && !onCooldown && standingProgress > 0f)
            {
                int segs = 16;
                int lit  = (int)(standingProgress * segs);
                float rR = 10f;
                for (int i = 0; i < segs; i++)
                {
                    float a0 = (i / (float)segs) * MathHelper.TwoPi - MathHelper.PiOver2;
                    float a1 = ((i + 0.65f) / segs) * MathHelper.TwoPi - MathHelper.PiOver2;
                    Vector2 p0 = flowerCtr + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * rR;
                    Vector2 p1 = flowerCtr + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * rR;
                    Color cc = (i < lit)
                        ? VFProg * (0.75f + AnimationClock.Pulse(4f, i * 0.2f) * 0.25f)
                        : new Color(20, 40, 30) * 0.7f;
                    GeometryBatch.DrawLine(sb, assets, p0, p1, cc, 2f);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // CAVE LICHEN — Fungal Rosette
        // ══════════════════════════════════════════════════════════════════════

        private static readonly Color CLNormal  = new Color(155, 195,  38);
        private static readonly Color CLPoison  = new Color( 95, 175,  18);
        private static readonly Color CLDark    = new Color( 55,  90,  15);
        private static readonly Color CLGlow    = new Color(210, 250,  90);

        public static void DrawCaveLichen(SpriteBatch sb, AssetManager assets,
            Vector2 pixelPos, bool isPoisonous, ItemRarity rarity = ItemRarity.Common)
        {
            float t = AnimationClock.Time;
            int seed = (int)(pixelPos.X * 3 + pixelPos.Y * 7);

            float pulse = AnimationClock.Pulse(rarity == ItemRarity.Rare ? 3f : 2f);
            float breath = AnimationClock.Sway(rarity == ItemRarity.Rare ? 2f : 1f, 1.4f);

            Color fill = isPoisonous ? CLPoison : CLNormal;
            if (rarity == ItemRarity.Rare)
                fill = Color.Lerp(fill, new Color(230, 255, 100), 0.4f);
            else if (rarity == ItemRarity.Uncommon)
                fill = Color.Lerp(fill, new Color(185, 235,  65), 0.2f);

            // 1. Halo (uncommon/rare)
            if (rarity != ItemRarity.Common)
            {
                float haloR = rarity == ItemRarity.Rare ? 18f : 12f;
                OrganicPrimitives.DrawGradientDisk(sb, assets, pixelPos,
                    rIn: 4f, rOut: haloR + breath,
                    innerColor: CLGlow * (rarity == ItemRarity.Rare ? 0.35f : 0.22f),
                    outerColor: CLGlow * 0f,
                    rings: 4, segments: 10);
            }

            // 2. Base concentric rosette blobs — breathing together
            float baseR = 7.5f + breath * 0.8f;
            OrganicPrimitives.DrawBlob(sb, assets, pixelPos, baseR + 1.5f,
                CLDark * 0.8f, lobeCount: 5, time: t * 0.6f, wobbleAmp: 0.14f, seed: seed);
            OrganicPrimitives.DrawBlob(sb, assets, pixelPos, baseR,
                fill, lobeCount: 5, time: t * 0.9f, wobbleAmp: 0.1f, seed: seed + 11);
            OrganicPrimitives.DrawBlob(sb, assets, pixelPos, baseR * 0.65f,
                Color.Lerp(fill, CLGlow, 0.25f + pulse * 0.25f),
                lobeCount: 4, time: t * 1.2f, wobbleAmp: 0.08f, seed: seed + 23);

            // 3. Gill ribs — small radial bezier ribs with tiny twitch
            int ribs = 8;
            for (int i = 0; i < ribs; i++)
            {
                float ang = (i / (float)ribs) * MathHelper.TwoPi
                          + NoiseHelpers.ValueNoise1DSigned(t * 0.5f + i * 0.3f, seed + i) * 0.08f;
                Vector2 inner = pixelPos + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * (baseR * 0.4f);
                Vector2 outer = pixelPos + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * (baseR * 1.05f);
                GeometryBatch.DrawLine(sb, assets, inner, outer, CLDark * 0.9f, 1f);
            }

            // 4. Central bloom (gradient disk)
            float coreR = 2.5f + pulse * 1f;
            OrganicPrimitives.DrawGradientDisk(sb, assets, pixelPos,
                rIn: 0.5f, rOut: coreR,
                innerColor: CLGlow * (0.8f + pulse * 0.2f),
                outerColor: fill,
                rings: 3, segments: 8);

            // 5. Rare — 3 orbital sparkles
            if (rarity == ItemRarity.Rare)
            {
                for (int s = 0; s < 3; s++)
                {
                    float angle = t * 2.5f + s * MathHelper.TwoPi / 3f;
                    float orbit = 12f + pulse * 3f;
                    Vector2 sp = pixelPos + new Vector2(MathF.Cos(angle) * orbit, MathF.Sin(angle) * orbit * 0.5f);
                    float sa = 0.5f + AnimationClock.Pulse(2f, s * 0.8f) * 0.5f;
                    assets.DrawRect(sb, new Rectangle((int)sp.X - 1, (int)sp.Y - 1, 2, 2),
                        CLGlow * sa);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // BLIND FISH — Translucent Ghost Fish
        // ══════════════════════════════════════════════════════════════════════

        private static readonly Color BFBody    = new Color(188, 208, 228);
        private static readonly Color BFPoison  = new Color(150, 185, 210);
        private static readonly Color BFSkeleton = new Color( 78,  98, 138);
        private static readonly Color BFEye     = new Color(228, 240, 255);
        private static readonly Color BFEyeDead = new Color( 20,  30,  50);
        private static readonly Color BFFin     = new Color(168, 192, 218);

        /// <summary>
        /// Draw a blind fish. proximity01: tail flicks sharper when player close.
        /// </summary>
        public static void DrawBlindFish(SpriteBatch sb, AssetManager assets,
            Vector2 pixelPos, bool isPoisonous, ItemRarity rarity, float proximity01)
        {
            float t = AnimationClock.Time;
            int seed = (int)(pixelPos.X * 7 + pixelPos.Y * 11);

            float pulse = AnimationClock.Pulse(rarity == ItemRarity.Rare ? 3.5f : 2f);

            float bobAmp = rarity == ItemRarity.Rare ? 3f : 2f;
            float bobY   = AnimationClock.Sway(bobAmp, 1.5f);
            float driftX = AnimationClock.Sway(1f, 0.8f, 0.5f);
            float tailFreq  = 2.5f + proximity01 * 5f;
            float tailAmp   = 0.25f + proximity01 * 0.45f;
            float tailAngle = AnimationClock.Sway(tailAmp, tailFreq);

            int cx = (int)(pixelPos.X + driftX);
            int cy = (int)(pixelPos.Y + bobY);
            Vector2 ctr = new Vector2(cx, cy);

            const int W = 20;
            const int H = 10;

            Color body = isPoisonous ? BFPoison : BFBody;
            if (rarity == ItemRarity.Rare)
                body = Color.Lerp(body, new Color(185, 220, 255), 0.35f);
            else if (rarity == ItemRarity.Uncommon)
                body = Color.Lerp(body, new Color(165, 205, 240), 0.2f);

            // Iridescent color cycling (rare)
            if (rarity == ItemRarity.Rare)
            {
                float hue = (MathF.Sin(t * 0.8f) + 1f) * 0.5f;
                body = Color.Lerp(body, Color.Lerp(new Color(200, 180, 255), new Color(180, 240, 220), hue), 0.25f);
            }

            // 1. Halo (Uncommon / Rare)
            if (rarity != ItemRarity.Common)
            {
                float haloR = rarity == ItemRarity.Rare ? 16f : 10f;
                OrganicPrimitives.DrawGradientDisk(sb, assets, ctr,
                    rIn: 6f, rOut: haloR,
                    innerColor: new Color(140, 200, 255) * (rarity == ItemRarity.Rare ? 0.28f : 0.15f),
                    outerColor: new Color(140, 200, 255) * 0f,
                    rings: 4, segments: 10);
            }

            // 2. Translucent body (blob teardrop) — slightly transparent to show skeleton
            OrganicPrimitives.DrawBlob(sb, assets, ctr, W * 0.46f,
                body * 0.78f, lobeCount: 2, time: t * 0.9f, wobbleAmp: 0.06f, seed: seed);
            // A tapered back fill for teardrop silhouette
            OrganicPrimitives.DrawBlob(sb, assets,
                ctr + new Vector2(W * 0.18f, 0), W * 0.28f,
                body * 0.55f, lobeCount: 2, time: t * 1.1f, wobbleAmp: 0.06f, seed: seed + 7);

            // 3. Visible skeleton — bezier spine + short rib lines
            Vector2 spineStart = ctr + new Vector2(-W * 0.42f, 0);
            Vector2 spineEnd   = ctr + new Vector2( W * 0.40f, 0);
            Vector2 spineCtrl  = ctr + new Vector2(0, MathF.Sin(t * 2f) * 0.8f);
            OrganicPrimitives.DrawBezierQuad(sb, assets, spineStart, spineCtrl, spineEnd,
                BFSkeleton * 0.8f, 1f, 8);
            int ribCount = 5;
            for (int r = 0; r < ribCount; r++)
            {
                float rt = (r + 0.5f) / ribCount;
                Vector2 rib = Vector2.Lerp(spineStart, spineEnd, rt);
                GeometryBatch.DrawLine(sb, assets,
                    rib + new Vector2(0, -H * 0.35f),
                    rib + new Vector2(0,  H * 0.35f),
                    BFSkeleton * 0.55f, 1f);
            }

            // 4. Tail fin (flicking rotated rect)
            GeometryBatch.DrawRotatedRect(sb, assets,
                ctr + new Vector2(W / 2f + 2, 0),
                6f, H + 4, tailAngle, BFFin);
            // 4b. Jittering fluke edge (noisy line)
            Vector2 tailRoot = ctr + new Vector2(W / 2f - 1, 0);
            Vector2 tailTip  = ctr + new Vector2(W / 2f + 7 + MathF.Cos(tailAngle) * 2f,
                                                  MathF.Sin(tailAngle) * (H + 4));
            OrganicPrimitives.DrawNoisyLine(sb, assets, tailRoot, tailTip,
                BFFin * 0.9f, 1.5f,
                amplitude: 1.2f, frequency: tailFreq, time: t, seed: seed, segments: 5);

            // 5. Dorsal fin (small triangle)
            GeometryBatch.DrawTriangleSolid(sb, assets,
                ctr + new Vector2(-2, -H / 2f),
                ctr + new Vector2( 4, -H / 2f),
                ctr + new Vector2( 1, -H / 2f - 5),
                BFFin * 0.8f);

            // 6. Dead white eye (unseeing)
            assets.DrawRect(sb,
                new Rectangle(cx - W / 2 + 3, cy - 1, 3, 3),
                BFEye * 0.85f);
            assets.DrawRect(sb,
                new Rectangle(cx - W / 2 + 4, cy, 1, 1),
                BFEyeDead);

            // 7. Rare: trailing bubbles
            if (rarity == ItemRarity.Rare)
            {
                for (int b = 0; b < 3; b++)
                {
                    float bt = AnimationClock.Loop(1.8f, b * 0.6f);
                    float bx = cx + W / 2 - bt * 20f;
                    float by = cy + (b - 1) * 3f + MathF.Sin(bt * MathF.PI * 2f + b) * 1.2f;
                    float ba = bt < 0.7f ? bt / 0.7f : (1f - (bt - 0.7f) / 0.3f);
                    if (ba > 0.05f)
                        assets.DrawRect(sb, new Rectangle((int)bx - 1, (int)by - 1, 2, 2),
                            new Color(140, 200, 255) * (ba * (0.5f + pulse * 0.3f)));
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // CLIMBABLE SURFACE — Vine-Wrapped Stone
        // ══════════════════════════════════════════════════════════════════════

        private static readonly Color CSStone  = new Color( 68,  68,  72);
        private static readonly Color CSStoneD = new Color( 36,  36,  40);
        private static readonly Color CSStoneH = new Color( 92,  92,  98);
        private static readonly Color CSVine   = new Color( 60, 140,  70);
        private static readonly Color CSVineH  = new Color( 95, 185,  92);
        private static readonly Color CSScar   = new Color( 28,  50,  25);
        private static readonly Color CSMoist  = new Color( 80, 140, 180);

        public static void DrawClimbableSurface(SpriteBatch sb, AssetManager assets,
            Vector2 pixelPos, int heightPx, int tileHash)
        {
            float t = AnimationClock.Time;
            int halfH = heightPx / 2;
            int cx = (int)pixelPos.X;
            int cy = (int)pixelPos.Y;

            // 1. Stone base — stacked tapered quads
            var baseRect = new Rectangle(cx - 15, cy - halfH, 30, heightPx);
            assets.DrawRect(sb, baseRect, CSStoneD);
            assets.DrawRect(sb,
                new Rectangle(cx - 13, cy - halfH + 1, 26, heightPx - 2),
                CSStone);

            // 2. Stone highlight cracks (deterministic)
            for (int i = 0; i < 3; i++)
            {
                int s = tileHash + i * 17;
                float y0 = cy - halfH + 3 + (s % Math.Max(1, heightPx - 6));
                float y1 = y0 + 3 + (s % 5);
                Vector2 a = new Vector2(cx - 10 + (s % 16), y0);
                Vector2 b = new Vector2(a.X + (s & 7) - 3, y1);
                OrganicPrimitives.DrawNoisyLine(sb, assets, a, b,
                    CSStoneH * 0.5f, 1f,
                    amplitude: 0.6f, frequency: 1f, time: t * 0.2f, seed: s, segments: 4);
            }

            // 3. Wrapping vines — 2 beziers criss-crossing the face with sway
            int vineCount = 2 + (tileHash % 2);
            for (int v = 0; v < vineCount; v++)
            {
                int vs = tileHash + v * 41;
                float phase = v * 0.8f;
                float sway1 = AnimationClock.Sway(1.2f, 0.9f, phase);
                float sway2 = AnimationClock.Sway(1.2f, 0.9f, phase + MathF.PI);
                Vector2 vStart = new Vector2(cx - 14 + sway1, cy - halfH + 2 + v * 6);
                Vector2 vEnd   = new Vector2(cx + 14 + sway2, cy + halfH - 4 - v * 4);
                Vector2 vCtrl1 = new Vector2(cx + ((vs & 7) - 3), vStart.Y + heightPx * 0.33f);
                Vector2 vCtrl2 = new Vector2(cx + ((vs & 15) - 7), vStart.Y + heightPx * 0.66f);
                Color vc = (v & 1) == 0 ? CSVine : CSVineH;
                OrganicPrimitives.DrawBezier(sb, assets, vStart, vCtrl1, vCtrl2, vEnd, vc, 2f, 12);
            }

            // 4. Grip-notch scars (dark blob dents)
            for (int g = 0; g < 3; g++)
            {
                int gs = tileHash + g * 29;
                float gy = cy - halfH + 5 + (gs % Math.Max(1, heightPx - 10));
                float gx = cx - 8 + ((gs * 3) & 15);
                OrganicPrimitives.DrawBlob(sb, assets, new Vector2(gx, gy), 2.2f,
                    CSScar, lobeCount: 3, time: 0f, wobbleAmp: 0.15f, seed: gs);
            }

            // 5. Moisture drip (occasional)
            if ((tileHash & 3) == 0)
            {
                float dropPhase = (tileHash % 5) * 0.5f;
                float dropT = AnimationClock.Loop(2f, dropPhase);
                int dropX = cx - 8 + (tileHash & 15);
                int dropY = cy - halfH + (int)(dropT * heightPx);
                float dropAlpha = dropT < 0.9f ? 0.7f : (1f - (dropT - 0.9f) / 0.1f) * 0.7f;
                if (dropAlpha > 0.05f)
                    assets.DrawRect(sb, new Rectangle(dropX, dropY, 2, 3), CSMoist * dropAlpha);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // FLARE OBJECT — Thrown Light Flare
        // ══════════════════════════════════════════════════════════════════════

        private static readonly Color FlareCasing  = new Color(210, 130,  50);
        private static readonly Color FlareCap     = new Color(255, 230,  80);
        private static readonly Color FlareSpark   = new Color(255, 220,  80);
        private static readonly Color FlareGlow    = new Color(255, 180,  60,  60);
        private static readonly Color FlareSmoke   = new Color(180, 140, 100,  40);
        private static readonly Color FlareDying   = new Color(255, 100,  50);

        /// <summary>
        /// Draw an in-world flare object. Call from world-space SpriteBatch block.
        /// </summary>
        public static void DrawFlare(SpriteBatch sb, AssetManager assets,
            Vector2 pixelPos, float remainingLife, bool hasLanded)
        {
            float alpha = remainingLife < 5f ? remainingLife / 5f : 1f;
            if (alpha <= 0f) return;

            float pulse = AnimationClock.Pulse(2.5f);

            // Glow halo
            GeometryBatch.DrawCircleApprox(sb, assets, pixelPos,
                (int)(10f + pulse * 3f), FlareGlow * alpha, 8);

            // Casing body
            assets.DrawRect(sb,
                new Rectangle((int)pixelPos.X - 3, (int)pixelPos.Y - 1, 6, 3),
                FlareCasing * alpha);

            // Bright burning cap (right end)
            assets.DrawRect(sb,
                new Rectangle((int)pixelPos.X + 3, (int)pixelPos.Y - 1, 2, 2),
                FlareCap * alpha);

            // Rotating sparks
            for (int i = 0; i < 4; i++)
            {
                float angle  = AnimationClock.Time * 6f + i * (MathF.PI / 2f);
                float orbitR = 4f + AnimationClock.Pulse(3f, i * 0.5f) * 2f;
                var sparkPos = pixelPos + new Vector2(
                    MathF.Cos(angle) * orbitR,
                    MathF.Sin(angle) * orbitR - 3f);
                assets.DrawRect(sb,
                    new Rectangle((int)sparkPos.X - 1, (int)sparkPos.Y - 1, 2, 2),
                    FlareSpark * alpha);
            }

            // Smoke wisps when landed
            if (hasLanded)
            {
                for (int i = 0; i < 2; i++)
                {
                    float phase  = i * 0.4f;
                    float loop   = AnimationClock.Loop(1.2f, phase);
                    float wispY  = loop * -10f;
                    float wispX  = AnimationClock.Sway(2f, 0.7f, phase);
                    var wispPos  = pixelPos + new Vector2(wispX, wispY - 2f);
                    float wAlpha = (1f - loop) * alpha * 0.6f;
                    if (wAlpha > 0.02f)
                        assets.DrawRect(sb,
                            new Rectangle((int)wispPos.X - 1, (int)wispPos.Y - 1, 2, 2),
                            FlareSmoke * wAlpha);
                }
            }

            // Dying warning pulse in final 5 seconds
            if (remainingLife < 5f)
            {
                float warnPulse = AnimationClock.Pulse(4f);
                GeometryBatch.DrawCircleOutline(sb, assets, pixelPos, 10f,
                    FlareDying * (warnPulse * alpha), 8);
            }
        }

        /// <summary>
        /// Draw the flare throw trajectory arc in world space.
        /// </summary>
        public static void DrawFlareTrajectory(SpriteBatch sb, AssetManager assets,
            Vector2[] trajectoryPoints)
        {
            if (trajectoryPoints == null || trajectoryPoints.Length < 2) return;

            var arcColor = new Color(255, 180, 60, 140);

            for (int i = 0; i < trajectoryPoints.Length - 1; i++)
            {
                if (i % 2 == 0)
                    GeometryBatch.DrawLine(sb, assets,
                        trajectoryPoints[i], trajectoryPoints[i + 1],
                        arcColor, 1.5f);
            }

            // Landing dot
            GeometryBatch.DrawCircleOutline(sb, assets,
                trajectoryPoints[trajectoryPoints.Length - 1], 4f,
                new Color(255, 200, 80, 200), 8);
        }
    }
}
