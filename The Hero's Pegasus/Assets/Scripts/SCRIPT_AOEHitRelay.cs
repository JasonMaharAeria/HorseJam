using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach this script to the SAME GameObject as the enemy's AOE cone ParticleSystem.
///
/// When a particle from that system strikes the player, this relay forwards the
/// hit to the player's health component. The owning enemy (SCRIPT_EnemyType3)
/// configures damagePerParticle and playerHealth at startup.
///
/// SETUP REQUIRED on the Particle System's Collision module:
///   • Type: World
///   • Send Collision Messages: ON
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class SCRIPT_AOEHitRelay : MonoBehaviour
{
    /// <summary>Damage dealt to the player per particle hit. Set by the owning enemy.</summary>
    [HideInInspector] public float damagePerParticle;
    /// <summary>Player health component to damage. Set by the owning enemy.</summary>
    [HideInInspector] public SCRIPT_PlayerMovementController playerHealth;

    private ParticleSystem _ps;
    private readonly List<ParticleCollisionEvent> _events = new List<ParticleCollisionEvent>();

    void Awake() => _ps = GetComponent<ParticleSystem>();

    void OnParticleCollision(GameObject other)
    {
        Debug.Log($"[AOEHitRelay] OnParticleCollision hit: {other.name}  playerHealth={(playerHealth != null ? "set" : "NULL")}");

        if (playerHealth == null) return;

        // Only damage the player — ignore any other colliders.
        if (other.GetComponentInParent<SCRIPT_PlayerMovementController>() == null)
        {
            Debug.Log($"[AOEHitRelay] '{other.name}' has no SCRIPT_PlayerMovementController in parent chain — skipped.");
            return;
        }

        int count = _ps.GetCollisionEvents(other, _events);
        Debug.Log($"[AOEHitRelay] Dealing {damagePerParticle} damage x{count} events to player.");
        for (int i = 0; i < count; i++)
            playerHealth.TakeDamage(damagePerParticle);
    }
}
