using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Regles d'import par dossier : chaque fichier qui arrive dans un dossier recoit les bons reglages, sans que
// personne n'ait a y penser (taille max des textures, normal maps, sprites d'interface, normales lissees du
// contour pour les personnages, LOD automatiques pour le decor...).
// Les regles sont dans ProjectSettings/ShaderToolsImportRules.json : partagees par toute l'equipe avec le projet (Git).
// Elles ne s'appliquent qu'a l'arrivee d'un fichier (pas encore de .meta) : les reglages sont alors ecrits dans
// son .meta, et une retouche a la main n'est jamais ecrasee. Pour les fichiers deja la : bouton "Appliquer".
public class ToonImportRules : AssetPostprocessor
{
    [Serializable]
    public class Rule
    {
        public string label = "";
        public string folder = "Assets";        // ce dossier et ses sous-dossiers
        public int maxTextureSize = 2048;        // une texture n'est jamais agrandie, seulement limitee
        public bool sprites;                     // interface : textures en sprites, sans mipmaps
        public bool outlineNormals;              // personnages : normales lissees du contour (ToonOutlineNormals)
        public bool meshLods;                    // decor : LOD generes par Unity a l'import (Unity 6.2+, pas pour les personnages animes)
    }

    [Serializable]
    private class RuleFile
    {
        public List<Rule> rules = new List<Rule>();
    }

    private const string FilePath = "ProjectSettings/ShaderToolsImportRules.json";
    public static readonly int[] TextureSizes = { 256, 512, 1024, 2048, 4096 };

    private static List<Rule> cache;
    private static DateTime cacheTime;

    public static List<Rule> Rules
    {
        get
        {
            // Relu si le fichier a change (recupere par Git, modifie par un autre editeur...)
            DateTime time = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
            if (cache == null || time != cacheTime)
            {
                cacheTime = time;
                cache = time == DateTime.MinValue ? new List<Rule>()
                    : (JsonUtility.FromJson<RuleFile>(File.ReadAllText(FilePath)) ?? new RuleFile()).rules;
            }
            return cache;
        }
    }

    public static void Save()
    {
        File.WriteAllText(FilePath, JsonUtility.ToJson(new RuleFile { rules = Rules }, true));
        cacheTime = File.GetLastWriteTimeUtc(FilePath);
    }

    // La regle du dossier le plus proche (un sous-dossier peut avoir sa propre regle)
    public static Rule Find(string assetPath)
    {
        Rule best = null;
        foreach (var rule in Rules)
        {
            string folder = rule.folder.TrimEnd('/');
            if (!assetPath.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)) continue;
            if (best == null || folder.Length > best.folder.TrimEnd('/').Length) best = rule;
        }
        return best;
    }

    // ---- A l'arrivee d'un fichier ----

    private void OnPreprocessTexture()
    {
        if (!assetImporter.importSettingsMissing) return;
        var rule = Find(assetPath);
        if (rule != null) ApplyTexture((TextureImporter)assetImporter, rule, assetPath);
    }

    private void OnPreprocessModel()
    {
        if (!assetImporter.importSettingsMissing) return;
        var rule = Find(assetPath);
        if (rule != null) ApplyModel((ModelImporter)assetImporter, rule);
    }

    // ---- Reglages appliques (renvoient true si quelque chose a change) ----

    public static bool ApplyTexture(TextureImporter importer, Rule rule, string path)
    {
        bool changed = false;
        string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (rule.sprites)
        {
            if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; changed = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
        }
        else if (importer.textureType == TextureImporterType.Default)
        {
            // Normal map (relief) : sans ce type, Unity la lit comme une image et l'eclairage est faux
            if (IsNormalMapName(name)) { importer.textureType = TextureImporterType.NormalMap; changed = true; }
            // Masques (zones, occlusion, rugosite...) : des valeurs, pas des couleurs, donc pas de correction sRGB
            else if (IsMaskName(name) && importer.sRGBTexture) { importer.sRGBTexture = false; changed = true; }
        }
        if (importer.maxTextureSize > rule.maxTextureSize) { importer.maxTextureSize = rule.maxTextureSize; changed = true; }
        return changed;
    }

    public static bool ApplyModel(ModelImporter importer, Rule rule)
    {
        bool changed = false;
        // Cameras et lumieres exportees avec le modele (Blender...) : elles s'ajouteraient a chaque scene
        if (importer.importCameras) { importer.importCameras = false; changed = true; }
        if (importer.importLights) { importer.importLights = false; changed = true; }
        if (rule.outlineNormals && (importer.userData == null || !importer.userData.Contains(ToonOutlineNormals.Marker)))
        {
            importer.userData = ((importer.userData ?? "") + " " + ToonOutlineNormals.Marker).Trim();
            changed = true;
        }
        if (rule.meshLods && !importer.generateMeshLods) { importer.generateMeshLods = true; changed = true; }
        return changed;
    }

    private static bool IsNormalMapName(string name) =>
        name.EndsWith("_n") || name.EndsWith("_nrm") || name.Contains("normal");

    private static bool IsMaskName(string name) =>
        new[] { "mask", "masque", "_ao", "rough", "metal", "lightmap", "_orm", "height" }.Any(name.Contains);

    // ---- Fichiers deja importes : meme reglages, a la demande ----

    public static int ApplyToExisting(Rule rule)
    {
        string folder = rule.folder.TrimEnd('/');
        if (!AssetDatabase.IsValidFolder(folder)) return 0;
        var paths = AssetDatabase.FindAssets("t:Texture2D t:Model", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath).Distinct()
            .Where(p => Find(p) == rule).ToList(); // un sous-dossier avec sa propre regle garde la sienne
        int count = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (string path in paths)
            {
                var importer = AssetImporter.GetAtPath(path);
                bool changed = importer is TextureImporter texture ? ApplyTexture(texture, rule, path)
                    : importer is ModelImporter model && ApplyModel(model, rule);
                if (!changed) continue;
                importer.SaveAndReimport();
                count++;
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        return count;
    }

    // ---- Interface (onglet "Préparer le projet") ----

    private static readonly string[] SizeLabels = TextureSizes.Select(s => s.ToString()).ToArray();

    public static void DrawRules()
    {
        EditorGUILayout.LabelField("Chaque fichier qui arrive dans un de ces dossiers reçoit ses réglages automatiquement. " +
            "Partout : normal maps et masques reconnus à leur nom, caméras et lumières des modèles ignorées.", EditorStyles.wordWrappedMiniLabel);
        if (Rules.Count == 0)
            EditorGUILayout.HelpBox("Aucune règle : « Créer les dossiers du projet » en ajoute pour le décor, les objets, les personnages, les effets et l'interface.", MessageType.None);

        Rule remove = null;
        foreach (var rule in Rules)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                rule.label = EditorGUILayout.TextField(rule.label);
                if (GUILayout.Button(new GUIContent("Retirer", "Supprime la règle (les fichiers gardent leurs réglages)"), EditorStyles.miniButton, GUILayout.Width(60)))
                    remove = rule;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                rule.folder = EditorGUILayout.TextField(new GUIContent("Dossier", "Ce dossier et ses sous-dossiers"), rule.folder);
                if (GUILayout.Button(new GUIContent("Voir", "Montre le dossier dans la fenêtre Project"), EditorStyles.miniButton, GUILayout.Width(40)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(rule.folder.TrimEnd('/'));
                    if (asset != null) { Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
                }
                EditorGUILayout.EndHorizontal();
                int index = Mathf.Max(0, Array.IndexOf(TextureSizes, rule.maxTextureSize));
                rule.maxTextureSize = TextureSizes[EditorGUILayout.Popup(new GUIContent("Textures : taille max", "Une texture plus petite n'est jamais agrandie"), index, SizeLabels)];
                rule.sprites = EditorGUILayout.Toggle(new GUIContent("Interface (sprites)", "Textures en sprites, sans mipmaps"), rule.sprites);
                rule.outlineNormals = EditorGUILayout.Toggle(new GUIContent("Personnages : normales lissées", "Contour sans trous aux arêtes vives (calculé à l'import)"), rule.outlineNormals);
                rule.meshLods = EditorGUILayout.Toggle(new GUIContent("Décor : LOD automatiques", "Versions simplifiées des modèles affichées de loin (Unity 6.2+). Pas pour les personnages animés."), rule.meshLods);
                if (EditorGUI.EndChangeCheck()) Save();

                if (!AssetDatabase.IsValidFolder(rule.folder.TrimEnd('/')))
                    EditorGUILayout.LabelField("Ce dossier n'existe pas encore.", EditorStyles.miniLabel);
                else if (GUILayout.Button(new GUIContent("Appliquer aux fichiers déjà dans ce dossier",
                    "Les règles ne s'appliquent seules qu'aux nouveaux fichiers. Ici, les fichiers déjà importés sont mis à jour.")))
                {
                    int count = ApplyToExisting(rule);
                    EditorUtility.DisplayDialog("Règles d'import", count + " fichier(s) mis à jour dans " + rule.folder + ".", "OK");
                    GUIUtility.ExitGUI();
                }
            }
        }
        if (remove != null) { Rules.Remove(remove); Save(); }

        // Dossier choisi dans la fenetre Project : nouvelle regle pour lui
        string selected = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
        bool isFolder = !string.IsNullOrEmpty(selected) && AssetDatabase.IsValidFolder(selected);
        using (new EditorGUI.DisabledScope(!isFolder))
        {
            if (GUILayout.Button(isFolder ? "Ajouter une règle pour « " + selected + " »" : "Ajouter une règle (sélectionne un dossier dans la fenêtre Project)"))
            {
                Rules.Add(new Rule { label = Path.GetFileName(selected), folder = selected });
                Save();
            }
        }
    }

    // Regles de depart pour les dossiers crees par l'assistant (une regle deja presente pour un dossier est gardee)
    public static void AddDefaultRules(string root)
    {
        void Add(string label, string folder, int size, bool sprites = false, bool normals = false, bool lods = false)
        {
            string path = root + "/" + folder;
            if (Rules.Any(r => string.Equals(r.folder.TrimEnd('/'), path, StringComparison.OrdinalIgnoreCase))) return;
            Rules.Add(new Rule { label = label, folder = path, maxTextureSize = size, sprites = sprites, outlineNormals = normals, meshLods = lods });
        }
        Add("Décor (murs, sols, grands éléments)", "Art/Decor", 2048, lods: true);
        Add("Objets (meubles, objets du puzzle)", "Art/Objets", 1024, lods: true);
        Add("Personnages", "Art/Personnages", 2048, normals: true);
        Add("Effets", "Art/VFX", 512);
        Add("Interface", "UI", 2048, sprites: true);
        Save();
    }
}
