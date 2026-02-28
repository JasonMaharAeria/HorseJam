using UnityEngine;

/// <summary>
/// Enemy Type 2 — Standoff Orbiter.
/// Maintains a configurable standoff distance from the player while circling them,
/// and fires projectiles at the player's predicted position.
///
/// Movement: GetDesiredHeading() blends a radial component (close/open range to
/// reach preferredDist) with a tangential component (orbit). Orbit direction is
/// randomised per instance so spawned pairs naturally flank from opposite sides.
///
/// Shooting: independent of movement heading — the enemy aims at the predicted
/// intercept point using a quadratic lead solve, so it can orbit sideways and
/// still track accurately.
///
/// Suggested starting values:
///   flightSpeed        = 18
///   trackingGain       = 2
///   maxTurnRate        = 80
///   steeringSmoothTime = 0.25
///   preferredDist      = 25
///   orbitStrength      = 0.6
///   fireRate           = 0.8
///   projectileSpeed    = 30
/// </summary>
public class SCRIPT_EnemyType2 : SCRIPT_EnemyBase
{
    [Header("Type 2: Standoff")]
    [Tooltip("Target standoff distance the enemy tries to maintain from the player.")]
    public float preferredDist = 25f;
    [Tooltip("Blend between pure range-correction (0) and pure orbit (1). " +
             "Values around 0.5–0.7 produce a natural spiralling orbit.")]
    [Range(0f, 1f)]
    public float orbitStrength = 0.6f;

    [Header("Type 2: Shooting")]
    [Tooltip("Projectile prefab to fire. Must have a SCRIPT_EnemyProjectile component.")]
    public GameObject projectilePrefab;
    [Tooltip("Spawn point for projectiles. Defaults to this transform if empty.")]
    public Transform  firePoint;
    [Tooltip("World-unit speed of fired projectiles.")]
    public float projectileSpeed  = 30f;
    [Tooltip("Damage dealt to the player per projectile hit.")]
    public float projectileDamage = 15f;
    [Tooltip("Shots fired per second.")]
    public float fireRate = 0.8f;
    [Tooltip("Max random spread applied independently to pitch and yaw on each shot (degrees).")]
    public float aimOffsetDegrees = 4f;
    [Tooltip("Lead-aim at where the player will be when the projectile arrives.")]
    public bool usePredictiveAim = true;

    // ── private state ──────────────────────────────────────────────────────────

    private float   _fireCooldown;
    private int     _orbitDir;        // +1 = CW, -1 = CCW, randomised on spawn

    private Vector3 _prevPlayerPos;
    private Vector3 _playerVelocity;

    // ──────────────────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        // Each instance picks a random orbit direction so paired enemies naturally flank.
        _orbitDir = Random.value > 0.5f ? 1 : -1;
    }

    protected override void Start()
    {
        base.Start();
        if (firePoint == null)
            firePoint = transform;
        if (playerTarget != null)
            _prevPlayerPos = playerTarget.position;
    }

    void Update()
    {
        _fireCooldown -= Time.deltaTime;

        if (playerTarget == null) return;

        // Estimate player velocity from frame-to-frame position delta.
        // Works even though the player Rigidbody is kinematic.
        if (Time.deltaTime > 0f)
            _playerVelocity = (playerTarget.position - _prevPlayerPos) / Time.deltaTime;
        _prevPlayerPos = playerTarget.position;

        if (_fireCooldown <= 0f && projectilePrefab != null)
        {
            FireProjectile();
            _fireCooldown = 1f / Mathf.Max(fireRate, 0.01f);
        }
    }

    // ── movement ───────────────────────────────────────────────────────────────

    protected override Vector3 GetDesiredHeading()
    {
        Vector3 toPlayer = playerTarget.position - transform.position;
        float dist = toPlayer.magnitude;
        if (dist < 0.001f) return transform.forward;

        Vector3 toPlayerDir = toPlayer / dist;

        // Radial: proportional controller driving distance error toward zero.
        // Clamped to [-1, 1] so it blends cleanly with the orbital component.
        float distError = (dist - preferredDist) / Mathf.Max(preferredDist, 1f);
        Vector3 radial  = toPlayerDir * Mathf.Clamp(distError, -1f, 1f);

        // Tangential: perpendicular to toPlayer in the horizontal plane.
        Vector3 tangent = Vector3.Cross(Vector3.up, toPlayerDir);
        if (tangent.sqrMagnitude < 0.001f)                   // guard near-vertical
            tangent = Vector3.Cross(Vector3.forward, toPlayerDir);
        tangent = tangent.normalized * _orbitDir;

        return (radial + tangent * orbitStrength).normalized;
    }

    // ── shooting ───────────────────────────────────────────────────────────────

    void FireProjectile()
    {
        Vector3 origin = firePoint.position;

        Vector3 aimTarget = usePredictiveAim
            ? PredictPosition(origin, playerTarget.position, _playerVelocity, projectileSpeed)
            : playerTarget.position;

        Vector3 baseDir = (aimTarget - origin).normalized;

        // Spread in the projectile's local frame for a symmetric cone.
        float yaw   = Random.Range(-aimOffsetDegrees, aimOffsetDegrees);
        float pitch = Random.Range(-aimOffsetDegrees, aimOffsetDegrees);
        Vector3 aimDir = Quaternion.LookRotation(baseDir)
                       * Quaternion.Euler(pitch, yaw, 0f)
                       * Vector3.forward;

        GameObject proj        = Instantiate(projectilePrefab, origin, Quaternion.LookRotation(aimDir));
        var        projScript  = proj.GetComponent<SCRIPT_EnemyProjectile>();
        if (projScript != null)
            projScript.Init(projectileDamage, projectileSpeed);
    }

    /// <summary>
    /// Returns the world-space position where a projectile travelling at
    /// <paramref name="projSpeed"/> will intercept the target.
    /// Falls back to the target's current position if no solution exists.
    /// </summary>
    static Vector3 PredictPosition(Vector3 shooterPos, Vector3 targetPos,
                                   Vector3 targetVel,  float   projSpeed)
    {
        Vector3 d = targetPos - shooterPos;
        float   a = targetVel.sqrMagnitude - projSpeed * projSpeed;
        float   b = 2f * Vector3.Dot(d, targetVel);
        float   c = d.sqrMagnitude;

        float t = -1f;

        if (Mathf.Abs(a) < 0.0001f)
        {
            if (Mathf.Abs(b) > 0.0001f) t = -c / b;
        }
        else
        {
            float disc = b * b - 4f * a * c;
            if (disc >= 0f)
            {
                float sq = Mathf.Sqrt(disc);
                float t1 = (-b - sq) / (2f * a);
                float t2 = (-b + sq) / (2f * a);
                if      (t1 > 0f && t2 > 0f) t = Mathf.Min(t1, t2);
                else if (t1 > 0f)             t = t1;
                else if (t2 > 0f)             t = t2;
            }
        }

        return t > 0f ? targetPos + targetVel * t : targetPos;
    }
}
