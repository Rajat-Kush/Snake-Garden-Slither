using UnityEngine;

/// A scenery rock (replaces the old orange block).
/// Applies one of the three procedural low-poly RockKit meshes + shared
/// rock material, with slight per-instance rotation for variety.
public class Obstacle : MonoBehaviour
{
    void Start()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf == null) return;

        int variant = Mathf.Abs(gameObject.GetInstanceID()) % 3;
        mf.sharedMesh = RockKit.Mesh(variant);

        var r = GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = RockKit.Material;

        transform.rotation = Quaternion.Euler(
            Random.Range(-4f, 4f), Random.Range(0f, 360f), Random.Range(-4f, 4f));
    }
}
