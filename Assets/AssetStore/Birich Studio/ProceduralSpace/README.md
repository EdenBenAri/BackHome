# Procedural Space

Procedural space backgrounds for Unity (URP). One quad, one draw call, **no texture
assets**: planets, moons, rings, nebulae, galaxies, asteroid belts and parallax star
fields, all generated from a single seed.

## Start here

**`GameObject > Birich Studio > Procedural Space > Space Background`**

That is the whole setup. The object arrives with a preset assigned, a material, and a
camera fit that keeps it covering your view — you should be looking at a finished sky
straight away. Then press **Randomize Seed** in the inspector until you find one you
like.

The prefab in this folder does the same thing if you prefer dragging.

## Full documentation

**[Documentation/Documentation.md](Documentation/Documentation.md)** — every setting
explained in the order the panel shows them, the scripting API, and troubleshooting.
Also reachable from **`Window > Birich Studio > Procedural Space > Documentation`**.

[Documentation/CHANGELOG.md](Documentation/CHANGELOG.md) — release history.

## What is in this folder

| | |
|---|---|
| `Presets/` | 13 ready-made styles, seven pixel art and six smooth |
| `Demo/` | A sample scene that cycles through all of them |
| `Runtime/` `Editor/` `Shaders/` | The package itself |
| `SpaceBackground.prefab` | A configured background, ready to drop in |

## Requirements

Unity 2022.3 LTS or newer, with the **Universal Render Pipeline** — 2D Renderer or
Forward, orthographic or perspective camera. The Built-in pipeline and HDRP are not
supported.

The Input System is used only by the demo scene's mouse-look camera. Deleting `Demo/`
removes that dependency.

## Two things that trip people up

**Judge framing in the Game view.** The sky anchors to whichever camera renders it —
that is what makes parallax work — so in the Scene view it follows the *Scene* camera
while the quad follows your game camera. They only line up when you are looking from
the game camera's position.

**In pixel-art mode, match Pixels Per Unit to your camera.** The grid is in world
space, and if it does not divide evenly into your screen pixels the image shimmers
whenever anything moves. The inspector checks this and offers to align either side;
for a shipping game, pair it with URP's Pixel Perfect Camera.

Both are covered properly in the Troubleshooting section of the documentation.

## Editing presets

The presets in `Presets/` ship with the package and are **overwritten when it
updates**. The inspector warns you and offers **Duplicate & Edit** — take it, or use
**Save As New Preset** at the bottom of the settings panel.

To keep one specific sky rather than a style, use **Save Snapshot…**: it stores the
seed together with a complete copy of the settings, so that exact sky can be restored
later even after the profile has changed.

## Support

Questions, bug reports and feature requests: **birichgame@gmail.com**
