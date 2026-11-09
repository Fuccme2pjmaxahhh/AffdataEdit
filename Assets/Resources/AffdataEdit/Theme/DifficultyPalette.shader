// Normalize difficulty artwork brightness while retaining its color direction and alpha.
Shader "AffdataEdit/UI/Difficulty Palette"
{
    Properties
    {
        [PerRendererData] _MainTex ("HUD Artwork", 2D) = "white" {}
        _Brightness ("Brightness", Range(0,1)) = 0.29
        _SurfaceColor ("Surface", Color) = (0.13,0.13,0.13,1)
        _RaisedColor ("Raised Surface", Color) = (0.19,0.19,0.19,1)
        _BorderColor ("Border", Color) = (0.31,0.31,0.31,1)
        _TextColor ("Text", Color) = (0.93,0.93,0.93,1)
        _ClipRect ("Clip Rect", Vector) = (-32767,-32767,32767,32767)
        _MaskSoftnessX ("Mask SoftnessX", Float) = 0
        _MaskSoftnessY ("Mask SoftnessY", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _CullMode ("Cull Mode", Float) = 0
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull [_CullMode]
        ZWrite Off
        Lighting Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct VertexInput
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct FragmentInput
            {
                UNITY_VERTEX_OUTPUT_STEREO
                float4 vertex : SV_POSITION;
                fixed4 face : COLOR;
                
                float2 uv : TEXCOORD0;
                float4 mask : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _SurfaceColor, _RaisedColor, _BorderColor, _TextColor;
            float _Brightness;
            float4 _ClipRect;
            float _MaskSoftnessX, _MaskSoftnessY;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            FragmentInput Vert(VertexInput input)
            {
                FragmentInput output;
                UNITY_INITIALIZE_OUTPUT(FragmentInput, output);
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                // Keep the original geometry and alpha mask.
                output.vertex = UnityObjectToClipPos(input.vertex);
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    input.color.rgb = UIGammaToLinear(input.color.rgb);
                output.face = input.color;
                output.uv = input.uv;

                float2 pixelSize = output.vertex.w
                    / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                float2 softness = float2(max(_UIMaskSoftnessX, _MaskSoftnessX),
                    max(_UIMaskSoftnessY, _MaskSoftnessY));
                output.mask = float4(input.vertex.xy * 2 - rect.xy - rect.zw,
                    0.25 / (0.25 * softness + pixelSize));
                return output;
            }

            fixed4 Frag(FragmentInput input) : SV_Target
            {
                fixed4 coverage = tex2D(_MainTex, input.uv);
                half3 rgb = IsGammaSpace() ? coverage.rgb : LinearToGammaSpace(coverage.rgb);
                half brightness = dot(rgb, half3(0.2126, 0.7152, 0.0722));
                // Scale the original RGB together. Never blend white into the difficulty color.
                half peak = max(rgb.r, max(rgb.g, rgb.b));
                half gain = min(_Brightness / max(0.0001, brightness), 0.95 / max(0.0001, peak));
                rgb *= gain;
                if (!IsGammaSpace()) rgb = GammaToLinearSpace(rgb);
                fixed4 color = fixed4(rgb, 1);
                color *= input.face;
                color.a *= coverage.a;
                color.rgb *= color.a;
                #if UNITY_UI_CLIP_RECT
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                color *= mask.x * mask.y;
                #endif
                #if UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
