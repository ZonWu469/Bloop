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
    ///   Climbing, Sliding                → scing_climbing  (rotated −90°, facing up)
    ///   Rappelling, Swinging (grounded)  → scing_idle / scing_walking
    ///   Rappelling, Swinging (airborne)  → scing_climbing  (rotated toward anchor, 1.5× size)
    ///   Controlling                      → scing_controlling
    ///   Stunned                          → scing_stunned
    ///   Dead                             → scing_dead
    ///
    /// Scale: sprite height is scaled to match Player.StandingHeightPx so the
    /// visual size matches the physics hitbox. Rope-attached airborne state uses 1.5× that.
    ///
    /// Facing: sprites are authored facing right.
    ///   Normal states  — SpriteEffects.FlipHorizontally when FacingDirection &lt; 0.
    ///   Climbing states — rotated −90° (character faces up, back on left).
    ///                     Flip when touching left wall so the back faces the wall.
    ///   Rope airborne  — rotated toward anchor; flip when FacingDirection &lt; 0.
    /// </summary>
    public static class PlayerRenderer
    {
        // ── Smoothed render state (Phase 6.1) ─────────────────────────────────
        // Static is acceptable here: there is exactly one Player rendered.
        // These prevent sprite scale and rope rotation from popping on state
        // transitions or sudden anchor swings.
        private static float _smoothedScale  = 1f;
        private static float _smoothedRot    = 0f;
        private static bool  _smoothedValid  = false;
        private static System.Diagnostics.Stopwatch _renderClock = new();
        private const float ScaleRate = 12f;   // ~80ms settle
        private const float RotRate   = 16f;   // ~60ms settle

        public static void Draw(SpriteBatch sb, AssetManager assets, Player player)
        {
            bool isRopeAttached = player.ActiveRopeAnchorPixels.HasValue
                && (player.State == PlayerState.Rappelling
                    || player.State == PlayerState.Swinging);

            PlayerSpritesheet? sheet;
            int   frameIndex;
            float scale;
            float rotation;
            SpriteEffects effects;

            if (isRopeAttached && player.IsGrounded)
            {
                // ── On rope but touching ground: idle or walking ───────────────
                bool isMoving = MathF.Abs(player.PixelVelocity.X) > 5f;
                sheet = isMoving ? assets.PlayerWalking : assets.PlayerIdle;
                if (sheet == null) return;

                int frameCount = Math.Max(1, sheet.FrameCount);
                frameIndex = (int)(AnimationClock.Time * sheet.Fps) % frameCount;
                scale      = sheet.FrameHeight > 0
                             ? Player.StandingHeightPx / sheet.FrameHeight * 0.7f : 1f;
                rotation   = 0f;
                effects    = player.FacingDirection < 0
                             ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            }
            else if (isRopeAttached)
            {
                // ── Airborne on rope: scing_climbing ──────────────────────────
                sheet = assets.PlayerClimbing;
                if (sheet == null) return;

                int frameCount = Math.Max(1, sheet.FrameCount);
                // Animate only when actively pressing W/S; otherwise hold first frame
                frameIndex = player.IsRopeClimbing
                             ? (int)(AnimationClock.Time * sheet.Fps) % frameCount
                             : 0;

                scale = sheet.FrameHeight > 0
                        ? Player.StandingHeightPx / sheet.FrameHeight
                        : 1f;

                // Rotation so sprite "top" points toward the rope anchor.
                // Unrotated sprite top = screen vector (0,−1).
                // After clockwise rotation θ it becomes (sinθ, −cosθ).
                // Setting equal to anchorDir: sinθ = dx, −cosθ = dy
                // → θ = atan2(anchorDir.X, −anchorDir.Y)
                Vector2 toAnchor = player.ActiveRopeAnchorPixels!.Value - player.PixelPosition;
                if (toAnchor.LengthSquared() < 0.01f) toAnchor = new Vector2(0f, -1f);
                toAnchor.Normalize();
                rotation = MathF.Atan2(toAnchor.X, -toAnchor.Y);

                effects = player.FacingDirection < 0
                          ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            }
            else
            {
                // ── All other states: standard logic ──────────────────────────
                sheet = PickSheet(assets, player.State);
                if (sheet == null) return;

                int frameCount = Math.Max(1, sheet.FrameCount);
                frameIndex = (int)(AnimationClock.Time * sheet.Fps) % frameCount;
                bool isClimbing = IsClimbingState(player.State);
                scale      = sheet.FrameHeight > 0
                             ? Player.StandingHeightPx / sheet.FrameHeight * (isClimbing ? 1f : 0.7f) : 1f;

                if (isClimbing)
                {
                    // Rotate −90° so the sprite faces upward.
                    rotation = -MathF.PI / 2f;
                    // Flip when facing left so the character always faces toward the wall.
                    effects  = player.FacingDirection < 0
                               ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                }
                else
                {
                    rotation = 0f;
                    effects  = player.FacingDirection < 0
                               ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                }
            }

            // ── Phase 6.1: smooth scale and rotation across frames ────────────
            float dt;
            if (!_renderClock.IsRunning) { _renderClock.Start(); dt = 1f / 60f; _smoothedValid = false; }
            else { dt = (float)System.Math.Min(0.05, _renderClock.Elapsed.TotalSeconds); _renderClock.Restart(); }

            if (!_smoothedValid)
            {
                _smoothedScale = scale;
                _smoothedRot   = rotation;
                _smoothedValid = true;
            }
            else
            {
                _smoothedScale = Bloop.Core.Smoothing.ExpDecay(_smoothedScale, scale, ScaleRate, dt);
                // Wrap-aware rotation lerp: shortest-arc.
                float rotDelta = WrapAngle(rotation - _smoothedRot);
                _smoothedRot   = _smoothedRot + rotDelta *
                                 (1f - MathF.Exp(-RotRate * dt));
            }

            // ── Draw ───────────────────────────────────────────────────────────
            var srcRect = sheet.GetSourceRect(frameIndex);
            var origin  = new Vector2(sheet.FrameWidth / 2f, sheet.FrameHeight / 2f);

            sb.Draw(
                sheet.Texture,
                player.PixelPosition,
                srcRect,
                Color.White,
                _smoothedRot,
                origin,
                _smoothedScale,
                effects,
                0f);
        }

        /// <summary>Wrap angle to [-π, π] for shortest-arc rotation lerp.</summary>
        private static float WrapAngle(float a)
        {
            while (a > MathF.PI)  a -= MathF.PI * 2f;
            while (a < -MathF.PI) a += MathF.PI * 2f;
            return a;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        // Climbing and Sliding use the −90° wall/vine rotation.
        // Rappelling and Swinging are handled by the rope-attach branches above.
        private static bool IsClimbingState(PlayerState state)
            => state is PlayerState.Climbing or PlayerState.Sliding;

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
                    or PlayerState.Sliding                                   => assets.PlayerClimbing,

                PlayerState.Controlling                                      => assets.PlayerControlling,

                PlayerState.Stunned                                          => assets.PlayerStunned,

                PlayerState.Dead                                             => assets.PlayerDead,

                // Idle, Crouching, ThrowingFlare, and any future states
                _                                                            => assets.PlayerIdle,
            };
    }
}
