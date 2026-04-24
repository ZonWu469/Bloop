using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Contacts;
using Bloop.Core;
using Bloop.Effects;
using Bloop.Gameplay;
using Bloop.Physics;
using Bloop.Rendering;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Objects
{
    /// <summary>
    /// A chunk of rubble spawned by the EarthquakeSystem when a tile collapses.
    /// Falls under gravity with motion-blur echo trails and a dust emitter.
    /// Radial debris burst + dust plume on landing.
    /// </summary>
    public class FallingRubble : WorldObject
    {
        private enum RubbleState { Falling, Shattered }
        private RubbleState _state = RubbleState.Falling;

        private const float ShatterDuration = 0.6f;
        private const float MaxFallTime     = 4f;
        private const float Damage          = 10f;
        private const int   Size            = 28; // 2× larger

        private float _failsafeTimer;
        private Body? _bodyToRemove;

        // Echo positions for motion-blur trail (ring buffer)
        private readonly Vector2[] _echoPositions = new Vector2[3];
        private int _echoHead;
        private float _echoTimer;
        private const float EchoInterval = 0.05f;

        private readonly ObjectParticleEmitter _particles = new ObjectParticleEmitter(32);

        private static readonly Color ColRock = new Color( 95,  78,  58);
        private static readonly Color ColDark = new Color( 55,  44,  30);
        private static readonly Color ColHi   = new Color(128, 108,  82);
        private static readonly Color ColDust = new Color(155, 132, 100);

        private readonly int _seed;

        public FallingRubble(Vector2 pixelPosition, AetherWorld world)
            : base(pixelPosition, world)
        {
            _seed = (int)(pixelPosition.X * 11 + pixelPosition.Y * 17);

            Body = BodyFactory.CreateStalactiteBody(world, pixelPosition);
            foreach (var fixture in Body.FixtureList)
                fixture.OnCollision += OnHitGround;

            _failsafeTimer = MaxFallTime;
            for (int i = 0; i < _echoPositions.Length; i++)
                _echoPositions[i] = pixelPosition;
        }

        public override bool WantsPlayerContact => _state == RubbleState.Falling;

        public override void OnPlayerContact(Player player)
        {
            if (_state != RubbleState.Falling) return;
            player.Stats.TakeDamage(Damage);
            Shatter();
        }

        public override Rectangle GetBounds()
        {
            if (_state == RubbleState.Shattered) return Rectangle.Empty;
            return new Rectangle(
                (int)(PixelPosition.X - Size / 2),
                (int)(PixelPosition.Y - Size / 2),
                Size, Size);
        }

        public override void Update(GameTime gameTime)
        {
            if (_bodyToRemove != null)
            {
                World.Remove(_bodyToRemove);
                _bodyToRemove = null;
            }

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _particles.Update(dt);

            switch (_state)
            {
                case RubbleState.Falling:
                    _failsafeTimer -= dt;
                    if (_failsafeTimer <= 0f) Shatter();

                    // Capture echo for motion-blur trail
                    _echoTimer += dt;
                    if (_echoTimer >= EchoInterval)
                    {
                        _echoTimer = 0f;
                        _echoPositions[_echoHead] = PixelPosition;
                        _echoHead = (_echoHead + 1) % _echoPositions.Length;
                    }

                    // Dust trail
                    if (_particles.ActiveCount < 20 && (int)(AnimationClock.Time * 30f) % 2 == 0)
                    {
                        _particles.Emit(PixelPosition + new Vector2(NoiseHelpers.HashSigned(_seed + _particles.ActiveCount) * 4f, 0),
                            new Vector2(NoiseHelpers.HashSigned(_seed + _echoHead) * 8f, -5f),
                            ColDust, life: 0.35f, size: 2f, gravity: 20f, drag: 1.5f);
                    }
                    break;

                case RubbleState.Shattered:
                    if (_particles.ActiveCount == 0)
                        Destroy();
                    break;
            }
        }

        public override void Draw(SpriteBatch spriteBatch, AssetManager assets)
        {
            _particles.Draw(spriteBatch, assets);

            if (_state == RubbleState.Falling)
            {
                // Motion-blur ghost echoes (oldest → faintest)
                for (int i = 0; i < _echoPositions.Length; i++)
                {
                    int idx = (_echoHead + i) % _echoPositions.Length;
                    float alpha = (i + 1) / (float)(_echoPositions.Length + 1) * 0.35f;
                    int es = Size - 2 + i;
                    assets.DrawRect(spriteBatch,
                        new Rectangle((int)_echoPositions[idx].X - es / 2,
                                      (int)_echoPositions[idx].Y - es / 2, es, es),
                        ColRock * alpha);
                }

                // Main rock — irregular polygon via stacked triangles
                int cx = (int)PixelPosition.X;
                int cy = (int)PixelPosition.Y;

                // Dark base
                GeometryBatch.DrawTriangleSolid(spriteBatch, assets,
                    new Vector2(cx - Size / 2f, cy + Size / 2f),
                    new Vector2(cx + Size / 2f, cy + Size / 2f),
                    new Vector2(cx, cy - Size / 2f),
                    ColDark);

                // Main fill (seeded irregular shape)
                float jL = NoiseHelpers.Hash01(_seed)      * 4f - 2f;
                float jR = NoiseHelpers.Hash01(_seed + 1)  * 4f - 2f;
                float jT = NoiseHelpers.Hash01(_seed + 2)  * 3f - 1f;
                GeometryBatch.DrawTriangleSolid(spriteBatch, assets,
                    new Vector2(cx - Size / 2f + jL, cy + Size / 3f),
                    new Vector2(cx + Size / 2f + jR, cy + Size / 3f),
                    new Vector2(cx + jT, cy - Size / 2f),
                    ColRock);
                // Lower fill
                assets.DrawRect(spriteBatch,
                    new Rectangle(cx - Size / 2 + 1, cy, Size - 2, Size / 2 - 1),
                    ColRock);
                // Highlight chip
                GeometryBatch.DrawTriangleSolid(spriteBatch, assets,
                    new Vector2(cx - Size / 3f, cy - Size / 4f),
                    new Vector2(cx, cy - Size / 2f),
                    new Vector2(cx + Size / 5f, cy - Size / 5f),
                    ColHi * 0.7f);
            }
        }

        private bool OnHitGround(Fixture sender, Fixture other, Contact contact)
        {
            if (other.Body?.Tag is Player player)
            {
                player.Stats.TakeDamage(Damage);
                Shatter();
                return true;
            }
            if (other.Body != null && other.Body.BodyType == BodyType.Static)
                Shatter();
            return true;
        }

        private void Shatter()
        {
            if (_state == RubbleState.Shattered) return;

            if (Body != null)
            {
                _bodyToRemove = Body;
                Body = null;
            }

            _state = RubbleState.Shattered;

            // Radial debris shards
            for (int i = 0; i < 8; i++)
            {
                float a = (i / 8f) * MathHelper.TwoPi + NoiseHelpers.HashSigned(_seed + i) * 0.5f;
                float sp = 55f + NoiseHelpers.Hash01(_seed + i * 7) * 65f;
                _particles.Emit(PixelPosition,
                    new Vector2(MathF.Cos(a) * sp, MathF.Sin(a) * sp - 30f),
                    Color.Lerp(ColRock, ColHi, NoiseHelpers.Hash01(_seed + i * 3)),
                    life: ShatterDuration, size: 2f + (i & 2), gravity: 180f, drag: 0.5f);
            }
            // Dust plume
            for (int i = 0; i < 8; i++)
            {
                float a = NoiseHelpers.Hash01(_seed + i * 13) * MathHelper.TwoPi;
                _particles.Emit(PixelPosition,
                    new Vector2(MathF.Cos(a) * 22f, MathF.Sin(a) * 10f - 15f),
                    ColDust, life: 0.7f, size: 3f, gravity: 25f, drag: 2f);
            }
        }
    }
}
