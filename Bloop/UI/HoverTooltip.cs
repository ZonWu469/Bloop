using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Objects;
using Bloop.World;

namespace Bloop.UI
{
    /// <summary>
    /// Screen-space tooltip that names the world object or tile under the mouse.
    /// Drawn in the HUD pass (no camera transform). Uses Camera.ScreenToWorld
    /// to pick a world position and tests first against level objects, then
    /// falls back to the tile grid.
    /// </summary>
    public class HoverTooltip
    {
        private string? _name;
        private string? _effect;
        private Vector2 _mouseScreen;

        private static readonly Color PanelBg     = new Color(10, 14, 20, 230);
        private static readonly Color PanelBorder = new Color(80, 100, 120);
        private static readonly Color TitleColor  = new Color(220, 220, 200);
        private static readonly Color EffectColor = new Color(150, 170, 190);

        private const int PaddingX = 8;
        private const int PaddingY = 6;
        private const float TitleScale  = 0.8f;
        private const float EffectScale = 0.7f;
        private const int LineGap = 4;

        private static readonly Dictionary<Type, (string name, string effect)> ObjectInfo = new()
        {
            [typeof(BlindFish)]             = ("Blind Fish",         "Heals 30 HP. 30% chance of poison (5 dmg + 3s stun)."),
            [typeof(CaveLichen)]            = ("Cave Lichen",        "Heals 20 HP. 30% chance of poison (10 dmg + 2s stun)."),
            [typeof(VentFlower)]            = ("Vent Flower",        "Stand 5s to refill breath and lantern fuel."),
            [typeof(GlowVine)]              = ("Glow Vine",          "Emits soft ambient light."),
            [typeof(StunDamageObject)]      = ("Spiked Growth",      "Hazard: 10 damage + 2s stun on contact."),
            [typeof(DisappearingPlatform)] = ("Crumbling Platform", "Collapses shortly after being stepped on."),
            [typeof(RootClump)]             = ("Root Clump",         "Tangled roots — slows movement."),
        };

        public void Update(InputManager input, Camera camera, Level level)
        {
            _mouseScreen = input.GetMouseWorldPosition(); // virtual-resolution coords
            Vector2 worldMouse = camera.ScreenToWorld(_mouseScreen);

            _name = null;
            _effect = null;

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
                        _name = info.name;
                        _effect = info.effect;
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

            Vector2 titleSize  = assets.GameFont.MeasureString(_name) * TitleScale;
            Vector2 effectSize = _effect != null
                ? assets.GameFont.MeasureString(_effect) * EffectScale
                : Vector2.Zero;

            int textW = (int)Math.Ceiling(Math.Max(titleSize.X, effectSize.X));
            int textH = (int)Math.Ceiling(titleSize.Y + (_effect != null ? LineGap + effectSize.Y : 0));

            int w = textW + PaddingX * 2;
            int h = textH + PaddingY * 2;

            int x = (int)_mouseScreen.X + 16;
            int y = (int)_mouseScreen.Y + 16;

            // Clamp inside virtual viewport
            if (x + w > screenWidth)  x = screenWidth  - w - 4;
            if (y + h > screenHeight) y = screenHeight - h - 4;
            if (x < 4) x = 4;
            if (y < 4) y = 4;

            var rect = new Rectangle(x, y, w, h);
            assets.DrawRect(spriteBatch, rect, PanelBg);
            assets.DrawRectOutline(spriteBatch, rect, PanelBorder, 1);

            assets.DrawString(spriteBatch, _name,
                new Vector2(x + PaddingX, y + PaddingY), TitleColor, TitleScale);

            if (_effect != null)
            {
                assets.DrawString(spriteBatch, _effect,
                    new Vector2(x + PaddingX, y + PaddingY + titleSize.Y + LineGap),
                    EffectColor, EffectScale);
            }
        }
    }
}
