namespace Bloop.Audio
{
    /// <summary>
    /// String constants for every sound effect key the gameplay layer uses.
    /// Centralizing them avoids stringly-typed key drift between play sites
    /// and the asset registration in <c>AudioManager.TryLoad</c>.
    ///
    /// Asset files don't need to exist for the build to succeed — calls with
    /// unknown keys are no-ops. Authors can add WAV/OGG files to
    /// <c>Content/Audio/...</c> incrementally.
    /// </summary>
    public static class SfxKeys
    {
        // ── Movement ───────────────────────────────────────────────────────
        public const string FootstepStone   = "footstep_stone";
        public const string FootstepDirt    = "footstep_dirt";
        public const string FootstepCrystal = "footstep_crystal";
        public const string Jump            = "jump";
        public const string LandSoft        = "land_soft";
        public const string LandHard        = "land_hard";
        public const string WallSlide       = "wall_slide";
        public const string WallJumpKick    = "wall_jump_kick";
        public const string MantlePull      = "mantle_pull";

        // ── Grapple / rope ─────────────────────────────────────────────────
        public const string GrappleFire     = "grapple_fire";
        public const string GrappleHit      = "grapple_hit";
        public const string GrappleHitCrystal = "grapple_hit_crystal";
        public const string RopeTaut        = "rope_taut";
        public const string RopeWrap        = "rope_wrap";
        public const string RopeRelease     = "rope_release";
        public const string LaunchWhoosh    = "launch_whoosh";

        // ── Resources / state warnings ─────────────────────────────────────
        public const string LowHealthHeartbeat = "lo_health_heartbeat";
        public const string LowBreathWheeze    = "lo_breath_wheeze";
        public const string LowFuelAlarm       = "lo_fuel_alarm";
        public const string SanityWhisper      = "sanity_whisper";

        // ── Combat / threat ────────────────────────────────────────────────
        public const string DamageHit       = "damage_hit";
        public const string StunHit         = "stun_hit";
        public const string FallDamage      = "fall_damage";
        public const string Death           = "death";
        public const string DebuffApplied   = "debuff_applied";
        public const string PoisonGulp      = "poison_gulp";

        // ── Items ──────────────────────────────────────────────────────────
        public const string ItemPickup      = "item_pickup";
        public const string ItemUse         = "item_use";
        public const string FlareThrow      = "flare_throw";
        public const string FlareImpact     = "flare_impact";

        // ── Entity possession ──────────────────────────────────────────────
        public const string PossessEnter    = "possess_enter";
        public const string PossessExit     = "possess_exit";
        public const string EntitySkill     = "entity_skill";

        // ── UI ─────────────────────────────────────────────────────────────
        public const string UiClick         = "ui_click";
        public const string UiHover         = "ui_hover";
        public const string InventoryOpen   = "inventory_open";
        public const string InventoryClose  = "inventory_close";

        // ── World ──────────────────────────────────────────────────────────
        public const string CaveDrip        = "cave_drip";
        public const string DistantRumble   = "distant_rumble";
        public const string RubbleFall      = "rubble_fall";
    }
}
