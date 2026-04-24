using System.Collections.Generic;
using System.IO;
using Bloop.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Bloop.Core
{
    /// <summary>
    /// Centralized asset manager that generates and caches placeholder textures at runtime.
    /// Since the game uses colored rectangles as placeholders, no external art files are needed.
    /// Also holds the shared SpriteFont for UI text rendering.
    /// </summary>
    public class AssetManager
    {
        // ── Fields ─────────────────────────────────────────────────────────────
        private readonly GraphicsDevice _graphicsDevice;
        private readonly Dictionary<string, Texture2D> _textureCache = new();

        /// <summary>Large bold font for menus and UI screens.</summary>
        public SpriteFont? MenuFont { get; private set; }

        /// <summary>Small regular font for gameplay HUD.</summary>
        public SpriteFont? GameFont { get; private set; }

        // ── Well-known 1×1 pixel textures ──────────────────────────────────────
        /// <summary>A single white pixel — tint it any color when drawing.</summary>
        public Texture2D Pixel { get; private set; } = null!;

        // ── Player spritesheets ────────────────────────────────────────────────
        /// <summary>Idle / default animation (also used for Crouching, ThrowingFlare).</summary>
        public PlayerSpritesheet? PlayerIdle        { get; private set; }
        /// <summary>Walking animation.</summary>
        public PlayerSpritesheet? PlayerWalking     { get; private set; }
        /// <summary>Jumping / falling / airborne animation.</summary>
        public PlayerSpritesheet? PlayerJumping     { get; private set; }
        /// <summary>Climbing / sliding / rappelling / swinging animation (drawn rotated −90°).</summary>
        public PlayerSpritesheet? PlayerClimbing    { get; private set; }
        /// <summary>Controlling an entity animation.</summary>
        public PlayerSpritesheet? PlayerControlling { get; private set; }
        /// <summary>Stunned animation.</summary>
        public PlayerSpritesheet? PlayerStunned     { get; private set; }
        /// <summary>Dead animation.</summary>
        public PlayerSpritesheet? PlayerDead        { get; private set; }

        // ── Entity spritesheets ────────────────────────────────────────────────
        /// <summary>Echo Bat animation spritesheet.</summary>
        public EntitySpritesheet? EntityEchoBat             { get; private set; }
        /// <summary>Silk Weaver Spider animation spritesheet.</summary>
        public EntitySpritesheet? EntitySilkWeaverSpider    { get; private set; }
        /// <summary>Chain Centipede animation spritesheet.</summary>
        public EntitySpritesheet? EntityChainCentipede      { get; private set; }
        /// <summary>Luminescent Glowworm animation spritesheet.</summary>
        public EntitySpritesheet? EntityLuminescentGlowworm { get; private set; }
        /// <summary>Deep Burrow Worm animation spritesheet.</summary>
        public EntitySpritesheet? EntityDeepBurrowWorm      { get; private set; }
        /// <summary>Blind Cave Salamander animation spritesheet.</summary>
        public EntitySpritesheet? EntityBlindCaveSalamander { get; private set; }
        /// <summary>Luminous Isopod animation spritesheet.</summary>
        public EntitySpritesheet? EntityLuminousIsopod      { get; private set; }

        // ── Constructor ────────────────────────────────────────────────────────
        public AssetManager(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice;
        }

        // ── Initialization ─────────────────────────────────────────────────────

        /// <summary>
        /// Call once after GraphicsDevice is ready.
        /// Creates the shared pixel texture and any other runtime-generated assets.
        /// </summary>
        public void Initialize()
        {
            Pixel = CreateSolidTexture(1, 1, Color.White);
        }

        /// <summary>
        /// Load both SpriteFonts from the ContentManager.
        /// If fonts are not available, UI will fall back to not rendering text.
        /// </summary>
        public void LoadFonts(SpriteFont? menuFont, SpriteFont? gameFont)
        {
            MenuFont = menuFont;
            GameFont = gameFont;
        }

        /// <summary>
        /// Load all 7 player animation spritesheets.
        /// Reads Pixelorama JSON files from disk for metadata and loads compiled
        /// PNG textures via the content pipeline.
        /// </summary>
        /// <param name="content">The game's ContentManager.</param>
        /// <param name="contentRoot">Content.RootDirectory (e.g. "Content").</param>
        public void LoadPlayerSpritesheets(ContentManager content, string contentRoot)
        {
            PlayerSpritesheet LoadSheet(string name)
                => PlayerSpritesheetLoader.Load(
                    content,
                    Path.Combine(contentRoot, "Data", "Player", $"{name}.png.json"),
                    $"Data/Player/{name}");

            PlayerIdle        = LoadSheet("scing_idle");
            PlayerWalking     = LoadSheet("scing_walking");
            PlayerJumping     = LoadSheet("scing_jumping");
            PlayerClimbing    = LoadSheet("scing_climbing");
            PlayerControlling = LoadSheet("scing_controlling");
            PlayerStunned     = LoadSheet("scing_stunned");
            PlayerDead        = LoadSheet("scing_dead");
        }

        /// <summary>
        /// Load all 7 entity animation spritesheets.
        /// Reads Pixelorama JSON files from disk for metadata and loads compiled
        /// PNG textures via the content pipeline.
        /// </summary>
        /// <param name="content">The game's ContentManager.</param>
        /// <param name="contentRoot">Content.RootDirectory (e.g. "Content").</param>
        public void LoadEntitySpritesheets(ContentManager content, string contentRoot)
        {
            EntitySpritesheet LoadSheet(string name)
                => EntitySpritesheetLoader.Load(
                    content,
                    Path.Combine(contentRoot, "Data", "Entities", $"{name}.png.json"),
                    $"Data/Entities/{name}");

            EntityEchoBat             = LoadSheet("EchoBat");
            EntitySilkWeaverSpider    = LoadSheet("SilkWeaverSpider");
            EntityChainCentipede      = LoadSheet("ChainCentipede");
            EntityLuminescentGlowworm = LoadSheet("LuminescentGlowworm");
            EntityDeepBurrowWorm      = LoadSheet("DeepBurrowWorm");
            EntityBlindCaveSalamander = LoadSheet("BlindCaveSalamander");
            EntityLuminousIsopod      = LoadSheet("LuminousIsopod");
        }

        // ── Texture helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Get or create a solid-color texture of the given size.
        /// Results are cached by a "WxH_RRGGBB" key.
        /// </summary>
        public Texture2D GetSolidTexture(int width, int height, Color color)
        {
            string key = $"{width}x{height}_{color.PackedValue:X8}";
            if (_textureCache.TryGetValue(key, out var cached))
                return cached;

            var tex = CreateSolidTexture(width, height, color);
            _textureCache[key] = tex;
            return tex;
        }

        /// <summary>
        /// Create a radial gradient texture for light rendering.
        /// Center is white (full intensity), edges are black (zero intensity).
        /// </summary>
        public Texture2D CreateRadialGradient(int diameter)
        {
            string key = $"radial_{diameter}";
            if (_textureCache.TryGetValue(key, out var cached))
                return cached;

            int radius = diameter / 2;
            var data   = new Color[diameter * diameter];

            for (int y = 0; y < diameter; y++)
            {
                for (int x = 0; x < diameter; x++)
                {
                    float dx   = x - radius;
                    float dy   = y - radius;
                    float dist = MathHelper.Clamp(
                        (float)System.Math.Sqrt(dx * dx + dy * dy) / radius, 0f, 1f);

                    // Smooth falloff: 1 at center, 0 at edge
                    float intensity = 1f - dist * dist;
                    byte  b         = (byte)(intensity * 255);
                    data[y * diameter + x] = new Color(b, b, b, (byte)255);
                }
            }

            var tex = new Texture2D(_graphicsDevice, diameter, diameter);
            tex.SetData(data);
            _textureCache[key] = tex;
            return tex;
        }

        /// <summary>Draw a filled rectangle using the shared pixel texture.</summary>
        public void DrawRect(SpriteBatch sb, Rectangle rect, Color color)
        {
            sb.Draw(Pixel, rect, color);
        }

        /// <summary>Draw a filled rectangle using the shared pixel texture.</summary>
        public void DrawRect(SpriteBatch sb, Vector2 position, Vector2 size, Color color)
        {
            sb.Draw(Pixel,
                new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y),
                color);
        }

        /// <summary>Draw a hollow rectangle outline.</summary>
        public void DrawRectOutline(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
        {
            // Top
            sb.Draw(Pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
            // Bottom
            sb.Draw(Pixel, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
            // Left
            sb.Draw(Pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
            // Right
            sb.Draw(Pixel, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
        }

        /// <summary>Draw a string using GameFont. No-ops if font is null.</summary>
        public void DrawString(SpriteBatch sb, string text, Vector2 position, Color color, float scale = 1f)
        {
            if (GameFont == null) return;
            sb.DrawString(GameFont, text, position, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        /// <summary>Draw a string centered horizontally using GameFont.</summary>
        public void DrawStringCentered(SpriteBatch sb, string text, float y, Color color, float scale = 1f)
        {
            if (GameFont == null) return;
            Vector2 size = GameFont.MeasureString(text) * scale;
            float   x    = (_graphicsDevice.Viewport.Width - size.X) / 2f;
            sb.DrawString(GameFont, text, new Vector2(x, y), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        /// <summary>Draw a string using MenuFont. No-ops if font is null.</summary>
        public void DrawMenuString(SpriteBatch sb, string text, Vector2 position, Color color, float scale = 1f)
        {
            if (MenuFont == null) return;
            sb.DrawString(MenuFont, text, position, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        /// <summary>Draw a string centered horizontally using MenuFont.</summary>
        public void DrawMenuStringCentered(SpriteBatch sb, string text, float y, Color color, float scale = 1f)
        {
            if (MenuFont == null) return;
            Vector2 size = MenuFont.MeasureString(text) * scale;
            float   x    = (_graphicsDevice.Viewport.Width - size.X) / 2f;
            sb.DrawString(MenuFont, text, new Vector2(x, y), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        // ── Cleanup ────────────────────────────────────────────────────────────
        public void Dispose()
        {
            foreach (var tex in _textureCache.Values)
                tex?.Dispose();
            _textureCache.Clear();
        }

        // ── Private helpers ────────────────────────────────────────────────────
        private Texture2D CreateSolidTexture(int width, int height, Color color)
        {
            var tex  = new Texture2D(_graphicsDevice, width, height);
            var data = new Color[width * height];
            for (int i = 0; i < data.Length; i++)
                data[i] = color;
            tex.SetData(data);
            return tex;
        }
    }
}
