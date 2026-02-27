using UnityEngine;

/// <summary>
/// Spawns and recycles a pool of particle-based clouds around the player.
/// - Clouds drift with wind.
/// - Laser particles within laserDestroyRadius destroy cloud particles.
/// - Flying through clouds pushes particles outward from the pegasus.
/// </summary>
public class SCRIPT_CloudManager : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("Auto-found if left empty")]
    public Transform player;

    [Header("Pool")]
    public int   cloudCount      = 60;
    [Tooltip("XZ radius of the cloud field around the player")]
    public float spawnRadius     = 300f;
    [Tooltip("Max Y distance above and below the player")]
    public float verticalRange   = 80f;
    public float minSpawnDist    = 40f;

    [Header("Recycling")]
    [Tooltip("A cloud this far behind the player gets repositioned ahead")]
    public float recycleDistance = 200f;

    [Header("Anti Pop-in")]
    [Tooltip("Camera used for viewport checks. Auto-finds Camera.main if left empty.")]
    public Camera sceneCamera;
    [Tooltip("Minimum distance from the player when placing a recycled cloud. " +
             "Keep this large so the cloud has time to drift into view naturally.")]
    public float recycleMinDist  = 120f;
    [Tooltip("How far outside the screen edges (in viewport units) a position must be " +
             "before it is accepted as a spawn point. 0 = exactly at edge, 0.15 = 15% outside.")]
    public float fovSpawnMargin  = 0.15f;

    [Header("Wind")]
    [Tooltip("World-space drift per second. Keep Y at 0 for purely horizontal wind.")]
    public Vector3 windVelocity = new Vector3(2f, 0f, 0f);

    [Header("Cloud Appearance")]
    public int   particlesPerCloud = 18;
    public float minPuffSize       = 8f;
    public float maxPuffSize       = 28f;
    [Tooltip("Emission sphere radius — how spread out the puffs are within one cloud")]
    public float cloudSpread       = 6f;
    [Range(0f, 1f)]
    public float cloudAlpha        = 0.45f;
    public Color cloudTint         = Color.white;

    [Header("Laser Interaction")]
    [Tooltip("World-space radius around each laser particle that destroys cloud particles. " +
             "Increase this if destruction feels weak — try values between 5 and 20.")]
    public float laserDestroyRadius = 10f;

    [Header("Player Displacement")]
    [Tooltip("Cloud particles within this radius of the player get pushed away")]
    public float playerPushRadius = 20f;
    [Tooltip("Peak outward push force applied at point-blank range (falls off to zero at playerPushRadius)")]
    public float playerPushForce  = 40f;
    [Tooltip("Exponential drag coefficient — higher = particles slow down faster (try 2–6)")]
    public float particleDrag     = 3f;

    [Header("Performance")]
    [Tooltip("Seconds between GetParticles/SetParticles passes (displacement + laser destruction). " +
             "0 = every frame. 0.05 = 20 Hz — cuts GPU buffer uploads to 1/3 with no visible difference.")]
    public float particleUpdateInterval = 0.05f;

    [Header("Optional")]
    [Tooltip("Cloud-texture particle material. Leave empty to use Unity's default soft-circle particle.")]
    public Material cloudMaterial;

    // ── private state ──────────────────────────────────────────────────────────
    private Transform[]      cloudTransforms;
    private ParticleSystem[] cloudSystems;
    private bool[]           cloudHasVelocity; // true = particles have been displaced, need drag pass

    private ParticleSystem.Particle[] cloudBuffer;
    private ParticleSystem.Particle[] laserBuffer;

    private SCRIPT_LaserController laser;
    private bool                    laserIsWorldSpace;
    private Camera                  mainCam;
    private float                   _particleUpdateTimer;

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
            Debug.LogWarning("SCRIPT_CloudManager: no player found.", this);
            return;
        }

        laser   = FindFirstObjectByType<SCRIPT_LaserController>();
        mainCam = (sceneCamera != null) ? sceneCamera : Camera.main;
        cloudBuffer = new ParticleSystem.Particle[particlesPerCloud + 4];
        laserBuffer = new ParticleSystem.Particle[512];

        if (laser != null && laser.LaserPS != null)
            laserIsWorldSpace = laser.LaserPS.main.simulationSpace == ParticleSystemSimulationSpace.World;

        BuildPool();
    }

    void Update()
    {
        if (player == null || cloudTransforms == null) return;

        Vector3 playerPos = player.position;
        Vector3 playerFwd = player.forward;

        // ── Wind + recycle: cheap transforms, run every frame ─────────────────
        for (int i = 0; i < cloudTransforms.Length; i++)
        {
            cloudTransforms[i].position += windVelocity * Time.deltaTime;

            Vector3 toCloud   = cloudTransforms[i].position - playerPos;
            bool tooFarBehind = Vector3.Dot(toCloud, -playerFwd) > recycleDistance;
            bool outsideField = toCloud.magnitude > spawnRadius * 1.2f;
            bool emptied      = cloudSystems[i].particleCount == 0;

            if (tooFarBehind || outsideField || emptied)
                Reposition(i, playerPos, playerFwd);
        }

        // ── GetParticles / SetParticles: throttled ────────────────────────────
        // These calls marshal managed <-> native memory and upload GPU buffers.
        // Running them at 20 Hz instead of 60 Hz cuts the upload cost by ~2/3
        // with no perceptible visual difference.
        _particleUpdateTimer += Time.deltaTime;
        if (_particleUpdateTimer < particleUpdateInterval) return;
        _particleUpdateTimer = 0f;

        int laserCount = 0;
        if (laser != null && laser.IsLaserActive && laser.LaserPS != null)
            laserCount = laser.LaserPS.GetParticles(laserBuffer);

        for (int i = 0; i < cloudTransforms.Length; i++)
        {
            if (laserCount > 0)
                HandleLaserDestruction(i, laserCount);

            HandlePlayerDisplacement(i, playerPos);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────

    void HandleLaserDestruction(int cloudIndex, int laserCount)
    {
        Transform cloudTf = cloudTransforms[cloudIndex];

        // Bounding check: if the closest laser particle is further than
        // (cloudSpread + laserDestroyRadius) from the cloud centre, skip.
        float thresholdSq = (cloudSpread + laserDestroyRadius) * (cloudSpread + laserDestroyRadius);
        float minDistSq   = float.MaxValue;

        for (int k = 0; k < laserCount; k++)
        {
            float sq = (cloudTf.position - LaserWorldPos(k)).sqrMagnitude;
            if (sq < minDistSq) minDistSq = sq;
            if (minDistSq < thresholdSq) break; // close enough — proceed to particle check
        }

        if (minDistSq >= thresholdSq) return;

        int  count        = cloudSystems[cloudIndex].GetParticles(cloudBuffer);
        bool anyKilled    = false;
        float destroySq   = laserDestroyRadius * laserDestroyRadius;

        for (int j = 0; j < count; j++)
        {
            Vector3 cloudWorldPos = cloudTf.TransformPoint(cloudBuffer[j].position);

            for (int k = 0; k < laserCount; k++)
            {
                if ((cloudWorldPos - LaserWorldPos(k)).sqrMagnitude < destroySq)
                {
                    cloudBuffer[j].remainingLifetime = 0f;
                    anyKilled = true;
                    break;
                }
            }
        }

        if (anyKilled)
            cloudSystems[cloudIndex].SetParticles(cloudBuffer, count);
    }

    void HandlePlayerDisplacement(int cloudIndex, Vector3 playerPos)
    {
        Transform cloudTf     = cloudTransforms[cloudIndex];
        bool      nearPlayer  = Vector3.Distance(cloudTf.position, playerPos) < playerPushRadius + cloudSpread;
        bool      hasVelocity = cloudHasVelocity[cloudIndex];

        // Nothing to do if the cloud isn't near the player and has no prior velocity
        if (!nearPlayer && !hasVelocity) return;

        int  count      = cloudSystems[cloudIndex].GetParticles(cloudBuffer);
        bool anyVel     = false;
        bool anyChanged = false;

        float dragMult     = Mathf.Exp(-particleDrag * Time.deltaTime); // framerate-independent
        float pushRadiusSq = playerPushRadius * playerPushRadius;

        for (int j = 0; j < count; j++)
        {
            // ── Push outward from player ──────────────────────────────────────
            if (nearPlayer)
            {
                Vector3 worldPos   = cloudTf.TransformPoint(cloudBuffer[j].position);
                Vector3 toParticle = worldPos - playerPos;
                float   distSq     = toParticle.sqrMagnitude;

                if (distSq < pushRadiusSq && distSq > 0.0001f)
                {
                    float dist     = Mathf.Sqrt(distSq);
                    float strength = Mathf.Lerp(playerPushForce, 0f, dist / playerPushRadius);

                    // Push is in world space; convert to local space (sim space = Local)
                    Vector3 worldPush = (toParticle / dist) * strength * Time.deltaTime;
                    cloudBuffer[j].velocity += cloudTf.InverseTransformDirection(worldPush);
                    anyChanged = true;
                }
            }

            // ── Drag — applied whenever the particle is moving ────────────────
            if (cloudBuffer[j].velocity.sqrMagnitude > 0.0001f)
            {
                cloudBuffer[j].velocity *= dragMult;
                anyChanged = true;
                anyVel     = true;
            }
        }

        cloudHasVelocity[cloudIndex] = anyVel;

        if (anyChanged)
            cloudSystems[cloudIndex].SetParticles(cloudBuffer, count);
    }

    Vector3 LaserWorldPos(int k)
    {
        Vector3 pos = laserBuffer[k].position;
        return laserIsWorldSpace ? pos : laser.transform.TransformPoint(pos);
    }

    // ──────────────────────────────────────────────────────────────────────────

    void BuildPool()
    {
        cloudTransforms  = new Transform[cloudCount];
        cloudSystems     = new ParticleSystem[cloudCount];
        cloudHasVelocity = new bool[cloudCount];

        for (int i = 0; i < cloudCount; i++)
        {
            var go = new GameObject($"Cloud_{i:00}");
            go.transform.SetParent(transform);
            cloudTransforms[i] = go.transform;
            cloudSystems[i]    = BuildCloudParticleSystem(go);
            cloudTransforms[i].position = RandomPosition(player.position, false);
        }
    }

    ParticleSystem BuildCloudParticleSystem(GameObject go)
    {
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop            = false;
        main.startLifetime   = 99999f;
        main.startSpeed      = 0f;
        main.startSize       = new ParticleSystem.MinMaxCurve(minPuffSize, maxPuffSize);
        main.startColor      = new Color(cloudTint.r, cloudTint.g, cloudTint.b, cloudAlpha);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles    = particlesPerCloud + 4;
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, particlesPerCloud) });

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = cloudSpread;

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        if (cloudMaterial != null) rend.material = cloudMaterial;

        ps.Play();
        return ps;
    }

    void Reposition(int index, Vector3 playerPos, Vector3 playerFwd)
    {
        cloudTransforms[index].position  = RandomPosition(playerPos, true);
        cloudHasVelocity[index]          = false; // clear displacement state
        cloudSystems[index].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        cloudSystems[index].Play();
    }

    Vector3 RandomPosition(Vector3 center, bool recycling)
    {
        float minDist = recycling ? recycleMinDist : Mathf.Max(minSpawnDist, recycleMinDist);

        // More attempts when recycling because we also need to pass the FOV test
        int maxAttempts = recycling ? 40 : 16;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Pick a random angle and distance, biased toward the forward hemisphere
            // so recycled clouds naturally drift into view ahead of the player
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist  = Random.Range(minDist, spawnRadius);
            float y     = Random.Range(-verticalRange, verticalRange);

            Vector3 candidate = center + new Vector3(
                Mathf.Cos(angle) * dist,
                y,
                Mathf.Sin(angle) * dist);

            // When recycling, reject any position the camera can currently see
            if (recycling && !IsOutsideFOV(candidate)) continue;

            if (Vector3.Distance(candidate, center) >= minDist)
                return candidate;
        }

        // Fallback: directly behind the player at near-max distance —
        // always outside the forward-facing camera's view.
        return center - player.forward * spawnRadius * 0.85f
                      + Vector3.up * Random.Range(-verticalRange * 0.5f, verticalRange * 0.5f);
    }

    // Returns true if worldPos is outside the camera's viewport (plus fovSpawnMargin buffer).
    // Positions behind the camera are always considered outside.
    bool IsOutsideFOV(Vector3 worldPos)
    {
        if (mainCam == null) return true; // no camera reference — allow anywhere

        Vector3 vp = mainCam.WorldToViewportPoint(worldPos);

        if (vp.z < 0f) return true; // behind the camera

        return vp.x < -fovSpawnMargin || vp.x > 1f + fovSpawnMargin ||
               vp.y < -fovSpawnMargin || vp.y > 1f + fovSpawnMargin;
    }
}
