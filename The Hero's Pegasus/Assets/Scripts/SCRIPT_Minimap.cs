using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimap HUD that renders enemy positions as dots relative to the player.
/// The minimap is heading-up: the player always faces "up" on the display.
///
/// Height is communicated via color:
///   same level → colorAtLevel (red)
///   above player → colorAbove (blue)
///   below player → colorBelow (brown)
///
/// Proximity is communicated via dot size: enemies closer than proximityRadius
/// scale up to maxDotScale, giving an immediate read on nearby threats.
///
/// Setup:
///   1. Add this component to any scene GameObject (e.g. a child of the HUD Canvas).
///   2. Assign minimapRoot to the RectTransform that defines the map display area.
///      A circular mask (Image + Mask component) on that object gives a round minimap.
///   3. Create an EnemyDot prefab: a UI Image with a white circle sprite (color is set
///      at runtime — keep the prefab color white so tinting works correctly).
/// </summary>
public class SCRIPT_Minimap : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The player transform. Auto-finds the 'Player' tag if left empty.")]
    public Transform player;

    [Tooltip("RectTransform that defines the minimap display area. " +
             "Dots are parented and positioned inside it.")]
    public RectTransform minimapRoot;

    [Tooltip("UI prefab for an enemy dot. Must have an Image component. " +
             "Keep the prefab color white — runtime color tinting overrides it.")]
    public GameObject enemyDotPrefab;

    [Header("Map Scale")]
    [Tooltip("World-space radius represented by the minimap's full half-width. " +
             "Enemies further than this are clamped to the minimap edge.")]
    public float worldRadius = 500f;

    [Tooltip("Visual radius of the minimap in UI/canvas units (pixels on a 1:1 canvas). " +
             "Set this to match the actual pixel size of your minimapRoot — e.g. if your " +
             "minimap panel is 200×200, enter 100. This avoids relying on RectTransform.rect " +
             "which can return 0 before the Canvas finishes its first layout pass.")]
    public float mapDisplayRadius = 100f;

    [Header("Height → Color")]
    [Tooltip("Color for enemies at the same altitude as the player.")]
    public Color colorAtLevel = new Color(1f, 0.1f, 0.1f);

    [Tooltip("Color for enemies directly above the player (at or beyond maxHeightDifference).")]
    public Color colorAbove = new Color(0.25f, 0.5f, 1f);

    [Tooltip("Color for enemies directly below the player (at or beyond maxHeightDifference).")]
    public Color colorBelow = new Color(0.55f, 0.27f, 0.05f);

    [Tooltip("Height difference in world units that maps to the full above/below color. " +
             "At 0 the dot is colorAtLevel; at this value it fully reaches colorAbove/colorBelow.")]
    public float maxHeightDifference = 200f;

    [Header("Proximity → Size")]
    [Tooltip("World-space radius within which dot size scales up toward maxDotScale. " +
             "Outside this radius dots render at their base dotSize.")]
    public float proximityRadius = 100f;

    [Tooltip("Maximum dot scale multiplier applied to the nearest enemy. " +
             "At distance 0 the dot is this many times its base dotSize.")]
    [Range(1f, 3f)]
    public float maxDotScale = 2f;

    [Header("Dot Appearance")]
    [Tooltip("Base UI size (width and height) of each enemy dot in canvas units.")]
    public float dotSize = 10f;

    [Header("Edge Clamping")]
    [Tooltip("When true, out-of-range dots are clamped to the minimap edge so the " +
             "player can see the direction. When false, they are hidden entirely.")]
    public bool clampToEdge = true;

    [Tooltip("Scale multiplier applied to clamped (out-of-range) dots.")]
    [Range(0.25f, 1f)]
    public float clampedDotScale = 0.65f;

    // ── private ───────────────────────────────────────────────────────────────

    private readonly List<RectTransform> _dotPool  = new List<RectTransform>();
    private readonly List<Image>         _dotImages = new List<Image>();
    private SCRIPT_EnemyBase[] _enemies = System.Array.Empty<SCRIPT_EnemyBase>();

    private float _scanTimer;
    private const float ScanInterval = 0.1f;

    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        if (player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null)
                player = p.transform;
            else
                Debug.LogWarning("[Minimap] No GameObject tagged 'Player' found.", this);
        }

        if (minimapRoot == null)
            Debug.LogWarning("[Minimap] minimapRoot is not assigned.", this);

        if (enemyDotPrefab == null)
            Debug.LogWarning("[Minimap] enemyDotPrefab is not assigned.", this);
    }

    void LateUpdate()
    {
        if (player == null || minimapRoot == null || enemyDotPrefab == null) return;

        _scanTimer -= Time.deltaTime;
        if (_scanTimer <= 0f)
        {
            _enemies   = FindObjectsByType<SCRIPT_EnemyBase>(FindObjectsSortMode.None);
            _scanTimer = ScanInterval;
        }

        int enemyCount = _enemies.Length;

        // Grow pool to match enemy count; cache the Image component alongside.
        while (_dotPool.Count < enemyCount)
        {
            GameObject go = Instantiate(enemyDotPrefab, minimapRoot);
            go.name = "EnemyDot";
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(dotSize, dotSize);
            _dotPool.Add(rt);
            _dotImages.Add(go.GetComponent<Image>());
        }

        float      mapHalfSize = mapDisplayRadius;
        float      playerYaw   = player.eulerAngles.y;
        Quaternion invYaw      = Quaternion.Euler(0f, -playerYaw, 0f);

        for (int i = 0; i < _dotPool.Count; i++)
        {
            RectTransform dot = _dotPool[i];

            if (i >= enemyCount || _enemies[i] == null)
            {
                dot.gameObject.SetActive(false);
                continue;
            }

            dot.gameObject.SetActive(true);

            Vector3 worldOffset   = _enemies[i].transform.position - player.position;
            Vector3 rotatedOffset = invYaw * worldOffset;

            // XZ → minimap XY  (world Z-forward = minimap Y-up).
            float mapX = (rotatedOffset.x / worldRadius) * mapHalfSize;
            float mapY = (rotatedOffset.z / worldRadius) * mapHalfSize;

            Vector2 mapPos  = new Vector2(mapX, mapY);
            bool    clamped = mapPos.magnitude > mapHalfSize;

            if (clamped)
            {
                if (!clampToEdge)
                {
                    dot.gameObject.SetActive(false);
                    continue;
                }
                mapPos = mapPos.normalized * mapHalfSize;
            }

            dot.anchoredPosition = mapPos;

            // ── Height → color ────────────────────────────────────────────────
            // Signed: positive = enemy is above player, negative = below.
            float heightT = Mathf.Clamp(worldOffset.y / maxHeightDifference, -1f, 1f);
            Color dotColor = heightT >= 0f
                ? Color.Lerp(colorAtLevel, colorAbove,  heightT)
                : Color.Lerp(colorAtLevel, colorBelow, -heightT);

            // ── Proximity → scale ─────────────────────────────────────────────
            // Horizontal distance only — height shouldn't inflate the dot just
            // because an enemy is directly above/below you.
            float horizontalDist  = new Vector2(worldOffset.x, worldOffset.z).magnitude;
            float proximityT      = Mathf.Clamp01(horizontalDist / proximityRadius);
            float proximityScale  = Mathf.Lerp(maxDotScale, 1f, proximityT);

            float finalScale = (clamped ? clampedDotScale : 1f) * proximityScale;
            dot.localScale = new Vector3(finalScale, finalScale, 1f);

            // ── Apply color (alpha always 1 — color carries all height info) ──
            Image img = _dotImages[i];
            if (img != null)
                img.color = dotColor;
        }
    }
}
