using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;           // Volume
using UnityEngine.Rendering.Universal; // LensDistortion, Vignette  (requires URP)

// Rigidbody is configured in code — nothing to set in Inspector.
[RequireComponent(typeof(Rigidbody))]
public class SCRIPT_PlayerMovementController : MonoBehaviour
{
    [Header("Flight Speed")]
    public float flightSpeed = 20f;

    [Header("Dash")]
    public float      dashSpeed      = 60f;
    public float      dashRampUp     = 8f;   // exponential ramp rate toward dashSpeed
    public float      dashRampDown   = 3f;   // exponential ramp rate back to flightSpeed
    public GameObject dashParticles;          // activated on RMB press, deactivated on release

    [Header("Gravity Influence")]
    [Tooltip("Extra speed added when flying straight down (scales with how directly downward you point)")]
    public float gravitySpeedBonus = 10f;

    [Header("Steering Sensitivity")]
    public float yawSensitivity   = 0.1f;
    public float pitchSensitivity = 0.1f;
    public float maxPitchAngle    = 80f;

    [Header("Rotational Inertia")]
    [Tooltip("How quickly the pegasus responds to steering. Lower = more inertia / smoother.")]
    public float steeringSmoothTime = 0.12f;

    [Header("Speed-Based Maneuverability")]
    [Tooltip("Fraction of base sensitivity at full speed (e.g. 0.15 = 15% as responsive)")]
    public float minTurnMultiplier = 0.15f;
    [Tooltip("Speed at which minimum turn rate is reached")]
    public float referenceSpeed    = 30f;

    [Header("Banking (visual roll)")]
    public bool  enableBanking = true;
    public float bankStrength  = 30f;
    public float bankSmoothing = 5f;

    [Header("Dash Post-Processing")]
    [Tooltip("Leave empty to auto-find the first Volume in the scene")]
    public Volume globalVolume;
    public float dashLensDistortionIntensity = -0.75f;
    public float dashVignetteIntensity       =  0.5f;
    [Tooltip("How fast the PP effects blend in/out")]
    public float ppBlendSpeed = 6f;

    [Header("Circle Flip — Detection")]
    [Tooltip("Max seconds to complete the circle gesture before it resets")]
    public float circleWindowSeconds  = 1.35f;
    [Tooltip("Minimum mouse speed (px/s) to count toward the circle gesture")]
    public float circleMinMouseSpeed  = 70f;
    [Tooltip("Total degrees the mouse direction must rotate to trigger (~345 allows 15° slop)")]
    public float circleAngleThreshold = 345f;
    [Tooltip("Degrees of momentary reversal allowed before the gesture resets")]
    public float circleReverseAllowed = 25f;
    [Tooltip("Minimum width and height (px) of traced motion bounds to count as a real circle")]
    public float circleMinDiameter    = 120f;
    [Tooltip("Minimum total traced distance (px) required before a circle can trigger")]
    public float circleMinPathLength  = 420f;

    [Header("Health")]
    public float maxHealth = 100f;
    [SerializeField] private float _currentHealth;

    /// <summary>Current health, read-only from outside.</summary>
    public float CurrentHealth => _currentHealth;
    public bool  IsAlive       => _currentHealth > 0f;

    [Header("Dash Attack")]
    [Tooltip("Enemies within this radius are instantly destroyed while dashing or bursting. " +
             "Tune to roughly match the pegasus's visual size.")]
    public float dashKillRadius = 3f;

    /// <summary>True while RMB dash is held or the post-flip burst is active.
    /// Used for damage immunity and dash-kill detection.</summary>
    public bool IsDashing { get; private set; }

    [Header("Circle Flip — Maneuver")]
    [Tooltip("Seconds for phase 1: vertical inversion to upside-down")]
    public float flipVerticalDuration   = 0.20f;
    [Tooltip("Seconds for phase 2: horizontal turnaround back to right-side-up")]
    public float flipHorizontalDuration = 0.24f;
    [Tooltip("Fraction of entry speed reached by the end of the maneuver before burst")]
    [Range(0.1f, 1f)]
    public float flipSlowMultiplier     = 0.35f;
    [Tooltip("Speed applied immediately upon and during the burst")]
    public float burstSpeed             = 90f;
    [Tooltip("How long the burst speed holds after the flip completes")]
    public float burstDurationSeconds   = 0.4f;
    [Tooltip("Minimum seconds between flip triggers")]
    public float flipCooldownSeconds  = 2.0f;
    public GameObject flipParticles;



    [Header("HUD")]
    [Tooltip("Health points. Player is killed when it reaches 0.")]
    public float health = 1000f;

    public SCRIPT_StaminaBar staminaBar;


    // ── private state ──────────────────────────────────────────────────────────
    private Rigidbody rb;

    private float currentYaw;
    private float currentPitch;
    private float currentBank;

    private float yawRate,   yawRateVel;
    private float pitchRate, pitchRateVel;

    private Vector2 pendingMouseDelta;

    // currentSpeed is driven by dash input and gravity; used for both movement
    // and for feeding the speed-based maneuverability calculation.
    private float currentSpeed;

    // Dash particles
    private ParticleSystem dashPs;

    // Post-processing
    private LensDistortion lensDistortion;
    private Vignette       vignette;
    private float          baseLensDistortion;
    private float          baseVignette;

    // ── circle gesture detection ───────────────────────────────────────────────
    private float _gestureAccumulatedAngle;
    private float _gestureElapsed;
    private float _gesturePrevAngle;
    private bool  _gestureTracking;
    private int   _gestureDirection;   // +1 = CCW, -1 = CW
    private float _gesturePathLength;
    private Vector2 _gestureTracePos;
    private Vector2 _gestureTraceMin;
    private Vector2 _gestureTraceMax;

    // ── flip state machine ─────────────────────────────────────────────────────
    private enum FlipState { None, VerticalInversion, HorizontalTurn, Bursting }
    private FlipState _flipState            = FlipState.None;
    private float     _flipTimer            = 0f;
    private float     _flipStartYaw         = 0f;
    private float     _flipStartPitch       = 0f;
    private float     _flipStartBank        = 0f;
    private float     _flipEntrySpeed       = 0f;
    private float     _flipCooldownRemaining = 0f;

    // Flip particles
    private ParticleSystem _flipPs;

    // Reusable buffer for dash-kill overlap checks — avoids per-frame heap allocation.
    private readonly Collider[] _dashKillBuffer = new Collider[32];

    // Camera stays in pre-maneuver framing until burst starts.
    public bool HoldCameraUntilBurst =>
        _flipState == FlipState.VerticalInversion || _flipState == FlipState.HorizontalTurn;

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic   = true;
        rb.useGravity    = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        currentYaw   = transform.eulerAngles.y;
        currentPitch = transform.eulerAngles.x;
        if (currentPitch > 180f) currentPitch -= 360f;

        currentSpeed    = flightSpeed;
        _currentHealth  = maxHealth;
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        if (dashParticles != null)
        {
            dashPs = dashParticles.GetComponentInChildren<ParticleSystem>();
            dashParticles.SetActive(false);
        }

        if (flipParticles != null)
        {
            _flipPs = flipParticles.GetComponentInChildren<ParticleSystem>();
            flipParticles.SetActive(false);
        }

        staminaBar = GetComponentInChildren<SCRIPT_StaminaBar>();

        InitPostProcessing();
    }

    void InitPostProcessing()
    {
        if (globalVolume == null)
            globalVolume = FindFirstObjectByType<Volume>();

        if (globalVolume == null) return;

        // Clone the profile so we never write back to the shared asset on disk.
        globalVolume.profile = Instantiate(globalVolume.profile);

        globalVolume.profile.TryGet(out lensDistortion);
        globalVolume.profile.TryGet(out vignette);

        if (lensDistortion != null)
        {
            baseLensDistortion = lensDistortion.intensity.value;
            lensDistortion.intensity.overrideState = true;
        }
        if (vignette != null)
        {
            baseVignette = vignette.intensity.value;
            vignette.intensity.overrideState = true;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────

    void Update()
    {
        Vector2 rawDelta = Mouse.current.delta.ReadValue();

        // Gesture detector reads raw per-frame delta before it gets batched.
        UpdateGestureDetection(rawDelta, Time.deltaTime);

        pendingMouseDelta += rawDelta;

        if (dashParticles != null)
        {
            if (Mouse.current.rightButton.wasPressedThisFrame || (Mouse.current.rightButton.isPressed && staminaBar.IsFull()))  
            {
                dashParticles.SetActive(true);
                if (dashPs != null) dashPs.Play(withChildren: true);
            }
            else if (Mouse.current.rightButton.wasReleasedThisFrame || !staminaBar.CanDash())
            {
                if (dashPs != null) dashPs.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
                dashParticles.SetActive(false);
            }
        }

        UpdatePostProcessing();
    }

    // ── circle gesture detection ───────────────────────────────────────────────

    void UpdateGestureDetection(Vector2 rawDelta, float dt)
    {
        // Tick cooldown.
        if (_flipCooldownRemaining > 0f)
        {
            _flipCooldownRemaining -= dt;
            if (_flipCooldownRemaining < 0f) _flipCooldownRemaining = 0f;
        }

        // No gesture tracking while a flip or burst is active, or during cooldown.
        if (_flipState != FlipState.None || _flipCooldownRemaining > 0f)
        {
            ResetGesture();
            return;
        }

        if (_gestureTracking)
        {
            _gestureElapsed += dt;
            if (_gestureElapsed > circleWindowSeconds)
            {
                ResetGesture();
                return;
            }
        }

        float speed = rawDelta.magnitude / Mathf.Max(dt, 0.0001f);

        if (speed < circleMinMouseSpeed)
            return;

        float currentAngle = Mathf.Atan2(rawDelta.y, rawDelta.x) * Mathf.Rad2Deg;

        // Seed the tracking on the first fast-enough frame.
        if (!_gestureTracking)
        {
            _gestureTracking          = true;
            _gesturePrevAngle         = currentAngle;
            _gestureAccumulatedAngle  = 0f;
            _gestureElapsed           = 0f;
            _gestureDirection         = 0;
            _gesturePathLength        = 0f;
            _gestureTracePos          = Vector2.zero;
            _gestureTraceMin          = Vector2.zero;
            _gestureTraceMax          = Vector2.zero;
            return;
        }

        _gesturePathLength += rawDelta.magnitude;
        _gestureTracePos   += rawDelta;
        _gestureTraceMin    = Vector2.Min(_gestureTraceMin, _gestureTracePos);
        _gestureTraceMax    = Vector2.Max(_gestureTraceMax, _gestureTracePos);

        float delta = Mathf.DeltaAngle(_gesturePrevAngle, currentAngle);

        // Establish rotation direction on first substantial frame.
        if (_gestureDirection == 0)
        {
            if (Mathf.Abs(delta) > 5f)
                _gestureDirection = delta > 0f ? 1 : -1;
            _gesturePrevAngle  = currentAngle;
            return;
        }

        float signedDelta = _gestureDirection * delta;

        // Intentional gesture: significant reversals invalidate and restart.
        if (signedDelta < -circleReverseAllowed)
        {
            ResetGesture();
            return;
        }

        if (signedDelta > 0f)
            _gestureAccumulatedAngle += Mathf.Abs(signedDelta);

        _gesturePrevAngle  = currentAngle;

        // Trigger!
        if (_gestureAccumulatedAngle >= circleAngleThreshold && GestureShapeLooksIntentional())
            TriggerFlip();
    }

    bool GestureShapeLooksIntentional()
    {
        Vector2 span = _gestureTraceMax - _gestureTraceMin;
        return span.x >= circleMinDiameter &&
               span.y >= circleMinDiameter &&
               _gesturePathLength >= circleMinPathLength;
    }

    void ResetGesture()
    {
        _gestureTracking         = false;
        _gestureDirection        = 0;
        _gestureAccumulatedAngle = 0f;
        _gestureElapsed          = 0f;
        _gesturePrevAngle        = 0f;
        _gesturePathLength       = 0f;
        _gestureTracePos         = Vector2.zero;
        _gestureTraceMin         = Vector2.zero;
        _gestureTraceMax         = Vector2.zero;
    }

    void TriggerFlip()
    {
        _flipState      = FlipState.VerticalInversion;
        _flipTimer      = 0f;
        _flipStartYaw   = currentYaw;
        _flipStartPitch = currentPitch;
        _flipStartBank  = currentBank;
        _flipEntrySpeed = Mathf.Max(currentSpeed, 1f);

        // Zero out angular velocity so SmoothDamp doesn't fight the flip.
        yawRate      = 0f;
        yawRateVel   = 0f;
        pitchRate    = 0f;
        pitchRateVel = 0f;

        ResetGesture();

        if (flipParticles != null)
        {
            flipParticles.SetActive(true);
            if (_flipPs != null) _flipPs.Play(withChildren: true);
        }
    }

    // ── physics ────────────────────────────────────────────────────────────────

    void FixedUpdate()
    {
        Vector2 mouseDelta    = pendingMouseDelta;
        pendingMouseDelta     = Vector2.zero;

        // ── dash: ramp currentSpeed toward target ──────────────────────────────
        bool  dashing     = Mouse.current.rightButton.isPressed && staminaBar.CanDash();
        float targetSpeed = dashing ? dashSpeed : flightSpeed;
        float ramp        = dashing ? dashRampUp : dashRampDown;

        if (dashing)
            staminaBar.UseStamina();
        else 
            staminaBar.RegenStamina();


        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed,
                                  1f - Mathf.Exp(-ramp * Time.fixedDeltaTime));

        // ── flip state machine (may override currentSpeed) ─────────────────────
        UpdateFlipStateMachine();

        // ── dash state (set after flip machine so Bursting is already current) ─
        IsDashing = dashing || _flipState == FlipState.Bursting;

        // ── turn scaling driven by currentSpeed ────────────────────────────────
        float speedT   = Mathf.Clamp01(currentSpeed / referenceSpeed);
        float turnMult = Mathf.Lerp(1f, minTurnMultiplier, speedT);

        // ── smooth angular rates (suppressed during maneuver/burst) ────────────
        Vector2 steeringInput = (_flipState == FlipState.None) ? mouseDelta : Vector2.zero;

        float desiredYaw   =  steeringInput.x * yawSensitivity   * turnMult;
        float desiredPitch = -steeringInput.y * pitchSensitivity * turnMult;

        yawRate   = Mathf.SmoothDamp(yawRate,   desiredYaw,   ref yawRateVel,   steeringSmoothTime, Mathf.Infinity, Time.fixedDeltaTime);
        pitchRate = Mathf.SmoothDamp(pitchRate, desiredPitch, ref pitchRateVel, steeringSmoothTime, Mathf.Infinity, Time.fixedDeltaTime);

        // ── accumulate orientation (overridden during the flip maneuver) ───────
        bool maneuvering = _flipState == FlipState.VerticalInversion || _flipState == FlipState.HorizontalTurn;
        if (!maneuvering)
        {
            currentYaw   += yawRate;
            currentPitch += pitchRate;
            currentPitch  = Mathf.Clamp(currentPitch, -maxPitchAngle, maxPitchAngle);
        }
        else
        {
            // Keep angular rates clean so inertia doesn't kick on maneuver exit.
            yawRate    = 0f;
            yawRateVel = 0f;
            pitchRate  = 0f;
            pitchRateVel = 0f;
        }

        if (!maneuvering)
        {
            float targetBank = enableBanking ? -yawRate * bankStrength : 0f;
            currentBank = Mathf.Lerp(currentBank, targetBank,
                                     1f - Mathf.Exp(-bankSmoothing * Time.fixedDeltaTime));
        }

        Quaternion newRotation = Quaternion.Euler(currentPitch, currentYaw, currentBank);

        // ── gravity speed bonus ────────────────────────────────────────────────
        float downDot        = Mathf.Clamp01(Vector3.Dot(newRotation * Vector3.forward, Vector3.down));
        float effectiveSpeed = currentSpeed + downDot * gravitySpeedBonus;

        // ── apply ──────────────────────────────────────────────────────────────
        Vector3 newPosition = rb.position + newRotation * Vector3.forward * effectiveSpeed * Time.fixedDeltaTime;

        rb.MovePosition(newPosition);
        rb.MoveRotation(newRotation);

        // ── dash kill ──────────────────────────────────────────────────────────
        if (IsDashing)
            CheckDashKill();
    }

    void UpdateFlipStateMachine()
    {
        float slowTargetSpeed = Mathf.Max(1f, _flipEntrySpeed * Mathf.Clamp01(flipSlowMultiplier));

        switch (_flipState)
        {
            case FlipState.None:
                return;

            case FlipState.VerticalInversion:
                _flipTimer += Time.fixedDeltaTime;
                {
                    float phaseDuration = Mathf.Max(0.01f, flipVerticalDuration);
                    float t = Mathf.Clamp01(_flipTimer / phaseDuration);
                    float eased = Mathf.SmoothStep(0f, 1f, t);
                    float yawLeadT = Mathf.Clamp01((eased - 0.5f) / 0.5f);

                    // Phase 1: rotate in the vertical plane to upside-down.
                    // Begin yaw halfway through this phase.
                    currentPitch = _flipStartPitch + eased * 180f;
                    currentYaw   = _flipStartYaw + yawLeadT * 90f;
                    currentBank  = _flipStartBank;

                    // Slow continuously during the maneuver.
                    currentSpeed = Mathf.Lerp(_flipEntrySpeed, slowTargetSpeed, eased * 0.5f);

                    if (_flipTimer >= phaseDuration)
                    {
                        _flipState = FlipState.HorizontalTurn;
                        _flipTimer = 0f;
                    }
                }
                break;

            case FlipState.HorizontalTurn:
                _flipTimer += Time.fixedDeltaTime;
                {
                    float phaseDuration = Mathf.Max(0.01f, flipHorizontalDuration);
                    float t = Mathf.Clamp01(_flipTimer / phaseDuration);
                    float eased = Mathf.SmoothStep(0f, 1f, t);

                    // Phase 2: yaw around to opposite heading while righting the body.
                    currentYaw   = _flipStartYaw + 90f + eased * 90f;
                    currentPitch = _flipStartPitch + (1f - eased) * 180f;
                    currentBank  = _flipStartBank;

                    currentSpeed = Mathf.Lerp(_flipEntrySpeed, slowTargetSpeed, 0.5f + eased * 0.5f);

                    if (_flipTimer >= phaseDuration)
                    {
                        // Snap to exact final orientation.
                        currentYaw   = _flipStartYaw + 180f;
                        currentPitch = _flipStartPitch;
                        currentBank  = _flipStartBank;

                        _flipTimer = 0f;
                        _flipState = FlipState.Bursting;

                        // Burst only after the full maneuver completes.
                        currentSpeed = burstSpeed;
                    }
                }
                break;

            case FlipState.Bursting:
                _flipTimer += Time.fixedDeltaTime;

                // Hold burst speed, overriding the dash ramp that ran above.
                currentSpeed = burstSpeed;

                if (_flipTimer >= burstDurationSeconds)
                {
                    _flipState             = FlipState.None;
                    _flipTimer             = 0f;
                    _flipCooldownRemaining = flipCooldownSeconds;

                    // Do NOT reset currentSpeed — let the dash ramp decay it naturally.

                    if (_flipPs != null)
                        _flipPs.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    if (flipParticles != null)
                        flipParticles.SetActive(false);
                }
                break;
        }
    }

    // ── health ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Global entry point for anything that wants to hurt the player.
    /// Enemies, hazards, etc. — call this and nothing else.
    /// </summary>
    public void TakeDamage(float amount)
    {
        if (!IsAlive || IsDashing) return;

        _currentHealth = Mathf.Max(0f, _currentHealth - amount);

        if (_currentHealth <= 0f)
            OnDeath();
    }

    void CheckDashKill()
    {
        int count = Physics.OverlapSphereNonAlloc(rb.position, dashKillRadius, _dashKillBuffer);
        for (int i = 0; i < count; i++)
        {
            SCRIPT_EnemyBase enemy = _dashKillBuffer[i].GetComponentInParent<SCRIPT_EnemyBase>();
            if (enemy != null)
                enemy.TakeDamage(float.PositiveInfinity, rb.position);
        }
    }

    protected virtual void OnDeath()
    {
        // Hook for game-over logic — replace or extend as the project grows.
        Debug.Log("[Player] Died!");
    }

    void UpdatePostProcessing()
    {
        // dashT: 0 = cruising at flightSpeed, 1 = fully dashing at dashSpeed.
        // Uses currentSpeed so it follows the same ramp as movement — no separate timer.
        // During a burst (currentSpeed > dashSpeed), InverseLerp > 1 but Lerp clamps,
        // so the effect simply stays at full dash intensity. No extra code needed.
        float dashT = Mathf.InverseLerp(flightSpeed, dashSpeed, currentSpeed);
        float t     = 1f - Mathf.Exp(-ppBlendSpeed * Time.deltaTime);

        if (lensDistortion != null)
        {
            float target = Mathf.Lerp(baseLensDistortion, dashLensDistortionIntensity, dashT);
            lensDistortion.intensity.value = Mathf.Lerp(lensDistortion.intensity.value, target, t);
        }

        if (vignette != null)
        {
            float target = Mathf.Lerp(baseVignette, dashVignetteIntensity, dashT);
            vignette.intensity.value = Mathf.Lerp(vignette.intensity.value, target, t);
        }
    }
}
