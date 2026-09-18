using Mirror;
using UnityEngine;

// показывает готовность печати яркостью импортированного круга, не вмешиваясь в серверную механику.
public class FireSealCircleVisual : MonoBehaviour
{
    [SerializeField] private TacticalEffect effect;
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private Renderer[] renderers;
    private MaterialPropertyBlock properties;
    private Color[] baseColors;
    private bool appearanceReady;
    private bool wasArmed;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    // выделенному серверу не нужны частицы; клиент один раз запоминает цвета сохранённых материалов.
    private void Start()
    {
        if (NetworkServer.active && !NetworkClient.active)
        {
            visualRoot.SetActive(false);
            enabled = false;
            return;
        }
        properties = new MaterialPropertyBlock();
        baseColors = new Color[renderers.Length];
        for (int index = 0; index < renderers.Length; index++)
            baseColors[index] = renderers[index].sharedMaterial.GetColor(BaseColor);
    }

    // читаем сетевое время взведения, а материалы обновляем только при смене готовности.
    private void LateUpdate()
    {
        if (properties == null || effect == null) return;
        bool armed = effect.Armed;
        if (appearanceReady && wasArmed == armed) return;
        appearanceReady = true;
        wasArmed = armed;
        for (int index = 0; index < renderers.Length; index++)
        {
            Color color = baseColors[index];
            float brightness = armed ? 1f : .35f;
            color.r *= brightness;
            color.g *= brightness;
            color.b *= brightness;
            renderers[index].GetPropertyBlock(properties);
            properties.SetColor(BaseColor, color);
            renderers[index].SetPropertyBlock(properties);
        }
    }

#if UNITY_EDITOR
    // сохраняем прямые ссылки при сборке префаба, без поиска объектов во время игры.
    public void Configure(TacticalEffect owner, GameObject root, Renderer[] targets)
    {
        effect = owner;
        visualRoot = root;
        renderers = targets;
    }
#endif
}
