using UnityEngine;

/// <summary>
/// Abstract base for all enemy types. Handles kinematic flight physics that mirrors
/// the player controller — same MovePosition/MoveRotation approach, same exponential
/// smoothing, same SmoothDamp inertia on angular rates.
///
/// Child classes override GetDesiredHeading() to implement their pursuit strategy.
/// All flight parameters are tunable from the inspector.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public abstract class SCRIPT_EnemyBase : MonoBehaviour
{
    [Header("General")]
    [Tooltip("Health points. Enemy is destroyed when it reaches 0.")]
    public float health = 100f;
    [Tooltip("Damage dealt to the player on collision.")]
    public float damageOnCollision = 25f;

    [Header("Hit & Death Effects")]
    [Tooltip("Particle prefab spawned at the hit point when a laser particle connects.")]
    public GameObject hitParticlePrefab;
    [Tooltip("Particle prefab spawned at the enemy's position on death.")]
    public GameObject deathParticlePrefab;
    [Tooltip("Clip played at the hit position on each laser hit (rate-limited by hitSFXInterval).")]
    public AudioClip  hitSFX;
    [Tooltip("Clip played at the enemy's position on death.")]
    public AudioClip  deathSFX;
    [Tooltip("Minimum seconds between hit sound plays. Prevents audio spam from rapid-fire particles.")]
    public float      hitSFXInterval = 0.1f;

    // ── hit SFX rate limiter ───────────────────────────────────────────────────
    private float _nextHitSFXTime;

    [Header("Target")]
    [Tooltip("The transform to pursue. Auto-finds the GameObject tagged 'Player' if left empty.")]
    public Transform playerTarget;

    [Header("Flight Speed")]
    public float flightSpeed = 15f;

    [Header("Tracking — Proportional Controller")]
    [Tooltip("Gain on angular error (deg). Higher = snappier, more aggressive pursuit.")]
    public float trackingGain    = 2f;
    [Tooltip("Maximum turn rate in degrees per second (clamps the proportional output).")]
    public float maxTurnRate     = 90f;

    [Header("Rotational Inertia")]
    [Tooltip("Smooth time for SmoothDamp on yaw/pitch rates. Lower = snappier, higher = more inertia.")]
    public float steeringSmoothTime = 0.2f;

    [Header("Pitch Limits")]
    public float maxPitchAngle = 80f;

    [Header("Banking (visual roll)")]
    public bool  enableBanking = true;
    [Tooltip("Maximum bank angle in degrees reached at max turn rate.")]
    public float bankStrength  = 25f;
    [Tooltip("Exponential smoothing rate for banking. Higher = snappier roll.")]
    public float bankSmoothing = 5f;

    // ── shared state ───────────────────────────────────────────────────────────
    protected Rigidbody rb;

    protected float currentYaw;
    protected float currentPitch;
    protected float currentBank;

    private float yawRate,    yawRateVel;
    private float pitchRate,  pitchRateVel;

    // ──────────────────────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic   = true;
        rb.useGravity    = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        currentYaw   = transform.eulerAngles.y;
        currentPitch = transform.eulerAngles.x;
        if (currentPitch > 180f) currentPitch -= 360f;
    }

    protected virtual void Start()
    {
        if (playerTarget == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
                playerTarget = playerObj.transform;
            else
                Debug.LogWarning($"[{GetType().Name}] No GameObject with tag 'Player' found.", this);
        }
    }

    void FixedUpdate()
    {
        if (playerTarget == null) return;

        // ── ask child type for a desired world-space heading ───────────────────
        Vector3 targetDir = GetDesiredHeading();
        if (targetDir.sqrMagnitude < 0.001f) return;
        targetDir.Normalize();

        // ── target direction → desired yaw + pitch ─────────────────────────────
        float desiredYaw   =  Mathf.Atan2(targetDir.x, targetDir.z) * Mathf.Rad2Deg;
        float desiredPitch = -Mathf.Asin(Mathf.Clamp(targetDir.y, -1f, 1f)) * Mathf.Rad2Deg;

        // ── angular error → desired rates  (P-controller, deg/s) ───────────────
        float yawError   = Mathf.DeltaAngle(currentYaw,   desiredYaw);
        float pitchError = Mathf.DeltaAngle(currentPitch, desiredPitch);

        float desiredYawRate   = Mathf.Clamp(yawError   * trackingGain, -maxTurnRate, maxTurnRate);
        float desiredPitchRate = Mathf.Clamp(pitchError * trackingGain, -maxTurnRate, maxTurnRate);

        // ── smooth rates (inertia, mirrors player SmoothDamp approach) ─────────
        yawRate   = Mathf.SmoothDamp(yawRate,   desiredYawRate,   ref yawRateVel,
                                     steeringSmoothTime, Mathf.Infinity, Time.fixedDeltaTime);
        pitchRate = Mathf.SmoothDamp(pitchRate, desiredPitchRate, ref pitchRateVel,
                                     steeringSmoothTime, Mathf.Infinity, Time.fixedDeltaTime);

        // ── accumulate orientation ─────────────────────────────────────────────
        currentYaw   += yawRate   * Time.fixedDeltaTime;
        currentPitch += pitchRate * Time.fixedDeltaTime;
        currentPitch  = Mathf.Clamp(currentPitch, -maxPitchAngle, maxPitchAngle);

        // bank proportional to how hard we're turning (normalised to maxTurnRate)
        float turnFraction = maxTurnRate > 0f ? yawRate / maxTurnRate : 0f;
        float targetBank   = enableBanking ? -turnFraction * bankStrength : 0f;
        currentBank = Mathf.Lerp(currentBank, targetBank,
                                 1f - Mathf.Exp(-bankSmoothing * Time.fixedDeltaTime));

        Quaternion newRotation = Quaternion.Euler(currentPitch, currentYaw, currentBank);

        // ── apply (same as player: kinematic MovePosition + MoveRotation) ──────
        Vector3 newPosition = rb.position
                            + newRotation * Vector3.forward * flightSpeed * Time.fixedDeltaTime;

        rb.MovePosition(newPosition);
        rb.MoveRotation(newRotation);
    }

    /// <summary>
    /// Called by the laser controller for each particle that intersects this enemy.
    /// Spawns a hit VFX at the contact point, plays a rate-limited hit SFX,
    /// reduces health by <paramref name="damage"/>, and triggers death if HP hits 0.
    /// </summary>
    public void TakeDamage(float damage, Vector3 hitPosition)
    {
        health -= damage;

        if (hitParticlePrefab != null)
            Instantiate(hitParticlePrefab, hitPosition, Quaternion.identity);

        if (hitSFX != null && Time.time >= _nextHitSFXTime)
        {
            AudioSource.PlayClipAtPoint(hitSFX, hitPosition);
            _nextHitSFXTime = Time.time + hitSFXInterval;
        }

        if (health <= 0f)
            Die();
    }

    private void Die()
    {
        if (deathParticlePrefab != null)
            Instantiate(deathParticlePrefab, transform.position, Quaternion.identity);

        if (deathSFX != null)
            AudioSource.PlayClipAtPoint(deathSFX, transform.position);

        Destroy(gameObject);
    }

    /// <summary>
    /// Returns the world-space direction this enemy wants to fly toward this frame.
    /// Override in each child type to implement a different pursuit strategy.
    /// The vector does not need to be pre-normalised.
    /// </summary>
    protected virtual Vector3 GetDesiredHeading()
    {
        return playerTarget.position - transform.position;
    }
}
