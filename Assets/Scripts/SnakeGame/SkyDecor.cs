using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Runtime sky dressing: light-blue gradient skybox + slowly drifting clouds.
/// Applied from code so the saved scene stays untouched, and the ambient
/// lighting is pinned to the values the old procedural skybox produced, so
/// the garden keeps exactly the same mood.
///
/// The camera looks down over the field, so the sky is only visible as the
/// band past the far fence (roughly 22-37 degrees below the camera's
/// horizontal). Clouds live in that shell: fixed height + fixed radius from
/// the camera, drifting along an arc, so they float through the visible band
/// forever without ever leaving it.
public class SkyDecor : MonoBehaviour
{
    static SkyDecor instance;

    // ambient colors captured from the previous Skybox/Procedural setup
    static readonly Color AmbSky = new Color32(0x36, 0x3A, 0x42, 0xFF);
    static readonly Color AmbEq  = new Color32(0x1D, 0x20, 0x22, 0xFF);
    static readonly Color AmbGnd = new Color32(0x0C, 0x0B, 0x09, 0xFF);

    const int CloudCount = 16;
    const float FadeIn  = 0.78f;   // |azimuth offset| where clouds start fading
    const float FadeOut = 0.98f;   // fully invisible past this (frame edge is ~0.81)
    const float WrapAt  = 1.35f;   // drift past this -> teleport to the other side (invisible there)

    Camera cam;
    bool spawned;

    struct Cloud
    {
        public Transform t;
        public Material mat;
        public float theta;    // azimuth offset from the camera facing (radians)
        public float radius;   // horizontal distance from the camera
        public float y;        // world height
        public float omega;    // drift speed (rad/s)
    }

    readonly List<Cloud> clouds = new List<Cloud>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("SkyDecor");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SkyDecor>();
    }

    void Awake()
    {
        // ---- skybox (material asset preferred: shaders referenced by assets
        //      are guaranteed to be in a build; Shader.Find is the fallback)
        var sky = Resources.Load<Material>("Sky/SkyGradient");
        if (sky == null)
        {
            var sh = Shader.Find("SnakeGame/SkyGradient");
            if (sh != null) sky = new Material(sh);
        }
        if (sky != null) RenderSettings.skybox = sky;

        // ---- pin the previous ambient lighting so the scene mood is unchanged
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = AmbSky;
        RenderSettings.ambientEquatorColor = AmbEq;
        RenderSettings.ambientGroundColor = AmbGnd;
        RenderSettings.ambientIntensity = 1f;
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        if (!spawned) { SpawnClouds(); spawned = true; }

        float dt = Time.deltaTime;
        Vector3 cp = cam.transform.position;
        float camAz = Mathf.Atan2(cam.transform.forward.x, cam.transform.forward.z);

        for (int i = 0; i < clouds.Count; i++)
        {
            var c = clouds[i];
            c.theta += c.omega * dt;

            // wrap while invisible: drift off one edge, reappear on the other
            if (c.theta > WrapAt) c.theta -= WrapAt * 2f;
            else if (c.theta < -WrapAt) c.theta += WrapAt * 2f;

            float az = camAz + c.theta;
            Vector3 pos = new Vector3(
                cp.x + Mathf.Sin(az) * c.radius,
                c.y,
                cp.z + Mathf.Cos(az) * c.radius);
            c.t.position = pos;
            c.t.rotation = Quaternion.LookRotation(pos - cp, Vector3.up);

            // fade with distance from the centre of the view: no pop at the edges
            float edge = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(FadeOut, FadeIn, Mathf.Abs(c.theta)));
            var col = c.mat.color;
            col.a = edge;
            c.mat.color = col;

            clouds[i] = c;
        }
    }

    void SpawnClouds()
    {
        var cs = Shader.Find("SnakeGame/SoftCloud");
        if (cs == null) return;

        var texes = new[] { BuildCloudTex(11), BuildCloudTex(47), BuildCloudTex(83) };
        Vector3 cp = cam.transform.position;

        var rnd = new System.Random(20260930);
        float Next(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        for (int i = 0; i < CloudCount; i++)
        {
            float y = Next(1.5f, 6.5f);
            // elevation window: high enough to clear the fence line (~37 deg
            // down), low enough to stay inside the top of frame (~22 deg down)
            float rMax = (cp.y - y) / Mathf.Tan(22.5f * Mathf.Deg2Rad);
            float rMin = Mathf.Max(34f, (cp.y - y) / Mathf.Tan(37f * Mathf.Deg2Rad));
            if (rMax - rMin < 3f) { rMin = Mathf.Max(20f, rMax - 3f); }
            if (rMax <= rMin + 1f) continue;
            float radius = Next(rMin, rMax);

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Cloud" + i;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            float width = Next(10f, 24f);
            go.transform.localScale = new Vector3(width, width * Next(0.34f, 0.46f), 1f);

            var mat = new Material(cs) { mainTexture = texes[i % texes.Length] };
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            clouds.Add(new Cloud
            {
                t = go.transform,
                mat = mat,
                theta = Next(-1.05f, 1.05f),
                radius = radius,
                y = y,
                omega = Next(0.010f, 0.020f),
            });
        }
    }

    /// One soft puff: a cluster of falloff blobs, white on top with a light
    /// blue-grey underside, transparent everywhere else.
    static Texture2D BuildCloudTex(int seed)
    {
        const int W = 384, H = 192;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, true)
        {
            name = "Cloud" + seed,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        var rnd = new System.Random(seed);
        float Next(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        int blobs = 10 + rnd.Next(5);
        var bx = new float[blobs];
        var by = new float[blobs];
        var br = new float[blobs];
        for (int i = 0; i < blobs; i++)
        {
            bx[i] = Next(0.16f, 0.84f);
            by[i] = Next(0.42f, 0.74f);           // mass sits high -> flat bottom
            br[i] = Next(0.09f, 0.22f);
        }

        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = y / (float)H;
            // underside shading: soft blue-grey low, bright white up top
            Color body = Color.Lerp(new Color(0.79f, 0.84f, 0.91f), Color.white,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((v - 0.28f) / 0.42f)));

            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W;
                float a = 0f;
                for (int i = 0; i < blobs; i++)
                {
                    float dx = (u - bx[i]) / br[i];
                    float dy = (v - by[i]) / (br[i] * 0.78f);
                    float d2 = dx * dx + dy * dy;
                    if (d2 < 1f)
                    {
                        float f = 1f - d2;
                        a += f * f;               // smooth, dense-centred falloff
                    }
                }
                a = Mathf.Clamp01(a * 1.5f);
                px[y * W + x] = new Color(body.r, body.g, body.b, a);
            }
        }

        tex.SetPixels(px);
        tex.Apply(true, false);
        return tex;
    }
}
