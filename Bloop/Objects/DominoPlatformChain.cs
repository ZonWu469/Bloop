using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Bloop.Objects
{
    /// <summary>
    /// Manages a chain of linked DisappearingPlatforms with a 1-second cascade delay.
    ///
    /// When the first platform in the chain is triggered (by player contact or
    /// TriggerFromChain()), this manager starts a cascade timer. Every CascadeDelay
    /// seconds, it triggers the next platform in the chain.
    ///
    /// This is NOT a WorldObject — it is a coordinator class held by Level.
    /// Level.Update() calls Update() on all chains each frame.
    /// </summary>
    public class DominoPlatformChain
    {
        // ── Tuning ─────────────────────────────────────────────────────────────
        private const float CascadeDelay = 1f; // seconds between each domino trigger

        // ── Identity ───────────────────────────────────────────────────────────
        public int ChainId { get; }

        // ── Platforms (sorted by ChainOrder) ──────────────────────────────────
        private readonly List<DisappearingPlatform> _platforms = new();

        // ── Cascade state ──────────────────────────────────────────────────────
        private bool  _cascadeActive;
        private float _cascadeTimer;
        private int   _nextCascadeIndex;

        // ── Constructor ────────────────────────────────────────────────────────
        public DominoPlatformChain(int chainId)
        {
            ChainId = chainId;
        }

        // ── Platform registration ──────────────────────────────────────────────

        /// <summary>
        /// Add a platform to this chain. Platforms should be added in ChainOrder.
        /// Wires up the OnTriggered callback so the chain is notified when any
        /// platform in the chain is touched.
        /// </summary>
        public void AddPlatform(DisappearingPlatform platform)
        {
            _platforms.Add(platform);

            // Wire the callback: when this platform is triggered, notify the chain
            platform.OnTriggered += OnPlatformTriggered;
        }

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Advance the cascade timer and trigger the next platform when ready.
        /// Call once per frame from Level.Update().
        /// </summary>
        public void Update(GameTime gameTime)
        {
            if (!_cascadeActive) return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _cascadeTimer -= dt;

            if (_cascadeTimer <= 0f && _nextCascadeIndex < _platforms.Count)
            {
                var next = _platforms[_nextCascadeIndex];
                if (!next.IsDestroyed)
                    next.TriggerFromChain();

                _nextCascadeIndex++;
                _cascadeTimer = CascadeDelay;

                // Stop cascade when all platforms have been triggered
                if (_nextCascadeIndex >= _platforms.Count)
                    _cascadeActive = false;
            }
        }

        // ── Callback ───────────────────────────────────────────────────────────

        /// <summary>
        /// Called when any platform in the chain is triggered.
        /// Starts the cascade from the platform AFTER the triggered one.
        /// </summary>
        private void OnPlatformTriggered(DisappearingPlatform source)
        {
            if (_cascadeActive) return; // already cascading

            // Find the index of the triggered platform
            int sourceIndex = _platforms.IndexOf(source);
            if (sourceIndex < 0) return;

            // Start cascade from the next platform
            _nextCascadeIndex = sourceIndex + 1;
            if (_nextCascadeIndex >= _platforms.Count) return; // last in chain, nothing to cascade

            _cascadeActive = true;
            _cascadeTimer  = CascadeDelay;
        }
    }
}
