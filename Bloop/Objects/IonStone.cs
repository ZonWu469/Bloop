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
    /// Decorative glowing stone — faceted geode with caged violet lightning.
    /// Emits arc sparks toward random facets, synced with LightSource flicker/sputter.
    /// </summary>
    public class IonStone : WorldObject
    {
        private LightSource? _light;
        private readonly int _seed;

        private static readonly Color ColBase  = new Color( 58,  40,  92);
        private static readonly Color ColHi    = new Color(120,  85, 200);
        private static readonly Color ColCore  = new Color(196, 152, 255);
        private static readonly Color ColArc   = new Color(230, 200, 255);
        private static readonly Color ColGlow  = new Color(100,  60, 200);

        private readonly ObjectParticleEmitter _arcs = new ObjectParticleEmitter(12);
        private float _arcTimer;

        public IonStone(Vector2 pixelPosition, AetherWorld world)
            : base(pixelPosition, world)
        {
            _seed = (int)(pixelPosition.X * 11 + pixelPosition.Y * 13);
        }

        public void SetLightSource(LightSource light)
        {
            _light = light;
            light.FlickerAmplitude = 0.12f;
            light.FlickerFrequency = 5f;
            light.SputterChance    = 0.35f;
        }

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - 24), (int)(PixelPosition.Y - 24), 48, 48);

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_light != null) _light.Position = PixelPosition;
            _arcs.Update(dt);

            // Emit arc spark toward random facet
            _arcTimer -= dt;
            if (_arcTimer <= 0f)
            {
                _arcTimer = 0.25f + NoiseHelpers.Hash01(_seed + (int)(AnimationClock.Time * 10f)) * 0.4f;
                float a = NoiseHelpers.Hash01(_seed + (int)(AnimationClock.Time * 20f)) * MathHelper.TwoPi;
                Vector2 vel = new Vector2(MathF.Cos(a), MathF.Sin(a)) * (20f + NoiseHelpers.Hash01(_seed + _arcs.ActiveCount) * 20f);
                _arcs.Emit(PixelPosition, vel, ColArc, life: 0.18f, size: 1f, gravity: 0f, drag: 3f);
            }
        }

        public override void Draw(SpriteBatch sb, AssetManager assets)
        {
            float t = AnimationClock.Time;
            float pulse = AnimationClock.Pulse(2.5f, _seed * 0.001f);

            _arcs.Draw(sb, assets);

            // 1. Outer glow halo
            float glowR = 14f + pulse * 5f;
            OrganicPrimitives.DrawGradientDisk(sb, assets, PixelPosition,
                rIn: 4f, rOut: glowR,
                innerColor: ColGlow * (0.25f + pulse * 0.2f),
                outerColor: ColGlow * 0f,
                rings: 4, segments: 10);

            // 2. Faceted gem shell — irregular polygon, violet base
            OrganicPrimitives.DrawFacetedGem(sb, assets, PixelPosition,
                radius: 8f, facetCount: 7,
                baseColor: ColBase, highlight: ColHi,
                time: t, seed: _seed);

            // 3. Inner core glow
            float coreR = 3f + pulse * 1.5f;
            OrganicPrimitives.DrawGradientDisk(sb, assets, PixelPosition,
                rIn: 0.5f, rOut: coreR,
                innerColor: Color.Lerp(ColCore, Color.White, pulse * 0.5f),
                outerColor: ColCore * 0.4f,
                rings: 3, segments: 8);

            // 4. Internal micro-lightning arcs (2–3 noisy lines, short-lived)
            int arcCount = 2 + (int)(pulse * 1.5f);
            for (int i = 0; i < arcCount; i++)
            {
                float arcPhase = i * 0.33f;
                float arcLife  = t * 3f + arcPhase;
                if ((arcLife % 1f) < 0.35f)
                {
                    float a0 = NoiseHelpers.Hash01(_seed + i * 13) * MathHelper.TwoPi;
                    float a1 = a0 + NoiseHelpers.HashSigned(_seed + i * 29) * 1.2f;
                    float r  = 3.5f + NoiseHelpers.Hash01(_seed + i * 7) * 3f;
                    Vector2 from = PixelPosition + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * 1f;
                    Vector2 to   = PixelPosition + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r;
                    OrganicPrimitives.DrawNoisyLine(sb, assets, from, to,
                        ColArc * (0.6f + pulse * 0.4f), 1f,
                        amplitude: 1.2f, frequency: 12f, time: t + i, seed: _seed + i, segments: 4);
                }
            }
        }
    }
}
