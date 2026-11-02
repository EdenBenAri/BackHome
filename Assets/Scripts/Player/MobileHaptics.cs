using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Small, event-driven phone feedback. No polling, coroutines, or per-shot objects.</summary>
public static class MobileHaptics
{
    const string PreferenceKey = "BackHome.Haptics.Enabled";
    static bool _enabled;
    static bool _preferenceLoaded;
    static bool _available;
    static bool _paused;
    static HapticRateLimiter _limiter;

    // Stable IDs shared with the native/browser bridges.
    public enum Cue { Attack, CargoStart, CargoComplete }

    /// <summary>Persistent opt-out, ready to bind to a settings toggle.</summary>
    public static bool Enabled
    {
        get
        {
            if (!_preferenceLoaded)
            {
                _enabled = PlayerPrefs.GetInt(PreferenceKey, 1) != 0;
                _preferenceLoaded = true;
            }
            return _enabled;
        }
        set
        {
            if (Enabled == value) return;
            _enabled = value;
            PlayerPrefs.SetInt(PreferenceKey, value ? 1 : 0);
            PlayerPrefs.Save();
            if (!value) Suspend();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        _preferenceLoaded = false;
        _available = false;
        _paused = false;
        _limiter = default;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Initialize()
    {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS || UNITY_WEBGL)
        try
        {
#if UNITY_ANDROID
            _android = new AndroidJavaClass("com.backhome.haptics.BackHomeHaptics");
            _playMethod = AndroidJNIHelper.GetMethodID(_android.GetRawClass(), "play", "(I)V", true);
            _available = true;
#else
            _available = BackHomeHapticsInitialize() != 0;
#endif
        }
        catch (Exception)
        {
            // Optional feedback must never interrupt gameplay, even if a bridge is unavailable.
            _available = false;
        }
        if (!_available) return;
        var driver = new GameObject("Mobile haptics");
        UnityEngine.Object.DontDestroyOnLoad(driver);
        driver.AddComponent<MobileHapticsLifecycle>();
#endif
    }

    public static void Attack() => Play(Cue.Attack);
    public static void CargoStart() => Play(Cue.CargoStart);
    public static void CargoComplete() => Play(Cue.CargoComplete);

    static void Play(Cue cue)
    {
        if (!_available || _paused || !Application.isFocused || Time.timeScale <= 0f || !Enabled)
            return;
        if (!_limiter.TryAccept(cue, Time.realtimeSinceStartupAsDouble))
            return;

        try
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Reuse the JNI argument buffer; CallStatic(params object[]) boxes/allocates each shot.
            AndroidArguments[0].i = (int)cue;
            AndroidJNI.CallStaticVoidMethod(_android.GetRawClass(), _playMethod, AndroidArguments);
            IntPtr exception = AndroidJNI.ExceptionOccurred();
            if (exception != IntPtr.Zero)
            {
                AndroidJNI.ExceptionClear();
                AndroidJNI.DeleteLocalRef(exception);
                _available = false;
            }
#elif (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR
            BackHomeHapticsPlay((int)cue);
#endif
        }
        catch (Exception)
        {
            _available = false; // Don't retry a failing native bridge on every attack.
        }
    }

    internal static void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused) Suspend();
    }

    internal static void Suspend()
    {
        if (!_available) return;
        try
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _android.CallStatic("cancel");
#elif (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR
            BackHomeHapticsCancel();
#endif
        }
        catch (Exception) { _available = false; }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    static AndroidJavaClass _android;
    static IntPtr _playMethod;
    static readonly jvalue[] AndroidArguments = new jvalue[1];
#elif (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR
    [DllImport("__Internal")] static extern int BackHomeHapticsInitialize();
    [DllImport("__Internal")] static extern void BackHomeHapticsPlay(int cue);
    [DllImport("__Internal")] static extern void BackHomeHapticsCancel();
#endif
}

/// <summary>Shared budget across all weapons. Completion can replace a softer pulse.</summary>
public struct HapticRateLimiter
{
    double _nextPulse;
    double _nextAttack;
    double _nextCargoStart;
    double _nextCargoComplete;

    public bool TryAccept(MobileHaptics.Cue cue, double now)
    {
        switch (cue)
        {
            case MobileHaptics.Cue.Attack:
                if (now < _nextPulse || now < _nextAttack) return false;
                _nextAttack = now + 0.2; // At most five light shots/second across the whole loadout.
                break;
            case MobileHaptics.Cue.CargoStart:
                if (now < _nextPulse || now < _nextCargoStart) return false;
                _nextCargoStart = now + 0.5;
                break;
            case MobileHaptics.Cue.CargoComplete:
                if (now < _nextCargoComplete) return false;
                _nextCargoComplete = now + 0.75;
                _nextPulse = now + 0.3;
                return true;
            default:
                return false;
        }
        _nextPulse = now + 0.16;
        return true;
    }
}

sealed class MobileHapticsLifecycle : MonoBehaviour
{
    void OnApplicationPause(bool paused) => MobileHaptics.SetPaused(paused);
    void OnApplicationFocus(bool focused) { if (!focused) MobileHaptics.Suspend(); }
    void OnApplicationQuit() => MobileHaptics.Suspend();
}
