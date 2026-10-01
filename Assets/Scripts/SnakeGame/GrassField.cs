using UnityEngine;
using UnityEngine.Rendering;

/// Procedural grass field + snake influence trail.
/// One static mesh (all blades baked in world space) = one draw call.
/// A small CPU field (128x128) is splatted with the snake's segments every
/// frame and decays over ~4s: blades bend away under the snake, the pressed
/// path behind it springs back up.
public class GrassField : MonoBehaviour
{
    public static GrassField Instance { get; private set; }

    const int Res = 128;             // influence map resolution (covers 30x30 world units)
    const float Extent = 15f;        // world half-extent of map & field
    const float SpringTau = 1.1f;    // exp decay time constant (visible recovery ~4s)
    const int Blades = 46000;
    const int SplatR = 3;            // splat radius in texels (~wider pressed band)

    float[] _field;
    Color32[] _px;
    Texture2D _tex;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _field = new float[Res * Res];
        _px = new Color32[Res * Res];
        _tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "GrassInfluence"
        };

        var mat = new Material(Shader.Find("SnakeGame/GrassBlades"));
        mat.SetTexture("_Influence", _tex);
        mat.SetFloat("_MapExtent", Extent);

        var mf = gameObject.AddComponent<MeshFilter>();
        var mr = gameObject.AddComponent<MeshRenderer>();
        mf.sharedMesh = BuildMesh();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    Mesh BuildMesh()
    {
        var verts = new Vector3[Blades * 8];
        var uvs = new Vector2[Blades * 8];
        var cols = new Color[Blades * 8];
        var tris = new int[Blades * 18];   // 3 segments x 2 tris x 3 indices

        // blade profile: 4 rows (t = 0, .33, .66, 1) x 2 sides, width tapers to a tip
        float[] rowT = { 0f, 0.33f, 0.66f, 1f };

        Vector2 clump = Vector2.zero;
        int vi = 0, ti = 0;
        for (int b = 0; b < Blades; b++)
        {
            if (b % 6 == 0)
                clump = new Vector2(Random.Range(-Extent + 0.3f, Extent - 0.3f),
                                    Random.Range(-Extent + 0.3f, Extent - 0.3f));
            Vector2 pos = clump + Random.insideUnitCircle * 0.45f;

            float yaw = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float h = Random.Range(0.34f, 0.62f);
            float halfW = Random.Range(0.018f, 0.036f);
            Vector2 lean = Random.insideUnitCircle * 0.14f;
            float phase = Random.value;

            // per-blade color jitter around 1.0 (hue stays in palette)
            var tint = new Color(Random.Range(0.85f, 1.15f),
                                 Random.Range(0.90f, 1.10f),
                                 Random.Range(0.80f, 1.05f), phase);

            var right = new Vector3(Mathf.Cos(yaw + Mathf.PI * 0.5f), 0f, Mathf.Sin(yaw + Mathf.PI * 0.5f));
            var fwd = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));

            int baseVi = vi;
            for (int r = 0; r < 4; r++)
            {
                float t = rowT[r];
                float taper = 1f - t * 0.85f;
                var center = new Vector3(pos.x + lean.x * t * t, t * h, pos.y + lean.y * t * t);

                verts[vi] = center - right * (halfW * taper);
                verts[vi + 1] = center + right * (halfW * taper);
                uvs[vi] = new Vector2(0f, t);
                uvs[vi + 1] = new Vector2(1f, t);
                cols[vi] = tint;
                cols[vi + 1] = tint;
                vi += 2;
            }

            // 3 segments x 2 tris, wound to face outward (both sides visible via cull off? -> keep front wound CCW)
            for (int r = 0; r < 3; r++)
            {
                int a0 = baseVi + r * 2;
                tris[ti] = a0; tris[ti + 1] = a0 + 3; tris[ti + 2] = a0 + 1;
                tris[ti + 3] = a0; tris[ti + 4] = a0 + 2; tris[ti + 5] = a0 + 3;
                ti += 6;
            }
        }

        var mesh = new Mesh { name = "GrassBlades" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.colors = cols;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    void LateUpdate()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.snake == null) return;

        // spring-back: exponential decay of the whole field
        float decay = Mathf.Exp(-Time.deltaTime / SpringTau);
        for (int i = 0; i < _field.Length; i++)
        {
            float v = _field[i] * decay;
            _field[i] = v < 0.004f ? 0f : v;
        }

        // paint current snake (head + every segment)
        Splat(gm.snake.transform.position);
        var segs = gm.snake.segments;
        for (int i = 0; i < segs.Count; i++)
            if (segs[i] != null) Splat(segs[i].transform.position);

        // upload
        for (int i = 0; i < _field.Length; i++)
        {
            byte b = (byte)(_field[i] * 255f);
            _px[i] = new Color32(b, b, b, 255);
        }
        _tex.SetPixels32(_px);
        _tex.Apply(false);
    }

    void Splat(Vector3 w)
    {
        int cx = (int)((w.x + Extent) / (2f * Extent) * Res);
        int cz = (int)((w.z + Extent) / (2f * Extent) * Res);
        float rMax = SplatR + 0.5f;
        for (int dz = -SplatR; dz <= SplatR; dz++)
        {
            int z = cz + dz;
            if (z < 0 || z >= Res) continue;
            for (int dx = -SplatR; dx <= SplatR; dx++)
            {
                int x = cx + dx;
                if (x < 0 || x >= Res) continue;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > rMax) continue;
                float v = 1f - d / rMax;
                v *= v;
                int idx = z * Res + x;
                if (v > _field[idx]) _field[idx] = v;
            }
        }
    }
}
