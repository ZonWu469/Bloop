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

**Solution:** `Bloop.sln` → project: `Bloop/Bloop.csproj`

---

## Architecture Map

```
Bloop/
├── Core/           AssetManager, Camera, InputManager, ResolutionManager, Screen, ScreenManager
├── Entities/       ControllableEntity (base), EntityControlSystem, EntitySkill, + 7 enemy types
├── Gameplay/       Player, PlayerController, PlayerStats, Inventory, GrapplingHook,
│                   RopeSystem, RopeWrapSystem, MomentumSystem
├── Generators/     LevelGenerator (entry), ObjectPlacer, RoomTemplates, PathValidator,
│                   CavityAnalyzer, DominoChainLinker, PerlinNoise
├── Lighting/       LightingSystem, LightSource, SporeLight, FlareLight
├── Objects/        WorldObject subclasses — collectibles (CaveLichen, BlindFish) and
│                   hazards/platforms (FallingStalactite, DisappearingPlatform, etc.)
├── Physics/        PhysicsManager, BodyFactory, CollisionCategories, PhysicsDebugDraw
├── Rendering/      EntityRenderer, GeometryBatch, OrganicPrimitives, AnimationClock, NoiseHelpers
├── Screens/        Screen subclasses (gameplay, menus, pause, etc.)
├── UI/             HUD components
├── World/          WorldObject (base), level state coordination
└── SaveLoad/       JSON save/load
```

---

## Physics — `Bloop.Physics`

### Key constants
```csharp
PhysicsManager.PIXELS_PER_METER = 64f   // 64 pixels = 1 meter
PhysicsManager.Gravity          = new Vector2(0f, 20f)  // m/s² downward
```

### Unit conversion — ALWAYS convert before passing to Aether
```csharp
PhysicsManager.ToMeters(pixelVec)   // Vector2 or float: pixels → meters
PhysicsManager.ToPixels(meterVec)   // Vector2 or float: meters → pixels
```

### Stepping — sub-stepped at 120 Hz, max 4 sub-steps per frame
```csharp
_physicsManager.Step((float)gameTime.ElapsedGameTime.TotalSeconds);
```

### Body removal — never remove bodies inside collision callbacks
```csharp
_physicsManager.QueueRemoveBody(body);  // deferred, safe inside callbacks
```

### BodyFactory — use these, never call world.CreateBody directly for game objects
```csharp
BodyFactory.CreatePlayerBody(world, pixelPosition)
BodyFactory.CreateStaticRect(world, pixelCenter, pixelW, pixelH, category)
BodyFactory.CreateSensorRect(world, pixelCenter, pixelW, pixelH)
BodyFactory.CreateTerrainChain(world, pixelVertices)
BodyFactory.CreateEntityBody(world, pixelPosition, pixelW, pixelH, canFly)
BodyFactory.CreateGrappleBody(world, pixelPosition)
BodyFactory.CreateStalactiteBody(world, pixelPosition)
```

### CollisionCategories bitmasks
```csharp
CollisionCategories.Terrain            // Cat1 — static terrain tiles
CollisionCategories.Platform           // Cat2 — one-way platforms
CollisionCategories.Player             // Cat3 — player body
CollisionCategories.Trigger            // Cat4 — sensor-only zones
CollisionCategories.DisappearingPlatform // Cat5
CollisionCategories.Hazard             // Cat6 — stun/damage objects
CollisionCategories.Climbable          // Cat7 — glow vines, root clumps
CollisionCategories.GrappleHook        // Cat8
CollisionCategories.Collectible        // Cat9
CollisionCategories.WorldObject        // Cat10 — falling stalactites, etc.
CollisionCategories.Entity             // Cat11 — cave entities (sensor select + physics)
CollisionCategories.CrystalBridge      // Cat12

// Pre-composed masks
CollisionCategories.PlayerCollidesWith      // Terrain|Platform|DisappearingPlatform|Hazard|Climbable|Collectible|CrystalBridge
CollisionCategories.GrappleCollidesWith     // Terrain|Climbable|CrystalBridge
CollisionCategories.TriggerCollidesWith     // Player
CollisionCategories.EntityCollidesWith      // Terrain|Platform|DisappearingPlatform
```

---

## World Objects — `Bloop.World.WorldObject`

All collectibles, hazards, and platforms extend `WorldObject`. It handles:
- Optional `Body? Body` (Aether, pixel position auto-derived via `ToPixels`)
- `IsActive`, `IsDestroyed` lifecycle flags
- `Destroy()` — removes body, marks destroyed; called by subclass, cleanup done by Level

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

        // For sensor-based pickup, override these:
        public override void OnPlayerContact(Player player) { /* activate */ }
        public override void OnPlayerSeparate(Player player) { }

        // OR use the AABB fallback (more reliable for collectibles):
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

### Effect state fields (set by EntityControlSystem / skills)
```csharp
IsFollowing / FollowTarget / FollowPath   // same-type follow
IsDisoriented / DisorientTimer            // different-type disorient
IsStuck / StuckTimer                      // immobilized (web/slime)
IsFleeing / FleeDirection / FleeTimer     // flee from light/entity
IsInfighting / InfightTimer               // aggro against non-same-type
```

### Adding a new entity type
1. Add value to `ControllableEntityType` enum in `ControllableEntity.cs`
2. Create `Bloop/Entities/MyEntity.cs` extending `ControllableEntity`
3. In constructor call `BodyFactory.CreateEntityBody(world, pos, w, h, canFly)`
4. Implement all abstract members above
5. Register rendering in `EntityRenderer`

---

## Lighting — `Bloop.Lighting`

### LightingSystem draw pipeline (in GameplayScreen.Draw order)
```
lightingSystem.BeginScene()
  → [draw world: level, player, objects]
lightingSystem.EndScene()
lightingSystem.Update(deltaSeconds)   // ticks lifetimes, removes expired
lightingSystem.RenderLightMap(camera) // additive radial gradients to lightTarget
lightingSystem.Composite(spriteBatch) // multiply scene × lightMap
  → [draw HUD — unaffected by lighting]
```

### Managing lights
```csharp
lightingSystem.AddLight(light)           // permanent or temporary
lightingSystem.RemoveLight(light)        // immediate removal
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

### Standard light sizes used in the game
| Source         | Radius | Color              |
|----------------|--------|--------------------|
| Player lantern | 200px  | Warm yellow/gold   |
| VentFlower     | 120px  | Cyan-green         |
| SporeLight     | 160px  | Pale purple        |
| GlowVine active| 80px   | Green              |

### Ambient level
```csharp
lightingSystem.AmbientLevel = 0.05f; // 0.0 = pitch black, 0.08 = shallow cave
lightingSystem.ColorGradeTint = Color.White; // depth/biome tint
```

---

## Core Systems

### Screen — `Bloop.Core.Screen`
```csharp
public class MyScreen : Screen
{
    public override bool BlocksUpdate => true;  // false for overlays
    public override bool BlocksDraw   => true;  // false for transparent overlays

    public override void Initialize(ScreenManager sm, GraphicsDevice gd) { base.Initialize(sm, gd); }
    public override void LoadContent() { }
    public override void UnloadContent() { }
    public override void Update(GameTime gameTime) { }
    public override void Draw(GameTime gameTime, SpriteBatch spriteBatch) { }
}
// Push/pop via: ScreenManager.Push(screen) / ScreenManager.Pop()
```

### AssetManager — `Bloop.Core.AssetManager`
```csharp
// ALWAYS load assets through AssetManager, never Content.Load directly
assets.Pixel                          // 1×1 white texture — tint any color
assets.GameFont / assets.MenuFont     // SpriteFonts (may be null)
assets.GetSolidTexture(w, h, color)   // cached colored rect
assets.CreateRadialGradient(diameter) // cached radial gradient for lights
assets.DrawRect(sb, rect, color)
assets.DrawRectOutline(sb, rect, color, thickness)
assets.DrawString(sb, text, position, color, scale)
assets.DrawStringCentered(sb, text, y, color, scale)
assets.DrawMenuString(...)
assets.DrawMenuStringCentered(...)
```

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
// receive seeded Random from LevelGenerator/ObjectPlacer
```

### Physics body disposal
- Call `Destroy()` (WorldObject) or `_physicsManager.QueueRemoveBody(body)` to remove bodies.
- Never call `World.Remove(body)` directly inside an Aether collision callback.
- Bodies removed via `QueueRemoveBody` are flushed at the start of `PhysicsManager.Step`.

### Content loading
- All textures/fonts loaded through `AssetManager`.
- Content pipeline assets referenced in `Bloop/Content/Content.mgcb`.
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
