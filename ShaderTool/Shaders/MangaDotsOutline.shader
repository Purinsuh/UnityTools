Shader "Custom/MangaDotsOutline"
{
    Properties
    {
        _DarkColor ("Dot Color (dark)", Color) = (0.05,0.05,0.1,1)
        _BaseColorBottom ("Base Color - Bottom (dark)", Color) = (0.35,0.35,0.45,1)
        _BaseColorTop ("Base Color - Top (light)", Color) = (0.95,0.95,0.92,1)
        _DotDensity ("Dot Density", Range(2,60)) = 18
        _MaxDotRadius ("Max Dot Radius (base)", Range(0,0.9)) = 0.55
        _MinDotRadius ("Min Dot Radius (top)", Range(0,0.9)) = 0.03
        _DotSoftness ("Dot Softness", Range(0.001,0.3)) = 0.06
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Width", Range(0.0, 0.05)) = 0.01
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        // ---- Outline Pass (inverted hull) ----
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionOS = IN.positionOS.xyz + IN.normalOS * _OutlineWidth;
                OUT.positionHCS = TransformObjectToHClip(positionOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        // ---- Manga Dots Gradient Pass ----
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DarkColor;
                float4 _BaseColorBottom;
                float4 _BaseColorTop;
                float _DotDensity;
                float _MaxDotRadius;
                float _MinDotRadius;
                float _DotSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float heightT : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                // Assumes default primitive cube local bounds (-0.5 .. 0.5)
                OUT.heightT = saturate(IN.positionOS.y + 0.5);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 gridUV = frac(IN.uv * _DotDensity) - 0.5;
                float dist = length(gridUV);

                // Base = dark (0), Top = light (1)
                float dotRadius = lerp(_MaxDotRadius, _MinDotRadius, IN.heightT);
                float dotMask = smoothstep(dotRadius, dotRadius - _DotSoftness, dist);

                half4 bgColor = lerp(_BaseColorBottom, _BaseColorTop, IN.heightT);
                half4 col = lerp(bgColor, _DarkColor, dotMask);
                return col;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
