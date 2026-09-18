using System.Collections.Generic;
using Mirror;
using UnityEngine;

// управляет внешностью мага, поворотом головы, парящим посохом и локальными обломками при смерти.
[DefaultExecutionOrder(100)]
public class WizardAppearance : NetworkBehaviour
{
    public Transform visualRoot;
    public Transform[] pieces;
    [Header("Head and floating staff")]
    [SerializeField, Min(1)] private float headTurnSpeed = 24;
    [SerializeField, Range(0, 80)] private float maxHeadYaw = 55;
    [SerializeField, Min(1)] private float staffFollowSpeed = 7;
    [SerializeField, Min(0)] private float maxStaffLag = .35f;
    [SerializeField, Min(0)] private float staffBobHeight = .025f;
    [SyncVar] private float lookYaw;
    private RelativeMovement movement;
    private Transform head, hat, staff;
    private Quaternion headRest, hatRest, staffRest;
    private Vector3 staffRestPosition, staffPosition, lastPosition;
    private Quaternion staffRotation;
    private float headYaw, lastSentYaw;
    private double nextLookSend;
    private bool poseReady;
    private Health health;
    private bool wasDead;
    private Collider[] bodyColliders;
    private bool[] colliderDefaults;
    private readonly List<GameObject> debris = new List<GameObject>();
    // запоминаем исходные позы частей модели и состояние коллайдеров для восстановления после смерти.
    private void Awake()
    {
        health = GetComponent<Health>();
        movement = GetComponent<RelativeMovement>();
        if (pieces != null)
            foreach (Transform piece in pieces)
            {
                if (piece == null) continue;
                if (piece.name == "Head") { head = piece; headRest = piece.localRotation; }
                if (piece.name == "Hat") { hat = piece; hatRest = piece.localRotation; }
                if (piece.name == "Staff")
                {
                    staff = piece; staffRest = piece.localRotation;
                    staffRestPosition = piece.localPosition;
                }
            }
        bodyColliders = GetComponentsInChildren<Collider>(true);
        colliderDefaults = new bool[bodyColliders.Length];
        for (int i = 0; i < bodyColliders.Length; i++) colliderDefaults[i] = bodyColliders[i].enabled;
    }
    // реагируем на изменение состояния смерти: скрываем модель, отключаем коллайдеры и создаём обломки.
    private void Update()
    {
        if (health == null || visualRoot == null) return;
        bool dead = health.IsDead;
        if (dead && !wasDead && isClient) BreakApart();
        if (dead != wasDead)
            for (int i = 0; i < bodyColliders.Length; i++)
                if (bodyColliders[i] != null) bodyColliders[i].enabled = !dead && colliderDefaults[i];
        visualRoot.gameObject.SetActive(!dead);
        wasDead = dead;
    }
    // сглаживаем поворот головы и отставание посоха; направление взгляда отправляем не чаще десяти раз в секунду.
    private void LateUpdate()
    {
        if (!isClient || visualRoot == null) return;
        if (health != null && health.IsDead) { poseReady = false; return; }
        float bodyYaw = transform.eulerAngles.y;
        float targetYaw = lookYaw;
        if (isOwned && movement != null && movement.ViewCamera != null)
        {
            targetYaw = movement.ViewCamera.transform.eulerAngles.y;
            if (NetworkTime.time >= nextLookSend && (!poseReady || Mathf.Abs(Mathf.DeltaAngle(lastSentYaw, targetYaw)) > .5f))
            {
                CmdSetLookYaw(targetYaw);
                lastSentYaw = targetYaw;
                nextLookSend = NetworkTime.time + .1;
            }
        }
        bool reset = !poseReady || (transform.position - lastPosition).sqrMagnitude > 9;
        // после возрождения или резкого переноса сразу восстанавливаем позу, не тянем посох через арену.
        if (reset) headYaw = bodyYaw;
        headYaw = Mathf.LerpAngle(headYaw, targetYaw, 1 - Mathf.Exp(-headTurnSpeed * Time.deltaTime));
        float offset = Mathf.Clamp(Mathf.DeltaAngle(bodyYaw, headYaw), -maxHeadYaw, maxHeadYaw);
        headYaw = bodyYaw + offset;
        Quaternion turn = Quaternion.AngleAxis(offset, Vector3.up);
        if (head != null) head.localRotation = turn * headRest;
        if (hat != null) hat.localRotation = turn * hatRest;
        if (staff != null && staff.parent != null)
        {
            Vector3 target = staff.parent.TransformPoint(staffRestPosition);
            target += Vector3.up * (Mathf.Sin((float)(NetworkTime.time * 2.5) + netId) * staffBobHeight);
            Quaternion rotation = staff.parent.rotation * staffRest;
            float blend = 1 - Mathf.Exp(-staffFollowSpeed * Time.deltaTime);
            staffPosition = reset ? target : Vector3.Lerp(staffPosition, target, blend);
            staffPosition = target + Vector3.ClampMagnitude(staffPosition - target, maxStaffLag);
            staffRotation = reset ? rotation : Quaternion.Slerp(staffRotation, rotation, blend);
            staff.SetPositionAndRotation(staffPosition, staffRotation);
        }
        lastPosition = transform.position;
        poseReady = true;
    }
    // сервер отклоняет некорректные числа и нормализует угол взгляда для синхронизации.
    [Command]
    private void CmdSetLookYaw(float yaw)
    {
        if (float.IsNaN(yaw) || float.IsInfinity(yaw)) return;
        lookYaw = Mathf.Repeat(yaw, 360);
    }
    // окрашиваем мантию, шляпу и кристалл через блоки свойств, не изменяя общие материалы.
    public void Tint(Color teamColor)
    {
        if (visualRoot == null) return;
        foreach (Renderer renderer in visualRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.name == "Body_Robe" || renderer.name == "Hat_Separate")
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", teamColor);
                renderer.SetPropertyBlock(block);
            }
            // посох состоит из одного меша с отдельными материалами дерева, металла и кристалла.
            // окрашиваем только кристалл, сохраняя исходные цвета дерева и золотой отделки.
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] != null && materials[i].name.Contains("white crystal"))
                {
                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block, i);
                    block.SetColor("_BaseColor", teamColor);
                    renderer.SetPropertyBlock(block, i);
                }
        }
    }
    // создаём локальные физические копии частей мага, исключаем столкновения с игроками и удаляем через шесть секунд.
    private void BreakApart()
    {
        foreach (Transform piece in pieces)
        {
            if (piece == null) continue;
            var fragment = Instantiate(piece.gameObject, piece.position, piece.rotation);
            fragment.name = "Wizard debris - " + piece.name;
            fragment.transform.localScale = piece.lossyScale;
            var renderers = fragment.GetComponentsInChildren<Renderer>();
            // простые коллайдеры охватывают группы мешей без затрат на построение выпуклой геометрии.
            Bounds bounds = new Bounds();
            bool first = true;
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = world.center + Vector3.Scale(world.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = fragment.transform.InverseTransformPoint(corner);
                    if (first) { bounds = new Bounds(local, Vector3.zero); first = false; }
                    else bounds.Encapsulate(local);
                }
            }
            var collider = fragment.AddComponent<BoxCollider>();
            collider.center = bounds.center; collider.size = bounds.size;
            foreach (Collider playerCollider in FindObjectsByType<Collider>(FindObjectsSortMode.None))
                if (playerCollider.GetComponentInParent<Health>() != null)
                    Physics.IgnoreCollision(collider, playerCollider);
            foreach (Transform child in fragment.GetComponentsInChildren<Transform>()) child.gameObject.layer = 2;
            var body = fragment.AddComponent<Rigidbody>();
            body.mass = piece.name == "Body" ? 2 : 0.6f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.AddExplosionForce(7, transform.position + Vector3.down * 0.3f, 4, 1.5f, ForceMode.Impulse);
            body.AddTorque(Random.onUnitSphere * 5, ForceMode.Impulse);
            debris.Add(fragment);
            Destroy(fragment, 6);
        }
        debris.RemoveAll(item => item == null);
    }
    // удаляем оставшиеся обломки вместе с владельцем.
    private void OnDestroy()
    {
        foreach (GameObject fragment in debris) if (fragment != null) Destroy(fragment);
    }
}
