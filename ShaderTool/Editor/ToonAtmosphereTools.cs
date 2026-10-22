using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Onglet "Atmosphère" de l'outil : ambiances prêtes à l'emploi (voile ToonAtmosphere + balance des blancs + vignette,
// + exposition / saturation pour les ambiances sombres) posées en un clic, pour toute la scène (Volume global)
// ou pour une pièce (Volume local autour de la sélection). Unity mélange les ambiances quand la caméra passe d'une zone à l'autre.
public static class ToonAtmosphereTools
{
    private const int GlobalPriority = 10;
    private const int ZonePriority = 20;
    private const string GlobalName = "Atmosphere (scene)";
    private const string SettingsFoldoutKey = "ShaderTools_AtmoSettingsOpen";

    public struct Preset
    {
        public string family, name, description;
        public Color near, far, sun;
        public float colorDistance, start, density, heightBase, heightFalloff, sky, sunGlow;
        public float intensity, temperature, tint, vignette;
        // Ambiances sombres seulement : 0 = la valeur du post-process de la scène est gardée
        public float exposure, saturation;
    }

    public static readonly string[] Families = { "Extérieur", "Intérieur", "Sombre / flippant", "Autre" };

    // Extérieur : voile lointain, brume au sol, halo du soleil. Intérieur : voile court et régulier, teinte de la pièce.
    // Sombre : voile épais et foncé, image assombrie et désaturée, forte vignette.
    public static readonly Preset[] Presets =
    {
        new Preset { family = "Extérieur", name = "Jour clair", description = "Lointains bleutés, léger halo du soleil.",
            intensity = 1f, near = new Color(0.80f, 0.88f, 1f), far = new Color(0.62f, 0.76f, 0.95f), colorDistance = 150f,
            start = 15f, density = 0.012f, heightFalloff = 0.02f, sky = 0.35f, sun = new Color(1f, 0.9f, 0.75f), sunGlow = 0.35f, vignette = 0.15f },
        new Preset { family = "Extérieur", name = "Matin brumeux", description = "Brume blanche au sol, horizon effacé.",
            intensity = 1f, near = new Color(0.85f, 0.87f, 0.9f), far = new Color(0.78f, 0.82f, 0.88f), colorDistance = 60f,
            start = 2f, density = 0.06f, heightFalloff = 0.15f, sky = 0.7f, sun = new Color(1f, 0.95f, 0.85f), sunGlow = 0.25f, temperature = -5f, vignette = 0.2f },
        new Preset { family = "Extérieur", name = "Coucher de soleil", description = "Lointains orangés, grand halo vers le soleil.",
            intensity = 1f, near = new Color(1f, 0.78f, 0.65f), far = new Color(0.95f, 0.55f, 0.5f), colorDistance = 120f,
            start = 10f, density = 0.02f, heightFalloff = 0.03f, sky = 0.5f, sun = new Color(1f, 0.6f, 0.3f), sunGlow = 0.7f, temperature = 20f, tint = 5f, vignette = 0.25f },
        new Preset { family = "Extérieur", name = "Nuit", description = "Voile bleu nuit, image plus froide.",
            intensity = 1f, near = new Color(0.15f, 0.2f, 0.35f), far = new Color(0.1f, 0.14f, 0.28f), colorDistance = 80f,
            start = 8f, density = 0.03f, heightFalloff = 0.05f, sky = 0.4f, sun = new Color(0.6f, 0.7f, 1f), sunGlow = 0.2f, temperature = -15f, vignette = 0.3f },
        new Preset { family = "Intérieur", name = "Chaleureux", description = "Pièce habitée : lumière chaude, voile très léger.",
            intensity = 1f, near = new Color(1f, 0.9f, 0.78f), far = new Color(0.95f, 0.8f, 0.65f), colorDistance = 25f,
            start = 4f, density = 0.02f, sun = new Color(1f, 0.85f, 0.6f), sunGlow = 0.15f, temperature = 25f, tint = 3f, vignette = 0.25f },
        new Preset { family = "Intérieur", name = "Frais et lumineux", description = "Pièce moderne : lumière froide et nette.",
            intensity = 1f, near = new Color(0.92f, 0.96f, 1f), far = new Color(0.85f, 0.92f, 1f), colorDistance = 30f,
            start = 5f, density = 0.012f, sun = new Color(1f, 0.95f, 0.9f), sunGlow = 0.1f, temperature = -10f, vignette = 0.1f },
        new Preset { family = "Intérieur", name = "Mystérieux (cave)", description = "Voile bleu-vert épais, coins sombres.",
            intensity = 1f, near = new Color(0.25f, 0.35f, 0.4f), far = new Color(0.12f, 0.2f, 0.25f), colorDistance = 15f,
            start = 1f, density = 0.12f, sun = Color.white, temperature = -10f, tint = -5f, vignette = 0.4f },
        new Preset { family = "Sombre / flippant", name = "Brouillard angoissant", description = "Brouillard noir et épais au sol, couleurs éteintes : on ne voit pas ce qui arrive.",
            intensity = 1f, near = new Color(0.18f, 0.2f, 0.22f), far = new Color(0.05f, 0.06f, 0.07f), colorDistance = 20f,
            start = 1f, density = 0.15f, heightFalloff = 0.2f, sky = 0.8f, sun = Color.white, temperature = -15f, tint = -5f, vignette = 0.5f,
            exposure = -0.6f, saturation = -40f },
        new Preset { family = "Sombre / flippant", name = "Alarme rouge", description = "Voile rouge sang, comme sous un éclairage de secours.",
            intensity = 1f, near = new Color(0.6f, 0.06f, 0.05f), far = new Color(0.2f, 0.02f, 0.03f), colorDistance = 12f,
            start = 0.5f, density = 0.22f, sky = 0.6f, sun = new Color(1f, 0.2f, 0.1f), sunGlow = 0.3f, temperature = 20f, tint = 15f, vignette = 0.45f,
            exposure = -0.4f, saturation = -10f },
        new Preset { family = "Sombre / flippant", name = "Lieu abandonné", description = "Poussière dans l'air, couleurs passées, ambiance de vieux sépia.",
            intensity = 1f, near = new Color(0.45f, 0.4f, 0.32f), far = new Color(0.25f, 0.22f, 0.17f), colorDistance = 20f,
            start = 1f, density = 0.1f, sky = 0.5f, sun = new Color(1f, 0.85f, 0.6f), sunGlow = 0.4f, temperature = 35f, tint = 5f, vignette = 0.4f,
            exposure = -0.3f, saturation = -50f },
        new Preset { family = "Sombre / flippant", name = "Nuit sans lune", description = "Presque noir au-delà de quelques mètres : seules les lumières proches comptent.",
            intensity = 1f, near = new Color(0.03f, 0.04f, 0.07f), far = new Color(0.01f, 0.015f, 0.03f), colorDistance = 30f,
            start = 3f, density = 0.1f, heightFalloff = 0.05f, sky = 0.9f, sun = Color.white, temperature = -25f, vignette = 0.55f,
            exposure = -0.8f, saturation = -30f },
        new Preset { family = "Autre", name = "Aucune", description = "Annule le voile (utile pour une pièce dans une scène brumeuse).",
            intensity = 0f, near = Color.white, far = Color.white, colorDistance = 100f, sun = Color.white },
    };

    private static int target; // 0 = toute la scène, 1 = une pièce

    public static void DrawTab(string folder, bool showHelp)
    {
        if (showHelp)
            EditorGUILayout.HelpBox(
                "Donne une ambiance à la scène : voile coloré au loin, teinte chaude ou froide, coins assombris.\n" +
                "1. Choisis où l'appliquer : toute la scène, ou une pièce (sélectionne d'abord ses murs et son sol).\n" +
                "2. Clique sur une ambiance. Tu peux en changer à tout moment, Ctrl+Z annule.\n" +
                "3. Si besoin, ajuste-la dans « Réglages fins ».",
                MessageType.Info);

        if (!DrawInstallation()) return;

        // 1. Où appliquer
        EditorGUILayout.LabelField("1. Appliquer à", EditorStyles.boldLabel);
        target = GUILayout.Toolbar(target, new[] { "Toute la scène", "Une pièce (sélection)" }, GUILayout.Height(24));
        var bounds = GetSelectionBounds();
        if (target == 1)
            EditorGUILayout.HelpBox(bounds.HasValue
                ? "La pièce prendra la taille des " + Selection.gameObjects.Length + " objet(s) sélectionné(s)."
                : "Sélectionne dans la Hierarchy les objets de la pièce (murs, sol, plafond).", bounds.HasValue ? MessageType.None : MessageType.Warning);

        // 2. Choisir l'ambiance : boutons par famille, avec les couleurs du voile (proche / lointaine)
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("2. Choisir une ambiance", EditorStyles.boldLabel);
        bool canApply = target == 0 || bounds.HasValue;
        using (new EditorGUI.DisabledScope(!canApply))
        {
            foreach (var family in Families)
            {
                var presets = Presets.Where(p => p.family == family).ToList();
                EditorGUILayout.LabelField(family, EditorStyles.miniBoldLabel);
                for (int i = 0; i < presets.Count; i += 2)
                {
                    EditorGUILayout.BeginHorizontal();
                    for (int j = i; j < i + 2; j++)
                    {
                        if (j < presets.Count) { if (PresetButton(presets[j])) Apply(presets[j], bounds, folder); }
                        else GUILayout.FlexibleSpace();
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        // Ambiances présentes dans la scène, et réglages fins de celle qui est sélectionnée (sinon la globale)
        var volumes = FindVolumes().Where(v => v.sharedProfile != null && v.sharedProfile.Has<ToonAtmosphere>()).ToList();
        if (volumes.Count == 0) return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Ambiances déjà dans la scène", EditorStyles.boldLabel);
        var global = volumes.FirstOrDefault(v => v.isGlobal && v.name == GlobalName);
        var selected = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Volume>() : null;
        if (selected == null || !volumes.Contains(selected)) selected = global ?? volumes[0];
        foreach (var v in volumes)
        {
            EditorGUILayout.BeginHorizontal();
            var style = v == selected ? EditorStyles.boldLabel : EditorStyles.label;
            if (GUILayout.Button(new GUIContent((v.isGlobal ? "Scène : " : "Pièce : ") + v.name, "Cliquer pour la régler"), style))
                Selection.activeGameObject = v.gameObject;
            if (GUILayout.Button(new GUIContent("Supprimer", "Retire cette ambiance de la scène (Ctrl+Z pour annuler)"), GUILayout.Width(80)))
            {
                Undo.DestroyObjectImmediate(v.gameObject);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();
        bool open = EditorPrefs.GetBool(SettingsFoldoutKey, false);
        bool newOpen = EditorGUILayout.Foldout(open, "3. Réglages fins : " + selected.name, true, EditorStyles.foldoutHeader);
        if (newOpen != open) EditorPrefs.SetBool(SettingsFoldoutKey, newOpen);
        if (newOpen) DrawSettings(selected);
    }

    // Bouton d'ambiance : pastille des couleurs du voile + nom, description en infobulle
    private static GUIStyle presetButtonStyle; // cree une fois (la fenetre se redessine souvent)

    private static bool PresetButton(Preset p)
    {
        if (presetButtonStyle == null)
            presetButtonStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(30, 4, 2, 2) };
        bool clicked = GUILayout.Button(new GUIContent(p.name, p.description), presetButtonStyle, GUILayout.Height(26), GUILayout.MinWidth(120));
        if (Event.current.type == EventType.Repaint)
        {
            var r = GUILayoutUtility.GetLastRect();
            var swatch = new Rect(r.x + 6, r.y + 5, 18, r.height - 10);
            Color near = p.intensity > 0f ? p.near : new Color(0.5f, 0.5f, 0.5f), far = p.intensity > 0f ? p.far : new Color(0.5f, 0.5f, 0.5f);
            EditorGUI.DrawRect(new Rect(swatch.x, swatch.y, swatch.width / 2, swatch.height), near);
            EditorGUI.DrawRect(new Rect(swatch.x + swatch.width / 2, swatch.y, swatch.width / 2, swatch.height), far);
        }
        return clicked;
    }

    private static void Apply(Preset preset, Bounds? bounds, string folder)
    {
        if (target == 0) ApplyGlobal(preset, folder);
        else if (bounds.HasValue) CreateZone(preset, bounds.Value, folder);
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
        EditorGUILayout.HelpBox("Première utilisation dans ce projet : il faut d'abord ajouter l'effet d'atmosphère au rendu URP (" +
            string.Join(", ", missing.Select(d => d.name)) + "). C'est à faire une seule fois.", MessageType.Warning);
        if (GUILayout.Button("Installer l'effet d'atmosphère", GUILayout.Height(30)))
            Install();
        return false;
    }

    // Ajoute ToonAtmosphereFeature à chaque renderer URP qui ne l'a pas (comme "Add Renderer Feature")
    public static void Install()
    {
        foreach (var data in GetMissingRenderers())
        {
            var feature = ScriptableObject.CreateInstance<ToonAtmosphereFeature>();
            feature.name = "Toon Atmosphere";
            Undo.RegisterCreatedObjectUndo(feature, "Installer l'atmosphère");
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

    // ---- Création des ambiances ----

    public static Volume ApplyGlobal(Preset preset, string folder)
    {
        var volume = FindVolumes().FirstOrDefault(v => v.isGlobal && v.name == GlobalName);
        if (volume == null)
        {
            var go = new GameObject(GlobalName);
            Undo.RegisterCreatedObjectUndo(go, "Ambiance de la scène");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = GlobalPriority;
        }
        Undo.RecordObject(volume, "Ambiance de la scène");
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        // Le profil de la scène est réutilisé (les autres scènes ne sont pas touchées)
        if (volume.sharedProfile == null || !AssetDatabase.GetAssetPath(volume.sharedProfile).StartsWith(folder))
            volume.sharedProfile = CreateProfile(folder + "/Atmosphere_" + scene + ".asset");
        ApplyPreset(volume.sharedProfile, preset);
        Selection.activeGameObject = volume.gameObject;
        return volume;
    }

    public static void CreateZone(Preset preset, Bounds bounds, string folder)
    {
        var go = new GameObject("Atmosphere - " + preset.name);
        Undo.RegisterCreatedObjectUndo(go, "Zone d'atmosphère");
        go.transform.position = bounds.center;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = bounds.size + Vector3.one * 0.2f;
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = false;
        volume.priority = ZonePriority;
        volume.blendDistance = 1.5f; // transition douce en entrant dans la pièce
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
        AddComponent<ColorAdjustments>(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    private static T AddComponent<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var component = profile.Add<T>(false);
        component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    public static void ApplyPreset(VolumeProfile profile, Preset p)
    {
        if (!profile.TryGet(out ToonAtmosphere a)) a = AddComponent<ToonAtmosphere>(profile);
        Undo.RecordObject(a, "Préréglage d'atmosphère");
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

        // Teinte de la pièce et vignette : seules ces valeurs sont surchargées, le reste du post-process ne bouge pas
        if (!profile.TryGet(out WhiteBalance wb)) wb = AddComponent<WhiteBalance>(profile);
        Undo.RecordObject(wb, "Préréglage d'atmosphère");
        wb.temperature.Override(p.temperature);
        wb.tint.Override(p.tint);
        EditorUtility.SetDirty(wb);

        if (!profile.TryGet(out Vignette vignette)) vignette = AddComponent<Vignette>(profile);
        Undo.RecordObject(vignette, "Préréglage d'atmosphère");
        vignette.intensity.Override(p.vignette);
        vignette.smoothness.Override(0.4f);
        EditorUtility.SetDirty(vignette);

        // Exposition / saturation : surchargées seulement par les ambiances sombres, sinon celles de la scène sont gardées
        if (!profile.TryGet(out ColorAdjustments grading)) grading = AddComponent<ColorAdjustments>(profile);
        Undo.RecordObject(grading, "Préréglage d'atmosphère");
        grading.postExposure.value = p.exposure;
        grading.postExposure.overrideState = p.exposure != 0f;
        grading.saturation.value = p.saturation;
        grading.saturation.overrideState = p.saturation != 0f;
        EditorUtility.SetDirty(grading);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        SceneView.RepaintAll();
    }

    // ---- Réglages fins de l'ambiance sélectionnée ----

    private static void DrawSettings(Volume volume)
    {
        var profile = volume.sharedProfile;
        if (!profile.TryGet(out ToonAtmosphere a)) return;
        EditorGUI.indentLevel++;

        EditorGUILayout.LabelField("Voile", EditorStyles.miniBoldLabel);
        Slider(a, a.intensity, "Intensité (0 = aucun voile)", 0f, 1f);
        ColorField(a, a.nearColor, "Couleur proche");
        ColorField(a, a.farColor, "Couleur au loin");
        Float(a, a.startDistance, "Commence à (m)");
        Slider(a, a.density, "Épaisseur", 0f, 0.5f);
        Float(a, a.colorDistance, "Couleur au loin atteinte à (m)");

        EditorGUILayout.LabelField("Extérieur", EditorStyles.miniBoldLabel);
        Slider(a, a.heightFalloff, "Brume au sol (0 = partout pareil)", 0f, 1f);
        Float(a, a.heightBase, "Hauteur du sol de la brume (m)");
        Slider(a, a.skyAmount, "Voile sur l'horizon", 0f, 1f);
        ColorField(a, a.sunColor, "Couleur du halo du soleil");
        Slider(a, a.sunGlow, "Halo du soleil", 0f, 1f);
        ColorField(a, a.nightTint, "Teinte du voile la nuit");

        EditorGUILayout.LabelField("Image", EditorStyles.miniBoldLabel);
        if (profile.TryGet(out WhiteBalance wb))
        {
            Slider(wb, wb.temperature, "Froid ← → chaud", -100f, 100f);
            Slider(wb, wb.tint, "Vert ← → magenta", -100f, 100f);
        }
        if (profile.TryGet(out Vignette vignette))
            Slider(vignette, vignette.intensity, "Coins assombris (vignette)", 0f, 1f);
        if (profile.TryGet(out ColorAdjustments grading))
        {
            Slider(grading, grading.postExposure, "Luminosité (0 = celle de la scène)", -3f, 1f);
            Slider(grading, grading.saturation, "Couleurs (0 = celles de la scène)", -100f, 50f);
        }
        if (!volume.isGlobal)
        {
            EditorGUI.BeginChangeCheck();
            float blend = EditorGUILayout.FloatField(new GUIContent("Fondu en entrant (m)", "Distance sur laquelle l'ambiance de la pièce se mélange à celle de la scène"), volume.blendDistance);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(volume, "Fondu de la zone");
                volume.blendDistance = Mathf.Max(0f, blend);
            }
        }
        EditorGUI.indentLevel--;
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
        Undo.RecordObject(owner, "Réglage d'atmosphère");
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
