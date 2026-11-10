// Procedural flame sheet for billboard quads (URP unlit, no texture needed).
//
// Technique (classic Shader Graph fire):
//   Gradient Noise (low freq, overall shape) x Voronoi cell veining (high freq, flame tongues)
//   UV shaping: wide at the bottom, narrow at the top, faded near the tip.
//
// IMPORTANT about alpha:
//   Multiplying two noises gives a very small average value, so a hard threshold
//   (smoothstep(threshold, 1, x)) makes the whole flame invisible.
//   Here alpha is a plain product with an _Opacity boost and a _Contrast curve.
//
// Blending is driven by _SrcBlend/_DstBlend (default 5/10 = alpha blend;
// set them to 5/1 to get additive blend).
//
// NOTE: keep this file ASCII-only. Non-ASCII bytes can break the shader compiler
// on some Windows setups (file is UTF-8, compiler may decode it as GBK).
Shader "PenBattle/Flame"
{
    Properties
    {
        [HDR] _HotColor ("Hot Core Color", Color) = (0.99, 0.78, 0.0, 1)
        [HDR] _Color ("Flame Color", Color) = (0.94, 0.60, 0.04, 1)
        _Intensity ("Intensity", Range(0, 3)) = 1

        _Speed ("Flow Speed", Float) = 1.6
        _NoiseScale ("Gradient Noise Density", Range(0.5, 16)) = 3
        _CellDensity ("Cell Density", Range(0.5, 20)) = 5
        _Density ("Cell Contrast", Range(0.2, 6)) = 1.4
        _TongueAmount ("Tongue Amount", Range(0, 1)) = 0.7
        _Sway ("Sway Amount", Range(0, 1)) = 0.25
        _VerticalStretch ("Vertical Stretch", Range(0.5, 6)) = 2.2

        _EndFade ("Top Fade", Range(0.2, 4)) = 1.4
        _Opacity ("Opacity", Range(0, 5)) = 2.2
        _Contrast ("Alpha Contrast", Range(0.2, 3)) = 0.8

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
            Name "FlameUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _HotColor;
                float _Intensity;
                float _Speed;
                float _NoiseScale;
                float _CellDensity;
                float _Density;
                float _TongueAmount;
                float _Sway;
                float _VerticalStretch;
                float _EndFade;
                float _Opacity;
                float _Contrast;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            // gradient (value) noise: low frequency, drives the overall shape
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

            float GradientNoise(float2 p)
            {
                float sum = 0.0;
                float amp = 0.5;

                [unroll]
                for (int g = 0; g < 4; g++)
                {
                    sum += amp * ValueNoise(p);
                    p = p * 2.03 + 13.7;
                    amp *= 0.5;
                }

                return sum;
            }

            float2 Hash22(float2 p)
            {
                float2 q = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(q) * 43758.5453123);
            }

            // Voronoi: distance to the nearest feature point (small near cell centers).
            // Bright cell borders become the flame veins / tongues.
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
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float t = _Time.y * _Speed;

                // horizontal sway so the flame feels alive
                float sway = (ValueNoise(float2(uv.y * 2.6, t * 0.45)) - 0.5) * _Sway;
                float2 suv = float2(uv.x + sway, uv.y);

                // 1) low frequency gradient noise, normalized to 0..1
                float2 gradUV = float2(suv.x * _NoiseScale, suv.y * _NoiseScale * _VerticalStretch - t);
                float gn = GradientNoise(gradUV);
                gn = saturate((gn - 0.12) / 0.62);

                // 2) high frequency Voronoi -> flame veins / tongues
                float2 cellUV = float2(suv.x * _CellDensity, suv.y * _CellDensity * _VerticalStretch - t * 1.35);
                float f1 = VoronoiF1(cellUV);
                float cell = pow(smoothstep(0.10, 0.50, f1), _Density);

                // keep a floor (1 - _TongueAmount) so the veins never crush the flame away
                float flame = gn * lerp(1.0, cell, _TongueAmount);

                // 3) UV shaping: wide bottom, narrow top
                float width = lerp(0.62, 0.06, uv.y);
                float lateral = 1.0 - saturate(abs(uv.x - 0.5) / max(width, 0.001));
                float vertical = pow(saturate(1.0 - uv.y), _EndFade);
                float shape = lateral * vertical;

                // 4) alpha: plain product with a boost + contrast curve (NO hard threshold)
                float alpha = saturate(pow(saturate(flame * shape * _Opacity), _Contrast));

                // 5) color: outer orange-red -> hot yellow core
                float3 col = lerp(_Color.rgb, _HotColor.rgb, saturate(pow(alpha, 1.4))) * _Intensity;

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
