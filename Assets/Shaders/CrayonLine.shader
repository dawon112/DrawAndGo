Shader "DrawAndGo/CrayonLine"
{
    Properties
    {
        _BaseMap ("Crayon Mask", 2D) = "white" {}
        _BaseColor ("Line Color", Color) = (0.086, 0.498, 0.765, 1)
        _MinOpacity ("Minimum Opacity", Range(0, 1)) = 0.72
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "CrayonLineUnlit"
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
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Overlap offset parts of the supplied mask so the stroke stays dense
                // while the small differences still read as crayon grain.
                half3 sampleA = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half3 sampleB = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv + half2(0.31h, 0.06h)).rgb;
                half3 sampleC = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv + half2(0.67h, -0.05h)).rgb;
                const half3 luminanceWeights = half3(0.299h, 0.587h, 0.114h);
                half brightness = max(dot(sampleA, luminanceWeights),
                    max(dot(sampleB, luminanceWeights), dot(sampleC, luminanceWeights)));
                half textureOpacity = smoothstep(0.02h, 0.55h, brightness);
                // Read two independent bands of the mask to carve a different,
                // irregular silhouette into the top and bottom of the stroke.
                half lowerGrain = dot(
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, half2(input.uv.x + 0.17h, 0.44h)).rgb,
                    luminanceWeights);
                half upperGrain = dot(
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, half2(input.uv.x + 0.53h, 0.56h)).rgb,
                    luminanceWeights);
                half lowerCut = lerp(0.18h, 0.025h, smoothstep(0.03h, 0.68h, lowerGrain));
                half upperCut = lerp(0.18h, 0.025h, smoothstep(0.03h, 0.68h, upperGrain));
                half lowerEdge = smoothstep(lowerCut - 0.025h, lowerCut + 0.025h, input.uv.y);
                half upperEdge = smoothstep(upperCut - 0.025h, upperCut + 0.025h, 1.0h - input.uv.y);
                half edge = lowerEdge * upperEdge;
                half alpha = edge * lerp(_MinOpacity, 1.0h, textureOpacity) * _BaseColor.a * input.color.a;
                return half4(_BaseColor.rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
