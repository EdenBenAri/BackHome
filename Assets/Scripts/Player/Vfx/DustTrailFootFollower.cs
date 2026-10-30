using UnityEngine;

/// <summary>
/// Pins this transform to one foot bone so its DustTrailVfx/ParticleSystem trails from
/// that specific foot instead of the character's center - used to give running two separate dust
/// kicks, one per leg ("DustTrail_LeftFoot" / "DustTrail_RightFoot" under Player.prefab).
///
/// Follows the foot's lateral motion without inheriting bone scale or roll. A short ground probe
/// along the character's local up keeps the puffs on flat floors and spherical terrain while
/// ignoring the foot's swing height. Supports both Humanoid and named Generic bones.
/// </summary>
[DefaultExecutionOrder(50)]
public class DustTrailFootFollower : MonoBehaviour
{
    [SerializeField] HumanBodyBones foot = HumanBodyBones.LeftFoot;
    [Tooltip("Extra local offset (relative to the up-reference transform, i.e. the character root) " +
        "added after pinning to the foot's ground-level position.")]
    [SerializeField] Vector3 localOffset = Vector3.zero;
    [SerializeField] LayerMask groundLayers = 1 << 3;
    [SerializeField, Min(0.01f)] float surfaceOffset = 0.08f;

    public bool IsGrounded { get; private set; }

    Transform _footBone;
    Transform _upReference;

    public void SetFoot(HumanBodyBones value) => foot = value;

    void Awake()
    {
        Animator animator = GetComponentInParent<Animator>();
        _upReference = animator != null ? animator.transform : transform.parent;
        if (animator != null && animator.isHuman)
            _footBone = animator.GetBoneTransform(foot);

        // The current Starbot uses a Generic avatar. GetBoneTransform only supports Humanoid.
        if (_footBone == null && animator != null)
        {
            string boneName = foot == HumanBodyBones.RightFoot ? "RightFoot" : "LeftFoot";
            foreach (Transform bone in animator.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == boneName || bone.name.EndsWith(":" + boneName))
                {
                    _footBone = bone;
                    break;
                }
            }
        }
    }

    void LateUpdate()
    {
        if (_upReference == null)
            return;

        Vector3 local = _footBone != null
            ? _upReference.InverseTransformPoint(_footBone.position)
            : new Vector3(foot == HumanBodyBones.RightFoot ? 0.16f : -0.16f, 0f, 0f);
        local.y = 0f; // stay at ground level instead of following the foot's swing height
        Vector3 up = _upReference.up;
        Vector3 position = _upReference.TransformPoint(local + localOffset);
        IsGrounded = Physics.Raycast(position + up * 0.5f, -up, out RaycastHit hit,
            0.85f, groundLayers, QueryTriggerInteraction.Ignore);
        if (IsGrounded)
            position = hit.point + up * surfaceOffset;

        transform.SetPositionAndRotation(position, _upReference.rotation);
    }
}
