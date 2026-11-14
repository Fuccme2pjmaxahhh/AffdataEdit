Shader "AffdataEdit/AppearanceDiamond"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Alpha ("Opacity", Float) = 1
        _SrcFactor ("Source", Float) = 1
        _DstFactor ("Destination", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="true" }
        Blend [_SrcFactor] [_DstFactor]
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            sampler2D _MainTex; float _Alpha;
            Varyings vert(Attributes v) { Varyings o; o.vertex=TransformObjectToHClip(v.vertex.xyz); o.uv=v.uv; o.color=v.color; return o; }
            half4 frag(Varyings i):SV_Target { half4 c=tex2D(_MainTex,i.uv)*i.color; c.a*=_Alpha; c.rgb*=c.a; return c; }
            ENDHLSL
        }
    }
}
