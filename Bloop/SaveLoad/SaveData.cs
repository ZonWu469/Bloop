using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Bloop.SaveLoad
{
    /// <summary>
    /// Serializable data model for a complete game save.
    /// Stored as JSON in the user's save directory.
    /// </summary>
    public class SaveData
    {
        // ── Meta ───────────────────────────────────────────────────────────────
        [JsonPropertyName("saveDate")]
        public string SaveDate { get; set; } = "";

        [JsonPropertyName("saveVersion")]
        public int SaveVersion { get; set; } = 1;

        // ── Run info ───────────────────────────────────────────────────────────
        [JsonPropertyName("seed")]
        public int Seed { get; set; }

        [JsonPropertyName("currentDepth")]
        public int CurrentDepth { get; set; } = 1;

        // ── Player stats ───────────────────────────────────────────────────────
        [JsonPropertyName("health")]
        public float Health { get; set; } = 100f;

        [JsonPropertyName("maxHealth")]
        public float MaxHealth { get; set; } = 100f;

        [JsonPropertyName("breathMeter")]
        public float BreathMeter { get; set; } = 100f;

        [JsonPropertyName("lanternFuel")]
        public float LanternFuel { get; set; } = 100f;

        [JsonPropertyName("sanity")]
        public float Sanity { get; set; } = 100f;

        // ── Inventory ──────────────────────────────────────────────────────────
        [JsonPropertyName("inventoryItems")]
        public List<SavedItem> InventoryItems { get; set; } = new();

        // ── Discovered map tiles (list of "x,y" strings per depth) ─────────────
        [JsonPropertyName("discoveredTiles")]
        public Dictionary<int, List<string>> DiscoveredTiles { get; set; } = new();
    }

    /// <summary>A single inventory item stored in the save file.</summary>
    public class SavedItem
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; } = 1;
    }
}
