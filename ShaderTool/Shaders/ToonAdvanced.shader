Shader "Custom/ToonAdvanced"
{
    // Toon "facon Genshin / ZZZ" pour les decors, objets et personnages (contour reserve aux personnages) :
    //  - ombres par ramp texture (ombres teintees) ou 2 tons seuil + teinte
    //  - ombres portees de la lumiere principale + lumieres secondaires (point / spot)
    //  - rim light (lisere) en mode Fresnel ou Depth (base sur la profondeur ecran)
    //  - reflet speculaire net
    //  - contour colore facon Genshin (coque inversee) : passe coupee sur le materiau tant qu'il est desactive
    // Chaque effet optionnel est un mot-cle : desactive, il n'est pas compile dans la variante utilisee.
    Properties
    {
        [Header(Couleur)]
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)

        [Header(Ombres)]
        [Toggle(_USE_RAMP)] _UseRamp ("Utiliser une Ramp", Float) = 0
        [NoScaleOffset] _RampMap ("Ramp (gauche ombre - droite lumiere)", 2D) = "white" {}
        // Atlas de ramps (une ligne par type de matiere, lignes de nuit en dessous) : regle par l'outil
        _RampRow ("Ligne de l'atlas", Float) = 0
        [HideInInspector] _RampRows ("Nombre de lignes de l'atlas", Float) = 1
        [HideInInspector] _RampNightOffset ("Decalage des lignes de nuit (0 = pas de nuit)", Float) = 0
        _ShadowColor ("Teinte d'ombre (sans Ramp)", Color) = (0.55,0.58,0.7,1)
        _ShadowThreshold ("Seuil ombre / lumiere", Range(0,1)) = 0.5
        _ShadowSmoothness ("Douceur du bord", Range(0.001,0.5)) = 0.05
        _ShadowSaturation ("Saturation des ombres", Range(0,1)) = 0
        _ReceiveShadowStrength ("Force des ombres portees", Range(0,1)) = 1
        _AmbientStrength ("Lumiere ambiante", Range(0,1)) = 0.1

        [Header(Rim Light)]
        [KeywordEnum(Off, Fresnel, Depth)] _Rim ("Mode", Float) = 0
        [HDR] _RimColor ("Couleur", Color) = (1,1,1,1)
        _RimWidth ("Largeur", Range(0,1)) = 0.3
        _RimSoftness ("Douceur", Range(0.001,0.5)) = 0.05
        _RimLightAlign ("Cote eclaire uniquement", Range(0,1)) = 0.5
        _RimAlbedoTint ("Teinte par la couleur de base", Range(0,1)) = 0.5
        _RimDepthThreshold ("Ecart de profondeur min (Depth)", Range(0.01,2)) = 0.15
        [Enum(Lumiere, 0, Encre, 1)] _RimBlend ("Melange", Float) = 0

        [Header(Reflet speculaire)]
        [Toggle(_SPECULAR_ON)] _Specular ("Activer", Float) = 0
        [HDR] _SpecularColor ("Couleur", Color) = (1,1,1,1)
        _SpecularSize ("Taille", Range(0,0.5)) = 0.05
        _SpecularSoftness ("Douceur", Range(0.001,0.2)) = 0.02

        [Header(Reflet metal MatCap)]
        [Toggle(_MATCAP)] _UseMatCap ("Activer", Float) = 0
        [NoScaleOffset] _MatCapMap ("MatCap (sphere eclairee)", 2D) = "grey" {}
        _MatCapStrength ("Force", Range(0,1)) = 1
        _MatCapAdditive ("Mode (0 = metal, 1 = brillance ajoutee)", Range(0,1)) = 0
        [NoScaleOffset] _MatCapMask ("Masque (blanc = metal)", 2D) = "white" {}

        [Header(Lumieres secondaires)]
        _AdditionalLightsStrength ("Intensite", Range(0,2)) = 1

        [Header(Emissif)]
        [Toggle(_EMISSION)] _UseEmission ("Activer", Float) = 0
        [HDR] _EmissionColor ("Couleur (HDR)", Color) = (0,0,0,1)
        [NoScaleOffset] _EmissionMap ("Masque (blanc = brille)", 2D) = "white" {}

        [Header(Contour personnages)]
        // [ToonOutline] : active / coupe la passe Outline du materiau (ToonOutlineDrawer.cs), coupee = aucun cout
        [ToonOutline] _Outline ("Activer", Float) = 0
        _OutlineColor ("Couleur", Color) = (0.45,0.38,0.36,1)
        _OutlineTextureTint ("Teinte par la texture", Range(0,1)) = 1
        _OutlineWidth ("Largeur (pixels en 1080p)", Range(0,8)) = 2
        _OutlineDistance ("Distance de reference (m)", Range(0.5,20)) = 3
        _OutlineZOffset ("Recul (m) : cache le contour derriere les tissus", Range(0,0.05)) = 0.005
        // Partie du personnage (reglee par l'outil) : 0 perso, 1 cheveux, 2 peau, 3 vetements, 4 peau + vetements
        [HideInInspector] _OutlineZone ("Partie", Float) = 0
        [NoScaleOffset] _OutlineSkinMask ("Masque peau (blanc = peau)", 2D) = "black" {}
        _OutlineSkinColor ("Couleur sur la peau (masque)", Color) = (0.8,0.55,0.5,1)
        _OutlineOuterFade ("Silhouette exterieure plus claire", Range(0,1)) = 0

        [Header(Rendu)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // Partage entre toutes les passes : meme CBUFFER partout = compatible SRP Batcher.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_RampMap);
        SAMPLER(sampler_RampMap);
        TEXTURE2D(_EmissionMap);
        TEXTURE2D(_MatCapMap);
        SAMPLER(sampler_MatCapMap);
        TEXTURE2D(_MatCapMask);
        TEXTURE2D(_OutlineSkinMask);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadowColor;
            half _RampRow;
            half _RampRows;
            half _RampNightOffset;
            half _ShadowThreshold;
            half _ShadowSmoothness;
            half _ShadowSaturation;
            half _ReceiveShadowStrength;
            half _AmbientStrength;
            half4 _RimColor;
            half _RimWidth;
            half _RimSoftness;
            half _RimLightAlign;
            half _RimAlbedoTint;
            half _RimDepthThreshold;
            half _RimBlend;
            half4 _SpecularColor;
            half _SpecularSize;
            half _SpecularSoftness;
            half _AdditionalLightsStrength;
            half4 _EmissionColor;
            half _MatCapStrength;
            half _MatCapAdditive;
            half _Outline;
            half4 _OutlineColor;
            half _OutlineTextureTint;
            half _OutlineWidth;
            half _OutlineDistance;
            half _OutlineZOffset;
            half4 _OutlineSkinColor;
            half _OutlineOuterFade;
        CBUFFER_END

        // GPU Resident Drawer (Unity 6) / Entities Graphics : variante DOTS_INSTANCING_ON.
        // La couleur de base peut y etre surchargee par instance ; hors de cette variante, ce bloc n'existe pas.
        #ifdef UNITY_DOTS_INSTANCING_ENABLED
        UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
        UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

        static float4 unity_DOTS_Sampled_BaseColor;

        void SetupDOTSToonMaterialPropertyCaches()
        {
            unity_DOTS_Sampled_BaseColor = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor);
        }

        #undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
        #define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSToonMaterialPropertyCaches()
        #define _BaseColor unity_DOTS_Sampled_BaseColor
        #endif
        ENDHLSL

        // ---- Forward pass : eclairage toon ----
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local_fragment _USE_RAMP
            #pragma shader_feature_local_fragment _RIM_OFF _RIM_FRESNEL _RIM_DEPTH
            #pragma shader_feature_local_fragment _SPECULAR_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _MATCAP

            // Eclairage calcule uniquement dans le fragment shader (coordonnee d'ombre par pixel, pas de
            // lumieres par sommet) : multi_compile_fragment evite de dupliquer inutilement le vertex shader.
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // Fondu entre niveaux de detail (LOD Group en mode Cross Fade)
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            // Eclairage precalcule : seules les ombres "shadowmask" des objets statiques sont lues
            // (l'eclairage indirect precalcule est ignore, l'ambiance plate reste pilotee par ToonDayNight)
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            // Mode "Shadowmask" (pas "Distance Shadowmask") : les objets statiques ne projettent plus d'ombre
            // temps reel, l'ombre precalculee doit alors se combiner (min) a celle des objets dynamiques
            #pragma multi_compile_fragment _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            #if defined(LIGHTMAP_ON)
                float2 lightmapUV : TEXCOORD4;
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
            #if defined(LIGHTMAP_ON)
                OUTPUT_LIGHTMAP_UV(IN.lightmapUV, unity_LightmapST, OUT.lightmapUV);
            #endif
                return OUT;
            }

            // Quantite de lumiere "toon" : demi-Lambert (0 = dos a la lumiere, 0.5 = profil, 1 = face),
            // attenue par les ombres portees. C'est cette valeur qui sert de coordonnee dans la ramp.
            half ToonLightTerm(half3 normalWS, Light light)
            {
                half halfLambert = dot(normalWS, light.direction) * 0.5h + 0.5h;
                half shadow = lerp(1.0h, light.shadowAttenuation, _ReceiveShadowStrength);
                return halfLambert * shadow;
            }

            // 0 = cote ombre, 1 = cote eclaire (sert aussi a masquer le speculaire et orienter le rim)
            half ToonLitMask(half lightTerm)
            {
                return smoothstep(_ShadowThreshold - _ShadowSmoothness, _ShadowThreshold + _ShadowSmoothness, lightTerm);
            }

            // Jour/nuit de toute la scene (0 = jour, 1 = nuit), regle par le script ToonDayNight.
            // Variable globale (Shader.SetGlobalFloat) : un seul reglage pour tous les materials.
            half _ToonNightBlend;

            // Multiplicateur de couleur : ramp (ombres teintees, plusieurs bandes possibles) ou 2 tons
            half3 ToonShade(half lightTerm)
            {
            #if defined(_USE_RAMP)
                // Le seuil decale la ramp : 0.5 = la limite de la ramp tombe pile au profil de l'objet
                half u = clamp(lightTerm + (0.5h - _ShadowThreshold), 0.01h, 0.99h);
                // Atlas : on lit le centre de la ligne de ce material, la ligne 0 etant en haut de l'image
                // (une ramp simple = 1 ligne, v = 0.5)
                float rows = max(_RampRows, 1.0);
                half3 ramp = SAMPLE_TEXTURE2D(_RampMap, sampler_RampMap, float2(u, 1.0 - (_RampRow + 0.5) / rows)).rgb;
                // Condition identique pour tout l'objet (reglages du material + valeur globale) : en plein jour,
                // ou sans ligne de nuit, la seconde lecture de texture n'est pas faite.
                if (_ToonNightBlend > 0.0 && _RampNightOffset > 0.0)
                {
                    half3 nightRamp = SAMPLE_TEXTURE2D(_RampMap, sampler_RampMap, float2(u, 1.0 - (_RampRow + _RampNightOffset + 0.5) / rows)).rgb;
                    ramp = lerp(ramp, nightRamp, _ToonNightBlend);
                }
                return ramp;
            #else
                return lerp(_ShadowColor.rgb, half3(1.0h, 1.0h, 1.0h), ToonLitMask(lightTerm));
            #endif
            }

            half3 AdditionalToonLight(half3 albedo, half3 normalWS, Light light)
            {
                half lit = ToonLitMask(ToonLightTerm(normalWS, light));
                return albedo * light.color * (lit * light.distanceAttenuation * _AdditionalLightsStrength);
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(IN.positionCS);
            #endif

                // Face arriere (Cull Off : tissus, meches) : la normale est retournee pour etre eclairee de son cote
                half3 normalWS = normalize(IN.normalWS);
                normalWS = IS_FRONT_VFACE(isFrontFace, normalWS, -normalWS);
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                half3 albedo = (SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor).rgb;

                // Ombres precalculees des objets statiques (Shadowmask). Objet precalcule : lue dans la lightmap ;
                // objet dynamique : lue dans les light probes. Sans precalcul, valeur neutre (pas d'ombre).
            #if defined(LIGHTMAP_ON)
                half4 shadowMask = SAMPLE_SHADOWMASK(IN.lightmapUV);
            #else
                half4 shadowMask = SAMPLE_SHADOWMASK(float2(0, 0));
            #endif

                // ---- Lumiere principale ----
                // Coordonnee d'ombre calculee par pixel (et non par sommet) : correcte meme avec les cascades.
                // URP melange ici l'ombre temps reel (proche) et l'ombre precalculee (au-dela de la distance d'ombre).
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS), IN.positionWS, shadowMask);
                half mainTerm = ToonLightTerm(normalWS, mainLight);
                half mainLit = ToonLitMask(mainTerm);

                half3 color = albedo * ToonShade(mainTerm) * mainLight.color;

                // Multiplier par une teinte d'ombre ternit les couleurs : on resature la zone a l'ombre
                // (les ombres Genshin / ZZZ restent colorees, pas grisatres)
                half luma = dot(color, half3(0.2126h, 0.7152h, 0.0722h));
                color = max(0.0h, lerp(luma.xxx, color, 1.0h + _ShadowSaturation * (1.0h - mainLit)));

                // Ambiante "plate" (bande L0 des harmoniques spheriques = couleur moyenne du ciel) :
                // eclaircit les ombres sans recreer un degrade realiste qui casserait le look toon.
                half3 flatAmbient = half3(unity_SHAr.w, unity_SHAg.w, unity_SHAb.w);
                color += albedo * flatAmbient * _AmbientStrength;

                // ---- Reflet metal (MatCap) ----
                // Une MatCap est l'image d'une sphere deja "eclairee" : on y lit la couleur qui correspond a
                // l'orientation du pixel vue depuis la camera. Une lecture de texture pour un aspect metal stylise.
            #if defined(_MATCAP)
                float3 viewDirVS = normalize(TransformWorldToView(IN.positionWS));
                float3 matcapNormalVS = TransformWorldToViewNormal(normalWS, true);
                // Coordonnees calculees a partir du reflet (formule "sphere map") : moins de deformation
                // sur les bords de l'objet qu'avec la normale seule
                float3 r = reflect(viewDirVS, matcapNormalVS);
                float m = max(2.0 * sqrt(r.x * r.x + r.y * r.y + (r.z + 1.0) * (r.z + 1.0)), 1e-4);
                half3 matcap = SAMPLE_TEXTURE2D(_MatCapMap, sampler_MatCapMap, r.xy / m + 0.5).rgb;
                half matcapMask = SAMPLE_TEXTURE2D(_MatCapMask, sampler_BaseMap, IN.uv).r * _MatCapStrength;
                // Mode metal : la MatCap module la couleur (gris 0.5 = neutre) ; mode brillance : elle s'ajoute
                half3 metal = lerp(color * matcap * 2.0h, color + matcap, _MatCapAdditive);
                color = lerp(color, metal, matcapMask);
            #endif

                // ---- Reflet speculaire net ----
            #if defined(_SPECULAR_ON)
                half3 halfDir = SafeNormalize(mainLight.direction + viewDirWS);
                half NdotH = saturate(dot(normalWS, halfDir));
                half spec = smoothstep(1.0h - _SpecularSize - _SpecularSoftness, 1.0h - _SpecularSize + _SpecularSoftness, NdotH);
                color += spec * mainLit * _SpecularColor.rgb * mainLight.color;
            #endif

                // ---- Lumieres secondaires (point / spot) ----
            #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                uint pixelLightCount = GetAdditionalLightsCount();

                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); dirIndex++)
                {
                    Light dirLight = GetAdditionalLight(dirIndex, IN.positionWS, shadowMask);
                    color += AdditionalToonLight(albedo, normalWS, dirLight);
                }
                #endif

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, IN.positionWS, shadowMask);
                    color += AdditionalToonLight(albedo, normalWS, light);
                LIGHT_LOOP_END
            #endif

                // ---- Rim light ----
            #if defined(_RIM_FRESNEL) || defined(_RIM_DEPTH)
                half rim = 0.0h;
                #if defined(_RIM_FRESNEL)
                    half fresnel = 1.0h - saturate(dot(normalWS, viewDirWS));
                    half edge = 1.0h - _RimWidth;
                    rim = smoothstep(edge - _RimSoftness, edge + _RimSoftness, fresnel);
                #else
                    // On decale l'UV ecran vers l'exterieur (le long de la normale) et on regarde si la
                    // profondeur saute derriere l'objet : si oui, on est sur un bord de silhouette.
                    // Avantage sur le Fresnel : les grandes surfaces plates (sol, murs) ne s'illuminent pas.
                    float selfDepth = -TransformWorldToView(IN.positionWS).z;
                    half3 normalVS = TransformWorldToViewNormal(normalWS, true);
                    // Largeur constante en metres (0 a 5 cm) : le lisere garde la meme taille sur l'objet quelle que soit la distance
                    float2 uvPerMeter = float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11) * (0.5 / max(selfDepth, 0.01));
                    #if UNITY_UV_STARTS_AT_TOP
                        uvPerMeter.y = -uvPerMeter.y;
                    #endif
                    float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                    float2 offsetUV = normalVS.xy * uvPerMeter * (_RimWidth * 0.05);
                    float otherDepth = LinearEyeDepth(SampleSceneDepth(screenUV + offsetUV), _ZBufferParams);
                    // Sur une surface plane vue en biais (sol, mur), la profondeur augmente naturellement
                    // dans la direction du decalage : on predit cette profondeur "attendue" avec la pente
                    // locale (ddx/ddy) et on ne garde que l'ecart au-dela. Sans ca, les murs rasants s'allument.
                    float2 offsetPx = offsetUV * _ScaledScreenParams.xy;
                    float expectedDepth = selfDepth + offsetPx.x * ddx(selfDepth) + offsetPx.y * ddy(selfDepth);
                    rim = smoothstep(_RimDepthThreshold, _RimDepthThreshold + _RimSoftness, otherDepth - expectedDepth);
                #endif

                half align = lerp(1.0h, smoothstep(0.35h, 0.65h, mainTerm), _RimLightAlign);
                half3 rimColor = _RimColor.rgb * lerp(half3(1.0h, 1.0h, 1.0h), albedo, _RimAlbedoTint);
                half rimMask = rim * align;
                // Lumiere : le lisere s'ajoute (eclaircit, suit la lumiere) -> le noir n'a aucun effet.
                // Encre : le lisere remplace la couleur (independant de l'eclairage) -> un noir donne un vrai contour.
                half3 rimLit = color + rimMask * rimColor * mainLight.color;
                half3 rimInk = lerp(color, rimColor, rimMask);
                color = lerp(rimLit, rimInk, _RimBlend);
            #endif

                // ---- Emissif ----
                // Ajoute apres l'eclairage : la zone brille meme dans l'ombre. Avec une couleur HDR (> 1),
                // le bloom de la post-production la fait deborder (neons, ecrans, lampes).
            #if defined(_EMISSION)
                color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, IN.uv).rgb * _EmissionColor.rgb;
            #endif

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // ---- Outline : contour des personnages (coque inversee) ----
        // LightMode SRPDefaultUnlit = dessinee par URP juste apres la passe Forward, sans Renderer Feature.
        // L'outil et ToonOutlineDrawer coupent cette passe sur le materiau quand le contour est desactive :
        // les decors ne la paient donc jamais.
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                // Normale lissee en espace tangent, ecrite a l'import par ToonOutlineNormals.cs (0 si absente)
                float3 smoothNormalTS : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Aux aretes vives, chaque copie du sommet a sa propre normale : la coque s'ouvrirait.
                // La normale lissee (moyenne des normales a cette position) la garde fermee.
                float3 normalOS = IN.normalOS;
                if (dot(IN.smoothNormalTS, IN.smoothNormalTS) > 0.01)
                {
                    float3 bitangentOS = cross(IN.normalOS, IN.tangentOS.xyz) * IN.tangentOS.w;
                    normalOS = IN.tangentOS.xyz * IN.smoothNormalTS.x + bitangentOS * IN.smoothNormalTS.y + IN.normalOS * IN.smoothNormalTS.z;
                }

                // Recul le long du rayon de vue : meme place a l'ecran, mais un peu plus loin. Vue de dos, une
                // surface simple face (tissu, meche) laisse voir sa coque : la face arriere du tissu (Cull Off)
                // passe alors devant et seul le bord du contour reste visible.
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                positionWS += normalize(positionWS - GetCameraPositionWS()) * _OutlineZOffset;
                float4 positionCS = TransformWorldToHClip(positionWS);
                float3 normalCS = mul((float3x3)UNITY_MATRIX_VP, TransformObjectToWorldNormal(normalOS));

                // Largeur en pixels (reference 1080p) exacte a _OutlineDistance : plus pres le trait grossit (x2 max,
                // gros plans), plus loin il s'affine comme un vrai trait sur l'objet.
                float pixels = _OutlineWidth * (_ScreenParams.y / 1080.0) * min(2.0, _OutlineDistance / max(positionCS.w, 0.001));
                float2 dirPx = normalCS.xy * _ScreenParams.xy;
                dirPx *= rsqrt(max(dot(dirPx, dirPx), 1e-8));
                positionCS.xy += dirPx * (pixels * 2.0 / _ScreenParams.xy) * positionCS.w;

                // Garde-fou si la passe n'a pas ete coupee alors que le contour est desactive : rien n'est trace
                OUT.positionCS = _Outline > 0.5 ? positionCS : float4(2.0, 2.0, 2.0, 1.0);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(positionCS.z);
                return OUT;
            }

            half4 OutlineFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                // Couleur tiree de la texture : la mettre au carre l'assombrit et la sature (cheveux noirs -> trait
                // noir, peau claire -> trait brun rose), comme les contours par materiau de Genshin.
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;
                // Materiau qui melange peau et vetements (ex. corps) : le masque (genere par l'outil) donne
                // la couleur de la peau la ou il est blanc. Sans masque (noir par defaut), rien ne change.
                half skin = SAMPLE_TEXTURE2D(_OutlineSkinMask, sampler_BaseMap, IN.uv).r;
                half3 tint = lerp(_OutlineColor.rgb, _OutlineSkinColor.rgb, skin);
                half3 color = tint * lerp(1.0h, albedo * albedo, _OutlineTextureTint);

                // Bord exterieur de la silhouette plus clair, traits interieurs (plis, couches) bien marques :
                // derriere un bord exterieur, la profondeur de la scene est loin derriere le trait.
                if (_OutlineOuterFade > 0.0h)
                {
                    float sceneDepth = LinearEyeDepth(SampleSceneDepth(IN.positionCS.xy / _ScaledScreenParams.xy), _ZBufferParams);
                    float outlineDepth = LinearEyeDepth(IN.positionCS.z, _ZBufferParams);
                    half outer = smoothstep(0.3, 1.0, sceneDepth - outlineDepth);
                    color = lerp(color, albedo, outer * _OutlineOuterFade);
                }
                // Suit un peu la lumiere principale (plus sombre et bleute la nuit)
                color *= lerp(1.0h, min(_MainLightColor.rgb, 1.0h), 0.5h);
                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // ---- ShadowCaster : l'objet projette des ombres ----
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            // Les lumieres point/spot utilisent une autre formule de bias que la directionnelle
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // Shadows.hlsl contient ApplyShadowBias / ApplyShadowClamping (bias regle sur la lumiere elle-meme)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // L'ID d'instance va jusqu'au fragment : avec le GPU Resident Drawer, le facteur de fondu LOD
            // fait partie des donnees par instance.
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirWS = _LightDirection;
            #endif
                OUT.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirWS)));
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(IN.positionCS);
            #endif
                return 0;
            }
            ENDHLSL
        }

        // ---- DepthOnly : ecrit la profondeur (depth prepass, texture de profondeur camera) ----
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half DepthFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(IN.positionCS);
            #endif
                return IN.positionCS.z;
            }
            ENDHLSL
        }

        // ---- DepthNormals : profondeur + normales (utilise par le SSAO et le rim en mode Depth) ----
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 DepthNormalsFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(IN.positionCS);
            #endif
                return half4(NormalizeNormalPerPixel(IN.normalWS), 0.0h);
            }
            ENDHLSL
        }

        // ---- Meta : utilisee uniquement par le precalcul de lumiere (couleur et emission de l'objet) ----
        Pass
        {
            Name "Meta"
            Tags { "LightMode"="Meta" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex UniversalVertexMeta
            #pragma fragment ToonFragmentMeta
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UniversalMetaPass.hlsl"

            half4 ToonFragmentMeta(Varyings IN) : SV_Target
            {
                MetaInput metaInput = (MetaInput)0;
                metaInput.Albedo = (SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor).rgb;
            #if defined(_EMISSION)
                metaInput.Emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, IN.uv).rgb * _EmissionColor.rgb;
            #endif
                return UniversalFragmentMeta(IN, metaInput);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
