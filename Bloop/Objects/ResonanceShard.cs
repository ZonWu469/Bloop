using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Contacts;
using Bloop.Core;
using Bloop.Gameplay;
using Bloop.Lighting;
using Bloop.Physics;
using Bloop.Rendering;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Objects
{
    /// <summary>
    /// Primary collectible goal. 6-sided faceted artifact core with two
    /// counter-rotating gradient halo rings and 3 motion-trailed orbital sparks.
    /// </summary>
    public class ResonanceShard : WorldObject
    {
        private const int SensorSize = 20;

        private static readonly Color ColCore  = new Color(180, 140, 255);
        private static readonly Color ColOuter = new Color(220, 190, 255);
        private static readonly Color ColGlow  = new Color(140, 100, 230);
        private static readonly Color ColSpark = new Color(240, 220, 255);
        private static readonly Color ColHalo1 = new Color(160, 110, 240);
        private static readonly Color ColHalo2 = new Color(110, 160, 250);

        private LightSource? _lightSource;

        public event Action? OnCollected;

        public ResonanceShard(Vector2 pixelPosition, AetherWorld world)
            : base(pixelPosition, world)
        {
            Body = BodyFactory.CreateSensorRect(world, pixelPosition, SensorSize, SensorSize);
            Body.Tag = this;
            foreach (var fixture in Body.FixtureList)
                fixture.OnCollision += OnCollision;
        }

        public override bool WantsPlayerContact => true;

        public void SetLightSource(LightSource light) => _lightSource = light;

        public override void Update(GameTime gameTime)
        {
            if (_lightSource != null)
                _lightSource.Position = PixelPosition;
        }

        public override void Draw(SpriteBatch spriteBatch, AssetManager assets)
        {
            float t     = AnimationClock.Time;
            float pulse = AnimationClock.Pulse(3f);
            int   seed  = (int)(PixelPosition.X * 7 + PixelPosition.Y * 11);

            // 1. Outer gradient halo ring (rotating CW)
            float halo1R = 20f + pulse * 4f;
            OrganicPrimitives.DrawGradientDisk(spriteBatch, assets, PixelPosition,
                rIn: halo1R - 4f, rOut: halo1R,
                innerColor: ColHalo1 * (0.22f + pulse * 0.15f),
                outerColor: ColHalo1 * 0f,
                rings: 3, segments: 12);

            // 2. Inner gradient halo ring (counter-rotating, offset color)
            float halo2R = 13f + pulse * 2f;
            OrganicPrimitives.DrawGradientDisk(spriteBatch, assets, PixelPosition,
                rIn: halo2R - 3f, rOut: halo2R,
                innerColor: ColHalo2 * (0.18f + pulse * 0.12f),
                outerColor: ColHalo2 * 0f,
                rings: 3, segments: 10);

            // 3. Center glow
            OrganicPrimitives.DrawGradientDisk(spriteBatch, assets, PixelPosition,
                rIn: 2f, rOut: 8f + pulse * 3f,
                innerColor: ColGlow * (0.35f + pulse * 0.3f),
                outerColor: ColGlow * 0f,
                rings: 4, segments: 10);

            // 4. Faceted gem core — 6-sided
            float gemR = 6f + pulse * 1.5f;
            OrganicPrimitives.DrawFacetedGem(spriteBatch, assets, PixelPosition,
                radius: gemR, facetCount: 6,
                baseColor: ColCore, highlight: ColOuter,
                time: t, seed: seed);

            // 5. Three orbiting sparks with short motion-trail lines
            int sparkCount = 3;
            for (int s = 0; s < sparkCount; s++)
            {
                float ang  = t * 1.5f * (s % 2 == 0 ? 1f : -0.75f) + s * MathHelper.TwoPi / sparkCount;
                float orbit = 13f + pulse * 3f;
                Vector2 sp  = PixelPosition + new Vector2(MathF.Cos(ang) * orbit, MathF.Sin(ang) * orbit * 0.65f);

                // Trail line behind spark
                float ang2 = ang - 0.35f * (s % 2 == 0 ? 1f : -0.75f);
                Vector2 sp2 = PixelPosition + new Vector2(MathF.Cos(ang2) * orbit, MathF.Sin(ang2) * orbit * 0.65f);
                GeometryBatch.DrawLine(spriteBatch, assets, sp2, sp,
                    ColSpark * (0.25f + pulse * 0.15f), 1.5f);

                float spAlpha = 0.5f + AnimationClock.Pulse(2.5f, s * 0.5f) * 0.5f;
                assets.DrawRect(spriteBatch,
                    new Rectangle((int)sp.X - 1, (int)sp.Y - 1, 2, 2),
                    ColSpark * spAlpha);
            }

            // 6. Cardinal light rays
            float rayLen   = 6f + pulse * 5f;
            float rayAlpha = 0.2f + pulse * 0.2f;
            for (int d = 0; d < 4; d++)
            {
                float a   = d * MathHelper.PiOver2 + t * 0.3f;
                Vector2 dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                GeometryBatch.DrawLine(spriteBatch, assets,
                    PixelPosition + dir * (gemR + 1f),
                    PixelPosition + dir * (gemR + 1f + rayLen),
                    ColOuter * rayAlpha, 1.5f);
            }
        }

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - SensorSize / 2f),
            (int)(PixelPosition.Y - SensorSize / 2f),
            SensorSize, SensorSize);

        public override void OnPlayerContact(Player player)
        {
            if (IsDestroyed) return;

            if (_lightSource != null)
            {
                _lightSource.Radius    = 0f;
                _lightSource.Intensity = 0f;
            }

            OnCollected?.Invoke();
            Destroy();
        }

        private bool OnCollision(Fixture sender, Fixture other, Contact contact)
        {
            if (other.Body?.Tag is Player player)
                OnPlayerContact(player);
            return true;
        }
    }
}
