using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Normales lissees pour le contour de ToonAdvanced, calculees a l'import du modele.
// Aux aretes vives et aux coutures d'UV, un meme point a plusieurs normales : la coque inversee du contour
// s'y ouvre et laisse des trous. On ecrit dans l'UV 8 (TEXCOORD7) la moyenne des normales de chaque position,
// exprimee dans l'espace tangent du sommet pour qu'elle suive aussi les maillages animes (skinning).
// Seuls les modeles marques par l'outil (userData de l'importeur) sont traites ; le fichier source n'est pas modifie.
public class ToonOutlineNormals : AssetPostprocessor
{
    public const string Marker = "ToonOutlineNormals";
    public const int UVChannel = 7;

    public override uint GetVersion() => 1;

    public static bool IsMarked(string modelPath)
    {
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        return importer != null && importer.userData != null && importer.userData.Contains(Marker);
    }

    // Ajoute ou retire la marque puis reimporte le modele
    public static void SetMarked(string modelPath, bool marked)
    {
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null || IsMarked(modelPath) == marked) return;
        string data = importer.userData ?? "";
        importer.userData = marked ? (data + " " + Marker).Trim() : data.Replace(Marker, "").Trim();
        importer.SaveAndReimport();
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (assetImporter.userData == null || !assetImporter.userData.Contains(Marker)) return;

        var meshes = new HashSet<Mesh>();
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh != null) meshes.Add(filter.sharedMesh);
        foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (skinned.sharedMesh != null) meshes.Add(skinned.sharedMesh);

        foreach (var mesh in meshes)
            Bake(mesh);
    }

    private static void Bake(Mesh mesh)
    {
        var positions = mesh.vertices;
        var normals = mesh.normals;
        if (normals.Length != positions.Length) return;
        var tangents = mesh.tangents;
        if (tangents.Length != positions.Length)
        {
            mesh.RecalculateTangents();
            tangents = mesh.tangents;
            if (tangents.Length != positions.Length) return;
        }

        // Normales distinctes par position (les copies d'une couture d'UV ont la meme : comptee une fois)
        var byPosition = new Dictionary<Vector3, List<Vector3>>();
        for (int i = 0; i < positions.Length; i++)
        {
            if (!byPosition.TryGetValue(positions[i], out var list))
                byPosition[positions[i]] = list = new List<Vector3>(2);
            bool known = false;
            foreach (var n in list)
                if (Vector3.Dot(n, normals[i]) > 0.999f) { known = true; break; }
            if (!known) list.Add(normals[i]);
        }

        var smooth = new List<Vector3>(positions.Length);
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 sum = Vector3.zero;
            foreach (var n in byPosition[positions[i]]) sum += n;
            Vector3 s = sum.sqrMagnitude > 1e-8f ? sum.normalized : normals[i];

            Vector3 normal = normals[i];
            Vector3 tangent = tangents[i];
            Vector3 bitangent = Vector3.Cross(normal, tangent) * tangents[i].w;
            smooth.Add(new Vector3(Vector3.Dot(s, tangent), Vector3.Dot(s, bitangent), Vector3.Dot(s, normal)));
        }
        mesh.SetUVs(UVChannel, smooth);
    }
}
