# Demo media

Short loops embedded in the root README, so people can see what the thing
actually looks like before cloning it.

Committed files here should be **optimised GIFs**. GitHub renders animated GIFs
inline from a repo path; it does *not* reliably play `.mp4` referenced that way
— video only autoplays when uploaded straight to GitHub's CDN by dragging into
an issue or PR, which produces a URL that lives outside this repo. So GIF is
the format that survives a clone.

Aim for **5-6 MB each**. That is looser than it sounds: almost every pixel
changes every frame here, so there is very little for GIF's inter-frame
compression to exploit and the format's usual size rules do not apply. Six
seconds at 640px/12fps lands around 6 MB for a full-frame shot and around 4 MB
for a mostly-dark one. Raw captures (`.mov`, `.mp4`) are ignored by git —
convert, commit the GIF, leave the source out.

## Shot list

| File | Shows | Notes |
|---|---|---|
| `pyroclastic.gif` | the default rule churning, then a fly-in | ✅ recorded |
| `glider-gun.gif` | the Gosper gun firing in slab mode | ✅ recorded |
| `clouds.gif` | the Clouds rule's dense magenta mass | ✅ recorded |
| `lightweight-spaceship.gif` | the 2D LWSS crossing the slab | ✅ recorded |
| `spaceship-3d.gif` | the Bays5766 traveller crossing the volume | still wanted — it is `G` preset **0**, and needs a 3D grid, not the slab |

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
ffmpeg -ss 0 -t 6 -i Recordings/Movie_001.mp4 \
  -vf "fps=12,scale=640:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=96[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5" \
  -loop 0 docs/media/output.gif
```

`-ss` is the start offset and `-t` the length, both in seconds, and both belong
*before* `-i` so ffmpeg seeks rather than decoding and discarding.

To shrink a file, in order of least visible damage: shorten it, then drop `fps`,
then `scale`. Measured on this footage, `fps` and resolution dominate; the
dither mode barely matters (bayer vs none differed by under 20%) because the
content is already noise-like, and `max_colors` below ~96 starts banding the
bloom gradients without saving much.
