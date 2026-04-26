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
        private const int SensorSize = 40; // 2× larger

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
            var sheet = assets.ObjectResonanceShard;
            if (sheet == null) return;
            int frame  = (int)(AnimationClock.Time * sheet.Fps) % Math.Max(1, sheet.FrameCount);
            var src    = sheet.GetSourceRect(frame);
            float scale = sheet.FrameHeight > 0 ? SensorSize / (float)sheet.FrameHeight : 1f;
            var origin  = new Vector2(sheet.FrameWidth / 2f, sheet.FrameHeight / 2f);
            spriteBatch.Draw(sheet.Texture, PixelPosition, src, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
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
