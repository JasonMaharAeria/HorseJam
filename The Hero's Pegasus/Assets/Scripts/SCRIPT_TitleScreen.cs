using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Title screen panel. Displays Play and Quit buttons, then slides off-screen to
/// the left when Play is pressed and hands control to the wave spawner and player.
///
/// Also serves as the pause menu — a downward mouse flick (SCRIPT_PlayerMovementController)
/// calls TogglePause(), which slides the panel back in and freezes the game.
/// The button label swaps between "START" and "CONTINUE" automatically.
///
/// Scene setup:
///   1. Create a full-screen Panel child of your Canvas. Attach this script to it.
///   2. Assign the Panel's own RectTransform to 'panel'.
///   3. Add Play and Quit Button children; assign them below.
///   4. Assign the Play button's TMP_Text child to 'playButtonLabel'.
///   5. On SCRIPT_WaveController, set startOnLoad = false.
///   6. On SCRIPT_PlayerMovementController, set startFrozen = true.
///   7. Assign SFX clips (beginClip, hoverClip, clickClip) in the Inspector.
/// </summary>
public class SCRIPT_TitleScreen : MonoBehaviour
{
    public static SCRIPT_TitleScreen Instance { get; private set; }

    [Header("Panel")]
    [Tooltip("RectTransform of the title panel. Slides off-screen to the left when Play is pressed.")]
    public RectTransform panel;

    [Header("Buttons")]
    public Button   playButton;
    public Button   quitButton;
    [Tooltip("TMP_Text label on the Play button. Swaps between 'START' and 'CONTINUE' when pausing.")]
    public TMP_Text playButtonLabel;

    [Header("SFX")]
    [Tooltip("One-shot played the moment Play is pressed (game-start fanfare).")]
    public AudioClip beginClip;
    [Tooltip("One-shot played when the mouse enters either button.")]
    public AudioClip hoverClip;
    [Tooltip("One-shot played when either button is clicked.")]
    public AudioClip clickClip;
    [Range(0f, 1f)]
    public float sfxVolume = 1f;

    [Header("Slide Animation")]
    [Tooltip("Seconds for the panel to slide fully off-screen to the left.")]
    public float slideOutDuration = 0.55f;
    [Tooltip("Seconds for the panel to slide back in from the left when pausing.")]
    public float slideInDuration  = 0.45f;

    [Header("Button Hover Animation")]
    [Tooltip("Peak scale during the bubble-pop hover animation.")]
    public float buttonHoverScale    = 1.1f;
    [Tooltip("Seconds for the pop to reach its peak then settle.")]
    public float buttonHoverDuration = 0.22f;

    // ── public state ──────────────────────────────────────────────────────────

    /// <summary>True while the game is paused via TogglePause. Read by SCRIPT_PlayerMovementController.</summary>
    public bool IsGamePaused { get; private set; }

    // ── private state ─────────────────────────────────────────────────────────

    private AudioSource _sfxSource;
    private SCRIPT_PlayerMovementController _player;
    private SCRIPT_WaveController           _waves;
    private bool  _hasStarted;
    private float _originalAnchoredX;
    private Coroutine _slideCoroutine;

    // Per-button active bubble coroutine so rapid hovers cancel cleanly.
    private readonly Dictionary<Button, Coroutine> _bubbles = new();

    // ── lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Free the cursor immediately so the player can interact with the UI.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        if (playButton != null)
        {
            playButton.onClick.AddListener(OnPlay);
            WireButtonEvents(playButton);
        }

        if (quitButton != null)
        {
            quitButton.onClick.AddListener(OnQuit);
            WireButtonEvents(quitButton);
        }
    }

    void Start()
    {
        _player = FindFirstObjectByType<SCRIPT_PlayerMovementController>();
        _waves  = FindFirstObjectByType<SCRIPT_WaveController>();

        // Remember the panel's home position so we can slide it back here when pausing.
        if (panel != null)
            _originalAnchoredX = panel.anchoredPosition.x;

        AudioMixerGroup sfx = SCRIPT_AudioManager.Instance != null
                                  ? SCRIPT_AudioManager.Instance.sfxGroup : null;
        _sfxSource = gameObject.AddComponent<AudioSource>();
        _sfxSource.outputAudioMixerGroup = sfx;
        _sfxSource.spatialBlend          = 0f;
        _sfxSource.playOnAwake           = false;
    }

    // ── button event wiring ───────────────────────────────────────────────────

    void WireButtonEvents(Button btn)
    {
        var trigger = btn.gameObject.AddComponent<EventTrigger>();

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => { PlaySFX(hoverClip); BubblePop(btn); });
        trigger.triggers.Add(enter);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => BubbleShrink(btn));
        trigger.triggers.Add(exit);

        var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        click.callback.AddListener(_ => PlaySFX(clickClip));
        trigger.triggers.Add(click);
    }

    // ── hover animation ───────────────────────────────────────────────────────

    void BubblePop(Button btn)
    {
        RestartBubble(btn, ScaleTo(btn.transform, Vector3.one * buttonHoverScale,
                                   buttonHoverDuration, overshootPop: true));
    }

    void BubbleShrink(Button btn)
    {
        RestartBubble(btn, ScaleTo(btn.transform, Vector3.one,
                                   buttonHoverDuration, overshootPop: false));
    }

    void RestartBubble(Button btn, IEnumerator routine)
    {
        if (_bubbles.TryGetValue(btn, out var existing) && existing != null)
            StopCoroutine(existing);
        _bubbles[btn] = StartCoroutine(routine);
    }

    IEnumerator ScaleTo(Transform t, Vector3 target, float duration, bool overshootPop)
    {
        Vector3 start   = t.localScale;
        float   elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p     = Mathf.Clamp01(elapsed / duration);
            float eased = overshootPop ? EaseOutBack(p) : Mathf.SmoothStep(0f, 1f, p);
            t.localScale = Vector3.LerpUnclamped(start, target, eased);
            yield return null;
        }

        t.localScale = target;
    }

    // Ease-out-back: overshoots slightly then settles, giving the "pop" feel.
    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    // ── play / quit ───────────────────────────────────────────────────────────

    void OnPlay()
    {
        // If the game is paused, this button acts as CONTINUE.
        if (IsGamePaused)
        {
            if (playButton != null) playButton.interactable = false;
            if (quitButton != null) quitButton.interactable = false;
            RestartSlide(SlideOutAndResume());
            return;
        }

        if (_hasStarted) return;
        _hasStarted = true;

        PlaySFX(beginClip);

        if (playButton != null) playButton.interactable = false;
        if (quitButton != null) quitButton.interactable = false;

        RestartSlide(SlideOutAndBegin());
    }

    // ── slide helpers ─────────────────────────────────────────────────────────

    void RestartSlide(IEnumerator routine)
    {
        if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
        _slideCoroutine = StartCoroutine(routine);
    }

    IEnumerator SlideOutAndBegin()
    {
        if (panel != null)
        {
            float startX  = panel.anchoredPosition.x;
            float endX    = startX - panel.rect.width;
            float elapsed = 0f;

            while (elapsed < slideOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / slideOutDuration));
                panel.anchoredPosition = new Vector2(Mathf.Lerp(startX, endX, p),
                                                     panel.anchoredPosition.y);
                yield return null;
            }
        }

        // Hand control to the game systems.
        if (_player != null) _player.StartGame();
        if (_waves  != null) _waves.StartWaves();

        if (panel != null) panel.gameObject.SetActive(false);
    }

    // ── pause / resume ────────────────────────────────────────────────────────

    /// <summary>
    /// Pause if running, resume if paused. Safe to call from anywhere (e.g. player flick gesture).
    /// No-ops if the game hasn't started yet.
    /// </summary>
    public void TogglePause()
    {
        if (!_hasStarted) return;
        if (IsGamePaused)
            OnPlay(); // CONTINUE path inside OnPlay handles resume
        else
            Pause();
    }

    void Pause()
    {
        if (IsGamePaused) return;
        IsGamePaused     = true;
        Time.timeScale   = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        // Snap panel to its off-screen-left starting point, then animate it in.
        if (panel != null)
        {
            panel.gameObject.SetActive(true);
            panel.anchoredPosition = new Vector2(
                _originalAnchoredX - panel.rect.width,
                panel.anchoredPosition.y);
        }

        if (playButtonLabel != null) playButtonLabel.text = "CONTINUE";
        if (playButton  != null)     playButton.interactable = true;
        if (quitButton  != null)     quitButton.interactable = true;

        PlaySFX(beginClip);
        RestartSlide(SlideIn());
    }

    IEnumerator SlideIn()
    {
        if (panel == null) yield break;

        float startX  = panel.anchoredPosition.x;
        float endX    = _originalAnchoredX;
        float elapsed = 0f;

        while (elapsed < slideInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / slideInDuration));
            panel.anchoredPosition = new Vector2(Mathf.Lerp(startX, endX, p),
                                                 panel.anchoredPosition.y);
            yield return null;
        }

        panel.anchoredPosition = new Vector2(endX, panel.anchoredPosition.y);
    }

    IEnumerator SlideOutAndResume()
    {
        if (panel != null)
        {
            float startX  = panel.anchoredPosition.x;
            float endX    = startX - panel.rect.width;
            float elapsed = 0f;

            while (elapsed < slideOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / slideOutDuration));
                panel.anchoredPosition = new Vector2(Mathf.Lerp(startX, endX, p),
                                                     panel.anchoredPosition.y);
                yield return null;
            }
        }

        // Resume everything.
        IsGamePaused     = false;
        Time.timeScale   = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        if (playButtonLabel != null) playButtonLabel.text = "START";
        if (panel != null)           panel.gameObject.SetActive(false);

        // Re-enable buttons for the next pause.
        if (playButton != null) playButton.interactable = true;
        if (quitButton != null) quitButton.interactable = true;
    }

    void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── audio ─────────────────────────────────────────────────────────────────

    void PlaySFX(AudioClip clip)
    {
        if (_sfxSource == null || clip == null) return;
        _sfxSource.pitch = SCRIPT_AudioManager.RandomPitch();
        _sfxSource.PlayOneShot(clip, sfxVolume);
    }
}
