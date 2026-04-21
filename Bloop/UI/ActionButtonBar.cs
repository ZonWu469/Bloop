using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Bloop.Core;
using Bloop.Entities;
using Bloop.Gameplay;
using Bloop.Rendering;

namespace Bloop.UI
{
    /// <summary>
    /// Horizontal action button bar drawn at the bottom-center of the screen.
    /// Replaces the plain text controls strip with visual key-cap buttons.
    /// Each button shows a key label, action name, and dims/pulses based on availability.
    /// </summary>
    public static class ActionButtonBar
    {
        // ── Layout ─────────────────────────────────────────────────────────────
        private const int ButtonW   = 52;
        private const int ButtonH   = 36;
        private const int ButtonGap = 4;
        private const int BarY      = 12;   // distance from bottom of screen

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color BgNormal    = new Color(12, 16, 22, 210);
        private static readonly Color BgReady     = new Color(20, 28, 38, 220);
        private static readonly Color BgDisabled  = new Color( 8, 10, 14, 160);
        private static readonly Color BorderNormal = new Color(50, 65, 80);
        private static readonly Color BorderReady  = new Color(80, 110, 140);
        private static readonly Color BorderPulse  = new Color(120, 180, 220);
        private static readonly Color KeyColor     = new Color(200, 220, 240);
        private static readonly Color KeyDisabled  = new Color( 80,  90, 100);
        private static readonly Color LabelColor   = new Color(130, 155, 175);
        private static readonly Color LabelDisabled= new Color( 55,  65,  75);
        private static readonly Color CooldownColor= new Color(180, 120, 220);

        // ── Button descriptor ──────────────────────────────────────────────────
        private readonly struct ButtonDef
        {
            public readonly string Key;
            public readonly string Label;
            public ButtonDef(string key, string label) { Key = key; Label = label; }
        }

        /// <summary>
        /// Draw the action button bar.
        /// </summary>
        /// <param name="playerState">Current player state for contextual visibility.</param>
        /// <param name="stats">Player stats (flare count, kinetic charge).</param>
        /// <param name="entityControl">Entity control system (cooldown, ready state).</param>
        /// <param name="vw">Virtual viewport width.</param>
        /// <param name="vh">Virtual viewport height.</param>
        public static void Draw(SpriteBatch sb, AssetManager assets,
            PlayerState playerState, PlayerStats stats,
            EntityControlSystem? entityControl,
            int vw, int vh)
        {
            bool isControlling = entityControl?.IsControlling ?? false;
            bool isSelecting   = entityControl?.IsSelecting   ?? false;
            bool entityReady   = entityControl?.IsReady       ?? false;
            float entityCooldown = entityControl?.CooldownTimer ?? 0f;

            // Define buttons based on current state
            ButtonDef[] buttons;
            bool[]      available;

            if (isControlling)
            {
                // Controlling an entity — show entity-specific controls
                buttons = new[]
                {
                    new ButtonDef("WASD",  "Move"),
                    new ButtonDef("E",     "Skill"),
                    new ButtonDef("RMB",   "Release"),
                };
                available = new[] { true, true, true };
            }
            else if (isSelecting)
            {
                // Selecting an entity
                buttons = new[]
                {
                    new ButtonDef("LMB",   "Select"),
                    new ButtonDef("Q/RMB", "Cancel"),
                };
                available = new[] { true, true };
            }
            else
            {
                // Normal gameplay
                bool canJump    = playerState != PlayerState.Dead && playerState != PlayerState.Stunned;
                bool canRappel  = playerState == PlayerState.Falling || playerState == PlayerState.Idle
                               || playerState == PlayerState.Walking;
                bool canClimb   = playerState != PlayerState.Dead;
                bool canGrapple = playerState != PlayerState.Dead && playerState != PlayerState.Stunned;
                bool canFlare   = stats.FlareCount > 0;
                bool canEntity  = entityReady && !isControlling;

                buttons = new[]
                {
                    new ButtonDef("Space",    "Jump"),
                    new ButtonDef("S+Space",  "Rappel"),
                    new ButtonDef("C",        "Climb"),
                    new ButtonDef("LMB",      "Grapple"),
                    new ButtonDef("F",        "Flare"),
                    new ButtonDef("Q",        "Entity"),
                    new ButtonDef("Tab",      "Bag"),
                    new ButtonDef("Esc",      "Pause"),
                };
                available = new[]
                {
                    canJump, canRappel, canClimb, canGrapple,
                    canFlare, canEntity, true, true
                };
            }

            int totalW = buttons.Length * ButtonW + (buttons.Length - 1) * ButtonGap;
            int startX = vw / 2 - totalW / 2;
            int barY   = vh - ButtonH - BarY;

            for (int i = 0; i < buttons.Length; i++)
            {
                int bx = startX + i * (ButtonW + ButtonGap);
                bool avail = available[i];

                // Background
                Color bg = avail ? BgReady : BgDisabled;
                assets.DrawRect(sb, new Rectangle(bx, barY, ButtonW, ButtonH), bg);

                // Border — pulse for entity button when ready
                Color border = BorderNormal;
                if (avail && buttons[i].Key == "Q" && entityReady)
                {
                    float pulse = AnimationClock.Pulse(2f);
                    border = Color.Lerp(BorderReady, BorderPulse, pulse);
                }
                else if (avail)
                {
                    border = BorderReady;
                }
                assets.DrawRectOutline(sb, new Rectangle(bx, barY, ButtonW, ButtonH), border, 1);

                // Key label (top half)
                Color keyCol = avail ? KeyColor : KeyDisabled;
                if (assets.GameFont != null)
                {
                    Vector2 keySize = assets.GameFont.MeasureString(buttons[i].Key) * 0.7f;
                    float keyX = bx + (ButtonW - keySize.X) / 2f;
                    float keyY = barY + 5f;
                    assets.DrawString(sb, buttons[i].Key, new Vector2(keyX, keyY), keyCol, 0.7f);
                }

                // Action label (bottom half)
                Color lblCol = avail ? LabelColor : LabelDisabled;
                if (assets.GameFont != null)
                {
                    Vector2 lblSize = assets.GameFont.MeasureString(buttons[i].Label) * 0.6f;
                    float lblX = bx + (ButtonW - lblSize.X) / 2f;
                    float lblY = barY + ButtonH - lblSize.Y - 5f;
                    assets.DrawString(sb, buttons[i].Label, new Vector2(lblX, lblY), lblCol, 0.6f);
                }

                // Entity button: cooldown sweep overlay
                if (buttons[i].Key == "Q" && !entityReady && entityCooldown > 0f)
                {
                    float progress = 1f - (entityCooldown / EntityControlSystem.GlobalCooldown);
                    // Draw cooldown fill from bottom
                    int fillH = (int)((1f - progress) * ButtonH);
                    if (fillH > 0)
                        assets.DrawRect(sb,
                            new Rectangle(bx, barY, ButtonW, fillH),
                            CooldownColor * 0.25f);

                    // Remaining time text
                    if (assets.GameFont != null)
                    {
                        string cdText = $"{entityCooldown:0}s";
                        Vector2 cdSize = assets.GameFont.MeasureString(cdText) * 0.65f;
                        assets.DrawString(sb, cdText,
                            new Vector2(bx + (ButtonW - cdSize.X) / 2f, barY + ButtonH / 2f - cdSize.Y / 2f),
                            CooldownColor * 0.9f, 0.65f);
                    }
                }

                // Flare button: show count as small dots
                if (buttons[i].Key == "F" && avail)
                {
                    int maxFlares = PlayerStats.MaxFlareCount;
                    float dotSpacing = (ButtonW - 8f) / maxFlares;
                    for (int d = 0; d < maxFlares; d++)
                    {
                        bool lit = d < stats.FlareCount;
                        float dx = bx + 4f + d * dotSpacing + dotSpacing / 2f;
                        float dy = barY + ButtonH - 4f;
                        Color dotCol = lit
                            ? Color.Lerp(new Color(200, 130, 40), new Color(255, 210, 80),
                                AnimationClock.Pulse(1.5f, d * 0.4f) * 0.5f)
                            : new Color(50, 40, 20);
                        assets.DrawRect(sb, new Rectangle((int)dx - 1, (int)dy - 1, 2, 2), dotCol);
                    }
                }
            }
        }
    }
}
