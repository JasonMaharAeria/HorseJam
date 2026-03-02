using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Audio;
using UnityEngine.Rendering;           // Volume
using UnityEngine.Rendering.Universal; // LensDistortion, Vignette  (requires URP)

// Rigidbody is configured in code — nothing to set in Inspector.
[RequireComponent(typeof(Rigidbody))]
public class SCRIPT_PlayerMovementController : MonoBehaviour
{
    [Header("Flight Speed")]
    public float flightSpeed = 20f;

    [Header("Dash")]
    public float      dashSpeed          = 60f;
    public float      dashRampUp         = 8f;
    public float      dashRampDown       = 3f;
    public GameObject dashParticles;
    [Tooltip("Maximum extra speed added when dash is held continuously (resets on release).")]
    public float      dashHoldSpeedBonus = 5f;
    [Tooltip("Seconds of continuous dash holding to ramp from 0 to the full speed bonus.")]
    public float      dashHoldRampTime   = 3f;

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

    /// <summary>Current health, read-only from outside. Reads the health bar when available so regen is reflected.</summary>
    public float CurrentHealth => healthBar != null ? healthBar.CurrentHealth : _currentHealth;
    public bool  IsAlive       => CurrentHealth > 0f;

    [Header("Dash Attack")]
    [Tooltip("Enemies within this radius are instantly destroyed while dashing or bursting.")]
    public float dashKillRadius = 3f;

    /// <summary>True while RMB dash is held or the post-flip burst is active.</summary>
    public bool  IsDashing    { get; private set; }
    /// <summary>Seconds the dash button has been held continuously this press (resets to 0 on release).</summary>
    public float DashHoldTime { get; private set; }

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

    [Header("Pause — Downward Flick Detection")]
    [Tooltip("Minimum mouse speed (px/s) for a frame to count toward the downward flick that opens the pause menu.")]
    public float pauseFlickMinSpeed = 500f;
    [Tooltip("Time window (seconds) over which the downward displacement is accumulated.")]
    public float pauseFlickWindow = 0.1f;
    [Tooltip("Total downward pixel displacement required within the window to trigger the pause.")]
    public float pauseFlickMinDisplacement = 60f;
    [Tooltip("The downward (negative Y) component must be at least this fraction of the total delta magnitude.")]
    [Range(0f, 1f)]
    public float pauseFlickMinVerticalFraction = 0.7f;
    [Tooltip("Seconds before the downward flick can trigger pause again after it fires.")]
    public float pauseFlickCooldown = 1f;

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
    [Header("Vertical Loop — Laser Audio")]
    [Tooltip("Pitch at loop-laser start and end.")]
    [Min(0.01f)] public float loopLaserBasePitch = 1f;
    [Tooltip("Pitch reached halfway through loop-laser firing.")]
    [Min(0.01f)] public float loopLaserPeakPitch = 1.2f;

    [Header("Island Collision")]
    [Tooltip("Physics layer(s) that island colliders live on. Leave at zero to detect all default layers.")]
    public LayerMask islandLayerMask;
    [Tooltip("Sphere radius used to detect island contact. Should roughly match the pegasus body width.")]
    public float islandBounceCheckRadius = 1.0f;
    [Tooltip("Fraction of speed kept after bouncing off an island (0 = full stop, 1 = no damping).")]
    [Range(0f, 1f)]
    public float islandBounceDamping = 0.65f;

    [Header("Death")]
    [Tooltip("Seconds after death before the death screen starts fading in.")]
    public float deathScreenDelay = 2f;
    [Tooltip("Number of debris pieces spawned when the pegasus disintegrates.")]
    public int deathDebrisCount = 24;
    [Tooltip("Outward impulse applied to each debris piece.")]
    public float deathExplosionForce = 8f;
    [Tooltip("Additional random impulse per piece to break the uniform shell pattern.")]
    public float deathExplosionSpread = 5f;
    [Tooltip("Max random angular velocity (rad/s) giving each piece a tumble.")]
    public float deathDebrisSpinMax = 12f;
    [Tooltip("Optional prefab for each debris piece — a small shard, cube, etc. " +
             "If left empty a default Unity cube primitive is used as a fallback.")]
    public GameObject deathDebrisPrefab;

    [Header("Startup")]
    [Tooltip("If true, all player input and movement are frozen until StartGame() is called. Use with SCRIPT_TitleScreen.")]
    public bool startFrozen = false;

    // ── computed ───────────────────────────────────────────────────────────────

    // Inspector dashSpeed scaled by any DashSpeedIncrease upgrades collected so far.
    float EffectiveDashSpeed => dashSpeed *
        (SCRIPT_PlayerStats.Instance != null ? SCRIPT_PlayerStats.Instance.DashSpeedMultiplier : 1f);

    // ── private state ──────────────────────────────────────────────────────────
    private bool _gameStarted;
    //HUD
    private SCRIPT_HealthBar healthBar;
    private SCRIPT_StaminaBar staminaBar;
    // Movement
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

    // ── pause flick gesture detection ─────────────────────────────────────────
    private float _pauseFlickAccumY;
    private float _pauseFlickWindowTimer;
    private float _pauseFlickCooldownRemaining;

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
    private SCRIPT_LaserController _laserController;
    private AudioSource       _loopLaserAudioSource;
    private SCRIPT_PlayerAudio _playerAudio;
    private bool  _loopLaserAudioPlaying;
    private float _loopLaserAudioTimer;
    private float _loopLaserAudioDuration;
    private float _loopLaserPitchOffset;

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
        _playerAudio = GetComponent<SCRIPT_PlayerAudio>();
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
        if (!startFrozen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
            _gameStarted     = true;
        }

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
        //get first healthbarscript in scene
        healthBar = FindFirstObjectByType<SCRIPT_HealthBar>();
        healthBar.SetMaxHealth(maxHealth);
        healthBar.SetRegeneration(true);

        staminaBar = FindFirstObjectByType<SCRIPT_StaminaBar>();
        InitLoopLaserAudio();
        InitPostProcessing();
    }

    /// <summary>Called by SCRIPT_TitleScreen when the player presses Play. Unlocks the cursor and enables input.</summary>
    public void StartGame()
    {
        if (_gameStarted) return;
        _gameStarted     = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
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

    void InitLoopLaserAudio()
    {
        _laserController = GetComponent<SCRIPT_LaserController>();
        if (_laserController == null || _laserController.laserSFX == null) return;

        AudioMixerGroup sfxGroup = SCRIPT_AudioManager.Instance != null
                                       ? SCRIPT_AudioManager.Instance.sfxGroup : null;

        _loopLaserAudioSource = gameObject.AddComponent<AudioSource>();
        _loopLaserAudioSource.clip = _laserController.laserSFX;
        _loopLaserAudioSource.outputAudioMixerGroup = sfxGroup;
        _loopLaserAudioSource.loop = true;
        _loopLaserAudioSource.playOnAwake = false;
        _loopLaserAudioSource.spatialBlend = 0f;
        _loopLaserAudioSource.volume = 0f;
        _loopLaserAudioSource.pitch = loopLaserBasePitch;
    }

    void UpdateLoopLaserAudioPitch()
    {
        if (!_loopLaserAudioPlaying || _loopLaserAudioSource == null) return;

        _loopLaserAudioTimer += Time.deltaTime;
        float t = Mathf.Clamp01(_loopLaserAudioTimer / Mathf.Max(_loopLaserAudioDuration, 0.001f));
        float riseFall = 1f - Mathf.Abs(2f * t - 1f); // 0 -> 1 -> 0
        _loopLaserAudioSource.pitch = Mathf.Lerp(loopLaserBasePitch, loopLaserPeakPitch, riseFall) + _loopLaserPitchOffset;
    }

    // ──────────────────────────────────────────────────────────────────────────

    void Update()
    {
        if (_gameStarted)
        {
            Vector2 rawDelta = Mouse.current.delta.ReadValue();

            UpdateGestureDetection(rawDelta, Time.deltaTime);
            UpdateLoopFlickDetection(rawDelta, Time.deltaTime);
            UpdatePauseFlickDetection(rawDelta, Time.deltaTime);

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
        }

        UpdatePostProcessing();
        UpdateLoopLaserAudioPitch();
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

    // ── pause flick gesture detection ─────────────────────────────────────────

    void UpdatePauseFlickDetection(Vector2 rawDelta, float dt)
    {
        // Tick cooldown.
        if (_pauseFlickCooldownRemaining > 0f)
        {
            _pauseFlickCooldownRemaining = Mathf.Max(0f, _pauseFlickCooldownRemaining - dt);
            _pauseFlickAccumY      = 0f;
            _pauseFlickWindowTimer = 0f;
            return;
        }

        // Don't allow pausing during a scripted maneuver.
        if (_flipState != FlipState.None)
        {
            _pauseFlickAccumY      = 0f;
            _pauseFlickWindowTimer = 0f;
            return;
        }

        // If already paused, clear state — don't re-trigger.
        if (SCRIPT_TitleScreen.Instance != null && SCRIPT_TitleScreen.Instance.IsGamePaused)
        {
            _pauseFlickAccumY      = 0f;
            _pauseFlickWindowTimer = 0f;
            return;
        }

        // Decay window.
        _pauseFlickWindowTimer = Mathf.Max(0f, _pauseFlickWindowTimer - dt);
        if (_pauseFlickWindowTimer <= 0f)
            _pauseFlickAccumY = 0f;

        float totalSpeed = rawDelta.magnitude / Mathf.Max(dt, 0.0001f);

        // Accumulate downward (negative Y) displacement when moving fast enough.
        if (totalSpeed >= pauseFlickMinSpeed && rawDelta.y < 0f)
        {
            float verticalFraction = Mathf.Abs(rawDelta.y) / rawDelta.magnitude;
            if (verticalFraction >= pauseFlickMinVerticalFraction)
            {
                if (_pauseFlickWindowTimer <= 0f)
                    _pauseFlickWindowTimer = pauseFlickWindow;

                _pauseFlickAccumY += Mathf.Abs(rawDelta.y);
            }
        }

        if (_pauseFlickAccumY >= pauseFlickMinDisplacement)
        {
            _pauseFlickAccumY            = 0f;
            _pauseFlickWindowTimer       = 0f;
            _pauseFlickCooldownRemaining = pauseFlickCooldown;
            if (SCRIPT_TitleScreen.Instance != null)
                SCRIPT_TitleScreen.Instance.TogglePause();
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
        if (!_gameStarted) return;

        Vector2 mouseDelta    = pendingMouseDelta;
        pendingMouseDelta     = Vector2.zero;

        // ── dash: ramp currentSpeed toward target ──────────────────────────────
        bool  dashing = Mouse.current.rightButton.isPressed && staminaBar.CanDash();
        float ramp    = dashing ? dashRampUp : dashRampDown;

        if (dashing)
        {
            DashHoldTime += Time.fixedDeltaTime;
            staminaBar.UseStamina();
        }
        else
        {
            DashHoldTime = 0f;
            staminaBar.RegenStamina();
        }

        float holdFrac    = dashHoldRampTime > 0f ? Mathf.Clamp01(DashHoldTime / dashHoldRampTime) : 1f;
        float targetSpeed = dashing ? EffectiveDashSpeed + holdFrac * dashHoldSpeedBonus : flightSpeed;

        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed,
                                  1f - Mathf.Exp(-ramp * Time.fixedDeltaTime));

        // ── flip / loop state machine (may override currentSpeed) ──────────────
        UpdateFlipStateMachine();

        // ── dash state ─────────────────────────────────────────────────────────
        IsDashing = dashing || _flipState == FlipState.Bursting;

        // ── turn scaling driven by currentSpeed ────────────────────────────────
        float speedT2   = Mathf.Clamp01(currentSpeed / referenceSpeed);
        float turnMult2 = Mathf.Lerp(1f, minTurnMultiplier, speedT2);

        // ── suppress steering during ANY scripted maneuver ─────────────────────
        bool suppressSteering = _flipState != FlipState.None && _flipState != FlipState.Bursting;
        Vector2 steeringInput = suppressSteering ? Vector2.zero : mouseDelta;

        float desiredYaw2   =  steeringInput.x * yawSensitivity   * turnMult2;
        float desiredPitch2 = -steeringInput.y * pitchSensitivity * turnMult2;

        yawRate   = Mathf.SmoothDamp(yawRate,   desiredYaw2,   ref yawRateVel,   steeringSmoothTime, Mathf.Infinity, Time.fixedDeltaTime);
        pitchRate = Mathf.SmoothDamp(pitchRate, desiredPitch2, ref pitchRateVel, steeringSmoothTime, Mathf.Infinity, Time.fixedDeltaTime);

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

        Vector3 moveDir  = newRotation * Vector3.forward;
        float   moveDist = effectiveSpeed * Time.fixedDeltaTime;
        Vector3 newPosition = rb.position + moveDir * moveDist;

        // ── island bounce: SphereCast along the movement vector ──────────────
        if (moveDist > 0f)
        {
            int castMask = islandLayerMask != 0
                ? (int)islandLayerMask
                : Physics.DefaultRaycastLayers;

            if (Physics.SphereCast(rb.position, islandBounceCheckRadius, moveDir,
                                   out RaycastHit islandHit, moveDist + islandBounceCheckRadius, castMask) &&
                islandHit.collider.GetComponentInParent<SCRIPT_FloatingIsland>() != null)
            {
                // Non-convex meshes can return inward-pointing normals on interior/concave faces.
                // If the normal agrees with the movement direction it would drive the player into
                // the island, so flip it to always oppose movement.
                Vector3 bounceNormal = islandHit.normal;
                if (Vector3.Dot(bounceNormal, moveDir) > 0f)
                    bounceNormal = -bounceNormal;

                // Reflect the flight direction off the corrected surface normal.
                Vector3 reflected = Vector3.Reflect(moveDir, bounceNormal).normalized;

                currentYaw   = Mathf.Atan2(reflected.x, reflected.z) * Mathf.Rad2Deg;
                currentPitch = -Mathf.Asin(Mathf.Clamp(reflected.y, -1f, 1f)) * Mathf.Rad2Deg;
                currentPitch = Mathf.Clamp(currentPitch, -maxPitchAngle, maxPitchAngle);
                currentBank  = 0f;
                currentSpeed *= islandBounceDamping;
                TakeDamage(maxHealth * 0.1f);
                if (_playerAudio != null) _playerAudio.PlayIslandBounce();

                // Abort any active scripted maneuver.
                if (_flipState != FlipState.None)
                {
                    _flipState = FlipState.None;
                    _flipTimer = 0f;
                    StopLoopLasers();
                    if (_flipPs != null)
                        _flipPs.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    if (flipParticles != null)
                        flipParticles.SetActive(false);
                }

                yawRate = 0f; yawRateVel = 0f;
                pitchRate = 0f; pitchRateVel = 0f;

                // Place the player just outside the surface so the next SphereCast never
                // starts inside the collider (which would make it blind to the island).
                // islandHit.point is the surface contact; offset by the sphere radius + a
                // small epsilon along the validated normal.
                newRotation = Quaternion.Euler(currentPitch, currentYaw, currentBank);
                newPosition = islandHit.point + bounceNormal * (islandBounceCheckRadius + 0.1f);
            }
        }

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

        if (fired > 0)
            StartLoopLaserAudio();
    }

    void StartLoopLaserAudio()
    {
        if (_loopLaserAudioSource == null) return;

        AudioClip clip = (_laserController != null) ? _laserController.laserSFX : null;
        if (clip == null) return;

        float normalLaserVolume = (_laserController != null) ? _laserController.volume : 0.5f;

        _loopLaserAudioSource.Stop();
        _loopLaserAudioSource.clip = clip;
        _loopLaserAudioSource.volume = normalLaserVolume * 0.5f;
        _loopLaserPitchOffset              = SCRIPT_AudioManager.RandomPitch() - 1f;
        _loopLaserAudioSource.pitch        = loopLaserBasePitch + _loopLaserPitchOffset;
        _loopLaserAudioSource.loop         = true;
        _loopLaserAudioSource.Play();

        _loopLaserAudioDuration = Mathf.Max(0.01f, _loopDuration - _flipTimer);
        _loopLaserAudioTimer = 0f;
        _loopLaserAudioPlaying = true;
    }

    void StopLoopLaserAudio()
    {
        if (_loopLaserAudioSource != null)
        {
            _loopLaserAudioSource.Stop();
            _loopLaserAudioSource.pitch = loopLaserBasePitch;
        }

        _loopLaserAudioPlaying = false;
        _loopLaserAudioTimer = 0f;
        _loopLaserAudioDuration = 0f;
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
        StopLoopLaserAudio();
    }

    void OnDisable()
    {
        StopLoopLasers();
    }

    // ── health ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by SCRIPT_PlayerStats when a MaxHealthIncrease upgrade is collected.
    /// Scales maxHealth by <paramref name="multiplier"/> and heals the player by the gained amount.
    /// </summary>
    public void IncreaseMaxHealth(float multiplier)
    {
        float gained   = maxHealth * (multiplier - 1f);
        maxHealth     *= multiplier;
        _currentHealth = Mathf.Min(_currentHealth + gained, maxHealth);
        if (healthBar != null)
            healthBar.ExpandMax(maxHealth, _currentHealth);
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive || IsDashing) return;
        // Sync with the health bar, which tracks regen independently.
        if (healthBar != null) _currentHealth = healthBar.CurrentHealth;
        _currentHealth = Mathf.Max(-10f, _currentHealth - amount);

        healthBar.SetHealth(_currentHealth);

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
        healthBar.SetRegeneration(false);

        // Freeze stats timer and stop the wave spawner.
        SCRIPT_GameStats.Instance?.Stop();
        FindFirstObjectByType<SCRIPT_WaveController>()?.Stop();

        // Freeze the camera in place.
        FindFirstObjectByType<SCRIPT_CameraController>()?.Freeze();

        // Disintegrate the pegasus visually.
        Disintegrate();

        // Show the death screen after a delay.
        SCRIPT_DeathScreen deathScreen = FindFirstObjectByType<SCRIPT_DeathScreen>();
        if (deathScreen != null)
            deathScreen.ShowAfterDelay(deathScreenDelay);
    }

    void Disintegrate()
    {
        // ── 1. Measure the model's bounding volume before hiding anything ─────
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
            bounds.Encapsulate(r.bounds);

        // Guarantee a minimum size so pieces still spread even on a tiny model.
        Vector3 minExtent = Vector3.one * 0.5f;
        bounds.extents = Vector3.Max(bounds.extents, minExtent);

        // ── 2. Hide all renderers so the original vanishes instantly ──────────
        foreach (Renderer r in renderers)
            r.enabled = false;

        // ── 3. Spawn debris pieces scattered through the bounding volume ──────
        Vector3 center = bounds.center;

        for (int i = 0; i < deathDebrisCount; i++)
        {
            // Random point inside the model's bounding box.
            Vector3 spawnPos = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                Random.Range(bounds.min.y, bounds.max.y),
                Random.Range(bounds.min.z, bounds.max.z));

            GameObject piece = deathDebrisPrefab != null
                ? Instantiate(deathDebrisPrefab, spawnPos, Random.rotation)
                : CreateDebrisFallback(spawnPos);

            Rigidbody pieceRb = piece.GetComponent<Rigidbody>();
            if (pieceRb == null) pieceRb = piece.AddComponent<Rigidbody>();
            pieceRb.useGravity  = true;
            pieceRb.isKinematic = false;

            // Outward from centre + random spread for a chaotic burst.
            Vector3 outward = spawnPos - center;
            if (outward.sqrMagnitude < 0.001f) outward = Random.onUnitSphere;
            pieceRb.AddForce(outward.normalized * deathExplosionForce
                           + Random.onUnitSphere * deathExplosionSpread,
                             ForceMode.Impulse);

            // Random tumble.
            pieceRb.angularVelocity = Random.onUnitSphere * deathDebrisSpinMax;

            Destroy(piece, 5f);
        }
    }

    // Fallback when no deathDebrisPrefab is assigned.
    // Creates a small randomly-scaled cube using Unity's built-in mesh.
    static GameObject CreateDebrisFallback(Vector3 pos)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.position = pos;
        go.transform.rotation = Random.rotation;
        float s = Random.Range(0.08f, 0.35f);
        go.transform.localScale = new Vector3(s, s * Random.Range(0.4f, 2.5f), s);

        // Remove the collider — debris should pass through each other cleanly.
        Destroy(go.GetComponent<Collider>());
        return go;
    }

    void UpdatePostProcessing()
    {
        float dashT = Mathf.InverseLerp(flightSpeed, EffectiveDashSpeed, currentSpeed);
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
