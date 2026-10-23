Shader "DrawAndGo/FirstPersonOverlay"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Overlay+1000"
        }
        Pass
        {
            Name "FirstPersonOverlay"
            Tags { "LightMode"="UniversalForward" }
            // The view model is squeezed into the nearest sliver of the depth range (see Vert),
            // so it still draws over the world, but its own faces now hide each other properly.
            ZTest LEqual
            ZWrite On
            // The arm is cut out of the full body mesh, so some faces are only seen from inside.
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END

            // Fraction of the depth range, next to the near plane, reserved for the view model.
            #define VIEW_MODEL_DEPTH_RANGE 0.01

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

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Keep the depth order of the model's own surfaces but remap it into the
                // near sliver, in front of anything else the camera sees.
                float depth = positionCS.z / positionCS.w;
                #if UNITY_REVERSED_Z
                    depth = 1.0 - (1.0 - depth) * VIEW_MODEL_DEPTH_RANGE;
                #else
                    // Near plane is -1 (OpenGL) or 0 (others).
                    float nearClip = UNITY_NEAR_CLIP_VALUE;
                    depth = nearClip + (depth - nearClip) * VIEW_MODEL_DEPTH_RANGE;
                #endif
                positionCS.z = depth * positionCS.w;
                output.positionHCS = positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
            }
            ENDHLSL
        }
    }
}
