Shader "AffdataEdit/AppearanceWall"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Phase ("Phase", Float) = 0
        _Alpha ("Alpha", Float) = 1
        _SrcFactor ("Source", Float) = 1
        _DstFactor ("Destination", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="true" }
        Blend [_SrcFactor] [_DstFactor]
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; float z:TEXCOORD1; };
            sampler2D _MainTex; float _Phase, _Alpha;
            Varyings vert(Attributes v)
            {
                Varyings o; float3 world = TransformObjectToWorld(v.vertex.xyz);
                o.vertex = TransformWorldToHClip(world); o.z = world.z;
                o.uv = v.uv; o.uv.y += _Phase; o.color = v.color; return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                half4 c = tex2D(_MainTex,i.uv) * i.color;
                c.a *= _Alpha; c.rgb *= c.a; return c;
            }
            ENDHLSL
        }
    }
}
