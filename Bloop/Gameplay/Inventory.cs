using System;
using System.Collections.Generic;
using Bloop.SaveLoad;

namespace Bloop.Gameplay
{
    // ── Item type enum ─────────────────────────────────────────────────────────

    /// <summary>All collectible item types in the game.</summary>
    public enum ItemType
    {
        CaveLichen,
        BlindFish,
    }

    // ── Item data class ────────────────────────────────────────────────────────

    /// <summary>
    /// A single inventory item with its properties.
    /// Immutable after creation — use factory methods to create items.
    /// </summary>
    public class InventoryItem
    {
        public ItemType Type        { get; }
        public string   DisplayName { get; }
        public float    Weight      { get; }   // kg
        public float    HealAmount  { get; }   // health restored on use
        public bool     IsPoisonous { get; }
        public float    PoisonDamage    { get; }
        public float    PoisonStunTime  { get; }

        public InventoryItem(ItemType type, string displayName, float weight,
            float healAmount, bool isPoisonous, float poisonDamage, float poisonStunTime)
        {
            Type           = type;
            DisplayName    = displayName;
            Weight         = weight;
            HealAmount     = healAmount;
            IsPoisonous    = isPoisonous;
            PoisonDamage   = poisonDamage;
            PoisonStunTime = poisonStunTime;
        }

        // ── Factory methods ────────────────────────────────────────────────────

        /// <summary>Create a Cave Lichen item.</summary>
        public static InventoryItem CreateCaveLichen(bool isPoisonous) =>
            new InventoryItem(
                type:           ItemType.CaveLichen,
                displayName:    "Cave Lichen",
                weight:         2f,
                healAmount:     20f,
                isPoisonous:    isPoisonous,
                poisonDamage:   10f,
                poisonStunTime: 2f);

        /// <summary>Create a Blind Fish item.</summary>
        public static InventoryItem CreateBlindFish(bool isPoisonous) =>
            new InventoryItem(
                type:           ItemType.BlindFish,
                displayName:    "Blind Fish",
                weight:         3f,
                healAmount:     30f,
                isPoisonous:    isPoisonous,
                poisonDamage:   5f,
                poisonStunTime: 3f);
    }

    // ── Inventory class ────────────────────────────────────────────────────────

    /// <summary>
    /// Manages the player's collected items with weight tracking.
    /// Weight affects player physics via Player.SetInventoryWeight().
    ///
    /// Design:
    ///   - Simple list-based storage (no grid/slot system)
    ///   - Maximum carry weight: 50kg — TryAdd returns false if exceeded
    ///   - Items can be used (consumed) from inventory
    ///   - Weight changes fire OnWeightChanged event so Player can update physics
    ///   - Serializable to/from SaveData.InventoryItems
    /// </summary>
    public class Inventory
    {
        // ── Constants ──────────────────────────────────────────────────────────
        public const float MaxWeight = 50f;

        // ── State ──────────────────────────────────────────────────────────────
        private readonly List<InventoryItem> _items = new();

        public IReadOnlyList<InventoryItem> Items => _items;
        public int   ItemCount   => _items.Count;
        public float TotalWeight { get; private set; }
        public bool  IsFull      => TotalWeight >= MaxWeight;

        // ── Events ─────────────────────────────────────────────────────────────
        /// <summary>Fired when an item is added. Argument is the new total weight.</summary>
        public event Action<float>? OnWeightChanged;

        /// <summary>Fired when an item is added.</summary>
        public event Action<InventoryItem>? OnItemAdded;

        /// <summary>Fired when an item is removed or used.</summary>
        public event Action<InventoryItem>? OnItemRemoved;

        // ── Operations ─────────────────────────────────────────────────────────

        /// <summary>
        /// Try to add an item to the inventory.
        /// Returns false if adding the item would exceed MaxWeight.
        /// </summary>
        public bool TryAdd(InventoryItem item)
        {
            if (TotalWeight + item.Weight > MaxWeight)
                return false;

            _items.Add(item);
            TotalWeight += item.Weight;

            OnItemAdded?.Invoke(item);
            OnWeightChanged?.Invoke(TotalWeight);
            return true;
        }

        /// <summary>Remove a specific item from the inventory.</summary>
        public void Remove(InventoryItem item)
        {
            if (!_items.Remove(item)) return;

            TotalWeight = MathHelper.Clamp(TotalWeight - item.Weight, 0f, MaxWeight);
            OnItemRemoved?.Invoke(item);
            OnWeightChanged?.Invoke(TotalWeight);
        }

        /// <summary>
        /// Use (consume) the item at the given index.
        /// Applies the item's heal effect and removes it from inventory.
        /// If poisonous, applies debuff via the player's DebuffSystem.
        /// Returns false if index is out of range.
        /// </summary>
        public bool UseItem(int index, Player player)
        {
            if (index < 0 || index >= _items.Count) return false;

            var item = _items[index];

            // Apply heal
            player.Stats.HealHealth(item.HealAmount);

            // Apply poison debuff if applicable
            if (item.IsPoisonous)
            {
                player.Stats.TakeDamage(item.PoisonDamage);
                player.Stun(item.PoisonStunTime);

                // Apply debuff based on item type
                switch (item.Type)
                {
                    case ItemType.CaveLichen:
                        player.Debuffs.ApplyDebuff(DebuffType.SlowMovement, 10f);
                        player.Debuffs.ApplyDebuff(DebuffType.Blurred, 8f);
                        break;
                    case ItemType.BlindFish:
                        player.Debuffs.ApplyDebuff(DebuffType.ReducedJump, 10f);
                        player.Debuffs.ApplyDebuff(DebuffType.Blurred, 8f);
                        break;
                }
            }

            // Remove from inventory (weight update fires automatically)
            Remove(item);

            // TODO: play item use sound effect
            return true;
        }

        /// <summary>Remove all items from the inventory.</summary>
        public void Clear()
        {
            _items.Clear();
            TotalWeight = 0f;
            OnWeightChanged?.Invoke(0f);
        }

        // ── Serialization ──────────────────────────────────────────────────────

        /// <summary>Convert inventory to save data format.</summary>
        public List<SavedItem> ToSaveData()
        {
            var result = new List<SavedItem>();
            foreach (var item in _items)
            {
                result.Add(new SavedItem
                {
                    Type     = item.Type.ToString(),
                    Quantity = 1
                });
            }
            return result;
        }

        /// <summary>
        /// Restore inventory from save data.
        /// Unknown item types are skipped gracefully.
        /// </summary>
        public void LoadFromSave(List<SavedItem> savedItems)
        {
            Clear();
            foreach (var saved in savedItems)
            {
                if (!Enum.TryParse<ItemType>(saved.Type, out var itemType))
                    continue; // skip unknown types

                // Recreate items — poison state is not saved (re-roll on load)
                InventoryItem? item = itemType switch
                {
                    ItemType.CaveLichen => InventoryItem.CreateCaveLichen(isPoisonous: false),
                    ItemType.BlindFish  => InventoryItem.CreateBlindFish(isPoisonous: false),
                    _                   => null
                };

                if (item != null)
                    TryAdd(item);
            }
        }
    }

    // ── MathHelper shim ────────────────────────────────────────────────────────
    // Avoid a MonoGame dependency in this file by using System.Math directly.
    file static class MathHelper
    {
        public static float Clamp(float value, float min, float max)
            => value < min ? min : value > max ? max : value;
    }
}
