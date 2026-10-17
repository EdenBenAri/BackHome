# Changelog

## [1.0.0] — 2026-08-17

First release.

- Seed-driven generation: the same seed with the same profile always builds the same
  sky, on every machine and platform.
- Eight sky archetypes — Deep Space, Solar System, Nebula Field, Planet Orbit, Star
  Cluster, Galaxy Vista, Asteroid Field, Orbital System.
- Eight planet kinds, each with its own surface generator: rocky, gas giant, ice,
  lava, terran, star, desert, toxic.
- Moons on inclined 3D orbits passing in front of and behind their planet, ring
  systems with shadows cast onto the planet, atmospheric scattering shells.
- Nebulae in three styles with palette groups, spiral galaxies, asteroid belts,
  interstellar dust, distant galaxies, shooting stars.
- Three-layer parallax star field with twinkling, blinking and diffraction spikes,
  plus a constant sky drift.
- Pixel-art and smooth rendering from one shader, switched on the profile.
- Thirteen presets — seven pixel art, six smooth.
- Tabbed settings panel with search, per-tab reset and live updates in edit mode.
- Seed history, so a sky you rolled past is not lost.
- Sky snapshots: save a seed together with a complete copy of the settings, and restore
  that exact sky later even after the profile has been edited.
- Automatic fit to the camera, so one profile frames the same way at any camera size.
- Body tiling, so a travelling camera never runs out of sky.
- One draw call, one material, no texture assets.
- Demo scene cycling every preset, a prefab, and a creation menu.
