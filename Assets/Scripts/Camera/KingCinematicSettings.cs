using UnityEngine;

/// <summary>Shared, designer-editable chapter presentation settings. All durations are real seconds.</summary>
[CreateAssetMenu(menuName = "BackHome/Camera/King Cinematic Settings")]
public sealed class KingCinematicSettings : ScriptableObject
{
    [Header("Chapter Introduction")]
    public bool playIntroduction = true;
    public string firstKingArea = "KingA1";
    [Min(0f)] public float initialFreeMovement = .3f;
    [Tooltip("Grace period for a delayed spawner. A missing king never freezes gameplay.")]
    [Min(0f)] public float spawnAvailabilityTimeout = 2f;
    [Tooltip("Ignore long scene-loading frames when counting the initial playable window.")]
    [Range(.016f, .3f)] public float maximumIntroFrameStep = .1f;

    [Header("Victory")]
    public bool playVictoryReveals = true;
    [Tooltip("Replay once per King on each chapter visit, even if that King was defeated in the saved progression. Respawns in the same visit do not replay it.")]
    public bool replayVictoriesOnChapterEntry = true;
    [Min(0f)] public float slowMotionDuration = .7f;
    [Range(.01f, 1f)] public float slowMotionScale = .04f;
    [Min(.01f)] public float turnDuration = .3f;

    [Header("Reveal")]
    [Min(.01f)] public float zoomInDuration = .65f;
    [Min(.01f)] public float zoomOutDuration = .65f;
    [Tooltip("Hold for the actual Spawn animation, or this minimum, whichever is longer.")]
    [Min(.5f)] public float minimumSpawnHold = .5f;
    public AnimationCurve movementEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Close Shot")]
    [Min(1f)] public float closeShotHeight = 8f;
    [Min(1f)] public float closeShotBack = 10f;
    public bool fitKingToScreen = true;
    [Min(1f)] public float framingPadding = 1.1f;
    [Min(.1f)] public float fallbackBodyRadius = 1.5f;
    [Min(0f)] public float heightPerBodyRadius = 1.5f;
    [Min(0f)] public float minimumFocusHeight = .8f;
    [Range(0f, 2f)] public float focusHeightPerBodyRadius = .45f;
}
