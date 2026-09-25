using UnityEngine;
using UnityEngine.UI;

// Ten vector sigils stay crisp at HUD size without requiring external sprite assets.
[RequireComponent(typeof(CanvasRenderer))]
public class UltimateIconGraphic : MaskableGraphic
{
    public UltimateKind kind;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        Vector2 center = rect.center;
        float r = Mathf.Min(rect.width, rect.height) * .45f;
        Ring(mesh, center, r, r * .055f, 40);
        int points = kind == UltimateKind.PolarPiercer ? 3 :
            kind == UltimateKind.MirrorLabyrinth ? 6 : 4;
        float rotation = (int)kind * 13;
        for (int i = 0; i < points; i++)
        {
            float a = (rotation + i * 360f / points) * Mathf.Deg2Rad;
            Vector2 outer = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * .75f;
            Segment(mesh, center, outer, r * .09f);
            Ring(mesh, outer, r * .16f, r * .05f, kind == UltimateKind.GlacierRam ? 8 : 4);
        }
        Ring(mesh, center, r * .25f, r * .1f, kind == UltimateKind.PhoenixBirth ? 3 : 8);
    }
    void Ring(VertexHelper mesh, Vector2 center, float radius, float width, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float a = i * Mathf.PI * 2 / count, b = (i + 1) * Mathf.PI * 2 / count;
            Segment(mesh, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, width);
        }
    }
    void Segment(VertexHelper mesh, Vector2 a, Vector2 b, float width)
    {
        Vector2 delta = (b - a).normalized;
        Vector2 side = new Vector2(-delta.y, delta.x) * width * .5f;
        int index = mesh.currentVertCount;
        mesh.AddVert(a - side, color, Vector2.zero); mesh.AddVert(a + side, color, Vector2.zero);
        mesh.AddVert(b + side, color, Vector2.zero); mesh.AddVert(b - side, color, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
    }
}
