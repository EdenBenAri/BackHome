# ChapterOne combat and progression balance

Implemented and researched on 2026-11-03. This is a balance pass for BackHome's current three-weapon, 30-level chapter. The percentages and encounter budgets below are BackHome design decisions informed by XP Hero; they are not presented as XP Hero's official formulas.

## Research and confidence

The [XP Hero progression wiki](https://xp-hero.fandom.com/wiki/Progression_Guide) describes progression as a combination of account level, unlocked systems, weapons, hero stars and traits. Equal account levels do not imply equal power. It also acknowledges missing data. The [upgrades wiki](https://xp-hero.fandom.com/wiki/Upgrades) separates Ability, Special, Extra and Soul upgrades, with POWER and HP as distinct upgrade lines. Its v25.1.0 cost data explicitly comes from a modified client and is provisional. Those costs were not used as verified stock-game balance.

The unofficial [XP Hero Fans enemy database](https://www.xphero.fans/stats/index.html), labelled v27.0.0, exposes useful relative budgets: stage-one Slime has 16 HP / 1 ATK, Spider 57 / 4, Slime King 63 / 6, and Rootling 256 / 18. Thus Slime King has about four times Slime's HP, while the stage boss has about 4.5 times the strongest listed stage-one ordinary enemy's HP. Later stages rise sharply: stage-two Cactus is 128 / 3 and Scarlet is 1,992 / 142. These are extracted fan data, not an official developer specification or a guarantee about the current live version.

The fan [hero data](https://www.xphero.fans/hero/index.html) contains 61 anchor rows through level 1,500 and interpolates between them. Examples are level 1: 2 attack / 50 HP; level 8: 5 / 80; level 21: 37 / 2,171; level 30: 106 / 8,356. The site cautions that interpolation may differ from actual game values. Its power index is a reconstruction. This long progression curve is unsuitable for directly copying into our 30-level chapter.

The fan [weapon database](https://www.xphero.fans/weapons/index.html) adds quality multipliers and weapon-specific growth. Its [damage calculator](https://www.xphero.fans/calc/index.html) explicitly says the exact combat formula is unpublished and its estimate omits some modifiers. The [starting guide](https://www.xphero.fans/guides/getting-started/index.html) links quests, weapon-slot unlocks, upgrades and world-boss gates. Together these explain why reducing the problem to account level alone would miss much of XP Hero's progression.

Two YouTube playthroughs were visually sampled; neither was watched in full or used to claim frame-accurate kill times:

- [Android Gameplay KL, around 1:00](https://www.youtube.com/watch?v=b4k_pYQxo1w&t=60s): level 1 combat and a displayed damage number of 6. [2:03](https://www.youtube.com/watch?v=b4k_pYQxo1w&t=123s): level 3 and weapon creation. [4:06](https://www.youtube.com/watch?v=b4k_pYQxo1w&t=246s): level 5, groups of spiders and repeated 16-damage hits. [5:40–6:10](https://www.youtube.com/watch?v=b4k_pYQxo1w&t=340s): level 8, multiple weapons, the Rootling defeat/countdown and the cleared first stage. This supports frequent early progression and a boss-gated transition; it does not isolate the source of every damage modifier.
- [Ranroid, around 5:56](https://www.youtube.com/watch?v=XR2MUVGoJHc&t=356s): the camera reveals the desert after the first region. [10:25](https://www.youtube.com/watch?v=XR2MUVGoJHc&t=625s): camp progression displays attack 79 and an increment of 6. That visible increment is not evidence for our chosen percentage formula. Our compounded growth follows the requested design and our own combat budget.

The transferable pattern is short ordinary encounters, distinct boss milestones, meaningful equipment roles and stronger later regions. Absolute numbers differ between sources, versions and loadouts. No reliable public source inspected established a complete clean-client damage formula, an oxygen budget for our game, or an ideal boss duration for this map.

## What was wrong locally

The player started with 900 base damage, added independently to each of three weapons, plus 3 base attacks per second. Kings had very large authored health budgets (1,200 / 8,500 / 12,500) yet reused the same prefab levels across later regions. Flat +3 player attack per level barely mattered beside 900 base attack. The 900 oxygen tank lasted 225 seconds, and fixed 5 HP/second suffocation became increasingly forgiving as maximum health rose.

The new pass budgets the full loadout and encounter levels together. Enemy assets contain level-one budgets. Spawns select their threat level without modifying shared assets. Respawns retain that area's level. Old regions remain easier as the player levels up; enemies do not automatically follow the player's current level.

## Player, weapons and oxygen

- Base attack: **900 → 8**. Base attack speed: **3 → 0.5 attacks/second**. Starting HP remains **80**.
- Every account level compounds complete weapon-hit damage by **1.10** and maximum HP by **1.08**, with no rounding between levels. The HUD displays **+10% / +8%**. Weapon contributions are included once before multiplying.
- Itchy contributes 2 damage and 3.5 attacks/second: **10 damage at 4 shots/second** at level 1. Poison activates after 3 hits, lasts 4 seconds and uses 25% of the triggering hit as damage/second. Refreshing poison now preserves its next tick; previously rapid fire could postpone damage indefinitely.
- Frosty contributes 6 damage and 1.5 attacks/second: **14 damage at 2 shots/second**. Its chain hits up to 3 additional targets at 65% of the initial hit each. This keeps a crowd role without four additional full-strength hits.
- Stinger contributes 20 damage and no additional speed: **28 damage at 0.5 attacks/second**, with a 1.5 blast multiplier (**42 blast damage** at level 1). Its existing charge, recovery and delayed explosion still limit actual output.
- Oxygen: **900 → 360**, drained at 4/second: **90 seconds** outside recovery zones. At zero oxygen, health drains at 6.25% of maximum per second, giving at most **16 seconds** from full HP regardless of level. Existing camp and level-up refills remain. This is an expedition budget; level-up refills can extend a progressing player's time away from camp.

Movement speed, reach, attack radius and oxygen capacity do not increase automatically with account level. Level cap remains 30.

## Enemy budgets and route

Level-one budgets are written as HP / damage per hit / attacks per second / XP:

- Slime: **90 / 5 / 0.8 / 8**.
- Grimling: **110 / 8 / 0.9 / 10**.
- Snarlfang: **100 / 6 / 1.2 / 11**.
- Rime: **100 / 7 / 0.8 / 9**.
- Slime King: **650 / 14 / 0.7 / 45**.
- Grimling King: **700 / 16 / 0.65 / 55**.
- Snarlfang King: **850 / 18 / 0.6 / 65**.

Enemy HP multiplies by `1.10^(threatLevel-1)`; damage by `1.08^(threatLevel-1)`. Matching these to player damage and HP growth preserves equal-level encounter duration and danger. A player five levels stronger deals about 61% more damage while having about 47% more health. Contact attacks still happen immediately on entry and then at their interval; this pass does not add attack telegraphs.

Both the ordinary area and the corresponding king arena use the same mob level. Mob / king levels in route order are:

- Stage A: A1 **1 / 3**, A2 **3 / 5**, A3 **5 / 7**.
- Stage B: B1 **8 / 10**, B2 **10 / 12**, B3 **12 / 14**.
- Stage C: C1 **15 / 17**, C2 **17 / 19**, C3 **19 / 21**.
- Stage D: D1 **22 / 24**, D2 **25 / 27**. These are the currently configured final two encounters; no extra D3 encounter was invented.

Actual king HP consequently rises along the full route: approximately **787, 1,025, 1,506, 1,533, 1,997, 2,934, 2,987, 3,892, 5,718, 5,820, 8,343**. The large old base pools were replaced with these smaller, explicit encounter budgets.

## XP and pacing

XP required for the next level is `round(80 * 1.15^(playerLevel-1))`, with zero at the cap. A kill awards `round(baseReward * 1.14^(threatLevel-1))`; a zero reward stays zero. Rewards follow enemy level, so revisiting an early area does not inflate its XP with player level.

A one-clear budget using the scene's actual spawn counts, every mob and king killed once in route order, reaches the kings at account levels **2, 4, 6, 8, 11, 13, 15, 17, 19, 21, 24**, and finishes at **25** without respawn farming. This is a reproducible XP budget, not a predicted player route or elapsed playtime. Skipped mobs, repeat kills, target sharing and travel change the result. Levels 26–30 remain available for further farming.

## Verification and tuning

Run `node tools/validation/balance-audit.mjs` from the project root. It reads the actual stats, weapon prefabs, level constants and scene rather than a separate hand-maintained table. It checks 33 area/enemy combinations, 11 ordered kings, all 30 levels, encounter HP/XP ordering, underlevel/overlevel behavior, oxygen and the one-clear XP budget. Detailed output is generated at `Logs/BalanceAudit.json`.

The damage simulation assumes one stationary target, all three weapons in range, 60 Hz cooldowns and 0.15-second Itchy projectile flight. At two player levels below the king, estimates are 8.65 / 9.50 / 11.50 seconds for the three king types. These are estimates, not measured playthrough times.

With Galaxy open and outside Play, choose **BackHome > Balance > Validate In Play**. This uses an isolated Play session, preserves persisted stage progression, checks actual XP level-up and percentage labels, all 11 kings and 136 spawned mobs across unlocks, respawn levels, asset isolation, oxygen scaling and continuous-fire poison. It then uses the real weapon components against isolated stationary kings. Measured in the Unity Editor:

- Slime King level 3 versus player level 1: **8.78 seconds**.
- Snarlfang King level 7 versus player level 5: **11.58 seconds**.
- Grimling King level 27 versus player level 25: **9.69 seconds**.

All runtime checks passed, and the isolated startup reported no errors. Results are written to `Logs/BalancePlayValidation.txt` and `Logs/PlanetStartupValidation.txt`. The test temporarily removes cinematics and other targets from its combat measurement; it does not alter their production behavior or save the scene.

These checks establish consistent arithmetic and actual stationary damage output. A full human chapter run is still needed to judge movement, crowd pressure, route readability, oxygen return trips and enjoyment. Boss duration in a group can be longer because weapons share targets, or shorter because the player levels up during the encounter. The intended stationary budget is roughly 8–12 seconds, with room for real movement and interruptions.

Tune global growth in `Assets/Scripts/Progression/LevelTable.cs`, baseline player stats in `Assets/Resources/Player/PlayerStats.asset`, weapon roles in `Assets/Resources/Weapons`, species budgets in `Assets/Resources/Galaxy/ChapterOne/Creatures`, and per-area **Creature Level / King Level** on Galaxy's CreatureSpawner. A value of zero uses the species asset's default level for compatibility with standalone spawns.
