using UnityEngine;

/// <summary>
/// Singleton that accumulates session stats (time survived, kills per enemy type)
/// throughout a run. Call Stop() on player death to freeze the timer.
/// </summary>
public class SCRIPT_GameStats : MonoBehaviour
{
    public static SCRIPT_GameStats Instance { get; private set; }

    // ── public read-only stats ─────────────────────────────────────────────────

    public float TimeSurvived { get; private set; }
    public int   KillsType1   { get; private set; }
    public int   KillsType2   { get; private set; }
    public int   KillsType3   { get; private set; }

    // ── private state ──────────────────────────────────────────────────────────

    private bool _stopped;

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Update()
    {
        if (!_stopped)
            TimeSurvived += Time.deltaTime;
    }

    /// <summary>
    /// Called by SCRIPT_EnemyBase.Die() for each enemy kill.
    /// Uses runtime type to increment the right counter.
    /// </summary>
    public void ReportKill(SCRIPT_EnemyBase enemy)
    {
        if (enemy is SCRIPT_EnemyType1) KillsType1++;
        else if (enemy is SCRIPT_EnemyType2) KillsType2++;
        else if (enemy is SCRIPT_EnemyType3) KillsType3++;
    }

    /// <summary>Freeze the timer. Called when the player dies.</summary>
    public void Stop() => _stopped = true;
}
