using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Bloop.Generators;
using Bloop.Objects;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.World
{
    /// <summary>
    /// Recurring world event: every 10–30s the cave shakes, random solid tiles are
    /// destroyed across the whole map, and a support-propagation pass triggers
    /// semi-realistic collapse of any now-unsupported tiles.
    ///
    /// Lifecycle (single quake):
    ///   Idle → Warning (telegraphing shake + dust) → Active (destruction + collapse)
    ///        → Aftershock (settling + physics rebuild) → Idle.
    ///
    /// Support rule: a solid tile is "supported" iff
    ///   1. The tile directly below is supported, or
    ///   2. A supported tile lies within SupportChainLength cardinal steps
    ///      through an unbroken chain of solid tiles.
    /// Any solid tile that fails both is dropped as FallingRubble.
    ///
    /// Protected zones (entry/exit) are never directly destroyed so the run
    /// cannot be soft-locked.
    /// </summary>
    public class EarthquakeSystem
    {
        // ── Phases ─────────────────────────────────────────────────────────────
        public enum Phase { Idle, Warning, Active, Aftershock }

        public Phase CurrentPhase { get; private set; } = Phase.Idle;

        // ── Tuning constants ───────────────────────────────────────────────────
        private const float MinInterval        = 45f;
        private const float MaxInterval        = 90f;
        private const float WarningDuration    = 2.0f;
        private const float ActiveDuration     = 3.0f;
        private const float AftershockDuration = 1.0f;
        private const float ProtectionRadius   = 160f;   // ~5 tiles around entry/exit
        private const float PlayerRubbleSkipRadius = 48f; // don't drop rubble on top of player
        private const int   SupportChainLength = 3;
        private const int   AffectedRegionMargin = 8;
        private const int   SplashDamage       = 24;    // per-neighbor crack damage (4-cardinal)
        private const int   ReachabilityPruneAttempts = 1; // retries beyond the initial plan

        // ── Dependencies ───────────────────────────────────────────────────────
        private readonly TileMap     _tileMap;
        private readonly AetherWorld _world;
        private readonly BiomeTier   _biome;
        private readonly Vector2     _entryPoint;
        private readonly Vector2     _exitPoint;
        private readonly Random      _rng;
        private readonly Func<IEnumerable<Vector2>>? _liveShardPositions;

        // ── Timers ─────────────────────────────────────────────────────────────
        private float _idleCountdown;
        private float _phaseTimer;

        // ── Observation hooks ──────────────────────────────────────────────────
        /// <summary>Invoked each frame during shake phases: (amplitude, duration).</summary>
        public Action<float, float>? OnShakeRequested;

        /// <summary>Invoked once per spawned rubble piece so the caller can add it to the Level.</summary>
        public Action<FallingRubble>? OnRubbleSpawned;

        /// <summary>Invoked at the start of the Warning phase so the caller can react (SFX, lantern flicker).</summary>
        public Action? OnWarningStarted;

        /// <summary>Invoked after tile physics bodies are rebuilt by a committed quake.</summary>
        public Action? OnTerrainRebuilt;

        // ── Constructor ────────────────────────────────────────────────────────
        public EarthquakeSystem(TileMap tileMap, AetherWorld world, int seed,
            Vector2 entryPoint, Vector2 exitPoint, BiomeTier biome,
            Func<IEnumerable<Vector2>>? liveShardPositions = null)
        {
            _tileMap            = tileMap;
            _world              = world;
            _biome              = biome;
            _entryPoint         = entryPoint;
            _exitPoint          = exitPoint;
            _liveShardPositions = liveShardPositions;
            _rng                = new Random(seed ^ 0x4E7E1);

            // First quake biased toward the long end so the player can orient.
            _idleCountdown = MinInterval + 10f
                + (float)_rng.NextDouble() * (MaxInterval - MinInterval - 10f);
        }

        // ── Update ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Tick the earthquake state machine. Call once per frame from Level.Update.
        /// playerPos is used to avoid spawning rubble on top of the player.
        /// </summary>
        public void Update(GameTime gameTime, Vector2 playerPos)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            switch (CurrentPhase)
            {
                case Phase.Idle:
                    _idleCountdown -= dt;
                    if (_idleCountdown <= 0f) EnterWarning();
                    break;

                case Phase.Warning:
                    _phaseTimer -= dt;
                    if (_phaseTimer <= 0f) EnterActive(playerPos);
                    break;

                case Phase.Active:
                    _phaseTimer -= dt;
                    if (_phaseTimer <= 0f) EnterAftershock();
                    break;

                case Phase.Aftershock:
                    _phaseTimer -= dt;
                    if (_phaseTimer <= 0f) EnterIdle();
                    break;
            }
        }

        // ── State transitions ──────────────────────────────────────────────────

        private void EnterWarning()
        {
            CurrentPhase = Phase.Warning;
            _phaseTimer  = WarningDuration;
            OnShakeRequested?.Invoke(2f, WarningDuration);
            OnWarningStarted?.Invoke();
        }

        private void EnterActive(Vector2 playerPos)
        {
            CurrentPhase = Phase.Active;
            _phaseTimer  = ActiveDuration;
            OnShakeRequested?.Invoke(9f, ActiveDuration);

            ExecuteQuake(playerPos);
        }

        private void EnterAftershock()
        {
            CurrentPhase = Phase.Aftershock;
            _phaseTimer  = AftershockDuration;
            OnShakeRequested?.Invoke(3.5f, AftershockDuration);

            // Physics bodies are already in sync — they were regenerated at the
            // end of ExecuteQuake so the player falls immediately. This phase
            // is purely shake + settling animation.
        }

        private void EnterIdle()
        {
            CurrentPhase   = Phase.Idle;
            _idleCountdown = MinInterval
                + (float)_rng.NextDouble() * (MaxInterval - MinInterval);
        }

        // ── Quake execution ────────────────────────────────────────────────────

        private void ExecuteQuake(Vector2 playerPos)
        {
            int targetCount = BaseDestroyedTilesForBiome();
            var primaries   = SelectDestructionTiles(targetCount);
            if (primaries.Count == 0) return;

            // Preview: build the full collapse set on a scratch copy, then check
            // entry → shards → exit reachability. Prune primaries that break it.
            HashSet<(int, int)>? collapsed = PlanReachableCollapse(primaries);
            if (collapsed == null || collapsed.Count == 0) return; // skipped

            // Commit: drop tiles, crack cardinal neighbors, spawn rubble.
            foreach (var (tx, ty) in collapsed)
            {
                var pos = TileMap.TileCenter(tx, ty);
                if (ShouldSpawnRubble(pos, playerPos))
                    SpawnRubble(pos);

                _tileMap.SetTile(tx, ty, TileType.Empty);

                _tileMap.AddDamage(tx, ty - 1, SplashDamage);
                _tileMap.AddDamage(tx + 1, ty, SplashDamage);
                _tileMap.AddDamage(tx, ty + 1, SplashDamage);
                _tileMap.AddDamage(tx - 1, ty, SplashDamage);
            }

            // Rebuild terrain physics in the same frame as the tile edits so the
            // player falls through a newly-destroyed floor immediately instead of
            // standing on an invisible ghost body for the rest of the Active phase.
            _tileMap.GeneratePhysicsBodies(_world);
            OnTerrainRebuilt?.Invoke();
        }

        /// <summary>
        /// Given a set of primary destruction tiles, compute the full collapse
        /// set (primaries + unsupported cascaded tiles) and verify that the
        /// player can still reach every live shard and the exit from the entry.
        /// Returns the collapse set if reachable, null if no safe plan was found.
        /// </summary>
        private HashSet<(int, int)>? PlanReachableCollapse(List<(int tx, int ty)> primaries)
        {
            int attempts = ReachabilityPruneAttempts + 1;
            while (attempts-- > 0 && primaries.Count > 0)
            {
                var collapsed = ComputeCollapseSet(primaries);
                if (IsPlanReachable(collapsed))
                    return collapsed;

                // Prune: drop the primary whose local cell is most central to the
                // collapse set (the one that cascaded the most), then retry.
                primaries.RemoveAt(PickPruneIndex(primaries, collapsed));
            }
            return null;
        }

        private int PickPruneIndex(List<(int tx, int ty)> primaries,
            HashSet<(int, int)> collapsed)
        {
            // Score each primary by how many of its 4-cardinal neighbors also
            // collapsed — the more, the bigger its ripple and the better its
            // removal will restore a path.
            int bestIdx = 0;
            int bestScore = -1;
            for (int i = 0; i < primaries.Count; i++)
            {
                var (tx, ty) = primaries[i];
                int score = 0;
                if (collapsed.Contains((tx, ty - 1))) score++;
                if (collapsed.Contains((tx + 1, ty))) score++;
                if (collapsed.Contains((tx, ty + 1))) score++;
                if (collapsed.Contains((tx - 1, ty))) score++;
                if (score > bestScore) { bestScore = score; bestIdx = i; }
            }
            return bestIdx;
        }

        private bool IsPlanReachable(HashSet<(int, int)> collapsed)
        {
            // Empty collapse set = nothing to check.
            if (collapsed.Count == 0) return true;

            var waypoints = new List<Vector2> { _entryPoint };
            if (_liveShardPositions != null)
                waypoints.AddRange(_liveShardPositions());
            waypoints.Add(_exitPoint);

            for (int i = 0; i + 1 < waypoints.Count; i++)
            {
                int sx = (int)(waypoints[i].X / TileMap.TileSize);
                int sy = (int)(waypoints[i].Y / TileMap.TileSize);
                int gx = (int)(waypoints[i + 1].X / TileMap.TileSize);
                int gy = (int)(waypoints[i + 1].Y / TileMap.TileSize);

                var path = PathValidator.BFS(_tileMap, sx, sy, gx, gy, collapsed);
                if (path == null) return false;
            }
            return true;
        }

        private int BaseDestroyedTilesForBiome() => _biome switch
        {
            BiomeTier.ShallowCaves  => 1,
            BiomeTier.FungalGrottos => 2,
            BiomeTier.CrystalDepths => 3,
            BiomeTier.TheAbyss      => 5,
            _                       => 2,
        };

        // ── Tile selection (level-wide weighted random) ────────────────────────

        private List<(int tx, int ty)> SelectDestructionTiles(int target)
        {
            var candidates = new List<((int tx, int ty) coord, float weight)>();
            float pRadSq = ProtectionRadius * ProtectionRadius;

            for (int ty = 0; ty < _tileMap.Height; ty++)
            for (int tx = 0; tx < _tileMap.Width;  tx++)
            {
                TileType type = _tileMap.GetTile(tx, ty);
                if (!TileProperties.IsSolid(type)) continue;

                var pos = TileMap.TileCenter(tx, ty);
                if (Vector2.DistanceSquared(pos, _entryPoint) < pRadSq) continue;
                if (Vector2.DistanceSquared(pos, _exitPoint)  < pRadSq) continue;

                int exposure = CountExposedCardinals(tx, ty);
                if (exposure == 0) continue;  // interior rock isn't destroyed directly

                bool cantilever = exposure == 3; // only one solid cardinal neighbor
                byte dmg = _tileMap.GetDamage(tx, ty);

                float weight = exposure
                             + (cantilever ? 3f : 0f)
                             + dmg / 80f;
                candidates.Add(((tx, ty), weight));
            }

            var result = new List<(int, int)>();
            for (int i = 0; i < target && candidates.Count > 0; i++)
            {
                float total = 0f;
                for (int j = 0; j < candidates.Count; j++) total += candidates[j].weight;

                float r = (float)_rng.NextDouble() * total;
                int idx = candidates.Count - 1;
                for (int j = 0; j < candidates.Count; j++)
                {
                    r -= candidates[j].weight;
                    if (r <= 0f) { idx = j; break; }
                }

                result.Add(candidates[idx].coord);
                candidates.RemoveAt(idx);
            }
            return result;
        }

        private int CountExposedCardinals(int tx, int ty)
        {
            int count = 0;
            if (!TileProperties.IsSolid(_tileMap.GetTile(tx, ty - 1))) count++;
            if (!TileProperties.IsSolid(_tileMap.GetTile(tx + 1, ty))) count++;
            if (!TileProperties.IsSolid(_tileMap.GetTile(tx, ty + 1))) count++;
            if (!TileProperties.IsSolid(_tileMap.GetTile(tx - 1, ty))) count++;
            return count;
        }

        // ── Support propagation (pure; no side effects on the tile map) ────────

        /// <summary>
        /// Dry-run collapse simulation. Returns the full set of tiles that would
        /// be removed if the given primaries were dropped — primaries ∪ cascaded
        /// unsupported tiles. The live tile map is not modified.
        /// </summary>
        private HashSet<(int, int)> ComputeCollapseSet(List<(int tx, int ty)> primaries)
        {
            var collapsed = new HashSet<(int, int)>(primaries);
            if (primaries.Count == 0) return collapsed;

            int minTx = int.MaxValue, minTy = int.MaxValue;
            int maxTx = int.MinValue, maxTy = int.MinValue;
            foreach (var (tx, ty) in primaries)
            {
                if (tx < minTx) minTx = tx;
                if (ty < minTy) minTy = ty;
                if (tx > maxTx) maxTx = tx;
                if (ty > maxTy) maxTy = ty;
            }

            minTx = Math.Max(0, minTx - AffectedRegionMargin);
            minTy = Math.Max(0, minTy - AffectedRegionMargin);
            maxTx = Math.Min(_tileMap.Width  - 1, maxTx + AffectedRegionMargin);
            maxTy = Math.Min(_tileMap.Height - 1, maxTy + AffectedRegionMargin);

            int w = maxTx - minTx + 1;
            int h = maxTy - minTy + 1;
            bool[,] supported = new bool[w, h];

            // Seed: bottom-row solids, or solids whose direct below-neighbor is
            // a solid tile outside the region (= trusted supported). A primary
            // counts as "empty" even though the tile map still reports it solid.
            for (int ty = minTy; ty <= maxTy; ty++)
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                if (!IsSolidForSim(tx, ty, collapsed)) continue;

                if (ty == _tileMap.Height - 1)
                {
                    supported[tx - minTx, ty - minTy] = true;
                    continue;
                }

                int by = ty + 1;
                bool belowInRegion = by >= minTy && by <= maxTy;
                if (!belowInRegion && IsSolidForSim(tx, by, collapsed))
                    supported[tx - minTx, ty - minTy] = true;
            }

            for (int pass = 0; pass < 32; pass++)
            {
                bool changed = false;

                for (int ty = minTy; ty <= maxTy; ty++)
                for (int tx = minTx; tx <= maxTx; tx++)
                {
                    if (supported[tx - minTx, ty - minTy]) continue;
                    if (!IsSolidForSim(tx, ty, collapsed)) continue;

                    if (IsSupportedAt(tx, ty + 1, minTx, minTy, maxTx, maxTy, supported, collapsed))
                    {
                        supported[tx - minTx, ty - minTy] = true;
                        changed = true;
                        continue;
                    }

                    bool found = false;
                    for (int d = 1; d <= SupportChainLength && !found; d++)
                    {
                        if (IsSupportedAt(tx - d, ty, minTx, minTy, maxTx, maxTy, supported, collapsed) &&
                            AllSolidBetween(tx - d + 1, tx - 1, ty, collapsed))
                            found = true;
                        else if (IsSupportedAt(tx + d, ty, minTx, minTy, maxTx, maxTy, supported, collapsed) &&
                                 AllSolidBetween(tx + 1, tx + d - 1, ty, collapsed))
                            found = true;
                    }
                    if (found)
                    {
                        supported[tx - minTx, ty - minTy] = true;
                        changed = true;
                    }
                }

                if (!changed) break;
            }

            // Any in-region solid left unsupported cascades.
            for (int ty = minTy; ty <= maxTy; ty++)
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                if (!IsSolidForSim(tx, ty, collapsed)) continue;
                if (supported[tx - minTx, ty - minTy]) continue;
                collapsed.Add((tx, ty));
            }

            return collapsed;
        }

        private bool IsSolidForSim(int tx, int ty, HashSet<(int, int)> removed) =>
            !removed.Contains((tx, ty))
            && TileProperties.IsSolid(_tileMap.GetTile(tx, ty));

        private bool IsSupportedAt(int tx, int ty, int minTx, int minTy, int maxTx, int maxTy,
            bool[,] supported, HashSet<(int, int)> removed)
        {
            if (tx < 0 || ty < 0 || tx >= _tileMap.Width || ty >= _tileMap.Height) return false;
            if (!IsSolidForSim(tx, ty, removed)) return false;

            bool inRegion = tx >= minTx && tx <= maxTx && ty >= minTy && ty <= maxTy;
            if (!inRegion) return true;
            return supported[tx - minTx, ty - minTy];
        }

        private bool AllSolidBetween(int tx0, int tx1, int ty, HashSet<(int, int)> removed)
        {
            if (tx0 > tx1) return true;
            for (int tx = tx0; tx <= tx1; tx++)
                if (!IsSolidForSim(tx, ty, removed))
                    return false;
            return true;
        }

        // ── Rubble spawning ────────────────────────────────────────────────────

        private bool ShouldSpawnRubble(Vector2 tilePos, Vector2 playerPos)
        {
            return Vector2.DistanceSquared(tilePos, playerPos)
                 > PlayerRubbleSkipRadius * PlayerRubbleSkipRadius;
        }

        private void SpawnRubble(Vector2 pos)
        {
            var rubble = new FallingRubble(pos, _world);
            OnRubbleSpawned?.Invoke(rubble);
        }
    }
}
