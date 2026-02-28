using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Abstract base for all enemy types. Handles kinematic flight physics that mirrors
/// the player controller — same MovePosition/MoveRotation approach, same exponential
/// smoothing, same SmoothDamp inertia on angular rates.
///
/// Child classes override GetDesiredHeading() to implement their pursuit flavor.
/// The base class drives a state machine on top: enemies randomly strafe, pitch, flee,
/// and evade rather than always tracking directly. Parameters vary per-instance at spawn.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public abstract class SCRIPT_EnemyBase : MonoBehaviour
{
    // ── enums ──────────────────────────────────────────────────────────────────

    protected enum FlightState
    {
        Pursuing,     // standard: use GetDesiredHeading()
        Strafing,     // veer left or right while generally facing the player
        Climbing,     // pitch up while generally facing the player
        Diving,       // pitch down while generally facing the player
        Fleeing,      // fly straight (no tracking) — escape wind-up
        Evading,      // sharp diagonal break — escape maneuver
        DashStriking  // subclass strike mode: boosted speed/tracking, flies directly at player
    }

    // ── inspector ──────────────────────────────────────────────────────────────

    [Header("General")]
    [Tooltip("Health points. Enemy is destroyed when it reaches 0.")]
    public float health = 100f;
    [Tooltip("Damage dealt to the player on collision.")]
    public float damageOnCollision = 25f;

    [Header("Hit & Death Effects")]
    [Tooltip("Particle prefab instantiated at the hit position on each laser hit.")]
    public GameObject hitParticlePrefab;
    [Tooltip("Particle prefab spawned at the enemy's position on death.")]
    public GameObject deathParticlePrefab;
    [Tooltip("Clip played at full volume on each laser hit (played at listener position, rate-limited by hitSFXInterval).")]
    public AudioClip  hitSFX;
    [Tooltip("Clip played at the enemy's position on death.")]
    public AudioClip  deathSFX;
    [Tooltip("Minimum seconds between hit sound plays. Prevents audio spam from rapid-fire particles.")]
    public float      hitSFXInterval = 0.1f;

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

    [Header("Parameter Randomization")]
    [Tooltip("On spawn, flightSpeed is offset by a random value in ±this range.")]
    public float   speedVariance          = 3f;
    [Tooltip("On spawn, trackingGain is multiplied by a random factor in [x, y].")]
    public Vector2 trackingGainVariance   = new Vector2(0.8f, 1.2f);

    [Header("Behavior — Wander Timing")]
    [Tooltip("While pursuing, a random maneuver triggers after this many seconds (min, max).")]
    public Vector2 pursueInterruptInterval = new Vector2(3f, 7f);

    [Header("Behavior — Strafe")]
    [Tooltip("How long a strafe lasts (min, max) in seconds.")]
    public Vector2 strafeDuration  = new Vector2(1.5f, 3f);
    [Tooltip("Lateral bias strength. 1 = perpendicular to player, 0 = no strafe.")]
    [Range(0f, 2f)]
    public float   strafeStrength  = 1f;

    [Header("Behavior — Pitch (Climb / Dive)")]
    [Tooltip("How long a climb or dive lasts (min, max) in seconds.")]
    public Vector2 pitchDuration   = new Vector2(1f, 2.5f);
    [Tooltip("Vertical bias strength. 1 = 45° bias, higher = steeper.")]
    [Range(0f, 2f)]
    public float   pitchStrength   = 0.8f;

    [Header("Behavior — Escape")]
    [Tooltip("Dot product of forward vs. to-player below which an escape triggers. " +
             "Negative values mean 'player is behind': -0.3 ≈ 107°, -1 = directly behind.")]
    [Range(-1f, 0f)]
    public float   escapeTriggerDot   = -0.3f;
    [Tooltip("Fly straight for this many seconds before executing the escape maneuver (min, max).")]
    public Vector2 fleeDuration       = new Vector2(1f, 2.5f);
    [Tooltip("Hold the escape turn for this many seconds (min, max).")]
    public Vector2 evadeDuration      = new Vector2(1.5f, 3f);
    [Tooltip("Seconds before the escape sequence can trigger again after completing one.")]
    public float   escapeCooldown     = 5f;

    // ── runtime state (visible in inspector for live debugging) ───────────────

    [Header("Runtime State (read-only)")]
    [SerializeField] protected FlightState _state            = FlightState.Pursuing;
    [SerializeField] private float       _stateTimeRemaining = 0f;
    [SerializeField] private float       _pursueTimer        = 0f;
    [SerializeField] private float       _escapeCooldown     = 0f;

    // ── private physics state ──────────────────────────────────────────────────
    protected Rigidbody rb;

    protected float currentYaw;
    protected float currentPitch;
    protected float currentBank;

    private float yawRate,   yawRateVel;
    private float pitchRate, pitchRateVel;

    // ── private behavior state ─────────────────────────────────────────────────
    private float   _nextHitSFXTime;
    private int     _strafeDir;   // +1 right, -1 left
    private Vector3 _evadeDir;    // world-space evade heading, set on entering Evading

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

        // Randomise per-instance parameters so enemies feel distinct.
        if (speedVariance > 0f)
            flightSpeed = Mathf.Max(1f, flightSpeed + Random.Range(-speedVariance, speedVariance));

        if (trackingGainVariance.x < trackingGainVariance.y)
            trackingGain *= Random.Range(trackingGainVariance.x, trackingGainVariance.y);
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

        // Start in pursuit with a staggered first interrupt so spawned enemies
        // don't all change state simultaneously.
        EnterPursuing();
    }

    // ── main loop ──────────────────────────────────────────────────────────────

    void FixedUpdate()
    {
        if (playerTarget == null) return;

        UpdateBehaviorState();

        // ── get heading from current state ─────────────────────────────────────
        Vector3 targetDir = GetStateHeading();
        if (targetDir.sqrMagnitude < 0.001f) return;
        targetDir.Normalize();

        // ── target direction → desired yaw + pitch ─────────────────────────────
        float desiredYaw   =  Mathf.Atan2(targetDir.x, targetDir.z) * Mathf.Rad2Deg;
        float desiredPitch = -Mathf.Asin(Mathf.Clamp(targetDir.y, -1f, 1f)) * Mathf.Rad2Deg;

        // ── angular error → desired rates (P-controller, deg/s) ────────────────
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

        float turnFraction = maxTurnRate > 0f ? yawRate / maxTurnRate : 0f;
        float targetBank   = enableBanking ? -turnFraction * bankStrength : 0f;
        currentBank = Mathf.Lerp(currentBank, targetBank,
                                 1f - Mathf.Exp(-bankSmoothing * Time.fixedDeltaTime));

        Quaternion newRotation = Quaternion.Euler(currentPitch, currentYaw, currentBank);

        // ── apply ──────────────────────────────────────────────────────────────
        Vector3 newPosition = rb.position
                            + newRotation * Vector3.forward * flightSpeed * Time.fixedDeltaTime;

        rb.MovePosition(newPosition);
        rb.MoveRotation(newRotation);
    }

    // ── state machine ──────────────────────────────────────────────────────────

    void UpdateBehaviorState()
    {
        _escapeCooldown     = Mathf.Max(0f, _escapeCooldown - Time.fixedDeltaTime);
        _stateTimeRemaining = Mathf.Max(0f, _stateTimeRemaining - Time.fixedDeltaTime);
        _pursueTimer        = Mathf.Max(0f, _pursueTimer - Time.fixedDeltaTime);

        switch (_state)
        {
            case FlightState.Pursuing:
                // Random wander interrupt
                if (_pursueTimer <= 0f)
                    EnterRandomManeuver();

                // Escape trigger: player is behind us and cooldown has expired
                if (_escapeCooldown <= 0f)
                {
                    Vector3 toPlayer = (playerTarget.position - transform.position).normalized;
                    if (Vector3.Dot(transform.forward, toPlayer) < escapeTriggerDot)
                        EnterFleeing();
                }
                break;

            case FlightState.Strafing:
            case FlightState.Climbing:
            case FlightState.Diving:
                if (_stateTimeRemaining <= 0f)
                    EnterPursuing();
                break;

            case FlightState.Fleeing:
                if (_stateTimeRemaining <= 0f)
                    EnterEvading();
                break;

            case FlightState.Evading:
                if (_stateTimeRemaining <= 0f)
                {
                    _escapeCooldown = escapeCooldown;
                    EnterPursuing();
                }
                break;

            case FlightState.DashStriking:
                if (_stateTimeRemaining <= 0f)
                    ExitDashStriking();
                break;
        }
    }

    protected void EnterPursuing()
    {
        _state       = FlightState.Pursuing;
        _pursueTimer = Random.Range(pursueInterruptInterval.x, pursueInterruptInterval.y);
    }

    protected void EnterDashStriking(float duration)
    {
        _state              = FlightState.DashStriking;
        _stateTimeRemaining = duration;
    }

    /// <summary>Called when the DashStriking timer expires. Override to restore boosted
    /// stats before calling base, which returns the enemy to Pursuing.</summary>
    protected virtual void ExitDashStriking() => EnterPursuing();

    /// <summary>World-space heading used while DashStriking. Defaults to direct pursuit.</summary>
    protected virtual Vector3 GetDashStrikeHeading() => GetDesiredHeading();

    void EnterRandomManeuver()
    {
        switch (Random.Range(0, 3))
        {
            case 0: EnterStrafing(); break;
            case 1: EnterClimbing(); break;
            case 2: EnterDiving();   break;
        }
    }

    void EnterStrafing()
    {
        _state              = FlightState.Strafing;
        _strafeDir          = Random.value > 0.5f ? 1 : -1;
        _stateTimeRemaining = Random.Range(strafeDuration.x, strafeDuration.y);
    }

    void EnterClimbing()
    {
        _state              = FlightState.Climbing;
        _stateTimeRemaining = Random.Range(pitchDuration.x, pitchDuration.y);
    }

    void EnterDiving()
    {
        _state              = FlightState.Diving;
        _stateTimeRemaining = Random.Range(pitchDuration.x, pitchDuration.y);
    }

    void EnterFleeing()
    {
        _state              = FlightState.Fleeing;
        _stateTimeRemaining = Random.Range(fleeDuration.x, fleeDuration.y);
    }

    void EnterEvading()
    {
        _state = FlightState.Evading;

        // Diagonal break from current forward: sharp lateral turn with optional vertical.
        float lateral  = Random.value > 0.5f ? 1f : -1f;
        float vertical = Random.Range(-0.5f, 0.5f);
        _evadeDir = (transform.forward
                   + transform.right * lateral
                   + Vector3.up      * vertical).normalized;

        _stateTimeRemaining = Random.Range(evadeDuration.x, evadeDuration.y);
    }

    // ── heading per state ──────────────────────────────────────────────────────

    Vector3 GetStateHeading()
    {
        Vector3 toPlayer = playerTarget != null
                         ? (playerTarget.position - transform.position).normalized
                         : transform.forward;

        switch (_state)
        {
            case FlightState.Pursuing:
                return GetDesiredHeading();

            case FlightState.Strafing:
                Vector3 strafeRight = Vector3.Cross(Vector3.up, toPlayer);
                if (strafeRight.sqrMagnitude < 0.001f)
                    strafeRight = Vector3.Cross(Vector3.forward, toPlayer);
                strafeRight.Normalize();
                return (toPlayer + strafeRight * _strafeDir * strafeStrength).normalized;

            case FlightState.Climbing:
                return (toPlayer + Vector3.up   * pitchStrength).normalized;

            case FlightState.Diving:
                return (toPlayer + Vector3.down * pitchStrength).normalized;

            case FlightState.Fleeing:
                return transform.forward;   // hold current heading, no tracking

            case FlightState.Evading:
                return _evadeDir;

            case FlightState.DashStriking:
                return GetDashStrikeHeading();

            default:
                return GetDesiredHeading();
        }
    }

    // ── damage & death ─────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the laser relay for each particle that intersects this enemy.
    /// </summary>
    public void TakeDamage(float damage, Vector3 hitPosition)
    {
        health -= damage;

        if (hitParticlePrefab != null)
            Instantiate(hitParticlePrefab, hitPosition, Quaternion.identity);

        if (hitSFX != null && Time.time >= _nextHitSFXTime)
        {
            // Play at the listener position so volume is unaffected by distance to impact.
            // Routes through the SFX mixer group so the settings menu volume slider works.
            Vector3 listenerPos = Camera.main != null ? Camera.main.transform.position : hitPosition;
            AudioMixerGroup sfx = SCRIPT_AudioManager.Instance != null
                                      ? SCRIPT_AudioManager.Instance.sfxGroup : null;
            SCRIPT_AudioManager.PlayClipAtPoint(hitSFX, listenerPos, sfx);
            _nextHitSFXTime = Time.time + hitSFXInterval;
        }

        if (health <= 0f)
            Die();
    }

    private bool _dead;
    private void Die()
    {
        if (_dead) return;
        _dead = true;

        if (deathParticlePrefab != null)
            Instantiate(deathParticlePrefab, transform.position, Quaternion.identity);

        if (deathSFX != null)
        {
            AudioMixerGroup sfx = SCRIPT_AudioManager.Instance != null
                                      ? SCRIPT_AudioManager.Instance.sfxGroup : null;
            SCRIPT_AudioManager.PlayClipAtPoint(deathSFX, transform.position, sfx);
        }

        Destroy(gameObject);
    }

    // ── child override ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the world-space direction this enemy wants to fly toward when in the
    /// Pursuing state. Override in each child type to implement a different pursuit
    /// flavor (pure pursuit, lead pursuit, erratic, etc.).
    /// </summary>
    protected virtual Vector3 GetDesiredHeading()
    {
        return playerTarget.position - transform.position;
    }
}
