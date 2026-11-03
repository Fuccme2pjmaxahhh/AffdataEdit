Shader "Gameplay/JudgeParticle"
{
    Properties
    {
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
            Blend [_SrcFactor] [_DstFactor]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ USE_GRID_SPLIT
            #pragma multi_compile _ USE_VERT_COLOR
            #pragma multi_compile _ USE_ASPECT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
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
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = (v.uv * _MainTex_ST.xy) + _MainTex_ST.zw;
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half2 uv = i.uv;

            #if USE_GRID_SPLIT
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, CalculateUV(uv));
            #else
            #if USE_ASPECT
                uv.y = ScaleCenter(uv.y, _Aspect);
            #endif
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
            #endif

            #if USE_VERT_COLOR
                col *= i.color;
            #endif

                // Zero-alpha output is a blend identity in both Light and Conflict modes.
                // Reject it before the dense hit quads reach the framebuffer blending stage.
                clip(col.a == 0 ? -1 : 1);
                col.rgb *= col.a;
                return col;
            }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        ZWrite Off
        ZTest Always
        Cull Off
        Lighting Off

        Pass
        {
            Blend [_SrcFactor] [_DstFactor]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ USE_GRID_SPLIT
            #pragma multi_compile _ USE_VERT_COLOR
            #pragma multi_compile _ USE_ASPECT

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            uint _XSplit;
            uint _YSplit;
            uint _Index;
            float _Aspect;

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
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
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
                return col;
            }
            ENDCG
        }
    }
}
