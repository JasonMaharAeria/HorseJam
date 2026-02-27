using UnityEngine;

/// <summary>
/// Enemy Type 2 — Lead Pursuer.
/// Predicts where the player will be in the near future and aims there,
/// instead of chasing the player's current position. This makes it feel
/// heavier and more "tactical" — it cuts off angles rather than just tailing.
///
/// Player velocity is estimated from frame-to-frame position delta,
/// so it works even though the player's Rigidbody is kinematic.
///
/// Suggested starting values:
///   flightSpeed        = 22
///   trackingGain       = 1.5
///   maxTurnRate        = 60
///   steeringSmoothTime = 0.35   (heavier, less agile)
///   lookAheadTime      = 0.6
///   bankStrength       = 20
/// </summary>
public class SCRIPT_EnemyType2 : SCRIPT_EnemyBase
{
    [Header("Type 2: Lead Pursuit")]
    [Tooltip("How many seconds ahead to predict the player's position. " +
             "Higher values = more aggressive intercept, but can overshoot.")]
    public float lookAheadTime = 0.6f;

    // ── player velocity estimation ─────────────────────────────────────────────
    private Vector3 prevPlayerPos;
    private Vector3 playerVelocity;

    protected override void Start()
    {
        base.Start();
        if (playerTarget != null)
            prevPlayerPos = playerTarget.position;
    }

    void Update()
    {
        if (playerTarget == null) return;

        // Estimate player velocity from positional delta each frame.
        // Using Update (not FixedUpdate) for a smooth per-frame estimate.
        if (Time.deltaTime > 0f)
            playerVelocity = (playerTarget.position - prevPlayerPos) / Time.deltaTime;

        prevPlayerPos = playerTarget.position;
    }

    protected override Vector3 GetDesiredHeading()
    {
        // Aim at where the player will be in lookAheadTime seconds.
        Vector3 predictedPos = playerTarget.position + playerVelocity * lookAheadTime;
        return predictedPos - transform.position;
    }
}
