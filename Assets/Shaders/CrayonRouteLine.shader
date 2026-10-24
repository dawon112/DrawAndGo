Shader "DrawAndGo/CrayonRouteLine"
{
    // Dudu's ground lines (CrayonRouteLine). Unlike the drawn-stroke shader, nothing here
    // repeats along the line: the outline drifts with layered noise at unrelated
    // frequencies, and the crayon grain mixes the mask at two scales with fine paper
    // tooth that shows through mostly near the edges, like crayon dragged over paper.
    Properties
    {
        _BaseMap ("Crayon Mask", 2D) = "white" {}
        _BaseColor ("Line Color", Color) = (0.07, 0.06, 0.07, 1)
        _MinOpacity ("Minimum Opacity", Range(0, 1)) = 0.97
        _EdgeDrift ("Edge Drift", Range(0, 0.4)) = 0.2
        _EdgeRough ("Edge Roughness", Range(0, 0.3)) = 0.1
        _EdgeSoftness ("Edge Softness", Range(0.005, 0.2)) = 0.03
        _Tooth ("Paper Tooth", Range(0, 1)) = 1
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "CrayonRouteLineUnlit"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _MinOpacity;
                float _EdgeDrift;
                float _EdgeRough;
                float _EdgeSoftness;
                float _Tooth;
                float _Seed;
            CBUFFER_END

            float Hash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            float Noise1(float x)
            {
                float i = floor(x);
                float f = frac(x);
                float u = f * f * (3.0 - 2.0 * f);
                return lerp(Hash(float2(i, _Seed)), Hash(float2(i + 1.0, _Seed)), u);
            }

            float Noise2(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash(i + _Seed);
                float b = Hash(i + float2(1.0, 0.0) + _Seed);
                float c = Hash(i + float2(0.0, 1.0) + _Seed);
                float d = Hash(i + float2(1.0, 1.0) + _Seed);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // How much of the line's width is missing at one edge: slow swells,
            // smaller bumps and a little fine roughness, none in step with the others.
            float EdgeCut(float along, float offset)
            {
                float swell = Noise1(along * 1.13 + offset) * 0.6 + Noise1(along * 2.87 + offset * 1.7) * 0.4;
                float rough = Noise1(along * 23.7 + offset * 2.3) * 0.55 + Noise1(along * 61.3 + offset * 3.1) * 0.45;
                return 0.02 + swell * _EdgeDrift + rough * _EdgeRough;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // uv.x runs along the line in world units (Tile mode); uv.y crosses it.
                float along = input.uv.x;
                float across = input.uv.y;

                float lowerCut = EdgeCut(along, 3.1);
                float upperCut = EdgeCut(along, 47.9);
                float lower = smoothstep(lowerCut, lowerCut + _EdgeSoftness, across);
                float upper = smoothstep(upperCut, upperCut + _EdgeSoftness, 1.0 - across);
                float body = lower * upper;
                // 0 at the ragged edge, 1 in the middle of the stroke.
                float depth = saturate(min(across - lowerCut, 1.0 - across - upperCut) * 8.0);

                // Crayon grain: the mask at two unrelated scales and offsets, so its
                // pattern never lines up into a visible repeat.
                float2 grainA = float2(along, across) * _BaseMap_ST.xy + _BaseMap_ST.zw;
                float2 grainB = float2(along * 0.613 + 0.29 + _Seed * 0.17, across * 0.71 + 0.13);
                const float3 luminance = float3(0.299, 0.587, 0.114);
                float grain = max(dot(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, grainA).rgb, luminance),
                                  dot(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, grainB).rgb, luminance));
                float density = lerp(_MinOpacity, 1.0, smoothstep(0.05, 0.6, grain));

                // Paper tooth: tiny bare specks where the crayon skipped, more at the edges.
                float tooth = Noise2(float2(along * 97.0, across * 15.0)) * 0.6
                            + Noise2(float2(along * 213.0 + 7.3, across * 33.0)) * 0.4;
                float threshold = lerp(0.6, 0.8, depth);
                float bare = smoothstep(threshold, threshold + 0.08, tooth) * _Tooth;

                float alpha = body * density * (1.0 - bare) * _BaseColor.a * input.color.a;
                return half4(_BaseColor.rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
