using Mirror;
using UnityEngine;
using UnityEngine.VFX;

// настраивает декоративные эффекты; перемещение, попадания и сетевое время жизни остаются у родительского объекта.
public class SpellVfx : MonoBehaviour
{
    public VisualEffect effect;
    public Color tint = Color.white;
    [Min(.05f)] public float size = .65f;
    [Min(.05f)] public float radius = 1;
    public bool area;
    public bool impact;
    // пропускаем графику на выделенном сервере и запускаем эффект на клиентах.
    private void Start()
    {
        if (!NetworkClient.active && NetworkServer.active) { if (effect != null) effect.enabled = false; return; }
        Configure();
        if (effect != null)
        {
            effect.Play();
            // импортированный эффект выпускает частицы редко; быстрый снаряд может
            // долететь до стены раньше первой частицы, поэтому заранее продвигаем локальную симуляцию.
            if (!area) effect.Simulate(.05f, 30);
        }
    }
    // задаём только поддерживаемые графом параметры цвета, размера и радиуса.
    public void Configure()
    {
        if (effect == null) return;
        if (effect.HasVector3("FireballMovingSpeed")) effect.SetVector3("FireballMovingSpeed", Vector3.zero);
        if (effect.HasFloat("FireballSize")) effect.SetFloat("FireballSize", size);
        if (effect.HasFloat("Radius")) effect.SetFloat("Radius", radius);
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.Lerp(tint, Color.white, .5f), 0), new GradientColorKey(tint, .35f), new GradientColorKey(tint * .35f, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        if (effect.HasGradient("FIreballColorGradient")) effect.SetGradient("FIreballColorGradient", gradient);
        if (effect.HasGradient("FireballTrailsGradient")) effect.SetGradient("FireballTrailsGradient", gradient);
        foreach (string property in new[] { "Color", "Color2", "Color3", "Color4" })
        {
            Color color = property == "Color" ? Color.Lerp(tint, Color.white, .3f) : tint;
            if (effect.HasVector4(property)) effect.SetVector4(property, color * 2);
            else if (effect.HasVector3(property)) effect.SetVector3(property, new Vector3(color.r, color.g, color.b) * 2);
        }
    }
    // взрыв и попадание используют общий набор одноразовых эффектов пакета.
    public static void Burst(Vector3 position, Color color, float radius,
        SpellHitKind kind = SpellHitKind.Holy)
    {
        SpellHitLibrary.Play(kind, position, color, radius);
    }

    // цвет применяется к магической вспышке, а тип определяет снег, камни или пламя.
    public static void Impact(Vector3 position, Color color, float radius, SpellHitKind kind = SpellHitKind.Holy)
    {
        SpellHitLibrary.Play(kind, position, color, radius);
    }
}