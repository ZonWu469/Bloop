using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using Bloop.Core;
using Bloop.Entities;
using Bloop.Generators;
using Bloop.Lighting;
using Bloop.Objects;
using Bloop.Physics;
using Bloop.Rendering;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.World
{
    /// <summary>
    /// Container for a single procedurally generated level.
    /// Holds the TileMap, all world objects, domino chains, entry/exit points,
    /// and depth number. Delegates update/draw to its children.
    ///
    /// Construction pipeline:
    ///   1. LevelGenerator.Generate() produces a GenerationResult
    ///   2. TileMap physics bodies are generated
    ///   3. ObjectPlacements are instantiated as concrete WorldObject subclasses
    ///   4. DominoPlatformChains are assembled from chained DisappearingPlatforms
    ///
    /// Phase 9 additions:
    ///   - RegisterLightsWithSystem(): adds VentFlower ambient lights to LightingSystem
    ///   - RemoveLightsFromSystem(): removes level-specific lights on level unload
    ///   - Update() checks DisappearingPlatform.SpawnSporeLight flags and spawns SporeLights
    /// </summary>
    public class Level
    {
        // ── Data ───────────────────────────────────────────────────────────────
        public TileMap   TileMap    { get; }
        public int       Depth      { get; }
        public int       Seed       { get; }
        /// <summary>Biome tier for this level (drives tile color palette and color grading).</summary>
        public BiomeTier Biome      { get; private set; }

        /// <summary>Player spawn position in pixel space.</summary>
        public Vector2 EntryPoint  { get; private set; }
        /// <summary>Level exit position in pixel space (bottom of level).</summary>
        public Vector2 ExitPoint   { get; private set; }

        // ── World objects ──────────────────────────────────────────────────────
        private readonly List<WorldObject>           _objects    = new();
        private readonly List<WorldObject>           _toRemove   = new();
        private readonly List<DominoPlatformChain>   _chains     = new();
        private readonly HashSet<WorldObject>        _overlapping = new();

        public IReadOnlyList<WorldObject> Objects => _objects;

        // ── Physics reference ──────────────────────────────────────────────────
        private readonly AetherWorld _physicsWorld;

        // ── Entity control context (set after construction by GameplayScreen) ──
        private InputManager? _inputManager;
        private Camera?       _camera;

        // ── Earthquake system (spawns FallingRubble, reshapes tilemap) ─────────
        private EarthquakeSystem? _earthquake;
        /// <summary>Exposed so GameplayScreen can trigger camera shake / lantern flicker.</summary>
        public EarthquakeSystem? Earthquake => _earthquake;

        // ── Lighting: level-owned light sources ───────────────────────────────
        /// <summary>
        /// Permanent light sources owned by this level (VentFlowers, etc.).
        /// Registered with LightingSystem on load, removed on unload.
        /// </summary>
        private readonly List<LightSource> _levelLights = new();

        // ── Water pools (1.5) ──────────────────────────────────────────────────
        private readonly WaterPoolSystem _waterPools = new();

        // ── Discovery / fog-of-reveal ─────────────────────────────────────────
        /// <summary>
        /// Marks which tiles have been illuminated by the player's lantern.
        /// [tx, ty] = true once the tile has been seen. Updated each frame by GameplayScreen.
        /// </summary>
        public bool[,] Discovered { get; private set; } = new bool[0, 0];

        /// <summary>Initialize the discovered grid after the TileMap is ready.</summary>
        private void InitDiscovered()
        {
            Discovered = new bool[TileMap.Width, TileMap.Height];
        }

        /// <summary>
        /// Mark all tiles within pixelRadius of pixelCenter as discovered.
        /// Called each frame from GameplayScreen with the current lantern radius.
        /// </summary>
        public void RevealAround(Vector2 pixelCenter, float pixelRadius)
        {
            int tileRadius = (int)(pixelRadius / TileMap.TileSize) + 1;
            int cx = (int)(pixelCenter.X / TileMap.TileSize);
            int cy = (int)(pixelCenter.Y / TileMap.TileSize);

            int x0 = System.Math.Max(0, cx - tileRadius);
            int x1 = System.Math.Min(TileMap.Width  - 1, cx + tileRadius);
            int y0 = System.Math.Max(0, cy - tileRadius);
            int y1 = System.Math.Min(TileMap.Height - 1, cy + tileRadius);

            float radiusSq = pixelRadius * pixelRadius;
            for (int ty = y0; ty <= y1; ty++)
            {
                for (int tx = x0; tx <= x1; tx++)
                {
                    float px = tx * TileMap.TileSize + TileMap.TileSize / 2f - pixelCenter.X;
                    float py = ty * TileMap.TileSize + TileMap.TileSize / 2f - pixelCenter.Y;
                    if (px * px + py * py <= radiusSq)
                        Discovered[tx, ty] = true;
                }
            }
        }

        // ── Shard collection state ─────────────────────────────────────────────
        /// <summary>Total resonance shards placed in this level.</summary>
        public int ShardsRequired  { get; private set; }
        /// <summary>Shards the player has collected so far.</summary>
        public int ShardsCollected { get; private set; }
        /// <summary>True once all shards are collected and the exit is open.</summary>
        public bool IsExitUnlocked => ShardsCollected >= ShardsRequired;

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color EntryColor = new Color( 80, 220, 120);
        private static readonly Color ExitColor  = new Color(220, 180,  60);
        private static readonly Color BgColor    = new Color(  6,   8,  14); // deep cave background

        // ── Constructor ────────────────────────────────────────────────────────
        public Level(int depth, int seed, AetherWorld physicsWorld,
            InputManager? inputManager = null, Camera? camera = null)
        {
            Depth         = depth;
            Seed          = seed;
            _physicsWorld = physicsWorld;
            _inputManager = inputManager;
            _camera       = camera;

            // ── Generate the level procedurally ───────────────────────────────
            var result = LevelGenerator.Generate(seed, depth);

            TileMap    = result.TileMap;
            EntryPoint = result.EntryPoint;
            ExitPoint  = result.ExitPoint;
            Biome      = result.Biome;

            // Apply biome palette to tile renderer
            TileRenderer.SetBiome(Biome);

            InitDiscovered();

            // ── Build water pools from shaft bottoms (1.5) ───────────────────
            _waterPools.BuildPools(TileMap, seed);

            // ── Generate Aether physics bodies for the tile map ───────────────
            TileMap.GeneratePhysicsBodies(_physicsWorld);

            // ── Instantiate world objects from placements ─────────────────────
            var chainMap = new Dictionary<int, DominoPlatformChain>();

            foreach (var placement in result.ObjectPlacements)
            {
                var obj = CreateObject(placement, physicsWorld);
                if (obj == null) continue;

                _objects.Add(obj);

                // Wire up domino chains for DisappearingPlatforms
                if (obj is DisappearingPlatform dp && placement.ChainId >= 0)
                {
                    dp.ChainId    = placement.ChainId;
                    dp.ChainOrder = placement.ChainOrder;

                    if (!chainMap.TryGetValue(placement.ChainId, out var chain))
                    {
                        chain = new DominoPlatformChain(placement.ChainId);
                        chainMap[placement.ChainId] = chain;
                        _chains.Add(chain);
                    }
                    chain.AddPlatform(dp);
                }

                // Create ambient light sources for VentFlowers
                if (obj is VentFlower ventFlower)
                {
                    var ventLight = new LightSource(
                        ventFlower.PixelPosition,
                        radius:    140f,
                        intensity: 1.2f,
                        color:     new Color(120, 255, 200));
                    _levelLights.Add(ventLight);
                }

                // Resonance shards: count them and add emissive light
                if (obj is ResonanceShard shard)
                {
                    ShardsRequired++;
                    shard.OnCollected += () => ShardsCollected++;
                    var shardLight = new LightSource(
                        shard.PixelPosition,
                        radius:    70f,
                        intensity: 1.4f,
                        color:     new Color(180, 140, 255));
                    _levelLights.Add(shardLight);
                    shard.SetLightSource(shardLight);
                }

                // Rare collectibles: small ambient glow to lure explorers
                if (obj is CaveLichen lichen && lichen.Rarity == Generators.ItemRarity.Rare)
                {
                    _levelLights.Add(new LightSource(
                        lichen.PixelPosition, radius: 50f, intensity: 0.9f,
                        color: new Color(160, 220, 60)));
                }
                else if (obj is BlindFish fish && fish.Rarity == Generators.ItemRarity.Rare)
                {
                    _levelLights.Add(new LightSource(
                        fish.PixelPosition, radius: 45f, intensity: 0.8f,
                        color: new Color(100, 160, 220)));
                }

                // ── Glow objects: attach an ambient light matching each color ─────
                if (obj is IonStone ion)
                {
                    var ionLight = new LightSource(
                        ion.PixelPosition,
                        radius:    90f,
                        intensity: 1.2f,
                        color:     new Color(196, 152, 255));
                    _levelLights.Add(ionLight);
                    ion.SetLightSource(ionLight);
                }
                else if (obj is PhosphorMoss moss)
                {
                    var mossLight = new LightSource(
                        moss.PixelPosition,
                        radius:    55f,
                        intensity: 0.8f,
                        color:     new Color(168, 214, 90));
                    _levelLights.Add(mossLight);
                    moss.SetLightSource(mossLight);
                }
                else if (obj is CrystalCluster crystal)
                {
                    var crystalLight = new LightSource(
                        crystal.PixelPosition,
                        radius:    100f,
                        intensity: 1.3f,
                        color:     CrystalCluster.ColorFor(crystal.Flavor));
                    _levelLights.Add(crystalLight);
                    crystal.SetLightSource(crystalLight);
                }

                if (obj is LuminescentGlowworm worm)
                {
                    var wormLight = new LightSource(
                        worm.PixelPosition,
                        radius:    110f,
                        intensity: 1.2f,
                        color:     new Color(160, 230, 80));
                    _levelLights.Add(wormLight);
                    worm.SetLightSource(wormLight);
                }

                if (obj is LuminousIsopod isopod)
                {
                    var isopodLight = new LightSource(
                        isopod.PixelPosition,
                        radius:    90f,
                        intensity: 1.1f,
                        color:     new Color(80, 200, 200));
                    _levelLights.Add(isopodLight);
                    isopod.SetLightSource(isopodLight);
                }

                if (obj is GlowVine vine2)
                {
                    var vineLight = new LightSource(
                        vine2.PixelPosition,
                        radius:    20f,
                        intensity: 0f,       // activated by GlowVine.Activate()
                        color:     new Color(60, 220, 120));
                    _levelLights.Add(vineLight);
                    vine2.SetLightSource(vineLight);
                }
            }

            // ── Earthquake system ─────────────────────────────────────────────
            _earthquake = new EarthquakeSystem(
                TileMap, _physicsWorld, seed, EntryPoint, ExitPoint, Biome,
                liveShardPositions: () =>
                {
                    var positions = new List<Vector2>();
                    foreach (var obj in _objects)
                        if (obj is ResonanceShard shard && !shard.IsDestroyed)
                            positions.Add(shard.PixelPosition);
                    return positions;
                });
            _earthquake.OnRubbleSpawned += rubble => _objects.Add(rubble);
            _earthquake.OnTerrainRebuilt += () => _lastPlayer?.ResetGroundContacts();
        }

        /// <summary>
        /// Cached reference to the player passed into Update each frame. Used by
        /// earthquake + disappearing-platform hooks that need to reset ground
        /// contacts when the ground under the player's feet is destroyed.
        /// </summary>
        private Gameplay.Player? _lastPlayer;

        // ── Object management ──────────────────────────────────────────────────

        /// <summary>
        /// Provides InputManager and Camera to Level so entity constructors
        /// that require them can be called from CreateObject.
        /// Call this from GameplayScreen immediately after Level construction.
        /// </summary>
        public void SetControlContext(InputManager input, Camera camera)
        {
            _inputManager = input;
            _camera       = camera;
        }

        public void AddObject(WorldObject obj)
        {
            _objects.Add(obj);
        }

        // ── Lighting integration ───────────────────────────────────────────────

        /// <summary>
        /// Register all level-owned light sources with the global LightingSystem.
        /// Call after level construction, before the first frame is drawn.
        /// </summary>
        public void RegisterLightsWithSystem(LightingSystem? lighting)
        {
            if (lighting == null) return;
            foreach (var light in _levelLights)
                lighting.AddLight(light);
        }

        /// <summary>
        /// Remove all level-owned light sources from the global LightingSystem.
        /// Call before disposing the level (on depth transition).
        /// </summary>
        public void RemoveLightsFromSystem(LightingSystem? lighting)
        {
            if (lighting == null) return;
            foreach (var light in _levelLights)
                lighting.RemoveLight(light);
        }

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Update all world objects and domino chains.
        /// Pass the player reference so objects can check proximity/lighting.
        /// Also checks for spore light spawn requests from DisappearingPlatforms.
        /// </summary>
        public void Update(GameTime gameTime, Gameplay.Player? player = null)
        {
            _lastPlayer = player;

            // Tick the earthquake state machine (may add FallingRubble to _objects).
            if (_earthquake != null && player != null)
                _earthquake.Update(gameTime, player.PixelPosition);

            // Update domino chains
            foreach (var chain in _chains)
                chain.Update(gameTime);

            // Update all active objects
            foreach (var obj in _objects)
            {
                if (!obj.IsActive)
                {
                    if (obj.IsDestroyed)
                        _toRemove.Add(obj);
                    continue;
                }

                obj.Update(gameTime);

                // ── AABB-based player contact dispatch ─────────────────────────
                // Reliable pickup path for collectibles/triggers whose physics
                // sensor callbacks don't fire in this Aether configuration.
                if (player != null && obj.WantsPlayerContact)
                {
                    var bounds = obj.GetBounds();
                    bool overlaps = !bounds.IsEmpty && bounds.Intersects(player.PixelBounds);
                    bool wasOverlapping = _overlapping.Contains(obj);

                    if (overlaps)
                    {
                        obj.OnPlayerContact(player);
                        if (!wasOverlapping) _overlapping.Add(obj);
                    }
                    else if (wasOverlapping)
                    {
                        obj.OnPlayerSeparate(player);
                        _overlapping.Remove(obj);
                    }
                }

                // Per-object player interaction updates (lighting, illumination, etc.)
                if (player != null)
                {
                    switch (obj)
                    {
                        case StunDamageObject stun:
                            stun.UpdateLighting(player);
                            break;
                        case GlowVine vine:
                            vine.UpdateIllumination(player);
                            break;
                        case RootClump root:
                            root.ResetIdleTimer(player);
                            break;
                        case VentFlower:
                            // VentFlower tracks standing time via collision callbacks
                            // handled inside VentFlower.Update() + OnPlayerContact()
                            break;
                        case FallingStalactite stalactite:
                            stalactite.CheckPlayerProximity(player);
                            break;
                        case BlindFish fish:
                            fish.UpdateProximity(player);
                            break;
                    }
                }

                // ── Phase 9: Spore light spawning ──────────────────────────────
                // Check if a DisappearingPlatform was triggered while lit by lantern
                if (obj is DisappearingPlatform dp2 && dp2.SpawnSporeLight)
                {
                    SpawnSporeLight(dp2.PixelPosition);
                    // Note: SpawnSporeLight flag is consumed — DisappearingPlatform
                    // sets it once on trigger; we clear it by spawning the light.
                    // The platform will be destroyed soon anyway.
                }

                if (obj.IsDestroyed)
                    _toRemove.Add(obj);
            }

            // Remove destroyed objects
            foreach (var obj in _toRemove)
            {
                _objects.Remove(obj);
                _overlapping.Remove(obj);
            }
            _toRemove.Clear();

            // ── Update water pools (1.5) ───────────────────────────────────────
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _waterPools.Update(dt, player);
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw the level background, tiles, and world objects.
        /// Call inside a SpriteBatch.Begin/End block with camera transform applied.
        /// visibleBounds is used for culling — only visible content is drawn.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, AssetManager assets, Rectangle visibleBounds)
        {
            // Background fill — clamp to visible bounds for performance
            int bgX = System.Math.Max(0, visibleBounds.X);
            int bgY = System.Math.Max(0, visibleBounds.Y);
            int bgW = System.Math.Min(TileMap.PixelWidth,  visibleBounds.Right)  - bgX;
            int bgH = System.Math.Min(TileMap.PixelHeight, visibleBounds.Bottom) - bgY;
            if (bgW > 0 && bgH > 0)
                assets.DrawRect(spriteBatch, new Rectangle(bgX, bgY, bgW, bgH), BgColor);

            // Tiles (already viewport-culled inside TileMap.Draw)
            TileMap.Draw(spriteBatch, assets, visibleBounds);

            // Water pools (1.5) — drawn after tiles, before objects
            _waterPools.Draw(spriteBatch, assets, visibleBounds);

            // Entry marker — only draw if visible
            var entryRect = new Rectangle((int)EntryPoint.X - 16, (int)EntryPoint.Y - 4, 32, 8);
            if (visibleBounds.Intersects(entryRect))
                assets.DrawRect(spriteBatch, entryRect, EntryColor * 0.8f);

            // Exit portal — enhanced visual (3.7)
            DrawExitPortal(spriteBatch, assets, visibleBounds);

            // World objects — cull to visible bounds
            foreach (var obj in _objects)
            {
                if (!obj.IsActive) continue;

                var objBounds = obj.GetBounds();
                if (objBounds.IsEmpty || visibleBounds.Intersects(objBounds))
                    obj.Draw(spriteBatch, assets);
            }
        }

        // ── Cleanup ────────────────────────────────────────────────────────────

        public void Dispose()
        {
            TileMap.ClearPhysicsBodies(_physicsWorld);
            _objects.Clear();
            _chains.Clear();
            _levelLights.Clear();
        }

        // ── Object factory ─────────────────────────────────────────────────────

        /// <summary>
        /// Instantiate a concrete WorldObject subclass from an ObjectPlacement record.
        /// Returns null if the placement type is unrecognized.
        /// </summary>
        private WorldObject? CreateObject(ObjectPlacement placement, AetherWorld world)
        {
            return placement.Type switch
            {
                ObjectType.DisappearingPlatform =>
                    new DisappearingPlatform(placement.PixelPosition, world),

                ObjectType.StunDamageObject =>
                    new StunDamageObject(placement.PixelPosition, world),

                ObjectType.GlowVine =>
                    new GlowVine(placement.PixelPosition, world, placement.TileHeight),

                ObjectType.RootClump =>
                    new RootClump(placement.PixelPosition, world, placement.TileHeight),

                ObjectType.VentFlower =>
                    new VentFlower(placement.PixelPosition, world),

                ObjectType.CaveLichen =>
                    new CaveLichen(placement.PixelPosition, world, placement.IsPoisonous, placement.Rarity),

                ObjectType.BlindFish =>
                    new BlindFish(placement.PixelPosition, world, placement.IsPoisonous, placement.Rarity),

                ObjectType.ResonanceShard =>
                    new ResonanceShard(placement.PixelPosition, world),

                ObjectType.FallingStalactite =>
                    new FallingStalactite(placement.PixelPosition, world),

                ObjectType.IonStone =>
                    new IonStone(placement.PixelPosition, world),

                ObjectType.PhosphorMoss =>
                    new PhosphorMoss(placement.PixelPosition, world),

                ObjectType.CrystalCluster =>
                    new CrystalCluster(placement.PixelPosition, world,
                        (CrystalCluster.Variant)placement.Variant),

                // ── Controllable entities ──────────────────────────────────────
                ObjectType.EchoBat when _inputManager != null && _camera != null =>
                    new EchoBat(placement.PixelPosition, world, _inputManager, _camera),

                ObjectType.SilkWeaverSpider when _inputManager != null && _camera != null =>
                    new SilkWeaverSpider(placement.PixelPosition, world, _inputManager, _camera),

                ObjectType.ChainCentipede when _inputManager != null =>
                    new ChainCentipede(placement.PixelPosition, world, _inputManager),

                ObjectType.LuminescentGlowworm when _inputManager != null =>
                    new LuminescentGlowworm(placement.PixelPosition, world, _inputManager),

                ObjectType.DeepBurrowWorm when _inputManager != null =>
                    new DeepBurrowWorm(placement.PixelPosition, world, _inputManager),

                ObjectType.BlindCaveSalamander when _inputManager != null && _camera != null =>
                    new BlindCaveSalamander(placement.PixelPosition, world, _inputManager, _camera),

                ObjectType.LuminousIsopod when _inputManager != null && _camera != null =>
                    new LuminousIsopod(placement.PixelPosition, world, _inputManager, _camera),

                _ => null
            };
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Draw the animated exit portal (3.7).
        /// Locked: pulsing purple membrane with tendril particles.
        /// Unlocked: golden swirling vortex with radial rays.
        /// </summary>
        private void DrawExitPortal(SpriteBatch spriteBatch, AssetManager assets, Rectangle visibleBounds)
        {
            const int PortalW = 40;
            const int PortalH = 48;
            var center = ExitPoint;
            var portalRect = new Rectangle(
                (int)(center.X - PortalW / 2), (int)(center.Y - PortalH / 2),
                PortalW, PortalH);

            if (!visibleBounds.Intersects(new Rectangle(
                    portalRect.X - 20, portalRect.Y - 20,
                    portalRect.Width + 40, portalRect.Height + 40)))
                return;

            float t = Rendering.AnimationClock.Time;
            float pulse = Rendering.AnimationClock.Pulse(2.5f);

            if (IsExitUnlocked)
            {
                // ── Unlocked: golden vortex ────────────────────────────────────
                Color goldCore  = new Color(255, 230, 80);
                Color goldOuter = new Color(200, 140, 20);
                Color goldRay   = new Color(255, 200, 60);

                // Outer glow rings (3 concentric, pulsing)
                for (int ring = 3; ring >= 1; ring--)
                {
                    float ringScale = ring + pulse * 0.4f;
                    int rw = (int)(PortalW * ringScale * 0.5f);
                    int rh = (int)(PortalH * ringScale * 0.4f);
                    float alpha = (0.15f + pulse * 0.1f) / ring;
                    assets.DrawRect(spriteBatch,
                        new Rectangle((int)(center.X - rw / 2), (int)(center.Y - rh / 2), rw, rh),
                        goldOuter * alpha);
                }

                // Radial rays (8 rays rotating)
                for (int ray = 0; ray < 8; ray++)
                {
                    float angle = t * 1.2f + ray * MathF.PI / 4f;
                    float rayLen = 18f + pulse * 6f;
                    float rx = center.X + MathF.Cos(angle) * rayLen;
                    float ry = center.Y + MathF.Sin(angle) * rayLen;
                    assets.DrawRect(spriteBatch,
                        new Rectangle((int)rx - 1, (int)ry - 1, 3, 3),
                        goldRay * (0.5f + pulse * 0.4f));
                    // Inner ray segment
                    float rx2 = center.X + MathF.Cos(angle) * (rayLen * 0.5f);
                    float ry2 = center.Y + MathF.Sin(angle) * (rayLen * 0.5f);
                    assets.DrawRect(spriteBatch,
                        new Rectangle((int)rx2 - 1, (int)ry2 - 1, 2, 2),
                        goldRay * 0.8f);
                }

                // Core portal body
                assets.DrawRect(spriteBatch, portalRect, goldCore * (0.7f + pulse * 0.3f));
                assets.DrawRectOutline(spriteBatch,
                    new Rectangle(portalRect.X - 3, portalRect.Y - 3, portalRect.Width + 6, portalRect.Height + 6),
                    goldOuter * (0.6f + pulse * 0.4f), 2);

                // Spiral particles (6 dots orbiting)
                for (int i = 0; i < 6; i++)
                {
                    float a = t * 2.5f + i * MathF.PI / 3f;
                    float r = 14f + MathF.Sin(t * 3f + i) * 4f;
                    assets.DrawRect(spriteBatch,
                        new Rectangle((int)(center.X + MathF.Cos(a) * r) - 1,
                                       (int)(center.Y + MathF.Sin(a) * r * 0.6f) - 1, 3, 3),
                        goldCore * 0.9f);
                }
            }
            else
            {
                // ── Locked: purple membrane ────────────────────────────────────
                Color purpleCore  = new Color(100, 60, 180);
                Color purpleOuter = new Color(140, 80, 220);
                Color tendrilColor = new Color(160, 100, 240);

                // Membrane body
                assets.DrawRect(spriteBatch, portalRect,
                    purpleCore * (0.45f + pulse * 0.1f));
                assets.DrawRectOutline(spriteBatch,
                    new Rectangle(portalRect.X - 2, portalRect.Y - 2, portalRect.Width + 4, portalRect.Height + 4),
                    purpleOuter * (0.7f + pulse * 0.2f), 2);

                // Tendril particles reaching outward
                for (int i = 0; i < 5; i++)
                {
                    float a = t * 0.8f + i * MathF.PI * 2f / 5f;
                    float r = 16f + MathF.Sin(t * 2f + i * 1.3f) * 5f;
                    assets.DrawRect(spriteBatch,
                        new Rectangle((int)(center.X + MathF.Cos(a) * r) - 1,
                                       (int)(center.Y + MathF.Sin(a) * r * 0.55f) - 1, 2, 2),
                        tendrilColor * (0.5f + pulse * 0.3f));
                }

                // Shard count indicator (small dots showing remaining shards)
                int remaining = ShardsRequired - ShardsCollected;
                for (int i = 0; i < remaining && i < 8; i++)
                {
                    float a = i * MathF.PI * 2f / System.Math.Max(1, remaining);
                    assets.DrawRect(spriteBatch,
                        new Rectangle(
                            (int)(center.X + MathF.Cos(a) * 22f) - 2,
                            (int)(center.Y + MathF.Sin(a) * 14f) - 2, 4, 4),
                        new Color(200, 160, 255) * 0.8f);
                }
            }
        }

        /// <summary>
        /// Spawn a temporary SporeLight at the given position and add it to the
        /// global LightingSystem. Called when a DisappearingPlatform is triggered
        /// while lit by the player's lantern.
        /// </summary>
        private void SpawnSporeLight(Vector2 pixelPosition)
        {
            var lighting = Game1.Lighting;
            if (lighting == null) return;

            var sporeLight = new SporeLight(pixelPosition);
            lighting.AddLight(sporeLight);
            // SporeLight is temporary — LightingSystem.Update() removes it when expired
        }
    }
}
