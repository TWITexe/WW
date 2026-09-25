using Mirror;
using UnityEngine;

// All persistent visuals are baked into editable prefabs. This component only animates them.
[DefaultExecutionOrder(240)]
public class UltimatePresentation : MonoBehaviour
{
    public GameObject phoenixAura, meteor, spirit, flight, prisms, healingRing;
    public Transform[] crystals;
    public LineRenderer beam;
    public RectTransform meteorBar;
    public UnityEngine.UI.Image meteorFill;
    public Collider meteorCollider;
    public Collider[] spiritColliders;
    public Transform meteorModel, spiritModel;
    public Transform[] smoothRoots;
    public LineRenderer polarGuide;
    private static Material beamMaterial;
    private float lastMeteorAngle;
    private PlayerUltimate owner;
    private RelativeMovement movement;
    private Vector3[] restPositions;

    public bool IsFormCollider(Collider collider, bool meteorForm)
    {
        if (meteorForm) return collider == meteorCollider;
        if (spiritColliders != null) foreach (var part in spiritColliders) if (part == collider) return true;
        return false;
    }
    void Awake()
    {
        foreach (var item in new[] { phoenixAura, meteor, spirit, flight, prisms, healingRing }) if (item != null) item.SetActive(false);
        if (beam != null) beam.enabled = false;
        if (polarGuide != null) polarGuide.enabled = false;
        movement = GetComponentInParent<RelativeMovement>();
        if (smoothRoots != null)
        {
            restPositions = new Vector3[smoothRoots.Length];
            for (int i = 0; i < smoothRoots.Length; i++) if (smoothRoots[i] != null) restPositions[i] = smoothRoots[i].localPosition;
        }
    }
    public void Refresh(PlayerUltimate owner)
    {
        this.owner = owner;
        bool alive = !owner.GetComponent<Health>().IsDead;
        bool visible = owner.isClient;
        // Forms retain hitboxes on a dedicated server; their renderers need no updates there.
        Set(meteor, alive && owner.IsMeteor);
        Set(spirit, alive && owner.IsSpirit);
        Set(phoenixAura, alive && visible && owner.Active && owner.Kind == UltimateKind.PhoenixBirth && !owner.IsMeteor);
        Set(flight, alive && visible && owner.Active && owner.Kind == UltimateKind.SteamFlight);
        Set(prisms, alive && visible && owner.Active && owner.Kind == UltimateKind.PrismaticVolley);
        Set(healingRing, alive && visible && owner.Active && owner.Kind == UltimateKind.HeatDrain);
        if (!visible) return;
        if (meteor != null && meteor.activeSelf)
        {
            lastMeteorAngle += Time.deltaTime * 12;
            if (meteorModel != null) meteorModel.localRotation = Quaternion.Euler(8, lastMeteorAngle, 12);
        }
        if (meteorBar != null)
        {
            Set(meteorBar.gameObject, alive && owner.IsMeteor);
            if (meteorFill != null) meteorFill.fillAmount = owner.FormHealth / (float)Mathf.Max(1, owner.FormMaxHealth);
            var camera = Camera.main;
            if (camera == null && NetworkClient.localPlayer != null) camera = NetworkClient.localPlayer.GetComponentInChildren<RelativeMovement>()?.ViewCamera;
            if (camera != null) meteorBar.rotation = camera.transform.rotation;
        }
        if (crystals != null && prisms != null && prisms.activeSelf)
        {
            for (int i = 0; i < crystals.Length; i++)
            {
                if (crystals[i] == null) continue;
                crystals[i].localScale = Vector3.one * (i < owner.Charges ? .45f : .2f);
                crystals[i].Rotate(Vector3.up, Time.deltaTime * 50, Space.Self);
            }
        }
        if (phoenixAura != null && phoenixAura.activeSelf) phoenixAura.transform.Rotate(Vector3.up, Time.deltaTime * 35);
        if (beam != null)
        {
            beam.enabled = alive && owner.Active && owner.Kind == UltimateKind.HeatDrain;
            if (beam.enabled)
            {
                beam.SetPosition(0, owner.GetComponent<PlayerNetworkCaster>().ShotOrigin());
                beam.SetPosition(1, owner.BeamEnd);
                Color tint = owner.BeamHit ? new Color(.9f, .015f, .045f) : new Color(.55f, .01f, .025f, .65f);
                beam.startColor = tint; beam.endColor = new Color(.4f, .005f, .015f, tint.a);
            }
        }
    }
    void LateUpdate()
    {
        if (owner == null || !owner.isClient) return;
        Vector3 offset = owner.isLocalPlayer && movement != null ? movement.PresentationOffset : Vector3.zero;
        if (smoothRoots != null && restPositions != null)
            for (int i = 0; i < smoothRoots.Length; i++)
                if (smoothRoots[i] != null) smoothRoots[i].localPosition = restPositions[i] + smoothRoots[i].parent.InverseTransformVector(offset);
        // ShotOrigin already follows the smoothed staff; applying the body offset again would jitter it.
        if (beam != null && beam.enabled) beam.SetPosition(0, owner.GetComponent<PlayerNetworkCaster>().ShotOrigin());
        if (polarGuide == null) return;
        polarGuide.enabled = owner.isLocalPlayer && owner.Active && owner.Kind == UltimateKind.PolarPiercer && !PlayerGameUI.InputBlocked;
        if (!polarGuide.enabled) return;
        var caster = owner.GetComponent<PlayerNetworkCaster>();
        var ray = caster.ViewCamera.ViewportPointToRay(new Vector3(.5f, .5f));
        float depth = Mathf.Max(0, Vector3.Dot(caster.ShotOrigin() - ray.origin, ray.direction));
        Vector3 start = caster.ShotOrigin();
        Vector3 direction = (ray.GetPoint(depth + owner.catalog.laserRange) - start).normalized;
        polarGuide.SetPosition(0, start); polarGuide.SetPosition(1, start + direction * 2.5f);
    }
    static void Set(GameObject root, bool active) { if (root != null && root.activeSelf != active) root.SetActive(active); }
    public static void FlashBeam(Vector3 start, Vector3 end, Color color, float lifetime)
    {
        var root = new GameObject("Polar laser trail"); root.layer = 2;
        var line = root.AddComponent<LineRenderer>();
        if (beamMaterial == null) beamMaterial = Resources.Load<Material>("UltimateBeam");
        line.sharedMaterial = beamMaterial;
        line.positionCount = 2; line.SetPosition(0, start); line.SetPosition(1, end);
        line.startWidth = .13f; line.endWidth = .055f;
        line.startColor = line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        root.AddComponent<UltimateLaserTrail>().Initialize(line, color, lifetime);
    }
}
