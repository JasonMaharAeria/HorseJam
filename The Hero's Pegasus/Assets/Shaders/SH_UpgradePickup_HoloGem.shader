// ─────────────────────────────────────────────────────────────────────────────
//  SH_UpgradePickup_HoloGem
//
//  A holographic-energy-crystal effect designed for a bobbing/spinning cylinder
//  upgrade pickup.  Features (all animated, all tweakable per-material):
//
//    • Hue-cycling base colour — slowly drifts through the rainbow
//    • Fresnel rim glow        — bright silhouette edges, slightly out-of-phase pulse
//    • Breathing emission      — overall brightness rises and falls sinusoidally
//    • Animated scan bands     — sharp bright stripes that scroll up the world Y axis
//    • Hex-grid overlay        — Voronoi-based hexagonal lattice printed on the UVs
//    • Half-Lambert diffuse    — still responds to scene lights
//    • Semi-transparency       — glassy interior, nearly opaque at rim and features
//
//  Assign to a Material with Rendering Mode = Transparent, then apply to the
//  MeshRenderer of your upgrade cylinder prefabs.
// ─────────────────────────────────────────────────────────────────────────────
Shader "Custom/UpgradePickup_HoloGem"
{
    Properties
    {
        [Header(Base)]
        _BaseColor          ("Base Color",          Color)             = (0.2, 0.6, 1.0, 0.4)
        _Transparency       ("Body Transparency",   Range(0,1))        = 0.25

        [Header(Rim Glow)]
        _RimColor           ("Rim Color",           Color)             = (0.5, 0.9, 1.0, 1.0)
        _RimPower           ("Rim Power",           Float)             = 2.5
        _RimStrength        ("Rim Strength",        Float)             = 3.0

        [Header(Emission and Pulse)]
        _EmissionStrength   ("Emission Strength",   Float)             = 2.5
        _PulseSpeed         ("Pulse Speed (Hz)",    Float)             = 1.2
        _PulseMin           ("Pulse Min",           Range(0,1))        = 0.35

        [Header(Colour Cycling)]
        _HueShiftSpeed      ("Hue Shift Speed",     Float)             = 0.18
        _Saturation         ("Saturation",          Range(0,1))        = 0.85

        [Header(Scan Bands)]
        // Bands are in world-Y space.  Frequency = bands per world unit.
        // A scale-3 Unity cylinder is ~6 units tall; frequency 0.8 ≈ 5 bands visible.
        _ScanSpeed          ("Scroll Speed (wu/s)", Float)             = 0.35
        _ScanFrequency      ("Frequency",           Float)             = 0.8
        _ScanBrightness     ("Brightness",          Float)             = 0.75

        [Header(Hex Grid)]
        _HexScale           ("Scale",               Float)             = 3.5
        _HexLineWidth       ("Line Width",          Range(0.01, 0.35)) = 0.065
        _HexBrightness      ("Brightness",          Float)             = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back       // single-sided; cylinder exterior only

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ─────────────────────────────────────────────────────────────────
            // Structs
            // ─────────────────────────────────────────────────────────────────

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ─────────────────────────────────────────────────────────────────
            // Material CBuffer
            // ─────────────────────────────────────────────────────────────────

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half  _Transparency;
                half4 _RimColor;
                half  _RimPower;
                half  _RimStrength;
                half  _EmissionStrength;
                half  _PulseSpeed;
                half  _PulseMin;
                half  _HueShiftSpeed;
                half  _Saturation;
                half  _ScanSpeed;
                half  _ScanFrequency;
                half  _ScanBrightness;
                half  _HexScale;
                half  _HexLineWidth;
                half  _HexBrightness;
            CBUFFER_END

            // ─────────────────────────────────────────────────────────────────
            // Utility helpers
            // ─────────────────────────────────────────────────────────────────

            // Hue (0-1) → RGB, saturated at full value
            half3 HueToRGB(half h)
            {
                return saturate(abs(frac(h + half3(0.0h, 0.6667h, 0.3333h)) * 6.0h - 3.0h) - 1.0h);
            }

            // Hex-grid line strength at uv.
            // Returns 1 at cell-boundary lines, 0 at cell centres.
            // Method: two staggered rectangular lattices whose Voronoi boundaries
            // together form the hexagonal grid.  The boundary is where the two
            // nearest-centre distances are equal (da ≈ db).
            float HexLine(float2 uv, float scale, float lineWidth)
            {
                uv *= scale;
                const float2 s = float2(1.0, 1.73205080757); // (1, sqrt(3))
                float2 a = fmod(uv,           s) - s * 0.5;
                float2 b = fmod(uv - s * 0.5, s) - s * 0.5;
                float da = length(a);
                float db = length(b);
                // boundary = 0 at the perpendicular bisector between nearest centres
                float boundary = abs(da - db);
                return 1.0 - smoothstep(0.0, lineWidth, boundary);
            }

            // ─────────────────────────────────────────────────────────────────
            // Vertex shader
            // ─────────────────────────────────────────────────────────────────

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   ni = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = pi.positionCS;
                OUT.positionWS = pi.positionWS;
                OUT.normalWS   = ni.normalWS;
                OUT.viewDirWS  = GetWorldSpaceViewDir(pi.positionWS);
                OUT.uv         = IN.uv;
                OUT.fogFactor  = ComputeFogFactor(pi.positionCS.z);
                return OUT;
            }

            // ─────────────────────────────────────────────────────────────────
            // Fragment shader
            // ─────────────────────────────────────────────────────────────────

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;

                // ── 1. Animated hue-cycling colour ────────────────────────────
                // Slowly drifts through the full rainbow; saturation keeps it
                // vivid rather than washed out.
                half3 dynColor = lerp(1.0h.xxx, HueToRGB(frac(t * _HueShiftSpeed)), _Saturation);
                // Mix 50/50 with the inspector base colour so tinting still works.
                half3 baseRGB  = lerp(_BaseColor.rgb, dynColor, 0.5h);

                // ── 2. Fresnel rim glow ────────────────────────────────────────
                // Bright glow at the silhouette; also slightly pulsed to give the
                // impression the pickup is breathing.
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);
                float  NdotV   = saturate(dot(N, V));
                float  fresnel = pow(1.0 - NdotV, _RimPower);

                // Rim pulse is slightly out-of-phase with body pulse (offset by 1 rad).
                half rimPulse = sin(t * _PulseSpeed * (PI * 2.0) + 1.0h) * 0.3h + 0.7h;
                half3 rim     = fresnel * _RimStrength * rimPulse * _RimColor.rgb * dynColor;

                // ── 3. Breathing body emission ────────────────────────────────
                // Overall brightness rises and falls; _PulseMin prevents it from
                // going completely dark.
                half pulse    = lerp(_PulseMin, 1.0h,
                                     sin(t * _PulseSpeed * (PI * 2.0)) * 0.5h + 0.5h);
                half emission = _EmissionStrength * pulse;

                // ── 4. Animated scan bands (world-Y space) ────────────────────
                // A series of sharp bright stripes that scroll continuously upward.
                // Using pow(sin, 8) produces narrow peaks with smooth anti-aliasing.
                float scanPhase = IN.positionWS.y * _ScanFrequency - t * _ScanSpeed;
                float sinVal    = sin(scanPhase * PI);          // peaks at 0, ±2, ±4…
                float scanLine  = pow(max(0.0, sinVal), 8.0);   // very sharp, only positive
                half3 scanContrib = scanLine * _ScanBrightness * dynColor;

                // ── 5. Hex-grid overlay (UV space) ────────────────────────────
                // Scale V slightly so the hex cells look square on the cylinder
                // barrel (Unity's cylinder UV is ~1:2 width:height before scale).
                float2 hexUV     = float2(IN.uv.x, IN.uv.y * 0.55);
                float  hexLine   = HexLine(hexUV, _HexScale, _HexLineWidth);
                half3  hexContrib = hexLine * _HexBrightness * dynColor;

                // ── 6. Diffuse from main light (Half-Lambert) ─────────────────
                Light mainLight = GetMainLight();
                half  diffuse   = saturate(dot(N, mainLight.direction)) * 0.4h + 0.6h;

                // ── 7. Combine ────────────────────────────────────────────────
                // Everything is modulated by the pulsing emission value so the
                // whole pickup breathes together.
                half3 col = (baseRGB * diffuse + rim + scanContrib + hexContrib) * emission;

                // ── 8. Alpha ──────────────────────────────────────────────────
                // The body is mostly transparent; rim, hex lines, and scan bands
                // locally push alpha toward 1 so features are visible.
                half alpha = saturate(
                    _Transparency
                  + fresnel   * (1.0h - _Transparency)   // rim fully opaque
                  + hexLine   * 0.35h                    // hex lines semi-opaque
                  + scanLine  * 0.2h                     // scan bands add a little
                );

                col = MixFog(col, IN.fogFactor);
                return half4(col, alpha);
            }

            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
