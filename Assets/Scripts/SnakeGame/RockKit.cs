using System.Collections.Generic;
using UnityEngine;

/// Procedural low-poly rocks: three variants built from a noise-deformed
/// icosphere, flat-shaded, with the imported CC0 boulder texture.
/// Meshes are baked to ~1 unit wide with the base at y = -0.5 so a rock
/// sitting at cell height (y = 0.5) rests exactly on the floor (y = 0).
public static class RockKit
{
    static Mesh[] _meshes;
    static Material _mat;

    public static Material Material
    {
        get { if (_mat == null) Build(); return _mat; }
    }

    public static Mesh Mesh(int variant)
    {
        if (_meshes == null) Build();
        return _meshes[Mathf.Abs(variant) % _meshes.Length];
    }

    static void Build()
    {
        _mat = new Material(Shader.Find("Standard"));
        // Loaded from Resources so it ships inside player builds too.
        var tex = Resources.Load<Texture2D>("Textures/boulder_01_diff_1k");
        if (tex != null)
        {
            _mat.mainTexture = tex;
            _mat.color = new Color(0.80f, 0.82f, 0.76f);
        }
        else
        {
            _mat.color = new Color(0.50f, 0.52f, 0.48f);   // stone fallback
        }
        _mat.SetFloat("_Glossiness", 0.12f);

        _meshes = new[] { MakeRock(11), MakeRock(37), MakeRock(73) };
    }

    static Mesh MakeRock(int seed)
    {
        // ---- icosahedron, subdivided once (42 verts -> 80 faces) ----
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var baseV = new[]
        {
            new Vector3(-1,  t, 0), new Vector3( 1,  t, 0), new Vector3(-1, -t, 0), new Vector3( 1, -t, 0),
            new Vector3( 0, -1, t), new Vector3( 0, 1, t), new Vector3( 0, -1,-t), new Vector3( 0, 1,-t),
            new Vector3( t, 0,-1), new Vector3( t, 0, 1), new Vector3(-t, 0,-1), new Vector3(-t, 0, 1)
        };
        int[] baseF =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
        };

        var verts = new List<Vector3>(baseV.Length * 4);
        foreach (var v in baseV) verts.Add(v.normalized);
        var faces = new List<int>(baseF);
        var midCache = new Dictionary<long, int>();

        int Mid(int a, int b)
        {
            long key = ((long)Mathf.Min(a, b) << 32) + Mathf.Max(a, b);
            if (midCache.TryGetValue(key, out int idx)) return idx;
            verts.Add((verts[a] + verts[b]).normalized);
            idx = verts.Count - 1;
            midCache[key] = idx;
            return idx;
        }

        for (int i = faces.Count - 3; i >= 0; i -= 3)
        {
            int a = faces[i], b = faces[i + 1], c = faces[i + 2];
            int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            faces[i] = a; faces[i + 1] = ab; faces[i + 2] = ca;
            faces.Add(ab); faces.Add(b); faces.Add(bc);
            faces.Add(ca); faces.Add(bc); faces.Add(c);
            faces.Add(ab); faces.Add(bc); faces.Add(ca);
        }

        // ---- deform: perlin lumps, squash, flattened base ----
        var rnd = new System.Random(seed);
        float ox = (float)rnd.NextDouble() * 20f;
        float oy = (float)rnd.NextDouble() * 20f;
        float stretchX = 0.85f + (float)rnd.NextDouble() * 0.50f;
        float stretchZ = 0.85f + (float)rnd.NextDouble() * 0.50f;
        float squashY = 0.68f + (float)rnd.NextDouble() * 0.28f;

        for (int i = 0; i < verts.Count; i++)
        {
            var d = verts[i];
            float n = Mathf.PerlinNoise(d.x * 1.6f + ox, d.y * 1.3f + d.z * 0.9f + oy);
            float r = 0.72f + 0.55f * n;
            var p = new Vector3(d.x * r * stretchX, d.y * r * squashY, d.z * r * stretchZ);
            if (p.y < -0.34f) p.y = -0.34f + (p.y + 0.34f) * 0.12f;   // flattened base
            verts[i] = p;
        }

        // ---- normalize: max horizontal extent -> 1.05, base at y = -0.5 ----
        Vector3 mn = verts[0], mx = verts[0];
        foreach (var v in verts) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
        float spanX = mx.x - mn.x, spanZ = mx.z - mn.z;
        float span = Mathf.Max(spanX, spanZ);
        if (span < 1e-5f) span = 1f;
        float scale = 1.05f / span;
        float cx = (mn.x + mx.x) * 0.5f, cz = (mn.z + mx.z) * 0.5f;
        float minY = mn.y * scale;

        // ---- flat-shaded output: unique verts per face ----
        var oV = new Vector3[faces.Count * 3];
        var oN = new Vector3[faces.Count * 3];
        var oUV = new Vector2[faces.Count * 3];
        var oT = new int[faces.Count * 3];

        for (int fi = 0; fi < faces.Count; fi += 3)
        {
            var a = verts[faces[fi]];
            var b = verts[faces[fi + 1]];
            var c = verts[faces[fi + 2]];
            Vector3 cen = (a + b + c) / 3f;
            var nrm = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(nrm, cen) < 0f) nrm = -nrm;    // guarantee outward

            int o = fi * 3;
            for (int k = 0; k < 3; k++)
            {
                var src = k == 0 ? a : k == 1 ? b : c;
                var p = new Vector3((src.x - cx) * scale, src.y * scale - minY - 0.5f, (src.z - cz) * scale);
                oV[o + k] = p;
                oN[o + k] = nrm;
                oUV[o + k] = new Vector2(p.x * 0.6f, p.z * 0.6f);
                oT[o + k] = o + k;
            }
        }

        var mesh = new Mesh { name = "Rock_" + seed };
        mesh.vertices = oV;
        mesh.normals = oN;
        mesh.uv = oUV;
        mesh.triangles = oT;
        mesh.RecalculateBounds();
        return mesh;
    }
}
