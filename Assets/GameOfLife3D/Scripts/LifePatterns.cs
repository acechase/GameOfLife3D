using UnityEngine;

namespace GameOfLife3D
{
    /// <summary>
    /// A named starting configuration, in cell coordinates relative to its own
    /// corner. Stamped into the grid by <see cref="LifeVolume.StampPattern"/>.
    /// </summary>
    public struct LifePattern
    {
        public string name;
        public Vector3Int[] cells;
        /// <summary>Rule this pattern is defined under — stamping switches to it.</summary>
        public RulePreset rule;
        /// <summary>Needs a single-layer grid (z = 1) to behave as designed.</summary>
        public bool flat;
        /// <summary>
        /// Grid shape to stage this pattern in; zero keeps whatever is current.
        ///
        /// Patterns are fussy about room in both directions. The Gosper gun is
        /// destroyed by its own boundary below 80x80 (measured — see the note
        /// on it), while a 10-cell spaceship in a 96-cube renders as a speck,
        /// since the volume always spans the same metres however many cells
        /// are in it.
        /// </summary>
        public Vector3Int grid;
        /// <summary>
        /// Torus edges. True for travelers, so they fly forever instead of
        /// dying against a wall; false for guns, whose own output would
        /// otherwise wrap around and crash back into them.
        /// </summary>
        public bool wrap;
        public string note;
    }

    /// <summary>
    /// Patterns that actually do something, as opposed to random soup.
    ///
    /// Every one of these was verified against the Python reference in
    /// Validation~ rather than copied from memory: the 2D patterns for stable
    /// population / growth, and the 3D spaceship by replaying it for six full
    /// periods and checking it reproduces itself exactly, translated.
    ///
    /// Worth knowing before hunting for more: a search of 3520 random starting
    /// blobs found NO spaceships under Pyroclastic or Coral. Those rules are
    /// turbulent — they sustain activity but not coherent traveling structures.
    /// Bays5766 has one; Bays4555's published glider did not turn up in the
    /// same search, so it likely needs a hand-built configuration.
    /// </summary>
    public static class LifePatterns
    {
        static Vector3Int[] V(params int[] xyz)
        {
            var cells = new Vector3Int[xyz.Length / 3];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = new Vector3Int(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]);
            return cells;
        }

        /// <summary>2D cells, laid into the z = 0 plane of a flat grid.</summary>
        static Vector3Int[] Flat(params int[] xy)
        {
            var cells = new Vector3Int[xy.Length / 2];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = new Vector3Int(xy[i * 2], xy[i * 2 + 1], 0);
            return cells;
        }

        public static readonly LifePattern[] All =
        {
            new LifePattern
            {
                name = "3D Spaceship (Bays 5766)",
                rule = RulePreset.Bays5766,
                flat = false,
                wrap = true,
                grid = new Vector3Int(32, 32, 32),   // small, so 10 cells read large
                note = "10 cells, period 4, travels (-1, 0, +1) — a genuine 3D glider.",
                cells = V(
                    0,0,0,  0,0,1,  0,1,0,  0,1,1,
                    1,0,1,  1,0,2,  1,1,1,  1,1,2,
                    2,0,0,  2,1,0),
            },
            new LifePattern
            {
                name = "Gosper Glider Gun",
                rule = RulePreset.Conway2D,
                flat = true,
                wrap = false,
                // Measured in the Python reference: at 48^2 and 64^2 the gun is
                // destroyed within ~150 generations, because its own boundary
                // is too close and the debris walks back into the mechanism. At
                // 80^2 and up it keeps emitting indefinitely. 96 leaves margin
                // without shrinking the cells so far that the gun is hard to see.
                grid = new Vector3Int(96, 96, 1),
                note = "Emits a glider every 30 generations, forever. The clearest " +
                       "'things are being built and traveling' pattern there is.",
                cells = Flat(
                    0,4,   0,5,   1,4,   1,5,
                    10,4,  10,5,  10,6,  11,3,  11,7,
                    12,2,  12,8,  13,2,  13,8,
                    14,5,  15,3,  15,7,
                    16,4,  16,5,  16,6,  17,5,
                    20,2,  20,3,  20,4,  21,2,  21,3,  21,4,
                    22,1,  22,5,  24,0,  24,1,  24,5,  24,6,
                    34,2,  34,3,  35,2,  35,3),
            },
            new LifePattern
            {
                name = "Glider (Conway)",
                rule = RulePreset.Conway2D,
                flat = true,
                wrap = true,
                grid = new Vector3Int(48, 48, 1),
                note = "The original. 5 cells, period 4, travels diagonally forever.",
                cells = Flat(1,0,  2,1,  0,2,  1,2,  2,2),
            },
            new LifePattern
            {
                name = "Lightweight Spaceship",
                rule = RulePreset.Conway2D,
                flat = true,
                wrap = true,
                grid = new Vector3Int(48, 48, 1),
                note = "Period 4, travels 2 cells straight along x.",
                cells = Flat(0,1, 3,1, 4,2, 0,3, 4,3, 1,4, 2,4, 3,4, 4,4),
            },
        };

        public static LifePattern Get(int index) => All[((index % All.Length) + All.Length) % All.Length];

        /// <summary>Bounding-box size of a pattern, in cells.</summary>
        public static Vector3Int Extent(in LifePattern p)
        {
            Vector3Int max = p.cells[0];
            foreach (Vector3Int c in p.cells) max = Vector3Int.Max(max, c);
            return max + Vector3Int.one;
        }
    }
}
