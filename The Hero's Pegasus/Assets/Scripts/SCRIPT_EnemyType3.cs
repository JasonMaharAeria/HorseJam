using UnityEngine;

/// <summary>
/// Enemy Type 3 — Erratic Pursuer.
/// Weaves toward the player with a sinusoidal figure-8 oscillation layered on top
/// of the base pursuit direction, making it unpredictable and hard to target.
///
/// The oscillation uses two sine waves at different frequencies and phases
/// (lateral and vertical) to produce a natural Lissajous-style path.
///
/// Suggested starting values:
///   flightSpeed          = 12
///   trackingGain         = 4
///   maxTurnRate          = 150
///   steeringSmoothTime   = 0.08   (very snappy / responsive)
///   oscillationStrength  = 0.4
///   oscillationFrequency = 0.8
///   bankStrength         = 35
/// </summary>
public class SCRIPT_EnemyType3 : SCRIPT_EnemyBase
{
    [Header("Type 3: Erratic Oscillation")]
    [Tooltip("How far the weaving deviates from the direct pursuit direction. " +
             "0 = no weave, 1 = very erratic (~45° max deviation).")]
    [Range(0f, 1f)]
    public float oscillationStrength = 0.4f;

    [Tooltip("Oscillation speed in cycles per second. " +
             "The vertical axis uses 1.3× this value for an asymmetric figure-8.")]
    public float oscillationFrequency = 0.8f;

    protected override Vector3 GetDesiredHeading()
    {
        Vector3 toPlayer = playerTarget.position - transform.position;
        Vector3 baseDir  = toPlayer.normalized;

        // Build a local right/up frame relative to the pursuit direction.
        // Using world up as reference keeps the oscillation visually readable.
        Vector3 right = Vector3.Cross(baseDir, Vector3.up);
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.Cross(baseDir, Vector3.forward); // fallback near vertical
        right.Normalize();
        Vector3 up = Vector3.Cross(right, baseDir).normalized;

        // Two sine waves at slightly different frequencies — creates a Lissajous weave.
        float t        = Time.time * oscillationFrequency * Mathf.PI * 2f;
        float lateral  = Mathf.Sin(t)               * oscillationStrength;
        float vertical = Mathf.Sin(t * 1.3f + 1.2f) * oscillationStrength;

        return (baseDir + right * lateral + up * vertical).normalized;
    }
}
