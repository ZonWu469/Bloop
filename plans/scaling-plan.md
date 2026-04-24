# Scaling Plan: Player 1.5×, Entities & World Objects 2×

## Overview

After analysis, the tile system (`TileMap.TileSize = 32px`) stays unchanged. All pixel dimensions below are based on this 32px fixed tile size.

| Entity | Current | Scaled | Tiles (approx) |
|--------|---------|--------|----------------|
| **Player** (standing) | 24×40 | **36×60** | 1.125 × 1.875 tiles |
| **Player** (crouch) | 24×20 | **36×30** | 1.125 × 0.9375 tiles |
| EchoBat | 16×10 | **32×20** | 1.0 × 0.625 tiles |
| SilkWeaverSpider | 20×14 | **40×28** | 1.25 × 0.875 tiles |
| ChainCentipede | 30×8 | **60×16** | 1.875 × 0.5 tiles |
| DeepBurrowWorm | 10×24 | **20×48** | 0.625 × 1.5 tiles |
| BlindCaveSalamander | 22×10 | **44×20** | 1.375 × 0.625 tiles |
| LuminescentGlowworm | 12×8 | **24×16** | 0.75 × 0.5 tiles |
| LuminousIsopod | 14×8 | **28×16** | 0.875 × 0.5 tiles |

---

## Part A — Player Scaling (1.5×)

### A1. [`Player.cs`](Bloop/Gameplay/Player.cs:59) — Dimension Constants
- `WidthPx`: `24f` → **`36f`**
- `StandingHeightPx`: `40f` → **`60f`**
- `CrouchHeightPx`: `20f` → **`30f`**

### A2. [`BodyFactory.cs`](Bloop/Physics/BodyFactory.cs:42) — Player Body Creation
- `CreatePlayerBody`: hardcoded call to `CreatePlayerFixtures(body, 24f, 40f)` → update to **`36f, 60f`**

### A3. [`BodyFactory.cs`](Bloop/Physics/BodyFactory.cs:49) — `CreatePlayerFixtures`
- Main rect fixture uses `pixelWidth` × `pixelHeight` (already parameterized — no change needed)
- Foot sensor: `halfW * 1.6f` wide → may need widening proportionally (currently `12 * 1.6 = 19.2`; after scaling `18 * 1.6 = 28.8`)
- Foot sensor 4px tall → consider 6px (keep proportional)

### A4. [`BodyFactory.cs`](Bloop/Physics/BodyFactory.cs:74) — `ReplacePlayerFixtures`
- Used during crouch/stand transitions — passes `WidthPx` and `newHeightPx` already, so will use the new constants automatically.

### A5. [`PlayerController.cs`](Bloop/Gameplay/PlayerController.cs) — Movement Tuning (Review)
These may need adjustment since a larger body has more inertia:
- `MoveForce = 500f` — may need increase (proportionally ~750f)
- `JumpImpulse = 160f` — may need increase (~240f)
- `WallJumpVertical/Horizontal` — scale proportionally
- `ClimbSpeed = 100f` — may stay (visual speed)
- `MaxHorizontalSpeed = 180f` — consider scaling
- `MantleHeadTolerance = 12f` — scale to **18f**

### A6. [`Player.cs`](Bloop/Gameplay/Player.cs:611) — `CheckSolidOverlap`
Currently searches for **2-tile-tall** clear columns (line 645: `"Need 2 clear tiles vertically"`).
- Player is now 1.875 tiles tall — 2 tiles is tight (4px margin). Upgrade search to **3-tile-tall** clear columns for safety margin.

### A7. Player spritesheet JSONs (Content/Data/Player/*.png.json)
- These JSON files define sprite source rectangles. Each sheet's `FrameWidth`/`FrameHeight` may need updating if you want the sprite art to render at the new scale. However, if the game already uses scale-independent rendering via `PlayerRenderer`, this may not be needed. **Verify before changing.**

---

## Part B — Entity Scaling (2×)

### B1–B7. Modify dimension constants in each entity constructor

| File | Const | Current → New |
|------|-------|--------------|
| [`EchoBat.cs`](Bloop/Entities/EchoBat.cs:29) | `WidthPx` / `HeightPx` | 16×10 → **32×20** |
| [`SilkWeaverSpider.cs`](Bloop/Entities/SilkWeaverSpider.cs:23) | `WidthPx` / `HeightPx` | 20×14 → **40×28** |
| [`ChainCentipede.cs`](Bloop/Entities/ChainCentipede.cs:23) | `WidthPx` / `HeightPx` | 30×8 → **60×16** |
| [`DeepBurrowWorm.cs`](Bloop/Entities/DeepBurrowWorm.cs:23) | `WidthPx` / `HeightPx` | 10×24 → **20×48** |
| [`BlindCaveSalamander.cs`](Bloop/Entities/BlindCaveSalamander.cs:23) | `WidthPx` / `HeightPx` | 22×10 → **44×20** |
| [`LuminescentGlowworm.cs`](Bloop/Entities/LuminescentGlowworm.cs:25) | `WidthPx` / `HeightPx` | 12×8 → **24×16** |
| [`LuminousIsopod.cs`](Bloop/Entities/LuminousIsopod.cs:33) | `WidthPx` / `HeightPx` | 14×8 → **28×16** |

### B8. [`BodyFactory.cs`](Bloop/Physics/BodyFactory.cs:227) — `CreateEntityBody`
- Already parameterized with `pixelWidth`/`pixelHeight` — no changes needed, entity constructors call it passing their own consts.

---

## Part C — World Object Scaling (2×)

### C1. [`FallingStalactite.cs`](Bloop/Objects/FallingStalactite.cs:32)
- `Width = 10` → **20**, `Height = 20` → **40**
- `DetectRadiusX = 2f * 32f` → consider **3f * 32f** (larger detection for larger player)

### C2. [`DisappearingPlatform.cs`](Bloop/Objects/DisappearingPlatform.cs:28)
- `PlatformWidth = 64` → **128**, `PlatformHeight = 8` → **16**
- Sensor: `PlatformWidth + 8, PlatformHeight + 16` → scales automatically since it references the constants

### C3. [`CrystalBridge.cs`](Bloop/Objects/CrystalBridge.cs:34)
- `SegmentSize = TileMap.TileSize` (32) — stays (it's tile-aligned)
- `SegmentHeight = TileMap.TileSize * 0.5f` → **`TileMap.TileSize * 1.0f`** (16→32, doubling the platform thickness)

### C4. [`CrystalCluster.cs`](Bloop/Objects/CrystalCluster.cs:26)
- `SensorSize = 22` → **44**

### C5. [`VentFlower.cs`](Bloop/Objects/VentFlower.cs:32)
- `VisualWidth = 32` → **64**, `VisualHeight = 48` → **96**
- `SensorSize = 64` → **128**

### C6. [`ResonanceShard.cs`](Bloop/Objects/ResonanceShard.cs:22)
- `SensorSize = 20` → **40**
- Visual radii in `Draw()`: `halo1R = 20f + pulse * 4f` → **40f + pulse * 8f**
- Similarly scale `halo2R`, `gemR`, `orbit`, etc.

### C7. [`CaveLichen.cs`](Bloop/Objects/CaveLichen.cs:23)
- `ObjectWidth = 16` → **32**, `ObjectHeight = 16` → **32**

### C8. [`BlindFish.cs`](Bloop/Objects/BlindFish.cs:29)
- `ObjectWidth = 20` → **40**, `ObjectHeight = 10` → **20**

### C9. [`StunDamageObject.cs`](Bloop/Objects/StunDamageObject.cs:23)
- `ObjectSize = 24` → **48**
- `GetBounds()` uses `ObjectSize + 16` → will scale automatically

### C10. [`FlareObject.cs`](Bloop/Objects/FlareObject.cs)
- Physics body: `6f` × `3f` (meters) → **`12f`** × **`6f`** (line 39-40)
- `GetBounds()`: 24×24 rect → **48×48** (line 60)

### C11. [`FallingRubble.cs`](Bloop/Objects/FallingRubble.cs:29)
- `Size = 14` → **28**

### C12. [`IonStone.cs`](Bloop/Objects/IonStone.cs:45)
- `GetBounds()`: 24×24 → **48×48** (returns hardcoded rect)

### C13. [`PhosphorMoss.cs`](Bloop/Objects/PhosphorMoss.cs:41)
- `GetBounds()`: 28×20 → **56×40**

### C14. Objects that stay tile-aligned (no dimension change needed)
- [`RootClump.cs`](Bloop/Objects/RootClump.cs) — width is `TileSize = 32` (tile-aligned, stays)
- [`ClimbableSurface.cs`](Bloop/Objects/ClimbableSurface.cs) — width is `TileSize = 32` (tile-aligned, stays)
- [`GlowVine.cs`](Bloop/Objects/GlowVine.cs) — width is `TileSize = 32` (tile-aligned, stays)

### C15. [`ObjectPlacer.cs`](Bloop/Generators/ObjectPlacer.cs) — Minimum Spacing Constants
Search for any `MinSpacing` or minimum-distance constants used during object placement that might need scaling. Review these:
- `PlaceGlowVines`: spacing between vines
- `PlaceRootClumps`: minimum gap between clumps
- `PlaceCrystalBridges`: minimum gap between bridges
- `PlaceControllableEntities`: minimum spawn distance from walls/other entities

---

## Part D — World Generation Tile Gap Changes

### D1. [`LevelGenerator.EnforceMinPassageWidth()`](Bloop/Generators/LevelGenerator.cs:934) — Pass 1: Vertical Clearance
**Current**: Finds 1-tile-tall horizontal passages (solid above + solid below), clears the tile above → creates 2-tile clearance.
**After**: Player is 1.875 tiles tall (60px). 2-tile clearance (64px) gives only 4px margin.
→ **Upgrade to clear tile above AND tile above-that**, creating **3-tile-tall passages** (96px clearance for 60px player).

```diff
- // 1-tile-tall gap — clear the tile above to give 2-tile headroom
- if (solidAbove && solidBelow && ty > 2)
-     map.SetTile(tx, ty - 1, TileType.Empty);
+ // 1-tile-tall gap — clear 2 tiles above to give 3-tile headroom
+ if (solidAbove && solidBelow && ty > 3)
+ {
+     map.SetTile(tx, ty - 1, TileType.Empty);
+     map.SetTile(tx, ty - 2, TileType.Empty);
+ }
```

### D2. Pass 2: Horizontal Clearance
**Current**: Finds 1-tile-wide vertical passages (solid left + solid right), clears the tile to the right → creates 2-tile width.
**After**: Player is 1.125 tiles wide (36px) — 2 tiles (64px) is fine for player. But ChainCentipede will be 60px wide, needing ~2 tiles minimum with barely any margin.
→ **Upgrade to clear 2 tiles to the right**, creating **3-tile-wide vertical passages** (96px).

```diff
- // 1-tile-wide gap — clear the tile to the right
- if (solidLeft && solidRight && tx < w - 2)
-     map.SetTile(tx + 1, ty, TileType.Empty);
+ // 1-tile-wide gap — clear 2 tiles to the right
+ if (solidLeft && solidRight && tx < w - 3)
+ {
+     map.SetTile(tx + 1, ty, TileType.Empty);
+     map.SetTile(tx + 2, ty, TileType.Empty);
+ }
```

### D3. Pass 3: Player-Footprint Check
**Current**: Every empty tile must have empty tile above (2-tile vertical clearance).
**After**: Player is 1.875 tiles tall. → **Require 2 empty tiles above (3-tile vertical clearance)**.

```diff
- // Need the tile above to be empty for standing headroom
- if (TileProperties.IsSolid(map.GetTile(tx, ty - 1)))
+ // Need 2 tiles above empty for standing headroom
+ if (TileProperties.IsSolid(map.GetTile(tx, ty - 1)) ||
+     TileProperties.IsSolid(map.GetTile(tx, ty - 2)))
  {
-     // Clear the tile above
-     if (ty - 1 > 1)
-         map.SetTile(tx, ty - 1, TileType.Empty);
+     // Clear tiles above
+     if (ty - 1 > 1) map.SetTile(tx, ty - 1, TileType.Empty);
+     if (ty - 2 > 1) map.SetTile(tx, ty - 2, TileType.Empty);
  }
```

### D4. Update comment on line 916
```diff
- /// Ensure all passages are wide enough for the player body (~0.75×1.25 tiles).
+ /// Ensure all passages are wide enough for the player body (~1.125×1.875 tiles).
```

### D5. [`CarveBranchingShafts`](Bloop/Generators/LevelGenerator.cs:433) — Fork Branch Width
**Current**: Fork branches are **2 tiles wide** (line 484: `for (int dx = 0; dx < 2; dx++)`).
**After**: With some entities reaching 60px wide, 2 tiles (64px) is too tight.
→ **Widen fork branches to 3 tiles**:
```diff
- for (int dx = 0; dx < 2; dx++)
+ for (int dx = 0; dx < 3; dx++)
```

### D6. Alcove Height in Shafts
**Current**: Alcove height is **2 tiles** (line 508: `int alcoveHeight = 2`).
**After**: Player is 1.875 tiles tall.
→ **Upgrade alcove height to 3 tiles**.
```diff
- int alcoveHeight = 2;
+ int alcoveHeight = 3;
```

### D7. [`CarveWormTunnels`](Bloop/Generators/LevelGenerator.cs:549) — Minimum Worm Width
**Current**: `WormMinWidth = 2` tiles, `WormMaxWidth = 4` tiles.
**After**: Minimum 2 tiles (64px) is tight for 60px-wide entities.
→ **Bump `WormMinWidth = 3`**, keep `WormMaxWidth = 5` (or leave at 4).
```diff
- private const int WormMinWidth = 2;
+ private const int WormMinWidth = 3;
```

### D8. [`ConnectToMainRegion`](Bloop/Generators/LevelGenerator.cs:719) — Tunnel Width
**Current**: Carves 3-tile-wide L-shaped tunnels (line 744 comment).
**After**: 3 tiles (96px) is adequate for most entities. ChainCentipede at 60px still fits with 18px each side.
→ **No change needed** for 3-tile main tunnels. However, verify the tunnel end conditions don't create pinch points.

### D9. [`PathValidator.IsPassableForPlayer`](Bloop/Generators/PathValidator.cs:349)
**Current**: Checks tile (tx, ty) AND tile (tx, ty-1) must be passable (2-tile height footprint).
**After**: Player is ~1.875 tiles tall. Should check **3-tile height footprint** for safety.
```diff
- // Check current tile AND tile above (player is ~1.25 tiles tall)
+ // Check current tile AND 2 tiles above (player is ~1.875 tiles tall)
- return IsPassable(map, tx, ty) && IsPassable(map, tx, ty - 1);
+ return IsPassable(map, tx, ty) && IsPassable(map, tx, ty - 1) && IsPassable(map, tx, ty - 2);
```

### D10. [`Player.CheckSolidOverlap`](Bloop/Gameplay/Player.cs:645)
**Current**: Searches for 2-tile-tall clear columns.
**After**: Player is 1.875 tiles tall. Upgrade to **3-tile-tall** clear columns.
```diff
- // Need 2 clear tiles vertically (player is ~1.25 tiles tall)
- bool footClear = !TileProperties.IsSolid(_tileMap.GetTile(tx, ty));
- bool headClear = !TileProperties.IsSolid(_tileMap.GetTile(tx, ty - 1));
- if (!footClear || !headClear) continue;
+ // Need 3 clear tiles vertically (player is ~1.875 tiles tall)
+ bool footClear = !TileProperties.IsSolid(_tileMap.GetTile(tx, ty));
+ bool headClear1 = !TileProperties.IsSolid(_tileMap.GetTile(tx, ty - 1));
+ bool headClear2 = !TileProperties.IsSolid(_tileMap.GetTile(tx, ty - 2));
+ if (!footClear || !headClear1 || !headClear2) continue;
```

Also update the bounds check on line 643:
```diff
- if (ty < 1 || ty >= _tileMap.Height - 2) continue;
+ if (ty < 2 || ty >= _tileMap.Height - 2) continue;
```

### D11. [`IsOpenArea`](Bloop/Generators/LevelGenerator.cs:1460)
**Current**: Requires 3-tile vertical clearance (ty, ty-1, ty-2 all empty).
**After**: Player is 1.875 tiles tall. 3-tile clearance = 96px > 60px. Still adequate.
→ **No change needed**. The 3-tile check already provides enough margin.

### D12. [`InjectPillarBands`](Bloop/Generators/LevelGenerator.cs:832) — Gap Through Pillar
**Current**: Carves a 2-tile gap through pillar band when connectivity blocked.
**After**: 
→ **Consider widening to 3-tile gap** for the larger player/enemies.
```diff
- // Carve a 2-tile gap through the pillar band at this column
- map.SetTile(tx, ty, TileType.Empty);
- if (tx + 1 < w - 2)
-     map.SetTile(tx + 1, ty, TileType.Empty);
+ // Carve a 3-tile gap through the pillar band at this column
+ map.SetTile(tx, ty, TileType.Empty);
+ if (tx + 1 < w - 2) map.SetTile(tx + 1, ty, TileType.Empty);
+ if (tx + 2 < w - 2) map.SetTile(tx + 2, ty, TileType.Empty);
```

### D13. [`EnsureEscapeInShaft`](Bloop/Generators/LevelGenerator.cs:1377) — Last Resort Ledge
**Current**: Carves a 2-tile-wide ledge.
→ **Upgrade to 3-tile-wide ledge**:
```diff
- map.SetTile(tx - 1, ledgeY, TileType.Empty);
- map.SetTile(tx - 2, ledgeY, TileType.Empty);
+ map.SetTile(tx - 1, ledgeY, TileType.Empty);
+ map.SetTile(tx - 2, ledgeY, TileType.Empty);
+ map.SetTile(tx - 3, ledgeY, TileType.Empty);
```
(and similarly for the right-side fallback)

---

## Part E — ObjectPlacer Passability & Spacing

### E1. [`ObjectPlacer.ValidatePassability`](Bloop/Generators/ObjectPlacer.cs:307)
Review to ensure objects placed in 2-tile-wide passages don't block the now-larger player.

### E2. [`MeasurePassageWidth`](Bloop/Generators/ObjectPlacer.cs:426)
This helper measures the width of empty space around a tile. Review if the minimum threshold needs adjustment.

### E3. Entites placed in ObjectPlacer (lines 1419-1673)
`PlaceControllableEntities` spawns entities with minimum spacing. Review all `MinSpacing`-type constants.

---

## Summary of All Files to Modify

| # | File | Change |
|---|------|--------|
| 1 | `Bloop/Gameplay/Player.cs` | WidthPx 24→36, StandingHeightPx 40→60, CrouchHeightPx 20→30; CheckSolidOverlap 2-tile→3-tile |
| 2 | `Bloop/Physics/BodyFactory.cs` | CreatePlayerBody call to 36×60; foot sensor size |
| 3 | `Bloop/Gameplay/PlayerController.cs` | Review/adjust MoveForce, JumpImpulse, MantleHeadTolerance |
| 4-10 | `Bloop/Entities/*.cs` (7 files) | Double each entity's WidthPx/HeightPx |
| 11-23 | `Bloop/Objects/*.cs` (13 files) | Double each object's dimensions |
| 24 | `Bloop/Generators/LevelGenerator.cs` | EnforceMinPassageWidth passes 1-3; fork width 2→3; alcove height 2→3; WormMinWidth 2→3; pillar gap 2→3; escape ledge 2→3 |
| 25 | `Bloop/Generators/PathValidator.cs` | IsPassableForPlayer 2-tile→3-tile height check |
| 26 | `Bloop/Generators/ObjectPlacer.cs` | Review spacing/passability constants |
