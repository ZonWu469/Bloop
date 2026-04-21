using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Gameplay;

namespace Bloop.Rendering
{
    /// <summary>
    /// Draws the player as an animated geometric figure.
    ///
    /// Anatomy (all pixel offsets from body center):
    ///   Head      : circle (r=7) at (0, -22)
    ///   Torso     : rounded rect 14×18 at (0, -6)
    ///   Backpack  : rect 8×12 on the back side at (±10, -8)
    ///   Lantern   : diamond 6×6 held in front hand at (±12, -4)
    ///   Arms      : two line segments from shoulder to elbow to hand
    ///   Legs      : two line segments from hip to knee to foot
    ///
    /// State-specific postures:
    ///   Idle       – slight breathing bob, arms relaxed at sides
    ///   Walking    – leg swing cycle, arm counter-swing
    ///   Jumping    – arms up, legs tucked
    ///   Falling    – arms spread, legs trailing
    ///   Climbing   – arms alternating reach, legs braced
    ///   Sliding    – body tilted, arms back, legs forward
    ///   Rappelling – body upright, arms gripping rope above
    ///   Swinging   – body angled with swing, arms up
    ///   Stunned    – wobbly head, arms limp, stars orbiting
    ///   Dead       – body collapsed, no animation
    /// </summary>
    public static class PlayerRenderer
    {
        // ── Palette ────────────────────────────────────────────────────────────
        // Suit / body
        private static readonly Color ColSuit        = new Color( 55,  90, 160);  // deep blue suit
        private static readonly Color ColSuitShade   = new Color( 35,  60, 120);  // shadow side
        private static readonly Color ColSuitHighlight= new Color( 90, 130, 210); // highlight
        // Helmet / head
        private static readonly Color ColHelmet      = new Color( 70, 110, 180);
        private static readonly Color ColVisor       = new Color(140, 200, 255, 180); // translucent visor
        private static readonly Color ColVisorShine  = new Color(220, 240, 255, 120);
        // Skin (hands visible at cuffs)
        private static readonly Color ColSkin        = new Color(220, 175, 130);
        // Backpack
        private static readonly Color ColPack        = new Color( 80,  60,  40);
        private static readonly Color ColPackStrap   = new Color( 60,  45,  30);
        // Lantern
        private static readonly Color ColLanternBody = new Color(200, 160,  60);
        private static readonly Color ColLanternGlow = new Color(255, 220, 100, 80);
        private static readonly Color ColLanternBeam = new Color(255, 240, 160, 40);
        // Stun stars
        private static readonly Color ColStar        = new Color(255, 230,  80);
        // Dead
        private static readonly Color ColDead        = new Color( 40,  30,  30);
        private static readonly Color ColDeadSuit    = new Color( 30,  45,  80);

        // ── Constants ──────────────────────────────────────────────────────────
        private const float HeadR      = 7f;
        private const float TorsoW     = 14f;
        private const float TorsoH     = 18f;
        private const float LimbThick  = 3f;
        private const float LimbThickT = 2f; // thinner for forearm/shin

        // ── Entry point ────────────────────────────────────────────────────────

        public static void Draw(SpriteBatch sb, AssetManager assets, Player player)
        {
            Vector2 center = player.PixelPosition;
            PlayerState state = player.State;
            int facing = player.FacingDirection; // +1 right, -1 left
            float t = AnimationClock.Time;

            switch (state)
            {
                case PlayerState.Dead:
                    DrawDead(sb, assets, center, facing);
                    break;
                case PlayerState.Stunned:
                    DrawStunned(sb, assets, center, facing, t);
                    break;
                case PlayerState.Idle:
                    DrawIdle(sb, assets, center, facing, t);
                    break;
                case PlayerState.Walking:
                    DrawWalking(sb, assets, center, facing, t);
                    break;
                case PlayerState.Crouching:
                    DrawCrouching(sb, assets, center, facing, t);
                    break;
                case PlayerState.Jumping:
                    DrawJumping(sb, assets, center, facing, t);
                    break;
                case PlayerState.Falling:
                    DrawFalling(sb, assets, center, facing, t);
                    break;
                case PlayerState.Climbing:
                    DrawClimbing(sb, assets, center, facing, t);
                    break;
                case PlayerState.Sliding:
                    DrawSliding(sb, assets, center, facing, t);
                    break;
                case PlayerState.Rappelling:
                    DrawRappelling(sb, assets, center, facing, t);
                    break;
                case PlayerState.Swinging:
                    DrawSwinging(sb, assets, center, facing, t);
                    break;
                case PlayerState.WallJumping:
                    DrawWallJumping(sb, assets, center, facing, t);
                    break;
                case PlayerState.Mantling:
                    DrawMantling(sb, assets, center, facing, t);
                    break;
                case PlayerState.Launching:
                    DrawLaunching(sb, assets, center, facing, t, player.LaunchSpeedPx);
                    break;
                case PlayerState.ThrowingFlare:
                    DrawThrowingFlare(sb, assets, center, facing, t);
                    break;
                default:
                    DrawIdle(sb, assets, center, facing, t);
                    break;
            }
        }

        // ── State renderers ────────────────────────────────────────────────────

        private static void DrawIdle(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float t)
        {
            // Gentle breathing: torso bobs ±1px, head follows
            float breathY = AnimationClock.Sway(1f, 0.4f);

            // Arms: relaxed at sides, slight sway
            float armSway = AnimationClock.Sway(1.5f, 0.3f);
            Vector2 lShoulder = center + new Vector2(-6f, -10f + breathY);
            Vector2 rShoulder = center + new Vector2( 6f, -10f + breathY);
            Vector2 lElbow    = lShoulder + new Vector2(-3f,  8f + armSway * 0.5f);
            Vector2 rElbow    = rShoulder + new Vector2( 3f,  8f - armSway * 0.5f);
            Vector2 lHand     = lElbow    + new Vector2(-1f,  6f);
            Vector2 rHand     = rElbow    + new Vector2( 1f,  6f);

            // Legs: standing straight
            Vector2 lHip  = center + new Vector2(-4f,  8f + breathY);
            Vector2 rHip  = center + new Vector2( 4f,  8f + breathY);
            Vector2 lKnee = lHip   + new Vector2(-1f, 10f);
            Vector2 rKnee = rHip   + new Vector2( 1f, 10f);
            Vector2 lFoot = lKnee  + new Vector2(-1f,  9f);
            Vector2 rFoot = rKnee  + new Vector2( 1f,  9f);

            DrawFullBody(sb, assets, center, facing, breathY,
                lShoulder, lElbow, lHand,
                rShoulder, rElbow, rHand,
                lHip, lKnee, lFoot,
                rHip, rKnee, rFoot);
        }

        private static void DrawCrouching(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float t)
        {
            // Hitbox is 20px tall (half of standing). Shift the drawing center
            // down by ~12px so the head/torso (drawn at fixed negative offsets
            // inside DrawFullBody) render compressed into the shrunken hitbox.
            float breathY = AnimationClock.Sway(0.6f, 0.6f);
            Vector2 drawCenter = center + new Vector2(0f, 12f);

            // Shoulders tucked close to drawCenter (shorter torso clearance)
            Vector2 lShoulder = drawCenter + new Vector2(-6f, -6f + breathY);
            Vector2 rShoulder = drawCenter + new Vector2( 6f, -6f + breathY);
            // Arms folded forward, hands resting on thighs
            Vector2 lElbow    = lShoulder + new Vector2(-1f, 3f);
            Vector2 rElbow    = rShoulder + new Vector2( 1f, 3f);
            Vector2 lHand     = lElbow    + new Vector2( 2f, 2f);
            Vector2 rHand     = rElbow    + new Vector2(-2f, 2f);

            // Hips just below drawCenter; knees splayed outward at hip height
            Vector2 lHip  = drawCenter + new Vector2(-4f, 1f + breathY);
            Vector2 rHip  = drawCenter + new Vector2( 4f, 1f + breathY);
            Vector2 lKnee = lHip + new Vector2(-5f, 1f);
            Vector2 rKnee = rHip + new Vector2( 5f, 1f);
            // Feet tucked back inward and down to sit at the hitbox bottom
            Vector2 lFoot = lKnee + new Vector2( 3f, 5f);
            Vector2 rFoot = rKnee + new Vector2(-3f, 5f);

            DrawFullBody(sb, assets, drawCenter, facing, breathY,
                lShoulder, lElbow, lHand,
                rShoulder, rElbow, rHand,
                lHip, lKnee, lFoot,
                rHip, rKnee, rFoot);
        }

        private static void DrawWalking(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float t)
        {
            // Walk cycle: legs swing at ~2 Hz, arms counter-swing
            float cycle = AnimationClock.Sway(10f, 2.0f); // -10..+10 px swing
            float breathY = AnimationClock.Sway(0.5f, 4.0f); // slight vertical bob

            // Arms counter-swing to legs
            Vector2 lShoulder = center + new Vector2(-6f, -10f + breathY);
            Vector2 rShoulder = center + new Vector2( 6f, -10f + breathY);
            Vector2 lElbow    = lShoulder + new Vector2(-3f + cycle * 0.3f,  7f);
            Vector2 rElbow    = rShoulder + new Vector2( 3f - cycle * 0.3f,  7f);
            Vector2 lHand     = lElbow    + new Vector2(-1f + cycle * 0.4f,  6f);
            Vector2 rHand     = rElbow    + new Vector2( 1f - cycle * 0.4f,  6f);

            // Legs swing
            Vector2 lHip  = center + new Vector2(-4f,  8f + breathY);
            Vector2 rHip  = center + new Vector2( 4f,  8f + breathY);
            Vector2 lKnee = lHip   + new Vector2(-1f + cycle * 0.2f, 9f);
            Vector2 rKnee = rHip   + new Vector2( 1f - cycle * 0.2f, 9f);
            Vector2 lFoot = lKnee  + new Vector2(-1f + cycle * 0.6f, 9f);
            Vector2 rFoot = rKnee  + new Vector2( 1f - cycle * 0.6f, 9f);

            DrawFullBody(sb, assets, center, facing, breathY,
                lShoulder, lElbow, lHand,
                rShoulder, rElbow, rHand,
                lHip, lKnee, lFoot,
                rHip, rKnee, rFoot);
        }

        private static void DrawJumping(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float t)
        {
            // Arms raised, legs tucked
            float tuck = AnimationClock.Sway(2f, 0.5f); // slight tuck oscillation

            Vector2 lShoulder = center + new Vector2(-6f, -12f);
            Vector2 rShoulder = center + new Vector2( 6f, -12f);
            Vector2 lElbow    = lShoulder + new Vector2(-5f, -5f);
            Vector2 rElbow    = rShoulder + new Vector2( 5f, -5f);
            Vector2 lHand     = lElbow    + new Vector2(-3f, -4f);
            Vector2 rHand     = rElbow    + new Vector2( 3f, -4f);

            // Legs tucked up
            Vector2 lHip  = center + new Vector2(-4f,  6f);
            Vector2 rHip  = center + new Vector2( 4f,  6f);
            Vector2 lKnee = lHip   + new Vector2(-6f,  5f + tuck);
            Vector2 rKnee = rHip   + new Vector2( 6f,  5f + tuck);
            Vector2 lFoot = lKnee  + new Vector2(-2f, -3f);
            Vector2 rFoot = rKnee  + new Vector2( 2f, -3f);

            DrawFullBody(sb, assets, center, facing, -2f,
                lShoulder, lElbow, lHand,
                rShoulder, rElbow, rHand,
                lHip, lKnee, lFoot,
                rHip, rKnee, rFoot);
        }

        private static void DrawFalling(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float t)
        {
            // Arms spread wide, legs trailing behind
            float flutter = AnimationClock.Sway(3f, 1.5f);

            Vector2 lShoulder = center + new Vector2(-6f, -10f);
            Vector2 rShoulder = center + new Vector2( 6f, -10f);
            Vector2 lElbow    = lShoulder + new Vector2(-8f,  2f + flutter * 0.3f);
            Vector2 rElbow    = rShoulder + new Vector2( 8f,  2f - flutter * 0.3f);
            Vector2 lHand     = lElbow    + new Vector2(-5f,  3f);
            Vector2 rHand     = rElbow    + new Vector2( 5f,  3f);

            // Legs trailing (slightly behind = upward in screen space)
            Vector2 lHip  = center + new Vector2(-4f,  8f);
            Vector2 rHip  = center + new Vector2( 4f,  8f);
            Vector2 lKnee = lHip   + new Vector2(-3f,  6f + flutter * 0.2f);
            Vector2 rKnee = rHip   + new Vector2( 3f,  6f - flutter * 0.2f);
            Vector2 lFoot = lKnee  + new Vector2(-2f,  7f);
            Vector2 rFoot = rKnee  + new Vector2( 2f,  7f);

            DrawFullBody(sb, assets, center, facing, 0f,
                lShoulder, lElbow, lHand,
                rShoulder, rElbow, rHand,
                lHip, lKnee, lFoot,
                rHip, rKnee, rFoot);
        }

        private static void DrawClimbing(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float t)
        {
            // Alternating arm reach upward, legs braced against surface
            float reach = AnimationClock.Sway(8f, 1.2f); // -8..+8 alternating reach

            Vector2 lShoulder = center + new Vector2(-6f, -12f);
            Vector2 rShoulder = center + new Vector2( 6f, -12f);
            // Left arm reaches up when right is down and vice versa
            Vector2 lElbow = lShoulder + new Vector2(-3f, -4f - reach * 0.5f);
            Vector2 rElbow = rShoulder + new Vector2( 3f, -4f + reach * 0.5f);
            Vector2 lHand  = lElbow    + new Vector2(-2f, -5f - reach * 0.3f);
            Vector2 rHand  = rElbow    + new Vector2( 2f, -5f + reach * 0.3f);

            // Legs braced: one bent, one extended
            Vector2 lHip  = center + new Vector2(-4f,  8f);
            Vector2 rHip  = center + new Vector2( 4f,  8f);
            Vector2 lKnee = lHip   + new Vector2(-5f,  6f + reach * 0.3f);
            Vector2 rKnee = rHip   + new Vector2( 5f,  6f - reach * 0.3f);
            Vector2 lFoot = lKnee  + new Vector2(-3f,  5f);
            Vector2 rFoot = rKnee  + new Vector2( 3f,  5f);

            DrawFullBody(sb, assets, center, facing, 0f,
                lShoulder, lElbow, lHand,
                rShoulder, rElbow, rHand,
                lHip, lKnee, lFoot,
                rHip, rKnee, rFoot);
        }

        private static void DrawSliding(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float t)
        {
            // Body tilted forward (facing direction), arms swept back, legs forward
            float tiltX = facing * 4f;

            Vector2 lShoulder = center + new Vector2(-6f + tiltX, -8f);
            Vector2 rShoulder = center + new Vector2( 6f + tiltX, -8f);
            // Arms swept back (opposite to facing)
            Vector2 lElbow = lShoulder + new Vector2(-facing * 6f, 4f);
            Vector2 rElbow = rShoulder + new Vector2(-facing * 4f, 4f);
            Vector2 lHand  = lElbow    + new Vector2(-facing * 4f, 3f);
            Vector2 rHand  = rElbow    + new Vector2(-facing * 3f, 3f);

            // Legs forward (toward facing direction)
            Vector2 lHip  = center + new Vector2(-4f + tiltX,  8f);
            Vector2 rHip  = center + new Vector2( 4f + tiltX,  8f);
            Vector2 lKnee = lHip   + new Vector2(facing * 4f, 8f);
            Vector2 rKnee = rHip   + new Vector2(facing * 6f, 8f);
            Vector2 lFoot = lKnee  + new Vector2(facing * 3f, 5f);
            Vector2 rFoot = rKnee  + new Vector2(facing * 4f, 5f);

            DrawFullBody(sb, assets, center, facing, 0f,
                lShoulder, lElbow, lHand,
                rShoulder, rElbow, rHand,
                lHip, lKnee, lFoot,
                rHip, rKnee, rFoot);
        }

        private static void DrawRappelling(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float t)
        {
            // Body upright, both arms raised gripping rope above head
            float grip = AnimationClock.Sway(1.5f, 0.8f); // slight grip shift

            Vector2 lShoulder = center + new Vector2(-6f, -12f);
            Vector2 rShoulder = center + new Vector2( 6f, -12f);
            Vector2 lElbow    = lShoulder + new Vector2(-2f, -6f + grip);
            Vector2 rElbow    = rShoulder + new Vector2( 2f, -6f - grip);
            Vector2 lHand     = lElbow    + new Vector2(-1f, -5f);
            Vector2 rHand     = rElbow    + new Vector2( 1f, -5f);

            // Legs slightly bent, feet braced
            Vector2 lHip  = center + new Vector2(-4f,  8f);
            Vector2 rHip  = center + new Vector2( 4f,  8f);
            Vector2 lKnee = lHip   + new Vector2(-3f,  8f);
            Vector2 rKnee = rHip   + new Vector2( 3f,  8f);
            Vector2 lFoot = lKnee  + new Vector2(-1f,  7f);
            Vector2 rFoot = rKnee  + new Vector2( 1f,  7f);

            DrawFullBody(sb, assets, center, facing, 0f,
                lShoulder, lElbow, lHand,
                rShoulder, rElbow, rHand,
                lHip, lKnee, lFoot,
                rHip, rKnee, rFoot);
        }

        private static void DrawSwinging(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float t)
        {
            // Body angled with swing momentum, arms up gripping hook
            float swing = AnimationClock.Sway(5f, 0.6f);

            Vector2 lShoulder = center + new Vector2(-6f + swing * 0.2f, -12f);
            Vector2 rShoulder = center + new Vector2( 6f + swing * 0.2f, -12f);
            Vector2 lElbow    = lShoulder + new Vector2(-3f + swing * 0.3f, -5f);
            Vector2 rElbow    = rShoulder + new Vector2( 3f + swing * 0.3f, -5f);
            Vector2 lHand     = lElbow    + new Vector2(-2f, -5f);
            Vector2 rHand     = rElbow    + new Vector2( 2f, -5f);

            // Legs trailing behind swing
            Vector2 lHip  = center + new Vector2(-4f,  8f);
            Vector2 rHip  = center + new Vector2( 4f,  8f);
            Vector2 lKnee = lHip   + new Vector2(-2f - swing * 0.3f, 9f);
            Vector2 rKnee = rHip   + new Vector2( 2f - swing * 0.3f, 9f);
            Vector2 lFoot = lKnee  + new Vector2(-1f, 8f);
            Vector2 rFoot = rKnee  + new Vector2( 1f, 8f);

            DrawFullBody(sb, assets, center, facing, 0f,
                lShoulder, lElbow, lHand,
                rShoulder, rElbow, rHand,
                lHip, lKnee, lFoot,
                rHip, rKnee, rFoot);
        }

        private static void DrawWallJumping(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float t)
        {
            // Player is kicking off a wall: body angled away, one leg extended toward wall,
            // arms spread for balance, slight upward lean.
            // facing is already set to the kick-away direction by PlayerController.

            // Torso leans slightly upward/away from wall
            float leanX = facing * 2f;

            // Arms: one arm back toward wall, one arm forward for balance
            Vector2 lShoulder = center + new Vector2(-6f + leanX, -11f);
            Vector2 rShoulder = center + new Vector2( 6f + leanX, -11f);

            // Arm toward wall (back arm) — reaches back
            Vector2 wallArmShoulder = facing > 0 ? lShoulder : rShoulder;
            Vector2 freeArmShoulder = facing > 0 ? rShoulder : lShoulder;

            Vector2 wallElbow = wallArmShoulder + new Vector2(-facing * 6f,  3f);
            Vector2 wallHand  = wallElbow       + new Vector2(-facing * 5f,  2f);
            Vector2 freeElbow = freeArmShoulder + new Vector2( facing * 4f, -4f);
            Vector2 freeHand  = freeElbow       + new Vector2( facing * 3f, -4f);

            // Assign back to l/r for DrawFullBody
            Vector2 lElbow, lHand, rElbow, rHand;
            if (facing > 0)
            {
                lElbow = wallElbow; lHand = wallHand;
                rElbow = freeElbow; rHand = freeHand;
            }
            else
            {
                lElbow = freeElbow; lHand = freeHand;
                rElbow = wallElbow; rHand = wallHand;
            }

            // Legs: wall-side leg bent and pushing off, free leg extended outward
            Vector2 lHip  = center + new Vector2(-4f + leanX,  8f);
            Vector2 rHip  = center + new Vector2( 4f + leanX,  8f);

            // Wall-side leg (bent, pushing off)
            Vector2 wallHip  = facing > 0 ? lHip  : rHip;
            Vector2 freeHip  = facing > 0 ? rHip  : lHip;
            Vector2 wallKnee = wallHip + new Vector2(-facing * 5f,  6f);
            Vector2 wallFoot = wallKnee + new Vector2(-facing * 4f, -3f); // foot toward wall
            Vector2 freeKnee = freeHip  + new Vector2( facing * 3f, 10f);
            Vector2 freeFoot = freeKnee + new Vector2( facing * 2f,  7f);

            Vector2 lKnee, lFoot, rKnee, rFoot;
            if (facing > 0)
            {
                lKnee = wallKnee; lFoot = wallFoot;
                rKnee = freeKnee; rFoot = freeFoot;
            }
            else
            {
                lKnee = freeKnee; lFoot = freeFoot;
                rKnee = wallKnee; rFoot = wallFoot;
            }

            DrawFullBody(sb, assets, center, facing, 0f,
                lShoulder, lElbow, lHand,
                rShoulder, rElbow, rHand,
                lHip, lKnee, lFoot,
                rHip, rKnee, rFoot);
        }

        private static void DrawMantling(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float t)
        {
            // Pull-up animation: player is hauling themselves over a ledge.
            // Arms are raised and bent, pulling the body upward.
            // Legs dangle below, knees bent as if pushing off the wall.

            // Arms raised high, elbows bent outward — gripping the ledge above
            Vector2 lShoulder = center + new Vector2(-6f, -14f);
            Vector2 rShoulder = center + new Vector2( 6f, -14f);
            Vector2 lElbow    = lShoulder + new Vector2(-5f, -4f);
            Vector2 rElbow    = rShoulder + new Vector2( 5f, -4f);
            Vector2 lHand     = lElbow    + new Vector2(-2f, -5f); // hands at ledge level
            Vector2 rHand     = rElbow    + new Vector2( 2f, -5f);

            // Legs dangling: knees bent, feet trailing below
            Vector2 lHip  = center + new Vector2(-4f, 10f);
            Vector2 rHip  = center + new Vector2( 4f, 10f);
            Vector2 lKnee = lHip   + new Vector2(-3f,  8f);
            Vector2 rKnee = rHip   + new Vector2( 3f,  8f);
            Vector2 lFoot = lKnee  + new Vector2( 1f,  7f);
            Vector2 rFoot = rKnee  + new Vector2(-1f,  7f);

            DrawFullBody(sb, assets, center, facing, 0f,
                lShoulder, lElbow, lHand,
                rShoulder, rElbow, rHand,
                lHip, lKnee, lFoot,
                rHip, rKnee, rFoot);
        }

        // ── Launching (2.4) ────────────────────────────────────────────────────

        private static void DrawLaunching(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float t, float launchSpeedPx)
        {
            // Body posture: arms swept back, legs trailing — like a rocket launch
            Vector2 lShoulder = center + new Vector2(-6f, -12f);
            Vector2 rShoulder = center + new Vector2( 6f, -12f);
            // Arms swept back (opposite to facing direction)
            float armSweep = facing * 8f;
            Vector2 lElbow = lShoulder + new Vector2(-armSweep * 0.5f,  5f);
            Vector2 rElbow = rShoulder + new Vector2(-armSweep * 0.5f,  5f);
            Vector2 lHand  = lElbow    + new Vector2(-armSweep * 0.6f,  4f);
            Vector2 rHand  = rElbow    + new Vector2(-armSweep * 0.6f,  4f);

            // Legs trailing behind
            Vector2 lHip  = center + new Vector2(-4f,  8f);
            Vector2 rHip  = center + new Vector2( 4f,  8f);
            Vector2 lKnee = lHip   + new Vector2(-facing * 2f, 9f);
            Vector2 rKnee = rHip   + new Vector2(-facing * 2f, 9f);
            Vector2 lFoot = lKnee  + new Vector2(-facing * 3f, 6f);
            Vector2 rFoot = rKnee  + new Vector2(-facing * 3f, 6f);

            // Slight forward lean
            float lean = facing * 3f;
            DrawFullBody(sb, assets, center + new Vector2(lean, 0f), facing, 0f,
                lShoulder, lElbow, lHand,
                rShoulder, rElbow, rHand,
                lHip, lKnee, lFoot,
                rHip, rKnee, rFoot);

            // ── Speed lines ────────────────────────────────────────────────────
            // Draw 4–6 horizontal streaks behind the player, fading with alpha.
            // Intensity scales with launch speed (capped at 300 px/s for full effect).
            float intensity = MathHelper.Clamp(launchSpeedPx / 300f, 0.2f, 1.0f);
            int lineCount = (int)(4 + intensity * 2f); // 4–6 lines
            var lineColor = new Color(180, 220, 255, (int)(160 * intensity));

            for (int i = 0; i < lineCount; i++)
            {
                // Stagger lines vertically around the player center
                float yOffset = (i - lineCount * 0.5f) * 7f + AnimationClock.Sway(2f, 0.8f + i * 0.15f);
                float lineLen = (30f + i * 12f) * intensity;
                float xStart  = center.X - facing * 8f;  // start just behind player
                float xEnd    = xStart   - facing * lineLen;

                GeometryBatch.DrawLine(sb, assets,
                    new Vector2(xStart, center.Y + yOffset),
                    new Vector2(xEnd,   center.Y + yOffset),
                    lineColor, 1f + i * 0.3f);
            }
        }

        private static void DrawStunned(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float t)
        {
            // Wobbly head, arms limp, orbiting stars
            float wobble = AnimationClock.Sway(4f, 3.0f);

            Vector2 lShoulder = center + new Vector2(-6f, -10f);
            Vector2 rShoulder = center + new Vector2( 6f, -10f);
            // Arms limp and drooping
            Vector2 lElbow = lShoulder + new Vector2(-5f,  9f);
            Vector2 rElbow = rShoulder + new Vector2( 5f,  9f);
            Vector2 lHand  = lElbow    + new Vector2(-3f,  7f);
            Vector2 rHand  = rElbow    + new Vector2( 3f,  7f);

            Vector2 lHip  = center + new Vector2(-4f,  8f);
            Vector2 rHip  = center + new Vector2( 4f,  8f);
            Vector2 lKnee = lHip   + new Vector2(-2f, 10f);
            Vector2 rKnee = rHip   + new Vector2( 2f, 10f);
            Vector2 lFoot = lKnee  + new Vector2(-1f,  8f);
            Vector2 rFoot = rKnee  + new Vector2( 1f,  8f);

            DrawFullBody(sb, assets, center, facing, wobble,
                lShoulder, lElbow, lHand,
                rShoulder, rElbow, rHand,
                lHip, lKnee, lFoot,
                rHip, rKnee, rFoot);

            // Orbiting stun stars above head
            Vector2 headCenter = center + new Vector2(wobble, -22f);
            for (int i = 0; i < 3; i++)
            {
                float angle = t * 4f + i * (MathF.PI * 2f / 3f);
                float orbitR = 10f;
                Vector2 starPos = headCenter + new Vector2(
                    MathF.Cos(angle) * orbitR,
                    MathF.Sin(angle) * orbitR * 0.5f - 4f);
                // Draw 4-pointed star as two crossed lines
                GeometryBatch.DrawLine(sb, assets,
                    starPos + new Vector2(-3f, 0f),
                    starPos + new Vector2( 3f, 0f),
                    ColStar, 2f);
                GeometryBatch.DrawLine(sb, assets,
                    starPos + new Vector2(0f, -3f),
                    starPos + new Vector2(0f,  3f),
                    ColStar, 2f);
            }
        }

        private static void DrawDead(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing)
        {
            // Collapsed body: torso horizontal, limbs splayed
            // Torso lies flat
            int tx = (int)(center.X - TorsoH / 2f);
            int ty = (int)(center.Y + 8f);
            assets.DrawRect(sb, new Rectangle(tx, ty, (int)TorsoH, (int)TorsoW / 2), ColDeadSuit);
            assets.DrawRectOutline(sb, new Rectangle(tx, ty, (int)TorsoH, (int)TorsoW / 2), ColDead, 1);

            // Head to the side
            GeometryBatch.DrawCircleApprox(sb, assets,
                center + new Vector2(facing * (TorsoH / 2f + HeadR - 2f), 10f),
                HeadR, ColHelmet, 10);

            // Limbs splayed
            Vector2 torsoCenter = center + new Vector2(0f, 12f);
            // Arms
            GeometryBatch.DrawLine(sb, assets,
                torsoCenter + new Vector2(-4f, 0f),
                torsoCenter + new Vector2(-14f, -6f),
                ColDeadSuit, LimbThick);
            GeometryBatch.DrawLine(sb, assets,
                torsoCenter + new Vector2( 4f, 0f),
                torsoCenter + new Vector2( 14f, -6f),
                ColDeadSuit, LimbThick);
            // Legs
            GeometryBatch.DrawLine(sb, assets,
                torsoCenter + new Vector2(-3f, 4f),
                torsoCenter + new Vector2(-8f, 14f),
                ColDeadSuit, LimbThick);
            GeometryBatch.DrawLine(sb, assets,
                torsoCenter + new Vector2( 3f, 4f),
                torsoCenter + new Vector2( 8f, 14f),
                ColDeadSuit, LimbThick);
        }

        // ── Core body drawing ──────────────────────────────────────────────────

        /// <summary>
        /// Draws the full articulated body given pre-computed joint positions.
        /// Order: back arm → back leg → torso → backpack → front leg → front arm → head → lantern.
        /// </summary>
        private static void DrawFullBody(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float breathY,
            Vector2 lShoulder, Vector2 lElbow, Vector2 lHand,
            Vector2 rShoulder, Vector2 rElbow, Vector2 rHand,
            Vector2 lHip, Vector2 lKnee, Vector2 lFoot,
            Vector2 rHip, Vector2 rKnee, Vector2 rFoot)
        {
            // Determine which arm/leg is "back" (behind torso) based on facing
            // facing +1 = right: left side is back, right side is front
            // facing -1 = left: right side is back, left side is front
            bool leftIsFront = facing < 0;

            // ── Back limbs ─────────────────────────────────────────────────────
            if (leftIsFront)
            {
                // Right arm is back
                DrawArm(sb, assets, rShoulder, rElbow, rHand, ColSuitShade);
                // Right leg is back
                DrawLeg(sb, assets, rHip, rKnee, rFoot, ColSuitShade);
            }
            else
            {
                // Left arm is back
                DrawArm(sb, assets, lShoulder, lElbow, lHand, ColSuitShade);
                // Left leg is back
                DrawLeg(sb, assets, lHip, lKnee, lFoot, ColSuitShade);
            }

            // ── Torso ──────────────────────────────────────────────────────────
            DrawTorso(sb, assets, center, facing, breathY);

            // ── Backpack (on back side) ────────────────────────────────────────
            DrawBackpack(sb, assets, center, facing, breathY);

            // ── Front limbs ────────────────────────────────────────────────────
            if (leftIsFront)
            {
                // Left leg is front
                DrawLeg(sb, assets, lHip, lKnee, lFoot, ColSuit);
                // Left arm is front
                DrawArm(sb, assets, lShoulder, lElbow, lHand, ColSuit);
            }
            else
            {
                // Right leg is front
                DrawLeg(sb, assets, rHip, rKnee, rFoot, ColSuit);
                // Right arm is front
                DrawArm(sb, assets, rShoulder, rElbow, rHand, ColSuit);
            }

            // ── Head ───────────────────────────────────────────────────────────
            DrawHead(sb, assets, center, facing, breathY);

            // ── Lantern (held in front hand) ───────────────────────────────────
            Vector2 lanternPos = leftIsFront ? lHand : rHand;
            DrawLantern(sb, assets, lanternPos);
        }

        // ── Sub-component drawers ──────────────────────────────────────────────

        private static void DrawTorso(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float breathY)
        {
            // Main torso block
            int tx = (int)(center.X - TorsoW / 2f);
            int ty = (int)(center.Y - 16f + breathY);
            var torsoRect = new Rectangle(tx, ty, (int)TorsoW, (int)TorsoH);
            assets.DrawRect(sb, torsoRect, ColSuit);

            // Highlight strip on front side
            int hlX = facing > 0 ? tx + (int)TorsoW - 4 : tx;
            assets.DrawRect(sb, new Rectangle(hlX, ty + 2, 3, (int)TorsoH - 4), ColSuitHighlight);
// Shadow strip on back side
int shX = facing > 0 ? tx : tx + (int)TorsoW - 3;
assets.DrawRect(sb, new Rectangle(shX, ty + 2, 3, (int)TorsoH - 4), ColSuitShade);

// Belt line
assets.DrawRect(sb, new Rectangle(tx, ty + (int)TorsoH - 5, (int)TorsoW, 2), ColSuitShade);
}

private static void DrawBackpack(SpriteBatch sb, AssetManager assets,
Vector2 center, int facing, float breathY)
{
// Backpack sits on the back side of the torso
int packW = 8;
int packH = 12;
int packX = facing > 0
    ? (int)(center.X - TorsoW / 2f) - packW + 1
    : (int)(center.X + TorsoW / 2f) - 1;
int packY = (int)(center.Y - 14f + breathY);

assets.DrawRect(sb, new Rectangle(packX, packY, packW, packH), ColPack);
assets.DrawRectOutline(sb, new Rectangle(packX, packY, packW, packH), ColPackStrap, 1);

// Strap lines
assets.DrawRect(sb, new Rectangle(packX + 1, packY + 2, packW - 2, 1), ColPackStrap);
assets.DrawRect(sb, new Rectangle(packX + 1, packY + 6, packW - 2, 1), ColPackStrap);
}

private static void DrawHead(SpriteBatch sb, AssetManager assets,
Vector2 center, int facing, float breathY)
{
Vector2 headCenter = center + new Vector2(0f, -22f + breathY);

// Helmet shell
GeometryBatch.DrawCircleApprox(sb, assets, headCenter, HeadR, ColHelmet, 12);

// Visor (front half of head)
// Draw as a filled arc approximation: 3 small rects
int visorX = facing > 0
    ? (int)(headCenter.X)
    : (int)(headCenter.X - HeadR);
assets.DrawRect(sb,
    new Rectangle(visorX, (int)(headCenter.Y - 4f), (int)HeadR, 8),
    ColVisor);

// Visor shine (small highlight)
assets.DrawRect(sb,
    new Rectangle(visorX + 1, (int)(headCenter.Y - 3f), 2, 3),
    ColVisorShine);

// Helmet outline
GeometryBatch.DrawCircleOutline(sb, assets, headCenter, HeadR, ColSuitShade, 12);
}

private static void DrawArm(SpriteBatch sb, AssetManager assets,
Vector2 shoulder, Vector2 elbow, Vector2 hand, Color color)
{
// Upper arm
GeometryBatch.DrawLine(sb, assets, shoulder, elbow, color, LimbThick);
// Forearm (slightly thinner)
GeometryBatch.DrawLine(sb, assets, elbow, hand, color, LimbThickT);
// Hand dot
assets.DrawRect(sb,
    new Rectangle((int)(hand.X - 2f), (int)(hand.Y - 2f), 4, 4),
    ColSkin);
}

private static void DrawLeg(SpriteBatch sb, AssetManager assets,
Vector2 hip, Vector2 knee, Vector2 foot, Color color)
{
// Thigh
GeometryBatch.DrawLine(sb, assets, hip, knee, color, LimbThick);
// Shin (slightly thinner)
GeometryBatch.DrawLine(sb, assets, knee, foot, color, LimbThickT);
// Boot
assets.DrawRect(sb,
    new Rectangle((int)(foot.X - 3f), (int)(foot.Y - 1f), 6, 4),
    ColSuitShade);
}

private static void DrawLantern(SpriteBatch sb, AssetManager assets, Vector2 pos)
{
float t = AnimationClock.Time;
float pulse = AnimationClock.Pulse(1.2f);

// Glow halo (large, very transparent)
int glowR = (int)(14f + pulse * 4f);
GeometryBatch.DrawCircleApprox(sb, assets, pos,
    glowR, ColLanternGlow * 0.3f, 10);

// Medium glow
GeometryBatch.DrawCircleApprox(sb, assets, pos,
    (int)(8f + pulse * 2f), ColLanternGlow * 0.5f, 8);

// Lantern body (diamond shape)
GeometryBatch.DrawDiamond(sb, assets, pos, 4f, ColLanternBody);

// Lantern outline
GeometryBatch.DrawDiamondOutline(sb, assets, pos, 4f, new Color(255, 200, 80), 1f);

// Beam cone in facing direction (drawn as a dashed line)
// (lantern always points slightly forward/down)
GeometryBatch.DrawLine(sb, assets,
    pos,
    pos + new Vector2(0f, 12f),
    ColLanternBeam, 4f);
}

        // ── Throw-flare stance ─────────────────────────────────────────────────

        private static void DrawThrowingFlare(SpriteBatch sb, AssetManager assets,
            Vector2 center, int facing, float t)
        {
            float breathY = AnimationClock.Sway(0.4f, 0.3f); // subtle tension breath

            // Front arm: raised and extended forward-up (ready to throw)
            // Back arm: braced slightly behind for balance
            Vector2 fShoulder = center + new Vector2(facing *  6f, -11f + breathY);
            Vector2 fElbow    = fShoulder + new Vector2(facing *  5f, -7f);
            Vector2 fHand     = fElbow    + new Vector2(facing *  5f, -4f);

            Vector2 bShoulder = center + new Vector2(-facing * 5f, -10f + breathY);
            Vector2 bElbow    = bShoulder + new Vector2(-facing * 2f,  6f);
            Vector2 bHand     = bElbow    + new Vector2(-facing * 2f,  5f);

            // Wide balanced stance
            Vector2 lHip  = center + new Vector2(-5f,  8f + breathY);
            Vector2 rHip  = center + new Vector2( 5f,  8f + breathY);
            Vector2 lKnee = lHip + new Vector2(-2f, 10f);
            Vector2 rKnee = rHip + new Vector2( 2f, 10f);
            Vector2 lFoot = lKnee + new Vector2(-2f,  8f);
            Vector2 rFoot = rKnee + new Vector2( 2f,  8f);

            // Map front/back to left/right for DrawFullBody
            Vector2 lShoulder, lElbow, lHand, rShoulder, rElbow, rHand;
            if (facing > 0)
            {
                rShoulder = fShoulder; rElbow = fElbow; rHand = fHand;
                lShoulder = bShoulder; lElbow = bElbow; lHand = bHand;
            }
            else
            {
                lShoulder = fShoulder; lElbow = fElbow; lHand = fHand;
                rShoulder = bShoulder; rElbow = bElbow; rHand = bHand;
            }

            DrawFullBody(sb, assets, center, facing, breathY,
                lShoulder, lElbow, lHand,
                rShoulder, rElbow, rHand,
                lHip, lKnee, lFoot,
                rHip, rKnee, rFoot);

            // Draw held flare over the lantern at the front hand
            DrawHeldFlare(sb, assets, fHand);
        }

        private static void DrawHeldFlare(SpriteBatch sb, AssetManager assets, Vector2 pos)
        {
            float pulse = AnimationClock.Pulse(3f);

            // Glow aura
            GeometryBatch.DrawCircleApprox(sb, assets, pos,
                (int)(6f + pulse * 2f), new Color(255, 180, 60, 80), 8);

            // Casing body
            assets.DrawRect(sb, new Rectangle((int)pos.X - 2, (int)pos.Y - 1, 5, 2),
                new Color(210, 130, 50));

            // Bright burning cap
            assets.DrawRect(sb, new Rectangle((int)pos.X + 2, (int)pos.Y - 1, 2, 2),
                new Color(255, 230, 80));
        }
}
}

