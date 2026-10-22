using UnityEditor;
using UnityEngine;

// Attribut [ToonOutline] de ToonAdvanced : la case "Activer" du contour active ou coupe aussi la passe Outline
// du materiau. Coupee, URP ne la dessine pas du tout (pas de draw call, pas de sommets traites) :
// les decors en ToonAdvanced ne paient donc rien pour le contour.
public class ToonOutlineDrawer : MaterialPropertyDrawer
{
    public const string PassLightMode = "SRPDefaultUnlit";

    public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
    {
        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = prop.hasMixedValue;
        bool value = EditorGUI.Toggle(position, label, prop.floatValue > 0.5f);
        EditorGUI.showMixedValue = false;
        if (EditorGUI.EndChangeCheck())
        {
            prop.floatValue = value ? 1f : 0f;
            Apply(prop);
        }
    }

    // Appelee aussi par MaterialEditor.ApplyMaterialPropertyDrawers (changement de shader, outil, conversion)
    public override void Apply(MaterialProperty prop)
    {
        base.Apply(prop);
        if (prop.hasMixedValue) return;
        foreach (Object target in prop.targets)
        {
            if (target is Material mat)
                mat.SetShaderPassEnabled(PassLightMode, prop.floatValue > 0.5f);
        }
    }

    // Un materiau a toutes ses passes actives par defaut : ceux crees avant l'ajout du contour dessineraient
    // la passe Outline pour rien (le shader n'y trace rien, mais le draw call reste). Une fois par projet, on la
    // coupe sur tous les materiaux ToonAdvanced du dossier Assets dont le contour est desactive.
    [InitializeOnLoadMethod]
    private static void DisableUnusedOutlinePassesOnce()
    {
        // Les processus d'import en arriere-plan chargent aussi les scripts d'editeur : pas de nettoyage la-bas
        if (AssetDatabase.IsAssetImportWorkerProcess()) return;
        string key = "ToonOutlinePassCleanup_v1_" + Application.dataPath;
        if (EditorPrefs.GetBool(key, false)) return;
        EditorApplication.delayCall += () =>
        {
            EditorPrefs.SetBool(key, true);
            DisableUnusedOutlinePasses();
        };
    }

    public static int DisableUnusedOutlinePasses()
    {
        var shader = Shader.Find("Custom/ToonAdvanced");
        if (shader == null) return 0;
        // Lecture du texte des .mat (rapide) plutot que de charger tous les materiaux du projet
        string shaderGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(shader));
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".mat") || !System.IO.File.ReadAllText(path).Contains(shaderGuid)) continue;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader != shader || !mat.GetShaderPassEnabled(PassLightMode) || mat.GetFloat("_Outline") > 0.5f) continue;
            mat.SetShaderPassEnabled(PassLightMode, false);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            count++;
        }
        return count;
    }
}
