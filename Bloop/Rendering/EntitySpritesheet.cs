using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Bloop.Rendering
{
    /// <summary>
    /// Holds a loaded entity animation spritesheet (horizontal strip) and its metadata
    /// parsed from the accompanying Pixelorama JSON file.
    ///
    /// Layout: frames are arranged left-to-right in a single row.
    /// Frame i occupies source rect: (i * FrameWidth, 0, FrameWidth, FrameHeight).
    /// </summary>
    public class EntitySpritesheet
    {
        // ── Properties ─────────────────────────────────────────────────────────

        /// <summary>The full spritesheet texture (horizontal strip of all frames).</summary>
        public Texture2D Texture     { get; }

        /// <summary>Total number of animation frames.</summary>
        public int       FrameCount  { get; }

        /// <summary>Width of a single frame in pixels (size_x from JSON).</summary>
        public int       FrameWidth  { get; }

        /// <summary>Height of a single frame in pixels (size_y from JSON).</summary>
        public int       FrameHeight { get; }

        /// <summary>Playback speed in frames per second (fps from JSON).</summary>
        public float     Fps         { get; }

        // ── Constructor ────────────────────────────────────────────────────────

        public EntitySpritesheet(Texture2D texture, int frameCount,
                                 int frameWidth, int frameHeight, float fps)
        {
            Texture     = texture;
            FrameCount  = frameCount;
            FrameWidth  = frameWidth;
            FrameHeight = frameHeight;
            Fps         = fps;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the source rectangle for the given frame index within the strip.
        /// frameIndex is NOT clamped — callers must ensure 0 ≤ frameIndex &lt; FrameCount.
        /// </summary>
        public Rectangle GetSourceRect(int frameIndex)
            => new Rectangle(frameIndex * FrameWidth, 0, FrameWidth, FrameHeight);
    }
}
