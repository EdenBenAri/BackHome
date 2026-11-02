package com.backhome.haptics;

import android.app.Activity;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.view.HapticFeedbackConstants;
import android.view.View;
import com.unity3d.player.UnityPlayer;

/** Device-tuned feedback, honoring Android's touch-feedback setting without VIBRATE permission. */
public final class BackHomeHaptics {
    private static final Handler MAIN = new Handler(Looper.getMainLooper());
    private static final Pulse[] PULSES = { new Pulse(0), new Pulse(1), new Pulse(2) };
    private static volatile boolean available = true;

    private BackHomeHaptics() {}

    public static void play(int cue) {
        if (!available || cue < 0 || cue >= PULSES.length) return;
        Pulse pulse = PULSES[cue];
        // At most one pending callback. A busy UI never accumulates a burst of late pulses.
        cancel();
        pulse.requestedAt = SystemClock.uptimeMillis();
        MAIN.post(pulse);
    }

    public static void cancel() {
        for (Pulse pulse : PULSES) MAIN.removeCallbacks(pulse);
    }

    private static final class Pulse implements Runnable {
        private final int cue;
        private volatile long requestedAt;

        Pulse(int cue) { this.cue = cue; }

        @Override public void run() {
            if (SystemClock.uptimeMillis() - requestedAt > 100) return;
            Activity activity = UnityPlayer.currentActivity;
            if (activity == null || activity.isFinishing() || activity.getWindow() == null) return;
            View view = activity.getWindow().getDecorView();
            if (!view.isShown() || !view.hasWindowFocus()) return;
            int effect = HapticFeedbackConstants.KEYBOARD_TAP;
            if (cue == 1) effect = HapticFeedbackConstants.CLOCK_TICK;
            if (cue == 2) effect = Build.VERSION.SDK_INT >= 30
                ? HapticFeedbackConstants.CONFIRM : HapticFeedbackConstants.VIRTUAL_KEY;
            // No IGNORE_GLOBAL_SETTING / IGNORE_VIEW_SETTING flags.
            try {
                view.performHapticFeedback(effect);
            } catch (RuntimeException error) {
                // A vendor haptics failure on the UI thread must not crash the game.
                available = false;
            }
        }
    }
}
