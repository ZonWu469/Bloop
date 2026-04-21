using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Entities;
using Bloop.Objects;
using Bloop.World;

namespace Bloop.UI
{
    /// <summary>
    /// Screen-space tooltip that names the world object or tile under the mouse.
    /// Drawn in the HUD pass (no camera transform). Uses Camera.ScreenToWorld
    /// to pick a world position and tests first against level objects, then
    /// falls back to the tile grid.
    ///
    /// Task 11: Expanded with entity support, rich multi-line format, danger icons,
    /// and color-coded panel borders (red=hazard, green=beneficial, cyan=controllable).
    /// </summary>
    public class HoverTooltip
    {
        private string? _name;
        private string? _effect;
        private string? _statsLine;
        private string? _actionHint;
        private bool    _isHazard;
        private bool    _isControllable;
        private bool    _isBeneficial;
        private Vector2 _mouseScreen;

        // ── Panel colors ───────────────────────────────────────────────────────
        private static readonly Color PanelBg          = new Color(10, 14, 20, 230);
        private static readonly Color PanelBorderNormal = new Color(80, 100, 120);
        private static readonly Color PanelBorderHazard = new Color(180, 50, 50);
        private static readonly Color PanelBorderGood   = new Color(50, 160, 80);
        private static readonly Color PanelBorderEntity = new Color(50, 160, 200);
        private static readonly Color TitleColor        = new Color(220, 220, 200);
        private static readonly Color TitleHazard       = new Color(255, 120, 100);
        private static readonly Color TitleEntity       = new Color(120, 210, 240);
        private static readonly Color EffectColor       = new Color(150, 170, 190);
        private static readonly Color StatsColor        = new Color(220, 100, 80);
        private static readonly Color HintColor         = new Color(80, 200, 220);

        private const int PaddingX = 8;
        private const int PaddingY = 6;
        private const float TitleScale  = 0.8f;
        private const float EffectScale = 0.7f;
        private const float StatsScale  = 0.65f;
        private const float HintScale   = 0.65f;
        private const int LineGap = 3;

        // ── Object info dictionary (Task 11: expanded) ─────────────────────────
        private static readonly Dictionary<Type, (string name, string effect, bool hazard, bool beneficial)> ObjectInfo = new()
        {
            [typeof(BlindFish)]            = ("Blind Fish",          "Heals 30 HP. 30% chance of poison (5 dmg + 3s stun).", false, true),
            [typeof(CaveLichen)]           = ("Cave Lichen",         "Heals 20 HP. 30% chance of poison (10 dmg + 2s stun).", false, true),
            [typeof(VentFlower)]           = ("Vent Flower",         "Stand 5s to refill breath and lantern fuel.", false, true),
            [typeof(GlowVine)]             = ("Glow Vine",           "Emits soft ambient light. Illuminates with lantern.", false, false),
            [typeof(StunDamageObject)]     = ("Spiked Growth",       "Hazard: 10 damage + 2s stun on contact.", true, false),
            [typeof(DisappearingPlatform)] = ("Crumbling Platform",  "Collapses shortly after being stepped on.", false, false),
            [typeof(RootClump)]            = ("Root Clump",          "Tangled roots — slows movement.", false, false),
            [typeof(IonStone)]             = ("Ion Stone",           "Emits electric arcs. Provides ambient light.", false, false),
            [typeof(PhosphorMoss)]         = ("Phosphor Moss",       "Bioluminescent moss. Provides soft green light.", false, false),
            [typeof(CrystalCluster)]       = ("Crystal Cluster",     "Resonating crystals. Provides colored light.", false, false),
            [typeof(FallingStalactite)]    = ("Stalactite",          "Fragile! Falls when disturbed.", true, false),
            [typeof(FallingRubble)]        = ("Falling Rubble",      "Debris from earthquake activity.", true, false),
            [typeof(ResonanceShard)]       = ("Resonance Shard",     "Collect all shards to open the exit.", false, true),
            [typeof(FlareObject)]          = ("Flare",               "Temporary light source. Burns out after 30s.", false, false),
            [typeof(ClimbableSurface)]     = ("Climbable Surface",   "Hold C to climb.", false, false),
            [typeof(DominoPlatformChain)]  = ("Chain Platform",      "Triggers adjacent platforms when stepped on.", false, false),
        };

        public void Update(InputManager input, Camera camera, Level level)
        {
            _mouseScreen    = input.GetMouseWorldPosition();
            Vector2 worldMouse = camera.ScreenToWorld(_mouseScreen);

            _name           = null;
            _effect         = null;
            _statsLine      = null;
            _actionHint     = null;
            _isHazard       = false;
            _isControllable = false;
            _isBeneficial   = false;

            // Priority 0.5: controllable entities (checked before world objects)
            foreach (var obj in level.Objects)
            {
                if (obj is ControllableEntity entity && !entity.IsDestroyed)
                {
                    var b = entity.GetBounds();
                    if (!b.IsEmpty && b.Contains((int)worldMouse.X, (int)worldMouse.Y))
                    {
                        var (desc, hint) = entity.GetTooltipInfo();
                        _name           = (entity.DamagesPlayerOnContact ? "! " : "") + entity.DisplayName;
                        _effect         = desc;
                        _statsLine      = entity.DamagesPlayerOnContact
                            ? $"Damage: {entity.ContactDamage}  Stun: {entity.ContactStunDuration:0.0}s"
                            : null;
                        _actionHint     = hint;
                        _isHazard       = entity.DamagesPlayerOnContact;
                        _isControllable = true;
                        return;
                    }
                }
            }

            // Priority 1: world objects
            foreach (var obj in level.Objects)
            {
                if (!obj.IsActive) continue;
                var b = obj.GetBounds();
                if (b.IsEmpty) continue;
                if (b.Contains((int)worldMouse.X, (int)worldMouse.Y))
                {
                    if (ObjectInfo.TryGetValue(obj.GetType(), out var info))
                    {
                        _name         = info.name;
                        _effect       = info.effect;
                        _isHazard     = info.hazard;
                        _isBeneficial = info.beneficial;
                        return;
                    }
                }
            }

            // Priority 2: tile under cursor
            int tx = (int)(worldMouse.X / TileMap.TileSize);
            int ty = (int)(worldMouse.Y / TileMap.TileSize);
            if (tx < 0 || ty < 0 || tx >= level.TileMap.Width || ty >= level.TileMap.Height)
                return;

            var tile = level.TileMap.GetTile(tx, ty);
            switch (tile)
            {
                case TileType.Solid:
                    _name = "Rock"; _effect = "Solid cave rock."; break;
                case TileType.Platform:
                    _name = "Ledge"; _effect = "One-way platform — pass through from below."; break;
                case TileType.Climbable:
                    _name = "Vine Wall"; _effect = "Hold C near to climb."; break;
                case TileType.SlopeLeft:
                case TileType.SlopeRight:
                    _name = "Slope"; _effect = "Slide down for speed."; break;
            }
        }

        public void Draw(SpriteBatch spriteBatch, AssetManager assets, int screenWidth, int screenHeight)
        {
            if (_name == null) return;
            if (assets.GameFont == null) return;

            // Measure all lines
            Vector2 titleSize  = assets.GameFont.MeasureString(_name)  * TitleScale;
            Vector2 effectSize = _effect    != null ? assets.GameFont.MeasureString(_effect)    * EffectScale : Vector2.Zero;
            Vector2 statsSize  = _statsLine != null ? assets.GameFont.MeasureString(_statsLine) * StatsScale  : Vector2.Zero;
            Vector2 hintSize   = _actionHint!= null ? assets.GameFont.MeasureString(_actionHint)* HintScale   : Vector2.Zero;

            float maxW = Math.Max(titleSize.X, Math.Max(effectSize.X, Math.Max(statsSize.X, hintSize.X)));
            float totalH = titleSize.Y
                + (_effect     != null ? LineGap + effectSize.Y  : 0)
                + (_statsLine  != null ? LineGap + statsSize.Y   : 0)
                + (_actionHint != null ? LineGap + hintSize.Y    : 0);

            int w = (int)Math.Ceiling(maxW) + PaddingX * 2;
            int h = (int)Math.Ceiling(totalH) + PaddingY * 2;

            int x = (int)_mouseScreen.X + 16;
            int y = (int)_mouseScreen.Y + 16;

            if (x + w > screenWidth)  x = screenWidth  - w - 4;
            if (y + h > screenHeight) y = screenHeight - h - 4;
            if (x < 4) x = 4;
            if (y < 4) y = 4;

            var rect = new Rectangle(x, y, w, h);
            assets.DrawRect(spriteBatch, rect, PanelBg);

            // Color-coded border
            Color border = _isHazard       ? PanelBorderHazard
                         : _isControllable ? PanelBorderEntity
                         : _isBeneficial   ? PanelBorderGood
                         : PanelBorderNormal;
            assets.DrawRectOutline(spriteBatch, rect, border, 1);

            // Title
            Color titleCol = _isHazard ? TitleHazard : _isControllable ? TitleEntity : TitleColor;
            assets.DrawString(spriteBatch, _name,
                new Vector2(x + PaddingX, y + PaddingY), titleCol, TitleScale);

            float curY = y + PaddingY + titleSize.Y;

            // Effect line
            if (_effect != null)
            {
                curY += LineGap;
                assets.DrawString(spriteBatch, _effect,
                    new Vector2(x + PaddingX, curY), EffectColor, EffectScale);
                curY += effectSize.Y;
            }

            // Stats line (red for damage info)
            if (_statsLine != null)
            {
                curY += LineGap;
                assets.DrawString(spriteBatch, _statsLine,
                    new Vector2(x + PaddingX, curY), StatsColor, StatsScale);
                curY += statsSize.Y;
            }

            // Action hint (cyan)
            if (_actionHint != null)
            {
                curY += LineGap;
                assets.DrawString(spriteBatch, _actionHint,
                    new Vector2(x + PaddingX, curY), HintColor, HintScale);
            }
        }
    }
}
