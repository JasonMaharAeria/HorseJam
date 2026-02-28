using UnityEngine;

/// <summary>
/// Projectile fired by SCRIPT_EnemyType2 (and any future ranged enemy).
/// Travels forward at a fixed world-space speed and deals damage to the player
/// on first contact, then destroys itself.
///
/// PREFAB SETUP REQUIRED:
///   - Rigidbody: useGravity = false, isKinematic = false.
///     Freeze Rotation on all axes to prevent tumbling.
///   - Collider (any shape): isTrigger = true.
///     Size to match the projectile's visual body.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SCRIPT_EnemyProjectile : MonoBehaviour
{
    [Tooltip("Seconds before auto-destroying if nothing is hit.")]
    public float maxLifetime = 5f;

    // ── set by the firing enemy ────────────────────────────────────────────────

    private float _damage;
    private bool  _initialized;

    // ── internal ───────────────────────────────────────────────────────────────

    private Rigidbody _rb;

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _rb            = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    /// <summary>
    /// Called by the firing enemy immediately after Instantiate.
    /// Launches the projectile and arms the damage trigger.
    /// </summary>
    public void Init(float damage, float speed)
    {
        _damage      = damage;
        _initialized = true;

        // VelocityChange sets velocity instantly without mass scaling.
        _rb.AddForce(transform.forward * speed, ForceMode.VelocityChange);

        Destroy(gameObject, maxLifetime);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!_initialized) return;

        // Only damage the player — ignore enemies, arrows, world geometry.
        var player = other.GetComponentInParent<SCRIPT_PlayerMovementController>();
        if (player == null) return;

        player.TakeDamage(_damage);
        Destroy(gameObject);
    }
}
