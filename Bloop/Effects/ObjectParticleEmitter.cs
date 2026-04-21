using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;

namespace Bloop.Effects
{
    /// <summary>
    /// Lightweight per-object particle pool. Used by individual world objects
    /// to emit spores, drips, sparks, shards, dust, bubbles etc. attached to
    /// that specific object.
    ///
    /// Distinct from the global Bloop.Effects.ParticleSystem (ambient weather).
    /// Struct-array based — zero per-frame allocations after construction.
    /// </summary>
    public sealed class ObjectParticleEmitter
    {
        private struct P
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public Color   Color;
            public float   Life;
            public float   MaxLife;
            public float   Size;
            public float   Gravity;
            public float   Drag;
            public bool    Active;
        }

        private readonly P[] _pool;
        private int _head;
        private int _activeCount;

        public ObjectParticleEmitter(int capacity = 32)
        {
            if (capacity < 1) capacity = 1;
            _pool = new P[capacity];
        }

        public int ActiveCount => _activeCount;
        public int Capacity    => _pool.Length;

        /// <summary>
        /// Spawn a new particle. Overwrites the oldest slot if full.
        /// </summary>
        public void Emit(Vector2 pos, Vector2 vel, Color color,
            float life, float size, float gravity = 0f, float drag = 0f)
        {
            // Find a free slot, else overwrite the round-robin head.
            int idx = -1;
            for (int i = 0; i < _pool.Length; i++)
            {
                int k = (_head + i) % _pool.Length;
                if (!_pool[k].Active) { idx = k; break; }
            }
            if (idx < 0)
            {
                idx = _head;
                _head = (_head + 1) % _pool.Length;
            }
            else
            {
                _head = (idx + 1) % _pool.Length;
                _activeCount++;
            }

            ref var p = ref _pool[idx];
            p.Position = pos;
            p.Velocity = vel;
            p.Color    = color;
            p.Life     = life;
            p.MaxLife  = life;
            p.Size     = MathF.Max(1f, size);
            p.Gravity  = gravity;
            p.Drag     = drag;
            p.Active   = true;
        }

        public void Update(float dt)
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                ref var p = ref _pool[i];
                if (!p.Active) continue;

                p.Life -= dt;
                if (p.Life <= 0f)
                {
                    p.Active = false;
                    _activeCount--;
                    continue;
                }

                p.Velocity.Y += p.Gravity * dt;
                if (p.Drag > 0f)
                {
                    float k = MathF.Max(0f, 1f - p.Drag * dt);
                    p.Velocity *= k;
                }
                p.Position += p.Velocity * dt;
            }
        }

        public void Draw(SpriteBatch sb, AssetManager assets)
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                ref var p = ref _pool[i];
                if (!p.Active) continue;

                float alpha = MathHelper.Clamp(p.Life / MathF.Max(0.0001f, p.MaxLife), 0f, 1f);
                int s = MathF.Max(1f, p.Size) < 2f ? 1 : (int)p.Size;
                assets.DrawRect(sb,
                    new Rectangle((int)p.Position.X - s / 2, (int)p.Position.Y - s / 2, s, s),
                    p.Color * alpha);
            }
        }

        /// <summary>Clear all active particles.</summary>
        public void Clear()
        {
            for (int i = 0; i < _pool.Length; i++) _pool[i].Active = false;
            _activeCount = 0;
        }
    }
}
