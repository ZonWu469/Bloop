using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
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
    /// A thrown light flare. Bounces on landing, illuminates a 200px radius for 30 seconds,
    /// reveals fog-of-war tiles while active, and fades out in the final 5 seconds.
    /// </summary>
    public class FlareObject : WorldObject
    {
        private readonly LightingSystem? _lighting;
        private readonly Level           _level;
        private readonly FlareLight      _light;
        private float                    _remainingLife;
        private bool                     _hasLanded;
        private bool                     _lightRemoved;

        public FlareObject(Vector2 spawnPixelPos, Vector2 launchVelocityPixels,
            AetherWorld world, LightingSystem? lighting, Level level)
            : base(spawnPixelPos, world)
        {
            _lighting      = lighting;
            _level         = level;
            _remainingLife = FlareLight.FlareLightLifetime;

            // Create a small dynamic physics body
            Body = world.CreateBody(PhysicsManager.ToMeters(spawnPixelPos), 0f, BodyType.Dynamic);
            Body.FixedRotation = true;

            float w = PhysicsManager.ToMeters(12f); // 2× larger
            float h = PhysicsManager.ToMeters(6f);  // 2× larger
            var fixture = Body.CreateRectangle(w, h, 0.8f, Vector2.Zero);
            fixture.CollisionCategories = CollisionCategories.WorldObject;
            fixture.CollidesWith        = CollisionCategories.Terrain | CollisionCategories.Platform;
            fixture.Restitution         = 0.15f;
            fixture.Friction            = 0.6f;

            Body.LinearVelocity = PhysicsManager.ToMeters(launchVelocityPixels);

            _light = new FlareLight(spawnPixelPos);
            _lighting?.AddLight(_light);
        }

        /// <summary>
        /// The light source attached to this flare. Exposed so Level.Update can
        /// include it in the entity light-reaction perception pass.
        /// </summary>
        public FlareLight Light => _light;

        public override Rectangle GetBounds()
            => new Rectangle((int)(PixelPosition.X - 24), (int)(PixelPosition.Y - 24), 48, 48);

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            _remainingLife -= dt;
            if (_remainingLife <= 0f)
            {
                RemoveLight();
                Destroy();
                return;
            }

            // Track flare position
            _light.Position = PixelPosition;

            // Reveal fog of war around the flare
            _level.RevealAround(PixelPosition, FlareLight.FlareLightRadius);

            // Detect landing: body almost still → lock it in place
            if (!_hasLanded && Body != null)
            {
                float speed = PhysicsManager.ToPixels(Body.LinearVelocity).Length();
                if (speed < 15f)
                {
                    _hasLanded          = true;
                    Body.LinearDamping  = 8f;
                }
            }
        }

        public override void Draw(SpriteBatch spriteBatch, AssetManager assets)
        {
            if (!IsActive || IsDestroyed) return;

            var sheet = assets.ObjectFlareObject;
            if (sheet == null) return;
            int frame  = (int)(AnimationClock.Time * sheet.Fps) % MathHelper.Max(1, sheet.FrameCount);
            var src    = sheet.GetSourceRect(frame);
            float scale = sheet.FrameHeight > 0 ? 12f / sheet.FrameHeight : 1f;
            var origin  = new Vector2(sheet.FrameWidth / 2f, sheet.FrameHeight / 2f);
            spriteBatch.Draw(sheet.Texture, PixelPosition, src, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
        }

        /// <summary>Force-expire this flare immediately (called on level unload).</summary>
        public void ExpireNow()
        {
            RemoveLight();
            Destroy();
        }

        private void RemoveLight()
        {
            if (_lightRemoved) return;
            _lightRemoved = true;
            _lighting?.RemoveLight(_light);
        }
    }
}
