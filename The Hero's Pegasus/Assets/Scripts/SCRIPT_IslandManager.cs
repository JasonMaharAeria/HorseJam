using UnityEngine;

/// <summary>
/// Spawns and recycles a pool of floating island GameObjects around the player.
///
///   - On Start, islands fill the world within spawnRadius of the player,
///     with a clearance zone around the player so none spawn inside them.
///     All islands spawn right-side up, rotated randomly around Y only.
///   - Each Update, islands too far from the player are repositioned anywhere
///     within the field at recycleMinDist or beyond (no FOV restriction).
///
/// Island prefabs must have at least one Collider component.
/// SCRIPT_FloatingIsland is automatically added to each instance if not present.
/// </summary>
public class SCRIPT_IslandManager : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("Auto-found from SCRIPT_PlayerMovementController if left empty.")]
    public Transform player;

    [Header("Island Prefabs")]
    [Tooltip("Array of island prefab variants. One is picked at random per slot in the pool.")]
    public GameObject[] islandPrefabs;

    [Header("Pool")]
    [Tooltip("Total number of island instances kept alive at once.")]
    public int   islandCount   = 20;
    [Tooltip("XZ radius of the island field around the player.")]
    public float spawnRadius   = 400f;
    [Tooltip("Max Y distance above and below the player.")]
    public float verticalRange = 100f;
    [Tooltip("Minimum XZ distance from player when placing a new island.")]
    public float minSpawnDist  = 60f;

    [Header("Recycling")]
    [Tooltip("An island this far from the player (any direction) gets repositioned.")]
    public float recycleDistance = 350f;
    [Tooltip("Minimum distance from the player when placing a recycled island.")]
    public float recycleMinDist  = 150f;

    [Header("Initial Spawn")]
    [Tooltip("No island will spawn within this radius of the player at game start.")]
    public float initialPlayerClearance    = 35f;
    [Tooltip("Minimum distance between any two islands during initial fill. " +
             "Prevents islands spawning inside each other.")]
    public float initialIslandMinSeparation = 25f;

    // ── private state ──────────────────────────────────────────────────────────
    private Transform[] _islands;

    // ──────────────────────────────────────────────────────────────────────────

    void Start()
    {
        if (player == null)
        {
            var mc = FindFirstObjectByType<SCRIPT_PlayerMovementController>();
            if (mc != null) player = mc.transform;
        }

        if (player == null)
        {
            Debug.LogWarning("SCRIPT_IslandManager: no player found.", this);
            return;
        }

        if (islandPrefabs == null || islandPrefabs.Length == 0)
        {
            Debug.LogWarning("SCRIPT_IslandManager: no island prefabs assigned.", this);
            return;
        }

        BuildPool();
    }

    void Update()
    {
        if (player == null || _islands == null) return;

        Vector3 playerPos = player.position;

        for (int i = 0; i < _islands.Length; i++)
        {
            if (_islands[i] == null) continue;

            float dist = Vector3.Distance(_islands[i].position, playerPos);
            if (dist > recycleDistance)
                Reposition(i, playerPos);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────

    void BuildPool()
    {
        _islands = new Transform[islandCount];

        for (int i = 0; i < islandCount; i++)
        {
            GameObject prefab = islandPrefabs[Random.Range(0, islandPrefabs.Length)];
            Vector3    pos    = InitialPosition(i, player.position);

            GameObject go = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
            go.name = $"Island_{i:00}";

            // Ensure the marker component is present.
            if (go.GetComponentInChildren<SCRIPT_FloatingIsland>() == null)
                go.AddComponent<SCRIPT_FloatingIsland>();

            _islands[i] = go.transform;
        }
    }

    // Picks an initial position that respects player clearance and tries to
    // stay away from already-placed islands.
    Vector3 InitialPosition(int index, Vector3 center)
    {
        int maxAttempts = 30;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist  = Random.Range(initialPlayerClearance, spawnRadius);
            float y     = Random.Range(-verticalRange, verticalRange);

            Vector3 candidate = center + new Vector3(
                Mathf.Cos(angle) * dist,
                y,
                Mathf.Sin(angle) * dist);

            // Enforce player clearance.
            if (Vector3.Distance(candidate, center) < initialPlayerClearance)
                continue;

            // Enforce island-to-island separation using already-placed islands.
            bool tooClose = false;
            for (int j = 0; j < index; j++)
            {
                if (_islands[j] != null &&
                    Vector3.Distance(candidate, _islands[j].position) < initialIslandMinSeparation)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            return candidate;
        }

        // Fallback: random position ignoring separation (better than stalling).
        float a = Random.Range(0f, Mathf.PI * 2f);
        float d = Random.Range(initialPlayerClearance, spawnRadius);
        return center + new Vector3(Mathf.Cos(a) * d,
                                    Random.Range(-verticalRange, verticalRange),
                                    Mathf.Sin(a) * d);
    }

    void Reposition(int index, Vector3 playerPos)
    {
        Vector3 newPos = RecyclePosition(playerPos);
        _islands[index].position = newPos;
        // Keep the existing random rotation — or randomise again for variety.
        _islands[index].rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
    }

    // Picks a recycle position at least recycleMinDist from the player,
    // in any direction (including ahead), so the island field feels full.
    Vector3 RecyclePosition(Vector3 center)
    {
        int maxAttempts = 20;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist  = Random.Range(recycleMinDist, spawnRadius);
            float y     = Random.Range(-verticalRange, verticalRange);

            Vector3 candidate = center + new Vector3(
                Mathf.Cos(angle) * dist,
                y,
                Mathf.Sin(angle) * dist);

            if (Vector3.Distance(candidate, center) >= recycleMinDist)
                return candidate;
        }

        // Fallback: random point at mid-range distance.
        float fa = Random.Range(0f, Mathf.PI * 2f);
        return center + new Vector3(Mathf.Cos(fa), 0f, Mathf.Sin(fa))
                      * (recycleMinDist + spawnRadius) * 0.5f
                      + Vector3.up * Random.Range(-verticalRange * 0.5f, verticalRange * 0.5f);
    }

}

