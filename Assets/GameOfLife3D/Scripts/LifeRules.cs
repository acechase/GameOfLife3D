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
        Bays4555,   // S45/B5  — the classic "3D Life" (Carter Bays, 1987). Gliders exist.
        Bays5766,   // S567/B6 — Bays' second Life candidate. Blockier, slower decay.
        Clouds,     // S13-26/B13-14 — grows dense billowing solids. Mesmerizing in AR.
        Conway2D,   // S23/B3  — the original. Use with gridSize.z = 1 for a flat slab.
        Custom,     // Use the custom birth/survive strings on LifeVolume.
    }

    public static class LifeRules
    {
        public static void GetMasks(RulePreset preset, out int birth, out int survive)
        {
            switch (preset)
            {
                case RulePreset.Bays4555: birth = Mask(5);       survive = Mask(4, 5);       break;
                case RulePreset.Bays5766: birth = Mask(6);       survive = Mask(5, 6, 7);    break;
                case RulePreset.Clouds:   birth = MaskRange(13, 14); survive = MaskRange(13, 26); break;
                case RulePreset.Conway2D: birth = Mask(3);       survive = Mask(2, 3);       break;
                default:                  birth = 0;             survive = 0;                break;
            }
        }

        /// <summary>Recommended random-seed density for each rule's character.</summary>
        public static float DefaultDensity(RulePreset preset)
        {
            switch (preset)
            {
                case RulePreset.Clouds:   return 0.50f;
                case RulePreset.Conway2D: return 0.30f;
                default:                  return 0.35f;
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
