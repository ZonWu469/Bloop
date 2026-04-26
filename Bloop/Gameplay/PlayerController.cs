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
        /// <summary>Horizontal movement force in Newtons (applied per frame).</summary>
        private const float MoveForce         = 750f;  // scaled 1.5× for larger body
        /// <summary>Maximum horizontal speed in pixels/second.</summary>
        private const float MaxHorizontalSpeed = 180f;
        /// <summary>
        /// Hard cap on horizontal speed (px/s) applied every frame after forces.
        /// Prevents slingshot/grapple launches from producing velocities so high
        /// that CCD + sub-stepping cannot prevent tunneling.
        /// 600 px/s ≈ 18.75 tiles/s — fast enough for exciting gameplay.
        /// </summary>
        private const float MaxHorizontalSpeedHard = 600f;
        /// <summary>
        /// Hard cap on downward vertical speed (px/s).
        /// Prevents terminal-velocity tunneling on very long falls.
        /// 800 px/s ≈ 25 tiles/s.
        /// </summary>
        private const float MaxFallSpeedHard = 800f;
        /// <summary>Jump impulse in pixel-space units (converted to meters).</summary>
        private const float JumpImpulse       = 240f;  // scaled 1.5× for larger body
        /// <summary>Climbing vertical speed in pixels/second.</summary>
        private const float ClimbSpeed        = 100f;
        /// <summary>Slope angle threshold for auto-slide (degrees).</summary>
        private const float SlideAngleThreshold = 20f;
        /// <summary>Multiplier applied to horizontal force when airborne (0–1).</summary>
        private const float AirControlMultiplier = 0.70f;
        /// <summary>Speed/force multiplier while crouching (30%).</summary>
        private const float CrouchSpeedMultiplier = 0.30f;

        // ── Wall jump constants ────────────────────────────────────────────────
        /// <summary>Vertical component of wall jump impulse (60% of normal jump).</summary>
        private const float WallJumpVertical   = JumpImpulse * 0.60f;
        /// <summary>Horizontal kick-away component of wall jump impulse.</summary>
        private const float WallJumpHorizontal = JumpImpulse * 0.55f;
        /// <summary>Cooldown in seconds between wall jumps (prevents infinite climbing).</summary>
        private const float WallJumpCooldown   = 0.30f;

        // ── Ledge grab / mantle constants (2.2) ───────────────────────────────
        /// <summary>
        /// Maximum pixels the player's head can be above a ledge top to trigger a mantle.
        /// Allows grabbing ledges even when slightly above them.
        /// </summary>
        private const float MantleHeadTolerance = 18f;  // scaled 1.5× for larger body

        // ── Coyote time constants ──────────────────────────────────────────────
        /// <summary>
        /// Grace period (seconds) after leaving a platform where jumping is still allowed.
        /// Prevents the frustrating "I pressed jump right as I walked off the edge" miss.
        /// </summary>
        private const float CoyoteTimeDuration = 0.12f;

        // ── Jump buffer constants ──────────────────────────────────────────────
        /// <summary>
        /// Window (seconds) before landing where a jump press is buffered.
        /// If the player lands within this window, the jump fires automatically.
        /// </summary>
        private const float JumpBufferDuration = 0.10f;

        // ── Wall raycast constants ─────────────────────────────────────────────
        /// <summary>How far (pixels) to cast the horizontal wall-detection rays.</summary>
        private const float WallRayLength = 4f;

        // ── References ─────────────────────────────────────────────────────────
        private readonly Player        _player;
        private readonly InputManager  _input;
        private readonly RopeSystem    _rope;
        private readonly GrapplingHook _grapple;
        private readonly MomentumSystem _momentum;
        private readonly Camera        _camera;

        // ── Wall jump state ────────────────────────────────────────────────────
        /// <summary>Remaining cooldown time before another wall jump is allowed.</summary>
        private float _wallJumpCooldownTimer = 0f;

        // ── Wall detection hysteresis (holds flags 1 extra frame after losing contact) ─
        private bool _prevTouchingWallLeft  = false;
        private bool _prevTouchingWallRight = false;

        // ── Wall-cling coyote time (allows wall-jump briefly after releasing cling) ─
        private float _wallClingCoyoteTimer = 0f;
        private const float WallClingCoyoteDuration = 0.08f;
        // Which wall was last clung (true = right wall), used to compute kick direction during coyote window
        private bool _lastClingWasRightWall = false;

        // ── Wall slide / cling state ───────────────────────────────────────────
        /// <summary>How long the player has been pressing toward a wall while falling.</summary>
        private float _wallSlideTimer = 0f;
        /// <summary>Current linear damping applied during wall slide (ramps up).</summary>
        private float _wallSlideDamping = 0f;
        private const float WallSlideMaxDamping       = 20f;  // damping at full slide
        private const float WallSlideRampTime         = 0.5f; // seconds to reach max damping
        private const float WallClingVelocityThreshold = 10f; // px/s — below this, snap to cling
        private const float WallClingTimerThreshold   = 0.6f; // seconds pressing wall → auto-cling
        private const float WallClimbSpeed            = 40f;  // px/s upward while clinging

        // ── Coyote time state ──────────────────────────────────────────────────
        /// <summary>Remaining coyote time after leaving a platform.</summary>
        private float _coyoteTimer = 0f;
        /// <summary>Whether the player was grounded last frame (for coyote time detection).</summary>
        private bool _wasGroundedLastFrame = false;

        // ── Jump buffer state ──────────────────────────────────────────────────
        /// <summary>Remaining time for a buffered jump press.</summary>
        private float _jumpBufferTimer = 0f;

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
            UpdateWallDetection();

            // ── Wall slide → cling transition ──────────────────────────────────
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
                }
            }
            else if (_player.State != PlayerState.WallClinging)
            {
                _wallSlideTimer   = 0f;
                _wallSlideDamping = 0f;
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
                    // Down or pressing away: release cling — start coyote window for wall jump
                    _wallClingCoyoteTimer = WallClingCoyoteDuration;
                    _lastClingWasRightWall = _player.IsTouchingWallRight;
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
                _player.ActiveRopeAnchorPixels = _grapple.AnchorPixelPos;
                _player.IsRopeClimbing = !_player.IsGrounded &&
                    (_input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.W) ||
                     _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Up) ||
                     _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.S) ||
                     _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Down));

                // Apply small horizontal force to swing direction
                float swingForce = _input.GetHorizontalAxis() * MoveForce * 0.4f;
                _player.Body.ApplyForce(PhysicsManager.ToMeters(new Vector2(swingForce, 0f)));

                // Climb the rope: W/Up shortens it (reel in), S/Down extends it
                if (_input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.W) ||
                    _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Up))
                    _grapple.AdjustLength(-ClimbSpeed * dt);
                else if (_input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.S) ||
                         _input.IsKeyHeld(Microsoft.Xna.Framework.Input.Keys.Down))
                    _grapple.AdjustLength(+ClimbSpeed * dt);
                return;
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
                    _player.Body.LinearVelocity = new Vector2(vel.X * 0.75f, vel.Y);

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
                _wallClingCoyoteTimer -= dt;

            // ── Jump ───────────────────────────────────────────────────────────
            bool jumpPressed = _input.IsJumpPressed();
            if (jumpPressed && !isCrouching)
                _jumpBufferTimer = JumpBufferDuration; // buffer the press

            // Can jump if: grounded OR within coyote time window, and not crouching
            bool canJump = (_player.IsGrounded || _coyoteTimer > 0f) && !isCrouching;

            if ((jumpPressed || _jumpBufferTimer > 0f) && canJump)
            {
                // Consume the buffer
                _jumpBufferTimer = 0f;
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
        /// </summary>
        private void UpdateWallDetection()
        {
            if (_tileMap == null)
            {
                _player.IsTouchingWallLeft  = false;
                _player.IsTouchingWallRight = false;
                return;
            }

            Vector2 pos  = _player.PixelPosition;
            float halfW  = Player.WidthPx / 2f;
            float halfH  = _player.CurrentHeightPx / 2f;
            int   ts     = TileMap.TileSize;

            // Check three vertical sample points (top, middle, bottom of body)
            // to handle partial wall contacts correctly
            float[] sampleYOffsets = { -halfH * 0.6f, 0f, halfH * 0.6f };

            bool touchLeft  = false;
            bool touchRight = false;

            foreach (float yOff in sampleYOffsets)
            {
                float sampleY = pos.Y + yOff;

                // Left wall: tile just to the left of the player body
                float leftX  = pos.X - halfW - WallRayLength;
                int   ltx    = (int)(leftX  / ts);
                int   lty    = (int)(sampleY / ts);
                if (ltx >= 0 && ltx < _tileMap.Width && lty >= 0 && lty < _tileMap.Height)
                {
                    if (TileProperties.IsSolid(_tileMap.GetTile(ltx, lty)))
                        touchLeft = true;
                }

                // Right wall: tile just to the right of the player body
                float rightX = pos.X + halfW + WallRayLength;
                int   rtx    = (int)(rightX  / ts);
                int   rty    = (int)(sampleY  / ts);
                if (rtx >= 0 && rtx < _tileMap.Width && rty >= 0 && rty < _tileMap.Height)
                {
                    if (TileProperties.IsSolid(_tileMap.GetTile(rtx, rty)))
                        touchRight = true;
                }
            }

            // 1-frame hysteresis: hold the wall flag for one extra frame after losing contact
            // to prevent single-frame flicker on diagonal walk-off corners.
            _player.IsTouchingWallLeft  = touchLeft  || _prevTouchingWallLeft;
            _player.IsTouchingWallRight = touchRight || _prevTouchingWallRight;
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

            // Side to check: positive input = right side, negative = left side
            float sideX = pos.X + (horiz > 0 ? halfW + 2f : -(halfW + 2f));

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
        /// </summary>
        private void CheckLedgeGrabFromCling()
        {
            if (_tileMap == null) return;

            int     ts      = TileMap.TileSize;
            Vector2 pos     = _player.PixelPosition;
            float   halfH   = _player.CurrentHeightPx / 2f;

            // Tile column directly under the player center
            int cx = (int)(pos.X / ts);
            // Tile row at the player's head
            int headTy  = (int)((pos.Y - halfH) / ts);
            int aboveTy = headTy - 1;

            if (cx < 0 || cx >= _tileMap.Width)  return;
            if (headTy  < 0 || headTy  >= _tileMap.Height) return;
            if (aboveTy < 0 || aboveTy >= _tileMap.Height) return;

            // Head must be pressing into a solid tile (the floor)
            if (!TileProperties.IsSolid(_tileMap.GetTile(cx, headTy))) return;
            // There must be clear space to stand on top of that tile
            if (TileProperties.IsSolid(_tileMap.GetTile(cx, aboveTy))) return;

            // Mantle: stand on top of the blocking tile
            float targetY = headTy * ts - halfH;
            _player.StartMantle(new Vector2(pos.X, targetY));
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
            // Gravity in pixel-space: 20 m/s² × 64 px/m = 1280 px/s²
            const float gravityPx = 1280f;
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
