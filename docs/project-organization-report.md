# דוח ארגון הפרויקט — 3 בנובמבר 2026

בוצע ארגון מבני של פרויקט BackHome ב־Unity 6000.6.0f1. תוכן קוד המשחק, הסצנות, ה־Prefabs, החומרים, המודלים, הגדרות הבנייה והחבילות נשמר ללא שינוי. תיקיית `AssetStore`, כולל קובצי המטא שלה, נבדקה באמצעות SHA-256 ונשארה זהה לחלוטין.

## מה השתנה

- **80 קובצי Unity הועברו עם קובצי ה־`.meta` המקוריים שלהם**, דרך `AssetDatabase.MoveAsset`. כל ה־GUID וכל תוכן הקבצים שהועברו נשמרו.
- **51 קובצי כלי עורך** רוכזו תחת `Assets/Editor/`. אלה כוללים את כלי הבנייה תחת `Build/`, כלי תחזוקה תחת `Maintenance/`, וכלי עריכה לפי תחום.
- מתוך כלי העורך, **19 קובצי בדיקות** רוכזו תחת `Assets/Editor/Validation/`, בחלוקה ל־Camera, Creatures, HUD, Planet, Player ו־World.
- **29 סקריפטים של הממשק** חולקו בתוך `Assets/Scripts/UI/` לתיקיות `HUD`, `Cargo`, `Inventory`, `Input`, `Navigation`, `Hologram`, `Feedback` ו־`Tutorial`.
- **5 כלי עזר חיצוניים** חולקו ל־`tools/validation/` ול־`tools/capture/`. הנתיב היחסי בבדיקת WebGL והפקודות בתיעוד עודכנו בהתאם.
- כלי הצילום כותבים כברירת מחדל ל־`Logs/Captures/`, במקום לפזר תמונות בשורש הפרויקט. הנתיב מחושב ממיקום הכלי ואינו תלוי ב־`C:\Projects\BackHome`.
- נוסף [מדריך מבנה הפרויקט](../README.md) ונוסף כלי לקריאת חתימות, GUID והפניות לפני ואחרי שינוי מבני: `tools/maintenance/project-structure-audit.mjs`.

## מה נוקה

- הוסרו **5 תיקיות ריקות** בענף `Assets/Resources/Galaxy/ChapterOne/Creatures/Rime/King`, כולל תיקיות המשנה וקובצי המטא שלהן. אומת שאין הפניות ל־GUID שלהן.
- הוסרו **11 תיקיות ישנות שהתרוקנו** תחת `Assets/Scripts/Editor`, כולל קובצי המטא שלהן. גם כאן לא נמצאו הפניות למזהי התיקיות.
- שני תוצרי Burst שנשמרו בטעות תחת `Data/Plugins/` — `lib_burst_generated.cpp` ו־`lib_burst_generated.wasm`, בסך 417,270 בתים — הועברו לגיבוי מקומי ב־`Logs/ProjectOrganization/GeneratedData/`. הם אינם עוד חלק מעץ קוד המקור. נוסף כלל ממוקד ב־`.gitignore` למניעת כניסתם מחדש.
- כלי ההעברה הזמני הוצא מ־`Assets` אחרי סיום העבודה; עותק שלו נשמר רק עם ראיות הבדיקה ב־`Logs`.

## מה נבדק

- סריקת הבסיס כללה **11,038 קבצים** ב־Assets, Packages ו־ProjectSettings ו־**5,655 GUID**.
- השוואת SHA-256 לאחר העבודה: **אפס שינויים בתוכן של קובצי Unity קיימים**, לאחר התאמת הנתיבים שהועברו. נמחקו רק קובצי מטא של התיקיות הריקות שצוינו לעיל.
- **אפס שינויים ב־AssetStore**, אפס GUID כפולים, אפס נכסים ללא מטא ואפס קובצי מטא יתומים.
- **אפס הפניות GUID בלתי פתורות חדשות**, ואפס הפניות למזהי התיקיות שהוסרו.
- **72 Prefabs** של הפרויקט נטענו לפני ואחרי הארגון, ללא סקריפטים חסרים וללא כשלי טעינה.
- שתי סצנות הבנייה, `Galaxy` ו־`Ship`, נטענות כנכסי Scene עם אותם GUID. גם בסצנת Galaxy הפתוחה לא נמצאו סקריפטים חסרים.
- הקומפילציה בתוך Unity עברה אחרי ההעברות. גבולות קוד הריצה וקוד העורך נשמרו.
- בדיקת Play של Galaxy עברה: הכוכב פעיל, **391 מיקומים ייחודיים**, השחקן חי ו־**18 יצורים** נוצרו, ללא שגיאות או אזהרות אתחול שהבדיקה מנטרת. הבדיקה מבודדת את ההתקדמות השמורה של השחקן.
- עברו **11 בדיקות WebGL haptics** ו־**6 בדיקות haptics בתוך Unity**.
- כלי PowerShell עברו בדיקת תחביר. נבדקו גם הפניות לנתיבים הישנים ורווחים בעייתיים ב־Git.

## ממצאים קיימים ומגבלות הבדיקה

- בדיקת האיזון נעצרת על `KingA1: dies too quickly`. אותה בדיקה מהגרסה המקורית ב־Git נכשלת בדיוק באותו תנאי, וכל נתוני המשחק שנקראים על ידה נשארו זהים. האיזון לא שונה במסגרת הארגון.
- סריקת הבסיס מצאה 92 צירופי קובץ/מחרוזת GUID שלא נפתרו במפת הנכסים והחבילות המקומית. הם נשארו ללא שינוי. סריקה סטטית כזו אינה קובעת שכל מחרוזת היא תלות פעילה שבורה; חלקן נמצאות בנכסי צד שלישי ובהגדרות שמורות. לא בוצע ניקוי אוטומטי שלהן.
- נשמרו Prefabs ונתוני King שנראים כפולים: יש הפניות נפרדות מהסצנה וטעינה דינמית לצורך בדיקות. מחיקתם על סמך שם או תוכן דומה בלבד אינה בטוחה.
- נתיבי `Resources`, שמות הסצנות, תיקיות התוספים לפלטפורמות, הגדרות החבילות, `TextMesh Pro` ותוצרי השחרור ב־`Builds` נשמרו. תיקיות המטמון המקומיות נשארו במקומן.
- לא נוצר Build מלא להפצה ולא בוצעה בדיקה על מכשיר פיזי. האימות כולל קומפילציה, בדיקות נכסים ובדיקת Play מקומית. סצנות לא נשמרו מחדש בעקבות הבדיקות; Unity עשוי לסמן את הסצנה הפתוחה כמשתנה בעקבות רענון כלי העריכה ו־Play.

ראיות מפורטות נשמרו מקומית ב־`Logs/ProjectOrganization/`, ובכללן `before.json`, `after.json`, `comparison.json`, בדיקות Unity לפני ואחרי, ודוחות בדיקת האיזון לפני ואחרי. תיקיית Logs אינה נשמרת ב־Git.

## פירוט ההעברות

- `Assets/Scripts/Editor/CameraFollowValidation.cs` → `Assets/Editor/Validation/Camera/CameraFollowValidation.cs`
- `Assets/Scripts/Editor/CasualSeaValidation.cs` → `Assets/Editor/Validation/World/CasualSeaValidation.cs`
- `Assets/Scripts/Editor/ChapterOne/ChapterOneContinentBaker.cs` → `Assets/Editor/ChapterOne/ChapterOneContinentBaker.cs`
- `Assets/Scripts/Editor/ChapterOne/ChapterOneTileAtlasImporter.cs` → `Assets/Editor/ChapterOne/ChapterOneTileAtlasImporter.cs`
- `Assets/Scripts/Editor/ChapterRespawnPlayValidation.cs` → `Assets/Editor/Validation/World/ChapterRespawnPlayValidation.cs`
- `Assets/Scripts/Editor/Combat/RangedWeaponEditor.cs` → `Assets/Editor/Combat/RangedWeaponEditor.cs`
- `Assets/Scripts/Editor/Combat/WeaponDefinitionEditor.cs` → `Assets/Editor/Combat/WeaponDefinitionEditor.cs`
- `Assets/Scripts/Editor/Creatures/AnimatorGraphRepair.cs` → `Assets/Editor/Maintenance/AnimatorGraphRepair.cs`
- `Assets/Scripts/Editor/Creatures/BalancePlayValidation.cs` → `Assets/Editor/Validation/Creatures/BalancePlayValidation.cs`
- `Assets/Scripts/Editor/Creatures/CreatureAssetUtil.cs` → `Assets/Editor/Creatures/CreatureAssetUtil.cs`
- `Assets/Scripts/Editor/Creatures/CreatureSpawnerEditor.cs` → `Assets/Editor/Creatures/CreatureSpawnerEditor.cs`
- `Assets/Scripts/Editor/Creatures/CreatureStatsEditor.cs` → `Assets/Editor/Creatures/CreatureStatsEditor.cs`
- `Assets/Scripts/Editor/Creatures/KingLeashIndicatorValidation.cs` → `Assets/Editor/Validation/Creatures/KingLeashIndicatorValidation.cs`
- `Assets/Scripts/Editor/Creatures/KingLeashValidation.cs` → `Assets/Editor/Validation/Creatures/KingLeashValidation.cs`
- `Assets/Scripts/Editor/Creatures/KingObjectiveMarkerAuthoring.cs` → `Assets/Editor/Creatures/KingObjectiveMarkerAuthoring.cs`
- `Assets/Scripts/Editor/Creatures/KingObjectiveMarkerValidation.cs` → `Assets/Editor/Validation/Creatures/KingObjectiveMarkerValidation.cs`
- `Assets/Scripts/Editor/HUD/CasualHudKit.cs` → `Assets/Editor/HUD/CasualHudKit.cs`
- `Assets/Scripts/Editor/HUD/DragToMoveTutorialAuthoring.cs` → `Assets/Editor/HUD/DragToMoveTutorialAuthoring.cs`
- `Assets/Scripts/Editor/HUD/DragToMoveTutorialValidation.cs` → `Assets/Editor/Validation/HUD/DragToMoveTutorialValidation.cs`
- `Assets/Scripts/Editor/HUD/HudScenePresence.cs` → `Assets/Editor/HUD/HudScenePresence.cs`
- `Assets/Scripts/Editor/HUD/MiniMapPrefabAuthoring.cs` → `Assets/Editor/HUD/MiniMapPrefabAuthoring.cs`
- `Assets/Scripts/Editor/HUD/MiniMapValidation.cs` → `Assets/Editor/Validation/HUD/MiniMapValidation.cs`
- `Assets/Scripts/Editor/KingCinematicPlayValidation.cs` → `Assets/Editor/Validation/Camera/KingCinematicPlayValidation.cs`
- `Assets/Scripts/Editor/Maintenance/CasualShaderForceImport.cs` → `Assets/Editor/Maintenance/CasualShaderForceImport.cs`
- `Assets/Scripts/Editor/Maintenance/CleanMissingPrefabs.cs` → `Assets/Editor/Maintenance/CleanMissingPrefabs.cs`
- `Assets/Scripts/Editor/MobileHapticsValidation.cs` → `Assets/Editor/Validation/Player/MobileHapticsValidation.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetAreaSynchronization.cs` → `Assets/Editor/Planet/Authoring/PlanetAreaSynchronization.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetAuthoringEditor.cs` → `Assets/Editor/Planet/Authoring/PlanetAuthoringEditor.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetAuthoringPreview.cs` → `Assets/Editor/Planet/Authoring/PlanetAuthoringPreview.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetAuthoringPreview.shader` → `Assets/Editor/Planet/Authoring/PlanetAuthoringPreview.shader`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetAuthoringTools.cs` → `Assets/Editor/Planet/Authoring/PlanetAuthoringTools.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetAuthoringValidation.cs` → `Assets/Editor/Validation/Planet/PlanetAuthoringValidation.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetPlacementDuplication.cs` → `Assets/Editor/Planet/Authoring/PlanetPlacementDuplication.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetPlacementDuplicationValidation.cs` → `Assets/Editor/Validation/Planet/PlanetPlacementDuplicationValidation.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetPlayValidation.cs` → `Assets/Editor/Validation/Planet/PlanetPlayValidation.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetReferenceTerrain.cs` → `Assets/Editor/Planet/Authoring/PlanetReferenceTerrain.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetStageFogPlayValidation.cs` → `Assets/Editor/Validation/Planet/PlanetStageFogPlayValidation.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetStageFogValidation.cs` → `Assets/Editor/Validation/Planet/PlanetStageFogValidation.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetStageSessionValidation.cs` → `Assets/Editor/Validation/Planet/PlanetStageSessionValidation.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetStageValidation.cs` → `Assets/Editor/Validation/Planet/PlanetStageValidation.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetStartupValidation.cs` → `Assets/Editor/Validation/Planet/PlanetStartupValidation.cs`
- `Assets/Scripts/Editor/Planet/Authoring/PlanetTerrainPresets.cs` → `Assets/Editor/Planet/Authoring/PlanetTerrainPresets.cs`
- `Assets/Scripts/Editor/Planet/PlanetEnvironmentBrushEditor.cs` → `Assets/Editor/Planet/PlanetEnvironmentBrushEditor.cs`
- `Assets/Scripts/Editor/Planet/PlanetTileMapEditor.cs` → `Assets/Editor/Planet/PlanetTileMapEditor.cs`
- `Assets/Scripts/Editor/Planet/PlanetTileMapPresetEditor.cs` → `Assets/Editor/Planet/PlanetTileMapPresetEditor.cs`
- `Assets/Scripts/Editor/Player/PlayerModelSwapTool.cs` → `Assets/Editor/Player/PlayerModelSwapTool.cs`
- `Assets/Scripts/Editor/Player/PlayerStatsEditor.cs` → `Assets/Editor/Player/PlayerStatsEditor.cs`
- `Assets/Scripts/Editor/Ship/ShipHudArrange.cs` → `Assets/Editor/Ship/ShipHudArrange.cs`
- `Assets/Scripts/Editor/UI/SelectableReloadGuard.cs` → `Assets/Editor/UI/SelectableReloadGuard.cs`
- `Assets/Editor/ItchTestBuild.cs` → `Assets/Editor/Build/ItchTestBuild.cs`
- `Assets/Editor/MobileReleaseTools.cs` → `Assets/Editor/Build/MobileReleaseTools.cs`
- `Assets/Scripts/UI/HudChrome.cs` → `Assets/Scripts/UI/HUD/HudChrome.cs`
- `Assets/Scripts/UI/CoinHudCounter.cs` → `Assets/Scripts/UI/HUD/CoinHudCounter.cs`
- `Assets/Scripts/UI/OxygenHudBar.cs` → `Assets/Scripts/UI/HUD/OxygenHudBar.cs`
- `Assets/Scripts/UI/XpHudBar.cs` → `Assets/Scripts/UI/HUD/XpHudBar.cs`
- `Assets/Scripts/UI/LevelUpStatsBanner.cs` → `Assets/Scripts/UI/HUD/LevelUpStatsBanner.cs`
- `Assets/Scripts/UI/CargoCampDeposit.cs` → `Assets/Scripts/UI/Cargo/CargoCampDeposit.cs`
- `Assets/Scripts/UI/CargoChip.cs` → `Assets/Scripts/UI/Cargo/CargoChip.cs`
- `Assets/Scripts/UI/CargoFullCallout.cs` → `Assets/Scripts/UI/Cargo/CargoFullCallout.cs`
- `Assets/Scripts/UI/CargoMeter.cs` → `Assets/Scripts/UI/Cargo/CargoMeter.cs`
- `Assets/Scripts/UI/EquipmentSlotView.cs` → `Assets/Scripts/UI/Inventory/EquipmentSlotView.cs`
- `Assets/Scripts/UI/InventoryGridSlotView.cs` → `Assets/Scripts/UI/Inventory/InventoryGridSlotView.cs`
- `Assets/Scripts/UI/InventoryUI.cs` → `Assets/Scripts/UI/Inventory/InventoryUI.cs`
- `Assets/Scripts/UI/DynamicFloatingJoystick.cs` → `Assets/Scripts/UI/Input/DynamicFloatingJoystick.cs`
- `Assets/Scripts/UI/MobileInputBinder.cs` → `Assets/Scripts/UI/Input/MobileInputBinder.cs`
- `Assets/Scripts/UI/UICanvasControllerInput.cs` → `Assets/Scripts/UI/Input/UICanvasControllerInput.cs`
- `Assets/Scripts/UI/UIVirtualJoystick.cs` → `Assets/Scripts/UI/Input/UIVirtualJoystick.cs`
- `Assets/Scripts/UI/HomeArrowNavigation.cs` → `Assets/Scripts/UI/Navigation/HomeArrowNavigation.cs`
- `Assets/Scripts/UI/HomeNavigationRing.cs` → `Assets/Scripts/UI/Navigation/HomeNavigationRing.cs`
- `Assets/Scripts/UI/MiniMap.cs` → `Assets/Scripts/UI/Navigation/MiniMap.cs`
- `Assets/Scripts/UI/MiniMapRaster.cs` → `Assets/Scripts/UI/Navigation/MiniMapRaster.cs`
- `Assets/Scripts/UI/HologramBeam.cs` → `Assets/Scripts/UI/Hologram/HologramBeam.cs`
- `Assets/Scripts/UI/PlanetHologram.cs` → `Assets/Scripts/UI/Hologram/PlanetHologram.cs`
- `Assets/Scripts/UI/CameraOverlayQuad.cs` → `Assets/Scripts/UI/Feedback/CameraOverlayQuad.cs`
- `Assets/Scripts/UI/PlayerDamageOverlay.cs` → `Assets/Scripts/UI/Feedback/PlayerDamageOverlay.cs`
- `Assets/Scripts/UI/PlayerDeathCameraLock.cs` → `Assets/Scripts/UI/Feedback/PlayerDeathCameraLock.cs`
- `Assets/Scripts/UI/PlayerDeathUI.cs` → `Assets/Scripts/UI/Feedback/PlayerDeathUI.cs`
- `Assets/Scripts/UI/PlayerLowHpWarning.cs` → `Assets/Scripts/UI/Feedback/PlayerLowHpWarning.cs`
- `Assets/Scripts/UI/PlayerLowOxygenWarning.cs` → `Assets/Scripts/UI/Feedback/PlayerLowOxygenWarning.cs`
- `Assets/Scripts/UI/DragToMoveTutorial.cs` → `Assets/Scripts/UI/Tutorial/DragToMoveTutorial.cs`

### כלי עזר

- `tools/balance-audit.mjs` → `tools/validation/balance-audit.mjs`
- `tools/test-mobile-haptics.cjs` → `tools/validation/test-mobile-haptics.cjs`
- `tools/crop_screenshot.ps1` → `tools/capture/crop_screenshot.ps1`
- `tools/focus_unity.ps1` → `tools/capture/focus_unity.ps1`
- `tools/screenshot_unity.ps1` → `tools/capture/screenshot_unity.ps1`
