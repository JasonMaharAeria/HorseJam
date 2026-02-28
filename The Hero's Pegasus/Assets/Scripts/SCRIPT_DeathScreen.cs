using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Manages the death screen panel. Assign this component to a child of your HUD Canvas.
///
/// Setup:
///   1. Create a full-screen Panel under your HUD Canvas. Add a CanvasGroup to it.
///      Set CanvasGroup alpha = 0, interactable = false, blocksRaycasts = false.
///   2. Assign that CanvasGroup to the 'panel' field.
///   3. Add TMP Text children for each stat and assign them below.
///   4. Add two Buttons (Try Again / Quit) and assign them below.
///   5. Call ShowAfterDelay(seconds) from SCRIPT_PlayerMovementController.OnDeath().
/// </summary>
public class SCRIPT_DeathScreen : MonoBehaviour
{
    [Header("Panel")]
    [Tooltip("CanvasGroup on the root death-screen panel. Starts at alpha 0.")]
    public CanvasGroup panel;

    [Tooltip("Duration in seconds for the panel to fade in once it starts appearing.")]
    public float fadeDuration = 1f;

    [Header("Stat Labels")]
    public TMP_Text killsType1Text;
    public TMP_Text killsType2Text;
    public TMP_Text killsType3Text;
    public TMP_Text waveText;
    public TMP_Text timeText;

    [Header("Buttons")]
    public Button tryAgainButton;
    public Button quitButton;

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        // Ensure the panel is hidden and non-interactive at startup.
        if (panel != null)
        {
            panel.alpha          = 0f;
            panel.interactable   = false;
            panel.blocksRaycasts = false;
        }

        if (tryAgainButton != null)
            tryAgainButton.onClick.AddListener(OnTryAgain);

        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuit);
    }

    /// <summary>
    /// Begin the delayed appearance sequence.
    /// Typically called from SCRIPT_PlayerMovementController.OnDeath().
    /// </summary>
    public void ShowAfterDelay(float delay)
    {
        StartCoroutine(ShowSequence(delay));
    }

    // ── coroutine ──────────────────────────────────────────────────────────────

    IEnumerator ShowSequence(float delay)
    {
        yield return new WaitForSeconds(delay);

        PopulateStats();

        panel.interactable   = true;
        panel.blocksRaycasts = true;

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed    += Time.deltaTime;
            panel.alpha = Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
        }

        panel.alpha = 1f;

        // Release the cursor so the player can click the buttons.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    // ── stat population ────────────────────────────────────────────────────────

    void PopulateStats()
    {
        SCRIPT_GameStats stats = SCRIPT_GameStats.Instance;
        if (stats == null) return;

        if (killsType1Text != null) killsType1Text.text = $"Type 1 Killed:  {stats.KillsType1}";
        if (killsType2Text != null) killsType2Text.text = $"Type 2 Killed:  {stats.KillsType2}";
        if (killsType3Text != null) killsType3Text.text = $"Type 3 Killed:  {stats.KillsType3}";

        // Wave is read directly from the WaveController so it reflects the wave
        // that was active when the player died, even after spawning is stopped.
        SCRIPT_WaveController waves = FindFirstObjectByType<SCRIPT_WaveController>();
        int wave = waves != null ? waves.CurrentWave : 0;
        if (waveText != null) waveText.text = $"Wave Reached:  {wave}";

        int minutes = Mathf.FloorToInt(stats.TimeSurvived / 60f);
        int seconds = Mathf.FloorToInt(stats.TimeSurvived % 60f);
        if (timeText != null) timeText.text = $"Time Survived:  {minutes:00}:{seconds:00}";
    }

    // ── button handlers ────────────────────────────────────────────────────────

    void OnTryAgain()
    {
        Time.timeScale = 1f; // safety reset in case anything paused it
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
