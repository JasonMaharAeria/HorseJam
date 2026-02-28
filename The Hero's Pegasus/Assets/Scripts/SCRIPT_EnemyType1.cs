using UnityEngine;

/// <summary>
/// Enemy Type 1 — Standard Pursuer with Dash Strike.
///
/// Normal behaviour: pure pursuit (base GetDesiredHeading as-is).
///
/// Dash Strike: when the player enters dashStrikeRadius, Type 1 boosts its speed
/// and maneuverability and locks on directly. The strike ends when:
///   - the player is passed (enemy forward dot to-player goes negative), or
///   - dashStrikeDuration seconds elapse.
/// After exiting, stats are restored and dashStrikeCooldown must expire before
/// another strike can begin.
///
/// Collision damage: while DashStriking, a proximity check is used each frame
/// (kinematic–kinematic collisions are unreliable). On overlap, damageOnCollision
/// (base field) is applied to the player and a per-hit cooldown begins.
/// </summary>
public class SCRIPT_EnemyType1 : SCRIPT_EnemyBase
{
    [Header("Dash Strike — Trigger")]
    [Tooltip("Distance at which the dash strike activates (only from Pursuing state).")]
    public float dashStrikeRadius = 20f;
    [Tooltip("Max seconds the dash strike lasts before aborting and resuming pursuit.")]
    public float dashStrikeDuration = 3f;
    [Tooltip("Seconds before another dash strike can trigger after the previous one ends.")]
    public float dashStrikeCooldown = 5f;

    [Header("Dash Strike — Boosted Stats")]
    [Tooltip("flightSpeed multiplier during the strike.")]
    public float dashStrikeSpeedMult       = 1.8f;
    [Tooltip("trackingGain multiplier during the strike.")]
    public float dashStrikeTrackingMult    = 3.0f;
    [Tooltip("maxTurnRate multiplier during the strike — most impactful for homing precision.")]
    public float dashStrikeMaxTurnMult     = 4.0f;
    [Tooltip("steeringSmoothTime multiplier during the strike. Very low values kill angular inertia " +
             "so the enemy tracks instantly. 0.03 ≈ homing missile feel.")]
    public float dashStrikeSmoothTimeMult  = 0.03f;

    [Header("Collision Damage")]
    [Tooltip("Proximity radius (world units) that counts as a hit. Size to match the enemy's visual.")]
    public float hitRadius = 2f;
    [Tooltip("Seconds between successive hits so one strike can't drain health in a single frame.")]
    public float hitCooldown = 1f;

    // ── private state ──────────────────────────────────────────────────────────

    private SCRIPT_PlayerMovementController _playerHealth;

    // Base stat snapshots taken after Awake randomisation, restored on strike exit.
    private float _baseFlightSpeed;
    private float _baseTrackingGain;
    private float _baseMaxTurnRate;
    private float _baseSmoothTime;
    private float _baseBankStrength;

    private float _strikeCooldownRemaining;
    private float _hitCooldownRemaining;

    // ──────────────────────────────────────────────────────────────────────────

    protected override void Start()
    {
        base.Start();

        // Cache the player's health component via the target already found by the base.
        if (playerTarget != null)
            _playerHealth = playerTarget.GetComponent<SCRIPT_PlayerMovementController>();

        // Snapshot stats AFTER base.Awake() has applied per-instance randomisation.
        _baseFlightSpeed  = flightSpeed;
        _baseTrackingGain = trackingGain;
        _baseMaxTurnRate  = maxTurnRate;
        _baseSmoothTime   = steeringSmoothTime;
        _baseBankStrength = bankStrength;
    }

    void Update()
    {
        _strikeCooldownRemaining = Mathf.Max(0f, _strikeCooldownRemaining - Time.deltaTime);
        _hitCooldownRemaining    = Mathf.Max(0f, _hitCooldownRemaining    - Time.deltaTime);

        if (playerTarget == null) return;

        if (_state == FlightState.DashStriking)
        {
            // ── collision damage ───────────────────────────────────────────────
            if (_hitCooldownRemaining <= 0f && _playerHealth != null)
            {
                float sqrDist = (playerTarget.position - transform.position).sqrMagnitude;
                if (sqrDist <= hitRadius * hitRadius)
                {
                    _playerHealth.TakeDamage(damageOnCollision);
                    _hitCooldownRemaining = hitCooldown;
                }
            }

            // ── early exit: player is now behind us ────────────────────────────
            Vector3 toPlayer = playerTarget.position - transform.position;
            if (Vector3.Dot(transform.forward, toPlayer) < 0f)
                ExitDashStriking();
        }
        else if (_state == FlightState.Pursuing && _strikeCooldownRemaining <= 0f)
        {
            // ── dash strike trigger ────────────────────────────────────────────
            float sqrDist = (playerTarget.position - transform.position).sqrMagnitude;
            if (sqrDist <= dashStrikeRadius * dashStrikeRadius)
                BeginDashStrike();
        }
    }

    // ── dash strike lifecycle ──────────────────────────────────────────────────

    void BeginDashStrike()
    {
        flightSpeed        = _baseFlightSpeed  * dashStrikeSpeedMult;
        trackingGain       = _baseTrackingGain * dashStrikeTrackingMult;
        maxTurnRate        = _baseMaxTurnRate  * dashStrikeMaxTurnMult;
        steeringSmoothTime = _baseSmoothTime   * dashStrikeSmoothTimeMult;

        // Banking rolls the enemy's forward vector off-axis during hard turns, causing
        // the path to arc past the player instead of tracking precisely. Zero it out so
        // every FixedUpdate applies thrust in the exact computed heading direction.
        bankStrength = 0f;
        currentBank  = 0f;   // snap residual roll to flat immediately

        EnterDashStriking(dashStrikeDuration);
    }

    protected override void ExitDashStriking()
    {
        // Restore base stats before returning to pursuit.
        flightSpeed        = _baseFlightSpeed;
        trackingGain       = _baseTrackingGain;
        maxTurnRate        = _baseMaxTurnRate;
        steeringSmoothTime = _baseSmoothTime;
        bankStrength       = _baseBankStrength;

        _strikeCooldownRemaining = dashStrikeCooldown;

        base.ExitDashStriking(); // → EnterPursuing()
    }

    protected override Vector3 GetDashStrikeHeading()
    {
        // Fly directly at the player's current position — no wander, no bias.
        return playerTarget != null
            ? (playerTarget.position - transform.position)
            : transform.forward;
    }
}
