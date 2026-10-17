# Procedural Space — Documentation

Fully procedural space backgrounds for Unity (URP). One quad, one draw call, **zero
texture assets**: planets, moons, rings, nebulae, spiral galaxies, asteroid belts,
shooting stars and multi-layer parallax star fields, all composed from a single seed.

Release history is in [CHANGELOG.md](CHANGELOG.md).

## Contents

- [Requirements](#requirements) · [Quick start](#quick-start) · [The four ideas](#the-four-ideas-worth-understanding) · [Presets](#presets)
- [Settings](#settings) — [Using the panel](#using-the-panel) · [On the component](#on-the-component) · [Style](#style) · [Sky](#sky) · [Motion](#motion) · [Deep Sky](#deep-sky) · [Light](#light)
- [Snapshots](#snapshots)
- [Scripting](#scripting)
- [Troubleshooting](#troubleshooting)

---

## Requirements

- **Universal Render Pipeline.** The shader does not compile under the Built-in
  pipeline or HDRP. Both the URP 2D Renderer and the standard renderer work, with
  orthographic or perspective cameras.
- Developed and verified on **Unity 6.3 (6000.3)** with **URP 17.3**.
- **Input System** — needed only by the `Demo` folder, for the sample scene's
  mouse-look camera. Delete `Demo/` and the dependency goes with it.

## Quick start

**`GameObject > Birich Studio > Procedural Space > Space Background`.**

That is the whole setup. The object arrives with a preset assigned, a material, and
a `Space Camera Fit` that keeps the quad covering your camera. You should be looking
at a finished sky immediately.

Then, in the inspector:

1. Press **Randomize Seed** until you find a sky you like. The ◀ ▶ arrows step back
   and forward through the ones you have already seen, so passing a good one does
   not lose it.
2. Swap **Profile** for any preset in `Presets/` to change the whole style.
3. Open the settings panel underneath to tune it — see [Settings](#settings).

The prefab at the package root does the same thing if you prefer dragging.

## The four ideas worth understanding

**A seed is a sky.** The same seed with the same profile always composes the same
sky, on every machine and platform. A backdrop costs one integer to store, which
makes it safe for procedurally generated levels, replays, and "this planet is that
planet" identity.

**A profile is a style, not a sky.** The profile decides what *kind* of skies the
seed can draw — which sky types appear, which planet kinds exist, how crowded, how
lit, how fast. Change the seed for another sky in the same style; change the profile
to change the style. One profile can drive any number of backgrounds.

**Keep a sky with a snapshot.** Those two facts together have a gap in them: a seed only
means something *given a profile*, so the moment you nudge a slider, every seed you had
saved resolves to something else. A snapshot closes it by storing both at once — see
[Snapshots](#snapshots).

**A sky is built in layers.** Gas at the very back, three tiers of stars, planets
between them, rubble in front. Each layer drifts against camera motion at its own
rate, and that difference is what reads as depth. There is also a constant drift so
the sky travels even when the camera does not.

## Presets

Seven pixel-art and six smooth. Each one answers a different question rather than
being the same sky in another colour, and each says in the panel what it is for.

| Pixel art | Smooth |
|---|---|
| Pixel Odyssey — every sky type in rotation | Smooth Cosmos — the balanced default |
| Pixel Deep Space — quiet, gameplay-safe | Smooth Nebula Bloom — the showpiece |
| Pixel Nebula Dreams — gas dominates | Smooth Galaxy Vista — a galaxy edge-on |
| Pixel Asteroid Run — rubble and motion | Smooth Solar System — planets in orbit |
| Pixel Galaxy Edge — a spiral in the distance | Smooth Frozen Reach — a cold, pale system |
| Pixel Solar System — a system you can watch | Smooth Minimal — space as a whisper |
| Pixel Retro Mono — four colours, one hue | |

Presets that ship with the package are overwritten when it updates. The inspector
warns you and offers **Duplicate & Edit** — take it.

---

## Settings

Everything here appears in the panel in this order, so you can read this beside the
inspector. Values in brackets are `[range] default`.

Settings marked **(More)** live under the collapsed *More* foldout at the bottom of
their tab — they are rarely worth touching, not less powerful.

Some settings only exist in one rendering mode and are hidden in the other. That is
deliberate: a control that silently does nothing is worse than a missing one.

### Using the panel

- **Tabs** by subject. One tab at a time keeps the panel short.
- **Search** ignores the tabs and shows every match, labelled with where it lives.
  Type `dither` and you get it without knowing which tab it is on.
- **Reset** restores the current tab only. Push the motion settings around, dislike
  them, and get the motion settings back — not your palette too.
- Edits apply **live**, in edit mode, to every background using that profile.

---

### On the component

These are on the `Space Background` object, above the profile panel.

**Profile** — the style preset. Empty runs on built-in defaults and cannot be tuned;
the inspector offers to create one.

**Seed** `[int] 20260813` — the sky's identity. The same seed with the same profile
always composes the same sky.

**Randomize Seed On Play** `false` — draw a fresh seed each time play mode starts.
Every run gets its own sky.

**Random Archetype** `true` — the sky type is drawn from the seed using the profile's
weights. Turn it off to force one, which is what **Forced Archetype** below is for.
Useful for art direction and for testing.

**Sorting Layer / Sorting Order** `"" / -1000` — where the backdrop sits among your
sprites. Layer is compared *before* order, so a very negative order cannot put the
sky behind something on a lower layer; set the layer too if your project defines its
own.

**Material** — optional. Left empty, one is created from the shader at runtime. It is
a fallback: assigning it to an object that already has a material does nothing.

##### Seed controls

**◀ / Randomize Seed / ▶ / Rebuild.** The arrows walk the seeds you have already
rolled (32 deep), so rolling past one you liked does not lose it. **Composed
Archetype** underneath tells you what the current seed actually drew.

#### Space Camera Fit

A separate component, added for you by the menu and the prefab.

**Camera** — empty means the main camera.
**Padding** `[1–2] 1.15` — how far the quad overhangs the view, so a changing aspect
ratio never exposes an edge.
**Distance** `50` — how far along the camera's forward axis the quad sits. Only
matters for perspective cameras and for staying inside the far clip plane.

Disable or remove this component to place and scale the quad yourself.

---

### Style

What the sky is made of visually — the rendering language, and nothing else.

**Description** — free text. What this preset is for, in a sentence. It is the only
thing that makes a library of presets browsable instead of guessable.

**Pixel Art** `false` — the master switch between two complete rendering languages,
not a filter over one. Off: anti-aliased limbs, continuous terminators, soft
atmospheric haze, dust and distant galaxies. On: everything snaps to a pixel grid,
brightness is posterized, transitions are broken with ordered dithering.

The remaining Style settings only exist with Pixel Art on.

**Pixels Per Unit** `12` — the pixel grid's density, in world space. **Match your
project's PPU.** If this does not divide evenly into your screen pixels the whole
image shimmers whenever anything moves; the inspector checks this for you and offers
to fix it. See [Troubleshooting](#the-pixels-shimmer).

**Color Levels** `[2–32] 8` — how many brightness steps a surface is allowed. Fewer
is bolder and more obviously posterized; 4 is Game Boy territory.

**Dither** `[0–1] 1` — 0 gives hard bands between those levels, 1 breaks every
transition with an ordered dot pattern. Lower it if the sky reads as noisy; with very
few Color Levels almost the whole sphere counts as a transition, so keep it partial
there or planets turn into checkerboards.

**Hue Shift** `[0–1] 0.6` — how far shading moves the *hue* as well as the value:
shadows toward cool violet, lit faces toward warm. Darkening a colour alone only
walks it toward grey, and a sky full of grey shadows is the flattest thing pixel art
can do. 0 is pure value shading.

**Terminator Steps** `[1–16] 5` **(More)** — brightness steps across the day/night
boundary on a planet.

**Surface Texels** `34` **(More)** — target texel count across a body's *diameter*.
Surface chunkiness cannot come from one fixed PPU: a value that looks right on a
giant collapses a small body to a handful of pixels.

**Surface Block Size** `(2, 4)` **(More)** — the finest and coarsest a surface block
may be, in *screen* pixels. Blocks are rounded to whole screen pixels so their edges
do not crawl as the camera pans. Both components are held at 1 or above, and the
second at or above the first, so the pair cannot be inverted or zeroed.

---

### Sky

What is in the sky and where.

#### Contents

**Sky Types** — eight weights, one per sky archetype. The archetype is drawn first
and decides what *dominates* the frame, so skies differ in structure rather than only
in colour. Set one to 0 to remove it from your game entirely.

| Type | What it is |
|---|---|
| Deep Space | Bare void: dense stars, a hint of dust, a few distant planets. The safest to put gameplay in front of. |
| Solar System | A distant sun plus several planets of different kinds, some with moons. |
| Nebula Field | Coloured gas dominates, stars fade behind it, a single planet in silhouette. |
| Planet Orbit | One giant dominates from the edge, rings and moons; everything else is forced tiny. |
| Star Cluster | Very dense, hue-varied stars, almost no planets. |
| Galaxy Vista | A tilted spiral galaxy rules the distance, foreground stays sparse. |
| Asteroid Field | A rubble belt sweeps across the sky, planets stay small and distant. |
| Orbital System | A star with its planets on concentric orbits, actually revolving. The only sky that reads as a *system*. |

**Planet Kinds** — eight weights, one per planet kind. Raising one only ever converts
bodies *to* that kind; the worlds it does not win stay exactly as they were, so you
can tune one entry at a time.

| Kind | What it looks like |
|---|---|
| Rocky | Crater fields, dark plains, sharp airless edge |
| Gas Giant | Latitude bands, turbulence, a longitude-locked storm |
| Ice | High albedo, crack network, wide polar caps |
| Lava | Dark crust with self-lit cracks and eruptions — glows on the night side |
| Terran | Continents, ocean, polar cap, a cloud layer at its own speed |
| Star | Placed by the sky type, never as a planet — the slider is disabled |
| Desert | Sheared dune banding, canyon lines, faint frosted poles |
| Toxic | One unbroken, heavily swirled acid cloud deck |

**Size has the last word.** Gas giants and toxic worlds are never drawn small — band
structure and swirling cloud decks cannot read across a handful of pixels, so raising
those weights changes nothing in a sky made of distant specks.

**Planet Count** `[0–3] 1` — scales how many bodies each sky type places. The type
still decides the *shape* of the sky; this only changes how crowded it is. A hero
body is never scaled away, even at 0 — a sky type built around one giant is defined
by having it.

**Planet Size** `[0.2–3] 1` — scales every body's radius, keeping the relation between
the size classes intact. **This is the control to reach for when planets do not fit
your camera**, not Placement Area.

#### Placement

**Placement Area** `(18, 10)` — the **half**-extent of the region body centres are
scattered over; the area is twice this in each direction. It also sets how far apart a
body's repeats are placed, so a small value brings the repeat into the same frame.

**Spacing** `[≥0.5] 2.2` — two bodies must be at least this multiple of their summed
radii apart. Raise for an airier sky. 1 puts two bodies edge to edge; below that they
overlap on purpose, which is a legitimate look but gets hard to read.

**Offset** `(0, 0)` — shifts the whole composition in world units. Use it to keep a
corner of the frame clear for a HUD.

#### Framing

**Fit To Camera** `true` — scales body sizes and placement to what your camera can
actually see, so one profile frames the same way on a phone and an ultrawide.

**Designed For View Height** `[≥1] 17` — the view height, in world units, the settings
above are written for. Leave it at 17 unless you have deliberately re-tuned the sizes
for a different frame.

#### Rings & moons

**Rings** `[0–1] 0.5` — master chance. Each kind carries its own odds on top: gas
giants always qualify (×1.0), ice often (×0.45), rock, desert and toxic worlds rarely
(×0.25, ×0.2, ×0.2), terran worlds and stars never. 0 removes every ring.

**Moons** `[0–1] 0.6` — chance of moons on mid and hero bodies. Small bodies never get
them; a moon of a distant speck lands on one pixel.

**Max Moons Per Body** `[0–3] 2` **(More)** — a cap, not a target. Lowering it trims
the bodies that wanted more and leaves the others untouched.

#### Scale classes **(More)**

The three size tiers, in world units. **Planet Size** above scales all three together,
which is almost always what you want instead of editing these.

**Hero Radius** `(9, 15)` — covers a significant part of the screen. At most one per sky.
**Mid Radius** `(2.2, 4)` — prominent without dominating.
**Small Radius** `(0.7, 1.8)` — a distant planet reading as little more than a dot.
**Hero Edge Push** `11` — how far a hero is pushed from the centre. A giant in the
middle swallows the play area.
**Tilt Range** `(-38, 38)` — axis tilt in degrees, so not everything spins about the
same axis.

---

### Motion

Everything here is deliberately slow. A backdrop you can *watch* is a backdrop that
pulls attention off the game.

#### Drift & spin

**Sky Drift** `(0.12, 0.015)` — constant sky movement in world units per second, a
virtual camera glide. Every layer moves at its own parallax rate, so the sky reads as
slow travel even with a static camera. Zero disables it.

**Rotation (seconds)** `(85, 210)` — seconds for a full planet revolution. Rotation
should be something you notice *having happened*, not something you can watch
happening. Too slow is the safer error.

#### Parallax

**Parallax** `[0–3] 1` — scales the whole ladder at once. The *ratios* between layers
are what produce depth, and this leaves them untouched. This is the one to reach for.

The six below are the ladder itself. On-screen shift is `camera displacement ×
parallax`, so lower means further away.

**Bodies (near)** `[0–1] 0.1` **(More)** — the nearest body's rate.
**Bodies (far)** `[0–1] 0.025` **(More)** — the farthest body's rate. Keep it below the
near value: bodies are painted in parallax order, so inverting the band also inverts
which body covers which.
**Nebula** `[0–1] 0.03` **(More)** — the deepest thing in the sky.
**Stars (far / mid / near)** `0.1 / 0.2 / 0.35` **(More)** — three tiers. Two layers
read as "front and back"; three read as continuous depth.

#### Repeat

**Repeat Bodies** `true` — tiles the planets so sustained motion never empties the sky.
Star fields, gas and dust are computed from position and so are endless, but there are
only a handful of bodies at fixed places: with drift on, or a camera that keeps
travelling, they all eventually leave and nothing replaces them.

Turn it off only for a camera that stays put, where the repeat is the only thing it
could ever show you.

**Repeat Spacing** `[1.2–4] 2.6` — extra spacing between a body and its copies, as a
multiple of the placement area. Larger takes longer to come around, at the cost of
gaps with nothing in them. The tile is never smaller than the camera's own frame.

---

### Deep Sky

The things behind the planets.

#### Stars

**Star Density** `[0.1–2] 0.42` — multiplier on the densities the sky type chose, for
all three layers. Thins or thickens without breaking the ratio between types: a star
cluster still reads denser than deep space.

**Bright Stars** `[0–0.3] 0.05` — density of the few that read as points of light with
a halo rather than specks. Scarcity is the point; they are what give the field scale.

**Twinkle** `[0–1] 0.5` — brightness pulsing of near stars. They never fully disappear.

**Blinking** `[0–1] 0.1` — fraction that *do* blink fully out and back. Keep it small:
scarcity is what makes the motion precious.

**Hue Variety** `[0–2] 1` **(More)** — multiplier on how far star colours spread between
warm and cool. The sky type sets the character; 1 leaves its choice alone, 0 gives a
white field.

**Diffraction Spikes** `[0–1] 0.45` **(More, smooth only)** — the rays on a bright star
in a telescope photograph. They are the instrument, not the star, which is why every
star shares one orientation. Pixel mode draws a plus on the pixel grid instead.

**Spike Count** `[2–8] 6` **(More, smooth only)** — six is the JWST signature, four the
classic Hubble cross.

#### Gas & dust

**Nebula** `[0–2] 1` — multiplier on the gas density each sky type composes.

**Nebula Palette** — colours in **groups of three**: gas A, gas B, hot core. Every sky
draws one group and jitters it, so a group is a family rather than a single nebula.
Four groups ship by default. Keep the count a multiple of three; extras are ignored.

**Dust** `[0–1] 0.55` — interstellar dust: cold clouds that emit nothing and absorb the
galaxy and star layers behind them. Without absorption a sky can only get brighter and
flattens into an even wash, so this is what carves structure into the void.

**Distant Galaxies** `[0–0.2] 0.035` **(More, smooth only)** — faint elliptical smudges,
too far to resolve. They say the void keeps going past the stars in it. Keep them rare
and dim; once they read as objects rather than a suspicion of light, they look painted.

#### Shooting stars

**Enabled** `true` — occasional streaks. They work with every sky type.

**Seconds Between** `9` — average, per track. Each streak jitters its own timing so the
rhythm never turns metronomic.

**Brightness** `[0–3] 1.3` — above 1 glows nicely with bloom.

**Speed** `30` **(More)** — world units per second.
**Tail Length** `4` **(More)** — world units.
**Simultaneous** `[1–2] 1` **(More)** — how many can be airborne at once.
**Color** `(0.9, 0.95, 1)` **(More)** — slightly blue-white reads as the classic meteor.

---

### Light

#### Sun

**Light From Seed** `true` — the sky lights itself. When the composed sky contains a
star, light comes **from it**: every body is lit from its own angle toward that star
and dims with distance from it, which is what makes a solar system read as one system
rather than as unrelated spheres sharing a light rig. With no star, an angle is drawn
from the seed.

**Direction** `(-0.55, 0.45)` *(only with Light From Seed off)* — a fixed light angle
for every sky.

**Elevation** `[0.05–1] 0.45` *(only with Light From Seed off)* — how far the sun sits
toward the viewer. Near 1 the light comes from behind the camera and discs read flat
and evenly lit; low values swing the terminator across the visible face and bodies
model themselves as spheres.

**Elevation Range** `(0.14, 0.45)` **(More, Light From Seed on)** — the range that
elevation is drawn from. Widen it for skies that swing between fully lit worlds and
dramatic crescents.

**Pulse** `[0–1] 0.45` — how far the sky's star's output rises and falls over time. Real
stars vary far less than this, so treat it as a stylistic amplitude rather than a
simulation. The rhythm is built from three periods that never line up, so it never
repeats the same shape. The whole sky breathes with it — planets, moons, rings and the
belt are lit by that star, and the change reaches distant bodies later than near ones.
0 is a perfectly steady sun.

#### Shading

**Night Side** `[0–1] 0.96` — darkness of the shadowed face. Space is not a room: there
is no floor or wall bouncing light back, so an unlit face is lit only by starlight.
Values below about 0.9 read as a studio fill light and quietly flatten every body.

**Atmosphere** `[0–2] 0.6` — master scale on the scattering shell every body wears at
its limb. Keep it low: wide halos make planets look like light sources and fill the
void between them, and it is the darkness between bodies that carries depth. 0 is
airless everywhere.

Both modes shade the limb. Past the disc the two part ways: smooth mode carries the
glow outward, while pixel mode keeps a planet's hard edge — a shell a fraction of a
texel wide is not a halo, it is an outline. A star's corona survives in both,
posterized in pixel mode.

**Ambient Fill** `[0–1] 0.035` **(More)** — light on the shadowed side.

#### Background

**Background** `(0.031, 0.024, 0.055)` — the deep end of the screen-locked gradient.
**Background (far)** `(0.078, 0.051, 0.125)` — the other end.
**Gradient Direction** `(0.35, 1)` **(More)** — direction in world space.
**Gradient Scale** `0.012` **(More)** — how fast it sweeps across world space.

---

## Snapshots

A profile is a style and a seed is a sky inside that style. That works until the style
moves: roll seeds until one is perfect, nudge a slider a week later, and the sky is gone.
The seed still resolves — it just no longer means what it meant, and there is no way back,
because the thing that changed was not the seed.

**A snapshot stores both.** The seed, the archetype mode, and a complete copy of every
setting, in one small asset. Restoring it does not depend on the profile still being what
it was, or on that profile existing at all.

### Saving

In the `Space Background` inspector, under **Snapshot**, press **Save Snapshot…** and choose
where to put it. It defaults to `Assets/Space Backgrounds/` and suggests a name from the
profile and the seed.

Give it a **Note** afterwards. It is the only thing a list of snapshots cannot work out for
itself; the seed, the source profile and the capture date are all recorded automatically.

### Loading

Drop the snapshot into the field and press **Load**.

**Loading writes the stored settings into the assigned profile.** That is what restoring
means — but it also means every background sharing that profile changes with it, exactly as
if you had edited the settings by hand. Undo reverses it. If the profile is one that ships
with the package, you are asked first, because those are overwritten on the next update.

A background with no profile assigned gets one created for it, so Load never silently does
nothing.

### What a snapshot does not capture

**The camera.** With *Fit To Camera* on, body sizes and placement follow the view, so a
snapshot restored against a very different camera frames differently. The settings are
identical; the framing is not. Turn Fit To Camera off if you need a sky pinned in world
units.

### Version tolerance

Settings are stored as JSON, which has two consequences worth knowing. A snapshot is a small
text file that source control can diff and merge. And a snapshot taken by an older version
still restores under a newer one — a setting it has never heard of simply keeps whatever the
target profile already had, rather than the load failing.

---

## Scripting

```csharp
using BirichStudio.ProceduralSpace;
```

Everything below is on the `SpaceBackground` component. The composer itself is
internal — you drive the sky through the seed and the profile, not by placing bodies.

### Composing

```csharp
var bg = GetComponent<SpaceBackground>();

bg.Rebuild(12345);        // compose this seed (and push it onto the seed history)
bg.Rebuild();             // recompose the current seed — after changing a profile field
bg.RandomizeSeed();       // roll a new one
```

`Rebuild` is cheap enough to call on a level load or a scene transition. It is not
something to call every frame — nothing about the sky needs recomposing per frame,
and the animation happens in the shader.

### Reading

```csharp
int seed = bg.Seed;                    // current seed
SkyArchetype what = bg.Archetype;      // what this seed actually composed
```

`Archetype` is useful for reacting to the sky you got — different music for a nebula
sky, a different fog colour near a star.

### Changing the style

```csharp
bg.Profile = myProfile;                // swaps the whole style; rebuilds immediately
```

One profile can drive any number of backgrounds. Assigning it recomposes.

### Forcing a sky type

```csharp
bg.SetArchetype(SkyArchetype.NebulaField);   // force it
bg.SetArchetype(null);                       // back to drawing it from the seed
```

Useful for art direction — a nebula chapter, a deep-space chapter — without giving up
the variety inside that type.

### Seed history

```csharp
if (bg.CanStepSeed(-1)) bg.StepSeedHistory(-1);   // back
if (bg.CanStepSeed(+1)) bg.StepSeedHistory(+1);   // forward
```

The last 32 seeds are kept, so a "give me another one / no, the previous one" flow
works without you storing anything. This is what the ◀ ▶ buttons in the inspector
use; expose it in-game if your players pick their own sky.

#### Snapshots

```csharp
public SkySnapshot snapshot;

bg.LoadSnapshot(snapshot);                 // restore into this background's own profile
bg.LoadSnapshot(snapshot, someProfile);    // ...or into one you nominate
bg.CaptureSnapshot(snapshot);              // freeze the current sky into it
```

`LoadSnapshot` returns false when the snapshot is empty or there is no profile to write
into. It writes the settings into the profile it is given, sets the seed and archetype mode,
and recomposes.

At runtime, hand it `Instantiate(myProfile)` if you would rather not mutate the shared asset:

```csharp
var scratch = Instantiate(bg.Profile);
bg.Profile = scratch;
bg.LoadSnapshot(snapshot);
```

### Types

```csharp
enum SkyArchetype { DeepSpace, SolarSystem, NebulaField, PlanetOrbit,
                    StarCluster, GalaxyVista, AsteroidField, OrbitalSystem }

enum PlanetKind   { Rocky, GasGiant, Ice, Lava, Terran, Star, Desert, Toxic }

enum NebulaStyle  { Wispy, Billow, Filament }
```

`SpaceBackgroundProfile` is a plain `ScriptableObject` — every setting in
[Settings](#settings) is a public field you can write from script. Call
`bg.Rebuild()` afterwards; assigning through `bg.Profile` does it for you.

### Determinism

The same **(profile, seed, camera size)** always produces the same sky — on every
machine, platform and Unity version. Hashing is integer arithmetic, so results are
bit-identical across graphics backends, and the generator uses its own PRNG rather
than `System.Random`, whose algorithm is not contractual between .NET versions.

So a sky costs one `int` to save. Store the seed with your level, not a screenshot.

One consequence worth knowing: **camera size is part of the identity** when *Fit To
Camera* is on. That is what makes a profile frame the same way on any display — but
if you need a sky to be pixel-identical across resolutions, turn Fit To Camera off and
place it in fixed world units.

---

## Troubleshooting

### The pixels shimmer

**The most common problem, and it only exists in motion** — no screenshot can show it.

*Pixels Per Unit* is a grid in **world** space: the shader snaps every sampling
position to `1/PPU` world units. Nothing ties that grid to your camera, so one shader
texel comes out at whatever fraction of a screen pixel your camera happens to give it.
Two things then go wrong at once. Blocks are drawn at uneven sizes, some five screen
pixels and some six. And the moment anything moves — the sky drift, the camera, a
planet turning — every block boundary re-lands on a different screen pixel, so the
image reshuffles which blocks are which size, every frame. On a lava world, whose
glowing cracks put maximum contrast across single texels, it reads as the surface
boiling.

The inspector checks this and tells you the exact ratio, with two buttons: move the
camera to the grid, or the grid to the camera.

**Either button aligns the current window size only** — the grid is a ratio, so it
comes apart again at another resolution. For a shipping game use URP's **Pixel Perfect
Camera** with a fixed reference resolution, and set *Pixels Per Unit* here to its
Assets PPU, or an exact divisor of it. The sky then rides the same grid as your
sprites at every resolution.

### The planets are in the wrong place / outside the frame

**Judge framing in the Game view.** The sky anchors to whichever camera renders it —
that is what makes parallax work — and in the Scene view that camera is the Scene
camera. So the composition follows your Scene navigation while the quad stays glued to
your game camera, and the bodies look like they are being generated off to one side.
The two only line up when you are looking from the game camera's position.

If they are genuinely too big or too small in the Game view, reach for **Planet Size**,
not Placement Area. Placement Area is a half-extent and also sets how far apart a
body's repeats sit, so shrinking it crowds the sky and can bring a repeat into frame.

Check that **Fit To Camera** is on and **Designed For View Height** is not 0.

### The sky empties out as the camera travels

Turn on **Repeat Bodies** (Motion tab). Star fields, gas and dust are computed from
position and are endless, but there are only a handful of bodies at fixed places —
with drift on, or a camera that keeps moving, they all eventually leave the frame.

If you can see a body and its copy at once, raise **Repeat Spacing**.

### I changed a setting and the whole sky changed

Some settings are *about* the layout and cannot avoid it: **Sky Types**, **Planet
Count**, **Placement Area**, **Planet Size**, **Spacing**.

Everything else should change only what it names. Raising a **Planet Kind** weight
converts some bodies to that kind and leaves their positions, moons, lighting and
nebula exactly as they were. If you see something else move, that is a bug worth
reporting.

### Editing a preset does not stick

Presets that ship with the package are overwritten when the package updates, and the
inspector warns you. Press **Duplicate & Edit**, or **Save As New Preset** at the
bottom of the panel.

### Performance

The cost is **full-screen fragment work** — it scales with screen pixels, not with how
much is in the sky. Adding planets is close to free; rendering at 4K is not.

In rough order of what to turn down first:

1. Render the backdrop at a lower resolution than the game if your pipeline allows it.
2. **Dust**, **Distant Galaxies** and **Diffraction Spikes** (Deep Sky tab) are each a
   separate cost and each can go to 0.
3. **Star Density** — three layers are evaluated at every pixel.
4. **Nebula** — the gas is the single most expensive thing drawn on every pixel.
5. Pixel-art mode is *cheaper* than smooth mode: it skips the relief normals, a
   planet's outer atmosphere glow, the diffraction-spike star pass and distant
   galaxies. Dust still costs the same in both.

Rocky planets are the most expensive bodies, because craters are a real 3D scan rather
than a texture lookup.

### Shader compile time

The shader is large and compiles both rendering modes into one program. If the editor
freezes while it imports, turn on **Project Settings → Editor → Asynchronous Shader
Compilation** — it moves the work off the main thread and shows a placeholder
meanwhile. Pinning Player Settings to a single graphics API during development also
cuts the work proportionally.

### It renders magenta

The shader failed to compile. Check the Console. Magenta means the material is falling
back, not that a setting is wrong.

### Nothing is drawn at all

- The project is not on URP, or is on the Built-in pipeline. The shader targets URP.
- The backdrop is behind the camera's far clip plane — check *Space Camera Fit →
  Distance*.
- Sorting: a very negative **Sorting Order** cannot put the sky behind something on a
  *lower* sorting layer, because layer is compared before order. Set **Sorting Layer**
  as well if your project defines its own.
