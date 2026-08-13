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
- `Scripts/LifeRules.cs` — rule presets + "B/S count string" parsing.
- `Scripts/LifeDesktopControls.cs` — keyboard/mouse (Space/N/R/C/T, brackets
  for speed, LMB paint / RMB erase; skips painting while Alt held).
- `Scripts/LifeOrbitCamera.cs` — Game-view orbit/zoom (Alt+drag, scroll, F).
- `Scripts/LifeXRWand.cs` — controller painting via Input System actions
  (`#if ENABLE_INPUT_SYSTEM`), context menu for default XR bindings.
- `Validation~/reference_life.py` — headless Python mirror of the step rule.
  Unity ignores the `~` folder. Run `python3 reference_life.py`; must print
  ALL PASS. If you change step/neighbor/edge logic in HLSL, change the
  reference FIRST, keep them in lockstep, and re-run it.

## Core design decisions (don't accidentally undo these)

- Cell state is one uint: 0 = dead, else **age in generations** (clamped 255).
  Age drives the color gradient. Step is a pure function of the previous grid.
- Rules are 27-bit masks over live-neighbor counts (26-neighbor 3D Moore).
  Presets: Bays4555 (default), Bays5766, Clouds (S13-26/B13-14), Conway2D,
  Custom. A grid with an axis of size 1 degenerates exactly to 2D Moore, so
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

1. Bloom/post-processing polish (Global Volume, HDR camera) — may still be unset.
2. OpenXR + XRI + XR Device Simulator setup (README §3 has the steps).
3. Glider/pattern injection presets (known 4555 gliders, 2D glider guns in
   Conway2D slab mode).
4. Trail ghosts: recently-dead cells linger dim and fade (needs a second
   "corpse age" channel or separate buffer).
5. XR grab-and-scale tuning; two-hand scale.
6. Quest 3 passthrough build (README §4).
7. Stretch ideas from the original design chat: multi-species color
   inheritance, sound from population dynamics.

## Provenance

Designed and built in a Claude (Cowork) session, 2026-08-13. The step
algorithm was validated against the Python reference before the HLSL was
written (blinker period-2, block still life, glider translation, wrap-vs-
bounded edges, Bays 4555 determinism/boundedness).
