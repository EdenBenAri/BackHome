using UnityEngine;

/// <summary>A flat authoring handle for one existing object in the private runtime snapshot.</summary>
[DisallowMultipleComponent, SelectionBase]
public sealed class PlanetPlacement : MonoBehaviour
{
    [SerializeField, HideInInspector] Transform runtimeTarget;
    [SerializeField, HideInInspector] Vector3 runtimeScaleRatio = Vector3.one;
    public Transform RuntimeTarget => runtimeTarget;

    public void Bind(Transform target)
    {
        runtimeTarget = target;
        Vector3 scale = transform.lossyScale;
        runtimeScaleRatio = new Vector3(Ratio(target.localScale.x, scale.x),
            Ratio(target.localScale.y, scale.y), Ratio(target.localScale.z, scale.z));
    }

    static float Ratio(float target, float source) => Mathf.Abs(source) > .000001f ? target / source : 1f;

    public void Apply(PlanetAuthoring owner)
    {
        if (runtimeTarget == null)
            return;
        Transform chart = owner.transform;
        Transform sphere = owner.RuntimePlanet.transform;
        Vector3 flat = chart.InverseTransformPoint(transform.position);
        Quaternion flatRotation = Quaternion.Inverse(chart.rotation) * transform.rotation;
        runtimeTarget.SetPositionAndRotation(
            sphere.TransformPoint(PlanetProjection.ToSphere(flat, owner.Radius)),
            sphere.rotation * PlanetProjection.Frame(flat, owner.Radius) * flatRotation);
        runtimeTarget.localScale = Vector3.Scale(transform.lossyScale, runtimeScaleRatio);
        // The parent snapshot is still inactive, so Awake cannot observe a flat position.
        runtimeTarget.gameObject.SetActive(gameObject.activeInHierarchy);
    }
}
