using System.Collections.Generic;
using Microsoft.Xna.Framework;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Common;

// Alias to avoid conflict with Bloop.World namespace
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Physics
{
    /// <summary>
    /// Helper factory for creating Aether.Physics2D bodies with correct
    /// collision categories, unit conversion, and fixture configuration.
    /// All pixel positions/sizes are converted to meters internally.
    /// Uses Aether body extension methods (CreateRectangle, CreateCircle, CreateChainShape).
    /// </summary>
    public static class BodyFactory
    {
        // ── Player body ────────────────────────────────────────────────────────

        /// <summary>
        /// Create the player's dynamic body: a rectangle approximating a capsule.
        /// Position is the pixel-space center of the player.
        /// </summary>
        public static Body CreatePlayerBody(AetherWorld world, Vector2 pixelPosition)
        {
            var body = world.CreateBody(
                PhysicsManager.ToMeters(pixelPosition),
                0f,
                BodyType.Dynamic);

            body.FixedRotation = true;
            body.LinearDamping = 0f;
            // Enable Continuous Collision Detection so the player body cannot
            // tunnel through thin edge-chain terrain at high velocities
            // (slingshot launches, grapple releases, long falls).
            body.IsBullet = true;

            CreatePlayerFixtures(body, 36f, 60f); // 1.5× larger

            return body;
        }

        /// <summary>
        /// Create (or recreate) the main body fixture + foot sensor for the player,
        /// sized in pixel units. Used both at spawn and when the hitbox changes size
        /// (crouch). Foot sensor is tagged "foot" so it can be identified and re-wired.
        /// </summary>
        public static void CreatePlayerFixtures(Body body, float pixelWidth, float pixelHeight)
        {
            float halfW = PhysicsManager.ToMeters(pixelWidth  / 2f);
            float halfH = PhysicsManager.ToMeters(pixelHeight / 2f);

            // Main rectangle fixture — width/height are full extents
            var fixture = body.CreateRectangle(halfW * 2f, halfH * 2f, 1f, Vector2.Zero);
            fixture.CollisionCategories = CollisionCategories.Player;
            fixture.CollidesWith        = CollisionCategories.PlayerCollidesWith;

            // Foot sensor (slightly below body bottom, used for ground detection)
            var footOffset  = new Vector2(0f, halfH + PhysicsManager.ToMeters(3f));
            var footFixture = body.CreateRectangle(halfW * 1.6f, PhysicsManager.ToMeters(6f), 0f, footOffset);
            footFixture.IsSensor            = true;
            footFixture.CollisionCategories = CollisionCategories.Player;
            footFixture.CollidesWith        = CollisionCategories.Terrain |
                                              CollisionCategories.Platform |
                                              CollisionCategories.DisappearingPlatform;
            footFixture.Tag = "foot";
        }

        /// <summary>
        /// Destroy the player's current main fixture + foot sensor and rebuild them
        /// at the given pixel size. Other fixtures on the body (if any) are left alone.
        /// </summary>
        public static void ReplacePlayerFixtures(Body body, float pixelWidth, float pixelHeight)
        {
            // Snapshot the list because DestroyFixture mutates FixtureList
            var toRemove = new List<Fixture>();
            foreach (var f in body.FixtureList)
            {
                // Keep any fixtures that aren't part of the player's body/foot (paranoia
                // in case external systems ever attach extra fixtures). Our own fixtures
                // have either the "foot" tag (sensor) or no tag (main body rect).
                if (f.Tag is string tag)
                {
                    if (tag == "foot") toRemove.Add(f);
                    // unknown-tag fixtures are preserved
                }
                else
                {
                    toRemove.Add(f);
                }
            }
            foreach (var f in toRemove)
                body.Remove(f);

            CreatePlayerFixtures(body, pixelWidth, pixelHeight);
        }

        // ── Static terrain body from tile edges ────────────────────────────────

        /// <summary>
        /// Create a static edge-chain body from a list of pixel-space vertices.
        /// </summary>
        public static Body CreateTerrainChain(AetherWorld world, List<Vector2> pixelVertices)
        {
            var body = world.CreateBody(Vector2.Zero, 0f, BodyType.Static);

            var vertices = new Vertices(pixelVertices.Count);
            foreach (var v in pixelVertices)
                vertices.Add(PhysicsManager.ToMeters(v));

            var fixture = body.CreateChainShape(vertices);
            fixture.CollisionCategories = CollisionCategories.Terrain;
            fixture.CollidesWith        = Category.All;
            fixture.Friction            = 0.3f;
            fixture.Restitution         = 0f;

            return body;
        }

        // ── Static rectangle body (platforms, objects) ─────────────────────────

        /// <summary>
        /// Create a static rectangle body at the given pixel-space position and size.
        /// </summary>
        public static Body CreateStaticRect(AetherWorld world, Vector2 pixelCenter,
            float pixelWidth, float pixelHeight,
            Category category = CollisionCategories.Platform)
        {
            var body = world.CreateBody(
                PhysicsManager.ToMeters(pixelCenter), 0f, BodyType.Static);

            float w = PhysicsManager.ToMeters(pixelWidth);
            float h = PhysicsManager.ToMeters(pixelHeight);

            var fixture = body.CreateRectangle(w, h, 0f, Vector2.Zero);
            fixture.CollisionCategories = category;
            fixture.CollidesWith        = Category.All;
            fixture.Friction            = 0.2f;

            return body;
        }

        // ── Sensor trigger zone ────────────────────────────────────────────────

        /// <summary>
        /// Create a sensor-only rectangle body for trigger zones.
        /// </summary>
        public static Body CreateSensorRect(AetherWorld world, Vector2 pixelCenter,
            float pixelWidth, float pixelHeight)
        {
            var body = world.CreateBody(
                PhysicsManager.ToMeters(pixelCenter), 0f, BodyType.Static);

            float w = PhysicsManager.ToMeters(pixelWidth);
            float h = PhysicsManager.ToMeters(pixelHeight);

            var fixture = body.CreateRectangle(w, h, 0f, Vector2.Zero);
            fixture.IsSensor            = true;
            fixture.CollisionCategories = CollisionCategories.Trigger;
            fixture.CollidesWith        = CollisionCategories.TriggerCollidesWith;

            return body;
        }

        // ── Dynamic small body (grapple projectile) ────────────────────────────

        /// <summary>
        /// Create a small dynamic body for the grapple hook projectile.
        /// </summary>
        public static Body CreateGrappleBody(AetherWorld world, Vector2 pixelPosition)
        {
            var body = world.CreateBody(
                PhysicsManager.ToMeters(pixelPosition), 0f, BodyType.Dynamic);

            body.IsBullet = true;

            float r     = PhysicsManager.ToMeters(4f);
            var fixture = body.CreateCircle(r, 0.1f, Vector2.Zero);
            fixture.CollisionCategories = CollisionCategories.GrappleHook;
            fixture.CollidesWith        = CollisionCategories.GrappleCollidesWith;
            fixture.Restitution         = 0f;
            fixture.Friction            = 1f;

            return body;
        }

        // ── Dynamic stalactite body ────────────────────────────────────────────

        /// <summary>
        /// Create a small dynamic body for a falling stalactite.
        /// Uses a narrow rectangle matching the visual shape.
        /// </summary>
        public static Body CreateStalactiteBody(AetherWorld world, Vector2 pixelPosition)
        {
            var body = world.CreateBody(
                PhysicsManager.ToMeters(pixelPosition), 0f, BodyType.Dynamic);

            body.FixedRotation = true;

            float halfW = PhysicsManager.ToMeters(5f);  // 10px wide
            float halfH = PhysicsManager.ToMeters(10f); // 20px tall

            var fixture = body.CreateRectangle(halfW * 2f, halfH * 2f, 1f, Vector2.Zero);
            fixture.CollisionCategories = CollisionCategories.WorldObject;
            fixture.CollidesWith        = CollisionCategories.Terrain;
            fixture.Restitution         = 0.1f;
            fixture.Friction            = 0.5f;

            return body;
        }

        // ── Controllable entity body ───────────────────────────────────────────

        /// <summary>
        /// Create a dynamic body for a controllable cave entity.
        /// The main fixture uses the Entity collision category so it collides with
        /// terrain for movement/gravity. A separate sensor fixture (tagged "select")
        /// uses the Trigger category so the EntityControlSystem can detect player
        /// proximity for selection without physically blocking the player.
        /// </summary>
        /// <param name="world">The physics world.</param>
        /// <param name="pixelPosition">Spawn position in pixel space.</param>
        /// <param name="pixelWidth">Entity width in pixels.</param>
        /// <param name="pixelHeight">Entity height in pixels.</param>
        /// <param name="canFly">If true, body starts with IgnoreGravity = true.</param>
        public static Body CreateEntityBody(AetherWorld world, Vector2 pixelPosition,
            float pixelWidth, float pixelHeight, bool canFly = false)
        {
            var body = world.CreateBody(
                PhysicsManager.ToMeters(pixelPosition), 0f, BodyType.Dynamic);

            body.FixedRotation = true;
            body.IgnoreGravity = canFly;
            body.LinearDamping = canFly ? 2f : 0f;

            float w = PhysicsManager.ToMeters(pixelWidth);
            float h = PhysicsManager.ToMeters(pixelHeight);

            // Main physics fixture — collides with terrain so entity can stand/fly
            var mainFixture = body.CreateRectangle(w, h, 1f, Vector2.Zero);
            mainFixture.CollisionCategories = CollisionCategories.Entity;
            mainFixture.CollidesWith        = CollisionCategories.EntityCollidesWith;
            mainFixture.Friction            = 0.4f;
            mainFixture.Restitution         = 0f;

            // Selection sensor — slightly larger than the body, detects player proximity
            // for the EntityControlSystem range check. Does not block movement.
            float selW = PhysicsManager.ToMeters(pixelWidth  + 8f);
            float selH = PhysicsManager.ToMeters(pixelHeight + 8f);
            var selFixture = body.CreateRectangle(selW, selH, 0f, Vector2.Zero);
            selFixture.IsSensor            = true;
            selFixture.CollisionCategories = CollisionCategories.Entity;
            selFixture.CollidesWith        = CollisionCategories.Player;
            selFixture.Tag                 = "select";

            return body;
        }
    }
}
