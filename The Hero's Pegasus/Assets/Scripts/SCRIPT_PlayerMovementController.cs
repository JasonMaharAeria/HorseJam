using System.Collections.Generic;
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
    public float      dashRampUp     = 8f;
    public float      dashRampDown   = 3f;
    public GameObject dashParticles;

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
    [Tooltip("Enemies within this radius are instantly destroyed while dashing or bursting.")]
    public float dashKillRadius = 3f;

    /// <summary>True while RMB dash is held or the post-flip burst is active.</summary>
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
    public float flipCooldownSeconds    = 2.0f;
    public GameObject flipParticles;

    [Header("Vertical Loop — Detection")]
    [Tooltip("Minimum mouse speed (px/s) for a frame to count toward the upward flick.")]
    public float loopFlickMinSpeed = 500f;
    [Tooltip("Time window (seconds) over which the upward displacement is accumulated.")]
    public float loopFlickWindow = 0.1f;
    [Tooltip("Total upward pixel displacement required within the window to trigger the loop.")]
    public float loopFlickMinDisplacement = 60f;
    [Tooltip("The upward (Y) component must be at least this fraction of the total delta magnitude.")]
    [Range(0f, 1f)]
    public float loopFlickMinVerticalFraction = 0.7f;
    [Tooltip("Seconds before another loop can trigger after one completes.")]
    public float loopCooldownSeconds = 4f;

    [Header("Vertical Loop — Maneuver")]
    [Tooltip("Radius of the loop circle in world units.")]
    public float loopRadius = 15f;
    [Tooltip("Seconds to complete a full 360-degree loop. Controls loop finish speed regardless of radius.")]
    public float loopDurationSeconds = 2.5f;
    [Tooltip("Starting speed multiplier for the loop. Lower values keep the early loop slower; end speed is auto-calculated so the full loop still completes exactly on time.")]
    [Range(0.1f, 1f)]
    public float loopFirstHalfSpeedMultiplier = 0.7f;
    [Tooltip("Max world-space radius to search for lock-on targets when loop laser lock-on begins.")]
    public float loopFireRange = 80f;
    [Tooltip("Maximum number of enemies to lock on to simultaneously.")]
    public int   loopMaxTargets = 6;
    [Tooltip("Laser particle prefab instantiated per locked target when loop lock-on begins. Parented to the player " +
             "and continuously rotated to track its assigned enemy. Should have SCRIPT_LaserHitRelay.")]
    public GameObject loopLaserPrefab;

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
    private float   currentSpeed;

    // Dash particles
    private ParticleSystem dashPs;

    // Post-processing
    private LensDistortion lensDistortion;
    private Vignette       vignette;
    private float          baseLensDistortion;
    private float          baseVignette;

    // ── circle gesture detection ───────────────────────────────────────────────
    private float   _gestureAccumulatedAngle;
    private float   _gestureElapsed;
    private float   _gesturePrevAngle;
    private bool    _gestureTracking;
    private int     _gestureDirection;
    private float   _gesturePathLength;
    private Vector2 _gestureTracePos;
    private Vector2 _gestureTraceMin;
    private Vector2 _gestureTraceMax;

    // ── loop flick gesture detection ───────────────────────────────────────────
    private float _loopFlickAccumY;
    private float _loopFlickWindowTimer;

    // ── flip state machine ─────────────────────────────────────────────────────
    private enum FlipState
    {
        None,
        VerticalInversion, HorizontalTurn, Bursting,   // circle-flip maneuver
        Looping                                         // vertical loop maneuver
    }
    private FlipState _flipState             = FlipState.None;
    private float     _flipTimer             = 0f;
    private float     _flipStartYaw          = 0f;
    private float     _flipStartPitch        = 0f;
    private float     _flipStartBank         = 0f;
    private float     _flipEntrySpeed        = 0f;
    private float     _flipCooldownRemaining = 0f;

    // Flip particles
    private ParticleSystem _flipPs;

    // ── loop maneuver state ────────────────────────────────────────────────────
    private float   _loopStartPitch;
    private float   _loopStartYaw;
    private float   _loopStartBank;
    private float   _loopStartSpeed;     // base loop speed for multiplier = 1
    private float   _loopDuration;       // full 360-degree loop duration (seconds), computed at trigger
    private float   _loopProgress;       // 0 → 1 over the full loop, exposed for camera
    private bool    _loopFiredYet;       // true once lasers have been spawned for this loop

    // Entry-frame orientation vectors exposed for the camera boom calculation.
    public bool    IsLooping        => _flipState == FlipState.Looping;
    public float   LoopProgress     => _loopProgress;
    public Vector3 LoopEntryForward { get; private set; }
    public Vector3 LoopEntryRight   { get; private set; }

    private readonly List<GameObject> _loopLasers = new();

    // Reusable buffer for dash-kill overlap checks.
    private readonly Collider[] _dashKillBuffer = new Collider[32];

    // Camera holds its forward during the circle-flip only.
    // The loop has its own cinematic camera handled in SCRIPT_CameraController.
    public bool HoldCameraUntilBurst =>
        _flipState == FlipState.VerticalInversion ||
        _flipState == FlipState.HorizontalTurn;

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

        currentSpeed   = flightSpeed;
        _currentHealth = maxHealth;
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

        UpdateGestureDetection(rawDelta, Time.deltaTime);
        UpdateLoopFlickDetection(rawDelta, Time.deltaTime);

        pendingMouseDelta += rawDelta;

        if (dashParticles != null)
        {
            if (Mouse.current.rightButton.wasPressedThisFrame ||
               (Mouse.current.rightButton.isPressed && staminaBar.IsFull()))
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
        if (_flipCooldownRemaining > 0f)
        {
            _flipCooldownRemaining -= dt;
            if (_flipCooldownRemaining < 0f) _flipCooldownRemaining = 0f;
        }

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
        if (speed < circleMinMouseSpeed) return;

        float currentAngle = Mathf.Atan2(rawDelta.y, rawDelta.x) * Mathf.Rad2Deg;

        if (!_gestureTracking)
        {
            _gestureTracking         = true;
            _gesturePrevAngle        = currentAngle;
            _gestureAccumulatedAngle = 0f;
            _gestureElapsed          = 0f;
            _gestureDirection        = 0;
            _gesturePathLength       = 0f;
            _gestureTracePos         = Vector2.zero;
            _gestureTraceMin         = Vector2.zero;
            _gestureTraceMax         = Vector2.zero;
            return;
        }

        _gesturePathLength += rawDelta.magnitude;
        _gestureTracePos   += rawDelta;
        _gestureTraceMin    = Vector2.Min(_gestureTraceMin, _gestureTracePos);
        _gestureTraceMax    = Vector2.Max(_gestureTraceMax, _gestureTracePos);

        float delta = Mathf.DeltaAngle(_gesturePrevAngle, currentAngle);

        if (_gestureDirection == 0)
        {
            if (Mathf.Abs(delta) > 5f)
                _gestureDirection = delta > 0f ? 1 : -1;
            _gesturePrevAngle = currentAngle;
            return;
        }

        float signedDelta = _gestureDirection * delta;

        if (signedDelta < -circleReverseAllowed)
        {
            ResetGesture();
            return;
        }

        if (signedDelta > 0f)
            _gestureAccumulatedAngle += Mathf.Abs(signedDelta);

        _gesturePrevAngle = currentAngle;

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

    // ── loop flick gesture detection ───────────────────────────────────────────

    void UpdateLoopFlickDetection(Vector2 rawDelta, float dt)
    {
        if (_flipState != FlipState.None || _flipCooldownRemaining > 0f)
        {
            _loopFlickAccumY      = 0f;
            _loopFlickWindowTimer = 0f;
            return;
        }

        _loopFlickWindowTimer = Mathf.Max(0f, _loopFlickWindowTimer - dt);
        if (_loopFlickWindowTimer <= 0f)
            _loopFlickAccumY = 0f;

        float totalSpeed = rawDelta.magnitude / Mathf.Max(dt, 0.0001f);

        if (totalSpeed >= loopFlickMinSpeed && rawDelta.y > 0f)
        {
            float verticalFraction = rawDelta.y / rawDelta.magnitude;
            if (verticalFraction >= loopFlickMinVerticalFraction)
            {
                if (_loopFlickWindowTimer <= 0f)
                    _loopFlickWindowTimer = loopFlickWindow;

                _loopFlickAccumY += rawDelta.y;
            }
        }

        if (_loopFlickAccumY >= loopFlickMinDisplacement)
        {
            _loopFlickAccumY      = 0f;
            _loopFlickWindowTimer = 0f;
            TriggerLoop();
        }
    }

    void TriggerLoop()
    {
        _flipState      = FlipState.Looping;
        _flipTimer      = 0f;
        _loopStartPitch = currentPitch;
        _loopStartYaw   = currentYaw;
        _loopStartBank  = currentBank;
        _loopDuration   = Mathf.Max(0.01f, loopDurationSeconds);
        _loopStartSpeed = 2f * Mathf.PI * Mathf.Max(0.01f, loopRadius) / _loopDuration;
        _loopFiredYet   = false;
        _loopProgress   = 0f;

        // Capture entry orientation for the camera boom calculation.
        LoopEntryForward = transform.forward;
        LoopEntryRight   = transform.right;

        yawRate      = 0f;
        yawRateVel   = 0f;
        pitchRate    = 0f;
        pitchRateVel = 0f;

        ResetGesture();
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

        // ── flip / loop state machine (may override currentSpeed) ──────────────
        UpdateFlipStateMachine();

        // ── dash state ─────────────────────────────────────────────────────────
        IsDashing = dashing || _flipState == FlipState.Bursting;

        // ── turn scaling driven by currentSpeed ────────────────────────────────
        float speedT   = Mathf.Clamp01(currentSpeed / referenceSpeed);
        float turnMult = Mathf.Lerp(1f, minTurnMultiplier, speedT);

        // ── suppress steering during ANY scripted maneuver ─────────────────────
        bool suppressSteering = _flipState != FlipState.None && _flipState != FlipState.Bursting;
        Vector2 steeringInput = suppressSteering ? Vector2.zero : mouseDelta;

        float desiredYaw   =  steeringInput.x * yawSensitivity   * turnMult;
        float desiredPitch = -steeringInput.y * pitchSensitivity * turnMult;

        yawRate   = Mathf.SmoothDamp(yawRate,   desiredYaw,   ref yawRateVel,   steeringSmoothTime, Mathf.Infinity, Time.fixedDeltaTime);
        pitchRate = Mathf.SmoothDamp(pitchRate, desiredPitch, ref pitchRateVel, steeringSmoothTime, Mathf.Infinity, Time.fixedDeltaTime);

        // ── accumulate orientation (overridden during scripted phases) ─────────
        bool scriptedRotation = _flipState == FlipState.VerticalInversion ||
                                _flipState == FlipState.HorizontalTurn    ||
                                _flipState == FlipState.Looping;

        if (!scriptedRotation)
        {
            currentYaw   += yawRate;
            currentPitch += pitchRate;
            currentPitch  = Mathf.Clamp(currentPitch, -maxPitchAngle, maxPitchAngle);
        }
        else
        {
            yawRate      = 0f;
            yawRateVel   = 0f;
            pitchRate    = 0f;
            pitchRateVel = 0f;
        }

        if (!scriptedRotation)
        {
            float targetBank = enableBanking ? -yawRate * bankStrength : 0f;
            currentBank = Mathf.Lerp(currentBank, targetBank,
                                     1f - Mathf.Exp(-bankSmoothing * Time.fixedDeltaTime));
        }

        Quaternion newRotation = Quaternion.Euler(currentPitch, currentYaw, currentBank);

        // Suppress gravity bonus during the loop so the circular path stays true.
        float downDot = _flipState == FlipState.Looping
            ? 0f
            : Mathf.Clamp01(Vector3.Dot(newRotation * Vector3.forward, Vector3.down));

        float effectiveSpeed = currentSpeed + downDot * gravitySpeedBonus;

        Vector3 newPosition = rb.position + newRotation * Vector3.forward * effectiveSpeed * Time.fixedDeltaTime;

        rb.MovePosition(newPosition);
        rb.MoveRotation(newRotation);

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

            // ── circle flip ───────────────────────────────────────────────────

            case FlipState.VerticalInversion:
                _flipTimer += Time.fixedDeltaTime;
                {
                    float dur    = Mathf.Max(0.01f, flipVerticalDuration);
                    float t      = Mathf.Clamp01(_flipTimer / dur);
                    float eased  = Mathf.SmoothStep(0f, 1f, t);
                    float yawT   = Mathf.Clamp01((eased - 0.5f) / 0.5f);

                    currentPitch = _flipStartPitch + eased * 180f;
                    currentYaw   = _flipStartYaw + yawT * 90f;
                    currentBank  = _flipStartBank;
                    currentSpeed = Mathf.Lerp(_flipEntrySpeed, slowTargetSpeed, eased * 0.5f);

                    if (_flipTimer >= dur)
                    {
                        _flipState = FlipState.HorizontalTurn;
                        _flipTimer = 0f;
                    }
                }
                break;

            case FlipState.HorizontalTurn:
                _flipTimer += Time.fixedDeltaTime;
                {
                    float dur   = Mathf.Max(0.01f, flipHorizontalDuration);
                    float t     = Mathf.Clamp01(_flipTimer / dur);
                    float eased = Mathf.SmoothStep(0f, 1f, t);

                    currentYaw   = _flipStartYaw + 90f + eased * 90f;
                    currentPitch = _flipStartPitch + (1f - eased) * 180f;
                    currentBank  = _flipStartBank;
                    currentSpeed = Mathf.Lerp(_flipEntrySpeed, slowTargetSpeed, 0.5f + eased * 0.5f);

                    if (_flipTimer >= dur)
                    {
                        currentYaw   = _flipStartYaw + 180f;
                        currentPitch = _flipStartPitch;
                        currentBank  = _flipStartBank;
                        _flipTimer   = 0f;
                        _flipState   = FlipState.Bursting;
                        currentSpeed = burstSpeed;
                    }
                }
                break;

            case FlipState.Bursting:
                _flipTimer += Time.fixedDeltaTime;
                currentSpeed = burstSpeed;

                if (_flipTimer >= burstDurationSeconds)
                {
                    _flipState             = FlipState.None;
                    _flipTimer             = 0f;
                    _flipCooldownRemaining = flipCooldownSeconds;

                    if (_flipPs != null)
                        _flipPs.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    if (flipParticles != null)
                        flipParticles.SetActive(false);
                }
                break;

            // ── vertical loop — single continuous 360° pitch sweep ─────────────
            //
            //   Duration is controlled directly by loopDurationSeconds.
            //   Loop speed is derived at trigger time so larger radii move faster.
            //   Speed transitions smoothly from slower early to faster late.
            //   Lasers start at 90 degrees into the loop (progress = 0.25).

            case FlipState.Looping:
                _flipTimer += Time.fixedDeltaTime;
                {
                    // Time-normalized loop phase.
                    float loopT = Mathf.Clamp01(_flipTimer / _loopDuration);

                    // Start below 1, end above 1, with average multiplier of exactly 1
                    // so the loop still completes on the configured duration.
                    float startMult = Mathf.Clamp(loopFirstHalfSpeedMultiplier, 0.1f, 1f);
                    float endMult   = Mathf.Max(0.01f, 2f - startMult);

                    // Smooth easing from start -> end without any sudden jump.
                    float smoothT   = loopT * loopT * (3f - 2f * loopT); // smoothstep(0..1)
                    float speedMult = Mathf.Lerp(startMult, endMult, smoothT);

                    // Integral of speedMult over loop time gives exact angle progress.
                    // For smoothstep: ∫(3t^2 - 2t^3)dt = t^3 - 0.5t^4
                    _loopProgress = startMult * loopT
                                  + (endMult - startMult) * (loopT * loopT * loopT - 0.5f * loopT * loopT * loopT * loopT);
                    _loopProgress = Mathf.Clamp01(_loopProgress);

                    // Matching speed profile preserves loop radius while progress advances.
                    currentSpeed = _loopStartSpeed * speedMult;
                }

                currentPitch = _loopStartPitch - _loopProgress * 360f;
                currentYaw   = _loopStartYaw;
                currentBank  = 0f;

                // Fire lasers once at 90 degrees into the loop (quarter progress).
                if (!_loopFiredYet && _loopProgress >= 0.25f)
                {
                    _loopFiredYet = true;
                    FireLoopLasers();
                }

                if (_flipTimer >= _loopDuration)
                {
                    // Snap back to exact entry values — eliminates float drift.
                    currentPitch           = _loopStartPitch;
                    currentBank            = _loopStartBank;
                    _loopProgress          = 1f;
                    _flipTimer             = 0f;
                    _flipState             = FlipState.None;
                    _flipCooldownRemaining = loopCooldownSeconds;
                    StopLoopLasers();
                }
                break;
        }
    }

    // ── loop laser helpers ─────────────────────────────────────────────────────

    void FireLoopLasers()
    {
        if (loopLaserPrefab == null) return;

        SCRIPT_EnemyBase[] all = FindObjectsByType<SCRIPT_EnemyBase>(FindObjectsSortMode.None);
        if (all.Length == 0) return;

        float sqrRange = loopFireRange * loopFireRange;
        bool[] used    = new bool[all.Length];
        int    fired   = 0;

        while (fired < loopMaxTargets)
        {
            int   best    = -1;
            float bestSqr = sqrRange;

            for (int i = 0; i < all.Length; i++)
            {
                if (used[i] || all[i] == null) continue;
                float sqr = (all[i].transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = i; }
            }

            if (best == -1) break;

            used[best] = true;

            Vector3    dir = (all[best].transform.position - transform.position).normalized;

            // Parent to player so the emitter follows us through the loop.
            // World-space particle simulation means each new particle fires from the
            // current player position toward the target — creates a continuous beam.
            GameObject go = Instantiate(loopLaserPrefab, transform.position,
                                        Quaternion.LookRotation(dir), transform);

            // Dynamically add the tracker so it continuously re-aims at the target.
            var tracker = go.AddComponent<SCRIPT_LoopLaserTracker>();
            tracker.target = all[best].transform;

            var ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps != null) ps.Play(withChildren: true);

            _loopLasers.Add(go);
            fired++;
        }
    }

    void StopLoopLasers()
    {
        foreach (var go in _loopLasers)
        {
            if (go == null) continue;
            var ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps != null)
                ps.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Destroy(go);
        }
        _loopLasers.Clear();
    }

    // ── health ─────────────────────────────────────────────────────────────────

    public void TakeDamage(float amount)
    {
        if (!IsAlive || IsDashing) return;
        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        if (_currentHealth <= 0f) OnDeath();
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
        Debug.Log("[Player] Died!");
    }

    void UpdatePostProcessing()
    {
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
