using UnityEngine;

/// <summary>
/// Enemy Type 1 — Standard Pursuer.
/// Flies directly toward the player's current position at all times.
/// Balanced speed and turn rate; good all-rounder.
///
/// Inspector defaults: medium speed, moderate tracking.
/// </summary>
public class SCRIPT_EnemyType1 : SCRIPT_EnemyBase
{
    // Pure pursuit uses the base GetDesiredHeading() as-is:
    //   return playerTarget.position - transform.position;
    //
    // All behaviour is tuned entirely through the inherited inspector parameters.
    // Suggested starting values for this type:
    //   flightSpeed      = 15
    //   trackingGain     = 2
    //   maxTurnRate      = 90
    //   steeringSmoothTime = 0.2
    //   bankStrength     = 25
}
