# Demo media

Short loops embedded in the root README, so people can see what the thing
actually looks like before cloning it.

Committed files here should be **optimised GIFs**. GitHub renders animated GIFs
inline from a repo path; it does *not* reliably play `.mp4` referenced that way
— video only autoplays when uploaded straight to GitHub's CDN by dragging into
an issue or PR, which produces a URL that lives outside this repo. So GIF is
the format that survives a clone.

Keep each one **under about 5 MB**. Raw captures (`.mov`, `.mp4`) are ignored by
git — convert, commit the GIF, leave the source out.

## Shot list

| File | Shows | Notes |
|---|---|---|
| `pyroclastic.gif` | the default rule churning, slow orbit | the money shot — bloom, trails, fronts |
| `glider-gun.gif` | `G` to the Gosper gun, gliders streaming away | the clearest "things are built and travel" |
| `spaceship-3d.gif` | the Bays5766 traveller crossing the volume | zoom in; it is only 10 cells |
| `navigation.gif` | pan, orbit, zoom over the ground grid | shows the parallax the grid exists for |

## Capturing

Set the Game view to a fixed **16:9, 1280×720** first (the resolution dropdown
at the top of the Game view → `+` → Fixed Resolution). Consistent framing across
clips looks far better than whatever the window happened to be.

Drop **Steps Per Second** to 3–5 while recording. The default 6 is fine to watch
live but too fast to read in a 10-second loop.

**Option A — macOS, no installs.** `⇧⌘5` → *Record Selected Portion* → drag
around the Game view → Record. Stop from the menu bar. Saves a `.mov`.

**Option B — Unity Recorder.** Cleaner: it captures the Game view directly at a
locked framerate with no window chrome or cursor, so the result does not stutter
when the editor hitches. Window → Package Manager → Unity Registry → *Recorder*
→ Install, then Window → General → Recorder → Recorder Window. Add a **Movie**
recorder, source *Game View*, 30 FPS, and point the output at `Recordings/`
(already git-ignored).

## Converting to GIF

Needs ffmpeg once: `brew install ffmpeg`

Two-pass palette generation — a generated palette matters a lot here, because
the default 256-colour quantiser wrecks the bloom gradients into visible bands:

```sh
ffmpeg -i input.mov \
  -vf "fps=18,scale=800:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=bayer:bayer_scale=3" \
  -loop 0 docs/media/output.gif
```

Trim before converting rather than after — `-ss 00:00:02 -t 8` after `-i` takes
8 seconds starting at 2s.

If a file lands over 5 MB, in order of least visible damage: drop `fps` to 15,
then `scale` to 640, then `max_colors` to 96.
