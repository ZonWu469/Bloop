namespace Bloop.Gameplay
{
    /// <summary>
    /// Single source of truth for player movement tuning.
    ///
    /// Designers/programmers tune one file. <see cref="PlayerController"/>,
    /// <see cref="Player"/>, and related systems read these values rather than
    /// scattering magic numbers across many files.
    ///
    /// All units:
    ///   - Forces in pixel-space Newtons (the controller converts to meters).
    ///   - Speeds in pixels/second.
    ///   - Impulses in pixel-space (converted to meters at the call site).
    ///   - Times in seconds.
    /// </summary>
    public static class MovementTuning
    {
        // ── Horizontal movement ─────────────────────────────────────────────
        public const float MoveForce              = 750f;
        public const float MaxHorizontalSpeed     = 180f;
        public const float MaxHorizontalSpeedHard = 600f;
        public const float MaxFallSpeedHard       = 800f;
        public const float AirControlMultiplier   = 0.70f;
        public const float CrouchSpeedMultiplier  = 0.30f;

        // ── Jump ─────────────────────────────────────────────────────────────
        public const float JumpImpulse            = 240f;
        public const float CoyoteTimeDuration     = 0.12f;
        public const float JumpBufferDuration     = 0.10f;

        // ── Wall jump / cling ───────────────────────────────────────────────
        public const float WallJumpVertical       = JumpImpulse * 0.60f;
        public const float WallJumpHorizontal     = JumpImpulse * 0.55f;
        public const float WallJumpCooldown       = 0.30f;

        public const float WallRayLength          = 4f;
        /// <summary>Time of continuous contact required before reporting "on wall." Eliminates phantom flicker.</summary>
        public const float WallContactRequired    = 0.030f;
        /// <summary>Time after losing contact during which we still report "on wall."</summary>
        public const float WallReleaseGrace       = 0.080f;
        public const float WallClingCoyoteDuration = 0.08f;
        /// <summary>If the player has drifted further than this since last cling, the wall-cling coyote is invalid.</summary>
        public const float WallClingCoyoteMaxDriftPx = 12f;

        public const float WallSlideMaxDamping        = 20f;
        public const float WallSlideRampTime          = 0.5f;
        public const float WallClingVelocityThreshold = 10f;
        public const float WallClingTimerThreshold    = 0.6f;
        public const float WallClimbSpeed             = 40f;

        // ── Climbing / mantle ───────────────────────────────────────────────
        public const float ClimbSpeed             = 100f;
        public const float MantleHeadTolerance    = 18f;
        public const float MantleDuration         = 0.35f;

        // ── Slope / sliding ─────────────────────────────────────────────────
        public const float SlideAngleThreshold    = 20f;

        // ── Corner correction ───────────────────────────────────────────────
        public const float MaxCornerNudgePx       = 6f;

        // ── Fall damage ─────────────────────────────────────────────────────
        public const float SafeImpactMs           = 11f;
        public const float DamagePerMs            = 10f;
        public const float LethalImpactMs         = 20f;

        // ── Launch (post-grapple boost) ─────────────────────────────────────
        public const float LaunchDuration         = 0.30f;
        public const float LaunchGravityScale     = 0.30f;

        // ── Rope/grapple input buffer ───────────────────────────────────────
        public const float RopeActionBufferDuration = 0.08f;

        // ── Gamepad ─────────────────────────────────────────────────────────
        public const float GamepadStickDeadzone   = 0.18f;
        /// <summary>Below this raw stick magnitude, walk speed scales linearly with deflection.</summary>
        public const float GamepadWalkThreshold   = 0.55f;
    }
}
