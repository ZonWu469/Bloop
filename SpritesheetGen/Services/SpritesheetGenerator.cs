using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using SpritesheetGen.Models;

namespace SpritesheetGen.Services;

public class SpritesheetGenerator
{
    private const int HorizontalPadding = 0; // 8px padding on left and right
    private const int VerticalPadding = 0; // 8px padding on top and bottom

    public void GenerateSpritesheet(
        List<ImageLoader.ImageData> images,
        Dictionary<string, SpriteInfo> spritePositions,
        int width,
        int height,
        string outputPath)
    {
        if (width <= 0 || height <= 0)
        {
            Console.WriteLine($"Invalid spritesheet dimensions: {width}x{height}");
            return;
        }

        // Create the output directory if it doesn't exist
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // Create a bitmap with 32-bit ARGB format for high quality and transparency
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            // Configure graphics for pixel-perfect rendering
            graphics.CompositingQuality = CompositingQuality.AssumeLinear;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.PageUnit = GraphicsUnit.Pixel;

            // Clear with transparent background
            graphics.Clear(Color.Transparent);

            // Draw each image at its calculated position with exact dimensions
            foreach (var imageData in images)
            {
                if (spritePositions.TryGetValue(imageData.FileName, out var spriteInfo))
                {
                    // Create a destination rectangle with padding applied
                    var destRect = new Rectangle(
                        spriteInfo.X + HorizontalPadding,
                        spriteInfo.Y + VerticalPadding,
                        spriteInfo.Width,
                        spriteInfo.Height);

                    // Draw the image without any scaling or interpolation
                    graphics.DrawImage(
                        imageData.Image,
                        destRect,
                        0, 0, spriteInfo.Width, spriteInfo.Height,
                        GraphicsUnit.Pixel);
                }
                else
                {
                    Console.WriteLine($"  Warning: No position data for {imageData.FileName}");
                }
            }

            // Save the bitmap as PNG with maximum quality
            var encoder = GetEncoder(ImageFormat.Png);
            var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, 100L);

            bitmap.Save(outputPath, encoder, encoderParameters);
        }

        Console.WriteLine($"  Saved spritesheet to {outputPath} ({width}x{height})");
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == format.Guid)
                return codec;
        }
        return null;
    }
}