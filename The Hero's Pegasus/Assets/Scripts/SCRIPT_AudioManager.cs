using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Central audio hub. Place on a persistent GameObject in the scene (e.g. "AudioManager").
/// All AudioSources in the project set their Output to musicGroup or sfxGroup so that
/// the player can control volumes separately from a settings menu.
///
/// AudioMixer setup (one-time, in the Unity Editor):
///   1. Right-click in Project → Create → Audio Mixer. Name it "GameAudioMixer".
///   2. In the Mixer window, add two child Groups under Master: "Music" and "SFX".
///   3. On the Master group: right-click Volume → Expose Parameter → rename to "MasterVolume".
///   4. On Music:            right-click Volume → Expose Parameter → rename to "MusicVolume".
///   5. On SFX:              right-click Volume → Expose Parameter → rename to "SFXVolume".
///   6. Drag the mixer asset into this component's audioMixer field.
///   7. Drag the Music and SFX groups into musicGroup and sfxGroup respectively.
///
/// Script Execution Order: set this script to -100 so it initialises before anything
/// that calls Instance in their Start() (Project Settings → Script Execution Order).
/// </summary>
public class SCRIPT_AudioManager : MonoBehaviour
{
    public static SCRIPT_AudioManager Instance { get; private set; }

    [Header("AudioMixer")]
    public AudioMixer audioMixer;

    [Header("Mixer Groups — drag from the AudioMixer window")]
    public AudioMixerGroup musicGroup;
    public AudioMixerGroup sfxGroup;

    // These strings must exactly match the names you typed in the Exposed Parameters list.
    private const string k_MasterVol = "MasterVolume";
    private const string k_MusicVol  = "MusicVolume";
    private const string k_SFXVol    = "SFXVolume";

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Volume Control (0–1 linear ↔ dB) ─────────────────────────────────────

    public void SetMasterVolume(float linear) => SetMixerVolume(k_MasterVol, linear);
    public void SetMusicVolume(float linear)  => SetMixerVolume(k_MusicVol,  linear);
    public void SetSFXVolume(float linear)    => SetMixerVolume(k_SFXVol,    linear);

    public float GetMasterVolume() => GetMixerVolume(k_MasterVol);
    public float GetMusicVolume()  => GetMixerVolume(k_MusicVol);
    public float GetSFXVolume()    => GetMixerVolume(k_SFXVol);

    void SetMixerVolume(string param, float linear)
    {
        if (audioMixer == null) return;
        audioMixer.SetFloat(param, LinearToDb(linear));
    }

    float GetMixerVolume(string param)
    {
        if (audioMixer == null) return 1f;
        audioMixer.GetFloat(param, out float db);
        return DbToLinear(db);
    }

    // ── Mixer-routed PlayClipAtPoint ──────────────────────────────────────────

    /// <summary>
    /// Drop-in replacement for AudioSource.PlayClipAtPoint that routes audio
    /// through the given AudioMixerGroup, so mixer-based volume sliders work.
    /// If group is null the call silently falls back to AudioSource.PlayClipAtPoint.
    /// </summary>
    public static void PlayClipAtPoint(AudioClip clip, Vector3 position,
                                       AudioMixerGroup group, float volume = 1f)
    {
        if (clip == null) return;

        if (group == null)
        {
            AudioSource.PlayClipAtPoint(clip, position, volume);
            return;
        }

        var go = new GameObject("_OneShot_" + clip.name);
        go.transform.position = position;
        var src = go.AddComponent<AudioSource>();
        src.clip                  = clip;
        src.outputAudioMixerGroup = group;
        src.volume                = volume;
        src.spatialBlend          = 1f;
        src.pitch                 = RandomPitch();
        src.Play();
        Destroy(go, clip.length + 0.1f);
    }

    // ── Pitch Randomisation ───────────────────────────────────────────────────

    /// <summary>
    /// Returns a random pitch multiplier in [0.9, 1.1].
    /// Apply to an AudioSource.pitch (or pass as a scale) before playing any SFX
    /// to give each sound a subtle natural variation.
    /// </summary>
    public static float RandomPitch() => Random.Range(0.9f, 1.1f);

    // ── Conversion helpers ────────────────────────────────────────────────────

    public static float LinearToDb(float linear)
        => linear > 0.0001f ? 20f * Mathf.Log10(linear) : -80f;

    public static float DbToLinear(float db)
        => Mathf.Pow(10f, db / 20f);
}
