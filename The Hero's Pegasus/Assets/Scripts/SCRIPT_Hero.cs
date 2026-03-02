using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Controls the hero sitting on top of the pegasus.
///
/// ROTATION:
///   The hero root always matches the pegasus orientation so the lower body stays
///   seated correctly. If an upperBodyBone is assigned, that bone is rotated in
///   LateUpdate to independently aim at the nearest enemy. If no upper-body bone
///   is set the whole hero rotates toward the target (legacy behaviour).
///
/// ANIMATION:
///   If a heroAnimator is assigned the script drives a bow-attack state machine:
///     DoAttack (trigger) → BowDraw → BowAim → BowFire
///   The arrow spawns when the BowFire state reaches arrowFireNormalizedTime (0.75).
///   heroAnimator.SetFloat("AttackSpeed", fireRate / referenceFireRate) keeps the
///   animation speed in sync with the actual fire rate.
///   Without an animator the legacy timer-based arrow spawn is used instead.
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
    [Tooltip("Degrees per second the hero (or upper-body bone) rotates toward the aim direction.")]
    public float rotationSpeed = 270f;

    [Header("References")]
    [Tooltip("Arrow spawn point. Defaults to this transform if empty.")]
    public Transform firePoint;
    [Tooltip("Pegasus / player transform. Auto-finds the 'Player' tag if empty.")]
    public Transform pegasus;

    [Header("Animation")]
    [Tooltip("Animator on the hero model. Leave null to use timer-based firing instead.")]
    public Animator heroAnimator;
    [Tooltip("Spine or chest bone to rotate for upper-body aiming. " +
             "Leave null to rotate the whole hero toward the target.")]
    public Transform upperBodyBone;
    [Tooltip("Normalized time (0–1) within the BowFire state at which the arrow actually spawns.")]
    [Range(0f, 1f)]
    public float arrowFireNormalizedTime = 0.75f;
    [Tooltip("Names of each Animator fire state (one per clip variation). " +
             "A random one is selected each shot. e.g. BowFire_01, BowFire_02, BowFire_03")]
    public string[] fireStateNames = { "BowFire_01", "BowFire_02", "BowFire_03" };
    [Tooltip("Animator layer index to monitor for attack states.")]
    public int animatorLayer = 1;
    [Tooltip("Fire rate at which the attack animations were authored. " +
             "AttackSpeed = fireRate / referenceFireRate is sent to the Animator each frame.")]
    public float referenceFireRate = 1f;
    [Tooltip("Name of the Animator trigger that kicks off an attack cycle.")]
    public string attackTriggerName = "DoAttack";
    [Tooltip("Name of the Animator int parameter that selects which fire variation plays (0, 1, 2…).")]
    public string attackVariantParam = "AttackVariant";
    [Tooltip("Name of the Animator float parameter used to scale animation speed.")]
    public string attackSpeedParam = "AttackSpeed";

    [Header("Audio")]
    [Tooltip("Played at the fire point each time an arrow is released.")]
    public AudioClip bowReleaseSFX;
    [Tooltip("Volume multiplier for the bow release SFX.")]
    [Range(0f, 1f)]
    public float bowReleaseVolume = 1f;

    [Header("Upper-Body Aim")]
    [Tooltip("Maximum left/right swivel from the bind pose (degrees).")]
    public float upperBodyYawLimit = 70f;
    [Tooltip("Maximum upward swivel from the bind pose (degrees).")]
    public float upperBodyPitchUpLimit = 45f;
    [Tooltip("Maximum downward swivel from the bind pose (degrees).")]
    public float upperBodyPitchDownLimit = 35f;
    [Tooltip("How quickly the upper body approaches the target pose. Higher = snappier.")]
    public float upperBodyAimSmoothing = 14f;

    // ── computed ───────────────────────────────────────────────────────────────

    // Base fireRate scaled by any AttackSpeedIncrease upgrades collected so far.
    float EffectiveFireRate => fireRate *
        (SCRIPT_PlayerStats.Instance != null ? SCRIPT_PlayerStats.Instance.AttackSpeedMultiplier : 1f);

    // ── private state ──────────────────────────────────────────────────────────

    private float            _fireCooldown;
    private Vector3          _prevPosition;
    private Vector3          _heroVelocity;
    private SCRIPT_EnemyBase _currentTarget;
    private Vector3          _currentAimDir;
    private bool             _arrowFiredThisCycle;
    private bool             _upperBodyAimInitialized;
    private Quaternion       _upperBodyBaseLocalRotation;
    private Quaternion       _upperBodySmoothedLocalRotation;

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

        _prevPosition  = transform.position;
        _currentAimDir = transform.forward;

        InitializeUpperBodyAim();
    }

    void Update()
    {
        // Kinematic Rigidbodies don't expose meaningful rb.velocity so we diff positions.
        _heroVelocity = (transform.position - _prevPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        _prevPosition = transform.position;

        _fireCooldown -= Time.deltaTime;

        _currentTarget = FindNearestEnemy();
        if (_currentTarget != null)
            _currentAimDir = ComputeLeadAim(_currentTarget);

        // ── Root rotation ──────────────────────────────────────────────────────
        if (upperBodyBone != null && pegasus != null)
        {
            // Lower body locked to the pegasus — legs stay seated correctly.
            // Upper-body aiming happens in LateUpdate via the bone.
            transform.rotation = pegasus.rotation;
        }
        else
        {
            // No split: rotate the whole hero toward the aim direction (legacy).
            if (_currentTarget != null)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(_currentAimDir),
                    rotationSpeed * Time.deltaTime);
            }
            else if (pegasus != null)
            {
                // Drift back to match the pegasus when there is no target.
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    pegasus.rotation,
                    rotationSpeed * Time.deltaTime);
            }
        }

        // ── Attack ─────────────────────────────────────────────────────────────
        if (_currentTarget == null) return;

        if (heroAnimator != null)
            UpdateAnimatedAttack();
        else
            UpdateDirectAttack();
    }

    void LateUpdate()
    {
        // This runs after animation writes its pose, so this always wins for aiming.
        if (upperBodyBone == null) return;
        if (!_upperBodyAimInitialized) InitializeUpperBodyAim();

        Quaternion desiredLocal = _upperBodyBaseLocalRotation;

        if (_currentTarget != null)
        {
            Transform parent = upperBodyBone.parent != null ? upperBodyBone.parent : transform;
            Vector3 localAimDir = parent.InverseTransformDirection(_currentAimDir);
            if (localAimDir.sqrMagnitude > 0.0001f)
                desiredLocal = ComputeClampedUpperBodyLocalRotation(localAimDir.normalized);
        }

        float t = upperBodyAimSmoothing > 0f
            ? 1f - Mathf.Exp(-upperBodyAimSmoothing * Time.deltaTime)
            : 1f;

        _upperBodySmoothedLocalRotation = Quaternion.Slerp(
            _upperBodySmoothedLocalRotation,
            desiredLocal,
            t);
        upperBodyBone.localRotation = _upperBodySmoothedLocalRotation;
    }

    void InitializeUpperBodyAim()
    {
        if (upperBodyBone == null) return;
        _upperBodyBaseLocalRotation = upperBodyBone.localRotation;
        _upperBodySmoothedLocalRotation = _upperBodyBaseLocalRotation;
        _upperBodyAimInitialized = true;
    }

    Quaternion ComputeClampedUpperBodyLocalRotation(Vector3 localAimDir)
    {
        Vector3 baseForward = _upperBodyBaseLocalRotation * Vector3.forward;
        Vector3 baseUp = _upperBodyBaseLocalRotation * Vector3.up;
        Vector3 baseRight = _upperBodyBaseLocalRotation * Vector3.right;

        Vector3 yawFrom = Vector3.ProjectOnPlane(baseForward, baseUp);
        Vector3 yawTo = Vector3.ProjectOnPlane(localAimDir, baseUp);
        if (yawFrom.sqrMagnitude < 0.0001f || yawTo.sqrMagnitude < 0.0001f)
            yawTo = yawFrom;

        float yaw = Vector3.SignedAngle(yawFrom, yawTo, baseUp);
        yaw = Mathf.Clamp(yaw, -upperBodyYawLimit, upperBodyYawLimit);

        Quaternion yawRot = Quaternion.AngleAxis(yaw, baseUp);
        Vector3 yawedForward = yawRot * baseForward;

        Vector3 pitchFrom = Vector3.ProjectOnPlane(yawedForward, baseRight);
        Vector3 pitchTo = Vector3.ProjectOnPlane(localAimDir, baseRight);
        if (pitchFrom.sqrMagnitude < 0.0001f || pitchTo.sqrMagnitude < 0.0001f)
            pitchTo = pitchFrom;

        float pitch = Vector3.SignedAngle(pitchFrom, pitchTo, baseRight);
        pitch = Mathf.Clamp(pitch, -upperBodyPitchDownLimit, upperBodyPitchUpLimit);

        Quaternion pitchRot = Quaternion.AngleAxis(pitch, baseRight);
        return pitchRot * yawRot * _upperBodyBaseLocalRotation;
    }

    // ── Animation-driven attack ────────────────────────────────────────────────

    void UpdateAnimatedAttack()
    {
        // Keep animation speed proportional to the desired fire rate.
        heroAnimator.SetFloat(attackSpeedParam, EffectiveFireRate / Mathf.Max(referenceFireRate, 0.01f));

        // Check if any fire variation state has reached the arrow-release point.
        AnimatorStateInfo state = heroAnimator.GetCurrentAnimatorStateInfo(animatorLayer);
        if (!_arrowFiredThisCycle && state.normalizedTime >= arrowFireNormalizedTime
            && IsInFireState(state))
        {
            FireArrow(_currentAimDir);
            _arrowFiredThisCycle = true;
        }

        // Trigger a new attack cycle at the configured fire rate.
        // Pick a random variation, then set the trigger.
        // Resetting _arrowFiredThisCycle here clears it for the incoming cycle.
        if (_fireCooldown <= 0f && arrowPrefab != null && fireStateNames.Length > 0)
        {
            int variant = Random.Range(0, fireStateNames.Length);
            heroAnimator.SetInteger(attackVariantParam, variant);
            heroAnimator.SetTrigger(attackTriggerName);
            _arrowFiredThisCycle = false;
            _fireCooldown = 1f / Mathf.Max(EffectiveFireRate, 0.01f);
        }
    }

    bool IsInFireState(AnimatorStateInfo state)
    {
        foreach (string name in fireStateNames)
            if (state.IsName(name)) return true;
        return false;
    }

    // ── Timer-driven attack (no animator) ─────────────────────────────────────

    void UpdateDirectAttack()
    {
        if (_fireCooldown <= 0f && arrowPrefab != null)
        {
            FireArrow(_currentAimDir);
            _fireCooldown = 1f / Mathf.Max(EffectiveFireRate, 0.01f);
        }
    }

    // ── Targeting ─────────────────────────────────────────────────────────────

    SCRIPT_EnemyBase FindNearestEnemy()
    {
        SCRIPT_EnemyBase[] enemies = FindObjectsByType<SCRIPT_EnemyBase>(FindObjectsSortMode.None);

        float minSqr             = Mathf.Infinity;
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

        // Enemy velocity: SCRIPT_EnemyBase drives movement via rb.MovePosition so
        // forward * flightSpeed is the actual world-space velocity.
        Vector3 targetVel = target.transform.forward * target.flightSpeed;

        // Arrow speed is relative to the hero; add _heroVelocity back in FireArrow.
        Vector3 relVel = targetVel - _heroVelocity;
        float   t      = SolveInterceptTime(d, relVel, arrowVelocity);

        return t > 0f ? (d + relVel * t).normalized : d.normalized;
    }

    /// <summary>
    /// Smallest positive time t at which a projectile at speed <paramref name="speed"/>
    /// (relative) can reach an object at offset <paramref name="d"/> moving at relative
    /// velocity <paramref name="vel"/>. Returns -1 if no valid solution exists.
    /// </summary>
    static float SolveInterceptTime(Vector3 d, Vector3 vel, float speed)
    {
        // |d + vel*t|² = speed²*t²  →  at² + bt + c = 0
        float a = vel.sqrMagnitude - speed * speed;
        float b = 2f * Vector3.Dot(d, vel);
        float c = d.sqrMagnitude;

        if (Mathf.Abs(a) < 0.0001f)
        {
            if (Mathf.Abs(b) < 0.0001f) return -1f;
            float tLin = -c / b;
            return tLin > 0f ? tLin : -1f;
        }

        float disc = b * b - 4f * a * c;
        if (disc < 0f) return -1f;

        float sq = Mathf.Sqrt(disc);
        float t1  = (-b - sq) / (2f * a);
        float t2  = (-b + sq) / (2f * a);

        if (t1 > 0f && t2 > 0f) return Mathf.Min(t1, t2);
        if (t1 > 0f)             return t1;
        if (t2 > 0f)             return t2;
        return -1f;
    }

    // ── Firing ─────────────────────────────────────────────────────────────────

    void FireArrow(Vector3 baseDir)
    {
        float   yaw    = Random.Range(-aimOffsetDegrees, aimOffsetDegrees);
        float   pitch  = Random.Range(-aimOffsetDegrees, aimOffsetDegrees);
        Vector3 aimDir = Quaternion.LookRotation(baseDir)
                       * Quaternion.Euler(pitch, yaw, 0f)
                       * Vector3.forward;

        if (bowReleaseSFX != null)
        {
            AudioMixerGroup sfx = SCRIPT_AudioManager.Instance != null
                                      ? SCRIPT_AudioManager.Instance.sfxGroup : null;
            SCRIPT_AudioManager.PlayClipAtPoint(bowReleaseSFX, firePoint.position, sfx, bowReleaseVolume);
        }

        GameObject   obj   = Instantiate(arrowPrefab, firePoint.position, Quaternion.LookRotation(aimDir));

        // Apply any accumulated arrow-size upgrades.
        float sizeMultiplier = SCRIPT_PlayerStats.Instance != null
                             ? SCRIPT_PlayerStats.Instance.ArrowSizeMultiplier
                             : 1f;
        if (sizeMultiplier != 1f)
            obj.transform.localScale *= sizeMultiplier;

        SCRIPT_Arrow arrow = obj.GetComponent<SCRIPT_Arrow>();
        if (arrow != null)
        {
            float damageMultiplier   = SCRIPT_PlayerStats.Instance != null
                                     ? SCRIPT_PlayerStats.Instance.ArrowDamageMultiplier   : 1f;
            float velocityMultiplier = SCRIPT_PlayerStats.Instance != null
                                     ? SCRIPT_PlayerStats.Instance.ArrowVelocityMultiplier : 1f;
            arrow.Init(arrowDamage * damageMultiplier, arrowVelocity * velocityMultiplier, _heroVelocity);
        }
    }
}
