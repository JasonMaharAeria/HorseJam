using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the player. Assign a particle system GameObject (child of the player,
/// or any scene object) to laserParticles. LMB activates it, release deactivates it.
/// </summary>
public class SCRIPT_LaserController : MonoBehaviour
{
    [Tooltip("The particle system GameObject to activate when firing")]
    public GameObject laserParticles;

    // Cloud manager polls these to run particle-proximity destruction
    public bool            IsLaserActive { get; private set; }
    public ParticleSystem  LaserPS       { get; private set; }

    // ──────────────────────────────────────────────────────────────────────────

    void Start()
    {
        if (laserParticles == null)
        {
            Debug.LogWarning("SCRIPT_LaserController: no laserParticles assigned.", this);
            return;
        }

        LaserPS = laserParticles.GetComponentInChildren<ParticleSystem>();
        laserParticles.SetActive(false);
    }

    void Update()
    {
        if (laserParticles == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            IsLaserActive = true;
            laserParticles.SetActive(true);
            if (LaserPS != null) LaserPS.Play(withChildren: true);
        }
        else if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            IsLaserActive = false;
            // StopEmittingAndClear removes in-flight particles immediately.
            // Swap to StopEmitting if you want existing particles to finish their lifetime.
            if (LaserPS != null) LaserPS.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
            laserParticles.SetActive(false);
        }
    }
}
