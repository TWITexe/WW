using UnityEngine;
using UnityEngine.UI;

// отображает плоскую копию настоящей эмблемы с обложки книги, сохраняя её форму и цвета.
[RequireComponent(typeof(CanvasRenderer))]
public class BookSymbolGraphic : MaskableGraphic
{
    [SerializeField] private Mesh symbol;

    // перестраиваем изображение только при смене назначенной стихии.
    public void SetSymbol(Mesh value)
    {
        if (symbol == value) return;
        symbol = value;
        SetVerticesDirty();
    }

    // сохранённая геометрия уже нормализована редактором; в игре не читаем модели обложек.
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        if (symbol == null) return;
        Rect rect = GetPixelAdjustedRect();
        Vector3[] vertices = symbol.vertices;
        Color[] colors = symbol.colors;
        int[] triangles = symbol.triangles;
        for (int index = 0; index < vertices.Length; index++)
        {
            Vector3 point = vertices[index];
            point.x = rect.center.x + point.x * rect.width;
            point.y = rect.center.y + point.y * rect.height;
            helper.AddVert(point, colors[index] * color, Vector2.zero);
        }
        for (int index = 0; index < triangles.Length; index += 3)
            helper.AddTriangle(triangles[index], triangles[index + 1], triangles[index + 2]);
    }
}
