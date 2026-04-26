using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using Bloop.Core;
using Bloop.Effects;
using Bloop.Gameplay;
using Bloop.Generators;
using Bloop.Rendering;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Objects
{
    /// <summary>
    /// Collectible blind fish. Same mechanics as CaveLichen but with different stat values.
    ///
    /// Effects on collection:
    ///   - Heals 30 health (more than lichen)
    ///   - 30% chance (seed-determined) of applying a poison debuff:
    ///     stun for 3 seconds + 5 damage
    ///   - Adds 3 kg to player inventory weight
    ///
    /// Visual: translucent ghost-fish. Tail flicks sharper when the player is close;
    /// emits occasional bubbles from the snout.
    /// </summary>
    public class BlindFish : CaveLichen
    {
        private new const int ObjectWidth  = 40; // 2× larger
        private new const int ObjectHeight = 20; // 2× larger

        private new const float HealAmount         = 30f;
        private new const float PoisonDamage       = 5f;
        private new const float PoisonStunDuration = 3f;
        private new const float Weight             = 3f;

        private const float ProximityRadius = 60f;
        private const float BubbleInterval  = 3f;

        private float _proximity01;
        private float _bubbleTimer;
        private readonly ObjectParticleEmitter _bubbles = new ObjectParticleEmitter(12);

        public BlindFish(Vector2 pixelPosition, AetherWorld world, bool isPoisonous,
            ItemRarity rarity = ItemRarity.Common)
            : base(pixelPosition, world, isPoisonous, rarity)
        {
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _bubbles.Update(dt);

            _bubbleTimer -= dt;
            if (_bubbleTimer <= 0f)
            {
                _bubbleTimer = BubbleInterval;
                Vector2 snout = PixelPosition + new Vector2(-ObjectWidth * 0.42f, 0f);
                _bubbles.Emit(snout,
                    new Vector2(NoiseHelpers.HashSigned(_bubbles.ActiveCount) * 3f, -8f),
                    new Color(170, 210, 240), life: 1.4f, size: 2f, gravity: -6f, drag: 0.6f);
            }
        }

        public void UpdateProximity(Player player)
        {
            float d = Vector2.Distance(player.PixelPosition, PixelPosition);
            _proximity01 = MathHelper.Clamp(1f - d / ProximityRadius, 0f, 1f);
        }

        public override void Draw(SpriteBatch spriteBatch, AssetManager assets)
        {
            _bubbles.Draw(spriteBatch, assets);
            base.Draw(spriteBatch, assets);
        }

        protected override void DrawCollectible(SpriteBatch spriteBatch, AssetManager assets)
        {
            var sheet = assets.ObjectBlindFish;
            if (sheet == null) return;
            int frame  = (int)(AnimationClock.Time * sheet.Fps) % Math.Max(1, sheet.FrameCount);
            var src    = sheet.GetSourceRect(frame);
            float scale = sheet.FrameHeight > 0 ? ObjectHeight / (float)sheet.FrameHeight : 1f;
            var origin  = new Vector2(sheet.FrameWidth / 2f, sheet.FrameHeight / 2f);
            spriteBatch.Draw(sheet.Texture, PixelPosition, src, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
        }

        public override Rectangle GetBounds() => new Rectangle(
            (int)(PixelPosition.X - ObjectWidth  / 2f),
            (int)(PixelPosition.Y - ObjectHeight / 2f),
            ObjectWidth, ObjectHeight);

        public override void OnPlayerContact(Player player)
        {
            if (IsDestroyed || IsCollected) return;

            var item = InventoryItem.CreateBlindFish(IsPoisonous);
            bool picked = player.Inventory.TryAdd(item);

            if (!picked) return;

            player.Stats.HealHealth(HealAmount);

            if (IsPoisonous)
            {
                player.Stats.TakeDamage(PoisonDamage);
                player.Stun(PoisonStunDuration);
                player.Debuffs.ApplyDebuff(DebuffType.ReducedJump, 10f);
                player.Debuffs.ApplyDebuff(DebuffType.Blurred, 8f);
            }

            OnCollected();
        }
    }
}
