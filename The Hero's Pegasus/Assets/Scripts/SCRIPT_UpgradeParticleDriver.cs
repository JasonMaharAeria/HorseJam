using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Programmatically builds and drives a child ParticleSystem that creates a
/// holographic energy-aura around the upgrade pickup cylinder.
///
/// Particles emit from the actual cylinder mesh surface, drift outward with
/// waving noise, and sync their hue/pulse exactly to the SH_UpgradePickup_HoloGem
/// shader on this object's MeshRenderer.
///
/// SETUP:
///   1. Add this component to the upgrade pickup root (same GameObject as the
///      MeshRenderer / HoloGem material).
///   2. Optionally assign a pre-made URP Particles/Unlit Additive material to
///      the <ParticleMaterial> slot.  If left empty, one is generated at runtime.
///   3. A child "PS_HoloAura" ParticleSystem is created automatically at Awake
///      if one is not already present.
///
/// SOFT-PARTICLES NOTE:
///   For the blur/fade-at-edges effect to work the URP Forward Renderer asset
///   needs "Depth Texture" enabled (Renderer → Depth Texture = Enabled).
/// </summary>
[DisallowMultipleComponent]
public class SCRIPT_UpgradeParticleDriver : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("Material (optional)")]
    [Tooltip("URP Particles/Unlit material with Additive blend + Soft Particles enabled. "
           + "Leave null to auto-generate one at runtime.")]
    public Material particleMaterial;

    [Header("Emission")]
    [Tooltip("Particles spawned per second.")]
    public float emissionRate = 30f;

    [Header("Motion")]
    [Range(0f, 1f)]
    [Tooltip("How much the noise module pushes particles sideways (waving amplitude).")]
    public float noiseStrength = 0.22f;

    [Tooltip("How fast the noise field drifts (makes the wave pattern shift over time).")]
    public float noiseScrollSpeed = 0.38f;

    [Tooltip("Speed particles drift radially away from the cylinder axis.")]
    public float radialDriftSpeed = 0.18f;

    [Header("Particle Appearance")]
    [Tooltip("Min/max particle lifetime in seconds.")]
    public Vector2 lifetime  = new Vector2(0.8f, 1.8f);

    [Tooltip("Min/max particle size in world units (before object scale).")]
    public Vector2 sizeRange = new Vector2(0.04f, 0.16f);

    [Tooltip("How far above the mesh surface particles are spawned (world units).")]
    public float surfaceNormalOffset = 0.07f;

    // ── Private state ──────────────────────────────────────────────────────

    ParticleSystem _ps;
    MeshRenderer   _mr;

    // Values read from the HoloGem shader on the MeshRenderer (or defaults)
    float _hueRate  = 0.18f;
    float _pulseHz  = 1.2f;
    float _pulseMin = 0.35f;
    float _sat      = 0.85f;

    // Cached shader property IDs
    static readonly int ID_HueShift = Shader.PropertyToID("_HueShiftSpeed");
    static readonly int ID_Pulse    = Shader.PropertyToID("_PulseSpeed");
    static readonly int ID_PulseMin = Shader.PropertyToID("_PulseMin");
    static readonly int ID_Sat      = Shader.PropertyToID("_Saturation");

    // ── Unity lifecycle ────────────────────────────────────────────────────

    void Awake()
    {
        _mr = GetComponent<MeshRenderer>();
        ReadShaderProperties();
        _ps = EnsureChildParticleSystem();
        BuildParticleSystem();
    }

    void Update()
    {
        if (_ps == null) return;
        SyncStartColour();
    }

    // ── Setup helpers ──────────────────────────────────────────────────────

    // Pulls timing/colour parameters from the HoloGem material so the aura
    // stays in lockstep with the gem body without duplicating magic numbers.
    void ReadShaderProperties()
    {
        if (_mr == null) return;
        var mat = _mr.sharedMaterial;
        if (mat == null) return;

        if (mat.HasProperty(ID_HueShift)) _hueRate  = mat.GetFloat(ID_HueShift);
        if (mat.HasProperty(ID_Pulse))    _pulseHz  = mat.GetFloat(ID_Pulse);
        if (mat.HasProperty(ID_PulseMin)) _pulseMin = mat.GetFloat(ID_PulseMin);
        if (mat.HasProperty(ID_Sat))      _sat      = mat.GetFloat(ID_Sat);
    }

    ParticleSystem EnsureChildParticleSystem()
    {
        var existing = GetComponentInChildren<ParticleSystem>(true);
        if (existing != null) return existing;

        var go = new GameObject("PS_HoloAura");
        go.transform.SetParent(transform, false);
        return go.AddComponent<ParticleSystem>();
    }

    void BuildParticleSystem()
    {
        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // ── Main ──────────────────────────────────────────────────────────
        var main = _ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(lifetime.x,  lifetime.y);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.04f, 0.20f);
        main.startSize       = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
        main.maxParticles    = 200;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor      = Color.white;   // overwritten every frame by SyncStartColour

        // ── Emission ──────────────────────────────────────────────────────
        var emission = _ps.emission;
        emission.rateOverTime = emissionRate;

        // ── Shape — emit from the cylinder mesh surface ───────────────────
        // Unity's built-in cylinder (fileID 10206) is always readable,
        // so MeshRenderer shape will work.  BoxShell is the fallback.
        var shape = _ps.shape;
        shape.enabled      = true;
        shape.normalOffset = surfaceNormalOffset;

        var mf    = GetComponent<MeshFilter>();
        bool ok   = _mr != null && mf != null
                 && mf.sharedMesh != null && mf.sharedMesh.isReadable;
        if (ok)
        {
            shape.shapeType     = ParticleSystemShapeType.MeshRenderer;
            shape.meshRenderer  = _mr;
            shape.useMeshColors = true;   // reads vertex colours; no-op on default cylinder
        }
        else
        {
            // Approximate the cylinder barrel
            shape.shapeType = ParticleSystemShapeType.BoxShell;
            shape.scale     = new Vector3(1f, 2f, 1f);
        }

        // ── Velocity over lifetime — radial outward + gentle swirl ────────
        var vel = _ps.velocityOverLifetime;
        vel.enabled  = true;
        vel.space    = ParticleSystemSimulationSpace.Local;
        vel.radial   = new ParticleSystem.MinMaxCurve(radialDriftSpeed);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(0.35f);  // slow orbit around Y

        // ── Noise — organic waving motion ─────────────────────────────────
        var noise = _ps.noise;
        noise.enabled     = true;
        noise.strength    = new ParticleSystem.MinMaxCurve(noiseStrength);
        noise.frequency   = 0.65f;
        noise.scrollSpeed = new ParticleSystem.MinMaxCurve(noiseScrollSpeed);
        noise.damping     = true;
        noise.quality     = ParticleSystemNoiseQuality.Medium;

        // ── Colour over lifetime — rapid fade-in, gradual fade-out ────────
        var col  = _ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f),
            },
            new[]
            {
                new GradientAlphaKey(0f,    0f),      // born invisible
                new GradientAlphaKey(1f,    0.12f),   // snap to full alpha
                new GradientAlphaKey(0.70f, 0.55f),   // hold semi-opaque
                new GradientAlphaKey(0f,    1f),      // dissolve away
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // ── Size over lifetime — bloom out, then slowly shrink ────────────
        var sizeOL = _ps.sizeOverLifetime;
        sizeOL.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f,    0f,   0f,   4f),
            new Keyframe(0.18f, 1f,   0f,   0f),
            new Keyframe(1f,    0.35f, -0.5f, 0f)
        );
        sizeOL.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // ── Renderer — additive billboard with soft-particle blur at edges ─
        var rend = _ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode   = ParticleSystemRenderMode.Billboard;
        rend.sortingFudge = -5f;   // draw in front of the cylinder body
        rend.material     = particleMaterial != null
                                ? particleMaterial
                                : BuildAdditiveMaterial();

        _ps.Play();
    }

    // Creates a URP Particles/Unlit material with additive blending and soft
    // particles enabled.  If the shader isn't found (non-URP project) falls
    // back to the legacy Particles shader gracefully.
    static Material BuildAdditiveMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Particles/Standard Unlit");

        if (shader == null) return null;

        var mat = new Material(shader) { name = "MAT_HoloAura_Generated" };

        mat.SetFloat("_Surface",  1f);                              // Transparent
        mat.SetFloat("_Blend",    2f);                              // Additive
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.One);
        mat.SetFloat("_ZWrite",   0f);
        mat.SetColor("_BaseColor", Color.white);

        // Soft particles — fade/blur where particles clip into scene geometry
        mat.SetFloat("_SoftParticlesEnabled",          1f);
        mat.SetFloat("_SoftParticlesFadeNearDistance", 0.08f);
        mat.SetFloat("_SoftParticlesFadeFarDistance",  0.5f);

        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3001;

        return mat;
    }

    // ── Per-frame colour sync ──────────────────────────────────────────────

    // Keeps the particles in chromatic lockstep with the HoloGem shader.
    // Hue is offset by +0.08 (≈30°) so the aura reads as a complementary
    // tonal shift rather than an exact duplicate of the gem colour.
    void SyncStartColour()
    {
        float hue   = Mathf.Repeat(Time.time * _hueRate + 0.08f, 1f);
        float pulse = Mathf.Sin(Time.time * _pulseHz * Mathf.PI * 2f) * 0.5f + 0.5f;
        float val   = Mathf.Lerp(_pulseMin, 1f, pulse);

        Color c = Color.HSVToRGB(hue, _sat, val);

        var m = _ps.main;
        m.startColor = new ParticleSystem.MinMaxGradient(c);
    }
}
