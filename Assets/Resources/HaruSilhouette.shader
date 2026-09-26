Shader "DrawAndGo/HaruSilhouette"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.05,0.06,0.08,0.35)
        [HideInInspector] _ClipMinX ("Clip Minimum X", Float) = -10000
        [HideInInspector] _ClipMaxX ("Clip Maximum X", Float) = 10000
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "HaruSilhouette"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _ClipMinX;
                float _ClipMaxX;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float positionWSX : TEXCOORD0;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.positionWSX = positionWS.x;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                clip(input.positionWSX - _ClipMinX);
                clip(_ClipMaxX - input.positionWSX);
                return _BaseColor;
            }
            ENDHLSL
        }
    }
}
