using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Joints;
using nkast.Aether.Physics2D.Dynamics.Contacts;
using Bloop.Core;
using Bloop.Physics;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Gameplay
{
    /// <summary>
    /// Manages the grappling hook mechanic.
    /// Fires a small projectile body toward the mouse cursor.
    /// On terrain contact, creates a RopeJoint at the anchor point so the
    /// player swings as a pendulum via Aether physics.
    /// Release launches the player with current velocity.
    ///
    /// Terrain collision is handled by RopeWrapSystem: the rope wraps around tile
    /// corners instead of phasing through solid geometry, preventing the player from
    /// swinging far beyond what the rope length should physically allow.
    ///
    /// When the hook anchors, the player stays in place — no yank impulse is applied.
    /// The RopeJoint constraint alone governs movement; gravity and player input
    /// drive the swing naturally.
    /// </summary>
    public class GrapplingHook
    {
        // ── Tuning ─────────────────────────────────────────────────────────────
        private const float ProjectileSpeed = 800f; // pixels/second
        private const float MaxRange        = 600f; // pixels — destroy if exceeded
        private const float HookRadius      = 4f;   // pixels

        // ── Rope length bounds (pixels) ────────────────────────────────────────
        private const float MinRopeLength = 20f;

        // ── Aim line tuning ────────────────────────────────────────────────────
        /// <summary>Number of dashes drawn in the aim line.</summary>
        private const int   AimLineDashes    = 10;
        /// <summary>Length of each dash segment in pixels.</summary>
        private const float AimLineDashLen   = 12f;
        /// <summary>Gap between dash segments in pixels.</summary>
        private const float AimLineGapLen    = 8f;
        /// <summary>Alpha of the aim line (subtle, not distracting).</summary>
        private const float AimLineAlpha     = 0.25f;

        // ── State ──────────────────────────────────────────────────────────────
        private Body?      _hookBody;
        private RopeJoint? _joint;
        private Body?      _anchorBody;
        private Vector2        _fireOriginPixels;
        private Vector2        _anchorPixelPos;   // stored for drawing after hook body is removed
        private float          _currentRopeLengthPixels;
        private bool           _isFlying;
        private bool           _isAnchored;
        private bool           _pendingAnchor;
        private Vector2        _pendingAnchorPos;    // in meter space
        private Vector2        _pendingPlayerPosMeters; // player position captured at collision time

        public bool IsFlying   => _isFlying;
        public bool IsAnchored => _isAnchored;
        public Vector2 AnchorPixelPos => _anchorPixelPos;

        // ── Rope wrap system ───────────────────────────────────────────────────
        private readonly RopeWrapSystem _wrapSystem;

        // ── References ─────────────────────────────────────────────────────────
        private readonly AetherWorld _world;
        private Player?              _ownerPlayer;

        // ── Mouse world position (for aim line) ────────────────────────────────
        private Vector2 _lastMouseWorldPos;

        public GrapplingHook(AetherWorld world)
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

        // ── Fire ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Fire the grapple hook toward the given world-space pixel target.
        /// </summary>
        public void TryFire(Player player, Vector2 targetPixelPos)
        {
            if (_isFlying || _isAnchored) return;

            _ownerPlayer      = player;
            _fireOriginPixels = player.PixelPosition;
            _lastMouseWorldPos = targetPixelPos;

            Vector2 dir = targetPixelPos - _fireOriginPixels;
            if (dir == Vector2.Zero) return;
            dir.Normalize();

            // 2.4 — chain bonus: hook travels 30% faster when fired during Launching
            float speed = player.LaunchBoostActive
                ? ProjectileSpeed * ChainFireSpeedBonus
                : ProjectileSpeed;

            _hookBody = BodyFactory.CreateGrappleBody(_world, _fireOriginPixels);
            _hookBody.LinearVelocity = PhysicsManager.ToMeters(dir * speed);
            _hookBody.Tag            = this; // back-reference

            // Wire collision callback
            foreach (var fixture in _hookBody.FixtureList)
                fixture.OnCollision += OnHookCollision;

            _isFlying = true;
        }

        // ── Release ────────────────────────────────────────────────────────────

        /// <summary>
        /// Release the grapple. If the player was swinging, applies a momentum boost
        /// biased toward the tangential (swing) direction and enters the Launching
        /// state (reduced gravity for 0.3s) — 2.4.
        /// </summary>
        public void Release()
        {
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
            DestroyHookBody();

            bool wasSwinging = _ownerPlayer?.State == PlayerState.Swinging;

            // ── Momentum-aware release ─────────────────────────────────────────
            // Boost only the tangential component of velocity (perpendicular to rope).
            // The radial component (toward/away from anchor) is left unchanged.
            // This feels more natural than a flat 1.2× multiplier on both axes.
            if (wasSwinging && _ownerPlayer != null)
            {
                Vector2 toAnchorMeters = PhysicsManager.ToMeters(_anchorPixelPos) - _ownerPlayer.Body.Position;
                if (toAnchorMeters.LengthSquared() > 0.0001f)
                {
                    Vector2 radial = toAnchorMeters;
                    radial.Normalize();

                    Vector2 vel = _ownerPlayer.Body.LinearVelocity;
                    float   radialComponent     = Vector2.Dot(vel, radial);
                    Vector2 radialVelocity      = radial * radialComponent;
                    Vector2 tangentialVelocity  = vel - radialVelocity;

                    // Boost tangential component by 1.3×, leave radial unchanged
                    _ownerPlayer.Body.LinearVelocity = radialVelocity + tangentialVelocity * 1.3f;
                }
            }

            _isAnchored    = false;
            _isFlying      = false;
            _pendingAnchor = false;

            if (_ownerPlayer != null)
            {
                if (wasSwinging)
                    _ownerPlayer.StartLaunch(); // 2.4 — momentum boost + reduced gravity
                else if (_ownerPlayer.State == PlayerState.Swinging)
                    _ownerPlayer.SetState(PlayerState.Falling);
            }
        }

        // ── Chain-fire speed bonus (2.4) ───────────────────────────────────────

        /// <summary>
        /// Speed multiplier applied to the hook projectile when fired during a
        /// Launching state (chain bonus: 30% faster hook travel).
        /// </summary>
        public const float ChainFireSpeedBonus = 1.30f;

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Check if the hook has exceeded max range while flying, finalize
        /// pending anchor after World.Step() has completed, and update wrap points.
        /// Call once per frame.
        /// </summary>
        public void Update(GameTime gameTime)
        {
            // Finalize pending anchor now that World.Step() has completed
            if (_pendingAnchor && _ownerPlayer != null)
            {
                // Create a static anchor body at the contact point (meter space)
                _anchorBody     = _world.CreateBody(_pendingAnchorPos, 0f, BodyType.Static);
                _anchorPixelPos = PhysicsManager.ToPixels(_pendingAnchorPos);

                // Calculate rope length using the player position captured at collision time,
                // not now — the player continues to fall during the remaining physics sub-steps
                // between OnHookCollision and this Update(), which would produce a too-long rope.
                float ropeLength = Vector2.Distance(_pendingAnchorPos, _pendingPlayerPosMeters);
                ropeLength = MathHelper.Clamp(ropeLength,
                    PhysicsManager.ToMeters(MinRopeLength),
                    PhysicsManager.ToMeters(MaxRange));

                _currentRopeLengthPixels = PhysicsManager.ToPixels(ropeLength);

                // RopeJoint constrains only the MAXIMUM distance (one-sided, like a real rope).
                // A DistanceJoint would force the exact length, trapping the player against
                // the ground and fighting any reel-in attempt.
                _joint = new RopeJoint(
                    _anchorBody,
                    _ownerPlayer.Body,
                    Vector2.Zero,
                    Vector2.Zero,
                    false);
                _joint.MaxLength = ropeLength;
                _world.Add(_joint);

                // Destroy the hook projectile body — anchor body takes its place
                DestroyHookBody();

                _ownerPlayer.SetState(PlayerState.Swinging);

                // NOTE: No yank impulse applied here.
                // The player stays where they are when the hook anchors.
                // Gravity and player input drive the swing naturally.

                _isAnchored    = true;
                _pendingAnchor = false;
            }

            // Update wrap system while anchored
            if (_isAnchored && _ownerPlayer != null && _joint != null)
            {
                float remainingLength = _currentRopeLengthPixels;
                _wrapSystem.Update(
                    _anchorPixelPos,
                    _ownerPlayer.PixelPosition,
                    _ownerPlayer.Body,
                    ref remainingLength);

                // Update the primary joint's max length to the remaining (unwrapped) length
                _joint.MaxLength = System.Math.Max(0.1f,
                    PhysicsManager.ToMeters(remainingLength));

                // If external code moved the player out of Swinging, restore it so rope
                // climbing and swing controls keep working.
                // Exceptions: don't restore if the player is grounded (they landed while
                // anchored — let Idle/Walking stand), Stunned, Dead, or in Launching.
                if (_ownerPlayer.State != PlayerState.Swinging &&
                    _ownerPlayer.State != PlayerState.Stunned &&
                    _ownerPlayer.State != PlayerState.Dead &&
                    _ownerPlayer.State != PlayerState.Launching &&
                    !(_ownerPlayer.IsGrounded &&
                      (_ownerPlayer.State == PlayerState.Idle ||
                       _ownerPlayer.State == PlayerState.Walking ||
                       _ownerPlayer.State == PlayerState.Crouching)))
                {
                    _ownerPlayer.SetState(PlayerState.Swinging);
                }
            }

            if (!_isFlying || _hookBody == null) return;

            Vector2 hookPixelPos = PhysicsManager.ToPixels(_hookBody.Position);
            float   dist         = Vector2.Distance(_fireOriginPixels, hookPixelPos);

            if (dist > MaxRange)
                DestroyHookBody();
        }

        // ── Update mouse position (for aim line) ───────────────────────────────

        /// <summary>
        /// Update the stored mouse world position for aim line rendering.
        /// Call each frame from GameplayScreen before Draw().
        /// </summary>
        public void UpdateAimTarget(Vector2 mouseWorldPos)
        {
            _lastMouseWorldPos = mouseWorldPos;
        }

        // ── Climb the rope ─────────────────────────────────────────────────────

        /// <summary>
        /// Shorten (negative delta) or extend (positive delta) the rope, in pixels.
        /// Shortening pulls the player toward the anchor; extending lets them descend.
        /// Clamped to [MinRopeLength, MaxRange].
        /// </summary>
        public void AdjustLength(float deltaPixels)
        {
            if (_joint == null || !_isAnchored) return;

            _currentRopeLengthPixels = MathHelper.Clamp(
                _currentRopeLengthPixels + deltaPixels,
                MinRopeLength,
                MaxRange);

            // The joint MaxLength is updated in Update() via the wrap system
            // but also set directly here for immediate response
            _joint.MaxLength = MathHelper.Clamp(
                _joint.MaxLength + PhysicsManager.ToMeters(deltaPixels),
                PhysicsManager.ToMeters(MinRopeLength),
                PhysicsManager.ToMeters(MaxRange));
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        /// <summary>Draw the hook line and projectile dot, with wrap-point polyline.
        /// Also draws a faint aim line when the hook is ready to fire.</summary>
        public void Draw(SpriteBatch spriteBatch, AssetManager assets, Player player)
        {
            Vector2 playerPos = player.PixelPosition;
            var     lineColor = new Color(200, 200, 100);

            if (_isFlying && _hookBody != null)
            {
                // Hook is in flight — draw straight line to projectile position
                Vector2 hookPos = PhysicsManager.ToPixels(_hookBody.Position);
                DrawLine(spriteBatch, assets, playerPos, hookPos, lineColor);

                // Hook dot
                assets.DrawRect(spriteBatch,
                    new Rectangle((int)hookPos.X - 4, (int)hookPos.Y - 4, 8, 8),
                    new Color(255, 220, 60));
            }
            else if (_isAnchored)
            {
                // Hook is anchored — draw rope as polyline through wrap points
                _wrapSystem.Draw(spriteBatch, assets, _anchorPixelPos, playerPos, lineColor);

                // Anchor dot
                assets.DrawRect(spriteBatch,
                    new Rectangle((int)_anchorPixelPos.X - 4, (int)_anchorPixelPos.Y - 4, 8, 8),
                    new Color(255, 220, 60));
            }
            else
            {
                // Hook is ready — draw faint dashed aim line toward mouse cursor
                DrawAimLine(spriteBatch, assets, playerPos);
            }
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Draw a faint dashed line from the player toward the mouse cursor,
        /// capped at MaxRange. Helps the player aim in dark environments.
        /// </summary>
        private void DrawAimLine(SpriteBatch spriteBatch, AssetManager assets, Vector2 playerPos)
        {
            Vector2 dir = _lastMouseWorldPos - playerPos;
            float   len = dir.Length();
            if (len < 1f) return;

            dir /= len;
            float maxLen = System.Math.Min(len, MaxRange);

            var aimColor = new Color((byte)200, (byte)200, (byte)100, (byte)(255 * AimLineAlpha));
            float dashStep = AimLineDashLen + AimLineGapLen;
            float traveled = 0f;

            while (traveled < maxLen)
            {
                float dashEnd = System.Math.Min(traveled + AimLineDashLen, maxLen);
                Vector2 a = playerPos + dir * traveled;
                Vector2 b = playerPos + dir * dashEnd;
                DrawLine(spriteBatch, assets, a, b, aimColor);
                traveled += dashStep;
            }
        }

        private void DrawLine(SpriteBatch sb, AssetManager assets, Vector2 a, Vector2 b, Color color)
        {
            Vector2 diff   = b - a;
            float   length = diff.Length();
            if (length < 1f) return;
            float angle = (float)System.Math.Atan2(diff.Y, diff.X);
            sb.Draw(assets.Pixel,
                new Rectangle((int)a.X, (int)a.Y, (int)length, 2),
                null, color, angle, Vector2.Zero, SpriteEffects.None, 0f);
        }

        private bool OnHookCollision(Fixture sender, Fixture other, Contact contact)
        {
            if (!_isFlying) return false;
            if (_ownerPlayer == null) return false;

            // Only anchor to terrain, climbable surfaces, and crystal bridge segments
            if (other.CollisionCategories != Bloop.Physics.CollisionCategories.Terrain &&
                other.CollisionCategories != Bloop.Physics.CollisionCategories.Climbable &&
                other.CollisionCategories != Bloop.Physics.CollisionCategories.CrystalBridge)
                return true;

            // Stop the hook; defer body/joint creation until after World.Step() completes
            if (_hookBody != null)
                _hookBody.LinearVelocity = Vector2.Zero;

            _isFlying = false;

            // Use the contact point from the manifold for accurate anchor placement
            // rather than the hook body center (which may have moved during solver).
            // FixedArray2<Vector2> always has 2 slots; the live count is in Manifold.PointCount.
            contact.GetWorldManifold(out _, out var contactPoints);
            if (contact.Manifold.PointCount == 0)
            {
                // Manifold has no points — fall back to hook body center
                _pendingAnchorPos = _hookBody!.Position;
            }
            else
            {
                _pendingAnchorPos = contactPoints[0]; // meter space — first contact point
            }
            _pendingPlayerPosMeters = _ownerPlayer.Body.Position; // capture now, before player falls further

            _pendingAnchor = true;

            return true;
        }

        /// <summary>Destroy only the hook projectile body (not the anchor).</summary>
        private void DestroyHookBody()
        {
            if (_hookBody != null)
            {
                _world.Remove(_hookBody);
                _hookBody = null;
            }
            _isFlying = false;
        }
    }
}
