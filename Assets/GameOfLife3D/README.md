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
4. Add components: **LifeVolume** and **LifeDesktopControls**.
5. Press **Play**.

You should see a glowing cyan colony evolving under the Bays 4555 rule.

**Desktop controls**

| Input | Action |
|---|---|
| `Space` | pause / resume |
| `N` | single step (pauses first) |
| `R` | reseed with a new random soup |
| `C` | clear all cells |
| `T` | cycle rule preset |
| `[` / `]` | slower / faster |
| Left-drag | paint cells where the pointer ray enters the volume |
| Right-drag | erase |

**Make it glow (recommended):** the cell colors are HDR, so bloom does the
aesthetic heavy lifting.

1. Select your camera → enable **Post Processing** (Rendering section). Ensure
   the camera (or the URP asset) has **HDR** on.
2. **GameObject → Volume → Global Volume** → create a new profile →
   **Add Override → Post-processing → Bloom**. Set Intensity ≈ `0.8`,
   Threshold ≈ `0.9`, Scatter ≈ `0.6`.
3. A dark scene sells it: set the camera **Background Type** to Solid Color,
   near-black (e.g. `#05070C`).

## 2. Things to try

- **Rules** (`LifeVolume → Rule`):
  - `Bays4555` — the classic 3D Life. Amoeba-like colonies, gliders exist.
  - `Bays5766` — blockier, crystalline, decays slowly.
  - `Clouds` — billowing solid masses that carve caves through themselves.
    Try grid `64³`, and stick your head inside it in XR.
  - `Conway2D` — set **Grid Size** z = `1` (e.g. `96 × 96 × 1`) and you have
    the genuine 1970 article, floating in space. Gliders and all. The
    single-layer grid degenerates exactly to the 2D Moore neighborhood.
  - `Custom` — birth/survive count strings like `"5"` / `"4,5"` or ranges
    `"13-14"` / `"13-26"`. Rule-space safari: most rules die or explode;
    finding the living edge is the fun.
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
│   └── LifeXRWand.cs          # controller painting / pause / reseed
└── Resources/
    ├── LifeCompute.compute    # seed / step / paint / compact kernels
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
