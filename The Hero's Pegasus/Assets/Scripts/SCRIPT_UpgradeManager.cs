using UnityEngine;

/// <summary>
/// Singleton manager for upgrade pickups.
///
/// SCENE SETUP:
///   - Add this component to any persistent scene GameObject.
///   - Assign one prefab per upgrade type in upgradePrefabs (each must have
///     SCRIPT_UpgradePickup + a non-trigger Collider).
///   - Assign SCRIPT_PlayerStats to the same or another persistent GameObject.
///
/// SPAWN LOGIC:
///   SCRIPT_IslandManager calls TrySpawnAbove() whenever an island is first
///   placed or recycled to a new position. This script uses the island's combined
///   Renderer bounds to place the pickup on top of the actual mesh, regardless of
///   island scale or shape. The pickup is parented to the island so it is destroyed
///   together with it when the island is recycled.
/// </summary>
public class SCRIPT_UpgradeManager : MonoBehaviour
{
    public static SCRIPT_UpgradeManager Instance { get; private set; }

    [Header("Spawn Settings")]
    [Tooltip("Probability (0–1) that an upgrade spawns above any given island placement.\n" +
             "Default 0.02 = 2 %. Raise this during testing to see pickups more often.")]
    [Range(0f, 1f)]
    public float upgradeSpawnChance = 0.02f;

    [Tooltip("Extra clearance added above the highest point of the island's mesh bounds.")]
    public float spawnClearanceAboveMesh = 2f;

    [Header("Upgrade Prefabs")]
    [Tooltip("Pool of upgrade prefabs to choose from at random. " +
             "Each should have SCRIPT_UpgradePickup + a non-trigger Collider.")]
    public GameObject[] upgradePrefabs;

    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Roll the spawn-chance dice. If it passes, pick a random upgrade prefab,
    /// place it on top of the island mesh, and parent it to the island so it is
    /// cleaned up automatically when the island is recycled.
    /// Safe to call with a null island (does nothing).
    /// </summary>
    public void TrySpawnAbove(Transform island)
    {
        if (island == null) return;
        if (upgradePrefabs == null || upgradePrefabs.Length == 0) return;
        if (Random.value > upgradeSpawnChance) return;

        GameObject prefab = upgradePrefabs[Random.Range(0, upgradePrefabs.Length)];
        if (prefab == null) return;

        float topY = GetMeshTopY(island);
        Vector3 spawnPos = new Vector3(island.position.x,
                                       topY + spawnClearanceAboveMesh,
                                       island.position.z);

        // Parent to the island — destroyed together when the island is recycled.
        Instantiate(prefab, spawnPos, Quaternion.identity, island);
    }

    // Returns the world-space Y of the highest vertex in the island's combined
    // Renderer bounds. Falls back to a reasonable offset if no Renderers are found.
    static float GetMeshTopY(Transform island)
    {
        Renderer[] renderers = island.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return island.position.y + 5f;

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            combined.Encapsulate(renderers[i].bounds);

        return combined.max.y;
    }
}
