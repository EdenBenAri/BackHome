# Nyxara: 3D plane authoring, spherical gameplay

The production world is `Assets/Scenes/PlanetNyxara.unity`, backed by `Assets/Resources/Galaxy/Nyxara/PlanetNyxara.prefab`. Use **BackHome > Nyxara > Open World**. The scene retains its original GUID and build entry, so death/restart, ship takeoff and hologram travel all use this world. There is no separate prototype world.

## Editing

Select **PlanetNyxara > Frame 3D Plane** for perspective editing. **Top View** is optional. Frame isolates the world in Scene View so HUD and background objects do not intercept clicks; **Show Whole Scene** exits isolation. These controls do not change the Game camera. Use Unity's usual orbit, pan, zoom and W/E/R transform tools.

**Paint in Scene View** edits terrain or raises, lowers and sets ground height. Shift reverses raise/lower or resets terrain/height to its default. The inspector also provides a cached cell grid, eyedropper, flood fill, whole-map fill and terrain preset load/save. The cursor hits the visible terrain mesh even in perspective. A stroke is one Undo operation; the preview mesh and picking collider refresh on release. Alt navigates and Esc exits painting.

Under **Authoring Plane**, select a visible model to edit its saved `PlanetPlacement` handle. Use **Place On Plane**, **Snap To Ground**, **Duplicate Selected Prop** and **Remove Prop** in its inspector. Use the duplicate button instead of Ctrl+D, which creates an invalid second binding to the same gameplay object. Disabling a handle also omits its gameplay object. Removing an inherited prefab child from a scene instance records this disabled override; removing an added object or a child in Prefab Mode deletes its handle and runtime target.

Add rigid environment prefabs through **New prop prefab > Place New Prop On Plane**. **Environment Palette / Scatter** opens the existing Grass, Rocks and Trees palettes with radius, spacing, density, yaw and scale settings. Paint on the plane; Shift erases the chosen category. Each scatter stroke is one Undo operation. The palette is stored on **Authoring Plane/Environment**.

**Terrain Settings** edits the underlying grid and tileset. The continent generator and tileset importer operate on this data too. **Edit Gameplay Settings** on a placement selects its corresponding runtime object when you need to change a gameplay component. Edit positions through the plane handles: their poses are authoritative in Play.

Save the scene for instance overrides, or edit the prefab in Prefab Mode for reusable changes. Press Play to project the world onto the sphere; stopping restores plane editing. Keep **Reload Scene** enabled in Enter Play Mode Settings. Keep the world at unit scale and both **Runtime Planet** and **Authoring Plane** at identity local transforms. Leave **Runtime Planet** inactive while editing.

## Runtime and performance

`Scripts/World/Planet/Authoring` contains `PlanetAuthoring`, `PlanetPlacement` and `PlanetProjection`. Editor tools and the disposable preview shader live in `Scripts/Editor/Planet/Authoring`. There is one authoritative `PlanetTileMap` on the inactive Runtime Planet, initially 72 × 36 cells, and 228 authored placements including the ship and spawn point.

X is longitude, Z is latitude and Y is radial elevation. At radius 75 the plane spans approximately 471.24 × 235.62 metres. Left/right edges join; each north/south edge collapses to a pole. A tangent rotation frame preserves heading and tilt. `PlanetAuthoring` executes at order -10000, applies all poses and active states, hides the authoring hierarchy, then activates spherical gameplay. The runtime target references remain internal to the prefab.

Projection runs once per scene load. There is no per-frame conversion, deformation or second physics world. Plane terrain, water and prop previews are editor-only, never serialized or included in a player build, and released before Play. Terrain painting updates only the ground preview on release. Model picking rejects bounding-box misses and lazily reuses one query collider per shared mesh. The inactive placement handles remain in memory during Play but have no update callbacks.

The old sphere placement windows, layout generator, test terrain, direct sphere painting and `PlanetSurfaceAlign` snapping code have been retired. `PlanetEnvironmentObject` preserves streaming grouping without the previous per-object `LateUpdate`. Spherical terrain generation, locomotion, combat distances, water, environment streaming and scene travel are still required for spherical gameplay. `NyxaraHologram` is an independent prefab; it does not inherit the gameplay world.

This architecture reduces authoring duplication and removes unnecessary callbacks. It does not establish an FPS improvement on target hardware; profile a device build before making that claim.

## Verification

**BackHome > Nyxara > Validate World** runs 4,096 projection/rotation/tangent cases, authored pose/scale checks, terrain seam brushes, topology checks, 128 perspective/vertical ray hits on a transformed plane, exact prop picking, preview cleanup, palette add/project/remove, preset round trips and edited prefab save/reload. It uses temporary contents and verifies that production scene and prefab files remain unchanged. Report: `Logs/PlanetAuthoring-validation.txt`.

**Validate Play And Travel** opens the production scene, drives player movement, invokes death/restart, takes off to Ship and returns through the hologram. It checks single planet/player/HUD, camera, creatures, terrain render/collider agreement, 64 surface probes per world entry, radial alignment, stable terrain version and absence of editor preview physics. The Ship check verifies cockpit camera, pilot hands, hologram mesh, click collider and beam reference. Report: `Logs/PlanetPlayValidation.txt`. Save open scenes first. These are Editor regression checks, not a device benchmark or exhaustive gameplay QA.

## Authoring limits

This is an equirectangular chart: spacing and object footprints distort towards the poles. Seam-crossing props are not drawn twice in the editor. The flat preview uses a lower-resolution ground grid; spherical generation defines exact polar caps and gameplay collisions. Large props remain rigid. Changing terrain does not automatically move existing props; use **Snap To Ground** where needed.
