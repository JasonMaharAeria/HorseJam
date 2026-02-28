using UnityEngine;

/// <summary>
/// Attach to the SAME GameObject as the laser ParticleSystem.
///
/// Each Update this script reads every live particle, finds the nearest
/// SCRIPT_EnemyBase within seekRadius, and bends that particle's velocity
/// toward that enemy at seekTurnRate degrees/second. Particles outside
/// seekRadius are left completely alone, so the beam still fires straight
/// until it gets close to a target.
///
/// Works with both World and Local simulation spaces.
/// World simulation space is strongly recommended for long-range lasers.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class SCRIPT_LaserSeeker : MonoBehaviour
{
    [Tooltip("World-space radius within which a particle begins homing toward the nearest enemy.")]
    public float seekRadius = 15f;

    [Tooltip("How aggressively particles curve toward the enemy (degrees per second). " +
             "Higher values = tighter tracking. 90–270 gives a satisfying but not instant snap.")]
    public float seekTurnRate = 180f;

    [Tooltip("How often the enemy list is rebuilt (seconds). " +
             "0.2 is a good balance: cheap, but responsive to enemies spawning / dying.")]
    public float enemyRefreshInterval = 0.2f;

    // ── private state ──────────────────────────────────────────────────────────

    private ParticleSystem              _ps;
    private ParticleSystem.Particle[]   _particles;
    private SCRIPT_EnemyBase[]          _enemies;
    private float                       _refreshTimer;

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
    }

    void Update()
    {
        RefreshEnemiesIfNeeded();

        if (_enemies == null || _enemies.Length == 0) return;

        // Resize the reusable buffer if the particle cap has changed.
        int maxParticles = _ps.main.maxParticles;
        if (_particles == null || _particles.Length < maxParticles)
            _particles = new ParticleSystem.Particle[maxParticles];

        int count = _ps.GetParticles(_particles);
        if (count == 0) return;

        bool worldSpace   = _ps.main.simulationSpace == ParticleSystemSimulationSpace.World;
        float sqrRadius   = seekRadius * seekRadius;
        float turnRad     = seekTurnRate * Mathf.Deg2Rad * Time.deltaTime;
        bool  anyModified = false;

        for (int i = 0; i < count; i++)
        {
            // Resolve world-space position regardless of simulation space.
            Vector3 worldPos = worldSpace
                ? _particles[i].position
                : transform.TransformPoint(_particles[i].position);

            // ── find nearest enemy within seek radius ──────────────────────────
            SCRIPT_EnemyBase nearest     = null;
            float            nearestSqr  = sqrRadius;

            for (int e = 0; e < _enemies.Length; e++)
            {
                if (_enemies[e] == null) continue;
                float sqrDist = (_enemies[e].transform.position - worldPos).sqrMagnitude;
                if (sqrDist < nearestSqr)
                {
                    nearestSqr = sqrDist;
                    nearest    = _enemies[e];
                }
            }

            if (nearest == null) continue;

            // ── bend velocity toward the enemy ─────────────────────────────────
            Vector3 vel   = _particles[i].velocity;
            float   speed = vel.magnitude;
            if (speed < 0.001f) continue;

            // Direction to enemy in the particle's own simulation space.
            Vector3 toEnemy = nearest.transform.position - worldPos;
            if (!worldSpace)
                toEnemy = transform.InverseTransformDirection(toEnemy);

            // RotateTowards keeps speed constant and clamps the turn per frame.
            Vector3 newDir = Vector3.RotateTowards(vel / speed, toEnemy.normalized, turnRad, 0f);
            _particles[i].velocity = newDir * speed;
            anyModified = true;
        }

        // Only push data back when something actually changed — avoids a CPU↔GPU
        // sync every frame when no enemies are in range.
        if (anyModified)
            _ps.SetParticles(_particles, count);
    }

    // ──────────────────────────────────────────────────────────────────────────

    void RefreshEnemiesIfNeeded()
    {
        _refreshTimer -= Time.deltaTime;
        if (_refreshTimer > 0f) return;

        _refreshTimer = enemyRefreshInterval;
        _enemies = FindObjectsByType<SCRIPT_EnemyBase>(FindObjectsSortMode.None);
    }
}
