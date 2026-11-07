// Spc/SkyLineGlow
// Re-authoring of "Shader Graphs/SkyJudgementLineGlow" (Variant, queue 3011), the material on the
// JUDGE_SKY/SkyJudgementLineGlow quad - scaled (2, 0.144, 1) and rotated upright so it faces the
// camera. Track sets _CenterX = (l + r) / 2 and _Width = r - l of the active sky area in normalised
// sky-track units, and _Opacity = 1 while safe.
//
// Transcribed instruction by instruction from the shipped DXBC (the _SURFACE_TYPE_TRANSPARENT
// variant), not reconstructed by eye. The earlier hand-written version of this file was a gaussian
// falloff times a dot tile, which is not what the original does at all:
//
//   p     = float2((uv.x - _CenterX*0.8 - 0.1) * 40, uv.y*2 - 1)
//   d     = abs(p) - float2(_Width*16.8, _Opacity*0.1) + 0.5
//   box   = length(max(d, 0)) + min(max(d.y, d.x), 0)          // rounded-box SDF
//   band  = pow(saturate((1 - (box - 0.5)) * (1 - 2*abs(0.5 - uv.y))), 0.74) * 1.05
//   g     = max(_DownscaledGrid.r, _DownscaledBlurredGrid.r)   // the triangle grid, red only
//   s     = smoothstep-curve of min(pow(g, 0.3), 1)
//   m     = min(max(s, band), 1)
//   alpha = saturate(m * _Opacity)
//   rgb   = a two-stage lerp between three constants driven by band*m, encoded to sRGB, then
//           lerped to white wherever s crosses _TriangleBoundary (that is the bright rim on the
//           lit triangles)
//
// The two grid textures come from SpcJudgementGrid: the triangle mesh drawn offscreen, blurred
// once for _DownscaledGrid and through the full chain for _DownscaledBlurredGrid. Without them the
// band still renders, just with no triangle pattern in it.
//
// Blend is SrcAlpha/OneMinusSrcAlpha (material _SrcBlend 5 / _DstBlend 10), not additive.
// AFF fits the quad and band to the active Slide's exact width and samples the grid in world space.
Shader "Spc/SkyLineGlow"
{
    Properties
    {
        [NoScaleOffset] _DownscaledGrid ("Downscaled Grid", 2D) = "black" {}
        [NoScaleOffset] _DownscaledBlurredGrid ("Downscaled Blurred Grid", 2D) = "black" {}
        _CenterX ("Center X (normalised sky x)", Float) = 0.5
        _Width ("Width (normalised sky units)", Float) = 0.33
        _Opacity ("Opacity", Range(0, 1)) = 0
        _TriangleBoundary ("Triangle Boundary", Float) = 0.95
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+11"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "Universal Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float skyX        : TEXCOORD1;
            };

            TEXTURE2D(_DownscaledGrid);
            SAMPLER(sampler_DownscaledGrid);
            TEXTURE2D(_DownscaledBlurredGrid);
            SAMPLER(sampler_DownscaledBlurredGrid);

            CBUFFER_START(UnityPerMaterial)
                float _CenterX;
                float _Width;
                float _Opacity;
                float _TriangleBoundary;
            CBUFFER_END

            // The three colour stops the original interpolates between, in linear space.
            static const float3 kStopA = float3(0.211688, 0.170657, 0.613208);
            static const float3 kStopADelta = float3(0.173436, 0.064836, 0.179245);
            static const float3 kStopB = float3(0.507770, 0.247196, 0.952830);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = v.uv;
                o.skyX = 0.5 - positionWS.x / 8.5;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                // The quad follows the active Slide's world-space endpoints every frame.
                float originalU = i.skyX * 0.8 + 0.1;

                // Rounded-box band around the active sky interval.
                float2 p = float2((originalU - _CenterX * 0.8 - 0.1) * 40.0, uv.y * 2.0 - 1.0);
                float2 d = abs(p) - float2(_Width * 16.0, _Opacity * 0.1) + 0.5;
                float box = length(max(d, 0.0)) + min(max(d.y, d.x), 0.0);
                float vfade = 1.0 - 2.0 * abs(0.5 - uv.y);
                float band = pow(saturate((1.0 - (box - 0.5)) * vfade), 0.74) * 1.05;

                // The triangle grid, sharp and blurred, red channel only.
                float2 gridUV = float2((originalU + 0.5) * 0.5, uv.y);
                float g = max(SAMPLE_TEXTURE2D(_DownscaledGrid, sampler_DownscaledGrid, gridUV).r,
                              SAMPLE_TEXTURE2D(_DownscaledBlurredGrid, sampler_DownscaledBlurredGrid, gridUV).r);
                // AFF can contain disjoint simultaneous Slides. Each glow samples only its own range.
                g *= step(0.0, gridUV.x) * step(gridUV.x, 1.0);
                // Source DXBC uses log/exp of the sampled grid directly: a cleared
                // grid stays exactly zero, with no artificial epsilon haze.
                float t = min(pow(max(g, 0.0), 0.3), 1.0);
                float s = t * t * (3.0 - 2.0 * t);

                float m = min(max(s, band), 1.0);
                float k = band * m;

                float3 c = kStopA + min(k * 2.222203, 1.0) * kStopADelta;
                c = lerp(c, kStopB, saturate((k - 0.450004) * 5.3125));
                c = lerp(c, float3(1.0, 1.0, 1.0), saturate((k - 0.638239) * 3.366293));
                c = 1.055 * pow(max(c, 1e-6), 1.0 / 2.4) - 0.055;   // linear -> sRGB, as shipped

                // Bright white rim on triangles that have crossed the boundary.
                float ramp = (s >= _TriangleBoundary)
                           ? (s - _TriangleBoundary) / max(1.0 - _TriangleBoundary, 1e-4)
                           : 0.0;
                float3 rgb = lerp(c, float3(1.0, 1.0, 1.0), saturate(ramp));

                return float4(rgb, saturate(m * _Opacity));
            }
            ENDHLSL
        }
    }
}
