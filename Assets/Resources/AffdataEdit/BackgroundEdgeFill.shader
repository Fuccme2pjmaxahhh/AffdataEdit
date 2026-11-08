Shader "AffdataEdit/BackgroundEdgeFill"
{
    Properties { [PerRendererData] _MainTex ("Background", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Attributes { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _ImageRect, _ImageUV;
            float4x4 _FillToImage;
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = (mul(_FillToImage, v.vertex).xy - _ImageRect.xy) / _ImageRect.zw;
                o.color = v.color;
                return o;
            }
            half4 sampleEdge(float2 uv)
            {
                float2 inset = abs(_MainTex_TexelSize.xy) * .5;
                float2 sampleUV = clamp(lerp(_ImageUV.xy, _ImageUV.zw, uv), _ImageUV.xy + inset, _ImageUV.zw - inset);
                return tex2D(_MainTex, sampleUV);
            }
            half4 frag(Varyings i):SV_Target
            {
                if (all(i.uv >= 0) && all(i.uv <= 1)) return 0;
                float distanceOutside = length(i.uv - saturate(i.uv));
                // Blur grows away from the image so the border joins continuously.
                float radius = .018 * smoothstep(0, .04, distanceOutside);
                half4 c = sampleEdge(i.uv) * .25;
                c += (sampleEdge(i.uv + float2(radius,0)) + sampleEdge(i.uv - float2(radius,0))
                    + sampleEdge(i.uv + float2(0,radius)) + sampleEdge(i.uv - float2(0,radius))) * .125;
                c += (sampleEdge(i.uv + radius) + sampleEdge(i.uv - radius)
                    + sampleEdge(i.uv + float2(radius,-radius)) + sampleEdge(i.uv + float2(-radius,radius))) * .0625;
                c *= i.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}

