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

        currentSpeed = flightSpeed;
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
        pendingMouseDelta += Mouse.current.delta.ReadValue();

        if (dashParticles != null)
        {
            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                dashParticles.SetActive(true);
                if (dashPs != null) dashPs.Play(withChildren: true);
            }
            else if (Mouse.current.rightButton.wasReleasedThisFrame)
            {
                if (dashPs != null) dashPs.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
                dashParticles.SetActive(false);
            }
        }

        UpdatePostProcessing();
    }

    void FixedUpdate()
    {
        Vector2 mouseDelta    = pendingMouseDelta;
        pendingMouseDelta     = Vector2.zero;

        // ── dash: ramp currentSpeed toward target ──────────────────────────────
        bool  dashing     = Mouse.current.rightButton.isPressed;
        float targetSpeed = dashing ? dashSpeed : flightSpeed;
        float ramp        = dashing ? dashRampUp : dashRampDown;

        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed,
                                  1f - Mathf.Exp(-ramp * Time.fixedDeltaTime));

        // ── turn scaling driven by currentSpeed ────────────────────────────────
        float speedT   = Mathf.Clamp01(currentSpeed / referenceSpeed);
        float turnMult = Mathf.Lerp(1f, minTurnMultiplier, speedT);

        // ── smooth angular rates ───────────────────────────────────────────────
        float desiredYaw   =  mouseDelta.x * yawSensitivity   * turnMult;
        float desiredPitch = -mouseDelta.y * pitchSensitivity * turnMult;

        yawRate   = Mathf.SmoothDamp(yawRate,   desiredYaw,   ref yawRateVel,   steeringSmoothTime, Mathf.Infinity, Time.fixedDeltaTime);
        pitchRate = Mathf.SmoothDamp(pitchRate, desiredPitch, ref pitchRateVel, steeringSmoothTime, Mathf.Infinity, Time.fixedDeltaTime);

        // ── accumulate orientation ─────────────────────────────────────────────
        currentYaw   += yawRate;
        currentPitch += pitchRate;
        currentPitch  = Mathf.Clamp(currentPitch, -maxPitchAngle, maxPitchAngle);

        float targetBank = enableBanking ? -yawRate * bankStrength : 0f;
        currentBank = Mathf.Lerp(currentBank, targetBank,
                                 1f - Mathf.Exp(-bankSmoothing * Time.fixedDeltaTime));

        Quaternion newRotation = Quaternion.Euler(currentPitch, currentYaw, currentBank);

        // ── gravity speed bonus ────────────────────────────────────────────────
        // downDot is 1 when flying straight down, 0 when level or climbing.
        float downDot        = Mathf.Clamp01(Vector3.Dot(newRotation * Vector3.forward, Vector3.down));
        float effectiveSpeed = currentSpeed + downDot * gravitySpeedBonus;

        // ── apply ──────────────────────────────────────────────────────────────
        Vector3 newPosition = rb.position + newRotation * Vector3.forward * effectiveSpeed * Time.fixedDeltaTime;

        rb.MovePosition(newPosition);
        rb.MoveRotation(newRotation);
    }

    void UpdatePostProcessing()
    {
        // dashT: 0 = cruising at flightSpeed, 1 = fully dashing at dashSpeed.
        // Uses currentSpeed so it follows the same ramp as movement — no separate timer.
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
