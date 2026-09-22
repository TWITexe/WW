using UnityEngine;

// дополнительный короткий след делает усиленный выстрел различимым на каждом клиенте.
public class LensProjectileGlow : MonoBehaviour
{
    private static Material material;

    public static void Show(GameObject projectile)
    {
        if (projectile.GetComponent<LensProjectileGlow>() != null) return;
        projectile.AddComponent<LensProjectileGlow>();
        var child = new GameObject("Lens fire and ice trail");
        child.transform.SetParent(projectile.transform, false);
        var trail = child.AddComponent<TrailRenderer>();
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            material.SetFloat("_Surface", 1);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
        }
        trail.sharedMaterial = material;
        trail.time = .25f;
        trail.minVertexDistance = .05f;
        trail.startWidth = .12f;
        trail.endWidth = 0;
        trail.startColor = new Color(1, .45f, .1f);
        trail.endColor = new Color(.35f, .8f, 1);
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
