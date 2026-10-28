using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Crash-landing cinematic: the live Player transform falls from space onto the planet. The
/// visible mesh during the fall is the nested Starbot FBX from PlayerDiveDownCapsule (the rig the
/// dive/land clips were authored on). The gameplay Player mesh stays hidden until the land clip
/// finishes (or is skipped by move input in its last 1.5s), then locomotion unlocks. The authored
/// fire trail rides on that cinematic mesh.
///
/// One reusable prefab: every planet scene drops this in and wires only <see cref="playerCapsule"/>
/// to its own PlayerDiveDownCapsule instance. Start pose and fall distance are derived from the
/// planet (via the walk-surface pose, same stick as gameplay).
/// </summary>
[DefaultExecutionOrder(-500)]
public class PlayerCrashIntro : MonoBehaviour
{
    [Header("Actor")]
    [FormerlySerializedAs("shipCapsule")]
    [SerializeField] Transform playerCapsule;
    [Tooltip("Optional override. Leave empty to use SceneBootstrap's player prefab, then Resources/Player/Player.")]
    [SerializeField] GameObject playerPrefab;
    [Tooltip("Authored impact burst (Hovl Studio explosion/dust inside ImpactVfx). Passed " +
        "to PlayerCrashImpact so the look is edited as a normal prefab instead of generated in code. " +
        "Leave empty to load ImpactVfx from Resources (see PlayerDiveDownCapsulePaths).")]
    [SerializeField] GameObject impactEffectPrefab;

    [Header("Timing")]
    [Tooltip("Seconds for the crash fall itself. Keep this short - it's a hard crash, not a slow glide.")]
    [SerializeField, Min(0.05f)] float fallDuration = 1.1f;
    [Tooltip("Straight-line speed profile. Linear (or ease-in) reads as a hard crash; ease-in-out reads as a soft landing.")]
    [SerializeField] AnimationCurve fallEase = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Space Start Pose")]
    [Tooltip("How far out (along the planet's radial up at the landing site) the capsule starts, alone in space.")]
    [SerializeField, Min(1f)] float startDistance = 70f;
    [Tooltip("Snap the landing spot onto the planet surface instead of trusting wherever the " +
        "capsule was authored in the scene.")]
    [SerializeField] bool snapLandingToGround = true;
    [Tooltip("Extra world-space clearance above the planet walk surface at rest.")]
    [SerializeField] float extraGroundClearance = 0.35f;

    [Header("Crash Tremble")]
    [Tooltip("Tremble amplitude applied throughout the fall itself, in world units - sells a rough, out-of-control crash rather than a smooth glide.")]
    [SerializeField, Min(0f)] float fallTrembleAmplitude = 0.12f;
    [Tooltip("How fast the tremble oscillates.")]
    [SerializeField, Min(0.01f)] float fallTrembleFrequency = 18f;

    [Header("Cinematic Camera")]
    [Tooltip("Tight opening shot, so the falling player fills the frame.")]
    [SerializeField] float cinematicStartOffsetHeight = 3.2f;
    [SerializeField] float cinematicStartOffsetBack = 5.5f;
    [Tooltip("Wide crash shot as the player nears the ground - the previous cinematic framing.")]
    [SerializeField] float cinematicOffsetHeight = 16f;
    [SerializeField] float cinematicOffsetBack = 26f;
    [Tooltip("Pulls the camera from the opening close-up to the wide crash shot. 0 = tight, 1 = wide.")]
    [SerializeField] AnimationCurve cinematicZoomOut = DefaultCinematicZoomOut();
    [Tooltip("Camera jolt on landing. Set duration to 0 to disable.")]
    [SerializeField, Min(0f)] float impactShakeDuration = 0.5f;
    [SerializeField, Min(0f)] float impactShakeMagnitude = 5.5f;

    /// <summary>Raised as soon as the falling Player exists, before the fall starts.
    /// SceneBootstrap binds camera / HUD here — the same transform later walks.</summary>
    public event Action<Transform> OnPlayerReady;

    /// <summary>Raised once locomotion is unlocked (land clip finished or skipped by move).</summary>
    public event Action OnLanded;

    Transform _landingSite;
    Transform _player;
    PlayerLandIntro _actor;
    PlayerDiveAnimation _diveAnimation;
    PlayerFireTrail _attachedTrail;
    Transform _diveModel;
    Renderer[] _hiddenPlayerRenderers;
    Vector3 _restPosition;
    Quaternion _restRotation;
    bool _hasRun;
    Renderer[] _capsuleRenderers;
    Collider[] _capsuleColliders;

    void Awake()
    {
        DisableCapsuleTriggers();
    }

    void Start()
    {
        if (playerCapsule == null)
        {
            Debug.LogWarning("PlayerCrashIntro: no playerCapsule assigned, skipping cinematic.", this);
            if (TrySpawnPlayer(transform.position, transform.rotation))
                OnPlayerReady?.Invoke(_player);
            OnLanded?.Invoke();
            return;
        }

        StartCoroutine(RunSequence());
    }

    void DisableCapsuleTriggers()
    {
        if (playerCapsule == null)
            return;

        Collider[] colliders = playerCapsule.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }

    Transform LandingAnchor => playerCapsule;

    IEnumerator RunSequence()
    {
        if (_hasRun)
            yield break;
        _hasRun = true;

        Transform landingAnchor = LandingAnchor;
        _restPosition = landingAnchor.position;
        _restRotation = landingAnchor.rotation;

        SphericalPlanet planet = ResolvePlanet();
        Vector3 up = planet != null ? planet.GetUpAt(_restPosition) : Vector3.up;

        if (snapLandingToGround && planet != null)
            SnapLandingToWalkSurface(planet, extraGroundClearance, ref _restPosition, ref _restRotation, ref up);
        else if (planet != null && extraGroundClearance != 0f)
            _restPosition += up * extraGroundClearance;

        Vector3 spacePosition = _restPosition + up * startDistance;
        EnsureLandingSite(_restPosition, _restRotation);

        if (!TrySpawnPlayer(spacePosition, _restRotation))
        {
            Debug.LogWarning("PlayerCrashIntro: could not spawn Player.prefab, skipping cinematic.", this);
            OnLanded?.Invoke();
            yield break;
        }

        playerCapsule.SetPositionAndRotation(spacePosition, _restRotation);
        _player.SetPositionAndRotation(spacePosition, _restRotation);
        ConvertCapsuleToVfx();

        _actor.BeginCinematic();
        HidePlayerGameplayVisuals();
        if (_diveAnimation != null && _diveAnimation.HasDiveClip)
            _diveAnimation.PlayDive();

        OnPlayerReady?.Invoke(_player);

        CameraFollow cameraFollow = ResolveCameraFollow();
        bool hasOriginalOffsets = cameraFollow != null;
        float originalHeight = hasOriginalOffsets ? cameraFollow.OffsetHeight : 0f;
        float originalBack = hasOriginalOffsets ? cameraFollow.OffsetBack : 0f;

        if (cameraFollow != null)
        {
            ApplyCinematicCamera(cameraFollow, 0f);
            cameraFollow.SetTarget(_player);
            cameraFollow.SetSnapToTarget(true);
        }

        _attachedTrail?.Play(-up);

        float contact = _diveAnimation != null && _diveAnimation.HasLandClip
            ? _diveAnimation.LandGroundContactTime
            : 0f;
        // Switch onto the land clip just before impact, already at the grounded pose — starting
        // the land take at t=0 would replay the aerial hips-drop and pop the character back up.
        float lead = Mathf.Min(contact, 0.25f);
        float landClipStart = Mathf.Max(0f, contact - lead);
        bool landStarted = false;

        float fallTimer = 0f;
        while (fallTimer < fallDuration)
        {
            fallTimer += Time.deltaTime;
            float t = fallEase.Evaluate(Mathf.Clamp01(fallTimer / fallDuration));
            Vector3 basePosition = Vector3.LerpUnclamped(spacePosition, _restPosition, t);
            Vector3 pose = basePosition + GetFallTrembleOffset(fallTimer);
            _player.SetPositionAndRotation(pose, _restRotation);
            playerCapsule.SetPositionAndRotation(pose, _restRotation);
            ApplyCinematicCamera(cameraFollow, t);

            if (!landStarted && fallTimer >= fallDuration - lead)
            {
                _diveAnimation?.PlayLand(landClipStart);
                landStarted = true;
            }

            _diveAnimation?.Tick(Time.deltaTime);
            yield return null;
        }

        _player.SetPositionAndRotation(_restPosition, _restRotation);
        playerCapsule.SetPositionAndRotation(_restPosition, _restRotation);
        ApplyCinematicCamera(cameraFollow, 1f);

        if (!landStarted)
            _diveAnimation?.PlayLand(landClipStart);

        HideCrashVfx();
        ResolveImpactEffect()?.Trigger();
        cameraFollow?.ImpactShake(impactShakeDuration, impactShakeMagnitude);
        HideCapsuleAfterLanding();

        // Hold the crash framing through the landing jolt so it doesn't get lost in the
        // handoff back to the gameplay camera.
        if (cameraFollow != null && impactShakeDuration > 0f)
            yield return new WaitForSeconds(Mathf.Min(0.22f, impactShakeDuration * 0.45f));

        if (cameraFollow != null)
        {
            cameraFollow.SetSnapToTarget(false);
            if (hasOriginalOffsets)
                cameraFollow.SetOffsets(originalHeight, originalBack);
        }

        while (_diveAnimation != null && !_diveAnimation.IsFinished)
        {
            _player.SetPositionAndRotation(_restPosition, _restRotation);
            _diveAnimation.Tick(Time.deltaTime);
            if (_diveAnimation.CanSkipLand && _actor != null && _actor.WantsMove)
                break;
            yield return null;
        }

        RevealPlayerGameplayVisuals();
        _actor.EndCinematic();
        OnLanded?.Invoke();
    }

    bool TrySpawnPlayer(Vector3 position, Quaternion rotation)
    {
        Transform existing = FindExistingPlayer();
        if (existing != null)
        {
            _player = existing;
        }
        else
        {
            GameObject prefab = ResolvePlayerPrefab();
            if (prefab == null)
                return false;

            GameObject playerObject = Instantiate(prefab, position, rotation);
            playerObject.name = prefab.name;
            _player = playerObject.transform;
        }

        _actor = _player.GetComponent<PlayerLandIntro>();
        if (_actor == null)
            _actor = _player.gameObject.AddComponent<PlayerLandIntro>();
        return true;
    }

    GameObject ResolvePlayerPrefab()
    {
        if (playerPrefab != null)
            return playerPrefab;

        SceneBootstrap bootstrap = FindAnyObjectByType<SceneBootstrap>();
        if (bootstrap != null && bootstrap.PlayerPrefab != null)
            return bootstrap.PlayerPrefab;

        return Resources.Load<GameObject>("Player/Player");
    }

    static Transform FindExistingPlayer()
    {
        TouchController motor = FindAnyObjectByType<TouchController>();
        if (motor != null)
            return motor.transform;

        GameObject tagged = GameObject.FindGameObjectWithTag("Player");
        return tagged != null ? tagged.transform : null;
    }

    /// <summary>Wear the authored dive FBX on the live Player transform. That mesh matches the
    /// dive/land clips; the gameplay Player mesh does not (different bind pose). Trail stays on
    /// the dive model so it follows Armature/Hips.</summary>
    void ConvertCapsuleToVfx()
    {
        DisableCapsuleBeacon();

        _diveAnimation = playerCapsule.GetComponent<PlayerDiveAnimation>();
        if (_diveAnimation == null)
            _diveAnimation = playerCapsule.GetComponentInChildren<PlayerDiveAnimation>(true);
        if (_diveAnimation == null)
            _diveAnimation = playerCapsule.gameObject.AddComponent<PlayerDiveAnimation>();
        _diveAnimation.enabled = true;

        _diveModel = _diveAnimation.ModelRoot;
        if (_diveModel == null)
            _diveModel = playerCapsule.Find(PlayerDiveDownCapsulePaths.DiveModelChildName);

        if (_diveModel != null)
        {
            _diveModel.SetParent(_player, false);
            _diveModel.localPosition = Vector3.zero;
            _diveModel.localRotation = Quaternion.identity;
            _diveModel.localScale = Vector3.one;
            _diveModel.gameObject.SetActive(true);
        }

        _attachedTrail = _diveModel != null
            ? _diveModel.GetComponentInChildren<PlayerFireTrail>(true)
            : playerCapsule.GetComponentInChildren<PlayerFireTrail>(true);
        if (_attachedTrail != null)
            _attachedTrail.RetargetFollow();

        _capsuleRenderers = playerCapsule.GetComponentsInChildren<Renderer>(true);
        _capsuleColliders = playerCapsule.GetComponentsInChildren<Collider>(true);
        if (_capsuleColliders != null)
        {
            for (int i = 0; i < _capsuleColliders.Length; i++)
            {
                if (_capsuleColliders[i] != null)
                    _capsuleColliders[i].enabled = false;
            }
        }
    }

    void HidePlayerGameplayVisuals()
    {
        if (_player == null)
            return;

        Renderer[] renderers = _player.GetComponentsInChildren<Renderer>(true);
        var hidden = new System.Collections.Generic.List<Renderer>(renderers.Length);
        HpBarView vitalsView = _player.GetComponentInChildren<HpBarView>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;
            if (IsCinematicVisual(renderer.transform))
                continue;
            if (vitalsView != null && renderer.transform.IsChildOf(vitalsView.transform))
                continue;
            renderer.enabled = false;
            hidden.Add(renderer);
        }

        _hiddenPlayerRenderers = hidden.ToArray();
    }

    void RevealPlayerGameplayVisuals()
    {
        _diveAnimation?.Stop();
        if (_diveModel != null)
        {
            _diveModel.gameObject.SetActive(false);
            if (playerCapsule != null)
                _diveModel.SetParent(playerCapsule, false);
        }

        if (_hiddenPlayerRenderers == null)
            return;

        for (int i = 0; i < _hiddenPlayerRenderers.Length; i++)
        {
            if (_hiddenPlayerRenderers[i] != null)
                _hiddenPlayerRenderers[i].enabled = true;
        }

        _hiddenPlayerRenderers = null;
    }

    bool IsCinematicVisual(Transform t)
    {
        if (t == null)
            return false;
        if (_diveModel != null && (t == _diveModel || t.IsChildOf(_diveModel)))
            return true;
        if (_attachedTrail != null && (t == _attachedTrail.transform || t.IsChildOf(_attachedTrail.transform)))
            return true;
        return false;
    }

    void HideCrashVfx()
    {
        _attachedTrail?.Stop();
        if (_attachedTrail != null)
            _attachedTrail.gameObject.SetActive(false);
    }

    Vector3 GetFallTrembleOffset(float time)
    {
        if (fallTrembleAmplitude <= 0f)
            return Vector3.zero;

        float t = time * fallTrembleFrequency;
        float x = Mathf.PerlinNoise(t, 0.17f) - 0.5f;
        float y = Mathf.PerlinNoise(0.42f, t) - 0.5f;
        float z = Mathf.PerlinNoise(t, 0.83f) - 0.5f;
        return new Vector3(x, y, z) * (2f * fallTrembleAmplitude);
    }

    /// <summary>0 = opening close-up, 1 = the wide crash shot we used to hold for the whole fall.</summary>
    static AnimationCurve DefaultCinematicZoomOut()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(0.22f, 0.08f, 0.45f, 0.45f),
            new Keyframe(1f, 1f, 1.45f, 0f));
    }

    void ApplyCinematicCamera(CameraFollow cameraFollow, float fallProgress)
    {
        if (cameraFollow == null)
            return;

        float zoom = cinematicZoomOut != null && cinematicZoomOut.length > 0
            ? Mathf.Clamp01(cinematicZoomOut.Evaluate(Mathf.Clamp01(fallProgress)))
            : Mathf.Clamp01(fallProgress);
        cameraFollow.SetOffsets(
            Mathf.Lerp(cinematicStartOffsetHeight, cinematicOffsetHeight, zoom),
            Mathf.Lerp(cinematicStartOffsetBack, cinematicOffsetBack, zoom));
    }

    static SphericalPlanet ResolvePlanet() =>
        SphericalPlanet.Instance != null ? SphericalPlanet.Instance : FindAnyObjectByType<SphericalPlanet>();

    static CameraFollow ResolveCameraFollow()
    {
        CameraFollow follow = FindAnyObjectByType<CameraFollow>();
        if (follow == null && Camera.main != null)
            follow = Camera.main.GetComponent<CameraFollow>();
        return follow;
    }

    PlayerCrashImpact ResolveImpactEffect()
    {
        PlayerCrashImpact impact = playerCapsule.GetComponent<PlayerCrashImpact>();
        if (impact == null)
            impact = playerCapsule.gameObject.AddComponent<PlayerCrashImpact>();
        // Only override if wired here - lets PlayerCrashImpact keep its own prefab if it was added
        // and configured by hand directly on the capsule instead.
        if (impactEffectPrefab != null)
            impact.SetEffectPrefab(impactEffectPrefab);
        return impact;
    }

    static void SnapLandingToWalkSurface(
        SphericalPlanet planet,
        float hover,
        ref Vector3 restPosition,
        ref Quaternion restRotation,
        ref Vector3 up)
    {
        PlanetTileMap tiles = planet.GetComponent<PlanetTileMap>();
        float yaw = PlanetSurfacePose.ExtractYaw(restRotation, up);
        if (!PlanetSurfacePose.TryGetPose(
                planet, tiles, up, yaw, hover,
                out Vector3 groundPosition, out Quaternion groundRotation, out Vector3 groundUp))
        {
            restPosition = planet.GetSurfacePoint(up, hover);
            return;
        }

        restPosition = groundPosition;
        restRotation = groundRotation;
        up = groundUp;

        if (PlanetSurfacePose.TrySampleGroundBelow(
                restPosition + up * 2f, up, 12f, hover, out Vector3 rayPosition, out Vector3 rayNormal))
        {
            restPosition = rayPosition;
            up = rayNormal;
            restRotation = PlanetSurfacePose.RotationFromUp(up, yaw);
        }
    }

    /// <summary>Invisible marker at the crash site: hosts <see cref="PlayerCapsuleBeacon"/> so
    /// the home arrow still points here after the cinematic capsule is hidden.</summary>
    void EnsureLandingSite(Vector3 position, Quaternion rotation)
    {
        if (_landingSite != null)
            return;

        GameObject site = new GameObject("CrashLandingSite");
        site.transform.SetPositionAndRotation(position, rotation);

        if (site.GetComponent<PlayerCapsuleBeacon>() == null)
            site.AddComponent<PlayerCapsuleBeacon>();

        Transform parent = PlanetSurfacePose.GetOrCreateObjectsRoot(ResolvePlanet());
        if (parent != null)
            site.transform.SetParent(parent, true);

        _landingSite = site.transform;
    }

    /// <summary>Computes where the player should spawn if this cinematic is not the one placing
    /// them: the crash site itself. Used by <see cref="SceneBootstrap"/> as a fallback.</summary>
    public bool TryComputePlayerSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        Transform anchor = _landingSite != null ? _landingSite : LandingAnchor;
        if (anchor == null)
            return false;

        position = anchor.position;
        rotation = anchor.rotation;
        return true;
    }

    /// <summary>Hides the dive capsule's own renderers and colliders once the landing
    /// site marker has taken over the home arrow.</summary>
    void HideCapsuleAfterLanding()
    {
        if (_capsuleRenderers != null)
        {
            for (int i = 0; i < _capsuleRenderers.Length; i++)
            {
                Renderer renderer = _capsuleRenderers[i];
                if (renderer == null)
                    continue;
                if (!renderer.transform.IsChildOf(playerCapsule))
                    continue;
                renderer.enabled = false;
            }
        }

        if (_capsuleColliders != null)
        {
            for (int i = 0; i < _capsuleColliders.Length; i++)
            {
                Collider collider = _capsuleColliders[i];
                if (collider == null)
                    continue;
                if (!collider.transform.IsChildOf(playerCapsule))
                    continue;
                collider.enabled = false;
            }
        }

        DisableCapsuleBeacon();
    }

    void DisableCapsuleBeacon()
    {
        if (playerCapsule == null)
            return;

        PlayerCapsuleBeacon beacon = playerCapsule.GetComponent<PlayerCapsuleBeacon>();
        if (beacon != null)
            beacon.enabled = false;
    }
}
