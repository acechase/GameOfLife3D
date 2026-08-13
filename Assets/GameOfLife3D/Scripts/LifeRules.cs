using System;

namespace GameOfLife3D
{
    /// <summary>
    /// Rule presets for the 3D cellular automaton, expressed as bitmasks over
    /// live-neighbor counts (bit n set = the rule applies at n neighbors).
    /// 26-neighbor (3D Moore) neighborhood; counts range 0..26.
    /// </summary>
    public enum RulePreset
    {
        Pyroclastic, // S4-7/B6-8, 10 states — sustained churning fronts. The default.
        Coral,       // S5-8/B6-7, 4 states — denser, slower, reef-like growth.
        Bays4555,    // S45/B5  — Carter Bays' 1987 "3D Life". Gliders exist, but
                     // random soup always dies out. Interesting, not self-sustaining.
        Bays5766,    // S567/B6 — Bays' second candidate. Same caveat.
        Clouds,      // S13-26/B13-14 — see the note in DefaultDensity: this one is
                     // knife-edge and mostly dies. Kept for rule-safari purposes.
        Conway2D,    // S23/B3  — the original. Use with gridSize.z = 1 for a flat slab.
        Custom,      // Use the custom birth/survive strings on LifeVolume.
    }

    public static class LifeRules
    {
        public static void GetMasks(RulePreset preset, out int birth, out int survive)
        {
            switch (preset)
            {
                case RulePreset.Pyroclastic: birth = MaskRange(6, 8); survive = MaskRange(4, 7); break;
                case RulePreset.Coral:    birth = MaskRange(6, 7); survive = MaskRange(5, 8);   break;
                case RulePreset.Bays4555: birth = Mask(5);       survive = Mask(4, 5);       break;
                case RulePreset.Bays5766: birth = Mask(6);       survive = Mask(5, 6, 7);    break;
                case RulePreset.Clouds:   birth = MaskRange(13, 14); survive = MaskRange(13, 26); break;
                case RulePreset.Conway2D: birth = Mask(3);       survive = Mask(2, 3);       break;
                default:                  birth = 0;             survive = 0;                break;
            }
        }

        /// <summary>
        /// Number of cell states. 2 = plain binary (a cell that fails to
        /// survive vanishes). Higher values give the cell a refractory decay
        /// shell: it lingers for N-2 generations, cannot be reborn while it
        /// lingers, and is not counted as a live neighbor.
        ///
        /// That shell is what makes a 3D rule sustain. Without it, activity
        /// either dies out or degenerates into dense boiling; with it, the
        /// dead cells block immediate rebirth and the activity organizes into
        /// propagating fronts. The published 3D rules are all defined this way.
        /// </summary>
        public static int DefaultStates(RulePreset preset)
        {
            switch (preset)
            {
                case RulePreset.Pyroclastic: return 10;
                case RulePreset.Coral:       return 4;
                default:                     return 2;   // binary, as originally built
            }
        }

        /// <summary>
        /// Recommended random-seed density for each rule's character.
        ///
        /// These were measured, not guessed: each rule was run from random soup
        /// in the Python reference across densities and grid sizes, scoring for
        /// a see-through population that keeps changing. See Validation~.
        /// </summary>
        public static float DefaultDensity(RulePreset preset)
        {
            switch (preset)
            {
                // Converges to the same attractor from anywhere in 0.06..0.25,
                // so this is a comfortable middle rather than a knife-edge.
                case RulePreset.Pyroclastic: return 0.15f;
                case RulePreset.Coral:       return 0.20f;

                // Bays' rules die out from soup at EVERY density; these are the
                // values that merely last the longest (~80 generations) before
                // collapsing to a few still lifes. 0.35 — the value this used
                // to use for both — gave each cell ~9 live neighbors when the
                // survival window tops out at 5, i.e. instant mass extinction.
                case RulePreset.Bays4555: return 0.15f;
                case RulePreset.Bays5766: return 0.20f;

                // Clouds has no good regime: below ~0.65 it goes extinct within
                // a hundred generations, and above that it saturates into a
                // frozen solid block. Seeded at the survivable end.
                case RulePreset.Clouds:   return 0.68f;

                case RulePreset.Conway2D: return 0.30f;
                default:                  return 0.20f;
            }
        }

        public static int Mask(params int[] counts)
        {
            int m = 0;
            foreach (int c in counts) m |= 1 << c;
            return m;
        }

        public static int MaskRange(int lo, int hi)
        {
            int m = 0;
            for (int c = lo; c <= hi; c++) m |= 1 << c;
            return m;
        }

        /// <summary>
        /// Parse strings like "5", "4,5", "13-26", "2,4-6" into a neighbor-count
        /// bitmask. Invalid fragments are ignored; counts clamp to 0..26.
        /// </summary>
        public static int ParseMask(string spec)
        {
            int m = 0;
            if (string.IsNullOrWhiteSpace(spec)) return m;
            foreach (string raw in spec.Split(','))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                int dash = part.IndexOf('-');
                if (dash > 0)
                {
                    if (int.TryParse(part.Substring(0, dash), out int lo) &&
                        int.TryParse(part.Substring(dash + 1), out int hi))
                    {
                        lo = Math.Clamp(lo, 0, 26);
                        hi = Math.Clamp(hi, 0, 26);
                        if (lo <= hi) m |= MaskRange(lo, hi);
                    }
                }
                else if (int.TryParse(part, out int c) && c >= 0 && c <= 26)
                {
                    m |= 1 << c;
                }
            }
            return m;
        }
    }
}
