using TMPro;
using UnityEngine;

public class UltimateHUD : MonoBehaviour
{
    public UnityEngine.UI.Image fill, frame;
    public UltimateIconGraphic icon;
    public TMP_Text timer, keyLabel, title;
    public UnityEngine.UI.Button debugChargeButton;
    private PlayerUltimate ultimate;
    private int previousSeconds = -1, previousCharges = -1, previousPercent = -1;
    private bool previousActive;
    void Awake()
    {
        if (debugChargeButton == null) return;
        debugChargeButton.gameObject.SetActive(false);
        debugChargeButton.onClick.AddListener(ChargeForTesting);
    }
    void OnDestroy()
    {
        if (debugChargeButton != null) debugChargeButton.onClick.RemoveListener(ChargeForTesting);
    }
    void ChargeForTesting() => ultimate?.DebugChargeUltimate();
    public void Bind(PlayerUltimate value)
    {
        ultimate = value;
        if (value == null || value.Definition == null) return;
        title.text = value.Definition.title;
        icon.kind = value.Definition.kind; icon.color = value.Definition.color; icon.SetVerticesDirty();
        frame.color = value.Definition.color;
    }
    void Update()
    {
        bool canDebugCharge = ultimate != null && ultimate.CanDebugCharge;
        if (debugChargeButton != null)
        {
            if (debugChargeButton.gameObject.activeSelf != canDebugCharge)
                debugChargeButton.gameObject.SetActive(canDebugCharge);
            debugChargeButton.interactable = canDebugCharge && !ultimate.ChargeReady;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (canDebugCharge && Input.GetKeyDown(KeyCode.F8)) ChargeForTesting();
#endif
        if (ultimate == null || ultimate.Definition == null) return;
        bool active = ultimate.Active;
        int seconds = Mathf.CeilToInt((float)ultimate.ActiveRemaining);
        int percent = Mathf.FloorToInt(ultimate.ChargePercent);
        if (seconds != previousSeconds || ultimate.Charges != previousCharges || percent != previousPercent || active != previousActive)
        {
            previousSeconds = seconds; previousCharges = ultimate.Charges; previousPercent = percent; previousActive = active;
            bool charges = active && (ultimate.Kind == UltimateKind.PolarPiercer || ultimate.Kind == UltimateKind.PrismaticVolley || ultimate.Kind == UltimateKind.MirrorLabyrinth);
            int capacity = ultimate.Kind == UltimateKind.PrismaticVolley ? UltimateCatalog.VolleyCapacity : ultimate.Kind == UltimateKind.MirrorLabyrinth ? 6 : 3;
            timer.text = !active ? percent + "%" : charges ? ultimate.Charges + "/" + capacity : seconds > 0 ? seconds.ToString() : "✦";
            keyLabel.text = active ? (ultimate.Kind == UltimateKind.PolarPiercer ? "ЛКМ" : "F") : "F";
        }
        fill.fillAmount = active ? 0 : 1 - ultimate.ChargePercent / 100;
        frame.color = active || ultimate.ChargeReady ? Color.Lerp(ultimate.Definition.color, Color.white, .25f + Mathf.Sin(Time.unscaledTime * 4) * .15f) : ultimate.Definition.color;
    }
}
