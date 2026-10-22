using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Atmosphere stylisee facon Genshin : voile colore selon la distance (et la hauteur en exterieur), lointains
// bleutes, halo vers le soleil. Reglee par des Volumes : un global pour la scene, des locaux pour les pieces,
// melanges automatiquement par Unity. Dessinee par ToonAtmosphereFeature (a ajouter au renderer URP).
[Serializable, VolumeComponentMenu("Toon/Atmosphere")]
[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public class ToonAtmosphere : VolumeComponent, IPostProcessComponent
{
    [Tooltip("0 = pas d'atmosphere (l'effet n'est pas dessine du tout)")]
    public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

    [Tooltip("Couleur du voile pres de la camera")]
    public ColorParameter nearColor = new ColorParameter(new Color(0.8f, 0.88f, 1f), false, false, true);
    [Tooltip("Couleur du voile au loin")]
    public ColorParameter farColor = new ColorParameter(new Color(0.62f, 0.76f, 0.95f), false, false, true);
    [Tooltip("Distance (m) a laquelle le voile atteint la couleur lointaine")]
    public MinFloatParameter colorDistance = new MinFloatParameter(150f, 0.1f);

    [Tooltip("Distance (m) avant que le voile commence")]
    public MinFloatParameter startDistance = new MinFloatParameter(15f, 0f);
    [Tooltip("Epaisseur du voile : plus haut = plus opaque, plus vite")]
    public ClampedFloatParameter density = new ClampedFloatParameter(0.012f, 0f, 0.5f);
    [Tooltip("Hauteur (m) sous laquelle le voile est le plus dense")]
    public FloatParameter heightBase = new FloatParameter(0f);
    [Tooltip("Le voile s'eclaircit en montant (0 = meme densite partout, ex. interieur)")]
    public ClampedFloatParameter heightFalloff = new ClampedFloatParameter(0.02f, 0f, 1f);

    [Tooltip("Part du voile posee sur le ciel, pres de l'horizon")]
    public ClampedFloatParameter skyAmount = new ClampedFloatParameter(0.35f, 0f, 1f);
    [Tooltip("Couleur du halo quand on regarde vers le soleil")]
    public ColorParameter sunColor = new ColorParameter(new Color(1f, 0.9f, 0.75f), false, false, true);
    public ClampedFloatParameter sunGlow = new ClampedFloatParameter(0.35f, 0f, 1f);

    [Tooltip("Teinte appliquee au voile la nuit (suit ToonDayNight)")]
    public ColorParameter nightTint = new ColorParameter(new Color(0.25f, 0.3f, 0.5f), false, false, true);

    public bool IsActive() => intensity.value > 0f;
}
