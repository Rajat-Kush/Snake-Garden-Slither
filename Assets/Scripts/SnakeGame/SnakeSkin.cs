using System.Collections.Generic;
using UnityEngine;

/// Procedural snake visuals:
///  - a continuous tapered body tube that follows the glide spine (slithers
///    through turns because it is a smooth spline, not stacked spheres),
///  - a curved low-poly head mesh (replaces the cube),
///  - a forked tongue that flicks,
///  - a generated emerald + gold diamond skin pattern shared by head & body.
/// The old sphere segments stay in the logic pool but are never rendered.
public class SnakeSkin : MonoBehaviour
{
    public static SnakeSkin Instance { get; private set; }

    const int RingVerts = 11;        // 10 sides + duplicated seam for clean UVs
    const int MaxSpine = 450;        // safety clamp -> rings <= 2*450-1 = 899
    const float BodyRepeat = 1.5f;   // world units per pattern tile along the body
    const float WaveAmp = 0.085f;    // lateral undulation amplitude (kept subtle)
    const float WaveFreq = 2.5f;     // rad per world unit (~2.5u wavelength)
    const float WaveSpeed = 9f;      // rad/s -> waves travel head-to-tail

    static Texture2D _pattern;
    static Mesh _headMesh;
    static Mesh _tongueMesh;

    public static Texture2D Pattern => _pattern != null ? _pattern : _pattern = BuildPattern();

    Snake _snake;
    MeshFilter _bodyMf;
    MeshRenderer _bodyMr;
    Transform _tongue;
    Vector3 _tongueBase;
    Mesh _bodyMesh;
    float _slitherPhase; // advances only while the snake is alive

    readonly List<Vector3> _spine = new List<Vector3>(512);
    readonly List<Vector3> _ringC = new List<Vector3>(1024);
    readonly List<Vector3> _verts = new List<Vector3>(10240);
    readonly List<Vector3> _norms = new List<Vector3>(10240);
    readonly List<Vector2> _uvs = new List<Vector2>(10240);
    readonly List<int> _tris = new List<int>(60000);

    /// Wire visuals to a freshly reset snake (idempotent per play session).
    public static void Attach(Snake snake)
    {
        if (Instance == null)
        {
            var go = new GameObject("SnakeSkin");
            Instance = go.AddComponent<SnakeSkin>();
        }
        Instance.Bind(snake);
    }

    void Bind(Snake snake)
    {
        _snake = snake;

        // curved head in place of the cube
        var headMf = snake.GetComponent<MeshFilter>();
        if (headMf != null) headMf.sharedMesh = HeadMesh;

        // body tube (layer 2: glow lights skip it, same as the old spheres)
        if (_bodyMf == null)
        {
            var go = new GameObject("SnakeBody");
            go.layer = 2;
            _bodyMf = go.AddComponent<MeshFilter>();
            _bodyMr = go.AddComponent<MeshRenderer>();
            _bodyMesh = new Mesh { name = "SnakeBodyMesh" };
            _bodyMesh.MarkDynamic();
            _bodyMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _bodyMf.sharedMesh = _bodyMesh;
            _bodyMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _bodyMr.receiveShadows = true;
        }
        if (Snake.SegMat != null) _bodyMr.sharedMaterial = Snake.SegMat;

        // forked tongue
        if (_tongue == null)
        {
            var tg = new GameObject("Tongue");
            tg.layer = 2;
            tg.transform.SetParent(snake.transform, false);
            var tf = tg.AddComponent<MeshFilter>();
            var tr = tg.AddComponent<MeshRenderer>();
            tf.sharedMesh = TongueMesh;
            var tm = new Material(Shader.Find("Standard"));
            tm.color = new Color(0.78f, 0.11f, 0.14f);
            tm.EnableKeyword("_EMISSION");
            tm.SetColor("_EmissionColor", new Color(0.18f, 0.02f, 0.03f));
            tr.sharedMaterial = tm;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tongue = tg.transform;
            _tongueBase = new Vector3(0f, -0.05f, 0.54f);
        }
        _tongue.SetParent(snake.transform, false);
        _tongue.localPosition = _tongueBase;
        _tongue.localScale = Vector3.one;
    }

    void LateUpdate()
    {
        if (_snake == null) return;
        if (_snake.IsMoving)
            _slitherPhase += Time.deltaTime * WaveSpeed;   // freeze mid-wave on death
        AnimateTongue();
        RebuildBody();
    }

    void AnimateTongue()
    {
        if (_tongue == null) return;
        float c = Mathf.Repeat(Time.time, 1.9f);                       // flick every ~1.9s
        float k = c < 0.4f ? Mathf.Sin(c / 0.4f * Mathf.PI) : 0f;
        _tongue.localScale = new Vector3(1f, 1f, 0.3f + 0.7f * k);
        _tongue.localPosition = _tongueBase + new Vector3(0f, 0f, 0.14f * k);
    }

    // ---------------------------------------------------------------- body tube

    void RebuildBody()
    {
        if (_bodyMesh == null) return;

        var segs = _snake.segments;
        _spine.Clear();
        // neck anchor tucked just behind the head so the tube emerges from it
        _spine.Add(_snake.transform.position - _snake.transform.forward * 0.45f);
        for (int i = 0; i < segs.Count; i++)
            if (segs[i] != null) _spine.Add(segs[i].transform.position);

        if (_spine.Count < 2) { _bodyMesh.Clear(); return; }
        if (_spine.Count > MaxSpine) _spine.RemoveRange(MaxSpine, _spine.Count - MaxSpine);

        // ---- smoothed ring centers: every point + a catmull-rom midpoint between each
        int n = _spine.Count;
        _ringC.Clear();
        for (int i = 0; i < n; i++)
        {
            _ringC.Add(_spine[i]);
            if (i < n - 1)
                _ringC.Add(Cr(Sp(i - 1), _spine[i], _spine[i + 1], Sp(i + 2), 0.5f));
        }

        int M = _ringC.Count;
        if (M < 2) { _bodyMesh.Clear(); return; }

        // total arc length (needed for the neck ramp / tail taper)
        float L = 0f;
        for (int i = 1; i < M; i++) L += Vector3.Distance(_ringC[i], _ringC[i - 1]);
        float tailZone = Mathf.Min(6f, Mathf.Max(1.5f, L * 0.3f));

        _verts.Clear(); _norms.Clear(); _uvs.Clear(); _tris.Clear();

        float arc = 0f;
        Vector3 lastN = Vector3.up, lastB = Vector3.left, lastT = Vector3.forward;
        Vector3 lastC = Vector3.zero;
        float lastR = 0.36f;

        for (int i = 0; i < M; i++)
        {
            if (i > 0) arc += Vector3.Distance(_ringC[i], _ringC[i - 1]);

            Vector3 tan;
            if (i < M - 1) tan = _ringC[i + 1] - _ringC[i];
            else tan = _ringC[i] - _ringC[i - 1];
            if (tan.sqrMagnitude < 1e-8f) tan = Vector3.forward;
            tan.Normalize();

            Vector3 N = Vector3.up - tan * Vector3.Dot(Vector3.up, tan);
            if (N.sqrMagnitude < 1e-6f) N = Vector3.right - tan * Vector3.Dot(Vector3.right, tan);
            N.Normalize();
            Vector3 B = Vector3.Cross(tan, N);

            float r = RadiusAt(arc, L, tailZone);
            float v = arc / BodyRepeat;

            // Gentle serpentine: a lateral sine travelling head -> tail along the
            // spine. Envelope ramps 0 -> 1 over the first 1.2u so the tube emerges
            // from the (straight, logic-driven) head without a kink.
            float env = Smooth(0f, 1.2f, arc);
            Vector3 center = _ringC[i]
                + B * (WaveAmp * Mathf.Sin(arc * WaveFreq - _slitherPhase) * env);

            for (int k = 0; k < RingVerts; k++)
            {
                float th = (k / (float)(RingVerts - 1)) * Mathf.PI * 2f;   // 0 = dorsal top
                Vector3 radial = N * Mathf.Cos(th) + B * Mathf.Sin(th);
                _verts.Add(center + radial * r);
                _norms.Add(radial);
                _uvs.Add(new Vector2(k / (float)(RingVerts - 1), v));
            }
            lastN = N; lastB = B; lastT = tan; lastR = r; lastC = center;
        }

        // quad strip between rings
        for (int i = 0; i < M - 1; i++)
        {
            int row = i * RingVerts, next = (i + 1) * RingVerts;
            for (int k = 0; k < RingVerts - 1; k++)
            {
                int a = row + k, b = row + k + 1, c = next + k, d = next + k + 1;
                _tris.Add(a); _tris.Add(b); _tris.Add(c);
                _tris.Add(b); _tris.Add(d); _tris.Add(c);
            }
        }

        // rounded tail tip
        int lastRow = (M - 1) * RingVerts;
        int tipIdx = _verts.Count;
        _verts.Add(lastC + lastT * (lastR * 0.9f + 0.14f));
        _norms.Add(lastT);
        _uvs.Add(new Vector2(0.5f, _uvs[lastRow].y + 0.05f));
        for (int k = 0; k < RingVerts - 1; k++)
        {
            _tris.Add(lastRow + k);
            _tris.Add(lastRow + k + 1);
            _tris.Add(tipIdx);
        }

        _bodyMesh.Clear();
        _bodyMesh.SetVertices(_verts);
        _bodyMesh.SetNormals(_norms);
        _bodyMesh.SetUVs(0, _uvs);
        _bodyMesh.SetTriangles(_tris, 0);
        _bodyMesh.RecalculateBounds();
    }

    Vector3 Sp(int i) => _spine[Mathf.Clamp(i, 0, _spine.Count - 1)];

    static Vector3 Cr(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t
                     + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                     + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    static float RadiusAt(float arc, float L, float tailZone)
    {
        if (arc < 1.0f) return Mathf.Lerp(0.26f, 0.36f, arc / 1.0f);      // slim neck under the head
        if (arc > L - tailZone)
        {
            float t = Mathf.Clamp01((arc - (L - tailZone)) / tailZone);
            return Mathf.Lerp(0.36f, 0.045f, t * t * (3f - 2f * t));      // tapering tail
        }
        return 0.36f;
    }

    // ---------------------------------------------------------------- head mesh

    static Mesh HeadMesh => _headMesh != null ? _headMesh : _headMesh = BuildHeadMesh();

    static Mesh BuildHeadMesh()
    {
        // (z, halfWidth, halfHeight), +Z = snout, last section tucks under the body
        var secs = new[]
        {
            new Vector3( 0.58f, 0.070f, 0.050f),
            new Vector3( 0.50f, 0.160f, 0.110f),
            new Vector3( 0.36f, 0.230f, 0.165f),
            new Vector3( 0.18f, 0.290f, 0.210f),
            new Vector3( 0.00f, 0.335f, 0.250f),
            new Vector3(-0.18f, 0.360f, 0.275f),
            new Vector3(-0.34f, 0.350f, 0.275f),
            new Vector3(-0.48f, 0.330f, 0.265f)
        };
        const int VP = 11;

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        for (int s = 0; s < secs.Length; s++)
        {
            var sec = secs[s];
            for (int k = 0; k < VP; k++)
            {
                float phi = (k / (float)(VP - 1)) * Mathf.PI * 2f;   // 0 = dorsal top
                float x = Mathf.Sin(phi) * sec.y;
                float y = Mathf.Cos(phi) * sec.z;
                if (y < 0f) y *= 0.72f;                              // flatter jaw
                verts.Add(new Vector3(x, y, sec.x));
                uvs.Add(new Vector2(k / (float)(VP - 1), (0.58f - sec.x) * 0.55f));
            }
        }
        int tipIdx = verts.Count; verts.Add(new Vector3(0f, 0f, 0.62f)); uvs.Add(new Vector2(0.5f, 0f));
        int neckIdx = verts.Count; verts.Add(new Vector3(0f, 0f, -0.50f)); uvs.Add(new Vector2(0.5f, 0.59f));

        for (int s = 0; s < secs.Length - 1; s++)
        {
            int row = s * VP, next = (s + 1) * VP;
            for (int k = 0; k < VP - 1; k++)
            {
                int a = row + k, b = row + k + 1, c = next + k, d = next + k + 1;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(b); tris.Add(d); tris.Add(c);
            }
        }
        for (int k = 0; k < VP - 1; k++)                     // snout cap
        {
            tris.Add(tipIdx); tris.Add(k + 1); tris.Add(k);
        }
        int lastRow = (secs.Length - 1) * VP;
        for (int k = 0; k < VP - 1; k++)                     // neck cap (hidden inside body)
        {
            tris.Add(neckIdx); tris.Add(lastRow + k); tris.Add(lastRow + k + 1);
        }

        var mesh = new Mesh { name = "SnakeHead" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();

        // winding safeguard: mid-section normals must point away from the head axis
        int agree = 0;
        Vector3 axis = new Vector3(0f, 0f, secs[3].x);
        for (int k = 0; k < VP; k++)
            if (Vector3.Dot(mesh.normals[3 * VP + k], verts[3 * VP + k] - axis) > 0f) agree++;
        if (agree < VP / 2)
        {
            for (int i = 0; i < tris.Count; i += 3)
            {
                int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t;
            }
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
        }
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------------------------------------------------------- tongue

    static Mesh TongueMesh => _tongueMesh != null ? _tongueMesh : _tongueMesh = BuildTongueMesh();

    static Mesh BuildTongueMesh()
    {
        var verts = new List<Vector3>
        {
            new Vector3(-0.018f, 0f, 0f),      // 0 stem
            new Vector3( 0.018f, 0f, 0f),      // 1
            new Vector3( 0.014f, 0f, 0.07f),   // 2
            new Vector3(-0.014f, 0f, 0.07f),   // 3
            new Vector3(-0.016f, 0f, 0.062f),  // 4 left prong
            new Vector3(-0.004f, 0f, 0.066f),
            new Vector3(-0.070f, 0f, 0.210f),  // 6
            new Vector3( 0.004f, 0f, 0.066f),  // 7 right prong
            new Vector3( 0.016f, 0f, 0.062f),
            new Vector3( 0.070f, 0f, 0.210f)   // 9
        };
        var tris = new List<int>
        {
            0, 3, 2,  0, 2, 1,          // stem (faces up)
            4, 6, 5,                    // left fork
            7, 9, 8                     // right fork
        };
        var uvs = new List<Vector2>(new Vector2[verts.Count]);

        var mesh = new Mesh { name = "Tongue" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------------------------------------------------------- skin pattern

    static Texture2D BuildPattern()
    {
        const int S = 256;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, true)
        {
            name = "SnakeSkinPattern",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 4
        };

        var back = new Color(0.32f, 0.20f, 0.11f);    // warm brown
        var gold = new Color(0.86f, 0.68f, 0.20f);     // golden diamonds (kept)
        var outline = new Color(0.09f, 0.055f, 0.03f); // dark brown outlines
        var cream = new Color(0.90f, 0.86f, 0.68f);    // belly
        var speck = new Color(0.72f, 0.66f, 0.42f);    // side halo

        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
        {
            float v = y / (float)S;
            float band = Mathf.Repeat(v * 22f, 1f) < 0.55f ? 1f : 0.87f;   // ventral scute bands
            float cellA = v * 4f;
            float cellB = v * 4f + 0.5f;
            float dvDia = Mathf.Abs(cellA - Mathf.Round(cellA)) * 0.25f;    // v-distance to diamond
            float dvBlot = Mathf.Abs(cellB - Mathf.Round(cellB)) * 0.25f;   // half-period offset

            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S;
                float du = Mathf.Min(u, 1f - u) * 2f;   // 0 = dorsal ridge .. 1 = belly center

                Color c = back;

                // dorsal gold diamond chain with dark outline
                float m = du / 0.30f + dvDia / 0.10f;
                if (m < 1f) c = gold;
                else if (m < 1.28f) c = outline;

                // dark side blotches with a thin golden halo
                float m2 = Mathf.Abs(du - 0.55f) / 0.13f + dvBlot / 0.075f;
                if (m2 < 1f) c = outline;
                else if (m2 < 1.35f) c = Color.Lerp(c, speck, 0.85f);

                // creamy banded belly
                float wb = Smooth(0.62f, 0.86f, du);
                if (wb > 0f) c = Color.Lerp(c, cream * band, wb);

                // organic mottling
                c *= 0.94f + 0.12f * Hash01(x * 7.31f + y * 3.17f);

                px[y * S + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply(true, false);
        return tex;
    }

    static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    static float Hash01(float n) => Mathf.Repeat(Mathf.Sin(n) * 43758.5453f, 1f);
}
