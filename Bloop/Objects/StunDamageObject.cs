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
    /// A hazard object that damages and stuns the player on contact.
    /// Visual: a pulsing barnacle-eye. Iris dilates when the player approaches;
    /// emits blood-red sparks when lit. Nearly invisible when dark.
    /// </summary>
    public class StunDamageObject : WorldObject
    {
        private const int   ObjectSize    = 24;
        private const float DamageAmount  = 15f;
        private const float StunDuration  = 1.5f;
        private const float Cooldown      = 3f;
        private const float LanternRadius = 200f;
        private const float ProximityRadius = 80f;

        private float _cooldownTimer;
        private bool  _isLit;
        private float _proximity01;
        private float _sparkTimer;
        private readonly ObjectParticleEmitter _sparks = new ObjectParticleEmitter(24);

        public override bool WantsPlayerContact => true;

        public StunDamageObject(Vector2 pixelPosition, AetherWorld world)
            : base(pixelPosition, world)
        {
            Body = BodyFactory.CreateSensorRect(world, pixelPosition, ObjectSize, ObjectSize);
            Body.Tag = this;

            foreach (var fixture in Body.FixtureList)
                fixture.OnCollision += OnCollision;
        }

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (_cooldownTimer > 0f)
                _cooldownTimer -= dt;

            _sparks.Update(dt);

            if (_isLit)
            {
                _sparkTimer -= dt;
                if (_sparkTimer <= 0f)
                {
                    float rate = 1f + _proximity01 * 1.5f;
                    _sparkTimer = 1f / rate;
                    float ang = NoiseHelpers.Hash01((int)(PixelPosition.X + PixelPosition.Y + _sparks.ActiveCount * 7)) * MathHelper.TwoPi;
                    Vector2 vel = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * (8f + _proximity01 * 10f);
                    _sparks.Emit(PixelPosition, vel,
                        new Color(255, 90, 120), life: 0.5f, size: 2f, gravity: 18f, drag: 1.2f);
                }
            }
        }

        public void UpdateLighting(Player player)
        {
            _isLit = IsLitByLantern(player, PixelPosition, LanternRadius);
            float d = Vector2.Distance(player.PixelPosition, PixelPosition);
            _proximity01 = MathHelper.Clamp(1f - d / ProximityRadius, 0f, 1f);
        }

        public override void Draw(SpriteBatch spriteBatch, AssetManager assets)
        {
            _sparks.Draw(spriteBatch, assets);
            WorldObjectRenderer.DrawStunDamageObject(spriteBatch, assets, PixelPosition, _isLit, _proximity01);
        }

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - ObjectSize / 2f - 8),
            (int)(PixelPosition.Y - ObjectSize / 2f - 8),
            ObjectSize + 16, ObjectSize + 16);

        public override void OnPlayerContact(Player player)
        {
            if (_cooldownTimer > 0f) return;

            player.Stats.TakeDamage(DamageAmount);
            player.Stun(StunDuration);
            _cooldownTimer = Cooldown;
        }

        private bool OnCollision(Fixture sender, Fixture other, Contact contact)
        {
            if (other.Body?.Tag is Player player)
                OnPlayerContact(player);
            return true;
        }
    }
}
