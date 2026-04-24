using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Effects;
using Bloop.Lighting;
using Bloop.Rendering;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Objects
{
    /// <summary>
    /// Decorative bioluminescent moss carpet. 12–15 seeded frond stalks with
    /// luminous tips pulse on independent phases; emits 1 spore every ~2s.
    /// </summary>
    public class PhosphorMoss : WorldObject
    {
        private LightSource? _light;
        private readonly int _seed;
        private readonly int _frondCount;

        private static readonly Color ColDark   = new Color( 30,  55,  16);
        private static readonly Color ColMid    = new Color( 72, 118,  38);
        private static readonly Color ColBright = new Color(160, 210,  72);
        private static readonly Color ColGlow   = new Color(200, 250, 110);
        private static readonly Color ColSpore  = new Color(210, 255, 140);

        private readonly ObjectParticleEmitter _spores = new ObjectParticleEmitter(12);
        private float _sporeTimer;

        public PhosphorMoss(Vector2 pixelPosition, AetherWorld world)
            : base(pixelPosition, world)
        {
            _seed       = (int)(pixelPosition.X * 11 + pixelPosition.Y * 7);
            _frondCount = 12 + ((_seed & 7) % 4);  // 12–15
        }

        public void SetLightSource(LightSource light) => _light = light;

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - 28), (int)(PixelPosition.Y - 32), 56, 40);

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_light != null) _light.Position = PixelPosition;
            _spores.Update(dt);

            _sporeTimer -= dt;
            if (_sporeTimer <= 0f)
            {
                _sporeTimer = 1.6f + NoiseHelpers.Hash01(_seed + _spores.ActiveCount) * 0.8f;
                // Pick a random frond to emit from
                int fi = (int)(NoiseHelpers.Hash01(_seed + (int)(AnimationClock.Time * 10f)) * _frondCount);
                float fx = PixelPosition.X + NoiseHelpers.HashSigned(_seed + fi * 37) * 10f;
                float fy = PixelPosition.Y - 6f - NoiseHelpers.Hash01(_seed + fi * 13) * 4f;
                _spores.Emit(new Vector2(fx, fy),
                    new Vector2(NoiseHelpers.HashSigned(_seed + fi) * 3f, -7f),
                    ColSpore, life: 1.8f, size: 2f, gravity: -3f, drag: 0.4f);
            }
        }

        public override void Draw(SpriteBatch sb, AssetManager assets)
        {
            float t = AnimationClock.Time;
            float basePulse = AnimationClock.Pulse(1.2f, _seed * 0.01f);

            _spores.Draw(sb, assets);

            // 1. Dark base blob — carpet underlayer
            OrganicPrimitives.DrawBlob(sb, assets, PixelPosition + new Vector2(0, 4),
                12f, ColDark * 0.85f,
                lobeCount: 4, time: t * 0.5f, wobbleAmp: 0.12f, seed: _seed);

            // 2. Mid-layer bloom
            OrganicPrimitives.DrawBlob(sb, assets, PixelPosition + new Vector2(0, 3),
                9f, ColMid * 0.8f,
                lobeCount: 5, time: t * 0.7f, wobbleAmp: 0.1f, seed: _seed + 11);

            // 3. Frond stalks — each a short bezier quad with luminous tip
            for (int i = 0; i < _frondCount; i++)
            {
                float phase    = NoiseHelpers.Hash01(_seed + i * 37) * MathHelper.TwoPi;
                float tipPulse = AnimationClock.Pulse(1.2f + NoiseHelpers.Hash01(_seed + i * 13) * 0.6f, phase);
                float bx = PixelPosition.X + NoiseHelpers.HashSigned(_seed + i * 37) * 10f;
                float byBase = PixelPosition.Y + 2f;
                float byTip  = byBase - 5f - NoiseHelpers.Hash01(_seed + i * 7) * 4f;
                float ctrlX  = bx + NoiseHelpers.HashSigned(_seed + i * 53) * 2.5f;
                float ctrlY  = (byBase + byTip) * 0.5f;

                Color stalkCol = Color.Lerp(ColMid, ColBright, tipPulse * 0.4f);
                OrganicPrimitives.DrawBezierQuad(sb, assets,
                    new Vector2(bx, byBase),
                    new Vector2(ctrlX, ctrlY),
                    new Vector2(bx + NoiseHelpers.HashSigned(_seed + i) * 1.5f, byTip),
                    stalkCol, 1.5f, 5);

                // Luminous tip dot
                float tipAlpha = 0.6f + tipPulse * 0.4f;
                Color tipCol   = Color.Lerp(ColBright, ColGlow, tipPulse);
                assets.DrawRect(sb,
                    new Rectangle((int)bx - 1, (int)byTip - 1, 2, 2),
                    tipCol * tipAlpha);
            }

            // 4. Soft center glow
            OrganicPrimitives.DrawGradientDisk(sb, assets, PixelPosition,
                rIn: 2f, rOut: 8f + basePulse * 3f,
                innerColor: ColGlow * (0.35f + basePulse * 0.25f),
                outerColor: ColGlow * 0f,
                rings: 3, segments: 10);
        }
    }
}
