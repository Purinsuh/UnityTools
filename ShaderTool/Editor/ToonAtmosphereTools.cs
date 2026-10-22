using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Onglet "Atmosphere" de l'outil : ambiances pretes a l'emploi (voile ToonAtmosphere + balance des blancs + vignette)
// posees en un clic, pour toute la scene (Volume global) ou pour une piece (Volume local autour de la selection).
// Unity melange les ambiances quand la camera passe d'une zone a l'autre.
public static class ToonAtmosphereTools
{
    private const int GlobalPriority = 10;
    private const int ZonePriority = 20;
    private const string GlobalName = "Atmosphere (scene)";

    public struct Preset
    {
        public string label;
        public Color near, far, sun;
        public float colorDistance, start, density, heightBase, heightFalloff, sky, sunGlow;
        public float intensity, temperature, tint, vignette;
    }

    // Exterieur : voile lointain, brume au sol, halo du soleil. Interieur : voile court et regulier, teinte de la piece.
    public static readonly Preset[] Presets =
    {
        new Preset { label = "Exterieur - Jour clair", intensity = 1f, near = new Color(0.80f, 0.88f, 1f), far = new Color(0.62f, 0.76f, 0.95f), colorDistance = 150f,
            start = 15f, density = 0.012f, heightFalloff = 0.02f, sky = 0.35f, sun = new Color(1f, 0.9f, 0.75f), sunGlow = 0.35f, vignette = 0.15f },
        new Preset { label = "Exterieur - Matin brumeux", intensity = 1f, near = new Color(0.85f, 0.87f, 0.9f), far = new Color(0.78f, 0.82f, 0.88f), colorDistance = 60f,
            start = 2f, density = 0.06f, heightFalloff = 0.15f, sky = 0.7f, sun = new Color(1f, 0.95f, 0.85f), sunGlow = 0.25f, temperature = -5f, vignette = 0.2f },
        new Preset { label = "Exterieur - Coucher de soleil", intensity = 1f, near = new Color(1f, 0.78f, 0.65f), far = new Color(0.95f, 0.55f, 0.5f), colorDistance = 120f,
            start = 10f, density = 0.02f, heightFalloff = 0.03f, sky = 0.5f, sun = new Color(1f, 0.6f, 0.3f), sunGlow = 0.7f, temperature = 20f, tint = 5f, vignette = 0.25f },
        new Preset { label = "Exterieur - Nuit", intensity = 1f, near = new Color(0.15f, 0.2f, 0.35f), far = new Color(0.1f, 0.14f, 0.28f), colorDistance = 80f,
            start = 8f, density = 0.03f, heightFalloff = 0.05f, sky = 0.4f, sun = new Color(0.6f, 0.7f, 1f), sunGlow = 0.2f, temperature = -15f, vignette = 0.3f },
        new Preset { label = "Interieur - Chaleureux", intensity = 1f, near = new Color(1f, 0.9f, 0.78f), far = new Color(0.95f, 0.8f, 0.65f), colorDistance = 25f,
            start = 4f, density = 0.02f, sun = new Color(1f, 0.85f, 0.6f), sunGlow = 0.15f, temperature = 25f, tint = 3f, vignette = 0.25f },
        new Preset { label = "Interieur - Frais et lumineux", intensity = 1f, near = new Color(0.92f, 0.96f, 1f), far = new Color(0.85f, 0.92f, 1f), colorDistance = 30f,
            start = 5f, density = 0.012f, sun = new Color(1f, 0.95f, 0.9f), sunGlow = 0.1f, temperature = -10f, vignette = 0.1f },
        new Preset { label = "Interieur - Mysterieux (cave)", intensity = 1f, near = new Color(0.25f, 0.35f, 0.4f), far = new Color(0.12f, 0.2f, 0.25f), colorDistance = 15f,
            start = 1f, density = 0.12f, sun = Color.white, temperature = -10f, tint = -5f, vignette = 0.4f },
        new Preset { label = "Aucune (annule le voile)", intensity = 0f, near = Color.white, far = Color.white, colorDistance = 100f, sun = Color.white },
    };

    private static int globalPreset;
    private static int zonePreset = 4;

    public static void DrawTab(string folder)
    {
        EditorGUILayout.LabelField("Atmosphere de la scene active", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Voile colore selon la distance, lointains bleutes, halo vers le soleil, teinte de la piece. " +
            "Une ambiance pour toute la scene, et des zones (pieces, caves...) qui la remplacent quand la camera y entre.",
            MessageType.Info);

        if (!DrawInstallation()) return;
        var labels = Presets.Select(p => p.label).ToArray();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Toute la scene", EditorStyles.miniBoldLabel);
        var global = FindVolumes().FirstOrDefault(v => v.isGlobal && v.name == GlobalName);
        EditorGUILayout.BeginHorizontal();
        globalPreset = EditorGUILayout.Popup(globalPreset, labels);
        if (GUILayout.Button(global == null ? "Creer" : "Remplacer", GUILayout.Width(90)))
            global = ApplyGlobal(Presets[globalPreset], folder);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Zone (piece, cave...)", EditorStyles.miniBoldLabel);
        var bounds = GetSelectionBounds();
        EditorGUILayout.BeginHorizontal();
        zonePreset = EditorGUILayout.Popup(zonePreset, labels);
        using (new EditorGUI.DisabledScope(!bounds.HasValue))
        {
            if (GUILayout.Button(new GUIContent("Creer autour de la selection",
                "Selectionner les objets de la piece (murs, sol, plafond) : la zone prend leur taille."), GUILayout.Width(190)))
                CreateZone(Presets[zonePreset], bounds.Value, folder);
        }
        EditorGUILayout.EndHorizontal();
        if (!bounds.HasValue)
            EditorGUILayout.HelpBox("Selectionner dans la Hierarchy les objets de la piece pour creer une zone.", MessageType.None);

        // Ambiances presentes dans la scene, et reglages de celle qui est selectionnee (sinon la globale)
        var volumes = FindVolumes().Where(v => v.sharedProfile != null && v.sharedProfile.Has<ToonAtmosphere>()).ToList();
        if (volumes.Count == 0) return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Ambiances de la scene", EditorStyles.miniBoldLabel);
        var selected = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Volume>() : null;
        if (selected == null || !volumes.Contains(selected)) selected = global ?? volumes[0];
        foreach (var v in volumes)
        {
            var style = v == selected ? EditorStyles.boldLabel : EditorStyles.label;
            if (GUILayout.Button((v.isGlobal ? "Scene : " : "Zone : ") + v.name, style))
                Selection.activeGameObject = v.gameObject;
        }
        EditorGUILayout.Space();
        DrawSettings(selected);
    }

    // ---- Installation de l'effet sur les renderers URP du projet ----

    private static IEnumerable<ScriptableRendererData> GetRendererDatas()
    {
        var assets = new List<UniversalRenderPipelineAsset>();
        if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset main) assets.Add(main);
        for (int i = 0; i < QualitySettings.count; i++)
            if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset q && !assets.Contains(q)) assets.Add(q);
        foreach (var asset in assets)
        {
            var list = new SerializedObject(asset).FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is ScriptableRendererData data)
                    yield return data;
        }
    }

    private static List<ScriptableRendererData> GetMissingRenderers() =>
        GetRendererDatas().Distinct().Where(d => !d.rendererFeatures.Any(f => f is ToonAtmosphereFeature)).ToList();

    private static bool DrawInstallation()
    {
        var missing = GetMissingRenderers();
        if (missing.Count == 0) return true;
        EditorGUILayout.HelpBox("L'effet d'atmosphere n'est pas encore sur les renderers URP du projet (" +
            string.Join(", ", missing.Select(d => d.name)) + ").", MessageType.Warning);
        if (GUILayout.Button("Installer l'effet sur les renderers", GUILayout.Height(28)))
            Install();
        return false;
    }

    // Ajoute ToonAtmosphereFeature a chaque renderer URP qui ne l'a pas (comme "Add Renderer Feature")
    public static void Install()
    {
        foreach (var data in GetMissingRenderers())
        {
            var feature = ScriptableObject.CreateInstance<ToonAtmosphereFeature>();
            feature.name = "Toon Atmosphere";
            Undo.RegisterCreatedObjectUndo(feature, "Installer l'atmosphere");
            AssetDatabase.AddObjectToAsset(feature, data);
            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long id);
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
            so.ApplyModifiedProperties();
            var shaderProp = new SerializedObject(feature);
            shaderProp.FindProperty("shader").objectReferenceValue = Shader.Find("Hidden/Toon/Atmosphere");
            shaderProp.ApplyModifiedProperties();
            data.SetDirty();
            EditorUtility.SetDirty(data);
        }
        AssetDatabase.SaveAssets();
    }

    // ---- Creation des ambiances ----

    public static Volume ApplyGlobal(Preset preset, string folder)
    {
        var volume = FindVolumes().FirstOrDefault(v => v.isGlobal && v.name == GlobalName);
        if (volume == null)
        {
            var go = new GameObject(GlobalName);
            Undo.RegisterCreatedObjectUndo(go, "Ambiance de la scene");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = GlobalPriority;
        }
        Undo.RecordObject(volume, "Ambiance de la scene");
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        // Le profil de la scene est reutilise (les autres scenes ne sont pas touchees)
        if (volume.sharedProfile == null || !AssetDatabase.GetAssetPath(volume.sharedProfile).StartsWith(folder))
            volume.sharedProfile = CreateProfile(folder + "/Atmosphere_" + scene + ".asset");
        ApplyPreset(volume.sharedProfile, preset);
        Selection.activeGameObject = volume.gameObject;
        return volume;
    }

    public static void CreateZone(Preset preset, Bounds bounds, string folder)
    {
        var go = new GameObject("Atmosphere - " + preset.label.Replace("Interieur - ", "").Replace("Exterieur - ", ""));
        Undo.RegisterCreatedObjectUndo(go, "Zone d'atmosphere");
        go.transform.position = bounds.center;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = bounds.size + Vector3.one * 0.2f;
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = false;
        volume.priority = ZonePriority;
        volume.blendDistance = 1.5f; // transition douce en entrant dans la piece
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        volume.sharedProfile = CreateProfile(folder + "/Atmosphere_" + scene + "_Zone.asset");
        ApplyPreset(volume.sharedProfile, preset);
        Selection.activeGameObject = go;
    }

    private static VolumeProfile CreateProfile(string path)
    {
        EnsureFolderExists(Path.GetDirectoryName(path).Replace('\\', '/'));
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, AssetDatabase.GenerateUniqueAssetPath(path));
        AddComponent<ToonAtmosphere>(profile);
        AddComponent<WhiteBalance>(profile);
        AddComponent<Vignette>(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    private static void AddComponent<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var component = profile.Add<T>(false);
        component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(component, profile);
    }

    public static void ApplyPreset(VolumeProfile profile, Preset p)
    {
        if (!profile.TryGet(out ToonAtmosphere a)) a = profile.Add<ToonAtmosphere>(false);
        Undo.RecordObject(a, "Prereglage d'atmosphere");
        a.intensity.Override(p.intensity);
        a.nearColor.Override(p.near);
        a.farColor.Override(p.far);
        a.colorDistance.Override(p.colorDistance);
        a.startDistance.Override(p.start);
        a.density.Override(p.density);
        a.heightBase.Override(p.heightBase);
        a.heightFalloff.Override(p.heightFalloff);
        a.skyAmount.Override(p.sky);
        a.sunColor.Override(p.sun);
        a.sunGlow.Override(p.sunGlow);
        a.nightTint.Override(a.nightTint.value);
        EditorUtility.SetDirty(a);
        // Teinte de la piece et vignette : seules ces valeurs sont surchargees, le reste du post-process ne bouge pas
        if (profile.TryGet(out WhiteBalance wb))
        {
            Undo.RecordObject(wb, "Prereglage d'atmosphere");
            wb.temperature.Override(p.temperature);
            wb.tint.Override(p.tint);
            EditorUtility.SetDirty(wb);
        }
        if (profile.TryGet(out Vignette vignette))
        {
            Undo.RecordObject(vignette, "Prereglage d'atmosphere");
            vignette.intensity.Override(p.vignette);
            vignette.smoothness.Override(0.4f);
            EditorUtility.SetDirty(vignette);
        }
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        SceneView.RepaintAll();
    }

    // ---- Reglages de l'ambiance selectionnee ----

    private static void DrawSettings(Volume volume)
    {
        var profile = volume.sharedProfile;
        if (!profile.TryGet(out ToonAtmosphere a)) return;
        EditorGUILayout.LabelField("Reglages : " + volume.name, EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        int preset = EditorGUILayout.Popup("Partir du prereglage", -1, Presets.Select(p => p.label).ToArray());
        EditorGUILayout.EndHorizontal();
        if (preset >= 0) ApplyPreset(profile, Presets[preset]);

        Slider(a, a.intensity, "Intensite", 0f, 1f);
        ColorField(a, a.nearColor, "Couleur proche");
        ColorField(a, a.farColor, "Couleur lointaine");
        Float(a, a.colorDistance, "Distance de la couleur lointaine (m)");
        Float(a, a.startDistance, "Debut du voile (m)");
        Slider(a, a.density, "Densite", 0f, 0.5f);
        Float(a, a.heightBase, "Hauteur du sol de brume (m)");
        Slider(a, a.heightFalloff, "Brume au sol (0 = partout pareil)", 0f, 1f);
        Slider(a, a.skyAmount, "Voile sur le ciel (horizon)", 0f, 1f);
        ColorField(a, a.sunColor, "Couleur du halo du soleil");
        Slider(a, a.sunGlow, "Halo du soleil", 0f, 1f);
        ColorField(a, a.nightTint, "Teinte la nuit");

        if (profile.TryGet(out WhiteBalance wb))
        {
            Slider(wb, wb.temperature, "Temperature (chaud / froid)", -100f, 100f);
            Slider(wb, wb.tint, "Teinte (vert / magenta)", -100f, 100f);
        }
        if (profile.TryGet(out Vignette vignette))
            Slider(vignette, vignette.intensity, "Vignette", 0f, 1f);
        if (!volume.isGlobal)
        {
            EditorGUI.BeginChangeCheck();
            float blend = EditorGUILayout.FloatField("Fondu en entrant (m)", volume.blendDistance);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(volume, "Fondu de la zone");
                volume.blendDistance = Mathf.Max(0f, blend);
            }
        }
    }

    private static void Slider(VolumeComponent owner, ClampedFloatParameter p, string label, float min, float max)
    {
        EditorGUI.BeginChangeCheck();
        float v = EditorGUILayout.Slider(label, p.value, min, max);
        if (EditorGUI.EndChangeCheck()) Set(owner, () => p.Override(v));
    }

    private static void Slider(VolumeComponent owner, FloatParameter p, string label, float min, float max)
    {
        EditorGUI.BeginChangeCheck();
        float v = EditorGUILayout.Slider(label, p.value, min, max);
        if (EditorGUI.EndChangeCheck()) Set(owner, () => p.Override(v));
    }

    private static void Float(VolumeComponent owner, FloatParameter p, string label)
    {
        EditorGUI.BeginChangeCheck();
        float v = EditorGUILayout.FloatField(label, p.value);
        if (EditorGUI.EndChangeCheck()) Set(owner, () => p.Override(v));
    }

    private static void ColorField(VolumeComponent owner, ColorParameter p, string label)
    {
        EditorGUI.BeginChangeCheck();
        var v = EditorGUILayout.ColorField(label, p.value);
        if (EditorGUI.EndChangeCheck()) Set(owner, () => p.Override(v));
    }

    private static void Set(VolumeComponent owner, System.Action change)
    {
        Undo.RecordObject(owner, "Reglage d'atmosphere");
        change();
        EditorUtility.SetDirty(owner);
        SceneView.RepaintAll();
    }

    // ---- Utilitaires ----

    private static IEnumerable<Volume> FindVolumes() =>
        Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);

    private static Bounds? GetSelectionBounds()
    {
        Bounds? result = null;
        foreach (var go in Selection.gameObjects)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (result == null) result = r.bounds;
                else { var b = result.Value; b.Encapsulate(r.bounds); result = b; }
            }
        }
        return result;
    }

    private static void EnsureFolderExists(string assetsPath)
    {
        if (AssetDatabase.IsValidFolder(assetsPath)) return;
        string parent = Path.GetDirectoryName(assetsPath).Replace('\\', '/');
        EnsureFolderExists(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(assetsPath));
    }
}
