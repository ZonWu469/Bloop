using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using Bloop.Core;
using Bloop.Effects;
using Bloop.Lighting;
using Bloop.Physics;
using Bloop.Rendering;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Objects
{
    /// <summary>
    /// A timed crystal formation that grows from a wall/floor anchor in a fixed direction,
    /// forming a walkable platform and grappling-hook attachment point, then retracts.
    ///
    /// State machine:
    ///   Dormant  (2–4 s)  → Growing   (1–2 s)  → Extended (5–10 s)
    ///   Extended           → Retracting (1–2 s) → Dormant
    ///
    /// Each segment is one tile (32 px) wide. Segments appear/disappear one by one
    /// during Growing/Retracting. Each visible segment has a static Aether body with
    /// CollisionCategories.CrystalBridge so the player can stand on it and the
    /// grappling hook can attach to it.
    /// </summary>
    public class CrystalBridge : WorldObject
    {
        // ── State machine ──────────────────────────────────────────────────────
        public enum BridgeState { Dormant, Growing, Extended, Retracting }

        // ── Segment geometry ───────────────────────────────────────────────────
        private const float SegmentSize   = TileMap.TileSize;       // 32 px
        private const float SegmentHeight = TileMap.TileSize * 1.0f; // 32 px — thicker platform (2×)

        // ── Timing ranges ──────────────────────────────────────────────────────
        private const float DormantMin   = 2f;
        private const float DormantMax   = 4f;
        private const float GrowDuration = 1.5f;  // total grow time (all segments)
        private const float ExtendedMin  = 5f;
        private const float ExtendedMax  = 10f;
        private const float RetractDuration = 1.2f; // total retract time

        // ── State ──────────────────────────────────────────────────────────────
        private BridgeState _state      = BridgeState.Dormant;
        private float       _stateTimer;
        private float       _dormantDuration;
        private float       _extendedDuration;

        // ── Geometry ───────────────────────────────────────────────────────────
        private readonly Vector2 _growDirection;  // normalized, e.g. (1,0) or (-1,0)
        private readonly int     _segmentCount;   // 3–6
        private readonly int     _seed;

        // ── Physics bodies (one per segment, null when not extended) ──────────
        private readonly Body?[] _segmentBodies;
        private readonly AetherWorld _world;

        // ── Visible segment count (0 → _segmentCount during grow/retract) ─────
        private int _visibleSegments;

        // ── Lighting ──────────────────────────────────────────────────────────
        private LightSource? _light;

        // ── Particles ─────────────────────────────────────────────────────────
        private readonly ObjectParticleEmitter _sparks = new ObjectParticleEmitter(32);

        // ── Colors ────────────────────────────────────────────────────────────
        private static readonly Color CoreColor  = new Color(150, 240, 255);
        private static readonly Color EdgeColor  = new Color( 40, 120, 170);
        private static readonly Color GlowColor  = new Color(110, 200, 240);
        private static readonly Color FlashColor = new Color(220, 255, 255);

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Create a CrystalBridge anchored at <paramref name="pixelPosition"/>.
        /// </summary>
        /// <param name="pixelPosition">Anchor point (wall/floor tile center).</param>
        /// <param name="world">Aether physics world.</param>
        /// <param name="growDirection">Normalized direction the bridge grows toward.</param>
        /// <param name="segmentCount">Number of segments (3–6).</param>
        /// <param name="seed">Deterministic seed for timing randomisation.</param>
        public CrystalBridge(Vector2 pixelPosition, AetherWorld world,
            Vector2 growDirection, int segmentCount, int seed = 0)
            : base(pixelPosition, world)
        {
            _world         = world;
            _growDirection = Vector2.Normalize(growDirection);
            _segmentCount  = Math.Clamp(segmentCount, 3, 6);
            _seed          = seed == 0 ? (int)(pixelPosition.X * 13 + pixelPosition.Y * 7) : seed;
            _segmentBodies = new Body?[_segmentCount];

            // Randomise initial dormant duration so nearby bridges don't sync
            var rng = new Random(_seed);
            _dormantDuration  = DormantMin + (float)rng.NextDouble() * (DormantMax - DormantMin);
            _extendedDuration = ExtendedMin + (float)rng.NextDouble() * (ExtendedMax - ExtendedMin);
            _stateTimer       = _dormantDuration;

            // No physics body on the anchor itself — segments create their own bodies
            Body = null;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        public BridgeState State => _state;

        /// <summary>Pixel-space center of segment <paramref name="index"/> (0-based).</summary>
        public Vector2 GetSegmentCenter(int index)
            => PixelPosition + _growDirection * ((index + 0.5f) * SegmentSize);

        /// <summary>True if segment <paramref name="index"/> is currently visible.</summary>
        public bool IsSegmentVisible(int index) => index < _visibleSegments;

        public void SetLightSource(LightSource light)
        {
            _light = light;
            UpdateLight();
        }

        // ── Update ─────────────────────────────────────────────────────────────

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _sparks.Update(dt);

            _stateTimer -= dt;

            switch (_state)
            {
                case BridgeState.Dormant:
                    if (_stateTimer <= 0f)
                        TransitionTo(BridgeState.Growing);
                    break;

                case BridgeState.Growing:
                {
                    // Compute how many segments should be visible based on progress
                    float progress = 1f - Math.Max(0f, _stateTimer / GrowDuration);
                    int target = (int)Math.Ceiling(progress * _segmentCount);
                    target = Math.Clamp(target, 0, _segmentCount);

                    // Spawn new segments
                    while (_visibleSegments < target)
                        SpawnSegment(_visibleSegments++);

                    if (_stateTimer <= 0f)
                    {
                        // Ensure all segments are spawned
                        while (_visibleSegments < _segmentCount)
                            SpawnSegment(_visibleSegments++);
                        TransitionTo(BridgeState.Extended);
                    }
                    break;
                }

                case BridgeState.Extended:
                    // Emit ambient crystal motes
                    EmitAmbientMotes(dt);
                    if (_stateTimer <= 0f)
                        TransitionTo(BridgeState.Retracting);
                    break;

                case BridgeState.Retracting:
                {
                    // Compute how many segments should remain based on progress
                    float progress = 1f - Math.Max(0f, _stateTimer / RetractDuration);
                    int target = _segmentCount - (int)Math.Ceiling(progress * _segmentCount);
                    target = Math.Clamp(target, 0, _segmentCount);

                    // Remove segments from the tip inward
                    while (_visibleSegments > target)
                        RemoveSegment(--_visibleSegments);

                    if (_stateTimer <= 0f)
                    {
                        // Ensure all segments are removed
                        while (_visibleSegments > 0)
                            RemoveSegment(--_visibleSegments);
                        TransitionTo(BridgeState.Dormant);
                    }
                    break;
                }
            }

            UpdateLight();
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        public override void Draw(SpriteBatch sb, AssetManager assets)
        {
            _sparks.Draw(sb, assets);

            float t     = AnimationClock.Time;
            float pulse = AnimationClock.Pulse(1.8f, (_seed & 0xFF) * 0.004f);

            // ── Anchor seed crystal (always visible) ──────────────────────────
            float seedR = 4f + pulse * 2f;
            OrganicPrimitives.DrawGradientDisk(sb, assets, PixelPosition,
                rIn: 1f, rOut: seedR + 4f,
                innerColor: GlowColor * (0.15f + pulse * 0.1f),
                outerColor: GlowColor * 0f,
                rings: 3, segments: 8);
            OrganicPrimitives.DrawFacetedGem(sb, assets, PixelPosition,
                seedR, 5, CoreColor, EdgeColor, t * 0.3f, _seed);

            if (_visibleSegments == 0) return;

            // ── Segment prisms ────────────────────────────────────────────────
            for (int i = 0; i < _visibleSegments; i++)
            {
                Vector2 center = GetSegmentCenter(i);

                // Fade-in alpha for the newest segment during Growing
                float alpha = 1f;
                if (_state == BridgeState.Growing && i == _visibleSegments - 1)
                    alpha = 0.5f + pulse * 0.5f;
                else if (_state == BridgeState.Retracting && i == _visibleSegments - 1)
                    alpha = 0.3f + pulse * 0.4f;

                // Light cascade traveling along the bridge
                float cascadePhase = t * 2f - i * 0.4f;
                float cascade      = MathF.Max(0f, MathF.Sin(cascadePhase));
                Color segCore = Color.Lerp(EdgeColor, CoreColor, 0.4f + pulse * 0.2f);
                Color segHi   = Color.Lerp(segCore, FlashColor, cascade * 0.6f);

                // Ambient glow per segment
                OrganicPrimitives.DrawGradientDisk(sb, assets, center,
                    rIn: 2f, rOut: 10f + pulse * 3f,
                    innerColor: GlowColor * (0.08f * alpha),
                    outerColor: GlowColor * 0f,
                    rings: 3, segments: 6);

                // Elongated crystal prism (drawn as a faceted gem, wider than tall)
                OrganicPrimitives.DrawFacetedGem(sb, assets, center,
                    SegmentSize * 0.38f, 4,
                    segCore * alpha, segHi * alpha,
                    t * 0.15f + i * 0.3f, _seed + i * 17);
            }

            // ── Tip glow when fully extended ──────────────────────────────────
            if (_state == BridgeState.Extended && _visibleSegments == _segmentCount)
            {
                Vector2 tip = GetSegmentCenter(_segmentCount - 1)
                              + _growDirection * (SegmentSize * 0.5f);
                OrganicPrimitives.DrawGradientDisk(sb, assets, tip,
                    rIn: 1f, rOut: 8f + pulse * 4f,
                    innerColor: FlashColor * (0.2f + pulse * 0.15f),
                    outerColor: FlashColor * 0f,
                    rings: 3, segments: 8);
            }
        }

        public override Rectangle GetBounds()
        {
            if (_visibleSegments == 0)
                return new Rectangle((int)PixelPosition.X - 8, (int)PixelPosition.Y - 8, 16, 16);

            Vector2 tip = GetSegmentCenter(_visibleSegments - 1) + _growDirection * (SegmentSize * 0.5f);
            float minX = Math.Min(PixelPosition.X, tip.X);
            float minY = Math.Min(PixelPosition.Y, tip.Y);
            float maxX = Math.Max(PixelPosition.X, tip.X);
            float maxY = Math.Max(PixelPosition.Y, tip.Y);
            return new Rectangle((int)minX, (int)minY, (int)(maxX - minX) + 1, (int)(maxY - minY) + 1);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private void TransitionTo(BridgeState next)
        {
            _state = next;
            switch (next)
            {
                case BridgeState.Dormant:
                    var rng = new Random(_seed + (int)(AnimationClock.Time * 100f));
                    _dormantDuration  = DormantMin + (float)rng.NextDouble() * (DormantMax - DormantMin);
                    _extendedDuration = ExtendedMin + (float)rng.NextDouble() * (ExtendedMax - ExtendedMin);
                    _stateTimer = _dormantDuration;
                    break;
                case BridgeState.Growing:
                    _stateTimer = GrowDuration;
                    break;
                case BridgeState.Extended:
                    _stateTimer = _extendedDuration;
                    break;
                case BridgeState.Retracting:
                    _stateTimer = RetractDuration;
                    break;
            }
        }

        private void SpawnSegment(int index)
        {
            if (_segmentBodies[index] != null) return;

            Vector2 center = GetSegmentCenter(index);
            var body = BodyFactory.CreateStaticRect(
                _world, center,
                SegmentSize, SegmentHeight,
                CollisionCategories.CrystalBridge);
            body.Tag = this;
            _segmentBodies[index] = body;

            // Emit a small flash of sparks when a segment appears
            EmitSpawnSparks(center);
        }

        private void RemoveSegment(int index)
        {
            if (_segmentBodies[index] == null) return;
            _world.Remove(_segmentBodies[index]!);
            _segmentBodies[index] = null;

            // Emit retract sparks
            EmitRetractSparks(GetSegmentCenter(index));
        }

        private void EmitSpawnSparks(Vector2 pos)
        {
            var rng = new Random(_seed + (int)(pos.X + pos.Y));
            for (int i = 0; i < 4; i++)
            {
                float a  = (float)rng.NextDouble() * MathHelper.TwoPi;
                float sp = 20f + (float)rng.NextDouble() * 30f;
                _sparks.Emit(pos,
                    new Vector2(MathF.Cos(a) * sp, MathF.Sin(a) * sp - 15f),
                    FlashColor, life: 0.4f, size: 1.5f, gravity: 60f, drag: 1f);
            }
        }

        private void EmitRetractSparks(Vector2 pos)
        {
            var rng = new Random(_seed + (int)(pos.X * 3 + pos.Y));
            for (int i = 0; i < 3; i++)
            {
                float a  = (float)rng.NextDouble() * MathHelper.TwoPi;
                float sp = 15f + (float)rng.NextDouble() * 20f;
                _sparks.Emit(pos,
                    new Vector2(MathF.Cos(a) * sp, MathF.Sin(a) * sp),
                    GlowColor, life: 0.3f, size: 1f, gravity: 40f, drag: 1.5f);
            }
        }

        private void EmitAmbientMotes(float dt)
        {
            // Occasional ambient mote drifting off the bridge
            if (_visibleSegments == 0) return;
            float moteChance = dt * 2f;
            if (NoiseHelpers.Hash01(_seed + (int)(AnimationClock.Time * 10f)) < moteChance)
            {
                int idx = (int)(NoiseHelpers.Hash01(_seed + _sparks.ActiveCount) * _visibleSegments);
                idx = Math.Clamp(idx, 0, _visibleSegments - 1);
                Vector2 pos = GetSegmentCenter(idx);
                float a = NoiseHelpers.Hash01(_seed + idx * 7) * MathHelper.TwoPi;
                _sparks.Emit(pos + new Vector2(MathF.Cos(a) * 6f, MathF.Sin(a) * 6f),
                    new Vector2(0f, -8f),
                    GlowColor, life: 0.8f, size: 1f, gravity: 0f, drag: 2f);
            }
        }

        private void UpdateLight()
        {
            if (_light == null) return;
            _light.Position = PixelPosition;

            switch (_state)
            {
                case BridgeState.Dormant:
                    _light.Intensity = 0.05f;
                    _light.Radius    = 20f;
                    break;
                case BridgeState.Growing:
                {
                    float t = 1f - Math.Max(0f, _stateTimer / GrowDuration);
                    _light.Intensity = MathHelper.Lerp(0.05f, 0.5f, t);
                    _light.Radius    = MathHelper.Lerp(20f, 60f, t);
                    break;
                }
                case BridgeState.Extended:
                {
                    float pulse = AnimationClock.Pulse(1.8f, (_seed & 0xFF) * 0.004f);
                    _light.Intensity = 0.45f + pulse * 0.1f;
                    _light.Radius    = 55f + pulse * 10f;
                    break;
                }
                case BridgeState.Retracting:
                {
                    float t = 1f - Math.Max(0f, _stateTimer / RetractDuration);
                    _light.Intensity = MathHelper.Lerp(0.5f, 0.05f, t);
                    _light.Radius    = MathHelper.Lerp(60f, 20f, t);
                    break;
                }
            }
        }

        // ── Cleanup ────────────────────────────────────────────────────────────

        /// <summary>
        /// Remove all active segment bodies from the physics world.
        /// Called by Level.cs when the level is disposed or this object is removed.
        /// The physics world is cleared on level reload anyway, but this ensures
        /// clean removal if the object is explicitly destroyed mid-level.
        /// </summary>
        public void CleanupBodies()
        {
            for (int i = 0; i < _segmentBodies.Length; i++)
            {
                if (_segmentBodies[i] != null)
                {
                    _world.Remove(_segmentBodies[i]!);
                    _segmentBodies[i] = null;
                }
            }
            _visibleSegments = 0;
        }
    }
}
