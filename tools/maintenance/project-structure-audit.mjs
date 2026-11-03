// Run from the project root: node tools/maintenance/project-structure-audit.mjs snapshot|compare
// Reports go to ignored Logs/ProjectOrganization; this never changes Unity assets.
import fs from 'node:fs';
import crypto from 'node:crypto';

const output = 'Logs/ProjectOrganization';
const baselinePath = `${output}/before.json`;
const walk = dir => fs.existsSync(dir) ? fs.readdirSync(dir, { withFileTypes: true })
    .filter(entry => !entry.name.startsWith('.'))
    .flatMap(entry => entry.isDirectory() ? walk(`${dir}/${entry.name}`) : [`${dir}/${entry.name}`]) : [];
const hash = data => crypto.createHash('sha256').update(data).digest('hex');
const files = [...walk('Assets'), ...walk('Packages'), ...walk('ProjectSettings')].sort();
const records = {};
const guidPaths = {};
const duplicateGuids = [];
const references = [];
for (const file of files) {
    const data = fs.readFileSync(file);
    records[file] = hash(data);
    if (file.endsWith('.meta')) {
        const guid = data.toString().match(/^guid: ([a-f0-9]{32})\s*$/m)?.[1];
        if (guid) {
            if (guidPaths[guid]) duplicateGuids.push(`${guid}: ${guidPaths[guid]}, ${file.slice(0, -5)}`);
            guidPaths[guid] = file.slice(0, -5);
        }
    }
    if (!data.subarray(0, 8192).includes(0)) {
        for (const match of data.toString().matchAll(/guid:\s*([a-f0-9]{32})/g)) {
            if (!/^0{16}/.test(match[1])) references.push([file, match[1]]);
        }
    }
}
const packageGuids = new Set();
for (const file of walk('Library/PackageCache').filter(file => file.endsWith('.meta'))) {
    const guid = fs.readFileSync(file, 'utf8').match(/^guid: ([a-f0-9]{32})\s*$/m)?.[1];
    if (guid) packageGuids.add(guid);
}
const missingMeta = files.filter(file => file.startsWith('Assets/') && !file.endsWith('.meta') && !fs.existsSync(`${file}.meta`));
const orphanMeta = files.filter(file => file.endsWith('.meta') && !fs.existsSync(file.slice(0, -5)));
const unresolved = [...new Set(references.filter(([, guid]) => !guidPaths[guid] && !packageGuids.has(guid))
    .map(([file, guid]) => `${file}: ${guid}`))].sort();
const state = { records, guidPaths, duplicateGuids, missingMeta, orphanMeta, unresolved };
fs.mkdirSync(output, { recursive: true });
const mode = process.argv[2];
if (mode === 'snapshot') {
    if (fs.existsSync(baselinePath)) throw new Error(`Baseline already exists: ${baselinePath}. Archive it before starting a new audit.`);
    fs.writeFileSync(baselinePath, JSON.stringify(state, null, 2));
    console.log(JSON.stringify({ files: files.length, guids: Object.keys(guidPaths).length, duplicateGuids, missingMeta, orphanMeta, unresolvedCount: unresolved.length }, null, 2));
} else if (mode === 'compare') {
    const before = JSON.parse(fs.readFileSync(baselinePath, 'utf8'));
    const moves = Object.entries(before.guidPaths)
        .filter(([guid, oldPath]) => guidPaths[guid] && guidPaths[guid] !== oldPath)
        .map(([guid, from]) => ({ guid, from, to: guidPaths[guid] }));
    const movedPaths = new Map(moves.flatMap(({ from, to }) => [[from, to], [`${from}.meta`, `${to}.meta`]]));
    const translate = file => movedPaths.get(file) ?? file;
    const deletedGuids = Object.entries(before.guidPaths).filter(([guid]) => !guidPaths[guid]);
    const newUnresolved = unresolved.filter(issue => !before.unresolved.some(old => {
        const split = old.lastIndexOf(': ');
        return `${translate(old.slice(0, split))}${old.slice(split)}` === issue;
    }));
    const assetStoreChanges = [...new Set([...Object.keys(before.records), ...files])]
        .filter(file => (file === 'Assets/AssetStore.meta' || file.startsWith('Assets/AssetStore/')) && before.records[file] !== records[file]);
    const contentChanges = Object.entries(before.records)
        .filter(([file, digest]) => records[translate(file)] && records[translate(file)] !== digest)
        .map(([file]) => translate(file));
    const removedFiles = Object.keys(before.records).filter(file => !records[translate(file)]);
    const resourcesChanges = Object.entries(before.records).filter(([file, digest]) => file.includes('/Resources/') && records[file] !== digest).map(([file]) => file);
    const danglingDeletedGuids = deletedGuids.filter(([guid]) => references.some(([, ref]) => ref === guid));
    const report = { moves, deletedGuids, removedFiles, contentChanges, assetStoreChanges, resourcesChanges, danglingDeletedGuids, duplicateGuids, missingMeta, orphanMeta, newUnresolved, preExistingUnresolvedCount: before.unresolved.length };
    fs.writeFileSync(`${output}/after.json`, JSON.stringify(state, null, 2));
    fs.writeFileSync(`${output}/comparison.json`, JSON.stringify(report, null, 2));
    const failures = { assetStoreChanges, danglingDeletedGuids, newUnresolved,
        duplicateGuids: duplicateGuids.filter(value => !before.duplicateGuids.includes(value)),
        missingMeta: missingMeta.filter(value => !before.missingMeta.includes(value)),
        orphanMeta: orphanMeta.filter(value => !before.orphanMeta.includes(value)) };
    console.log(JSON.stringify({ movedAssets: moves.length, removedFiles, contentChanges, resourcesChanges, failures, preExistingUnresolvedCount: before.unresolved.length }, null, 2));
    if (Object.values(failures).some(values => values.length)) process.exitCode = 1;
} else {
    throw new Error('Expected snapshot or compare');
}
