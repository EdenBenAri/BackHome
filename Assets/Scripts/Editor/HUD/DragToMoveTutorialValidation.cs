using System;
using System.IO;
using StarterAssets;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in live checks using temporary input devices. Never saves the open scene.</summary>
[InitializeOnLoad]
public static class DragToMoveTutorialValidation
{
    const string Key = "BackHome.TutorialValidation";
    const string Trigger = "Temp/BackHomeTutorialValidation.trigger";
    public const string Report = "Logs/DragToMoveTutorialValidation.txt";
    static DragToMoveTutorialValidation() => EditorApplication.update += Poll;

    [MenuItem("BackHome/HUD/Validate Drag To Move Tutorial In Play")]
    public static void Request() => File.WriteAllText(Trigger, "run");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        if (SessionState.GetBool(Key, false))
            new GameObject("Temporary tutorial validation").AddComponent<DragToMoveTutorialValidationRunner>();
    }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Key + ".restore", false))
        {
            var progress = Object.FindAnyObjectByType<PlanetStageProgression>(FindObjectsInactive.Include);
            if (progress != null) SetPersistence(progress, SessionState.GetBool(Key + ".persist", true));
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
            Time.timeScale = SessionState.GetFloat(Key + ".scale", 1f);
            SessionState.SetBool(Key + ".restore", false);
            SessionState.SetBool(Key, false);
        }
        if (File.Exists(Trigger) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(Trigger);
            var progress = Object.FindAnyObjectByType<PlanetStageProgression>(FindObjectsInactive.Include);
            if (progress == null) { Debug.LogError("Open Galaxy before running tutorial validation."); return; }
            var so = new SerializedObject(progress);
            SessionState.SetBool(Key + ".persist", so.FindProperty("persistProgress").boolValue);
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            SessionState.SetFloat(Key + ".scale", Time.timeScale);
            SessionState.SetFloat(Key + ".deadline", (float)EditorApplication.timeSinceStartup + 90f);
            SessionState.SetBool(Key + ".restore", true);
            SessionState.SetBool(Key, true);
            SetPersistence(progress, false);
            Application.runInBackground = true;
            Time.timeScale = 1f;
            File.WriteAllText(Report, "Tutorial live validation\n");
            EditorApplication.isPlaying = true;
        }
        if (SessionState.GetBool(Key, false)
            && EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + ".deadline", 0f))
            Finish("FAIL timeout");
    }

    static void SetPersistence(PlanetStageProgression progress, bool persist)
    {
        var so = new SerializedObject(progress);
        so.FindProperty("persistProgress").boolValue = persist;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void Log(string message) => File.AppendAllText(Report, message + "\n");
    public static void Finish(string result)
    {
        Log(result);
        SessionState.SetBool(Key, false);
        EditorApplication.isPlaying = false;
    }
}

[DefaultExecutionOrder(30000)]
public sealed class DragToMoveTutorialValidationRunner : MonoBehaviour
{
    DragToMoveTutorial _tutorial;
    KingCinematicDirector _director;
    StarterAssetsInputs _input;
    Transform _parent;
    RectTransform _hand;
    Mouse _mouse;
    Touchscreen _touch;
    Vector2 _handStart;
    float _since;
    int _step, _frame;
    bool _sawIntro;

    void Start()
    {
        _tutorial = FindAnyObjectByType<DragToMoveTutorial>();
        _director = FindAnyObjectByType<KingCinematicDirector>();
        _input = FindAnyObjectByType<StarterAssetsInputs>();
        _mouse = InputSystem.AddDevice<Mouse>("Tutorial validation mouse");
        _touch = InputSystem.AddDevice<Touchscreen>("Tutorial validation touch");
        foreach (var combat in FindObjectsByType<PlayerRangeCombat>(FindObjectsSortMode.None)) combat.enabled = false;
        _since = Time.unscaledTime;
    }

    void LateUpdate()
    {
        try { Step(); }
        catch (Exception ex)
        {
            enabled = false;
            DragToMoveTutorialValidation.Finish("FAIL " + ex);
        }
    }

    void Step()
    {
        Require(_tutorial != null && _director != null && _input != null, "Missing scene wiring.");
        switch (_step)
        {
            case 0:
                if (_director.CurrentPhase != KingCinematicDirector.Phase.Idle)
                {
                    _sawIntro = true;
                    Require(!_tutorial.IsVisible, "Tutorial appeared during the introduction.");
                    if (_director.IsPresenting)
                    {
                        MouseButton(true);
                        Next();
                    }
                }
                break;
            case 1:
                Require(!_tutorial.IsDismissed && !_tutorial.IsVisible, "Intro touch incorrectly dismissed/revealed tutorial.");
                MouseButton(false);
                Next();
                break;
            case 2:
                if (!_tutorial.IsVisible) break;
                Require(_sawIntro && _director.CurrentPhase == KingCinematicDirector.Phase.Idle, "Camera has not returned.");
                Require(!_input.GameplayBlocked && Time.timeScale > 0f, "Gameplay was not released.");
                Require(!_tutorial.GetComponent<CanvasGroup>().blocksRaycasts, "Tutorial blocks gestures.");
                foreach (Graphic graphic in _tutorial.GetComponentsInChildren<Graphic>())
                    Require(!graphic.raycastTarget, "Tutorial graphic blocks gestures: " + graphic.name);
                _parent = _tutorial.transform.parent;
                _hand = (RectTransform)new SerializedObject(_tutorial).FindProperty("hand").objectReferenceValue;
                _handStart = _hand.anchoredPosition;
                DragToMoveTutorialValidation.Log("PASS hidden through introduction (including early touch); visible after camera/input release; no raycast interception");
                Next();
                break;
            case 3:
                if (Time.unscaledTime - _since < .7f) break;
                Require(Vector2.Distance(_handStart, _hand.anchoredPosition) > 25f, "Hand did not follow the arrow.");
                ScreenCapture.CaptureScreenshot("Logs/DragToMoveTutorial-Portrait.png");
                DragToMoveTutorialValidation.Log("PASS animated hand; saved portrait screenshot");
                Next();
                break;
            case 4:
                if (Time.frameCount - _frame < 3) break;
                MouseButton(true);
                Next();
                break;
            case 5:
                Require(_tutorial.IsDismissed && !_tutorial.IsVisible, "Mouse press did not dismiss on its first frame.");
                Require(_input.IsMoveGestureActive, "First press was swallowed instead of starting the joystick.");
                DragToMoveTutorialValidation.Log("PASS first mouse press dismisses and starts movement in the same frame");
                MouseButton(false);
                Next();
                break;
            case 6:
                if (Time.frameCount - _frame < 3) break;
                _input.EndMoveGesture();
                NewHint();
                Next();
                break;
            case 7:
                if (!_tutorial.IsVisible) break;
                Touch(UnityEngine.InputSystem.TouchPhase.Began);
                Next();
                break;
            case 8:
                Require(_tutorial.IsDismissed && !_tutorial.IsVisible, "Touch did not dismiss on its first frame.");
                Require(_input.IsMoveGestureActive, "First touch did not reach the joystick.");
                Touch(UnityEngine.InputSystem.TouchPhase.Ended);
                DragToMoveTutorialValidation.Log("PASS first touchscreen contact dismisses and starts movement in the same frame");
                Next();
                break;
            case 9:
                if (Time.frameCount - _frame < 3) break;
                _input.EndMoveGesture();
                NewHint();
                Time.timeScale = 0f;
                Next();
                break;
            case 10:
                if (Time.frameCount - _frame < 3) break;
                Require(!_tutorial.IsVisible, "Hint appeared over a paused game.");
                Time.timeScale = 1f;
                _director.enabled = false;
                Next();
                break;
            case 11:
                if (!_tutorial.IsVisible) break;
                DragToMoveTutorialValidation.Log("PASS pause hides hint; disabled/skipped cinematic does not stall it");
                _tutorial.Dismiss();
                _tutorial.gameObject.SetActive(true);
                Next();
                break;
            case 12:
                Require(_tutorial.IsDismissed && !_tutorial.IsVisible, "Dismissed tutorial replayed on re-enable.");
                enabled = false;
                DragToMoveTutorialValidation.Finish("PASS all tutorial checks");
                break;
        }
    }

    void Next() { _step++; _since = Time.unscaledTime; _frame = Time.frameCount; }
    void NewHint() => _tutorial = Instantiate(Resources.Load<GameObject>("Tutorial/DragToMoveTutorial"), _parent, false)
        .GetComponent<DragToMoveTutorial>();
    Vector2 Contact => new(Screen.width * .5f, Screen.height * .32f);
    void MouseButton(bool pressed) => InputSystem.QueueStateEvent(_mouse,
        new MouseState { position = Contact }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, pressed));
    void Touch(UnityEngine.InputSystem.TouchPhase phase) => InputSystem.QueueStateEvent(_touch,
        new TouchState { touchId = 1, phase = phase, position = Contact });
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    void OnDestroy()
    {
        if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
        if (_touch != null && _touch.added) InputSystem.RemoveDevice(_touch);
    }
}
