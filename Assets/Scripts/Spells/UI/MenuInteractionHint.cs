using UnityEngine;

// управляет яркостью сохранённого префаба; форма и размер задаются вручную через transform и линии.
public sealed class MenuInteractionHint : MonoBehaviour
{
    [SerializeField] private LineRenderer glow;
    [SerializeField] private LineRenderer core;
    [SerializeField] private Color color = new Color(1, .65f, .24f);
    [SerializeField, Min(.001f)] private float glowWidth = .07f;
    [SerializeField, Min(.001f)] private float coreWidth = .014f;
    private float hoverAmount;

    // подсветка не меняет свою геометрию ни при запуске, ни при изменении коллайдеров.
    private void Awake() => ApplyAppearance(.26f);

    // показываем настройки цвета и толщины сразу в редакторе.
    private void OnValidate() => ApplyAppearance(.26f);

    public void Show(bool hovered)
    {
        gameObject.SetActive(true);
        hoverAmount = Mathf.MoveTowards(hoverAmount, hovered ? 1 : 0, Time.unscaledDeltaTime * 5);
        float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 1.8f);
        ApplyAppearance(Mathf.Lerp(.22f + pulse * .08f, .85f, hoverAmount));
    }

    // прячем готовый объект, сохраняя выставленные пользователем положение и масштаб.
    public void Hide()
    {
        hoverAmount = 0;
        gameObject.SetActive(false);
    }

    private void ApplyAppearance(float alpha)
    {
        if (glow != null)
        {
            glow.widthMultiplier = glowWidth;
            glow.startColor = glow.endColor = new Color(color.r, color.g, color.b, alpha);
        }
        if (core != null)
        {
            core.widthMultiplier = coreWidth;
            Color edge = Color.Lerp(color, Color.white, .5f);
            edge.a = alpha * .7f;
            core.startColor = core.endColor = edge;
        }
    }

#if UNITY_EDITOR
    // установщик сохраняет прямые ссылки на две линии внутри префаба.
    public void Configure(LineRenderer soft, LineRenderer edge)
    {
        glow = soft;
        core = edge;
        ApplyAppearance(.26f);
    }
#endif
}
