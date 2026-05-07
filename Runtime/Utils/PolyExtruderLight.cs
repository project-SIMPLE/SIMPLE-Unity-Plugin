using System.Collections.Generic;
using UnityEngine;

public class PolyExtruderLight : MonoBehaviour
{
    private const float BottomY = 0f;
    private const float Epsilon = 1e-6f;

    private string prismName;
    private Color32 prismColor;
    private float extrusionHeightY = 1f;
    private Vector2[] originalPolygonVertices;

    private MeshFilter prismMeshFilter;
    private MeshRenderer prismMeshRenderer;

    public void createPrism(string name, float height, Vector2[] points, Color32 color, Material material)
    {
        prismName = name;
        prismColor = color;
        extrusionHeightY = Mathf.Max(0.0001f, height);
        originalPolygonVertices = SanitizePoints(points);

        prismMeshFilter = GetComponent<MeshFilter>();
        if (prismMeshFilter == null)
        {
            prismMeshFilter = gameObject.AddComponent<MeshFilter>();
        }

        prismMeshRenderer = GetComponent<MeshRenderer>();
        if (prismMeshRenderer == null)
        {
            prismMeshRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        if (!TryBuildCombinedMesh(originalPolygonVertices, extrusionHeightY, prismName, out Mesh mesh))
        {
            Debug.LogWarning("[PolyExtruderLight] createPrism failed. Invalid polygon for " + prismName);
            return;
        }

        prismMeshFilter.sharedMesh = mesh;
        ApplyMaterial(material, color);
    }

    public void updatePrism(MeshFilter meshFilter, Vector2[] points)
    {
        prismMeshFilter = meshFilter != null ? meshFilter : GetComponent<MeshFilter>();
        if (prismMeshFilter == null)
        {
            prismMeshFilter = gameObject.AddComponent<MeshFilter>();
        }

        if (prismMeshRenderer == null)
        {
            prismMeshRenderer = GetComponent<MeshRenderer>();
        }

        originalPolygonVertices = SanitizePoints(points);
        if (!TryBuildCombinedMesh(originalPolygonVertices, extrusionHeightY, prismName, out Mesh mesh))
        {
            Debug.LogWarning("[PolyExtruderLight] updatePrism failed. Invalid polygon for " + prismName);
            return;
        }

        prismMeshFilter.sharedMesh = mesh;
        if (prismMeshRenderer != null && prismMeshRenderer.sharedMaterial != null)
        {
            SetMaterialColor(prismMeshRenderer.sharedMaterial, prismColor);
        }
    }

    private void ApplyMaterial(Material material, Color32 color)
    {
        Material target = material != null ? new Material(material) : CreateDefaultMaterial();

        // Ensure double-sided rendering for GAMA geometry regardless of source material
        if (target.HasProperty("_Cull"))
        {
            target.SetFloat("_Cull", 0f);
        }

        SetMaterialColor(target, color);
        prismMeshRenderer.sharedMaterial = target;
    }

    private static Material CreateDefaultMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material mat = new Material(shader);

        // Render both faces by default — safety net for inconsistent polygon winding from GAMA.
        // URP Lit _Cull: 0=Off(Both), 1=Front, 2=Back
        if (mat.HasProperty("_Cull"))
        {
            mat.SetFloat("_Cull", 0f);
        }

        return mat;
    }

    private static void SetMaterialColor(Material material, Color32 color)
    {
        if (material == null)
        {
            return;
        }

        Color unityColor = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", unityColor);
        }

        bool hasLegacyColor = material.HasProperty("_Color");
        if (hasLegacyColor)
        {
            material.SetColor("_Color", unityColor);
        }
    }

    private static Vector2[] SanitizePoints(Vector2[] points)
    {
        if (points == null)
        {
            return new Vector2[0];
        }

        List<Vector2> cleaned = new List<Vector2>(points.Length);
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 p = points[i];
            if (cleaned.Count == 0 || Vector2.Distance(cleaned[cleaned.Count - 1], p) > Epsilon)
            {
                cleaned.Add(p);
            }
        }

        if (cleaned.Count > 1 && Vector2.Distance(cleaned[0], cleaned[cleaned.Count - 1]) <= Epsilon)
        {
            cleaned.RemoveAt(cleaned.Count - 1);
        }

        cleaned = RemoveCollinearPoints(cleaned);
        return cleaned.ToArray();
    }

    private static bool TryBuildCombinedMesh(Vector2[] points, float height, string meshName, out Mesh mesh)
    {
        mesh = null;
        if (points == null || points.Length < 3)
        {
            return false;
        }

        float signedArea = ComputeSignedArea(points);
        if (Mathf.Abs(signedArea) <= Epsilon)
        {
            return false;
        }

        Vector2[] polygon = new Vector2[points.Length];
        System.Array.Copy(points, polygon, points.Length);
        if (signedArea < 0f)
        {
            System.Array.Reverse(polygon);
        }

        if (IsSelfIntersecting(polygon))
        {
            polygon = ComputeConvexHull(polygon);
            if (polygon == null || polygon.Length < 3)
            {
                return false;
            }

            if (ComputeSignedArea(polygon) < 0f)
            {
                System.Array.Reverse(polygon);
            }
        }

        List<int> topTriangles = Triangulate(polygon);
        if (topTriangles.Count < 3)
        {
            topTriangles = BuildTriangleFan(polygon.Length);
            if (topTriangles.Count < 3)
            {
                return false;
            }
        }

        int n = polygon.Length;
        List<Vector3> vertices = new List<Vector3>(n * 2);
        for (int i = 0; i < n; i++)
        {
            Vector2 p = polygon[i];
            vertices.Add(new Vector3(p.x, BottomY, p.y));
        }

        for (int i = 0; i < n; i++)
        {
            Vector2 p = polygon[i];
            vertices.Add(new Vector3(p.x, height, p.y));
        }

        List<int> triangles = new List<int>(topTriangles.Count * 2 + n * 6);
        int topOffset = n;

        // --- Top cap (faces UP, visible from above) ---
        // Polygon is CCW in XZ. For Unity's LH system, reverse winding so normal points +Y.
        for (int i = 0; i < topTriangles.Count; i += 3)
        {
            triangles.Add(topOffset + topTriangles[i + 2]);
            triangles.Add(topOffset + topTriangles[i + 1]);
            triangles.Add(topOffset + topTriangles[i]);
        }

        // --- Bottom cap (faces DOWN, visible from below) ---
        // Keep original CCW winding → normal points -Y in Unity's LH system.
        for (int i = 0; i < topTriangles.Count; i += 3)
        {
            triangles.Add(topTriangles[i]);
            triangles.Add(topTriangles[i + 1]);
            triangles.Add(topTriangles[i + 2]);
        }

        // --- Side walls (faces OUTWARD) ---
        // For a CCW polygon in XZ, outward-facing side quads need this winding:
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            int b0 = i;
            int b1 = next;
            int t0 = topOffset + i;
            int t1 = topOffset + next;

            // First triangle of quad: b0 → t0 → b1
            triangles.Add(b0);
            triangles.Add(t0);
            triangles.Add(b1);

            // Second triangle of quad: b1 → t0 → t1
            triangles.Add(b1);
            triangles.Add(t0);
            triangles.Add(t1);
        }

        mesh = new Mesh();
        mesh.name = string.IsNullOrEmpty(meshName) ? "GAMA polygon" : meshName + " mesh";
        if (vertices.Count > 65535)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return true;
    }

    private static float ComputeSignedArea(IReadOnlyList<Vector2> points)
    {
        float area2 = 0f;
        int n = points.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % n];
            area2 += a.x * b.y - b.x * a.y;
        }

        return area2 * 0.5f;
    }

    private static List<int> Triangulate(IReadOnlyList<Vector2> points)
    {
        List<int> result = new List<int>();
        int n = points.Count;
        if (n < 3)
        {
            return result;
        }

        List<int> indices = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            indices.Add(i);
        }

        int guard = 0;
        while (indices.Count > 3 && guard < n * n)
        {
            bool earFound = false;
            for (int i = 0; i < indices.Count; i++)
            {
                int prev = indices[(i - 1 + indices.Count) % indices.Count];
                int curr = indices[i];
                int next = indices[(i + 1) % indices.Count];

                if (!IsConvex(points[prev], points[curr], points[next]))
                {
                    continue;
                }

                bool contains = false;
                for (int j = 0; j < indices.Count; j++)
                {
                    int idx = indices[j];
                    if (idx == prev || idx == curr || idx == next)
                    {
                        continue;
                    }

                    if (PointInTriangle(points[idx], points[prev], points[curr], points[next]))
                    {
                        contains = true;
                        break;
                    }
                }

                if (contains)
                {
                    continue;
                }

                result.Add(prev);
                result.Add(curr);
                result.Add(next);
                indices.RemoveAt(i);
                earFound = true;
                break;
            }

            if (!earFound)
            {
                break;
            }

            guard++;
        }

        if (indices.Count == 3)
        {
            result.Add(indices[0]);
            result.Add(indices[1]);
            result.Add(indices[2]);
        }

        return result;
    }

    private static bool IsConvex(Vector2 a, Vector2 b, Vector2 c)
    {
        return Cross(b - a, c - b) > Epsilon;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float c1 = Cross(b - a, p - a);
        float c2 = Cross(c - b, p - b);
        float c3 = Cross(a - c, p - c);
        return c1 >= -Epsilon && c2 >= -Epsilon && c3 >= -Epsilon;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static List<Vector2> RemoveCollinearPoints(List<Vector2> points)
    {
        if (points.Count < 4)
        {
            return points;
        }

        List<Vector2> reduced = new List<Vector2>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 prev = points[(i - 1 + points.Count) % points.Count];
            Vector2 curr = points[i];
            Vector2 next = points[(i + 1) % points.Count];
            if (Mathf.Abs(Cross(curr - prev, next - curr)) > Epsilon)
            {
                reduced.Add(curr);
            }
        }

        return reduced.Count >= 3 ? reduced : points;
    }

    private static bool IsSelfIntersecting(IReadOnlyList<Vector2> polygon)
    {
        int n = polygon.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 a1 = polygon[i];
            Vector2 a2 = polygon[(i + 1) % n];
            for (int j = i + 1; j < n; j++)
            {
                if (Mathf.Abs(i - j) <= 1 || (i == 0 && j == n - 1))
                {
                    continue;
                }

                Vector2 b1 = polygon[j];
                Vector2 b2 = polygon[(j + 1) % n];
                if (SegmentsIntersect(a1, a2, b1, b2))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        float o1 = Cross(p2 - p1, q1 - p1);
        float o2 = Cross(p2 - p1, q2 - p1);
        float o3 = Cross(q2 - q1, p1 - q1);
        float o4 = Cross(q2 - q1, p2 - q1);
        return (o1 * o2 < -Epsilon) && (o3 * o4 < -Epsilon);
    }

    private static Vector2[] ComputeConvexHull(Vector2[] points)
    {
        if (points == null || points.Length < 3)
        {
            return points;
        }

        List<Vector2> sorted = new List<Vector2>(points);
        sorted.Sort((a, b) =>
        {
            int cmpX = a.x.CompareTo(b.x);
            return cmpX != 0 ? cmpX : a.y.CompareTo(b.y);
        });

        List<Vector2> hull = new List<Vector2>();
        for (int i = 0; i < sorted.Count; i++)
        {
            while (hull.Count >= 2 &&
                   Cross(hull[hull.Count - 1] - hull[hull.Count - 2], sorted[i] - hull[hull.Count - 1]) <= Epsilon)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(sorted[i]);
        }

        int lowerCount = hull.Count;
        for (int i = sorted.Count - 2; i >= 0; i--)
        {
            while (hull.Count > lowerCount &&
                   Cross(hull[hull.Count - 1] - hull[hull.Count - 2], sorted[i] - hull[hull.Count - 1]) <= Epsilon)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(sorted[i]);
        }

        if (hull.Count > 1)
        {
            hull.RemoveAt(hull.Count - 1);
        }

        return hull.ToArray();
    }

    private static List<int> BuildTriangleFan(int count)
    {
        List<int> fan = new List<int>();
        if (count < 3)
        {
            return fan;
        }

        for (int i = 1; i < count - 1; i++)
        {
            fan.Add(0);
            fan.Add(i);
            fan.Add(i + 1);
        }

        return fan;
    }
}
