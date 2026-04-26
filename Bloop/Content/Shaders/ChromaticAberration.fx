// ChromaticAberration.fx
// Post-process effect: splits RGB channels in opposing directions.
// Applied after LightingSystem.Composite() when player sanity drops below 20%.
// SM 2.0 compatible (MonoGame Reach / MojoShader).

sampler2D InputTexture : register(s0);

// 0.0 = no effect, 1.0 = maximum split
float Intensity;

// Viewport size in pixels, used to convert pixel offset to UV space
float2 ViewportSize;

float4 PixelShaderFunction(float4 position : POSITION0,
                           float4 color    : COLOR0,
                           float2 texCoord : TEXCOORD0) : COLOR0
{
    // Max split = 4 pixels at full intensity
    float2 offset = float2(4.0 / ViewportSize.x, 2.0 / ViewportSize.y) * Intensity;

    // Red shifted right+down, blue shifted left+up, green unchanged
    float r = tex2D(InputTexture, texCoord + offset).r;
    float g = tex2D(InputTexture, texCoord).g;
    float b = tex2D(InputTexture, texCoord - offset).b;
    float a = tex2D(InputTexture, texCoord).a;

    // Edge vignette darkens toward screen corners, stronger at high intensity
    float2 edgeDist = min(texCoord, 1.0 - texCoord);
    float  edgeFade = saturate((edgeDist.x + edgeDist.y) * 4.0);
    float  vignette = lerp(0.75, 1.0, edgeFade) * lerp(1.0, 0.65, Intensity);

    return float4(r * vignette, g * vignette, b * vignette, a);
}

technique ChromaticAberration
{
    pass Pass0
    {
        PixelShader = compile ps_2_0 PixelShaderFunction();
    }
}
