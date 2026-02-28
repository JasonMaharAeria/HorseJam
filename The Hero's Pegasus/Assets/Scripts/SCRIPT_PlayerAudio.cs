using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// All player-side audio: wing flaps, wind rush, taking damage, laser beam,
/// dashing, and background music. Attach to the same GameObject as
/// SCRIPT_PlayerMovementController.
///
/// All AudioSources are created at runtime and routed through the SFX (or Music)
/// group in SCRIPT_AudioManager so that volume sliders in a settings menu
/// control them correctly.
///
/// ── Wing Flap setup ──────────────────────────────────────────────────────────
/// The wing flap sound should only play at the MIDPOINT of the wing-down stroke,
/// not on every frame. The cleanest way to do this is an AnimationEvent:
///   1. Open the Animator window, select the wing-flap animation clip.
///   2. Scrub to the frame where the wings are at their lowest point (midpoint).
///   3. Click "Add Event" in the Animation window.
///   4. Set the Function to "OnWingFlapMidpoint" (no parameters).
///   5. The event fires on the GameObject that has this component.
///
/// Unity only fires AnimationEvents from a blend tree state when that state's
/// blend weight is ≥ 0.5, so the sound is automatically suppressed during gliding.
/// An additional FlapBlend threshold check is applied as a secondary guard.
/// </summary>
[RequireComponent(typeof(SCRIPT_PlayerMovementController))]
public class SCRIPT_PlayerAudio : MonoBehaviour
{
    // ── Clip References ───────────────────────────────────────────────────────

    [Header("Clip — Wings")]
    [Tooltip("Short flap sound played at the midpoint of each wing-down stroke via AnimationEvent.")]
    public AudioClip wingFlapClip;

    [Header("Clip — Flight Wind")]
    [Tooltip("Looping ambient wind. Volume scales continuously with flight speed.")]
    public AudioClip windRushClip;

    [Header("Clips — Combat")]
    [Tooltip("Played once each time the player takes a hit.")]
    public AudioClip damageClip;
    [Tooltip("Looping laser beam tone. Starts on LMB press, stops on release.")]
    public AudioClip laserLoopClip;

    [Header("Clip — Dash")]
    [Tooltip("Looping dash whoosh. Pitch drops as stamina drains.")]
    public AudioClip dashLoopClip;

    [Header("Clip — Music")]
    [Tooltip("Background music track. Loops for the entire session.")]
    public AudioClip backgroundMusicClip;

    // ── Wind Rush Tuning ──────────────────────────────────────────────────────

    [Header("Wind Rush — Volume Curve")]
    [Tooltip("Speed at or below which wind volume is at its minimum.")]
    public float windMinSpeed        = 5f;
    [Tooltip("Speed at or above which wind volume reaches its maximum.")]
    public float windMaxSpeed        = 65f;
    [Range(0f, 1f)]
    [Tooltip("Volume when flying at windMinSpeed or below.")]
    public float windVolumeAtMin     = 0.05f;
    [Range(0f, 1f)]
    [Tooltip("Volume when flying at windMaxSpeed or above.")]
    public float windVolumeAtMax     = 1.0f;
    [Tooltip("How quickly the wind volume responds to speed changes. Higher = snappier.")]
    public float windVolumeSmoothing = 3f;

    // ── Dash Pitch Tuning ─────────────────────────────────────────────────────

    [Header("Dash — Pitch Curve")]
    [Tooltip("Pitch multiplier at the start of a dash when stamina is full.")]
    public float dashPitchFull  = 1.2f;
    [Tooltip("Pitch multiplier when stamina is nearly depleted.")]
    public float dashPitchEmpty = 0.7f;

    // ── Wing Flap Blend Gate ──────────────────────────────────────────────────

    [Header("Wing Flap — Blend Gate")]
    [Tooltip("Animator FlapBlend must exceed this value for the flap sound to play. " +
             "Prevents the sound while the pegasus is mostly gliding (blend near 0).")]
    [Range(0f, 1f)]
    public float flapSoundBlendThreshold = 0.3f;

    // ── Music Volume ──────────────────────────────────────────────────────────

    [Header("Music")]
    [Range(0f, 1f)]
    public float musicVolume = 0.6f;

    // ── Private State ─────────────────────────────────────────────────────────

    private SCRIPT_PlayerMovementController   _movement;
    private SCRIPT_LaserController            _laser;
    private SCRIPT_StaminaBar                 _stamina;
    private SCRIPT_PegasusAnimationController _anim;

    // One AudioSource per continuous layer; one-shots share _oneShotSource.
    private AudioSource _windRushSource;
    private AudioSource _laserSource;
    private AudioSource _dashSource;
    private AudioSource _bgmSource;
    private AudioSource _oneShotSource; // wing flap + damage

    private Vector3 _prevPos;
    private float   _currentSpeed;
    private float   _smoothWindVolume;
    private float   _prevHealth;
    private bool    _laserWasActive;
    private bool    _dashWasActive;

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _movement = GetComponent<SCRIPT_PlayerMovementController>();
    }

    void Start()
    {
        _laser   = GetComponent<SCRIPT_LaserController>();
        _stamina = GetComponentInChildren<SCRIPT_StaminaBar>();
        _anim    = GetComponentInChildren<SCRIPT_PegasusAnimationController>();

        AudioMixerGroup sfx   = SCRIPT_AudioManager.Instance != null
                                    ? SCRIPT_AudioManager.Instance.sfxGroup   : null;
        AudioMixerGroup music = SCRIPT_AudioManager.Instance != null
                                    ? SCRIPT_AudioManager.Instance.musicGroup  : null;

        _oneShotSource  = MakeSource(null,                  sfx,   loop: false, volume: 1f,         spatialBlend: 0f);
        _windRushSource = MakeSource(windRushClip,          sfx,   loop: true,  volume: 0f,         spatialBlend: 0f);
        _laserSource    = MakeSource(laserLoopClip,         sfx,   loop: true,  volume: 0f,         spatialBlend: 0f);
        _dashSource     = MakeSource(dashLoopClip,          sfx,   loop: true,  volume: 0f,         spatialBlend: 0f);
        _bgmSource      = MakeSource(backgroundMusicClip,   music, loop: true,  volume: musicVolume, spatialBlend: 0f);

        _prevPos          = transform.position;
        _smoothWindVolume = windVolumeAtMin;
        _prevHealth       = _movement != null ? _movement.CurrentHealth : 0f;

        // Start persistent loops.
        if (windRushClip        != null) _windRushSource.Play();
        if (backgroundMusicClip != null) _bgmSource.Play();
    }

    void Update()
    {
        // Estimate speed from position delta — works even with kinematic rigidbody.
        _currentSpeed = (transform.position - _prevPos).magnitude
                      / Mathf.Max(Time.deltaTime, 0.0001f);
        _prevPos = transform.position;

        UpdateWindRush();
        UpdateLaser();
        UpdateDash();
        PollForDamage();
    }

    // ── Wind Rush ─────────────────────────────────────────────────────────────

    void UpdateWindRush()
    {
        if (_windRushSource == null || windRushClip == null) return;

        float t             = Mathf.InverseLerp(windMinSpeed, windMaxSpeed, _currentSpeed);
        float targetVol     = Mathf.Lerp(windVolumeAtMin, windVolumeAtMax, t);
        float smoothT       = 1f - Mathf.Exp(-windVolumeSmoothing * Time.deltaTime);
        _smoothWindVolume   = Mathf.Lerp(_smoothWindVolume, targetVol, smoothT);
        _windRushSource.volume = _smoothWindVolume;
    }

    // ── Laser Loop ────────────────────────────────────────────────────────────

    void UpdateLaser()
    {
        if (_laserSource == null || _laser == null || laserLoopClip == null) return;

        bool active = _laser.IsLaserActive;

        if (active && !_laserWasActive)
        {
            _laserSource.clip   = laserLoopClip;
            _laserSource.volume = 1f;
            _laserSource.Play();
        }
        else if (!active && _laserWasActive)
        {
            _laserSource.Stop();
        }

        _laserWasActive = active;
    }

    // ── Dash Loop (pitch = f(stamina)) ────────────────────────────────────────

    void UpdateDash()
    {
        if (_dashSource == null) return;

        bool isDashing = _movement != null && _movement.IsDashing;

        if (isDashing && !_dashWasActive)
        {
            if (dashLoopClip != null)
            {
                _dashSource.clip   = dashLoopClip;
                _dashSource.volume = 1f;
                _dashSource.Play();
            }
        }
        else if (!isDashing && _dashWasActive)
        {
            _dashSource.Stop();
        }

        // Modulate pitch while dashing: full stamina → high pitch, empty → low pitch.
        if (isDashing && _dashSource.isPlaying && _stamina != null)
        {
            float frac        = _stamina.StaminaFraction;
            _dashSource.pitch = Mathf.Lerp(dashPitchEmpty, dashPitchFull, frac);
        }

        _dashWasActive = isDashing;
    }

    // ── Damage Polling ────────────────────────────────────────────────────────

    void PollForDamage()
    {
        if (_movement == null) return;

        float currentHealth = _movement.CurrentHealth;
        if (currentHealth < _prevHealth)
            PlayDamage();
        _prevHealth = currentHealth;
    }

    // ── Wing Flap — AnimationEvent Receiver ───────────────────────────────────

    /// <summary>
    /// Called by an AnimationEvent on the wing-flap clip at its midpoint frame.
    /// Unity only fires AnimationEvents when the state's blend weight ≥ 0.5,
    /// so this won't trigger during pure gliding. A secondary FlapBlend check
    /// is applied for extra safety.
    /// </summary>
    public void OnWingFlapMidpoint()
    {
        if (_oneShotSource == null || wingFlapClip == null) return;

        // Check animator blend — skip if mostly gliding.
        if (_anim != null && _anim.animator != null)
        {
            float blend = _anim.animator.GetFloat(_anim.flapBlendParam);
            if (blend < flapSoundBlendThreshold) return;
        }

        _oneShotSource.PlayOneShot(wingFlapClip);
    }

    // ── Damage One-Shot ───────────────────────────────────────────────────────

    public void PlayDamage()
    {
        if (_oneShotSource == null || damageClip == null) return;
        _oneShotSource.PlayOneShot(damageClip);
    }

    // ── AudioSource Factory ───────────────────────────────────────────────────

    AudioSource MakeSource(AudioClip clip, AudioMixerGroup group,
                           bool loop, float volume, float spatialBlend)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.clip                  = clip;
        src.outputAudioMixerGroup = group;
        src.loop                  = loop;
        src.volume                = volume;
        src.playOnAwake           = false;
        src.spatialBlend          = spatialBlend;
        return src;
    }
}
