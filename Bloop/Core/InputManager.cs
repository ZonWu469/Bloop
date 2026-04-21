using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Bloop.Core
{
    /// <summary>
    /// Determines how LMB/RMB are routed — normal grapple mode, entity selection, or entity control.
    /// </summary>
    public enum InputMode
    {
        /// <summary>LMB = fire grapple, RMB = release grapple. Default gameplay.</summary>
        Normal,
        /// <summary>LMB = select entity in range, RMB = cancel selection. Grapple disabled.</summary>
        EntitySelecting,
        /// <summary>LMB/RMB routed to controlled entity. Normal grapple disabled.</summary>
        EntityControlling
    }

    /// <summary>
    /// Centralized input manager that tracks current and previous frame keyboard/mouse state.
    /// Provides helper methods for pressed (single-frame), held (continuous), and released detection.
    /// Mouse positions are returned in virtual resolution space (1280×720) via ResolutionManager.
    /// </summary>
    public class InputManager
    {
        // ── Keyboard ──────────────────────────────────────────────────────────
        private KeyboardState _currentKeyboard;
        private KeyboardState _previousKeyboard;

        // ── Mouse ─────────────────────────────────────────────────────────────
        private MouseState _currentMouse;
        private MouseState _previousMouse;

        // ── Input mode ────────────────────────────────────────────────────────
        /// <summary>
        /// Current input routing mode. Set by EntityControlSystem to redirect
        /// LMB/RMB away from the grappling hook during entity selection/control.
        /// </summary>
        public InputMode CurrentMode { get; set; } = InputMode.Normal;

        // ── Resolution manager reference (optional) ───────────────────────────
        private ResolutionManager? _resolution;

        /// <summary>
        /// Attach a ResolutionManager so mouse coordinates are automatically
        /// converted from actual screen space to virtual resolution space.
        /// Call once after ResolutionManager is created.
        /// </summary>
        public void SetResolutionManager(ResolutionManager resolution)
        {
            _resolution = resolution;
        }

        /// <summary>Call once per frame at the start of Update to snapshot input state.</summary>
        public void Update()
        {
            _previousKeyboard = _currentKeyboard;
            _currentKeyboard  = Keyboard.GetState();

            _previousMouse = _currentMouse;
            _currentMouse  = Mouse.GetState();
        }

        // ── Keyboard helpers ──────────────────────────────────────────────────

        /// <summary>True only on the first frame the key is pressed down.</summary>
        public bool IsKeyPressed(Keys key)
            => _currentKeyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);

        /// <summary>True every frame the key is held down.</summary>
        public bool IsKeyHeld(Keys key)
            => _currentKeyboard.IsKeyDown(key);

        /// <summary>True only on the first frame the key is released.</summary>
        public bool IsKeyReleased(Keys key)
            => _currentKeyboard.IsKeyUp(key) && _previousKeyboard.IsKeyDown(key);

        // ── Mouse helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Current mouse position in virtual resolution coordinates (1280×720 space).
        /// If no ResolutionManager is set, returns raw screen coordinates.
        /// Use this for all game-world mouse interactions.
        /// </summary>
        public Vector2 GetMouseWorldPosition()
        {
            var raw = new Vector2(_currentMouse.X, _currentMouse.Y);
            return _resolution != null ? _resolution.ToVirtualCoords(raw) : raw;
        }

        /// <summary>
        /// Current mouse position in virtual resolution coordinates.
        /// Alias for GetMouseWorldPosition() — kept for backward compatibility with menu screens.
        /// </summary>
        public Vector2 GetMousePosition()
            => GetMouseWorldPosition();

        /// <summary>
        /// Current mouse position in raw screen coordinates (actual window pixels).
        /// Use only when you need the true screen position (e.g., UI hit testing before scaling).
        /// </summary>
        public Vector2 GetMouseScreenPosition()
            => new Vector2(_currentMouse.X, _currentMouse.Y);

        /// <summary>True only on the first frame the left mouse button is clicked.</summary>
        public bool IsLeftClickPressed()
            => _currentMouse.LeftButton  == ButtonState.Pressed &&
               _previousMouse.LeftButton == ButtonState.Released;

        /// <summary>True every frame the left mouse button is held.</summary>
        public bool IsLeftClickHeld()
            => _currentMouse.LeftButton == ButtonState.Pressed;

        /// <summary>True only on the first frame the left mouse button is released.</summary>
        public bool IsLeftClickReleased()
            => _currentMouse.LeftButton  == ButtonState.Released &&
               _previousMouse.LeftButton == ButtonState.Pressed;

        /// <summary>True only on the first frame the right mouse button is clicked.</summary>
        public bool IsRightClickPressed()
            => _currentMouse.RightButton  == ButtonState.Pressed &&
               _previousMouse.RightButton == ButtonState.Released;

        /// <summary>True every frame the right mouse button is held.</summary>
        public bool IsRightClickHeld()
            => _currentMouse.RightButton == ButtonState.Pressed;

        /// <summary>Scroll wheel delta this frame (positive = scroll up).</summary>
        public int GetScrollDelta()
            => _currentMouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;

        // ── Convenience movement helpers ──────────────────────────────────────

        /// <summary>Returns -1 (left), +1 (right), or 0 based on A/D or arrow keys.</summary>
        public float GetHorizontalAxis()
        {
            float axis = 0f;
            if (IsKeyHeld(Keys.A) || IsKeyHeld(Keys.Left))  axis -= 1f;
            if (IsKeyHeld(Keys.D) || IsKeyHeld(Keys.Right)) axis += 1f;
            return axis;
        }

        /// <summary>True when the jump key (Space or Up arrow) is pressed this frame.</summary>
        public bool IsJumpPressed()
            => IsKeyPressed(Keys.Space) || IsKeyPressed(Keys.Up);

        /// <summary>True when the rappel combo (Down + Space) is held.</summary>
        public bool IsRappelHeld()
            => IsKeyHeld(Keys.Down) && IsKeyHeld(Keys.Space);

        /// <summary>True when the climb key (C) is held.</summary>
        public bool IsClimbHeld()
            => IsKeyHeld(Keys.C);

        /// <summary>True when the crouch key (Left Ctrl) is held.</summary>
        public bool IsCrouchHeld()
            => IsKeyHeld(Keys.LeftControl);

        /// <summary>True when the interact/eat key (E) is pressed.</summary>
        public bool IsInteractPressed()
            => IsKeyPressed(Keys.E);

        /// <summary>True when the pause key (Escape) is pressed.</summary>
        public bool IsPausePressed()
            => IsKeyPressed(Keys.Escape);

        /// <summary>True when the fullscreen toggle key (F11) is pressed.</summary>
        public bool IsFullscreenTogglePressed()
            => IsKeyPressed(Keys.F11);

        // ── Entity control helpers ─────────────────────────────────────────────

        /// <summary>
        /// True on the first frame Q is pressed. Activates entity selection mode
        /// when the EntityControlSystem cooldown is ready.
        /// </summary>
        public bool IsControlEntityPressed()
            => IsKeyPressed(Keys.Q);

        /// <summary>
        /// True on the first frame T is pressed. Throws the attached Luminous Isopod
        /// in the direction of the mouse cursor.
        /// </summary>
        public bool IsThrowPressed()
            => IsKeyPressed(Keys.T);

        /// <summary>
        /// True every frame T is held. Used to show the isopod throw trajectory arc.
        /// </summary>
        public bool IsThrowHeld()
            => IsKeyHeld(Keys.T);

        /// <summary>
        /// True on the first frame T is released. Launches the isopod when T is released.
        /// </summary>
        public bool IsThrowReleased()
            => IsKeyReleased(Keys.T);

        /// <summary>True on the first frame F is pressed. Enters throw-flare stance.</summary>
        public bool IsThrowFlarePressed()
            => IsKeyPressed(Keys.F);

        /// <summary>Returns -1 (up), +1 (down), or 0 based on W/S or arrow keys.</summary>
        public float GetVerticalAxis()
        {
            float axis = 0f;
            if (IsKeyHeld(Keys.W) || IsKeyHeld(Keys.Up))   axis -= 1f;
            if (IsKeyHeld(Keys.S) || IsKeyHeld(Keys.Down)) axis += 1f;
            return axis;
        }
    }
}
