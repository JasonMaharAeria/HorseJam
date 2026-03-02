using UnityEngine;

/// <summary>
/// Drives wing-flap/glide animation blending for Enemy Type 1 (dash striker).
///
/// Same attitude-based approach as SCRIPT_PegasusAnimationController, Type 2, and Type 3
/// controllers. Because Type 1's attack IS the dash strike, the second Animator layer
/// (the attack pose) fades in for the full duration of the dash and back out when it ends.
///
/// Animator setup required:
///   Layer 0 — Base:
///     Float "FlapBlend" driving a blend tree: 0 = full glide, 1 = full flap
///
///   Layer 1 — DashAttack (Override or Additive, default weight 0):
///     The charge/attack animation clip.
///     This script controls the layer weight: ramps to 1 when the dash begins,
///     back to 0 when the dash ends.
///
/// Place this component on the same GameObject as (or a parent of) the Animator.
/// </summary>
public class SCRIPT_EnemyType1AnimationController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Leave empty to auto-find the first Animator in children.")]
    public Animator animator;
    [Tooltip("Leave empty to auto-find SCRIPT_EnemyType1 on this GameObject.")]
    public SCRIPT_EnemyType1 enemy;

    [Header("Animator Parameter")]
    [Tooltip("Name of the float parameter in the Animator that blends glide→flap (0→1).")]
    public string flapBlendParam = "FlapBlend";

    [Header("Attack Layer")]
    [Tooltip("Index of the dash-attack layer in the Animator (typically 1).")]
    public int attackLayerIndex = 1;
    [Tooltip("Speed at which the attack layer weight fades in and out (units per second).")]
    public float attackLayerBlendSpeed = 6f;

    [Header("Attitude Thresholds (degrees)")]
    [Tooltip("Pitch angle above which the enemy is considered to be climbing.")]
    public float climbThreshold = 15f;
    [Tooltip("Pitch angle below which the enemy is considered to be diving.")]
    public float diveThreshold  = 15f;
    [Tooltip("Absolute bank angle above which we consider it a sharp bank.")]
    public float bankThreshold  = 25f;

    [Header("Flap Blend Targets  (0 = glide, 1 = flap)")]
    [Range(0f, 1f)] public float cruiseFlapBlend = 0.5f;
    [Range(0f, 1f)] public float climbFlapBlend  = 1.0f;
    [Range(0f, 1f)] public float bankFlapBlend   = 1.0f;
    [Range(0f, 1f)] public float diveFlapBlend   = 0.0f;
    [Tooltip("Urgent flapping during the full dash-strike charge.")]
    [Range(0f, 1f)] public float dashFlapBlend   = 0.9f;

    [Header("Animator Speed Targets  (1 = normal)")]
    public float cruiseFlapSpeed = 1.0f;
    public float climbFlapSpeed  = 1.8f;
    public float bankFlapSpeed   = 1.6f;
    public float diveFlapSpeed   = 0.8f;
    [Tooltip("Wing speed during the dash-strike — full power charge.")]
    public float dashFlapSpeed   = 2.0f;

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
            enemy = GetComponent<SCRIPT_EnemyType1>();

        if (animator == null)
        {
            Debug.LogWarning("SCRIPT_EnemyType1AnimationController: no Animator found.", this);
            return;
        }

        if (enemy == null)
        {
            Debug.LogWarning("SCRIPT_EnemyType1AnimationController: no SCRIPT_EnemyType1 found.", this);
            return;
        }

        _flapBlendHash   = Animator.StringToHash(flapBlendParam);
        _smoothFlapBlend = cruiseFlapBlend;
        _smoothFlapSpeed = cruiseFlapSpeed;
    }

    void Update()
    {
        if (animator == null || enemy == null) return;

        bool isDashing = enemy.IsDashStriking;

        // ── read attitude from transform ───────────────────────────────────────
        float pitch = transform.eulerAngles.x;
        if (pitch > 180f) pitch -= 360f;

        float bank = transform.eulerAngles.z;
        if (bank > 180f) bank -= 360f;
        bank = Mathf.Abs(bank);

        float diveT  = Mathf.Clamp01( pitch / diveThreshold);
        float climbT = Mathf.Clamp01(-pitch / climbThreshold);
        float bankT  = Mathf.Clamp01( bank  / bankThreshold);

        // ── choose blend targets ───────────────────────────────────────────────
        float targetBlend, targetSpeed;

        if (isDashing)
        {
            // Full-power charge: maximum flap urgency for the whole dash duration.
            targetBlend = dashFlapBlend;
            targetSpeed = dashFlapSpeed;
        }
        else
        {
            // Normal attitude-driven blend — mirrors PegasusAnimationController.
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

        // ── attack layer weight — full during dash, zero otherwise ─────────────
        float targetWeight = isDashing ? 1f : 0f;
        _currentAttackWeight = Mathf.MoveTowards(
            _currentAttackWeight, targetWeight,
            attackLayerBlendSpeed * Time.deltaTime);

        // ── push to animator ───────────────────────────────────────────────────
        animator.SetFloat(_flapBlendHash, _smoothFlapBlend);
        animator.speed = _smoothFlapSpeed;
        animator.SetLayerWeight(attackLayerIndex, _currentAttackWeight);
    }
}
