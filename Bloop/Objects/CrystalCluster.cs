using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Contacts;
using Bloop.Core;
using Bloop.Effects;
using Bloop.Gameplay;
using Bloop.Lighting;
using Bloop.Physics;
using Bloop.Rendering;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Objects
{
    /// <summary>
    /// Interactive glowing crystal cluster — multi-facet prismatic shards.
    /// A light cascade travels up each prong in staggered phase.
    /// Shatters into a rainbow burst of 10–12 shard particles on player contact.
    /// </summary>
    public class CrystalCluster : WorldObject
    {
        public enum Variant { Cyan, Violet, Red }

        private const int   SensorSize       = 44; // 2× larger
        private const float ShatterDuration  = 5f;
        private const float ShatterFlashPeak = 4.5f;

        public Variant Flavor { get; }

        private LightSource? _light;
        private float        _shatterT = -1f;
        private float        _origLightRadius;
        private float        _origLightIntensity;

        private readonly Color _coreCol;
        private readonly Color _edgeCol;
        private readonly Color _glowCol;
        private readonly Color _hiCol;

        private readonly int _seed;
        private readonly int _shardCount;

        private readonly ObjectParticleEmitter _shards = new ObjectParticleEmitter(24);
        private float _moteTimer;

        public CrystalCluster(Vector2 pixelPosition, AetherWorld world, Variant variant)
            : base(pixelPosition, world)
        {
            Flavor    = variant;
            _seed     = (int)(pixelPosition.X * 13 + pixelPosition.Y * 7);
            _shardCount = 5 + (_seed & 3); // 5–7 shards

            Body = BodyFactory.CreateSensorRect(world, pixelPosition, SensorSize, SensorSize);
            Body.Tag = this;
            foreach (var fixture in Body.FixtureList)
                fixture.OnCollision += OnCollision;

            switch (variant)
            {
                case Variant.Cyan:
                    _coreCol = new Color(150, 240, 255);
                    _edgeCol = new Color( 40, 120, 170);
                    _glowCol = new Color(110, 200, 240);
                    _hiCol   = new Color(220, 255, 255);
                    break;
                case Variant.Violet:
                    _coreCol = new Color(220, 170, 255);
                    _edgeCol = new Color( 90,  50, 140);
                    _glowCol = new Color(180, 120, 240);
                    _hiCol   = new Color(245, 230, 255);
                    break;
                default: // Red
                    _coreCol = new Color(255, 150, 150);
                    _edgeCol = new Color(150,  40,  50);
                    _glowCol = new Color(230, 100, 110);
                    _hiCol   = new Color(255, 220, 210);
                    break;
            }
        }

        public override bool WantsPlayerContact => _shatterT < 0f;

        public void SetLightSource(LightSource light)
        {
            _light              = light;
            _origLightRadius    = light.Radius;
            _origLightIntensity = light.Intensity;
        }

        public static Color ColorFor(Variant v) => v switch
        {
            Variant.Cyan   => new Color(110, 200, 240),
            Variant.Violet => new Color(180, 120, 240),
            _              => new Color(230, 100, 110),
        };

        public static Variant VariantFromSeed(int seed) =>
            (Variant)(((seed & 0x7fffffff) % 3));

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - SensorSize / 2f),
            (int)(PixelPosition.Y - SensorSize / 2f),
            SensorSize, SensorSize);

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_light != null) _light.Position = PixelPosition;
            _shards.Update(dt);

            if (_shatterT >= 0f)
            {
                _shatterT += dt;

                if (_light != null)
                {
                    float norm  = MathHelper.Clamp(_shatterT / ShatterDuration, 0f, 1f);
                    float boost = (1f - norm) * (1f - norm);
                    _light.Radius    = _origLightRadius    + ShatterFlashPeak * 40f * boost;
                    _light.Intensity = _origLightIntensity + boost * 0.8f;
                }

                if (_shatterT >= ShatterDuration)
                {
                    if (_light != null) { _light.Radius = 0f; _light.Intensity = 0f; }
                    Destroy();
                }
                return;
            }

            // Ambient orbiting mote
            _moteTimer -= dt;
            if (_moteTimer <= 0f)
            {
                _moteTimer = 0.9f + NoiseHelpers.Hash01(_seed + _shards.ActiveCount * 7) * 0.6f;
                float a = NoiseHelpers.Hash01(_seed + (int)(AnimationClock.Time * 5f)) * MathHelper.TwoPi;
                float r = 10f + NoiseHelpers.Hash01(_seed + _shards.ActiveCount) * 4f;
                _shards.Emit(PixelPosition + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r),
                    new Vector2(MathF.Cos(a + MathHelper.PiOver2) * 6f, MathF.Sin(a + MathHelper.PiOver2) * 6f),
                    _hiCol, life: 0.8f, size: 1f, gravity: 0f, drag: 2f);
            }
        }

        public override void Draw(SpriteBatch sb, AssetManager assets)
        {
            _shards.Draw(sb, assets);

            if (_shatterT >= 0f)
            {
                // Fading glow flash
                float norm  = MathHelper.Clamp(_shatterT / ShatterDuration, 0f, 1f);
                float flash = (1f - norm) * (1f - norm);
                int haloR = (int)(14 + flash * 22);
                OrganicPrimitives.DrawGradientDisk(sb, assets, PixelPosition,
                    rIn: 2f, rOut: haloR,
                    innerColor: _glowCol * (0.3f + flash * 0.7f),
                    outerColor: _glowCol * 0f,
                    rings: 5, segments: 12);
                return;
            }

            var sheet = assets.ObjectCrystalCluster;
            if (sheet == null) return;
            int frame  = (int)(AnimationClock.Time * sheet.Fps) % Math.Max(1, sheet.FrameCount);
            var src    = sheet.GetSourceRect(frame);
            float scale = sheet.FrameHeight > 0 ? SensorSize / (float)sheet.FrameHeight : 1f;
            var origin  = new Vector2(sheet.FrameWidth / 2f, sheet.FrameHeight / 2f);
            sb.Draw(sheet.Texture, PixelPosition, src, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
        }

        public override void OnPlayerContact(Player player)
        {
            if (_shatterT >= 0f || IsDestroyed) return;
            _shatterT = 0f;

            if (Body != null) Body.Enabled = false;

            // Rainbow shard burst
            for (int i = 0; i < 12; i++)
            {
                float a  = (i / 12f) * MathHelper.TwoPi + NoiseHelpers.HashSigned(_seed + i) * 0.4f;
                float sp = 50f + NoiseHelpers.Hash01(_seed + i * 7) * 70f;
                // Cycle colors across shards for rainbow effect
                float hue  = (i / 12f) + AnimationClock.Time * 0.1f;
                Color col  = Color.Lerp(_coreCol, _glowCol, NoiseHelpers.Hash01(_seed + i * 3));
                _shards.Emit(PixelPosition,
                    new Vector2(MathF.Cos(a) * sp, MathF.Sin(a) * sp - 30f),
                    col, life: ShatterDuration * 0.6f, size: 2f + (i & 2), gravity: 120f, drag: 0.5f);
            }
        }

        private bool OnCollision(Fixture sender, Fixture other, Contact contact)
        {
            if (other.Body?.Tag is Player player)
                OnPlayerContact(player);
            return true;
        }
    }
}
