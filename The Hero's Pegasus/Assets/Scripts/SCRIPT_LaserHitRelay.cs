using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach this script to the SAME GameObject as the laser ParticleSystem component.
///
/// Unity only calls OnParticleCollision on the GameObject that owns the ParticleSystem,
/// so this relay lives right alongside it and forwards hits to the struck enemy.
///
/// SETUP REQUIRED on the Particle System's Collision module:
///   • Type: World
///   • Send Collision Messages: ON
///   • Lifetime Loss: 1  (kills the particle on impact)
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class SCRIPT_LaserHitRelay : MonoBehaviour
{
    private ParticleSystem _ps;

    // Reused buffer — avoids per-frame heap allocation.
    private readonly List<ParticleCollisionEvent> _events = new List<ParticleCollisionEvent>();

    void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
    }

    void OnParticleCollision(GameObject other)
    {
        SCRIPT_EnemyBase enemy = other.GetComponentInParent<SCRIPT_EnemyBase>();
        if (enemy != null)
        {
            int count = _ps.GetCollisionEvents(other, _events);
            for (int i = 0; i < count; i++)
                enemy.TakeDamage(1f, _events[i].intersection);
            return;
        }

        SCRIPT_UpgradePickup upgrade = other.GetComponentInParent<SCRIPT_UpgradePickup>();
        if (upgrade != null)
            upgrade.Collect();
    }
}
