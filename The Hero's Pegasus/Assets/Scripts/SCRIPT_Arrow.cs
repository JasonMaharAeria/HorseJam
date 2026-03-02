using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Projectile fired by the hero. Travels forward at a fixed speed. On first contact
/// with any enemy it deals damage once, then embeds inside that enemy (parented to it)
/// so it travels and is destroyed with the enemy.
///
/// PREFAB SETUP REQUIRED:
///   - Rigidbody: useGravity = false, isKinematic = false.
///     Freeze Rotation on all axes to prevent tumbling.
///   - Collider (any shape): isTrigger = true.
///     Size the trigger to match the arrow's visual body.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SCRIPT_Arrow : MonoBehaviour
{
    [Tooltip("How many seconds the arrow travels before destroying itself if it never hits anything.")]
    public float maxLifetime = 6f;
    [Tooltip("Clip played at the impact point when the arrow hits an enemy.")]
    public AudioClip hitSFX;

    // ── set by SCRIPT_Hero.FireArrow ───────────────────────────────────────────

    private float _damage;
    private bool  _initialized;

    // ── internal ───────────────────────────────────────────────────────────────

    private Rigidbody _rb;
    private bool      _embedded;

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _rb            = GetComponent<Rigidbody>();
        _rb.useGravity = false;

        // Prevent physics forces from rotating the arrow shaft.
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    /// <summary>
    /// Called by SCRIPT_Hero immediately after Instantiate.
    /// <paramref name="inheritedVelocity"/> is the hero/pegasus world-space velocity so
    /// the arrow's total world velocity matches the lead-targeting prediction.
    /// </summary>
    public void Init(float damage, float speed, Vector3 inheritedVelocity)
    {
        _damage      = damage;
        _initialized = true;

        // Arrow world velocity = platform velocity + (firing speed × aim direction).
        // AddForce VelocityChange sets velocity instantly without mass scaling and
        // works across Unity 2022, 2023, and Unity 6 without deprecation warnings.
        _rb.AddForce(transform.forward * speed + inheritedVelocity, ForceMode.VelocityChange);

        Destroy(gameObject, maxLifetime);
    }

    void OnTriggerEnter(Collider other)
    {
        // Ignore hits before Init() is called (shouldn't happen, but just in case).
        if (!_initialized || _embedded) return;

        // Walk up the hierarchy to find the enemy root, matching the laser system approach.
        SCRIPT_EnemyBase enemy = other.GetComponentInParent<SCRIPT_EnemyBase>();
        if (enemy == null) return;

        // Deal damage exactly once.
        enemy.TakeDamage(_damage, transform.position);

        if (hitSFX != null)
        {
            AudioMixerGroup sfx = SCRIPT_AudioManager.Instance != null
                                      ? SCRIPT_AudioManager.Instance.sfxGroup : null;
            SCRIPT_AudioManager.PlayClipAtPoint(hitSFX, transform.position, sfx);
        }

        Embed(other.transform);
    }

    void Embed(Transform hitTransform)
    {
        _embedded = true;

        // Stop moving.
        _rb.isKinematic = true;

        // Disable the trigger so repeated OnTriggerEnter calls can't fire.
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Parent to the enemy so the arrow travels with it.
        // When the enemy is destroyed, Unity destroys all children — including this arrow.
        transform.SetParent(hitTransform, worldPositionStays: true);

        // Cancel the lifetime auto-destroy; the arrow now lives and dies with the enemy.
        CancelInvoke();
    }
}
