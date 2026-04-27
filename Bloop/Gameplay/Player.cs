using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Contacts;
using Bloop.Physics;
using Bloop.Rendering;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Gameplay
{
    /// <summary>
    /// Player state machine states.
    /// </summary>
    public enum PlayerState
    {
        Idle,
        Walking,
        Crouching,
        Jumping,
        Falling,
        Climbing,
        Sliding,
        Rappelling,
        Swinging,
        WallJumping,
        /// <summary>
        /// Player is clinging to a wall — gravity disabled, body frozen.
        /// Entered from Falling when pressing toward a wall for WallClingTimerThreshold seconds.
        /// Exited by jumping (wall jump), pressing away from wall, pressing down, or losing wall contact.
        /// </summary>
        WallClinging,
        Mantling,
        Launching,   // 2.4 — brief post-release boost state (reduced gravity, speed lines)
        Stunned,
        Dead,
        /// <summary>
        /// Player is possessing a controllable entity. Body is frozen in place.
        /// Input is routed to the controlled entity instead of the player.
        /// Exception: Luminous Isopod does NOT trigger this state — player moves normally.
        /// </summary>
        Controlling,
        /// <summary>
        /// Player is holding a flare and aiming a throw. Physics identical to Idle.
        /// LMB throws the flare, RMB cancels.
        /// </summary>
        ThrowingFlare
    }

    /// <summary>
    /// The player entity. Owns the Aether dynamic body, foot sensor, state machine,
    /// stats, and inventory weight. Renders as a colored rectangle placeholder.
    /// PlayerController drives the actual input-to-physics logic.
    /// </summary>
    public class Player
    {
        // ── Dimensions (pixels) ────────────────────────────────────────────────
        public const float WidthPx         = 36f;  // 1.5× larger
        public const float StandingHeightPx = 60f; // 1.5× larger
        public const float CrouchHeightPx   = 30f; // 1.5× larger

        /// <summary>Current hitbox height in pixels — switches between standing and crouch.</summary>
        public float CurrentHeightPx { get; private set; } = StandingHeightPx;

        /// <summary>Current hitbox height in pixels. Alias for CurrentHeightPx.</summary>
        public float HeightPx => CurrentHeightPx;

        // ── Physics ────────────────────────────────────────────────────────────
        public Body   Body        { get; }
        public Fixture FootSensor { get; private set; } = null!;

        // ── State machine ──────────────────────────────────────────────────────
        public PlayerState State { get; private set; } = PlayerState.Idle;

        // ── Ground detection ───────────────────────────────────────────────────
        /// <summary>Number of fixtures currently overlapping the foot sensor.</summary>
        private int _groundContactCount = 0;
        public bool IsGrounded => _groundContactCount > 0;

        /// <summary>
        /// Zero the ground contact count. Call after a physics-body rebuild that
        /// destroys the ground under the player (quake collapse, disappearing
        /// platform fade) — Aether does not always fire OnSeparation when the
        /// body on the other side of a live contact is removed, so without this
        /// the player would keep thinking they are grounded on empty space.
        /// </summary>
        public void ResetGroundContacts() => _groundContactCount = 0;

        // ── Wall contact detection (raycast-based, set each frame) ─────────────
        /// <summary>True if the player is pressing against a wall on the left (set by PlayerController each frame).</summary>
        public bool IsTouchingWallLeft  { get; set; }
        /// <summary>True if the player is pressing against a wall on the right (set by PlayerController each frame).</summary>
        public bool IsTouchingWallRight { get; set; }
        /// <summary>True if the player is touching any wall (left or right).</summary>
        public bool IsTouchingWall => IsTouchingWallLeft || IsTouchingWallRight;

        // ── Facing direction ───────────────────────────────────────────────────
        public int FacingDirection { get; set; } = 1; // +1 = right, -1 = left

        // ── Rope rendering data (set by PlayerController each frame) ───────────
        /// <summary>Pixel-space anchor of the active rope; null when not rope-attached.</summary>
        public Vector2? ActiveRopeAnchorPixels { get; set; }
        /// <summary>True when W/S is held while rope-attached and airborne.</summary>
        public bool IsRopeClimbing { get; set; }

        // ── Stats ──────────────────────────────────────────────────────────────
        public PlayerStats Stats { get; } = new PlayerStats();

        // ── Inventory ──────────────────────────────────────────────────────────
        public Inventory    Inventory { get; }
        public DebuffSystem Debuffs   { get; }

        // ── Inventory weight (affects physics mass) ────────────────────────────
        private float _inventoryWeightKg = 0f;
        private const float BaseBodyMass = 70f; // kg

        // ── Tile map reference (for solid-overlap push-out) ────────────────────
        private TileMap? _tileMap;

        /// <summary>
        /// Provide the tile map so the player can perform per-frame solid-overlap
        /// push-out checks. Call after construction and after each level reload.
        /// </summary>
        public void SetTileMap(TileMap? tileMap) => _tileMap = tileMap;

        // ── Stun timer ─────────────────────────────────────────────────────────
        private float _stunTimer = 0f;

        // ── Contact invulnerability (prevents rapid multi-hits from entities) ──
        private float _contactInvulnTimer = 0f;
        private const float ContactInvulnDuration = 1.0f;
        /// <summary>True while the player is immune to entity contact damage.</summary>
        public bool IsContactInvulnerable => _contactInvulnTimer > 0f;

        /// <summary>
        /// Optional callback invoked when the player receives contact damage.
        /// Parameter: damage amount. Wired up by GameplayScreen for damage flash effect.
        /// </summary>
        public Action<float>? OnDamageReceived { get; set; }

        // ── Mantle state (2.2) ─────────────────────────────────────────────────
        private float   _mantleTimer       = 0f;
        private Vector2 _mantleTargetPixel = Vector2.Zero;
        private const float MantleDuration = MovementTuning.MantleDuration;

        // ── Launch state (2.4) — post-grapple-release momentum boost ──────────
        private float _launchTimer = 0f;
        private const float LaunchDuration     = MovementTuning.LaunchDuration;
        private const float LaunchGravityScale = MovementTuning.LaunchGravityScale;
        /// <summary>True while the player is in the post-release launch window.</summary>
        public bool LaunchBoostActive => State == PlayerState.Launching;
        /// <summary>Horizontal speed (px/s) at launch start — used for speed-line intensity.</summary>
        public float LaunchSpeedPx { get; private set; }

        // ── Screen shake callback (3.2) ────────────────────────────────────────
        /// <summary>
        /// Optional callback invoked when a screen shake should be triggered.
        /// Parameters: (amplitude in pixels, duration in seconds).
        /// Wired up by GameplayScreen after construction.
        /// </summary>
        public Action<float, float>? OnShakeRequested { get; set; }

        // ── Fall damage tracking ───────────────────────────────────────────────
        // Tracks peak downward velocity (m/s) on landing. Phase 1.6: the peak is
        // only cleared on grounded landing or explicit upward impulse — not when
        // the player briefly enters Climbing/Rappelling/Swinging/WallClinging
        // mid-fall, which previously let players "cheese" past damage thresholds
        // by tapping a rope or grabbing a wall during a long drop.
        private float _peakFallVelMs = 0f;
        private const float SafeImpactMs   = MovementTuning.SafeImpactMs;
        private const float DamagePerMs    = MovementTuning.DamagePerMs;
        private const float LethalImpactMs = MovementTuning.LethalImpactMs;

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Create the player at the given pixel-space spawn position.
        /// </summary>
        public Player(AetherWorld world, Vector2 spawnPixelPosition)
        {
            // Initialize inventory and debuff system
            Inventory = new Inventory();
            Debuffs   = new DebuffSystem();

            // Wire inventory weight changes to physics body
            Inventory.OnWeightChanged += OnInventoryWeightChanged;

            Body = BodyFactory.CreatePlayerBody(world, spawnPixelPosition);
            Body.Tag = this; // back-reference for collision callbacks

            // Find and wire up the foot sensor fixture
            foreach (var fixture in Body.FixtureList)
            {
                if (fixture.Tag is string tag && tag == "foot")
                {
                    FootSensor = fixture;
                    break;
                }
            }

            // Register collision callbacks for ground detection only
            // Wall detection is now done via raycasts in PlayerController
            Body.OnCollision   += OnCollision;
            Body.OnSeparation  += OnSeparation;
        }

        // ── Position helpers ───────────────────────────────────────────────────

        /// <summary>Player center position in pixel space.</summary>
        public Vector2 PixelPosition
            => PhysicsManager.ToPixels(Body.Position);

        /// <summary>Player velocity in pixel space per second.</summary>
        public Vector2 PixelVelocity
            => PhysicsManager.ToPixels(Body.LinearVelocity);

        /// <summary>Axis-aligned bounding rectangle in pixel space.</summary>
        public Rectangle PixelBounds => new Rectangle(
            (int)(PixelPosition.X - WidthPx        / 2f),
            (int)(PixelPosition.Y - CurrentHeightPx / 2f),
            (int)WidthPx,
            (int)CurrentHeightPx);

        // ── State machine ──────────────────────────────────────────────────────

        /// <summary>
        /// Transition to a new state. Applies body configuration for the new state.
        /// </summary>
        public void SetState(PlayerState newState)
        {
            if (State == newState) return;
            if (State == PlayerState.Dead) return; // dead is terminal

            bool wasCrouching = State == PlayerState.Crouching;
            bool willCrouch   = newState == PlayerState.Crouching;

            State = newState;
            ApplyStateBodyConfig();

            // Resize hitbox when entering/leaving the crouch state
            if (willCrouch && !wasCrouching)
                RebuildHitbox(CrouchHeightPx);
            else if (wasCrouching && !willCrouch)
                RebuildHitbox(StandingHeightPx);

            // Phase 1.6: only clear peak fall velocity on states that are
            // genuinely a "safe stop" (Stunned/Dead end the fall outright; the
            // grounded auto-transition in Update() resets it after damage is
            // resolved). Climbing/Rappelling/Swinging/WallClinging used to
            // reset the peak — that allowed a brief rope-tap mid-fall to skip
            // damage entirely. Now those states preserve the peak so when the
            // player drops back off, damage is still owed.
            if (newState == PlayerState.Stunned || newState == PlayerState.Dead)
            {
                _peakFallVelMs = 0f;
            }
            // An upward impulse (jump or wall-jump) genuinely interrupts the fall.
            else if (newState == PlayerState.Jumping || newState == PlayerState.WallJumping)
            {
                _peakFallVelMs = 0f;
            }

            // Reset launch timer when entering Launching state
            if (newState == PlayerState.Launching)
                _launchTimer = LaunchDuration;
        }

        private void ApplyStateBodyConfig()
        {
            switch (State)
            {
                case PlayerState.Idle:
                case PlayerState.Walking:
                case PlayerState.Crouching:
                case PlayerState.Jumping:
                case PlayerState.Falling:
                case PlayerState.WallJumping:
                case PlayerState.Stunned:
                case PlayerState.Dead:
                case PlayerState.ThrowingFlare:
                    Body.IgnoreGravity = false;
                    Body.LinearDamping = 0f;
                    break;

                case PlayerState.WallClinging:
                    // Freeze against the wall — gravity off, full damping
                    Body.IgnoreGravity = true;
                    Body.LinearDamping = 99f;
                    Body.LinearVelocity = Vector2.Zero;
                    break;

                case PlayerState.Mantling:
                    // Freeze physics during mantle pull-up animation
                    Body.IgnoreGravity = true;
                    Body.LinearDamping = 99f;
                    Body.LinearVelocity = Microsoft.Xna.Framework.Vector2.Zero;
                    break;

                case PlayerState.Launching:
                    // Phase 1.7: disable world gravity entirely and apply our own
                    // reduced-gravity acceleration in Update(). Eliminates the
                    // fragile "must match PhysicsManager.Gravity" duplication.
                    Body.IgnoreGravity = true;
                    Body.LinearDamping = 0f;
                    break;

                case PlayerState.Climbing:
                    Body.IgnoreGravity = true;
                    Body.LinearDamping = 8f; // high damping so player doesn't slide off
                    Body.LinearVelocity = Vector2.Zero;
                    break;

                case PlayerState.Sliding:
                    Body.IgnoreGravity = false;
                    Body.LinearDamping = 0.2f; // low damping for smooth slide
                    break;

                case PlayerState.Rappelling:
                    Body.IgnoreGravity = false;
                    Body.LinearDamping = 1f;
                    break;

                case PlayerState.Swinging:
                    Body.IgnoreGravity = false;
                    Body.LinearDamping = 0.1f;
                    break;

                case PlayerState.Controlling:
                    // Freeze the player in place while possessing an entity.
                    // IgnoreGravity keeps them suspended if they were mid-air or on a rope.
                    Body.IgnoreGravity  = true;
                    Body.LinearDamping  = 99f;
                    Body.LinearVelocity = Vector2.Zero;
                    break;
            }
        }

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Update player logic (stun timer, death check, state auto-transitions).
        /// Call once per frame before PlayerController.Update().
        /// </summary>
        public void Update(GameTime gameTime, int currentDepth)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Tick contact invulnerability timer
            if (_contactInvulnTimer > 0f) _contactInvulnTimer -= dt;

            // Tick stats (breath, lantern, suffocation)
            Stats.Tick(dt, currentDepth);

            // Death check
            if (!Stats.IsAlive && State != PlayerState.Dead)
            {
                SetState(PlayerState.Dead);
                Body.LinearVelocity = Vector2.Zero;
                return;
            }

            // ── Solid-tile overlap push-out (B2) ──────────────────────────────
            // Safety net: if the player body has tunneled into solid geometry,
            // find the nearest clear position and teleport there.
            CheckSolidOverlap();

            // Stun countdown
            if (State == PlayerState.Stunned)
            {
                _stunTimer -= dt;
                if (_stunTimer <= 0f)
                    SetState(PlayerState.Idle);
                return;
            }

            // ── Launch countdown (2.4) ────────────────────────────────────────
            if (State == PlayerState.Launching)
            {
                _launchTimer -= dt;

                // Phase 1.7: apply our own reduced gravity (single source of truth).
                // IgnoreGravity is set in ApplyStateBodyConfig; we add a manual
                // downward force scaled by LaunchGravityScale.
                Vector2 reducedGravity = PhysicsManager.Gravity * LaunchGravityScale;
                Body.ApplyForce(reducedGravity * Body.Mass);

                if (_launchTimer <= 0f || IsGrounded)
                    SetState(PlayerState.Falling);

                return; // skip normal update during launch
            }

            // ── Mantle countdown (2.2) ─────────────────────────────────────────
            if (State == PlayerState.Mantling)
            {
                _mantleTimer -= dt;
                if (_mantleTimer <= 0f)
                {
                    // Teleport to standing position on top of the ledge
                    Body.Position = PhysicsManager.ToMeters(_mantleTargetPixel);
                    // Small upward pop so chained mantle→jump flows naturally
                    Body.LinearVelocity = PhysicsManager.ToMeters(new Vector2(0f, -40f));
                    Body.IgnoreGravity  = false;
                    Body.LinearDamping  = 0f;
                    _peakFallVelMs = 0f;
                    SetState(PlayerState.Idle);
                }
                return; // skip normal update during mantle
            }

            // Track peak downward impact velocity whenever airborne.
            if (!IsGrounded && Body.LinearVelocity.Y > _peakFallVelMs)
                _peakFallVelMs = Body.LinearVelocity.Y;

            // Auto-transition: WallClinging → Falling when wall contact is lost
            if (State == PlayerState.WallClinging && !IsTouchingWall)
            {
                SetState(PlayerState.Falling);
            }

            // Auto-transition: Idle ↔ Falling based on vertical velocity
            if (State == PlayerState.Idle || State == PlayerState.Walking ||
                State == PlayerState.Crouching || State == PlayerState.ThrowingFlare)
            {
                if (!IsGrounded && Body.LinearVelocity.Y > 0.5f)
                    SetState(PlayerState.Falling);
            }
            else if (State == PlayerState.Falling
                  || State == PlayerState.Jumping
                  || State == PlayerState.WallJumping)
            {
                if (IsGrounded)
                {
                    ResolveFallDamage();
                    SetState(PlayerState.Idle);
                }
            }
        }

        // ── Fall damage resolution ─────────────────────────────────────────────

        private void ResolveFallDamage()
        {
            float peak = _peakFallVelMs;
            _peakFallVelMs = 0f;

            if (peak <= SafeImpactMs) return;

            float damage = (peak - SafeImpactMs) * DamagePerMs;
            Stats.TakeDamage(damage);

            // ── Screen shake on fall damage (3.2) ─────────────────────────────
            // Amplitude scales with damage: 2px at minimum, 6px at lethal impact
            float shakeAmp = MathHelper.Clamp(
                2f + (peak - SafeImpactMs) / (LethalImpactMs - SafeImpactMs) * 4f,
                2f, 6f);
            OnShakeRequested?.Invoke(shakeAmp, 0.30f);

            if (peak >= LethalImpactMs)
                Stun(0.4f);
        }

        // ── Stun ───────────────────────────────────────────────────────────────

        /// <summary>Apply a stun effect for the given duration in seconds.</summary>
        public void Stun(float durationSeconds)
        {
            if (State == PlayerState.Dead) return;
            _stunTimer = durationSeconds;
            SetState(PlayerState.Stunned);
            Body.LinearVelocity = Vector2.Zero;

            // ── Screen shake on stun hit (3.2) ────────────────────────────────
            // Sharp horizontal shake: 5px amplitude, 0.2s duration
            OnShakeRequested?.Invoke(5f, 0.20f);
        }

        // ── Contact damage (entity collision) ─────────────────────────────────

        /// <summary>
        /// Apply contact damage from a hostile entity.
        /// Respects the invulnerability window to prevent rapid multi-hits.
        /// Also triggers a stun if stunDuration > 0.
        /// </summary>
        public void ApplyContactDamage(float damage, float stunDuration, string? source = null)
        {
            if (IsContactInvulnerable || State == PlayerState.Dead) return;
            Stats.TakeDamage(damage, source);
            if (stunDuration > 0f) Stun(stunDuration);
            _contactInvulnTimer = ContactInvulnDuration;
            OnDamageReceived?.Invoke(damage);
        }

        // ── Launch (2.4) ───────────────────────────────────────────────────────

        /// <summary>
        /// Begin the post-grapple-release launch state.
        /// Applies a 1.2× velocity boost and enters reduced-gravity Launching state
        /// for LaunchDuration seconds.
        /// </summary>
        public void StartLaunch()
        {
            if (State == PlayerState.Dead || State == PlayerState.Stunned) return;

            // Record speed for visual intensity
            LaunchSpeedPx = Math.Abs(PhysicsManager.ToPixels(Body.LinearVelocity.X));

            // Apply 1.2× momentum boost
            Body.LinearVelocity *= 1.2f;

            SetState(PlayerState.Launching);
        }

        // ── Mantle (2.2) ───────────────────────────────────────────────────────

        /// <summary>
        /// Begin a ledge-grab mantle animation.
        /// Freezes physics for MantleDuration seconds, then teleports the player
        /// to targetPixelPos (the standing position on top of the ledge).
        /// Called by PlayerController when ledge-grab conditions are met.
        /// </summary>
        public void StartMantle(Vector2 targetPixelPos)
        {
            if (State == PlayerState.Dead || State == PlayerState.Stunned) return;
            if (State == PlayerState.Mantling) return; // already mantling

            _mantleTargetPixel = targetPixelPos;
            _mantleTimer       = MantleDuration;
            // Peak fall velocity is reset after the mantle completes (player is safely on ledge),
            // not here — resetting early allowed escaping damage by grazing a ledge mid-fall.

            // Reset contact counters before freezing physics to avoid stale state
            ResetContactCounters();

            SetState(PlayerState.Mantling);
        }

        // ── Contact counter reset ──────────────────────────────────────────────

        /// <summary>
        /// Reset all contact counters to zero.
        /// Call when teleporting the player body (mantle, level reload) to prevent
        /// stale contact state from persisting after the body position changes.
        /// </summary>
        /// <summary>
        /// Destroy and recreate the body/foot fixtures at the given height (pixels).
        /// Used to shrink/grow the player for crouch. Updates CurrentHeightPx and
        /// resets ground contact counters (the sensor is recreated, so prior contacts
        /// are stale).
        /// </summary>
        private void RebuildHitbox(float newHeightPx)
        {
            if (MathF.Abs(newHeightPx - CurrentHeightPx) < 0.01f) return;

            // Shift the body center so the foot position stays fixed across the
            // resize. (feet y = center y + halfH; keep that constant.)
            float deltaHalfHpx = (CurrentHeightPx - newHeightPx) * 0.5f;
            Vector2 offsetMeters = PhysicsManager.ToMeters(new Vector2(0f, deltaHalfHpx));
            Body.Position = Body.Position + offsetMeters;

            CurrentHeightPx = newHeightPx;
            BodyFactory.ReplacePlayerFixtures(Body, WidthPx, newHeightPx);

            // Re-wire the foot sensor reference (the old fixture was destroyed)
            FootSensor = null!;
            foreach (var fixture in Body.FixtureList)
            {
                if (fixture.Tag is string tag && tag == "foot")
                {
                    FootSensor = fixture;
                    break;
                }
            }

            ResetContactCounters();
        }

        public void ResetContactCounters()
        {
            _groundContactCount = 0;
            IsTouchingWallLeft  = false;
            IsTouchingWallRight = false;
        }

        // ── Inventory weight ───────────────────────────────────────────────────

        /// <summary>
        /// Update the player's body mass to reflect inventory weight.
        /// Heavier backpack = more inertia, slower rope retraction.
        /// Called automatically when Inventory.OnWeightChanged fires.
        /// Also available as a direct setter for backward compatibility.
        /// </summary>
        public void SetInventoryWeight(float weightKg)
        {
            _inventoryWeightKg = weightKg;
            // Aether mass is set per-fixture via density; we adjust linear damping
            // as a proxy for weight effects on movement feel
            float dampingScale = MathHelper.Clamp(weightKg / 30f, 0f, 1f);
            if (State == PlayerState.Idle || State == PlayerState.Walking)
                Body.LinearDamping = dampingScale * 2f;
        }

        public float InventoryWeightKg => _inventoryWeightKg;

        private void OnInventoryWeightChanged(float newWeight)
        {
            SetInventoryWeight(newWeight);
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw the player as a colored rectangle placeholder.
        /// Call inside a SpriteBatch.Begin/End block with camera transform applied.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, Bloop.Core.AssetManager assets)
        {
            PlayerRenderer.Draw(spriteBatch, assets, this);
        }

        // ── Solid-tile overlap push-out (B2) ───────────────────────────────────

        /// <summary>
        /// Detects whether the player body center is inside a solid tile and, if so,
        /// teleports the player to the nearest clear 2-tile-tall position.
        /// This is a last-resort safety net for tunneling that slips past CCD.
        /// </summary>
        private void CheckSolidOverlap()
        {
            if (_tileMap == null) return;
            // Skip during states where the body is intentionally frozen/teleporting
            if (State == PlayerState.Mantling || State == PlayerState.Dead) return;

            int ts = TileMap.TileSize;
            Vector2 pos = PixelPosition;

            // Convert player center to tile coordinates
            int cx = (int)(pos.X / ts);
            int cy = (int)(pos.Y / ts);

            // Check if the tile at the player center is solid
            if (!TileProperties.IsSolid(_tileMap.GetTile(cx, cy))) return;

            // Player is inside solid geometry — search outward in expanding rings
            // for the nearest tile that has a 2-tile-tall clear column (player height)
            for (int radius = 1; radius <= 8; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        // Only check the ring perimeter
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue;

                        int tx = cx + dx;
                        int ty = cy + dy;

                        // Bounds check
                        if (tx < 1 || tx >= _tileMap.Width  - 1) continue;
                        if (ty < 2 || ty >= _tileMap.Height - 2) continue;

                        // Need 3 clear tiles vertically (player is ~1.875 tiles tall)
                        bool footClear  = !TileProperties.IsSolid(_tileMap.GetTile(tx, ty));
                        bool headClear1 = !TileProperties.IsSolid(_tileMap.GetTile(tx, ty - 1));
                        bool headClear2 = !TileProperties.IsSolid(_tileMap.GetTile(tx, ty - 2));
                        if (!footClear || !headClear1 || !headClear2) continue;

                        // Found a safe position — teleport there and zero velocity
                        Vector2 safePixel = new Vector2(
                            tx * ts + ts / 2f,
                            ty * ts + ts / 2f);
                        Body.Position     = PhysicsManager.ToMeters(safePixel);
                        Body.LinearVelocity = Vector2.Zero;
                        ResetContactCounters();
                        System.Diagnostics.Debug.WriteLine(
                            $"[Player] Push-out: was at tile ({cx},{cy}), moved to ({tx},{ty})");
                        return;
                    }
                }
            }
        }

        // ── Collision callbacks ────────────────────────────────────────────────

        private bool OnCollision(Fixture sender, Fixture other, Contact contact)
        {
            // Foot sensor → ground detection only
            if (sender == FootSensor || other == FootSensor)
            {
                _groundContactCount++;
                return true;
            }

            return true;
        }

        private void OnSeparation(Fixture sender, Fixture other, Contact contact)
        {
            if (sender == FootSensor || other == FootSensor)
            {
                _groundContactCount = Math.Max(0, _groundContactCount - 1);
                return;
            }
        }
    }
}
