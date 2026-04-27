using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Bloop.Gameplay;

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
    /// Centralized input manager that tracks current and previous frame keyboard/mouse/gamepad state.
    /// Provides helper methods for pressed (single-frame), held (continuous), and released detection.
    /// Mouse positions are returned in virtual resolution space (1280×720) via ResolutionManager.
    ///
    /// Gamepad support is transparent: when a gamepad is connected, all existing
    /// helpers (<see cref="IsJumpPressed"/>, <see cref="GetHorizontalAxis"/>, etc.)
    /// also poll the gamepad. Right-stick deflection drives a virtual cursor used
    /// for grapple/flare aim — <see cref="GetMouseWorldPosition"/> returns the
    /// virtual cursor when the gamepad is the active input source.
    /// </summary>
    public class InputManager
    {
        // ── Keyboard ──────────────────────────────────────────────────────────
        private KeyboardState _currentKeyboard;
        private KeyboardState _previousKeyboard;

        // ── Mouse ─────────────────────────────────────────────────────────────
        private MouseState _currentMouse;
        private MouseState _previousMouse;

        // ── Gamepad ───────────────────────────────────────────────────────────
        private GamePadState _currentPad;
        private GamePadState _previousPad;
        private bool _padConnected;

        /// <summary>True if a gamepad is currently connected on player-one slot.</summary>
        public bool IsGamepadConnected => _padConnected;

        /// <summary>
        /// True when the most recent input came from the gamepad. Used by UI to
        /// switch button-prompt glyphs between keyboard and gamepad icons.
        /// </summary>
        public bool GamepadIsActive { get; private set; }

        // ── Virtual aim cursor (driven by right stick when gamepad is active) ──
        private Vector2 _virtualCursor = new Vector2(640f, 360f); // virtual-resolution coords
        /// <summary>Cursor speed (virtual px/s) at full right-stick deflection.</summary>
        private const float VirtualCursorSpeed = 900f;

        // ── Input mode ────────────────────────────────────────────────────────
        public InputMode CurrentMode { get; set; } = InputMode.Normal;

        // ── Resolution manager reference (optional) ───────────────────────────
        private ResolutionManager? _resolution;

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

            _previousPad = _currentPad;
            _currentPad  = GamePad.GetState(PlayerIndex.One);
            _padConnected = _currentPad.IsConnected;

            // Track which input source is "active" for UI prompts.
            // Switch to gamepad on any meaningful pad activity; switch to KB/M on any KB or mouse activity.
            if (_padConnected && PadHasActivity())
                GamepadIsActive = true;
            else if (KeyboardOrMouseHasActivity())
                GamepadIsActive = false;

            // Update virtual cursor from right stick when gamepad is active.
            if (GamepadIsActive)
            {
                Vector2 rs = ApplyDeadzone(new Vector2(
                    _currentPad.ThumbSticks.Right.X,
                    -_currentPad.ThumbSticks.Right.Y));  // invert Y: stick up = screen up

                if (rs.LengthSquared() > 0f)
                {
                    _virtualCursor += rs * VirtualCursorSpeed * (1f / 60f);
                    _virtualCursor.X = MathHelper.Clamp(_virtualCursor.X, 0f, 1280f);
                    _virtualCursor.Y = MathHelper.Clamp(_virtualCursor.Y, 0f, 720f);
                }
            }
        }

        private bool PadHasActivity()
        {
            if (_currentPad.ThumbSticks.Left.LengthSquared() > MovementTuning.GamepadStickDeadzone * MovementTuning.GamepadStickDeadzone) return true;
            if (_currentPad.ThumbSticks.Right.LengthSquared() > MovementTuning.GamepadStickDeadzone * MovementTuning.GamepadStickDeadzone) return true;
            if (_currentPad.Triggers.Left > 0.2f || _currentPad.Triggers.Right > 0.2f) return true;
            // Any button held?
            return _currentPad.Buttons.A == ButtonState.Pressed
                || _currentPad.Buttons.B == ButtonState.Pressed
                || _currentPad.Buttons.X == ButtonState.Pressed
                || _currentPad.Buttons.Y == ButtonState.Pressed
                || _currentPad.Buttons.LeftShoulder  == ButtonState.Pressed
                || _currentPad.Buttons.RightShoulder == ButtonState.Pressed
                || _currentPad.Buttons.Start == ButtonState.Pressed
                || _currentPad.Buttons.Back  == ButtonState.Pressed;
        }

        private bool KeyboardOrMouseHasActivity()
        {
            if (_currentKeyboard.GetPressedKeys().Length > 0) return true;
            if (_currentMouse.LeftButton  == ButtonState.Pressed) return true;
            if (_currentMouse.RightButton == ButtonState.Pressed) return true;
            // Mouse motion
            return _currentMouse.X != _previousMouse.X || _currentMouse.Y != _previousMouse.Y;
        }

        // ── Keyboard helpers ──────────────────────────────────────────────────

        public bool IsKeyPressed(Keys key)
            => _currentKeyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);

        public bool IsKeyHeld(Keys key)
            => _currentKeyboard.IsKeyDown(key);

        public bool IsKeyReleased(Keys key)
            => _currentKeyboard.IsKeyUp(key) && _previousKeyboard.IsKeyDown(key);

        // ── Gamepad button helpers (private; used to compose the public helpers) ──

        private bool PadPressed(Buttons b)
            => _padConnected && _currentPad.IsButtonDown(b) && _previousPad.IsButtonUp(b);

        private bool PadHeld(Buttons b)
            => _padConnected && _currentPad.IsButtonDown(b);

        private bool PadReleased(Buttons b)
            => _padConnected && _currentPad.IsButtonUp(b) && _previousPad.IsButtonDown(b);

        // ── Mouse helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Mouse cursor position in virtual resolution coordinates (1280×720 space).
        /// When a gamepad is the active input source, returns the right-stick virtual cursor.
        /// </summary>
        public Vector2 GetMouseWorldPosition()
        {
            if (GamepadIsActive)
                return _virtualCursor;
            var raw = new Vector2(_currentMouse.X, _currentMouse.Y);
            return _resolution != null ? _resolution.ToVirtualCoords(raw) : raw;
        }

        public Vector2 GetMousePosition()
            => GetMouseWorldPosition();

        public Vector2 GetMouseScreenPosition()
            => new Vector2(_currentMouse.X, _currentMouse.Y);

        /// <summary>
        /// Snap the virtual gamepad cursor to a target virtual-space position.
        /// Call when entering states where we want the cursor near the player
        /// (e.g., entering grapple aim with gamepad).
        /// </summary>
        public void SetVirtualCursor(Vector2 virtualPos)
        {
            _virtualCursor = new Vector2(
                MathHelper.Clamp(virtualPos.X, 0f, 1280f),
                MathHelper.Clamp(virtualPos.Y, 0f, 720f));
        }

        public bool IsLeftClickPressed()
            => (_currentMouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
            || PadTriggerPressed(true);

        public bool IsLeftClickHeld()
            => _currentMouse.LeftButton == ButtonState.Pressed
            || PadTriggerHeld(true);

        public bool IsLeftClickReleased()
            => (_currentMouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed)
            || PadTriggerReleased(true);

        public bool IsRightClickPressed()
            => (_currentMouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released)
            || PadPressed(Buttons.RightShoulder);

        public bool IsRightClickHeld()
            => _currentMouse.RightButton == ButtonState.Pressed
            || PadHeld(Buttons.RightShoulder);

        public int GetScrollDelta()
            => _currentMouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;

        // ── Trigger helpers (treat trigger > 0.5 as "pressed") ────────────────
        private bool PadTriggerPressed(bool right)
        {
            if (!_padConnected) return false;
            float cur = right ? _currentPad.Triggers.Right : _currentPad.Triggers.Left;
            float prev = right ? _previousPad.Triggers.Right : _previousPad.Triggers.Left;
            return cur > 0.5f && prev <= 0.5f;
        }
        private bool PadTriggerHeld(bool right)
        {
            if (!_padConnected) return false;
            float cur = right ? _currentPad.Triggers.Right : _currentPad.Triggers.Left;
            return cur > 0.5f;
        }
        private bool PadTriggerReleased(bool right)
        {
            if (!_padConnected) return false;
            float cur = right ? _currentPad.Triggers.Right : _currentPad.Triggers.Left;
            float prev = right ? _previousPad.Triggers.Right : _previousPad.Triggers.Left;
            return cur <= 0.5f && prev > 0.5f;
        }

        // ── Convenience movement helpers ──────────────────────────────────────

        /// <summary>
        /// Returns horizontal axis from -1 to +1. Combines A/D, arrow keys, and
        /// gamepad left stick. Stick deflection below <see cref="MovementTuning.GamepadWalkThreshold"/>
        /// scales linearly so a half-pushed stick produces walk speed.
        /// </summary>
        public float GetHorizontalAxis()
        {
            float axis = 0f;
            if (IsKeyHeld(Keys.A) || IsKeyHeld(Keys.Left))  axis -= 1f;
            if (IsKeyHeld(Keys.D) || IsKeyHeld(Keys.Right)) axis += 1f;

            if (axis == 0f && _padConnected)
            {
                float sx = _currentPad.ThumbSticks.Left.X;
                float dz = MovementTuning.GamepadStickDeadzone;
                if (MathF.Abs(sx) > dz)
                {
                    // Re-map [dz, 1] → [0, 1] preserving sign
                    float sign = MathF.Sign(sx);
                    float mag  = (MathF.Abs(sx) - dz) / (1f - dz);
                    // Below walk-threshold → analog walk; above → full speed
                    float walkT = MovementTuning.GamepadWalkThreshold;
                    if (MathF.Abs(sx) < walkT)
                        axis = sign * mag * 0.5f; // walk speed
                    else
                        axis = sign;              // full speed
                }
            }
            return axis;
        }

        /// <summary>True when jump (Space, Up arrow, or gamepad A) is pressed this frame.</summary>
        public bool IsJumpPressed()
            => IsKeyPressed(Keys.Space) || IsKeyPressed(Keys.Up) || PadPressed(Buttons.A);

        /// <summary>
        /// True when the rappel input is held. Keyboard: Down + Space combo.
        /// Gamepad: left trigger.
        /// </summary>
        public bool IsRappelHeld()
            => (IsKeyHeld(Keys.Down) && IsKeyHeld(Keys.Space))
            || PadTriggerHeld(false);

        /// <summary>True when the climb key (C) or gamepad LB is held.</summary>
        public bool IsClimbHeld()
            => IsKeyHeld(Keys.C) || PadHeld(Buttons.LeftShoulder);

        /// <summary>True when the crouch key (LCtrl) or gamepad B is held.</summary>
        public bool IsCrouchHeld()
            => IsKeyHeld(Keys.LeftControl) || PadHeld(Buttons.B);

        /// <summary>True when the interact/eat key (E) or gamepad Y is pressed.</summary>
        public bool IsInteractPressed()
            => IsKeyPressed(Keys.E) || PadPressed(Buttons.Y);

        /// <summary>True when Escape or gamepad Start is pressed.</summary>
        public bool IsPausePressed()
            => IsKeyPressed(Keys.Escape) || PadPressed(Buttons.Start);

        public bool IsFullscreenTogglePressed()
            => IsKeyPressed(Keys.F11);

        // ── Entity control helpers ─────────────────────────────────────────────

        /// <summary>True on first frame Q or gamepad Back (Select) is pressed.</summary>
        public bool IsControlEntityPressed()
            => IsKeyPressed(Keys.Q) || PadPressed(Buttons.Back);

        /// <summary>True on first frame T or gamepad X is pressed.</summary>
        public bool IsThrowPressed()
            => IsKeyPressed(Keys.T) || PadPressed(Buttons.X);

        public bool IsThrowHeld()
            => IsKeyHeld(Keys.T) || PadHeld(Buttons.X);

        public bool IsThrowReleased()
            => IsKeyReleased(Keys.T) || PadReleased(Buttons.X);

        /// <summary>True on first frame F or gamepad DPad-Left is pressed (enters flare stance).</summary>
        public bool IsThrowFlarePressed()
            => IsKeyPressed(Keys.F) || PadPressed(Buttons.DPadLeft);

        public float GetVerticalAxis()
        {
            float axis = 0f;
            if (IsKeyHeld(Keys.W) || IsKeyHeld(Keys.Up))   axis -= 1f;
            if (IsKeyHeld(Keys.S) || IsKeyHeld(Keys.Down)) axis += 1f;

            if (axis == 0f && _padConnected)
            {
                float sy = _currentPad.ThumbSticks.Left.Y; // up positive
                float dz = MovementTuning.GamepadStickDeadzone;
                if (MathF.Abs(sy) > dz)
                    axis = -MathF.Sign(sy); // invert so down on stick = +1
            }
            return axis;
        }

        public bool IsAnyKeyPressed()
        {
            var pressed = _currentKeyboard.GetPressedKeys();
            if (pressed.Length == 0) return false;
            foreach (var k in pressed)
                if (_previousKeyboard.IsKeyUp(k)) return true;
            return false;
        }

        // ── Stick utilities ────────────────────────────────────────────────────

        private static Vector2 ApplyDeadzone(Vector2 stick)
        {
            float dz = MovementTuning.GamepadStickDeadzone;
            float mag = stick.Length();
            if (mag < dz) return Vector2.Zero;
            // Re-map [dz, 1] → [0, 1] preserving direction
            float scaled = (mag - dz) / (1f - dz);
            return stick * (scaled / mag);
        }
    }
}
