Shader "Custom/ToonSky"
{
    // Ciel compatible avec le jour/nuit de ToonAdvanced :
    //  - de jour, rendu identique au Skybox/Cubemap d'Unity (cubemap, teinte, exposition, rotation)
    //  - de nuit, cubemap assombrie + degrade de nuit + etoiles
    // Le melange suit la variable globale _ToonNightBlend (composant ToonDayNight) : aucun script
    // n'a besoin de modifier ce material, il peut donc etre partage sans risque.
    Properties
    {
        [NoScaleOffset] _Tex ("Ciel (cubemap)", Cube) = "grey" {}
        _Rotation ("Rotation", Range(0, 360)) = 0

        [Header(Jour)]
        _DayTint ("Teinte", Color) = (0.5, 0.5, 0.5, 0.5)
        _DayExposure ("Exposition", Range(0, 8)) = 1

        [Header(Nuit)]
        _NightTint ("Teinte de la cubemap", Color) = (0.3, 0.36, 0.6, 0.5)
        _NightExposure ("Exposition de la cubemap", Range(0, 8)) = 0.4
        _NightZenith ("Couleur du zenith", Color) = (0.02, 0.03, 0.1, 1)
        _NightHorizon ("Couleur de l'horizon", Color) = (0.1, 0.14, 0.3, 1)
        _NightGradient ("Degrade de nuit (0 = nuages teintes, 1 = degrade seul)", Range(0, 1)) = 0.6
        _StarAmount ("Quantite d'etoiles", Range(0, 1)) = 0.5
        [HDR] _StarColor ("Couleur des etoiles (HDR = bloom)", Color) = (2, 2, 2.5, 1)
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

            // Equivalent de unity_ColorSpaceDouble en espace lineaire (le projet est en Linear) :
            // c'est ce qui fait qu'une teinte grise a 0.5 laisse la cubemap inchangee, comme Skybox/Cubemap.
            #define COLOR_SPACE_DOUBLE 4.59479380

            TEXTURECUBE(_Tex);
            SAMPLER(sampler_Tex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tex_HDR;
                float _Rotation;
                half4 _DayTint;
                half _DayExposure;
                half4 _NightTint;
                half _NightExposure;
                half4 _NightZenith;
                half4 _NightHorizon;
                half _NightGradient;
                half _StarAmount;
                half4 _StarColor;
            CBUFFER_END

            half _ToonNightBlend; // globale, ecrite par ToonDayNight

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 RotateAroundY(float3 v, float degrees)
            {
                float a = degrees * PI / 180.0;
                float s = sin(a);
                float c = cos(a);
                return float3(c * v.x - s * v.z, v.y, s * v.x + c * v.z);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(RotateAroundY(IN.positionOS.xyz, _Rotation));
                OUT.direction = IN.positionOS.xyz;
                return OUT;
            }

            // Etoiles : grille 3D de cellules sur la sphere du ciel, une petite fraction des cellules
            // (tiree au hasard mais fixe) contient un point lumineux. Seulement au-dessus de l'horizon.
            half Stars(float3 dir)
            {
                float3 p = dir * 140.0;
                float3 cell = floor(p);
                float h = frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
                float d = length(frac(p) - 0.5);
                half isStar = step(1.0 - _StarAmount * 0.04, h);
                return isStar * smoothstep(0.22, 0.0, d) * saturate(dir.y * 5.0);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.direction);
                half3 sky = DecodeHDREnvironment(SAMPLE_TEXTURECUBE(_Tex, sampler_Tex, dir), _Tex_HDR);

                half3 day = sky * _DayTint.rgb * COLOR_SPACE_DOUBLE * _DayExposure;

                // Calcul de nuit seulement quand il sert (condition identique pour tout le ciel)
                half3 color = day;
                if (_ToonNightBlend > 0.0)
                {
                    half3 nightClouds = sky * _NightTint.rgb * COLOR_SPACE_DOUBLE * _NightExposure;
                    half3 gradient = lerp(_NightHorizon.rgb, _NightZenith.rgb, saturate(dir.y));
                    half3 night = lerp(nightClouds, gradient, _NightGradient);
                    night += Stars(dir) * _StarColor.rgb;
                    color = lerp(day, night, _ToonNightBlend);
                }
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
