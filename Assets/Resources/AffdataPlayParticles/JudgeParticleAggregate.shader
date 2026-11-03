Shader "Gameplay/JudgeParticleAggregate"
{
    Properties
    {
        _HitRepeat ("Coincident hits override", Float) = 0
        _HitQuantize ("UNorm8 framebuffer", Float) = 0
        _HitConflict ("Conflict blend", Float) = 0
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcFactor ("Src Factor", Int) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstFactor ("Dst Factor", Int) = 10
        _XSplit ("X Split", Int) = 4
        _YSplit ("Y Split", Int) = 4
        _Index ("Index", Int) = 0
        _Aspect ("Aspect", Float) = 1
        [Toggle(USE_GRID_SPLIT)] _USE_GRID_SPLIT ("Use Grid Split", Int) = 0
        [Toggle(USE_VERT_COLOR)] _USE_VERT_COLOR ("Use Vertex Color", Int) = 0
        [Toggle(USE_ASPECT)] _USE_ASPECT ("Use Aspect", Int) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        ZWrite Off
        ZTest Always
        Cull Off
        Lighting Off

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend Off

            CGPROGRAM
            #pragma target 3.0
            #pragma only_renderers framebufferfetch
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ USE_GRID_SPLIT
            #pragma multi_compile _ USE_VERT_COLOR
            #pragma multi_compile _ USE_ASPECT

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                nointerpolation float repeat : TEXCOORD1;
            };

            sampler2D _MainTex;

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _HitRepeat, _HitConflict, _HitQuantize;
                uint _XSplit;
                uint _YSplit;
                uint _Index;
                float _Aspect;
                float4 _MainTex_ST;
            CBUFFER_END

            float2 CalculateUV(float2 uv)
            {
                uint xTile = _Index % _XSplit;
                uint yTile = _Index / _XSplit;
                yTile = (_YSplit - 1) - yTile;

                float2 tileSize = float2(1.0 / _XSplit, 1.0 / _YSplit);
                float2 offset = float2(xTile, yTile) * tileSize;
                return uv * tileSize + offset;
            }

            float ScaleCenter(float x, float scale)
            {
                return (x - 0.5f) * scale + 0.5f;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = (v.uv.xy * _MainTex_ST.xy) + _MainTex_ST.zw;
                o.color = v.color * _Color;
                o.repeat = max(1, v.uv.z);
                return o;
            }

            void frag(v2f i, inout float4 destination : SV_Target)
            {
                half2 uv = i.uv;

            #if USE_GRID_SPLIT
                half4 col = tex2D(_MainTex, CalculateUV(uv));
            #else
            #if USE_ASPECT
                uv.y = ScaleCenter(uv.y, _Aspect);
            #endif
                half4 col = tex2D(_MainTex, uv);
            #endif

            #if USE_VERT_COLOR
                col *= i.color;
            #endif

                // Zero-alpha output is a blend identity in both Light and Conflict modes.
                // Reject it before the dense hit quads reach the framebuffer blending stage.
                clip(col.a == 0 ? -1 : 1);
                col.rgb *= col.a;
                float a = col.a;
                float n = _HitRepeat > 0 ? _HitRepeat : i.repeat;
                // Fixed point targets round after each original blend. Preserve that recurrence
                // inside one fragment rather than brightening the faint edge with a single round.
                if (_HitQuantize > .5)
                {
                    for (uint hit = 0; hit < (uint)n; hit++)
                    {
                        float4 next = _HitConflict > .5
                            ? float4(col.rgb * destination.a + destination.rgb * (1-a), destination.a)
                            : col * a + destination * (1-a);
                        next = round(saturate(next) * 255) / 255;
                        if (all(next == destination)) break;
                        destination = next;
                    }
                    return;
                }
                float coverage;
                if (n * a < .01)
                    coverage = n*a*(1-(n-1)*a*.5+(n-1)*(n-2)*a*a/6);
                else
                    coverage = 1-pow(max(0,1-a),n);
                if (_HitConflict > .5)
                {
                    destination.rgb = col.rgb * destination.a * (coverage/a) + destination.rgb*(1-coverage);
                }
                else
                {
                    destination.rgb = col.rgb * coverage + destination.rgb*(1-coverage);
                    destination.a = a*coverage + destination.a*(1-coverage);
                }
            }
            ENDCG
        }
    }

}
