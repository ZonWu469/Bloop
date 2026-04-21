// LightingEffect.fx
// Multi-pass lighting shader for Descent Into the Deep.
//
// Pass 1: Scene is rendered to sceneTexture (done by LightingSystem.BeginScene/EndScene)
// Pass 2: Light map is rendered to lightMapTexture (additive radial gradients)
// Pass 3: This shader composites: finalColor = sceneColor * (lightMap + ambient)
//
// Compatible with MonoGame Reach profile (SM 2.0 / GLSL ES equivalent).
// MojoShader cross-compiles this HLSL to GLSL for DesktopGL.
//
// NOTE: No custom vertex shader — MonoGame SpriteBatch provides its own
// MatrixTransform-aware vertex shader (SpriteEffect). Defining a custom VS
// would bypass the SpriteBatch matrix and break screen-space positioning.

// ── Parameters ────────────────────────────────────────────────────────────────

/// The rendered game world (scene pass output). Bound to s0 by SpriteBatch.
sampler2D sceneTexture : register(s0);

/// The light map (black = dark, white = fully lit, additive blended lights).
/// Bound to s1 manually via GraphicsDevice.Textures[1] before the draw call.
sampler2D lightMapTexture : register(s1);

/// Ambient light floor: 0.0 = pitch black in unlit areas, 0.15 = dim cave glow.
float ambientLevel;

// ── Pixel shader ──────────────────────────────────────────────────────────────

float4 PixelShaderFunction(float4 position : POSITION0,
                           float4 color    : COLOR0,
                           float2 texCoord : TEXCOORD0) : COLOR0
{
    // Sample the scene color at this pixel
    float4 sceneColor = tex2D(sceneTexture, texCoord);

    // Sample the light map at this pixel
    float4 lightColor = tex2D(lightMapTexture, texCoord);

    // Ambient floor: ensures a minimum visibility even in total darkness
    float3 ambient = float3(ambientLevel, ambientLevel, ambientLevel);

    // Composite: scene multiplied by (light.rgb + ambient)
    // lightColor.rgb is in [0,1] from additive blending of radial gradients
    float3 lit = sceneColor.rgb * (lightColor.rgb + ambient);

    // Clamp to [0,1] to avoid over-brightening
    lit = clamp(lit, 0.0, 1.0);

    return float4(lit, sceneColor.a);
}

// ── Technique ─────────────────────────────────────────────────────────────────

technique LightingTechnique
{
    pass Pass0
    {
        PixelShader = compile ps_2_0 PixelShaderFunction();
    }
}
