using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class ShaderToolsWindow : EditorWindow
{
    private const string ShaderFlat = "Custom/MangaDotsOutline";
    private const string ShaderTextured = "Custom/MangaDotsOutlineTextured";
    private const string DefaultCelShader = "Custom/CelShading";
    private const string DefaultMaterialsFolder = "Assets/ShaderTools/Materials";
    private const string HistoryAssetPath = "Assets/ShaderTools/Materials/MaterialConversionHistory.asset";

    [MenuItem("Tools/Outils Shaders/Editeur")]
    public static void ShowWindow()
    {
        var window = GetWindow<ShaderToolsWindow>("Outils Shaders");
        window.minSize = new Vector2(380, 480);
    }

    private int tab;
    private Vector2 scroll;

    private void OnEnable()
    {
        Selection.selectionChanged += Repaint;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        RefreshMaterialList();
        RefreshHistory();
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= Repaint;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
    }

    private void OnHierarchyChanged()
    {
        // Evite que la liste garde des references vers des objets supprimes/renommes
        // entre deux clics manuels sur "Rafraichir".
        RefreshMaterialList();
        Repaint();
    }

    private void OnGUI()
    {
        tab = GUILayout.Toolbar(tab, new[] { "Reglages", "Conversion", "Historique" });
        EditorGUILayout.Space();

        if (tab == 0) DrawSettingsTab();
        else if (tab == 1) DrawConversionTab();
        else DrawHistoryTab();
    }

    // =====================================================================
    // Onglet 1 : reglages rapides (pois / outline / degrade) sur la selection
    // =====================================================================

    private HashSet<Renderer> GetSelectedRenderers()
    {
        // GetComponentsInChildren (et pas juste GetComponent) car sur beaucoup de props/prefabs
        // (ex: BasketBallNet) le Renderer est sur un enfant, pas sur l'objet selectionne lui-meme.
        var set = new HashSet<Renderer>();
        foreach (var go in Selection.gameObjects)
        {
            foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
                set.Add(rend);
        }
        return set;
    }

    private HashSet<Material> GetSelectedMaterials()
    {
        var set = new HashSet<Material>();
        foreach (var rend in GetSelectedRenderers())
        {
            foreach (var m in rend.sharedMaterials)
                if (m != null) set.Add(m);
        }
        return set;
    }

    private List<Material> GetCompatibleMaterials()
    {
        var mats = new List<Material>();
        foreach (var go in Selection.gameObjects)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend == null || rend.sharedMaterial == null) continue;
            var mat = rend.sharedMaterial;
            if (mat.shader == null) continue;
            if (mat.shader.name == ShaderFlat || mat.shader.name == ShaderTextured)
            {
                if (!mats.Contains(mat))
                    mats.Add(mat);
            }
        }
        return mats;
    }

    private void DrawSettingsTab()
    {
        var mats = GetCompatibleMaterials();

        EditorGUILayout.LabelField("Manga Dots Outline - Editeur rapide", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (mats.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "Selectionne dans la Hierarchy un ou plusieurs objets utilisant le shader\n" +
                "'Custom/MangaDotsOutline' ou 'Custom/MangaDotsOutlineTextured'.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox(
            mats.Count + " material(s) compatible(s) detecte(s) dans la selection :\n" +
            string.Join(", ", mats.Select(m => m.name)),
            MessageType.None);
        EditorGUILayout.Space();

        scroll = EditorGUILayout.BeginScrollView(scroll);

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

        EditorGUILayout.EndScrollView();
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

    private void DrawColor(List<Material> targets, Material reference, string property, string label)
    {
        if (!reference.HasProperty(property)) return;
        Color current = reference.GetColor(property);
        EditorGUI.BeginChangeCheck();
        Color newValue = EditorGUILayout.ColorField(label, current);
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
        public bool isTransparent;
        public bool alreadyConverted;
    }

    private List<MaterialEntry> materialEntries = new List<MaterialEntry>();
    private Shader targetShader;
    private string materialsFolder = DefaultMaterialsFolder;

    private void RefreshMaterialList()
    {
        if (targetShader == null)
        {
            targetShader = Shader.Find(DefaultCelShader);
        }

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
                    entry.isTransparent = m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f;
                    entry.alreadyConverted = targetShader != null && m.shader == targetShader;
                    map[m] = entry;
                    materialEntries.Add(entry);
                }
                entry.renderers.Add(r);
            }
        }

        materialEntries = materialEntries.OrderBy(e => e.material.name).ToList();
    }

    private void DrawConversionTab()
    {
        EditorGUILayout.LabelField("Conversion de materials (scene active)", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        targetShader = (Shader)EditorGUILayout.ObjectField("Shader cible", targetShader, typeof(Shader), false);
        if (GUILayout.Button("Rafraichir", GUILayout.Width(80)))
        {
            RefreshMaterialList();
        }
        EditorGUILayout.EndHorizontal();

        materialsFolder = EditorGUILayout.TextField("Dossier de sortie", materialsFolder);

        EditorGUILayout.Space();

        if (materialEntries.Count == 0)
        {
            EditorGUILayout.HelpBox("Aucun material trouve. Clique sur Rafraichir apres avoir ouvert/modifie la scene.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Tout selectionner (eligibles)"))
        {
            foreach (var e in materialEntries)
                e.selected = !e.isTransparent && !e.alreadyConverted;
        }
        if (GUILayout.Button("Tout deselectionner"))
        {
            foreach (var e in materialEntries)
                e.selected = false;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        scroll = EditorGUILayout.BeginScrollView(scroll);

        Color previousBg = GUI.backgroundColor;
        var selectedMats = GetSelectedMaterials();

        var richBoldLabel = new GUIStyle(EditorStyles.boldLabel) { richText = true };

        foreach (var entry in materialEntries)
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

            EditorGUI.BeginDisabledGroup(entry.isTransparent || entry.alreadyConverted);
            entry.selected = EditorGUILayout.Toggle(entry.selected, GUILayout.Width(20));
            EditorGUI.EndDisabledGroup();

            // Pastille de statut : couleur pleine dessinee directement (pas de multiplication
            // avec une texture sombre), donc le vert "deja converti" reste un vrai vert net.
            Color statusDotColor = entry.alreadyConverted ? new Color(0.35f, 0.85f, 0.35f)
                : entry.isTransparent ? new Color(0.55f, 0.55f, 0.55f)
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
                : entry.isTransparent ? "#9E9E9E"
                : "#64B5F6";
            string status = entry.isTransparent ? "Transparent - ignore"
                : entry.alreadyConverted ? "Deja converti"
                : "Pret a convertir";
            if (isHierarchySelected) status += " - SELECTIONNE";
            EditorGUILayout.LabelField(entry.material.name + "  [ <color=" + statusColorHex + ">" + status + "</color> ]", richBoldLabel);

            var objNames = entry.renderers.Where(r => r != null).Select(r => r.gameObject.name).Distinct().ToList();
            string namesLabel = string.Join(", ", objNames.Take(3));
            if (objNames.Count > 3) namesLabel += " (+" + (objNames.Count - 3) + " autres)";
            EditorGUILayout.LabelField("Objets : " + namesLabel, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Shader actuel : " + (entry.material.shader != null ? entry.material.shader.name : "?"), EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

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

    // Cree le dossier (et ses parents) s'il n'existe pas encore, pour que le package
    // fonctionne directement dans un nouveau projet sans configuration prealable.
    private bool EnsureFolderExists(string assetsPath)
    {
        if (AssetDatabase.IsValidFolder(assetsPath)) return true;
        if (!assetsPath.StartsWith("Assets")) return false;

        string[] parts = assetsPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
        return AssetDatabase.IsValidFolder(assetsPath);
    }

    private void ConvertSelected()
    {
        if (!AssetDatabase.IsValidFolder(materialsFolder))
        {
            if (!EnsureFolderExists(materialsFolder))
            {
                EditorUtility.DisplayDialog("Chemin invalide", "Impossible de creer le dossier '" + materialsFolder + "'. Verifie qu'il commence bien par 'Assets/'.", "OK");
                return;
            }
        }

        var history = GetOrCreateHistory();

        Undo.SetCurrentGroupName("Convertir vers " + targetShader.name);
        int undoGroup = Undo.GetCurrentGroup();

        int converted = 0;

        foreach (var entry in materialEntries)
        {
            if (!entry.selected || entry.isTransparent || entry.alreadyConverted) continue;

            Material original = entry.material;
            string newMatPath = materialsFolder + "/M_Cel_" + original.name + ".mat";

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

            if (original.HasProperty("_BaseMap") && celMat.HasProperty("_BaseMap"))
                celMat.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
            if (original.HasProperty("_BaseColor") && celMat.HasProperty("_BaseColor"))
                celMat.SetColor("_BaseColor", original.GetColor("_BaseColor"));

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

        RefreshMaterialList();
        RefreshHistory();
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

    private MaterialConversionHistory GetOrCreateHistory()
    {
        var history = AssetDatabase.LoadAssetAtPath<MaterialConversionHistory>(HistoryAssetPath);
        if (history == null)
        {
            history = ScriptableObject.CreateInstance<MaterialConversionHistory>();
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
        EditorGUILayout.LabelField("Historique des conversions", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Garde la trace du material d'origine de chaque objet converti, meme apres avoir ferme et rouvert le projet " +
            "(contrairement a Ctrl+Z, qui ne survit pas a la fermeture d'Unity). Fonctionne aussi sur les prefabs : " +
            "la reversion met a jour l'instance du prefab correctement.",
            MessageType.Info);
        EditorGUILayout.Space();

        if (GUILayout.Button("Rafraichir l'historique"))
        {
            RefreshHistory();
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

        foreach (var row in historyRows)
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
                toForget = row;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();

        if (toForget != null)
        {
            var history = AssetDatabase.LoadAssetAtPath<MaterialConversionHistory>(HistoryAssetPath);
            if (history != null)
            {
                history.entries.Remove(toForget.entry);
                SaveHistory(history);
            }
            RefreshHistory();
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

        RefreshHistory();
        RefreshMaterialList();
    }
}
