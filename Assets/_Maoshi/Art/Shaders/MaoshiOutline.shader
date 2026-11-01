// Экранные контуры для мультяшного вида: тёмная линия по силуэтам и резким изломам формы.
// Рисуется на весь экран через Full Screen Pass Renderer Feature (подключает Stage 18).
//
// Почему не рябит:
// - Силуэт ищем по второй производной обратной глубины. Для любой плоскости 1/z меняется по экрану линейно,
//   поэтому пол, стены и дорога под скользящим углом дают ноль — линия появляется только на настоящих перепадах.
// - Линия только с ближней стороны перепада (не двоится).
// - Изломы по нормалям учитываются только вблизи (дальние фаски и мелкая геометрия мерцают при движении)
//   и только если излом сильный.
// - Толщина и включение — из настроек графики (глобальная _MaoshiOutlineScale: 0 — по умолчанию, <0 — выключено).
Shader "Maoshi/Outline"
{
    Properties
    {
        _OutlineColor ("Цвет контура", Color) = (0.13, 0.09, 0.14, 1)
        _Thickness ("Толщина, пиксели", Range(0.5, 4)) = 1.2
        _DepthThreshold ("Порог силуэта", Range(0.01, 1)) = 0.12
        _NormalThreshold ("Порог излома", Range(0.05, 2)) = 0.9
        _NormalFadeStart ("Изломы: начало затухания, м", Float) = 10
        _NormalFadeEnd ("Изломы: конец затухания, м", Float) = 28
        _FadeStart ("Начало затухания, м", Float) = 40
        _FadeEnd ("Конец затухания, м", Float) = 120
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Outline"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float4 _OutlineColor;
            float _Thickness;
            float _DepthThreshold;
            float _NormalThreshold;
            float _NormalFadeStart;
            float _NormalFadeEnd;
            float _FadeStart;
            float _FadeEnd;
            float _MaoshiOutlineScale; // глобальная, из настроек графики

            bool IsSky(float raw)
            {
            #if UNITY_REVERSED_Z
                return raw <= 0.0000001;
            #else
                return raw >= 0.9999999;
            #endif
            }

            float InvDepth(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                return IsSky(raw) ? 0.0 : 1.0 / LinearEyeDepth(raw, _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float scale = _MaoshiOutlineScale == 0 ? 1.0 : _MaoshiOutlineScale;
                if (scale < 0) return color;

                float raw = SampleSceneDepth(uv);
                if (IsSky(raw)) return color;

                float d0 = LinearEyeDepth(raw, _ZBufferParams);
                float i0 = 1.0 / d0;
                float2 px = max(_Thickness * scale, 0.5) / _ScreenParams.xy;

                // Силуэт: насколько «проваливаются» соседи относительно плоскости через центр.
                float iL = InvDepth(uv - float2(px.x, 0)), iR = InvDepth(uv + float2(px.x, 0));
                float iD = InvDepth(uv - float2(0, px.y)), iU = InvDepth(uv + float2(0, px.y));
                float silhouette = (max(0, 2 * i0 - iL - iR) + max(0, 2 * i0 - iD - iU)) / i0;
                float depthEdge = smoothstep(_DepthThreshold, _DepthThreshold * 2.0, silhouette);

                // Изломы формы: только вблизи и только сильные.
                float normalEdge = 0;
                float normalFade = 1.0 - smoothstep(_NormalFadeStart, _NormalFadeEnd, d0);
                if (normalFade > 0.001)
                {
                    float3 n0 = SampleSceneNormals(uv);
                    float nd = 0;
                    nd += 1.0 - saturate(dot(n0, SampleSceneNormals(uv + float2(px.x, 0))));
                    nd += 1.0 - saturate(dot(n0, SampleSceneNormals(uv - float2(px.x, 0))));
                    nd += 1.0 - saturate(dot(n0, SampleSceneNormals(uv + float2(0, px.y))));
                    nd += 1.0 - saturate(dot(n0, SampleSceneNormals(uv - float2(0, px.y))));
                    normalEdge = smoothstep(_NormalThreshold, _NormalThreshold * 1.5, nd) * normalFade;
                }

                float edge = max(depthEdge, normalEdge);
                edge *= 1.0 - smoothstep(_FadeStart, _FadeEnd, d0);

                color.rgb = lerp(color.rgb, _OutlineColor.rgb, edge * _OutlineColor.a);
                return color;
            }
            ENDHLSL
        }
    }
}
