using UnityEngine;

/// <summary>
/// First-person pilot for the Ship scene. The scene camera stays where it is;
/// the sitting astronaut is moved so the head bone sits in that camera, and
/// only the arms and hands are drawn.
/// </summary>
[DefaultExecutionOrder(200)]
public class ShipPilotHands : MonoBehaviour
{
    const string ModelResource = "Player/Models/Starbot_Animation_Sitting";
    const string MaterialResource = "Player/Materials/Player";

    Camera _camera;
    GameObject _pilot;
    Transform _root;
    Transform _head;
    Transform _leftArm;
    Transform _rightArm;
    Transform _leftHand;
    Transform _rightHand;
    AnimationClip _clip;
    Vector3 _leftArmBaseScale = Vector3.one;
    Vector3 _rightArmBaseScale = Vector3.one;
    bool _scaleCaptured;

    [Tooltip("How far the forearms swing up from the sit pose, in degrees, so the hands enter the view.")]
    [SerializeField] float handLiftDegrees = 38f;

    [Tooltip("Ship scene only. Shrinks the arms in place so the gloves stay where they are, just smaller.")]
    [SerializeField] float handScale = 0.4f;

    [Tooltip("Ship scene only. Slides the hands toward the lens so more of them sits outside the frame.")]
    [SerializeField] float handCloser = 0.07f;

    float _time;
    bool _ready;

    void Awake()
    {
        _camera = GetComponent<Camera>();
        if (_camera == null)
            _camera = Camera.main;
        if (_camera != null && _camera.nearClipPlane > 0.08f)
            _camera.nearClipPlane = 0.05f;
    }

    void Start()
    {
        Spawn();
    }

    void OnDestroy()
    {
        if (_pilot != null)
            Destroy(_pilot);
    }

    void LateUpdate()
    {
        if (!_ready || _clip == null || _pilot == null)
            return;

        if (_clip.length > 0.05f)
            _time = Mathf.Repeat(_time + Time.deltaTime, _clip.length);

        _clip.SampleAnimation(_pilot, _time);
        AlignHeadToCamera();
        LiftHands();
        ShrinkArms();
        BringHandsCloser();
    }

    void Spawn()
    {
        Object[] assets = Resources.LoadAll(ModelResource);
        GameObject prefab = null;
        AnimationClip clip = null;
        if (assets != null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (prefab == null)
                    prefab = assets[i] as GameObject;
                var candidate = assets[i] as AnimationClip;
                if (candidate == null || candidate.name.IndexOf("__preview__", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (clip == null || candidate.length > clip.length)
                    clip = candidate;
            }
        }

        if (prefab == null || clip == null)
        {
            Debug.LogError("[BackHome] Sitting astronaut model or clip was not found.");
            return;
        }

        _pilot = Instantiate(prefab);
        _pilot.name = "Pilot";
        _root = _pilot.transform;
        StripExtras(_pilot);

        // The imported Animator rebinds on the first frame and drops any playable
        // bound to it. The clip is sampled onto the hierarchy instead.
        Animator[] animators = _pilot.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
            animators[i].enabled = false;

        SkinnedMeshRenderer[] skins = _pilot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var material = Resources.Load<Material>(MaterialResource);
        for (int i = 0; i < skins.Length; i++)
        {
            skins[i].updateWhenOffscreen = true;
            skins[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            skins[i].receiveShadows = true;
            if (material != null)
            {
                var slots = skins[i].sharedMaterials;
                for (int s = 0; s < slots.Length; s++)
                    slots[s] = material;
                skins[i].sharedMaterials = slots;
            }
            KeepArmsAndHands(skins[i]);
        }

        CacheBones(_pilot.transform);
        if (_head == null || _leftHand == null || _rightHand == null)
        {
            Debug.LogError("[BackHome] Sitting astronaut is missing a head or hand bone.");
            return;
        }

        _clip = clip;
        _time = 0f;
        _clip.SampleAnimation(_pilot, 0f);
        AlignHeadToCamera();
        LiftHands();
        ShrinkArms();
        BringHandsCloser();
        _ready = true;
    }

    static void StripExtras(GameObject root)
    {
        var cameras = root.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
            cameras[i].enabled = false;
        var lights = root.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
            lights[i].enabled = false;
        var colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;
    }

    void CacheBones(Transform root)
    {
        Transform[] bones = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < bones.Length; i++)
        {
            string name = bones[i].name;
            if (_head == null && name.EndsWith("Head", System.StringComparison.Ordinal))
                _head = bones[i];
            else if (_leftArm == null && name.EndsWith("LeftArm", System.StringComparison.Ordinal))
                _leftArm = bones[i];
            else if (_rightArm == null && name.EndsWith("RightArm", System.StringComparison.Ordinal))
                _rightArm = bones[i];
            else if (_leftHand == null && name.EndsWith("LeftHand", System.StringComparison.Ordinal))
                _leftHand = bones[i];
            else if (_rightHand == null && name.EndsWith("RightHand", System.StringComparison.Ordinal))
                _rightHand = bones[i];
        }
    }

    void AlignHeadToCamera()
    {
        if (_camera == null)
            return;

        Vector3 headPos = _head.position;
        // The new sit pose holds the hands out to the sides, so the face
        // direction is the head bone, not the midpoint of the hands.
        Vector3 facing = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
        if (facing.sqrMagnitude < 0.02f)
            facing = Vector3.ProjectOnPlane(_head.up, Vector3.up);
        Vector3 camFwd = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up);
        if (facing.sqrMagnitude > 0.0001f && camFwd.sqrMagnitude > 0.0001f)
        {
            Quaternion yaw = Quaternion.FromToRotation(facing, camFwd);
            Vector3 pivot = headPos;
            _root.position = pivot + yaw * (_root.position - pivot);
            _root.rotation = yaw * _root.rotation;
        }

        _root.position += _camera.transform.position - _head.position;
    }

    void LiftHands()
    {
        if (_camera == null || handLiftDegrees <= 0.01f)
            return;

        float distance = 0.76f;
        LiftArm(_leftArm, _leftHand, new Vector3(0.40f, 0.38f, distance));
        LiftArm(_rightArm, _rightHand, new Vector3(0.60f, 0.38f, distance));
        TipHand(_leftHand, -18f);
        TipHand(_rightHand, 18f);
    }

    void TipHand(Transform hand, float inwardDegrees)
    {
        if (hand == null || _camera == null)
            return;

        Transform cam = _camera.transform;
        Quaternion tilt = Quaternion.AngleAxis(26f, cam.right) * Quaternion.AngleAxis(inwardDegrees, cam.up);
        hand.rotation = tilt * hand.rotation;
    }

    void LiftArm(Transform arm, Transform hand, Vector3 viewport)
    {
        if (arm == null || hand == null)
            return;

        Vector3 target = _camera.ViewportToWorldPoint(viewport);
        Vector3 toHand = hand.position - arm.position;
        Vector3 toTarget = target - arm.position;
        if (toHand.sqrMagnitude < 0.0001f || toTarget.sqrMagnitude < 0.0001f)
            return;

        Quaternion delta = Quaternion.FromToRotation(toHand, toTarget);
        float weight = Mathf.Clamp01(handLiftDegrees / 42f);
        arm.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * arm.rotation;
    }

    // Scales the arms around the wrists so the gloves stay in view and read smaller.
    void ShrinkArms()
    {
        if (!_scaleCaptured)
        {
            if (_leftArm != null)
                _leftArmBaseScale = _leftArm.localScale;
            if (_rightArm != null)
                _rightArmBaseScale = _rightArm.localScale;
            _scaleCaptured = true;
        }

        ShrinkArm(_leftArm, _leftHand, _leftArmBaseScale);
        ShrinkArm(_rightArm, _rightHand, _rightArmBaseScale);
    }

    void ShrinkArm(Transform arm, Transform hand, Vector3 baseScale)
    {
        if (arm == null || hand == null)
            return;

        float s = Mathf.Clamp(handScale, 0.35f, 1f);
        Vector3 handWorld = hand.position;
        arm.localScale = baseScale * s;
        arm.position += handWorld - hand.position;
    }

    void BringHandsCloser()
    {
        if (_camera == null || _root == null || handCloser <= 0.001f)
            return;

        _root.position -= _camera.transform.forward * handCloser;
    }

    static void KeepArmsAndHands(SkinnedMeshRenderer skin)
    {
        Mesh source = skin.sharedMesh;
        if (source == null || !source.isReadable || skin.bones == null || skin.bones.Length == 0)
        {
            Debug.LogWarning("[BackHome] Sitting mesh is not readable, so the full body stays visible.");
            return;
        }

        BoneWeight[] weights = source.boneWeights;
        if (weights == null || weights.Length != source.vertexCount)
        {
            Debug.LogWarning("[BackHome] Sitting mesh has no bone weights, so the full body stays visible.");
            return;
        }

        var armBone = new bool[skin.bones.Length];
        for (int i = 0; i < skin.bones.Length; i++)
            armBone[i] = skin.bones[i] != null && IsArmBone(skin.bones[i].name);

        var keep = new bool[source.vertexCount];
        for (int i = 0; i < weights.Length; i++)
        {
            float arm = WeightOn(weights[i].boneIndex0, weights[i].weight0, armBone)
                + WeightOn(weights[i].boneIndex1, weights[i].weight1, armBone)
                + WeightOn(weights[i].boneIndex2, weights[i].weight2, armBone)
                + WeightOn(weights[i].boneIndex3, weights[i].weight3, armBone);
            keep[i] = arm >= 0.45f;
        }

        int[] triangles = source.triangles;
        var kept = new System.Collections.Generic.List<int>(triangles.Length / 2);
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            if (keep[a] && keep[b] && keep[c])
            {
                kept.Add(a);
                kept.Add(b);
                kept.Add(c);
            }
        }

        if (kept.Count == 0)
        {
            Debug.LogWarning("[BackHome] No arm triangles found on the sitting astronaut.");
            return;
        }

        Mesh arms = Instantiate(source);
        arms.name = source.name + " Arms";
        arms.triangles = kept.ToArray();
        arms.RecalculateBounds();
        skin.sharedMesh = arms;
        Debug.Log("[BackHome] Pilot arms triangles " + (kept.Count / 3));
    }

    static float WeightOn(int bone, float weight, bool[] armBone)
    {
        if (bone < 0 || bone >= armBone.Length || !armBone[bone])
            return 0f;
        return weight;
    }

    static bool IsArmBone(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (name.IndexOf("Hand", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (name.IndexOf("Finger", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (name.IndexOf("Thumb", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (name.IndexOf("ForeArm", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return name.EndsWith("Arm", System.StringComparison.OrdinalIgnoreCase);
    }
}
