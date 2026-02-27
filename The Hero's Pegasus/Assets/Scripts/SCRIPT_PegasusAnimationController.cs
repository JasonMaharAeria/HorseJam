using UnityEngine;

/// <summary>
/// Drives flap/glide animation blending based on flight attitude.
///
/// Animator setup required:
///   - Float parameter matching flapBlendParam  (0 = full glide, 1 = full flap)
///   - A 1D blend tree (or two states with transition) driven by that parameter
///
/// Optional: set animator.speed at runtime (this script does it) to vary flap rate.
/// </summary>
public class SCRIPT_PegasusAnimationController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Leave empty to auto-find the first Animator in children")]
    public Animator animator;

    [Header("Animator Parameter")]
    [Tooltip("Name of the float parameter in your Animator that blends glide→flap (0→1)")]
    public string flapBlendParam = "FlapBlend";

    [Header("Attitude Thresholds (degrees)")]
    [Tooltip("Pitch angle above which the pegasus is considered to be climbing")]
    public float climbThreshold = 15f;
    [Tooltip("Pitch angle below which the pegasus is considered to be diving")]
    public float diveThreshold  = 15f;
    [Tooltip("Absolute bank angle above which we consider it a sharp bank")]
    public float bankThreshold  = 25f;

    [Header("Flap Blend Targets  (0 = glide, 1 = flap)")]
    [Range(0f, 1f)] public float cruiseFlapBlend = 0.5f;
    [Range(0f, 1f)] public float climbFlapBlend  = 1.0f;
    [Range(0f, 1f)] public float bankFlapBlend   = 1.0f;
    [Range(0f, 1f)] public float diveFlapBlend   = 0.0f;

    [Header("Animator Speed Targets  (1 = normal, 2 = double speed)")]
    public float cruiseFlapSpeed = 1.0f;
    public float climbFlapSpeed  = 1.8f;
    public float bankFlapSpeed   = 1.6f;
    public float diveFlapSpeed   = 0.8f;

    [Header("Smoothing")]
    [Tooltip("Higher = snappier transitions between states")]
    public float blendSmoothing = 4f;

    // ── private state ──────────────────────────────────────────────────────────
    private float smoothFlapBlend;
    private float smoothFlapSpeed;
    private int   flapBlendHash;

    // ──────────────────────────────────────────────────────────────────────────

    void Start()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null)
        {
            Debug.LogWarning("SCRIPT_PegasusAnimationController: no Animator found.", this);
            return;
        }

        flapBlendHash   = Animator.StringToHash(flapBlendParam);
        smoothFlapBlend = cruiseFlapBlend;
        smoothFlapSpeed = cruiseFlapSpeed;
    }

    void Update()
    {
        if (animator == null) return;

        // ── read attitude from transform ───────────────────────────────────────
        // Euler angles come back as 0-360; normalise to -180..180 so signs are meaningful.
        float pitch = transform.eulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        // Positive pitch in Unity Euler = nose tilted down (diving).
        // Negative pitch = nose tilted up (climbing).

        float bank = transform.eulerAngles.z;
        if (bank > 180f) bank -= 360f;
        bank = Mathf.Abs(bank);

        // ── per-state 0-1 contributions ────────────────────────────────────────
        // Each value smoothly goes from 0 (not in that state) to 1 (fully in it).
        float diveT  = Mathf.Clamp01( pitch / diveThreshold);   // positive pitch = diving
        float climbT = Mathf.Clamp01(-pitch / climbThreshold);  // negative pitch = climbing
        float bankT  = Mathf.Clamp01( bank  / bankThreshold);

        // ── blend targets ──────────────────────────────────────────────────────
        // Start from cruise, then layer dive → climb → bank on top.
        // Later layers partially override earlier ones, so priorities are:
        //   cruise (base) → overridden by dive → overridden by climb → bank adds on top.
        float targetBlend = cruiseFlapBlend;
        float targetSpeed = cruiseFlapSpeed;

        targetBlend = Mathf.Lerp(targetBlend, diveFlapBlend,  diveT);
        targetSpeed = Mathf.Lerp(targetSpeed, diveFlapSpeed,  diveT);

        targetBlend = Mathf.Lerp(targetBlend, climbFlapBlend, climbT);
        targetSpeed = Mathf.Lerp(targetSpeed, climbFlapSpeed, climbT);

        targetBlend = Mathf.Lerp(targetBlend, bankFlapBlend,  bankT);
        targetSpeed = Mathf.Lerp(targetSpeed, bankFlapSpeed,  bankT);

        // ── smooth toward targets ──────────────────────────────────────────────
        float t = 1f - Mathf.Exp(-blendSmoothing * Time.deltaTime);
        smoothFlapBlend = Mathf.Lerp(smoothFlapBlend, targetBlend, t);
        smoothFlapSpeed = Mathf.Lerp(smoothFlapSpeed, targetSpeed, t);

        // ── push to animator ───────────────────────────────────────────────────
        animator.SetFloat(flapBlendHash, smoothFlapBlend);
        animator.speed = smoothFlapSpeed;
    }
}
