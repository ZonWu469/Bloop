using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Bloop.SaveLoad
{
    /// <summary>
    /// Handles reading and writing save files as JSON.
    /// Save files are stored in a "Saves" folder next to the executable.
    /// Uses atomic writes (temp file + rename) to prevent corruption.
    /// </summary>
    public static class SaveManager
    {
        // ── Constants ──────────────────────────────────────────────────────────
        private const string SaveDirectory = "Saves";
        private const string SaveExtension = ".json";
        private const int    SaveVersion   = 1;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };

        // ── Directory helpers ──────────────────────────────────────────────────
        private static string GetSaveDir()
        {
            string dir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, SaveDirectory);
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all .json save file paths sorted by last-write time (newest first).
        /// </summary>
        public static List<string> GetSaveFiles()
        {
            string dir = GetSaveDir();
            var files  = new List<string>(
                Directory.GetFiles(dir, $"*{SaveExtension}"));
            files.Sort((a, b) =>
                File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
            return files;
        }

        /// <summary>
        /// Save game data to a file named "save_{seed}_depth{depth}.json".
        /// Uses atomic write: write to temp file, then rename.
        /// </summary>
        public static bool Save(SaveData data, string? customFileName = null)
        {
            try
            {
                data.SaveDate    = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                data.SaveVersion = SaveVersion;

                string fileName = customFileName
                    ?? $"save_{data.Seed}_depth{data.CurrentDepth}{SaveExtension}";
                string filePath = Path.Combine(GetSaveDir(), fileName);
                string tempPath = filePath + ".tmp";

                string json = JsonSerializer.Serialize(data, _jsonOptions);
                File.WriteAllText(tempPath, json);

                // Atomic rename
                if (File.Exists(filePath)) File.Delete(filePath);
                File.Move(tempPath, filePath);

                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SaveManager] Save failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load a SaveData from the given file path.
        /// Returns null if the file is missing or corrupt.
        /// </summary>
        public static SaveData? Load(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<SaveData>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SaveManager] Load failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Returns the most recent save file for a given seed, or null if none exists.
        /// </summary>
        public static SaveData? LoadLatestForSeed(int seed)
        {
            foreach (var file in GetSaveFiles())
            {
                var data = Load(file);
                if (data?.Seed == seed) return data;
            }
            return null;
        }
    }
}
