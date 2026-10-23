using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Eclairage "en sphere" des visages de ToonAdvanced (partie "Visage" de l'outil), calcule a l'import du modele.
// Avec ses vraies normales, le visage s'eclaire selon son relief (nez, pommettes, bouche) et l'ombre s'y decoupe
// en taches anguleuses. On ecrit dans l'UV 7 (TEXCOORD6) la direction depuis le centre de la tete : le visage
// s'eclaire alors comme une forme ronde, avec une seule limite d'ombre nette. Exprimee dans l'espace tangent
// du sommet pour suivre aussi la tete animee (skinning).
// Seuls les sous-maillages marques par l'outil (userData de l'importeur) sont traites ; le fichier source n'est pas modifie.
public class ToonFaceNormals : AssetPostprocessor
{
    public const int UVChannel = 6;
    private const string Start = "ToonFace{";
    private const string End = "}";

    public override uint GetVersion() => 1;

    // Parties marquees, sous la forme "nomDuMaillage:indexDuSousMaillage"
    public static HashSet<string> GetParts(string userData)
    {
        var parts = new HashSet<string>();
        if (string.IsNullOrEmpty(userData)) return parts;
        int start = userData.IndexOf(Start);
        if (start < 0) return parts;
        start += Start.Length;
        int end = userData.IndexOf(End, start);
        if (end < 0) return parts;
        foreach (var part in userData.Substring(start, end - start).Split('|'))
            if (part.Length > 0) parts.Add(part);
        return parts;
    }

    public static string PartName(Mesh mesh, int subMesh) => mesh.name + ":" + subMesh;

    public static bool IsMarked(string modelPath, string part)
    {
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        return importer != null && GetParts(importer.userData).Contains(part);
    }

    // Ajoute ou retire des parties puis reimporte le modele (seulement si quelque chose change)
    public static void SetMarked(string modelPath, IEnumerable<string> parts, bool marked)
    {
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null) return;
        var current = GetParts(importer.userData);
        bool changed = false;
        foreach (var part in parts)
            changed |= marked ? current.Add(part) : current.Remove(part);
        if (!changed) return;

        // Le reste du userData (marque des normales du contour...) est garde tel quel
        string data = importer.userData ?? "";
        int start = data.IndexOf(Start);
        if (start >= 0)
        {
            int end = data.IndexOf(End, start + Start.Length);
            data = data.Remove(start, (end < 0 ? data.Length : end + End.Length) - start);
        }
        data = data.Trim();
        if (current.Count > 0)
            data = (data + " " + Start + string.Join("|", current.OrderBy(p => p)) + End).Trim();
        importer.userData = data;
        importer.SaveAndReimport();
    }

    private void OnPostprocessModel(GameObject root)
    {
        var parts = GetParts(assetImporter.userData);
        if (parts.Count == 0) return;

        // Sommets du visage de chaque maillage concerne
        var targets = new List<(Mesh mesh, Transform transform, HashSet<int> vertices)>();
        var done = new HashSet<Mesh>();
        void Add(Mesh mesh, Transform transform)
        {
            if (mesh == null || !done.Add(mesh)) return;
            var vertices = new HashSet<int>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                if (parts.Contains(PartName(mesh, sub)))
                    vertices.UnionWith(mesh.GetIndices(sub));
            if (vertices.Count > 0) targets.Add((mesh, transform, vertices));
        }
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            Add(filter.sharedMesh, filter.transform);
        foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            Add(skinned.sharedMesh, skinned.transform);
        if (targets.Count == 0) return;

        // Sommets du visage dans l'espace du modele (le visage peut etre coupe en plusieurs maillages)
        var points = new List<Vector3>();
        foreach (var (mesh, transform, vertices) in targets)
        {
            var positions = mesh.vertices;
            foreach (int i in vertices)
                points.Add(root.transform.InverseTransformPoint(transform.TransformPoint(positions[i])));
        }
        Vector3 center = FitHeadCenter(points);

        foreach (var (mesh, transform, vertices) in targets)
            Bake(mesh, transform, root.transform, center, vertices);
    }

    // Centre de la tete : centre de la sphere qui epouse au mieux les sommets du visage (moindres carres).
    // Le milieu des sommets ne suffit pas : quand le visage est coupe en morceaux (bas du visage d'un cote,
    // yeux et front de l'autre), il tomberait au milieu du seul morceau marque, trop bas ou trop en avant.
    // Forme presque plate (sphere immense) ou calcul impossible : on garde le milieu des sommets.
    public static Vector3 FitHeadCenter(List<Vector3> points)
    {
        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (var p in points) bounds.Encapsulate(p);
        Vector3 origin = bounds.center;

        // |p|^2 = 2 c.p + k, lineaire en (c, k) : equations normales 4 x 4 (coordonnees centrees pour la precision)
        var a = new double[4, 5];
        foreach (var point in points)
        {
            Vector3 p = point - origin;
            double[] row = { 2.0 * p.x, 2.0 * p.y, 2.0 * p.z, 1.0 };
            double rhs = (double)p.x * p.x + (double)p.y * p.y + (double)p.z * p.z;
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++) a[r, c] += row[r] * row[c];
                a[r, 4] += row[r] * rhs;
            }
        }
        // Pivot de Gauss
        for (int col = 0; col < 4; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < 4; r++)
                if (System.Math.Abs(a[r, col]) > System.Math.Abs(a[pivot, col])) pivot = r;
            if (System.Math.Abs(a[pivot, col]) < 1e-12) return origin;
            for (int c = 0; c < 5; c++) { double t = a[col, c]; a[col, c] = a[pivot, c]; a[pivot, c] = t; }
            for (int r = 0; r < 4; r++)
            {
                if (r == col) continue;
                double f = a[r, col] / a[col, col];
                for (int c = col; c < 5; c++) a[r, c] -= f * a[col, c];
            }
        }
        var center = new Vector3((float)(a[0, 4] / a[0, 0]), (float)(a[1, 4] / a[1, 1]), (float)(a[2, 4] / a[2, 2]));
        float size = bounds.size.magnitude;
        if (float.IsNaN(center.x) || float.IsNaN(center.y) || float.IsNaN(center.z) || center.magnitude > size) return origin;
        return origin + center;
    }

    private static void Bake(Mesh mesh, Transform transform, Transform root, Vector3 center, HashSet<int> vertices)
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

        // Les autres sommets du maillage (corps dans le meme maillage...) gardent 0 : le shader prend alors leur vraie normale
        var face = new List<Vector3>(new Vector3[positions.Length]);
        foreach (int i in vertices)
        {
            Vector3 fromCenter = root.InverseTransformPoint(transform.TransformPoint(positions[i])) - center;
            // Tete un peu plus haute que large : l'ombre monte et descend moins vite qu'avec une sphere parfaite
            fromCenter.y *= 0.8f;
            if (fromCenter.sqrMagnitude < 1e-12f) continue;
            Vector3 s = transform.InverseTransformDirection(root.TransformDirection(fromCenter.normalized)).normalized;

            Vector3 normal = normals[i];
            Vector3 tangent = tangents[i];
            Vector3 bitangent = Vector3.Cross(normal, tangent) * tangents[i].w;
            face[i] = new Vector3(Vector3.Dot(s, tangent), Vector3.Dot(s, bitangent), Vector3.Dot(s, normal));
        }
        mesh.SetUVs(UVChannel, face);
    }
}
