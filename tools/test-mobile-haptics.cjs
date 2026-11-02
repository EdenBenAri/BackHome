// Run with: node tools/test-mobile-haptics.cjs
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../Assets/Plugins/WebGL/BackHomeHaptics.jslib'), 'utf8');

function browser(options = {}) {
    const calls = [], listeners = {};
    const context = {
        LibraryManager: { library: {} },
        mergeInto: Object.assign,
        navigator: {
            userAgent: options.userAgent ?? 'Mozilla/5.0 (Linux; Android 14) Chrome/130',
            userActivation: { hasBeenActive: options.active ?? true },
            vibrate: options.unsupported ? undefined : value => { calls.push(value); return true; }
        },
        document: {
            hidden: false,
            hasFocus: () => true,
            addEventListener: (name, fn) => { (listeners[name] ??= []).push(fn); }
        },
        window: { addEventListener: (name, fn) => { (listeners[name] ??= []).push(fn); } }
    };
    vm.runInNewContext(source, context);
    const api = context.LibraryManager.library;
    context.BackHomeHaptics = api.$BackHomeHaptics;
    return { ...context, calls, listeners, init: api.BackHomeHapticsInitialize,
        play: api.BackHomeHapticsPlay, cancel: api.BackHomeHapticsCancel };
}

let passed = 0;
function test(name, fn) { fn(); passed++; console.log('PASS ' + name); }
test('Android uses three brief, distinct single pulses', () => {
    const b = browser(); assert.equal(b.init(), 1);
    [0, 1, 2].forEach(b.play); assert.deepEqual(b.calls, [12, 8, 22]);
});
test('Desktop never vibrates, even if the API exists', () => {
    const b = browser({ userAgent: 'Mozilla/5.0 (Windows NT 10.0) Chrome/130' });
    assert.equal(b.init(), 0); b.play(0); b.cancel(); assert.deepEqual(b.calls, []);
});
test('Unsupported iPhone browser is a silent no-op', () => {
    const b = browser({ userAgent: 'iPhone Safari', unsupported: true });
    assert.equal(b.init(), 0); b.play(2); b.cancel(); assert.deepEqual(b.calls, []);
});
test('User-agent client hints can identify a mobile browser', () => {
    const b = browser({ userAgent: '' }); b.navigator.userAgentData = { mobile: true };
    assert.equal(b.init(), 1);
});
test('No vibration before interaction, no deferred backlog afterward', () => {
    const b = browser({ active: false }); b.init(); b.play(0); b.play(2);
    assert.deepEqual(b.calls, []); b.navigator.userActivation.hasBeenActive = true;
    assert.deepEqual(b.calls, []); b.play(1); assert.deepEqual(b.calls, [8]);
});
test('Hidden or unfocused tabs cannot play', () => {
    const b = browser(); b.init(); b.document.hidden = true; b.play(0);
    b.document.hidden = false; b.document.hasFocus = () => false; b.play(2);
    assert.deepEqual(b.calls, []);
});
test('Losing focus or leaving the page cancels hardware feedback', () => {
    const b = browser(); b.init(); b.document.hidden = true;
    b.listeners.visibilitychange[0](); b.listeners.pagehide[0](); b.listeners.blur[0]();
    assert.deepEqual(b.calls, [0, 0, 0]);
});
test('Initialization never duplicates lifecycle listeners', () => {
    const b = browser(); b.init(); b.init(); b.init();
    for (const handlers of Object.values(b.listeners)) assert.equal(handlers.length, 1);
});
test('Embedding policy rejection and exceptions do not escape into Unity', () => {
    const b = browser(); b.init(); b.navigator.vibrate = () => false; b.play(0);
    b.navigator.vibrate = () => { throw new Error('SecurityError'); };
    assert.doesNotThrow(() => { b.play(2); b.cancel(); });
});
test('Invalid cue IDs cannot create a vibration pattern', () => {
    const b = browser(); b.init(); [-1, 3, 200].forEach(b.play); assert.deepEqual(b.calls, []);
});
test('Older browsers without userActivation still delegate permission to the browser', () => {
    const b = browser(); delete b.navigator.userActivation; b.init(); b.play(0);
    assert.deepEqual(b.calls, [12]);
});
console.log(`PASS all ${passed} WebGL haptics checks`);
