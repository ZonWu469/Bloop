# Bug Fix Plan: Native Resolution Rendering + 5 Reported Issues

## Problem Summary

After implementing the resolution changes, 5 bugs were reported:
1. Player appears top-right instead of center screen
2. Interface scaling is wrong — should maintain original pixel size
3. Player "floating" when falling — wall-slide too aggressive
4. Mouse selection in main menu broken
5. Seed textbox key input still not working

**Root cause for bugs 1, 2, 4**: The virtual resolution system (1280×720 render target scaled to fill a larger window) creates coordinate mismatches. The user wants native resolution rendering — the game renders at the actual window size, showing more of the game world on larger screens, with UI at natural pixel size.

## Architecture Change: Remove Virtual Resolution, Use Native Rendering

### Current flow (broken):
```
Game renders at 1280×720 → scaled up to fill window → blurry, coordinate issues
```

### New flow:
```
Game renders at actual window size → no scaling → crisp, more visible area
```

---

## File-by-File Changes

### 1. `Bloop/Core/ResolutionManager.cs` — Simplify drastically

**Remove**: Render target, BeginDraw/EndDraw, ToVirtualCoords, scale/offset calculations.

**Keep**: Fullscreen toggle, window resize notification, VirtualWidth/VirtualHeight constants (for backward compat references).

**New behavior**:
- `BeginDraw()` → just clears the backbuffer (no render target)
- `EndDraw()` → no-op (nothing to blit)
- `ToVirtualCoords()` → returns input unchanged (identity transform)
- `OnWindowResize()` → fires an event so LightingSystem can recreate render targets
- Add `public int ActualWidth / ActualHeight` properties reading from PresentationParameters
- Add `public event Action<int,int> WindowResized` event

### 2. `Bloop/Game1.cs` — Simplify draw pipeline

**Changes**:
- Constructor: keep the display detection and backbuffer sizing (window at screen res minus taskbar)
- `Draw()`: remove `Resolution.BeginDraw()` / `Resolution.EndDraw()` wrapping. Just clear and draw.
- `Initialize()`: subscribe to `Window.ClientSizeChanged` → notify ResolutionManager → notify LightingSystem
- Remove `ScreenWidth`/`ScreenHeight` constants (no longer meaningful as fixed values)

### 3. `Bloop/Lighting/LightingSystem.cs` — Dynamic render target sizing

**Changes**:
- Constructor: accept initial width/height from actual backbuffer (not hardcoded 1280×720)
- Add `public void OnResize(int newWidth, int newHeight)` method that recreates `_sceneTarget` and `_lightTarget`
- `BeginScene()`: no longer needs to save/restore a parent render target (there is none). Just set `_sceneTarget`.
- `EndScene()`: restore to backbuffer (null) or to whatever was active.
- `Composite()`: draw to the current render target (backbuffer).

### 4. `Bloop/Core/InputManager.cs` — Remove virtual coord conversion

**Changes**:
- `GetMouseWorldPosition()`: return raw mouse coordinates (no `ToVirtualCoords` call)
- `SetResolutionManager()`: can be kept but the conversion is now identity
- Or simpler: just remove the `_resolution` field and always return raw coords

### 5. `Bloop/Screens/GameplayScreen.cs` — Camera at actual viewport

**Changes**:
- `LoadContent()` line 113: `_camera = new Camera(GraphicsDevice.Viewport)` — this now uses the actual window viewport, which is correct for native rendering. The camera will show more of the world.
- No other changes needed — `vw`/`vh` from `GraphicsDevice.Viewport` will be the actual window size.

### 6. `Bloop/Screens/MainMenuScreen.cs` — No changes needed

Already uses `GraphicsDevice.Viewport.Width/Height` for layout. With native rendering, these return the actual window size, so buttons will be centered correctly. Mouse coordinates are already in the same space.

### 7. `Bloop/Screens/SeedInputScreen.cs` — Fix TextInput lifecycle

**Changes**:
- The `OnTextInput` handler should check if the screen is still active before processing input. When the screen is popped, `UnloadContent()` unsubscribes, but there may be a frame where the event fires after the screen is logically gone.
- Add a guard: `if (_confirmed) return;` at the top of `OnTextInput` (already partially there for TryConfirm).
- Verify that `UnloadContent()` is actually called when the screen is popped. Check `ScreenManager.ApplyPendingChanges()` — it calls `screen.UnloadContent()` for removed screens. This should work.
- The real issue might be that `LoadContent()` is called but `Game1.Instance` is null at that point. Check timing.

### 8. `Bloop/Gameplay/PlayerController.cs` — Wall-slide requires directional input

**Changes** at the wall-slide friction block (lines 294-309):
- Add condition: player must be pressing horizontal input **toward** the wall
- If touching right wall: require `_input.GetHorizontalAxis() > 0`
- If touching left wall: require `_input.GetHorizontalAxis() < 0`
- This prevents "floating" when the player is merely brushing past a wall without pressing into it

```csharp
// Wall-slide: only when pressing INTO the wall
float hAxis = _input.GetHorizontalAxis();
bool pressingIntoWall =
    (_player.IsTouchingWallRight && hAxis > 0f) ||
    (_player.IsTouchingWallLeft  && hAxis < 0f);

if (!_player.IsGrounded
    && _player.IsTouchingWall
    && pressingIntoWall
    && _player.State == PlayerState.Falling
    && _wallJumpCooldownTimer <= 0f)
{
    // ... clamp velocity
}
```

---

## Execution Order

1. **ResolutionManager** — gut the render target system, keep as thin wrapper
2. **Game1** — simplify Draw(), wire up resize events
3. **LightingSystem** — dynamic render target sizing + resize handler
4. **InputManager** — remove virtual coord conversion
5. **PlayerController** — wall-slide directional check
6. **SeedInputScreen** — verify TextInput lifecycle
7. **Build and test**

---

## Risk Assessment

- **UI layout**: All screens already use `GraphicsDevice.Viewport.Width/Height`, so they will automatically adapt to the native resolution. Buttons, text, and panels will be positioned correctly.
- **Camera**: Will show more of the world on larger screens. This is a gameplay benefit — the player can see more of the cave.
- **Lighting**: Render targets at native resolution will use more VRAM but modern GPUs handle this easily.
- **Performance**: Rendering at native resolution (e.g., 1920×1080) instead of 1280×720 means ~2.25× more pixels. Should still be fine for a 2D tile game.
- **Font size**: Fonts will appear at their natural pixel size, which may look smaller on high-res screens. This is acceptable — the user explicitly wants "original scaling."
