using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SpritesheetGen.Models;

namespace SpritesheetGen.Services;

public class GridPacker
{
    private const int Spacing = 2;

    public class PackResult
    {
        public Dictionary<string, SpriteInfo> Sprites { get; set; } = new Dictionary<string, SpriteInfo>();
        public int TotalWidth { get; set; }
        public int TotalHeight { get; set; }
    }

    public PackResult PackImages(List<ImageLoader.ImageData> images)
    {
        if (images == null || images.Count == 0)
        {
            return new PackResult { TotalWidth = 0, TotalHeight = 0 };
        }

        // Sort images by area (width * height) descending for better packing
        var sortedImages = images.OrderByDescending(img => img.Image.Width * img.Image.Height).ToList();

        // Calculate grid dimensions
        int columns = (int)Math.Ceiling(Math.Sqrt(sortedImages.Count));
        int rows = (int)Math.Ceiling((double)sortedImages.Count / columns);

        // Arrays to store max width per column and max height per row
        int[] columnWidths = new int[columns];
        int[] rowHeights = new int[rows];

        // Initialize arrays
        for (int i = 0; i < columns; i++) columnWidths[i] = 0;
        for (int i = 0; i < rows; i++) rowHeights[i] = 0;

        // Place each image and update column/row dimensions
        var sprites = new Dictionary<string, SpriteInfo>();

        for (int i = 0; i < sortedImages.Count; i++)
        {
            var imageData = sortedImages[i];
            var image = imageData.Image;

            int row = i / columns;
            int col = i % columns;

            // Update column width and row height
            columnWidths[col] = Math.Max(columnWidths[col], image.Width);
            rowHeights[row] = Math.Max(rowHeights[row], image.Height);

            // Store position temporarily (will be adjusted after we know column/row dimensions)
            sprites[imageData.FileName] = new SpriteInfo
            {
                X = 0, // placeholder
                Y = 0, // placeholder
                Width = image.Width,
                Height = image.Height
            };
        }

        // Calculate cumulative positions with spacing
        int[] columnOffsets = new int[columns];
        int[] rowOffsets = new int[rows];

        // Column offsets: each column starts after previous column's width + spacing
        int currentX = 0;
        for (int col = 0; col < columns; col++)
        {
            columnOffsets[col] = currentX;
            currentX += columnWidths[col] + Spacing;
        }

        // Row offsets: each row starts after previous row's height + spacing
        int currentY = 0;
        for (int row = 0; row < rows; row++)
        {
            rowOffsets[row] = currentY;
            currentY += rowHeights[row] + Spacing;
        }

        // Now assign actual positions to each sprite
        for (int i = 0; i < sortedImages.Count; i++)
        {
            var imageData = sortedImages[i];
            int row = i / columns;
            int col = i % columns;

            var sprite = sprites[imageData.FileName];
            
            // Center the image within its cell (optional, but good for visual alignment)
            int cellWidth = columnWidths[col];
            int cellHeight = rowHeights[row];
            int offsetX = (cellWidth - sprite.Width) / 2;
            int offsetY = (cellHeight - sprite.Height) / 2;

            sprite.X = columnOffsets[col] + offsetX;
            sprite.Y = rowOffsets[row] + offsetY;
        }

        // Calculate total dimensions (remove extra spacing after last column/row)
        int totalWidth = currentX - Spacing;
        int totalHeight = currentY - Spacing;

        return new PackResult
        {
            Sprites = sprites,
            TotalWidth = totalWidth,
            TotalHeight = totalHeight
        };
    }
}