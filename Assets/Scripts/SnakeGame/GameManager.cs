using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    /// Playfield half-size in cells: the board runs from -Half..Half on both axes (29 x 29).
    public const int Half = 14;

    public int appleCount = 3;
    public int maxDynamicObstacles = 25;
    public int score { get; private set; }
    public int eaten { get; private set; }
    public bool gameOver { get; private set; }
    public string deathReason = "";

    // --- powerup state ---
    public const float RockDuration = 10f;
    public const float SpeedDuration = 6f;
    public const float BigAppleDuration = 10f;
    public const int BigAppleMultiplier = 3;
    public float rockTime { get; private set; }
    public float speedTime { get; private set; }
    public float bigAppleTime { get; private set; }
    public bool HasRockHead => rockTime > 0f;

    public Snake snake { get; private set; }

    Camera cam;
    Material headMat;
    Color headBaseColor;
    static readonly Color headRockColor = new Color(0.72f, 0.76f, 0.82f);
    static readonly Color segBaseEmission = new Color(0.08f, 0.045f, 0.018f);
    static readonly Color headBaseEmission = new Color(0.12f, 0.06f, 0.02f);
    static readonly Color flashEmission = new Color(0.9f, 0.9f, 0.9f);

    readonly HashSet<Vector2Int> obstacleCells = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> staticObstacleCells = new HashSet<Vector2Int>();
    readonly List<Apple> apples = new List<Apple>();
    readonly List<GameObject> dynamicObstacles = new List<GameObject>();
    readonly List<Powerup> powerups = new List<Powerup>();

    // Object pools — no Instantiate/Destroy spikes during play.
    readonly Stack<GameObject> applePool = new Stack<GameObject>();
    readonly Stack<GameObject> obstaclePool = new Stack<GameObject>();
    readonly Dictionary<PowerType, Stack<GameObject>> powerPool =
        new Dictionary<PowerType, Stack<GameObject>>
        {
            { PowerType.RockHead, new Stack<GameObject>() },
            { PowerType.SpeedBoost, new Stack<GameObject>() },
            { PowerType.BigApple, new Stack<GameObject>() },
        };

    struct DebrisFx { public GameObject go; public Vector3 vel; public float life; }
    readonly List<DebrisFx> debris = new List<DebrisFx>();
    readonly Stack<GameObject> debrisPool = new Stack<GameObject>();

    float nextObstacleTime;
    float warnTime;
    float nextPowerTime;
    float nextSlideTime;

    // roaming stones — difficulty phase that starts once every stone has spawned
    struct StoneSlide { public GameObject go; public Vector2Int from, to; public float t, dur; }
    readonly List<StoneSlide> stoneSlides = new List<StoneSlide>();
    const float StoneSlideDur = 0.32f;

    bool rockVisualOn;
    bool blinkOn;
    float blinkTimer;

    float speedFxTime;                 // trails the boost so the speed lines fade out

    string scoreStr = "SCORE  0";
    GUIStyle hud, big, mid, warnStyle, fx, dbg;

    // rolling frame stats (F1 overlay)
    bool showDebug;
    float fpsAcc;
    int fpsFrames;
    float fpsAvg;
    float worstMs;
    float worstShown;

    void Awake()
    {
        Instance = this;

        snake = FindFirstObjectByType<Snake>();
        cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();

        foreach (var o in FindObjectsByType<Obstacle>(FindObjectsSortMode.None))
            staticObstacleCells.Add(WorldToCell(o.transform.position));
        obstacleCells.UnionWith(staticObstacleCells);

        StyleLevel();
        StyleCamera();
        StyleSun();
        EnsureGrass();
        EnsureFence();
    }

    void EnsureFence()
    {
        if (GameObject.Find("/GardenFence") != null) return;
        var go = new GameObject("GardenFence");
        go.AddComponent<FenceBuilder>();
    }

    void StyleSun()
    {
        var dl = GameObject.Find("/Directional Light");
        if (dl == null) return;
        var l = dl.GetComponent<Light>();
        if (l == null) return;
        l.color = new Color(1f, 0.94f, 0.82f);
        l.intensity = 1.15f;
    }

    void EnsureGrass()
    {
        if (GrassField.Instance != null) return;
        var go = new GameObject("GrassField");
        go.AddComponent<GrassField>();
    }

#if UNITY_EDITOR
    static Texture2D LoadTex(string path) =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
#else
    static Texture2D LoadTex(string path) => null;
#endif

    void Start()
    {
        if (!MainMenu.Open) StartGame();   // normally the main menu launches round 1
    }

    void StyleLevel()
    {
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            string n = r.gameObject.name;
            Material m = r.material;

            if (n == "Floor")
            {
                m.color = new Color(0.44f, 0.66f, 0.38f);
                var grassTex = LoadTex("Assets/Textures/leafy_grass_diff_1k.jpg");
                if (grassTex != null)
                {
                    m.mainTexture = grassTex;
                    m.mainTextureScale = new Vector2(7f, 7f);
                }
            }
            else if (n.StartsWith("Wall"))
            {
                m.color = new Color(0.18f, 0.22f, 0.34f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.03f, 0.04f, 0.09f));
            }
            else if (n == "SnakeHead")
            {
                headBaseColor = Color.white;              // pattern texture carries the colors
                m.color = headBaseColor;
                m.mainTexture = SnakeSkin.Pattern;
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", headBaseEmission);
                headMat = m;
            }
        }
    }

    void StyleCamera()
    {
        if (cam == null) return;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.36f, 0.54f, 0.74f);   // soft garden sky
    }

    public static Vector2Int WorldToCell(Vector3 p) =>
        new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));

    // ---------------------------------------------------------------- round

    public void StartGame()
    {
        score = 0;
        eaten = 0;
        gameOver = false;
        deathReason = "";
        scoreStr = "SCORE  0";

        rockTime = speedTime = bigAppleTime = 0f;
        speedFxTime = 0f;
        ApplyRockVisual(false);
        ApplyBlink(false);
        blinkOn = false;
        blinkTimer = 0f;
        if (snake != null) snake.SetSpeedFactor(1f);

        foreach (var a in apples)
            if (a != null) { a.gameObject.SetActive(false); applePool.Push(a.gameObject); }
        apples.Clear();

        foreach (var o in dynamicObstacles)
            if (o != null) { o.SetActive(false); obstaclePool.Push(o); }
        dynamicObstacles.Clear();
        stoneSlides.Clear();
        nextSlideTime = 0f;

        foreach (var p in powerups)
            if (p != null) RecyclePowerup(p);
        powerups.Clear();

        foreach (var d in debris)
            if (d.go != null) { d.go.SetActive(false); debrisPool.Push(d.go); }
        debris.Clear();

        obstacleCells.Clear();
        obstacleCells.UnionWith(staticObstacleCells);
        RestoreStaticObstacles();

        if (snake != null) snake.ResetSnake();

        for (int i = 0; i < appleCount; i++)
            SpawnApple();

        ScheduleNextObstacle(6f);
        ScheduleNextPowerup(Random.Range(10f, 16f));
    }

    // ------------------------------------------------------------ pooling

    GameObject RentObstacle()
    {
        GameObject go = null;
        while (obstaclePool.Count > 0 && go == null) go = obstaclePool.Pop();
        if (go == null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ObstacleDynamic";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.AddComponent<Obstacle>();   // styles itself orange in Start
        }
        go.SetActive(true);
        return go;
    }

    Apple RentApple()
    {
        GameObject go = null;
        while (applePool.Count > 0 && go == null) go = applePool.Pop();
        if (go == null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Apple";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.AddComponent<Apple>();
        }
        go.SetActive(true);
        return go.GetComponent<Apple>();
    }

    void RecyclePowerup(Powerup p)
    {
        p.gameObject.SetActive(false);
        powerPool[p.type].Push(p.gameObject);
    }

    void RestoreStaticObstacles()
    {
        if (staticObstacleCells.Count == 0) return;
        var parentGo = GameObject.Find("/AllObstacle");

        foreach (var c in staticObstacleCells)
        {
            bool present = false;
            foreach (var o in FindObjectsByType<Obstacle>(FindObjectsSortMode.None))
                if (WorldToCell(o.transform.position) == c) { present = true; break; }
            if (present) continue;

            var go = RentObstacle();
            go.name = "ObstacleStatic";
            go.transform.SetParent(parentGo != null ? parentGo.transform : null, true);
            go.transform.position = new Vector3(c.x, 0.5f, c.y);
            go.transform.localScale = Vector3.one;
        }
    }

    // ------------------------------------------------------- obstacles

    void ScheduleNextObstacle(float delay)
    {
        nextObstacleTime = Time.time + delay;
        warnTime = nextObstacleTime - 0.55f;   // short fuse: alert stays snappy before the drop
    }

    float NextObstacleDelay()
    {
        // Random 5-9s, tightening as the snake scores.
        return Mathf.Max(3.5f, Random.Range(5f, 9f) - eaten * 0.15f);
    }

    void SpawnDynamicObstacle()
    {
        if (dynamicObstacles.Count >= maxDynamicObstacles) return;

        Vector2Int c = Vector2Int.zero;
        bool found = false;
        for (int g = 0; g < 800; g++)
        {
            c = new Vector2Int(Random.Range(-Half, Half + 1), Random.Range(-Half, Half + 1));
            if (obstacleCells.Contains(c)) continue;
            if (IsOnSnake(c)) continue;
            if (IsApple(c)) continue;
            if (IsPowerup(c)) continue;
            // At least 4 cells from the head so it is never an unfair instant death.
            if (snake != null && snake.trail.Count > 0 &&
                (c - snake.trail[0]).sqrMagnitude < 16) continue;
            found = true;
            break;
        }
        if (!found) return;

        var go = RentObstacle();
        go.name = "ObstacleDynamic";
        go.transform.position = new Vector3(c.x, 0.5f, c.y);
        go.transform.localScale = Vector3.one * 0.05f;

        obstacleCells.Add(c);
        dynamicObstacles.Add(go);
    }

    // --- roaming stones ---------------------------------------------------
    // Once the field is full, a few random stones quickly dart to new cells.
    void TryStartStoneSlide()
    {
        if (dynamicObstacles.Count == 0) return;

        for (int attempt = 0; attempt < 24; attempt++)
        {
            var go = dynamicObstacles[Random.Range(0, dynamicObstacles.Count)];
            if (go == null || !go.activeSelf || go.transform.localScale.x < 0.99f) continue; // still rising
            bool busy = false;
            for (int i = 0; i < stoneSlides.Count; i++)
                if (stoneSlides[i].go == go) { busy = true; break; }
            if (busy) continue;

            Vector2Int from = WorldToCell(go.transform.position);
            Vector2Int to = from + RandomDir4() * Random.Range(2, 5);   // dart 2–4 cells
            if (!SlidePathOK(from, to)) continue;

            obstacleCells.Remove(from);
            obstacleCells.Add(to);
            stoneSlides.Add(new StoneSlide { go = go, from = from, to = to, t = 0f, dur = StoneSlideDur });
            return;
        }
    }

    bool SlidePathOK(Vector2Int from, Vector2Int to)
    {
        int len = Mathf.Abs(to.x - from.x) + Mathf.Abs(to.y - from.y);
        if (len == 0) return false;
        var step = new Vector2Int((to.x - from.x) / len, (to.y - from.y) / len);
        Vector2Int head = (snake != null && snake.trail.Count > 0) ? snake.trail[0] : Vector2Int.zero;

        for (int k = 1; k <= len; k++)
        {
            Vector2Int c = from + step * k;
            if (Mathf.Abs(c.x) > Half || Mathf.Abs(c.y) > Half) return false;  // stays on the board
            if (obstacleCells.Contains(c)) return false;                        // no stone-on-stone overlap
            if (IsOnSnake(c)) return false;                                     // never sweeps through the snake
            if (IsApple(c) || IsPowerup(c)) return false;                       // don't swallow pickups
            if ((c - head).sqrMagnitude < 16) return false;                     // same 4-cell fairness as spawning
        }
        return true;
    }

    static Vector2Int RandomDir4()
    {
        switch (Random.Range(0, 4))
        {
            case 0: return Vector2Int.up;
            case 1: return Vector2Int.down;
            case 2: return Vector2Int.left;
            default: return Vector2Int.right;
        }
    }

    public bool IsObstacle(Vector2Int c) => obstacleCells.Contains(c);

    /// Rock Head: destroy the obstacle (+5 score, debris burst), snake recoils.
    public void SmashObstacle(Vector2Int c)
    {
        if (!obstacleCells.Remove(c)) return;
        AddScore(5);
        SoundManager.Play(SoundManager.Sfx.CrackStone);

        Vector3 pos = new Vector3(c.x, 0.5f, c.y);

        // a stone caught mid-slide: match on its destination (logical) cell
        for (int i = stoneSlides.Count - 1; i >= 0; i--)
        {
            if (stoneSlides[i].to != c) continue;
            var sg = stoneSlides[i].go;
            stoneSlides.RemoveAt(i);
            if (sg != null) { sg.SetActive(false); obstaclePool.Push(sg); }
            dynamicObstacles.Remove(sg);
            SpawnDebris(pos);
            return;
        }

        for (int i = dynamicObstacles.Count - 1; i >= 0; i--)
        {
            var o = dynamicObstacles[i];
            if (o != null && WorldToCell(o.transform.position) == c)
            {
                o.SetActive(false);
                obstaclePool.Push(o);
                dynamicObstacles.RemoveAt(i);
                SpawnDebris(pos);
                return;
            }
        }

        foreach (var o in FindObjectsByType<Obstacle>(FindObjectsSortMode.None))
        {
            if (WorldToCell(o.transform.position) != c) continue;
            o.gameObject.SetActive(false);     // pooled; restored on restart
            obstaclePool.Push(o.gameObject);
            SpawnDebris(pos);
            return;
        }
    }

    void SpawnDebris(Vector3 pos)
    {
        for (int i = 0; i < 10; i++)
        {
            GameObject go = null;
            while (debrisPool.Count > 0 && go == null) go = debrisPool.Pop();
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Debris";
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            go.SetActive(true);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * Random.Range(0.08f, 0.18f);
            go.GetComponent<Renderer>().material.color = new Color(0.52f, 0.52f, 0.50f);   // stone-gray chips

            debris.Add(new DebrisFx
            {
                go = go,
                vel = new Vector3(Random.Range(-3f, 3f), Random.Range(3f, 6f), Random.Range(-3f, 3f)),
                life = 0.7f
            });
        }
    }

    // ---------------------------------------------------------- powerups

    void ScheduleNextPowerup(float delay) => nextPowerTime = Time.time + delay;

    void SpawnPowerup()
    {
        var type = (PowerType)Random.Range(0, 3);

        Vector2Int c = Vector2Int.zero;
        bool found = false;
        for (int g = 0; g < 600; g++)
        {
            c = new Vector2Int(Random.Range(-Half, Half + 1), Random.Range(-Half, Half + 1));
            if (obstacleCells.Contains(c)) continue;
            if (IsOnSnake(c)) continue;
            if (IsApple(c)) continue;
            if (IsPowerup(c)) continue;
            if (snake != null && snake.trail.Count > 0 &&
                (c - snake.trail[0]).sqrMagnitude < 9) continue;
            found = true;
            break;
        }
        if (!found) return;

        GameObject go = null;
        while (powerPool[type].Count > 0 && go == null) go = powerPool[type].Pop();
        if (go == null)
        {
            PrimitiveType prim = type == PowerType.RockHead ? PrimitiveType.Cube
                               : type == PowerType.SpeedBoost ? PrimitiveType.Capsule
                               : PrimitiveType.Sphere;
            go = GameObject.CreatePrimitive(prim);
            go.name = "Powerup_" + type;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var fresh = go.AddComponent<Powerup>();
            fresh.type = type;                    // set before Start styles it
        }

        go.transform.SetParent(null, false);
        go.transform.localScale = type == PowerType.RockHead ? Vector3.one
                                : type == PowerType.SpeedBoost ? new Vector3(0.85f, 0.7f, 0.85f)
                                : Vector3.one * 1.25f;
        go.SetActive(true);

        var p = go.GetComponent<Powerup>();
        p.type = type;
        p.Place(c, 10f);
        powerups.Add(p);
    }

    public void TryPowerup(Vector2Int c)
    {
        for (int i = powerups.Count - 1; i >= 0; i--)
        {
            var p = powerups[i];
            if (p == null || p.cell != c) continue;

            switch (p.type)
            {
                case PowerType.RockHead:
                    rockTime = RockDuration;
                    ApplyRockVisual(true);
                    break;
                case PowerType.SpeedBoost:
                    speedTime = SpeedDuration;
                    speedFxTime = 0.8f;
                    if (snake != null) snake.SetSpeedFactor(1.6f);
                    break;
                case PowerType.BigApple:
                    bigAppleTime = BigAppleDuration;
                    blinkOn = false;
                    blinkTimer = 0f;
                    break;
            }

            SoundManager.Play(p.type == PowerType.RockHead
                ? SoundManager.Sfx.HeadPowerUp : SoundManager.Sfx.OtherPowerUp);
            RecyclePowerup(p);
            powerups.RemoveAt(i);
        }
    }

    void ApplyRockVisual(bool on)
    {
        rockVisualOn = on;
        if (headMat == null) return;
        headMat.color = on ? headRockColor : headBaseColor;
    }

    void ApplyBlink(bool on)
    {
        if (Snake.SegMat != null)
            Snake.SegMat.SetColor("_EmissionColor", on ? flashEmission : segBaseEmission);
        if (headMat != null)
            headMat.SetColor("_EmissionColor", on ? flashEmission : headBaseEmission);
    }

    bool IsPowerup(Vector2Int c)
    {
        foreach (var p in powerups)
            if (p != null && p.cell == c) return true;
        return false;
    }

    // ---------------------------------------------------------- apples

    public bool TryEat(Vector2Int c)
    {
        for (int i = apples.Count - 1; i >= 0; i--)
        {
            var a = apples[i];
            if (a == null) { apples.RemoveAt(i); continue; }
            if (a.cell != c) continue;

            a.gameObject.SetActive(false);
            applePool.Push(a.gameObject);
            apples.RemoveAt(i);

            int gain = 10;
            if (bigAppleTime > 0f) gain *= BigAppleMultiplier;
            AddScore(gain);
            eaten += 1;
            SoundManager.Play(SoundManager.Sfx.AppleBite);
            SpawnApple();
            return true;
        }
        return false;
    }

    void AddScore(int n)
    {
        score += n;
        scoreStr = "SCORE  " + score;
    }

    bool IsOnSnake(Vector2Int c)
    {
        if (snake == null) return false;
        var t = snake.trail;
        int max = Mathf.Min(snake.segments.Count, t.Count - 1);
        for (int i = 0; i <= max; i++)
            if (t[i] == c) return true;
        return false;
    }

    bool IsApple(Vector2Int c)
    {
        foreach (var a in apples)
            if (a != null && a.cell == c) return true;
        return false;
    }

    bool CellBlocked(Vector2Int c) =>
        obstacleCells.Contains(c) || IsOnSnake(c) || IsApple(c) || IsPowerup(c);

    void SpawnApple()
    {
        Vector2Int c = Vector2Int.zero;
        for (int g = 0; g < 600; g++)
        {
            c = new Vector2Int(Random.Range(-Half, Half + 1), Random.Range(-Half, Half + 1));
            if (CellBlocked(c)) continue;
            if (snake != null && snake.trail.Count > 0 &&
                (c - snake.trail[0]).sqrMagnitude < 9) continue; // keep some distance from the head
            break;
        }

        var ap = RentApple();
        ap.Place(c);
        apples.Add(ap);
    }

    // ------------------------------------------------------------ loop

    public void OnDied(string reason)
    {
        if (gameOver) return;
        gameOver = true;
        deathReason = reason;
        SoundManager.Play(SoundManager.Sfx.GameOver);
    }

    void Update()
    {
        if (MainMenu.Open) return;         // frozen while the main menu is up

        // rolling frame stats + F1 overlay toggle
        float udt = Time.unscaledDeltaTime;
        fpsAcc += udt;
        fpsFrames++;
        if (udt * 1000f > worstMs) worstMs = udt * 1000f;
        if (fpsAcc >= 1f)
        {
            fpsAvg = fpsFrames / fpsAcc;
            worstShown = worstMs;
            fpsAcc = 0f;
            fpsFrames = 0;
            worstMs = 0f;
        }
        if (Input.GetKeyDown(KeyCode.F1)) showDebug = !showDebug;

        if (gameOver)
        {
            if (Input.GetKeyDown(KeyCode.R)) StartGame();
            return;
        }

        float dt = Time.deltaTime;

        // debris physics + recycle
        for (int i = debris.Count - 1; i >= 0; i--)
        {
            var d = debris[i];
            if (d.go == null) { debris.RemoveAt(i); continue; }
            d.vel.y -= 14f * dt;
            d.go.transform.position += d.vel * dt;
            d.go.transform.Rotate(200f * dt, 150f * dt, 0f);
            d.life -= dt;
            if (d.life <= 0f)
            {
                d.go.SetActive(false);
                debrisPool.Push(d.go);
                debris.RemoveAt(i);
            }
            else debris[i] = d;
        }

        // dynamic obstacle rise-in (grows while surfacing from below the grass)
        for (int i = dynamicObstacles.Count - 1; i >= 0; i--)
        {
            var o = dynamicObstacles[i];
            if (o == null) { dynamicObstacles.RemoveAt(i); continue; }
            float s = o.transform.localScale.x;
            if (s < 1f)
            {
                s = Mathf.MoveTowards(s, 1f, dt * 3f);
                o.transform.localScale = Vector3.one * s;
                var p = o.transform.position;
                p.y = Mathf.Lerp(-0.55f, 0.5f, (s - 0.05f) / 0.95f);
                o.transform.position = p;
            }
        }

        // roaming stones: eased dart between cells (+ a small hop so it reads clearly)
        for (int i = stoneSlides.Count - 1; i >= 0; i--)
        {
            var s = stoneSlides[i];
            if (s.go == null || !s.go.activeSelf) { stoneSlides.RemoveAt(i); continue; }
            s.t += dt;
            float u = Mathf.Clamp01(s.t / s.dur);
            float e = u * u * (3f - 2f * u);   // smoothstep
            Vector3 p = Vector3.Lerp(new Vector3(s.from.x, 0.5f, s.from.y),
                                     new Vector3(s.to.x, 0.5f, s.to.y), e);
            p.y = 0.5f + Mathf.Sin(u * Mathf.PI) * 0.16f;
            s.go.transform.position = p;
            if (u >= 1f)
            {
                s.go.transform.position = new Vector3(s.to.x, 0.5f, s.to.y);
                stoneSlides.RemoveAt(i);
            }
            else stoneSlides[i] = s;
        }

        // powerup expiry
        for (int i = powerups.Count - 1; i >= 0; i--)
        {
            var p = powerups[i];
            if (p == null || Time.time >= p.dieTime)
            {
                if (p != null) RecyclePowerup(p);
                powerups.RemoveAt(i);
            }
        }

        // powerup timers
        if (rockTime > 0f)
        {
            rockTime = Mathf.Max(0f, rockTime - dt);
            if (rockTime <= 0f && rockVisualOn) ApplyRockVisual(false);
        }

        if (speedTime > 0f)
        {
            speedTime = Mathf.Max(0f, speedTime - dt);
            speedFxTime = 0.8f;
            if (speedTime <= 0f && snake != null) snake.SetSpeedFactor(1f);
        }
        else speedFxTime = Mathf.Max(0f, speedFxTime - dt);

        if (bigAppleTime > 0f)
        {
            bigAppleTime = Mathf.Max(0f, bigAppleTime - dt);
            blinkTimer -= dt;
            if (blinkTimer <= 0f)
            {
                blinkTimer = 0.12f;
                blinkOn = !blinkOn;
                ApplyBlink(blinkOn);
            }
            if (bigAppleTime <= 0f) ApplyBlink(false);
        }

        // speed-boost camera FOV kick
        if (cam != null)
        {
            float target = speedTime > 0f ? 67f : 60f;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target, dt * 6f);
        }

        if (dynamicObstacles.Count >= maxDynamicObstacles)
        {
            // Every stone has appeared: silence the incoming-stone alert for good
            // and open the roaming phase (random stones dart to new cells).
            if (!float.IsPositiveInfinity(nextObstacleTime))
            {
                nextObstacleTime = float.PositiveInfinity;
                warnTime = float.PositiveInfinity;
                nextSlideTime = Time.time + Random.Range(2f, 3.5f);
            }
        }
        else if (float.IsPositiveInfinity(nextObstacleTime))
        {
            ScheduleNextObstacle(NextObstacleDelay());   // a stone was smashed: keep the field full
        }
        else if (Time.time >= nextObstacleTime)
        {
            SpawnDynamicObstacle();
            ScheduleNextObstacle(NextObstacleDelay());
        }

        // roaming stones: with the field full, 1–3 random stones quickly slide
        // to new cells every few seconds (keeps the difficulty climbing)
        if (dynamicObstacles.Count >= maxDynamicObstacles && Time.time >= nextSlideTime)
        {
            nextSlideTime = Time.time + Random.Range(2.6f, 4.6f);
            int slides = Random.Range(1, 4);
            for (int i = 0; i < slides; i++) TryStartStoneSlide();
        }

        if (Time.time >= nextPowerTime)
        {
            SpawnPowerup();
            ScheduleNextPowerup(Random.Range(10f, 16f));
        }
    }

    // ------------------------------------------------------------ GUI

    void InitStyles()
    {
        hud = new GUIStyle(GUI.skin.label)
        {
            fontSize = 30,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperCenter
        };
        hud.normal.textColor = Color.white;

        big = new GUIStyle(hud) { fontSize = 56 };
        big.normal.textColor = new Color(1f, 0.35f, 0.35f);

        mid = new GUIStyle(hud) { fontSize = 26 };
        mid.normal.textColor = new Color(0.95f, 0.95f, 0.80f);

        warnStyle = new GUIStyle(hud) { fontSize = 24 };
        warnStyle.normal.textColor = new Color(1f, 0.75f, 0.2f);

        fx = new GUIStyle(hud) { fontSize = 22 };

        dbg = new GUIStyle(GUI.skin.label) { fontSize = 16 };
        dbg.normal.textColor = new Color(0.6f, 1f, 0.6f);
    }

    void DrawSpeedLines()
    {
        if (speedFxTime <= 0f) return;

        float fade = speedTime > 0f ? 1f : speedFxTime / 0.8f;
        for (int i = 0; i < 16; i++)
        {
            float s = Mathf.Abs(Mathf.Sin(i * 12.9898f) * 43758.5453f) % 1f;
            float y = s * Mathf.Max(1f, Screen.height - 6f);
            float len = (70f + s * 240f) * (0.55f + 0.45f * Mathf.Sin(Time.time * 9f + i));
            float h = 2f + s * 3f;
            float a = (0.30f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 16f + i * 1.7f))) * fade;

            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.DrawTexture(new Rect(0f, y, len, h), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Screen.width - len, (y + 47f) % Mathf.Max(1f, Screen.height - 6f),
                                     len, h), Texture2D.whiteTexture);
        }
        GUI.color = Color.white;
    }

    void OnGUI()
    {
        if (MainMenu.Open) return;         // no HUD behind the menu
        if (hud == null) InitStyles();

        DrawSpeedLines();

        GUI.Label(new Rect(0, 12, Screen.width, 46), scoreStr, hud);

        float y = 54;
        if (rockTime > 0f)
        {
            fx.normal.textColor = new Color(0.78f, 0.83f, 0.92f);
            GUI.Label(new Rect(0, y, Screen.width, 30),
                "ROCK HEAD  " + rockTime.ToString("0.0") + "s", fx);
            y += 28;
        }
        if (speedTime > 0f)
        {
            fx.normal.textColor = new Color(1f, 0.92f, 0.25f);
            GUI.Label(new Rect(0, y, Screen.width, 30),
                "SPEED BOOST  " + speedTime.ToString("0.0") + "s", fx);
            y += 28;
        }
        if (bigAppleTime > 0f)
        {
            fx.normal.textColor = new Color(1f, 0.6f, 0.2f);
            GUI.Label(new Rect(0, y, Screen.width, 30),
                "SCORE x" + BigAppleMultiplier + "  " + bigAppleTime.ToString("0.0") + "s", fx);
            y += 28;
        }
        fx.normal.textColor = Color.white;

        if (!gameOver && dynamicObstacles.Count < maxDynamicObstacles &&
            Time.time >= warnTime && Time.time < nextObstacleTime)
            GUI.Label(new Rect(0, y, Screen.width, 40), "!!  NEW OBSTACLE INCOMING  !!", warnStyle);

        if (gameOver)
        {
            GUI.Label(new Rect(0, Screen.height / 2 - 90, Screen.width, 70), "GAME OVER", big);
            GUI.Label(new Rect(0, Screen.height / 2 - 10, Screen.width, 44),
                      deathReason + "   -   press R to restart", mid);
        }

        if (showDebug)
            GUI.Label(new Rect(10, Screen.height - 66, 700, 60),
                "FPS " + fpsAvg.ToString("F0") + "   worst " + worstShown.ToString("F0") +
                "ms   segs " + (snake != null ? snake.segments.Count : 0) +
                "   trail " + (snake != null ? snake.trail.Count : 0), dbg);
    }
}
