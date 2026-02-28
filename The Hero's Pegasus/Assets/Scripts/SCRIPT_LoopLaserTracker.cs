using UnityEngine;

/// <summary>
/// Added programmatically by SCRIPT_PlayerMovementController to each laser
/// instantiated during the vertical loop maneuver. Re-aims the GameObject
/// toward its locked target every frame so the particle beam stays on target
/// even as both the player and the enemy move through the maneuver.
/// </summary>
public class SCRIPT_LoopLaserTracker : MonoBehaviour
{
    /// <summary>The enemy this laser has locked on to. Set by the player controller.</summary>
    public Transform target;

    void Update()
    {
        if (target == null) return;

        Vector3 dir = target.position - transform.position;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir);
    }
}
