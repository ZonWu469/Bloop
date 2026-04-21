using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;

namespace Bloop.Lighting
{
    /// <summary>
    /// Manages the multi-pass dynamic lighting rendering pipeline.
    ///
    /// Rendering pipeline (called from GameplayScreen.Draw):
    ///   1. BeginScene()          — redirect world draw to sceneTarget
    ///   2. [caller draws world]  — level, player, objects go to sceneTarget
    ///   3. EndScene()            — restore backbuffer
    ///   4. RenderLightMap()      — draw radial gradients to lightTarget (additive)
    ///   5. Composite()           — multiply scene by lightMap (ambient baked into base clear)
    ///   6. [caller draws HUD]    — HUD drawn on top, unaffected by lighting
    ///
    /// Render targets are created at the actual window resolution and recreated
    /// whenever the window is resized (call OnResize()).
    ///
    /// Light sources are managed as a list; permanent lights stay until removed,
    /// temporary lights (Lifetime >= 0) are auto-removed when expired.
    /// </summary>
    public class LightingSystem : IDisposable
    {
        // ── Constants ──────────────────────────────────────────────────────────
        /// <summary>Maximum number of active light sources (performance cap).</summary>
        private const int MaxLights = 256;

        // ── Blend states ───────────────────────────────────────────────────────
        /// <summary>
        /// Multiply blend: result = src * dst.
        /// Used to darken the scene by the light map in Composite().
        /// Unlit areas stay at the ambient level baked into the light map clear color.
        /// </summary>
        private static readonly BlendState MultiplyBlend = new BlendState
        {
            ColorSourceBlend      = Blend.DestinationColor,
            ColorDestinationBlend = Blend.Zero,
            AlphaSourceBlend      = Blend.One,
            AlphaDestinationBlend = Blend.Zero,
        };

        // ── References ─────────────────────────────────────────────────────────
        private readonly GraphicsDevice _graphicsDevice;
        private readonly AssetManager   _assets;

        // ── Render targets ─────────────────────────────────────────────────────
        /// <summary>Captures the rendered game world (scene pass).</summary>
        private RenderTarget2D _sceneTarget  = null!;

        /// <summary>Captures the light map (additive radial gradients).</summary>
        private RenderTarget2D _lightTarget  = null!;

        // ── Light sources ──────────────────────────────────────────────────────
        private readonly List<LightSource> _lights    = new();
        private readonly List<LightSource> _toRemove  = new();

        // ── Configuration ──────────────────────────────────────────────────────
        /// <summary>
        /// Ambient light floor: 0.0 = pitch black in unlit areas.
        /// Typical values: 0.02 (deep cave) to 0.08 (shallow cave).
        /// </summary>
        public float AmbientLevel { get; set; } = 0.05f;

        /// <summary>
        /// When false, Composite() draws the scene without any lighting effect
        /// (fully lit). Useful for debugging. Toggle with F2.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Depth-based color grading tint (3.3).
        /// Applied as a multiply pass after the scene×lightMap composite.
        /// White = no tint. Set from GameplayScreen based on current depth/biome.
        /// </summary>
        public Color ColorGradeTint { get; set; } = Color.White;

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Create the lighting system. Render targets are created at the given
        /// width/height (should match the actual window/backbuffer size).
        /// Call OnResize() whenever the window changes size.
        /// </summary>
        public LightingSystem(GraphicsDevice graphicsDevice, AssetManager assets,
            int width, int height)
        {
            _graphicsDevice = graphicsDevice;
            _assets         = assets;

            CreateRenderTargets(width, height);
        }

        // ── Window resize ──────────────────────────────────────────────────────

        /// <summary>
        /// Recreate render targets at the new window size.
        /// Call this from Game1 whenever Window.ClientSizeChanged fires.
        /// </summary>
        public void OnResize(int newWidth, int newHeight)
        {
            if (newWidth  <= 0) newWidth  = 1;
            if (newHeight <= 0) newHeight = 1;
            CreateRenderTargets(newWidth, newHeight);
        }

        // ── Light management ───────────────────────────────────────────────────

        /// <summary>Add a light source to the system. Capped at MaxLights.</summary>
        public void AddLight(LightSource light)
        {
            if (_lights.Count >= MaxLights) return;
            _lights.Add(light);
        }

        /// <summary>Remove a specific light source.</summary>
        public void RemoveLight(LightSource light)
        {
            _lights.Remove(light);
        }

        /// <summary>Remove all light sources.</summary>
        public void ClearLights()
        {
            _lights.Clear();
        }

        // ── Per-frame update ───────────────────────────────────────────────────

        /// <summary>
        /// Tick all light sources. Remove expired temporary lights.
        /// Call once per frame from GameplayScreen.Update().
        /// </summary>
        public void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            foreach (var light in _lights)
            {
                light.Update(dt);
                if (light.IsExpired)
                    _toRemove.Add(light);
            }

            foreach (var light in _toRemove)
                _lights.Remove(light);
            _toRemove.Clear();
        }

        // ── Rendering passes ───────────────────────────────────────────────────

        /// <summary>
        /// Pass 1: Redirect subsequent draw calls to the scene render target.
        /// Call before drawing the game world.
        /// </summary>
        public void BeginScene()
        {
            _graphicsDevice.SetRenderTarget(_sceneTarget);
            _graphicsDevice.Clear(Color.Transparent);
        }

        /// <summary>
        /// End scene capture. Restores the backbuffer as the active render target.
        /// Call after drawing the game world, before RenderLightMap().
        /// </summary>
        public void EndScene()
        {
            _graphicsDevice.SetRenderTarget(null);
        }

        /// <summary>
        /// Pass 2: Render the light map to lightTarget.
        /// Draws a radial gradient at each light source position using additive blending.
        /// cameraTransform is the SpriteBatch transform matrix from Camera.GetTransform().
        /// </summary>
        public void RenderLightMap(SpriteBatch spriteBatch, Matrix cameraTransform)
        {
            _graphicsDevice.SetRenderTarget(_lightTarget);

            // Clear to ambient color so that unlit pixels, when multiplied against
            // the scene in Composite(), produce scene * ambient (e.g. 5% brightness).
            byte ambByte = (byte)MathHelper.Clamp(AmbientLevel * 255f, 0f, 255f);
            _graphicsDevice.Clear(new Color(ambByte, ambByte, ambByte, (byte)255));

            if (_lights.Count > 0)
            {
                // Additive blending: overlapping lights combine naturally
                spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.Additive,
                    SamplerState.LinearClamp,
                    null, null, null,
                    cameraTransform);

                foreach (var light in _lights)
                {
                    // Use effective values (post-flicker/sputter) for rendering
                    float radius    = light.EffectiveRadius;
                    float intensity = light.EffectiveIntensity;
                    if (intensity <= 0f || radius <= 0f) continue;

                    // Get or create a radial gradient texture at the light's diameter
                    int diameter = (int)(radius * 2f);
                    if (diameter < 2) continue;

                    var gradientTex = _assets.CreateRadialGradient(diameter);

                    // Draw centered at the light's effective world position (includes sway)
                    // Scale only RGB by intensity; keep alpha=255 so SourceAlpha blend
                    // doesn't attenuate the contribution a second time.
                    Color tint = new Color(
                        (byte)MathHelper.Clamp(light.Color.R * intensity, 0f, 255f),
                        (byte)MathHelper.Clamp(light.Color.G * intensity, 0f, 255f),
                        (byte)MathHelper.Clamp(light.Color.B * intensity, 0f, 255f),
                        (byte)255);
                    spriteBatch.Draw(
                        gradientTex,
                        light.EffectivePosition,
                        null,
                        tint,
                        0f,
                        new Vector2(diameter / 2f, diameter / 2f), // center origin
                        1f,
                        SpriteEffects.None,
                        0f);
                }

                spriteBatch.End();
            }

            // Restore the backbuffer
            _graphicsDevice.SetRenderTarget(null);
        }

        /// <summary>
        /// Pass 3: Composite the scene and light map onto the backbuffer.
        /// Draws the scene at full brightness, then multiplies it by the light map.
        /// Call after RenderLightMap(), before drawing the HUD.
        /// </summary>
        public void Composite(SpriteBatch spriteBatch)
        {
            int w = _sceneTarget.Width;
            int h = _sceneTarget.Height;
            var destRect = new Rectangle(0, 0, w, h);

            if (!Enabled)
            {
                // Debug mode: draw scene without lighting effect
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                    SamplerState.PointClamp, null, null, null, null);
                spriteBatch.Draw(_sceneTarget, destRect, Color.White);
                spriteBatch.End();
                return;
            }

            // Pass 1: draw the scene at full brightness
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.PointClamp, null, null, null, null);
            spriteBatch.Draw(_sceneTarget, destRect, Color.White);
            spriteBatch.End();

            // Pass 2: multiply the scene by the light map.
            // The light map was cleared to the ambient color and radial gradients
            // were added on top, so:
            //   unlit pixels  → lightMap ≈ (ambient, ambient, ambient) → scene darkened to ~5%
            //   lit pixels    → lightMap ≈ white                        → scene at full brightness
            // Multiply blend: result = src * dst  (src=lightMap, dst=scene already drawn)
            spriteBatch.Begin(SpriteSortMode.Deferred, MultiplyBlend,
                SamplerState.LinearClamp, null, null, null, null);
            spriteBatch.Draw(_lightTarget, destRect, Color.White);
            spriteBatch.End();

            // Pass 3 (3.3): depth-based color grading tint.
            // Only applied when tint is not pure white (no-op cost when unused).
            if (ColorGradeTint != Color.White)
            {
                spriteBatch.Begin(SpriteSortMode.Deferred, MultiplyBlend,
                    SamplerState.PointClamp, null, null, null, null);
                spriteBatch.Draw(_sceneTarget, destRect, ColorGradeTint);
                spriteBatch.End();
            }
        }

        // ── Accessors ──────────────────────────────────────────────────────────

        /// <summary>Read-only view of all active light sources (for debugging).</summary>
        public IReadOnlyList<LightSource> Lights => _lights;

        // ── IDisposable ────────────────────────────────────────────────────────

        public void Dispose()
        {
            _sceneTarget?.Dispose();
            _lightTarget?.Dispose();
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private void CreateRenderTargets(int width, int height)
        {
            _sceneTarget?.Dispose();
            _lightTarget?.Dispose();

            var format = _graphicsDevice.PresentationParameters.BackBufferFormat;

            _sceneTarget = new RenderTarget2D(
                _graphicsDevice, width, height,
                false, format, DepthFormat.None);

            _lightTarget = new RenderTarget2D(
                _graphicsDevice, width, height,
                false, SurfaceFormat.Color, DepthFormat.None);
        }
    }
}
