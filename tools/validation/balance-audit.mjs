// Dependency-free audit of the actual Unity assets. Run: node tools/validation/balance-audit.mjs
// Single stationary target, 60 Hz, all weapons in range, 0.15 s bullet flight.
// This is a damage-budget simulation, not a measurement of travel/kiting/target sharing.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';

const read = p => fs.readFileSync(p, 'utf8');
const num = (s, key) => {
  const match = s.match(new RegExp('^\\s*' + key + ': ([\\d.-]+)\\s*$', 'm'));
  assert(match, `Missing ${key}`); return Number(match[1]);
};
const walk = p => fs.readdirSync(p, { withFileTypes: true }).flatMap(e =>
  e.isDirectory() ? walk(path.join(p, e.name)) : [path.join(p, e.name)]);
const files = walk('Assets/Resources');
const guidPaths = new Map(files.filter(p => p.endsWith('.meta')).map(p =>
  [read(p).match(/^guid: (\w+)/m)?.[1], p.slice(0, -5)]));
const constants = read('Assets/Scripts/Progression/LevelTable.cs');
const constant = key => Number(constants.match(new RegExp('const (?:float|int) ' + key + ' = ([\\d.]+)f?;'))[1]);
const cap = constant('MaxLevel');
const atk = l => (1 + constant('AttackGrowthPercent') / 100) ** (Math.min(cap, Math.max(1, l)) - 1);
const hp = l => (1 + constant('HealthGrowthPercent') / 100) ** (Math.min(cap, Math.max(1, l)) - 1);
const xp = l => l >= cap ? 0 : Math.round(constant('BaseXp') * constant('XpGrowth') ** (l - 1));
const reward = (base, l) => Math.round(base * constant('RewardGrowth') ** (l - 1));
const player = read('Assets/Resources/Player/PlayerStats.asset');
const vitals = read('Assets/Resources/Player/Player.prefab');
const weapons = [...player.matchAll(/- \{fileID: 11400000, guid: (\w+), type: 2\}/g)].map(m => {
  const asset = read(guidPaths.get(m[1]));
  const prefabGuid = asset.match(/  prefab: \{fileID: \d+, guid: (\w+)/)[1];
  const prefab = read(guidPaths.get(prefabGuid));
  return {
    name: asset.match(/  displayName: (.+)/)[1].trim(),
    damage: num(player, 'attackDamage') + num(asset, 'attackDamage'),
    speed: num(player, 'attackSpeed') + num(asset, 'attackSpeed'),
    effect: num(asset, 'hitEffect'), charge: num(prefab, 'chargeTime'), recover: num(prefab, 'recoverTime'),
    delay: num(prefab, 'delivery') === 1 ? 0.15 : num(asset, 'hitEffect') === 3 ? num(asset, 'damageDelay') : 0,
    blast: num(asset, 'damageMultiplier'), dotDps: num(asset, 'damagePerSecond'),
    dotHits: num(asset, 'hitsToApply'), dotTick: num(asset, 'tickInterval'), dotDuration: num(asset, 'duration')
  };
});
assert.equal(weapons.length, 3, 'Audit expects the authored three-weapon loadout');

function ttk(health, playerLevel, kit = weapons) {
  let total = 0, buildup = 0, dotEnd = -1, dotNext = Infinity, dotDps = 0, dotTick = 0.5;
  const events = kit.map(w => ({ time: w.charge + w.delay, w }));
  while (events.length) {
    events.sort((a, b) => a.time - b.time);
    const event = events.shift();
    while (dotNext <= event.time && dotNext <= dotEnd) {
      total += dotDps * dotTick;
      if (total >= health) return dotNext;
      dotNext += dotTick;
    }
    if (event.time > 180) return Infinity;
    const w = event.w, damage = w.damage * atk(playerLevel);
    total += damage * (w.effect === 3 ? w.blast : 1);
    if (total >= health) return event.time;
    if (w.effect === 1 && ++buildup >= w.dotHits) {
      if (dotEnd < event.time) dotNext = event.time + w.dotTick;
      dotTick = w.dotTick;
      dotDps = w.dotDps > 0 ? w.dotDps : damage * 0.25;
      dotEnd = event.time + w.dotDuration;
    }
    // Both the cooldown and charge/recovery restrict fire; quantize to game frames.
    const cycle = Math.ceil(Math.max(1 / w.speed, w.charge + w.recover) * 60) / 60;
    events.push({ time: event.time + cycle, w });
  }
  throw new Error('No damage events');
}
function creatureFromPrefab(guid) {
  const prefab = read(guidPaths.get(guid));
  const statsGuid = prefab.match(/  stats: \{fileID: 11400000, guid: (\w+)/)?.[1];
  assert(statsGuid, `Missing stats on ${guidPaths.get(guid)}`);
  const asset = read(guidPaths.get(statsGuid));
  return { name: asset.match(/  displayName: (.+)/)[1].trim(),
    hp: num(asset, 'maxHealth'), damage: num(asset, 'attackDamage'), speed: num(asset, 'attackSpeed'), xp: num(asset, 'experience') };
}
const scene = read('Assets/Scenes/Galaxy.unity');
const blocks = scene.slice(scene.indexOf('  spawnPoints:')).split(/(?=^  - areaId:)/m).slice(1);
const encounters = [];
for (const block of blocks) {
  const area = block.match(/^  - areaId: (.+)/m)[1].trim();
  const [mobs, king] = block.split('    king:');
  for (const [kind, text, level] of [['mob', mobs, num(mobs, 'creatureLevel')], ['king', king, num(mobs, 'kingLevel')]]) {
    const prefab = text.match(/prefab: \{fileID: \d+, guid: (\w+)/);
    if (!prefab) continue;
    const c = creatureFromPrefab(prefab[1]);
    const effectiveHp = c.hp * atk(level), damage = c.damage * hp(level);
    const recommended = kind === 'king' ? level - 2 : level;
    const incoming = damage / (num(player, 'maxHealth') * hp(recommended));
    const row = { area, kind, type: c.name, level, count: kind === 'king' ? 1 : num(mobs, 'count'), playerLevel: recommended,
      health: effectiveHp, damage, xp: reward(c.xp, level),
      secondsAtRecommended: ttk(effectiveHp, recommended), secondsFiveLevelsBehind: ttk(effectiveHp, Math.max(1, recommended - 5)),
      secondsFiveLevelsAhead: ttk(effectiveHp, Math.min(cap, recommended + 5)),
      singleItchySeconds: ttk(effectiveHp, recommended, weapons.filter(w => w.name === 'Itchy')),
      damagePercentOfPlayerHealth: incoming * 100, killsForLevel: xp(recommended) / reward(c.xp, level) };
    assert(row.secondsFiveLevelsAhead <= row.secondsAtRecommended, `${area}: leveling made the player weaker`);
    assert(row.secondsFiveLevelsBehind >= row.secondsAtRecommended, `${area}: underlevelled player faster`);
    assert(incoming < (kind === 'king' ? 0.3 : 0.15), `${area}: excessive one-hit damage`);
    assert(row.secondsAtRecommended >= (kind === 'king' ? 6 : 0.5), `${area}: dies too quickly`);
    assert(row.secondsAtRecommended <= (kind === 'king' ? 13 : 2.5), `${area}: excessive HP budget`);
    encounters.push(row);
  }
}
assert.equal(encounters.filter(e => e.kind === 'king').length, 11);
assert.equal(encounters.filter(e => e.kind === 'mob').length, 22);
const kings = encounters.filter(e => e.kind === 'king');
// One clear of every authored pack, in route order, with no respawn farming.
// This is an XP budget, not a promise that every player kills every mob in order.
let routeLevel = 1, routeXp = 0;
const route = [];
for (const encounter of encounters) {
  if (encounter.kind === 'king') route.push({ area: encounter.area, playerLevelOnArrival: routeLevel,
    kingLevel: encounter.level, seconds: ttk(encounter.health, routeLevel) });
  routeXp += encounter.xp * encounter.count;
  while (routeLevel < cap && routeXp >= xp(routeLevel)) routeXp -= xp(routeLevel++);
}
assert(route.every(r => r.kingLevel - r.playerLevelOnArrival <= 3), `Single-clear route falls too far behind: ${JSON.stringify(route)}`);
for (let i = 1; i < kings.length; i++) {
  assert(kings[i].health > kings[i - 1].health, 'King HP must increase along the chapter route');
  assert(kings[i].xp > kings[i - 1].xp, 'King XP must increase along the chapter route');
}
for (let l = 2; l <= cap; l++) {
  assert(Math.abs(atk(l) / atk(l - 1) - 1.1) < 1e-10);
  assert(Math.abs(hp(l) / hp(l - 1) - 1.08) < 1e-10);
  assert(Math.abs(ttk(650 * atk(l), l) - ttk(650, 1)) < 0.02, 'Equal-level boss duration drifted');
}
const oxygenSeconds = num(player, 'oxygenTank') / num(vitals, 'oxygenDrainPerSecond');
const suffocationGraceSeconds = 100 / num(vitals, 'healthDrainPercentPerSecond');
assert(oxygenSeconds >= 75 && oxygenSeconds <= 100);
assert(suffocationGraceSeconds >= 12 && suffocationGraceSeconds <= 20);
const report = {
  assumptions: 'Damage-budget simulation: stationary isolated target, all three weapons in range, no dodge/travel/target sharing, 0.15s Itchy projectile flight, 60Hz cooldowns. Not a measured playthrough.',
  growth: { attackPercent: constant('AttackGrowthPercent'), healthPercent: constant('HealthGrowthPercent'), cap },
  oxygenSeconds, suffocationGraceSeconds, weapons,
  playerLevels: Array.from({ length: cap }, (_, i) => ({ level: i + 1, health: num(player, 'maxHealth') * hp(i + 1), baseAttack: num(player, 'attackDamage') * atk(i + 1), xpToNext: xp(i + 1) })),
  encounters, singleClearRoute: { assumptions: 'Every authored mob and king killed once in serialized route order; no respawns, travel or level-up midfight.', bosses: route, endingLevel: routeLevel, remainingXp: routeXp }
};
fs.mkdirSync('Logs', { recursive: true });
fs.writeFileSync('Logs/BalanceAudit.json', JSON.stringify(report, null, 2));
console.log(`PASS ${encounters.length} encounters, 11 ordered kings, ${cap} levels, compounded growth, XP and oxygen budgets.`);
console.log(`Oxygen ${oxygenSeconds}s; empty-tank grace ${suffocationGraceSeconds}s. Kings (area: HP / seconds at recommended level):`);
for (const e of kings) console.log(`${e.area}: ${Math.round(e.health)} HP / ${e.secondsAtRecommended.toFixed(2)}s @ player Lv${e.playerLevel} vs king Lv${e.level}`);
console.log(`One clear, no farming: ${route.map(r => `${r.area}=Lv${r.playerLevelOnArrival}`).join(', ')}; finishes Lv${routeLevel}.`);
