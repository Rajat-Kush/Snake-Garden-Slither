using System.Collections.Generic;
using UnityEngine;

public class Snake : MonoBehaviour
{
    public float stepInterval = 0.15f;
    public int startLength = 4;
    public Vector2Int startCell = new Vector2Int(-8, 0);
    public Vector2Int startDirection = Vector2Int.right;

    // Index 0 is the newest (head) cell; the rest is history behind it.
    public List<Vector2Int> trail { get; } = new List<Vector2Int>();
    public List<GameObject> segments { get; } = new List<GameObject>();

    /// One shared material for every body segment so batching stays intact at any length.
    public static Material SegMat { get; private set; }

    Vector2Int dir;
    float timer;
    bool moving;
    float speedFactor = 1f;   // powerup speed boost (1 = normal)
    float recoilT;            // >0 while the smash recoil plays
    bool turnBoost;           // post-turn glide speed-up (applied smoothly, never a teleport)
    Vector3 outDirSm;         // eased outgoing heading at trail[0]: the turn
                              // bends the rendered path instantly, with no jump

    // Render smoothing: the spline target can still step sideways in a single
    // frame (a turn pressed in the last frames before a cell commit, when the
    // eased heading had no time to converge). SmoothDamping the RENDERED
    // position spreads any such spike over 3-4 frames (<=~15 deg per frame)
    // while adding only ~0.08 cell of along-path lag - zero lateral error on
    // straights, so line-following precision is unaffected.
    const float RenderSmoothTime = 0.012f;
    Vector3 visHead;
    Vector3 visHeadVel;
    Vector3[] visSeg = new Vector3[0];
    Vector3[] visSegVel = new Vector3[0];

    const float CellY = 0.5f;
    const float RecoilLen = 0.16f;
    const float RecoilAmp = 0.45f;

    static readonly Stack<GameObject> segPool = new Stack<GameObject>();
    static Material eyeMat;
    GameObject eyeL, eyeR;

    float EffInterval => stepInterval / Mathf.Max(0.01f, speedFactor);

    public void StopMoving() { moving = false; }
    public bool IsMoving => moving;   // SnakeSkin freezes the body wave when dead

    public void SetSpeedFactor(float f)
    {
        if (Mathf.Abs(f - speedFactor) < 0.001f) return;
        float oldEff = EffInterval;
        speedFactor = f;
        float newEff = EffInterval;
        if (oldEff > 0.0001f) timer = timer / oldEff * newEff;   // keep glide progress
    }

    /// Punch-back when the snake smashes through an obstacle with Rock Head.
    public void TriggerRecoil() { recoilT = RecoilLen; }

    public void ResetSnake()
    {
        foreach (var s in segments)
        {
            if (s == null) continue;
            s.SetActive(false);
            segPool.Push(s);
        }
        segments.Clear();
        trail.Clear();

        dir = startDirection;
        timer = 0f;
        moving = true;
        speedFactor = 1f;
        recoilT = 0f;
        outDirSm = new Vector3(dir.x, 0f, dir.y);

        Vector2Int back = new Vector2Int(-dir.x, -dir.y);
        Vector2Int c = startCell;
        trail.Add(c);
        for (int i = 0; i < startLength; i++)
        {
            c += back;
            trail.Add(c);
        }

        transform.position = ToWorld(trail[0]);
        transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y));
        transform.localScale = Vector3.one;

        // render-smoothing state starts exactly on the logical pose so the
        // first frame after a (re)start never glides in from a stale position
        visHead = transform.position;
        visHeadVel = Vector3.zero;
        visSeg = new Vector3[0];
        visSegVel = new Vector3[0];

        for (int i = 0; i < startLength; i++)
            CreateSegment(ToWorld(trail[i + 1]));

        CreateEyes();
        SnakeSkin.Attach(this);   // curved head + slithering body tube + flicking tongue
    }

    static Vector3 ToWorld(Vector2Int cell) => new Vector3(cell.x, CellY, cell.y);

    void CreateSegment(Vector3 pos)
    {
        GameObject go = null;
        while (segPool.Count > 0 && go == null) go = segPool.Pop();

        if (go == null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "SnakeSegment";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        if (SegMat == null)
        {
            SegMat = new Material(go.GetComponent<Renderer>().sharedMaterial);
            SegMat.color = Color.white;                 // the pattern texture carries the colors
            SegMat.mainTexture = SnakeSkin.Pattern;
            SegMat.EnableKeyword("_EMISSION");
            SegMat.SetColor("_EmissionColor", new Color(0.08f, 0.045f, 0.018f));
        }

        var sr = go.GetComponent<Renderer>();
        sr.sharedMaterial = SegMat;
        sr.enabled = false;   // spheres stay invisible: SnakeSkin draws the real slithering body
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * 0.8f;
        // Layer 2 (Ignore Raycast): glow lights skip body segments, so a long
        // snake is never re-rendered in every point light's additive pass.
        go.layer = 2;
        go.SetActive(true);
        segments.Add(go);
    }

    void CreateEyes()
    {
        if (eyeL != null) return;
        eyeL = MakeEye();
        eyeR = MakeEye();
        // On the curved head: high and forward-outer, bulging slightly like real snake eyes.
        eyeL.transform.localPosition = new Vector3(-0.27f, 0.11f, 0.08f);
        eyeR.transform.localPosition = new Vector3(0.27f, 0.11f, 0.08f);
        MakePupil(eyeL.transform, -1f);
        MakePupil(eyeR.transform, 1f);
    }

    static Material pupilMat;

    void MakePupil(Transform eye, float side)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        p.name = "Pupil";
        var col = p.GetComponent<Collider>();
        if (col != null) Destroy(col);
        p.transform.SetParent(transform, false);
        p.layer = 2;
        p.transform.localScale = Vector3.one * 0.075f;
        Vector3 dir = new Vector3(0.78f * side, 0.12f, 0.62f).normalized;
        p.transform.localPosition = eye.localPosition + dir * 0.088f;

        if (pupilMat == null)
        {
            pupilMat = new Material(p.GetComponent<Renderer>().sharedMaterial);
            pupilMat.color = new Color(0.03f, 0.03f, 0.04f);
            pupilMat.SetFloat("_Glossiness", 0.85f);
        }
        p.GetComponent<Renderer>().sharedMaterial = pupilMat;
    }

    GameObject MakeEye()
    {
        var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        e.name = "Eye";
        var col = e.GetComponent<Collider>();
        if (col != null) Destroy(col);
        e.transform.SetParent(transform, false);
        e.transform.localScale = Vector3.one * 0.19f;
        e.layer = 2;   // same light-skip layer as the body

        if (eyeMat == null)
        {
            eyeMat = new Material(e.GetComponent<Renderer>().sharedMaterial);
            eyeMat.color = new Color(0.98f, 0.78f, 0.28f);          // glossy amber iris
            eyeMat.SetFloat("_Glossiness", 0.7f);
            eyeMat.EnableKeyword("_EMISSION");
            eyeMat.SetColor("_EmissionColor", new Color(0.35f, 0.22f, 0.05f));
        }
        e.GetComponent<Renderer>().sharedMaterial = eyeMat;
        return e;
    }

    void Update()
    {
        if (moving)
        {
            HandleInput();
            float eff = EffInterval;
            float mul = 1f;
            bool boostCapped = false;
            if (turnBoost)
            {
                // Glide a little faster only until the turn-window (45ms left
                // in the cell) so the LOGICAL turn commits promptly (matters
                // for last-second escapes at the fence). The visible turn itself
                // is no longer done here: ApplyVisuals bends the path through a
                // smooth spline curve the moment the key is pressed, so the
                // boost is kept gentle (2x, still capped per frame) - the old
                // 4x surge was itself a visible "snap" at every turn.
                if (timer >= eff - Mathf.Min(0.045f, eff * 0.3f)) turnBoost = false;
                else { mul = 2f; boostCapped = true; }
            }
            float advance = Time.deltaTime * mul;
            if (boostCapped) advance = Mathf.Max(Time.deltaTime, Mathf.Min(advance, eff * 0.18f));
            timer += advance;
            while (moving && timer >= eff)
            {
                timer -= eff;
                if (!Step())
                {
                    // death (or guard) - the cell never committed, so give the
                    // timer back: the head freezes exactly where it was gliding
                    // instead of snapping backwards a full cell on the death frame
                    timer += eff;
                    break;
                }
                turnBoost = false;   // cell completed: back to normal pace
                eff = EffInterval;   // growth can shrink it mid-loop
            }
        }
        if (recoilT > 0f) recoilT = Mathf.Max(0f, recoilT - Time.deltaTime);
        ApplyVisuals();
    }

    void HandleInput()
    {
        Vector2Int nd = dir;
        bool pressed = true;
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) nd = Vector2Int.up;
        else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) nd = Vector2Int.down;
        else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) nd = Vector2Int.left;
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) nd = Vector2Int.right;
        else pressed = false;

        if (!pressed) return;
        TryTurn(nd);
    }

    /// Validates and applies a requested turn; returns false when the input is
    /// ignored (already heading that way, a 180 reversal, or a second quick
    /// turn that would step back into the cell the head just came from).
    bool TryTurn(Vector2Int nd)
    {
        if (nd.x == -dir.x && nd.y == -dir.y) return false;   // no 180-degree reversals
        if (nd == dir) return false;                          // already heading that way: no turbo

        // Two quick turns inside a single cell can swing the head back across
        // the cell behind it: heading right, press up and then left before the
        // up-cell commits -> the next step lands on trail[1] (the cell the
        // head just left) and the snake "eats its own tail". The per-press
        // reversal guard above only sees the current dir (up), so reject any
        // turn pointing straight at the occupied cell behind the head.
        if (trail.Count >= 2 && trail[0] + nd == trail[1]) return false;

        dir = nd;

        // Briefly run the glide a little faster so the logical turn commits
        // quickly (fence escapes). The VISUAL turn is handled by ApplyVisuals:
        // the spline curve starts bending toward the new direction instantly,
        // so it feels immediate without any speed surge.
        turnBoost = true;
        return true;
    }

    /// Advances the grid logic one cell. Returns false when the step KILLED
    /// the snake (fence / rock / tail) so Update() can keep the glide timer
    /// where it was - otherwise the head would snap backwards a whole cell on
    /// the death frame (the cell never commits, but timer had already stepped).
    bool Step()
    {
        var gm = GameManager.Instance;
        if (gm == null || trail.Count == 0) return false;

        Vector2Int next = trail[0] + dir;
        int limit = GameManager.Half;

        if (Mathf.Abs(next.x) > limit || Mathf.Abs(next.y) > limit)
        {
            gm.OnDied("You hit the fence.");
            StopMoving();
            return false;
        }

        if (gm.IsObstacle(next))
        {
            if (gm.HasRockHead)
            {
                gm.SmashObstacle(next);      // +score, debris burst
                TriggerRecoil();             // punch-back feel
                // fall through: the snake powers into the cleared cell
            }
            else
            {
                gm.OnDied("You crashed into a rock.");
                StopMoving();
                return false;
            }
        }

        for (int i = 1; i <= segments.Count && i < trail.Count; i++)
        {
            if (trail[i] == next)
            {
                gm.OnDied("You ate your own tail.");
                StopMoving();
                return false;
            }
        }

        Vector2Int oldTail = trail[trail.Count - 1];
        bool ate = gm.TryEat(next);
        gm.TryPowerup(next);

        trail.Insert(0, next);

        if (ate)
        {
            CreateSegment(ToWorld(oldTail));
            // speed up as you grow, preserving glide progress
            float oldEff = EffInterval;
            stepInterval = Mathf.Min(0.30f, stepInterval + 0.010f);
            float newEff = EffInterval;
            if (oldEff > 0.0001f) timer = Mathf.Min(timer / oldEff * newEff, newEff);
        }

        // Perf: history is never read beyond head + body + the tail's origin cell.
        int keep = segments.Count + 2;
        while (trail.Count > keep) trail.RemoveAt(trail.Count - 1);
        return true;
    }

    // Continuous glide rendered on a Catmull-Rom spline through the cell
    // centers. Straight runs stay mathematically straight (the spline is
    // exactly linear on collinear, evenly spaced points), while a turn bends
    // smoothly through the corner instead of pivoting 90 degrees on the grid
    // point - that hard pivot was the "snapping to grid" look.
    //
    // The cell ahead of the head is not committed yet: it is synthesized from
    // outDirSm, an eased copy of dir. Pressing a turn therefore bends the
    // rendered path immediately and continuously (the ease spreads the change
    // over ~60ms, so there is never a position jump), and the head's rotation
    // is locked to the path tangent, so the nose is always precisely aligned
    // with the direction of travel.
    void ApplyVisuals()
    {
        if (trail.Count == 0) return;

        float eff = Mathf.Max(EffInterval, 0.0001f);
        float p = Mathf.Clamp01(timer / eff);

        Vector3 fwd = new Vector3(dir.x, 0f, dir.y);

        // Frame-rate independent ease of the outgoing heading (visual only).
        // Gentle, constant rate: a press spreads its path adjustment thinly
        // over ~150ms (no lurch). Any residual at commit (a turn pressed in
        // the final frames of the cell) is handled by the render smoothing
        // layer below, not by rushing this ease.
        outDirSm = Vector3.Slerp(outDirSm, fwd, 1f - Mathf.Exp(-18f * Time.deltaTime));
        outDirSm = outDirSm.sqrMagnitude < 1e-8f ? fwd : outDirSm.normalized;

        Vector3 offset = Vector3.zero;
        float punch = 0f;
        if (recoilT > 0f)
        {
            float q = recoilT / RecoilLen;                 // 1 -> 0
            offset = -fwd * (RecoilAmp * q * q);           // ease-out kick backwards
            punch = 0.3f * q;
        }

        // Spline targets -> smoothed render positions. The damp absorbs any
        // single-frame sideways step of the target (turn pressed right before
        // a cell commit) by spreading it over 3-4 frames.
        EnsureVisState();
        if (visHead.sqrMagnitude < 1e-6f) { visHead = CurvePos(1, p); visHeadVel = Vector3.zero; }
        visHead = Vector3.SmoothDamp(visHead, CurvePos(1, p), ref visHeadVel, RenderSmoothTime);

        transform.position = visHead + offset;
        transform.localScale = Vector3.one * (1f + punch);

        // nose = direction the rendered snake actually travels (falls back to
        // the spline tangent while standing still, e.g. on death)
        Vector3 tang = visHeadVel.sqrMagnitude > 1e-6f
            ? visHeadVel
            : CurvePos(1, Mathf.Min(p + 0.06f, 1f)) - CurvePos(1, Mathf.Max(p - 0.06f, 0f));
        if (tang.sqrMagnitude > 1e-8f)
            transform.rotation = Quaternion.LookRotation(tang.normalized, Vector3.up);

        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            if (s == null) continue;
            visSeg[i] = Vector3.SmoothDamp(visSeg[i], CurvePos(i + 2, p), ref visSegVel[i], RenderSmoothTime);
            s.transform.position = visSeg[i] + offset;
        }
    }

    /// Keeps the render-smoothing buffers in sync with the segment count
    /// (segments are pooled and spawned on growth); new entries snap to the
    /// segment's current position.
    void EnsureVisState()
    {
        if (visSeg.Length == segments.Count) return;
        var np = new Vector3[segments.Count];
        var nv = new Vector3[segments.Count];
        for (int i = 0; i < segments.Count; i++)
            np[i] = segments[i] != null ? segments[i].transform.position : transform.position;
        visSeg = np;
        visSegVel = nv;
    }

    /// Catmull-Rom position along leg `leg` (the leg runs trail[leg] ->
    /// trail[leg-1]; the head glides trail[1] -> trail[0], so the head is leg
    /// 1 and body segment i is leg i+2). u = 0..1 across the leg.
    Vector3 CurvePos(int leg, float u)
    {
        int last = trail.Count - 1;
        Vector3 N(int k) => ToWorld(trail[Mathf.Clamp(k, 0, last)]);

        Vector3 p0 = N(leg + 1);
        Vector3 p1 = N(leg);
        Vector3 p2 = N(leg - 1);
        // past the head there is no committed cell yet: the virtual next cell
        // follows the eased outgoing direction (outDirSm)
        Vector3 p3 = leg >= 2 ? N(leg - 2) : ToWorld(trail[0]) + outDirSm;

        float u2 = u * u, u3 = u2 * u;
        return 0.5f * (2f * p1
                     + (-p0 + p2) * u
                     + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2
                     + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
    }
}
