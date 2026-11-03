# Mobile haptics

Feedback is deliberately sparse: a light shot pulse at `RangedWeapon.FireNow` (after charging and target validation), a soft tick as a nonempty cargo deposit launches, and one success cue after its final visible icon is banked. The direct-damage combat fallback also emits a shot cue. Secondary chain hits, poison ticks, individual coins, and canceled charges produce no extra feedback.

All weapons share a 200 ms shot interval. Softer cues share a 160 ms interval. Deposit completion can replace a softer cue, has a 750 ms repeat guard, and reserves 300 ms of quiet afterward. Missing HUD/icons bank immediately with just the success cue. Interrupted flyers still bank exactly as before, but cannot signal successful visible completion. A newer deposit owns feedback; older flyers continue banking.

`MobileHaptics.Enabled` persists an opt-out in `BackHome.Haptics.Enabled`; it can be bound to a future settings toggle. There is currently no settings screen. Paused/unfocused/background gameplay does not start feedback. No haptics Update loop, coroutine, scene search, or per-shot GameObject is added. The value-type rate limiter and cached JNI argument buffer avoid per-shot managed allocations; operating-system internals are outside this guarantee.

## Platforms

- **WebGL / current itch build:** 12 ms shot, 8 ms cargo start, 22 ms completion via `navigator.vibrate`. Feature and mobile-device detection; requires browser user activation, visible page and focus. No timers or deferred playback. Android Chromium browsers can support this; iOS Safari does not support the Vibration API. Embedding policies and device settings can deny it silently. A new build/upload is required to update an existing hosted game.
- **Android app:** reusable UI-thread callbacks call `View.performHapticFeedback` with `KEYBOARD_TAP`, `CLOCK_TICK`, and `CONFIRM` (API 30+, otherwise `VIRTUAL_KEY`). Honors system/view haptic preferences; no vibration permission. Pending work is bounded and drops after 100 ms. If enabling Java minification in a future release, keep `com.backhome.haptics.BackHomeHaptics` and its public static methods, which are called through JNI.
- **iOS app:** cached UIKit light-impact, selection, and success generators. Unsupported hardware is silent. This is separate from Safari/WebGL. Requires an iOS build and device validation.
- **Editor/desktop:** no hardware calls.

## Rationale and primary sources

Research checked 2026-11-02. Platform feedback is preferred over `Handheld.Vibrate`, whose duration is controlled by the operating system. Frequent generic buzzes would not give the requested subtle feel.

- [Android haptics design principles](https://developer.android.com/develop/ui/views/haptics/haptics-principles): use restraint and synchronize feedback with visible events.
- [Android event haptics](https://developer.android.com/develop/ui/views/haptics/haptic-feedback): device-tuned View feedback, system preferences, and no VIBRATE permission.
- [Unity Handheld.Vibrate](https://docs.unity.com/en-us/engine/6000.7/script-reference/unityengine/handheld/vibrate): operating-system-controlled duration.
- [Apple UIFeedbackGenerator](https://developer.apple.com/documentation/uikit/uifeedbackgenerator): semantic feedback generators.
- [W3C Vibration API implementation report](https://w3c.github.io/vibration/reports/implementation.html): Chromium support and lack of Safari support.
- [MDN Navigator.vibrate](https://developer.mozilla.org/en-US/docs/Web/API/Navigator/vibrate): activation, cancellation and unsupported devices.

## Verification

Run `node tools/validation/test-mobile-haptics.cjs` for browser capability, activation, background, cancellation, and rejection checks. In Unity, run **BackHome > Feedback > Validate Mobile Haptics**, or create `Temp/BackHomeHapticsValidation.trigger` outside Play Mode; results go to `Logs/MobileHapticsValidation.txt`. The checks cover rate limiting, completion priority, zero managed allocation in the limiter, deposit batch bookkeeping, and plugin platform isolation.

Before shipping, feel-test on physical Android and iPhone app builds: sustained multiweapon combat, canceled charge, a one-item/full cargo deposit, a scene change during flight, pause/background/resume, and disabled device haptics. Verify the itch embed on Android separately; browser policy can differ from a top-level page. Measure target-device CPU/GC and tune pulse cadence if necessary. Desktop tests cannot validate motor strength, native build/linking, battery impact, or device frame time.
