using Microsoft.Xna.Framework;

namespace Bloop.Lighting
{
    /// <summary>
    /// A temporary warm-amber light spawned when the player throws a flare.
    /// Lasts 30 seconds with mild flicker, fading out over the final 5 seconds.
    /// </summary>
    public class FlareLight : LightSource
    {
        public const float FlareLightRadius    = 280f;
        public const float FlareLightLifetime  = 30f;
        public const float FlareLightIntensity = 2.9f;
        private const float FadeDuration       = 5f;

        public static readonly Color FlareLightColor = new Color(255, 200, 100);

        private readonly float _initialIntensity;

        public FlareLight(Vector2 pixelPosition)
            : base(pixelPosition, FlareLightRadius, FlareLightIntensity, FlareLightColor, FlareLightLifetime)
        {
            _initialIntensity  = FlareLightIntensity;
            FlickerAmplitude   = 0.08f;
            FlickerFrequency   = 7f;
            SputterChance      = 0.04f;
        }

        public override void Update(float deltaSeconds)
        {
            base.Update(deltaSeconds);

            if (Lifetime < FadeDuration && FadeDuration > 0f)
                Intensity = _initialIntensity * MathHelper.Clamp(Lifetime / FadeDuration, 0f, 1f);
        }
    }
}
