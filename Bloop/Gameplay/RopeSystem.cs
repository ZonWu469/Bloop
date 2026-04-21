using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Joints;
using Bloop.Core;
using Bloop.Physics;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Gameplay
{
    /// <summary>
    /// Manages the rappel rope mechanic.
    /// When activated, attaches a RopeJoint (max-length constraint) from the player
    /// body to a ceiling anchor point directly above. The player can extend the rope
    /// to descend. Retraction speed is reduced by backpack weight.
    ///
    /// Terrain collision is handled by RopeWrapSystem: the rope wraps around tile
    /// corners instead of phasing through solid geometry, preventing the player from
    /// swinging far beyond what the rope length should physically allow.
    /// </summary>
    public class RopeSystem
    {
        // ── Tuning ─────────────────────────────────────────────────────────────
        private const float MaxRopeLength   = 400f; // pixels
        private const float ExtendSpeed     = 120f; // pixels/second
        private const float RetractSpeed    = 80f;  // pixels/second (base, reduced by weight)
        private const float RopeRaycastDist = 500f; // how far up to look for ceiling

        // ── State ──────────────────────────────────────────────────────────────
        private RopeJoint? _joint;
        private Body?      _anchorBody;
        private Vector2    _anchorPixelPos;
        private float      _currentLengthPixels;
        private bool       _isAttached;

        public bool IsAttached => _isAttached;

        // ── Rope wrap system ───────────────────────────────────────────────────
        private readonly RopeWrapSystem _wrapSystem;

        // ── Reference to physics world ─────────────────────────────────────────
        private readonly AetherWorld _world;

        public RopeSystem(AetherWorld world)
        {
            _world      = world;
            _wrapSystem = new RopeWrapSystem(world);
        }

        // ── TileMap injection ──────────────────────────────────────────────────

        /// <summary>
        /// Set the tile map for terrain-aware rope wrapping.
        /// Call after level generation, before the first frame.
        /// </summary>
        public void SetTileMap(TileMap tileMap)
        {
            _wrapSystem.SetTileMap(tileMap);
        }

        // ── Attach ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Try to attach the rope to the ceiling above the player.
        /// Uses a simple upward raycast to find a valid anchor point.
        /// </summary>
        public bool TryAttach(Player player)
        {
            if (_isAttached) return true;

            // Raycast straight up from player position
            Vector2 playerMeterPos = player.Body.Position;
            Vector2 rayStart       = playerMeterPos;
            Vector2 rayEnd         = playerMeterPos - new Vector2(0f, PhysicsManager.ToMeters(RopeRaycastDist));

            Vector2 hitPoint     = Vector2.Zero;
            bool    hitSomething = false;

            _world.RayCast((fixture, point, normal, fraction) =>
            {
                // Only attach to terrain
                if (fixture.CollisionCategories == Bloop.Physics.CollisionCategories.Terrain ||
                    fixture.CollisionCategories == Bloop.Physics.CollisionCategories.Platform)
                {
                    hitPoint     = point;
                    hitSomething = true;
                    return fraction; // stop at first hit
                }
                return -1f; // ignore this fixture
            }, rayStart, rayEnd);

            if (!hitSomething) return false;

            // Create a static anchor body at the hit point
            _anchorBody     = _world.CreateBody(hitPoint, 0f, BodyType.Static);
            _anchorPixelPos = PhysicsManager.ToPixels(hitPoint);

            // Initial rope length = distance from player to anchor
            float distMeters = Vector2.Distance(player.Body.Position, hitPoint);
            _currentLengthPixels = PhysicsManager.ToPixels(distMeters);

            // Create RopeJoint (max-length constraint — rope can go slack but not stretch)
            // This is more physically correct than DistanceJoint (exact length) because
            // it allows the player to swing freely when below the anchor.
            _joint = new RopeJoint(
                _anchorBody,
                player.Body,
                Vector2.Zero,
                Vector2.Zero,
                false);
            _joint.MaxLength = distMeters;
            _world.Add(_joint);

            _isAttached = true;
            player.SetState(PlayerState.Rappelling);
            return true;
        }

        // ── Detach ─────────────────────────────────────────────────────────────

        /// <summary>Release the rope and remove the joint and all wrap points.</summary>
        public void Detach()
        {
            if (!_isAttached) return;

            // Clear all wrap points first
            _wrapSystem.Clear();

            if (_joint != null)
            {
                _world.Remove(_joint);
                _joint = null;
            }
            if (_anchorBody != null)
            {
                _world.Remove(_anchorBody);
                _anchorBody = null;
            }

            _isAttached = false;
        }

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Handle rope extend/retract input while rappelling.
        /// Also updates wrap points to prevent rope from passing through terrain.
        /// Called by PlayerController when state is Rappelling.
        /// </summary>
        public void Update(GameTime gameTime, Player player, InputManager input)
        {
            if (!_isAttached || _joint == null) return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Weight penalty: heavier pack = slower retraction
            float weightPenalty = 1f - MathHelper.Clamp(player.InventoryWeightKg / 50f, 0f, 0.6f);
            float retractActual = RetractSpeed * weightPenalty;

            // Down key: extend rope (descend)
            if (input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Down) ||
                input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.S))
            {
                _currentLengthPixels += ExtendSpeed * dt;
                _currentLengthPixels  = System.Math.Min(_currentLengthPixels, MaxRopeLength);
            }
            // Up key: retract rope (ascend)
            else if (input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Up) ||
                     input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.W))
            {
                _currentLengthPixels -= retractActual * dt;
                _currentLengthPixels  = System.Math.Max(_currentLengthPixels, 10f);
            }

            // Update wrap system — this may add/remove wrap points based on terrain
            float remainingLength = _currentLengthPixels;
            Vector2 effectiveAnchorPixels = _wrapSystem.Update(
                _anchorPixelPos,
                player.PixelPosition,
                player.Body,
                ref remainingLength);

            // Update the primary joint's max length to the remaining (unwrapped) length
            _joint.MaxLength = System.Math.Max(0.1f, PhysicsManager.ToMeters(remainingLength));

            // Auto-detach if player reaches ground
            if (player.IsGrounded)
                Detach();
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw the rope as a polyline from anchor through all wrap points to player.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, AssetManager assets, Player player)
        {
            if (!_isAttached) return;

            var ropeColor = new Color(180, 140, 80);
            _wrapSystem.Draw(spriteBatch, assets, _anchorPixelPos, player.PixelPosition, ropeColor);
        }
    }
}
