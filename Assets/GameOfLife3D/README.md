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

Pan and orbit switch live, so pressing `Shift` a moment after you start
dragging still gets you an orbit. Only *ownership* is fixed at button-down:
a drag begun as a paint stroke stays the brush's until you release, so letting
go of `Cmd` partway through can't hand a half-finished stroke to the camera.

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

## 3. XR: simulator first, OpenXR when you have a headset

**Read this before enabling anything: OpenXR has no macOS runtime.** Unity's
OpenXR plugin targets Windows, Linux and Android (which is how Quest builds
work). On a Mac there is nothing for it to connect to, so switching it on for
the Mac Standalone target gets you errors, not a headset. That does *not* block
XR development, because the piece that matters on a Mac is the **XR Device
Simulator**, which fakes the HMD and both controllers through the Input System
and needs no runtime at all.

So the order is: build and test the interactions against the simulator on the
desktop, and enable OpenXR only when you switch the build target to Android for
a Quest build (§4).

The packages are already in `Packages/manifest.json` — XR Interaction Toolkit
and the OpenXR plugin install themselves when Unity next has focus. Nothing to
click in Package Manager for those.

### 3a. Import the two samples

Samples cannot be declared in the manifest, so this part is manual:

1. **Window → Package Manager → XR Interaction Toolkit → Samples** tab.
2. Import **Starter Assets** (the input actions and the rig prefab).
3. Import **XR Device Simulator**.

### 3b. Swap the camera for a rig

1. Delete the scene's plain **Main Camera**.
2. From `Assets/Samples/XR Interaction Toolkit/<version>/Starter Assets/Prefabs`,
   drag **XR Origin (XR Rig)** into the scene, about 1.5 m back from the Life
   Volume.
3. Confirm its camera is tagged **MainCamera** — `LifeGlow` configures
   `Camera.main`, and painting rays are cast from it.
4. Drag in the **XR Device Simulator** prefab from its sample folder.
5. Press **Play**. The simulator's on-screen legend shows how to drive the head
   and hands from the keyboard and mouse.

**If the volume flies away from you the moment you press Play, you are the one
moving.** The Starter Assets rig has a `CharacterController` and a
`GravityProvider`, and this scene has no other colliders in it — the ground grid
is drawn with `Graphics.RenderMesh` and is pure visuals. With nothing to stand
on the rig falls forever, and the volume appears to recede into the sky.
`LifeGround` handles this: **Physical Floor** adds an invisible collider matching
the grid, and **Use World Floor** puts that grid at `y = 0` instead of tucked
under the volume. Both are on by default. The second matters as much as the
first — the volume floats at 1.2 m, so a grid tucked beneath it sits at about
0.75 m, which is *above* where a standing rig starts, and a floor you begin
underneath is no floor at all.

`LifeOrbitCamera` detects that the camera is now driven by a tracked pose and
stands itself down, logging one line to say so — mouse navigation and head
tracking would otherwise write to the same transform every frame and fight.
You can leave the component on the object. `LifeGlow` likewise switches SMAA to
FXAA, since SMAA is unsupported in XR and URP silently drops it rather than
warning.

### 3c. Paint with a controller

Add **LifeXRWand** to the rig's *Right Controller* object. Either assign its
four actions from the XRI default input actions asset (Activate/trigger suits
painting), or right-click the component header → **Use Default XR Bindings**
for ready-made trigger/grip/button bindings.

### 3d. Grab the colony

On `Life Volume`, right-click the LifeVolume component header → **Fit Box
Collider For XR Grab**, which adds a fitted trigger collider. Then add **XR
Grab Interactable** plus a Rigidbody with **Is Kinematic** on and gravity off.
Grab it, turn it, set it down somewhere else. The view keeps tracking it:
`LifeOrbitCamera` stores its pan as an offset *from* the volume rather than as
an absolute point, for exactly this reason.

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
