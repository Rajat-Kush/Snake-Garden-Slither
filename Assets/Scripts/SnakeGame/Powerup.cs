using UnityEngine;

public enum PowerType
{
    RockHead,     // smash through obstacles
    SpeedBoost,   // 1.6x speed with anime speed lines
    BigApple      // x3 score window, snake blinks
}

public class Powerup : MonoBehaviour
{
    public PowerType type;
    public Vector2Int cell;
    public float dieTime;

    Vector3 basePos;
    Vector3 baseScale;
    float t;

    /// Called by GameManager when (re)placing from the pool. Captures base pose/scale.
    public void Place(Vector2Int c, float life)
    {
        cell = c;
        dieTime = Time.time + life;
        transform.position = new Vector3(c.x, 0.5f, c.y);
        basePos = transform.position;
        baseScale = transform.localScale;
        t = 0f;
    }

    void Start()
    {
        var m = GetComponent<Renderer>().material;
        Color col, em, lightCol;
        float lightRange, lightIntensity;

        switch (type)
        {
            case PowerType.RockHead:   // steel cube
                col = new Color(0.70f, 0.74f, 0.80f);
                em = new Color(0.30f, 0.34f, 0.42f) * 1.6f;
                lightCol = new Color(0.65f, 0.75f, 1f);
                lightRange = 4.5f; lightIntensity = 2.6f;
                break;
            case PowerType.SpeedBoost: // electric-yellow capsule
                col = new Color(1f, 0.90f, 0.15f);
                em = new Color(1f, 0.85f, 0.10f) * 1.8f;
                lightCol = new Color(1f, 0.92f, 0.25f);
                lightRange = 5f; lightIntensity = 3f;
                break;
            default:                   // Big Apple — hot gold-red, bigger glow
                col = new Color(1f, 0.55f, 0.10f);
                em = new Color(1f, 0.35f, 0.10f) * 2f;
                lightCol = new Color(1f, 0.5f, 0.15f);
                lightRange = 5.5f; lightIntensity = 3.2f;
                break;
        }

        m.color = col;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", em);

        var glow = gameObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = lightCol;
        glow.range = lightRange;
        glow.intensity = lightIntensity;
        glow.cullingMask = ~(1 << 2);   // don't re-render snake segments per light pass
    }

    void Update()
    {
        t += Time.deltaTime;

        transform.Rotate(0f, 70f * Time.deltaTime, 0f, Space.World);
        float bob = Mathf.Sin(t * 2.5f) * 0.15f;
        transform.position = basePos + new Vector3(0f, bob, 0f);

        // Blink and shrink during the last 2 seconds before it vanishes.
        float left = dieTime - Time.time;
        if (left > 0f && left < 2f)
        {
            float k = (Mathf.Sin(t * 20f) + 1f) * 0.5f;
            transform.localScale = baseScale * Mathf.Lerp(0.6f, 1.3f, k);
        }
        else if (left > 0f)
        {
            transform.localScale = baseScale;
        }
    }
}
