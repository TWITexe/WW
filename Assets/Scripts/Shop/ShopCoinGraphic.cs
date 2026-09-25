using UnityEngine;
using UnityEngine.UI;

// Vector W coin: stays sharp at small UI sizes and matches the existing lock icon.
[RequireComponent(typeof(CanvasRenderer))]
public class ShopCoinGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect rect = GetPixelAdjustedRect();
        float size = Mathf.Min(rect.width, rect.height);
        Vector2 origin = rect.center - Vector2.one * size * .5f;
        void Vertex(Vector2 point, Color tint) => helper.AddVert(origin + point * size, tint * color, Vector2.zero);
        void Disc(float radius, Vector2 center, Color tint)
        {
            int start = helper.currentVertCount; Vertex(center, tint);
            const int segments = 48;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                Vertex(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint);
                if (i > 0) helper.AddTriangle(start, start + i, start + i + 1);
            }
        }
        void Stroke(Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 delta = (b - a).normalized;
            Vector2 normal = new Vector2(-delta.y, delta.x) * width * .5f;
            int start = helper.currentVertCount;
            Vertex(a-normal,tint); Vertex(b-normal,tint); Vertex(b+normal,tint); Vertex(a+normal,tint);
            helper.AddTriangle(start,start+1,start+2); helper.AddTriangle(start,start+2,start+3);
        }
        Disc(.49f, new Vector2(.5f,.49f), new Color(.32f,.16f,.045f));
        Disc(.46f, new Vector2(.5f,.53f), new Color(1f,.82f,.37f));
        Disc(.375f, new Vector2(.5f,.53f), new Color(.70f,.39f,.075f));
        Disc(.335f, new Vector2(.5f,.53f), new Color(.95f,.64f,.15f));
        var points = new[]{new Vector2(.25f,.70f),new Vector2(.35f,.33f),new Vector2(.50f,.57f),new Vector2(.65f,.33f),new Vector2(.75f,.70f)};
        for(int i=0;i<points.Length-1;i++) Stroke(points[i],points[i+1],.085f,new Color(.30f,.135f,.025f));
    }
}
