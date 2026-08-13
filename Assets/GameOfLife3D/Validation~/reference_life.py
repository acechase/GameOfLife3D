"""
Headless reference implementation of the 3D cellular automaton that will run
in the Unity compute shader. This mirrors the HLSL step kernel EXACTLY:

- Grid of uint "age" values: 0 = dead, >=1 = alive (age in generations, clamped)
- 26-neighbor (3D Moore) counting
- Rules as bitmasks: bit n of `birth` set   -> dead cell with n live neighbors is born
                     bit n of `survive` set -> live cell with n live neighbors survives
- Edge handling: wrap (torus) or bounded (out-of-range = dead)
- A grid with size_z == 1 and no z-wrapping degenerates to classic 2D Moore
  neighborhood, so B3/S23 reproduces Conway's Game of Life exactly.

Validation checks:
1. 2D blinker (B3/S23, single layer) oscillates with period 2.
2. 2D block is a still life.
3. 2D glider translates by (1,1) every 4 generations (wrap mode).
4. 3D Bays 4555 evolution is deterministic and a known small seed behaves sanely.
5. Bounded vs wrap edge behavior differs as expected.
"""

MAX_AGE = 255


def mask(counts):
    m = 0
    for c in counts:
        m |= 1 << c
    return m


class Life3D:
    def __init__(self, sx, sy, sz, birth_mask, survive_mask, wrap):
        self.sx, self.sy, self.sz = sx, sy, sz
        self.birth = birth_mask
        self.survive = survive_mask
        self.wrap = wrap
        self.grid = [0] * (sx * sy * sz)

    def idx(self, x, y, z):
        return x + y * self.sx + z * self.sx * self.sy

    def get(self, x, y, z):
        if self.wrap:
            x %= self.sx
            y %= self.sy
            z %= self.sz
        elif not (0 <= x < self.sx and 0 <= y < self.sy and 0 <= z < self.sz):
            return 0
        return self.grid[self.idx(x, y, z)]

    def set(self, x, y, z, age=1):
        self.grid[self.idx(x, y, z)] = age

    def step(self):
        nxt = [0] * len(self.grid)
        # Dimensions of size 1 get no neighbor offset in that axis; otherwise a
        # wrapped offset lands back on the same cell/plane and triple-counts.
        # The HLSL step kernel mirrors this exactly.
        xr = (0,) if self.sx == 1 else (-1, 0, 1)
        yr = (0,) if self.sy == 1 else (-1, 0, 1)
        zr = (0,) if self.sz == 1 else (-1, 0, 1)
        for z in range(self.sz):
            for y in range(self.sy):
                for x in range(self.sx):
                    n = 0
                    for dz in zr:
                        for dy in yr:
                            for dx in xr:
                                if dx == 0 and dy == 0 and dz == 0:
                                    continue
                                if self.get(x + dx, y + dy, z + dz) > 0:
                                    n += 1
                    age = self.grid[self.idx(x, y, z)]
                    if age > 0:
                        nxt[self.idx(x, y, z)] = min(age + 1, MAX_AGE) if (self.survive >> n) & 1 else 0
                    else:
                        nxt[self.idx(x, y, z)] = 1 if (self.birth >> n) & 1 else 0
        self.grid = nxt

    def population(self):
        return sum(1 for a in self.grid if a > 0)

    def alive_set(self):
        out = set()
        for z in range(self.sz):
            for y in range(self.sy):
                for x in range(self.sx):
                    if self.grid[self.idx(x, y, z)] > 0:
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
    ok &= check("age accumulates", all(g.grid[g.idx(x, y, 0)] == 2 for x, y in ((3, 3), (4, 3), (3, 4), (4, 4))))

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

    print("\nALL PASS" if ok else "\nSOME CHECKS FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
