using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class ShaderToolsWindow : EditorWindow
{
    private const string ShaderFlat = "Custom/MangaDotsOutline";
    private const string ShaderTextured = "Custom/MangaDotsOutlineTextured";
    private const string ShaderToon = "Custom/ToonAdvanced";
    private const string DefaultCelShader = "Custom/CelShading";
    // Shader propose par defaut dans l'onglet Convertir : le shader principal des decors
    private const string DefaultTargetShader = "Custom/ToonAdvanced";
    private const string DefaultMaterialsFolder = "Assets/ShaderTools/Materials";
    private const string DefaultRampFolder = "Assets/ShaderTools/Ramps";
    private const string AtmosphereFolder = "Assets/ShaderTools/Atmospheres";
    private const string HistoryAssetPath = "Assets/ShaderTools/Materials/MaterialConversionHistory.asset";

    [MenuItem("Tools/Outils Shaders/Editeur")]
    public static void ShowWindow()
    {
        var window = GetWindow<ShaderToolsWindow>("Outils Shaders");
        window.minSize = new Vector2(380, 480);
    }

    // Serialises : l'onglet, la position de defilement et le filtre survivent a une recompilation
    [SerializeField] private int tab;
    [SerializeField] private Vector2 scroll;

    // Grosses scenes : rien n'est rescanne a chaque evenement. On pose un marqueur "a refaire",
    // et le recalcul a lieu au prochain affichage, seulement si l'onglet concerne est ouvert.
    private bool sceneDataDirty = true;
    private bool historyDirty = true;
    private HashSet<Renderer> selectedRenderersCache;
    private HashSet<Material> selectedMaterialsCache;

    private void OnEnable()
    {
        Selection.selectionChanged += OnSelectionChanged;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        ObjectChangeEvents.changesPublished += OnObjectChangesPublished;
        SceneView.duringSceneGui += OnSceneGUI;
        // Le survol des lignes de la liste des parties allume les objets dans la vue Scene
        wantsMouseMove = true;
        if (targetShader == null)
            targetShader = Shader.Find(DefaultTargetShader);
        MarkSceneDataDirty();
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        ObjectChangeEvents.changesPublished -= OnObjectChangesPublished;
        SceneView.duringSceneGui -= OnSceneGUI;
        SetHighlight(null);
    }

    private void OnSelectionChanged()
    {
        InvalidateSelectionCache();
        Repaint();
    }

    private void OnHierarchyChanged()
    {
        // Evite que la liste garde des references vers des objets supprimes/renommes
        // entre deux clics manuels sur "Rafraichir".
        MarkSceneDataDirty();
    }

    // Changer le material d'un Renderer (glisser-deposer dans l'Inspector...) ne declenche pas hierarchyChanged
    private void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
    {
        for (int i = 0; i < stream.length; i++)
        {
            if (stream.GetEventType(i) != ObjectChangeKind.ChangeGameObjectOrComponentProperties) continue;
            stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var data);
            if (EditorUtility.EntityIdToObject(data.instanceId) is Renderer)
            {
                MarkSceneDataDirty();
                return;
            }
        }
    }

    private void MarkSceneDataDirty()
    {
        sceneDataDirty = true;
        historyDirty = true;
        InvalidateSelectionCache();
        Repaint();
    }

    private void InvalidateSelectionCache()
    {
        selectedRenderersCache = null;
        selectedMaterialsCache = null;
    }

    private void OnGUI()
    {
        // Surbrillance : la souris bouge -> on redessine pour savoir quelle ligne est survolee ;
        // elle sort de la fenetre -> plus rien d'allume. Les lignes survolees sont relevees pendant la passe Repaint.
        if (Event.current.type == EventType.MouseMove) Repaint();
        if (Event.current.type == EventType.MouseLeaveWindow) SetHighlight(null);
        if (Event.current.type == EventType.Repaint) hoveredMaterials = null;

        // Recalculs uniquement pendant la passe Layout : les donnees ne changent pas entre la passe
        // Layout et la passe Repaint d'une meme image (sinon erreur "Invalid GUILayout state").
        if (Event.current.type == EventType.Layout)
        {
            if (tab == TabConvert && sceneDataDirty)
            {
                RefreshMaterialList();
                sceneDataDirty = false;
            }
            if (tab == TabHistory && historyDirty)
            {
                RefreshHistory();
                historyDirty = false;
            }
        }

        // En-tete : l'ordre de travail conseille, et l'aide qu'on peut masquer une fois l'outil connu
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Ordre conseillé : 1 Convertir → 2 Régler → 3 Atmosphère → 4 Jour / Nuit", EditorStyles.miniLabel);
        bool help = GUILayout.Toggle(ShowHelp, new GUIContent("Aide", "Afficher les explications en haut de chaque onglet"), EditorStyles.miniButton, GUILayout.Width(45));
        if (help != ShowHelp) EditorPrefs.SetBool(HelpPrefKey, help);
        EditorGUILayout.EndHorizontal();

        // Deux rangees : les 4 etapes du travail, puis les outils complementaires
        tab = GUILayout.SelectionGrid(tab, new[] { "1 Convertir", "2 Régler", "3 Atmosphère", "4 Jour / Nuit", "Ramps", "Historique", "Vérifier la scène" }, 4, GUILayout.Height(46));
        EditorGUILayout.Space();

        if (tab == TabConvert) DrawConversionTab();
        else if (tab == TabSettings) DrawSettingsTab();
        else if (tab == TabAtmosphere)
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            ToonAtmosphereTools.DrawTab(AtmosphereFolder, ShowHelp);
            EditorGUILayout.EndScrollView();
        }
        else if (tab == TabDayNight) DrawDayNightTab();
        else if (tab == TabRamp)
        {
            // Un atlas avec beaucoup de lignes depasse la hauteur de la fenetre
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawRampTab();
            EditorGUILayout.EndScrollView();
        }
        else if (tab == TabHistory) DrawHistoryTab();
        else ToonSceneAudit.DrawTab(ShowHelp);

        if (Event.current.type == EventType.Repaint)
            SetHighlight(hoveredMaterials);
    }

    // Onglets dans l'ordre de travail
    private const int TabConvert = 0, TabSettings = 1, TabAtmosphere = 2, TabDayNight = 3, TabRamp = 4, TabHistory = 5, TabAudit = 6;

    // =====================================================================
    // Onglet 5 : jour / nuit de la scene active (composant ToonDayNight)
    // =====================================================================

    private void DrawDayNightTab()
    {
        TabHelp("Fait passer toute la scène du jour à la nuit : ombres des materials, ciel, soleil, lumière ambiante, reflets.\n" +
            "1. Clique sur « Ajouter le jour / nuit » (une fois par scène).\n" +
            "2. Utilise les boutons Jour / Crépuscule / Nuit, ou le curseur.\n" +
            "Les couleurs de jour et de nuit se règlent sur le composant (bouton en bas).");

        var dayNight = FindFirstObjectByType<ToonDayNight>();
        if (dayNight == null)
        {
            EditorGUILayout.HelpBox("Aucun composant ToonDayNight dans la scene.", MessageType.None);
            if (GUILayout.Button("Ajouter le jour / nuit a la scene", GUILayout.Height(30)))
                CreateDayNight();
            return;
        }

        EditorGUI.BeginChangeCheck();
        float night = EditorGUILayout.Slider("Nuit", dayNight.night, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
            SetNight(dayNight, night);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Jour", GUILayout.Height(28))) SetNight(dayNight, 0f);
        if (GUILayout.Button("Crepuscule", GUILayout.Height(28))) SetNight(dayNight, 0.5f);
        if (GUILayout.Button("Nuit", GUILayout.Height(28))) SetNight(dayNight, 1f);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        if (GUILayout.Button("Selectionner le composant (couleurs, intensites...)"))
        {
            Selection.activeGameObject = dayNight.gameObject;
            EditorGUIUtility.PingObject(dayNight);
        }
    }

    private static void SetNight(ToonDayNight dayNight, float night)
    {
        // Le composant modifie aussi la lumiere et les reglages d'eclairage de la scene : on les inclut dans le Ctrl+Z
        var light = new SerializedObject(dayNight).FindProperty("mainLight").objectReferenceValue as Light;
        var undoTargets = new List<Object> { dayNight };
        if (light != null) undoTargets.Add(light);
        Undo.RecordObjects(undoTargets.ToArray(), "Jour / Nuit");
        dayNight.night = night;
        dayNight.Apply();
        EditorUtility.SetDirty(dayNight);
        if (light != null) EditorUtility.SetDirty(light);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(dayNight.gameObject.scene);
        SceneView.RepaintAll();
    }

    private static void CreateDayNight()
    {
        // Lumiere principale : le "Soleil" des reglages d'eclairage, sinon la premiere lumiere directionnelle
        Light sun = RenderSettings.sun;
        if (sun == null)
            sun = FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);

        // Releve de l'eclairage actuel AVANT d'ajouter le composant (il s'applique des son activation) :
        // les valeurs de "jour" reprennent la scene telle quelle, donc l'ajouter ne change pas l'image.
        // Ambiance : couleur moyenne des harmoniques spheriques (bande L0, en lineaire), valable quel que soit le mode.
        var sh = RenderSettings.ambientProbe;
        Color currentAmbient = new Color(sh[0, 0], sh[1, 0], sh[2, 0]).gamma;
        float currentReflections = RenderSettings.reflectionIntensity;
        Color sunColor = sun != null ? sun.color : Color.white;
        float sunIntensity = sun != null ? sun.intensity : 1f;

        var go = new GameObject("Jour_Nuit");
        go.SetActive(false); // pas d'application avec les valeurs par defaut
        var dayNight = go.AddComponent<ToonDayNight>();
        var so = new SerializedObject(dayNight);
        so.FindProperty("mainLight").objectReferenceValue = sun;
        so.FindProperty("dayLightColor").colorValue = sunColor;
        so.FindProperty("dayLightIntensity").floatValue = sunIntensity;
        so.FindProperty("dayAmbient").colorValue = currentAmbient;
        so.FindProperty("dayReflectionIntensity").floatValue = currentReflections;
        so.ApplyModifiedPropertiesWithoutUndo();
        go.SetActive(true);
        Undo.RegisterCreatedObjectUndo(go, "Ajouter le jour / nuit");

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
    }

    // =====================================================================
    // Onglet 1 : reglages rapides (pois / outline / degrade) sur la selection
    // =====================================================================

    // Mis en cache : la fenetre se redessine a chaque mouvement de souris, et parcourir les enfants
    // d'une grosse selection (des milliers d'objets) a chaque fois coute plusieurs millisecondes.
    // Le cache est vide quand la selection, la hierarchie ou un material de Renderer change.
    private HashSet<Renderer> GetSelectedRenderers()
    {
        if (selectedRenderersCache != null) return selectedRenderersCache;

        // GetComponentsInChildren (et pas juste GetComponent) car sur beaucoup de props/prefabs
        // (ex: BasketBallNet) le Renderer est sur un enfant, pas sur l'objet selectionne lui-meme.
        var set = new HashSet<Renderer>();
        foreach (var go in Selection.gameObjects)
        {
            foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
                set.Add(rend);
        }
        selectedRenderersCache = set;
        return set;
    }

    private HashSet<Material> GetSelectedMaterials()
    {
        if (selectedMaterialsCache != null) return selectedMaterialsCache;

        var set = new HashSet<Material>();
        foreach (var rend in GetSelectedRenderers())
        {
            if (rend == null) continue;
            foreach (var m in rend.sharedMaterials)
                if (m != null) set.Add(m);
        }
        selectedMaterialsCache = set;
        return set;
    }

    private List<Material> GetCompatibleMaterials()
    {
        // Meme logique que la Conversion : enfants inclus et tous les slots de material
        return GetSelectedMaterials()
            .Where(m => m.shader != null && (m.shader.name == ShaderFlat || m.shader.name == ShaderTextured || m.shader.name == ShaderToon))
            .OrderBy(m => m.name)
            .ToList();
    }

    private void DrawSettingsTab()
    {
        var mats = GetCompatibleMaterials();

        TabHelp("Règle l'apparence des objets sélectionnés.\n" +
            "1. Sélectionne un ou plusieurs objets dans la scène ou la Hierarchy (un mur, un meuble, un personnage...).\n" +
            "2. Choisis un style de base.\n" +
            "3. Déplie une section pour affiner. Ctrl+Z annule.");

        if (mats.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "Aucun objet sélectionné n'utilise un shader de l'outil.\n" +
                "Sélectionne des objets dans la scène, ou convertis d'abord leurs materials (onglet 1).",
                MessageType.Warning);
            if (GUILayout.Button("Aller à « 1 Convertir »", GUILayout.Height(26)))
                tab = TabConvert;
            return;
        }

        // Sur une grosse selection, lister tous les noms repousserait les reglages hors de la fenetre
        const int maxNames = 12;
        string names = string.Join(", ", mats.Take(maxNames).Select(m => m.name));
        if (mats.Count > maxNames) names += " (+" + (mats.Count - maxNames) + " autres)";
        EditorGUILayout.HelpBox(
            mats.Count + " material(s) compatible(s) detecte(s) dans la selection :\n" + names,
            MessageType.None);
        EditorGUILayout.Space();

        scroll = EditorGUILayout.BeginScrollView(scroll);

        var toonMats = mats.Where(m => m.shader.name == ShaderToon).ToList();
        if (toonMats.Count > 0)
            DrawToonSettings(toonMats);

        var mangaMats = mats.Where(m => m.shader.name != ShaderToon).ToList();
        if (mangaMats.Count > 0)
            DrawMangaDotsSettings(mangaMats);

        EditorGUILayout.EndScrollView();
    }

    private void DrawMangaDotsSettings(List<Material> mats)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Manga Dots Outline (" + mats.Count + ")", EditorStyles.largeLabel);

        Material reference = mats[0];

        EditorGUILayout.LabelField("Pois (Halftone)", EditorStyles.boldLabel);
        DrawFloatSlider(mats, reference, "_DotDensity", "Densite", 2f, 60f);
        DrawFloatSlider(mats, reference, "_MaxDotRadius", "Taille en bas", 0f, 0.9f);
        DrawFloatSlider(mats, reference, "_MinDotRadius", "Taille en haut", 0f, 0.9f);
        DrawFloatSlider(mats, reference, "_DotSoftness", "Adoucissement des bords", 0.001f, 0.3f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Outline", EditorStyles.boldLabel);
        DrawColor(mats, reference, "_OutlineColor", "Couleur du contour");
        DrawFloatSlider(mats, reference, "_OutlineWidth", "Epaisseur", 0f, 0.05f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Couleur / Degrade", EditorStyles.boldLabel);

        var flatMats = mats.Where(m => m.shader.name == ShaderFlat).ToList();
        var texMats = mats.Where(m => m.shader.name == ShaderTextured).ToList();

        if (flatMats.Count > 0)
        {
            EditorGUILayout.LabelField("Shader couleur plate (" + flatMats.Count + ")", EditorStyles.miniBoldLabel);
            DrawColor(flatMats, flatMats[0], "_DarkColor", "Couleur des pois (encre)");
            DrawColor(flatMats, flatMats[0], "_BaseColorBottom", "Couleur de base - Bas");
            DrawColor(flatMats, flatMats[0], "_BaseColorTop", "Couleur de base - Haut");
        }

        if (texMats.Count > 0)
        {
            EditorGUILayout.LabelField("Shader couleur d'origine (" + texMats.Count + ")", EditorStyles.miniBoldLabel);
            DrawColor(texMats, texMats[0], "_BaseColor", "Couleur de base (teinte)");
            DrawFloatSlider(texMats, texMats[0], "_ShadowStrength", "Force de l'ombre (bas)", 0f, 1f);
            DrawColor(texMats, texMats[0], "_DotColor", "Couleur des pois (encre)");
        }
    }

    private void DrawToonSettings(List<Material> mats)
    {
        Material reference = mats[0];
        EditorGUILayout.LabelField("Toon Advanced (" + mats.Count + " material(s))", EditorStyles.largeLabel);

        // Le point de départ : un style complet en un clic, les sections en dessous servent à affiner
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Style de base (remplace les réglages ci-dessous)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            foreach (var preset in ToonPresets)
            {
                if (GUILayout.Button(new GUIContent(preset.label, preset.tooltip), GUILayout.Height(26)))
                    ApplyToonPreset(mats, preset, rampFolder, presetForDecor);
            }
            EditorGUILayout.EndHorizontal();
            presetForDecor = EditorGUILayout.ToggleLeft(new GUIContent(
                "Pour un décor (murs, sols, plafonds) : sans bord lumineux",
                "Même look, mais le liseré est coupé. Sur les grandes surfaces il ne se voit presque pas " +
                "et c'est là que se trouvent la plupart des pixels de l'écran : autant ne pas le calculer."), presetForDecor);
        }

        if (Section("Ombres", "Ombres", "Couleur des ombres, netteté de la limite ombre / lumière, ombres projetées."))
        {
            DrawToggle(mats, reference, "_UseRamp", "Utiliser une Ramp");
            if (reference.GetFloat("_UseRamp") > 0.5f)
            {
                DrawRampTextureField(mats, reference);
                var atlas = ReadAtlasData(reference.GetTexture("_RampMap"));
                if (atlas != null)
                {
                    int current = Mathf.Clamp(Mathf.RoundToInt(reference.GetFloat("_RampRow")), 0, atlas.names.Length - 1);
                    EditorGUI.BeginChangeCheck();
                    int row = EditorGUILayout.Popup(new GUIContent("Ligne de l'atlas", atlas.hasNight ? "Version jour/nuit incluse" : "Pas de version nuit"), current, atlas.names);
                    if (EditorGUI.EndChangeCheck())
                    {
                        SetRampRow(mats, row);
                        SceneView.RepaintAll();
                    }
                }
            }
            else
                DrawColor(mats, reference, "_ShadowColor", "Teinte d'ombre");
            DrawFloatSlider(mats, reference, "_ShadowSaturation", "Saturation des ombres", 0f, 1f);
            DrawFloatSlider(mats, reference, "_ShadowThreshold", "Seuil ombre / lumiere", 0f, 1f);
            DrawFloatSlider(mats, reference, "_ShadowSmoothness", "Douceur du bord", 0.001f, 0.5f);
            DrawFloatSlider(mats, reference, "_ReceiveShadowStrength", "Force des ombres portees", 0f, 1f);
            DrawFloatSlider(mats, reference, "_AmbientStrength", "Lumiere ambiante", 0f, 1f);
        }

        if (Section("Rim", "Bord lumineux (liseré)", "Fine ligne claire (ou sombre en mode Encre) sur les bords des objets."))
        {
            DrawPopup(mats, reference, "_Rim", "Mode", new[] { "Off", "Fresnel", "Depth" });
            if (reference.GetFloat("_Rim") > 0.5f)
            {
                if (reference.HasProperty("_RimBlend"))
                {
                    int blend = Mathf.RoundToInt(reference.GetFloat("_RimBlend"));
                    EditorGUI.BeginChangeCheck();
                    blend = EditorGUILayout.Popup(new GUIContent("Melange",
                        "Lumiere : le lisere eclaircit le bord (un noir n'a aucun effet).\nEncre : le lisere remplace la couleur, pour un contour sombre."),
                        blend, new[] { "Lumiere (eclaircit)", "Encre (contour)" });
                    if (EditorGUI.EndChangeCheck())
                    {
                        foreach (var mat in mats)
                        {
                            Undo.RecordObject(mat, "Melange du lisere");
                            mat.SetFloat("_RimBlend", blend);
                            // Passage en Encre : contour noir tout autour (le reglage "cote eclaire" le couperait d'un cote)
                            if (blend == 1)
                            {
                                mat.SetColor("_RimColor", Color.black);
                                mat.SetFloat("_RimLightAlign", 0f);
                                mat.SetFloat("_RimAlbedoTint", 0f);
                            }
                            EditorUtility.SetDirty(mat);
                        }
                        SceneView.RepaintAll();
                    }
                }
                DrawColor(mats, reference, "_RimColor", "Couleur", true);
                DrawFloatSlider(mats, reference, "_RimWidth", "Largeur", 0f, 1f);
                DrawFloatSlider(mats, reference, "_RimSoftness", "Douceur", 0.001f, 0.5f);
                DrawFloatSlider(mats, reference, "_RimLightAlign", "Cote eclaire uniquement", 0f, 1f);
                DrawFloatSlider(mats, reference, "_RimAlbedoTint", "Teinte par la couleur de base", 0f, 1f);
                if (reference.GetFloat("_Rim") > 1.5f)
                    DrawFloatSlider(mats, reference, "_RimDepthThreshold", "Ecart de profondeur min", 0.01f, 2f);
            }
        }

        if (Section("Reflets", "Reflets (brillance, métal)", "Petit reflet net de la lumière, et reflet métallique (or, acier...)."))
        {
            EditorGUILayout.LabelField("Reflet net", EditorStyles.miniBoldLabel);
            DrawToggle(mats, reference, "_Specular", "Activer");
            if (reference.GetFloat("_Specular") > 0.5f)
            {
                DrawColor(mats, reference, "_SpecularColor", "Couleur", true);
                DrawFloatSlider(mats, reference, "_SpecularSize", "Taille", 0f, 0.5f);
                DrawFloatSlider(mats, reference, "_SpecularSoftness", "Douceur", 0.001f, 0.2f);
            }

            EditorGUILayout.LabelField("Reflet métal (MatCap)", EditorStyles.miniBoldLabel);
            DrawToggle(mats, reference, "_UseMatCap", "Activer");
            if (reference.HasProperty("_UseMatCap") && reference.GetFloat("_UseMatCap") > 0.5f)
            {
                DrawTexture(mats, reference, "_MatCapMap", "MatCap");
                if (GUILayout.Button("Utiliser la MatCap metal par defaut"))
                {
                    var matcap = GetOrCreateDefaultMatCap(rampFolder + "/" + DefaultMatCapName + ".png");
                    foreach (var mat in mats)
                    {
                        Undo.RecordObject(mat, "MatCap par defaut");
                        mat.SetTexture("_MatCapMap", matcap);
                        EditorUtility.SetDirty(mat);
                    }
                    SceneView.RepaintAll();
                }
                DrawFloatSlider(mats, reference, "_MatCapStrength", "Force", 0f, 1f);
                DrawFloatSlider(mats, reference, "_MatCapAdditive", "Mode (0 metal - 1 brillance)", 0f, 1f);
                DrawTexture(mats, reference, "_MatCapMask", "Masque (blanc = metal)");
            }
        }

        if (Section("Lumiere", "Lumières et émissif", "Effet des lampes de la scène, et objets qui brillent eux-mêmes (néons, écrans)."))
        {
            DrawFloatSlider(mats, reference, "_AdditionalLightsStrength", "Lampes de la scène (intensité)", 0f, 2f);
            EditorGUILayout.LabelField("Émissif (néons, écrans, lampes)", EditorStyles.miniBoldLabel);
            DrawToggle(mats, reference, "_UseEmission", "Activer");
            if (reference.HasProperty("_UseEmission") && reference.GetFloat("_UseEmission") > 0.5f)
            {
                DrawColor(mats, reference, "_EmissionColor", "Couleur (HDR, au-dessus du seuil du bloom = brille)", true);
                DrawTexture(mats, reference, "_EmissionMap", "Masque (blanc = brille)");
            }
        }

        if (reference.HasProperty("_Outline") &&
            Section("Contour", "Contour (personnages)", "Trait autour des personnages, avec une couleur par partie (cheveux, peau, vêtements). À laisser coupé sur le décor."))
            DrawOutlineSettings(mats);
    }

    // =====================================================================
    // Mise en page : aide repliable et sections a deplier (memorisees d'une session a l'autre)
    // =====================================================================

    private const string HelpPrefKey = "ShaderTools_ShowHelp";
    private static bool ShowHelp => EditorPrefs.GetBool(HelpPrefKey, true);

    private static bool Section(string key, string title, string summary)
    {
        string pref = "ShaderTools_Section_" + key;
        bool open = EditorPrefs.GetBool(pref, false);
        EditorGUILayout.Space(4);
        bool now = EditorGUILayout.BeginFoldoutHeaderGroup(open, new GUIContent(title, summary));
        EditorGUILayout.EndFoldoutHeaderGroup();
        if (now != open) EditorPrefs.SetBool(pref, now);
        if (now && ShowHelp) EditorGUILayout.LabelField(summary, EditorStyles.wordWrappedMiniLabel);
        return now;
    }

    private static void TabHelp(string text)
    {
        if (ShowHelp) EditorGUILayout.HelpBox(text, MessageType.Info);
    }


    // =====================================================================
    // Contour des personnages : une "partie" par material (cheveux, peau, vetements) avec sa couleur,
    // masque peau pour les materials qui melangent peau et vetements, objets sans contour (yeux...).
    // =====================================================================

    private const string NoOutlineSuffix = "_SansContour";

    private struct OutlineZone
    {
        public string label;
        public Color color;
        public float outerFade;
    }

    // Index = valeur de _OutlineZone (0 = personnalise : l'outil ne change rien)
    private static readonly OutlineZone[] OutlineZones =
    {
        new OutlineZone { label = "Personnalise" },
        new OutlineZone { label = "Cheveux", color = new Color(0.45f, 0.4f, 0.45f), outerFade = 0f },
        new OutlineZone { label = "Peau", color = new Color(0.8f, 0.55f, 0.5f), outerFade = 0f },
        // Gris bleu, et bord exterieur adouci : les vetements clairs ne sont pas cernes de sombre
        new OutlineZone { label = "Vetements", color = new Color(0.42f, 0.47f, 0.62f), outerFade = 0.6f },
        new OutlineZone { label = "Peau + vetements (masque)", color = new Color(0.42f, 0.47f, 0.62f), outerFade = 0.6f },
        // Contour de peau, eclairage en sphere et pas d'ombres portees (ToonFaceNormals.cs)
        new OutlineZone { label = "Visage", color = new Color(0.8f, 0.55f, 0.5f), outerFade = 0f },
    };

    private const int FaceZone = 5;
    // Recul du contour du visage : le cache sur le rebord des paupieres (l'oeil est enfonce d'environ 1 cm)
    // tout en le gardant sur le menton et les joues
    private const float FaceZOffset = 0.02f;

    private static bool IsNoOutlineVariant(Material mat) => mat.name.EndsWith(NoOutlineSuffix);

    private static int GetOutlineZone(Material mat) =>
        mat.HasProperty("_OutlineZone") ? Mathf.Clamp(Mathf.RoundToInt(mat.GetFloat("_OutlineZone")), 0, OutlineZones.Length - 1) : 0;

    private void DrawOutlineSettings(List<Material> allMats)
    {
        if (!allMats[0].HasProperty("_Outline")) return;

        // Les variantes "sans contour" (yeux...) ne suivent pas les reglages du personnage
        var mats = allMats.Where(m => !IsNoOutlineVariant(m)).ToList();
        if (mats.Count > 0)
        {
            Material reference = mats[0];
            // Le drawer [ToonOutline] du shader active / coupe la passe Outline en meme temps que la valeur
            DrawToggle(mats, reference, "_Outline", "Activer");
            if (reference.GetFloat("_Outline") > 0.5f)
            {
                DrawOutlineZones(mats);
                EditorGUILayout.LabelField("Tout le personnage", EditorStyles.miniBoldLabel);
                DrawFloatSlider(mats, reference, "_OutlineTextureTint", "Teinte par la texture", 0f, 1f);
                DrawFloatSlider(mats, reference, "_OutlineDistance", "Distance de reference (m)", 0.5f, 20f);
                // Une surface simple face (tissu, meche) laisse voir la coque du contour quand on la voit de dos :
                // l'afficher des deux cotes la recouvre (et bouche les trous du vetement).
                if (reference.HasProperty("_Cull"))
                {
                    bool twoSided = reference.GetFloat("_Cull") < 0.5f;
                    EditorGUI.BeginChangeCheck();
                    twoSided = EditorGUILayout.Toggle(new GUIContent("Deux faces (tissus, meches)",
                        "A cocher si des taches sombres apparaissent sur l'interieur des vetements ou des cheveux vus de dos."), twoSided);
                    if (EditorGUI.EndChangeCheck())
                        SetFloatWithKeywords(mats, "_Cull", twoSided ? 0f : 2f, "Deux faces");
                }
                DrawOutlineNormalsButton();
            }
        }
        DrawNoOutlineObjects();
    }

    // Une ligne par material pour choisir sa partie, puis les reglages de chaque partie
    private void DrawOutlineZones(List<Material> mats)
    {
        EditorGUILayout.LabelField("Partie de chaque material (survoler une ligne l'allume dans la vue Scene)", EditorStyles.miniBoldLabel);
        var labels = OutlineZones.Select(z => z.label).ToArray();
        // Une ligne par material : vignette de sa texture, nom court (sans le debut commun a tous), et sa partie
        var shortNames = GetShortNames(mats);
        for (int i = 0; i < mats.Count; i++)
        {
            var mat = mats[i];
            var baseMap = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
            var row = EditorGUILayout.BeginHorizontal();
            DrawMaterialThumbnail(GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32), GUILayout.Height(32)), mat, baseMap);
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(new GUIContent(shortNames[i], mat.name + (baseMap != null ? "\nTexture : " + baseMap.name : "")), EditorStyles.boldLabel);
            int zone = GetOutlineZone(mat);
            int previousZone = zone;
            EditorGUI.BeginChangeCheck();
            zone = EditorGUILayout.Popup(zone, labels);
            if (EditorGUI.EndChangeCheck())
            {
                SetOutlineZone(mat, zone, mats);
                if ((previousZone == FaceZone) != (zone == FaceZone))
                    SetFaceMarks(CollectFaceParts(new[] { mat }, false, out _), zone == FaceZone);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            TrackHover(row, mat);
        }

        // Peau : materials "Peau" + la partie peau (masque) des materials "Peau + vetements"
        var mixed = mats.Where(m => GetOutlineZone(m) == 4).ToList();
        var faces = mats.Where(m => GetOutlineZone(m) == FaceZone).ToList();
        DrawZoneSettings("Cheveux", mats.Where(m => GetOutlineZone(m) == 1).ToList(), new List<Material>());
        DrawZoneSettings("Visage", faces, new List<Material>(), true);
        if (faces.Count > 0) DrawFaceStatus(faces);
        DrawZoneSettings("Peau", mats.Where(m => GetOutlineZone(m) == 2).ToList(), mixed);
        DrawZoneSettings("Vetements", mats.Where(m => GetOutlineZone(m) == 3 || GetOutlineZone(m) == 4).ToList(), new List<Material>());
        DrawZoneSettings("Personnalise", mats.Where(m => GetOutlineZone(m) == 0).ToList(), new List<Material>());

        // Materials peau + vetements : il leur faut un masque pour savoir ou est la peau
        if (mixed.Count > 0)
        {
            DrawTexture(mixed, mixed[0], "_OutlineSkinMask", "Masque peau (blanc = peau)");
            var missing = mixed.Where(m => m.GetTexture("_OutlineSkinMask") == null).ToList();
            if (missing.Count > 0)
                EditorGUILayout.HelpBox("Sans masque, la couleur et la largeur de la peau ne sont pas utilisees sur ces materials.", MessageType.Warning);
            if (GUILayout.Button(new GUIContent(missing.Count > 0 ? "Creer le masque peau (detection automatique)" : "Recreer le masque peau",
                "Cherche dans la texture les pixels de la couleur de la peau. Le masque (PNG) peut etre retouche dans un logiciel de dessin.")))
            {
                foreach (var mat in mixed)
                {
                    var mask = CreateSkinMask(mat.GetTexture("_BaseMap") as Texture2D, rampFolder);
                    if (mask == null) continue;
                    Undo.RecordObject(mat, "Masque peau");
                    mat.SetTexture("_OutlineSkinMask", mask);
                    EditorUtility.SetDirty(mat);
                }
                SceneView.RepaintAll();
            }
        }
    }

    // Reglages d'une partie, partages par tous ses materials. "skinOfMixed" : materials peau + vetements, dont seules
    // la couleur et la largeur de la peau (masque) suivent cette partie ; le recul et la silhouette sont ceux des vetements.
    private void DrawZoneSettings(string label, List<Material> main, List<Material> skinOfMixed, bool face = false)
    {
        if (main.Count == 0 && skinOfMixed.Count == 0) return;
        // Survoler le bloc d'une partie allume tous ses materials dans la vue Scene
        var block = EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
        EditorGUI.indentLevel++;
        DrawZoneValue(main, "_OutlineColor", skinOfMixed, "_OutlineSkinColor", label, "Couleur", true, 0f, 0f);
        DrawZoneValue(main, "_OutlineWidth", skinOfMixed, "_OutlineSkinWidth", label, "Largeur (pixels en 1080p)", false, 0f, 8f);
        if (main.Count > 0)
        {
            DrawZoneValue(main, "_OutlineZOffset", null, null, label, "Recul (m)", false, 0f, 0.05f);
            DrawZoneValue(main, "_OutlineOuterFade", null, null, label, "Silhouette exterieure plus claire", false, 0f, 1f);
        }
        // Visage : 1 = s'eclaire comme une forme ronde (une seule limite d'ombre), 0 = selon son relief
        if (face && main[0].HasProperty("_FaceSphere"))
        {
            DrawZoneValue(main, "_FaceSphere", null, null, label, "Eclairage en sphere", false, 0f, 1f);
            DrawZoneValue(main, "_ReceiveShadowStrength", null, null, label, "Ombres portees (meches, tete)", false, 0f, 1f);
        }
        EditorGUI.indentLevel--;
        EditorGUILayout.EndVertical();
        foreach (var mat in main.Concat(skinOfMixed))
            TrackHover(block, mat);
    }

    // =====================================================================
    // Liste des parties : noms courts, vignettes, et surbrillance dans la vue Scene des materials survoles
    // (dessin d'editeur uniquement : rien n'est ajoute a la scene ni au jeu)
    // =====================================================================

    // Noms courts : le nom de la texture est souvent le plus parlant ("..._Hair_Diffuse"), sinon celui du material.
    // Les morceaux communs a toute la liste, au debut et a la fin, sont retires ("Avatar_..._Tex_", "_Diffuse").
    public static List<string> GetShortNames(List<Material> mats)
    {
        if (mats.Count == 1) return new List<string> { StripToolPrefix(mats[0].name) };
        var texNames = mats.Select(m => m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null ? m.GetTexture("_BaseMap").name : null).ToList();
        var texShort = StripCommonParts(texNames.Select(n => n ?? "").ToList());
        var matShort = StripCommonParts(mats.Select(m => StripToolPrefix(m.name)).ToList());
        var names = new List<string>();
        for (int i = 0; i < mats.Count; i++)
        {
            // Texture partagee par deux materials (ex. corps et ruban) : son nom ne les distingue pas
            bool textureNameUnique = texNames[i] != null && !texShort.Where((t, j) => j != i && texNames[j] != null).Contains(texShort[i]);
            string name = textureNameUnique ? texShort[i] : matShort[i];
            names.Add(string.IsNullOrEmpty(name) ? mats[i].name : name);
        }
        return names;
    }

    // Retire les morceaux (separes par "_" ou un espace) communs au debut et a la fin de tous les noms,
    // en gardant au moins un morceau a chacun
    private static List<string> StripCommonParts(List<string> names)
    {
        var parts = names.Select(n => n.Split('_', ' ')).ToList();
        int minLength = parts.Min(p => p.Length);
        int start = 0;
        while (start < minLength - 1 && parts.All(p => p[start] == parts[0][start])) start++;
        int end = 0;
        while (end < minLength - 1 - start && parts.All(p => p[p.Length - 1 - end] == parts[0][parts[0].Length - 1 - end])) end++;
        return parts.Select(p => string.Join("_", p.Skip(start).Take(p.Length - start - end))).ToList();
    }

    private void DrawMaterialThumbnail(Rect rect, Material mat, Texture baseMap)
    {
        // Un seul rectangle reserve, dessin immediat dedans : l'apercu peut arriver entre deux passes sans casser la mise en page
        var preview = baseMap != null ? AssetPreview.GetAssetPreview(baseMap) : AssetPreview.GetAssetPreview(mat);
        if (preview == null && AssetPreview.IsLoadingAssetPreviews()) Repaint();
        if (preview != null) GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
        else if (mat.HasProperty("_BaseColor")) EditorGUI.DrawRect(rect, mat.GetColor("_BaseColor"));
    }

    private HashSet<Material> hoveredMaterials;
    private static HashSet<Material> highlighted;
    private static Material highlightVisible, highlightHidden;
    private static Mesh bakedMesh;

    // Pendant la passe Repaint, la zone survolee par la souris ajoute son material a la surbrillance
    private void TrackHover(Rect area, Material mat)
    {
        if (Event.current.type != EventType.Repaint || !area.Contains(Event.current.mousePosition)) return;
        if (hoveredMaterials == null) hoveredMaterials = new HashSet<Material>();
        hoveredMaterials.Add(mat);
    }

    private static void SetHighlight(HashSet<Material> mats)
    {
        bool wasEmpty = highlighted == null || highlighted.Count == 0, isEmpty = mats == null || mats.Count == 0;
        if (wasEmpty && isEmpty) return;
        if (!wasEmpty && !isEmpty && highlighted.SetEquals(mats)) return;
        highlighted = isEmpty ? null : mats;
        SceneView.RepaintAll();
    }

    private static Material CreateHighlightMaterial(Color color, CompareFunction zTest)
    {
        // Shader des Handles d'Unity : couleur unie, melange, test de profondeur reglables
        var mat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_Cull", (int)CullMode.Off);
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_ZTest", (int)zTest);
        mat.SetFloat("_ZBias", -1f); // juste devant la surface, sans scintillement
        mat.SetColor("_Color", color);
        return mat;
    }

    private void OnSceneGUI(SceneView view)
    {
        if (highlighted == null || Event.current.type != EventType.Repaint) return;
        if (highlightVisible == null) highlightVisible = CreateHighlightMaterial(new Color(0.2f, 0.9f, 1f, 0.55f), CompareFunction.LessEqual);
        // Parties cachees derriere d'autres objets (dessous d'une jupe, oeil sous un voile) : plus discretes
        if (highlightHidden == null) highlightHidden = CreateHighlightMaterial(new Color(0.2f, 0.9f, 1f, 0.15f), CompareFunction.Greater);

        foreach (var r in GetSelectedRenderers())
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            var shared = r.sharedMaterials;
            if (!shared.Any(m => m != null && highlighted.Contains(m))) continue;

            Mesh mesh;
            if (r is SkinnedMeshRenderer skinned)
            {
                // Maillage anime : sa pose actuelle. Avec useScale, la pose cuite se place avec la transformation
                // complete de l'objet (echelle comprise : modeles importes en centimetres, echelle 100...)
                if (skinned.sharedMesh == null) continue;
                if (bakedMesh == null) bakedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                skinned.BakeMesh(bakedMesh, true);
                mesh = bakedMesh;
            }
            else if (r.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                mesh = filter.sharedMesh;
            else continue;
            Matrix4x4 matrix = r.transform.localToWorldMatrix;

            // Seulement les sous-maillages qui utilisent ce material (ex. les cheveux dans le maillage du corps)
            for (int i = 0; i < shared.Length && i < mesh.subMeshCount; i++)
            {
                if (shared[i] == null || !highlighted.Contains(shared[i])) continue;
                highlightHidden.SetPass(0);
                Graphics.DrawMeshNow(mesh, matrix, i);
                highlightVisible.SetPass(0);
                Graphics.DrawMeshNow(mesh, matrix, i);
            }
        }
    }

    private void DrawZoneValue(List<Material> main, string mainProperty, List<Material> secondary, string secondaryProperty,
        string zoneLabel, string label, bool isColor, float min, float max)
    {
        secondary = secondary ?? new List<Material>();
        var first = main.Count > 0 ? main[0] : secondary[0];
        string firstProperty = main.Count > 0 ? mainProperty : secondaryProperty;
        // Valeurs differentes entre les materials de la partie : affiche "-" comme l'Inspector
        bool mixedValues = main.Select(m => isColor ? (object)m.GetColor(mainProperty) : m.GetFloat(mainProperty))
            .Concat(secondary.Select(m => isColor ? (object)m.GetColor(secondaryProperty) : m.GetFloat(secondaryProperty)))
            .Distinct().Count() > 1;
        EditorGUI.showMixedValue = mixedValues;
        EditorGUI.BeginChangeCheck();
        Color color = default;
        float value = 0f;
        if (isColor) color = EditorGUILayout.ColorField(label, first.GetColor(firstProperty));
        else value = EditorGUILayout.Slider(label, first.GetFloat(firstProperty), min, max);
        EditorGUI.showMixedValue = false;
        if (!EditorGUI.EndChangeCheck()) return;

        void Apply(Material mat, string property)
        {
            Undo.RecordObject(mat, label + " " + zoneLabel);
            if (isColor) mat.SetColor(property, color);
            else mat.SetFloat(property, value);
            EditorUtility.SetDirty(mat);
        }
        foreach (var mat in main) Apply(mat, mainProperty);
        foreach (var mat in secondary) Apply(mat, secondaryProperty);
        SceneView.RepaintAll();
    }

    public static void SetOutlineZone(Material mat, int zone, List<Material> others)
    {
        Undo.RecordObject(mat, "Partie du contour");
        int previousZone = GetOutlineZone(mat);
        bool wasFace = previousZone == FaceZone;
        mat.SetFloat("_OutlineZone", zone);
        if (zone > 0)
        {
            // Reprend les reglages deja choisis pour cette partie sur un autre material, sinon ceux par defaut
            // (la largeur et le recul restent alors ceux du material)
            int sameZone = zone == 4 ? 3 : zone;
            var sibling = others.FirstOrDefault(m => m != mat && (GetOutlineZone(m) == sameZone || (sameZone == 3 && GetOutlineZone(m) == 4)));
            if (sibling != null)
            {
                mat.SetColor("_OutlineColor", sibling.GetColor("_OutlineColor"));
                mat.SetFloat("_OutlineWidth", sibling.GetFloat("_OutlineWidth"));
                mat.SetFloat("_OutlineZOffset", sibling.GetFloat("_OutlineZOffset"));
                mat.SetFloat("_OutlineOuterFade", sibling.GetFloat("_OutlineOuterFade"));
            }
            else if (zone == FaceZone)
            {
                // Premier visage : meme trait que la peau (la sienne s'il etait deja "Peau"), mais recule pour ne
                // pas souligner les paupieres
                var skin = previousZone == 2 ? mat : others.FirstOrDefault(m => m != mat && GetOutlineZone(m) == 2);
                mat.SetColor("_OutlineColor", skin != null ? skin.GetColor("_OutlineColor") : OutlineZones[zone].color);
                if (skin != null) mat.SetFloat("_OutlineWidth", skin.GetFloat("_OutlineWidth"));
                mat.SetFloat("_OutlineZOffset", Mathf.Max(skin != null ? skin.GetFloat("_OutlineZOffset") : 0f, FaceZOffset));
                mat.SetFloat("_OutlineOuterFade", skin != null ? skin.GetFloat("_OutlineOuterFade") : OutlineZones[zone].outerFade);
            }
            else
            {
                mat.SetColor("_OutlineColor", OutlineZones[zone].color);
                mat.SetFloat("_OutlineOuterFade", OutlineZones[zone].outerFade);
            }
            if (zone == 4)
            {
                var skin = others.FirstOrDefault(m => m != mat && GetOutlineZone(m) == 2);
                mat.SetColor("_OutlineSkinColor", skin != null ? skin.GetColor("_OutlineColor") : OutlineZones[2].color);
                mat.SetFloat("_OutlineSkinWidth", skin != null ? skin.GetFloat("_OutlineWidth") : mat.GetFloat("_OutlineWidth"));
            }
        }
        // Visage : eclairage en sphere et pas d'ombres portees (les meches y feraient des taches dures),
        // comme sur un autre visage deja regle. En quittant cette partie, le material retrouve un eclairage normal.
        if (mat.HasProperty("_FaceSphere"))
        {
            if (zone == FaceZone)
            {
                var face = others.FirstOrDefault(m => m != mat && GetOutlineZone(m) == FaceZone);
                mat.SetFloat("_FaceSphere", face != null ? face.GetFloat("_FaceSphere") : 1f);
                mat.SetFloat("_ReceiveShadowStrength", face != null ? face.GetFloat("_ReceiveShadowStrength") : 0f);
            }
            else if (wasFace)
            {
                mat.SetFloat("_FaceSphere", 0f);
                mat.SetFloat("_ReceiveShadowStrength", 1f);
            }
        }
        EditorUtility.SetDirty(mat);
        SceneView.RepaintAll();
    }

    // Sous-maillages des objets selectionnes qui utilisent ces materials de visage (ou leur variante sans contour :
    // yeux...), regroupes par fichier modele. "onlyMissing" : seulement ceux dont l'eclairage n'est pas encore calcule.
    private Dictionary<string, List<string>> CollectFaceParts(IEnumerable<Material> faceMats, bool onlyMissing, out bool notModel)
    {
        var mats = new HashSet<Material>(faceMats);
        var byModel = new Dictionary<string, List<string>>();
        notModel = false;
        foreach (var r in GetSelectedRenderers())
        {
            if (r == null) continue;
            var shared = r.sharedMaterials;
            Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                : r.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
            if (mesh == null) continue;
            string path = null;
            for (int i = 0; i < shared.Length && i < mesh.subMeshCount; i++)
            {
                var m = shared[i];
                if (m == null || !(mats.Contains(m) || (IsNoOutlineVariant(m) && m.parent != null && mats.Contains(m.parent)))) continue;
                if (path == null) path = AssetDatabase.GetAssetPath(mesh);
                // Maillage cree par script ou enregistre a part (.asset) : pas d'import, donc pas de calcul possible
                if (string.IsNullOrEmpty(path) || !(AssetImporter.GetAtPath(path) is ModelImporter)) { notModel = true; break; }
                string part = ToonFaceNormals.PartName(mesh, i);
                if (onlyMissing && ToonFaceNormals.IsMarked(path, part)) continue;
                if (!byModel.TryGetValue(path, out var parts)) byModel[path] = parts = new List<string>();
                if (!parts.Contains(part)) parts.Add(part);
            }
        }
        return byModel;
    }

    // Reimporte les modeles apres le dessin de la fenetre (un import pendant OnGUI casserait la mise en page)
    private static void SetFaceMarks(Dictionary<string, List<string>> byModel, bool marked)
    {
        if (byModel.Count == 0) return;
        EditorApplication.delayCall += () =>
        {
            foreach (var pair in byModel)
                ToonFaceNormals.SetMarked(pair.Key, pair.Value, marked);
            SceneView.RepaintAll();
        };
    }

    private void DrawFaceStatus(List<Material> faces)
    {
        var missing = CollectFaceParts(faces, true, out bool notModel);
        if (notModel)
            EditorGUILayout.HelpBox("Visage : un des objets n'a pas de fichier modele (FBX...) : il garde son eclairage selon le relief.", MessageType.Info);
        if (missing.Count == 0) return;
        EditorGUILayout.HelpBox("Visage : l'eclairage en sphere n'est pas encore calcule pour ce modele (calcul a l'import, " +
            "le fichier du modele n'est pas modifie).", MessageType.Warning);
        if (GUILayout.Button("Calculer l'eclairage du visage"))
            SetFaceMarks(missing, true);
    }


    // Masque peau : les pixels qui ont la "chaleur" (rouge - bleu, vert - bleu) de la peau et sont assez clairs.
    // La teinte de reference est la mediane des pixels clairs et chauds de la texture elle-meme.
    public static Texture2D CreateSkinMask(Texture2D source, string folder)
    {
        if (source == null) return null;
        int w = source.width, h = source.height;
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source, rt);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var readable = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        var pixels = readable.GetPixels();
        DestroyImmediate(readable);

        // Teinte de reference : un pixel sur 16 suffit (une texture 4096 en a 16 millions a trier sinon)
        var warm = new List<Color>();
        for (int i = 0; i < pixels.Length; i += 16)
        {
            var c = pixels[i];
            if (c.r > 0.75f && c.r - c.b > 0.08f && c.r - c.b < 0.35f) warm.Add(c);
        }
        if (warm.Count == 0)
        {
            EditorUtility.DisplayDialog("Masque peau", "Aucune couleur de peau trouvee dans '" + source.name + "'.", "OK");
            return null;
        }
        float Median(IEnumerable<float> values) { var sorted = values.OrderBy(v => v).ToList(); return sorted[sorted.Count / 2]; }
        var tone = new Color(Median(warm.Select(c => c.r)), Median(warm.Select(c => c.g)), Median(warm.Select(c => c.b)));
        float toneWarm = tone.r - tone.b, toneGreen = tone.g - tone.b, toneLum = (tone.r + tone.g + tone.b) / 3f;

        var mask = new Color[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            float dw = (c.r - c.b) - toneWarm, dg = (c.g - c.b) - toneGreen;
            float hue = Mathf.Sqrt(dw * dw + 2f * dg * dg);
            float darker = toneLum - (c.r + c.g + c.b) / 3f;
            float s = (1f - Smooth(0.05f, 0.1f, hue)) * (1f - Smooth(0.15f, 0.3f, darker));
            mask[i] = new Color(s, s, s, 1f);
        }

        string path = folder + "/MasquePeau_" + source.name + ".png";
        EnsureFolderExists(folder);
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.SetPixels(mask);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture = false; // un masque, pas une couleur
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // smoothstep du HLSL (Mathf.SmoothStep n'est pas la meme fonction)
    private static float Smooth(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    // Objets sans contour (yeux, reflets des yeux...) : l'objet recoit une variante de son material
    // ("<nom>_SansContour") qui suit tous les autres reglages du material d'origine.
    private void DrawNoOutlineObjects()
    {
        var renderers = Selection.gameObjects.Select(go => go.GetComponent<Renderer>()).Where(r => r != null).ToList();
        if (renderers.Count == 0) return;
        bool anyCut = renderers.Any(r => r.sharedMaterials.Any(m => m != null && IsNoOutlineVariant(m)));
        bool anyOutlined = renderers.Any(r => r.sharedMaterials.Any(m => m != null && !IsNoOutlineVariant(m)
            && m.HasProperty("_Outline") && m.GetFloat("_Outline") > 0.5f));
        if (!anyCut && !anyOutlined) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Objets sans contour (yeux, sourcils...)", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        if (anyOutlined && GUILayout.Button(new GUIContent("Couper sur " + renderers.Count + " objet(s) selectionne(s)",
            "Selectionner seulement les objets concernes (ex. les iris) dans la Hierarchy.")))
            SetObjectsOutline(renderers, false);
        if (anyCut && GUILayout.Button("Remettre le contour"))
            SetObjectsOutline(renderers, true);
        EditorGUILayout.EndHorizontal();
    }

    private void SetObjectsOutline(List<Renderer> renderers, bool outlined)
    {
        foreach (var r in renderers)
        {
            var materials = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var mat = materials[i];
                if (mat == null) continue;
                Material replacement = null;
                if (outlined && IsNoOutlineVariant(mat))
                    replacement = mat.parent;
                else if (!outlined && !IsNoOutlineVariant(mat) && mat.HasProperty("_Outline"))
                    replacement = GetOrCreateNoOutlineVariant(mat);
                if (replacement == null) continue;
                materials[i] = replacement;
                changed = true;
            }
            if (!changed) continue;
            Undo.RecordObject(r, outlined ? "Remettre le contour" : "Couper le contour");
            r.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        }
        sceneDataDirty = true;
        SceneView.RepaintAll();
    }

    public static Material GetOrCreateNoOutlineVariant(Material mat)
    {
        string matPath = AssetDatabase.GetAssetPath(mat);
        // Material cree par script (pas de fichier) ou range dans un package (lecture seule) : pas de variante possible
        if (string.IsNullOrEmpty(matPath) || !matPath.StartsWith("Assets"))
        {
            EditorUtility.DisplayDialog("Couper le contour", "Le material '" + mat.name + "' n'est pas un fichier du dossier Assets : " +
                "enregistre-le d'abord dans le projet (ou convertis-le).", "OK");
            return null;
        }
        // Material range dans un modele (.fbx, .glb) : la variante va dans le dossier du modele
        string folder = Path.GetDirectoryName(matPath).Replace('\\', '/');
        string path = folder + "/" + mat.name + NoOutlineSuffix + ".mat";
        var variant = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (variant != null) return variant;

        variant = new Material(mat);
        AssetDatabase.CreateAsset(variant, path);
        variant.parent = mat;
        variant.SetFloat("_Outline", 0f);
        variant.SetShaderPassEnabled(ToonOutlineDrawer.PassLightMode, false);
        EditorUtility.SetDirty(variant);
        AssetDatabase.SaveAssetIfDirty(variant);
        return variant;
    }

    // Normales lissees : bouchent les trous du contour aux aretes vives (calculees a l'import du modele)
    private void DrawOutlineNormalsButton()
    {
        var models = new HashSet<string>();
        foreach (var go in Selection.gameObjects)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : r.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
                string path = mesh != null ? AssetDatabase.GetAssetPath(mesh) : null;
                if (!string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter)
                    models.Add(path);
            }
        }
        if (models.Count == 0) return;

        bool allMarked = models.All(ToonOutlineNormals.IsMarked);
        EditorGUILayout.HelpBox(allMarked
            ? "Normales lissees actives sur le modele : le contour reste ferme aux aretes vives."
            : "Si le contour a des trous aux aretes vives (coutures, accessoires), lisser les normales du modele. " +
              "Le fichier du modele n'est pas modifie : c'est calcule a l'import.", MessageType.None);
        if (GUILayout.Button(allMarked ? "Retirer les normales lissees" : "Lisser les normales du contour"))
        {
            foreach (string path in models)
                ToonOutlineNormals.SetMarked(path, !allMarked);
            SceneView.RepaintAll();
        }
    }

    // Les proprietes avec [Toggle] / [KeywordEnum] dans le shader pilotent un mot-cle : apres avoir change
    // la valeur, ApplyMaterialPropertyDrawers active/desactive le mot-cle correspondant (comme l'Inspector).
    private void DrawToggle(List<Material> targets, Material reference, string property, string label)
    {
        if (!reference.HasProperty(property)) return;
        bool current = reference.GetFloat(property) > 0.5f;
        EditorGUI.BeginChangeCheck();
        bool newValue = EditorGUILayout.Toggle(label, current);
        if (EditorGUI.EndChangeCheck())
            SetFloatWithKeywords(targets, property, newValue ? 1f : 0f, label);
    }

    private void DrawPopup(List<Material> targets, Material reference, string property, string label, string[] options)
    {
        if (!reference.HasProperty(property)) return;
        int current = Mathf.RoundToInt(reference.GetFloat(property));
        EditorGUI.BeginChangeCheck();
        int newValue = EditorGUILayout.Popup(label, current, options);
        if (EditorGUI.EndChangeCheck())
            SetFloatWithKeywords(targets, property, newValue, label);
    }

    private void SetFloatWithKeywords(List<Material> targets, string property, float value, string label)
    {
        foreach (var mat in targets)
        {
            if (!mat.HasProperty(property)) continue;
            Undo.RecordObject(mat, "Modifier " + label);
            mat.SetFloat(property, value);
            MaterialEditor.ApplyMaterialPropertyDrawers(mat);
            EditorUtility.SetDirty(mat);
        }
        SceneView.RepaintAll();
    }

    // Comme DrawTexture, mais regle aussi la disposition (ramp simple ou atlas) lue par le shader
    private void DrawRampTextureField(List<Material> targets, Material reference)
    {
        EditorGUI.BeginChangeCheck();
        var newValue = (Texture2D)EditorGUILayout.ObjectField("Ramp / atlas (onglet Ramp)", reference.GetTexture("_RampMap"), typeof(Texture2D), false);
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var mat in targets)
            {
                Undo.RecordObject(mat, "Modifier la ramp");
                mat.SetTexture("_RampMap", newValue);
                ApplyRampLayout(mat, newValue);
                EditorUtility.SetDirty(mat);
            }
            SceneView.RepaintAll();
        }
    }

    private void DrawTexture(List<Material> targets, Material reference, string property, string label)
    {
        if (!reference.HasProperty(property)) return;
        Texture current = reference.GetTexture(property);
        EditorGUI.BeginChangeCheck();
        Texture newValue = (Texture)EditorGUILayout.ObjectField(label, current, typeof(Texture2D), false);
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var mat in targets)
            {
                if (!mat.HasProperty(property)) continue;
                Undo.RecordObject(mat, "Modifier " + label);
                mat.SetTexture(property, newValue);
                EditorUtility.SetDirty(mat);
            }
            SceneView.RepaintAll();
        }
    }

    // =====================================================================
    // Prereglages Toon Advanced : une ramp (generee si absente) + les reglages de rim / speculaire
    // =====================================================================

    private class ToonPreset
    {
        public string label;
        public string tooltip;
        public string rampName;
        public Color[] rampColors;
        public float[] rampTimes;
        public bool rampHardEdges;
        public float shadowSaturation;
        public float threshold;
        public float smoothness;
        public float ambient;
        public int rimMode;
        public Color rimColor;
        public float rimWidth;
        public float rimSoftness;
        public float rimAlign;
        public float rimTint;
        public float rimDepthThreshold;
        public Color specularColor;
        public float specularSize;
        public float specularSoftness;
    }

    private static readonly ToonPreset[] ToonPresets =
    {
        new ToonPreset
        {
            label = "Doux (Genshin)",
            tooltip = "Ombres lavande douces, lisere fin teinte par la couleur de l'objet, reflet discret (a activer sur les objets brillants).",
            rampName = "Ramp_Doux",
            rampColors = new[] { new Color(0.62f, 0.60f, 0.80f), new Color(0.74f, 0.71f, 0.88f), new Color(1f, 0.97f, 0.94f), Color.white },
            rampTimes = new[] { 0.2f, 0.44f, 0.52f, 1f },
            rampHardEdges = false,
            shadowSaturation = 0.35f,
            threshold = 0.5f, smoothness = 0.04f, ambient = 0.15f,
            rimMode = 2, rimColor = new Color(1f, 0.97f, 0.9f), rimWidth = 0.25f, rimSoftness = 0.05f, rimAlign = 0.9f, rimTint = 0.6f, rimDepthThreshold = 0.15f,
            specularColor = new Color(0.35f, 0.35f, 0.35f), specularSize = 0.03f, specularSoftness = 0.02f
        },
        new ToonPreset
        {
            label = "Contraste (ZZZ)",
            tooltip = "Ombres bleu-violet franches en 2 tons, lisere blanc marque, reflet net (a activer sur les objets brillants).",
            rampName = "Ramp_Contraste",
            rampColors = new[] { new Color(0.6f, 0.62f, 0.82f), Color.white },
            rampTimes = new[] { 0.5f, 1f },
            rampHardEdges = true,
            shadowSaturation = 0.25f,
            threshold = 0.5f, smoothness = 0.01f, ambient = 0.1f,
            rimMode = 2, rimColor = new Color(1.3f, 1.3f, 1.3f), rimWidth = 0.4f, rimSoftness = 0.02f, rimAlign = 0.8f, rimTint = 0.2f, rimDepthThreshold = 0.15f,
            specularColor = new Color(0.6f, 0.6f, 0.6f), specularSize = 0.04f, specularSoftness = 0.005f
        },
        new ToonPreset
        {
            label = "Simple (2 tons)",
            tooltip = "Equivalent du CelShading actuel : pas de ramp, pas de lisere.",
            rampName = null,
            threshold = 0.5f, smoothness = 0.05f, ambient = 0.1f,
            rimMode = 0,
            specularColor = new Color(0.35f, 0.35f, 0.35f), specularSize = 0.03f, specularSoftness = 0.02f
        },
    };

    [SerializeField] private bool presetForDecor;

    // Point d'entree scriptable (ex: depuis un autre outil ou un script d'automatisation)
    public static void ApplyToonPreset(List<Material> mats, int presetIndex, bool decor = false)
    {
        ApplyToonPreset(mats, ToonPresets[presetIndex], DefaultRampFolder, decor);
    }

    private static void ApplyToonPreset(List<Material> mats, ToonPreset preset, string rampFolder, bool decor)
    {
        Texture2D ramp = null;
        if (preset.rampName != null)
        {
            string rampPath = rampFolder + "/" + preset.rampName + ".png";
            ramp = AssetDatabase.LoadAssetAtPath<Texture2D>(rampPath);
            if (ramp == null)
                ramp = SaveRampTexture(MakeGradient(preset.rampColors, preset.rampTimes, preset.rampHardEdges), rampPath, 256, preset.rampHardEdges);
        }

        Undo.SetCurrentGroupName("Prereglage " + preset.label);
        int undoGroup = Undo.GetCurrentGroup();
        foreach (var mat in mats)
        {
            Undo.RecordObject(mat, "Prereglage " + preset.label);
            mat.SetFloat("_UseRamp", ramp != null ? 1f : 0f);
            if (ramp != null)
            {
                mat.SetTexture("_RampMap", ramp);
                ApplyRampLayout(mat, ramp);
            }
            mat.SetFloat("_ShadowSaturation", preset.shadowSaturation);
            mat.SetFloat("_ShadowThreshold", preset.threshold);
            mat.SetFloat("_ShadowSmoothness", preset.smoothness);
            mat.SetFloat("_AmbientStrength", preset.ambient);
            // Version decor : lisere coupe, mais ses reglages restent prets si on le reactive a la main
            mat.SetFloat("_Rim", decor ? 0f : preset.rimMode);
            if (preset.rimMode > 0)
            {
                mat.SetColor("_RimColor", preset.rimColor);
                mat.SetFloat("_RimWidth", preset.rimWidth);
                mat.SetFloat("_RimSoftness", preset.rimSoftness);
                mat.SetFloat("_RimLightAlign", preset.rimAlign);
                mat.SetFloat("_RimAlbedoTint", preset.rimTint);
                mat.SetFloat("_RimDepthThreshold", preset.rimDepthThreshold);
                if (mat.HasProperty("_RimBlend"))
                    mat.SetFloat("_RimBlend", 0f); // les prereglages utilisent un lisere lumineux
            }
            // Le reflet est DESACTIVE par le prereglage : sur les grandes surfaces planes (murs, sol)
            // il forme une grosse tache floue. On regle juste ses valeurs, a reactiver a la main
            // sur les objets ronds / brillants (balles, metal, plastique...).
            mat.SetFloat("_Specular", 0f);
            mat.SetColor("_SpecularColor", preset.specularColor);
            mat.SetFloat("_SpecularSize", preset.specularSize);
            mat.SetFloat("_SpecularSoftness", preset.specularSoftness);
            MaterialEditor.ApplyMaterialPropertyDrawers(mat);
            EditorUtility.SetDirty(mat);
        }
        Undo.CollapseUndoOperations(undoGroup);
        SceneView.RepaintAll();
    }

    private void DrawFloatSlider(List<Material> targets, Material reference, string property, string label, float min, float max)
    {
        if (!reference.HasProperty(property)) return;
        float current = reference.GetFloat(property);
        EditorGUI.BeginChangeCheck();
        float newValue = EditorGUILayout.Slider(label, current, min, max);
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var mat in targets)
            {
                if (!mat.HasProperty(property)) continue;
                Undo.RecordObject(mat, "Modifier " + label);
                mat.SetFloat(property, newValue);
                EditorUtility.SetDirty(mat);
            }
            SceneView.RepaintAll();
        }
    }

    private void DrawColor(List<Material> targets, Material reference, string property, string label, bool hdr = false)
    {
        if (!reference.HasProperty(property)) return;
        Color current = reference.GetColor(property);
        EditorGUI.BeginChangeCheck();
        Color newValue = EditorGUILayout.ColorField(new GUIContent(label), current, true, false, hdr);
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var mat in targets)
            {
                if (!mat.HasProperty(property)) continue;
                Undo.RecordObject(mat, "Modifier " + label);
                mat.SetColor(property, newValue);
                EditorUtility.SetDirty(mat);
            }
            SceneView.RepaintAll();
        }
    }

    // =====================================================================
    // Onglet 2 : conversion en masse des materials de la scene
    // =====================================================================

    private class MaterialEntry
    {
        public Material material;
        public List<Renderer> renderers = new List<Renderer>();
        public bool selected;
        // Raison pour laquelle ce material n'est pas converti (transparent, decoupe...), null s'il peut l'etre
        public string ignoreReason;
        public bool Ignored => ignoreReason != null;
        public bool alreadyConverted;
        // Textes prepares une seule fois au rescan, au lieu d'etre recalcules a chaque affichage
        public string objectsLabel;
        public string searchText;
    }

    private List<MaterialEntry> materialEntries = new List<MaterialEntry>();
    private List<MaterialEntry> conversionView = new List<MaterialEntry>(); // lignes apres filtre
    [SerializeField] private string conversionFilter = "";
    private float conversionRowHeight = 60f;
    private Shader targetShader;
    private string materialsFolder = DefaultMaterialsFolder;

    private void RefreshMaterialList()
    {
        if (targetShader == null)
        {
            targetShader = Shader.Find(DefaultTargetShader);
        }

        // Les cases cochees survivent au rescan (qui peut maintenant arriver sans clic de l'utilisateur)
        var previouslySelected = new HashSet<Material>(materialEntries.Where(e => e.selected).Select(e => e.material));

        materialEntries.Clear();
        var map = new Dictionary<Material, MaterialEntry>();

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (var r in renderers)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;

                MaterialEntry entry;
                if (!map.TryGetValue(m, out entry))
                {
                    entry = new MaterialEntry { material = m };
                    entry.ignoreReason = GetIgnoreReason(m);
                    entry.alreadyConverted = targetShader != null && m.shader == targetShader;
                    map[m] = entry;
                    materialEntries.Add(entry);
                }
                entry.renderers.Add(r);
            }
        }

        foreach (var entry in materialEntries)
        {
            // Particules, sprites, traits, texte... : ToonAdvanced est fait pour des maillages
            if (entry.ignoreReason == null && entry.renderers.Any(r => !(r is MeshRenderer || r is SkinnedMeshRenderer)))
                entry.ignoreReason = "Particules / sprite / texte";
            entry.selected = previouslySelected.Contains(entry.material) && !entry.Ignored && !entry.alreadyConverted;

            var objNames = entry.renderers.Select(r => r.gameObject.name).Distinct().ToList();
            entry.objectsLabel = string.Join(", ", objNames.Take(3));
            if (objNames.Count > 3) entry.objectsLabel += " (+" + (objNames.Count - 3) + " autres)";
            entry.searchText = (entry.material.name + " " + string.Join(" ", objNames)).ToLowerInvariant();
        }

        materialEntries = materialEntries.OrderBy(e => e.material.name).ToList();
    }

    // ToonAdvanced est opaque et sans decoupe : un material transparent ou decoupe (feuillage, grillage, meches)
    // deviendrait un bloc plein. Couvre URP (_Surface, _AlphaClip), Built-in (_Mode) et la file de rendu.
    private static string GetIgnoreReason(Material m)
    {
        bool transparent = (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f)
            || (m.HasProperty("_Mode") && m.GetFloat("_Mode") >= 2f) || m.renderQueue > 2500;
        if (transparent) return "Transparent";
        bool cutout = (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f) || m.IsKeywordEnabled("_ALPHATEST_ON")
            || (m.HasProperty("_Mode") && Mathf.Approximately(m.GetFloat("_Mode"), 1f)) || m.renderQueue >= 2450;
        if (cutout) return "Decoupe (alpha)";
        return null;
    }

    // Liste "virtualisee" : seules les lignes visibles sont dessinees, des espaces vides de meme hauteur
    // remplacent les autres. IMGUI redessine tout a chaque rafraichissement : sans ca, une liste de
    // plusieurs centaines de lignes rend la fenetre lente. Toutes les lignes doivent avoir la meme hauteur.
    private int virtualFirst;
    private int virtualLast;

    private void DrawVirtualRows(int count, ref float rowHeight, System.Action<int> drawRow)
    {
        // Plage calculee pendant la passe Layout puis reutilisee par les autres passes de la meme image :
        // sinon un coup de molette changerait les lignes dessinees entre deux passes (erreur GUILayout).
        if (Event.current.type == EventType.Layout)
        {
            virtualFirst = Mathf.Clamp(Mathf.FloorToInt(scroll.y / rowHeight) - 1, 0, count);
            virtualLast = Mathf.Min(count, virtualFirst + Mathf.CeilToInt(position.height / rowHeight) + 3);
        }
        int first = Mathf.Min(virtualFirst, count);
        int last = Mathf.Min(virtualLast, count);

        if (first > 0) GUILayout.Space(first * rowHeight);
        float previousY = -1f;
        float measured = -1f;
        for (int i = first; i < last; i++)
        {
            drawRow(i);
            if (Event.current.type == EventType.Repaint)
            {
                float y = GUILayoutUtility.GetLastRect().y;
                if (previousY >= 0f) measured = y - previousY;
                previousY = y;
            }
        }
        if (last < count) GUILayout.Space((count - last) * rowHeight);

        // Hauteur reelle d'une ligne (marges comprises) mesuree a l'affichage : corrige l'estimation de depart
        if (measured > 1f && Mathf.Abs(measured - rowHeight) > 0.5f)
        {
            rowHeight = measured;
            Repaint();
        }
    }

    private void DrawConversionTab()
    {
        TabHelp("Première étape : donne le style toon aux objets de la scène, en gardant leurs textures et couleurs.\n" +
            "1. Laisse « ToonAdvanced » comme shader (le shader principal pour le décor).\n" +
            "2. Coche les materials à convertir, ou « Tout sélectionner ».\n" +
            "3. Clique sur « Convertir la sélection ». Les originaux ne sont pas modifiés, l'onglet Historique permet de revenir en arrière.\n" +
            "Ensuite, règle le style dans l'onglet 2.");

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        targetShader = (Shader)EditorGUILayout.ObjectField(new GUIContent("Shader cible", "ToonAdvanced par défaut"), targetShader, typeof(Shader), false);
        if (EditorGUI.EndChangeCheck())
            sceneDataDirty = true; // le statut "deja converti" depend du shader cible
        if (GUILayout.Button("Rafraichir", GUILayout.Width(80)))
        {
            MarkSceneDataDirty();
        }
        EditorGUILayout.EndHorizontal();

        materialsFolder = EditorGUILayout.TextField("Dossier de sortie", materialsFolder);
        conversionFilter = EditorGUILayout.TextField(new GUIContent("Filtrer", "Nom de material ou d'objet"), conversionFilter);

        if (Event.current.type == EventType.Layout)
        {
            string filter = conversionFilter.Trim().ToLowerInvariant();
            conversionView = filter.Length == 0
                ? materialEntries
                : materialEntries.Where(e => e.searchText.Contains(filter)).ToList();
        }

        EditorGUILayout.Space();

        if (materialEntries.Count == 0)
        {
            EditorGUILayout.HelpBox("Aucun material trouve. Clique sur Rafraichir apres avoir ouvert/modifie la scene.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        // Les deux boutons agissent sur les lignes affichees : filtrer puis "Tout selectionner" = selection ciblee
        if (GUILayout.Button("Tout selectionner (eligibles)"))
        {
            foreach (var e in conversionView)
                e.selected = !e.Ignored && !e.alreadyConverted;
        }
        if (GUILayout.Button("Tout deselectionner"))
        {
            foreach (var e in conversionView)
                e.selected = false;
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField(conversionView.Count + " / " + materialEntries.Count + " materials affiches", EditorStyles.miniLabel);

        EditorGUILayout.Space();
        scroll = EditorGUILayout.BeginScrollView(scroll);

        Color previousBg = GUI.backgroundColor;
        var selectedMats = GetSelectedMaterials();

        var richBoldLabel = new GUIStyle(EditorStyles.boldLabel) { richText = true };

        var rows = conversionView;
        DrawVirtualRows(rows.Count, ref conversionRowHeight, i => DrawConversionRow(rows[i], selectedMats, richBoldLabel, previousBg));

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        int selectedCount = materialEntries.Count(e => e.selected);
        EditorGUI.BeginDisabledGroup(selectedCount == 0 || targetShader == null);
        if (GUILayout.Button("Convertir la selection (" + selectedCount + ")", GUILayout.Height(30)))
        {
            ConvertSelected();
        }
        EditorGUI.EndDisabledGroup();
    }

    private void DrawConversionRow(MaterialEntry entry, HashSet<Material> selectedMats, GUIStyle richBoldLabel, Color previousBg)
    {
        {
            bool isHierarchySelected = selectedMats.Contains(entry.material);
            if (isHierarchySelected)
            {
                // GUI.backgroundColor est multiplie par la texture (sombre) de la boite,
                // donc seule une teinte assez chaude/vive comme l'orange reste visible.
                // On garde ce systeme uniquement pour le surlignage de selection.
                GUI.backgroundColor = new Color(0.9f, 0.55f, 0.1f, 1f);
            }
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = previousBg;

            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(entry.Ignored || entry.alreadyConverted);
            entry.selected = EditorGUILayout.Toggle(entry.selected, GUILayout.Width(20));
            EditorGUI.EndDisabledGroup();

            // Pastille de statut : couleur pleine dessinee directement (pas de multiplication
            // avec une texture sombre), donc le vert "deja converti" reste un vrai vert net.
            Color statusDotColor = entry.alreadyConverted ? new Color(0.35f, 0.85f, 0.35f)
                : entry.Ignored ? new Color(0.55f, 0.55f, 0.55f)
                : new Color(0.4f, 0.65f, 0.95f);
            var dotRect = GUILayoutUtility.GetRect(10, 10, GUILayout.Width(10), GUILayout.Height(10));
            EditorGUI.DrawRect(dotRect, statusDotColor);

            // On reserve TOUJOURS le meme unique controle de layout ici (un seul GetRect),
            // puis on dessine dedans avec des appels immediats (GUI.DrawTexture / EditorGUI.DrawRect)
            // qui ne consomment pas de controle. Sinon, comme l'apercu se charge en asynchrone,
            // le nombre de controles peut changer entre le passage Layout et le passage Repaint
            // et Unity leve une erreur "Invalid GUILayout state".
            var thumbRect = GUILayoutUtility.GetRect(36, 36, GUILayout.Width(36), GUILayout.Height(36));
            Texture2D preview = AssetPreview.GetAssetPreview(entry.material);
            if (preview == null && AssetPreview.IsLoadingAssetPreview(entry.material.GetInstanceID()))
            {
                Repaint();
            }
            if (preview != null)
            {
                GUI.DrawTexture(thumbRect, preview, ScaleMode.ScaleToFit);
            }
            else if (entry.material.HasProperty("_BaseColor"))
            {
                EditorGUI.DrawRect(thumbRect, entry.material.GetColor("_BaseColor"));
            }

            EditorGUILayout.BeginVertical();

            string statusColorHex = entry.alreadyConverted ? "#5FD068"
                : entry.Ignored ? "#9E9E9E"
                : "#64B5F6";
            string status = entry.Ignored ? entry.ignoreReason + " - ignore"
                : entry.alreadyConverted ? "Deja converti"
                : "Pret a convertir";
            if (isHierarchySelected) status += " - SELECTIONNE";
            EditorGUILayout.LabelField(entry.material.name + "  [ <color=" + statusColorHex + ">" + status + "</color> ]", richBoldLabel);

            EditorGUILayout.LabelField("Objets : " + entry.objectsLabel, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Shader actuel : " + (entry.material.shader != null ? entry.material.shader.name : "?"), EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
    }

    private void ConvertSelected()
    {
        if (!materialsFolder.StartsWith("Assets"))
        {
            EditorUtility.DisplayDialog("Dossier invalide", "Le dossier de sortie doit etre dans Assets.", "OK");
            return;
        }
        // Premiere utilisation dans un projet : le dossier n'existe pas encore, on le cree
        EnsureFolderExists(materialsFolder);

        var history = GetOrCreateHistory();

        Undo.SetCurrentGroupName("Convertir vers " + targetShader.name);
        int undoGroup = Undo.GetCurrentGroup();

        int converted = 0;

        foreach (var entry in materialEntries)
        {
            if (!entry.selected || entry.Ignored || entry.alreadyConverted) continue;

            Material original = entry.material;
            // Prefixe propre au shader cible : sans ca, convertir vers un autre shader reutiliserait
            // (et modifierait) les materials M_Cel_ deja utilises par d'autres scenes.
            string newMatPath = materialsFolder + "/" + GetMaterialPrefix(targetShader) + GetSourceName(original) + ".mat";

            Material celMat = AssetDatabase.LoadAssetAtPath<Material>(newMatPath);
            bool isNewMaterial = celMat == null;
            if (isNewMaterial)
            {
                celMat = new Material(targetShader);
                AssetDatabase.CreateAsset(celMat, newMatPath);
                Undo.RegisterCreatedObjectUndo(celMat, "Creer " + celMat.name);
            }
            else
            {
                Undo.RecordObject(celMat, "Modifier " + celMat.name);
                celMat.shader = targetShader;
            }

            CopyBaseColor(original, celMat);

            // Un objet qui brillait (emission du Lit) continue de briller apres conversion
            if (original.IsKeywordEnabled("_EMISSION") && celMat.HasProperty("_UseEmission"))
            {
                celMat.SetFloat("_UseEmission", 1f);
                celMat.SetColor("_EmissionColor", original.GetColor("_EmissionColor"));
                if (original.HasProperty("_EmissionMap"))
                    celMat.SetTexture("_EmissionMap", original.GetTexture("_EmissionMap"));
            }

            // Synchronise les mots-cles avec les valeurs des [Toggle] / [KeywordEnum] du shader
            if (isNewMaterial || celMat.HasProperty("_UseEmission"))
                MaterialEditor.ApplyMaterialPropertyDrawers(celMat);

            EditorUtility.SetDirty(celMat);

            foreach (var r in entry.renderers)
            {
                if (r == null) continue;
                Undo.RecordObject(r, "Assigner " + celMat.name);
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == original)
                    {
                        mats[i] = celMat;
                        RecordHistoryEntry(history, r, i, celMat, original);
                    }
                }
                r.sharedMaterials = mats;
                EditorUtility.SetDirty(r);

                if (PrefabUtility.IsPartOfPrefabInstance(r.gameObject))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                }
            }

            converted++;
        }

        SaveHistory(history);
        Undo.CollapseUndoOperations(undoGroup);

        EditorUtility.DisplayDialog("Conversion terminee", converted + " material(s) converti(s) vers " + targetShader.name + ".\n\nCtrl+Z pour annuler dans l'immediat, ou l'onglet Historique plus tard (meme apres avoir rouvert le projet).", "OK");

        MarkSceneDataDirty();
    }

    // Noms de la texture / couleur de base selon l'origine du material :
    // URP (Lit, Simple Lit...), Built-in (Standard), glTF importe par glTFast (modeles Sketchfab...)
    private static readonly string[][] BaseColorNames =
    {
        new[] { "_BaseMap", "_BaseColor" },
        new[] { "_MainTex", "_Color" },
        new[] { "baseColorTexture", "baseColorFactor" },
    };

    private static void CopyBaseColor(Material source, Material target)
    {
        foreach (var names in BaseColorNames)
        {
            string texName = names[0], colorName = names[1];
            if (!source.HasProperty(texName) && !source.HasProperty(colorName)) continue;

            if (source.HasProperty(texName) && target.HasProperty("_BaseMap"))
            {
                target.SetTexture("_BaseMap", source.GetTexture(texName));
                // Tiling / offset aussi, sinon les motifs (tapis, sol quadrille...) changent d'echelle
                target.SetTextureScale("_BaseMap", source.GetTextureScale(texName));
                target.SetTextureOffset("_BaseMap", source.GetTextureOffset(texName));
            }
            if (source.HasProperty(colorName) && target.HasProperty("_BaseColor"))
            {
                // Alpha a 1 : ToonAdvanced est opaque, une transparence partielle n'a pas de sens ici
                Color c = source.GetColor(colorName);
                c.a = 1f;
                target.SetColor("_BaseColor", c);
            }
            return;
        }
    }

    // Material range DANS un modele (glb, fbx...) : noms souvent generiques ("material_0") et identiques
    // d'un modele a l'autre -> on prefixe par le nom du fichier pour ne pas ecraser les conversions d'un autre modele.
    private static string GetSourceName(Material original)
    {
        string name = StripToolPrefix(original.name);
        if (AssetDatabase.IsSubAsset(original))
            name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(original)) + "_" + name;
        return name;
    }

    private static string GetMaterialPrefix(Shader shader)
    {
        // "M_Cel_" conserve pour le CelShading (compatibilite avec les materials deja crees)
        if (shader.name == DefaultCelShader) return "M_Cel_";
        string shortName = shader.name.Substring(shader.name.LastIndexOf('/') + 1);
        return "M_" + shortName + "_";
    }

    // Reconvertir un material deja produit par l'outil (ex: M_Cel_Grey -> ToonAdvanced) donne
    // M_ToonAdvanced_Grey plutot que M_ToonAdvanced_M_Cel_Grey.
    private static string StripToolPrefix(string materialName)
    {
        foreach (var prefix in new[] { "M_Cel_", "M_ToonAdvanced_" })
        {
            if (materialName.StartsWith(prefix))
                return materialName.Substring(prefix.Length);
        }
        return materialName;
    }

    // =====================================================================
    // Historique persistant + reversion vers le material d'origine
    // (utile pour revenir en arriere un autre jour, y compris sur des prefabs,
    //  la ou Ctrl+Z ne fonctionne plus puisque l'historique d'Undo d'Unity
    //  ne survit pas a la fermeture du projet)
    // =====================================================================

    private class HistoryRow
    {
        public MaterialConversionHistory.Entry entry;
        public Renderer resolvedRenderer;
        public bool selected;
    }

    private List<HistoryRow> historyRows = new List<HistoryRow>();
    private float historyRowHeight = 44f;

    private MaterialConversionHistory GetOrCreateHistory()
    {
        var history = AssetDatabase.LoadAssetAtPath<MaterialConversionHistory>(HistoryAssetPath);
        if (history == null)
        {
            history = ScriptableObject.CreateInstance<MaterialConversionHistory>();
            EnsureFolderExists(Path.GetDirectoryName(HistoryAssetPath).Replace('\\', '/'));
            AssetDatabase.CreateAsset(history, HistoryAssetPath);
            AssetDatabase.SaveAssets();
        }
        return history;
    }

    private void SaveHistory(MaterialConversionHistory history)
    {
        EditorUtility.SetDirty(history);
        AssetDatabase.SaveAssets();
    }

    private void RecordHistoryEntry(MaterialConversionHistory history, Renderer r, int slotIndex, Material convertedMaterial, Material originalMaterial)
    {
        string globalId = GlobalObjectId.GetGlobalObjectIdSlow(r).ToString();

        var existing = history.entries.FirstOrDefault(e => e.globalId == globalId && e.slotIndex == slotIndex);
        if (existing != null)
        {
            // On garde l'original le plus ancien connu, on met juste a jour le material converti actuel
            existing.convertedMaterial = convertedMaterial;
            existing.objectNameHint = r.gameObject.name;
            return;
        }

        history.entries.Add(new MaterialConversionHistory.Entry
        {
            globalId = globalId,
            slotIndex = slotIndex,
            convertedMaterial = convertedMaterial,
            originalMaterial = originalMaterial,
            objectNameHint = r.gameObject.name
        });
    }

    private void RefreshHistory()
    {
        historyRows.Clear();
        var history = AssetDatabase.LoadAssetAtPath<MaterialConversionHistory>(HistoryAssetPath);
        if (history == null) return;

        // Nettoyage automatique : si le material actuellement sur l'objet ne correspond
        // plus a "convertedMaterial" (par ex. remis a l'original a la main dans l'Inspector,
        // ou par un Revert Unity natif sur un prefab), l'entree est obsolete et on la retire
        // plutot que de continuer a l'afficher indefiniment.
        var stale = new List<MaterialConversionHistory.Entry>();

        foreach (var e in history.entries)
        {
            Renderer resolved = null;
            GlobalObjectId id;
            if (GlobalObjectId.TryParse(e.globalId, out id))
            {
                var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                resolved = obj as Renderer;
            }

            if (resolved != null)
            {
                Material[] mats = resolved.sharedMaterials;
                bool stillConverted = e.slotIndex >= 0 && e.slotIndex < mats.Length && mats[e.slotIndex] == e.convertedMaterial;
                if (!stillConverted)
                {
                    stale.Add(e);
                    continue;
                }
            }

            historyRows.Add(new HistoryRow { entry = e, resolvedRenderer = resolved });
        }

        if (stale.Count > 0)
        {
            foreach (var e in stale)
                history.entries.Remove(e);
            SaveHistory(history);
        }
    }

    private void DrawHistoryTab()
    {
        TabHelp("Permet de revenir aux materials d'origine des objets convertis, même après avoir fermé Unity " +
            "(Ctrl+Z, lui, est perdu à la fermeture). Fonctionne aussi sur les prefabs.\n" +
            "Coche les objets puis « Revenir au material d'origine ».");

        if (GUILayout.Button("Rafraichir l'historique"))
        {
            historyDirty = true;
            Repaint();
        }

        EditorGUILayout.Space();

        if (historyRows.Count == 0)
        {
            EditorGUILayout.HelpBox("Aucun historique pour l'instant. Il se remplit automatiquement a chaque conversion.", MessageType.None);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Tout selectionner (trouves)"))
        {
            foreach (var row in historyRows)
                row.selected = row.resolvedRenderer != null;
        }
        if (GUILayout.Button("Tout deselectionner"))
        {
            foreach (var row in historyRows)
                row.selected = false;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        scroll = EditorGUILayout.BeginScrollView(scroll);

        HistoryRow toForget = null;
        var selectedRenderers = GetSelectedRenderers();
        Color previousBgHistory = GUI.backgroundColor;

        // Une entree par emplacement de material converti : apres une grosse scene, des milliers de lignes
        DrawVirtualRows(historyRows.Count, ref historyRowHeight, i =>
        {
            if (DrawHistoryRow(historyRows[i], selectedRenderers, previousBgHistory))
                toForget = historyRows[i];
        });

        EditorGUILayout.EndScrollView();

        if (toForget != null)
        {
            var history = AssetDatabase.LoadAssetAtPath<MaterialConversionHistory>(HistoryAssetPath);
            if (history != null)
            {
                history.entries.Remove(toForget.entry);
                SaveHistory(history);
            }
            historyDirty = true;
            Repaint();
        }

        EditorGUILayout.Space();
        int selectedCount = historyRows.Count(r => r.selected);
        EditorGUI.BeginDisabledGroup(selectedCount == 0);
        if (GUILayout.Button("Revenir au material d'origine (" + selectedCount + ")", GUILayout.Height(30)))
        {
            RevertSelectedHistory();
        }
        EditorGUI.EndDisabledGroup();
    }

    // Retourne true si on a clique sur "Oublier"
    private bool DrawHistoryRow(HistoryRow row, HashSet<Renderer> selectedRenderers, Color previousBgHistory)
    {
        bool forget = false;
        {
            bool isHierarchySelected = row.resolvedRenderer != null && selectedRenderers.Contains(row.resolvedRenderer);
            if (isHierarchySelected)
            {
                GUI.backgroundColor = new Color(1f, 0.7f, 0.15f, 1f);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = previousBgHistory;
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(row.resolvedRenderer == null);
            row.selected = EditorGUILayout.Toggle(row.selected, GUILayout.Width(20));
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.BeginVertical();

            string objectLabel = row.resolvedRenderer != null
                ? row.resolvedRenderer.gameObject.name
                : row.entry.objectNameHint + "  (introuvable dans la scene active)";
            if (isHierarchySelected) objectLabel += "  - SELECTIONNE";
            EditorGUILayout.LabelField(objectLabel, EditorStyles.boldLabel);

            string convertedName = row.entry.convertedMaterial != null ? row.entry.convertedMaterial.name : "?";
            string originalName = row.entry.originalMaterial != null ? row.entry.originalMaterial.name : "?";
            EditorGUILayout.LabelField("Actuel : " + convertedName + "   ->   Origine : " + originalName, EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();

            if (GUILayout.Button("Oublier", GUILayout.Width(60)))
            {
                forget = true;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
        return forget;
    }

    private void RevertSelectedHistory()
    {
        var history = AssetDatabase.LoadAssetAtPath<MaterialConversionHistory>(HistoryAssetPath);
        if (history == null) return;

        Undo.SetCurrentGroupName("Revenir au material d'origine");
        int undoGroup = Undo.GetCurrentGroup();

        int reverted = 0;
        var toRemove = new List<MaterialConversionHistory.Entry>();

        foreach (var row in historyRows)
        {
            if (!row.selected || row.resolvedRenderer == null) continue;

            Renderer r = row.resolvedRenderer;
            Undo.RecordObject(r, "Revenir au material d'origine");

            Material[] mats = r.sharedMaterials;
            if (row.entry.slotIndex >= 0 && row.entry.slotIndex < mats.Length)
            {
                mats[row.entry.slotIndex] = row.entry.originalMaterial;
                r.sharedMaterials = mats;
                EditorUtility.SetDirty(r);

                if (PrefabUtility.IsPartOfPrefabInstance(r.gameObject))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                }

                toRemove.Add(row.entry);
                reverted++;
            }
        }

        foreach (var e in toRemove)
            history.entries.Remove(e);
        SaveHistory(history);

        Undo.CollapseUndoOperations(undoGroup);

        EditorUtility.DisplayDialog("Reversion terminee", reverted + " objet(s) revenu(s) a leur material d'origine.", "OK");

        MarkSceneDataDirty();
    }

    // =====================================================================
    // Onglet 4 : generateur de ramp (texture degrade lue par Custom/ToonAdvanced)
    // Gauche = cote a l'ombre, droite = cote eclaire, milieu = limite ombre/lumiere.
    // Le degrade est memorise dans le fichier importe : on peut recharger une ramp pour la retoucher.
    // =====================================================================

    [System.Serializable]
    private class RampData
    {
        public Color[] colors;
        public float[] times;
        public bool hardEdges;
    }

    // [SerializeField] : survit aux recompilations, comme le reste de l'etat de la fenetre
    [SerializeField] private Gradient rampGradient;
    [SerializeField] private string rampName = "Ramp_Toon";
    [SerializeField] private string rampFolder = DefaultRampFolder;
    [SerializeField] private int rampWidth = 256;
    [SerializeField] private bool rampHardEdges;
    private Texture2D rampToLoad;

    private static Gradient MakeGradient(Color[] colors, float[] times, bool hardEdges)
    {
        var g = new Gradient();
        var colorKeys = new GradientColorKey[colors.Length];
        for (int i = 0; i < colors.Length; i++)
            colorKeys[i] = new GradientColorKey(colors[i], times[i]);
        g.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        // Fixed = pas de fondu entre les cles : chaque couleur s'arrete net a la position de sa cle
        g.mode = hardEdges ? GradientMode.Fixed : GradientMode.Blend;
        return g;
    }

    private void DrawRampTab()
    {
        if (rampGradient == null)
            rampGradient = MakeGradient(ToonPresets[0].rampColors, ToonPresets[0].rampTimes, false);

        TabHelp("Pour aller plus loin : crée les dégradés qui donnent la couleur des ombres (une « ramp »).\n" +
            "Gauche = côté à l'ombre, droite = côté éclairé. Plusieurs couleurs = plusieurs bandes d'ombre.\n" +
            "Les styles de base de l'onglet 2 utilisent déjà des ramps prêtes : cet onglet est facultatif.");

        EditorGUI.BeginChangeCheck();
        rampToLoad = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Retoucher", "Glisser une ramp ou un atlas cree par cet outil pour modifier ses degrades"), rampToLoad, typeof(Texture2D), false);
        if (EditorGUI.EndChangeCheck() && rampToLoad != null)
            LoadRamp(rampToLoad);

        rampMode = GUILayout.Toolbar(rampMode, new[] { "Ramp simple", "Atlas jour / nuit" });
        EditorGUILayout.Space();

        if (rampMode == 0) DrawSingleRampSection();
        else DrawRampAtlasSection();
    }

    private void DrawSingleRampSection()
    {
        EditorGUILayout.LabelField("Partir d'un modele", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        foreach (var preset in ToonPresets.Where(p => p.rampName != null))
        {
            if (GUILayout.Button(preset.label))
            {
                rampGradient = MakeGradient(preset.rampColors, preset.rampTimes, preset.rampHardEdges);
                rampHardEdges = preset.rampHardEdges;
                rampName = preset.rampName;
            }
        }
        if (GUILayout.Button("3 bandes"))
        {
            rampGradient = MakeGradient(
                new[] { new Color(0.5f, 0.48f, 0.72f), new Color(0.72f, 0.7f, 0.88f), Color.white },
                new[] { 0.3f, 0.5f, 1f }, true);
            rampHardEdges = true;
            rampName = "Ramp_3Bandes";
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        rampGradient = EditorGUILayout.GradientField("Degrade", rampGradient, GUILayout.Height(28));
        EditorGUI.BeginChangeCheck();
        rampHardEdges = EditorGUILayout.Toggle(new GUIContent("Bandes nettes", "Gradient en mode Fixed + texture en filtrage Point : transitions franches facon ZZZ."), rampHardEdges);
        if (EditorGUI.EndChangeCheck())
            rampGradient.mode = rampHardEdges ? GradientMode.Fixed : GradientMode.Blend;
        rampWidth = EditorGUILayout.IntSlider("Largeur (pixels)", rampWidth, 16, 512);
        rampName = EditorGUILayout.TextField("Nom", rampName);
        rampFolder = EditorGUILayout.TextField("Dossier de sortie", rampFolder);

        EditorGUILayout.Space();
        string path = rampFolder + "/" + rampName + ".png";
        bool exists = AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null;
        if (exists)
            EditorGUILayout.HelpBox("'" + rampName + ".png' existe deja : il sera mis a jour (les materials qui l'utilisent aussi).", MessageType.None);

        var toonMats = GetCompatibleMaterials().Where(m => m.shader.name == ShaderToon).ToList();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(exists ? "Mettre a jour la texture" : "Generer la texture", GUILayout.Height(28)))
        {
            var tex = SaveRampTexture(rampGradient, path, rampWidth, rampHardEdges);
            EditorGUIUtility.PingObject(tex);
        }
        EditorGUI.BeginDisabledGroup(toonMats.Count == 0);
        if (GUILayout.Button("Generer et appliquer a la selection (" + toonMats.Count + ")", GUILayout.Height(28)))
        {
            var tex = SaveRampTexture(rampGradient, path, rampWidth, rampHardEdges);
            AssignRamp(toonMats, tex, "Appliquer la ramp " + rampName);
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    private static void AssignRamp(List<Material> mats, Texture2D tex, string undoName)
    {
        foreach (var mat in mats)
        {
            Undo.RecordObject(mat, undoName);
            mat.SetTexture("_RampMap", tex);
            mat.SetFloat("_UseRamp", 1f);
            ApplyRampLayout(mat, tex);
            MaterialEditor.ApplyMaterialPropertyDrawers(mat);
            EditorUtility.SetDirty(mat);
        }
        SceneView.RepaintAll();
    }

    private void LoadRamp(Texture2D texture)
    {
        var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
        if (importer == null || string.IsNullOrEmpty(importer.userData))
        {
            EditorUtility.DisplayDialog("Ramp", "Cette texture n'a pas ete creee avec le generateur : impossible de retrouver son degrade.", "OK");
            return;
        }
        rampFolder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(texture)).Replace('\\', '/');
        rampWidth = texture.width;

        var atlas = ReadAtlasData(texture);
        if (atlas != null)
        {
            atlasRows = new List<RampAtlasRow>();
            for (int i = 0; i < atlas.names.Length; i++)
            {
                atlasRows.Add(new RampAtlasRow
                {
                    name = atlas.names[i],
                    day = MakeGradient(atlas.day[i].colors, atlas.day[i].times, atlas.hardEdges),
                    night = atlas.hasNight
                        ? MakeGradient(atlas.night[i].colors, atlas.night[i].times, atlas.hardEdges)
                        : MakeGradient(atlas.day[i].colors, atlas.day[i].times, atlas.hardEdges)
                });
            }
            atlasHasNight = atlas.hasNight;
            rampHardEdges = atlas.hardEdges;
            atlasName = texture.name;
            rampMode = 1;
            return;
        }

        var data = JsonUtility.FromJson<RampData>(importer.userData);
        rampGradient = MakeGradient(data.colors, data.times, data.hardEdges);
        rampHardEdges = data.hardEdges;
        rampName = texture.name;
        rampMode = 0;
    }

    // =====================================================================
    // Atlas de ramps : une ligne par type de matiere (decor, tissu, metal...), et en option
    // une version "nuit" de chaque ligne. Le composant ToonDayNight de la scene fait la transition.
    // Disposition : lignes de jour en haut, puis les lignes de nuit dans le meme ordre, 4 pixels par ligne.
    // =====================================================================

    [System.Serializable]
    private class RampAtlasRow
    {
        public string name = "Ligne";
        public Gradient day;
        public Gradient night;
    }

    [System.Serializable]
    private class RampAtlasData
    {
        public string[] names;
        public RampData[] day;
        public RampData[] night;
        public bool hasNight;
        public bool hardEdges;
    }

    [SerializeField] private int rampMode; // 0 = ramp simple, 1 = atlas
    [SerializeField] private List<RampAtlasRow> atlasRows = new List<RampAtlasRow>();
    [SerializeField] private bool atlasHasNight = true;
    [SerializeField] private string atlasName = "RampAtlas";

    private const int RampRowHeight = 4;
    private static readonly Dictionary<Texture, RampAtlasData> atlasDataCache = new Dictionary<Texture, RampAtlasData>();

    private static Gradient CopyGradient(Gradient g)
    {
        var copy = new Gradient();
        copy.SetKeys(g.colorKeys, g.alphaKeys);
        copy.mode = g.mode;
        return copy;
    }

    private static RampAtlasRow DefaultAtlasRow(string name)
    {
        return new RampAtlasRow
        {
            name = name,
            day = MakeGradient(new[] { new Color(0.6f, 0.62f, 0.82f), Color.white }, new[] { 0.5f, 1f }, true),
            night = MakeGradient(new[] { new Color(0.25f, 0.28f, 0.5f), new Color(0.55f, 0.62f, 0.9f) }, new[] { 0.5f, 1f }, true)
        };
    }

    private void DrawRampAtlasSection()
    {
        EditorGUILayout.HelpBox(
            "Une ligne par type de matiere (decor, tissu, metal...). Chaque material choisit sa ligne dans l'onglet " +
            "Reglages. Les lignes de nuit servent quand le composant ToonDayNight de la scene passe vers la nuit.",
            MessageType.None);

        if (atlasRows.Count == 0)
            atlasRows.Add(DefaultAtlasRow("Decor"));

        atlasHasNight = EditorGUILayout.Toggle(new GUIContent("Lignes de nuit", "Ajoute une version nuit de chaque ligne"), atlasHasNight);
        EditorGUI.BeginChangeCheck();
        rampHardEdges = EditorGUILayout.Toggle(new GUIContent("Bandes nettes", "Gradients en mode Fixed + filtrage Point"), rampHardEdges);
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var row in atlasRows)
            {
                row.day.mode = rampHardEdges ? GradientMode.Fixed : GradientMode.Blend;
                row.night.mode = row.day.mode;
            }
        }

        int toRemove = -1;
        for (int i = 0; i < atlasRows.Count; i++)
        {
            var row = atlasRows[i];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            row.name = EditorGUILayout.TextField("Ligne " + i, row.name);
            EditorGUI.BeginDisabledGroup(atlasRows.Count <= 1);
            if (GUILayout.Button(new GUIContent("X", "Supprimer la ligne"), GUILayout.Width(22)))
                toRemove = i;
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            row.day = EditorGUILayout.GradientField("Jour", row.day);
            if (atlasHasNight)
                row.night = EditorGUILayout.GradientField("Nuit", row.night);
            EditorGUILayout.EndVertical();
        }
        if (toRemove >= 0)
            atlasRows.RemoveAt(toRemove);
        if (GUILayout.Button("+ Ajouter une ligne"))
        {
            var last = atlasRows[atlasRows.Count - 1];
            atlasRows.Add(new RampAtlasRow { name = "Ligne " + atlasRows.Count, day = CopyGradient(last.day), night = CopyGradient(last.night) });
        }

        EditorGUILayout.Space();
        rampWidth = EditorGUILayout.IntSlider("Largeur (pixels)", rampWidth, 16, 512);
        atlasName = EditorGUILayout.TextField("Nom", atlasName);
        rampFolder = EditorGUILayout.TextField("Dossier de sortie", rampFolder);

        string path = rampFolder + "/" + atlasName + ".png";
        bool exists = AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null;
        var toonMats = GetCompatibleMaterials().Where(m => m.shader.name == ShaderToon).ToList();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(exists ? "Mettre a jour l'atlas" : "Generer l'atlas", GUILayout.Height(28)))
        {
            var tex = SaveRampAtlas(atlasRows, atlasHasNight, path, rampWidth, rampHardEdges);
            EditorGUIUtility.PingObject(tex);
        }
        EditorGUI.BeginDisabledGroup(toonMats.Count == 0);
        if (GUILayout.Button("Generer et appliquer a la selection (" + toonMats.Count + ")", GUILayout.Height(28)))
        {
            var tex = SaveRampAtlas(atlasRows, atlasHasNight, path, rampWidth, rampHardEdges);
            AssignRamp(toonMats, tex, "Appliquer l'atlas " + atlasName);
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    private static RampData ToRampData(Gradient g, bool hardEdges)
    {
        return new RampData
        {
            colors = g.colorKeys.Select(k => k.color).ToArray(),
            times = g.colorKeys.Select(k => k.time).ToArray(),
            hardEdges = hardEdges
        };
    }

    private static Texture2D SaveRampAtlas(List<RampAtlasRow> rows, bool hasNight, string assetPath, int width, bool hardEdges)
    {
        EnsureFolderExists(Path.GetDirectoryName(assetPath).Replace('\\', '/'));

        int dayRows = rows.Count;
        int totalRows = hasNight ? dayRows * 2 : dayRows;
        int height = totalRows * RampRowHeight;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color[width * height];
        for (int r = 0; r < totalRows; r++)
        {
            Gradient g = r < dayRows ? rows[r].day : rows[r - dayRows].night;
            // Ligne 0 en haut de l'image (en texture, y = 0 est en bas) : le shader lit v = 1 - (ligne + 0.5) / lignes
            int yStart = (totalRows - 1 - r) * RampRowHeight;
            for (int x = 0; x < width; x++)
            {
                Color c = g.Evaluate((x + 0.5f) / width);
                for (int y = yStart; y < yStart + RampRowHeight; y++)
                    pixels[y * width + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(assetPath, tex.EncodeToPNG());
        DestroyImmediate(tex);

        var data = new RampAtlasData
        {
            names = rows.Select(r => r.name).ToArray(),
            day = rows.Select(r => ToRampData(r.day, hardEdges)).ToArray(),
            night = hasNight ? rows.Select(r => ToRampData(r.night, hardEdges)).ToArray() : new RampData[0],
            hasNight = hasNight,
            hardEdges = hardEdges
        };
        return ImportRampTexture(assetPath, hardEdges, JsonUtility.ToJson(data));
    }

    // Point d'entree scriptable : atlas a partir de lignes (nom + couleurs/positions de jour et de nuit)
    public static Texture2D CreateRampAtlas(string assetPath, string[] names, Color[][] dayColors, float[][] dayTimes,
        Color[][] nightColors, float[][] nightTimes, bool hardEdges, int width = 256)
    {
        var rows = new List<RampAtlasRow>();
        bool hasNight = nightColors != null;
        for (int i = 0; i < names.Length; i++)
        {
            rows.Add(new RampAtlasRow
            {
                name = names[i],
                day = MakeGradient(dayColors[i], dayTimes[i], hardEdges),
                night = hasNight ? MakeGradient(nightColors[i], nightTimes[i], hardEdges) : MakeGradient(dayColors[i], dayTimes[i], hardEdges)
            });
        }
        return SaveRampAtlas(rows, hasNight, assetPath, width, hardEdges);
    }

    // Infos d'atlas memorisees dans le fichier importe (null pour une ramp simple ou une texture externe)
    private static RampAtlasData ReadAtlasData(Texture texture)
    {
        if (texture == null) return null;
        RampAtlasData data;
        if (atlasDataCache.TryGetValue(texture, out data)) return data;

        data = null;
        var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
        if (importer != null && !string.IsNullOrEmpty(importer.userData) && importer.userData.Contains("\"names\""))
        {
            data = JsonUtility.FromJson<RampAtlasData>(importer.userData);
            if (data.names == null || data.names.Length == 0) data = null;
        }
        atlasDataCache[texture] = data;
        return data;
    }

    // Regle les infos de disposition lues par le shader selon la ramp assignee (simple ou atlas)
    private static void ApplyRampLayout(Material mat, Texture ramp)
    {
        if (!mat.HasProperty("_RampRows")) return;
        var atlas = ReadAtlasData(ramp);
        int dayRows = atlas != null ? atlas.names.Length : 1;
        bool night = atlas != null && atlas.hasNight;
        mat.SetFloat("_RampRows", night ? dayRows * 2 : dayRows);
        mat.SetFloat("_RampNightOffset", night ? dayRows : 0);
        if (mat.GetFloat("_RampRow") > dayRows - 1)
            mat.SetFloat("_RampRow", 0);
    }

    // Point d'entree scriptable : choisit la ligne d'atlas de ces materials
    public static void SetRampRow(List<Material> mats, int row)
    {
        foreach (var mat in mats)
        {
            Undo.RecordObject(mat, "Ligne de l'atlas");
            mat.SetFloat("_RampRow", row);
            EditorUtility.SetDirty(mat);
        }
    }

    public static void AssignRampToMaterials(List<Material> mats, Texture2D ramp)
    {
        AssignRamp(mats, ramp, "Appliquer la ramp " + ramp.name);
    }

    // Point d'entree scriptable : cree (ou ecrase) une ramp a partir de couleurs + positions (0..1)
    public static Texture2D CreateRamp(string assetPath, Color[] colors, float[] times, bool hardEdges, int width = 256)
    {
        return SaveRampTexture(MakeGradient(colors, times, hardEdges), assetPath, width, hardEdges);
    }

    private static Texture2D SaveRampTexture(Gradient gradient, string assetPath, int width, bool hardEdges)
    {
        EnsureFolderExists(Path.GetDirectoryName(assetPath).Replace('\\', '/'));

        // 4 pixels de haut : le shader lit la ligne du milieu, ca evite tout melange avec les bords
        const int height = 4;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color[width * height];
        for (int x = 0; x < width; x++)
        {
            Color c = gradient.Evaluate((x + 0.5f) / width);
            for (int y = 0; y < height; y++)
                pixels[y * width + x] = c;
        }
        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(assetPath, tex.EncodeToPNG());
        DestroyImmediate(tex);

        return ImportRampTexture(assetPath, hardEdges, JsonUtility.ToJson(ToRampData(gradient, hardEdges)));
    }

    // Reglages d'import communs aux ramps et aux atlas + degrade(s) memorise(s) dans le fichier
    private static Texture2D ImportRampTexture(string assetPath, bool hardEdges, string userData)
    {
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = hardEdges ? FilterMode.Point : FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.userData = userData;
        importer.SaveAndReimport();

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        atlasDataCache.Remove(tex); // les infos d'atlas memorisees ont pu changer
        return tex;
    }

    // =====================================================================
    // MatCap metal par defaut : sphere "eclairee" en bandes franches (reflet du ciel en haut, du sol en bas)
    // + un point de lumiere net. En gris : c'est la couleur de l'objet qui donne la teinte (jaune = or...).
    // =====================================================================

    private const string DefaultMatCapName = "MatCap_Metal_Toon";

    public static Texture2D GetOrCreateDefaultMatCap(string assetPath)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (existing != null) return existing;
        return CreateDefaultMatCap(assetPath);
    }

    // Ecrase le fichier s'il existe (le .meta est conserve : les materials gardent leur reference)
    public static Texture2D CreateDefaultMatCap(string assetPath)
    {
        EnsureFolderExists(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                // Point du disque -> normale de la sphere vue de face
                float x = (px + 0.5f) / size * 2f - 1f;
                float y = (py + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(x * x + y * y);
                float ny = y; // la composante verticale de la normale suffit pour les bandes ciel / sol

                // Trois bandes, avec une transition de 2 % pour eviter l'escalier de pixels.
                // Gris 0.5 = couleur de l'objet inchangee : la bande "sol" (0.38) assombrit sans eteindre,
                // pour que les faces planes tournees vers le bas restent lisibles.
                float v = Mathf.Lerp(0.38f, 0.58f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.17f, -0.13f, ny)));
                v = Mathf.Lerp(v, 0.95f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.43f, 0.47f, ny)));
                // Point de lumiere net en haut a gauche
                float spot = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.17f, 0.15f, Vector2.Distance(new Vector2(x, y), new Vector2(-0.4f, 0.5f))));
                v = Mathf.Max(v, spot);
                // Bord legerement assombri, et tout ce qui est hors du disque reprend la valeur du bord
                v *= Mathf.Lerp(1f, 0.8f, Mathf.InverseLerp(0.9f, 1f, Mathf.Min(r, 1f)));
                pixels[py * size + px] = new Color(v, v, v, 1f);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(assetPath, tex.EncodeToPNG());
        DestroyImmediate(tex);

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        // Lineaire (pas sRGB) : c'est un multiplicateur, pas une couleur. En sRGB, le gris "neutre" 0.5
        // deviendrait ~0.21 dans le shader et assombrirait tout l'objet.
        importer.sRGBTexture = false;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private static void EnsureFolderExists(string assetsPath)
    {
        if (AssetDatabase.IsValidFolder(assetsPath)) return;
        string parent = Path.GetDirectoryName(assetsPath).Replace('\\', '/');
        EnsureFolderExists(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(assetsPath));
    }
}
