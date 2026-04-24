using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace SpritesheetGen.Services;

public class ImageLoader
{
    public class ImageData
    {
        public string FileName { get; set; } = string.Empty;
        public Bitmap Image { get; set; } = null!;
    }

    public List<ImageData> LoadImages(string directoryPath)
    {
        var images = new List<ImageData>();

        try
        {
            // Get all PNG files in the directory
            var pngFiles = Directory.GetFiles(directoryPath, "*.png");

            foreach (var filePath in pngFiles)
            {
                try
                {
                    var fileName = Path.GetFileName(filePath);

                    var bitmap = new Bitmap(filePath);

                    images.Add(new ImageData
                    {
                        FileName = fileName,
                        Image = bitmap
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Error loading {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            if (pngFiles.Length == 0)
            {
                Console.WriteLine($"  No PNG files found in {Path.GetFileName(directoryPath)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading directory {directoryPath}: {ex.Message}");
        }

        return images;
    }
}