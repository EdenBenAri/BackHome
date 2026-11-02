mergeInto(LibraryManager.library, {
    $BackHomeHaptics: {
        ready: false,
        supported: false,
        // Tiny single pulses; no timer, waveform allocation, or deferred playback.
        durations: [12, 8, 22],
        cancel: function () {
            if (!BackHomeHaptics.supported) return;
            try { navigator.vibrate(0); } catch (error) { /* Optional hardware. */ }
        }
    },

    BackHomeHapticsInitialize__deps: ['$BackHomeHaptics'],
    BackHomeHapticsInitialize: function () {
        if (!BackHomeHaptics.ready) {
            BackHomeHaptics.ready = true;
            var mobile = (navigator.userAgentData && navigator.userAgentData.mobile) ||
                /Android|iPhone|iPad|iPod/i.test(navigator.userAgent || '');
            BackHomeHaptics.supported = !!mobile && typeof navigator.vibrate === 'function';
            if (BackHomeHaptics.supported) {
                document.addEventListener('visibilitychange', function () {
                    if (document.hidden) BackHomeHaptics.cancel();
                });
                window.addEventListener('pagehide', BackHomeHaptics.cancel);
                window.addEventListener('blur', BackHomeHaptics.cancel);
            }
        }
        return BackHomeHaptics.supported ? 1 : 0;
    },

    BackHomeHapticsPlay__deps: ['$BackHomeHaptics'],
    BackHomeHapticsPlay: function (cue) {
        if (!BackHomeHaptics.supported || document.hidden || !document.hasFocus()) return;
        if (navigator.userActivation && !navigator.userActivation.hasBeenActive) return;
        var duration = BackHomeHaptics.durations[cue];
        if (!duration) return;
        try {
            // A browser/embed may reject vibration. Never queue it for a later gesture.
            navigator.vibrate(duration);
        } catch (error) { /* Unsupported hardware or embedding policy: stay silent. */ }
    },

    BackHomeHapticsCancel__deps: ['$BackHomeHaptics'],
    BackHomeHapticsCancel: function () { BackHomeHaptics.cancel(); }
});
