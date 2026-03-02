using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Enemy Type 3 — Close-Range AOE Attacker.
///
/// Normal behaviour: pure pursuit (same as base GetDesiredHeading).
///
/// Dash Strike: when the player enters dashStrikeRadius, Type 3 boosts its stats
/// and dashes to a position *beside* the player (lateral offset) rather than
/// directly at them. Once within aoeActivationRadius the cone particle system is
/// activated and oriented toward the player. It fires for aoeDuration seconds,
/// dealing damageOnCollision per particle that strikes the player, then the
/// strike ends and dashStrikeCooldown must expire before another can begin.
///
/// Setup (in the Inspector / prefab):
///   1. Assign aoeParticleObject to a child GameObject that contains:
///      - A ParticleSystem with a cone-shaped emission region.
///      - SCRIPT_AOEHitRelay attached to the SAME GameObject as the PS.
///      - Collision module: Type = World, Send Collision Messages = ON.
///   2. Leave that child inactive in the hierarchy — this script enables it.
/// </summary>
public class SCRIPT_EnemyType3 : SCRIPT_EnemyBase
{
    [Header("Dash Strike — Trigger")]
    [Tooltip("Distance at which the dash strike activates (only from Pursuing state).")]
    public float dashStrikeRadius = 25f;
    [Tooltip("Max seconds the dash strike lasts before aborting and resuming pursuit.")]
    public float dashStrikeDuration = 4f;
    [Tooltip("Seconds before another dash strike can trigger after the previous one ends.")]
    public float dashStrikeCooldown = 6f;

    [Header("Dash Strike — Boosted Stats")]
    [Tooltip("flightSpeed multiplier during the strike.")]
    public float dashStrikeSpeedMult      = 1.8f;
    [Tooltip("trackingGain multiplier during the strike.")]
    public float dashStrikeTrackingMult   = 3.0f;
    [Tooltip("maxTurnRate multiplier during the strike.")]
    public float dashStrikeMaxTurnMult    = 4.0f;
    [Tooltip("steeringSmoothTime multiplier during the strike. Very low values kill angular inertia.")]
    public float dashStrikeSmoothTimeMult = 0.03f;

    [Header("Lateral Offset")]
    [Tooltip("How far to the side of the player the enemy targets during the dash (world units).")]
    public float sideOffset = 6f;

    [Header("Post-Breath Retreat")]
    [Tooltip("How long the enemy retreats after the breath attack before resuming pursuit.")]
    public float retreatDuration = 5f;
    [Tooltip("Max distance from the player the enemy will drift during retreat. " +
             "If it exceeds this, it curves back toward this shell radius.")]
    public float retreatMaxDistance = 60f;

    [Header("AOE Audio")]
    [Tooltip("Played at the enemy's position each time the AOE cone activates (first strike and every repeat).")]
    public AudioClip aoeActivationClip;

    [Header("AOE Attack")]
    [Tooltip("Child GameObject that contains the cone ParticleSystem and SCRIPT_AOEHitRelay. " +
             "Must be inactive by default in the hierarchy.")]
    public GameObject aoeParticleObject;
    [Tooltip("Distance to the player at which the AOE fires (world units).")]
    public float aoeActivationRadius = 8f;
    [Tooltip("How long the AOE fires before being deactivated and the strike ending.")]
    public float aoeDuration = 1.5f;

    // ── public state ───────────────────────────────────────────────────────────

    /// <summary>True while the breath cone particle system is actively firing.</summary>
    public bool IsBreathAttackActive => _aoeActive;

    // ── private state ──────────────────────────────────────────────────────────

    private SCRIPT_PlayerMovementController _playerHealth;
    private ParticleSystem                  _aoePs;
    private SCRIPT_AOEHitRelay              _aoeRelay;

    // Base stat snapshots taken after Awake randomisation, restored on strike exit.
    private float _baseFlightSpeed;
    private float _baseTrackingGain;
    private float _baseMaxTurnRate;
    private float _baseSmoothTime;
    private float _baseBankStrength;

    private float _strikeCooldownRemaining;
    private float _aoeTimer;
    private bool  _aoeActive;
    private int   _sideSign; // +1 right / -1 left relative to approach vector, chosen at dash start

    // ──────────────────────────────────────────────────────────────────────────

    protected override void Start()
    {
        base.Start();

        if (playerTarget != null)
            _playerHealth = playerTarget.GetComponent<SCRIPT_PlayerMovementController>();

        // Snapshot stats AFTER base.Awake() has applied per-instance randomisation.
        _baseFlightSpeed  = flightSpeed;
        _baseTrackingGain = trackingGain;
        _baseMaxTurnRate  = maxTurnRate;
        _baseSmoothTime   = steeringSmoothTime;
        _baseBankStrength = bankStrength;

        // Wire up the AOE relay so it knows who to damage and how much.
        if (aoeParticleObject != null)
        {
            _aoePs    = aoeParticleObject.GetComponentInChildren<ParticleSystem>();
            _aoeRelay = aoeParticleObject.GetComponentInChildren<SCRIPT_AOEHitRelay>();

            Debug.Log($"[EnemyType3] aoeParticleObject={aoeParticleObject.name}  _aoePs={(_aoePs != null ? _aoePs.name : "NULL")}  _aoeRelay={(_aoeRelay != null ? "found" : "NULL")}  _playerHealth={(_playerHealth != null ? "found" : "NULL")}");

            if (_aoeRelay != null)
            {
                _aoeRelay.damagePerParticle = damageOnCollision;
                _aoeRelay.playerHealth      = _playerHealth;
            }

            aoeParticleObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning("[EnemyType3] aoeParticleObject is NULL — AOE will not fire.", this);
        }
    }

    void Update()
    {
        _strikeCooldownRemaining = Mathf.Max(0f, _strikeCooldownRemaining - Time.deltaTime);

        if (playerTarget == null) return;

        if (_state == FlightState.DashStriking)
        {
            if (!_aoeActive)
            {
                // Activate once the enemy is close enough to the player.
                float sqrDist = (playerTarget.position - transform.position).sqrMagnitude;
                if (sqrDist <= aoeActivationRadius * aoeActivationRadius)
                    ActivateAoe();
            }
            else
            {
                // Keep the cone aimed at the player every frame while it fires.
                OrientAoeTowardPlayer();

                _aoeTimer -= Time.deltaTime;
                if (_aoeTimer <= 0f)
                    ExitDashStriking();
            }
        }
        else if (_state == FlightState.Pursuing && _strikeCooldownRemaining <= 0f)
        {
            float sqrDist = (playerTarget.position - transform.position).sqrMagnitude;
            if (sqrDist <= dashStrikeRadius * dashStrikeRadius)
                BeginDashStrike();
        }
    }

    // ── dash strike lifecycle ──────────────────────────────────────────────────

    void BeginDashStrike()
    {
        // Pick a side randomly so the enemy doesn't always approach from the same angle.
        _sideSign = Random.value > 0.5f ? 1 : -1;

        flightSpeed        = _baseFlightSpeed  * dashStrikeSpeedMult;
        trackingGain       = _baseTrackingGain * dashStrikeTrackingMult;
        maxTurnRate        = _baseMaxTurnRate  * dashStrikeMaxTurnMult;
        steeringSmoothTime = _baseSmoothTime   * dashStrikeSmoothTimeMult;

        // Kill banking so the heading vector stays on-axis during the approach.
        bankStrength = 0f;
        currentBank  = 0f;

        EnterDashStriking(dashStrikeDuration);
    }

    protected override void ExitDashStriking()
    {
        if (_aoeActive)
            DeactivateAoe();

        flightSpeed        = _baseFlightSpeed;
        trackingGain       = _baseTrackingGain;
        maxTurnRate        = _baseMaxTurnRate;
        steeringSmoothTime = _baseSmoothTime;
        bankStrength       = _baseBankStrength;

        _strikeCooldownRemaining = dashStrikeCooldown;

        EnterRetreating(retreatDuration); // fly away before resuming pursuit
    }

    // ── AOE helpers ────────────────────────────────────────────────────────────

    void ActivateAoe()
    {
        if (aoeParticleObject == null) return;

        _aoeActive = true;
        _aoeTimer  = aoeDuration;

        // Play the activation sting — fires on first activation and again on each repeat.
        if (aoeActivationClip != null)
        {
            AudioMixerGroup sfx = SCRIPT_AudioManager.Instance != null
                                      ? SCRIPT_AudioManager.Instance.sfxGroup : null;
            SCRIPT_AudioManager.PlayClipAtPoint(aoeActivationClip, transform.position, sfx);
        }

        aoeParticleObject.SetActive(true);
        OrientAoeTowardPlayer();

        if (_aoePs != null)
            _aoePs.Play(withChildren: true);
    }

    void DeactivateAoe()
    {
        _aoeActive = false;

        if (_aoePs != null)
            _aoePs.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);

        if (aoeParticleObject != null)
            aoeParticleObject.SetActive(false);
    }

    void OrientAoeTowardPlayer()
    {
        if (aoeParticleObject == null || playerTarget == null) return;

        Vector3 toPlayer = playerTarget.position - aoeParticleObject.transform.position;
        if (toPlayer.sqrMagnitude < 0.001f) return;

        // Setting world rotation directly keeps the cone pointed at the player
        // regardless of the parent enemy's own facing direction.
        aoeParticleObject.transform.rotation = Quaternion.LookRotation(toPlayer);
    }

    // ── heading override ───────────────────────────────────────────────────────

    protected override Vector3 GetDashStrikeHeading()
    {
        if (playerTarget == null) return transform.forward;

        Vector3 toPlayer = playerTarget.position - transform.position;
        if (toPlayer.sqrMagnitude < 0.001f) return transform.forward;

        // Perpendicular to the approach direction (world-space, horizontal plane).
        Vector3 lateral = Vector3.Cross(toPlayer.normalized, Vector3.up);
        if (lateral.sqrMagnitude < 0.001f)
            lateral = Vector3.Cross(toPlayer.normalized, Vector3.forward);
        lateral.Normalize();

        // Aim for a point beside the player rather than directly at them.
        Vector3 dashTarget = playerTarget.position + lateral * (_sideSign * sideOffset);
        return (dashTarget - transform.position).normalized;
    }

    protected override Vector3 GetRetreatingHeading()
    {
        if (playerTarget == null) return transform.forward;

        Vector3 toPlayer = playerTarget.position - transform.position;
        float dist = toPlayer.magnitude;
        if (dist < 0.001f) return transform.forward;

        Vector3 awayDir = -toPlayer / dist;

        // Target a point on the surface of a sphere of radius retreatMaxDistance,
        // directly away from the player. If we're inside the sphere we head toward
        // that point (away). If we've overshot it, the point is back toward the
        // player, so we naturally arc back in — no explicit branch needed.
        Vector3 retreatTarget = playerTarget.position + awayDir * retreatMaxDistance;
        return (retreatTarget - transform.position).normalized;
    }
}
