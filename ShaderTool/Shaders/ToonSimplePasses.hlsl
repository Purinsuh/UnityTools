// Passes communes aux shaders simples de l'outil (CelShading, ToonOutline, MangaDotsOutline...) :
//  - ombre portee (ShadowCaster)
//  - profondeur (DepthOnly) et profondeur + normales (DepthNormals) : sans elles, l'objet manque dans la texture
//    de profondeur de la camera (atmosphere, lisere Depth, SSAO le prendraient pour du vide).
// Le shader qui l'inclut declare son CBUFFER UnityPerMaterial dans un HLSLINCLUDE commun a toutes ses passes
// (condition du SRP Batcher) ; ces passes n'utilisent aucune propriete du material.
#ifndef TOON_SIMPLE_PASSES_INCLUDED
#define TOON_SIMPLE_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct SimpleAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
};

struct SimpleDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS : TEXCOORD0;
};

SimpleDepthVaryings SimpleDepthVert(SimpleAttributes IN)
{
    SimpleDepthVaryings OUT;
    OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
    OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
    return OUT;
}

half SimpleDepthFrag(SimpleDepthVaryings IN) : SV_Target
{
    return IN.positionCS.z;
}

half4 SimpleDepthNormalsFrag(SimpleDepthVaryings IN) : SV_Target
{
    return half4(NormalizeNormalPerPixel(IN.normalWS), 0.0h);
}

#if defined(TOON_SIMPLE_SHADOW_CASTER)
// ApplyShadowBias / ApplyShadowClamping : bias regle sur la lumiere elle-meme, comme les shaders URP
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

float3 _LightDirection;
float3 _LightPosition;

float4 SimpleShadowVert(SimpleAttributes IN) : SV_POSITION
{
    float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirWS = _LightDirection;
#endif
    return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirWS)));
}

half SimpleShadowFrag() : SV_Target
{
    return 0;
}
#endif

#endif
