using UnityEngine;

/// <summary>
/// Marker component placed on every floating island GameObject.
/// Other systems (IslandManager, PlayerMovementController, EnemyBase) use
/// GetComponentInParent&lt;SCRIPT_FloatingIsland&gt;() to identify island colliders.
/// No logic of its own — just a tag that survives prefab hierarchies.
/// </summary>
public class SCRIPT_FloatingIsland : MonoBehaviour { }
