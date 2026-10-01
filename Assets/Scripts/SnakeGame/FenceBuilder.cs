using System.Collections.Generic;
using UnityEngine;

/// Garden fence: four picket-fence sides combined into ONE mesh, built at
/// runtime. Hides the old blue wall blocks while playing. Posts with pyramid
/// caps, two rails, pointed spacing of flat-top pickets, natural-brown CC0
/// plank texture (albedo + normal).
public class FenceBuilder : MonoBehaviour
{
    const float Half = 15f;     // fence line = old wall positions
    const float Len = 14.9f;    // inset for perpendicular sides (kills corner z-fighting)
    const float PostStep = 2f;
    const float Tile = 0.55f;   // texture repeats per world unit

    void Awake()
    {
        // hide the blue walls for the garden look (runtime only)
        var walls = GameObject.Find("/AllWall");
        if (walls != null)
            foreach (var r in walls.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;

        var mf = gameObject.AddComponent<MeshFilter>();
        var mr = gameObject.AddComponent<MeshRenderer>();
        mf.sharedMesh = Build();
        mr.sharedMaterial = MakeMat();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mr.receiveShadows = true;
    }

    static Material MakeMat()
    {
        var m = new Material(Shader.Find("Standard"));
        // Loaded from Resources so the textures ship inside player builds too.
        var alb = Resources.Load<Texture2D>("Textures/brown_planks_03_diff_1k");
        var nor = Resources.Load<Texture2D>("Textures/brown_planks_03_nor_gl_1k");
        if (alb != null) m.mainTexture = alb;
        m.color = new Color(0.98f, 0.85f, 0.66f);   // warm natural-wood brown
        if (nor != null)
        {
            m.SetTexture("_BumpMap", nor);
            m.SetFloat("_BumpScale", 0.6f);
            m.EnableKeyword("_NORMALMAP");
        }
        m.SetFloat("_Glossiness", 0.15f);
        return m;
    }

    Mesh Build()
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        // side 0,1 run along X (z = -/+15, full length)
        // side 2,3 run along Z (x = -/+15, inset so corner posts stay unique)
        for (int side = 0; side < 4; side++)
        {
            bool alongX = side < 2;
            float fixedPos = (side % 2 == 0) ? -Half : Half;
            float a0 = alongX ? -Half : -Len;
            float a1 = alongX ?  Half :  Len;

            // --- posts (pyramid-capped) ---
            for (float a = a0; a <= a1 + 0.001f; a += PostStep)
            {
                if (!alongX && Mathf.Abs(Mathf.Abs(a) - Half) < 0.01f) continue;   // corner posts belong to X sides
                Vector3 basePos = alongX ? new Vector3(a, 0f, fixedPos) : new Vector3(fixedPos, 0f, a);
                AddBox(basePos + new Vector3(0f, 0.675f, 0f), new Vector3(0.14f, 1.35f, 0.14f));
                AddCap(basePos + new Vector3(0f, 1.35f, 0f), 0.14f, 0.20f);
            }

            // --- two horizontal rails ---
            float railLen = a1 - a0;
            Vector3 railMid = alongX
                ? new Vector3((a0 + a1) * 0.5f, 0f, fixedPos)
                : new Vector3(fixedPos, 0f, (a0 + a1) * 0.5f);
            Vector3 railSize = alongX
                ? new Vector3(railLen, 0.10f, 0.06f)
                : new Vector3(0.06f, 0.10f, railLen);
            AddBox(railMid + new Vector3(0f, 0.42f, 0f), railSize);
            AddBox(railMid + new Vector3(0f, 0.98f, 0f), railSize);

            // --- pickets, skipping post slots ---
            for (float a = a0 + 0.25f; a < a1 - 0.1f; a += 0.25f)
            {
                float nearestPost = Mathf.Round((a - a0) / PostStep) * PostStep + a0;
                if (Mathf.Abs(a - nearestPost) < 0.15f) continue;
                Vector3 p = alongX ? new Vector3(a, 0f, fixedPos) : new Vector3(fixedPos, 0f, a);
                AddBox(p + new Vector3(0f, 0.46f, 0f),
                       alongX ? new Vector3(0.11f, 0.92f, 0.045f) : new Vector3(0.045f, 0.92f, 0.11f));
            }
        }

        var mesh = new Mesh { name = "GardenFence" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;

        // ---------------- geometry helpers ----------------

        void AddBox(Vector3 c, Vector3 s)
        {
            Vector3 h = s * 0.5f;
            AddFace(c, Vector3.right, h);
            AddFace(c, Vector3.left, h);
            AddFace(c, Vector3.up, h);
            AddFace(c, Vector3.down, h);
            AddFace(c, Vector3.forward, h);
            AddFace(c, Vector3.back, h);
        }

        void AddFace(Vector3 c, Vector3 n, Vector3 h)
        {
            // center of this face
            Vector3 fc = c + new Vector3(n.x * h.x, n.y * h.y, n.z * h.z);
            // tangent frame with u x v = n
            Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up;
            Vector3 v = Vector3.Cross(n, u);
            float hu = Mathf.Abs(u.x) * h.x + Mathf.Abs(u.y) * h.y + Mathf.Abs(u.z) * h.z;
            float hv = Mathf.Abs(v.x) * h.x + Mathf.Abs(v.y) * h.y + Mathf.Abs(v.z) * h.z;

            int i = verts.Count;
            verts.Add(fc + (-u * hu - v * hv));
            verts.Add(fc + ( u * hu - v * hv));
            verts.Add(fc + ( u * hu + v * hv));
            verts.Add(fc + (-u * hu + v * hv));
            for (int k = 0; k < 4; k++) norms.Add(n);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(hu * 2f * Tile, 0f));
            uvs.Add(new Vector2(hu * 2f * Tile, hv * 2f * Tile));
            uvs.Add(new Vector2(0f, hv * 2f * Tile));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        // Pyramid cap on top of a post: 4 slanted tris, outward-flipped.
        void AddCap(Vector3 top, float width, float height)
        {
            float w = width * 0.5f;
            var b = new[]
            {
                top + new Vector3(-w, 0f, -w),
                top + new Vector3( w, 0f, -w),
                top + new Vector3( w, 0f,  w),
                top + new Vector3(-w, 0f,  w)
            };
            Vector3 apex = top + new Vector3(0f, height, 0f);
            Vector3 center = top;
            for (int k = 0; k < 4; k++)
            {
                var b0 = b[k];
                var b1 = b[(k + 1) % 4];
                Vector3 n = Vector3.Cross(b1 - b0, apex - b0);
                Vector3 outDir = (b0 + b1) * 0.5f - center;
                int i = verts.Count;
                if (Vector3.Dot(n, outDir) < 0f) { var tmp = b0; b0 = b1; b1 = tmp; }   // fix winding
                verts.Add(b0); verts.Add(b1); verts.Add(apex);
                Vector3 fn = Vector3.Cross(b1 - b0, apex - b0).normalized;
                if (Vector3.Dot(fn, outDir) < 0f) fn = -fn;
                norms.Add(fn); norms.Add(fn); norms.Add(fn);
                uvs.Add(new Vector2((b0.x - center.x) * Tile, (b0.z - center.z) * Tile));
                uvs.Add(new Vector2((b1.x - center.x) * Tile, (b1.z - center.z) * Tile));
                uvs.Add(new Vector2((apex.x - center.x) * Tile, (apex.z - center.z) * Tile));
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }
        }
    }
}
