using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpritesheetGen.Models;

namespace SpritesheetGen.Services;

public class MetadataGenerator
{
    private const int HorizontalPadding = 0; // 8px padding on left and right
    private const int VerticalPadding =0; // 8px padding on top and bottom

    public void GenerateMetadata(Dictionary<string, SpriteInfo> sprites, string outputPath)
    {
        if (sprites == null || sprites.Count == 0)
        {
            Console.WriteLine("No sprite data to serialize.");
            return;
        }

        try
        {
            // Create the output directory if it doesn't exist
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Apply padding to sprite positions
            var adjustedSprites = new Dictionary<string, SpriteInfo>();
            foreach (var kvp in sprites)
            {
                adjustedSprites[kvp.Key] = new SpriteInfo
                {
                    X = kvp.Value.X + HorizontalPadding,
                    Y = kvp.Value.Y + VerticalPadding,
                    Width = kvp.Value.Width,
                    Height = kvp.Value.Height
                };
            }

            // Configure JSON serializer options
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            // Serialize the dictionary to JSON
            string json = JsonSerializer.Serialize(adjustedSprites, options);

            // Write to file
            File.WriteAllText(outputPath, json);

            Console.WriteLine($"  Saved metadata to {outputPath} ({adjustedSprites.Count} sprites)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving metadata to {outputPath}: {ex.Message}");
        }
    }
}