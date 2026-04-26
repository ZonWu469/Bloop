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

            // Create RopeJoint from wrap point to player
            float newSegmentMeters = PhysicsManager.ToMeters(remainingLengthPixels);
            var joint = new RopeJoint(body, playerBody, Vector2.Zero, Vector2.Zero, false);
            joint.MaxLength = Math.Max(0.1f, newSegmentMeters);
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

            // DDA ray march through tiles
            float stepSize = TileSize * 0.5f; // half-tile steps for accuracy
            int   steps    = (int)(length / stepSize) + 1;

            for (int i = 1; i < steps; i++)
            {
                Vector2 samplePos = fromPixels + dir * (i * stepSize);
                int tx = (int)(samplePos.X / TileSize);
                int ty = (int)(samplePos.Y / TileSize);

                if (TileProperties.IsSolid(_tileMap.GetTile(tx, ty)))
                    return samplePos;
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
        /// Given a terrain intersection point, find the nearest tile corner that the
        /// rope should wrap around. The corner is chosen to be on the side of the
        /// intersection that faces the anchor (so the rope wraps correctly).
        /// </summary>
        private Vector2 FindNearestCorner(Vector2 intersectionPixels,
            Vector2 anchorPixels, Vector2 playerPixels)
        {
            // Snap to the nearest tile corner
            int tx = (int)(intersectionPixels.X / TileSize);
            int ty = (int)(intersectionPixels.Y / TileSize);

            // The four corners of the intersected tile
            var corners = new Vector2[]
            {
                new Vector2(tx       * TileSize, ty       * TileSize), // top-left
                new Vector2((tx + 1) * TileSize, ty       * TileSize), // top-right
                new Vector2(tx       * TileSize, (ty + 1) * TileSize), // bottom-left
                new Vector2((tx + 1) * TileSize, (ty + 1) * TileSize), // bottom-right
            };

            // Find the corner that is closest to the intersection point AND
            // is in empty space (not inside solid terrain)
            Vector2 bestCorner = intersectionPixels;
            float   bestDist   = float.MaxValue;

            foreach (var corner in corners)
            {
                // Check if this corner is in empty space
                int ctx = (int)(corner.X / TileSize);
                int cty = (int)(corner.Y / TileSize);

                // A corner is valid if at least one adjacent tile is empty
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

                float dist = Vector2.Distance(corner, intersectionPixels);
                if (dist < bestDist)
                {
                    bestDist   = dist;
                    bestCorner = corner;
                }
            }

            // Offset slightly away from the tile to avoid re-intersection
            Vector2 toPlayer = playerPixels - bestCorner;
            if (toPlayer.LengthSquared() > 0.01f)
            {
                toPlayer.Normalize();
                bestCorner += toPlayer * 2f; // 2px offset
            }

            return bestCorner;
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
