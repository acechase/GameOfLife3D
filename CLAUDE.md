# GamOfLifeVolume3D — project context

## What this is

A co-designed 3D Game of Life for Unity 6 + URP, built AR-first for OpenXR
(simulator-first today, Quest 3 passthrough later). A volumetric cellular
automaton simulated entirely in a compute shader, rendered as glowing instanced
cubes the user can orbit, paint into, and (in XR) grab. All project code lives
in `Assets/GameOfLife3D/` — treat everything outside that folder as
Unity-managed.

The developer (Andrew) is new to Unity — explain Unity concepts plainly when
they come up, prefer zero-wiring solutions (components that boot themselves),
and give click-by-click editor instructions when a manual editor step is
unavoidable.

## Environment

- Unity 6.3 LTS, Universal 3D (URP) template, running on macOS → shaders
  compile through **Metal** (see gotchas below).
- Desktop now; OpenXR + XR Interaction Toolkit + XR Device Simulator next;
  Quest 3 passthrough AR eventually (grid ≤48³, modest bloom on mobile GPU).
- No code editor configured in Unity; all editing happens through Claude.

## Architecture (all in Assets/GameOfLife3D/)

- `Resources/LifeCompute.compute` — the whole sim. Kernels: CSStep (double-
  buffered CA step), CSSeed (hash-random fill of a center region), CSClear,
  CSPaint (noisy sphere spawn/erase), CSCompact (append live cells).
- `Resources/LifeCell.shader` — URP unlit instanced shader; reads the
  live-cell append buffer via SV_InstanceID, decodes cell index → grid pos,
  colors by age (HDR young-cyan → teal → old-violet; bloom does the glow),
  newborn scale-in driven by `_StepPhase`.
- `Scripts/LifeVolume.cs` — one-component orchestrator: creates buffers,
  builds its own cube mesh, loads shaders from Resources, dispatches kernels,
  draws with one `Graphics.RenderMeshIndirect`. Public API: Paused, StepOnce,
  Reseed, ClearAll, PaintSphere, CycleRule, FitBoxCollider (context menu, for
  XRGrabInteractable).
- `Scripts/LifeRules.cs` — rule presets, per-rule state counts and measured
  seed densities, + "B/S count string" parsing.
- `Scripts/LifePatterns.cs` — known-good starting configurations (Gosper gun,
  Conway glider/LWSS, the Bays5766 3D spaceship), each verified against the
  Python reference rather than copied from memory.
- `Scripts/LifeDesktopControls.cs` — keyboard (Space/N/R/C/T, G to stamp the
  next pattern, brackets for speed) + painting on **Cmd/Ctrl + drag only**.
- `Scripts/LifeInput.cs` — one definition of Alt / Shift / PaintModifier,
  shared by the camera and the brush. It exists because those two components
  each tested the keys themselves and the definitions drifted, which let a drag
  paint and navigate at once. Add a modifier here, not in a component.
- `Scripts/LifeOrbitCamera.cs` — Game-view navigation: left-drag pan,
  Shift+left-drag orbit, scroll zoom, F to frame (right/middle-drag are
  mouse-only aliases). **Use `Mathf.SmoothDamp`, never `SmoothDampAngle`, for
  yaw/pitch**: SmoothDampAngle routes through DeltaAngle and takes the shortest
  path, so a fast flick or a hitched frame that moves the target more than 180°
  snaps the camera backwards. That was the cause of every "orbit feels glitchy"
  report; per-frame mode selection was a wrong first guess. Orbit deltas are
  also clamped to 90°/frame, since trackpads report accumulated movement.
  Goes on the **camera** (not the volume); finds the LifeVolume itself and
  frames it on Play. It drives `_rig` — the found camera's transform — never
  its own, and falls back to Camera.main with a warning if attached to the
  volume. It carries no `[RequireComponent(typeof(Camera))]` on purpose: that
  silently adds a second Camera to whatever it lands on, which is how you end
  up with two cameras rendering the Game view and a "camera" orbiting itself. Drives
  pivot + spherical offset (no accumulated roll); pan is stored as an offset
  *from the target* so the view keeps tracking a volume that moves.
  **Bare left-drag pans and bare right-drag orbits — the mouse's primary job is
  navigation.** Painting is behind Cmd/Ctrl, and the camera ignores the mouse
  entirely while that is held, so the two can't fight.
- **Andrew is on a macOS trackpad**, which constrains bindings hard:
  no middle button, no wheel, and **no right-DRAG** (two-finger click is a
  right-click, but two fingers held and moved is the scroll gesture). So orbit
  lives on Shift+left-drag; right/middle-drag are mouse-only aliases. Scroll is
  normalized for wheel (~120/notch) vs trackpad deltas.
- **Ctrl is not a usable modifier on macOS**: Ctrl+click is an OS-level
  right-click, so a "Ctrl+drag" binding arrives as a right-button drag. The
  paint modifier is Cmd on macOS, Ctrl elsewhere (see `LifeInput`).
- `Scripts/LifeGlow.cs` — post-processing, zero wiring. Builds its own global
  Volume + VolumeProfile in code (priority 100, `HideAndDontSave` so it never
  dirties the scene) with Bloom/Tonemapping/ColorAdjustments/Vignette, and at
  play time forces the main camera to HDR + post-processing + SMAA + dithering
  + near-black clear. `[ExecuteAlways]`, live-tunable via `OnValidate`.
  For passthrough AR: turn `darkBackground` off and use FXAA (SMAA is
  unsupported in XR).
- `Scripts/LifeXRWand.cs` — controller painting via Input System actions
  (`#if ENABLE_INPUT_SYSTEM`), context menu for default XR bindings.
- `Validation~/reference_life.py` — headless Python mirror of the step rule.
  Unity ignores the `~` folder. Run `python3 reference_life.py`; must print
  ALL PASS. If you change step/neighbor/edge logic in HLSL, change the
  reference FIRST, keep them in lockstep, and re-run it.

## Core design decisions (don't accidentally undo these)

- Cell state is one uint packed as **`(state << 8) | age`**. `state == 0` is
  empty; `state == States-1` is ALIVE; anything between is a **refractory
  corpse** counting down one per generation. Only fully-alive cells are counted
  as neighbors, and a corpse cannot be reborn while it lingers. Age (clamped
  255) drives the color gradient, and a corpse keeps the age it died at so it
  fades at the color it reached. Step is a pure function of the previous grid.
- **`States = 2` is exactly the old binary engine** — that equivalence is
  asserted in the reference tests, so it's the safe fallback for any new rule.
- **Multi-state is why the 3D rules work at all.** Measured against random soup
  in the Python reference: Bays4555 and Bays5766 go extinct at *every* density,
  fill fraction, and edge mode; Clouds dies below 0.65 density and freezes
  solid above it. Pyroclastic (S4-7/B6-8, 10 states) sustains indefinitely at
  ~7% live with a Conway-like turnover ratio (~0.8 vs 2D Conway's 0.68). Don't
  "fix" a dying rule by tuning the seed — it was tried exhaustively and the
  refractory shell is the thing that matters.
- Rules are 27-bit masks over live-neighbor counts (26-neighbor 3D Moore).
  Presets: Pyroclastic (default, S4-7/B6-8 ×10 states), Coral (S5-8/B6-7 ×4),
  Bays4555, Bays5766, Clouds (S13-26/B13-14), Conway2D, Custom. A grid with an axis of size 1 degenerates exactly to 2D Moore, so
  Conway2D + z=1 is the genuine 1970 game.
- **Axes of size 1 get no neighbor offset in that axis.** Otherwise wrap mode
  folds the offset back onto the same plane and triple-counts neighbors. This
  bug actually happened; the reference tests catch it.
- Rendering draws only live cells: CSCompact appends to `_LiveCells`,
  `GraphicsBuffer.CopyCount` writes instanceCount into indirect args at byte
  offset 4. Dead cells cost nothing; 96³ grids are fine on desktop.
- Compute + cell shader live in `Resources/` and are loaded by name
  (`Resources.Load`) so LifeVolume needs zero inspector wiring. Keep it that
  way — new assets should self-wire.
- Scalar uniforms instead of int3 (`_SeedMinX` etc.): `SetInts` with int3 is
  unreliable across graphics APIs.

## Metal shader gotchas (already hit, keep clean)

- No early-return-in-branch helpers: use single-assignment + one return, or
  Metal warns "potentially uninitialized variable".
- Don't name functions `Sample` (collides with HLSL intrinsic).
- Use unsigned modulus for wrap math (values are non-negative there);
  signed % triggers Metal perf warnings.
- Goal: the Unity console stays completely clean after a reimport.

## Workflow

- Andrew keeps Unity open; after edits he refocuses Unity (auto-reimport) and
  pastes any Console messages back. There is no way to run Unity headless
  here — treat the Unity console as the test feedback loop, and the Python
  reference as the pre-commit test for rule logic.
- Commit early and often; small commits per feature.

## Roadmap (discussed, in rough priority order)

1. ~~Bloom/post-processing polish~~ — done via `LifeGlow`. The URP template
   already had HDR + post-processing + a Global Volume on; only the tuning was
   generic. Remaining polish: retune per-rule (Clouds is much denser than
   Bays4555, so it blooms hotter).
2. OpenXR + XRI + XR Device Simulator setup (README §3 has the steps).
3. ~~Glider/pattern injection~~ — done. `LifePatterns.cs` + `LifeVolume.
   StampPattern` + `G` key. Each pattern carries the rule, edge mode and grid
   shape it needs, and `Reshape()` reallocates buffers so flat patterns work.
   Note: a 3520-trial search found NO spaceships under Pyroclastic or Coral
   (turbulent rules don't have them) and none under Bays4555 either; the one
   3D traveler we ship is a Bays5766 period-4 10-cell glider found by that
   search. Any new pattern must be verified in Validation~ before shipping.
4. ~~Trail ghosts~~ — done, and for free: the refractory states ARE the
   corpses. CSCompact appends every non-empty cell and the shader dims/shrinks
   anything below the alive state (`trailBrightness`, `trailScale`).
5. XR grab-and-scale tuning; two-hand scale.
6. Quest 3 passthrough build (README §4).
7. Stretch ideas from the original design chat: multi-species color
   inheritance, sound from population dynamics.

## Provenance

Designed and built in a Claude (Cowork) session, 2026-08-13. The step
algorithm was validated against the Python reference before the HLSL was
written (blinker period-2, block still life, glider translation, wrap-vs-
bounded edges, Bays 4555 determinism/boundedness).
