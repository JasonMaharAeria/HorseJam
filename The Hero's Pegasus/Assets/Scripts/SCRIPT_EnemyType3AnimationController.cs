using UnityEngine;

/// <summary>
/// Drives wing-flap/glide animation blending for Enemy Type 3 (dragon/breath attacker).
///
/// Follows the same attitude-based approach as SCRIPT_PegasusAnimationController, then
/// overlays the breath attack on a second Animator layer so the wings keep flapping on
/// the base layer while the breath fires on top.
///
/// Animator setup required:
///   Layer 0 — Base:
///     Float "FlapBlend" driving a blend tree: 0 = full glide, 1 = full flap
///
///   Layer 1 — BreathAttack (Override or Additive, default weight 0):
///     Contains the breath/fire animation clip.
///     This script controls the layer weight: ramps to 1 when the breath fires,
///     back to 0 when it ends — combining fly and attack seamlessly.
///
/// Place this component on the same GameObject as (or a parent of) the Animator.
/// </summary>
public class SCRIPT_EnemyType3AnimationController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Leave empty to auto-find the first Animator in children.")]
    public Animator animator;
    [Tooltip("Leave empty to auto-find SCRIPT_EnemyType3 on this GameObject.")]
    public SCRIPT_EnemyType3 enemy;

    [Header("Animator Parameter")]
    [Tooltip("Name of the float parameter in the Animator that blends glide→flap (0→1).")]
    public string flapBlendParam = "FlapBlend";

    [Header("Attack Layer")]
    [Tooltip("Index of the breath-attack layer in the Animator (typically 1).")]
    public int attackLayerIndex = 1;
    [Tooltip("Speed at which the attack layer weight fades in and out (units per second).")]
    public float attackLayerBlendSpeed = 6f;

    [Header("Attitude Thresholds (degrees)")]
    [Tooltip("Pitch angle above which the dragon is considered to be climbing.")]
    public float climbThreshold = 15f;
    [Tooltip("Pitch angle below which the dragon is considered to be diving.")]
    public float diveThreshold  = 15f;
    [Tooltip("Absolute bank angle above which we consider it a sharp bank.")]
    public float bankThreshold  = 25f;

    [Header("Flap Blend Targets  (0 = glide, 1 = flap)")]
    [Range(0f, 1f)] public float cruiseFlapBlend = 0.5f;
    [Range(0f, 1f)] public float climbFlapBlend  = 1.0f;
    [Range(0f, 1f)] public float bankFlapBlend   = 1.0f;
    [Range(0f, 1f)] public float diveFlapBlend   = 0.0f;
    [Tooltip("Urgent flapping during the dash approach before the breath fires.")]
    [Range(0f, 1f)] public float dashFlapBlend   = 0.8f;
    [Tooltip("Steady, slower wingbeat while the breath is actively firing.")]
    [Range(0f, 1f)] public float attackFlapBlend = 0.6f;

    [Header("Animator Speed Targets  (1 = normal)")]
    public float cruiseFlapSpeed = 1.0f;
    public float climbFlapSpeed  = 1.8f;
    public float bankFlapSpeed   = 1.6f;
    public float diveFlapSpeed   = 0.8f;
    [Tooltip("Wing speed during the dash approach.")]
    public float dashFlapSpeed   = 1.4f;
    [Tooltip("Wing speed while breath is firing — slightly reduced for a powerful hold.")]
    public float attackFlapSpeed = 0.7f;

    [Header("Smoothing")]
    [Tooltip("Higher = snappier transitions between blend states.")]
    public float blendSmoothing = 4f;

    // ── private state ──────────────────────────────────────────────────────────
    private float _smoothFlapBlend;
    private float _smoothFlapSpeed;
    private float _currentAttackWeight;
    private int   _flapBlendHash;

    // ──────────────────────────────────────────────────────────────────────────

    void Start()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (enemy == null)
            enemy = GetComponent<SCRIPT_EnemyType3>();

        if (animator == null)
        {
            Debug.LogWarning("SCRIPT_EnemyType3AnimationController: no Animator found.", this);
            return;
        }

        if (enemy == null)
        {
            Debug.LogWarning("SCRIPT_EnemyType3AnimationController: no SCRIPT_EnemyType3 found.", this);
            return;
        }

        _flapBlendHash   = Animator.StringToHash(flapBlendParam);
        _smoothFlapBlend = cruiseFlapBlend;
        _smoothFlapSpeed = cruiseFlapSpeed;
    }

    void Update()
    {
        if (animator == null || enemy == null) return;

        bool isBreathing = enemy.IsBreathAttackActive;
        bool isDashing   = enemy.IsDashStriking;

        // ── read attitude from transform ───────────────────────────────────────
        // Euler angles come back 0-360; normalise to -180..180 so signs work.
        float pitch = transform.eulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        // Positive pitch in Unity Euler = nose down (diving), negative = nose up (climbing).

        float bank = transform.eulerAngles.z;
        if (bank > 180f) bank -= 360f;
        bank = Mathf.Abs(bank);

        float diveT  = Mathf.Clamp01( pitch / diveThreshold);
        float climbT = Mathf.Clamp01(-pitch / climbThreshold);
        float bankT  = Mathf.Clamp01( bank  / bankThreshold);

        // ── choose blend targets ───────────────────────────────────────────────
        // Priority: breath attack > dash approach > attitude-driven cruise.
        float targetBlend, targetSpeed;

        if (isBreathing)
        {
            // Steady, reduced wingbeat — the breath layer does the heavy visual lifting.
            targetBlend = attackFlapBlend;
            targetSpeed = attackFlapSpeed;
        }
        else if (isDashing)
        {
            // Urgent flapping during the high-speed dash approach.
            targetBlend = dashFlapBlend;
            targetSpeed = dashFlapSpeed;
        }
        else
        {
            // Normal flight: attitude-driven blending, same logic as PegasusAnimationController.
            targetBlend = cruiseFlapBlend;
            targetSpeed = cruiseFlapSpeed;

            targetBlend = Mathf.Lerp(targetBlend, diveFlapBlend,  diveT);
            targetSpeed = Mathf.Lerp(targetSpeed, diveFlapSpeed,  diveT);

            targetBlend = Mathf.Lerp(targetBlend, climbFlapBlend, climbT);
            targetSpeed = Mathf.Lerp(targetSpeed, climbFlapSpeed, climbT);

            targetBlend = Mathf.Lerp(targetBlend, bankFlapBlend,  bankT);
            targetSpeed = Mathf.Lerp(targetSpeed, bankFlapSpeed,  bankT);
        }

        // ── smooth toward targets ──────────────────────────────────────────────
        float t = 1f - Mathf.Exp(-blendSmoothing * Time.deltaTime);
        _smoothFlapBlend = Mathf.Lerp(_smoothFlapBlend, targetBlend, t);
        _smoothFlapSpeed = Mathf.Lerp(_smoothFlapSpeed, targetSpeed, t);

        // ── breath attack layer weight — fades in while firing, out when done ──
        float targetWeight = isBreathing ? 1f : 0f;
        _currentAttackWeight = Mathf.MoveTowards(
            _currentAttackWeight, targetWeight,
            attackLayerBlendSpeed * Time.deltaTime);

        // ── push to animator ───────────────────────────────────────────────────
        animator.SetFloat(_flapBlendHash, _smoothFlapBlend);
        animator.speed = _smoothFlapSpeed;
        animator.SetLayerWeight(attackLayerIndex, _currentAttackWeight);
    }
}
