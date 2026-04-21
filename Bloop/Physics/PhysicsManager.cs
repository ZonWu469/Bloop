using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using nkast.Aether.Physics2D.Dynamics;

// Alias to avoid conflict with Bloop.World namespace
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Physics
{
    /// <summary>
    /// Wraps the Aether.Physics2D World and provides helper methods for
    /// unit conversion, world stepping, and body lifecycle management.
    ///
    /// Unit convention:
    ///   Aether uses meters internally. We use PIXELS_PER_METER = 64 to convert.
    ///   Always convert pixel positions to meters before passing to Aether,
    ///   and meters back to pixels when reading positions for rendering.
    /// </summary>
    public class PhysicsManager : IDisposable
    {
        // ── Constants ──────────────────────────────────────────────────────────
        /// <summary>Pixels per physics meter. 64px = 1m gives good feel for a tile-based game.</summary>
        public const float PIXELS_PER_METER = 64f;
        public const float METERS_PER_PIXEL = 1f / PIXELS_PER_METER;

        /// <summary>Gravity in m/s² (downward = positive Y in screen space).</summary>
        public static readonly Vector2 Gravity = new Vector2(0f, 20f);

        // ── Aether world ───────────────────────────────────────────────────────
        public AetherWorld World { get; }

        // ── Tracked bodies ─────────────────────────────────────────────────────
        private readonly List<Body> _bodiesToRemove = new();

        // ── Constructor ────────────────────────────────────────────────────────
        public PhysicsManager()
        {
            // Gravity is already in m/s² — do NOT convert with ToMeters()
            World = new AetherWorld(Gravity);
        }

        // ── Step ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Advance the physics simulation by one fixed timestep.
        /// Call once per game Update frame.
        /// </summary>
        // ── Sub-stepping constants ─────────────────────────────────────────────
        /// <summary>Fixed sub-step size in seconds (120 Hz). Halves the maximum
        /// distance a body can travel between collision checks compared to 60 Hz,
        /// which significantly reduces tunneling through thin edge-chain terrain.</summary>
        private const float FixedSubStep = 1f / 120f;
        /// <summary>Maximum sub-steps per frame to prevent spiral-of-death on slow frames.</summary>
        private const int MaxSubSteps = 4;

        public void Step(float deltaSeconds)
        {
            // Clamp total delta to avoid spiral of death on slow frames
            float remaining = Math.Min(deltaSeconds, 1f / 30f);

            // Process deferred body removals before stepping
            foreach (var body in _bodiesToRemove)
                World.Remove(body);
            _bodiesToRemove.Clear();

            // Sub-step the simulation at a fixed 120 Hz rate.
            // This halves the maximum per-frame travel distance compared to a
            // single 60 Hz step, reducing tunneling through edge-chain terrain.
            int steps = 0;
            while (remaining > 0f && steps < MaxSubSteps)
            {
                float sub = Math.Min(remaining, FixedSubStep);
                World.Step(TimeSpan.FromSeconds(sub));
                remaining -= sub;
                steps++;
            }
        }

        // ── Unit conversion helpers ────────────────────────────────────────────

        /// <summary>Convert a pixel-space Vector2 to meter-space.</summary>
        public static Vector2 ToMeters(Vector2 pixels)
            => pixels * METERS_PER_PIXEL;

        /// <summary>Convert a meter-space Vector2 to pixel-space.</summary>
        public static Vector2 ToPixels(Vector2 meters)
            => meters * PIXELS_PER_METER;

        /// <summary>Convert a pixel scalar to meters.</summary>
        public static float ToMeters(float pixels)
            => pixels * METERS_PER_PIXEL;

        /// <summary>Convert a meter scalar to pixels.</summary>
        public static float ToPixels(float meters)
            => meters * PIXELS_PER_METER;

        // ── Body lifecycle ─────────────────────────────────────────────────────

        /// <summary>
        /// Queue a body for removal at the start of the next physics step.
        /// Never remove bodies during a collision callback — use this instead.
        /// </summary>
        public void QueueRemoveBody(Body body)
        {
            if (!_bodiesToRemove.Contains(body))
                _bodiesToRemove.Add(body);
        }

        // ── Cleanup ────────────────────────────────────────────────────────────
        public void Dispose()
        {
            _bodiesToRemove.Clear();
        }
    }
}
