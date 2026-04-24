using System;
using System.Collections.Generic;
using System.IO;

namespace SpritesheetGen.Services;

public class DirectoryScanner
{
    public List<string> GetProcessableDirectories(string dataPath)
    {
        var directories = new List<string>();

        try
        {
            // Get all subdirectories in the data path
            var allDirectories = Directory.GetDirectories(dataPath);

            foreach (var directory in allDirectories)
            {
                var directoryName = Path.GetFileName(directory);
                
                // Skip the "Spritesheets" directory
                if (directoryName.Equals("Spritesheets", StringComparison.OrdinalIgnoreCase)
                    || directoryName.Equals("Backgrounds", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                directories.Add(directory);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error scanning directory {dataPath}: {ex.Message}");
        }

        return directories;
    }
}