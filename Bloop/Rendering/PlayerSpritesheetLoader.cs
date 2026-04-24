using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Bloop.Rendering
{
    /// <summary>
    /// Loads a <see cref="PlayerSpritesheet"/> by reading the Pixelorama project JSON
    /// (*.png.json) for metadata and the compiled texture via the MonoGame ContentManager.
    ///
    /// The JSON is read directly from disk (not through the content pipeline) because
    /// Pixelorama project files are plain JSON and are not compiled by MGCB.
    /// The PNG texture IS compiled by MGCB and loaded via ContentManager.Load.
    /// </summary>
    public static class PlayerSpritesheetLoader
    {
        /// <summary>
        /// Load a player spritesheet.
        /// </summary>
        /// <param name="content">The game's ContentManager (Content.RootDirectory must be set).</param>
        /// <param name="jsonPath">
        ///   Path to the Pixelorama .png.json file, relative to the executable
        ///   (e.g. "Content/Data/Player/scing_idle.png.json").
        /// </param>
        /// <param name="contentKey">
        ///   Content pipeline key for the texture, without extension
        ///   (e.g. "Data/Player/scing_idle").
        /// </param>
        /// <returns>A fully populated <see cref="PlayerSpritesheet"/>.</returns>
        public static PlayerSpritesheet Load(ContentManager content,
                                             string jsonPath,
                                             string contentKey)
        {
            // ── Parse Pixelorama JSON ──────────────────────────────────────────
            string raw = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            float fps        = root.GetProperty("fps").GetSingle();
            int   frameCount = root.GetProperty("frames").GetArrayLength();
            int   sizeX      = root.GetProperty("size_x").GetInt32();
            int   sizeY      = root.GetProperty("size_y").GetInt32();

            // ── Load compiled texture via content pipeline ─────────────────────
            var texture = content.Load<Texture2D>(contentKey);

            return new PlayerSpritesheet(texture, frameCount, sizeX, sizeY, fps);
        }
    }
}
