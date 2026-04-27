using System;
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

        // ── Anchor surface type (for color-coding) ─────────────────────────────
        private nkast.Aether.Physics2D.Dynamics.Category _pendingAnchorSurface;

        // ── Swing arc tracking (for arc-scaled release) ────────────────────────
        private float _swingInitialAngle  = 0f;
        private float _swingMaxArcAngle   = 0f;
        // Phase 2.3: cumulative arc travelled across pendulum reversals.
        // Lets back-and-forth swings build into the release bonus, instead of
        // only counting the largest single-direction swing from initial angle.
        private float _swingPrevAngle       = 0f;
        private bool  _swingPrevAngleValid  = false;
        private float _swingCumulativeArc   = 0f;

        // ── Smooth wrap-length transitions (Phase 2.4) ─────────────────────────
        // The wrap system can change effective rope length frame-to-frame as the
        // rope wraps/unwraps around tile corners. Snapping the joint MaxLength to
        // the new value yanks the player; instead we lerp smoothly.
        private float _smoothedJointMaxMeters = 0f;
        private const float WrapLengthLerpRate = 18f; // continuous decay rate

        public bool IsFlying   => _isFlying;
        public bool IsAnchored => _isAnchored;
        public Vector2 AnchorPixelPos => _anchorPixelPos;

        // ── Impact feedback event ──────────────────────────────────────────────
        /// <summary>
        /// Fires when the hook anchor is finalized. Parameters: world-pixel position,
        /// surface color (terrain/climbable/crystal). Wire to Camera.Shake + particle burst.
        /// </summary>
        public event Action<Vector2, Color>? OnAnchored;

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

                    // Arc-scaled tangential boost: composes max-from-initial swing
                    // AND cumulative back-and-forth arc travel (Phase 2.3). Caps at 1.6×.
                    float maxArcFrac = _swingMaxArcAngle / (MathF.PI / 2f);
                    float cumulFrac  = _swingCumulativeArc / MathF.PI; // 1 full π of pendulum travel = max
                    float arcFraction = MathHelper.Clamp(MathF.Max(maxArcFrac, cumulFrac), 0f, 1f);
                    float tangBoost   = MathHelper.Lerp(1.0f, 1.6f, arcFraction);
                    _ownerPlayer.Body.LinearVelocity = radialVelocity + tangentialVelocity * tangBoost;
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

                // ── Phase 2.1: jerk-free anchor ────────────────────────────────
                // Compute rope length from the CURRENT player position with a tiny
                // slack epsilon. Any natural radial overshoot from the next physics
                // step is absorbed by the slack; the rope finds tautness on the next
                // pendulum swing rather than yanking the player on creation.
                const float SlackMeters = 0.06f; // ~4 px of slack
                float currentDistMeters = Vector2.Distance(
                    _pendingAnchorPos, _ownerPlayer.Body.Position);
                float ropeLength = MathHelper.Clamp(
                    currentDistMeters + SlackMeters,
                    PhysicsManager.ToMeters(MinRopeLength),
                    PhysicsManager.ToMeters(MaxRange));

                _currentRopeLengthPixels = PhysicsManager.ToPixels(ropeLength);
                _smoothedJointMaxMeters  = ropeLength;

                // RopeJoint constrains only the MAXIMUM distance (one-sided, like a real rope).
                _joint = new RopeJoint(
                    _anchorBody,
                    _ownerPlayer.Body,
                    Vector2.Zero,
                    Vector2.Zero,
                    false);
                _joint.MaxLength = ropeLength;
                _world.Add(_joint);

                // Phase 2.2: yank-feel impulse for low-velocity anchors.
                // If the player is barely moving relative to the rope tangent,
                // give them a small tangential nudge based on input direction so
                // the swing starts immediately. High-momentum swings are untouched.
                ApplyOptionalAnchorYank();

                // If the rope is meaningfully oblique (anchor is to the side, not
                // directly above), gravity has almost no tangential component and the
                // pendulum won't start on its own. Apply a tiny downward tangential
                // kick so the player drifts toward the bottom of the swing arc.
                {
                    Vector2 toAnchor = _pendingAnchorPos - _ownerPlayer.Body.Position;
                    if (toAnchor.LengthSquared() > 0.0001f)
                    {
                        Vector2 ropeDir = Vector2.Normalize(toAnchor);
                        // |ropeDir.X| ≈ sin(angle from vertical) — large when nearly horizontal
                        float obliqueness = MathF.Abs(ropeDir.X);
                        if (obliqueness > 0.25f) // rope is >15° from vertical
                        {
                            // Tangent perpendicular to rope, oriented toward the lower side
                            Vector2 tangent = new Vector2(-ropeDir.Y, ropeDir.X);
                            if (tangent.X < 0f) tangent = -tangent;
                            float kickMagnitude = obliqueness * 0.06f;
                            _ownerPlayer.Body.ApplyLinearImpulse(tangent * kickMagnitude);
                        }
                    }
                }

                // Destroy the hook projectile body — anchor body takes its place
                DestroyHookBody();

                // ── Grounded grapple fix: don't force Swinging when grounded ──
                // If the player is on the ground when the hook anchors, leave them
                // in their current state (Idle/Walking) so they can walk freely
                // within the rope radius. Swinging is only forced when airborne.
                if (!_ownerPlayer.IsGrounded)
                    _ownerPlayer.SetState(PlayerState.Swinging);

                // ── Swing arc tracking — reset on each new anchor ──────────────
                Vector2 toPlayer = _ownerPlayer.Body.Position - _pendingAnchorPos;
                _swingInitialAngle    = MathF.Atan2(toPlayer.Y, toPlayer.X);
                _swingMaxArcAngle     = 0f;
                _swingCumulativeArc   = 0f;
                _swingPrevAngle       = _swingInitialAngle;
                _swingPrevAngleValid  = true;

                // ── Impact feedback ────────────────────────────────────────────
                Color anchorColor = _pendingAnchorSurface == CollisionCategories.CrystalBridge
                    ? new Color(220, 120, 255)   // magenta for crystal
                    : _pendingAnchorSurface == CollisionCategories.Climbable
                        ? new Color(60, 220, 200) // teal for climbable
                        : new Color(255, 220, 80); // warm yellow for terrain
                OnAnchored?.Invoke(_anchorPixelPos, anchorColor);

                _isAnchored    = true;
                _pendingAnchor = false;
            }

            // ── Arc tracking: accumulate displacement AND cumulative travel ──
            // Phase 2.3: max-from-initial captures only the largest single swing;
            // cumulative arc rewards back-and-forth pendulum building too.
            if (_isAnchored && _ownerPlayer != null &&
                _ownerPlayer.State == PlayerState.Swinging)
            {
                Vector2 delta = _ownerPlayer.Body.Position - _pendingAnchorPos;
                if (delta.LengthSquared() > 0.0001f)
                {
                    float angle = MathF.Atan2(delta.Y, delta.X);
                    float diff  = MathF.Abs(WrapAngle(angle - _swingInitialAngle));
                    if (diff > _swingMaxArcAngle)
                        _swingMaxArcAngle = diff;

                    if (_swingPrevAngleValid)
                    {
                        float step = MathF.Abs(WrapAngle(angle - _swingPrevAngle));
                        // Ignore rotational noise; cap per-frame step to avoid spikes.
                        if (step < MathF.PI * 0.5f)
                            _swingCumulativeArc += step;
                    }
                    _swingPrevAngle      = angle;
                    _swingPrevAngleValid = true;
                }
            }
            else
            {
                _swingPrevAngleValid = false;
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

                // Phase 2.4: smooth the joint MaxLength across wrap/unwrap events.
                // Snapping it on a single frame (when a wrap point appears) yanks
                // the player; lerping over a couple of physics steps lets the
                // constraint settle without a visible jerk.
                float targetMeters = System.Math.Max(0.1f,
                    PhysicsManager.ToMeters(remainingLength));
                float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
                _smoothedJointMaxMeters = Smoothing.ExpDecay(
                    _smoothedJointMaxMeters, targetMeters, WrapLengthLerpRate, dt);
                _joint.MaxLength = _smoothedJointMaxMeters;

                // ── Grounded grapple fix: keep attached while grounded ────────────
                // The player can walk freely within the rope radius while grounded.
                // Only restore Swinging state if the player is airborne.
                // When grounded, leave the state as-is (Idle/Walking/Crouching) so
                // normal ground movement works, but keep the rope attached so it's
                // ready for the next swing.
                if (_ownerPlayer.IsGrounded)
                {
                    // Don't force Swinging state — let the player walk.
                    // The rope stays attached and will swing them when they jump/fall.
                }
                else if (_ownerPlayer.State != PlayerState.Swinging &&
                         _ownerPlayer.State != PlayerState.Stunned &&
                         _ownerPlayer.State != PlayerState.Dead &&
                         _ownerPlayer.State != PlayerState.Launching)
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
            _pendingAnchorSurface   = other.CollisionCategories;

            _pendingAnchor = true;

            return true;
        }

        /// <summary>Wrap an angle to [-π, π] for arc delta calculations.</summary>
        private static float WrapAngle(float angle)
        {
            while (angle > MathF.PI)  angle -= MathF.PI * 2f;
            while (angle < -MathF.PI) angle += MathF.PI * 2f;
            return angle;
        }

        /// <summary>
        /// Phase 2.2: optional gentle tangential impulse on anchor.
        /// If the player is barely moving along the swing tangent (e.g. anchored
        /// at near-zero velocity from a standing position), nudge them in the
        /// direction of horizontal input so the swing starts immediately.
        /// High-momentum anchors are untouched.
        /// </summary>
        private void ApplyOptionalAnchorYank()
        {
            if (_ownerPlayer == null) return;

            Vector2 toAnchor = _pendingAnchorPos - _ownerPlayer.Body.Position;
            if (toAnchor.LengthSquared() < 0.0001f) return;
            toAnchor.Normalize();

            // Tangent perpendicular to rope, pointing rightward by default.
            Vector2 tangent = new Vector2(-toAnchor.Y, toAnchor.X);
            if (tangent.X < 0f) tangent = -tangent;

            float tangentialSpeed = MathF.Abs(Vector2.Dot(_ownerPlayer.Body.LinearVelocity, tangent));
            // Threshold in m/s — below ~0.5 m/s tangential, give a nudge.
            const float MinTangentialSpeedMs = 0.5f;
            if (tangentialSpeed >= MinTangentialSpeedMs) return;

            // Use facing direction as the nudge sign (keyboard player has no
            // analog stick; using FacingDirection feels predictable).
            float sign = _ownerPlayer.FacingDirection >= 0 ? 1f : -1f;
            Vector2 nudge = tangent * sign * 1.5f; // m/s
            _ownerPlayer.Body.LinearVelocity += nudge;
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
