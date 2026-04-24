using SpritesheetGen.Services;
using System;
using System.IO;

const string dataPath = @"C:\Users\dbagn\Desktop\Progetti\BloopGit\Bloop\Content\Data";
const string outputPath = @"C:\Users\dbagn\Desktop\Progetti\BloopGit\Bloop\Content\Data\Spritesheets";

Console.WriteLine("Spritesheet Generator");
Console.WriteLine("====================");
Console.WriteLine($"Source: {dataPath}");
Console.WriteLine($"Output: {outputPath}");
Console.WriteLine();

// Check if source directory exists
if (!Directory.Exists(dataPath))
{
    Console.WriteLine($"Error: Source directory not found: {dataPath}");
    Console.WriteLine("Please ensure the directory exists and try again.");
    return 1;
}

// Ensure output directory exists
try
{
    Directory.CreateDirectory(outputPath);
    Console.WriteLine($"Output directory ready: {outputPath}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error creating output directory: {ex.Message}");
    return 1;
}

// Initialize services
var scanner = new DirectoryScanner();
var imageLoader = new ImageLoader();
var gridPacker = new GridPacker();
var spritesheetGenerator = new SpritesheetGenerator();
var metadataGenerator = new MetadataGenerator();

// Get directories to process
var directories = scanner.GetProcessableDirectories(dataPath);

if (directories.Count == 0)
{
    Console.WriteLine("No directories found to process.");
    return 0;
}

Console.WriteLine($"Found {directories.Count} directory(ies) to process:");
foreach (var dir in directories)
{
    Console.WriteLine($"  • {Path.GetFileName(dir)}");
}
Console.WriteLine();

int successCount = 0;
int errorCount = 0;

// Process each directory
foreach (var directory in directories)
{
    var directoryName = Path.GetFileName(directory);
    Console.WriteLine($"Processing {directoryName}...");

    try
    {
        // Load images
        var images = imageLoader.LoadImages(directory);
        
        if (images.Count == 0)
        {
            Console.WriteLine($"  Skipping {directoryName} - no PNG files found.");
            continue;
        }

        // Pack images into grid
        var packResult = gridPacker.PackImages(images);
        
        if (packResult.Sprites.Count == 0)
        {
            Console.WriteLine($"  Skipping {directoryName} - failed to pack images.");
            errorCount++;
            continue;
        }

        // Generate spritesheet PNG
        var spritesheetFilePath = Path.Combine(outputPath, $"{directoryName}.png");
        spritesheetGenerator.GenerateSpritesheet(
            images,
            packResult.Sprites,
            packResult.TotalWidth,
            packResult.TotalHeight,
            spritesheetFilePath);

        // Generate metadata JSON
        var metadataFilePath = Path.Combine(outputPath, $"{directoryName}.json");
        metadataGenerator.GenerateMetadata(packResult.Sprites, metadataFilePath);

        Console.WriteLine($"  ✓ Generated {directoryName}.png ({packResult.TotalWidth}x{packResult.TotalHeight})");
        Console.WriteLine($"  ✓ Generated {directoryName}.json ({images.Count} sprites)");

        successCount++;

        // Cleanup: dispose all loaded images
        foreach (var img in images)
        {
            img.Image?.Dispose();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ✗ Error processing {directoryName}: {ex.Message}");
        errorCount++;
    }

    Console.WriteLine();
}

Console.WriteLine("====================");
Console.WriteLine("Processing complete!");
Console.WriteLine($"  Directories processed successfully: {successCount}");
Console.WriteLine($"  Directories with errors: {errorCount}");
Console.WriteLine($"  Total files generated: {successCount * 2} (PNG + JSON)");

if (errorCount > 0)
{
    Console.WriteLine();
    Console.WriteLine("Note: Some directories had errors. Check the logs above for details.");
}

return 0;
