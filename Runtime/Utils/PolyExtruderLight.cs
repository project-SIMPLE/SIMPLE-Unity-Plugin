using UnityEngine;
using System.Collections.Generic;

public class PolyExtruderLight : MonoBehaviour
{
    private const float Epsilon = 0.0001f;

    public Mesh surroundMesh;
    public Mesh bottomMesh;
    public Mesh topMesh;

    private float currentHeight = 1f;

    public void createPrism(string name, float height, Vector2[] points, Color32 color, Material material)
    {
        currentHeight = Mathf.Max(height, Epsilon);

        MeshFilter mf = GamaSceneUtility.GetOrAddComponent<MeshFilter>(gameObject);
        MeshRenderer mr = GamaSceneUtility.GetOrAddComponent<MeshRenderer>(gameObject);

        RebuildMeshes(mf, points);

        Material runtimeMaterial = material != null ? new Material(material) : CreateFallbackMaterial(color);
        ApplyColor(runtimeMaterial, color);
        mr.sharedMaterial = runtimeMaterial;
    }

    public void updatePrism(MeshFilter mf, Vector2[] points)
    {
        if (mf == null)
        {
            mf = GetComponent<MeshFilter>();
        }

        RebuildMeshes(mf, points);
    }

    private void RebuildMeshes(MeshFilter meshFilter, Vector2[] points)
    {
        Vector2[] cleanPoints = CleanPoints(points);
        if (cleanPoints.Length < 3 || meshFilter == null)
        {
            return;
        }

        List<int> baseTriangles = Triangulate(cleanPoints);
        if (baseTriangles.Count < 3)
        {
            baseTriangles = FanTriangulate(cleanPoints);
        }

        Mesh fullMesh = BuildFullMesh(cleanPoints, baseTriangles, currentHeight);
        surroundMesh = BuildSideMesh(cleanPoints, currentHeight);
        bottomMesh = BuildFlatMesh(cleanPoints, baseTriangles, 0f, true);
        topMesh = BuildFlatMesh(cleanPoints, baseTriangles, currentHeight, true);

        fullMesh.name = gameObject.name + "_Prism";
        meshFilter.sharedMesh = fullMesh;
    }

    private static Mesh BuildFullMesh(Vector2[] points, List<int> baseTriangles, float height)
    {
        int count = points.Length;
        Vector3[] vertices = new Vector3[count * 2];
        for (int i = 0; i < count; i++)
        {
            vertices[i] = new Vector3(points[i].x, 0f, points[i].y);
            vertices[i + count] = new Vector3(points[i].x, height, points[i].y);
        }

        List<int> triangles = new List<int>();
        for (int i = 0; i < baseTriangles.Count; i += 3)
        {
            int a = baseTriangles[i];
            int b = baseTriangles[i + 1];
            int c = baseTriangles[i + 2];

            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);

            triangles.Add(c + count);
            triangles.Add(b + count);
            triangles.Add(a + count);
        }

        AddSideTriangles(triangles, count, count);
        return CreateMesh(vertices, triangles);
    }

    private static Mesh BuildSideMesh(Vector2[] points, float height)
    {
        int count = points.Length;
        Vector3[] vertices = new Vector3[count * 2];
        for (int i = 0; i < count; i++)
        {
            vertices[i] = new Vector3(points[i].x, 0f, points[i].y);
            vertices[i + count] = new Vector3(points[i].x, height, points[i].y);
        }

        List<int> triangles = new List<int>();
        AddSideTriangles(triangles, count, count);
        return CreateMesh(vertices, triangles);
    }

    private static Mesh BuildFlatMesh(Vector2[] points, List<int> baseTriangles, float y, bool upwardNormal)
    {
        Vector3[] vertices = new Vector3[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            vertices[i] = new Vector3(points[i].x, y, points[i].y);
        }

        List<int> triangles = new List<int>();
        for (int i = 0; i < baseTriangles.Count; i += 3)
        {
            if (upwardNormal)
            {
                triangles.Add(baseTriangles[i + 2]);
                triangles.Add(baseTriangles[i + 1]);
                triangles.Add(baseTriangles[i]);
            }
            else
            {
                triangles.Add(baseTriangles[i]);
                triangles.Add(baseTriangles[i + 1]);
                triangles.Add(baseTriangles[i + 2]);
            }
        }

        return CreateMesh(vertices, triangles);
    }

    private static void AddSideTriangles(List<int> triangles, int pointCount, int topOffset)
    {
        for (int i = 0; i < pointCount; i++)
        {
            int next = (i + 1) % pointCount;

            triangles.Add(i);
            triangles.Add(i + topOffset);
            triangles.Add(next + topOffset);

            triangles.Add(i);
            triangles.Add(next + topOffset);
            triangles.Add(next);
        }
    }

    private static Mesh CreateMesh(Vector3[] vertices, List<int> triangles)
    {
        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector2[] CleanPoints(Vector2[] points)
    {
        if (points == null)
        {
            return new Vector2[0];
        }

        List<Vector2> clean = new List<Vector2>();
        for (int i = 0; i < points.Length; i++)
        {
            if (clean.Count == 0 || Vector2.Distance(clean[clean.Count - 1], points[i]) > Epsilon)
            {
                clean.Add(points[i]);
            }
        }

        if (clean.Count > 1 && Vector2.Distance(clean[0], clean[clean.Count - 1]) <= Epsilon)
        {
            clean.RemoveAt(clean.Count - 1);
        }

        return clean.ToArray();
    }

    private static List<int> Triangulate(Vector2[] points)
    {
        List<int> indices = new List<int>();
        int n = points.Length;
        if (n < 3)
        {
            return indices;
        }

        int[] vertexOrder = new int[n];
        if (SignedArea(points) > 0f)
        {
            for (int i = 0; i < n; i++)
            {
                vertexOrder[i] = i;
            }
        }
        else
        {
            for (int i = 0; i < n; i++)
            {
                vertexOrder[i] = (n - 1) - i;
            }
        }

        int remaining = n;
        int guard = 2 * remaining;
        for (int v = remaining - 1; remaining > 2;)
        {
            if (guard-- <= 0)
            {
                return new List<int>();
            }

            int u = v;
            if (remaining <= u)
            {
                u = 0;
            }

            v = u + 1;
            if (remaining <= v)
            {
                v = 0;
            }

            int w = v + 1;
            if (remaining <= w)
            {
                w = 0;
            }

            if (!Snip(points, u, v, w, remaining, vertexOrder))
            {
                continue;
            }

            indices.Add(vertexOrder[u]);
            indices.Add(vertexOrder[v]);
            indices.Add(vertexOrder[w]);

            for (int s = v, t = v + 1; t < remaining; s++, t++)
            {
                vertexOrder[s] = vertexOrder[t];
            }

            remaining--;
            guard = 2 * remaining;
        }

        return indices;
    }

    private static List<int> FanTriangulate(Vector2[] points)
    {
        List<int> triangles = new List<int>();
        for (int i = 1; i < points.Length - 1; i++)
        {
            triangles.Add(0);
            triangles.Add(i);
            triangles.Add(i + 1);
        }

        return triangles;
    }

    private static bool Snip(Vector2[] points, int u, int v, int w, int n, int[] vertexOrder)
    {
        Vector2 a = points[vertexOrder[u]];
        Vector2 b = points[vertexOrder[v]];
        Vector2 c = points[vertexOrder[w]];

        if (Epsilon > (((b.x - a.x) * (c.y - a.y)) - ((b.y - a.y) * (c.x - a.x))))
        {
            return false;
        }

        for (int p = 0; p < n; p++)
        {
            if (p == u || p == v || p == w)
            {
                continue;
            }

            Vector2 point = points[vertexOrder[p]];
            if (InsideTriangle(a, b, c, point))
            {
                return false;
            }
        }

        return true;
    }

    private static bool InsideTriangle(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
    {
        float ax = c.x - b.x;
        float ay = c.y - b.y;
        float bx = a.x - c.x;
        float by = a.y - c.y;
        float cx = b.x - a.x;
        float cy = b.y - a.y;
        float apx = p.x - a.x;
        float apy = p.y - a.y;
        float bpx = p.x - b.x;
        float bpy = p.y - b.y;
        float cpx = p.x - c.x;
        float cpy = p.y - c.y;

        float aCross = ax * bpy - ay * bpx;
        float bCross = bx * cpy - by * cpx;
        float cCross = cx * apy - cy * apx;

        return aCross >= 0f && bCross >= 0f && cCross >= 0f;
    }

    private static float SignedArea(Vector2[] points)
    {
        float area = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            int next = (i + 1) % points.Length;
            area += points[i].x * points[next].y - points[next].x * points[i].y;
        }

        return area * 0.5f;
    }

    private static Material CreateFallbackMaterial(Color32 color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Hidden/InternalErrorShader");
        }

        Material material = new Material(shader);
        ApplyColor(material, color);
        return material;
    }

    private static void ApplyColor(Material material, Color32 color)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }
}
