using System.Collections;
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
    [Tooltip("Played once when the pegasus bounces off a floating island.")]
    public AudioClip islandBounceClip;
    [Tooltip("Single thump played twice in quick succession when health is below the danger threshold.")]
    public AudioClip heartbeatClip;
    [Tooltip("Looping laser beam tone. Starts on LMB press, stops on release.")]
    public AudioClip laserLoopClip;

    [Header("Clip — Dash")]
    [Tooltip("Looping dash whoosh. Pitch drops as stamina drains.")]
    public AudioClip dashLoopClip;

    [Header("Clip — Music")]
    [Tooltip("Background music track. Loops for the entire session.")]
    public AudioClip backgroundMusicClip;

    [Header("SFX Volume Knobs")]
    [Tooltip("Master multiplier for all non-music player SFX in this component.")]
    [Range(0f, 1f)]
    public float sfxMasterVolume = 1f;
    [Tooltip("Volume of wing flap one-shots.")]
    [Range(0f, 1f)]
    public float wingFlapVolume = 1f;
    [Tooltip("Volume of the player damage one-shot.")]
    [Range(0f, 1f)]
    public float damageVolume = 1f;
    [Tooltip("Volume of the island-bounce one-shot.")]
    [Range(0f, 1f)]
    public float islandBounceVolume = 1f;
    [Tooltip("Volume of the fallback looping laser tone.")]
    [Range(0f, 1f)]
    public float laserLoopVolume = 1f;
    [Tooltip("Volume of the dash looping whoosh.")]
    [Range(0f, 1f)]
    public float dashLoopVolume = 1f;
    [Tooltip("Additional multiplier applied to heartbeat volume.")]
    [Range(0f, 1f)]
    public float heartbeatVolumeMultiplier = 1f;

    // ── Wind Rush Tuning ──────────────────────────────────────────────────────

    [Header("Wind SFX")]
    [Tooltip("Short wind burst played once when a loop-di-loop or dash begins.")]
    [Range(0f, 1f)]
    public float windSFXVolume    = 0.3f;
    [Tooltip("Minimum seconds before the wind can trigger again after a play.")]
    public float windCooldownTime = 1.5f;

    // ── Dash Pitch Tuning ─────────────────────────────────────────────────────

    [Header("Dash — Pitch Curve")]
    [Tooltip("Pitch multiplier at the start of a dash when stamina is full.")]
    public float dashPitchFull      = 1.2f;
    [Tooltip("Pitch multiplier when stamina is nearly depleted.")]
    public float dashPitchEmpty     = 0.7f;
    [Tooltip("Seconds for the dash SFX to fade out after releasing the dash button.")]
    public float dashFadeOutTime    = 0.8f;
    [Tooltip("Maximum additional pitch added the longer dash is held continuously (resets to 0 on release).")]
    public float dashPitchHoldBonus = 0.12f;
    [Tooltip("Seconds of continuous holding to ramp from 0 to the full pitch bonus.")]
    public float dashPitchHoldRamp  = 3f;

    // ── Heartbeat Tuning ──────────────────────────────────────────────────────

    [Header("Heartbeat — Low Health")]
    [Tooltip("Health fraction (0–1) below which the heartbeat starts playing.")]
    [Range(0f, 1f)]
    public float heartbeatThreshold = 0.33f;
    [Tooltip("Volume of the heartbeat at exactly the threshold (subtle).")]
    [Range(0f, 1f)]
    public float heartbeatVolumeMin = 0.25f;
    [Tooltip("Volume of the heartbeat when health reaches 0 (full intensity).")]
    [Range(0f, 1f)]
    public float heartbeatVolumeMax = 1.0f;
    [Tooltip("Seconds between the first and second beat of each pair (the 'lub-dub' gap).")]
    public float heartbeatDoubleBeatGap = 0.18f;
    [Tooltip("Seconds between each heartbeat pair.")]
    public float heartbeatRepeatInterval = 1.1f;
    [Tooltip("Seconds for the heartbeat to fade out after health regens above the threshold.")]
    public float heartbeatFadeOutTime = 2.0f;

    // ── Wing Flap Blend Gate ──────────────────────────────────────────────────

    [Header("Wing Flap — Blend Gate")]
    [Tooltip("Animator FlapBlend must exceed this value for the flap sound to play. " +
             "Prevents the sound while the pegasus is mostly gliding (blend near 0).")]
    [Range(0f, 1f)]
    public float flapSoundBlendThreshold = 0.3f;

    [Header("Wing Flap — Fallback Trigger")]
    [Tooltip("Fallback trigger point within each flap cycle (normalized 0–1). Used if AnimationEvents are missing/not routed.")]
    [Range(0f, 1f)]
    public float flapMidpointNormalizedTime = 0.5f;
    [Tooltip("Minimum seconds between flap SFX plays. Prevents duplicate event+fallback triggers.")]
    [Min(0f)]
    public float flapMinInterval = 0.08f;

    // ── Music ─────────────────────────────────────────────────────────────────

    [Header("Music — Conditions")]
    [Tooltip("Minimum number of enemies within range before music can play.")]
    public int musicEnemyCountThreshold = 8;
    [Tooltip("Search radius (world units) for counting nearby enemies.")]
    public float musicEnemyRange = 500f;
    [Tooltip("Player health fraction (0–1) that must be at or below for music to play.")]
    [Range(0f, 1f)]
    public float musicHealthThreshold = 0.5f;

    [Header("Music — Volume")]
    [Range(0f, 1f)]
    [Tooltip("Target volume when all conditions are met.")]
    public float musicVolume = 0.6f;
    [Tooltip("Seconds to fade fully in or out when conditions change.")]
    public float musicFadeTime = 3.0f;

    // ── Private State ─────────────────────────────────────────────────────────

    private SCRIPT_PlayerMovementController   _movement;
    private SCRIPT_LaserController            _laser;
    private SCRIPT_StaminaBar                 _stamina;
    private SCRIPT_PegasusAnimationController _anim;
    private SCRIPT_HealthBar                  _healthBar;

    // One AudioSource per continuous layer; one-shots share _oneShotSource.
    private AudioSource _windRushSource;
    private AudioSource _laserSource;
    private AudioSource _dashSource;
    private AudioSource _bgmSource;
    private AudioSource _oneShotSource;    // wing flap + damage
    private AudioSource _heartbeatSource;  // dedicated so volume can be set per-beat

    private Vector3 _prevPos;
    private float   _prevHealth;
    private bool    _laserWasActive;
    private bool    _dashWasActive;
    private bool    _heartbeatRunning;
    private float   _heartbeatFadeMult;  // 0→1 fade envelope applied to each beat's volume
    private bool    _bgmActive;
    private float   _bgmCheckTimer;
    private float   _dashPitchOffset;    // random ±0.1 set when dash starts, held for duration
    private bool    _dashFading;         // true while the dash SFX is fading out after release
    private bool    _wasLooping;         // previous-frame IsLooping, for wind trigger
    private float   _windCooldown;       // prevents wind re-triggering immediately
    private int     _lastFlapEventCount = int.MinValue;
    private int     _lastFlapStateHash;
    private float   _nextFlapSFXTime;

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
        _healthBar = FindFirstObjectByType<SCRIPT_HealthBar>();

        AudioMixerGroup sfx   = SCRIPT_AudioManager.Instance != null
                                    ? SCRIPT_AudioManager.Instance.sfxGroup   : null;
        AudioMixerGroup music = SCRIPT_AudioManager.Instance != null
                                    ? SCRIPT_AudioManager.Instance.musicGroup  : null;

        _oneShotSource   = MakeSource(null,                  sfx,   loop: false, volume: 1f,          spatialBlend: 0f);
        _windRushSource  = MakeSource(windRushClip,          sfx,   loop: false, volume: 0f,          spatialBlend: 0f);
        _laserSource     = MakeSource(laserLoopClip,         sfx,   loop: true,  volume: 0f,          spatialBlend: 0f);
        _dashSource      = MakeSource(dashLoopClip,          sfx,   loop: true,  volume: 0f,          spatialBlend: 0f);
        _bgmSource       = MakeSource(backgroundMusicClip,   music, loop: true,  volume: 0f,           spatialBlend: 0f);
        _heartbeatSource = MakeSource(null,                  sfx,   loop: false, volume: 1f,          spatialBlend: 0f);

        _prevPos    = transform.position;
        _prevHealth = _movement != null ? _movement.CurrentHealth : 0f;

        // Start persistent loops.
        if (backgroundMusicClip != null) _bgmSource.Play();
    }

    void Update()
    {
        _prevPos = transform.position;

        UpdateWindSFX();
        UpdateLaser();
        UpdateDash();
        UpdateWingFlapFallback();
        PollForDamage();
        UpdateHeartbeat();
        UpdateBGM();
    }

    // ── Wind SFX ──────────────────────────────────────────────────────────────

    void UpdateWindSFX()
    {
        if (_windRushSource == null || windRushClip == null || _movement == null) return;

        _windCooldown = Mathf.Max(0f, _windCooldown - Time.deltaTime);

        bool loopJustStarted = _movement.IsLooping && !_wasLooping;
        bool dashJustStarted = _movement.IsDashing  && !_dashWasActive; // _dashWasActive from prev frame (UpdateDash runs after)
        _wasLooping = _movement.IsLooping;

        if (_windCooldown <= 0f && (loopJustStarted || dashJustStarted))
        {
            _windRushSource.pitch = SCRIPT_AudioManager.RandomPitch();
            _windRushSource.PlayOneShot(windRushClip, windSFXVolume * sfxMasterVolume);
            _windCooldown = windCooldownTime;
        }
    }

    // ── Laser Loop ────────────────────────────────────────────────────────────

    void UpdateLaser()
    {
        // If the laser controller is handling layered laser SFX, suppress this
        // fallback loop so the clip is not double-played.
        if (_laser != null && _laser.laserSFX != null)
        {
            if (_laserSource != null && _laserSource.isPlaying)
                _laserSource.Stop();

            _laserWasActive = _laser.IsLaserActive;
            return;
        }

        if (_laserSource == null || _laser == null || laserLoopClip == null) return;

        bool active = _laser.IsLaserActive;

        if (active && !_laserWasActive)
        {
            _laserSource.clip   = laserLoopClip;
            _laserSource.volume = laserLoopVolume * sfxMasterVolume;
            _laserSource.pitch  = SCRIPT_AudioManager.RandomPitch();
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
                _dashFading        = false;
                _dashPitchOffset   = SCRIPT_AudioManager.RandomPitch() - 1f;
                _dashSource.clip   = dashLoopClip;
                _dashSource.volume = dashLoopVolume * sfxMasterVolume;
                _dashSource.Play();
            }
        }
        else if (!isDashing && _dashWasActive)
        {
            _dashFading = true; // begin fade instead of hard stop
        }

        // Fade out smoothly after release.
        if (_dashFading && _dashSource.isPlaying)
        {
            float fadeRate     = dashFadeOutTime > 0f ? 1f / dashFadeOutTime : float.MaxValue;
            _dashSource.volume = Mathf.MoveTowards(_dashSource.volume, 0f, fadeRate * Time.deltaTime);
            if (_dashSource.volume <= 0f)
            {
                _dashSource.Stop();
                _dashFading = false;
            }
        }

        // Modulate pitch while dashing: full stamina → high pitch, empty → low pitch,
        // plus a subtle upward ramp the longer dash is held continuously.
        if (isDashing && _dashSource.isPlaying && _stamina != null)
        {
            _dashSource.volume = dashLoopVolume * sfxMasterVolume;
            float frac        = _stamina.StaminaFraction;
            float holdT       = _movement != null && dashPitchHoldRamp > 0f
                                    ? Mathf.Clamp01(_movement.DashHoldTime / dashPitchHoldRamp)
                                    : 0f;
            _dashSource.pitch = Mathf.Lerp(dashPitchEmpty, dashPitchFull, frac)
                              + _dashPitchOffset
                              + holdT * dashPitchHoldBonus;
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

    void UpdateWingFlapFallback()
    {
        if (_oneShotSource == null || wingFlapClip == null || _anim == null || _anim.animator == null) return;

        Animator animator = _anim.animator;
        float blend = animator.GetFloat(_anim.flapBlendParam);
        if (blend < flapSoundBlendThreshold)
        {
            _lastFlapEventCount = int.MinValue;
            return;
        }

        if (animator.IsInTransition(0)) return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        int stateHash = state.fullPathHash;
        int flapEventCount = Mathf.FloorToInt(state.normalizedTime - flapMidpointNormalizedTime) + 1;

        if (_lastFlapEventCount == int.MinValue || stateHash != _lastFlapStateHash)
        {
            _lastFlapEventCount = flapEventCount;
            _lastFlapStateHash = stateHash;
            return;
        }

        if (flapEventCount > _lastFlapEventCount)
        {
            _lastFlapEventCount = flapEventCount;
            OnWingFlapMidpoint();
        }
    }

    /// <summary>
    /// Called by an AnimationEvent on the wing-flap clip at its midpoint frame.
    /// Unity only fires AnimationEvents when the state's blend weight ≥ 0.5,
    /// so this won't trigger during pure gliding. A secondary FlapBlend check
    /// is applied for extra safety.
    /// </summary>
    public void OnWingFlapMidpoint()
    {
        if (_oneShotSource == null || wingFlapClip == null) return;
        if (Time.time < _nextFlapSFXTime) return;

        // Check animator blend — skip if mostly gliding.
        if (_anim != null && _anim.animator != null)
        {
            float blend = _anim.animator.GetFloat(_anim.flapBlendParam);
            if (blend < flapSoundBlendThreshold) return;
        }

        _oneShotSource.pitch = SCRIPT_AudioManager.RandomPitch();
        _oneShotSource.PlayOneShot(wingFlapClip, wingFlapVolume * sfxMasterVolume);
        _nextFlapSFXTime = Time.time + flapMinInterval;
    }

    // ── Damage One-Shot ───────────────────────────────────────────────────────

    public void PlayDamage()
    {
        if (_oneShotSource == null || damageClip == null) return;
        _oneShotSource.pitch = SCRIPT_AudioManager.RandomPitch();
        _oneShotSource.PlayOneShot(damageClip, damageVolume * sfxMasterVolume);
    }

    public void PlayIslandBounce()
    {
        if (_oneShotSource == null || islandBounceClip == null) return;
        _oneShotSource.pitch = SCRIPT_AudioManager.RandomPitch();
        _oneShotSource.PlayOneShot(islandBounceClip, islandBounceVolume * sfxMasterVolume);
    }

    // ── Heartbeat ─────────────────────────────────────────────────────────────

    float GetHealthFraction()
    {
        // Health bar is the source of truth for regeneration.
        if (_healthBar == null)
            _healthBar = FindFirstObjectByType<SCRIPT_HealthBar>();

        if (_healthBar != null)
            return Mathf.Clamp01(_healthBar.HealthFraction);

        if (_movement == null)
            return 0f;

        return Mathf.Clamp01(_movement.CurrentHealth / Mathf.Max(_movement.maxHealth, 0.0001f));
    }

    void UpdateHeartbeat()
    {
        if (heartbeatClip == null || _movement == null) return;

        float healthFrac = GetHealthFraction();
        bool  inDanger   = healthFrac < heartbeatThreshold && _movement.IsAlive;

        if (inDanger)
        {
            // Snap in immediately when danger begins.
            _heartbeatFadeMult = 1f;
        }
        else
        {
            // Smoothly fade out when health recovers.
            float fadeRate     = 1f / Mathf.Max(heartbeatFadeOutTime, 0.01f);
            _heartbeatFadeMult = Mathf.MoveTowards(_heartbeatFadeMult, 0f, fadeRate * Time.deltaTime);
        }

        bool shouldRun = _heartbeatFadeMult > 0f;

        if (shouldRun && !_heartbeatRunning)
        {
            _heartbeatRunning = true;
            StartCoroutine(HeartbeatCoroutine());
        }
        else if (!shouldRun && _heartbeatRunning)
        {
            _heartbeatRunning = false;
        }
    }

    IEnumerator HeartbeatCoroutine()
    {
        // Keep _heartbeatSource.volume at 1 so PlayOneShot's volumeScale is the actual volume.
        _heartbeatSource.volume = 1f;

        while (_heartbeatRunning)
        {
            float healthFrac = GetHealthFraction();

            // 0 at threshold, 1 at 0 hp → maps to volume min→max, then scaled by fade envelope.
            float danger = Mathf.InverseLerp(heartbeatThreshold, 0f, healthFrac);
            float vol    = Mathf.Lerp(heartbeatVolumeMin, heartbeatVolumeMax, danger)
                         * _heartbeatFadeMult
                         * heartbeatVolumeMultiplier
                         * sfxMasterVolume;

            // Randomise pitch once per pair so both beats share the same subtle variation.
            _heartbeatSource.pitch = SCRIPT_AudioManager.RandomPitch();

            // First beat (lub).
            _heartbeatSource.PlayOneShot(heartbeatClip, vol);

            yield return new WaitForSeconds(heartbeatDoubleBeatGap);
            if (!_heartbeatRunning) yield break;

            // Second beat (dub) — slightly quieter for a natural lub-dub asymmetry.
            _heartbeatSource.PlayOneShot(heartbeatClip, vol * 0.75f);

            yield return new WaitForSeconds(heartbeatRepeatInterval);
        }
    }

    // ── Background Music ──────────────────────────────────────────────────────

    void UpdateBGM()
    {
        if (_bgmSource == null || backgroundMusicClip == null) return;

        // Re-evaluate conditions every 0.5 s to avoid per-frame FindObjectsByType calls.
        _bgmCheckTimer -= Time.deltaTime;
        if (_bgmCheckTimer <= 0f)
        {
            _bgmCheckTimer = 0.5f;
            _bgmActive     = CheckBGMConditions();
        }

        float target   = _bgmActive ? musicVolume : 0f;
        float fadeRate = musicFadeTime > 0f ? musicVolume / musicFadeTime : float.MaxValue;
        _bgmSource.volume = Mathf.MoveTowards(_bgmSource.volume, target, fadeRate * Time.deltaTime);
    }

    bool CheckBGMConditions()
    {
        if (_movement == null || !_movement.IsAlive) return false;

        float healthFrac = GetHealthFraction();
        if (healthFrac > musicHealthThreshold) return false;

        float rangeSqr = musicEnemyRange * musicEnemyRange;
        int   count    = 0;
        foreach (SCRIPT_EnemyBase e in FindObjectsByType<SCRIPT_EnemyBase>(FindObjectsSortMode.None))
        {
            if ((e.transform.position - transform.position).sqrMagnitude <= rangeSqr)
                if (++count > musicEnemyCountThreshold) return true;
        }
        return false;
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
