namespace Bloop.Rendering
{
    /// <summary>
    /// Global animation time provider. Updated once per frame by GameplayScreen.
    /// All geometric renderers reference AnimationClock.Time for ambient animations,
    /// avoiding the need to pass GameTime through every draw call.
    /// </summary>
    public static class AnimationClock
    {
        // ── State ──────────────────────────────────────────────────────────────
        /// <summary>Total elapsed game time in seconds since the session started.</summary>
        public static float Time { get; private set; }

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Advance the clock by deltaSeconds. Call once per frame from GameplayScreen.Update().
        /// </summary>
        public static void Update(float deltaSeconds)
        {
            Time += deltaSeconds;
        }

        /// <summary>Reset the clock to zero (e.g. on level reload).</summary>
        public static void Reset()
        {
            Time = 0f;
        }

        // ── Common animation helpers ───────────────────────────────────────────

        /// <summary>
        /// Returns a value in [0, 1] that oscillates smoothly using a sine wave.
        /// frequency: oscillations per second. phase: offset in radians.
        /// </summary>
        public static float Pulse(float frequency = 1f, float phase = 0f)
            => (float)(System.Math.Sin(Time * frequency * System.Math.PI * 2.0 + phase) * 0.5 + 0.5);

        /// <summary>
        /// Returns a signed oscillation in [-amplitude, +amplitude].
        /// </summary>
        public static float Sway(float amplitude, float frequency = 1f, float phase = 0f)
            => (float)(System.Math.Sin(Time * frequency * System.Math.PI * 2.0 + phase) * amplitude);

        /// <summary>
        /// Returns a value in [0, 1] that loops linearly (sawtooth).
        /// Useful for drip/fall animations.
        /// </summary>
        public static float Loop(float period = 1f, float phase = 0f)
        {
            float t = (Time + phase) % period;
            return t < 0 ? t + period : t;
        }
    }
}
