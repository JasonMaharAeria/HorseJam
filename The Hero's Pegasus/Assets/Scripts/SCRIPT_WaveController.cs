using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Infinite wave spawner. Waves run forever — difficulty escalates automatically
/// via simple per-wave formulas for spawn rate and concurrent cap, and
/// AnimationCurves for how each enemy type's spawn weight changes over time.
///
/// Wave N runs for <see cref="waveDuration"/> seconds, then a
/// <see cref="repriveDuration"/> second pause triggers before wave N+1 begins.
/// </summary>
public class SCRIPT_WaveController : MonoBehaviour
{
    [Header("Enemy Prefabs")]
    public GameObject enemyType1Prefab;
    public GameObject enemyType2Prefab;
    public GameObject enemyType3Prefab;

    [Header("Timing")]
    [Tooltip("How long each wave lasts in seconds.")]
    public float waveDuration    = 120f;
    [Tooltip("Pause between waves in seconds.")]
    public float repriveDuration = 10f;

    [Header("Spawn Position")]
    [Tooltip("Center point for spawning. Auto-finds 'Player' tag if left empty.")]
    public Transform spawnCenter;
    [Tooltip("Distance from spawnCenter at which enemies appear.")]
    public float spawnRadius = 150f;

    [Header("Spawn Rate Scaling")]
    [Tooltip("Seconds between spawns on wave 1.")]
    public float startSpawnInterval = 6f;
    [Tooltip("Interval shrinks by this many seconds per wave.")]
    public float spawnIntervalDecayPerWave = 0.4f;
    [Tooltip("Minimum spawn interval — never faster than this regardless of wave number.")]
    public float minSpawnInterval = 1f;

    [Header("Concurrent Enemy Scaling")]
    [Tooltip("Maximum enemies alive simultaneously on wave 1.")]
    public int startMaxConcurrent = 5;
    [Tooltip("Max concurrent increases by this many each wave.")]
    public int concurrentIncreasePerWave = 2;
    [Tooltip("Hard upper cap on concurrent enemies.")]
    public int maxConcurrentCap = 30;

    [Header("Audio")]
    [Tooltip("Played (2D) when a new wave begins, including wave 1.")]
    public AudioClip waveStartClip;
    [Tooltip("Played (2D) when a wave ends and the reprieve begins.")]
    public AudioClip waveCompleteClip;

    [Header("Enemy Type Weight Curves")]
    [Tooltip("Spawn weight of Type 1 over wave number. X axis = wave, Y axis = weight. " +
             "Only ratios matter — weights are normalised at runtime.")]
    public AnimationCurve type1Curve = AnimationCurve.Constant(1, 20, 1f);
    [Tooltip("Spawn weight of Type 2. Set a rising curve to introduce it gradually.")]
    public AnimationCurve type2Curve = new AnimationCurve(
        new Keyframe(1, 0), new Keyframe(4, 1));
    [Tooltip("Spawn weight of Type 3. Set a rising curve to introduce it later.")]
    public AnimationCurve type3Curve = new AnimationCurve(
        new Keyframe(1, 0), new Keyframe(7, 1));

    // ── runtime state (read-only, visible in inspector for live debugging) ─────
    [Header("Runtime State (read-only)")]
    [SerializeField] private int   _currentWave = 1;
    [SerializeField] private bool  _inReprieve  = false;

    /// <summary>Current wave number. Read by SCRIPT_DeathScreen for the end-of-run stats.</summary>
    public int CurrentWave => _currentWave;

    /// <summary>Set to true by SCRIPT_PlayerMovementController on player death. Halts all spawning.</summary>
    public bool IsStopped { get; private set; }

    /// <summary>Stop all future spawning. Safe to call multiple times.</summary>
    public void Stop() => IsStopped = true;
    [SerializeField] private float _phaseTimer  = 0f;
    [SerializeField] private float _spawnInterval;
    [SerializeField] private int   _maxConcurrent;
    [SerializeField] private int   _aliveCount;

    private float _spawnTimer;
    private readonly List<SCRIPT_EnemyBase> _activeEnemies = new List<SCRIPT_EnemyBase>();
    private AudioSource _waveAudioSource;

    // ──────────────────────────────────────────────────────────────────────────

    void Start()
    {
        if (spawnCenter == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
                spawnCenter = player.transform;
            else
                Debug.LogWarning("[WaveController] No 'Player' tagged object found for spawn center.", this);
        }

        // Set up a 2D AudioSource for wave stings, routed through the SFX mixer group.
        AudioMixerGroup sfx = SCRIPT_AudioManager.Instance != null
                                  ? SCRIPT_AudioManager.Instance.sfxGroup : null;
        _waveAudioSource                       = gameObject.AddComponent<AudioSource>();
        _waveAudioSource.outputAudioMixerGroup = sfx;
        _waveAudioSource.spatialBlend          = 0f;
        _waveAudioSource.playOnAwake           = false;

        RefreshWaveParams();
        PlayWaveSound(waveStartClip);
        Debug.Log($"[WaveController] Wave {_currentWave} started. " +
                  $"Interval={_spawnInterval:F1}s  MaxConcurrent={_maxConcurrent}");
    }

    void Update()
    {
        if (IsStopped) return;

        _phaseTimer += Time.deltaTime;

        if (_inReprieve)
        {
            if (_phaseTimer >= repriveDuration)
                StartNextWave();
            return;
        }

        if (_phaseTimer >= waveDuration)
        {
            BeginReprieve();
            return;
        }

        HandleSpawning();
    }

    // ── wave lifecycle ─────────────────────────────────────────────────────────

    void BeginReprieve()
    {
        _inReprieve = true;
        _phaseTimer = 0f;
        PlayWaveSound(waveCompleteClip);
        Debug.Log($"[WaveController] Wave {_currentWave} complete — reprieve for {repriveDuration}s.");
    }

    void StartNextWave()
    {
        _currentWave++;
        _inReprieve  = false;
        _phaseTimer  = 0f;
        _spawnTimer  = 0f;
        RefreshWaveParams();
        PlayWaveSound(waveStartClip);
        Debug.Log($"[WaveController] Wave {_currentWave} started. " +
                  $"Interval={_spawnInterval:F1}s  MaxConcurrent={_maxConcurrent}");
    }

    /// <summary>Recomputes this wave's spawn interval and concurrent cap from the current wave number.</summary>
    void RefreshWaveParams()
    {
        _spawnInterval = Mathf.Max(minSpawnInterval,
            startSpawnInterval - (_currentWave - 1) * spawnIntervalDecayPerWave);

        _maxConcurrent = Mathf.Min(maxConcurrentCap,
            startMaxConcurrent + (_currentWave - 1) * concurrentIncreasePerWave);
    }

    // ── spawning ───────────────────────────────────────────────────────────────

    void HandleSpawning()
    {
        _spawnTimer += Time.deltaTime;
        if (_spawnTimer >= _spawnInterval)
        {
            _spawnTimer = 0f;
            TrySpawnEnemy();
        }
    }

    void TrySpawnEnemy()
    {
        _activeEnemies.RemoveAll(e => e == null);
        _aliveCount = _activeEnemies.Count;

        if (_aliveCount >= _maxConcurrent) return;

        GameObject prefab = PickPrefab();
        if (prefab == null)
        {
            Debug.LogWarning("[WaveController] Selected enemy prefab is not assigned.", this);
            return;
        }

        Vector3 center   = spawnCenter != null ? spawnCenter.position : Vector3.zero;
        Vector3 spawnPos = center + Random.onUnitSphere * spawnRadius;

        // Pass rotation into Instantiate so the enemy's Awake() seeds currentYaw/currentPitch correctly.
        Vector3 toCenter = center - spawnPos;
        Quaternion spawnRot = toCenter.sqrMagnitude > 0.001f
                            ? Quaternion.LookRotation(toCenter.normalized)
                            : Quaternion.identity;

        GameObject       go    = Instantiate(prefab, spawnPos, spawnRot);
        SCRIPT_EnemyBase enemy = go.GetComponent<SCRIPT_EnemyBase>();
        if (enemy != null)
        {
            _activeEnemies.Add(enemy);
            _aliveCount++;
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    void PlayWaveSound(AudioClip clip)
    {
        if (_waveAudioSource == null || clip == null) return;
        _waveAudioSource.PlayOneShot(clip);
    }

    /// <summary>Samples each type's weight at the current wave and picks a prefab via weighted random.</summary>
    GameObject PickPrefab()
    {
        float w1 = Mathf.Max(0f, type1Curve.Evaluate(_currentWave));
        float w2 = Mathf.Max(0f, type2Curve.Evaluate(_currentWave));
        float w3 = Mathf.Max(0f, type3Curve.Evaluate(_currentWave));
        float total = w1 + w2 + w3;

        if (total <= 0f) return enemyType1Prefab;

        float roll = Random.Range(0f, total);
        if (roll < w1)           return enemyType1Prefab;
        if (roll < w1 + w2)      return enemyType2Prefab;
        return                          enemyType3Prefab;
    }
}
