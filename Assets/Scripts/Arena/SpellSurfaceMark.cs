using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// небольшие следы привязываются к поверхности и исчезают; общее число ограничено для длинных матчей.
public class SpellSurfaceMark : MonoBehaviour
{
    // вдвое крупнее прежних следов; усиление непрозрачности делает их отчётливее.
    private const float SizeMultiplier = 6f;
    private const float OpacityMultiplier = 1.5f;
    private static readonly Queue<SpellSurfaceMark> marks = new Queue<SpellSurfaceMark>();
    private static readonly Vector3[] directions = { Vector3.down, Vector3.up, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
    private static Material material;
    private Mesh mesh;
    private MeshRenderer surface;
    private MaterialPropertyBlock properties;
    private Color tint;
    private float createdAt;
    private ArenaDestructible wall;

    public static void Place(Vector3 position, SpellHitKind kind, float radius)
    {
        RaycastHit nearest = default;
        float distance = 1.3f;
        foreach (var direction in directions)
        {
            if (!Physics.Raycast(position, direction, out var hit, distance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            if (hit.collider.GetComponentInParent<Health>() != null || hit.rigidbody != null) continue;
            nearest = hit;
            distance = hit.distance;
        }
        if (nearest.collider == null) return;
        if (material == null) material = Resources.Load<Material>("SpellSurfaceMark");
        if (material == null) return;
        while (marks.Count >= 64)
        {
            var oldest = marks.Dequeue();
            if (oldest != null) Destroy(oldest.gameObject);
        }
        var obj = new GameObject("след заклинания");
        obj.layer = 2;
        obj.transform.SetPositionAndRotation(nearest.point + nearest.normal * .018f, Quaternion.LookRotation(nearest.normal));
        // при разрушении стены её след тоже скрывается, а при выгрузке сцены удаляется вместе с ней.
        obj.transform.SetParent(nearest.collider.transform, true);
        var mark = obj.AddComponent<SpellSurfaceMark>();
        mark.wall = nearest.collider.GetComponentInParent<ArenaDestructible>();
        mark.Build(nearest, Mathf.Clamp(radius * .35f, .28f, 1.1f) * SizeMultiplier, kind);
        marks.Enqueue(mark);
    }

    private void Build(RaycastHit hit, float radius, SpellHitKind kind)
    {
        const int sides = 48;
        var vertices = new Vector3[sides + 1];
        var colors = new Color[sides + 1];
        var triangles = new int[sides * 3];
        colors[0] = Color.white;
        for (int i = 0; i < sides; i++)
        {
            float angle = i * Mathf.PI * 2 / sides;
            var local = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius * Random.Range(.72f, 1);
            vertices[i + 1] = ProjectEdge(hit, local);
            colors[i + 1] = new Color(1, 1, 1, 0);
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = (i + 1) % sides + 1;
        }
        mesh = new Mesh { name = "неровный след", vertices = vertices, colors = colors, triangles = triangles };
        mesh.RecalculateBounds();
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        surface = gameObject.AddComponent<MeshRenderer>();
        surface.sharedMaterial = material;
        surface.shadowCastingMode = ShadowCastingMode.Off;
        surface.receiveShadows = false;
        tint = kind == SpellHitKind.Fire ? new Color(.15f,.075f,.035f,.8f) :
            kind == SpellHitKind.Snow ? new Color(.55f,.85f,1,.7f) :
            kind == SpellHitKind.Stone ? new Color(.27f,.23f,.17f,.7f) : new Color(.2f,.65f,.65f,.45f);
        properties = new MaterialPropertyBlock();
        createdAt = Time.time;
        SetOpacity(1);
    }

    private Vector3 ProjectEdge(RaycastHit hit, Vector3 local)
    {
        if (TryProject(hit, local, out var projected)) return projected;

        // За границей коллайдера ищем последнюю точку поверхности, а не
        // схлопываем сектор в центр: это создавало треугольные вырезы.
        float inside = 0f;
        float outside = 1f;
        Vector3 edge = Vector3.zero;
        for (int step = 0; step < 12; step++)
        {
            float fraction = (inside + outside) * .5f;
            if (TryProject(hit, local * fraction, out projected))
            {
                inside = fraction;
                edge = projected;
            }
            else outside = fraction;
        }
        return edge;
    }

    private bool TryProject(RaycastHit hit, Vector3 local, out Vector3 projected)
    {
        Vector3 sample = transform.TransformPoint(local);
        if (hit.collider.Raycast(new Ray(sample + hit.normal * .35f, -hit.normal), out var surfaceHit, .7f)
            && Vector3.Dot(surfaceHit.normal, hit.normal) > .5f)
        {
            projected = transform.InverseTransformPoint(surfaceHit.point + surfaceHit.normal * .018f);
            return true;
        }
        projected = default;
        return false;
    }

    private void Update()
    {
        float age = Time.time - createdAt;
        if (age >= 18 || (wall != null && wall.Broken)) { Destroy(gameObject); return; }
        if (age > 14) SetOpacity((18 - age) / 4);
    }
    private void SetOpacity(float opacity)
    {
        Color color = tint;
        color.a = Mathf.Clamp01(color.a * OpacityMultiplier) * opacity;
        properties.SetColor("_BaseColor", color);
        surface.SetPropertyBlock(properties);
    }
    private void OnDestroy()
    {
        if (mesh == null) return;
        if (Application.isPlaying) Destroy(mesh);
        else DestroyImmediate(mesh);
    }
}
