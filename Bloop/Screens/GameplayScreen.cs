using System;
using System.Collections.Generic;
using Bloop.Core;
using Bloop.Effects;
using Bloop.Entities;
using Bloop.Gameplay;
using Bloop.Generators;
using Bloop.Lore;
using Bloop.Lighting;
using Bloop.Objects;
using Bloop.Physics;
using Bloop.Rendering;
using Bloop.SaveLoad;
using Bloop.UI;
using Bloop.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Bloop.Screens
{
    /// <summary>
    /// Main gameplay screen. Orchestrates all gameplay systems:
    /// physics, level, player, camera, rope, grapple, momentum, lighting, debug draw.
    ///
    /// Rendering pipeline (Phase 9 multi-pass):
    ///   1. LightingSystem.BeginScene()   — redirect world draw to sceneTarget
    ///   2. Draw world (level + player)   — captured in sceneTarget
    ///   3. LightingSystem.EndScene()     — restore virtual render target
    ///   4. LightingSystem.RenderLightMap — draw radial gradients to lightTarget
    ///   5. LightingSystem.Composite()    — shader: scene * lightMap + ambient
    ///   6. Draw HUD                      — screen-space, unaffected by lighting
    /// </summary>
    public class GameplayScreen : Screen
    {
        // ── Run parameters ─────────────────────────────────────────────────────
        public int       Seed      { get; }
        public int       Depth     { get; private set; }
        public SaveData? SaveData  { get; }

        // ── Core systems ───────────────────────────────────────────────────────
        private PhysicsManager?   _physics;
        private Level?            _level;
        private Player?           _player;
        private PlayerController? _controller;
        private Camera?           _camera;
        private PhysicsDebugDraw  _debugDraw = new();

        // ── Gameplay sub-systems ───────────────────────────────────────────────
        private RopeSystem?    _rope;
        private GrapplingHook? _grapple;
        private MomentumSystem? _momentum;

        // ── Particle system (3.1) ──────────────────────────────────────────────
        private ParticleSystem? _particles;

        // ── Audio (Phase 5) ────────────────────────────────────────────────────
        private readonly Bloop.Audio.AudioManager _audio = new();
        // Tracks last frame's PlayerState so we can fire one-shot SFX on transitions.
        private PlayerState _audioPrevState = PlayerState.Idle;
        // Throttle low-resource warnings so they don't loop every frame.
        private float _lowFuelAlarmCooldown = 0f;
        private float _lowBreathCooldown   = 0f;

        // ── Player trail effect (3.5) ──────────────────────────────────────────
        private readonly TrailEffect _trail = new TrailEffect();

        // ── Landing detection (for landing dust) ───────────────────────────────
        private PlayerState _prevPlayerState = PlayerState.Idle;
        private float       _prevFallSpeedPx = 0f;

        // ── HUD smooth display fractions (Task 9) ─────────────────────────────
        private float _displayLantern = 1f;
        private float _displayBreath  = 1f;
        private float _displayHealth  = 1f;
        private float _displayKinetic = 0f;

        // ── Sanity display & hallucination effects ────────────────────────────
        private float _displaySanity      = 1f;
        private float _chromaticIntensity = 0f;
        private float _heartbeatTimer     = 0f;
        private RenderTarget2D? _postProcessTarget;

        // ── Shard counter pulse (Task 10) ──────────────────────────────────────
        private int   _lastShardCount  = 0;
        private float _shardPulseTimer = 0f;
        private const float ShardPulseDuration = 0.6f;

        // ── Damage flash (Task 13.1) ───────────────────────────────────────────
        private float _damageFlashTimer = 0f;
        private const float DamageFlashDuration = 0.15f;

        // ── Level-transition flash (Phase 6.4) ────────────────────────────────
        private float _levelTransitionTimer = 0f;
        private const float LevelTransitionDuration = 0.45f;

        // ── Contextual prompt (Task 12) ────────────────────────────────────────
        private float  _promptFadeTimer = 0f;
        private string _promptText      = "";
        private const float PromptFadeSpeed = 4f;

        // ── Lighting ───────────────────────────────────────────────────────────
        /// <summary>The player's lantern — a permanent light that tracks player position.</summary>
        private LightSource? _lanternLight;

        // ── Lantern warm-up ────────────────────────────────────────────────────
        /// <summary>Remaining warm-up time when entering a new level (lantern fades in).</summary>
        private float _lanternWarmupTimer = 1.5f;
        private const float LanternWarmupDuration = 1.5f;

        // ── Inventory UI ───────────────────────────────────────────────────────
        private readonly InventoryUI _inventoryUI = new();

        // ── Minimap ────────────────────────────────────────────────────────────
        private readonly Minimap _minimap = new();

        // ── Entity control system ──────────────────────────────────────────────
        private EntityControlSystem? _entityControl;
        private EntityControlHUD?    _entityControlHUD;

        // ── Hover tooltip ──────────────────────────────────────────────────────
        private readonly HoverTooltip _hoverTooltip = new();

        // ── Active flares ──────────────────────────────────────────────────────
        private readonly List<FlareObject> _activeFlares = new();

        // ── Reusable reaction-light list (avoids per-frame List allocation) ────
        private readonly List<LightSource> _reactionLights = new();

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color BgColor   = new Color(4, 6, 10);
        private static readonly Color TextColor = new Color(180, 200, 220);

        // ── Lantern light constants ────────────────────────────────────────────
        private const float LanternBaseRadius    = 260f;  // increased from 180 → 260 (~8 tiles)
        private const float LanternBaseIntensity = 2.4f;  // increased from 1.9 → 2.4
        private static readonly Color LanternColor = new Color(255, 220, 150);

        // ── Constructor ────────────────────────────────────────────────────────
        public GameplayScreen(int seed, int startDepth = 1, SaveData? saveData = null)
        {
            Seed     = seed;
            Depth    = startDepth;
            SaveData = saveData;
        }

        public override bool BlocksDraw   => true;
        public override bool BlocksUpdate => true;

        // ── LoadContent ────────────────────────────────────────────────────────
        public override void LoadContent()
        {
            // ── Audio (Phase 5): try-load each SFX; missing files are no-ops ───
            // Asset names follow Content/Audio/<key>. Keep registrations even when
            // files don't exist yet so adding a WAV later "just works."
            var content = Game1.Instance.Content;
            string a(string n) => "Audio/" + n;
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.FootstepStone,   a("footstep_stone"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.Jump,            a("jump"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.LandSoft,        a("land_soft"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.LandHard,        a("land_hard"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.WallJumpKick,    a("wall_jump"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.MantlePull,      a("mantle"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.GrappleFire,     a("grapple_fire"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.GrappleHit,      a("grapple_hit"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.GrappleHitCrystal, a("grapple_hit_crystal"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.RopeRelease,     a("rope_release"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.LaunchWhoosh,    a("launch_whoosh"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.DamageHit,       a("damage_hit"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.StunHit,         a("stun_hit"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.FallDamage,      a("fall_damage"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.Death,           a("death"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.DebuffApplied,   a("debuff_applied"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.LowHealthHeartbeat, a("lo_health_heartbeat"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.LowBreathWheeze,    a("lo_breath_wheeze"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.LowFuelAlarm,       a("lo_fuel_alarm"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.ItemPickup,       a("item_pickup"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.ItemUse,          a("item_use"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.FlareThrow,       a("flare_throw"));
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.PossessEnter,     a("possess_enter"), Bloop.Audio.AudioBus.Sfx);
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.PossessExit,      a("possess_exit"), Bloop.Audio.AudioBus.Sfx);
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.UiClick,          a("ui_click"), Bloop.Audio.AudioBus.Ui);
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.InventoryOpen,    a("inventory_open"), Bloop.Audio.AudioBus.Ui);
            _audio.TryLoad(content, Bloop.Audio.SfxKeys.InventoryClose,   a("inventory_close"), Bloop.Audio.AudioBus.Ui);

            // ── Physics world ──────────────────────────────────────────────────
            _physics = new PhysicsManager();

            // ── Camera (created before Level so entities can receive it) ───────
            _camera = new Camera(GraphicsDevice.Viewport);

            // ── Level (passes InputManager + Camera so entity constructors work)
            _level = new Level(Depth, Seed, _physics.World,
                ScreenManager.Input, _camera);

            // ── Player ─────────────────────────────────────────────────────────
            _player = new Player(_physics.World, _level.EntryPoint);

            // Restore stats from save if available
            if (SaveData != null)
            {
                _player.Stats.SetFromSave(SaveData.Health, SaveData.BreathMeter, SaveData.LanternFuel, SaveData.Sanity);
                _player.Inventory.LoadFromSave(SaveData.InventoryItems);
            }

            // Wire lore/shard collection event
            _level.OnShardCollected += OnShardCollected;

            // ── Camera bounds (now that TileMap dimensions are known) ──────────
            _camera.SetBounds(
                0f, _level.TileMap.PixelWidth,
                0f, _level.TileMap.PixelHeight);
            _camera.SnapTo(_level.EntryPoint);

            // ── Gameplay sub-systems ───────────────────────────────────────────
            _rope      = new RopeSystem(_physics.World);
            _grapple   = new GrapplingHook(_physics.World);
            _momentum  = new MomentumSystem();

            // ── Particle system (3.1) ──────────────────────────────────────────
            _particles = new ParticleSystem(Seed);
            _particles.SetupEmitters(_level.TileMap);

            // ── Wire tile map into rope systems for terrain-aware wrapping ─────
            _rope.SetTileMap(_level.TileMap);
            _grapple.SetTileMap(_level.TileMap);
            // Wire tile map into player for solid-overlap push-out (B2)
            _player.SetTileMap(_level.TileMap);

            // ── Player controller ──────────────────────────────────────────────
            _controller = new PlayerController(
                _player, ScreenManager.Input, _rope, _grapple, _momentum, _camera);
            _controller.SetTileMap(_level.TileMap);

            // ── Flare throw callback ───────────────────────────────────────────
            _controller.OnFlareThrown = (spawnPos, velocity) =>
            {
                if (_level == null || _physics == null) return;
                var flare = new FlareObject(spawnPos, velocity,
                    _physics.World, Game1.Lighting, _level);
                _level.AddObject(flare);
                _activeFlares.Add(flare);
            };

            // ── Entity control system ──────────────────────────────────────────
            _entityControl    = new EntityControlSystem(_player, _camera, ScreenManager.Input);
            _entityControlHUD = new EntityControlHUD(_entityControl);

            // ── Wire screen shake callbacks (3.2) ──────────────────────────────
            // Player triggers shake on fall damage and stun hits
            _player.OnShakeRequested = (amplitude, duration) =>
                _camera?.Shake(amplitude, duration);
            // MomentumSystem triggers shake on slingshot launch
            _momentum.OnShakeRequested = (amplitude, duration) =>
                _camera?.Shake(amplitude, duration);

            // Grapple hook anchor: small shake + radial particle burst at contact point
            _grapple.OnAnchored += (pos, color) =>
            {
                _camera?.Shake(4f, 0.10f);
                _trail.SpawnGrappleFlash(pos, color);

                // Phase 5: anchor SFX with positional pan + crystal-variant pitch.
                if (_player != null)
                {
                    string key = color.B > 200 ? Bloop.Audio.SfxKeys.GrappleHitCrystal
                                               : Bloop.Audio.SfxKeys.GrappleHit;
                    _audio.PlayAt(key, _player.PixelPosition, pos, 700f, 0.8f);
                }
            };

            // ── Wire damage flash (Task 13.1) ──────────────────────────────────
            _player.OnDamageReceived = (amount) =>
            {
                _damageFlashTimer = DamageFlashDuration;
                // Phase 5: damage SFX scaled by hit magnitude.
                _audio.Play(Bloop.Audio.SfxKeys.DamageHit,
                    volume: MathHelper.Clamp(0.5f + amount / 60f, 0.5f, 1f));
            };

            // Earthquake system shakes the screen during warning/active/aftershock
            if (_level.Earthquake != null)
            {
                _level.Earthquake.OnShakeRequested = (amplitude, duration) =>
                    _camera?.Shake(amplitude, duration);
            }

            // ── Lighting setup ─────────────────────────────────────────────────
            SetupLighting();
        }

        // ── Update ─────────────────────────────────────────────────────────────
        public override void Update(GameTime gameTime)
        {
            var input = ScreenManager.Input;

            // Pause
            if (input.IsPausePressed())
            {
                ScreenManager.Push(new PauseScreen(onSave: SaveGame));
                return;
            }

            // Toggle physics debug draw (F1)
            if (input.IsKeyPressed(Keys.F1) && _debugDraw != null)
                _debugDraw.Enabled = !_debugDraw.Enabled;

            // Toggle lighting (F2)
            if (input.IsKeyPressed(Keys.F2) && Game1.Lighting != null)
                Game1.Lighting.Enabled = !Game1.Lighting.Enabled;

            // Inventory UI update (Tab toggle handled inside InventoryUI.Update)
            if (_player != null)
                _inventoryUI.Update(input, _player.Inventory, _player);

            if (_physics == null || _level == null || _player == null ||
                _controller == null || _camera == null) return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // ── Advance animation clock ────────────────────────────────────────
            AnimationClock.Update(dt);

            // ── Step physics ───────────────────────────────────────────────────
            _physics.Step(dt);

            // ── Update debuff system ───────────────────────────────────────────
            _player.Debuffs.Update(dt);

            // ── Update player ──────────────────────────────────────────────────
            _player.Update(gameTime, Depth);

            // ── Update entity control system (before controller so input mode is set) ──
            if (_entityControl != null)
                _entityControl.Update(gameTime, _level);

            // ── Update controller (input → forces) ─────────────────────────────
            _controller.Update(gameTime);

            // ── Wall friction particles (wall slide) ───────────────────────────
            if (_particles != null &&
                (_player.State == PlayerState.Falling || _player.State == PlayerState.WallClinging) &&
                _player.IsTouchingWall)
            {
                float fallSpeed = MathF.Abs(_player.PixelVelocity.Y);
                if (fallSpeed > 20f)
                {
                    float contactX = _player.IsTouchingWallLeft
                        ? _player.PixelPosition.X - Player.WidthPx / 2f
                        : _player.PixelPosition.X + Player.WidthPx / 2f;
                    _particles.EmitWallFriction(
                        new Vector2(contactX, _player.PixelPosition.Y), fallSpeed);
                }
            }

            // ── Update grapple hook aim target (for aim line rendering) ────────
            if (_grapple != null && _camera != null)
            {
                Vector2 mouseScreen = ScreenManager.Input.GetMouseWorldPosition();
                Vector2 mouseWorld  = _camera.ScreenToWorld(mouseScreen);
                _grapple.UpdateAimTarget(mouseWorld);
            }

            // ── Update grapple hook flight ─────────────────────────────────────
            _grapple?.Update(gameTime);

            // ── Prune expired flares from active list ─────────────────────────
            for (int i = _activeFlares.Count - 1; i >= 0; i--)
                if (_activeFlares[i].IsDestroyed) _activeFlares.RemoveAt(i);

            // ── Build reaction-light list (lantern + active flares) ───────────
            // Reuse the same list instance to avoid per-frame GC allocation.
            _reactionLights.Clear();
            if (_lanternLight != null && _lanternLight.EffectiveIntensity > 0f)
                _reactionLights.Add(_lanternLight);
            foreach (var flare in _activeFlares)
                _reactionLights.Add(flare.Light);

            // ── Update level objects (pass player + reaction lights) ──────────
            _level.Update(gameTime, _player, _reactionLights);

            // ── Update hover tooltip ───────────────────────────────────────────
            _hoverTooltip.Update(input, _camera, _level);

            // ── Update particle system (3.1) ───────────────────────────────────
            if (_particles != null && _camera != null)
            {
                _particles.Update(dt,
                    _camera.GetVisibleBounds(),
                    _player.PixelPosition,
                    _player.PixelVelocity);
            }

            // ── Update player trail effect (3.5) ───────────────────────────────
            _trail.Update(dt, _player);

            // ── Landing dust (transition from aerial to grounded) ───────────────
            bool wasAerial = _prevPlayerState == PlayerState.Falling
                          || _prevPlayerState == PlayerState.Jumping
                          || _prevPlayerState == PlayerState.WallJumping;
            bool justLanded = wasAerial && _player.IsGrounded;
            if (justLanded && _prevFallSpeedPx > 80f)
                _trail.SpawnLandingDust(_player.PixelPosition, _prevFallSpeedPx);

            // ── Phase 5: state-transition audio ───────────────────────────────
            // Fire one-shots when the player enters a state that has its own SFX.
            if (_player.State != _audioPrevState)
            {
                switch (_player.State)
                {
                    case PlayerState.Jumping:
                        _audio.PlayVaried(Bloop.Audio.SfxKeys.Jump, 0.7f);
                        break;
                    case PlayerState.WallJumping:
                        _audio.PlayVaried(Bloop.Audio.SfxKeys.WallJumpKick, 0.8f);
                        break;
                    case PlayerState.Mantling:
                        _audio.Play(Bloop.Audio.SfxKeys.MantlePull, 0.7f);
                        break;
                    case PlayerState.Launching:
                        _audio.Play(Bloop.Audio.SfxKeys.LaunchWhoosh, 0.9f);
                        break;
                    case PlayerState.Stunned:
                        _audio.Play(Bloop.Audio.SfxKeys.StunHit, 1f);
                        break;
                    case PlayerState.Dead:
                        _audio.Play(Bloop.Audio.SfxKeys.Death, 1f);
                        break;
                }
                // Land transition: any aerial → grounded state with non-trivial fall speed.
                if (justLanded)
                {
                    string key = _prevFallSpeedPx > 380f
                        ? Bloop.Audio.SfxKeys.LandHard
                        : Bloop.Audio.SfxKeys.LandSoft;
                    _audio.PlayVaried(key, MathHelper.Clamp(_prevFallSpeedPx / 500f, 0.4f, 1f));
                }
                _audioPrevState = _player.State;
            }

            // ── Phase 5: low-resource warnings (throttled) ────────────────────
            if (_lowFuelAlarmCooldown > 0f)  _lowFuelAlarmCooldown -= dt;
            if (_lowBreathCooldown   > 0f)   _lowBreathCooldown   -= dt;

            float fuelFrac   = _player.Stats.LanternFuel / PlayerStats.MaxLanternFuel;
            float breathFrac = _player.Stats.Breath      / PlayerStats.MaxBreath;
            if (fuelFrac < 0.15f && _lowFuelAlarmCooldown <= 0f)
            {
                _audio.Play(Bloop.Audio.SfxKeys.LowFuelAlarm, 0.5f);
                _lowFuelAlarmCooldown = 4f;
            }
            if (breathFrac < 0.20f && _lowBreathCooldown <= 0f)
            {
                _audio.Play(Bloop.Audio.SfxKeys.LowBreathWheeze, 0.6f);
                _lowBreathCooldown = 2.5f;
            }

            _prevPlayerState = _player.State;
            _prevFallSpeedPx = MathF.Max(_player.PixelVelocity.Y, 0f);

            // ── Update lighting system ─────────────────────────────────────────
            Game1.Lighting?.Update(gameTime);
            UpdateLighting(dt);

            // ── Reveal map tiles within lantern radius ─────────────────────────
            if (_lanternLight != null && _lanternLight.Radius > 0f)
                _level.RevealAround(_player.PixelPosition, _lanternLight.Radius);

            // ── Camera follow ──────────────────────────────────────────────────
            // While controlling an entity, track the entity; otherwise track the player.
            // The LuminousIsopod special case (IsIsopodAttached) keeps IsControlling=false
            // since the isopod rides with the player, so the camera correctly stays on the player.
            Vector2 cameraTarget = (_entityControl != null &&
                                    _entityControl.IsControlling &&
                                    _entityControl.ActiveEntity != null)
                ? _entityControl.ActiveEntity.PixelPosition
                : _player.PixelPosition;

            // Lookahead: bias toward player velocity so they can see where they're going
            _camera.SetLookahead(_player.PixelVelocity);
            _camera.Follow(cameraTarget, gameTime);

            // ── Smooth HUD display fractions (Task 9) ─────────────────────────
            // Phase 4.2: drops snap immediately so players FEEL hits within one
            // frame; only recovery (heals/refills) is lerped for visual polish.
            float actualLantern = _player.Stats.LanternFuel / PlayerStats.MaxLanternFuel;
            float actualBreath  = _player.Stats.Breath      / PlayerStats.MaxBreath;
            float actualHealth  = _player.Stats.Health      / PlayerStats.MaxHealth;
            float actualKinetic = _player.Stats.KineticCharge / PlayerStats.MaxKineticCharge;
            _displayLantern = SnapDownLerpUp(_displayLantern, actualLantern, dt * 8f);
            _displayBreath  = SnapDownLerpUp(_displayBreath,  actualBreath,  dt * 8f);
            _displayHealth  = SnapDownLerpUp(_displayHealth,  actualHealth,  dt * 8f);
            _displayKinetic = MathHelper.Lerp(_displayKinetic, actualKinetic, dt * 8f); // kinetic isn't a damage stat

            // ── Sanity smooth display + hallucination effects ──────────────────
            float actualSanity = _player.Stats.Sanity / PlayerStats.MaxSanity;
            _displaySanity = MathHelper.Lerp(_displaySanity, actualSanity, dt * 6f);
            ApplySanityDebuffs(dt);

            // Passive sanity drain from nearby hostile entities
            if (_level != null)
            {
                float drainRate = 0f;
                foreach (var obj in _level.Objects)
                {
                    if (obj is ControllableEntity entity && !entity.IsDestroyed
                        && entity.DamagesPlayerOnContact)
                    {
                        float dist = Vector2.Distance(_player.PixelPosition, entity.PixelPosition);
                        if (dist < 120f) drainRate += 3f;
                    }
                }
                if (drainRate > 0f)
                    _player.Stats.DrainSanity(drainRate, dt);
            }

            // ── Shard pulse timer (Task 10) ────────────────────────────────────
            if (_level != null)
            {
                int currentShards = _level.ShardsCollected;
                if (currentShards != _lastShardCount)
                {
                    _shardPulseTimer = ShardPulseDuration;
                    _lastShardCount  = currentShards;
                }
                if (_shardPulseTimer > 0f) _shardPulseTimer -= dt;
            }

            // ── Damage flash timer (Task 13.1) ─────────────────────────────────
            if (_damageFlashTimer > 0f) _damageFlashTimer -= dt;
            if (_levelTransitionTimer > 0f) _levelTransitionTimer -= dt;

            // ── Contextual prompt fade (Task 12) ───────────────────────────────
            string newPrompt = GetContextualPrompt();
            if (newPrompt != _promptText)
            {
                _promptText      = newPrompt;
                _promptFadeTimer = 0f;
            }
            if (_promptText.Length > 0)
                _promptFadeTimer = Math.Min(_promptFadeTimer + dt * PromptFadeSpeed, 1f);
            else
                _promptFadeTimer = Math.Max(_promptFadeTimer - dt * PromptFadeSpeed, 0f);

            // ── Death check ────────────────────────────────────────────────────
            if (_player.State == PlayerState.Dead)
            {
                string cause = _player.Stats.LastDamageSource ?? "Ran out of health";
                ScreenManager.Replace(new GameOverScreen(Seed, Depth, cause));
                return;
            }

            // ── Exit check: player reached exit point ──────────────────────────
            float exitDist = Vector2.Distance(_player.PixelPosition, _level.ExitPoint);
            if (exitDist < 40f && _level.IsExitUnlocked)
            {
                // Level 30 is the final level — victory!
                if (Depth >= 30)
                {
                    ScreenManager.Replace(new GameOverScreen(Seed, Depth, "You escaped the caves!"));
                    return;
                }

                // Auto-save and advance to next depth
                SaveGame();
                Depth++;
                ReloadLevel();

                // Phase 6.4: descent feedback — short fade-from-black + downward
                // shake + audio sting, so the level transition feels weighty
                // instead of just teleporting.
                _levelTransitionTimer = LevelTransitionDuration;
                _camera?.Shake(6f, 0.30f, new Vector2(0f, 1f));
                _audio.Play(Bloop.Audio.SfxKeys.DistantRumble, 0.7f);
            }
        }

        // ── Draw ───────────────────────────────────────────────────────────────
        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            var assets = Game1.Assets;
            var lighting = Game1.Lighting;
            int vw = GraphicsDevice.Viewport.Width;
            int vh = GraphicsDevice.Viewport.Height;

            if (_camera == null || _level == null || _player == null)
            {
                // Fallback: loading placeholder
                spriteBatch.Begin();
                assets.DrawRect(spriteBatch, new Rectangle(0, 0, vw, vh), BgColor);
                assets.DrawStringCentered(spriteBatch, "Loading...", vh / 2f, TextColor, 1f);
                spriteBatch.End();
                return;
            }

            if (lighting != null)
            {
                // ── Multi-pass lighting pipeline ───────────────────────────────

                // Pass 1: Capture world to sceneTarget
                lighting.BeginScene();

                spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    null, null, null,
                    _camera.GetTransform());

                DrawWorld(spriteBatch, assets);

                spriteBatch.End();

                lighting.EndScene();

                // Pass 2: Render light map (radial gradients, additive)
                lighting.RenderLightMap(spriteBatch, _camera.GetTransform());

                // Pass 3: Composite scene × lightMap + ambient
                // When chromatic aberration is active, composite to intermediate target
                // then apply the CA shader on the way to the backbuffer.
                var caEffect = Game1.ChromaticEffect;
                bool useCA   = _chromaticIntensity > 0.01f && caEffect != null;

                if (useCA)
                {
                    // Lazy-recreate post-process target if size changed
                    if (_postProcessTarget == null
                        || _postProcessTarget.Width  != vw
                        || _postProcessTarget.Height != vh)
                    {
                        _postProcessTarget?.Dispose();
                        _postProcessTarget = new RenderTarget2D(GraphicsDevice, vw, vh,
                            false, GraphicsDevice.PresentationParameters.BackBufferFormat,
                            DepthFormat.None);
                    }

                    lighting.Composite(spriteBatch, _postProcessTarget);

                    // Restore backbuffer and apply CA shader
                    GraphicsDevice.SetRenderTarget(null);
                    caEffect!.Parameters["Intensity"]?.SetValue(_chromaticIntensity);
                    caEffect.Parameters["ViewportSize"]?.SetValue(new Vector2(vw, vh));
                    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque,
                        SamplerState.LinearClamp, null, null, caEffect);
                    spriteBatch.Draw(_postProcessTarget,
                        new Rectangle(0, 0, vw, vh), Color.White);
                    spriteBatch.End();
                }
                else
                {
                    lighting.Composite(spriteBatch);
                }
            }
            else
            {
                // ── Fallback: no lighting system, draw world directly ──────────
                spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    null, null, null,
                    _camera.GetTransform());

                DrawWorld(spriteBatch, assets);

                spriteBatch.End();
            }

            // ── Screen-space HUD + Inventory UI (no camera transform, no lighting) ──
            spriteBatch.Begin();
            DrawHUD(spriteBatch, assets, vw, vh);
            // Contextual prompts (world-space text drawn in screen pass, near player)
            if (_player != null && _camera != null && _promptFadeTimer > 0.02f)
                DrawContextualPrompt(spriteBatch, assets, vw, vh);
            // Entity control HUD (Q button, cooldown ring, control bar, skill pips, edge arrows)
            _entityControlHUD?.Draw(spriteBatch, assets, vw, vh, _camera, _level);
            // Minimap (bottom-right corner)
            if (_level != null && _player != null)
                _minimap.Draw(spriteBatch, assets, _level, _player.PixelPosition, vw, vh);
            // Inventory panel (drawn on top of HUD if open)
            if (_player != null)
                _inventoryUI.Draw(spriteBatch, assets,
                    _player.Inventory, _player.Debuffs, vw, vh);
            // Hover tooltip (drawn last so it sits on top of everything)
            _hoverTooltip.Draw(spriteBatch, assets, vw, vh);
            spriteBatch.End();
        }

        // ── UnloadContent ──────────────────────────────────────────────────────
        public override void UnloadContent()
        {
            // Expire active flares before disposing the level
            foreach (var f in _activeFlares) f.ExpireNow();
            _activeFlares.Clear();

            // Remove the lantern light from the global lighting system
            if (_lanternLight != null)
                Game1.Lighting?.RemoveLight(_lanternLight);

            _level?.Dispose();
            _physics?.Dispose();
            _postProcessTarget?.Dispose();
            _postProcessTarget = null;
        }

        // ── Save helper ────────────────────────────────────────────────────────
        public void SaveGame()
        {
            if (_player == null) return;

            var data = new SaveData
            {
                Seed           = Seed,
                CurrentDepth   = Depth,
                Health         = _player.Stats.Health,
                BreathMeter    = _player.Stats.Breath,
                LanternFuel    = _player.Stats.LanternFuel,
                Sanity         = _player.Stats.Sanity,
                InventoryItems = _player.Inventory.ToSaveData(),
            };
            SaveManager.Save(data);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        // ── Contextual prompt helper (Task 12) ────────────────────────────────
        private string GetContextualPrompt()
        {
            if (_player == null || _level == null) return "";

            // Single pass over objects; track best result per priority level.
            // Priority 1 > 2 > 3 — return early if highest priority is found.
            bool entityControlReady = _entityControl != null
                && _entityControl.IsReady && !_entityControl.IsControlling;

            string? entityPrompt = null;
            string? climbPrompt  = null;
            string? ventPrompt   = null;

            Vector2 playerPos = _player.PixelPosition;

            foreach (var obj in _level.Objects)
            {
                if (obj.IsDestroyed) continue;

                if (entityPrompt == null && entityControlReady &&
                    obj is Entities.ControllableEntity ent && !ent.IsControlled)
                {
                    if (Vector2.Distance(playerPos, ent.PixelPosition) < Entities.EntityControlSystem.SelectionRange)
                    {
                        entityPrompt = "[Q] Control Entity";
                        break; // highest priority — no need to scan further
                    }
                }
                else if (climbPrompt == null && obj is Objects.ClimbableSurface cs)
                {
                    if (Vector2.Distance(playerPos, cs.PixelPosition) < 40f)
                        climbPrompt = "[C] Climb";
                }
                else if (ventPrompt == null && obj is Objects.VentFlower vf)
                {
                    if (Vector2.Distance(playerPos, vf.PixelPosition) < 48f)
                        ventPrompt = "Stand to Recharge";
                }
            }

            return entityPrompt ?? climbPrompt ?? ventPrompt ?? "";
        }

        private void DrawContextualPrompt(SpriteBatch sb, AssetManager assets, int vw, int vh)
        {
            if (_player == null || _camera == null || _promptText.Length == 0) return;

            // Convert player head position to screen space
            Vector2 worldHead = _player.PixelPosition - new Vector2(0, 28f);
            Vector2 screenPos = _camera.WorldToScreen(worldHead);

            float alpha = _promptFadeTimer;
            if (assets.GameFont == null) return;

            Vector2 textSize = assets.GameFont.MeasureString(_promptText) * 0.75f;
            float tx = screenPos.X - textSize.X / 2f;
            float ty = screenPos.Y - textSize.Y - 4f;

            // Clamp to screen
            tx = Math.Clamp(tx, 4f, vw - textSize.X - 4f);
            ty = Math.Clamp(ty, 4f, vh - textSize.Y - 4f);

            // Background pill
            var bgRect = new Rectangle((int)tx - 6, (int)ty - 3, (int)textSize.X + 12, (int)textSize.Y + 6);
            assets.DrawRect(sb, bgRect, new Color(8, 12, 18, (int)(180 * alpha)));
            assets.DrawRectOutline(sb, bgRect, new Color(60, 90, 110, (int)(160 * alpha)), 1);

            assets.DrawString(sb, _promptText, new Vector2(tx, ty),
                new Color(180, 220, 240, (int)(220 * alpha)), 0.75f);
        }

        /// <summary>Draw all world-space content (level, rope, grapple, player, debug).</summary>
        private void DrawWorld(SpriteBatch spriteBatch, AssetManager assets)
        {
            // Update shared per-frame data for EntityRenderer (danger + culling)
            if (_player != null)
                Rendering.EntityRenderer.PlayerPositionForDanger = _player.PixelPosition;
            Rendering.EntityRenderer.VisibleBounds = _camera!.GetVisibleBounds();

            // Level (background + tiles + objects)
            _level!.Draw(spriteBatch, assets, _camera!.GetVisibleBounds());

            // Ambient particles (3.1) — drawn after level tiles, before player
            _particles?.Draw(spriteBatch, assets);

            // Player trail (3.5) — drawn before player so it appears behind
            _trail.Draw(spriteBatch, assets);

            // Rope
            _rope?.Draw(spriteBatch, assets, _player!);

            // Grapple
            _grapple?.Draw(spriteBatch, assets, _player!);

            // Player
            _player!.Draw(spriteBatch, assets);

            // Flare trajectory arc (world space, drawn when player is in throw stance)
            if (_player?.State == PlayerState.ThrowingFlare &&
                _controller?.FlareTrajectoryPoints != null)
            {
                WorldObjectRenderer.DrawFlareTrajectory(spriteBatch, assets,
                    _controller.FlareTrajectoryPoints);
            }

            // Entity selection range circle (world space, drawn over player)
            if (_entityControl != null && _entityControl.IsSelecting && _player != null)
                EntityRenderer.DrawSelectionRangeCircle(spriteBatch, assets, _player.PixelPosition, 200f);

            // Skill effect range circle around controlled entity (world space)
            if (_entityControl != null && (_entityControl.IsControlling || _entityControl.IsIsopodAttached)
                && _entityControl.ActiveEntity != null)
            {
                var e = _entityControl.ActiveEntity;
                EntityRenderer.DrawSkillRangeCircle(spriteBatch, assets,
                    e.PixelPosition, e.GetEffectRadius());
            }

            // Physics debug overlay (F1)
            _debugDraw.Draw(spriteBatch, _physics!.World, assets, _camera.GetVisibleBounds());
        }

        // ── Lore / sanity helpers ──────────────────────────────────────────────

        private void OnShardCollected(int shardIndex, LoreEntry entry)
        {
            ScreenManager.Push(new LoreModalScreen(entry,
                sanityDelta => _player?.Stats.ApplySanityDelta(sanityDelta)));
        }

        private void ApplySanityDebuffs(float dt)
        {
            if (_player == null) return;
            float sanity = _player.Stats.Sanity;

            // < 60%: slow movement
            if (sanity < 60f)
                _player.Debuffs.ApplyDebuff(DebuffType.SlowMovement, 2f);

            // < 45%: heartbeat lantern flicker
            if (sanity < 45f && _lanternLight != null)
            {
                _heartbeatTimer += dt;
                float hbPhase  = _heartbeatTimer % 1.1f;  // 1.1s cycle
                float heartbeat = (hbPhase < 0.1f || (hbPhase > 0.3f && hbPhase < 0.4f))
                    ? 0.30f  // strong flicker during "beat"
                    : 0.04f; // normal background flicker
                float blend = 1f - (sanity / 45f);
                _lanternLight.FlickerAmplitude =
                    MathHelper.Lerp(_lanternLight.FlickerAmplitude, heartbeat, blend * 0.3f);
            }

            // < 40%: inverted controls
            if (sanity < 40f)
                _player.Debuffs.ApplyDebuff(DebuffType.InvertedControls, 2f);

            // < 30%: blurred lantern
            if (sanity < 30f)
                _player.Debuffs.ApplyDebuff(DebuffType.Blurred, 2f);

            // < 20%: chromatic aberration ramps up toward 1.0
            float targetCA = sanity < 20f
                ? MathHelper.Lerp(0.25f, 1.0f, 1f - sanity / 20f)
                : 0f;
            _chromaticIntensity = MathHelper.Lerp(_chromaticIntensity, targetCA, dt * 2f);
        }

        /// <summary>Reload the level for the new depth (called on exit).</summary>
        private void ReloadLevel()
        {
            // Expire any active flares before disposing the old level
            foreach (var f in _activeFlares) f.ExpireNow();
            _activeFlares.Clear();

            // Remove level-specific lights (VentFlowers, GlowVines) before disposing
            _level?.RemoveLightsFromSystem(Game1.Lighting);
            _level?.Dispose();
            _level = new Level(Depth, Seed, _physics!.World,
                ScreenManager.Input, _camera);

            // Register new level's lights
            _level.RegisterLightsWithSystem(Game1.Lighting);

            // Wire new tile map into rope systems
            _rope?.SetTileMap(_level.TileMap);
            _grapple?.SetTileMap(_level.TileMap);
            _controller?.SetTileMap(_level.TileMap);
            // Wire new tile map into player for solid-overlap push-out (B2)
            _player?.SetTileMap(_level.TileMap);

            // Re-setup particle emitters for new level (3.1)
            _particles?.SetupEmitters(_level.TileMap);

            // Teleport player to new entry point
            // Reset contact counters first to prevent stale ground/wall state
            // after the body position changes without firing separation events
            _player!.ResetContactCounters();
            _player.Body.Position = PhysicsManager.ToMeters(_level.EntryPoint);
            _player.Body.LinearVelocity = Vector2.Zero;

            _camera!.SetBounds(0f, _level.TileMap.PixelWidth, 0f, _level.TileMap.PixelHeight);
            _camera.SnapTo(_level.EntryPoint);

            // Refill flares for the new level
            _player!.Stats.RefillFlares();

            // Re-wire flare callback so it captures the new level reference
            if (_controller != null)
            {
                _controller.OnFlareThrown = (spawnPos, velocity) =>
                {
                    if (_level == null || _physics == null) return;
                    var flare = new FlareObject(spawnPos, velocity,
                        _physics.World, Game1.Lighting, _level);
                    _level.AddObject(flare);
                    _activeFlares.Add(flare);
                };
            }

            // Re-wire lore event for the new level
            _level.OnShardCollected += OnShardCollected;

            // Re-wire earthquake shake callback for the new level
            if (_level.Earthquake != null)
            {
                _level.Earthquake.OnShakeRequested = (amplitude, duration) =>
                    _camera?.Shake(amplitude, duration);
            }

            // Reset lantern warm-up for cinematic entrance
            _lanternWarmupTimer = LanternWarmupDuration;

            // Update ambient level and color grading for new depth
            if (Game1.Lighting != null)
            {
                Game1.Lighting.AmbientLevel   = GetAmbientForDepth(Depth);
                Game1.Lighting.ColorGradeTint  = GetColorGradeForBiome(_level.Biome);
            }
        }

        /// <summary>Set up the initial lighting state for this gameplay session.</summary>
        private void SetupLighting()
        {
            var lighting = Game1.Lighting;
            if (lighting == null || _player == null) return;

            // Clear any lights from a previous session
            lighting.ClearLights();

            // Create the player's lantern light (permanent, tracks player position)
            _lanternLight = new LightSource(
                _player.PixelPosition,
                LanternBaseRadius,
                LanternBaseIntensity,
                LanternColor);
            lighting.AddLight(_lanternLight);

            // Set ambient level based on starting depth
            lighting.AmbientLevel = GetAmbientForDepth(Depth);

            // ── 3.3: Depth-based color grading tint ───────────────────────────
            lighting.ColorGradeTint = GetColorGradeForBiome(_level?.Biome ?? BiomeTier.ShallowCaves);

            // Register level lights (VentFlowers, etc.)
            _level?.RegisterLightsWithSystem(lighting);
        }

        /// <summary>
        /// Returns the color grading tint for a given biome tier (3.3).
        /// Applied as a multiply pass over the final composited frame.
        /// </summary>
        private static Color GetColorGradeForBiome(BiomeTier biome) => biome switch
        {
            BiomeTier.ShallowCaves  => new Color(255, 240, 210), // warm amber
            BiomeTier.FungalGrottos => new Color(200, 255, 220), // cool green/teal
            BiomeTier.CrystalDepths => new Color(200, 210, 255), // cold blue/purple
            BiomeTier.TheAbyss      => new Color(255, 190, 190), // deep red
            _                       => Color.White,
        };

        /// <summary>Update lighting state each frame.</summary>
        private void UpdateLighting(float dt)
        {
            var lighting = Game1.Lighting;
            if (lighting == null || _player == null) return;

            // Update the lighting system (ticks temporary lights, removes expired ones)
            // Note: GameTime is not available here, so we call Update via gameTime in the main Update
            // The actual Update(GameTime) call is done in the main Update method above

            // Update lantern light position and properties based on player stats
            if (_lanternLight != null)
            {
                float fuelFraction = _player.Stats.LanternFuel / PlayerStats.MaxLanternFuel;

                if (_player.Stats.HasLanternFuel)
                {
                    // Radius shrinks as fuel depletes (min 80px so it never feels cramped)
                    float baseRadius = 80f + (LanternBaseRadius - 80f) * fuelFraction;

                    // Apply Blurred debuff: reduces lantern radius by 30%
                    float blurModifier = _player.Debuffs.GetModifier(DebuffType.Blurred);
                    _lanternLight.Radius = baseRadius * blurModifier;

                    // Intensity also dims slightly with fuel
                    _lanternLight.Intensity = 0.7f + (LanternBaseIntensity - 0.7f) * fuelFraction;

                    // ── Flicker (3.6) ──────────────────────────────────────────
                    // Low fuel → stronger flicker and sputtering
                    if (fuelFraction < 0.20f)
                    {
                        _lanternLight.FlickerAmplitude = 0.15f;   // ±15% radius
                        _lanternLight.FlickerFrequency = 10f;
                        _lanternLight.SputterChance    = 0.30f;   // 30% per second
                    }
                    else
                    {
                        _lanternLight.FlickerAmplitude = 0.04f;   // ±4% subtle flicker
                        _lanternLight.FlickerFrequency = 9f + fuelFraction * 3f; // 9–12 Hz
                        _lanternLight.SputterChance    = 0f;
                    }

                    // ── Sway (3.6) ─────────────────────────────────────────────
                    // Lantern lags slightly behind player movement direction
                    Vector2 vel = _player.PixelVelocity;
                    float swayX = -vel.X * 0.025f; // opposite to movement
                    float swayY = -vel.Y * 0.010f;
                    // Clamp sway to ±4px
                    swayX = MathHelper.Clamp(swayX, -4f, 4f);
                    swayY = MathHelper.Clamp(swayY, -4f, 4f);
                    _lanternLight.SwayOffset = new Vector2(swayX, swayY);

                    // ── Warm-up (3.6) ──────────────────────────────────────────
                    // On level entry the lantern fades in over LanternWarmupDuration seconds
                    if (_lanternWarmupTimer > 0f)
                    {
                        _lanternWarmupTimer -= dt;
                        float warmupFraction = 1f - MathHelper.Clamp(
                            _lanternWarmupTimer / LanternWarmupDuration, 0f, 1f);
                        _lanternLight.Intensity *= warmupFraction;
                        _lanternLight.Radius    *= warmupFraction;
                    }
                }
                else
                {
                    // Lantern is out — no light from it
                    _lanternLight.Radius           = 0f;
                    _lanternLight.Intensity        = 0f;
                    _lanternLight.FlickerAmplitude = 0f;
                    _lanternLight.SputterChance    = 0f;
                    _lanternLight.SwayOffset       = Vector2.Zero;
                }

                // Always update position (base position; sway is applied via SwayOffset)
                _lanternLight.Position = _player.PixelPosition;
            }
        }

        /// <summary>
        /// Calculate ambient light level for a given depth.
        /// Deeper = darker. Range: 0.06 (depth 1) → 0.025 (deep).
        /// Raised slightly so deep levels are dark but not pitch-black,
        /// while the lantern still forms a clearly visible pool of light.
        /// </summary>
        private static float GetAmbientForDepth(int depth)
        {
            return MathHelper.Clamp(0.06f - (depth - 1) * 0.005f, 0.025f, 0.06f);
        }

        /// <summary>
        /// HUD bar smoothing: drops snap to the new value (so damage feels
        /// instant); recoveries lerp toward the target. Phase 4.2.
        /// </summary>
        private static float SnapDownLerpUp(float displayed, float actual, float lerpAmount)
        {
            if (actual < displayed) return actual; // damage / drain → instant
            return MathHelper.Lerp(displayed, actual, MathHelper.Clamp(lerpAmount, 0f, 1f));
        }

        /// <summary>Draw the HUD in screen space (no camera transform).</summary>
        private void DrawHUD(SpriteBatch spriteBatch, AssetManager assets, int vw, int vh)
        {
            if (_player == null) return;

            const int barW  = 160;
            const int barH  = 18;
            const int barX  = 16;
            const int iconW = 14;
            int y = 16;

            // ── Phase 6.4: level-transition fade-from-black ───────────────────
            // Solid black overlay that fades out as the new level reveals.
            if (_levelTransitionTimer > 0f)
            {
                float t = _levelTransitionTimer / LevelTransitionDuration; // 1→0
                byte a = (byte)(MathHelper.Clamp(t, 0f, 1f) * 255f);
                assets.DrawRect(spriteBatch,
                    new Rectangle(0, 0, vw, vh),
                    new Color((byte)0, (byte)0, (byte)0, a));
            }

            // ── Damage flash vignette (Task 13.1) ─────────────────────────────
            if (_damageFlashTimer > 0f)
            {
                float flashAlpha = (_damageFlashTimer / DamageFlashDuration) * 0.55f;
                for (int ring = 0; ring < 6; ring++)
                {
                    int margin = ring * 14;
                    byte a = (byte)(flashAlpha * (80 - ring * 12));
                    if (a > 0)
                        assets.DrawRectOutline(spriteBatch,
                            new Rectangle(margin, margin, vw - margin * 2, vh - margin * 2),
                            new Color((byte)220, (byte)30, (byte)30, a), 14);
                }
            }

            // ── Lantern fuel bar (Task 9: icon + numeric + smooth + low warning) ─
            bool lanternLow = _displayLantern < 0.2f;
            Color lanternFill = lanternLow
                ? Color.Lerp(new Color(220, 180, 60), new Color(255, 80, 40),
                    AnimationClock.Pulse(4f) * 0.6f)
                : new Color(220, 180, 60);
            DrawBar(spriteBatch, assets, barX + iconW + 2, y, barW, barH,
                _displayLantern, lanternFill, new Color(40, 30, 10),
                $"Fuel {(int)(_player.Stats.LanternFuel):0}/{(int)PlayerStats.MaxLanternFuel:0}",
                lowWarning: lanternLow);
            // Icon: small flame symbol (diamond)
            GeometryBatch.DrawDiamond(spriteBatch, assets,
                new Vector2(barX + iconW / 2f, y + barH / 2f), 5f,
                lanternFill * (lanternLow ? 0.6f + AnimationClock.Pulse(4f) * 0.4f : 0.9f));
            y += barH + 8;

            // ── Breath meter (Task 9) ──────────────────────────────────────────
            bool breathLow = _displayBreath < 0.25f;
            Color breathFill = breathLow
                ? Color.Lerp(new Color(60, 160, 220), new Color(220, 60, 60),
                    AnimationClock.Pulse(5f) * 0.7f)
                : new Color(60, 160, 220);
            DrawBar(spriteBatch, assets, barX + iconW + 2, y, barW, barH,
                _displayBreath, breathFill, new Color(10, 20, 40),
                $"Air {(int)(_player.Stats.Breath):0}/{(int)PlayerStats.MaxBreath:0}",
                lowWarning: breathLow);
            // Icon: small circle (lungs approximation)
            GeometryBatch.DrawCircleApprox(spriteBatch, assets,
                new Vector2(barX + iconW / 2f, y + barH / 2f), 4f,
                breathFill * 0.9f, 6);
            y += barH + 8;

            // ── Health bar (Task 9) ────────────────────────────────────────────
            bool healthLow = _displayHealth < 0.2f;
            Color healthFill = healthLow
                ? Color.Lerp(new Color(200, 60, 60), new Color(255, 120, 40),
                    AnimationClock.Pulse(3f) * 0.5f)
                : new Color(200, 60, 60);
            DrawBar(spriteBatch, assets, barX + iconW + 2, y, barW, barH,
                _displayHealth, healthFill, new Color(40, 10, 10),
                $"HP {(int)_player.Stats.Health:0}/{(int)PlayerStats.MaxHealth:0}",
                lowWarning: healthLow);
            // Icon: small heart (two diamonds)
            GeometryBatch.DrawDiamond(spriteBatch, assets,
                new Vector2(barX + iconW / 2f - 2, y + barH / 2f), 3f,
                healthFill * 0.9f);
            GeometryBatch.DrawDiamond(spriteBatch, assets,
                new Vector2(barX + iconW / 2f + 2, y + barH / 2f), 3f,
                healthFill * 0.9f);
            y += barH + 8;

            // ── Kinetic charge bar (Task 9) ────────────────────────────────────
            bool kineticFull = _displayKinetic >= 0.99f;
            Color kineticFill = kineticFull
                ? Color.Lerp(new Color(180, 120, 220), new Color(255, 220, 40),
                    AnimationClock.Pulse(2f) * 0.6f)
                : new Color(180, 120, 220);
            DrawBar(spriteBatch, assets, barX + iconW + 2, y, barW, barH,
                _displayKinetic, kineticFill, new Color(20, 10, 40),
                $"KE {(int)(_player.Stats.KineticCharge * 100f):0}%",
                lowWarning: false);
            // Icon: lightning bolt (line)
            assets.DrawRect(spriteBatch,
                new Rectangle(barX + iconW / 2 - 1, y + 2, 2, barH - 4),
                kineticFill * 0.9f);
            y += barH + 8;

            // ── Sanity bar ─────────────────────────────────────────────────────
            {
                float sf    = _displaySanity;
                bool  slow  = sf < 0.60f;
                bool  low   = sf < 0.40f;
                bool  crit  = sf < 0.20f;

                Color sanityFill;
                if (sf > 0.60f)
                    sanityFill = Color.Lerp(new Color(220, 180, 40), new Color(80, 200, 80),
                        (sf - 0.60f) / 0.40f);
                else if (sf > 0.40f)
                    sanityFill = Color.Lerp(new Color(220, 100, 40), new Color(220, 180, 40),
                        (sf - 0.40f) / 0.20f);
                else
                    sanityFill = new Color(210, 50, 50);

                if (crit)
                    sanityFill = Color.Lerp(sanityFill, new Color(255, 200, 200),
                        AnimationClock.Pulse(8f) * 0.4f);
                else if (low)
                    sanityFill = Color.Lerp(sanityFill, new Color(255, 80, 80),
                        AnimationClock.Pulse(4f) * 0.3f);

                DrawBar(spriteBatch, assets, barX + iconW + 2, y, barW, barH,
                    sf, sanityFill, new Color(20, 8, 8),
                    $"Mind {(int)_player.Stats.Sanity}/{(int)PlayerStats.MaxSanity}",
                    lowWarning: low);

                // Icon: concentric-square eye
                int ix = barX + 2, iy = y + 2, is_ = barH - 4;
                assets.DrawRectOutline(spriteBatch,
                    new Rectangle(ix, iy, is_, is_), sanityFill * 0.55f, 1);
                assets.DrawRect(spriteBatch,
                    new Rectangle(ix + is_ / 2 - 1, iy + is_ / 2 - 1, 3, 3),
                    sanityFill * 0.9f);
            }
            y += barH + 8;

            // ── Flare count ────────────────────────────────────────────────────
            assets.DrawString(spriteBatch, "Flares", new Vector2(barX, y), TextColor, 0.75f);
            for (int i = 0; i < PlayerStats.MaxFlareCount; i++)
            {
                bool available = i < _player.Stats.FlareCount;
                var iconPos = new Vector2(barX + 52f + i * 18f, y + 5f);
                if (available)
                    GeometryBatch.DrawDiamond(spriteBatch, assets, iconPos, 5f,
                        Color.Lerp(new Color(200, 130, 40), new Color(255, 210, 80),
                            AnimationClock.Pulse(1.5f, i * 0.4f) * 0.5f));
                else
                    GeometryBatch.DrawDiamondOutline(spriteBatch, assets, iconPos, 5f,
                        new Color(80, 60, 30), 1f);
            }
            y += barH + 8;

            // ── Weight display ─────────────────────────────────────────────────
            float weight    = _player.Inventory.TotalWeight;
            bool  overweight = weight >= Inventory.MaxWeight * 0.9f;
            Color weightCol  = overweight ? new Color(220, 100, 60) : new Color(140, 160, 180);
            assets.DrawString(spriteBatch,
                $"Pack: {weight:0.#}/{Inventory.MaxWeight:0}kg",
                new Vector2(barX, y), weightCol, 0.75f);

            // ── Active debuffs strip ───────────────────────────────────────────
            if (_player.Debuffs.ActiveDebuffs.Count > 0)
            {
                y += 16;
                foreach (var debuff in _player.Debuffs.ActiveDebuffs)
                {
                    string name = DebuffSystem.GetDisplayName(debuff.Type);
                    Color  col  = DebuffSystem.GetDisplayColor(debuff.Type);
                    assets.DrawString(spriteBatch,
                        $"[{name}] {debuff.RemainingTime:0.0}s",
                        new Vector2(barX, y), col, 0.78f);
                    y += 16;
                }
            }

            // ── Shard counter (Task 10: diamond icon + pulse + EXIT OPEN glow) ─
            if (_level != null)
            {
                int   collected = _level.ShardsCollected;
                int   required  = _level.ShardsRequired;
                bool  allFound  = _level.IsExitUnlocked;

                float pulseFrac = _shardPulseTimer > 0f
                    ? (_shardPulseTimer / ShardPulseDuration)
                    : 0f;
                float exitPulse = allFound ? AnimationClock.Pulse(2.5f) : 0f;

                Color shardColor = allFound
                    ? Color.Lerp(new Color(180, 140, 255), new Color(240, 210, 255), exitPulse * 0.4f)
                    : Color.Lerp(new Color(100, 80, 160), new Color(200, 170, 255), pulseFrac);

                float shardCx = vw / 2f;
                float shardY  = 14f;

                // Diamond icon
                float iconScale = 1f + pulseFrac * 0.4f + exitPulse * 0.2f;
                GeometryBatch.DrawDiamond(spriteBatch, assets,
                    new Vector2(shardCx - 52f, shardY + 6f), 5f * iconScale, shardColor);

                // Shard text
                string shardText = $"Shards: {collected}/{required}";
                assets.DrawString(spriteBatch, shardText,
                    new Vector2(shardCx - 38f, shardY), shardColor, 0.8f);

                // EXIT OPEN glowing badge
                if (allFound)
                {
                    float badgePulse = AnimationClock.Pulse(2f);
                    Color badgeColor = Color.Lerp(new Color(140, 100, 220), new Color(220, 180, 255), badgePulse * 0.5f);
                    string exitText  = "EXIT OPEN";
                    if (assets.GameFont != null)
                    {
                        Vector2 exitSize = assets.GameFont.MeasureString(exitText) * 0.75f;
                        float ex = shardCx - exitSize.X / 2f;
                        float ey = shardY + 18f;
                        var exitRect = new Rectangle((int)ex - 6, (int)ey - 2, (int)exitSize.X + 12, (int)exitSize.Y + 4);
                        assets.DrawRect(spriteBatch, exitRect, new Color(30, 15, 50, 200));
                        assets.DrawRectOutline(spriteBatch, exitRect, badgeColor * (0.6f + badgePulse * 0.4f), 1);
                        assets.DrawString(spriteBatch, exitText, new Vector2(ex, ey), badgeColor, 0.75f);
                    }
                }
            }

            // ── Depth + seed + state (hidden when inventory panel is open) ──────
            if (!_inventoryUI.IsVisible)
            {
                const float infoScale = 0.8f;
                if (assets.GameFont != null)
                {
                    string depthText = $"Depth: {Depth}   Seed: {Seed}";
                    float depthW = assets.GameFont.MeasureString(depthText).X * infoScale;
                    assets.DrawString(spriteBatch, depthText,
                        new Vector2(vw - depthW - 12f, 16f), TextColor, infoScale);

                    string stateText = $"State: {_player.State}";
                    float stateW = assets.GameFont.MeasureString(stateText).X * 0.78f;
                    assets.DrawString(spriteBatch, stateText,
                        new Vector2(vw - stateW - 12f, 32f), new Color(120, 140, 160), 0.78f);
                }

                var lighting = Game1.Lighting;
                if (lighting != null && assets.GameFont != null)
                {
                    string lightingStatus = lighting.Enabled ? "Lighting: ON" : "Lighting: OFF (F2)";
                    float lightW = assets.GameFont.MeasureString(lightingStatus).X * 0.78f;
                    assets.DrawString(spriteBatch, lightingStatus,
                        new Vector2(vw - lightW - 12f, 48f), new Color(100, 120, 100), 0.78f);
                }
            }

            // ── Action button bar (Task 8) — replaces text controls strip ──────
            ActionButtonBar.Draw(spriteBatch, assets,
                _player.State, _player.Stats, _entityControl, vw, vh);
        }

        private void DrawBar(SpriteBatch spriteBatch, AssetManager assets,
            int x, int y, int w, int h,
            float fraction, Color fillColor, Color bgColor, string label,
            bool lowWarning = false)
        {
            // Background
            assets.DrawRect(spriteBatch, new Rectangle(x, y, w, h), bgColor);
            // Fill
            int fillW = (int)(w * MathHelper.Clamp(fraction, 0f, 1f));
            if (fillW > 0)
                assets.DrawRect(spriteBatch, new Rectangle(x, y, fillW, h), fillColor);
            // Border
            assets.DrawRectOutline(spriteBatch, new Rectangle(x, y, w, h), new Color(60, 80, 100), 1);
            // Label centered inside bar
            if (assets.GameFont != null)
            {
                const float labelScale = 0.72f;
                Vector2 labelSize = assets.GameFont.MeasureString(label) * labelScale;
                float lx = x + (w - labelSize.X) / 2f;
                float ly = y + (h - labelSize.Y) / 2f;
                assets.DrawString(spriteBatch, label, new Vector2(lx, ly), TextColor * 0.92f, labelScale);
            }
        }
    }
}
