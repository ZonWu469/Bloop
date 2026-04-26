using System;
using System.Collections.Generic;
using Bloop.Generators;

namespace Bloop.Lore
{
    public static class LoreGenerator
    {
        // ── Theme data ─────────────────────────────────────────────────────────

        private static readonly string[][] ThemeAuthors =
        {
            // 0 — Lost Expedition
            new[] { "Dr. Harlan Voss", "Lt. Mira Calloway", "Cpl. Fenwick Gray", "Scout Eryn Dast", "Archivist Solen Marsh", "Field Lead Orin Pask" },
            // 1 — Ancient Ritual
            new[] { "Acolyte Vren", "High Keeper Ossun", "The Sealed Witness", "Brother Talim", "Initiate — Name Unreadable", "The Last Keeper" },
            // 2 — Cave Madness
            new[] { "unknown — handwriting unstable", "the one who stayed", "—", "a survivor (unverified)", "First Cartographer Loess", "the second one down" },
            // 3 — Void Corruption
            new[] { "Observer Mael Crenn", "Station Archivist", "Signal Origin: Unknown", "Research Log — Author Overwritten", "The Replaced One", "Monitor Unit 7" },
            // 4 — Forgotten Depth
            new[] { "Cartographer Ryn Ash", "The Last Sleeper", "Depth Recorder", "Caver Isolt Vane", "No One You Know", "The One Who Mapped" },
        };

        private static readonly string[][] ThemeTitles =
        {
            // 0 — Lost Expedition
            new[] { "Entry — Day 9", "Final Survey Notes", "Camp Three: Observations", "The Descent Log", "Something Follows", "Do Not Return", "Last Known Position" },
            // 1 — Ancient Ritual
            new[] { "The Thirteenth Shard", "Before the Convergence", "Words of the Deep Mouth", "Preparation Notes", "What Was Asked of Us", "The Rite Is Complete", "Fragment of the Circle" },
            // 2 — Cave Madness
            new[] { "I Can Hear Color", "The Walls Changed Again", "Day Unknown", "Counting Keeps Me Here", "Stop Reading This", "A Message for Whoever", "Still Here" },
            // 3 — Void Corruption
            new[] { "Contamination Report #4", "The Spread Cannot Be Measured", "Source Located", "Exposure Log", "Do Not Approach the Black Water", "Terminal Entry", "Null Reading" },
            // 4 — Forgotten Depth
            new[] { "Chart of the Unmapped", "Depth Minus Seven", "The Silence Between Stones", "Found in a Pocket", "Things the Deep Remembers", "This Was a City", "The Old Survey" },
        };

        // Each entry is a complete paragraph (2-3 sentences).
        private static readonly string[][] ThemeContents =
        {
            // 0 — Lost Expedition
            new[]
            {
                "The passage narrowed past the third chamber. We lost Fenwick somewhere in the dark between the crystal formations. I can still hear him tapping against the rock.",
                "The survey equipment stopped functioning at this depth. Numbers no longer behave correctly — a compass spun for eleven minutes before it broke. We have been here longer than the provisions allow.",
                "Three days without rest has made Calloway unreliable. She insists the walls breathe at night. I have started to believe her.",
                "The map we brought is wrong. Every corridor leads back to a version of the place we started. Harlan says we should stop drawing new ones.",
                "There is something in the water below. Not a fish. We have agreed not to speak of it anymore.",
                "I dropped my lantern on the fourth descent and sat in the dark for what felt like hours. When light returned, the others were gone. Their footprints continued without them.",
                "The cave system is larger than the surface survey suggested. Twice as large, maybe more. The exit we marked on the chart does not exist at the coordinates we recorded.",
            },
            // 1 — Ancient Ritual
            new[]
            {
                "The stones were placed in the formation the old text demanded. We were told the humming would stop when the last shard fell into place. It has not stopped.",
                "Ossun led us down past the second gate. There are markings on the walls that predate the order's founding by centuries. None of us can read them, and yet we understand.",
                "The ritual requires a witness who will not leave. I have been chosen. I do not think I was asked.",
                "Seven shards were found. Seven were embedded in the circle. The eighth was missing, and the circle is hungry.",
                "Something accepted our offering. I heard it accept. I wish I had not been present for that sound.",
                "The initiates are changed after the third chamber. Not harmed — changed. They no longer use their given names. They answer to numbers now, and seem content with this.",
                "The formation is complete but the door has not opened. Ossun says we did it correctly. He says this every hour. His voice does not carry certainty the way it used to.",
            },
            // 2 — Cave Madness
            new[]
            {
                "The dripping sounds like names now. I wrote them down. There are sixty-three names and I do not recognize any of them.",
                "My lantern went out for three seconds. In those three seconds I was somewhere else. I saw an open sky. I would like to go back.",
                "The geometry here is wrong in a way I cannot explain to someone who has not seen it. Trust your feet. Do not trust your eyes.",
                "I have been walking in a straight line for what must be an hour. I am back where I started. The scratch on the rock I made is fresh.",
                "Do not read this if you are alone. If you are alone, put it down and walk quickly, following the wall on your left until you reach the light.",
                "I found the exit twice and could not make myself go through it. It looked too much like something else. Third time now.",
                "Sleep is difficult here. Not impossible — I sleep. But when I wake the cave has rearranged itself slightly. Only slightly. Enough to notice. Not enough to be certain.",
            },
            // 3 — Void Corruption
            new[]
            {
                "The crystals in sector nine have changed color. Not a color I have a name for. The instruments don't register it. We are calling it null.",
                "Crenn stopped speaking on the fourth day. He still follows commands. He still eats. His eyes reflect things that are not in the room.",
                "The void does not consume. It substitutes. Everything it touches remains in place and is wrong in a way you only notice later.",
                "Three of us entered the lower chamber. One of us came out. I am not certain which one.",
                "The black water does not ripple when you throw stones into it. We stopped throwing stones.",
                "I have written this entry before. The handwriting in the previous pages matches mine but I have no memory of writing them. The dates are wrong. The observations are correct.",
                "The contamination has a smell. Not unpleasant. That is the part that worries me.",
            },
            // 4 — Forgotten Depth
            new[]
            {
                "The cavern floor here was worked. Not natural. The angles are too deliberate and the spacing too regular. Something lived here long enough to build.",
                "I have been mapping for nineteen days. The map is three hundred meters long. I have not reached an edge. I am not certain this place has one.",
                "The walls remember things. Stand still long enough and you will hear them. I recommend not standing still.",
                "This deep, the rock is warm. Not hot. Warm, like skin. I do not think about this when I can avoid it.",
                "Whatever built this left in a hurry. The tools are still here. So is something else that is not a tool.",
                "The architecture changes at depth. Earlier levels were built for something my size. Below that threshold, the corridors are taller. Much taller. I have not gone further.",
                "I found a room full of objects I cannot identify. None of them are broken. All of them have been used recently.",
            },
        };

        private static readonly string[][] ThemePortalHints =
        {
            // 0 — Lost Expedition
            new[] { "The cold air rises from above — the exit waits upward.", "Follow the draft. It does not lie.", "The survey markers ascend toward the upper tunnels.", "Our ropes still hang where we left them — they lead outward.", "Go toward the silence. The exit is where the dripping stops.", "The exit is above us. I am certain now." },
            // 1 — Ancient Ritual
            new[] { "The ritual chamber has a throat that opens upward.", "Pass through the eye of the stone and ascend.", "The formation points outward along its longest axis.", "Climb above the circle — the exit is above the altar.", "The opening we sealed is the only true way.", "The gate responds to completion — bring everything together." },
            // 2 — Cave Madness
            new[] { "Follow the left wall without stopping — the exit is at the end.", "The light you see in the distance is not a trick.", "Trust the cold — the exit is where it blows from.", "Above. Always above. You have been walking level for too long.", "The humming stops at the threshold.", "I found it once. It is where the walls stop moving." },
            // 3 — Void Corruption
            new[] { "The fracture above the chamber — the null-zone does not reach that high.", "Exit through the sealed passage before it closes further.", "The upper levels are clean. Ascend before the spread reaches them.", "The contamination has not reached the topmost shaft. Go now.", "The signal points upward and east of the central column.", "The void does not follow you up. That is the one direction it will not go." },
            // 4 — Forgotten Depth
            new[] { "The builders descended from above — the exit is the way they came.", "Look for the worked stone ceiling with the carved channel — the exit is above it.", "The original shaft is still intact in the northern section.", "Up through the finished corridor, not the raw cave.", "The exit was the entrance once. It still opens.", "The old maps show one way in and one way out. They are the same passage." },
        };

        // ── Theme names (for biome bias) ───────────────────────────────────────

        // theme index = f(seed, biome)
        private static int SelectTheme(int seed, BiomeTier biome)
        {
            // Biome restricts the candidate set; seed picks within it.
            int[] candidates = biome switch
            {
                BiomeTier.ShallowCaves   => new[] { 0, 4 },
                BiomeTier.FungalGrottos  => new[] { 4, 2 },
                BiomeTier.CrystalDepths  => new[] { 1, 3 },
                BiomeTier.TheAbyss       => new[] { 3, 2 },
                _                        => new[] { 0, 1, 2, 3, 4 },
            };
            var rng = new Random(seed);
            return candidates[rng.Next(candidates.Length)];
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Generate one LoreEntry per shard deterministically from the level seed.
        /// All entries in a level share the same theme but differ in title/content.
        /// </summary>
        public static List<LoreEntry> GenerateForLevel(int seed, int shardCount, BiomeTier biome)
        {
            int theme   = SelectTheme(seed, biome);
            var entries = new List<LoreEntry>(shardCount);

            string[] authors  = ThemeAuthors[theme];
            string[] titles   = ThemeTitles[theme];
            string[] contents = ThemeContents[theme];
            string[] hints    = ThemePortalHints[theme];

            for (int i = 0; i < shardCount; i++)
            {
                // Per-shard RNG seeded with a hash of (seed, index) so each shard
                // is independent but deterministic for a given (seed, shardIndex) pair.
                var rng = new Random(HashCode.Combine(seed, i));

                string title   = titles[rng.Next(titles.Length)];
                string author  = authors[rng.Next(authors.Length)];
                string content = contents[rng.Next(contents.Length)];
                string hint    = hints[rng.Next(hints.Length)];

                int sanityDelta = i switch
                {
                    0 => -15,
                    1 => -10,
                    2 => -10,
                    _ => (rng.Next(0, 3) == 0) ? +5 : -5,  // 1/3 chance of +5, else -5
                };

                // Void Corruption theme is harsher
                if (theme == 3 && i >= 3) sanityDelta = -10;

                entries.Add(new LoreEntry(title, author, content, hint, sanityDelta));
            }

            return entries;
        }
    }
}
