// Мультяшное освещение для Maoshi (URP): ступенчатый свет от солнца, цветная тень, мягкий ободок.
// Имена свойств как у URP/Lit (_BaseMap, _BaseColor, _EmissionColor) — материалы переводятся на этот шейдер
// простой заменой шейдера, цвета и текстуры сохраняются. Контуры рисует отдельный экранный проход (Maoshi/Outline).
Shader "Maoshi/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap ("Текстура", 2D) = "white" {}
        [MainColor] _BaseColor ("Цвет", Color) = (1,1,1,1)
        _ShadeColor ("Цвет тени", Color) = (0.62, 0.62, 0.80, 1)
        _RampThreshold ("Граница света", Range(0,1)) = 0.5
        _RampSmooth ("Мягкость границы", Range(0.001,0.5)) = 0.05
        _ShadowStrength ("Сила падающих теней", Range(0,1)) = 0.85
        _AmbientStrength ("Окружающий свет", Range(0,1)) = 0.35
        _RimColor ("Ободок", Color) = (1, 0.96, 0.88, 0.35)
        _RimPower ("Ширина ободка", Range(0.5, 8)) = 3
        _Posterize ("Постеризация текстуры (0 — выкл)", Range(0, 16)) = 0
        _Saturation ("Насыщенность", Range(0, 2)) = 1.15
        [HDR] _EmissionColor ("Свечение", Color) = (0,0,0,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _Smoothness ("Smoothness (не используется)", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadeColor;
            half _RampThreshold;
            half _RampSmooth;
            half _ShadowStrength;
            half _AmbientStrength;
            half4 _RimColor;
            half _RimPower;
            half _Posterize;
            half _Saturation;
            half4 _EmissionColor;
            half _Cutoff;
            half _Smoothness;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            // Доп. источники (прожектор «солнца» в окне и т.п.) включает сцена: глобальное _MaoshiExtraLights = 1.
            // В остальных сценах (люстры, фонари) они по-прежнему не влияют на мультяшный свет.
            float _MaoshiExtraLights;

            half3 Saturate(half3 c, half amount)
            {
                half grey = dot(c, half3(0.299, 0.587, 0.114));
                return lerp(grey.xxx, c, amount);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                if (_Posterize > 0.5) albedo.rgb = floor(albedo.rgb * _Posterize + 0.5) / _Posterize;
                albedo.rgb = Saturate(albedo.rgb, _Saturation);

                float3 normalWS = normalize(input.normalWS);
                float3 viewWS = normalize(GetWorldSpaceViewDir(input.positionWS));

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half shadow = lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength) * mainLight.distanceAttenuation;

                // Полуламберт → ступенька света, тень от предметов тоже ложится ступенькой.
                half halfLambert = dot(normalWS, mainLight.direction) * 0.5h + 0.5h;
                half lit = smoothstep(_RampThreshold - _RampSmooth, _RampThreshold + _RampSmooth, halfLambert * shadow);

                half3 lightColor = mainLight.color;
                half3 shade = _ShadeColor.rgb * (SampleSH(normalWS) * 0.6h + 0.4h);
                half3 color = albedo.rgb * lerp(shade, lightColor, lit);
                color += albedo.rgb * SampleSH(normalWS) * _AmbientStrength;

                // Светлый ободок по краю силуэта (сильнее на освещённой стороне).
                half rim = pow(1.0h - saturate(dot(normalWS, viewWS)), _RimPower);
                color += _RimColor.rgb * _RimColor.a * rim * (0.35h + 0.65h * lit);

                #if defined(_ADDITIONAL_LIGHTS)
                if (_MaoshiExtraLights > 0.5)
                {
                    // Ступенчатый свет от доп. источников: яркое пятно с чёткой (чуть мягкой) границей и тенями.
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    half4 shadowMask = half4(1, 1, 1, 1);
                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light extra = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                        // Без «обёртки»: грани, отвёрнутые от солнца (потолок, стена над окном), им не светятся.
                        half facing = saturate(dot(normalWS, extra.direction) * 1.4h);
                        half3 radiance = extra.color * extra.distanceAttenuation; // яркость в точке (конус и расстояние)
                        half strength = max(radiance.r, max(radiance.g, radiance.b));
                        half band = smoothstep(0.08h, 0.25h, strength * extra.shadowAttenuation * facing);
                        half3 tint = radiance / max(strength, 0.0001h);
                        color += albedo.rgb * tint * band * min(strength, 1.6h) * (0.7h + 0.3h * facing);
                        color += _RimColor.rgb * _RimColor.a * rim * band * tint * 0.6h;
                    LIGHT_LOOP_END
                }
                #endif

                color += _EmissionColor.rgb;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                output.positionCS = positionCS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half frag(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
