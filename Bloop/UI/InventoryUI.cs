using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Bloop.Core;
using Bloop.Gameplay;

namespace Bloop.UI
{
    /// <summary>
    /// Toggleable inventory overlay panel.
    /// Displays collected items, total weight, and active debuffs.
    /// Toggled with Tab key. Game continues running while open.
    ///
    /// Layout (right side of screen):
    ///   ┌─────────────────────┐
    ///   │   BACKPACK          │
    ///   │─────────────────────│
    ///   │ ► Cave Lichen  2kg  │
    ///   │   Blind Fish   3kg  │
    ///   │─────────────────────│
    ///   │ Weight: 7 / 50 kg   │
    ///   │─────────────────────│
    ///   │ Debuffs:            │
    ///   │ [SLOW] 8.2s         │
    ///   └─────────────────────┘
    ///
    /// Controls when open:
    ///   Up/Down — navigate items
    ///   E/Enter — use selected item
    ///   Tab     — close panel
    /// </summary>
    public class InventoryUI
    {
        // ── Layout constants ───────────────────────────────────────────────────
        private const int PanelWidth   = 220;
        private const int PanelPadding = 10;
        private const int ItemRowHeight = 18;
        private const int MaxVisibleItems = 8;

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color PanelBg       = new Color( 10,  14,  20, 210);
        private static readonly Color PanelBorder   = new Color( 60,  80, 100);
        private static readonly Color HeaderColor   = new Color(180, 200, 220);
        private static readonly Color ItemColor     = new Color(140, 160, 180);
        private static readonly Color SelectedColor = new Color(220, 200, 120);
        private static readonly Color WeightColor   = new Color(160, 180, 200);
        private static readonly Color WeightWarnColor = new Color(220, 100,  60);
        private static readonly Color SeparatorColor = new Color( 40,  60,  80);
        private static readonly Color HintColor     = new Color( 70,  90, 110);

        // ── State ──────────────────────────────────────────────────────────────
        public bool IsVisible { get; set; }
        private int _selectedIndex;

        // ── Input tracking ─────────────────────────────────────────────────────
        private bool _prevTabDown;
        private bool _prevUpDown;
        private bool _prevDownDown;
        private bool _prevUseDown;

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Toggle the inventory panel open/closed.</summary>
        public void Toggle() => IsVisible = !IsVisible;

        /// <summary>
        /// Update inventory UI state: handle Tab toggle, navigation, item use.
        /// Call once per frame from GameplayScreen.Update().
        /// </summary>
        public void Update(InputManager input, Inventory inventory, Player player)
        {
            var kb = Keyboard.GetState();

            // Tab: toggle open/close
            bool tabDown = kb.IsKeyDown(Keys.Tab);
            if (tabDown && !_prevTabDown)
                Toggle();
            _prevTabDown = tabDown;

            if (!IsVisible) return;

            int itemCount = inventory.ItemCount;

            // Up arrow: navigate up
            bool upDown = kb.IsKeyDown(Keys.Up);
            if (upDown && !_prevUpDown && itemCount > 0)
                _selectedIndex = (_selectedIndex - 1 + itemCount) % itemCount;
            _prevUpDown = upDown;

            // Down arrow: navigate down
            bool downDown = kb.IsKeyDown(Keys.Down);
            if (downDown && !_prevDownDown && itemCount > 0)
                _selectedIndex = (_selectedIndex + 1) % itemCount;
            _prevDownDown = downDown;

            // Clamp selection to valid range
            if (itemCount == 0)
                _selectedIndex = 0;
            else
                _selectedIndex = Math.Clamp(_selectedIndex, 0, itemCount - 1);

            // E or Enter: use selected item
            bool useDown = kb.IsKeyDown(Keys.E) || kb.IsKeyDown(Keys.Enter);
            if (useDown && !_prevUseDown && itemCount > 0)
            {
                inventory.UseItem(_selectedIndex, player);
                // Clamp selection after removal
                if (_selectedIndex >= inventory.ItemCount && _selectedIndex > 0)
                    _selectedIndex--;
            }
            _prevUseDown = useDown;
        }

        /// <summary>
        /// Draw the inventory panel in screen space (no camera transform).
        /// Call inside a SpriteBatch.Begin/End block without camera transform.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, AssetManager assets,
            Inventory inventory, DebuffSystem debuffs, int screenWidth, int screenHeight)
        {
            if (!IsVisible) return;

            // ── Calculate panel height dynamically ────────────────────────────
            int itemCount    = inventory.ItemCount;
            int debuffCount  = debuffs.ActiveDebuffs.Count;

            int panelHeight =
                PanelPadding +          // top padding
                20 +                    // "BACKPACK" header
                6 +                     // separator
                Math.Max(1, Math.Min(itemCount, MaxVisibleItems)) * ItemRowHeight +
                6 +                     // separator
                18 +                    // weight line
                (debuffCount > 0 ? 6 + 16 + debuffCount * 16 : 0) + // debuffs section
                6 +                     // hint line
                PanelPadding;           // bottom padding

            // ── Panel position: right side of screen ──────────────────────────
            int panelX = screenWidth - PanelWidth - 16;
            int panelY = 16;

            // ── Background ────────────────────────────────────────────────────
            var panelRect = new Rectangle(panelX, panelY, PanelWidth, panelHeight);
            assets.DrawRect(spriteBatch, panelRect, PanelBg);
            assets.DrawRectOutline(spriteBatch, panelRect, PanelBorder, 1);

            int cx = panelX + PanelPadding;
            int cy = panelY + PanelPadding;

            // ── Header ────────────────────────────────────────────────────────
            assets.DrawString(spriteBatch, "BACKPACK", new Vector2(cx, cy), HeaderColor, 0.85f);
            cy += 20;

            DrawSeparator(spriteBatch, assets, panelX, cy, PanelWidth);
            cy += 6;

            // ── Item list ─────────────────────────────────────────────────────
            if (itemCount == 0)
            {
                assets.DrawString(spriteBatch, "(empty)", new Vector2(cx + 4, cy),
                    new Color(80, 100, 120), 0.75f);
                cy += ItemRowHeight;
            }
            else
            {
                int visibleStart = Math.Max(0, _selectedIndex - MaxVisibleItems + 1);
                int visibleEnd   = Math.Min(itemCount, visibleStart + MaxVisibleItems);

                for (int i = visibleStart; i < visibleEnd; i++)
                {
                    var item     = inventory.Items[i];
                    bool selected = i == _selectedIndex;

                    Color rowColor = selected ? SelectedColor : ItemColor;

                    // Selection indicator
                    string prefix = selected ? "► " : "  ";
                    string line   = $"{prefix}{item.DisplayName}";
                    string weight = $"{item.Weight:0}kg";

                    assets.DrawString(spriteBatch, line,
                        new Vector2(cx, cy), rowColor, 0.75f);

                    // Weight right-aligned
                    assets.DrawString(spriteBatch, weight,
                        new Vector2(panelX + PanelWidth - PanelPadding - 28, cy),
                        rowColor * 0.8f, 0.75f);

                    // Poison indicator
                    if (item.IsPoisonous)
                    {
                        assets.DrawString(spriteBatch, "☠",
                            new Vector2(panelX + PanelWidth - PanelPadding - 44, cy),
                            new Color(180, 60, 60), 0.7f);
                    }

                    cy += ItemRowHeight;
                }
            }

            DrawSeparator(spriteBatch, assets, panelX, cy, PanelWidth);
            cy += 6;

            // ── Weight display ────────────────────────────────────────────────
            bool overweight = inventory.TotalWeight >= Inventory.MaxWeight * 0.9f;
            Color weightColor = overweight ? WeightWarnColor : WeightColor;
            assets.DrawString(spriteBatch,
                $"Weight: {inventory.TotalWeight:0.#} / {Inventory.MaxWeight:0}kg",
                new Vector2(cx, cy), weightColor, 0.75f);
            cy += 18;

            // ── Debuffs section ───────────────────────────────────────────────
            if (debuffCount > 0)
            {
                DrawSeparator(spriteBatch, assets, panelX, cy, PanelWidth);
                cy += 6;

                assets.DrawString(spriteBatch, "Debuffs:", new Vector2(cx, cy),
                    new Color(180, 120, 120), 0.75f);
                cy += 16;

                foreach (var debuff in debuffs.ActiveDebuffs)
                {
                    string name = DebuffSystem.GetDisplayName(debuff.Type);
                    Color  col  = DebuffSystem.GetDisplayColor(debuff.Type);
                    string time = $"{debuff.RemainingTime:0.0}s";

                    assets.DrawString(spriteBatch,
                        $"[{name}] {time}",
                        new Vector2(cx + 4, cy), col, 0.72f);
                    cy += 16;
                }
            }

            // ── Controls hint ─────────────────────────────────────────────────
            cy += 2;
            assets.DrawString(spriteBatch,
                "↑↓ Navigate   E Use   Tab Close",
                new Vector2(cx, cy), HintColor, 0.65f);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private void DrawSeparator(SpriteBatch sb, AssetManager assets,
            int x, int y, int width)
        {
            assets.DrawRect(sb, new Rectangle(x + 4, y + 2, width - 8, 1), SeparatorColor);
        }
    }
}
