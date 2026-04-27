using System;
using Microsoft.Xna.Framework;
using Bloop.Core;
using Bloop.Physics;
using Bloop.World;

namespace Bloop.Gameplay
{
    /// <summary>
    /// Maps InputManager actions to Aether physics forces/impulses on the Player body.
    /// Handles movement, jumping, climbing, sliding detection, and state transitions.
    /// Works in conjunction with RopeSystem, GrapplingHook, and MomentumSystem.
    ///
    /// Wall detection is done via per-frame raycasts (not collision callbacks) to avoid
    /// counter drift when the player body is teleported or contacts change rapidly.
    /// </summary>
    public class PlayerController
    {
        // ── Tuning constants ───────────────────────────────────────────────────
        // All movement tuning lives in MovementTuning.cs — single source of truth.
        // Local aliases are kept short for readability at call sites.
        private const float MoveForce              = MovementTuning.MoveForce;
        private const float MaxHorizontalSpeed     = MovementTuning.MaxHorizontalSpeed;
        private const float MaxHorizontalSpeedHard = MovementTuning.MaxHorizontalSpeedHard;
        private const float MaxFallSpeedHard       = MovementTuning.MaxFallSpeedHard;
        private const float JumpImpulse            = MovementTuning.JumpImpulse;
        private const float ClimbSpeed             = MovementTuning.ClimbSpeed;
        private const float SlideAngleThreshold    = MovementTuning.SlideAngleThreshold;
        private const float AirControlMultiplier   = MovementTuning.AirControlMultiplier;
        private const float CrouchSpeedMultiplier  = MovementTuning.CrouchSpeedMultiplier;

        private const float WallJumpVertical    = MovementTuning.WallJumpVertical;
        private const float WallJumpHorizontal  = MovementTuning.WallJumpHorizontal;
        private const float WallJumpCooldown    = MovementTuning.WallJumpCooldown;

        private const float MantleHeadTolerance = MovementTuning.MantleHeadTolerance;
        private const float CoyoteTimeDuration  = MovementTuning.CoyoteTimeDuration;
        private const float JumpBufferDuration  = MovementTuning.JumpBufferDuration;
        private const float WallRayLength       = MovementTuning.WallRayLength;

        // ── References ─────────────────────────────────────────────────────────
        private readonly Player        _player;
        private readonly InputManager  _input;
        private readonly RopeSystem    _rope;
        private readonly GrapplingHook _grapple;
        private readonly MomentumSystem _momentum;
        private readonly Camera        _camera;

        // ── Wall jump state ────────────────────────────────────────────────────
        private float _wallJumpCooldownTimer = 0f;

        // ── Wall detection hysteresis (symmetric: requires sustained contact AND release) ──
        private bool _prevTouchingWallLeft  = false;
        private bool _prevTouchingWallRight = false;
        /// <summary>Time (s) of continuous left-wall raw contact — must exceed WallContactRequired to report.</summary>
        private float _leftContactTimer  = 0f;
        /// <summary>Time (s) of continuous right-wall raw contact — must exceed WallContactRequired to report.</summary>
        private float _rightContactTimer = 0f;
        /// <summary>Time (s) since last left-wall raw contact — must exceed WallReleaseGrace to drop reporting.</summary>
        private float _leftReleaseTimer  = 999f;
        /// <summary>Time (s) since last right-wall raw contact — must exceed WallReleaseGrace to drop reporting.</summary>
        private float _rightReleaseTimer = 999f;

        private const float WallContactRequired = MovementTuning.WallContactRequired;
        private const float WallReleaseGrace    = MovementTuning.WallReleaseGrace;

        // ── Wall-cling coyote time (allows wall-jump briefly after releasing cling) ─
        private float _wallClingCoyoteTimer = 0f;
        private const float WallClingCoyoteDuration = MovementTuning.WallClingCoyoteDuration;
        /// <summary>Which wall was last clung (true = right wall).</summary>
        private bool _lastClingWasRightWall = false;
        /// <summary>Player position when wall-cling coyote window opened — invalidate if player drifts too far.</summary>
        private Vector2 _wallClingCoyoteOriginPx = Vector2.Zero;

        // ── Wall slide / cling state ───────────────────────────────────────────
        private float _wallSlideTimer = 0f;
        private float _wallSlideDamping = 0f;
        private const float WallSlideMaxDamping        = MovementTuning.WallSlideMaxDamping;
        private const float WallSlideRampTime          = MovementTuning.WallSlideRampTime;
        private const float WallClingVelocityThreshold = MovementTuning.WallClingVelocityThreshold;
        private const float WallClingTimerThreshold    = MovementTuning.WallClingTimerThreshold;
        private const float WallClimbSpeed             = MovementTuning.WallClimbSpeed;

        // ── Coyote time state ──────────────────────────────────────────────────
        private float _coyoteTimer = 0f;
        private bool _wasGroundedLastFrame = false;

        // ── Jump buffer state ──────────────────────────────────────────────────
        private float _jumpBufferTimer = 0f;
        /// <summary>True if the buffered jump was set while a ground/coyote/wall jump was actually possible.
        /// Prevents a stale buffered press from firing after coyote expired without landing.</summary>
        private bool _jumpBufferGroundEligible = false;


        // ── Tile map reference (for ledge detection and wall raycasts) ─────────
        private TileMap? _tileMap;

        // ── Flare throw stance ────────────────────────────────────────────────
        private const float FlareThrowSpeed = 420f;
        private const int   FlareArcSteps  = 18;
        private const float FlareArcSimDt  = 0.05f;

        /// <summary>Precomputed trajectory arc for the flare aim indicator. Null when not aiming.</summary>
        public Vector2[]? FlareTrajectoryPoints { get; private set; }

        /// <summary>Invoked when a flare is thrown. Parameters: spawn position, launch velocity (both in pixels).</summary>
        public Action<Vector2, Vector2>? OnFlareThrown { get; set; }

        // ── Constructor ────────────────────────────────────────────────────────
        public PlayerController(Player player, InputManager input,
            RopeSystem rope, GrapplingHook grapple, MomentumSystem momentum,
            Camera camera)
        {
            _player   = player;
            _input    = input;
            _rope     = rope;
            _grapple  = grapple;
            _momentum = momentum;
            _camera   = camera;
        }

        /// <summary>
        /// Provide the tile map for ledge-grab detection and wall raycasts.
        /// Call after construction, before the first Update().
        /// </summary>
        public void SetTileMap(TileMap tileMap) => _tileMap = tileMap;

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Process input and apply physics forces for this frame.
        /// Call after Player.Update() each frame.
        /// </summary>
        public void Update(GameTime gameTime)
        {
            // Dead, stunned, mantling, or wall-clinging (handled below) — no normal input
            if (_player.State == PlayerState.Dead    ||
                _player.State == PlayerState.Stunned ||
                _player.State == PlayerState.Mantling)
                return;

            // Controlling state — player is frozen while possessing an entity.
            // EntityControlSystem handles all input routing in this state.
            if (_player.State == PlayerState.Controlling)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // ── Per-frame wall detection via raycasts ──────────────────────────
            UpdateWallDetection(dt);

            // ── Wall slide → cling transition ──────────────────────────────────
            // Phase 1.4: ramp resets whenever physical wall contact is lost (not just
            // on state exit) so brief separations don't accumulate stale damping.
            if (_player.State == PlayerState.Falling && _player.IsTouchingWall)
            {
                float horiz = _input.GetHorizontalAxis();
                bool pressingTowardWall = (_player.IsTouchingWallLeft  && horiz < 0f)
                                       || (_player.IsTouchingWallRight && horiz > 0f);
                if (pressingTowardWall)
                {
                    _wallSlideTimer += dt;
                    _wallSlideDamping = MathHelper.Lerp(0f, WallSlideMaxDamping,
                        MathHelper.Clamp(_wallSlideTimer / WallSlideRampTime, 0f, 1f));
                    _player.Body.LinearDamping = _wallSlideDamping;

                    float pushDir = _player.IsTouchingWallLeft ? -1f : 1f;
                    _player.Body.ApplyForce(PhysicsManager.ToMeters(
                        new Vector2(pushDir * MoveForce * 0.15f, 0f)));

                    float fallSpeed = Math.Abs(PhysicsManager.ToPixels(_player.Body.LinearVelocity.Y));
                    if (fallSpeed < WallClingVelocityThreshold || _wallSlideTimer > WallClingTimerThreshold)
                    {
                        _player.SetState(PlayerState.WallClinging);
                        _wallSlideTimer   = 0f;
                        _wallSlideDamping = 0f;
                    }
                }
                else
                {
                    _wallSlideTimer   = 0f;
                    _wallSlideDamping = 0f;
                    _player.Body.LinearDamping = 0f;
                }
            }
            else if (_player.State != PlayerState.WallClinging)
            {
                // Lost wall contact entirely — reset slide ramp and clear damping
                // so the next wall touch starts from zero.
                if (_wallSlideTimer > 0f || _wallSlideDamping > 0f)
                {
                    _wallSlideTimer   = 0f;
                    _wallSlideDamping = 0f;
                    _player.Body.LinearDamping = 0f;
                }
            }

            // ── Bug #4 fix: climb entry detection ──────────────────────────────
            // If the player is falling and touching a Climbable surface, pressing C
            // transitions directly to Climbing state (no need to wall-slide first).
            if (_tileMap != null && _player.State == PlayerState.Falling && _input.IsClimbHeld())
            {
                if (_player.IsTouchingWall)
                {
                    // Check if the wall tile is actually climbable
                    Vector2 pos = _player.PixelPosition;
                    int ts = TileMap.TileSize;
                    int tx = _player.IsTouchingWallLeft
                        ? (int)((pos.X - Player.WidthPx / 2f - 2f) / ts)
                        : (int)((pos.X + Player.WidthPx / 2f + 2f) / ts);
                    int ty = (int)(pos.Y / ts);

                    if (tx >= 0 && tx < _tileMap.Width && ty >= 0 && ty < _tileMap.Height)
                    {
                        TileType tile = _tileMap.GetTile(tx, ty);
                        if (TileProperties.IsClimbable(tile) || TileProperties.IsSolid(tile))
                        {
                            _player.Body.IgnoreGravity = false;
                            _player.Body.LinearDamping = 8f;
                            _player.Body.LinearVelocity = new Vector2(
                                _player.Body.LinearVelocity.X * 0.3f, 0f);
                            _player.SetState(PlayerState.Climbing);
                            _wallSlideTimer = 0f;
                            _wallSlideDamping = 0f;
                        }
                    }
                }
            }

            // ── Wall cling controls ────────────────────────────────────────────
            if (_player.State == PlayerState.WallClinging)
            {
                // Allow grapple fire/release while wall-clinging
                if (_input.CurrentMode == Bloop.Core.InputMode.Normal)
                {
                    if (_input.IsLeftClickPressed())
                    {
                        Vector2 ms = _input.GetMouseWorldPosition();
                        Vector2 mw = _camera.ScreenToWorld(ms);
                        _grapple.TryFire(_player, mw);
                    }
                    if (_input.IsRightClickPressed() && (_grapple.IsFlying || _grapple.IsAnchored))
                        _grapple.Release();
                }

                float horiz = _input.GetHorizontalAxis();
                bool pressingTowardWall = (_player.IsTouchingWallLeft  && horiz < 0f)
                                       || (_player.IsTouchingWallRight && horiz > 0f);

                if (_input.IsJumpPressed())
                {
                    // Wall jump — reuse existing wall jump logic by falling through
                    // to the jump section below. First restore normal physics.
                    _player.Body.IgnoreGravity = false;
                    _player.Body.LinearDamping = 0f;
                    _player.SetState(PlayerState.Falling); // will be overridden by wall jump below
                    // Fall through to jump handling
                }
                else if (_input.IsCrouchHeld() || (!pressingTowardWall && horiz != 0f))
                {
                    // Down or pressing away: release cling — start coyote window for wall jump.
                    // Snapshot position so we can invalidate the coyote if the player drifts
                    // to a different wall (Phase 1.1 — fixes wrong-direction kick at coyote edge).
                    _wallClingCoyoteTimer    = WallClingCoyoteDuration;
                    _lastClingWasRightWall   = _player.IsTouchingWallRight;
                    _wallClingCoyoteOriginPx = _player.PixelPosition;
                    _player.Body.IgnoreGravity = false;
                    _player.Body.LinearDamping = 0f;
                    _player.SetState(PlayerState.Falling);
                    return;
                }
                else if (_input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.W) ||
                         _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Up))
                {
                    // Slow climb upward while clinging
                    _player.Body.IgnoreGravity = false;
                    _player.Body.LinearDamping = 8f;
                    _player.Body.LinearVelocity = PhysicsManager.ToMeters(new Vector2(0f, -WallClimbSpeed));
                    // Mantle over the top when head meets a solid floor tile above
                    CheckLedgeGrabFromCling();
                    // Nudge around protruding tile corners while climbing upward
                    ApplyClimbingCornerCorrection();
                    // Also nudge around ceiling-edge corners (asymmetric shoulder overlap)
                    ApplyCornerCorrection();
                    return;
                }
                // ── Bug #5 fix: climb down while wall-clinging ─────────────────
                else if (_input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.S) ||
                         _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Down))
                {
                    // Climb downward while clinging
                    _player.Body.IgnoreGravity = false;
                    _player.Body.LinearDamping = 8f;
                    _player.Body.LinearVelocity = PhysicsManager.ToMeters(new Vector2(0f, WallClimbSpeed * 0.8f));
                    return;
                }
                else
                {
                    // Holding cling — stay frozen
                    return;
                }
            }

            // ── Launching state (2.4) — allow grapple re-fire, then fall through ─
            // The Launching state is handled in Player.Update() for gravity reduction.
            // Here we only need to allow grapple firing and skip rappel/climb logic.
            if (_player.State == PlayerState.Launching)
            {
                // Only fire grapple in Normal input mode
                if (_input.CurrentMode == Bloop.Core.InputMode.Normal)
                {
                    if (_input.IsLeftClickPressed())
                    {
                        Vector2 mouseScreen = _input.GetMouseWorldPosition();
                        Vector2 mouseWorld  = _camera.ScreenToWorld(mouseScreen);
                        _grapple.TryFire(_player, mouseWorld); // chain bonus applied inside TryFire
                    }
                    if (_input.IsRightClickPressed() && (_grapple.IsFlying || _grapple.IsAnchored))
                        _grapple.Release();
                }
                // Allow horizontal air-steering during launch
                float launchHoriz = _input.GetHorizontalAxis();
                if (launchHoriz != 0f)
                {
                    _player.FacingDirection = launchHoriz > 0 ? 1 : -1;
                    float currentVx = PhysicsManager.ToPixels(_player.Body.LinearVelocity.X);
                    if (System.Math.Abs(currentVx) < MaxHorizontalSpeed * 1.5f) // wider cap during launch
                        _player.Body.ApplyForce(PhysicsManager.ToMeters(new Vector2(launchHoriz * MoveForce * 0.5f, 0f)));
                }
                return;
            }

            // ── Flare throw stance ─────────────────────────────────────────────
            // Enter stance on F press when flares are available
            if (_input.IsThrowFlarePressed() && _player.Stats.HasFlares)
            {
                var s = _player.State;
                if (s != PlayerState.Dead     && s != PlayerState.Stunned    &&
                    s != PlayerState.Mantling && s != PlayerState.Rappelling &&
                    s != PlayerState.Climbing && s != PlayerState.ThrowingFlare)
                {
                    _player.SetState(PlayerState.ThrowingFlare);
                }
            }

            if (_player.State == PlayerState.ThrowingFlare)
            {
                Vector2 mouseScreen = _input.GetMouseWorldPosition();
                Vector2 mouseWorld  = _camera.ScreenToWorld(mouseScreen);

                // Keep facing toward the mouse cursor
                float dx = mouseWorld.X - _player.PixelPosition.X;
                if (MathF.Abs(dx) > 4f)
                    _player.FacingDirection = dx > 0f ? 1 : -1;

                UpdateFlareTrajectoryPreview(mouseWorld);

                // LMB: throw the flare (intercepts before grapple fires)
                if (_input.IsLeftClickPressed())
                {
                    Vector2 dir = mouseWorld - _player.PixelPosition;
                    if (dir.LengthSquared() < 1f)
                        dir = new Vector2(_player.FacingDirection, -0.3f);
                    dir = Vector2.Normalize(dir);

                    if (_player.Stats.UseFlare())
                    {
                        Vector2 spawnPos = _player.PixelPosition
                            + new Vector2(_player.FacingDirection * 14f, -10f);
                        OnFlareThrown?.Invoke(spawnPos, dir * FlareThrowSpeed);
                    }
                    FlareTrajectoryPoints = null;
                    _player.SetState(PlayerState.Idle);
                    return;
                }

                // RMB: cancel stance
                if (_input.IsRightClickPressed())
                {
                    FlareTrajectoryPoints = null;
                    _player.SetState(PlayerState.Idle);
                    return;
                }

                // Fall through so normal horizontal movement code still applies
            }

            // ── Rappel ─────────────────────────────────────────────────────────
            // Rappel is only available in Normal input mode
            if (_input.CurrentMode == Bloop.Core.InputMode.Normal)
            {
                if (_input.IsRappelHeld() && _player.IsGrounded == false &&
                    _player.State != PlayerState.Swinging)
                {
                    if (_player.State != PlayerState.Rappelling)
                        _rope.TryAttach(_player);
                }
                else if (_player.State == PlayerState.Rappelling && !_input.IsRappelHeld())
                {
                    _rope.Detach();
                }
            }

            // ── Grapple ────────────────────────────────────────────────────────
            // LMB/RMB are only routed to the grapple in Normal input mode.
            // In EntitySelecting mode, LMB selects an entity; RMB cancels selection.
            // In EntityControlling mode, LMB/RMB are handled by the controlled entity.
            if (_input.CurrentMode == Bloop.Core.InputMode.Normal)
            {
                if (_input.IsLeftClickPressed() && _player.State != PlayerState.Rappelling)
                {
                    // Convert screen-space mouse position to world-space pixel coordinates
                    Vector2 mouseScreen = _input.GetMouseWorldPosition();
                    Vector2 mouseWorld  = _camera.ScreenToWorld(mouseScreen);
                    _grapple.TryFire(_player, mouseWorld);
                }
                if (_input.IsRightClickPressed() && (_grapple.IsFlying || _grapple.IsAnchored))
                {
                    _grapple.Release();
                }
            }

            // ── Climbing ───────────────────────────────────────────────────────
            if (_input.IsClimbHeld() && _player.State == PlayerState.Climbing)
            {
                float vert = 0f;
                if (_input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Up) ||
                    _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.W))
                    vert = -ClimbSpeed;
                else if (_input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Down) ||
                         _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.S))
                    vert = ClimbSpeed;

                float horiz = _input.GetHorizontalAxis() * ClimbSpeed * 0.5f;
                _player.Body.LinearVelocity = PhysicsManager.ToMeters(new Vector2(horiz, vert));

                // Try to mantle over the top edge when climbing upward
                if (vert < 0f)
                {
                    CheckLedgeGrabFromCling();
                    // Nudge around protruding wall-side corners while climbing upward
                    ApplyClimbingCornerCorrection();
                    // Also nudge around ceiling-edge corners (handles top-of-vine case)
                    ApplyCornerCorrection();
                }

                return; // skip normal movement while climbing
            }
            else if (_player.State == PlayerState.Climbing && !_input.IsClimbHeld())
            {
                _player.SetState(PlayerState.Falling);
            }

            // ── Rappelling movement ────────────────────────────────────────────
            if (_player.State == PlayerState.Rappelling)
            {
                _player.ActiveRopeAnchorPixels = _rope.AnchorPixelPos;
                _player.IsRopeClimbing = !_player.IsGrounded &&
                    (_input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.W) ||
                     _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Up) ||
                     _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.S) ||
                     _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Down));
                _rope.Update(gameTime, _player, _input);
                return;
            }

            // ── Swinging movement ──────────────────────────────────────────────
            if (_player.State == PlayerState.Swinging)
            {
                // ── Grounded grapple fix: keep attached while grounded ────────────
                // When grounded with a grapple attached, set the rope rendering data
                // and fall through to normal ground movement. The player can walk
                // freely within the rope radius. The grapple stays attached so it's
                // ready for the next swing when they jump/fall off a ledge.
                if (_player.IsGrounded)
                {
                    _player.ActiveRopeAnchorPixels = _grapple.AnchorPixelPos;
                    _player.IsRopeClimbing = false;
                    // Fall through to normal ground movement below
                }
                else
                {
                    _player.ActiveRopeAnchorPixels = _grapple.AnchorPixelPos;
                    _player.IsRopeClimbing = !_player.IsGrounded &&
                        (_input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.W) ||
                         _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Up) ||
                         _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.S) ||
                         _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Down));

                    // Apply tangential force (perpendicular to rope) so input drives the pendulum arc
                    // rather than wasting energy on radial stretch that the RopeJoint absorbs.
                    float inputX = _input.GetHorizontalAxis();
                    if (inputX != 0f)
                    {
                        Vector2 toAnchor = PhysicsManager.ToMeters(_grapple.AnchorPixelPos) - _player.Body.Position;
                        if (toAnchor.LengthSquared() > 0.0001f)
                        {
                            toAnchor.Normalize();
                            // In screen-space (Y+ down): perpendicular = (-toAnchor.Y, toAnchor.X).
                            // Flip so that positive inputX always produces a rightward swing.
                            Vector2 tangent = new Vector2(-toAnchor.Y, toAnchor.X);
                            if (tangent.X < 0f) tangent = -tangent;
                            _player.Body.ApplyForce(PhysicsManager.ToMeters(tangent * inputX * MoveForce * 0.4f));
                        }
                    }

                    // Climb the rope: W/Up shortens it (reel in), S/Down extends it
                    if (_input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.W) ||
                        _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Up))
                        _grapple.AdjustLength(-ClimbSpeed * dt);
                    else if (_input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.S) ||
                             _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Down))
                        _grapple.AdjustLength(+ClimbSpeed * dt);
                    return;
                }
            }

            // Not on any rope — clear the rope rendering data
            _player.ActiveRopeAnchorPixels = null;
            _player.IsRopeClimbing = false;

            // ── Crouch entry / exit ────────────────────────────────────────────
            bool crouchHeld = _input.IsCrouchHeld();
            if (crouchHeld && _player.IsGrounded &&
                (_player.State == PlayerState.Idle || _player.State == PlayerState.Walking))
            {
                _player.SetState(PlayerState.Crouching);
            }
            else if (_player.State == PlayerState.Crouching && !crouchHeld && HasStandingClearance())
            {
                _player.SetState(PlayerState.Idle);
            }

            bool isCrouching = _player.State == PlayerState.Crouching;

            // ── Normal ground/air movement ─────────────────────────────────────
            float horizontal = _input.GetHorizontalAxis();

            // Apply InvertedControls debuff: flip horizontal direction
            if (_player.Debuffs.HasDebuff(DebuffType.InvertedControls))
                horizontal = -horizontal;

            if (horizontal != 0f)
            {
                _player.FacingDirection = horizontal > 0 ? 1 : -1;

                // Crouch cuts speed cap and applied force to 30%
                float crouchModifier = isCrouching ? CrouchSpeedMultiplier : 1f;
                float speedCap       = MaxHorizontalSpeed * crouchModifier;

                // Only apply force if below max speed
                float currentVx = PhysicsManager.ToPixels(_player.Body.LinearVelocity.X);
                if (System.Math.Abs(currentVx) < speedCap)
                {
                    // Weight penalty from inventory
                    float weightPenalty = 1f - MathHelper.Clamp(_player.InventoryWeightKg / 60f, 0f, 0.5f);
                    // SlowMovement debuff modifier (1.0 = no debuff, 0.6 = slowed)
                    float slowModifier  = _player.Debuffs.GetModifier(DebuffType.SlowMovement);
                    // Air control: reduced horizontal authority when airborne
                    float airModifier   = _player.IsGrounded ? 1f : AirControlMultiplier;

                    _player.Body.ApplyForce(
                        PhysicsManager.ToMeters(new Vector2(
                            horizontal * MoveForce * weightPenalty * slowModifier * airModifier * crouchModifier, 0f)));
                }

                if (_player.IsGrounded && !isCrouching && _player.State != PlayerState.Sliding)
                    _player.SetState(PlayerState.Walking);
            }
            else
            {
                // Friction: dampen horizontal velocity when no input on ground
                if (_player.IsGrounded)
                {
                    var vel = _player.Body.LinearVelocity;
                    // Frame-rate-independent friction: same deceleration at any Hz
                    _player.Body.LinearVelocity = new Vector2(vel.X * MathF.Pow(0.75f, dt * 60f), vel.Y);

                    if (_player.State == PlayerState.Walking)
                        _player.SetState(PlayerState.Idle);
                }
            }

            // ── Coyote time tracking ───────────────────────────────────────────
            // Start coyote timer when player walks off a ledge (was grounded, now isn't)
            bool isGroundedNow = _player.IsGrounded;
            if (_wasGroundedLastFrame && !isGroundedNow &&
                _player.State != PlayerState.Jumping &&
                _player.State != PlayerState.WallJumping)
            {
                _coyoteTimer = CoyoteTimeDuration;
            }
            if (_coyoteTimer > 0f)
                _coyoteTimer -= dt;
            _wasGroundedLastFrame = isGroundedNow;

            // ── Jump buffer tick ───────────────────────────────────────────────
            if (_jumpBufferTimer > 0f)
                _jumpBufferTimer -= dt;

            // ── Wall jump cooldown tick ────────────────────────────────────────
            if (_wallJumpCooldownTimer > 0f)
                _wallJumpCooldownTimer -= dt;

            // ── Wall-cling coyote tick ─────────────────────────────────────────
            if (_wallClingCoyoteTimer > 0f)
            {
                _wallClingCoyoteTimer -= dt;
                // Phase 1.1: invalidate if player has drifted too far from the cling origin,
                // OR if they are now touching the OPPOSITE wall (different surface entirely).
                Vector2 driftDelta = _player.PixelPosition - _wallClingCoyoteOriginPx;
                if (driftDelta.LengthSquared() > MovementTuning.WallClingCoyoteMaxDriftPx
                                              * MovementTuning.WallClingCoyoteMaxDriftPx)
                {
                    _wallClingCoyoteTimer = 0f;
                }
                else if ((_lastClingWasRightWall && _player.IsTouchingWallLeft  && !_player.IsTouchingWallRight) ||
                         (!_lastClingWasRightWall && _player.IsTouchingWallRight && !_player.IsTouchingWallLeft))
                {
                    // Touching the opposite wall now — defer to current touch, drop coyote.
                    _wallClingCoyoteTimer = 0f;
                }
            }

            // ── Jump ───────────────────────────────────────────────────────────
            bool jumpPressed = _input.IsJumpPressed();
            if (jumpPressed && !isCrouching)
            {
                _jumpBufferTimer = JumpBufferDuration;
                // Phase 1.3: a buffered jump should only fire on landing if it was
                // buffered while ground/coyote was actually live, OR while a
                // wall-jump opportunity was present. Otherwise a press during free-fall
                // would queue and fire on landing — surprising the player.
                _jumpBufferGroundEligible = _player.IsGrounded
                                         || _coyoteTimer > 0f
                                         || _player.IsTouchingWall
                                         || _wallClingCoyoteTimer > 0f;
            }
            // Drop the eligibility flag when the buffer expires without firing.
            if (_jumpBufferTimer <= 0f) _jumpBufferGroundEligible = false;

            // Can jump if: grounded OR within coyote time window, and not crouching
            bool canJump = (_player.IsGrounded || _coyoteTimer > 0f) && !isCrouching;
            // For a buffered (not just-pressed) trigger, require that the buffer was eligible.
            bool buffereTrigger = _jumpBufferTimer > 0f && (jumpPressed || _jumpBufferGroundEligible);

            if ((jumpPressed || buffereTrigger) && canJump)
            {
                _jumpBufferTimer = 0f;
                _jumpBufferGroundEligible = false;
                _coyoteTimer     = 0f;

                // Slingshot launch if kinetic charge is maxed
                if (_player.Stats.KineticCharge >= PlayerStats.MaxKineticCharge)
                {
                    _momentum.TriggerSlingshot(_player);
                }
                else
                {
                    // ReducedJump debuff modifier (1.0 = no debuff, 0.5 = reduced)
                    float jumpModifier = _player.Debuffs.GetModifier(DebuffType.ReducedJump);

                    _player.Body.ApplyLinearImpulse(
                        PhysicsManager.ToMeters(new Vector2(0f, -JumpImpulse * jumpModifier)));
                    _player.SetState(PlayerState.Jumping);
                }
            }
            else if (jumpPressed
                  && (_player.IsTouchingWall || _wallClingCoyoteTimer > 0f)
                  && !_player.IsGrounded
                  && _wallJumpCooldownTimer <= 0f
                  && (_player.State == PlayerState.Falling
                   || _player.State == PlayerState.Jumping
                   || _player.State == PlayerState.WallJumping))
            {
                // ── Wall jump ──────────────────────────────────────────────────
                // Kick away from the wall: if touching right wall (or last clung right), jump left
                bool onRightWall = _player.IsTouchingWallRight ||
                                   (_wallClingCoyoteTimer > 0f && _lastClingWasRightWall);
                float kickDir = onRightWall ? -1f : 1f;
                _wallClingCoyoteTimer = 0f; // consume coyote window

                // ReducedJump debuff modifier
                float jumpModifier = _player.Debuffs.GetModifier(DebuffType.ReducedJump);

                // Zero out current velocity first for a clean impulse
                _player.Body.LinearVelocity = new Vector2(
                    _player.Body.LinearVelocity.X * 0.3f,
                    0f);

                _player.Body.ApplyLinearImpulse(
                    PhysicsManager.ToMeters(new Vector2(
                        kickDir * WallJumpHorizontal * jumpModifier,
                        -WallJumpVertical * jumpModifier)));

                _player.FacingDirection = (int)kickDir;
                _player.SetState(PlayerState.WallJumping);
                _wallJumpCooldownTimer = WallJumpCooldown;
                _coyoteTimer = 0f;
                _jumpBufferTimer = 0f;
                _jumpBufferGroundEligible = false;
            }

            // ── Ledge grab / mantle detection (2.2) ───────────────────────────
            if (_tileMap != null
                && (_player.State == PlayerState.Falling
                 || _player.State == PlayerState.Jumping
                 || _player.State == PlayerState.WallJumping))
            {
                CheckLedgeGrab();
            }

            // ── Sliding detection ──────────────────────────────────────────────
            // Sliding is triggered by MomentumSystem when slope contact is detected
            _momentum.Update(gameTime, _player);

            // ── Corner correction: nudge past ceiling corners when jumping ──────
            // When the player's shoulder clips one corner of a ceiling tile but the
            // other side is clear, push them horizontally (up to 6px) so they slip
            // past instead of stalling. Only fires when moving upward.
            if (_player.Body.LinearVelocity.Y < 0f)
                ApplyCornerCorrection();

            // ── Hard velocity clamp (B4) ───────────────────────────────────────
            // Prevent slingshot/grapple launches and long falls from producing
            // velocities so extreme that CCD + sub-stepping cannot prevent tunneling.
            ClampVelocity();
        }

        // ── Hard velocity clamp ────────────────────────────────────────────────

        /// <summary>
        /// Clamp the player body velocity to safe maximums each frame.
        /// Applied after all forces so it acts as an absolute ceiling.
        /// </summary>
        private void ClampVelocity()
        {
            var vel = PhysicsManager.ToPixels(_player.Body.LinearVelocity);

            float clampedVx = System.Math.Clamp(vel.X,
                -MaxHorizontalSpeedHard, MaxHorizontalSpeedHard);
            float clampedVy = System.Math.Min(vel.Y, MaxFallSpeedHard); // only cap downward

            if (clampedVx != vel.X || clampedVy != vel.Y)
                _player.Body.LinearVelocity = PhysicsManager.ToMeters(
                    new Vector2(clampedVx, clampedVy));
        }

        // ── Standing clearance check (crouch exit) ─────────────────────────────

        /// <summary>
        /// True if the tiles above the player are empty enough to stand up.
        /// Samples two x positions across the player width at the height the
        /// standing hitbox would reach.
        /// </summary>
        private bool HasStandingClearance()
        {
            if (_tileMap == null) return true;

            Vector2 pos = _player.PixelPosition;
            int ts = TileMap.TileSize;

            // Top of the standing hitbox (player center - half standing height),
            // nudged up by 1px to be safely inside the ceiling tile if any.
            float topY = pos.Y - Player.StandingHeightPx / 2f - 1f;
            int ty = (int)(topY / ts);
            if (ty < 0) return true;
            if (ty >= _tileMap.Height) return true;

            float halfW = Player.WidthPx * 0.4f;
            int txL = (int)((pos.X - halfW) / ts);
            int txR = (int)((pos.X + halfW) / ts);

            for (int tx = txL; tx <= txR; tx++)
            {
                if (tx < 0 || tx >= _tileMap.Width) continue;
                if (TileProperties.IsSolid(_tileMap.GetTile(tx, ty)))
                    return false;
            }
            return true;
        }

        // ── Wall detection via raycasts ────────────────────────────────────────

        /// <summary>
        /// Cast short horizontal rays from the player center to detect wall contacts.
        /// This replaces the callback-based counter approach which was prone to drift
        /// when the player body was teleported or contacts changed rapidly.
        ///
        /// Uses symmetric per-side hysteresis: a wall must be touched continuously
        /// for <see cref="WallContactRequired"/> seconds before reporting "on wall,"
        /// and must be released for <see cref="WallReleaseGrace"/> seconds before
        /// reporting "off wall." Eliminates phantom cling near corners and prevents
        /// oscillation without giving the player asymmetric grace.
        /// </summary>
        private void UpdateWallDetection(float dt)
        {
            if (_tileMap == null)
            {
                _player.IsTouchingWallLeft  = false;
                _player.IsTouchingWallRight = false;
                _leftContactTimer = _rightContactTimer = 0f;
                _leftReleaseTimer = _rightReleaseTimer = 999f;
                return;
            }

            Vector2 pos  = _player.PixelPosition;
            float halfW  = Player.WidthPx / 2f;
            float halfH  = _player.CurrentHeightPx / 2f;
            int   ts     = TileMap.TileSize;

            float[] sampleYOffsets = { -halfH * 0.6f, 0f, halfH * 0.6f };

            bool touchLeft  = false;
            bool touchRight = false;

            foreach (float yOff in sampleYOffsets)
            {
                float sampleY = pos.Y + yOff;

                float leftX  = pos.X - halfW - WallRayLength;
                int   ltx    = (int)(leftX  / ts);
                int   lty    = (int)(sampleY / ts);
                if (ltx >= 0 && ltx < _tileMap.Width && lty >= 0 && lty < _tileMap.Height)
                {
                    if (TileProperties.IsSolid(_tileMap.GetTile(ltx, lty)))
                        touchLeft = true;
                }

                float rightX = pos.X + halfW + WallRayLength;
                int   rtx    = (int)(rightX  / ts);
                int   rty    = (int)(sampleY  / ts);
                if (rtx >= 0 && rtx < _tileMap.Width && rty >= 0 && rty < _tileMap.Height)
                {
                    if (TileProperties.IsSolid(_tileMap.GetTile(rtx, rty)))
                        touchRight = true;
                }
            }

            // ── Symmetric hysteresis: contact AND release each have a settling time ──
            // Already-clung walls stay reported through the release grace; new contacts
            // need a brief commitment to avoid 1-frame phantom clings near corners.
            // Special case: if the player is currently WallClinging, drop the contact
            // requirement so they don't spuriously fall off mid-cling.
            bool clingActive = _player.State == PlayerState.WallClinging;
            float contactRequired = clingActive ? 0f : WallContactRequired;

            if (touchLeft)  { _leftContactTimer  += dt; _leftReleaseTimer  = 0f; }
            else            { _leftContactTimer  = 0f;  _leftReleaseTimer += dt; }

            if (touchRight) { _rightContactTimer += dt; _rightReleaseTimer = 0f; }
            else            { _rightContactTimer = 0f;  _rightReleaseTimer += dt; }

            bool wasLeft  = _player.IsTouchingWallLeft;
            bool wasRight = _player.IsTouchingWallRight;

            // Promote to "on wall" once contact has held long enough.
            if (_leftContactTimer  >= contactRequired) wasLeft  = true;
            if (_rightContactTimer >= contactRequired) wasRight = true;

            // Demote to "off wall" once release grace has elapsed.
            if (_leftReleaseTimer  >= WallReleaseGrace) wasLeft  = false;
            if (_rightReleaseTimer >= WallReleaseGrace) wasRight = false;

            _player.IsTouchingWallLeft  = wasLeft;
            _player.IsTouchingWallRight = wasRight;

            _prevTouchingWallLeft  = touchLeft;
            _prevTouchingWallRight = touchRight;
        }

        // ── Ledge grab helper (2.2) ────────────────────────────────────────────

        /// <summary>
        /// Detects whether the player is adjacent to a ledge they can mantle onto.
        ///
        /// Conditions (all must be true):
        ///   1. Player is falling (downward velocity > 0) — not jumping upward
        ///   2. Player is pressing toward a wall (horizontal input in wall direction)
        ///   3. The tile at player torso height is solid (the ledge face)
        ///   4. The tile at player head height is empty (can grab the top)
        ///   5. The tile above the solid tile is empty (landing space exists)
        ///
        /// If all conditions are met, calls Player.StartMantle() with the
        /// target standing position on top of the ledge.
        ///
        /// Bug #5 fix: also detects horizontal ledges when the player is walking
        /// toward a wall with a gap above (grounded auto-mantle). This handles
        /// the case where the player walks off a low ledge and can grab the next one.
        /// </summary>
        private void CheckLedgeGrab()
        {
            if (_tileMap == null) return;

            // Only grab ledges when falling downward — not when jumping upward
            // This prevents accidental mantles during wall jumps
            if (_player.Body.LinearVelocity.Y < 0f) return;

            int ts = TileMap.TileSize;
            Vector2 pos = _player.PixelPosition;

            // Player body extents
            float halfW  = Player.WidthPx          / 2f;
            float halfH  = _player.CurrentHeightPx / 2f;

            // Head Y (top of player) and torso Y (center)
            float headY   = pos.Y - halfH;
            float torsoY  = pos.Y;

            // Check both sides based on horizontal input
            float horiz = _input.GetHorizontalAxis();
            if (horiz == 0f) return; // must be pressing toward a wall

            // Side to check: 6px reach beyond body edge for gentle ledge magnetism
            float sideX = pos.X + (horiz > 0 ? halfW + 6f : -(halfW + 6f));

            // Tile coordinates
            int wallTx   = (int)(sideX  / ts);
            int torsoTy  = (int)(torsoY / ts);
            int headTy   = (int)(headY  / ts);
            int aboveTy  = torsoTy - 1; // tile above the ledge top

            // Bounds check
            if (wallTx < 0 || wallTx >= _tileMap.Width) return;
            if (torsoTy < 0 || torsoTy >= _tileMap.Height) return;
            if (headTy  < 0 || headTy  >= _tileMap.Height) return;
            if (aboveTy < 0 || aboveTy >= _tileMap.Height) return;

            // Condition: torso tile is solid (the ledge face)
            bool torsoSolid = TileProperties.IsSolid(_tileMap.GetTile(wallTx, torsoTy));
            if (!torsoSolid) return;

            // Condition: head tile is empty (player can grab the top)
            bool headEmpty = !TileProperties.IsSolid(_tileMap.GetTile(wallTx, headTy));
            if (!headEmpty) return;

            // Condition: tile above the ledge top is empty (landing space)
            bool aboveEmpty = !TileProperties.IsSolid(_tileMap.GetTile(wallTx, aboveTy));
            if (!aboveEmpty) return;

            // Condition: player head must be at or slightly above the ledge top
            // (ledge top pixel = torsoTy * ts)
            float ledgeTopPx = torsoTy * ts;
            float headPx     = headY;
            if (headPx > ledgeTopPx + MantleHeadTolerance) return; // too far below

            // All conditions met — compute standing position on top of ledge
            // Standing center X = player X (don't move horizontally)
            // Standing center Y = ledge top - halfH (standing on top of the ledge)
            float targetX = pos.X;
            float targetY = ledgeTopPx - halfH;

            _player.StartMantle(new Vector2(targetX, targetY));
        }

        // ── Ledge grab from cling (climb-up mantle) ───────────────────────────

        /// <summary>
        /// Called while WallClinging and pressing W/Up.
        /// If the player's head is meeting a solid floor tile directly above and
        /// there is clear space on top of it, triggers a mantle to pull them over.
        ///
        /// Bug #5 fix: uses the wall-adjacent tile column (the column the player is
        /// clinging to) instead of the player center column. This ensures the ledge
        /// detection works correctly when the player is offset from the wall center.
        /// </summary>
        private void CheckLedgeGrabFromCling()
        {
            if (_tileMap == null) return;

            int     ts      = TileMap.TileSize;
            Vector2 pos     = _player.PixelPosition;
            float   halfW   = Player.WidthPx / 2f;
            float   halfH   = _player.CurrentHeightPx / 2f;

            // ── Bug #5 fix: use wall-adjacent tile column ──────────────────────
            // Determine which column the wall is in based on which side we're clinging to.
            // This is more reliable than using the player center column, which may be
            // offset from the wall when the player is pressed against it.
            int wallTx;
            if (_player.IsTouchingWallLeft)
                wallTx = (int)((pos.X - halfW - 2f) / ts);
            else if (_player.IsTouchingWallRight)
                wallTx = (int)((pos.X + halfW + 2f) / ts);
            else
                wallTx = (int)(pos.X / ts); // fallback to center

            // Tile row at the player's head
            int headTy  = (int)((pos.Y - halfH) / ts);
            int aboveTy = headTy - 1;

            if (wallTx < 0 || wallTx >= _tileMap.Width)  return;
            if (headTy  < 0 || headTy  >= _tileMap.Height) return;
            if (aboveTy < 0 || aboveTy >= _tileMap.Height) return;

            // Head must be pressing into a solid tile (the floor)
            if (!TileProperties.IsSolid(_tileMap.GetTile(wallTx, headTy))) return;
            // There must be clear space to stand on top of that tile
            if (TileProperties.IsSolid(_tileMap.GetTile(wallTx, aboveTy))) return;

            // Mantle: stand on top of the blocking tile
            float targetY = headTy * ts - halfH;
            _player.StartMantle(new Vector2(pos.X, targetY));
        }

        // ── Corner correction (ceiling / narrow gap) ───────────────────────────

        /// <summary>
        /// When the player's body edge clips exactly one corner of a ceiling tile while
        /// the opposite side is clear, nudge them horizontally (≤ 6 px) to slip past.
        /// Prevents "invisible wall" feel on tight vertical jumps.
        /// Only called when moving upward (LinearVelocity.Y &lt; 0).
        /// </summary>
        private void ApplyCornerCorrection()
        {
            if (_tileMap == null) return;

            int ts = TileMap.TileSize;
            Vector2 pos  = _player.PixelPosition;
            float halfW  = Player.WidthPx / 2f;
            float halfH  = _player.CurrentHeightPx / 2f;

            // Sample 1 px above the player's head
            float headY = pos.Y - halfH - 1f;
            if (headY < 0f) return;
            int headTy = (int)(headY / ts);
            if (headTy < 0 || headTy >= _tileMap.Height) return;

            // Left and right inner edges (2 px inward so trivial wall contacts don't trigger)
            float leftX  = pos.X - halfW + 2f;
            float rightX = pos.X + halfW - 2f;
            int leftTx   = (int)(leftX  / ts);
            int rightTx  = (int)(rightX / ts);
            if (leftTx < 0 || rightTx >= _tileMap.Width) return;

            bool leftSolid  = TileProperties.IsSolid(_tileMap.GetTile(leftTx,  headTy));
            bool rightSolid = TileProperties.IsSolid(_tileMap.GetTile(rightTx, headTy));

            const float MaxNudge = 6f;

            if (leftSolid && !rightSolid)
            {
                // Left shoulder in ceiling tile — nudge right
                float clearEdge = (leftTx + 1) * ts;
                float overlap   = clearEdge - leftX;
                if (overlap > 0f && overlap <= MaxNudge)
                    _player.Body.Position += PhysicsManager.ToMeters(new Vector2(overlap, 0f));
            }
            else if (!leftSolid && rightSolid)
            {
                // Right shoulder in ceiling tile — nudge left
                float blockLeft = rightTx * ts;
                float overlap   = rightX - blockLeft;
                if (overlap > 0f && overlap <= MaxNudge)
                    _player.Body.Position -= PhysicsManager.ToMeters(new Vector2(overlap, 0f));
            }
        }

        /// <summary>
        /// Corner correction for climbing: when the player climbs upward alongside a wall
        /// and their top edge encounters a protruding tile corner, nudge them horizontally
        /// around the corner instead of getting stuck.
        ///
        /// Checks the tile diagonally above the player's head on the wall side. If that
        /// tile is solid but the tile directly above the player is empty, it means there's
        /// a protruding corner — nudge the player away from the wall to slide around it.
        /// </summary>
        private void ApplyClimbingCornerCorrection()
        {
            if (_tileMap == null) return;

            int ts = TileMap.TileSize;
            Vector2 pos = _player.PixelPosition;
            float halfW = Player.WidthPx / 2f;
            float halfH = _player.CurrentHeightPx / 2f;

            // Determine which side the wall is on
            bool wallLeft = _player.IsTouchingWallLeft;
            bool wallRight = _player.IsTouchingWallRight;
            if (!wallLeft && !wallRight) return;

            // Sample the tile at the player's head level on the wall side
            float headY = pos.Y - halfH;
            int headTy = (int)(headY / ts);
            if (headTy < 0 || headTy >= _tileMap.Height) return;

            // The wall-adjacent column
            float wallEdgeX = wallLeft ? pos.X - halfW : pos.X + halfW;
            int wallTx = wallLeft
                ? (int)((wallEdgeX - 1f) / ts)
                : (int)((wallEdgeX + 1f) / ts);
            if (wallTx < 0 || wallTx >= _tileMap.Width) return;

            // The tile diagonally above the player's head on the wall side
            int aboveTy = headTy - 1;
            if (aboveTy < 0 || aboveTy >= _tileMap.Height) return;

            // The tile directly above the player's head (center column)
            int centerTx = (int)(pos.X / ts);
            if (centerTx < 0 || centerTx >= _tileMap.Width) return;

            bool wallCornerSolid = TileProperties.IsSolid(_tileMap.GetTile(wallTx, aboveTy));
            bool aboveClear = !TileProperties.IsSolid(_tileMap.GetTile(centerTx, aboveTy));

            // If the diagonal corner is solid but the space above the player is clear,
            // there's a protruding corner — nudge the player away from the wall.
            if (wallCornerSolid && aboveClear)
            {
                const float MaxNudge = 6f;

                if (wallLeft)
                {
                    // Protruding corner on the left — nudge right
                    float clearEdge = (wallTx + 1) * ts;
                    float overlap = clearEdge - (pos.X - halfW);
                    if (overlap > 0f && overlap <= MaxNudge)
                        _player.Body.Position += PhysicsManager.ToMeters(new Vector2(overlap, 0f));
                }
                else
                {
                    // Protruding corner on the right — nudge left
                    float blockLeft = wallTx * ts;
                    float overlap = (pos.X + halfW) - blockLeft;
                    if (overlap > 0f && overlap <= MaxNudge)
                        _player.Body.Position -= PhysicsManager.ToMeters(new Vector2(overlap, 0f));
                }
            }
        }

        // ── Flare trajectory preview ───────────────────────────────────────────

        private void UpdateFlareTrajectoryPreview(Vector2 mouseWorld)
        {
            Vector2 dir = mouseWorld - _player.PixelPosition;
            if (dir.LengthSquared() < 1f)
            {
                FlareTrajectoryPoints = null;
                return;
            }
            dir = Vector2.Normalize(dir);

            Vector2 spawnPos = _player.PixelPosition + new Vector2(_player.FacingDirection * 14f, -10f);
            Vector2 vel      = dir * FlareThrowSpeed;

            var pts = new Vector2[FlareArcSteps + 1];
            Vector2 pos = spawnPos;
            // Phase 2.6: read gravity from PhysicsManager — no more hardcoded duplication.
            float gravityPx = PhysicsManager.ToPixels(PhysicsManager.Gravity.Y);
            for (int i = 0; i <= FlareArcSteps; i++)
            {
                pts[i] = pos;
                vel   += new Vector2(0f, gravityPx * FlareArcSimDt);
                pos   += vel * FlareArcSimDt;
            }
            FlareTrajectoryPoints = pts;
        }
    }
}
