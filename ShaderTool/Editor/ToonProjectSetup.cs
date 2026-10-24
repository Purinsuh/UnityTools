using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Onglet "Préparer le projet" : ce qu'il faut régler une fois dans un nouveau projet (et dans chaque nouvelle scène)
// pour que les shaders de l'outil s'affichent comme prévu. Chaque étape dit si elle est faite ; "Tout préparer"
// fait celles qui manquent. Sans cet onglet, ces oublis ne se voient qu'après coup dans "Vérifier la scène".
public static class ToonProjectSetup
{
    private class Step
    {
        public string title;
        public string explanation;
        public Func<bool> isDone;
        public Action apply;
        public bool optional;   // pas compris dans "Tout préparer"
        public bool done;       // dernier etat calcule
    }

    private const string PostProcessName = "Post-process";
    private static string RootPrefKey => "ShaderTools_ProjectRoot_" + Application.dataPath;
    private static string Root
    {
        get => EditorPrefs.GetString(RootPrefKey, "Assets/_Projet");
        set => EditorPrefs.SetString(RootPrefKey, value);
    }
    private static readonly string[] Folders =
        { "Art/Decor", "Art/Objets", "Art/Personnages", "Art/VFX", "Audio", "Prefabs", "Scenes", "Scripts", "UI" };

    private static List<Step> projectSteps, sceneSteps;
    private static double lastCheck;
    private static Vector2 scroll;
    private static bool showRules = true;

    public static void DrawTab(string profileFolder, bool showHelp)
    {
        if (showHelp)
            EditorGUILayout.HelpBox(
                "À faire une fois dans un nouveau projet, puis pour chaque nouvelle scène.\n" +
                "1. « Tout préparer » dans chaque partie (ou une étape à la fois).\n" +
                "2. Range les fichiers dans les dossiers créés : leurs réglages d'import se font tout seuls.\n" +
                "Une coche verte = déjà bon. Rien n'est modifié tant que tu ne cliques pas.",
                MessageType.Info);

        if (projectSteps == null) CreateSteps(profileFolder);
        // Etat recalcule pendant la passe Layout, au plus deux fois par seconde (la fenetre se redessine au moindre mouvement)
        if (Event.current.type == EventType.Layout && EditorApplication.timeSinceStartup - lastCheck > 0.5)
        {
            lastCheck = EditorApplication.timeSinceStartup;
            foreach (var step in projectSteps.Concat(sceneSteps)) step.done = step.isDone();
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Projet (une seule fois)", EditorStyles.boldLabel);
        Root = EditorGUILayout.TextField(new GUIContent("Dossier du jeu", "Les dossiers du projet sont créés dedans"), Root);
        DrawSteps(projectSteps, "Tout préparer dans le projet", "Préparer le projet");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scène ouverte : " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, EditorStyles.boldLabel);
        DrawSteps(sceneSteps, "Tout préparer dans la scène", "Préparer la scène");
        EditorGUILayout.LabelField("Ensuite : l'ambiance de la scène dans l'onglet « 3 Atmosphère ».", EditorStyles.miniLabel);

        EditorGUILayout.Space();
        showRules = EditorGUILayout.Foldout(showRules, "Règles d'import par dossier", true, EditorStyles.foldoutHeader);
        if (showRules) ToonImportRules.DrawRules();
        EditorGUILayout.EndScrollView();
    }

    private static void DrawSteps(List<Step> steps, string allLabel, string undoName)
    {
        foreach (var step in steps)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.BeginHorizontal();
                var previous = GUI.contentColor;
                GUI.contentColor = step.done ? new Color(0.4f, 0.85f, 0.4f) : previous;
                EditorGUILayout.LabelField((step.done ? "✔  " : "○  ") + step.title + (step.optional ? " (au choix)" : ""), EditorStyles.boldLabel);
                GUI.contentColor = previous;
                if (!step.done && step.apply != null && GUILayout.Button("Faire", GUILayout.Width(60)))
                    Run(new[] { step }, undoName + " : " + step.title);
                EditorGUILayout.EndHorizontal();
                if (!step.done) EditorGUILayout.LabelField(step.explanation, EditorStyles.wordWrappedMiniLabel);
            }
        }
        var todo = steps.Where(s => !s.done && !s.optional && s.apply != null).ToList();
        using (new EditorGUI.DisabledScope(todo.Count == 0))
            if (GUILayout.Button(todo.Count == 0 ? "Tout est prêt" : allLabel + " (" + todo.Count + ")", GUILayout.Height(28)))
                Run(todo, undoName);
    }

    private static void Run(IEnumerable<Step> steps, string undoName)
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(undoName);
        foreach (var step in steps) step.apply();
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        AssetDatabase.SaveAssets();
        SceneView.RepaintAll();
        lastCheck = 0;
        GUIUtility.ExitGUI();
    }

    // Utilisable par script (tests) : applique les etapes manquantes, renvoie leurs titres
    public static List<string> PrepareAll(string profileFolder, bool scene)
    {
        CreateSteps(profileFolder);
        var todo = (scene ? sceneSteps : projectSteps).Where(s => !s.optional && s.apply != null && !s.isDone()).ToList();
        foreach (var step in todo) step.apply();
        AssetDatabase.SaveAssets();
        return todo.Select(s => s.title).ToList();
    }

    public static IEnumerable<string> Report(string profileFolder)
    {
        CreateSteps(profileFolder);
        return projectSteps.Concat(sceneSteps).Select(s => (s.isDone() ? "OK   " : "A FAIRE ") + s.title);
    }

    // =====================================================================
    // Etapes
    // =====================================================================

    private static void CreateSteps(string profileFolder)
    {
        projectSteps = new List<Step>
        {
            new Step
            {
                title = "Rendu URP",
                explanation = "Le projet n'utilise pas URP : crée-le avec le modèle « Universal 3D » dans Unity Hub (les shaders de l'outil sont faits pour URP).",
                isDone = () => GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset,
            },
            new Step
            {
                title = "Couleurs en espace linéaire",
                explanation = "Lumières, bloom et dégradés justes (l'espace « Gamma » délave les ombres). Réimporte les textures : quelques minutes sur un gros projet.",
                isDone = () => PlayerSettings.colorSpace == ColorSpace.Linear,
                apply = () => PlayerSettings.colorSpace = ColorSpace.Linear,
            },
            new Step
            {
                title = "Profondeur et HDR dans les réglages URP",
                explanation = "Profondeur : nécessaire au liseré « Depth », au contour et à l'atmosphère. HDR : nécessaire au bloom des émissifs.",
                isDone = () => PipelineAssets().All(a => a.supportsCameraDepthTexture && a.supportsHDR),
                apply = () =>
                {
                    foreach (var asset in PipelineAssets())
                    {
                        Undo.RecordObject(asset, "Profondeur et HDR");
                        asset.supportsCameraDepthTexture = true;
                        asset.supportsHDR = true;
                        EditorUtility.SetDirty(asset);
                    }
                },
            },
            new Step
            {
                title = "Effet d'atmosphère installé",
                explanation = "Ajoute l'effet d'atmosphère (voile coloré, brume, teinte de nuit) au rendu URP du projet.",
                isDone = ToonAtmosphereTools.IsInstalled,
                apply = ToonAtmosphereTools.Install,
            },
            new Step
            {
                title = "Dossiers du projet et règles d'import",
                explanation = "Crée Art/Decor, Art/Objets, Art/Personnages, Art/VFX, Audio, Prefabs, Scenes, Scripts et UI dans le dossier du jeu, " +
                    "avec des règles d'import (voir plus bas) : taille des textures, LOD du décor, normales lissées des personnages, sprites de l'interface.",
                isDone = () => Folders.All(f => AssetDatabase.IsValidFolder(Root + "/" + f)) && ToonImportRules.Find(Root + "/Art/Decor/x") != null,
                apply = () =>
                {
                    foreach (var folder in Folders) EnsureFolder(Root + "/" + folder);
                    ToonImportRules.AddDefaultRules(Root);
                },
            },
            new Step
            {
                title = "Fichier .gitignore",
                explanation = "Si vous partagez le projet avec Git : évite d'envoyer les dossiers que Unity recrée tout seul (Library, Temp, Logs...), lourds et sources de conflits.",
                optional = true,
                isDone = () => File.Exists(".gitignore"),
                apply = () => File.WriteAllText(".gitignore", GitIgnore),
            },
        };

        sceneSteps = new List<Step>
        {
            new Step
            {
                title = "Post-process toon",
                explanation = "Volume global avec le rendu de l'outil : tons « ACES », bloom sur les émissifs, contraste et couleurs un peu relevés. " +
                    "Un seul réglage partagé par toutes les scènes (" + profileFolder + "/PostProcess_Toon.asset).",
                isDone = HasToonPostProcess,
                apply = () => AddPostProcess(profileFolder),
            },
            new Step
            {
                title = "Caméra : post-process et anti-aliasing",
                explanation = "Sans post-process, la caméra n'affiche ni bloom ni atmosphère ; sans anti-aliasing, les bords et les contours crénelent.",
                isDone = () => BaseCameras().All(c => c.TryGetComponent<UniversalAdditionalCameraData>(out var d)
                    && d.renderPostProcessing && d.antialiasing != AntialiasingMode.None),
                apply = () =>
                {
                    foreach (var camera in BaseCameras())
                    {
                        // Camera sans reglages URP (creee par script...) : ses valeurs par defaut sont sans post-process
                        if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var data))
                            data = Undo.AddComponent<UniversalAdditionalCameraData>(camera.gameObject);
                        Undo.RecordObject(data, "Caméra");
                        data.renderPostProcessing = true;
                        if (data.antialiasing == AntialiasingMode.None)
                        {
                            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                            data.antialiasingQuality = AntialiasingQuality.High;
                        }
                        EditorUtility.SetDirty(data);
                    }
                },
            },
            new Step
            {
                title = "Soleil avec ombres douces",
                explanation = "La lumière principale de la scène (soleil, ou lumière de la pièce) projette des ombres : sans elles, les objets « flottent ».",
                isDone = () => Sun() != null && Sun().shadows != LightShadows.None,
                apply = () =>
                {
                    var sun = Sun();
                    if (sun == null)
                    {
                        var go = new GameObject("Soleil");
                        Undo.RegisterCreatedObjectUndo(go, "Soleil");
                        go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                        sun = go.AddComponent<Light>();
                        sun.type = LightType.Directional;
                        RenderSettings.sun = sun;
                    }
                    Undo.RecordObject(sun, "Soleil");
                    sun.shadows = LightShadows.Soft;
                    EditorUtility.SetDirty(sun);
                },
            },
        };
    }

    private static List<UniversalRenderPipelineAsset> PipelineAssets()
    {
        var assets = new List<UniversalRenderPipelineAsset>();
        if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset main) assets.Add(main);
        for (int i = 0; i < QualitySettings.count; i++)
            if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset q && !assets.Contains(q)) assets.Add(q);
        return assets;
    }

    // Plusieurs scenes ouvertes : seule la scene active compte (c'est la que les objets sont crees)
    private static bool InActiveScene(Component c) => c.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene();

    // Cameras du jeu dans la scene. Les cameras superposees (interface...) suivent leur camera de base.
    private static List<Camera> BaseCameras() =>
        Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(c => c.cameraType == CameraType.Game && InActiveScene(c)
                && !(c.TryGetComponent<UniversalAdditionalCameraData>(out var d) && d.renderType == CameraRenderType.Overlay)).ToList();

    private static Light Sun() =>
        RenderSettings.sun != null && InActiveScene(RenderSettings.sun) ? RenderSettings.sun
            : Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional && InActiveScene(l));

    private static bool HasToonPostProcess() =>
        Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).Any(v => v.isGlobal && InActiveScene(v) && v.sharedProfile != null
            && v.sharedProfile.TryGet(out Tonemapping t) && t.active && t.mode.overrideState && t.mode.value != TonemappingMode.None);

    private static void AddPostProcess(string profileFolder)
    {
        string path = profileFolder + "/PostProcess_Toon.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            EnsureFolder(profileFolder);
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            // Le rendu regle sur la scene de test : ACES, bloom reserve aux emissifs (seuil au-dessus du blanc),
            // un peu plus de contraste et de couleur pour compenser le tassement des tons ACES
            var tonemapping = Add<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.ACES);
            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(2.1f);
            bloom.intensity.Override(0.7f);
            bloom.scatter.Override(0.5f);
            var color = Add<ColorAdjustments>(profile);
            color.postExposure.Override(0.3f);
            color.contrast.Override(6f);
            color.saturation.Override(12f);
            AssetDatabase.SaveAssets();
        }
        var go = new GameObject(PostProcessName);
        Undo.RegisterCreatedObjectUndo(go, "Post-process toon");
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 0; // l'atmosphere (priorite 10) et ses zones passent par-dessus
        volume.sharedProfile = profile;
        Selection.activeGameObject = go;
    }

    private static T Add<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var component = profile.Add<T>(false);
        component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    private static void EnsureFolder(string path)
    {
        path = path.TrimEnd('/');
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // Modele GitHub pour Unity, raccourci
    private const string GitIgnore =
        "# Recrees par Unity : ne pas partager\n" +
        "/[Ll]ibrary/\n/[Tt]emp/\n/[Oo]bj/\n/[Bb]uild/\n/[Bb]uilds/\n/[Ll]ogs/\n/[Uu]ser[Ss]ettings/\n/[Mm]emoryCaptures/\n/[Rr]ecordings/\n\n" +
        "# Fichiers des editeurs de code\n" +
        ".vs/\n.vscode/\n.idea/\n*.csproj\n*.sln\n*.suo\n*.user\n*.pidb\n*.pdb\n*.mdb\n*.opendb\n*.VC.db\n\n" +
        "# Builds et paquets\n" +
        "*.apk\n*.aab\n*.unitypackage\ncrashlytics-build.properties\n\n" +
        "# Systeme\n.DS_Store\nThumbs.db\n";
}
