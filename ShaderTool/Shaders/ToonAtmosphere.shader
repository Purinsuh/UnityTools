Shader "Hidden/Toon/Atmosphere"
{
    // Voile d'atmosphere plein ecran (ToonAtmosphereFeature) : couleur proche -> lointaine selon la distance,
    // plus dense en bas (exterieur), un peu pose sur le ciel pres de l'horizon, halo vers le soleil.
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "ToonAtmosphere"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            SAMPLER(sampler_atmo_linear_clamp);
            half4 _AtmoNearColor;
            half4 _AtmoFarColor;
            half4 _AtmoSunColor;
            float4 _AtmoParams;  // intensite, debut (m), densite, distance de la couleur lointaine (m)
            float4 _AtmoParams2; // hauteur de base (m), attenuation en hauteur, part sur le ciel, halo du soleil

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_atmo_linear_clamp, uv);

                float rawDepth = SampleSceneDepth(uv);
                float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 ray = positionWS - GetCameraPositionWS();
                float distance = length(ray);
                float3 viewDir = ray / max(distance, 1e-4);

                float amount;
            #if UNITY_REVERSED_Z
                bool isSky = rawDepth <= 1e-6;
            #else
                bool isSky = rawDepth >= 1.0 - 1e-6;
            #endif
                if (isSky)
                {
                    // Ciel : voile seulement pres de l'horizon, pour fondre le decor lointain dans le ciel
                    amount = _AtmoParams2.z * (1.0 - saturate(viewDir.y * 3.0));
                    distance = _AtmoParams.w;
                }
                else
                {
                    amount = 1.0 - exp(-_AtmoParams.z * max(distance - _AtmoParams.y, 0.0));
                    // Plus dense en bas (brume au sol), s'eclaircit en montant
                    amount *= exp(-_AtmoParams2.y * max(positionWS.y - _AtmoParams2.x, 0.0));
                }

                half3 fogColor = lerp(_AtmoNearColor.rgb, _AtmoFarColor.rgb, saturate(distance / _AtmoParams.w));
                // Halo : vers le soleil, le voile prend la couleur du soleil (lumiere diffusee)
                half sun = pow(saturate(dot(viewDir, _MainLightPosition.xyz)), 8.0) * _AtmoParams2.w;
                fogColor = lerp(fogColor, _AtmoSunColor.rgb, sun);

                color.rgb = lerp(color.rgb, fogColor, saturate(amount) * _AtmoParams.x);
                return color;
            }
            ENDHLSL
        }
    }
}
