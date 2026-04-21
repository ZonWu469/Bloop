using System.Collections.Generic;
using Bloop.Core;
using Bloop.Effects;
using Bloop.Entities;
using Bloop.Gameplay;
using Bloop.Generators;
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

        // ── Player trail effect (3.5) ──────────────────────────────────────────
        private readonly TrailEffect _trail = new TrailEffect();

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
                _player.Stats.SetFromSave(SaveData.Health, SaveData.BreathMeter, SaveData.LanternFuel);
                _player.Inventory.LoadFromSave(SaveData.InventoryItems);
            }

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

            // ── Update grapple hook aim target (for aim line rendering) ────────
            if (_grapple != null && _camera != null)
            {
                Vector2 mouseScreen = ScreenManager.Input.GetMouseWorldPosition();
                Vector2 mouseWorld  = _camera.ScreenToWorld(mouseScreen);
                _grapple.UpdateAimTarget(mouseWorld);
            }

            // ── Update grapple hook flight ─────────────────────────────────────
            _grapple?.Update(gameTime);

            // ── Update level objects (pass player for lighting/interaction) ────
            _level.Update(gameTime, _player);

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
            _camera.Follow(cameraTarget, gameTime);

            // ── Death check ────────────────────────────────────────────────────
            if (_player.State == PlayerState.Dead)
            {
                ScreenManager.Replace(new GameOverScreen(Seed, Depth, "Ran out of health"));
                return;
            }

            // ── Exit check: player reached exit point ──────────────────────────
            float exitDist = Vector2.Distance(_player.PixelPosition, _level.ExitPoint);
            if (exitDist < 40f && _level.IsExitUnlocked)
            {
                // Auto-save and advance to next depth
                SaveGame();
                Depth++;
                ReloadLevel();
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

                // Pass 3: Composite scene × lightMap + ambient onto virtual target
                lighting.Composite(spriteBatch);
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
            // Entity control HUD (Q button, cooldown ring, control bar, skill pips)
            _entityControlHUD?.Draw(spriteBatch, assets, vw, vh);
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
                InventoryItems = _player.Inventory.ToSaveData(),
            };
            SaveManager.Save(data);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>Draw all world-space content (level, rope, grapple, player, debug).</summary>
        private void DrawWorld(SpriteBatch spriteBatch, AssetManager assets)
        {
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
                EntityRenderer.DrawSelectionRangeCircle(spriteBatch, assets, _player.PixelPosition);

            // Physics debug overlay (F1)
            _debugDraw.Draw(spriteBatch, _physics!.World, assets, _camera.GetVisibleBounds());
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

        /// <summary>Draw the HUD in screen space (no camera transform).</summary>
        private void DrawHUD(SpriteBatch spriteBatch, AssetManager assets, int vw, int vh)
        {
            if (_player == null) return;

            const int barW = 160;
            const int barH = 14;
            const int barX = 16;
            int y = 16;

            // ── Lantern fuel bar ───────────────────────────────────────────────
            DrawBar(spriteBatch, assets, barX, y, barW, barH,
                _player.Stats.LanternFuel / PlayerStats.MaxLanternFuel,
                new Color(220, 180, 60), new Color(40, 30, 10), "Lantern");
            y += barH + 6;

            // ── Breath meter ───────────────────────────────────────────────────
            float breathFrac = _player.Stats.Breath / PlayerStats.MaxBreath;
            Color breathColor = breathFrac < 0.25f
                ? new Color(220, 60, 60)   // warning red
                : new Color(60, 160, 220); // normal blue
            DrawBar(spriteBatch, assets, barX, y, barW, barH,
                breathFrac, breathColor, new Color(10, 20, 40), "Breath");
            y += barH + 6;

            // ── Health bar ─────────────────────────────────────────────────────
            DrawBar(spriteBatch, assets, barX, y, barW, barH,
                _player.Stats.Health / PlayerStats.MaxHealth,
                new Color(200, 60, 60), new Color(40, 10, 10), "Health");
            y += barH + 6;

            // ── Kinetic charge bar ─────────────────────────────────────────────
            float kineticFrac = _player.Stats.KineticCharge / PlayerStats.MaxKineticCharge;
            Color kineticColor = kineticFrac >= 1f
                ? new Color(255, 220, 40)  // max charge glow
                : new Color(180, 120, 220);
            DrawBar(spriteBatch, assets, barX, y, barW, barH,
                kineticFrac, kineticColor, new Color(20, 10, 40), "Kinetic");
            y += barH + 6;

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
            y += barH + 6;

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
                        new Vector2(barX, y), col, 0.7f);
                    y += 14;
                }
            }

            // ── Shard counter ──────────────────────────────────────────────────
            if (_level != null)
            {
                int collected = _level.ShardsCollected;
                int required  = _level.ShardsRequired;
                bool allFound = _level.IsExitUnlocked;
                Color shardColor = allFound
                    ? new Color(180, 140, 255)  // purple when complete
                    : new Color(100, 80, 160);  // dim when incomplete

                string shardText = allFound
                    ? $"Shards: {collected}/{required}  [EXIT OPEN]"
                    : $"Shards: {collected}/{required}";

                assets.DrawString(spriteBatch,
                    shardText,
                    new Vector2(vw / 2f - 60f, 16f), shardColor, 0.8f);
            }

            // ── Depth + seed info ──────────────────────────────────────────────
            assets.DrawString(spriteBatch,
                $"Depth: {Depth}   Seed: {Seed}",
                new Vector2(vw - 200f, 16f), TextColor, 0.8f);

            // ── State indicator ────────────────────────────────────────────────
            assets.DrawString(spriteBatch,
                $"State: {_player.State}",
                new Vector2(vw - 200f, 36f), new Color(120, 140, 160), 0.75f);

            // ── Lighting status ────────────────────────────────────────────────
            var lighting = Game1.Lighting;
            if (lighting != null)
            {
                string lightingStatus = lighting.Enabled ? "Lighting: ON" : "Lighting: OFF (F2)";
                assets.DrawString(spriteBatch,
                    lightingStatus,
                    new Vector2(vw - 200f, 56f), new Color(100, 120, 100), 0.7f);
            }

            // ── Controls strip (top center) ────────────────────────────────────
            assets.DrawStringCentered(spriteBatch,
                "WASD · Move    Space · Jump    Down+Space · Rappel    LMB · Grapple    F · Flare    F1 · Physics    F2 · Lighting    Tab · Inventory    Esc · Pause",
                8f, new Color(70, 90, 110), 0.75f);
        }

        private void DrawBar(SpriteBatch spriteBatch, AssetManager assets,
            int x, int y, int w, int h,
            float fraction, Color fillColor, Color bgColor, string label)
        {
            // Background
            assets.DrawRect(spriteBatch, new Rectangle(x, y, w, h), bgColor);
            // Fill
            int fillW = (int)(w * MathHelper.Clamp(fraction, 0f, 1f));
            if (fillW > 0)
                assets.DrawRect(spriteBatch, new Rectangle(x, y, fillW, h), fillColor);
            // Border
            assets.DrawRectOutline(spriteBatch, new Rectangle(x, y, w, h), new Color(60, 80, 100), 1);
            // Label
            assets.DrawString(spriteBatch, label, new Vector2(x + w + 6, y), TextColor, 0.7f);
        }
    }
}
