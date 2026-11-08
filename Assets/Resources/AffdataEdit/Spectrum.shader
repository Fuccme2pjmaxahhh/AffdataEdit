Shader "Arcade/Spectrum"
{
    Properties
    {
        _MainTex ("Spectrum", 2D) = "black" {}
        _MinColor ("Min Color", Color) = (1, 0.423529, 0.423529, 1)
        _MiddleColor ("Middle Color", Color) = (1, 0, 0.2, 1)
        _MaxColor ("Max Color", Color) = (1, 0.419608, 0, 1)
        _Opacity ("Opacity", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstFactor ("Destination Blend", Int) = 10
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off
        Blend One [_DstFactor]
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MinColor;
                float4 _MiddleColor;
                float4 _MaxColor;
                float _Opacity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.position = TransformObjectToHClip(input.position.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float magnitude = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).r;
                float value = magnitude * 1.5;
                float3 color = value < 0.5 ? lerp(_MinColor.rgb, _MiddleColor.rgb, value * 2)
                    : lerp(_MiddleColor.rgb, _MaxColor.rgb, (value - 0.5) * 2);
                float opacity = magnitude * _Opacity;
                return half4(color * opacity, opacity);
            }
            ENDHLSL
        }
    }
}
