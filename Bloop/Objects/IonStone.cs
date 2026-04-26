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
            _arcs.Draw(sb, assets);

            var sheet = assets.ObjectIonStone;
            if (sheet == null) return;
            int frame  = (int)(AnimationClock.Time * sheet.Fps) % Math.Max(1, sheet.FrameCount);
            var src    = sheet.GetSourceRect(frame);
            float scale = sheet.FrameHeight > 0 ? 48f / sheet.FrameHeight : 1f;
            var origin  = new Vector2(sheet.FrameWidth / 2f, sheet.FrameHeight / 2f);
            sb.Draw(sheet.Texture, PixelPosition, src, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
        }
    }
}
