# BackHome

Unity **6000.6.0f1** project. Open the repository root in Unity Hub. The build scenes are `Assets/Scenes/Galaxy.unity` and `Assets/Scenes/Ship.unity`.

## Where things belong

- `Assets/Scripts/` — runtime C# code, grouped by gameplay feature. UI code is split into HUD, cargo, inventory, input, navigation, holograms, feedback and tutorials.
- `Assets/Editor/` — all first-party editor code. `Build/` contains build/release tools, `Validation/` contains development checks grouped by feature, and the remaining folders contain inspectors, authoring and maintenance tools.
- `Assets/Resources/` — assets loaded by gameplay, including worlds, creatures, player, weapons and UI. These folder names form runtime resource paths.
- `Assets/Scenes/`, `Assets/Settings/`, `Assets/Input/` — scenes, rendering/game settings and input action assets.
- `Assets/Plugins/` — Android, iOS and WebGL native haptics integrations. Keep the platform folder names.
- `Assets/AssetStore/` — imported third-party assets. Preserve this directory.
- `Assets/TextMesh Pro/` — TextMesh Pro support assets, shaders, fonts and resource settings.
- `Packages/`, `ProjectSettings/` — Unity package dependencies and project configuration.
- `docs/` — feature documentation, references and the organization report.
- `tools/validation/` — checks that run outside Unity; `tools/maintenance/` — structure auditing; `tools/capture/` — Windows screenshot helpers.
- `Builds/`, `Library/`, `Logs/`, `Temp/`, `UserSettings/`, `.vs/` and generated solution/project files — local outputs or caches, excluded from version control. Keep release outputs in `Builds/` and diagnostic outputs in `Logs/`.

## Checks

Run from the repository root:

```sh
node tools/validation/test-mobile-haptics.cjs
node tools/validation/balance-audit.mjs
```

Unity checks remain available in the **BackHome** menu. Moving an editor script does not change its menu entry. See [mobile haptics](docs/mobile-haptics.md), [game balance](docs/game-balance.md) and [planet authoring](docs/planet-authoring.md) for feature-specific workflows.

For a future structural change, run `node tools/maintenance/project-structure-audit.mjs snapshot` before editing and `node tools/maintenance/project-structure-audit.mjs compare` afterwards. Archive any previous `Logs/ProjectOrganization/` reports first; the tool refuses to overwrite an existing baseline. It checks GUID references, metadata pairing and AssetStore hashes, and reports moved, removed and changed assets. It does not prove that an asset is unused or replace Unity compilation and play checks.

## Organization rules

Move Unity assets through the Project window or `AssetDatabase.MoveAsset`, preserving their `.meta` files and GUIDs. Keep editor-only code inside an `Editor` directory. Changes to `Resources` paths, scene names, plugin folder names or assembly boundaries require a separate dependency review. An asset with no serialized references can still be loaded dynamically; do not delete it on that evidence alone.

The [organization report](docs/project-organization-report.md) records the cleanup, verification and existing findings.
