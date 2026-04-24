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
    /// A stalactite hanging from the ceiling that falls when the player walks beneath it.
    ///
    /// Visual: organic fang with layered stone shading, pulsing veins during shaking,
    /// saliva drip forming at tip, dust puff from anchor, radial shard burst on shatter.
    /// </summary>
    public class FallingStalactite : WorldObject
    {
        private enum StalactiteState { Idle, Shaking, Falling, Shattered, Respawning }
        private StalactiteState _state = StalactiteState.Idle;

        private const float ShakeDuration   = 1.5f;
        private const float ShatterDuration = 0.5f;
        private const float RespawnDuration = 30f;
        private float _timer;

        private const int   Width         = 20;  // 2× larger
        private const int   Height        = 40;  // 2× larger
        private const float DetectRadiusX = 3f * 32f; // wider detection for larger player
        private const float Damage        = 20f;

        private readonly Vector2 _spawnPixelPos;
        private readonly int     _seed;
        private float _shakeOffset;

        private readonly ObjectParticleEmitter _particles = new ObjectParticleEmitter(32);

        private static readonly Color ColBase   = new Color( 68,  52,  36);
        private static readonly Color ColDark   = new Color( 40,  30,  18);
        private static readonly Color ColHi     = new Color(105,  82,  58);
        private static readonly Color ColVein   = new Color(180,  55,  35);
        private static readonly Color ColDrip   = new Color(200, 180, 155);
        private static readonly Color ColDust   = new Color(145, 118,  88);

        private Body? _bodyToRemove;

        public Action<float, float>? OnShakeRequested { get; set; }

        public FallingStalactite(Vector2 pixelPosition, AetherWorld world)
            : base(pixelPosition, world)
        {
            _spawnPixelPos = pixelPosition;
            _seed = (int)(pixelPosition.X * 7 + pixelPosition.Y * 13);
        }

        public override bool WantsPlayerContact => _state == StalactiteState.Falling;

        public override void OnPlayerContact(Player player)
        {
            if (_state != StalactiteState.Falling) return;
            player.Stun(0.6f);
            player.Stats.TakeDamage(Damage);
            OnShakeRequested?.Invoke(8f, 0.4f);
            Shatter();
        }

        public override Rectangle GetBounds()
        {
            if (_state == StalactiteState.Respawning || _state == StalactiteState.Shattered)
                return Rectangle.Empty;
            return new Rectangle(
                (int)(PixelPosition.X - Width / 2),
                (int)(PixelPosition.Y - Height / 2),
                Width, Height);
        }

        public void CheckPlayerProximity(Player player)
        {
            if (_state != StalactiteState.Idle) return;
            float dx = Math.Abs(player.PixelPosition.X - PixelPosition.X);
            float dy = player.PixelPosition.Y - PixelPosition.Y;
            if (dx <= DetectRadiusX && dy > 0f && dy < 200f)
                StartShaking();
        }

        public void TriggerByImpact()
        {
            if (_state == StalactiteState.Idle || _state == StalactiteState.Shaking)
                StartFalling();
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
                case StalactiteState.Shaking:
                    _timer -= dt;
                    _shakeOffset = MathF.Sin(_timer * 30f) * 2f;

                    // Saliva drip slowly grows at tip
                    if (_timer < ShakeDuration * 0.6f && _rng(0.04f, dt))
                    {
                        Vector2 tip = new Vector2(PixelPosition.X, PixelPosition.Y + Height / 2f);
                        _particles.Emit(tip, new Vector2(0, 0.5f), ColDrip,
                            life: ShakeDuration, size: 2f, gravity: 3f, drag: 2f);
                    }
                    if (_timer <= 0f) StartFalling();
                    break;

                case StalactiteState.Falling:
                    _timer -= dt;
                    if (_timer <= 0f) Shatter();
                    break;

                case StalactiteState.Shattered:
                    _timer -= dt;
                    if (_timer <= 0f) StartRespawn();
                    break;

                case StalactiteState.Respawning:
                    _timer -= dt;
                    if (_timer <= 0f) Respawn();
                    break;
            }
        }

        public override void Draw(SpriteBatch spriteBatch, AssetManager assets)
        {
            _particles.Draw(spriteBatch, assets);

            switch (_state)
            {
                case StalactiteState.Idle:
                    DrawFang(spriteBatch, assets, PixelPosition, 0f, 0f);
                    break;

                case StalactiteState.Shaking:
                    float shakeProgress = 1f - (_timer / ShakeDuration);
                    DrawFang(spriteBatch, assets,
                        new Vector2(PixelPosition.X + _shakeOffset, PixelPosition.Y),
                        shakeProgress, AnimationClock.Time);
                    break;

                case StalactiteState.Falling:
                    DrawFang(spriteBatch, assets, PixelPosition, 0f, AnimationClock.Time);
                    break;

                case StalactiteState.Shattered:
                case StalactiteState.Respawning:
                    // Particles carry the shatter visuals
                    break;
            }
        }

        // ── Private helpers ────────────────────────────────────────────────────

        // Simple per-frame random chance: returns true with probability `prob` per second.
        private bool _rng(float probPerSec, float dt)
        {
            // Cheap deterministic skip using timer fractional part
            return (AnimationClock.Time * 1000f % 1f) < (probPerSec * dt);
        }

        private void DrawFang(SpriteBatch sb, AssetManager assets,
            Vector2 pos, float shakeProgress, float t)
        {
            int cx = (int)pos.X;
            int cy = (int)pos.Y;
            int top = cy - Height / 2;

            // Layer 1: dark shadow offset
            GeometryBatch.DrawTriangleSolid(sb, assets,
                new Vector2(cx - Width / 2f + 1, top + 1),
                new Vector2(cx + Width / 2f + 1, top + 1),
                new Vector2(cx + 1, cy + Height / 2f + 1),
                ColDark);

            // Layer 2: stone body — tapered fang shape
            GeometryBatch.DrawTriangleSolid(sb, assets,
                new Vector2(cx - Width / 2f, top),
                new Vector2(cx + Width / 2f, top),
                new Vector2(cx, cy + Height / 2f),
                ColBase);

            // Layer 3: highlight ridge (left face catches cave ambient)
            GeometryBatch.DrawTriangleSolid(sb, assets,
                new Vector2(cx - Width / 2f, top),
                new Vector2(cx - Width / 4f, top),
                new Vector2(cx - Width / 4f, cy + Height / 4f),
                ColHi * 0.6f);

            // Ceiling anchor plug
            assets.DrawRect(sb, new Rectangle(cx - Width / 2 + 1, top, Width - 2, 4), ColBase);

            // Veins — pulse faster as shakeProgress increases
            if (shakeProgress > 0f || t > 0f)
            {
                float vpulse = t > 0f ? AnimationClock.Pulse(4f + shakeProgress * 8f) : 0f;
                Color vc = ColVein * (shakeProgress * 0.7f + vpulse * 0.5f);
                if (vc.A > 5)
                {
                    OrganicPrimitives.DrawVeinNetwork(sb, assets,
                        new Vector2(cx, cy - Height / 4f), vc,
                        branchCount: 3, length: Height * 0.35f,
                        thickness: 1f, time: t, seed: _seed);
                }
            }
        }

        private void StartShaking()
        {
            _state = StalactiteState.Shaking;
            _timer = ShakeDuration;

            // Dust puff from ceiling anchor
            for (int i = 0; i < 6; i++)
            {
                float a = (i / 6f) * MathHelper.TwoPi;
                _particles.Emit(
                    new Vector2(PixelPosition.X, PixelPosition.Y - Height / 2f),
                    new Vector2(MathF.Cos(a) * 15f, -5f - NoiseHelpers.Hash01(_seed + i) * 10f),
                    ColDust, life: 0.6f, size: 2f, gravity: 20f, drag: 1.5f);
            }
        }

        private void StartFalling()
        {
            _state = StalactiteState.Falling;
            _timer = 3f;

            Body = BodyFactory.CreateStalactiteBody(World, PixelPosition);
            foreach (var fixture in Body.FixtureList)
                fixture.OnCollision += OnHitGround;
        }

        private bool OnHitGround(Fixture sender, Fixture other, Contact contact)
        {
            if (other.Body.BodyType == BodyType.Static)
                Shatter();
            return true;
        }

        private void Shatter()
        {
            if (_state == StalactiteState.Shattered) return;

            if (Body != null)
            {
                _bodyToRemove = Body;
                Body = null;
            }

            _state = StalactiteState.Shattered;
            _timer = ShatterDuration;

            // Radial shard burst
            for (int i = 0; i < 10; i++)
            {
                float a = (i / 10f) * MathHelper.TwoPi + NoiseHelpers.HashSigned(_seed + i) * 0.4f;
                float sp = 50f + NoiseHelpers.Hash01(_seed + i * 17) * 60f;
                Color shardCol = Color.Lerp(ColBase, ColHi, NoiseHelpers.Hash01(_seed + i * 3));
                _particles.Emit(PixelPosition,
                    new Vector2(MathF.Cos(a) * sp, MathF.Sin(a) * sp - 20f),
                    shardCol, life: 0.5f, size: 2f + (i % 3), gravity: 150f, drag: 0.5f);
            }
            // Dust cloud
            for (int i = 0; i < 8; i++)
            {
                float a = NoiseHelpers.Hash01(_seed + i * 29) * MathHelper.TwoPi;
                _particles.Emit(PixelPosition,
                    new Vector2(MathF.Cos(a) * 20f, MathF.Sin(a) * 20f - 10f),
                    ColDust, life: 0.6f, size: 3f, gravity: 30f, drag: 2f);
            }
        }

        private void StartRespawn()
        {
            _state = StalactiteState.Respawning;
            _timer = RespawnDuration;
            IsActive = false;
        }

        private void Respawn()
        {
            _state = StalactiteState.Idle;
            PixelPosition = _spawnPixelPos;
            IsActive = true;
            _shakeOffset = 0f;
        }
    }
}
