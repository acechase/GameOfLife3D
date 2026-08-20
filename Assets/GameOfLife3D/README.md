# GameOfLife3D — a volumetric Game of Life for Unity + OpenXR

A 3D cellular automaton (Carter Bays' "Life in three dimensions" rules and
friends) simulated entirely on the GPU and rendered as a floating volume of
glowing cells you can walk around, grab, and reach into. Built simulator-first:
everything works on desktop today, and the same scene ships to a passthrough AR
headset later by adding the XR plumbing described at the bottom.

The whole thing boots from **one component on an empty GameObject** — no
prefabs or inspector wiring needed.

---

## 1. Quick start (plain desktop, 5 minutes — no XR needed)

Works in any Unity 6 URP project. Do this first to see it run; add XR after.

1. Create a project from the **Universal 3D** template (Unity 6, e.g. 6000.x LTS).
2. Drop the `GameOfLife3D` folder anywhere under `Assets/`.
3. In an empty scene: **GameObject → Create Empty**, name it `Life Volume`,
   position it at `(0, 1.2, 0.8)` so it hangs in front of the default camera.
4. Add components: **LifeVolume**, **LifeDesktopControls**, **LifeGlow**, and
   **LifeGround**.
5. Select your **Main Camera** → add **LifeOrbitCamera** (it finds the volume
   and frames it on Play).
6. Press **Play**.

You should see a glowing colony churning away under the Pyroclastic rule.
Press `G` to swap it for a glider gun or a 3D spaceship.

**Desktop controls**

| Input | Action |
|---|---|
| `Space` | pause / resume |
| `N` | single step (pauses first) |
| `R` | reseed with a new random soup |
| `C` | clear all cells |
| `T` | cycle rule preset |
| `G` | stamp the next known pattern (glider gun, spaceships) |
| `[` / `]` | slower / faster |
| **Left-drag** | **pan** |
| **`Shift` + left-drag** | **orbit** |
| Two-finger / wheel scroll | zoom |
| `F` | frame the volume |
| `Cmd` + left-drag | paint cells into the volume |
| `Cmd` + right-drag | erase cells |
| Right-drag | orbit (mouse alias) |
| Middle-drag | pan (mouse alias) |

**The mouse navigates by default.** Bare drags move the camera; editing cells
is the deliberate act and needs `Cmd` (or `Ctrl`) held. While that modifier is
down the camera ignores the mouse completely, so a paint stroke never drags the
view along with it.

Everything is trackpad-reachable. **Orbit is on `Shift` + drag rather than the
right button** because a Mac trackpad cannot right-*drag*: a two-finger click
is a right-click, but holding two fingers down and moving is the scroll
gesture, so the drag never arrives. Right-drag and middle-drag are kept as
aliases for anyone on an actual mouse.

The paint modifier is `Cmd` on macOS and `Ctrl` elsewhere — deliberately not
`Ctrl` on macOS, where `Ctrl`+click is a system right-click, so "Ctrl+drag to
paint" would arrive as a right-drag and silently erase instead.

A drag's meaning is fixed when the button goes down and held until release, so
a modifier pressed mid-stroke can't flip you between pan and orbit halfway
through — that flipping is what made the old `Alt` bindings feel glitchy and
unresponsive, since the two moves partly cancel.

**About painting:** it spawns a noisy sphere of live cells where your cursor
ray enters the volume — the desktop stand-in for reaching in with your hand in
XR. Two things make it easy to miss: under `Pyroclastic` the volume is already
~30% full of churning cells, so a fresh blob is visually lost in the noise, and
under the Bays rules a painted blob dies within a few generations (those rules
kill soup — see the rule notes above). Paint onto a cleared grid (`C`) to
actually see what it does.

**Make it glow:** the cell colors are HDR (young cyan peaks near `4.0`), so
bloom does the aesthetic heavy lifting. **LifeGlow** handles all of it — no
profile assets, no Global Volume, no camera checkboxes:

- It builds its own global Volume in code at priority `100`, overriding
  whatever profile the scene already carries (the URP template ships one tuned
  for a generic sample scene: bloom intensity `0.25`, too timid for this).
- At play time it forces the main camera to HDR + post-processing, turns on
  SMAA and dithering, and clears to near-black so the glow reads.
- The volume object and profile are `HideAndDontSave`: nothing appears in the
  Hierarchy and the scene never gets dirtied.

Tune it live in the inspector while playing — **Intensity** is the main
bioluminescence dial, **Threshold** decides how dim a cell still glows, and
**Scatter** trades a tight halo for volumetric haze.

Two flags matter later: turn **Dark Background** *off* for passthrough AR (it
would paint over the real world) and switch **Antialiasing** to `FXAA` in XR,
where SMAA isn't supported.

## 2. Things to try

- **Rules** (`LifeVolume → Rule`):
  - `Pyroclastic` — the default, and the one that actually sustains: churning
    fronts that keep evolving indefinitely, ~7% of cells alive with fading
    trails behind them. Converges to the same behaviour from any seed density
    between 0.06 and 0.25, so it's hard to break.
  - `Coral` — denser and slower, settling into a reef-like solid that keeps
    working at its surface. Roughly 25% alive, so it reads as more of a mass.
  - `Bays4555` — Carter Bays' classic 3D Life. Gliders exist and small
    hand-built patterns are interesting, **but random soup always dies out**,
    at every density and seed shape tested. Kept because the gliders are real;
    don't expect a self-sustaining colony from a reseed.
  - `Bays5766` — Bays' second candidate. Same caveat.
  - `Clouds` — knife-edge: below ~0.65 density it goes extinct within a
    hundred generations, above it freezes into a solid block. Left in for
    rule-safari purposes rather than as a good time.
  - `Conway2D` — set **Grid Size** z = `1` (e.g. `96 × 96 × 1`) and you have
    the genuine 1970 article, floating in space. Gliders and all. The
    single-layer grid degenerates exactly to the 2D Moore neighborhood.
  - `Custom` — birth/survive count strings like `"5"` / `"4,5"` or ranges
    `"13-14"` / `"13-26"`. Rule-space safari: most rules die or explode;
    finding the living edge is the fun.
- **Patterns** (`G`) — cycles through known configurations, each one clearing
  the grid and switching to the rule, edge mode and grid size it needs — each
  pattern stages its own playing field, since the gun needs room to survive
  while a 10-cell spaceship needs a small grid to be visible at all. This
  is the only way to see structures *travel*, and the reason why is worth
  knowing: a search over 3520 random starting blobs found **no spaceships at
  all** under Pyroclastic or Coral. Rules that sustain a random soup are
  turbulent; rules with spaceships (Bays) die from soup. So travelers have to
  be placed, not grown.
  - `3D Spaceship (Bays 5766)` — 10 cells, period 4, drifting diagonally
    through the volume forever on a torus. A genuine 3D glider, found by
    search and verified over six full periods.
  - `Gosper Glider Gun` — flattens to a `96x96` slab and fires a glider every
    30 generations, indefinitely. The clearest "things are being built and
    launched" pattern there is. It needs that room: measured in the reference,
    a `48x48` or `64x64` grid destroys the gun within ~150 generations, because
    its own boundary sits too close and the debris walks back into the
    mechanism.
  - `Glider` / `Lightweight Spaceship` — the 2D classics, in slab mode.
- **States** (`LifeVolume → States`, 0 = use the rule's default) — the single
  biggest lever on whether a 3D rule stays alive. At `2` the automaton is plain
  binary: a cell that fails to survive vanishes. Above `2`, a dying cell leaves
  a **refractory corpse** that fades over `States - 2` generations, can't be
  reborn while it lingers, and isn't counted as a neighbour. That shell is what
  stops activity from either dying out or collapsing into dense boiling — it
  organises it into propagating fronts instead. Raise it for longer trails,
  drop it toward 2 for a crisper, denser look.
- **Trail Brightness / Trail Scale** — how hot and how large a corpse stays as
  it fades. Keeping brightness under ~0.5 drops trails below the bloom
  threshold, so the living front glows and the trails read as dim ghosts.
- **The ground grid** (`LifeGround`) is a navigation aid rather than
  decoration. Orbiting keeps the subject centred by definition, so against a
  featureless background nothing in frame changes except the volume's own
  silhouette, and the move reads as the world spinning instead of you
  travelling. Near grid lines sweep past faster than far ones, and that
  parallax is what resolves it. It also states the scale: squares are **Minor
  Spacing** metres (10 cm by default), so the 0.6 m volume becomes an object of
  a definite size. **Turn Show Grid off for passthrough AR** — the real room
  already supplies both cues, and the plane would paint over your actual floor.
- **Wrap Edges** — torus topology; gliders that leave one face re-enter the
  opposite one.
- **Idle Spin** — a few degrees/second makes it read as a sculpture.
- **Grid size** — `48³` (~110k cells) is nothing for a desktop GPU; `96³`
  (~885k) still fine. The sim is a compute shader; rendering only draws live
  cells via one indirect instanced draw.
- Population / rule readout is the overlay top-left (`Show Stats` to toggle).

## 3. OpenXR + XR Device Simulator (headset-free XR)

Packages (Window → Package Manager):

- **XR Plugin Management** (`com.unity.xr.management`)
- **OpenXR Plugin** (`com.unity.xr.openxr`)
- **XR Interaction Toolkit** (`com.unity.xr.interaction.toolkit`, 3.x)
  - In its **Samples** tab, import **Starter Assets** and **XR Device Simulator**.

Setup:

1. **Edit → Project Settings → XR Plug-in Management**: enable **OpenXR** for
   your desktop platform. Under OpenXR settings, add an interaction profile
   you'll eventually target (e.g. *Oculus Touch Controller Profile*).
2. Delete the scene's plain Main Camera. Drag in the
   **XR Origin (XR Rig)** prefab from the Starter Assets sample, position it
   ~1.5 m back from the Life Volume. Make sure its camera is tagged
   **MainCamera** (mouse painting uses `Camera.main`).
3. Drag in the **XR Device Simulator** prefab from its sample.
4. Press Play — the simulator's on-screen help shows how to drive the HMD and
   controllers with mouse/keyboard.

**XR wand (paint with the controller):** add **LifeXRWand** to the rig's
*Right Controller* GameObject. Either assign its four actions from the XRI
default input actions asset (Activate/trigger for paint is a good fit), or
right-click the component header → **Use Default XR Bindings** for
ready-made trigger/grip/button bindings.

**Grab & scale the colony:** on the `Life Volume` object, right-click the
LifeVolume component header → **Fit Box Collider For XR Grab** (adds a fitted
trigger collider), then add **XR Grab Interactable** (add a Rigidbody with
**Is Kinematic** on, gravity off). Grab it, turn it, throw it gently across the
room. XRI's default setup gives two-hand rotate/scale if you enable it.

## 4. Later: passthrough AR on a headset (e.g. Quest 3)

The scene is already AR-shaped: the colony is a world-anchored object, not UI.
When you have hardware:

1. Switch platform to Android; in **XR Plug-in Management → Android**, enable
   OpenXR and (for Quest) the Meta Quest feature group / interaction profile.
2. Camera: **Background Type = Solid Color**, color `(0,0,0,0)` — fully
   transparent alpha — and enable the passthrough layer per your device's
   OpenXR passthrough documentation (on Quest: the *Meta Quest* OpenXR feature
   set; or the AR Foundation `ARCameraManager` path if you use AR Foundation).
3. Mobile GPU budget: start at grid `32³`–`48³`, steps/sec ≤ 8, and keep bloom
   modest (or fake it with brighter HDR colors and no bloom pass). The sim
   cost is trivial; fill-rate from many overlapping cubes is what to watch.
4. Anchor `Life Volume` ~1.2 m off the floor a meter in front of the user, or
   parent it to an OpenXR spatial anchor for real room-locking.

## 5. What's in the folder

```
GameOfLife3D/
├── README.md
├── Scripts/
│   ├── LifeVolume.cs          # the whole sim+render orchestrator (one component)
│   ├── LifeRules.cs           # rule presets & B/S mask parsing
│   ├── LifeDesktopControls.cs # mouse/keyboard for desktop & simulator
│   ├── LifeGlow.cs            # self-building bloom/grade volume + camera setup
│   ├── LifeGround.cs          # floor reference grid (parallax + scale cue)
│   ├── LifePatterns.cs        # verified gliders / guns / 3D spaceship
│   ├── LifeOrbitCamera.cs     # Game-view orbit / pan / zoom / frame
│   └── LifeXRWand.cs          # controller painting / pause / reseed
└── Resources/
    ├── LifeCompute.compute    # seed / step / paint / compact kernels
    ├── LifeGround.shader      # procedural antialiased grid, radial fade
    └── LifeCell.shader        # URP instanced cell shader (HDR, age gradient)
```

Design notes:

- **State** is one `uint` per cell: 0 = dead, otherwise age in generations
  (drives the young→old color gradient). Double-buffered; the step kernel is a
  pure function of the previous grid.
- **Rules** are 27-bit masks over live-neighbor counts (26-neighbor Moore
  neighborhood), so any "B…/S…" 3D rule is representable, and single-layer
  grids reproduce 2D Conway exactly. The step logic was validated against a
  headless reference implementation (blinker period, block stability, glider
  translation, wrap-vs-bounded edge cases).
- **Rendering**: after each mutation a compact kernel appends live cells to an
  append buffer; `CopyCount` writes the instance count into indirect draw args;
  one `Graphics.RenderMeshIndirect` call draws everything. Dead cells cost
  nothing.
- Axes of size 1 get no neighbor offset — otherwise wrap mode would fold the
  offset back onto the same plane and triple-count neighbors (a real bug the
  reference tests caught).
