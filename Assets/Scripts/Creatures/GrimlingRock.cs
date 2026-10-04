using UnityEngine;

/// <summary>
/// Rock thrown by Grimling. Flies in an arc and damages the player only on impact.
/// </summary>
public class GrimlingRock : MonoBehaviour
{
    const float Gravity = 16f;
    const float HitRadius = 0.7f;
    const float Lifetime = 4f;
    const float FlightTime = 0.6f;

    static Material _material;
    static Mesh _mesh;

    Vector3 _velocity;
    float _life;
    float _age;
    float _damage;
    PlayerVitals _player;
    Vector3 _spin;
    bool _armed;

    public static void Throw(Vector3 origin, Vector3 target, float damage, PlayerVitals player)
    {
        var rock = new GameObject("GrimlingRock");
        rock.transform.position = origin;
        rock.transform.localScale = Vector3.Scale(
            new Vector3(0.42f, 0.32f, 0.38f),
            Vector3.one * Random.Range(0.85f, 1.15f));

        var filter = rock.AddComponent<MeshFilter>();
        filter.sharedMesh = SharedMesh();
        var renderer = rock.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = SharedMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        var body = rock.AddComponent<GrimlingRock>();
        body.Arm(origin, target, damage, player);
    }

    void Arm(Vector3 origin, Vector3 target, float damage, PlayerVitals player)
    {
        _damage = Mathf.Max(0f, damage);
        _player = player;
        _life = Lifetime;
        _age = 0f;
        _spin = Random.insideUnitSphere * 540f;
        _armed = true;

        Vector3 up = UpAt(origin);
        Vector3 to = target - origin;
        Vector3 flat = Vector3.ProjectOnPlane(to, up);
        float distance = flat.magnitude;
        if (distance < 0.2f)
            flat = Vector3.ProjectOnPlane(transform.forward, up);
        if (flat.sqrMagnitude < 0.0001f)
            flat = Vector3.Cross(up, Vector3.right);

        float speed = Mathf.Clamp(distance / FlightTime, 9f, 18f);
        float time = Mathf.Max(0.2f, distance / speed);
        float vertical = Vector3.Dot(to, up);
        float rise = vertical / time + 0.5f * Gravity * time;
        _velocity = flat.normalized * speed + up * rise;
    }

    void Update()
    {
        if (!_armed)
            return;

        float step = Time.deltaTime;
        _age += step;
        _life -= step;
        if (_life <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 up = UpAt(transform.position);
        _velocity += -up * Gravity * step;
        transform.position += _velocity * step;
        transform.Rotate(_spin * step, Space.Self);

        if (HitPlayer(up))
        {
            _player.TakeDamage(_damage);
            Destroy(gameObject);
            return;
        }

        if (_age > 0.2f && Clearance(up) <= 0.18f)
            Destroy(gameObject);
    }

    bool HitPlayer(Vector3 up)
    {
        if (_player == null || !_player.IsAlive)
            return false;

        float radius = HitRadius * HitRadius;
        Vector3 chest = _player.transform.position + up * 0.9f;
        if ((chest - transform.position).sqrMagnitude <= radius)
            return true;

        return (_player.transform.position - transform.position).sqrMagnitude <= radius;
    }

    float Clearance(Vector3 up)
    {
        SphericalPlanet planet = SphericalPlanet.Instance;
        if (planet == null)
            return 1f;

        Vector3 fromCenter = transform.position - planet.Center;
        if (fromCenter.sqrMagnitude < 0.01f)
            return 1f;

        Vector3 surface = planet.GetSurfacePoint(fromCenter);
        return Vector3.Dot(transform.position - surface, up);
    }

    static Vector3 UpAt(Vector3 position)
    {
        return SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(position)
            : Vector3.up;
    }

    static Mesh SharedMesh()
    {
        if (_mesh != null)
            return _mesh;

        var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(temp);
        return _mesh;
    }

    static Material SharedMaterial()
    {
        if (_material != null)
            return _material;

        Shader shader = Shader.Find("BackHome/CasualToon");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");

        _material = new Material(shader)
        {
            name = "GrimlingRock",
            hideFlags = HideFlags.HideAndDontSave
        };
        if (_material.HasProperty("_BaseColor"))
            _material.SetColor("_BaseColor", new Color(0.46f, 0.45f, 0.40f, 1f));
        if (_material.HasProperty("_Opacity"))
            _material.SetFloat("_Opacity", 1f);
        if (_material.HasProperty("_EmissionColor"))
            _material.SetColor("_EmissionColor", Color.black);
        return _material;
    }
}
