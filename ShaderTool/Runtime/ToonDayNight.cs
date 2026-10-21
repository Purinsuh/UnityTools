using UnityEngine;
using UnityEngine.Rendering;

// Jour/nuit de la scene pour le shader Custom/ToonAdvanced : un seul curseur pilote
//  - les ramps (lignes de nuit des atlas), via la variable globale _ToonNightBlend
//  - la couleur / l'intensite de la lumiere principale
//  - l'ambiance "plate" lue par le shader
// ExecuteAlways : le curseur agit aussi en mode edition, pour regler l'ambiance sans lancer le jeu.
[ExecuteAlways]
public class ToonDayNight : MonoBehaviour
{
    [Header("0 = plein jour, 1 = pleine nuit")]
    [Range(0f, 1f)] public float night;

    [Header("Lumiere principale (soleil / lune)")]
    [SerializeField] private Light mainLight;
    [SerializeField] private Color dayLightColor = new Color(1f, 0.96f, 0.9f);
    [SerializeField] private float dayLightIntensity = 1f;
    [SerializeField] private Color nightLightColor = new Color(0.55f, 0.65f, 1f);
    [SerializeField] private float nightLightIntensity = 0.75f;

    [Header("Ambiance")]
    [SerializeField] private bool driveAmbient = true;
    [SerializeField] private Color dayAmbient = new Color(0.42f, 0.47f, 0.6f);
    [SerializeField] private Color nightAmbient = new Color(0.22f, 0.26f, 0.45f);

    [Header("Reflets de l'environnement (vitres, materials Lit)")]
    // Les reflets sont precalcules a partir du ciel de jour : sans ca, les vitres refletent le jour en pleine nuit
    [SerializeField] private bool driveReflections = true;
    [SerializeField] private float dayReflectionIntensity = 1f;
    [SerializeField] private float nightReflectionIntensity = 0.2f;

    private static readonly int NightBlendId = Shader.PropertyToID("_ToonNightBlend");
    private float appliedNight = -1f;

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    private void Update()
    {
        // Seulement quand la valeur change (animation, script de cycle jour/nuit...)
        if (!Mathf.Approximately(night, appliedNight))
            Apply();
    }

    private void OnDisable()
    {
        // Sans ce composant, la scene revient au jour
        Shader.SetGlobalFloat(NightBlendId, 0f);
        appliedNight = -1f;
    }

    public void Apply()
    {
        appliedNight = night;
        Shader.SetGlobalFloat(NightBlendId, night);

        if (mainLight != null)
        {
            mainLight.color = Color.Lerp(dayLightColor, nightLightColor, night);
            mainLight.intensity = Mathf.Lerp(dayLightIntensity, nightLightIntensity, night);
        }

        if (driveAmbient)
        {
            // Le shader lit la couleur moyenne des harmoniques spheriques (ambientProbe) :
            // on la remplit directement, ce qui marche aussi en jeu sans recalcul d'eclairage.
            Color ambient = Color.Lerp(dayAmbient, nightAmbient, night);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(ambient.linear); // les harmoniques sont en espace lineaire
            RenderSettings.ambientProbe = probe;
        }

        if (driveReflections)
            RenderSettings.reflectionIntensity = Mathf.Lerp(dayReflectionIntensity, nightReflectionIntensity, night);
    }
}
