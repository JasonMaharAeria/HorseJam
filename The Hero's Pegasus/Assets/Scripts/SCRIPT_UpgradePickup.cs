using UnityEngine;
using UnityEngine.Audio;

/// <summary>All possible upgrade types. Add new entries here to create new upgrades.</summary>
public enum UpgradeType
{
    ArrowSizeIncrease,
    AttackSpeedIncrease,
    ArrowDamageIncrease,
    DashSpeedIncrease,
    LaserUpgrade,
    ArrowVelocityIncrease,
    HealthRegenIncrease,
    StaminaRegenIncrease,
    MaxHealthIncrease,
    MaxStaminaIncrease,
}

/// <summary>
/// Attach to an upgrade pickup prefab. Bobs gently above a floating island and is
/// collected when the player's laser hits its collider.
///
/// PREFAB SETUP:
///   - Collider (non-trigger) so laser particles register World collision with it.
///   - This component on the root or on any parent of the collider GameObject.
///   - Optionally add a MeshRenderer / visual child to represent the pickup.
/// </summary>
public class SCRIPT_UpgradePickup : MonoBehaviour
{
    [Header("Upgrade")]
    [Tooltip("Which stat this pickup permanently boosts when collected.")]
    public UpgradeType upgradeType = UpgradeType.ArrowSizeIncrease;

    [Header("Audio")]
    [Tooltip("Played at the pickup's position when the player collects it.")]
    public AudioClip collectSFX;
    [Tooltip("Volume multiplier for the collect SFX.")]
    [Range(0f, 1f)]
    public float collectSFXVolume = 1f;

    [Header("Bob Animation")]
    [Tooltip("Half-height of the up-and-down bob in world units.")]
    public float bobAmplitude = 0.4f;
    [Tooltip("Cycles per second of the bob.")]
    public float bobSpeed = 1.5f;
    [Tooltip("Degrees per second the pickup spins on its Y axis.")]
    public float spinSpeed = 90f;

    // ── private ────────────────────────────────────────────────────────────────

    private Vector3 _spawnPosition;
    private bool    _collected;

    // ──────────────────────────────────────────────────────────────────────────

    void Start()
    {
        _spawnPosition = transform.position;
    }

    void Update()
    {
        // Vertical bob.
        float y = _spawnPosition.y + Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f) * bobAmplitude;
        transform.position = new Vector3(_spawnPosition.x, y, _spawnPosition.z);

        // Gentle spin.
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);  
    }

    /// <summary>
    /// Called by SCRIPT_LaserHitRelay when a laser particle strikes this pickup.
    /// Guard against duplicate calls (multiple particles in one frame).
    /// </summary>
    public void Collect()
    {
        if (_collected) return;
        _collected = true;

        SCRIPT_PlayerStats.Instance?.ApplyUpgrade(upgradeType);

        if (collectSFX != null)
        {
            AudioMixerGroup sfx = SCRIPT_AudioManager.Instance != null
                                      ? SCRIPT_AudioManager.Instance.sfxGroup : null;
            // Play at the camera/listener position so volume is always full.
            Vector3 listenerPos = Camera.main != null
                                      ? Camera.main.transform.position
                                      : Vector3.zero;
            SCRIPT_AudioManager.PlayClipAtPoint(collectSFX, listenerPos, sfx, collectSFXVolume);
        }

        Debug.Log($"[UpgradePickup] Collected {upgradeType}");
        Destroy(gameObject);
    }
}
