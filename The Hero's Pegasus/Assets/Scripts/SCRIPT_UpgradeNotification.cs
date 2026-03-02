using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// HUD notification that fades in, holds, then fades out whenever the player
/// collects an upgrade pickup.
///
/// SCENE SETUP:
///   1. Create a child Panel/GameObject under your HUD Canvas.
///   2. Add a CanvasGroup component to it (start alpha = 0).
///   3. Add two TMP_Text children: one for the header, one for the body.
///   4. Add this component and wire up the three fields below.
///   5. SCRIPT_PlayerStats calls Show() automatically on every upgrade collect.
/// </summary>
public class SCRIPT_UpgradeNotification : MonoBehaviour
{
    public static SCRIPT_UpgradeNotification Instance { get; private set; }

    [Header("References")]
    [Tooltip("CanvasGroup on this panel — controls overall alpha for the fade.")]
    public CanvasGroup canvasGroup;
    [Tooltip("TMP_Text for the bold title line, e.g. \"UPGRADE: Arrow Size\".")]
    public TMP_Text headerText;
    [Tooltip("TMP_Text for the description and before/after stat line.")]
    public TMP_Text bodyText;

    [Header("Timing")]
    [Tooltip("Seconds to fully fade in.")]
    public float fadeInDuration  = 0.25f;
    [Tooltip("Seconds the notification stays fully visible.")]
    public float holdDuration    = 2.5f;
    [Tooltip("Seconds to fully fade out.")]
    public float fadeOutDuration = 0.75f;

    [Header("Wave Transition Timing")]
    [Tooltip("Seconds used to animate the displayed wave number from previous to current.")]
    public float waveCountDuration = 0.35f;
    [Tooltip("Seconds the final wave number remains fully visible before fading out.")]
    public float waveHoldDuration = 1.0f;
    [Tooltip("Seconds to fade out the wave transition popup.")]
    public float waveFadeOutDuration = 0.45f;

    // ── private ────────────────────────────────────────────────────────────────

    private Coroutine _activeCoroutine;

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    /// <summary>
    /// Display the notification. If one is already showing it is replaced instantly.
    /// </summary>
    /// <param name="upgradeName">Short display name, e.g. "Arrow Size".</param>
    /// <param name="description">One-line description of the effect.</param>
    /// <param name="prevFormatted">Old stat value as a display string.</param>
    /// <param name="newFormatted">New stat value as a display string.</param>
    public void Show(string upgradeName, string description, string prevFormatted, string newFormatted)
    {
        if (headerText != null)
            headerText.text = $"UPGRADE: {upgradeName}";

        if (bodyText != null)
            bodyText.text = $"{description}\n{prevFormatted}  →  {newFormatted}";

        if (_activeCoroutine != null)
            StopCoroutine(_activeCoroutine);

        _activeCoroutine = StartCoroutine(ShowSequence());
    }

    /// <summary>
    /// Display a wave transition popup (e.g. "Wave 2" → "Wave 3"), then fade away.
    /// </summary>
    public void ShowWaveTransition(int previousWave, int currentWave)
    {
        if (_activeCoroutine != null)
            StopCoroutine(_activeCoroutine);

        _activeCoroutine = StartCoroutine(ShowWaveSequence(previousWave, currentWave));
    }

    // ── coroutine ──────────────────────────────────────────────────────────────

    IEnumerator ShowSequence()
    {
        // Fade in.
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            if (canvasGroup != null)
                canvasGroup.alpha = Mathf.Clamp01(elapsed / fadeInDuration);
            yield return null;
        }
        if (canvasGroup != null) canvasGroup.alpha = 1f;

        // Hold.
        yield return new WaitForSeconds(holdDuration);

        // Fade out.
        elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            if (canvasGroup != null)
                canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeOutDuration);
            yield return null;
        }
        if (canvasGroup != null) canvasGroup.alpha = 0f;

        _activeCoroutine = null;
    }

    IEnumerator ShowWaveSequence(int previousWave, int currentWave)
    {
        if (headerText != null)
            headerText.text = "WAVE";

        int startWave = Mathf.Max(0, previousWave);
        int endWave   = Mathf.Max(startWave, currentWave);

        if (bodyText != null)
            bodyText.text = $"Wave {startWave}";

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;

        // Fade in.
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            if (canvasGroup != null)
                canvasGroup.alpha = Mathf.Clamp01(elapsed / Mathf.Max(fadeInDuration, 0.0001f));
            yield return null;
        }
        if (canvasGroup != null) canvasGroup.alpha = 1f;

        // Animate number transition.
        float countElapsed = 0f;
        while (countElapsed < waveCountDuration)
        {
            countElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(countElapsed / Mathf.Max(waveCountDuration, 0.0001f));
            t = Mathf.SmoothStep(0f, 1f, t);

            int shownWave = Mathf.RoundToInt(Mathf.Lerp(startWave, endWave, t));
            if (bodyText != null)
                bodyText.text = $"Wave {shownWave}";

            yield return null;
        }
        if (bodyText != null)
            bodyText.text = $"Wave {endWave}";

        // Hold final value.
        yield return new WaitForSeconds(waveHoldDuration);

        // Fade out.
        elapsed = 0f;
        while (elapsed < waveFadeOutDuration)
        {
            elapsed += Time.deltaTime;
            if (canvasGroup != null)
                canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / Mathf.Max(waveFadeOutDuration, 0.0001f));
            yield return null;
        }
        if (canvasGroup != null) canvasGroup.alpha = 0f;

        _activeCoroutine = null;
    }
}
