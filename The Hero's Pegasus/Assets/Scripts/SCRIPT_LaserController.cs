using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the player. Assign a particle system GameObject (child of the player,
/// or any scene object) to laserParticles. LMB activates it, release deactivates it.
/// </summary>
public class SCRIPT_LaserController : MonoBehaviour
{
    [Tooltip("The particle system GameObject to activate when firing")]
    public GameObject laserParticles;

    [Header("Laser Audio")]
    [Tooltip("Clip played by all 3 laser layers while firing.")]
    public AudioClip laserSFX;
    [Tooltip("Volume applied to each laser layer (0 = silent, 1 = full).")]
    [Range(0f, 1f)]
    public float volume = 0.5f;
    [Tooltip("Seconds between each layer start (layer 1 at t=0, layer 2 at +offset, layer 3 at +2*offset).")]
    [Min(0f)]
    public float layerStartOffset = 0.33f;
    [Tooltip("Seconds to fade all laser layers to silence after releasing the fire button.")]
    [Min(0f)]
    public float releaseFadeTime = 0.2f;

    // Cloud manager polls these to run particle-proximity destruction
    public bool IsLaserActive { get; private set; }
    public ParticleSystem LaserPS { get; private set; }

    private const int LayerCount = 3;
    private AudioSource[] _sources;
    private bool _laserAudioPlaying;
    private float _audioGate = 0f;

    // ── laser upgrade state ────────────────────────────────────────────────────

    // Base values captured from the particle system at Start.
    private float _baseStartSpeed;
    private float _baseStartSize;
    // Last-applied multiplier — used to skip redundant main-module writes.
    private float _appliedLaserMult = -1f;

    // ──────────────────────────────────────────────────────────────────────────

    void Start()
    {
        if (laserParticles == null)
        {
            Debug.LogWarning("SCRIPT_LaserController: no laserParticles assigned.", this);
            return;
        }

        LaserPS = laserParticles.GetComponentInChildren<ParticleSystem>();
        laserParticles.SetActive(false);

        if (LaserPS != null)
        {
            var main = LaserPS.main;
            _baseStartSpeed = main.startSpeed.constant;
            _baseStartSize  = main.startSize.constant;
        }

        BuildLayers();
    }

    void BuildLayers()
    {
        if (laserSFX == null) return;

        AudioMixerGroup sfxGroup = SCRIPT_AudioManager.Instance != null
                                       ? SCRIPT_AudioManager.Instance.sfxGroup : null;

        _sources = new AudioSource[LayerCount];
        for (int i = 0; i < LayerCount; i++)
        {
            AudioSource src = gameObject.AddComponent<AudioSource>();
            src.clip = laserSFX;
            src.loop = true;
            src.playOnAwake = false;
            src.volume = volume;
            src.spatialBlend = 0f;
            src.outputAudioMixerGroup = sfxGroup;
            _sources[i] = src;
        }
    }

    void Update()
    {
        if (laserParticles == null) return;

        ApplyLaserUpgradeIfChanged();

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            IsLaserActive = true;
            laserParticles.SetActive(true);
            if (LaserPS != null) LaserPS.Play(withChildren: true);
        }
        else if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            IsLaserActive = false;
            if (LaserPS != null) LaserPS.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
            laserParticles.SetActive(false);
        }

        UpdateLaserAudio();
    }

    void UpdateLaserAudio()
    {
        if (_sources == null || laserSFX == null) return;

        if (IsLaserActive && !_laserAudioPlaying)
        {
            StartLayeredLaserAudio();
        }

        if (!_laserAudioPlaying) return;

        if (IsLaserActive)
        {
            _audioGate = 1f;
        }
        else
        {
            float fadeRate = releaseFadeTime > 0f ? 1f / releaseFadeTime : 999f;
            _audioGate = Mathf.MoveTowards(_audioGate, 0f, fadeRate * Time.deltaTime);
        }

        float gatedVolume = volume * _audioGate;
        for (int i = 0; i < _sources.Length; i++)
        {
            if (_sources[i] != null) _sources[i].volume = gatedVolume;
        }

        if (!IsLaserActive && _audioGate <= 0f)
            StopLayeredLaserAudio();
    }

    void StartLayeredLaserAudio()
    {
        double startDsp = AudioSettings.dspTime;

        for (int i = 0; i < _sources.Length; i++)
        {
            AudioSource src = _sources[i];
            if (src == null) continue;

            src.Stop();
            src.clip = laserSFX;
            // Each layer loops independently once it starts.
            src.loop = true;
            src.volume = 0f;
            src.PlayScheduled(startDsp + (layerStartOffset * i));
        }

        _audioGate = 1f;
        _laserAudioPlaying = true;
    }

    void StopLayeredLaserAudio()
    {
        if (_sources == null)
        {
            _laserAudioPlaying = false;
            return;
        }

        for (int i = 0; i < _sources.Length; i++)
        {
            if (_sources[i] != null) _sources[i].Stop();
        }

        _audioGate = 0f;
        _laserAudioPlaying = false;
    }

    void OnDisable()
    {
        IsLaserActive = false;
        StopLayeredLaserAudio();
    }

    // ── laser upgrade ──────────────────────────────────────────────────────────

    void ApplyLaserUpgradeIfChanged()
    {
        if (LaserPS == null) return;

        float mult = SCRIPT_PlayerStats.Instance != null
                         ? SCRIPT_PlayerStats.Instance.LaserMultiplier
                         : 1f;

        if (mult == _appliedLaserMult) return;
        _appliedLaserMult = mult;

        var main = LaserPS.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(_baseStartSpeed * mult);
        main.startSize  = new ParticleSystem.MinMaxCurve(_baseStartSize  * mult);
    }
}
