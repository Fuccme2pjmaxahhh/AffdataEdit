Shader "Arcade/Spectrum"
{
    Properties
    {
        _MainTex ("Spectrum", 2D) = "black" {}
        _Dark ("Conflict Side", Float) = 1
        _Opacity ("Opacity", Range(0,1)) = 0.5
        _LinearAmplitude ("Linear Amplitude", Float) = 0
        // User tuning (Settings > 频谱); defaults match the built-in look. Set every frame by AdeAudioSpectrum.
        _DbFloor ("dB Floor (0..1 of -80..0 dB)", Float) = 0.25
        _DbCeiling ("dB Ceiling (0..1 of -80..0 dB)", Float) = 0.75
        _BandMix ("Band-relative Share", Float) = 0.5
        _Contrast ("Contrast Exponent", Float) = 1.5
        _Gate ("Noise Gate (0..1 of -80..0 dB)", Float) = 0.1
        _ConflictBoost ("Conflict Brightness Boost", Float) = 1.6
        _ConflictStart ("Conflict Opacity Start", Float) = 0.1
        _ConflictFull ("Conflict Opacity Full", Float) = 0.8
        _LightFull ("Light Opacity Full", Float) = 0.6
        _Heat ("Conflict Color Heat", Float) = 1.15
        _LinearGain ("Linear Gain (x stored)", Float) = 3.2
        _LinearContrast ("Linear Contrast Exponent", Float) = 1
        _LinearBoost ("Linear Brightness Boost", Float) = 1.6
        // Conflict: additive (One, One) like Arcade Alpha, light adds onto the dark track; Light: premultiplied
        // alpha (One, OneMinusSrcAlpha). Set per side by AdeAudioSpectrum.
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off
        Blend One [_DstBlend]
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
                float _Dark;
                float _Opacity;
                float _LinearAmplitude;
                float _DbFloor, _DbCeiling, _BandMix, _Contrast, _Gate;
                float _ConflictBoost, _ConflictStart, _ConflictFull, _LightFull, _Heat;
                float _LinearGain, _LinearContrast, _LinearBoost;
            CBUFFER_END

            float ContrastCurve(float value)
            {
                // F(x) = x^p / (x^p + (1-x)^p), p = _Contrast (default 1.5); p = 1 leaves values unchanged.
                float2 weights = pow(abs(float2(value, 1.0 - value)), max(_Contrast, 0.1));
                return weights.x / max(weights.x + weights.y, 1e-6);
            }

            // Light (white track), matched to how Arcade Alpha actually looks: its crimson sits where AA's alpha is
            // still low, so on white it only ever reads as pink, and every opaque cell is orange or yellow. Our
            // opacity is full from about 0.6, so a crimson stop there turned into solid red. Pink now covers the
            // quiet (translucent) end and the colors are orange/gold by the time cells turn opaque:
            // pale pink, pink, coral (FF6C6C-ish), salmon, orange, then the AA-style ending to golden yellow.
            static const float RampStops[7] = { 0.0, 0.12, 0.25, 0.38, 0.5, 0.75, 1.0 };
            static const float3 LightRamp[7] = {
                float3(1.00, 0.86, 0.86), float3(1.00, 0.70, 0.72), float3(1.00, 0.47, 0.47), float3(1.00, 0.42, 0.25),
                float3(1.00, 0.58, 0.00), float3(1.00, 0.75, 0.00), float3(1.00, 0.84, 0.00) };

            float3 LightColor(float value)
            {
                float3 color = LightRamp[0];
                [unroll] for (int i = 1; i < 7; i++)
                {
                    float t = saturate((value - RampStops[i - 1]) / (RampStops[i] - RampStops[i - 1]));
                    color = lerp(color, LightRamp[i], t);
                }
                // Arcade Alpha style ending: above orange (stop 4) the salmon->orange direction keeps going,
                // with green capped so peaks stay golden rather than lemon on the white track.
                if (value > RampStops[4])
                {
                    color = saturate(lerp(LightRamp[3], LightRamp[4], (value - RampStops[3]) / (RampStops[4] - RampStops[3])));
                    color.g = min(color.g, 0.86);
                }
                return color;
            }

            // Light in linear amplitude mode: Arcade Alpha's own Light ramp (FF6C6C -> FF0033 over the lower third,
            // then FF0033 -> FF6B00 extrapolated, clipping to golden yellow). Here alpha already follows the level,
            // so the saturated lower colors read as soft coral/pink when quiet; the dB-mode ramp (pale pinks for
            // the quiet end) was invisible at low alpha, leaving only flat yellow with no gradient in between.
            float3 LightLinearColor(float value)
            {
                const float3 low = float3(1.0, 0.4235, 0.4235), middle = float3(1.0, 0.0, 0.2), high = float3(1.0, 0.4196, 0.0);
                return value < 1.0 / 3.0 ? lerp(low, middle, value * 3.0) : saturate(lerp(middle, high, (value - 1.0 / 3.0) * 3.0));
            }

            // Conflict (dark track, additive), shaped like Arcade Alpha's ramp with our own colors: deep violet ->
            // vivid violet over the lower third, then violet -> orange extrapolated, passing through magenta/red
            // and clipping to gold at the top. No green in the lower colors, so adding them onto the purple track
            // stays saturated instead of turning it pale; levels are pushed _Heat hotter (default 15%) so more of it reaches gold.
            float3 DarkColor(float value)
            {
                const float3 low = float3(0.35, 0.0, 0.55), middle = float3(0.55, 0.0, 1.0), high = float3(1.0, 0.36, 0.0);
                value = saturate(value * _Heat);
                return value < 1.0 / 3.0 ? lerp(low, middle, value * 3.0) : saturate(lerp(middle, high, (value - 1.0 / 3.0) * 3.0));
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.position = TransformObjectToHClip(input.position.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                // Single sample, no blur: time columns stay sharp for chart-offset alignment.
                float3 spectrum = saturate(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb);
                bool linearMode = _LinearAmplitude > 0.5;
                // Half band-relative (each band's own range, keeps high-frequency detail), half absolute level
                // (loud lows stand out from quiet highs), for contrast across frequencies.
                // The absolute part maps _DbFloor.._DbCeiling (default -60..-20 dB) to 0..1 (Arcade Alpha saturates
                // even earlier), so quieter passages such as a lone sub-bass still show under the band ranges.
                // Linear mode: B is the raw FFT magnitude x0.25 (Hann window); _LinearGain scales it (default
                // 3.2 = magnitude x0.8, matching Arcade Alpha's unwindowed x0.5 level), then an optional power curve.
                float absolute = saturate((spectrum.r - _DbFloor) / max(_DbCeiling - _DbFloor, 0.01));
                float value = linearMode ? pow(saturate(spectrum.b * _LinearGain), max(_LinearContrast, 0.1))
                    : ContrastCurve(lerp(absolute, spectrum.g, saturate(_BandMix)));
                bool dark = _Dark > 0.5;
                float3 color = dark ? DarkColor(value) : (linearMode ? LightLinearColor(value) : LightColor(value));
                float gate = smoothstep(0.0, max(_Gate, 0.001), spectrum.r);
                float opacity;
                if (linearMode)
                {
                    // Linear magnitude controls both color and premultiplied alpha directly (no dB gate or
                    // per-band normalization). Brightness is boosted like the dB mode (x1.6, capped), otherwise
                    // 50% brightness halved even saturated cells and the gold never reached full strength.
                    opacity = value * min(_Opacity * _LinearBoost, 1.0);
                }
                else if (dark)
                {
                    // Additive: strength sets how much light is added (like Arcade Alpha); x1.6 (capped) so
                    // 100% brightness is reached well before the slider's end.
                    opacity = smoothstep(_ConflictStart, max(_ConflictFull, _ConflictStart + 0.01), value) * gate
                        * min(_Opacity * _ConflictBoost, 1.0);
                }
                else
                {
                    // Light: opacity reaches full by the middle of the ramp so the colors show on white;
                    // boosted so partial transparency doesn't bleach the colors (50% brightness ~ 75%).
                    opacity = smoothstep(0.0, max(_LightFull, 0.01), value) * gate * _Opacity;
                    opacity = 1.0 - (1.0 - opacity) * (1.0 - opacity);
                }
                return half4(color * opacity, opacity);
            }
            ENDHLSL
        }
    }
}
