using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class ShopLockGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear(); Rect r = GetPixelAdjustedRect();
        void Box(float x, float y, float w, float h, Color tint)
        {
            int n = helper.currentVertCount;
            helper.AddVert(new Vector3(r.x + x*r.width, r.y + y*r.height), tint, Vector2.zero);
            helper.AddVert(new Vector3(r.x + (x+w)*r.width, r.y + y*r.height), tint, Vector2.zero);
            helper.AddVert(new Vector3(r.x + (x+w)*r.width, r.y + (y+h)*r.height), tint, Vector2.zero);
            helper.AddVert(new Vector3(r.x + x*r.width, r.y + (y+h)*r.height), tint, Vector2.zero);
            helper.AddTriangle(n,n+1,n+2); helper.AddTriangle(n,n+2,n+3);
        }
        Box(.12f,.08f,.76f,.51f,color);
        Box(.25f,.54f,.12f,.3f,color); Box(.63f,.54f,.12f,.3f,color); Box(.25f,.79f,.5f,.12f,color);
        Box(.43f,.26f,.14f,.20f,new Color(.13f,.08f,.04f,1));
    }
}
