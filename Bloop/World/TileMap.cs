using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using nkast.Aether.Physics2D.Dynamics;
using Bloop.Core;
using Bloop.Physics;
using Bloop.Rendering;
using AetherWorld = nkast.Aether.Physics2D.Dynamics.World;

namespace Bloop.World
{
    /// <summary>
    /// 2D grid of TileType values. Handles:
    ///   - Tile storage and coordinate lookup
    ///   - Generating Aether static edge-chain bodies from tile boundaries
    ///   - Rendering tiles as placeholder colored rectangles (viewport-culled)
    /// </summary>
    public class TileMap
    {
        // ── Constants ──────────────────────────────────────────────────────────
        /// <summary>Tile size in pixels. Each tile is TileSize × TileSize.</summary>
        public const int TileSize = 32;

        // ── Grid ───────────────────────────────────────────────────────────────
        public int Width  { get; }
        public int Height { get; }

        private readonly TileType[,] _tiles;

        /// <summary>
        /// Per-tile damage level (0 = pristine, 255 = about to collapse).
        /// Driven by the earthquake system — cracked tiles render with overlay lines
        /// and are more likely to be destroyed by subsequent quakes.
        /// </summary>
        private readonly byte[,] _damage;

        // ── Physics bodies ─────────────────────────────────────────────────────
        private readonly List<Body> _terrainBodies = new();

        // ── Neighbor cache (created lazily on first Draw) ──────────────────────
        private TileNeighborCache? _neighborCache;

        // ── Constructor ────────────────────────────────────────────────────────
        public TileMap(int width, int height)
        {
            Width  = width;
            Height = height;
            _tiles  = new TileType[width, height];
            _damage = new byte[width, height];
        }

        // ── Tile access ────────────────────────────────────────────────────────

        /// <summary>Get tile type at grid coordinates. Returns Empty if out of bounds.</summary>
        public TileType GetTile(int tx, int ty)
        {
            if (tx < 0 || tx >= Width || ty < 0 || ty >= Height)
                return TileType.Empty;
            return _tiles[tx, ty];
        }

        /// <summary>Set tile type at grid coordinates. Ignores out-of-bounds.</summary>
        public void SetTile(int tx, int ty, TileType type)
        {
            if (tx < 0 || tx >= Width || ty < 0 || ty >= Height) return;
            _tiles[tx, ty] = type;
            if (type == TileType.Empty) _damage[tx, ty] = 0;
        }

        /// <summary>Damage level [0..255] of the tile at (tx, ty). 0 = pristine.</summary>
        public byte GetDamage(int tx, int ty)
        {
            if (tx < 0 || tx >= Width || ty < 0 || ty >= Height) return 0;
            return _damage[tx, ty];
        }

        /// <summary>Add damage to a tile (saturating at 255). Safe out-of-bounds.</summary>
        public void AddDamage(int tx, int ty, int amount)
        {
            if (tx < 0 || tx >= Width || ty < 0 || ty >= Height) return;
            int v = _damage[tx, ty] + amount;
            _damage[tx, ty] = (byte)System.Math.Min(255, System.Math.Max(0, v));
        }

        /// <summary>Get tile at a pixel-space position.</summary>
        public TileType GetTileAtPixel(float px, float py)
            => GetTile((int)(px / TileSize), (int)(py / TileSize));

        /// <summary>Convert tile grid coords to pixel-space top-left corner.</summary>
        public static Vector2 TileToPixel(int tx, int ty)
            => new Vector2(tx * TileSize, ty * TileSize);

        /// <summary>Convert tile grid coords to pixel-space center.</summary>
        public static Vector2 TileCenter(int tx, int ty)
            => new Vector2(tx * TileSize + TileSize / 2f, ty * TileSize + TileSize / 2f);

        /// <summary>Total pixel width of the map.</summary>
        public int PixelWidth  => Width  * TileSize;
        /// <summary>Total pixel height of the map.</summary>
        public int PixelHeight => Height * TileSize;

        // ── Physics body generation ────────────────────────────────────────────

        /// <summary>
        /// Generate Aether static bodies for all solid tiles.
        /// Uses edge-chain bodies along contiguous horizontal runs for efficiency.
        /// Call once after the tile grid is populated.
        /// </summary>
        public void GeneratePhysicsBodies(AetherWorld world)
        {
            // Remove any existing bodies
            foreach (var body in _terrainBodies)
                world.Remove(body);
            _terrainBodies.Clear();

            // Generate horizontal edge chains for solid tile tops (ground surfaces)
            GenerateHorizontalEdges(world);

            // Generate vertical edge chains for solid tile sides (walls)
            GenerateVerticalEdges(world);

            // Generate slope bodies
            GenerateSlopeBodies(world);

            // Generate platform bodies (one-way)
            GeneratePlatformBodies(world);

            // Generate climbable surface bodies
            GenerateClimbableBodies(world);
        }

        /// <summary>Remove all physics bodies from the world.</summary>
        public void ClearPhysicsBodies(AetherWorld world)
        {
            foreach (var body in _terrainBodies)
                world.Remove(body);
            _terrainBodies.Clear();
        }

        // ── Draw ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw all visible tiles as geometric shapes using TileRenderer.
        /// Only draws tiles within the camera's visible bounds (viewport culling).
        /// Neighbor bitmasks are refreshed once per frame before the draw loop.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, AssetManager assets, Rectangle visibleBounds)
        {
            // Convert visible bounds to tile range
            int minTx = System.Math.Max(0, visibleBounds.Left  / TileSize - 1);
            int maxTx = System.Math.Min(Width  - 1, visibleBounds.Right  / TileSize + 1);
            int minTy = System.Math.Max(0, visibleBounds.Top   / TileSize - 1);
            int maxTy = System.Math.Min(Height - 1, visibleBounds.Bottom / TileSize + 1);

            // Lazy-create the neighbor cache (sized to the full map)
            if (_neighborCache == null)
                _neighborCache = new TileNeighborCache(Width, Height);

            // Refresh neighbor masks for the visible region this frame
            _neighborCache.Refresh(this, minTx, maxTx, minTy, maxTy);

            // Draw each visible tile via TileRenderer
            for (int ty = minTy; ty <= maxTy; ty++)
            {
                for (int tx = minTx; tx <= maxTx; tx++)
                {
                    TileType type = _tiles[tx, ty];
                    if (!TileProperties.IsVisible(type)) continue;

                    TileRenderer.DrawTile(spriteBatch, assets, type, tx, ty, _neighborCache);

                    byte dmg = _damage[tx, ty];
                    if (dmg > 0)
                        TileRenderer.DrawDamageOverlay(spriteBatch, assets, tx, ty, dmg);
                }
            }
        }

        // ── Private physics helpers ────────────────────────────────────────────

        private void GenerateHorizontalEdges(AetherWorld world)
        {
            // For each row, find runs of solid tiles and create top-edge chains
            for (int ty = 0; ty < Height; ty++)
            {
                int runStart = -1;
                for (int tx = 0; tx <= Width; tx++)
                {
                    bool solid = tx < Width && TileProperties.IsSolid(GetTile(tx, ty)) &&
                                 !TileProperties.IsSolid(GetTile(tx, ty - 1)); // exposed top

                    if (solid && runStart < 0)
                        runStart = tx;
                    else if (!solid && runStart >= 0)
                    {
                        // Create top edge from runStart to tx
                        var verts = new List<Vector2>
                        {
                            new Vector2(runStart * TileSize, ty * TileSize),
                            new Vector2(tx       * TileSize, ty * TileSize)
                        };
                        var body = BodyFactory.CreateTerrainChain(world, verts);
                        _terrainBodies.Add(body);
                        runStart = -1;
                    }
                }
            }

            // Bottom edges (floor of solid tiles)
            for (int ty = 0; ty < Height; ty++)
            {
                int runStart = -1;
                for (int tx = 0; tx <= Width; tx++)
                {
                    bool solid = tx < Width && TileProperties.IsSolid(GetTile(tx, ty)) &&
                                 !TileProperties.IsSolid(GetTile(tx, ty + 1)); // exposed bottom

                    if (solid && runStart < 0)
                        runStart = tx;
                    else if (!solid && runStart >= 0)
                    {
                        // Reversed (right-to-left) so the edge normal faces downward,
                        // allowing the grapple hook to anchor to ceilings from below.
                        var verts = new List<Vector2>
                        {
                            new Vector2(tx       * TileSize, (ty + 1) * TileSize),
                            new Vector2(runStart * TileSize, (ty + 1) * TileSize)
                        };
                        var body = BodyFactory.CreateTerrainChain(world, verts);
                        _terrainBodies.Add(body);
                        runStart = -1;
                    }
                }
            }
        }

        private void GenerateVerticalEdges(AetherWorld world)
        {
            // Left edges
            for (int tx = 0; tx < Width; tx++)
            {
                int runStart = -1;
                for (int ty = 0; ty <= Height; ty++)
                {
                    bool solid = ty < Height && TileProperties.IsSolid(GetTile(tx, ty)) &&
                                 !TileProperties.IsSolid(GetTile(tx - 1, ty)); // exposed left

                    if (solid && runStart < 0)
                        runStart = ty;
                    else if (!solid && runStart >= 0)
                    {
                        var verts = new List<Vector2>
                        {
                            new Vector2(tx * TileSize, runStart * TileSize),
                            new Vector2(tx * TileSize, ty       * TileSize)
                        };
                        var body = BodyFactory.CreateTerrainChain(world, verts);
                        _terrainBodies.Add(body);
                        runStart = -1;
                    }
                }
            }

            // Right edges
            for (int tx = 0; tx < Width; tx++)
            {
                int runStart = -1;
                for (int ty = 0; ty <= Height; ty++)
                {
                    bool solid = ty < Height && TileProperties.IsSolid(GetTile(tx, ty)) &&
                                 !TileProperties.IsSolid(GetTile(tx + 1, ty)); // exposed right

                    if (solid && runStart < 0)
                        runStart = ty;
                    else if (!solid && runStart >= 0)
                    {
                        var verts = new List<Vector2>
                        {
                            new Vector2((tx + 1) * TileSize, runStart * TileSize),
                            new Vector2((tx + 1) * TileSize, ty       * TileSize)
                        };
                        var body = BodyFactory.CreateTerrainChain(world, verts);
                        _terrainBodies.Add(body);
                        runStart = -1;
                    }
                }
            }
        }

        private void GenerateSlopeBodies(AetherWorld world)
        {
            for (int ty = 0; ty < Height; ty++)
            {
                for (int tx = 0; tx < Width; tx++)
                {
                    TileType type = GetTile(tx, ty);
                    if (!TileProperties.IsSlope(type)) continue;

                    float x0 = tx * TileSize;
                    float y0 = ty * TileSize;
                    float x1 = x0 + TileSize;
                    float y1 = y0 + TileSize;

                    List<Vector2> verts;
                    if (type == TileType.SlopeRight)
                    {
                        // Diagonal from top-left to bottom-right
                        verts = new List<Vector2>
                        {
                            new Vector2(x0, y0),
                            new Vector2(x1, y1)
                        };
                    }
                    else // SlopeLeft
                    {
                        // Diagonal from top-right to bottom-left
                        verts = new List<Vector2>
                        {
                            new Vector2(x1, y0),
                            new Vector2(x0, y1)
                        };
                    }

                    var body = BodyFactory.CreateTerrainChain(world, verts);
                    _terrainBodies.Add(body);
                }
            }
        }

        private void GeneratePlatformBodies(AetherWorld world)
        {
            for (int ty = 0; ty < Height; ty++)
            {
                int runStart = -1;
                for (int tx = 0; tx <= Width; tx++)
                {
                    bool isPlatform = tx < Width && GetTile(tx, ty) == TileType.Platform;

                    if (isPlatform && runStart < 0)
                        runStart = tx;
                    else if (!isPlatform && runStart >= 0)
                    {
                        // Platform top edge only
                        var verts = new List<Vector2>
                        {
                            new Vector2(runStart * TileSize, ty * TileSize),
                            new Vector2(tx       * TileSize, ty * TileSize)
                        };
                        var body = BodyFactory.CreateTerrainChain(world, verts);
                        // Override collision category to Platform
                        foreach (var fixture in body.FixtureList)
                            fixture.CollisionCategories = Bloop.Physics.CollisionCategories.Platform;
                        _terrainBodies.Add(body);
                        runStart = -1;
                    }
                }
            }
        }

        private void GenerateClimbableBodies(AetherWorld world)
        {
            for (int ty = 0; ty < Height; ty++)
            {
                for (int tx = 0; tx < Width; tx++)
                {
                    if (GetTile(tx, ty) != TileType.Climbable) continue;

                    var body = BodyFactory.CreateStaticRect(world,
                        TileCenter(tx, ty), TileSize, TileSize,
                        Bloop.Physics.CollisionCategories.Climbable);
                    _terrainBodies.Add(body);
                }
            }
        }

    }
}
