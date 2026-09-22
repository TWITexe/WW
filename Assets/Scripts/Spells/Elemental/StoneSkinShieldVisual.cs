using Mirror;
using UnityEngine;

// держит серый магический щит вокруг персонажа ровно пока сервер считает защиту активной.
[DefaultExecutionOrder(200)]
public class StoneSkinShieldVisual : NetworkBehaviour
{
    [SerializeField] private Health health;
    [SerializeField] private ElementalSpell definition;
    [SerializeField] private Vector3 localPosition;
    private GameObject instance;
    private WizardAppearance appearance;

    private void Awake() => appearance = GetComponent<WizardAppearance>();

    // синхронизированный запас защиты уже доступен, в том числе при позднем подключении клиента.
    public override void OnStartClient()
    {
        health.ShieldChanged += Refresh;
        Refresh(health.Shield);
    }

    // частицы создаются только на клиентах; выделенный сервер не тратит ресурсы на графику.
    private void Refresh(int amount)
    {
        if (amount > 0 && instance == null)
        {
            instance = Instantiate(definition.effectPrefab, transform);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;
            FollowBody();
        }
        else if (amount <= 0 && instance != null)
        {
            Destroy(instance);
            instance = null;
        }
    }

    private void LateUpdate() => FollowBody();

    // Следуем за тем же сглаженным положением, что и мантия. Локальные частицы щита
    // больше не разворачиваются скачком вместе с физическим корнем на каждом тике.
    private void FollowBody()
    {
        if (instance == null) return;
        Vector3 offset = appearance != null ? appearance.VisualDisplacement : Vector3.zero;
        instance.transform.SetPositionAndRotation(transform.TransformPoint(localPosition) + offset, Quaternion.identity);
    }

    // отключение клиента и удаление персонажа не оставляют частиц или подписок от прежнего щита.
    public override void OnStopClient()
    {
        health.ShieldChanged -= Refresh;
        if (instance != null) Destroy(instance);
        instance = null;
    }

#if UNITY_EDITOR
    // сохраняем ссылки и положение у ног игрока при настройке префаба.
    public void Configure(Health owner, ElementalSpell spell, Vector3 position)
    {
        health = owner;
        definition = spell;
        localPosition = position;
    }
#endif
}
