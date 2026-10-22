using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Onglet "Vérifier la scène" : une analyse à la demande (pas en continu, pour les grosses scènes) qui liste
// ce qui s'affiche mal ou coûte cher, avec pour chaque point les objets concernés et une correction en un clic.
public static class ToonSceneAudit
{
    private enum Severity { Error, Performance, Advice }

    private class Issue
    {
        public Severity severity;
        public string title;
        public string explanation;
        public List<Object> objects = new List<Object>();
        public string fixLabel;
        public Action fix;
    }

    // Seuils : volontairement simples, expliqués dans les messages
    private const float LargeObjectSize = 4f;    // m : au-delà, le liseré ne se voit presque plus
    private const float TinyObjectSize = 0.2f;   // m : en dessous, l'ombre portée ne fait que quelques pixels
    private const int MaxTextureSize = 2048;     // px
    private const int MaxShadowedLights = 4;     // lampes avec ombres en temps réel

    private static List<Issue> issues;
    private static DateTime lastScan;
    private static string lastScene;
    private static Vector2 scroll;

    private static readonly string[] BuiltInShaderPrefixes = { "Standard", "Legacy Shaders/", "Mobile/", "Nature/", "Autodesk Interactive", "Particles/Standard" };
    private static readonly string[] ToolShaders = { "Custom/ToonAdvanced", "Custom/CelShading", "Custom/ToonOutline", "Custom/MangaDotsOutline", "Custom/MangaDotsOutlineTextured", "Custom/ToonSky" };

    public static void DrawTab(bool showHelp)
    {
        if (showHelp)
            EditorGUILayout.HelpBox(
                "Repère ce qui s'affiche mal ou ralentit le jeu dans la scène ouverte.\n" +
                "1. Clique sur « Analyser la scène » (à refaire après des changements).\n" +
                "2. « Voir » sélectionne les objets concernés, « Corriger » règle le problème (Ctrl+Z annule).\n" +
                "Rouge = à corriger, orange = coûte cher en performances, bleu = conseil.",
                MessageType.Info);

        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (GUILayout.Button("Analyser la scène", GUILayout.Height(32)))
            Scan();
        if (issues == null) return;
        if (lastScene != scene)
        {
            EditorGUILayout.HelpBox("La scène a changé depuis la dernière analyse : relance l'analyse.", MessageType.Warning);
            return;
        }

        int errors = issues.Count(i => i.severity == Severity.Error);
        int perf = issues.Count(i => i.severity == Severity.Performance);
        int advice = issues.Count(i => i.severity == Severity.Advice);
        EditorGUILayout.LabelField("Analyse de " + lastScan.ToString("HH:mm") + " : " +
            (issues.Count == 0 ? "tout est bon !" : errors + " à corriger, " + perf + " performances, " + advice + " conseils"), EditorStyles.boldLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var issue in issues.OrderBy(i => i.severity).ToList())
            DrawIssue(issue);
        EditorGUILayout.EndScrollView();
    }

    private static void DrawIssue(Issue issue)
    {
        string icon = issue.severity == Severity.Error ? "console.erroricon.sml"
            : issue.severity == Severity.Performance ? "console.warnicon.sml" : "console.infoicon.sml";
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            var title = EditorGUIUtility.IconContent(icon);
            title.text = " " + issue.title + (issue.objects.Count > 0 ? " (" + issue.objects.Count + ")" : "");
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(issue.explanation, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (issue.objects.Count > 0 && GUILayout.Button(new GUIContent("Voir", "Sélectionne les objets concernés"), GUILayout.Width(70)))
            {
                Selection.objects = issue.objects.Where(o => o != null).ToArray();
                if (Selection.activeGameObject != null) EditorGUIUtility.PingObject(Selection.activeGameObject);
            }
            if (issue.fix != null && GUILayout.Button(issue.fixLabel, GUILayout.MinWidth(90)))
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Vérifier la scène : " + issue.title);
                issue.fix();
                Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                AssetDatabase.SaveAssets();
                SceneView.RepaintAll();
                Scan();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    // =====================================================================
    // Analyse
    // =====================================================================

    public static void Scan()
    {
        issues = new List<Issue>();
        lastScan = DateTime.Now;
        lastScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
        FindCharacters();

        CheckPinkMaterials(renderers);
        CheckMissingScripts();
        CheckRimOnLargeObjects(renderers);
        CheckOutlineOnDecor(renderers);
        CheckTinyShadowCasters(renderers);
        CheckHeavyTextures(renderers);
        CheckShadowedLights();
        CheckEmissionBelowBloom(renderers);
        CheckUnconvertedMaterials(renderers);
        CheckCameraAntialiasing();
        CheckAtmosphereInstalled();
        CheckDepthTexture(renderers);
        CheckIdleOutlinePass(renderers);

        issues.RemoveAll(i => i.objects.Count == 0 && i.fix == null);
    }

    // Résumé texte de la dernière analyse (une ligne par point), pour un rapport ou un test
    public static IEnumerable<string> Report() =>
        (issues ?? new List<Issue>()).OrderBy(i => i.severity).Select(i => i.severity + " | " + i.title + " | " + i.objects.Count +
            " objet(s)" + (i.fix != null ? " | correction : " + i.fixLabel : "") + " | " +
            string.Join(", ", i.objects.Where(o => o != null).Take(6).Select(o => o.name)));

    // Applique la correction du point dont le titre contient le texte donné (même effet que le bouton « Corriger »)
    public static bool Fix(string titlePart)
    {
        var issue = issues?.FirstOrDefault(i => i.fix != null && i.title.Contains(titlePart));
        if (issue == null) return false;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Vérifier la scène : " + issue.title);
        issue.fix();
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        return true;
    }

    private static void Add(Issue issue)
    {
        if (issue.objects.Count > 0) issues.Add(issue);
    }

    // Materials manquants ou shaders Built-in : s'affichent en rose dans URP
    private static void CheckPinkMaterials(List<Renderer> renderers)
    {
        var issue = new Issue
        {
            severity = Severity.Error,
            title = "Objets roses (material manquant ou incompatible)",
            explanation = "Ces objets n'ont pas de material, ou utilisent un shader de l'ancien rendu (Standard, Legacy...) que URP ne sait pas afficher. " +
                          "Convertis-les dans l'onglet « 1 Convertir » ou donne-leur un material."
        };
        foreach (var r in renderers)
        {
            // Particules comprises : leurs shaders Built-in (Particles/Standard...) sont roses aussi
            bool pink = r.sharedMaterials.Any(m => m == null || m.shader == null || !m.shader.isSupported
                || m.shader.name == "Hidden/InternalErrorShader" || BuiltInShaderPrefixes.Any(p => m.shader.name.StartsWith(p)));
            if (pink) issue.objects.Add(r.gameObject);
        }
        Add(issue);
    }

    // Composants dont le script a été supprimé : erreurs dans la console et en jeu
    private static void CheckMissingScripts()
    {
        var gos = new List<GameObject>();
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                    gos.Add(t.gameObject);
        var issue = new Issue
        {
            severity = Severity.Error,
            title = "Scripts manquants",
            explanation = "Ces objets portent un composant dont le script n'existe plus (supprimé ou renommé). Il ne fait plus rien et peut gêner les prefabs. " +
                          "Sur une instance de prefab, le script manquant est dans le prefab lui-même : il faut l'ouvrir pour le retirer.",
            objects = gos.Cast<Object>().ToList(),
            fixLabel = "Retirer les scripts manquants",
            fix = () =>
            {
                int skipped = 0;
                foreach (var go in gos)
                {
                    // Unity refuse de retirer un composant qui vient du prefab : seul le prefab peut le faire
                    if (PrefabUtility.IsPartOfPrefabInstance(go)) { skipped++; continue; }
                    Undo.RegisterCompleteObjectUndo(go, "Retirer les scripts manquants");
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                }
                if (skipped > 0)
                    Debug.LogWarning("Vérifier la scène : " + skipped + " objet(s) de prefab gardent leur script manquant : ouvre le prefab pour le retirer.");
            }
        };
        Add(issue);
    }

    // Liseré sur de grands objets : invisible sur un mur, mais calculé sur des milliers de pixels
    private static void CheckRimOnLargeObjects(List<Renderer> renderers)
    {
        var mats = new HashSet<Material>();
        var gos = new List<Object>();
        foreach (var r in renderers)
        {
            if (MaxSize(r) < LargeObjectSize) continue;
            var rimMats = r.sharedMaterials.Where(m => IsToon(m) && m.GetFloat("_Rim") > 0.5f).ToList();
            if (rimMats.Count == 0) continue;
            gos.Add(r.gameObject);
            foreach (var m in rimMats) mats.Add(m);
        }
        Add(new Issue
        {
            severity = Severity.Performance,
            title = "Liseré sur de grandes surfaces",
            explanation = "Le bord lumineux ne se voit presque pas sur un mur ou un sol de plus de " + LargeObjectSize + " m, mais il est calculé sur une grande partie de l'écran. " +
                          "Le corriger le coupe sur leurs " + mats.Count + " material(s) (comme la « version décor » des styles) : les petits objets qui partagent ces materials le perdent aussi.",
            objects = gos,
            fixLabel = "Couper le liseré",
            fix = () => SetOnMaterials(mats, "_Rim", 0f)
        });
    }

    // Contour sur un objet qui n'est pas un personnage : une passe de dessin en plus pour rien
    private static void CheckOutlineOnDecor(List<Renderer> renderers)
    {
        var mats = new HashSet<Material>();
        var gos = new List<Object>();
        foreach (var r in renderers)
        {
            if (IsCharacter(r)) continue;
            var outlined = r.sharedMaterials.Where(m => IsToon(m) && m.HasProperty("_Outline") && m.GetFloat("_Outline") > 0.5f).ToList();
            if (outlined.Count == 0) continue;
            gos.Add(r.gameObject);
            foreach (var m in outlined) mats.Add(m);
        }
        Add(new Issue
        {
            severity = Severity.Performance,
            title = "Contour sur du décor",
            explanation = "Ces objets ne semblent pas être des personnages, mais leur material dessine le contour des personnages : c'est une passe de dessin en plus. " +
                          "Si c'est voulu, donne une partie au material (section Contour de l'onglet « 2 Régler ») et ce point disparaîtra.",
            objects = gos,
            fixLabel = "Couper le contour",
            fix = () => SetOnMaterials(mats, "_Outline", 0f)
        });
    }

    // Petits objets qui projettent une ombre de quelques pixels : chaque ombre a un coût
    private static void CheckTinyShadowCasters(List<Renderer> renderers)
    {
        var tiny = renderers.Where(r => r.shadowCastingMode != ShadowCastingMode.Off && !(r is ParticleSystemRenderer)
            && MaxSize(r) < TinyObjectSize && !IsCharacter(r)).ToList();
        Add(new Issue
        {
            severity = Severity.Performance,
            title = "Petits objets qui projettent une ombre",
            explanation = "Ces objets font moins de " + (TinyObjectSize * 100) + " cm : leur ombre ne fait que quelques pixels mais coûte un dessin de plus pour chaque ombre. " +
                          "Les couper n'empêche pas les objets de recevoir les ombres des autres.",
            objects = tiny.Select(r => (Object)r.gameObject).ToList(),
            fixLabel = "Couper leurs ombres",
            fix = () =>
            {
                foreach (var r in tiny)
                {
                    Undo.RecordObject(r, "Couper l'ombre");
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        });
    }

    // Textures plus grandes que nécessaire : mémoire et temps de chargement
    private static void CheckHeavyTextures(List<Renderer> renderers)
    {
        var heavy = new Dictionary<Texture2D, List<Object>>();
        foreach (var r in renderers)
        {
            foreach (var m in r.sharedMaterials.Where(m => m != null))
            {
                foreach (int id in m.GetTexturePropertyNameIDs())
                {
                    if (!(m.GetTexture(id) is Texture2D tex) || Mathf.Max(tex.width, tex.height) <= MaxTextureSize) continue;
                    // Seulement les textures du projet (celles des packages sont en lecture seule)
                    string texPath = AssetDatabase.GetAssetPath(tex);
                    if (!texPath.StartsWith("Assets/") || !(AssetImporter.GetAtPath(texPath) is TextureImporter)) continue;
                    if (!heavy.TryGetValue(tex, out var users)) heavy[tex] = users = new List<Object>();
                    if (!users.Contains(r.gameObject)) users.Add(r.gameObject);
                }
            }
        }
        Add(new Issue
        {
            severity = Severity.Performance,
            title = "Textures très lourdes",
            explanation = heavy.Count + " texture(s) dépassent " + MaxTextureSize + " px (" + string.Join(", ", heavy.Keys.Take(4).Select(t => t.name + " " + t.width + "×" + t.height)) +
                          (heavy.Count > 4 ? "..." : "") + "). Les limiter à " + MaxTextureSize + " px divise leur poids en mémoire par 4, " +
                          "la différence ne se voit pas en toon. Le fichier d'origine n'est pas modifié. " +
                          "Attention : ce réglage d'import ne s'annule pas avec Ctrl+Z (remettre « Max Size » dans l'import de la texture).",
            objects = heavy.Values.SelectMany(v => v).Distinct().ToList(),
            fixLabel = "Limiter à " + MaxTextureSize + " px",
            fix = () =>
            {
                foreach (var tex in heavy.Keys)
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
                    importer.maxTextureSize = MaxTextureSize;
                    importer.SaveAndReimport();
                }
            }
        });
    }

    // Beaucoup de lampes avec ombres en temps réel : chaque ombre redessine la scène
    private static void CheckShadowedLights()
    {
        var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
            .Where(l => l.isActiveAndEnabled && l.type != LightType.Directional && l.shadows != LightShadows.None && l.lightmapBakeType != LightmapBakeType.Baked).ToList();
        if (lights.Count <= MaxShadowedLights) return;
        Add(new Issue
        {
            severity = Severity.Performance,
            title = "Beaucoup de lampes avec ombres",
            explanation = lights.Count + " lampes projettent des ombres en temps réel : chacune redessine les objets autour d'elle. " +
                          "Garde les ombres sur les " + MaxShadowedLights + " lampes les plus visibles et coupe-les sur les autres (Inspector de la lampe > Shadow Type).",
            objects = lights.Select(l => (Object)l.gameObject).ToList()
        });
    }

    // Émissif trop faible pour briller avec le bloom de la scène
    private static void CheckEmissionBelowBloom(List<Renderer> renderers)
    {
        var bloom = VolumeManager.instance.stack != null ? VolumeManager.instance.stack.GetComponent<Bloom>() : null;
        if (bloom == null || !bloom.IsActive()) return;
        float threshold = bloom.threshold.value;
        var gos = renderers.Where(r => r.sharedMaterials.Any(m => IsToon(m) && m.GetFloat("_UseEmission") > 0.5f
            && m.GetColor("_EmissionColor").maxColorComponent < threshold)).Select(r => (Object)r.gameObject).ToList();
        Add(new Issue
        {
            severity = Severity.Advice,
            title = "Émissif qui ne brille pas",
            explanation = "Le bloom de la scène ne fait briller que ce qui dépasse " + threshold.ToString("0.0") + ". L'émissif de ces objets est en dessous : ils sont colorés mais sans halo. " +
                          "Monte l'intensité de leur couleur émissive (onglet « 2 Régler », section Lumières et émissif).",
            objects = gos
        });
    }

    // Objets encore en Lit / autre shader URP : pas le style toon
    private static void CheckUnconvertedMaterials(List<Renderer> renderers)
    {
        // Seulement les maillages opaques : particules, sprites, texte, vitres et feuillages découpés gardent leur shader
        var gos = renderers.Where(r => (r is MeshRenderer || r is SkinnedMeshRenderer) && r.sharedMaterials.Any(m => m != null && m.shader != null && m.shader.isSupported
            && m.renderQueue < 2450 && !ToolShaders.Contains(m.shader.name) && !m.shader.name.StartsWith("Hidden/") && !m.shader.name.StartsWith("Skybox/")
            && !m.shader.name.StartsWith("TextMeshPro") && !BuiltInShaderPrefixes.Any(p => m.shader.name.StartsWith(p))))
            .Select(r => (Object)r.gameObject).ToList();
        Add(new Issue
        {
            severity = Severity.Advice,
            title = "Objets pas encore en style toon",
            explanation = "Ces objets utilisent un autre shader (Lit, Unlit...). C'est normal pour certains effets, sinon convertis-les dans l'onglet « 1 Convertir ».",
            objects = gos
        });
    }

    private static void CheckCameraAntialiasing()
    {
        var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
            .Where(c => c.cameraType == CameraType.Game && c.TryGetComponent<UniversalAdditionalCameraData>(out var d)
                && d.renderType == CameraRenderType.Base && d.antialiasing == AntialiasingMode.None).ToList(); // une caméra superposée suit sa caméra de base
        Add(new Issue
        {
            severity = Severity.Advice,
            title = "Caméra sans anti-aliasing",
            explanation = "Sans anti-aliasing, les contours et les bords d'ombre toon paraissent crénelés. Le SMAA les adoucit pour un coût très faible.",
            objects = cams.Select(c => (Object)c.gameObject).ToList(),
            fixLabel = "Activer le SMAA",
            fix = () =>
            {
                foreach (var c in cams)
                {
                    var data = c.GetComponent<UniversalAdditionalCameraData>();
                    Undo.RecordObject(data, "Activer le SMAA");
                    data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    data.antialiasingQuality = AntialiasingQuality.High;
                }
            }
        });
    }

    // Le liseré Depth et la silhouette claire des contours lisent la texture de profondeur de la caméra. Sans elle
    // (option « Depth Texture » de l'asset URP coupée), le liseré s'allume partout et le contour des vêtements disparaît.
    private static void CheckDepthTexture(List<Renderer> renderers)
    {
        var users = renderers.Where(r => r.sharedMaterials.Any(m => IsToon(m) && (m.GetFloat("_Rim") > 1.5f
            || (m.HasProperty("_Outline") && m.GetFloat("_Outline") > 0.5f && m.GetFloat("_OutlineOuterFade") > 0f))))
            .Select(r => (Object)r.gameObject).ToList();
        if (users.Count == 0) return;
        var assets = GetPipelineAssets().Where(a => !a.supportsCameraDepthTexture).ToList();
        if (assets.Count == 0) return;
        Add(new Issue
        {
            severity = Severity.Error,
            title = "Liseré ou contour sans profondeur",
            explanation = "Ces objets utilisent le liseré « Depth » ou la silhouette claire des contours, qui lisent la profondeur de l'image. " +
                          "L'option « Depth Texture » est coupée dans les réglages URP (" + string.Join(", ", assets.Select(a => a.name)) + ") : " +
                          "dans la vue Scene et avec les caméras qui suivent ces réglages, le liseré s'allume partout. La corriger active cette option (coût faible).",
            objects = users,
            fixLabel = "Activer la profondeur",
            fix = () =>
            {
                foreach (var a in assets)
                {
                    Undo.RecordObject(a, "Activer la texture de profondeur");
                    a.supportsCameraDepthTexture = true;
                    EditorUtility.SetDirty(a);
                }
            }
        });
    }

    // Materials créés par un script avec ToonAdvanced : contour coupé mais passe de contour restée active
    // (l'objet est dessiné une fois de plus pour rien). L'outil et l'Inspector, eux, coupent la passe.
    private static void CheckIdleOutlinePass(List<Renderer> renderers)
    {
        var mats = new HashSet<Material>(renderers.SelectMany(r => r.sharedMaterials)
            .Where(m => IsToon(m) && m.HasProperty("_Outline") && m.GetFloat("_Outline") < 0.5f && m.GetShaderPassEnabled(ToonOutlineDrawer.PassLightMode)));
        Add(new Issue
        {
            severity = Severity.Performance,
            title = "Passe de contour inutile",
            explanation = "Le contour de ces materials est coupé, mais sa passe est restée active (material créé par un script) : " +
                          "chaque objet est dessiné une fois de plus, sans rien afficher.",
            objects = renderers.Where(r => r.sharedMaterials.Any(mats.Contains)).Select(r => (Object)r.gameObject).ToList(),
            fixLabel = "Couper la passe",
            fix = () =>
            {
                foreach (var m in mats)
                {
                    Undo.RecordObject(m, "Couper la passe de contour");
                    m.SetShaderPassEnabled(ToonOutlineDrawer.PassLightMode, false);
                    EditorUtility.SetDirty(m);
                }
            }
        });
    }

    private static IEnumerable<UniversalRenderPipelineAsset> GetPipelineAssets()
    {
        var assets = new List<UniversalRenderPipelineAsset>();
        if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset main) assets.Add(main);
        for (int i = 0; i < QualitySettings.count; i++)
            if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset q && !assets.Contains(q)) assets.Add(q);
        return assets;
    }

    // Ambiances posées mais effet absent du rendu : le voile ne s'affiche pas
    private static void CheckAtmosphereInstalled()
    {
        var volumes = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)
            .Where(v => v.sharedProfile != null && v.sharedProfile.TryGet(out ToonAtmosphere a) && a.intensity.value > 0f).ToList();
        if (volumes.Count == 0) return;
        // Tous les assets URP (un par niveau de qualité possible), comme le bouton d'installation
        bool missing = false;
        foreach (var urp in GetPipelineAssets())
        {
            var list = new SerializedObject(urp).FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is ScriptableRendererData data && !data.rendererFeatures.Any(f => f is ToonAtmosphereFeature))
                    missing = true;
        }
        if (!missing) return;
        Add(new Issue
        {
            severity = Severity.Error,
            title = "Atmosphère invisible",
            explanation = "La scène a des ambiances, mais l'effet n'est pas installé sur tous les renderers URP : le voile ne s'affiche pas avec ces renderers.",
            objects = volumes.Select(v => (Object)v.gameObject).ToList(),
            fixLabel = "Installer l'effet",
            fix = ToonAtmosphereTools.Install
        });
    }

    // =====================================================================
    // Utilitaires
    // =====================================================================

    private static bool IsToon(Material m) => m != null && m.shader != null && m.shader.name == "Custom/ToonAdvanced";

    // Personnages : modèles avec un maillage animé (skinning), un Animator, ou un material dont on a choisi
    // la partie du contour (cheveux, peau...). Tout ce qui est rangé sous la même racine en fait partie (yeux, accessoires).
    private static HashSet<Transform> characterRoots = new HashSet<Transform>();

    private static void FindCharacters()
    {
        characterRoots = new HashSet<Transform>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool character = r is SkinnedMeshRenderer || r.GetComponentInParent<Animator>(true) != null
                || r.sharedMaterials.Any(m => IsToon(m) && m.HasProperty("_OutlineZone") && m.GetFloat("_OutlineZone") > 0.5f);
            if (character) characterRoots.Add(r.transform.root);
        }
    }

    private static bool IsCharacter(Renderer r) => characterRoots.Contains(r.transform.root);

    private static float MaxSize(Renderer r)
    {
        var s = r.bounds.size;
        return Mathf.Max(s.x, Mathf.Max(s.y, s.z));
    }

    // Change une valeur à mot-clé (liseré, contour) comme l'Inspector : les drawers mettent à jour mots-clés et passes
    private static void SetOnMaterials(IEnumerable<Material> mats, string property, float value)
    {
        foreach (var m in mats)
        {
            Undo.RecordObject(m, "Vérifier la scène");
            m.SetFloat(property, value);
            MaterialEditor.ApplyMaterialPropertyDrawers(m);
            EditorUtility.SetDirty(m);
        }
    }
}
