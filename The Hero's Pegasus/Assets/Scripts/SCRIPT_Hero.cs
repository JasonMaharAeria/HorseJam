using UnityEngine;

/// <summary>
/// Controls the hero sitting on top of the pegasus. Independently rotates to face
/// the nearest enemy and fires arrow projectiles with predictive lead targeting:
/// the hero solves for the intercept point by accounting for the enemy's velocity
/// and its own (the pegasus's) velocity so arrows lead the target correctly.
/// </summary>
public class SCRIPT_Hero : MonoBehaviour
{
    [Header("Arrow Settings")]
    [Tooltip("Arrow prefab to instantiate. Must have a SCRIPT_Arrow component.")]
    public GameObject arrowPrefab;
    [Tooltip("Speed of fired arrows in world units per second (relative to the hero).")]
    public float arrowVelocity = 35f;
    [Tooltip("Damage each arrow deals on first hit.")]
    public float arrowDamage = 20f;
    [Tooltip("Arrows fired per second.")]
    public float fireRate = 1.5f;
    [Tooltip("Max random spread applied independently to pitch and yaw on each shot (degrees).")]
    public float aimOffsetDegrees = 3f;

    [Header("Rotation")]
    [Tooltip("How fast the hero rotates to face the lead-aim direction, in degrees per second.")]
    public float rotationSpeed = 270f;

    [Header("References")]
    [Tooltip("Arrow spawn point. Defaults to this transform if empty.")]
    public Transform firePoint;
    [Tooltip("Pegasus / player transform. Auto-finds the 'Player' tag if empty.")]
    public Transform pegasus;

    // ── private state ──────────────────────────────────────────────────────────

    private float   _fireCooldown;
    private Vector3 _prevPosition;
    private Vector3 _heroVelocity;

    // ──────────────────────────────────────────────────────────────────────────

    void Start()
    {
        if (pegasus == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null)
                pegasus = p.transform;
            else
                Debug.LogWarning("[SCRIPT_Hero] No GameObject tagged 'Player' found.", this);
        }

        if (firePoint == null)
            firePoint = transform;

        _prevPosition = transform.position;
    }

    void Update()
    {
        // Kinematic Rigidbodies don't expose meaningful velocity via rb.velocity,
        // so we diff world positions each frame instead.
        _heroVelocity = (transform.position - _prevPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        _prevPosition = transform.position;

        _fireCooldown -= Time.deltaTime;

        SCRIPT_EnemyBase target = FindNearestEnemy();
        if (target == null) return;

        Vector3 aimDir = ComputeLeadAim(target);

        // Rotate hero in world space (independent of the parent pegasus's rotation).
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.LookRotation(aimDir),
            rotationSpeed * Time.deltaTime);

        if (_fireCooldown <= 0f && arrowPrefab != null)
        {
            FireArrow(aimDir);
            _fireCooldown = 1f / Mathf.Max(fireRate, 0.01f);
        }
    }

    // ── targeting ──────────────────────────────────────────────────────────────

    SCRIPT_EnemyBase FindNearestEnemy()
    {
        SCRIPT_EnemyBase[] enemies = FindObjectsByType<SCRIPT_EnemyBase>(FindObjectsSortMode.None);

        float minSqr            = Mathf.Infinity;
        SCRIPT_EnemyBase nearest = null;

        foreach (SCRIPT_EnemyBase e in enemies)
        {
            float sqr = (e.transform.position - transform.position).sqrMagnitude;
            if (sqr < minSqr) { minSqr = sqr; nearest = e; }
        }

        return nearest;
    }

    Vector3 ComputeLeadAim(SCRIPT_EnemyBase target)
    {
        Vector3 d = target.transform.position - firePoint.position;

        // Enemy velocity: SCRIPT_EnemyBase always drives movement as
        //   rb.MovePosition(pos + transform.forward * flightSpeed * dt)
        // so forward * flightSpeed IS the enemy's actual world-space velocity.
        Vector3 targetVel = target.transform.forward * target.flightSpeed;

        // Treat arrow speed as relative to the hero (we'll add _heroVelocity back
        // in FireArrow so the arrow inherits the platform velocity). That means the
        // intercept equation uses the relative velocity between target and hero.
        Vector3 relVel = targetVel - _heroVelocity;

        float t = SolveInterceptTime(d, relVel, arrowVelocity);

        // (d + relVel*t) is the aim offset in the hero's reference frame.
        // Normalised it gives the direction to fire (in hero-local terms).
        return t > 0f ? (d + relVel * t).normalized : d.normalized;
    }

    /// <summary>
    /// Smallest positive time t at which a projectile travelling at <paramref name="speed"/>
    /// (relative) can reach an object at offset <paramref name="d"/> moving at
    /// relative velocity <paramref name="vel"/>. Returns -1 if no valid solution.
    /// </summary>
    static float SolveInterceptTime(Vector3 d, Vector3 vel, float speed)
    {
        // |d + vel*t|² = speed²*t²  →  at² + bt + c = 0
        float a = vel.sqrMagnitude - speed * speed;
        float b = 2f * Vector3.Dot(d, vel);
        float c = d.sqrMagnitude;

        // Degenerate: relative speed ≈ arrow speed — solve linearly.
        if (Mathf.Abs(a) < 0.0001f)
        {
            if (Mathf.Abs(b) < 0.0001f) return -1f;
            float tLin = -c / b;
            return tLin > 0f ? tLin : -1f;
        }

        float disc = b * b - 4f * a * c;
        if (disc < 0f) return -1f;

        float sq = Mathf.Sqrt(disc);
        float t1 = (-b - sq) / (2f * a);
        float t2 = (-b + sq) / (2f * a);

        if (t1 > 0f && t2 > 0f) return Mathf.Min(t1, t2);
        if (t1 > 0f)             return t1;
        if (t2 > 0f)             return t2;
        return -1f;
    }

    // ── firing ─────────────────────────────────────────────────────────────────

    void FireArrow(Vector3 baseDir)
    {
        // Apply spread in the arrow's LOCAL frame (relative to its travel direction)
        // so the cone is symmetric regardless of the aim angle.
        float yaw   = Random.Range(-aimOffsetDegrees, aimOffsetDegrees);
        float pitch = Random.Range(-aimOffsetDegrees, aimOffsetDegrees);
        Vector3 aimDir = Quaternion.LookRotation(baseDir)
                       * Quaternion.Euler(pitch, yaw, 0f)
                       * Vector3.forward;

        GameObject obj   = Instantiate(arrowPrefab, firePoint.position, Quaternion.LookRotation(aimDir));
        SCRIPT_Arrow arrow = obj.GetComponent<SCRIPT_Arrow>();
        if (arrow != null)
            // Pass hero velocity so the arrow's world-space launch matches the lead prediction.
            arrow.Init(arrowDamage, arrowVelocity, _heroVelocity);
    }
}
