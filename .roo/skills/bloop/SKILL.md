---
name: bloop
description: >
  Developer for "Descent Into the Deep" — a MonoGame 3.8 / .NET 8 / Aether.Physics2D 2.1.0
  2D side-scrolling descent platformer. Activate for any file under Bloop/ in this solution.
---

---
name: bloop
description: >
  Use this skill when working on any C# file in the BloopGit project (Bloop/ directory).
  Covers project architecture, physics conventions, lighting, procedural generation,
  entity system, world objects, and all project-specific naming and coding patterns.
---

# Descent Into the Deep — Project Skill

## Project Overview

**"Descent Into the Deep"** — MonoGame 3.8 DesktopGL, .NET 8, Aether.Physics2D 2.1.0.
A 2D side-scrolling descent platformer with procedural generation, dynamic lighting,
survival meters (lantern fuel, breath), entity possession, grappling hook, and rope traversal.
All levels are seed-deterministic.

**Solution:** [`Bloop.sln`](Bloop.sln) → project: [`Bloop/Bloop.csproj`](Bloop/Bloop.csproj)

**Entry point:** [`Bloop/Program.cs`](Bloop/Program.cs) → [`Bloop/Game1.cs`](Bloop/Game1.cs)

---

## Architecture Map

```
Bloop/
├── Core/           AssetManager, Camera, InputManager, ResolutionManager, Screen, ScreenManager, Smoothing
├── Entities/       ControllableEntity (base), EntityControlSystem, EntitySkill, + 7 entity types
├── Gameplay/       Player, PlayerController, PlayerStats, PlayerStats, Inventory, GrapplingHook,
│                   RopeSystem, RopeWrapSystem, MomentumSystem, MovementTuning
├── Generators/     LevelGenerator (entry), ObjectPlacer, RoomTemplates, PathValidator,
│                   CavityAnalyzer, DominoChainLinker, PerlinNoise
├── Lighting/       LightingSystem, LightSource, SporeLight, FlareLight
├── Objects/        16 WorldObject subclasses — collectibles, hazards, platforms, obstacles
├── Physics/        PhysicsManager, BodyFactory, CollisionCategories, PhysicsDebugDraw
├── Rendering/      AnimationClock, EntityRenderer, PlayerRenderer, TileRenderer, GeometryBatch,
│                   OrganicPrimitives, NoiseHelpers, WorldObjectRenderer, WorldProgressBar,
│                   EntitySpritesheet, EntitySpritesheetLoader, PlayerSpritesheet,
│                   PlayerSpritesheetLoader, TileNeighborCache
├── Effects/        DebuffSystem, ParticleSystem, Particle, ObjectParticleEmitter, TrailEffect
├── Screens/        GameplayScreen, MainMenuScreen, PauseScreen, OptionsScreen, GameOverScreen,
│                   LoadGameScreen, LoreModalScreen, SeedInputScreen
├── UI/             ActionButtonBar, EntityControlHUD, HoverTooltip, InventoryUI, Minimap
├── Audio/          AudioManager, SfxKeys
├── Lore/           LoreEntry, LoreGenerator
├── World/          WorldObject (base), Tile, TileMap, Level, WaterPool, EarthquakeSystem
└── SaveLoad/       SaveData, SaveManager
```

---

## Key Namespaces & Aliases

```csharp
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;
using AetherBody  = nkast.Aether.Physics2D.Dynamics.Body;
// PhysicsManager.ToPixels/ToMeters for all unit conversion
```

All files that import both `Bloop.World` and Aether MUST use the `AetherWorld` alias.

---

## Physics — `Bloop.Physics`

### Key constants
```csharp
PhysicsManager.PIXELS_PER_METER = 64f   // 64 pixels = 1 meter
PhysicsManager.Gravity          = 20f   // m/s² downward (Vector2, Y component)
```

### Unit conversion — ALWAYS convert before passing to Aether
```csharp
PhysicsManager.ToMeters(pixelVec)   // Vector2 or float: pixels → meters
PhysicsManager.ToPixels(meterVec)   // Vector2 or float: meters → pixels
```

### Stepping — sub-stepped at 120 Hz, max 4 sub-steps per frame
```csharp
// PhysicsManager.Step() clamps dt to 4/120s = 0.0333s, sub-steps at 120Hz
_physicsManager.Step((float)gameTime.ElapsedGameTime.TotalSeconds);
```

### Body removal — never remove bodies inside collision callbacks
```csharp
_physicsManager.QueueRemoveBody(body);  // deferred, safe inside callbacks
// Flushed at start of PhysicsManager.Step()
```

### CCD (Continuous Collision Detection)
Player body is created with `IsBullet = true` for CCD. Grapple body also uses `IsBullet = true`.

### BodyFactory — use these, never call world.CreateBody directly for game objects
```csharp
BodyFactory.CreatePlayerBody(world, pixelPosition)           // capsule, CCD enabled
BodyFactory.CreatePlayerFixtures(body, pixelW, pixelH)       // add/replace player fixtures
BodyFactory.ReplacePlayerFixtures(body, pixelW, pixelH)      // replaces existing fixtures
BodyFactory.CreateStaticRect(world, pixelCenter, pixelW, pixelH, category, mask)
BodyFactory.CreateSensorRect(world, pixelCenter, pixelW, pixelH, category, mask)
BodyFactory.CreateTerrainChain(world, pixelVertices)         // edge-chain for tilemap
BodyFactory.CreateGrappleBody(world, pixelPosition)          // dynamic circle, bullet
BodyFactory.CreateStalactiteBody(world, pixelPosition)       // dynamic rect
BodyFactory.CreateEntityBody(world, pixelPosition, pixelW, pixelH, canFly)  // optional flight disable gravity
```

### CollisionCategories bitmasks (12 categories)
```csharp
CollisionCategories.Terrain                // Cat1 — static terrain tiles
CollisionCategories.Platform               // Cat2 — one-way platforms
CollisionCategories.Player                 // Cat3 — player body
CollisionCategories.Trigger                // Cat4 — sensor-only zones
CollisionCategories.DisappearingPlatform   // Cat5
CollisionCategories.Hazard                 // Cat6 — stun/damage objects
CollisionCategories.Climbable              // Cat7 — glow vines, root clumps
CollisionCategories.GrappleHook            // Cat8
CollisionCategories.Collectible            // Cat9
CollisionCategories.WorldObject            // Cat10 — falling stalactites, etc.
CollisionCategories.Entity                 // Cat11 — cave entities (sensor select + physics)
CollisionCategories.CrystalBridge          // Cat12

// Pre-composed masks
CollisionCategories.PlayerCollidesWith      // Terrain|Platform|DisappearingPlatform|Hazard|Climbable|Collectible|CrystalBridge
CollisionCategories.GrappleCollidesWith     // Terrain|Climbable|CrystalBridge
CollisionCategories.TriggerCollidesWith     // Player
CollisionCategories.EntityCollidesWith      // Terrain|Platform|DisappearingPlatform
```

---

## Gameplay — `Bloop.Gameplay`

### Player stats (`PlayerStats.cs`)
```csharp
MaxHealth       = 100f
MaxBreath       = 100f
MaxLanternFuel  = 100f
MaxKineticCharge= 100f
MaxFlareCount   = 3
MaxSanity       = 100f
```

### PlayerController
Handles state machine (`PlayerState` enum): `Idle | Walking | Jumping | Falling | Dead | Stunned | Climbing | Sliding | Hanging | RappelDown | Launching | WallSliding | Landing`.
Uses [`MovementTuning.cs`](Bloop/Gameplay/MovementTuning.cs) for configurable physics parameters.

### GrapplingHook (`GrapplingHook.cs`)
Fires a hook via `BodyFactory.CreateGrappleBody`. On hit: transitions to rope system.
Hook is aimed via mouse cursor (virtual cursor from `InputManager`).

### RopeSystem (`RopeSystem.cs`)
Manages rope physics: tension, length constraint, wrap points via [`RopeWrapSystem.cs`](Bloop/Gameplay/RopeWrapSystem.cs).

### MomentumSystem (`MomentumSystem.cs`)
Tracks kinetic charge. Building speed against terrain adds charge. Launching converts stored momentum.

### Inventory (`Inventory.cs`)
MaxWeight = 50kg. Items have weight, display name, IsPoisonous flag, heal amount. `UseItem(index, player)`.

### Lantern
- Base radius: 260px, base intensity: 2.4
- Shrinks with fuel (min 80px)
- Warm-up animation on level entry (1.5s)
- Sway ±4px, flicker amplitude 0.04–0.15
- Toggle for quick extinguish/re-light

---

## World Objects — `Bloop.World.WorldObject`

All collectibles, hazards, and platforms extend `WorldObject`. It handles:
- Optional `Body? Body` (Aether, pixel position auto-derived via `ToPixels`)
- `IsActive`, `IsDestroyed` lifecycle flags
- `Destroy()` — removes body from world (calls `World.Remove(Body)` directly, NOT `QueueRemoveBody`)
- `WantsPlayerContact` — if true, `OnPlayerContact` called via AABB overlap check in Level

### All 16 Object Types

| Class | File | Type | Key Details |
|-------|------|------|-------------|
| **ResonanceShard** | [`Objects/ResonanceShard.cs`](Bloop/Objects/ResonanceShard.cs) | Collectible (goal) | 32×32 sensor, pulsing draw, triggers OnShardCollected |
| **CaveLichen** | [`Objects/CaveLichen.cs`](Bloop/Objects/CaveLichen.cs) | Collectible (food) | 32×32 sensor, heal 20, 30% poison (SlowMovement/Blurred), OnCollected virtual |
| **BlindFish** | [`Objects/BlindFish.cs`](Bloop/Objects/BlindFish.cs) | Collectible (food) | Extends CaveLichen, 2× larger (40×20), heal 30, 30% poison (3s stun + 5 dmg + ReducedJump/Blurred), 3kg |
| **VentFlower** | [`Objects/VentFlower.cs`](Bloop/Objects/VentFlower.cs) | Refill station | Stand 5s to refill breath + lantern fuel. Sensor + collision. Cooldown overlay. |
| **DisappearingPlatform** | [`Objects/DisappearingPlatform.cs`](Bloop/Objects/DisappearingPlatform.cs) | Platform | 64×8 sensor, collapses after step, optional SporeLight spawn, domino chain support |
| **DominoPlatformChain** | [`Objects/DominoPlatformChain.cs`](Bloop/Objects/DominoPlatformChain.cs) | Coordinator | NOT a WorldObject. CascadeDelay=1s. Links platforms via OnTriggered callback. |
| **CrystalBridge** | [`Objects/CrystalBridge.cs`](Bloop/Objects/CrystalBridge.cs) | Bridge | Timed grow/retract, BridgeState enum, segment-by-segment build, LightSource, ambient motes |
| **GlowVine** | [`Objects/GlowVine.cs`](Bloop/Objects/GlowVine.cs) | Climbable | Requires 2s cumulative illumination, sensor→climbable body, photophore chase, spore emitter |
| **RootClump** | [`Objects/RootClump.cs`](Bloop/Objects/RootClump.cs) | Climbable | 8s idle timeout, 1s retraction, warning red veins, dust particles |
| **ClimbableSurface** | [`Objects/ClimbableSurface.cs`](Bloop/Objects/ClimbableSurface.cs) | Climbable | Static body, TileSize=32, tileHeight 1-8, delegates render to WorldObjectRenderer |
| **StunDamageObject** | [`Objects/StunDamageObject.cs`](Bloop/Objects/StunDamageObject.cs) | Hazard | 48px sensor, 15 damage, 1.5s stun, 3s cooldown, lantern-detectable (200px), iris dilation |
| **FallingStalactite** | [`Objects/FallingStalactite.cs`](Bloop/Objects/FallingStalactite.cs) | Hazard | Ceiling hazard, shaking→falling→shatter→respawn cycle, proximity trigger |
| **FallingRubble** | [`Objects/FallingRubble.cs`](Bloop/Objects/FallingRubble.cs) | Hazard | 28px, 10 damage, motion-blur echo trail, radial debris burst, spawned by EarthquakeSystem |
| **CrystalCluster** | [`Objects/CrystalCluster.cs`](Bloop/Objects/CrystalCluster.cs) | Decorative | 3 variants (Cyan/Violet/Red), 44px sensor, shatter 5s, rainbow shard burst, orbiting motes |
| **IonStone** | [`Objects/IonStone.cs`](Bloop/Objects/IonStone.cs) | Decorative | Violet lightning arcs, LightSource with flicker/sputter |
| **PhosphorMoss** | [`Objects/PhosphorMoss.cs`](Bloop/Objects/PhosphorMoss.cs) | Decorative | 12-15 frond stalks, spore every ~2s, seeded frond count |
| **FlareObject** | [`Objects/FlareObject.cs`](Bloop/Objects/FlareObject.cs) | Thrown item | Dynamic physics body (12×6), 30s lifetime via FlareLight, landing detection, fog reveal |

### Minimal new object pattern
```csharp
using Bloop.World;
using Bloop.Physics;
using Bloop.Gameplay;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Objects
{
    public class MyHazard : WorldObject
    {
        public MyHazard(Vector2 pixelPosition, AetherWorld world) : base(pixelPosition, world)
        {
            Body = BodyFactory.CreateSensorRect(world, pixelPosition, 32f, 32f);
            Body.Tag = this;
        }

        public override void Update(GameTime gameTime)
        {
            if (IsDestroyed) return;
            // logic here
        }

        public override void Draw(SpriteBatch spriteBatch, AssetManager assets)
        {
            DrawPlaceholder(spriteBatch, assets, 32, 32, Color.OrangeRed);
        }

        public override Rectangle GetBounds() =>
            new((int)(PixelPosition.X - 16), (int)(PixelPosition.Y - 16), 32, 32);

        public override void OnPlayerContact(Player player) { /* activate */ }
        public override void OnPlayerSeparate(Player player) { }
        public override bool WantsPlayerContact => true;
    }
}
```

### Key `WorldObject` helpers
```csharp
DrawPlaceholder(spriteBatch, assets, widthPx, heightPx, fillColor, outlineColor?)
IsPlayerNearby(player, objectPixelPos, radiusPx)
IsLitByLantern(player, objectPixelPos, lanternRadiusPx = 200f)
```

---

## Entities — `Bloop.Entities`

### ControllableEntityType enum
`EchoBat | SilkWeaverSpider | ChainCentipede | LuminescentGlowworm | DeepBurrowWorm | BlindCaveSalamander | LuminousIsopod`

### Abstract members every entity must implement
```csharp
public abstract string DisplayName { get; }
public abstract float ControlDuration { get; }         // seconds player can possess
public abstract float MovementSpeed { get; }           // pixels/sec while controlled
protected abstract void UpdateControlled(GameTime gameTime);
protected abstract void UpdateIdle(GameTime gameTime);
protected abstract void OnControlStart();
protected abstract void OnControlEnd();
public abstract void Draw(SpriteBatch spriteBatch, AssetManager assets);
public abstract Rectangle GetBounds();
```

### Optional overrides
```csharp
public virtual bool CanFly           => false;
public virtual bool CanWallClimb     => false;
public virtual bool CanCeilingClimb  => false;
public virtual bool CanBurrow        => false;
public virtual bool DamagesPlayerOnContact => false;
public virtual float ContactDamage   => 0f;
public virtual float ContactStunDuration => 0f;
public virtual (string description, string? actionHint) GetTooltipInfo() => ("A cave creature.", null);
```

### Physics helpers (from ControllableEntity)
```csharp
ApplyImpulse(pixelDirection, pixelSpeed)   // sets LinearVelocity
SetVelocity(pixelVelocity)                 // direct velocity in pixel space
GetVelocityPixels()                        // returns pixel-space velocity
```

### Effect state fields (set by EntityControlSystem / skills / inter-entity)
```csharp
IsFollowing / FollowTarget / FollowPath   // same-type follow behavior
IsDisoriented / DisorientTimer            // different-type disorient (ALSO repurposed as FollowTimer!)
IsStuck / StuckTimer                      // immobilized (web/slime)
IsFleeing / FleeDirection / FleeTimer     // flee from light/entity
IsInfighting / InfightTimer               // aggro against non-same-type
```

**Known bug:** `DisorientTimer` is reused as `FollowTimer` — 6/7 entities set `DisorientTimer = followDuration` when `IsFollowing=true` and `IsDisoriented=false`. No dedicated `FollowTimer` field exists.

### Per-Entity Reference

| Entity | File | Move Speed | Ctrl Duration | Can Fly | Can Climb | Can Burrow | Damages On Contact | Skill | Inter-Entity Effect |
|--------|------|-----------|---------------|---------|-----------|------------|--------------------|-------|--------------------|
| **EchoBat** | `Entities/EchoBat.cs` | 300 | 8s | Yes | No | No | No (passive) | Echo Screech (Stuns nearby, reveals map in 150px radius, 6s cd) | **Same-type**: 2-4 follow formation, +15% speed. **Different**: fleeting path (Disoriented 1.5s) |
| **ChainCentipede** | `Entities/ChainCentipede.cs` | 200 | 10s | No | Yes (wall/ceiling) | No | Yes (5 dmg, 0.5s stun) | Chain Lightning (Jumps to nearby entities/terrain, 3-chain max, 8s cd) | **Same-type**: 3-5 caravan line, +5% speed each. **Different**: Infighting (1.5s) |
| **SilkWeaverSpider** | `Entities/SilkWeaverSpider.cs` | 180 | 12s | No | Yes (wall/ceiling) | No | Yes (8 dmg, 1s stun) | Pheromone Web (Slows + Stuck 3s, 120px trail, 10s cd) | **Same-type**: silk highways (+30% speed). **Different**: Stuck 2s |
| **LuminescentGlowworm** | `Entities/LuminescentGlowworm.cs` | 120 | 15s | No (hover) | No | No | No (passive) | Bioluminescence Flash (Blinds enemies + heals player 20 HP, 14s cd) | **Same-type**: 3-6 cluster. **Different**: Disoriented 2s. **Light reaction**: Flees lantern! |
| **DeepBurrowWorm** | `Entities/DeepBurrowWorm.cs` | 250 | 6s | No | No | Yes | Yes (15 dmg, 1.5s stun) | Seismic Burrow (Burrow underground, pop up at cursor, 300px max, 12s cd) | **Same-type**: tunnels. **Different**: Infighting 2.5s |
| **BlindCaveSalamander** | `Entities/BlindCaveSalamander.cs` | 150 | 10s | No | No (swims) | No | Yes (10 dmg, 1s stun) | Slimy Trail (Leaves slippery trail, enemies slip 2s, 8s cd) | **Same-type**: 2-3 patrol group. **Different**: Disoriented 1.5s. **Water**: Fast in water |
| **LuminousIsopod** | `Entities/LuminousIsopod.cs` | 180 | 20s | No | Yes | No | No (passive) | Glow Surge (Bright flash + pushback 120px, 10s cd) | **Same-type**: glow chain. **Different**: Flee from isopod. **Special**: Attaches to player (T to throw, trajectory preview) |

### Adding a new entity type
1. Add value to `ControllableEntityType` enum in [`ControllableEntity.cs`](Bloop/Entities/ControllableEntity.cs)
2. Create `Bloop/Entities/MyEntity.cs` extending `ControllableEntity`
3. In constructor call `BodyFactory.CreateEntityBody(world, pos, w, h, canFly)`
4. Implement all abstract members above
5. Register rendering in [`EntityRenderer.cs`](Bloop/Rendering/EntityRenderer.cs)
6. Register placement in [`ObjectPlacer.cs`](Bloop/Generators/ObjectPlacer.cs) (`PlaceControllableEntities()`)

### EntityControlSystem
- `StartSelection()` / `CancelSelection()` — enters selection mode
- Q key to toggle selection, LMB to select highlighted entity
- RMB to release control
- `GlobalCooldown = 3s` after releasing an entity
- Max control range: 200px from player
- Selection range circle drawn by [`EntityRenderer.DrawSelectionRangeCircle()`](Bloop/Rendering/EntityRenderer.cs:135)

---

## Lighting — `Bloop.Lighting`

### LightingSystem draw pipeline (in GameplayScreen.Draw order)
```
lightingSystem.BeginScene()           // clear scene → draw world onto sceneTarget
  → [draw world: tilemap, entities, objects, player]
lightingSystem.EndScene()             // restore render target
lightingSystem.Update(deltaSeconds)   // ticks lifetimes, removes expired lights
lightingSystem.RenderLightMap(camera) // additive radial gradients to lightTarget
lightingSystem.Composite(spriteBatch) // multiply scene × lightMap
  → [draw HUD — unaffected by lighting]
```

### Managing lights
```csharp
lightingSystem.AddLight(light)           // permanent or temporary
lightingSystem.RemoveLight(light)        // immediate removal
lightingSystem.ClearLights()             // remove all
// Temporary lights auto-removed when Lifetime reaches 0
```

### LightSource construction
```csharp
// Permanent light (Lifetime = -1)
new LightSource(pixelPosition, radius, intensity, color)

// Temporary light
new LightSource(pixelPosition, radius, intensity, color, lifetimeSeconds)
```

### LightSource properties
```csharp
light.Position   = pixelPosition;   // update each frame for moving lights
light.Radius     = 200f;            // pixels
light.Intensity  = 1f;              // 0..1
light.Color      = Color.Goldenrod;
// Flicker support
light.FlickerAmplitude  = 0.05f;    // ±5% radius variation
light.FlickerFrequency  = 10f;      // Hz
light.SputterChance     = 0.3f;     // probability/sec of brief dim-out
light.SwayOffset        = Vector2.Zero; // velocity-driven sway, set each frame
```

### Light types used in the game
| Source | Radius | Intensity | Color | Flicker | Sputter | Lifetime |
|--------|--------|-----------|-------|---------|---------|----------|
| Player lantern | 260px (base) | 2.4 (base) | Warm golden | 0.04–0.15 | No | Permanent (fuel-dependant, min 80px) |
| VentFlower aura | 120px | 0.8 | Cyan-green | 0.06 | 0.2 | Permanent |
| SporeLight (platform) | 160px | 0.9 | Pale purple | 0.08 | 0.15 | 15s (linear fade) |
| GlowVine active | 80px | 1.2 | Green | 0.05 | 0.1 | Permanent (while lit) |
| CrystalCluster | 100px | 0.7 | Per variant | 0.06 | 0.12 | Permanent |
| IonStone | 90px | 0.6 | Violet | 0.12 (5Hz) | 0.35 | Permanent |
| FlareLight (thrown) | 280px | 1.5 | Warm amber | 0.08 (7Hz) | 0.04 | 30s (fades final 5s) |
| ResonanceShard | 80px | 1.0 | Cyan-purple | 0.1 | 0.1 | Permanent |

### Ambient & color grading
```csharp
// Ambient level decreases with depth (see GetAmbientForDepth)
lightingSystem.AmbientLevel = 0.05f;  // 0.02 (deep) to 0.06 (shallow)

// Color grade tint per biome
lightingSystem.ColorGradeTint = biome switch {
    BiomeTier.ShallowCaves   => new Color(255, 235, 200),  // warm amber
    BiomeTier.FungalGrottos  => new Color(200, 230, 210),  // cool teal
    BiomeTier.CrystalDepths  => new Color(190, 210, 240),  // cold blue
    BiomeTier.TheAbyss       => new Color(200, 160, 160),  // deep red
};
```

### Post-processing
- Optional chromatic aberration effect via `Content/Shaders/ChromaticAberration.fx`
- Main lighting shader: `Content/Shaders/LightingEffect.fx`
- MaxLights = 256

---

## Rendering — `Bloop.Rendering`

### AnimationClock (global animation time)
```csharp
AnimationClock.Time         // float, updated once per frame
AnimationClock.Update(dt)   // called from Game1.Update
AnimationClock.Pulse(freq, phase) // sine 0→1→0
AnimationClock.Sway(amp, freq, phase) // signed sine
AnimationClock.Loop(period, phase) // sawtooth 0→1
```
All renderers use `AnimationClock` instead of `GameTime` for consistency.

### PlayerRenderer ([`PlayerRenderer.cs`](Bloop/Rendering/PlayerRenderer.cs))
State-driven spritesheet selection: idle/walking/jumping/climbing/controlling/stunned/dead.
- Climbing rotation: -90°
- Rope-attached rotation: toward anchor via `Atan2(dx, -dy)`
- Smoothed scale/rotation via `Smoothing.ExpDecay` + wrap-angle lerp

### EntityRenderer ([`EntityRenderer.cs`](Bloop/Rendering/EntityRenderer.cs))
- `DrawEntity(SpriteBatch, AssetManager, ControllableEntity, hitboxW, hitboxH, EntitySpritesheet?)`
- Viewport culling via `VisibleBounds`
- Danger indicator (red pulse aura, 80px radius)
- Entity highlight ring (control/selection/in-range)
- Effect state icons: following/fleeing/stuck/disoriented/infighting (drawn via GeometryBatch lines)
- Control timer bar + skill cooldown pip with partial arc

### TileRenderer ([`TileRenderer.cs`](Bloop/Rendering/TileRenderer.cs))
- 4 biome color palettes (ShallowCaves warm brown, FungalGrottos cool green, CrystalDepths cold blue, TheAbyss deep red)
- `SetBiome(BiomeTier)` — call once per level load
- Neighbor-aware drawing with organic edge erosion
- Stalactite drips, stalagmite bumps, wall protrusions
- Platform sections with moss sway + drips
- Slope scanline fill, climbable vine strands + leaves + glow dots
- Damage overlay with crack lines (`DrawDamageOverlay`)

### TileNeighborCache ([`TileNeighborCache.cs`](Bloop/Rendering/TileNeighborCache.cs))
- 8-bit neighbor bitmasks (bit 0=Top through bit 7=TopLeft)
- Refreshed per frame for visible tile bounds
- `IsInterior`, `IsTopExposed`, etc. queries
- OOB tiles treated as solid

### GeometryBatch ([`GeometryBatch.cs`](Bloop/Rendering/GeometryBatch.cs))
All drawing primitives use 1×1 Pixel texture. Methods:
- `DrawLine(sb, assets, a, b, color, thickness)` — rotated rect
- `DrawTriangle(sb, assets, a, b, c, color, thickness)` — thick lines
- `DrawTriangleSolid(sb, assets, a, b, c, color)` — scanline rasterization
- `DrawCircleApprox(sb, assets, center, radius, color, segments)` — pie slices
- `DrawCircleOutline(sb, assets, center, radius, color, segments, thickness)` — line segments
- `DrawRoundedRect(sb, assets, rect, radius, color)` — corner arcs
- `DrawPolygonOutline(sb, assets, vertices, color, thickness)` — any polygon
- `DrawDiamond(sb, assets, center, w, h, color)` / `DrawDiamondOutline`
- `DrawRotatedRect(sb, assets, center, w, h, angle, color)` — axis-aligned in rotated space
- `DrawDashedLine(sb, assets, a, b, color, dashLength, gapLength, thickness)`

### OrganicPrimitives ([`OrganicPrimitives.cs`](Bloop/Rendering/OrganicPrimitives.cs))
- `DrawBlob(sb, assets, center, radius, color, lobes, phase)` — breathing non-circular shape (per-angle radius = radius × (1 + sin(lobeAngle) × amplitude))
- `DrawGradientDisk(sb, assets, center, radius, innerColor, outerColor)` — concentric ring steps
- `DrawBezier(sb, assets, p0, p1, p2, p3, color, thickness)` — cubic bezier via De Casteljau
- `DrawBezierQuad(sb, assets, p0, p1, p2, color, thickness)` — quadratic bezier
- `DrawNoisyLine(sb, assets, a, b, color, thickness, noiseScale, noiseAmp)` — perpendicular sine-noise offset for living edges
- `DrawVeinNetwork(sb, assets, origin, length, dir, color, branches, depth)` — depth-2 recursive branching
- `DrawFacetedGem(sb, assets, center, size, color, facets, seed)` — polygonal with rotating specular highlight

### WorldObjectRenderer ([`WorldObjectRenderer.cs`](Bloop/Rendering/WorldObjectRenderer.cs))
1119-line static renderer class. Per-instance `VisualVariant` struct (HueShift/Scale/ShapeVariant/PhaseOffset). `ShiftHue(baseColor, degrees)` helper.
- `DrawDisappearingPlatform` — fungal shelf, breath animation, gill ridges, spore threads, red dissolving veins
- `DrawStunDamageObject` — pulsing barnacle eye, outer halo, fleshy blob, vein network, gradient iris, pupil dilation, orbiting blood sparkles
- `DrawGlowVine` — bezier stem (3 sections, sway), leaf fronds, photophore nodes, drift spore cloud, illumination progress bar
- `DrawRootClump` — stacked blobs (tapering column), bark veins, writhing tendrils, grip-node bumps, warning red, retraction dust
- `DrawVentFlower` — gradient aura, bezier stem + leaves, 5-7 petal blobs, gradient center, heat shimmer streaks, progress bar, cooldown overlay
- `DrawCaveLichen` — concentric rosette blobs (4-6 lobes), gill ribs, central bloom, orbital sparkles for rare
- `DrawBlindFish` — teardrop body, visible skeleton (bezier spine + ribs), tail fin, dorsal fin, dead eye, trailing bubbles
- `DrawClimbableSurface` — stone base, highlight cracks, wrapping bezier vines, grip-notch scars, moisture drips
- `DrawFlare` — glow halo, casing, rotating sparks, smoke wisps, dying warning pulse
- `DrawFlareTrajectory` — arc trajectory points with landing circle

### WorldProgressBar ([`WorldProgressBar.cs`](Bloop/Rendering/WorldProgressBar.cs))
- `Draw(worldPos, progress01, width, height, fgColor, bgColor, label?, yOffset)` — pulse on full, border, optional label
- `DrawCooldownOverlay(worldPos, cooldownProgress01, radius, overlayColor, remainingTime)` — grey-out gradient disk, segmented arc, time text with string cache

### Spritesheets
- [`EntitySpritesheet.cs`](Bloop/Rendering/EntitySpritesheet.cs) / [`EntitySpritesheetLoader.cs`](Bloop/Rendering/EntitySpritesheetLoader.cs) — horizontal strip, loaded from Pixelorama JSON + compiled PNG
- [`PlayerSpritesheet.cs`](Bloop/Rendering/PlayerSpritesheet.cs) / [`PlayerSpritesheetLoader.cs`](Bloop/Rendering/PlayerSpritesheetLoader.cs) — same pattern for player
- Loaded via `AssetManager.LoadEntitySpritesheets()` / `AssetManager.LoadPlayerSpritesheets()`

### NoiseHelpers ([`NoiseHelpers.cs`](Bloop/Rendering/NoiseHelpers.cs))
```csharp
NoiseHelpers.Hash01(int seed)              // [0, 1]
NoiseHelpers.HashSigned(int seed)          // [-1, 1]
NoiseHelpers.ValueNoise1D(float t, int seed)    // cosine-interpolated [0, 1]
NoiseHelpers.ValueNoise1DSigned(float t, int seed) // cosine-interpolated [-1, 1]
```

---

## Effects — `Bloop.Effects`

### DebuffSystem ([`DebuffSystem.cs`](Bloop/Effects/DebuffSystem.cs))
5 debuff types with refresh-stack behavior (longer duration wins):

| DebuffType | Effect | Display Name | Color |
|-----------|--------|-------------|-------|
| SlowMovement | 0.6× speed | SLOW | Blue-gray |
| InvertedControls | -1× flip | INVERT | Purple |
| ReducedJump | 0.5× jump | WEIGHT | Orange |
| Blurred | 0.7× lantern range | BLUR | Dark red |
| Weakened | 0.6× weight capacity | WEAK | Pale green |

```csharp
debuffs.ApplyDebuff(DebuffType.SlowMovement, duration)
debuffs.HasDebuff(DebuffType.SlowMovement)
debuffs.GetModifier(DebuffType.SlowMovement)  // returns 0.6f
debuffs.ClearAll()
debuffs.Update(deltaSeconds)
// Events: OnDebuffApplied, OnDebuffExpired
```

### ParticleSystem ([`ParticleSystem.cs`](Bloop/Effects/ParticleSystem.cs))
- 1200 pooled particles, seeded `Random(seed ^ 0xFACE)`
- `ParticleKind` enum: DustMote, RainStreak, RainSplash, WaterfallDrop, WaterfallMist, WaterDrip, DripSplash, CaveSpore, WallFriction
- `SetupEmitters(TileMap)` — scans tilemap for rain/waterfall/drip/spore positions (computed once)
- Viewport-culled spawning, dust motes push away from player
- `EmitWallFriction(contactPoint, fallSpeed)` — on-demand for sliding

### ObjectParticleEmitter ([`ObjectParticleEmitter.cs`](Bloop/Effects/ObjectParticleEmitter.cs))
- Per-object lightweight pool (struct array, zero per-frame allocations)
- Ring-buffer overwrite when full
- Used by all world objects for spores/drips/sparks/shards

### TrailEffect ([`TrailEffect.cs`](Bloop/Effects/TrailEffect.cs))
- 256 pooled trail particles for player motion
- Per-state spawning:
  - Walking → footstep dust (24px, grey)
  - Sliding → orange sparks (rate scaled by speed/200)
  - Falling → grey dust (scaled by speed/250)
  - Launching → white/cyan streaks (scaled by speed/350)
  - WallJump → yellow flash burst
  - Landing → radial dust ring
  - Grapple → anchor flash

---

## Audio — `Bloop.Audio`

### AudioManager ([`AudioManager.cs`](Bloop/Audio/AudioManager.cs))
```csharp
public float MasterVolume { get; set; } = 1f;
audioManager.TryLoad(content, "footstep_stone", "Audio/footstep_stone", AudioBus.Sfx);
audioManager.Play("footstep_stone", volume, pitch, pan);
audioManager.PlayVaried("footstep_stone", volume, pitchJitter: 0.08f);
audioManager.PlayAt("grapple_fire", listenerPx, emitterPx, maxDistancePx: 600f);
```
- 3 audio buses: Sfx (1.0), Ambience (0.7), Ui (0.9)
- Silent fail on missing assets (graceful degradation)
- Positional attenuation with squared falloff

### SfxKeys ([`SfxKeys.cs`](Bloop/Audio/SfxKeys.cs))
Centralized string constants — 69 entries organized by category:
- Movement: FootstepStone, FootstepDirt, FootstepCrystal, Jump, LandSoft, LandHard, WallSlide, WallJumpKick, MantlePull
- Grapple/Rope: GrappleFire, GrappleHit, GrappleHitCrystal, RopeTaut, RopeWrap, RopeRelease, LaunchWhoosh
- Resources: LowHealthHeartbeat, LowBreathWheeze, LowFuelAlarm, SanityWhisper
- Combat: DamageHit, StunHit, FallDamage, Death, DebuffApplied, PoisonGulp
- Items: ItemPickup, ItemUse, FlareThrow, FlareImpact
- Entity: PossessEnter, PossessExit, EntitySkill
- UI: UiClick, UiHover, InventoryOpen, InventoryClose
- World: CaveDrip, DistantRumble, RubbleFall

---

## Level Generation — `Bloop.Generators`

### Seed determinism
All generation uses seeded `Random` instances. The level seed is passed from [`SeedInputScreen`](Bloop/Screens/SeedInputScreen.cs) through [`GameplayScreen`](Bloop/Screens/GameplayScreen.cs) to [`LevelGenerator`](Bloop/Generators/LevelGenerator.cs).

### LevelGenerator ([`LevelGenerator.cs`](Bloop/Generators/LevelGenerator.cs))
- Entry point for all generation
- Takes seed, depth → produces Level with TileMap, objects, entities
- Delegates to sub-systems: tile generation, room template injection, object placement, path validation

### PerlinNoise ([`PerlinNoise.cs`](Bloop/Generators/PerlinNoise.cs))
- Seed-based, 256-entry permutation table (Fisher-Yates shuffle)
- 8 gradient vectors, quintic fade (`6t⁵ - 15t⁴ + 10t³`)
- `Sample(x, y)` — single octave
- `SampleOctaves(x, y, octaves, persistence, lacunarity)` — fractal noise
- `GenerateGrid(width, height, octaves, persistence, lacunarity, scale)` — 2D grid

### CavityAnalyzer ([`CavityAnalyzer.cs`](Bloop/Generators/CavityAnalyzer.cs))
Analyzes tile maps for placement context. `CavityFlags` enum (Flags):
- `Surfaces` — top-exposed solid tiles (placement zones for plants)
- `OpenRadius` — reachable air pocket size (BFS capped)
- `DeadEnds` — tiles in passage endings
- `ShaftBottoms` — bottom of vertical shafts (water pool locations)
- `Junctions` — tile intersections
- `NarrowPassages` — 1-2 tile wide corridors

### PathValidator ([`PathValidator.cs`](Bloop/Generators/PathValidator.cs))
- BFS with player-height clearance (3-tile footprint)
- `DijkstraAvoidingPath` — alternate routes with `PrimaryPathPenalty = 5`
- `CarveAlternateWormTunnel` — when paths >40% overlap, carve worm-burrow shortcuts

### RoomTemplates ([`RoomTemplates.cs`](Bloop/Generators/RoomTemplates.cs))
`LandmarkKind` enum: 8 landmark room types (crystal chamber, ritual circle, collapsed tunnel, etc.)
- Templates are ASCII-string arrays, injected into tilemap via `CarveTemplate`
- Random placement with overlap detection, limited per depth

### ObjectPlacer ([`ObjectPlacer.cs`](Bloop/Generators/ObjectPlacer.cs))
`ObjectType` enum with 14+ types. 1761-line placement system.

**DifficultyProfile** — objects/entities unlock progressively:
- Level 1: VentFlower, CaveLichen, DisappearingPlatform (basics)
- Level 4: EchoBat, BlindCaveSalamander, RootClump
- Level 7: SilkWeaverSpider, StunDamageObject, CrystalCluster, FallingStalactite
- Level 11: ChainCentipede, CrystalBridge, IonStone
- Level 16: DeepBurrowWorm
- Density scales: 0.5 (level 1) → 1.5 (level 30)

**Placement strategies per object type:**
- `PlaceDisappearingPlatforms` — over gaps >3 tiles, domino-chained
- `PlaceShaftStaircases` — vertical shaft navigation
- `PlaceStunObjects` — wall-attached, >4 tile spacing
- `PlaceGlowVines` — dark shaft walls, requires nearby surface
- `PlaceRootClumps` — wall-attached, passable corridors
- `PlaceVentFlowers` — on surfaces, <2 per cavity
- `PlaceCaveLichen` — on surfaces, clustered
- `PlaceResonanceShards` — distributed across map, edge-biased
- `PlaceBlindFish` — in water pools
- `PlaceFallingStalactites` — ceilings, >3 tile spacing
- `PlaceGlowObjects` — decorative (IonStone, PhosphorMoss, CrystalCluster)
- `PlaceCrystalBridges` — over large gaps >6 tiles
- `PlaceControllableEntities` — BFS-weighted distribution, per-type limits

### DominoChainLinker ([`DominoChainLinker.cs`](Bloop/Generators/DominoChainLinker.cs))
Union-Find (Disjoint Set) grouping of disappearing platforms within 6-tile radius.
Spatial sort: Y-major, X-minor. Seed-derived RNG.

---

## Core Systems — `Bloop.Core`

### Game1 ([`Game1.cs`](Bloop/Game1.cs))
Main game class. Initializes all managers (physics, lighting, input, asset, audio, screen).
Update loop: `ElapsedGameTime` → physics step → entity control → gameplay screen.

### Screen / ScreenManager ([`Screen.cs`](Bloop/Core/Screen.cs), [`ScreenManager.cs`](Bloop/Core/ScreenManager.cs))
Stack-based screen management:
```csharp
ScreenManager.Push(screen)     // push overlay
ScreenManager.Pop()            // remove top screen
ScreenManager.Replace(screen)  // pop then push
```
Each screen has `BlocksUpdate` and `BlocksDraw` for overlay behavior.

### Camera ([`Camera.cs`](Bloop/Core/Camera.cs))
ExpDecay smooth-follow camera:
- Default follow rate ≈ 7.66 (`RateFromPerTick60Hz(0.12)`)
- Velocity lookahead: 0.15×, capped 48px, upward suppressed
- Screen shake with linear decay (amplitude, duration, direction)
- World-space bounds clamping
- `GetTransform()` → Matrix for sprite batch
- `ScreenToWorld` / `WorldToScreen` conversions
- Uses `new Random()` (unseeded) for shake direction — non-deterministic

### InputManager ([`InputManager.cs`](Bloop/Core/InputManager.cs))
- Keyboard + mouse + gamepad support
- Virtual cursor for gamepad (right-stick, 900px/s)
- `InputMode` enum: Normal, EntitySelecting, EntityControlling
- Stick deadzone: 0.18, walk threshold: 0.55
- `GetMouseWorldPosition()` — world-space cursor position

### ResolutionManager ([`ResolutionManager.cs`](Bloop/Core/ResolutionManager.cs))
- Native resolution rendering (no virtual render target)
- ToggleFullscreen, OnWindowResize
- `BeginDraw`/`EndDraw` for viewport management

### Smoothing ([`Smoothing.cs`](Bloop/Core/Smoothing.cs))
Frame-rate-independent ExpDecay helpers:
```csharp
Smoothing.ExpDecay(current, target, rate, dt)     // float
Smoothing.ExpDecay(current, target, rate, dt)     // Vector2
Smoothing.RateFromPerTick60Hz(perTickFactor)      // 1 - (1-perTickFactor)^(60*dt)
```

### AssetManager ([`AssetManager.cs`](Bloop/Core/AssetManager.cs))
Centralized asset loading:
- `Pixel` — 1×1 white texture
- `GameFont` / `MenuFont` — SpriteFonts (may be null)
- `GetSharedRadialGradient()` — 512×512 shared gradient (scaled at draw time, not per-diameter)
- `CreateRadialGradient(diameter)` — create new gradient
- `GetSolidTexture(w, h, color)` — cached colored rectangle
- `LoadPlayerSpritesheets()`, `LoadObjectSpritesheets()`, `LoadEntitySpritesheets()`
- Draw helpers: DrawRect, DrawRectOutline, DrawString, DrawStringCentered, DrawMenuString, DrawMenuStringCentered

---

## Screens (`Bloop.Screens`)

### MainMenuScreen ([`MainMenuScreen.cs`](Bloop/Screens/MainMenuScreen.cs))
Items: Start Game → SeedInputScreen, Load Game → LoadGameScreen, Options → OptionsScreen, Quit.
Dark background with scanline effect, flickering title.

### SeedInputScreen ([`SeedInputScreen.cs`](Bloop/Screens/SeedInputScreen.cs))
Text input via `Window.TextInput` event. Numeric only, up to 10 digits. Empty = random seed (100000-999999).
On confirm: replaces stack with [`GameplayScreen`](Bloop/Screens/GameplayScreen.cs)(seed, startDepth: 1).

### GameplayScreen ([`GameplayScreen.cs`](Bloop/Screens/GameplayScreen.cs))
1394-line orchestration screen. Handles:
- Level loading/generation
- World rendering pipeline (scene → lights → composite)
- HUD drawing (7 stat bars, shard counter, contextual prompts, action bar)
- Sanity system with progressive debuffs
- Level transition (auto-save, Depth++, ReloadLevel, fade 0.45s, lantern warm-up 1.5s)
- Pause → PauseScreen, Save → SaveManager
- Death → GameOverScreen

**HUD bars (in display order):** Lantern Fuel, Breath, Health, Kinetic Charge, Sanity, Flares, Weight
Additional: active debuff strip, shard counter with pulse + EXIT OPEN glow, damage flash vignette.

### PauseScreen ([`PauseScreen.cs`](Bloop/Screens/PauseScreen.cs))
Overlay (BlocksDraw=false). Items: Resume, Save Game, Options, Quit to Menu.

### OptionsScreen ([`OptionsScreen.cs`](Bloop/Screens/OptionsScreen.cs))
Volume slider with drag + keyboard adjustment. Controls reference list. `GameSettings.MasterVolume` static field.

### GameOverScreen ([`GameOverScreen.cs`](Bloop/Screens/GameOverScreen.cs))
Shows seed, depth reached, cause of death. Options: Try Again (same seed), New Seed, Main Menu.

### LoadGameScreen ([`LoadGameScreen.cs`](Bloop/Screens/LoadGameScreen.cs))
Lists JSON save files from `Saves/` directory. Shows seed, depth, date. Supports delete.

### LoreModalScreen ([`LoreModalScreen.cs`](Bloop/Screens/LoreModalScreen.cs))
Gothic diary-entry modal. Gothic dark palette, pulsing border. Shows title/author/content/portal hint/sanity delta. Blocks update but not draw. Dismiss with any key.

---

## UI (`Bloop.UI`)

### ActionButtonBar ([`ActionButtonBar.cs`](Bloop/UI/ActionButtonBar.cs))
Bottom-center visual key-cap buttons. Contextual: normal (Jump/Rappel/Climb/Grapple/Flare/Bag/Pause), controlling (WASD/Skill/Release), selecting (LMB/Q). Flare count dots. Scaling on narrow windows.

### EntityControlHUD ([`EntityControlHUD.cs`](Bloop/UI/EntityControlHUD.cs))
Screen-space HUD for entity control system:
- Q button at center-bottom with cooldown ring + pulsing ready indicator
- Selection prompt during selection mode
- Control duration bar at top-center
- Entity name + skill name during control
- Isopod attachment UI (icon, timer, throw prompt, glow surge cooldown)
- Edge arrows (Task 13.3) pointing to off-screen entities (cyan=friendly, red=hostile)

### HoverTooltip ([`HoverTooltip.cs`](Bloop/UI/HoverTooltip.cs))
Screen-space tooltip under mouse. Tests objects then tile grid. Color-coded borders (red=hazard, green=beneficial, cyan=controllable). Rich multi-line format: name, effect, stats, action hint.

### InventoryUI ([`InventoryUI.cs`](Bloop/UI/InventoryUI.cs))
Toggleable (Tab) right-side panel with slide-in animation (ExpDecay). Shows item list with weight, poison indicators, weight summary, active debuffs. Arrow keys navigate, E/Enter to use.

### Minimap ([`Minimap.cs`](Bloop/UI/Minimap.cs))
Bottom-right corner. One pixel = one tile (scale 2). Fog-of-reveal (discovered tiles only). Always-visible pulsing shard markers. Entity dots (red=hostile, green=friendly). Exit marker (locked/unlocked). Player dot on top.

---

## World Systems — `Bloop.World`

### Tile / TileMap ([`Tile.cs`](Bloop/World/Tile.cs), [`TileMap.cs`](Bloop/World/TileMap.cs))
```csharp
enum TileType { Empty, Solid, Platform, SlopeLeft, SlopeRight, Climbable }
TileProperties.IsSolid(type)     // Solid|Platform check
TileProperties.GetColor(type)    // debug color
```
TileMap: 2D array, TileSize=32, methods for GetTile/SetTile, neighbor checks.

### Level ([`Level.cs`](Bloop/World/Level.cs))
Holds TileMap, list of WorldObject, exit point, discovered tiles (fog of war), IsExitUnlocked flag.
- `EntryPoint` / `ExitPoint` — Vector2 pixel positions
- `Objects` — List of WorldObject
- `Discovered` — bool[,] fog of war
- `AddObject(WorldObject)`, `RemoveObject(WorldObject)` — lifecycle
- `Update(GameTime, Player)` — updates all active objects
- Cleanup: objects call `Destroy()` which marks them, Level removes on next update

### WaterPool ([`WaterPool.cs`](Bloop/World/WaterPool.cs))
- Detects shaft-bottom tiles via CavityAnalyzer
- Water physics: 60% speed cap, 25% entry velocity damp, exponential drag
- Animated surface with caustic streaks, splash particles, depth gradient rendering
- Spawn splash with `new Random()` (unseeded)

### EarthquakeSystem ([`EarthquakeSystem.cs`](Bloop/World/EarthquakeSystem.cs))
Full state machine: Idle → Warning (1-2s) → Active (supports propagation, chain-length 3) → Aftershock (2-4s) → Idle
- Biome-scaled destruction: ShallowCaves=1, FungalGrottos=2, CrystalDepths=3, TheAbyss=5
- Protected zones around entry/exit (160px radius)
- BFS reachability validation after collapse — rolls back if exit becomes unreachable
- Support propagation: if a tile collapses, tiles below it check support chain

---

## Save / Load — `Bloop.SaveLoad`

### SaveData ([`SaveData.cs`](Bloop/SaveLoad/SaveData.cs))
JSON-serializable model with `System.Text.Json`:
- Meta: saveDate, saveVersion (v1)
- Run info: seed, currentDepth
- Player stats: health, maxHealth, breathMeter, lanternFuel, sanity
- Inventory: List of SavedItem (type + quantity)
- Discovered tiles: Dictionary<int, List<string>> per depth ("x,y" format)

### SaveManager ([`SaveManager.cs`](Bloop/SaveLoad/SaveManager.cs))
- Saves to `{BaseDirectory}/Saves/save_{seed}_depth{depth}.json`
- Atomic writes (temp file + rename)
- `GetSaveFiles()` — sorted newest-first
- `Load(filePath)` — returns null on failure
- `LoadLatestForSeed(seed)` — convenience

---

## Lore — `Bloop.Lore`

### LoreEntry ([`LoreEntry.cs`](Bloop/Lore/LoreEntry.cs))
`record LoreEntry(string Title, string Author, string Content, string PortalHint, int SanityDelta)`

### LoreGenerator ([`LoreGenerator.cs`](Bloop/Lore/LoreGenerator.cs))
5 themes (biome-biased), each with 6-7 authors, titles, content paragraphs, and portal hints.
- Theme 0: Lost Expedition (ShallowCaves bias)
- Theme 1: Ancient Ritual (CrystalDepths bias)
- Theme 2: Cave Madness (FungalGrottos / TheAbyss bias)
- Theme 3: Void Corruption (CrystalDepths / TheAbyss bias)
- Theme 4: Forgotten Depth (ShallowCaves / FungalGrottos bias)

`GenerateForLevel(seed, shardCount, biome)` — deterministic per seed. SanityDelta: first shard -15, next two -10, then random ±5 (Void Corruption harsher at -10).

---

## Known Issues / TODOs

### Seed Determinism Violations (critical — breaks reproducible worlds)
The following use `new Random()` without a deterministic seed:
1. **ALL 7 entities** — idle AI uses unseeded Random for wander targets, patrol direction, flee direction, burrow decisions
2. **LuminescentGlowworm** — uses **static** `_syncRng = new Random()` (shared across instances)
3. **Camera.cs** — `new Random()` for shake direction (line 50)
4. **LightSource.cs** — `new Random(position.GetHashCode())` — deterministic per position but not from level seed
5. **CrystalBridge.cs** — `new Random(_seed + (int)(AnimationClock.Time * 100f))` — non-deterministic due to AnimationClock.Time
6. **WaterPoolSystem** — splash particles use `new Random()` (line 61)
7. **TrailEffect** — uses `new Random()` (line 63)
8. **AudioManager** — uses `new Random()` (line 38)
9. **SeedInputScreen** — random seed uses `new Random().Next()` instead of `Random.Shared.Next()`

### Physics / Disposal Issues
- **WorldObject.Destroy()** calls `World.Remove(Body)` directly, NOT `PhysicsManager.QueueRemoveBody()` — potential thread-safety issue. Should use deferred removal.

### DisorientTimer Reuse Bug
- `DisorientTimer` is repurposed as `FollowTimer` in 6/7 entities (all except EchoBat?). Entities set `DisorientTimer = followDuration` when `IsFollowing=true` and `IsDisoriented=false`. This means follow behavior and disorient behavior share the same timer field, which would conflict if both are active simultaneously.

### Audio / Content
- Audio assets don't exist yet — all SFX keys are defined but TryLoad calls will fail silently
- Content pipeline files (`.xnb`) may not be compiled for spritesheets
- "TODO: play sound effect" comments scattered through codebase

### Rendering
- Lighting currently uses CPU-side radial gradients — "placeholder until Phase 9 HLSL lighting" comments
- ChromaticAberration.fx shader exists but may not be wired up

---

## Conventions & Critical Rules

### Null-reference types
Nullable annotations are enabled. Always annotate nullable fields:
```csharp
private Body? _body;
private LightSource? _light;
protected ScreenManager ScreenManager { get; private set; } = null!; // initialized in Initialize()
```

### Seed determinism — MANDATORY
Never use `new Random()` (unseeded) anywhere in generation code.
Seeded instances come from `LevelGenerator` and are passed down.
```csharp
// WRONG:
var rng = new Random();
// CORRECT:
// receive seeded Random from LevelGenerator/ObjectPlacer, or use:
var rng = new Random(HashCode.Combine(baseSeed, localIndex));
```

### Physics body disposal
- Call `Destroy()` (WorldObject) or `_physicsManager.QueueRemoveBody(body)` to remove bodies.
- Never call `World.Remove(body)` directly inside an Aether collision callback.
- Bodies removed via `QueueRemoveBody` are flushed at the start of `PhysicsManager.Step`.

### Content loading
- All textures/fonts loaded through `AssetManager`.
- Content pipeline assets referenced in [`Bloop/Content/Content.mgcb`](Bloop/Content/Content.mgcb).
- Runtime-generated textures (colored rects, gradients) via `AssetManager`.

### Collision fixture setup
Always set both `CollisionCategories` (what the fixture IS) and `CollidesWith` (what it hits):
```csharp
fixture.CollisionCategories = CollisionCategories.Hazard;
fixture.CollidesWith        = CollisionCategories.Player;
```

### Aether namespace alias
All files that import both `Bloop.World` and Aether must use the alias:
```csharp
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;
```

### Drawing Conventions
- All geometry drawing uses `GeometryBatch` static methods with `AssetManager.Pixel` (1×1 white texture)
- World-space draw: inside `SpriteBatch.Begin` with camera transform matrix
- Screen-space draw (HUD): inside `SpriteBatch.Begin` WITHOUT camera transform
- Light rendering: additive blend mode
- Always use `AnimationClock.Time` for animation, not `GameTime`
- Use `Smoothing.ExpDecay` for lerp-based smoothing

### Code Style
- XML doc comments on all public types and methods
- Regions/spaced sections with `// ── Section Name ──` separators
- Constants at top of classes, private fields next, then public API
- PascalCase for public members, _camelCase for private fields
- File-scoped namespaces NOT used (classic `namespace { }` blocks)
