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
            _spores.Draw(sb, assets);

            var sheet = assets.ObjectPhosphorMoss;
            if (sheet == null) return;
            int frame  = (int)(AnimationClock.Time * sheet.Fps) % Math.Max(1, sheet.FrameCount);
            var src    = sheet.GetSourceRect(frame);
            float scale = sheet.FrameHeight > 0 ? 40f / sheet.FrameHeight : 1f;
            var origin  = new Vector2(sheet.FrameWidth / 2f, sheet.FrameHeight / 2f);
            sb.Draw(sheet.Texture, PixelPosition, src, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
        }
    }
}
