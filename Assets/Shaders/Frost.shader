// Frost overlay shader (URP, transparent) with a little animation.
//
// Drawn as a slightly inflated copy of the pen mesh on top of it, so it is
// UV-mapped exactly like the pen: the frost texture is tiled across the model.
//
// Animation (all driven by _AnimSpeed, set it to 0 to freeze the effect):
//   - slow pattern drift so the ice looks alive
//   - crystals slowly "grow" (facet sharpness breathes)
//   - sparkles twinkle (each spot has its own phase)
//   - coverage gently breathes, like frost slowly creeping
//
// Texture requirement (see _MainTex):
//   - grayscale (white = frost, black = no frost), tileable / seamless
//   - 512x512 or 1024x1024 PNG, PNG without alpha is fine
//   - a frost / ice crystal / snowflake pattern works best
//   - if no texture is assigned the shader still works ("white" default)
//
// Visibility notes (same trap as the flame shader):
//   Average noise values are small, so alpha must be a plain product with a
//   boost and a floor, never a hard threshold - otherwise the effect vanishes.
//   A fresnel rim is added so the ice reads clearly on the silhouette.
//
// NOTE: keep this file ASCII-only. Non-ASCII bytes can break the shader compiler
// on some Windows setups (file is UTF-8, compiler may decode it as GBK).
Shader "PenBattle/Frost"
{
    Properties
    {
        _MainTex ("Frost Texture (grayscale, tileable)", 2D) = "white" {}

        [HDR] _FrostColor ("Frost Color", Color) = (0.42, 0.72, 1.00, 1.0)
        [HDR] _EdgeColor ("Highlight / Rim Color", Color) = (0.92, 1.00, 1.00, 1.0)

        _Tiling ("Texture Tiling", Range(0.1, 20)) = 1
        _Progress ("Frost Coverage", Range(0, 1)) = 0.85
        _Softness ("Coverage Softness", Range(0.01, 1)) = 0.30
        _TextureStrength ("Texture Strength", Range(0, 1)) = 0.75

        // Many frost textures are overall dark (or are colour images whose red
        // channel is dark), which would make the frost invisible. These remap
        // the texture range so a dark texture still reads as frost.
        _TexLow ("Texture Range Low", Range(0, 1)) = 0.05
        _TexHigh ("Texture Range High", Range(0, 1)) = 0.75
        _InvertTex ("Invert Texture (black = frost)", Range(0, 1)) = 0

        // Crystal / grain density multipliers. The base density comes from
        // _NoiseScaleOS (set at runtime from the pen size); these are relative.
        _CrystalScale ("Crystal Scale", Range(0.1, 8)) = 1
        _CrystalSharpness ("Crystal Sharpness", Range(0.2, 6)) = 1.2
        _GrainScale ("Grain Scale", Range(0.1, 8)) = 1

        // The crystal pattern is computed in OBJECT SPACE, not from UVs, because
        // some models have no UVs (or a degenerate UV island) on part of the mesh
        // - that would make the pattern constant there and the frost invisible
        // (symptom: "only the pen tip gets frost"). This scale is set at runtime
        // by FreezeVisual from the measured pen size.
        _NoiseScaleOS ("Pattern Scale (object space)", Float) = 20000

        // A pen is long and thin: an isotropic pattern would be either too coarse
        // across it or far too dense along it. This stretches the pattern along
        // the object's local Y (the pen's length) to balance the two.
        _LengthStretch ("Length Stretch", Range(0.05, 1)) = 0.25

        _Sparkle ("Sparkle", Range(0, 2)) = 0.7
        _RimPower ("Rim Power", Range(0.2, 8)) = 2.0
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.8

        _AnimSpeed ("Animation Speed", Range(0, 3)) = 1.0
        _Drift ("Pattern Drift (cells)", Range(0, 1)) = 0.15
        _Twinkle ("Twinkle Strength", Range(0, 2)) = 0.6
        _Breath ("Coverage Breathing", Range(0, 0.3)) = 0.06

        _SrcBlend ("Src Blend", Float) = 5
        _DstBlend ("Dst Blend", Float) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            Name "FrostUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _FrostColor;
                float4 _EdgeColor;
                float _Tiling;
                float _Progress;
                float _Softness;
                float _TextureStrength;
                float _TexLow;
                float _TexHigh;
                float _InvertTex;
                float _CrystalScale;
                float _CrystalSharpness;
                float _GrainScale;
                float _NoiseScaleOS;
                float _LengthStretch;
                float _Sparkle;
                float _RimPower;
                float _RimStrength;
                float _AnimSpeed;
                float _Drift;
                float _Twinkle;
                float _Breath;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float2 Hash22(float2 p)
            {
                float2 q = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(q) * 43758.5453123);
            }

            // Voronoi cell borders -> ice crystal facets
            float VoronoiF1(float2 p)
            {
                float2 n = floor(p);
                float2 f = frac(p);

                float minDist = 8.0;

                [unroll]
                for (int vj = -1; vj <= 1; vj++)
                {
                    [unroll]
                    for (int vi = -1; vi <= 1; vi++)
                    {
                        float2 g = float2((float)vi, (float)vj);
                        float2 o = Hash22(n + g);
                        float2 r = g + o - f;
                        minDist = min(minDist, length(r));
                    }
                }

                return saturate(minDist);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                OUT.positionOS = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float tt = _Time.y * _AnimSpeed;

                // Object-space pattern coords. The crystal/grain pattern is NOT
                // taken from UVs: some models have no UVs (or a degenerate UV
                // island) on part of the mesh, which made the pattern constant
                // there -> the frost showed only on the pen tip.
                float3 pOS = IN.positionOS * _NoiseScaleOS;

                // stretch the pattern along the object's local Y (the pen's length),
                // otherwise a long thin pen gets far too many crystal cells along it
                pOS.y *= _LengthStretch;

                // slow drift so the ice looks alive (amplitude is in pattern cells)
                pOS.xy += float2(sin(tt * 0.5), cos(tt * 0.37)) * _Drift;

                // 1) artist texture (UV based, optional; white when nothing assigned)
                //    Used as a modulation with a floor, not a plain multiply: a dark
                //    texture can therefore never wipe the frost out.
                float texRaw = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv * _Tiling).r;
                texRaw = lerp(texRaw, 1.0 - texRaw, saturate(_InvertTex));

                float texCentered = smoothstep(_TexLow, max(_TexHigh, _TexLow + 0.001), texRaw);
                float texMod = saturate(0.45 + 1.10 * texCentered);   // 0.45 .. 1
                float tex = lerp(1.0, texMod, _TextureStrength);

                // 2) procedural ice crystals: Voronoi cell borders in object space.
                //    Sharpness breathes a little so the crystals look like they are
                //    slowly growing.
                float sharp = _CrystalSharpness * (1.0 + 0.18 * sin(tt * 0.7));
                float f1 = VoronoiF1(pOS.xy * _CrystalScale);
                float crystal = pow(saturate(f1), sharp);

                // 3) fine grain / snow dust (object space too)
                float grain = ValueNoise(pOS.xy * 2.3 * _GrainScale + pOS.z * 1.7);

                // boosted range: crystals separate clearly from the base
                float pat = tex * (0.15 + 1.10 * crystal) * (0.40 + 1.00 * grain);

                // 4) coverage: growing _Progress freezes more of the surface,
                //    plus a gentle breathing so the frost slowly creeps
                float progress = saturate(_Progress + sin(tt * 0.8) * _Breath);
                float edge = 1.0 - progress;
                float mask = smoothstep(edge - _Softness, edge + _Softness, pat);

                // 5) sparkle, twinkling (each spot has its own phase)
                float twinkle = lerp(1.0, 0.35 + 1.3 * (0.5 + 0.5 * sin(tt * 3.0 + grain * 24.0)), _Twinkle);
                float sparkle = saturate(pow(pat, 4.0)) * _Sparkle * twinkle;

                // 6) fresnel rim: ice reads best on the silhouette
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimStrength;

                float3 col = lerp(_FrostColor.rgb, _EdgeColor.rgb, saturate(mask * 0.7 + sparkle + rim));
                float alpha = saturate(mask * _FrostColor.a + sparkle * 0.6 + rim * 0.7);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
