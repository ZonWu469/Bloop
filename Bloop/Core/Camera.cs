using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Bloop.Core
{
    /// <summary>
    /// Smooth-follow 2D camera that tracks a target position.
    /// Supports vertical scrolling for descent levels and viewport clamping.
    /// Provides world-to-screen and screen-to-world coordinate transforms.
    /// Also supports screen shake via a decaying sinusoidal displacement offset.
    /// </summary>
    public class Camera
    {
        // ── Fields ─────────────────────────────────────────────────────────────
        private readonly Viewport _viewport;

        /// <summary>Current camera center position in world space.</summary>
        public Vector2 Position { get; private set; }

        /// <summary>Camera zoom factor (1.0 = no zoom).</summary>
        public float Zoom { get; set; } = 1f;

        /// <summary>Smoothing factor for follow (0 = instant, 1 = never moves).</summary>
        public float Smoothing { get; set; } = 0.12f;

        // World bounds for clamping (set by the level)
        private float _minX, _maxX, _minY, _maxY;
        private bool  _hasBounds;

        // ── Screen shake state ─────────────────────────────────────────────────
        private float _shakeAmplitude  = 0f;  // current peak displacement in pixels
        private float _shakeDuration   = 0f;  // total duration of current shake
        private float _shakeRemaining  = 0f;  // time left in current shake
        private float _shakeFrequency  = 30f; // oscillations per second
        private Vector2 _shakeDir      = Vector2.UnitX; // primary shake axis
        private readonly Random _shakeRng = new Random();

        // ── Constructor ────────────────────────────────────────────────────────
        public Camera(Viewport viewport)
        {
            _viewport = viewport;
            Position  = new Vector2(viewport.Width / 2f, viewport.Height / 2f);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Set world-space bounds so the camera never shows outside the level.
        /// </summary>
        public void SetBounds(float minX, float maxX, float minY, float maxY)
        {
            _minX      = minX;
            _maxX      = maxX;
            _minY      = minY;
            _maxY      = maxY;
            _hasBounds = true;
        }

        /// <summary>Instantly snap the camera to a world position.</summary>
        public void SnapTo(Vector2 worldPosition)
        {
            Position = Clamp(worldPosition);
        }

        /// <summary>
        /// Smoothly move the camera toward the target world position.
        /// Call once per frame from Update.
        /// </summary>
        public void Follow(Vector2 targetWorldPosition, GameTime gameTime)
        {
            // Lerp toward target
            Vector2 desired = Clamp(targetWorldPosition);
            Position = Vector2.Lerp(Position, desired, 1f - Smoothing);

            // Tick screen shake
            if (_shakeRemaining > 0f)
                _shakeRemaining -= (float)gameTime.ElapsedGameTime.TotalSeconds;
        }

        /// <summary>
        /// Trigger a screen shake effect.
        /// </summary>
        /// <param name="amplitude">Peak displacement in pixels.</param>
        /// <param name="duration">Duration of the shake in seconds.</param>
        /// <param name="direction">
        /// Primary shake axis. Pass Vector2.Zero for a random direction.
        /// </param>
        public void Shake(float amplitude, float duration, Vector2 direction = default)
        {
            // Only override a weaker ongoing shake
            if (amplitude <= _shakeAmplitude && _shakeRemaining > 0f) return;

            _shakeAmplitude = amplitude;
            _shakeDuration  = duration;
            _shakeRemaining = duration;

            if (direction == Vector2.Zero)
            {
                // Random direction
                float angle = (float)(_shakeRng.NextDouble() * Math.PI * 2.0);
                _shakeDir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            }
            else
            {
                _shakeDir = Vector2.Normalize(direction);
            }
        }

        /// <summary>
        /// Returns the SpriteBatch transform matrix for this camera,
        /// including any active screen shake offset.
        /// Pass to SpriteBatch.Begin(transformMatrix: camera.GetTransform()).
        /// </summary>
        public Matrix GetTransform()
        {
            // Compute shake offset: decaying sinusoidal displacement
            Vector2 shakeOffset = Vector2.Zero;
            if (_shakeRemaining > 0f && _shakeDuration > 0f)
            {
                float progress = _shakeRemaining / _shakeDuration;          // 1→0
                float decay    = progress * progress;                        // quadratic decay
                float phase    = _shakeRemaining * _shakeFrequency;
                float disp     = _shakeAmplitude * decay * (float)Math.Sin(phase);
                shakeOffset    = _shakeDir * disp;
            }

            // Translate so the camera center maps to the viewport center
            return Matrix.CreateTranslation(
                       -Position.X + shakeOffset.X,
                       -Position.Y + shakeOffset.Y, 0f) *
                   Matrix.CreateScale(Zoom, Zoom, 1f) *
                   Matrix.CreateTranslation(
                       _viewport.Width  / 2f,
                       _viewport.Height / 2f,
                       0f);
        }

        /// <summary>Convert a screen-space point to world-space coordinates.</summary>
        public Vector2 ScreenToWorld(Vector2 screenPoint)
        {
            Matrix inverse = Matrix.Invert(GetTransform());
            return Vector2.Transform(screenPoint, inverse);
        }

        /// <summary>Convert a world-space point to screen-space coordinates.</summary>
        public Vector2 WorldToScreen(Vector2 worldPoint)
        {
            return Vector2.Transform(worldPoint, GetTransform());
        }

        /// <summary>
        /// Returns the visible rectangle in world space (useful for culling).
        /// </summary>
        public Rectangle GetVisibleBounds()
        {
            Vector2 topLeft     = ScreenToWorld(Vector2.Zero);
            Vector2 bottomRight = ScreenToWorld(new Vector2(_viewport.Width, _viewport.Height));
            return new Rectangle(
                (int)topLeft.X,
                (int)topLeft.Y,
                (int)(bottomRight.X - topLeft.X),
                (int)(bottomRight.Y - topLeft.Y));
        }

        // ── Private helpers ────────────────────────────────────────────────────
        private Vector2 Clamp(Vector2 pos)
        {
            if (!_hasBounds) return pos;

            float halfW = (_viewport.Width  / 2f) / Zoom;
            float halfH = (_viewport.Height / 2f) / Zoom;

            float x = MathHelper.Clamp(pos.X, _minX + halfW, _maxX - halfW);
            float y = MathHelper.Clamp(pos.Y, _minY + halfH, _maxY - halfH);
            return new Vector2(x, y);
        }
    }
}
