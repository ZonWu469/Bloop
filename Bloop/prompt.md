**Prompt for Claude (copy and paste this entire block directly into Claude):**

You are an expert C# and MonoGame developer with deep experience building complete 2D side-scrolling platformers from scratch. Create a **complete, production-ready, single-solution MonoGame DesktopGL project** (targeting .NET 8) for the following game. The entire codebase must be clean, well-commented, modular, and ready to compile and run with zero external dependencies beyond MonoGame 3.8+.

### Game Title & High-Level Concept
**Title:** Descent Into the Deep  
**Genre:** 2D side-scrolling descent platformer with puzzle and survival elements.  
The player descends through 20–40 procedurally generated levels of ever-deeper, darker underground mazes (vertical shafts, separated platforms, tunnels, large/small caves, inclined surfaces). The goal on each run is to reach the final exit at the bottom while managing light, air, resources, and physics-based movement. Death or running out of resources forces a restart from the last checkpoint or full run restart.

### Core Requirements
- Use **seed-based procedural generation**: Player enters a numeric seed on the main menu → every level is 100 % reproducible from that seed.
- Save/Load system using JSON files (player progress, inventory, discovered areas, current depth, seed, etc.).
- Main menu screen with:
  - “Start Game” (prompt for seed input)
  - “Load Game” (list of JSON save files with preview of seed + current depth)
  - Basic options (volume, controls reminder)

### Player Controls & Base Mechanics (MUST be implemented exactly)
- Left/Right movement (A/D or arrows)
- Rappel down using rope (hold Down + Space or dedicated key; rope attaches to ceiling surfaces)
- Grappling hook to climb/swing upward (aim with mouse, fire with left-click; limited range)
- Slide on any surface with <45° inclination (automatic when moving left/right on eligible slopes)
- Climb specific surfaces with “C” key
- Jump only when on ground (standard platformer jump)

### World Objects (base types – expand with the new features below)
All world objects are attached to surfaces and generated procedurally:
- Disappear-after-5-seconds objects (touch → start 5-second timer → vanish)
- Stun/damage objects (touch → stun or damage player)
- Climbable objects (C key to climb)

### Required Unique Features (implement ALL of these exactly as described – these are the ONLY additional mechanics)
1. **Lantern Fuel + Bioluminescent Ecosystem**  
   - Player has a head-lantern with finite fuel that drains faster at greater depths (depth multiplier).  
   - Lantern creates a circular light radius around the player (use SpriteBatch with multiplicative blending or custom shader for darkness). Outside the radius the world is almost pitch black.  
   - Bioluminescent interaction:  
     - Touching a “disappear-after-5s” object in bright lantern light releases glowing spores that create a temporary 5-meter light radius for 15 seconds.  
     - Stun/damage objects pulse and become visible/bright only when lit.  
     - New object type: Glow-vines (only become climbable with C key after being lit by lantern for 2+ seconds).

2. **Disappearing-Object Dominoes**  
   - Some disappear-after-5s objects are linked in chains (same seed hash determines links).  
   - Touching the first starts a timed domino effect: each subsequent linked object disappears 1 second after the previous one.  
   - Used to create temporary paths, staircases, or bridges across vertical shafts.

3. **Climbable “Living” Surfaces**  
   - New object: Root Clumps (C to climb).  
   - They slowly retract into the wall after 8 seconds unless the player keeps moving on them.  
   - Must be combined with sliding or momentum to traverse long vertical sections before they retract.

4. **Air Pockets & “Breath of the Deep”**  
   - Thin air at deeper levels: a breath meter slowly drains.  
   - Glowing “vent flowers” (air pockets) are scattered procedurally. Standing inside one for 5 seconds fully refills breath meter AND lantern fuel.  
   - If breath meter hits zero → damage over time.

5. **Foraging & Poison Risk**  
   - Collectible items: cave lichen and blind fish (spawned procedurally).  
   - Eating restores health/stamina but has a 30 % chance (determined by seed) of applying a random temporary debuff (slow slide speed, inverted controls for 10 seconds, etc.).  
   - Items can be carried in inventory and eaten at any time.

6. **Backpack Weight**  
   - Collected resources and foraged items add weight to the player’s backpack.  
   - Higher weight = slower rope retraction speed and reduced maximum slide distance on inclines.  
   - Display current weight / max weight in UI.

7. **Momentum Sliding Mastery**  
   - Sliding on <45° surfaces builds “kinetic charge” (visual meter).  
   - At maximum charge the player can:  
     - Release to launch upward like a slingshot (reaches high grapple points).  
     - Combine with rappel to perform a “zip-drop” that pierces through multiple disappearing platforms in one motion.

### Technical & Implementation Requirements
- **Procedural Level Generation**: Single `LevelGenerator` class using the seed + depth number (`Random(seed + depth * largePrime)`). Each level must contain platforms, tunnels, caves, shafts, and inclined surfaces. Guarantee at least two possible paths per level (one easier, one riskier with better loot).  
- **Physics**: Use MonoGame’s built-in collision or integrate Farseer Physics if needed (but prefer simple AABB + raycast for rope/grapple). Rope and grappling hook must feel responsive and realistic.  
- **Lighting System**: Dynamic per-level darkness with player lantern as the only reliable light source. Support spore-based temporary lights.  
- **UI / HUD**: Lantern fuel bar, breath meter, backpack weight, kinetic charge meter, minimap (starts black, reveals as areas are lit).  
- **Checkpoint System**: Auto-save at the start of each depth level.  
- **Camera**: Smooth follow camera that can scroll vertically as the player descends (levels are tall).  
- **Save Format**: Clean JSON with all player stats, inventory, current depth, seed, and discovered map data.  
- **Project Structure**: Use proper folders (Core/, Gameplay/, UI/, Content/, Generators/, etc.). All classes must be well-named and commented. Include a `Game1.cs` that properly loads the menu and game screens.  
- **Art & Audio**: Use simple colored rectangles and basic shapes for placeholders (e.g., player = blue rectangle, platforms = brown, objects have distinct colors). Add placeholder sound effect comments where audio would go.  
- **Performance**: Must run at 60 FPS smoothly even with many objects and particles (spores, dust).

Generate the **full project** in a clear, copy-paste-ready format:  
1. First, output the complete folder structure with all file paths.  
2. Then provide the full code for every .cs file (starting with Program.cs, Game1.cs, then all other classes).  
3. Finally, provide a step-by-step “How to Build & Run” guide.

Make the game feel tense, atmospheric, and replayable through the combination of procedural seeds, light management, resource survival, and physics-based puzzle platforming. Do not add any mechanics not listed above. Begin coding now.