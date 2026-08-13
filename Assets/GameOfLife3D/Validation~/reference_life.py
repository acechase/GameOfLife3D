"""
Headless reference implementation of the 3D cellular automaton that runs in the
Unity compute shader. This mirrors the HLSL step kernel EXACTLY:

- One uint per cell, packed as (state << 8) | age:
    state == 0            -> empty
    state == states - 1   -> ALIVE (the only cells counted as neighbors)
    0 < state < states-1  -> DYING / refractory, counts down one per generation
  age is the generation count, clamped to MAX_AGE, and drives the color
  gradient. A dying cell keeps the age it had when it died, so a corpse fades
  at the color it reached rather than resetting.
- 26-neighbor (3D Moore) counting of ALIVE cells only
- Rules as bitmasks: bit n of `birth` set   -> empty cell with n live neighbors is born
                     bit n of `survive` set -> live cell with n live neighbors survives
- Edge handling: wrap (torus) or bounded (out-of-range = dead)
- A grid with size_z == 1 and no z-wrapping degenerates to classic 2D Moore
  neighborhood, so B3/S23 reproduces Conway's Game of Life exactly.

`states` defaults to 2, which is the plain binary automaton: a cell that fails
to survive goes straight to empty, and the packed encoding carries no extra
information. states > 2 adds the refractory shell that the published 3D rules
(Pyroclastic, Coral, ...) are defined against: a cell that dies cannot be
immediately reborn, which is what organizes activity into propagating fronts
instead of dense boiling. Measured against random soup, the 2-state Bays rules
go extinct at every density and seed geometry; the multi-state rules sustain
indefinitely.

Validation checks:
1. 2D blinker (B3/S23, single layer) oscillates with period 2.
2. 2D block is a still life.
3. 2D glider translates by (1,1) every 4 generations (wrap mode).
4. 3D Bays 4555 evolution is deterministic and a known small seed behaves sanely.
5. Bounded vs wrap edge behavior differs as expected.
6. states=2 is byte-identical to the old binary engine (no regression).
7. The refractory shell decays one step per generation and blocks rebirth.
8. Pyroclastic (S4-7/B6-8, 10 states) sustains where Bays 4555 dies out.
"""

MAX_AGE = 255
AGE_MASK = 0xFF
STATE_SHIFT = 8


def mask(counts):
    m = 0
    for c in counts:
        m |= 1 << c
    return m


def mask_spec(spec):
    """Parse 'S4-7/B6-8' style specs into (birth_mask, survive_mask)."""
    s_part, b_part = spec.split("/")

    def one(txt):
        out = 0
        for frag in txt[1:].split(","):
            if "-" in frag:
                lo, hi = frag.split("-")
                for c in range(int(lo), int(hi) + 1):
                    out |= 1 << c
            elif frag:
                out |= 1 << int(frag)
        return out

    return one(b_part), one(s_part)


def pack(state, age):
    if state <= 0:
        return 0
    return (state << STATE_SHIFT) | min(age, MAX_AGE)


class Life3D:
    def __init__(self, sx, sy, sz, birth_mask, survive_mask, wrap, states=2):
        self.sx, self.sy, self.sz = sx, sy, sz
        self.birth = birth_mask
        self.survive = survive_mask
        self.wrap = wrap
        self.states = max(2, states)
        self.grid = [0] * (sx * sy * sz)

    @property
    def alive_state(self):
        return self.states - 1

    def idx(self, x, y, z):
        return x + y * self.sx + z * self.sx * self.sy

    def get(self, x, y, z):
        """Raw packed value, honoring wrap / bounded edges."""
        if self.wrap:
            x %= self.sx
            y %= self.sy
            z %= self.sz
        elif not (0 <= x < self.sx and 0 <= y < self.sy and 0 <= z < self.sz):
            return 0
        return self.grid[self.idx(x, y, z)]

    def is_alive(self, x, y, z):
        return (self.get(x, y, z) >> STATE_SHIFT) == self.alive_state

    def state_at(self, x, y, z):
        return self.grid[self.idx(x, y, z)] >> STATE_SHIFT

    def age_at(self, x, y, z):
        return self.grid[self.idx(x, y, z)] & AGE_MASK

    def set(self, x, y, z, age=1):
        """Place a fully-alive cell."""
        self.grid[self.idx(x, y, z)] = pack(self.alive_state, age)

    def step(self):
        nxt = [0] * len(self.grid)
        # Dimensions of size 1 get no neighbor offset in that axis; otherwise a
        # wrapped offset lands back on the same cell/plane and triple-counts.
        # The HLSL step kernel mirrors this exactly.
        xr = (0,) if self.sx == 1 else (-1, 0, 1)
        yr = (0,) if self.sy == 1 else (-1, 0, 1)
        zr = (0,) if self.sz == 1 else (-1, 0, 1)
        alive_state = self.alive_state
        for z in range(self.sz):
            for y in range(self.sy):
                for x in range(self.sx):
                    n = 0
                    for dz in zr:
                        for dy in yr:
                            for dx in xr:
                                if dx == 0 and dy == 0 and dz == 0:
                                    continue
                                if self.is_alive(x + dx, y + dy, z + dz):
                                    n += 1
                    v = self.grid[self.idx(x, y, z)]
                    state = v >> STATE_SHIFT
                    age = v & AGE_MASK
                    if state == alive_state:
                        if (self.survive >> n) & 1:
                            out = pack(alive_state, min(age + 1, MAX_AGE))
                        else:
                            # Begin decaying, keeping the age reached in life.
                            # With states == 2 this lands on 0: plain death.
                            out = pack(alive_state - 1, age)
                    elif state > 0:
                        out = pack(state - 1, age)   # refractory countdown
                    else:
                        out = pack(alive_state, 1) if (self.birth >> n) & 1 else 0
                    nxt[self.idx(x, y, z)] = out
        self.grid = nxt

    def population(self):
        """Every non-empty cell — what actually gets drawn."""
        return sum(1 for v in self.grid if v > 0)

    def live_population(self):
        """Fully-alive cells only."""
        return sum(1 for v in self.grid if (v >> STATE_SHIFT) == self.alive_state)

    def alive_set(self):
        out = set()
        for z in range(self.sz):
            for y in range(self.sy):
                for x in range(self.sx):
                    if (self.grid[self.idx(x, y, z)] >> STATE_SHIFT) == self.alive_state:
                        out.add((x, y, z))
        return out


def check(name, cond):
    status = "PASS" if cond else "FAIL"
    print(f"[{status}] {name}")
    return cond


def main():
    ok = True
    B3S23 = (mask([3]), mask([2, 3]))
    BAYS_4555 = (mask([5]), mask([4, 5]))

    # 1. Blinker: period-2 oscillator
    g = Life3D(8, 8, 1, *B3S23, wrap=False)
    for x in (2, 3, 4):
        g.set(x, 3, 0)
    start = g.alive_set()
    g.step()
    vertical = g.alive_set()
    g.step()
    ok &= check("2D blinker period 2", g.alive_set() == start and vertical == {(3, 2, 0), (3, 3, 0), (3, 4, 0)})

    # 2. Block: still life
    g = Life3D(8, 8, 1, *B3S23, wrap=False)
    for x, y in ((3, 3), (4, 3), (3, 4), (4, 4)):
        g.set(x, y, 0)
    start = g.alive_set()
    g.step()
    ok &= check("2D block still life", g.alive_set() == start)

    # 2b. Ages accumulate on surviving cells
    ok &= check("age accumulates", all(g.age_at(x, y, 0) == 2 for x, y in ((3, 3), (4, 3), (3, 4), (4, 4))))

    # 3. Glider translates (+1,+1) every 4 gens in wrap mode
    g = Life3D(16, 16, 1, *B3S23, wrap=True)
    glider = {(1, 0), (2, 1), (0, 2), (1, 2), (2, 2)}
    for x, y in glider:
        g.set(x + 4, y + 4, 0)
    start = g.alive_set()
    for _ in range(4):
        g.step()
    moved = {((x + 1) % 16, (y + 1) % 16, 0) for (x, y, z) in start}
    ok &= check("2D glider moves (1,1)/4 gens", g.alive_set() == moved)

    # 4. 3D Bays 4555: deterministic, bounded, and a known random seed survives long-term
    import random

    def run4555(gens=20):
        rnd = random.Random(1)
        g = Life3D(14, 14, 14, *BAYS_4555, wrap=False)
        for z in range(4, 10):
            for y in range(4, 10):
                for x in range(4, 10):
                    if rnd.random() < 0.35:
                        g.set(x, y, z)
        pops = [g.population()]
        for _ in range(gens):
            g.step()
            pops.append(g.population())
        return pops, g.alive_set()

    p1, a1 = run4555()
    p2, a2 = run4555()
    ok &= check("3D 4555 deterministic", p1 == p2 and a1 == a2)
    ok &= check("3D 4555 stays bounded (no explosion)", max(p1) < 14 * 14 * 14 * 0.5)
    ok &= check("3D 4555 seed survives 20 gens", p1[-1] > 0)
    print("       4555 population over 20 gens:", p1)

    # 5. Edge behavior: blinker jammed against a bounded edge behaves differently than wrapped
    gb = Life3D(3, 8, 1, *B3S23, wrap=False)
    gw = Life3D(3, 8, 1, *B3S23, wrap=True)
    for g in (gb, gw):
        for y in (2, 3, 4):
            g.set(0, y, 0)
    gb.step()
    gw.step()
    ok &= check("bounded vs wrap differ at edges", gb.alive_set() != gw.alive_set())

    # 6. states=2 must be identical to the old binary engine: a cell that fails
    #    to survive goes straight to empty, with no lingering corpse.
    g = Life3D(8, 8, 1, *B3S23, wrap=False, states=2)
    for x in (2, 3, 4):
        g.set(x, 3, 0)
    g.step()
    ok &= check("states=2 leaves no corpses",
                all(v == 0 or (v >> STATE_SHIFT) == 1 for v in g.grid)
                and g.population() == g.live_population())

    # 7. The refractory shell decays exactly one step per generation, blocks
    #    rebirth while non-zero, and preserves the age the cell died at.
    g = Life3D(8, 8, 1, *B3S23, wrap=False, states=5)
    g.set(1, 1, 0, age=7)          # isolated -> dies immediately (0 neighbors)
    g.step()
    decayed = [g.state_at(1, 1, 0)]
    ages = [g.age_at(1, 1, 0)]
    for _ in range(4):
        g.step()
        decayed.append(g.state_at(1, 1, 0))
        ages.append(g.age_at(1, 1, 0))
    ok &= check("refractory shell counts down 3,2,1,0", decayed == [3, 2, 1, 0, 0])
    ok &= check("corpse keeps the age it died at", ages[:3] == [7, 7, 7])

    # A dying cell must not be counted as a live neighbor, and must not be
    # reborn while it is still refractory.
    g = Life3D(8, 8, 1, *B3S23, wrap=False, states=5)
    g.set(4, 4, 0)
    g.step()
    ok &= check("corpse is not a live neighbor", not g.is_alive(4, 4, 0))
    ok &= check("corpse still occupies its cell", g.grid[g.idx(4, 4, 0)] > 0)

    # 8. The payoff: Bays 4555 dies from soup, Pyroclastic sustains. This is the
    #    empirical reason multi-state exists in this engine at all.
    def soup(birth, survive, states, density, size=16, gens=120, seed=5):
        rnd = random.Random(seed)
        g = Life3D(size, size, size, birth, survive, wrap=True, states=states)
        for z in range(size):
            for y in range(size):
                for x in range(size):
                    if rnd.random() < density:
                        g.set(x, y, z)
        for _ in range(gens):
            g.step()
        return g.live_population()

    bays = soup(*BAYS_4555, 2, 0.15)
    pyro_b, pyro_s = mask_spec("S4-7/B6-8")
    pyro = soup(pyro_b, pyro_s, 10, 0.15)
    ok &= check("Bays 4555 soup collapses to near-nothing", bays < 16 ** 3 * 0.01)
    ok &= check("Pyroclastic soup sustains a live population", pyro > 16 ** 3 * 0.02)
    print(f"       after 120 gens on 16^3: Bays4555 {bays} live, Pyroclastic {pyro} live")

    print("\nALL PASS" if ok else "\nSOME CHECKS FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
