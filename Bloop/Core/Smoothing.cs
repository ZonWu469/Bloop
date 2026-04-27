using System;
using Microsoft.Xna.Framework;

namespace Bloop.Core
{
    /// <summary>
    /// Frame-rate-independent smoothing helpers.
    ///
    /// The classic <c>Lerp(current, target, factor)</c> pattern produces different
    /// behavior at different frame rates: a 0.12 factor reaches the target much
    /// faster at 240 Hz than at 60 Hz. <see cref="ExpDecay"/> uses
    /// <c>1 - exp(-rate * dt)</c> so the same <paramref name="rate"/> produces
    /// identical convergence regardless of frame rate.
    ///
    /// <paramref name="rate"/> is intuitive: it is the reciprocal of the
    /// "settling time constant" (~63% closure per <c>1/rate</c> seconds).
    /// </summary>
    public static class Smoothing
    {
        /// <summary>
        /// Frame-rate-independent exponential decay toward a target scalar.
        /// </summary>
        public static float ExpDecay(float current, float target, float rate, float dt)
        {
            if (rate <= 0f || dt <= 0f) return current;
            float t = 1f - MathF.Exp(-rate * dt);
            return current + (target - current) * t;
        }

        /// <summary>
        /// Frame-rate-independent exponential decay toward a target Vector2.
        /// </summary>
        public static Vector2 ExpDecay(Vector2 current, Vector2 target, float rate, float dt)
        {
            if (rate <= 0f || dt <= 0f) return current;
            float t = 1f - MathF.Exp(-rate * dt);
            return current + (target - current) * t;
        }

        /// <summary>
        /// Convert a per-60Hz-tick lerp factor (the legacy idiom) into a
        /// continuous rate suitable for <see cref="ExpDecay"/>. Useful when
        /// migrating call sites that previously used <c>Lerp(a, b, 0.12f)</c>:
        /// pass <c>0.12f</c> here to get an equivalent <c>rate</c> that matches
        /// the same speed at 60 Hz and stays consistent across frame rates.
        /// </summary>
        public static float RateFromPerTick60Hz(float perTickFactor)
        {
            float clamped = MathHelper.Clamp(perTickFactor, 0f, 0.999f);
            // Lerp(a,b,f) reaches f-fraction in 1 tick; convert to a continuous rate.
            return -60f * MathF.Log(1f - clamped);
        }
    }
}
