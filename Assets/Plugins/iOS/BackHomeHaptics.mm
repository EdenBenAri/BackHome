#import <UIKit/UIKit.h>

static UIImpactFeedbackGenerator *attackFeedback;
static UISelectionFeedbackGenerator *cargoFeedback;
static UINotificationFeedbackGenerator *completeFeedback;

extern "C" int BackHomeHapticsInitialize()
{
    if (![NSThread isMainThread]) return 0;
    if (@available(iOS 10.0, *)) {
        // Retain and reuse generators. Unsupported devices simply produce no feedback.
        if (!attackFeedback) attackFeedback = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
        if (!cargoFeedback) cargoFeedback = [[UISelectionFeedbackGenerator alloc] init];
        if (!completeFeedback) completeFeedback = [[UINotificationFeedbackGenerator alloc] init];
        return 1;
    }
    return 0;
}

extern "C" void BackHomeHapticsPlay(int cue)
{
    // Unity calls on the main thread. Never queue stale feedback from another thread.
    if (![NSThread isMainThread] || UIApplication.sharedApplication.applicationState != UIApplicationStateActive) return;
    if (@available(iOS 10.0, *)) {
        switch (cue) {
            case 0:
                if (@available(iOS 13.0, *)) [attackFeedback impactOccurredWithIntensity:0.45];
                else [attackFeedback impactOccurred];
                [attackFeedback prepare];
                break;
            case 1:
                [cargoFeedback selectionChanged];
                [completeFeedback prepare];
                break;
            case 2:
                [completeFeedback notificationOccurred:UINotificationFeedbackTypeSuccess];
                break;
        }
    }
}

extern "C" void BackHomeHapticsCancel()
{
    // UIKit feedback is transient; there is no repeating pattern or pending work to cancel.
}
