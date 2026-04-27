using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Joints;
using Bloop.Core;
using Bloop.Physics;
using Bloop.World;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.Gameplay
{
    /// <summary>
    /// Shared rope-terrain collision system used by both RopeSystem and GrapplingHook.
    ///
    /// Implements rope wrapping: when the straight line from the current anchor to the
    /// player passes through solid terrain, the rope wraps around the corner geometry.
    /// Intermediate "wrap point" anchors are created at terrain corners, splitting the
    /// rope into segments. When the player swings back, wrap points are removed (unwrap).
    ///
    /// This prevents the rope from phasing through walls and limits the swing arc to
    /// what is physically possible given the terrain.
    ///
    /// Usage:
    ///   1. Call Update() each frame while the rope is attached.
    ///   2. Call Draw() to render the rope as a polyline through all wrap points.
    ///   3. Call Clear() when the rope is detached to remove all wrap bodies.
    ///
    /// The caller is responsible for the primary anchor body and joint.
    /// RopeWrapSystem manages only the intermediate wrap-point bodies and joints.
    /// </summary>
    public class RopeWrapSystem
    {
        // ── Tuning ─────────────────────────────────────────────────────────────
        /// <summary>Maximum number of wrap points (prevents infinite wrapping).</summary>
        private const int MaxWrapPoints = 8;

        /// <summary>
        /// Minimum angle change (radians) between rope segments before a wrap point
        /// is considered stable. Prevents oscillation.
        /// </summary>
        private const float MinWrapAngle = 0.15f;

        /// <summary>
        /// Tile size in pixels — used to snap wrap points to tile corners.
        /// </summary>
        private const int TileSize = TileMap.TileSize;

        // ── State ──────────────────────────────────────────────────────────────

        /// <summary>Stack of intermediate wrap-point anchors (meter space).</summary>
        private readonly List<WrapPoint> _wrapPoints = new();

        /// <summary>Reference to the physics world for creating/removing bodies.</summary>
        private readonly AetherWorld _world;

        /// <summary>Reference to the tile map for terrain queries.</summary>
        private TileMap? _tileMap;

        // ── Wrap point data ────────────────────────────────────────────────────

        private struct WrapPoint
        {
            /// <summary>Position in meter space.</summary>
            public Vector2 PositionMeters;
            /// <summary>Position in pixel space (for drawing).</summary>
            public Vector2 PositionPixels;
            /// <summary>Static body at this wrap point.</summary>
            public Body Body;
            /// <summary>RopeJoint from this wrap point to the next anchor (or player).</summary>
            public RopeJoint Joint;
            /// <summary>Maximum length of this segment in meters.</summary>
            public float SegmentLength;
        }

        // ── Constructor ────────────────────────────────────────────────────────

        public RopeWrapSystem(AetherWorld world)
        {
            _world = world;
        }

        // ── Setup ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Set the tile map reference for terrain queries.
        /// Must be called before Update().
        /// </summary>
        public void SetTileMap(TileMap tileMap)
        {
            _tileMap = tileMap;
        }

        // ── Per-frame update ───────────────────────────────────────────────────

        /// <summary>
        /// Update wrap points each frame.
        /// Checks if the rope line from the current anchor to the player passes through
        /// terrain, and adds/removes wrap points accordingly.
        ///
        /// primaryAnchorPixels: the pixel position of the primary (ceiling) anchor
        /// playerPixels:        the player's current pixel position
        /// playerBody:          the player's Aether body (for joint attachment)
        /// remainingLength:     the total remaining rope length in pixels (modified by this method)
        ///
        /// Returns the effective anchor pixel position (last wrap point, or primary anchor if none).
        /// </summary>
        public Vector2 Update(
            Vector2 primaryAnchorPixels,
            Vector2 playerPixels,
            Body playerBody,
            ref float remainingLengthPixels)
        {
            if (_tileMap == null) return primaryAnchorPixels;

            // The current effective anchor is the last wrap point, or the primary anchor
            Vector2 currentAnchorPixels = GetCurrentAnchorPixels(primaryAnchorPixels);

            // ── Check for new wrap: does the rope line hit terrain? ────────────
            bool addedWrap = false;
            if (_wrapPoints.Count < MaxWrapPoints)
            {
                Vector2? wrapPixels = FindTerrainIntersection(currentAnchorPixels, playerPixels);
                if (wrapPixels.HasValue)
                {
                    // Find the best corner to wrap around
                    Vector2 cornerPixels = FindNearestCorner(wrapPixels.Value, currentAnchorPixels, playerPixels);

                    // Only add wrap point if it creates a meaningful angle change
                    float angle = ComputeAngle(currentAnchorPixels, cornerPixels, playerPixels);
                    if (Math.Abs(angle) > MinWrapAngle)
                    {
                        AddWrapPoint(cornerPixels, currentAnchorPixels, playerBody, ref remainingLengthPixels);
                        currentAnchorPixels = cornerPixels;
                        addedWrap = true;
                    }
                }
            }

            // ── Check for unwrap: can we remove the last wrap point? ───────────
            // Use else-if to avoid adding and removing in the same frame.
            if (!addedWrap && _wrapPoints.Count > 0)
            {
                Vector2 prevAnchorPixels = _wrapPoints.Count > 1
                    ? _wrapPoints[_wrapPoints.Count - 2].PositionPixels
                    : primaryAnchorPixels;

                // If the line from the previous anchor to the player is now clear,
                // we can remove the last wrap point (unwrap)
                if (!LineIntersectsTerrain(prevAnchorPixels, playerPixels))
                {
                    RemoveLastWrapPoint(ref remainingLengthPixels);
                    currentAnchorPixels = GetCurrentAnchorPixels(primaryAnchorPixels);
                }
            }

            // ── Bug #3 fix: segment validation pass ────────────────────────────
            // After the wrap/unwrap logic, verify each segment in the chain doesn't
            // intersect terrain. If a segment clips through geometry (e.g. because
            // the player moved diagonally past a corner), insert a new wrap point
            // at the intersection to keep the rope physically accurate.
            if (_wrapPoints.Count < MaxWrapPoints)
            {
                Vector2 segFrom = primaryAnchorPixels;
                for (int i = 0; i < _wrapPoints.Count; i++)
                {
                    Vector2 segTo = _wrapPoints[i].PositionPixels;

                    // Check if this segment passes through terrain
                    Vector2? segHit = FindTerrainIntersection(segFrom, segTo);
                    if (segHit.HasValue)
                    {
                        // Find the best corner to wrap around at this intersection
                        Vector2 cornerPixels = FindNearestCorner(segHit.Value, segFrom, segTo);

                        float angle = ComputeAngle(segFrom, cornerPixels, segTo);
                        if (Math.Abs(angle) > MinWrapAngle)
                        {
                            // Insert a new wrap point before the current index
                            // We need to rebuild the chain: insert at position i
                            InsertWrapPoint(i, cornerPixels, segFrom, playerBody, ref remainingLengthPixels);
                            currentAnchorPixels = GetCurrentAnchorPixels(primaryAnchorPixels);
                            break; // re-validate next frame
                        }
                    }

                    segFrom = segTo;
                }

                // Also validate the last segment (last wrap point → player)
                if (_wrapPoints.Count > 0)
                {
                    Vector2 lastAnchor = _wrapPoints[_wrapPoints.Count - 1].PositionPixels;
                    Vector2? lastHit = FindTerrainIntersection(lastAnchor, playerPixels);
                    if (lastHit.HasValue && _wrapPoints.Count < MaxWrapPoints)
                    {
                        Vector2 cornerPixels = FindNearestCorner(lastHit.Value, lastAnchor, playerPixels);
                        float angle = ComputeAngle(lastAnchor, cornerPixels, playerPixels);
                        if (Math.Abs(angle) > MinWrapAngle)
                        {
                            AddWrapPoint(cornerPixels, lastAnchor, playerBody, ref remainingLengthPixels);
                            currentAnchorPixels = cornerPixels;
                        }
                    }
                }
            }

            return currentAnchorPixels;
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw the rope as a polyline from the primary anchor through all wrap points
        /// to the player position.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, AssetManager assets,
            Vector2 primaryAnchorPixels, Vector2 playerPixels, Color ropeColor)
        {
            Vector2 prev = primaryAnchorPixels;

            foreach (var wp in _wrapPoints)
            {
                DrawLine(spriteBatch, assets, prev, wp.PositionPixels, ropeColor);
                prev = wp.PositionPixels;
            }

            DrawLine(spriteBatch, assets, prev, playerPixels, ropeColor);
        }

        // ── Clear ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Remove all wrap points and their physics bodies/joints.
        /// Call when the rope is detached.
        /// </summary>
        public void Clear()
        {
            for (int i = _wrapPoints.Count - 1; i >= 0; i--)
            {
                var wp = _wrapPoints[i];
                try { _world.Remove(wp.Joint); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RopeWrapSystem] Clear joint: {ex.Message}"); }
                try { _world.Remove(wp.Body); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RopeWrapSystem] Clear body: {ex.Message}"); }
            }
            _wrapPoints.Clear();
        }

        // ── Accessors ──────────────────────────────────────────────────────────

        /// <summary>Number of active wrap points.</summary>
        public int WrapPointCount => _wrapPoints.Count;

        /// <summary>
        /// Get the effective anchor pixel position:
        /// the last wrap point if any exist, otherwise the primary anchor.
        /// </summary>
        public Vector2 GetCurrentAnchorPixels(Vector2 primaryAnchorPixels)
        {
            return _wrapPoints.Count > 0
                ? _wrapPoints[_wrapPoints.Count - 1].PositionPixels
                : primaryAnchorPixels;
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Add a new wrap point at the given pixel position.
        /// Creates a static body and RopeJoint from this point to the player.
        /// Reduces remainingLengthPixels by the distance from the current anchor to this point.
        /// </summary>
        private void AddWrapPoint(Vector2 cornerPixels, Vector2 currentAnchorPixels,
            Body playerBody, ref float remainingLengthPixels)
        {
            float segmentLengthPixels = Vector2.Distance(currentAnchorPixels, cornerPixels);
            float segmentLengthMeters = PhysicsManager.ToMeters(segmentLengthPixels);

            // Reduce remaining rope length
            remainingLengthPixels = Math.Max(10f, remainingLengthPixels - segmentLengthPixels);

            // Create static body at wrap point
            Vector2 cornerMeters = PhysicsManager.ToMeters(cornerPixels);
            var body = _world.CreateBody(cornerMeters, 0f, BodyType.Static);

            // Create RopeJoint from wrap point to player.
            // MaxLength = max(actualDist + slack, theoretical) so the joint never
            // immediately snaps a player who is mid-swing past the wrap corner.
            float newSegmentMeters = PhysicsManager.ToMeters(remainingLengthPixels);
            const float SlackMeters = 0.05f; // ~3 px
            float actualDistMeters = Vector2.Distance(cornerMeters, playerBody.Position);
            var joint = new RopeJoint(body, playerBody, Vector2.Zero, Vector2.Zero, false);
            joint.MaxLength = Math.Max(actualDistMeters + SlackMeters, Math.Max(0.1f, newSegmentMeters));
            _world.Add(joint);

            _wrapPoints.Add(new WrapPoint
            {
                PositionMeters = cornerMeters,
                PositionPixels = cornerPixels,
                Body           = body,
                Joint          = joint,
                SegmentLength  = segmentLengthPixels,
            });
        }

        /// <summary>
        /// Remove the last wrap point, restoring its segment length to the remaining rope.
        /// </summary>
        private void RemoveLastWrapPoint(ref float remainingLengthPixels)
        {
            if (_wrapPoints.Count == 0) return;

            var wp = _wrapPoints[_wrapPoints.Count - 1];
            remainingLengthPixels += wp.SegmentLength;

            try { _world.Remove(wp.Joint); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RopeWrapSystem] Remove joint: {ex.Message}"); }
            try { _world.Remove(wp.Body); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RopeWrapSystem] Remove body: {ex.Message}"); }

            _wrapPoints.RemoveAt(_wrapPoints.Count - 1);
        }

        /// <summary>
        /// Insert a wrap point at the given index in the wrap-point chain.
        /// Used by the segment validation pass to fix rope segments that clip through
        /// terrain after the player moves past a corner.
        ///
        /// This is more complex than AddWrapPoint because we must:
        ///   1. Remove the existing joint from the wrap point currently at 'index'
        ///      (which was connected to the previous anchor).
        ///   2. Create a new wrap point body at 'cornerPixels'.
        ///   3. Create a joint from the previous anchor (or primary) to the new wrap point.
        ///   4. Create a joint from the new wrap point to the existing wrap point at 'index'.
        ///   5. Update the segment lengths accordingly.
        /// </summary>
        private void InsertWrapPoint(int index, Vector2 cornerPixels, Vector2 prevAnchorPixels,
            Body playerBody, ref float remainingLengthPixels)
        {
            if (index < 0 || index >= _wrapPoints.Count) return;

            var existing = _wrapPoints[index];

            // Remove the existing joint (it connected the previous anchor to this wrap point)
            try { _world.Remove(existing.Joint); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RopeWrapSystem] Insert remove joint: {ex.Message}"); }

            // Compute segment lengths
            float seg1LengthPixels = Vector2.Distance(prevAnchorPixels, cornerPixels);
            float seg2LengthPixels = Vector2.Distance(cornerPixels, existing.PositionPixels);

            // Reduce remaining rope length by the new segment
            remainingLengthPixels = Math.Max(10f, remainingLengthPixels - seg1LengthPixels);

            // Create static body at the new corner
            Vector2 cornerMeters = PhysicsManager.ToMeters(cornerPixels);
            var newBody = _world.CreateBody(cornerMeters, 0f, BodyType.Static);

            // Create joint from previous anchor (or primary) to new wrap point.
            // joint1 connects newBody → existing.Body (series chain), NOT to playerBody.
            float seg1Meters = PhysicsManager.ToMeters(seg1LengthPixels);
            var joint1 = new RopeJoint(newBody, existing.Body, Vector2.Zero, Vector2.Zero, false);
            joint1.MaxLength = Math.Max(0.1f, seg1Meters);
            _world.Add(joint1);

            // The existing wrap point's joint now connects from existing.Body to whatever
            // it was already connected to (the next wrap point or the player).
            // We keep the existing joint reference unchanged — it already connects
            // existing.Body → nextAnchor (or playerBody).
            // We only need to update its MaxLength to reflect the new segment length.
            float seg2Meters = PhysicsManager.ToMeters(seg2LengthPixels);
            existing.Joint.MaxLength = Math.Max(0.1f, seg2Meters);

            // Insert the new wrap point
            _wrapPoints.Insert(index, new WrapPoint
            {
                PositionMeters = cornerMeters,
                PositionPixels = cornerPixels,
                Body           = newBody,
                Joint          = joint1,
                SegmentLength  = seg1LengthPixels,
            });
        }

        /// <summary>
        /// Raycast from 'from' to 'to' in pixel space.
        /// Returns the first intersection point with solid terrain, or null if clear.
        /// Uses a simple DDA (Digital Differential Analyzer) tile traversal.
        /// </summary>
        private Vector2? FindTerrainIntersection(Vector2 fromPixels, Vector2 toPixels)
        {
            if (_tileMap == null) return null;

            Vector2 dir = toPixels - fromPixels;
            float length = dir.Length();
            if (length < 1f) return null;

            dir /= length;

            // Bug #3 fix: reduce step size from half-tile to quarter-tile (8px)
            // to reliably detect thin (1-tile) walls at any angle.
            float stepSize = TileSize * 0.25f; // 8px — quarter-tile steps
            int   steps    = (int)(length / stepSize) + 2; // +2 safety margin

            for (int i = 1; i < steps; i++)
            {
                Vector2 samplePos = fromPixels + dir * (i * stepSize);
                int tx = (int)(samplePos.X / TileSize);
                int ty = (int)(samplePos.Y / TileSize);

                if (TileProperties.IsSolid(_tileMap.GetTile(tx, ty)))
                {
                    // Skip fully-interior tiles — all 4 cardinal neighbors are solid,
                    // so there is no accessible corner for the rope to wrap around.
                    bool allNeighborsSolid =
                        TileProperties.IsSolid(_tileMap.GetTile(tx - 1, ty)) &&
                        TileProperties.IsSolid(_tileMap.GetTile(tx + 1, ty)) &&
                        TileProperties.IsSolid(_tileMap.GetTile(tx,     ty - 1)) &&
                        TileProperties.IsSolid(_tileMap.GetTile(tx,     ty + 1));
                    if (!allNeighborsSolid)
                        return samplePos;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns true if the line from 'from' to 'to' passes through any solid terrain tile.
        /// </summary>
        private bool LineIntersectsTerrain(Vector2 fromPixels, Vector2 toPixels)
        {
            return FindTerrainIntersection(fromPixels, toPixels).HasValue;
        }

        /// <summary>
        /// Given a terrain intersection point, find the best tile corner that the
        /// rope should wrap around. Uses direction-aware logic: the corner is chosen
        /// based on which side of the tile the rope enters (from anchor) and which
        /// side it exits (toward player), ensuring the rope wraps around the correct
        /// corner of the intersected tile.
        /// </summary>
        private Vector2 FindNearestCorner(Vector2 intersectionPixels,
            Vector2 anchorPixels, Vector2 playerPixels)
        {
            // Snap to the intersected tile
            int tx = (int)(intersectionPixels.X / TileSize);
            int ty = (int)(intersectionPixels.Y / TileSize);

            // Determine which side of the tile the rope enters from (anchor side)
            // and which side it exits to (player side).
            Vector2 anchorDir = anchorPixels - new Vector2(tx * TileSize + TileSize / 2f, ty * TileSize + TileSize / 2f);
            Vector2 playerDir = playerPixels - new Vector2(tx * TileSize + TileSize / 2f, ty * TileSize + TileSize / 2f);

            // Determine entry side (the side the anchor is on)
            float entryAx = Math.Abs(anchorDir.X);
            float entryAy = Math.Abs(anchorDir.Y);
            bool entryFromLeft   = anchorDir.X < 0 && entryAx > entryAy;
            bool entryFromRight  = anchorDir.X > 0 && entryAx > entryAy;
            bool entryFromTop    = anchorDir.Y < 0 && entryAy > entryAx;
            bool entryFromBottom = anchorDir.Y > 0 && entryAy > entryAx;

            // Determine exit side (the side the player is on)
            float exitAx = Math.Abs(playerDir.X);
            float exitAy = Math.Abs(playerDir.Y);
            bool exitToLeft   = playerDir.X < 0 && exitAx > exitAy;
            bool exitToRight  = playerDir.X > 0 && exitAx > exitAy;
            bool exitToTop    = playerDir.Y < 0 && exitAy > exitAx;
            bool exitToBottom = playerDir.Y > 0 && exitAy > exitAx;

            // The four corners of the intersected tile
            Vector2 tl = new Vector2(tx       * TileSize, ty       * TileSize); // top-left
            Vector2 tr = new Vector2((tx + 1) * TileSize, ty       * TileSize); // top-right
            Vector2 bl = new Vector2(tx       * TileSize, (ty + 1) * TileSize); // bottom-left
            Vector2 br = new Vector2((tx + 1) * TileSize, (ty + 1) * TileSize); // bottom-right

            // Collect candidate corners from the entry/exit side detection
            var candidates = new List<Vector2>();

            if (entryFromLeft && exitToTop)       candidates.Add(tl);
            else if (entryFromLeft && exitToBottom) candidates.Add(bl);
            else if (entryFromRight && exitToTop)    candidates.Add(tr);
            else if (entryFromRight && exitToBottom) candidates.Add(br);
            else if (entryFromTop && exitToLeft)     candidates.Add(tl);
            else if (entryFromTop && exitToRight)    candidates.Add(tr);
            else if (entryFromBottom && exitToLeft)  candidates.Add(bl);
            else if (entryFromBottom && exitToRight) candidates.Add(br);
            else
            {
                // Fallback: all four corners are candidates
                candidates.Add(tl);
                candidates.Add(tr);
                candidates.Add(bl);
                candidates.Add(br);
            }

            // Filter candidates: a valid corner must have at least one empty neighbor
            // AND both resulting segments (anchor→corner and corner→player) must be
            // clear of terrain. This prevents selecting a corner that would still clip.
            Vector2 selectedCorner = intersectionPixels;
            float bestDist = float.MaxValue;

            foreach (var corner in candidates)
            {
                int ctx = (int)(corner.X / TileSize);
                int cty = (int)(corner.Y / TileSize);

                // Corner is valid if at least one adjacent tile is empty
                bool hasEmptyNeighbor = false;
                foreach (var (dx, dy) in new[] { (-1,0),(1,0),(0,-1),(0,1) })
                {
                    if (_tileMap != null &&
                        !TileProperties.IsSolid(_tileMap.GetTile(ctx + dx, cty + dy)))
                    {
                        hasEmptyNeighbor = true;
                        break;
                    }
                }
                if (!hasEmptyNeighbor) continue;

                // Validate that both segments around this corner are clear of terrain.
                // Use a small inset (2px) from the corner to avoid self-intersection
                // with the tile we're wrapping around.
                Vector2 inset = corner - anchorPixels;
                float insetLen = inset.Length();
                Vector2 cornerFrom = corner;
                if (insetLen > 2f)
                {
                    inset /= insetLen;
                    cornerFrom = corner + inset * 2f;
                }

                Vector2 toPlayer = playerPixels - corner;
                float toPlayerLen = toPlayer.Length();
                Vector2 cornerTo = corner;
                if (toPlayerLen > 2f)
                {
                    toPlayer /= toPlayerLen;
                    cornerTo = corner + toPlayer * 2f;
                }

                bool seg1Clear = !LineIntersectsTerrain(anchorPixels, cornerFrom);
                bool seg2Clear = !LineIntersectsTerrain(cornerTo, playerPixels);

                if (!seg1Clear || !seg2Clear) continue;

                float dist = Vector2.Distance(corner, intersectionPixels);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    selectedCorner = corner;
                }
            }

            // If no candidate passed validation (shouldn't happen in normal cases),
            // fall back to the raw side-detection result or the intersection point.
            if (bestDist == float.MaxValue)
            {
                // Use the first candidate as a last resort
                if (candidates.Count > 0)
                    selectedCorner = candidates[0];
            }

            // Offset slightly away from the tile to avoid re-intersection
            Vector2 offsetDir = playerPixels - selectedCorner;
            if (offsetDir.LengthSquared() > 0.01f)
            {
                offsetDir.Normalize();
                selectedCorner += offsetDir * 4f; // 4px offset (increased from 2px)
            }

            return selectedCorner;
        }

        /// <summary>
        /// Compute the signed angle at 'vertex' between the vectors from 'vertex' to 'a'
        /// and from 'vertex' to 'b'. Used to detect meaningful wrap angles.
        /// </summary>
        private static float ComputeAngle(Vector2 a, Vector2 vertex, Vector2 b)
        {
            Vector2 va = a - vertex;
            Vector2 vb = b - vertex;

            if (va.LengthSquared() < 0.001f || vb.LengthSquared() < 0.001f)
                return 0f;

            va.Normalize();
            vb.Normalize();

            float dot = MathHelper.Clamp(Vector2.Dot(va, vb), -1f, 1f);
            return (float)Math.Acos(dot);
        }

        private static void DrawLine(SpriteBatch sb, AssetManager assets,
            Vector2 a, Vector2 b, Color color)
        {
            Vector2 diff   = b - a;
            float   length = diff.Length();
            if (length < 1f) return;
            float angle = (float)Math.Atan2(diff.Y, diff.X);
            sb.Draw(assets.Pixel,
                new Rectangle((int)a.X, (int)a.Y, (int)length, 2),
                null, color, angle, Vector2.Zero, SpriteEffects.None, 0f);
        }
    }
}
