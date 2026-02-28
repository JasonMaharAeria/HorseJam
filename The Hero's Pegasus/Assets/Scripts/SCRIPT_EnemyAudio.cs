using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Enemy flyby wind sound. Attach to each enemy prefab (Type 1, 2, and 3).
///
/// A looping 3D AudioSource provides the spatial "wind rush" as the enemy
/// passes near the player. Unity's logarithmic distance rolloff handles the
/// position-based volume automatically. On top of that, this script scales the
/// source's base volume by the enemy's flight speed and a per-prefab size
/// multiplier so large, fast enemies sound more imposing than small, slow ones.
///
/// Setup per prefab:
///   - Assign flybyWindClip (a looping wind / whoosh loop, ideally neutral pitch).
///   - Increase sizeVolumeMultiplier on larger enemy prefabs (e.g. 1.5 for big ones).
///   - Tune minAudioDistance / maxAudioDistance to match your world scale
///     (default 5 / 100 works for a scene where flightSpeed ≈ 15–30).
/// </summary>
[RequireComponent(typeof(SCRIPT_EnemyBase))]
public class SCRIPT_EnemyAudio : MonoBehaviour
{
    [Header("Clip")]
    [Tooltip("Looping wind sound played the entire time this enemy is alive.")]
    public AudioClip flybyWindClip;

    [Header("Volume Scaling")]
    [Tooltip("Enemy flight speed at which flyby volume reaches its maximum.")]
    public float maxSpeedReference     = 30f;
    [Tooltip("Multiplier applied on top of the speed-based volume. " +
             "Raise this on larger or more threatening enemy types.")]
    public float sizeVolumeMultiplier  = 1f;
    [Tooltip("Exponential smoothing rate for volume changes. Higher = snappier.")]
    public float volumeSmoothing       = 4f;

    [Header("3D Spatial Settings")]
    [Tooltip("Distance at which the flyby sound plays at full source volume.")]
    public float minAudioDistance      = 5f;
    [Tooltip("Distance at which the flyby sound becomes inaudible.")]
    public float maxAudioDistance      = 100f;

    // ──────────────────────────────────────────────────────────────────────────

    private SCRIPT_EnemyBase _enemy;
    private AudioSource      _source;

    void Awake()
    {
        _enemy = GetComponent<SCRIPT_EnemyBase>();
    }

    void Start()
    {
        AudioMixerGroup sfx = SCRIPT_AudioManager.Instance != null
                                  ? SCRIPT_AudioManager.Instance.sfxGroup : null;

        _source                        = gameObject.AddComponent<AudioSource>();
        _source.clip                   = flybyWindClip;
        _source.loop                   = true;
        _source.playOnAwake            = false;
        _source.spatialBlend           = 1f;                           // fully 3D
        _source.minDistance            = minAudioDistance;
        _source.maxDistance            = maxAudioDistance;
        _source.rolloffMode            = AudioRolloffMode.Logarithmic;
        _source.outputAudioMixerGroup  = sfx;
        _source.volume                 = 0f;                           // fade in via Update

        if (flybyWindClip != null)
            _source.Play();
    }

    void Update()
    {
        if (_source == null || flybyWindClip == null || _enemy == null) return;

        float speedFrac  = Mathf.Clamp01(_enemy.flightSpeed / Mathf.Max(maxSpeedReference, 0.001f));
        float targetVol  = speedFrac * sizeVolumeMultiplier;
        float smoothT    = 1f - Mathf.Exp(-volumeSmoothing * Time.deltaTime);

        _source.volume   = Mathf.Lerp(_source.volume, targetVol, smoothT);
    }
}
