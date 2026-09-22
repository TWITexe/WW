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
    private bool wasOwner;
    [SerializeField, Range(0.01f, .3f)] private float ownerVisibility = .08f;
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
        ElementalVisual.EnlargeAreaParticles(visualRoot);
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
        bool owner = NetworkClient.localPlayer != null && NetworkClient.localPlayer.netId == effect.ownerId;
        if (visualRoot != null) visualRoot.SetActive(!armed || owner);
        if (appearanceReady && wasArmed == armed && wasOwner == owner) return;
        appearanceReady = true;
        wasArmed = armed;
        wasOwner = owner;
        for (int index = 0; index < renderers.Length; index++)
        {
            Color color = baseColors[index];
            // только владелец различает слабый след взведённой ловушки; для остальных корень графики выключен.
            float brightness = .35f;
            color.r *= brightness;
            color.g *= brightness;
            color.b *= brightness;
            if (armed) color.a *= ownerVisibility;
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
