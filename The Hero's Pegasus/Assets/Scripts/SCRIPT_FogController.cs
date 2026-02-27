using UnityEngine;

/// <summary>
/// Controls Unity's built-in linear fog so that visibility is clear up to
/// <see cref="fogRadius"/> units from the camera, then fades to full fog
/// over <see cref="fogFadeDistance"/> additional units.
///
/// Setup:
///   1. Attach this script to any scene GameObject (e.g. the Player or a
///      dedicated FogController object).
///   2. Enable Fog in Lighting → Environment → Other Settings (checkbox).
///      The script forces Fog Mode to Linear and overrides Start/End each frame.
///   3. Tune fogRadius and fogFadeDistance in the Inspector.
/// </summary>
public class SCRIPT_FogController : MonoBehaviour
{
    [Header("Fog Shape")]
    [Tooltip("Distance from the camera at which fog begins to appear.")]
    public float fogRadius       = 200f;
    [Tooltip("Additional distance over which fog ramps from zero to full density.")]
    public float fogFadeDistance = 150f;

    [Header("Fog Appearance")]
    public Color fogColor = new Color(0.75f, 0.82f, 0.90f, 1f); // soft sky-blue

    [Header("Runtime Toggle")]
    public bool enableFog = true;

    // ──────────────────────────────────────────────────────────────────────────

    void OnEnable()
    {
        Apply();
    }

    void Update()
    {
        Apply();
    }

    void OnDisable()
    {
        RenderSettings.fog = false;
    }

    // ──────────────────────────────────────────────────────────────────────────

    void Apply()
    {
        RenderSettings.fog          = enableFog;
        RenderSettings.fogColor     = fogColor;
        RenderSettings.fogMode      = FogMode.Linear;
        RenderSettings.fogStartDistance = fogRadius;
        RenderSettings.fogEndDistance   = fogRadius + Mathf.Max(fogFadeDistance, 0.01f);
    }
}
