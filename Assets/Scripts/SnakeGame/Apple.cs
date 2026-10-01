using UnityEngine;

public class Apple : MonoBehaviour
{
    public Vector2Int cell;

    Vector3 basePos;
    float t;

    // Real apple model (Kenney, CC0) — loaded once, shared by every pooled apple.
    static GameObject modelPrefab;

    /// Called by GameManager when (re)placing from the pool.
    public void Place(Vector2Int c)
    {
        cell = c;
        transform.position = new Vector3(c.x, 0.5f, c.y);
        basePos = transform.position;
        t = 0f;
    }

    void Start()
    {
        basePos = transform.position;

        var body = GetComponent<Renderer>();
        if (modelPrefab == null) modelPrefab = Resources.Load<GameObject>("Models/apple/apple");

        if (modelPrefab != null && body != null)
        {
            body.enabled = false;                    // hide the placeholder sphere

            var model = Instantiate(modelPrefab, transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // The mesh is ~0.19 units tall with its origin at the base: scale it
            // to a proper fruit size, then drop it so it sits on the grass (the
            // apple pivot floats at cell height 0.5 — the old sphere spanned 0..1).
            var rend = model.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                float h = rend.bounds.size.y;
                if (h > 0.0001f) model.transform.localScale = Vector3.one * (0.55f / h);
            }
            model.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        }
        else if (body != null)
        {
            // Fallback if the model ever goes missing: plain red ball, no glow.
            body.material.color = new Color(0.85f, 0.12f, 0.1f);
        }
        // No point light / emission anymore — a real apple doesn't glow.
    }

    void Update()
    {
        t += Time.deltaTime;
        transform.Rotate(0f, 80f * Time.deltaTime, 0f, Space.World);
        transform.position = basePos + new Vector3(0f, Mathf.Sin(t * 2.5f) * 0.15f, 0f);
    }
}
