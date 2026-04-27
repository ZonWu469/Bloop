using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;

namespace Bloop.Audio
{
    /// <summary>
    /// Bus categories for volume control. Lets the Options screen scale categories
    /// independently (e.g., lower SFX, keep ambience full).
    /// </summary>
    public enum AudioBus { Sfx, Ambience, Ui }

    /// <summary>
    /// Centralized audio playback. Loads sound effects via the MonoGame
    /// <see cref="ContentManager"/> and plays them with simple pitch/volume/pan
    /// variation. Missing assets are tolerated silently — calls to <see cref="Play"/>
    /// with an unloaded key are no-ops, so gameplay code can request audio without
    /// requiring every sound file to be authored up front.
    ///
    /// Wire this once at startup, then call <see cref="Play"/> from gameplay.
    /// </summary>
    public class AudioManager
    {
        private readonly Dictionary<string, SoundEffect> _sounds = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AudioBus>    _busOf  = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<AudioBus, float>     _busVolume = new()
        {
            [AudioBus.Sfx]      = 1f,
            [AudioBus.Ambience] = 0.7f,
            [AudioBus.Ui]       = 0.9f,
        };

        /// <summary>Master volume scalar (0–1). Multiplied into every play call.</summary>
        public float MasterVolume { get; set; } = 1f;

        private readonly Random _rng = new();

        /// <summary>
        /// Try to load a sound effect from <c>Content/Audio/{contentName}</c>.
        /// Silent on failure so missing assets don't crash the game.
        /// </summary>
        public void TryLoad(ContentManager content, string key, string contentName, AudioBus bus = AudioBus.Sfx)
        {
            try
            {
                var sfx = content.Load<SoundEffect>(contentName);
                _sounds[key] = sfx;
                _busOf [key] = bus;
            }
            catch (ContentLoadException) { /* asset missing — ignored */ }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AudioManager] Load '{key}': {ex.Message}"); }
        }

        public void SetBusVolume(AudioBus bus, float volume)
            => _busVolume[bus] = MathHelper.Clamp(volume, 0f, 1f);

        public float GetBusVolume(AudioBus bus)
            => _busVolume.TryGetValue(bus, out float v) ? v : 1f;

        /// <summary>
        /// Play a one-shot sound. <paramref name="volume"/>, <paramref name="pitch"/>,
        /// and <paramref name="pan"/> are applied on top of bus and master volume.
        /// Pitch range [-1, 1] (semitones-ish per MonoGame convention).
        /// </summary>
        public void Play(string key, float volume = 1f, float pitch = 0f, float pan = 0f)
        {
            if (!_sounds.TryGetValue(key, out var sfx)) return;

            AudioBus bus = _busOf.TryGetValue(key, out var b) ? b : AudioBus.Sfx;
            float finalVol = MathHelper.Clamp(volume * GetBusVolume(bus) * MasterVolume, 0f, 1f);
            if (finalVol <= 0.001f) return;

            try { sfx.Play(finalVol, MathHelper.Clamp(pitch, -1f, 1f), MathHelper.Clamp(pan, -1f, 1f)); }
            catch (Exception) { /* audio device may be unavailable — silent fail */ }
        }

        /// <summary>
        /// Play with random pitch jitter. Useful for footsteps / repeated SFX so
        /// they don't sound robotic. <paramref name="pitchJitter"/> is the half-range
        /// (e.g., 0.1 = ±0.1 pitch).
        /// </summary>
        public void PlayVaried(string key, float volume = 1f, float pitchJitter = 0.08f, float pan = 0f)
        {
            float pitch = ((float)_rng.NextDouble() * 2f - 1f) * pitchJitter;
            Play(key, volume, pitch, pan);
        }

        /// <summary>
        /// Play with simple positional attenuation. Pass the listener (player) and
        /// emitter world positions; volume falls off with distance and pan tracks
        /// horizontal offset. <paramref name="maxDistancePx"/> bounds the audible range.
        /// </summary>
        public void PlayAt(string key, Vector2 listenerPx, Vector2 emitterPx,
                           float maxDistancePx = 600f, float volume = 1f, float pitchJitter = 0.05f)
        {
            Vector2 delta = emitterPx - listenerPx;
            float dist = delta.Length();
            if (dist >= maxDistancePx) return;

            float falloff = 1f - (dist / maxDistancePx);
            falloff = falloff * falloff; // ease-out: stays loud nearby, fades quickly far
            float pan = MathHelper.Clamp(delta.X / (maxDistancePx * 0.5f), -1f, 1f);
            PlayVaried(key, volume * falloff, pitchJitter, pan);
        }
    }
}
