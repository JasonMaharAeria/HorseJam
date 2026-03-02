using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Projectile fired by SCRIPT_EnemyType2 (and any future ranged enemy).
/// Launches straight, then gradually curves toward the player over time.
/// Explodes (spawning a particle prefab) on hitting the player or a floating island.
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

    [Header("Homing")]
    [Tooltip("How quickly the projectile steers toward the player (degrees per second). 0 = straight.")]
    public float homingStrength = 55f;
    [Tooltip("Seconds of straight travel before homing kicks in — gives the shot an initial lead.")]
    public float homingDelay = 0.15f;

    [Header("Explosion")]
    [Tooltip("Particle prefab spawned at the impact point on any hit. Leave empty for no effect.")]
    public GameObject explosionPrefab;
    [Tooltip("Clip played at the impact point when the projectile explodes.")]
    public AudioClip explosionSFX;

    // ── set by the firing enemy ────────────────────────────────────────────────

    private float     _damage;
    private bool      _initialized;

    // ── internal ───────────────────────────────────────────────────────────────

    private Rigidbody _rb;
    private Transform _playerTarget;
    private float     _timeAlive;
    private bool      _dead;   // guard against double-explode on the same frame

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _rb             = GetComponent<Rigidbody>();
        _rb.useGravity  = false;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;

        // Find the player once at spawn — cheaper than per-frame search.
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
            _playerTarget = playerObj.transform;
    }

    /// <summary>
    /// Called by the firing enemy immediately after Instantiate.
    /// Launches the projectile and arms the damage trigger.
    /// </summary>
    public void Init(float damage, float speed)
    {
        _damage      = damage;
        _initialized = true;

        _rb.AddForce(transform.forward * speed, ForceMode.VelocityChange);

        Destroy(gameObject, maxLifetime);
    }

    void FixedUpdate()
    {
        if (!_initialized || _dead || _playerTarget == null) return;

        _timeAlive += Time.fixedDeltaTime;
        if (_timeAlive < homingDelay) return;

        // Steer velocity toward the player without changing speed.
        float speed = _rb.linearVelocity.magnitude;
        if (speed < 0.001f) return;

        Vector3 toPlayer  = (_playerTarget.position - transform.position).normalized;
        Vector3 newDir    = Vector3.RotateTowards(
            _rb.linearVelocity / speed,
            toPlayer,
            homingStrength * Mathf.Deg2Rad * Time.fixedDeltaTime,
            0f);

        _rb.linearVelocity = newDir * speed;

        // Orient the visual to match flight direction.
        transform.rotation = Quaternion.LookRotation(_rb.linearVelocity);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!_initialized || _dead) return;

        // Player hit — deal damage then explode.
        var player = other.GetComponentInParent<SCRIPT_PlayerMovementController>();
        if (player != null)
        {
            player.TakeDamage(_damage);
            Explode();
            return;
        }

        // Island hit — explode without dealing damage.
        if (other.GetComponentInParent<SCRIPT_FloatingIsland>() != null)
        {
            Explode();
        }
    }

    void Explode()
    {
        _dead = true;

        if (explosionPrefab != null)
            Instantiate(explosionPrefab, transform.position, Quaternion.identity);

        if (explosionSFX != null)
        {
            AudioMixerGroup sfx = SCRIPT_AudioManager.Instance != null
                                      ? SCRIPT_AudioManager.Instance.sfxGroup : null;
            SCRIPT_AudioManager.PlayClipAtPoint(explosionSFX, transform.position, sfx);
        }

        Destroy(gameObject);
    }
}
