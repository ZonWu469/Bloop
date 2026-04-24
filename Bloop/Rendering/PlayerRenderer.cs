using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Gameplay;

namespace Bloop.Rendering
{
    /// <summary>
    /// Draws the player using state-specific animation spritesheets loaded from
    /// Pixelorama horizontal-strip PNGs.
    ///
    /// State → spritesheet mapping:
    ///   Idle, Crouching, ThrowingFlare  → scing_idle
    ///   Walking                          → scing_walking
    ///   Jumping, Falling, WallJumping,
    ///     Launching, WallClinging,
    ///     Mantling                       → scing_jumping
    ///   Climbing, Sliding, Rappelling,
    ///     Swinging                       → scing_climbing  (rotated −90°, facing up)
    ///   Controlling                      → scing_controlling
    ///   Stunned                          → scing_stunned
    ///   Dead                             → scing_dead
    ///
    /// Scale: sprite height is scaled to match Player.StandingHeightPx so the
    /// visual size matches the physics hitbox.
    ///
    /// Facing: sprites are authored facing right.
    ///   Normal states  — SpriteEffects.FlipHorizontally when FacingDirection &lt; 0.
    ///   Climbing states — rotated −90° (character faces up, back on left).
    ///                     Flip when touching left wall so the back faces the wall.
    /// </summary>
    public static class PlayerRenderer
    {
        // ── Entry point ────────────────────────────────────────────────────────

        public static void Draw(SpriteBatch sb, AssetManager assets, Player player)
        {
            var sheet = PickSheet(assets, player.State);
            if (sheet == null) return; // spritesheets not yet loaded

            // ── Frame index ────────────────────────────────────────────────────
            // Clamp frameCount to at least 1 to avoid divide-by-zero on bad data.
            int frameCount = Math.Max(1, sheet.FrameCount);
            int frameIndex = (int)(AnimationClock.Time * sheet.Fps) % frameCount;
            var srcRect    = sheet.GetSourceRect(frameIndex);

            // ── Scale: fit sprite height to standing hitbox ────────────────────
            float scale  = sheet.FrameHeight > 0
                           ? Player.StandingHeightPx / sheet.FrameHeight
                           : 1f;

            // ── Origin: center of a single frame ──────────────────────────────
            var origin = new Vector2(sheet.FrameWidth / 2f, sheet.FrameHeight / 2f);

            // ── Rotation & flip ────────────────────────────────────────────────
            float         rotation;
            SpriteEffects effects;

            if (IsClimbingState(player.State))
            {
                // Rotate −90° so the sprite faces upward.
                // The sprite's "back" (left side when facing right) becomes the
                // bottom after rotation. Flip horizontally when touching the left
                // wall so the back always faces the wall surface.
                rotation = -MathF.PI / 2f;
                effects  = player.IsTouchingWallLeft
                           ? SpriteEffects.FlipHorizontally
                           : SpriteEffects.None;
            }
            else
            {
                rotation = 0f;
                effects  = player.FacingDirection < 0
                           ? SpriteEffects.FlipHorizontally
                           : SpriteEffects.None;
            }

            // ── Draw ───────────────────────────────────────────────────────────
            sb.Draw(
                sheet.Texture,
                player.PixelPosition,
                srcRect,
                Color.White,
                rotation,
                origin,
                scale,
                effects,
                0f);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static bool IsClimbingState(PlayerState state)
            => state is PlayerState.Climbing
                     or PlayerState.Sliding
                     or PlayerState.Rappelling
                     or PlayerState.Swinging;

        private static PlayerSpritesheet? PickSheet(AssetManager assets, PlayerState state)
            => state switch
            {
                PlayerState.Walking                                          => assets.PlayerWalking,

                PlayerState.Jumping
                    or PlayerState.Falling
                    or PlayerState.WallJumping
                    or PlayerState.Launching
                    or PlayerState.WallClinging
                    or PlayerState.Mantling                                  => assets.PlayerJumping,

                PlayerState.Climbing
                    or PlayerState.Sliding
                    or PlayerState.Rappelling
                    or PlayerState.Swinging                                  => assets.PlayerClimbing,

                PlayerState.Controlling                                      => assets.PlayerControlling,

                PlayerState.Stunned                                          => assets.PlayerStunned,

                PlayerState.Dead                                             => assets.PlayerDead,

                // Idle, Crouching, ThrowingFlare, and any future states
                _                                                            => assets.PlayerIdle,
            };
    }
}
