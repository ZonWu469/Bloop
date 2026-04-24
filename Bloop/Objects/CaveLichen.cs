using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Contacts;
using Bloop.Core;
using Bloop.Effects;
using Bloop.Gameplay;
using Bloop.Generators;
using Bloop.Physics;
using Bloop.Rendering;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Objects
{
    /// <summary>
    /// Collectible cave lichen. Fungal rosette — breathes softly; bursts a puff
    /// of spores when picked up.
    /// </summary>
    public class CaveLichen : WorldObject
    {
        protected const int ObjectWidth  = 32; // 2× larger
        protected const int ObjectHeight = 32; // 2× larger

        protected const float HealAmount         = 20f;
        protected const float PoisonDamage       = 10f;
        protected const float PoisonStunDuration = 2f;
        protected const float Weight             = 2f;

        protected readonly bool IsPoisonous;
        public readonly ItemRarity Rarity;

        private bool  _collected;
        protected bool IsCollected => _collected;
        private float _puffTimer;
        private readonly ObjectParticleEmitter _puff = new ObjectParticleEmitter(16);

        public CaveLichen(Vector2 pixelPosition, AetherWorld world, bool isPoisonous,
            ItemRarity rarity = ItemRarity.Common)
            : base(pixelPosition, world)
        {
            IsPoisonous = isPoisonous;
            Rarity      = rarity;

            Body = BodyFactory.CreateSensorRect(world, pixelPosition, ObjectWidth, ObjectHeight);
            Body.Tag = this;

            foreach (var fixture in Body.FixtureList)
                fixture.OnCollision += OnCollision;
        }

        public override bool WantsPlayerContact => true;

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _puff.Update(dt);

            if (_collected)
            {
                _puffTimer -= dt;
                if (_puffTimer <= 0f && _puff.ActiveCount == 0)
                    Destroy();
            }
        }

        public override void Draw(SpriteBatch spriteBatch, AssetManager assets)
        {
            _puff.Draw(spriteBatch, assets);
            if (!_collected)
                DrawCollectible(spriteBatch, assets);
        }

        /// <summary>
        /// Renders the collectible body. Subclasses (e.g. BlindFish) override to
        /// draw their own appearance while inheriting the spore-puff pickup effect.
        /// </summary>
        protected virtual void DrawCollectible(SpriteBatch spriteBatch, AssetManager assets)
        {
            WorldObjectRenderer.DrawCaveLichen(spriteBatch, assets, PixelPosition, IsPoisonous, Rarity);
        }

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - ObjectWidth  / 2f),
            (int)(PixelPosition.Y - ObjectHeight / 2f),
            ObjectWidth, ObjectHeight);

        public override void OnPlayerContact(Player player)
        {
            if (IsDestroyed || _collected) return;

            var item = InventoryItem.CreateCaveLichen(IsPoisonous);
            bool picked = player.Inventory.TryAdd(item);

            if (!picked) return;

            player.Stats.HealHealth(HealAmount);

            if (IsPoisonous)
            {
                player.Stats.TakeDamage(PoisonDamage);
                player.Stun(PoisonStunDuration);
                player.Debuffs.ApplyDebuff(DebuffType.SlowMovement, 10f);
                player.Debuffs.ApplyDebuff(DebuffType.Blurred, 8f);
            }

            OnCollected();
        }

        /// <summary>Called after a successful pickup — detaches body, fires spore puff.</summary>
        protected virtual void OnCollected()
        {
            _collected = true;
            _puffTimer = 1.2f;

            // Detach body so we won't be picked up again while puffing.
            if (Body != null)
            {
                World.Remove(Body);
                Body = null;
            }

            // Spore puff burst
            Color puffColor = IsPoisonous ? new Color(140, 220, 60) : new Color(210, 250, 140);
            for (int i = 0; i < 12; i++)
            {
                float a = (i / 12f) * MathHelper.TwoPi + NoiseHelpers.HashSigned(i * 7) * 0.3f;
                float sp = 14f + NoiseHelpers.Hash01(i * 13) * 10f;
                _puff.Emit(PixelPosition,
                    new Vector2(MathF.Cos(a), MathF.Sin(a)) * sp,
                    puffColor, life: 0.9f, size: 2f, gravity: -10f, drag: 1.5f);
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
