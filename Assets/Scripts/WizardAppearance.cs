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
    [Header("Spell gestures and jumping hat")]
    [SerializeField, Min(.1f)] private float castPlaybackSpeed = 1;
    [SerializeField, Min(.01f)] private float castBlendIn = .075f;
    [SerializeField, Min(.01f)] private float staffReturnSmoothTime = .18f;
    [SerializeField, Min(1)] private float hatHeightFollowSpeed = 28;
    [SerializeField, Min(0)] private float hatRiseLag = .025f;
    [SerializeField, Min(0)] private float hatFallLag = .06f;
    private Vector3 staffGripLocal, hatRestPosition, headCenterLocal;
    private Vector3 displayedGripOffset, displayedGripVelocity;
    private Quaternion displayedStaffRotation;
    private float staffHalfLength, staffThickness;
    private bool staffAvoidBody;
    private float hatHeight;
    private WizardCastGesture castGesture;
    private Vector3 castTarget, castDirection;
    private Quaternion castFrame;
    private bool castAtPoint;
    private byte castVariant;
    private double castStartedAt;
    private float castDuration;
    [SyncVar] private float lookYaw;
    [Header("Idle, hover and sprint")]
    [SerializeField, Min(0)] private float idleBobHeight = .035f;
    [SerializeField, Min(0)] private float movingBobHeight = .065f;
    [SerializeField, Min(0)] private float movingHoverHeight = .16f;
    [SerializeField, Range(0, 10)] private float idleSwayAngle = 1.5f;
    [SerializeField, Range(0, 35)] private float sprintLeanAngle = 16f;
    [SerializeField, Min(.1f)] private float poseBlendSpeed = 7f;
    [SyncVar] private bool remoteSprinting;
    private Vector3 visualRestPosition, lastPresentationPosition, leanDirection;
    private Quaternion visualRestRotation;
    private float movingBlend, sprintBlend, hoverPhase;
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
    private readonly HashSet<Collider> damageColliders = new HashSet<Collider>();
    private readonly HashSet<Collider> headColliders = new HashSet<Collider>();
    public bool HasAnimatedHitboxes => damageColliders.Count > 0;
    public bool IsDamageCollider(Collider collider) => damageColliders.Contains(collider);
    public bool IsHeadCollider(Collider collider) => headColliders.Contains(collider);
    // Смещение отображаемого тела относительно физического корня, без влияния посоха и шляпы.
    public Vector3 VisualDisplacement => visualRoot == null ? Vector3.zero : visualRoot.position -
        (visualRoot.parent != null ? visualRoot.parent.TransformPoint(visualRestPosition) : visualRestPosition);
    private readonly List<GameObject> debris = new List<GameObject>();
    // запоминаем исходные позы частей модели и состояние коллайдеров для восстановления после смерти.
    private void Awake()
    {
        health = GetComponent<Health>();
        movement = GetComponent<RelativeMovement>();
        if (visualRoot != null)
        {
            visualRestPosition = visualRoot.localPosition;
            visualRestRotation = visualRoot.localRotation;
        }
        if (pieces != null)
            foreach (Transform piece in pieces)
            {
                if (piece == null) continue;
                if (piece.name == "Head") { head = piece; headRest = piece.localRotation; }
                if (piece.name == "Hat") { hat = piece; hatRest = piece.localRotation; hatRestPosition = piece.localPosition; }
                if (piece.name == "Staff")
                {
                    staff = piece; staffRest = piece.localRotation;
                    staffRestPosition = piece.localPosition;
                }
            }
        // Хитбоксы принадлежат частям модели и наследуют парение, наклон и поворот головы.
        // Посох не является частью тела; капсула контроллера обслуживает только передвижение.
        if (pieces != null)
            foreach (Transform piece in pieces)
            {
                if (piece == null || (piece.name != "Body" && piece.name != "Head" && piece.name != "Hat")) continue;
                foreach (var filter in piece.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    var hitbox = filter.gameObject.AddComponent<BoxCollider>();
                    hitbox.center = filter.sharedMesh.bounds.center;
                    hitbox.size = filter.sharedMesh.bounds.size;
                    hitbox.isTrigger = true;
                    damageColliders.Add(hitbox);
                    if (piece.name == "Head" || piece.name == "Hat") headColliders.Add(hitbox);
                }
            }
        if (staff != null)
        {
            Bounds bounds = FindLocalMeshBounds(staff);
            staffGripLocal = bounds.center;
            staffHalfLength = bounds.extents.y * Mathf.Abs(staff.lossyScale.y);
            staffThickness = Mathf.Max(bounds.extents.x * Mathf.Abs(staff.lossyScale.x),
                bounds.extents.z * Mathf.Abs(staff.lossyScale.z));
        }
        if (head != null) headCenterLocal = FindLocalMeshBounds(head).center;
        bodyColliders = GetComponentsInChildren<Collider>(true);
        colliderDefaults = new bool[bodyColliders.Length];
        for (int i = 0; i < bodyColliders.Length; i++) colliderDefaults[i] = bodyColliders[i].enabled;
    }
    // реагируем на изменение состояния смерти: скрываем модель, отключаем коллайдеры и создаём обломки.
    private void Update()
    {
        if (isServer) remoteSprinting = movement != null && movement.IsSprinting;
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
        if ((!isClient && !isServer) || visualRoot == null) return;
        if (health != null && health.IsDead) { poseReady = false; castGesture = WizardCastGesture.None; return; }
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
        if (reset && poseReady) castGesture = WizardCastGesture.None;
        Vector3 presentationOffset = isLocalPlayer && movement != null ? movement.PresentationOffset : Vector3.zero;
        Vector3 presentationPosition = transform.position + presentationOffset;
        Vector3 velocity = reset || Time.deltaTime <= 0 ? Vector3.zero :
            Vector3.ProjectOnPlane(presentationPosition - lastPresentationPosition, Vector3.up) / Time.deltaTime;
        AnimateLocomotion(velocity, isOwned && movement != null ? movement.IsSprinting : remoteSprinting,
            presentationOffset, Time.deltaTime, reset);
        lastPresentationPosition = presentationPosition;
        // после возрождения или резкого переноса сразу восстанавливаем позу, не тянем посох через арену.
        if (reset) headYaw = bodyYaw;
        headYaw = Mathf.LerpAngle(headYaw, targetYaw, 1 - Mathf.Exp(-headTurnSpeed * Time.deltaTime));
        float offset = Mathf.Clamp(Mathf.DeltaAngle(bodyYaw, headYaw), -maxHeadYaw, maxHeadYaw);
        headYaw = bodyYaw + offset;
        Quaternion turn = Quaternion.AngleAxis(offset, Vector3.up);
        if (head != null) head.localRotation = turn * headRest;
        if (hat != null)
        {
            hat.localRotation = turn * hatRest;
            Vector3 target = hat.parent != null ? hat.parent.TransformPoint(hatRestPosition) : hatRestPosition;
            hatHeight = WizardCastMotion.FollowHatHeight(hatHeight, target.y, Time.deltaTime, reset,
                hatHeightFollowSpeed, hatRiseLag, hatFallLag);
            hat.position = new Vector3(target.x, hatHeight, target.z);
        }
        if (staff != null && staff.parent != null)
        {
            Vector3 target = staff.parent.TransformPoint(staffRestPosition);
            target += Vector3.up * (Mathf.Sin((float)(NetworkTime.time * 2.5) + netId) * staffBobHeight);
            Quaternion rotation = staff.parent.rotation * staffRest;
            float blend = 1 - Mathf.Exp(-staffFollowSpeed * Time.deltaTime);
            staffPosition = reset ? target : Vector3.Lerp(staffPosition, target, blend);
            staffPosition = target + Vector3.ClampMagnitude(staffPosition - target, maxStaffLag);
            staffRotation = reset ? rotation : Quaternion.Slerp(staffRotation, rotation, blend);
            Pose desired = SampleStaffCast(staffPosition, staffRotation);
            ApplyStaffPose(desired, presentationPosition, reset, Time.deltaTime);
        }
        lastPosition = transform.position;
        poseReady = true;
        // Sweep/Overlap выполняются и между физическими шагами: сервер должен видеть последнюю позу.
        if (isServer) Physics.SyncTransforms();
    }

    // У импортированного посоха pivot находится у ног мага: находим середину его реальной геометрии.
    private static Bounds FindLocalMeshBounds(Transform root)
    {
        Bounds bounds = default;
        bool found = false;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Bounds mesh = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 point = root.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                else bounds.Encapsulate(point);
            }
        }
        return bounds;
    }

    [Server]
    public void ServerPlayCast(WizardCastGesture gesture, Vector3 target, bool atPoint, float duration = 0)
    {
        if (gesture == WizardCastGesture.None || (health != null && health.IsDead)) return;
        byte variant = (byte)Random.Range(0, 6);
        double startedAt = NetworkTime.time;
        duration = (duration > 0 ? duration : WizardCastMotion.Duration(gesture)) / castPlaybackSpeed;
        BeginCast(gesture, target, atPoint, variant, startedAt, duration);
        RpcPlayCast(gesture, target, atPoint, variant, startedAt, duration);
    }

    [ClientRpc]
    private void RpcPlayCast(WizardCastGesture gesture, Vector3 target, bool atPoint, byte variant, double startedAt, float duration)
    {
        if (!isServer) BeginCast(gesture, target, atPoint, variant, startedAt, duration);
    }

    private void BeginCast(WizardCastGesture gesture, Vector3 target, bool atPoint, byte variant, double startedAt, float duration)
    {
        if (staff == null || (health != null && health.IsDead) || NetworkTime.time >= startedAt + duration) return;
        castGesture = gesture;
        staffAvoidBody = gesture == WizardCastGesture.Shot || gesture == WizardCastGesture.Lob;
        castTarget = target;
        castAtPoint = atPoint;
        castDirection = atPoint ? target - (transform.position + Vector3.up * .6f) : target;
        if (castDirection.sqrMagnitude < .001f) castDirection = transform.forward;
        castDirection.Normalize();
        Vector3 flat = Vector3.ProjectOnPlane(castDirection, Vector3.up);
        castFrame = Quaternion.LookRotation(flat.sqrMagnitude > .001f ? flat : transform.forward, Vector3.up);
        castVariant = variant;
        castStartedAt = startedAt;
        castDuration = duration;
    }

    private Pose SampleStaffCast(Vector3 restPosition, Quaternion restRotation)
    {
        Vector3 scaledGrip = Vector3.Scale(staffGripLocal, staff.lossyScale);
        Pose idle = new Pose(restPosition + restRotation * scaledGrip, restRotation);
        if (castGesture == WizardCastGesture.None) return idle;
        float elapsed = Mathf.Max(0, (float)(NetworkTime.time - castStartedAt));
        if (elapsed >= castDuration) { castGesture = WizardCastGesture.None; return idle; }
        Quaternion inverseFrame = Quaternion.Inverse(castFrame);
        Vector3 restGrip = inverseFrame * (idle.position - transform.position);
        Vector3 aim = castAtPoint ? castTarget - (transform.position + Vector3.up * .6f) : castDirection;
        Vector3 overhead = head != null ? head.TransformPoint(headCenterLocal) + Vector3.up * 1.1f :
            transform.position + Vector3.up * 2.05f;
        Pose pose = WizardCastMotion.Sample(castGesture, elapsed / castDuration, castVariant,
            restGrip, inverseFrame * restRotation, inverseFrame * aim, inverseFrame * (overhead - transform.position));
        return new Pose(transform.position + castFrame * pose.position, castFrame * pose.rotation);
    }

    // Один непрерывный фильтр работает и во время жеста, и после его конца: смена состояния
    // не подменяет текущую позу исходной и не обнуляет скорость при повторном касте.
    private void ApplyStaffPose(Pose desired, Vector3 anchor, bool reset, float dt)
    {
        // после жеста возвращаем обычную инерцию; прежний зазор вокруг тела не должен фиксировать посох навсегда.
        if (castGesture == WizardCastGesture.None) staffAvoidBody = false;
        float elapsed = Mathf.Max(0, (float)(NetworkTime.time - castStartedAt));
        float returning = castGesture == WizardCastGesture.None ? 1 : Mathf.SmoothStep(0, 1,
            Mathf.InverseLerp(.65f, 1, elapsed / castDuration));
        float smoothTime = Mathf.Lerp(castBlendIn, staffReturnSmoothTime, returning);
        if (reset)
        {
            displayedGripOffset = desired.position - anchor;
            displayedGripVelocity = Vector3.zero;
            displayedStaffRotation = desired.rotation;
            staffAvoidBody = castGesture == WizardCastGesture.Shot || castGesture == WizardCastGesture.Lob;
        }
        else
        {
            displayedGripOffset = Vector3.SmoothDamp(displayedGripOffset, desired.position - anchor,
                ref displayedGripVelocity, smoothTime, Mathf.Infinity, dt);
            displayedStaffRotation = Quaternion.Slerp(displayedStaffRotation, desired.rotation,
                1 - Mathf.Exp(-2f * dt / smoothTime));
        }
        Pose display = new Pose(displayedGripOffset, displayedStaffRotation);
        if (staffAvoidBody)
        {
            display = WizardCastMotion.KeepOutsideBody(display, staffHalfLength, StaffClearance(anchor));
            displayedGripOffset = display.position;
        }
        Vector3 scaledGrip = Vector3.Scale(staffGripLocal, staff.lossyScale);
        staff.SetPositionAndRotation(anchor + display.position - display.rotation * scaledGrip, display.rotation);
    }

    private float StaffClearance(Vector3 anchor)
    {
        float radius = .55f;
        // Учитываем наклон мага при беге и широкие поля шляпы. Читаем локальную форму,
        // поэтому расчёт не зависит от того, успела ли физика обновить мировые bounds.
        foreach (Collider collider in damageColliders)
        {
            if (!(collider is BoxCollider box)) continue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 local = box.center + Vector3.Scale(box.size * .5f,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 point = box.transform.TransformPoint(local) - anchor;
                radius = Mathf.Max(radius, new Vector2(point.x, point.z).magnitude);
            }
        }
        return radius + staffThickness + .05f;
    }
    // Анимируем только модель; направление берём из фактического движения, включая движение боком и назад.
    private void AnimateLocomotion(Vector3 velocity, bool sprinting, Vector3 presentationOffset, float dt, bool reset)
    {
        if (reset)
        {
            movingBlend = sprintBlend = 0;
            leanDirection = Vector3.zero;
            hoverPhase = netId % 32 * .37f;
        }
        float blend = 1 - Mathf.Exp(-poseBlendSpeed * dt);
        float moving = Mathf.InverseLerp(.15f, 1.5f, velocity.magnitude);
        movingBlend = Mathf.Lerp(movingBlend, moving, blend);
        sprintBlend = Mathf.Lerp(sprintBlend, sprinting ? moving : 0, blend);
        Vector3 direction = velocity.sqrMagnitude > .0225f ? velocity.normalized : Vector3.zero;
        leanDirection = Vector3.Lerp(leanDirection, direction, blend);
        hoverPhase = Mathf.Repeat(hoverPhase + dt * Mathf.Lerp(2f, 3.8f, movingBlend), Mathf.PI * 2);
        float bob = Mathf.Sin(hoverPhase) * Mathf.Lerp(idleBobHeight, movingBobHeight, movingBlend);
        Vector3 worldOffset = presentationOffset + Vector3.up * (movingHoverHeight * movingBlend + bob);
        visualRoot.localPosition = visualRestPosition + (visualRoot.parent != null
            ? visualRoot.parent.InverseTransformVector(worldOffset) : worldOffset);
        // Мировое направление переводим в пространство родителя, чтобы наклон не зависел от взгляда.
        Vector3 localDirection = visualRoot.parent != null
            ? visualRoot.parent.InverseTransformDirection(leanDirection) : leanDirection;
        Quaternion lean = localDirection.sqrMagnitude > .000001f
            ? Quaternion.AngleAxis(sprintLeanAngle * sprintBlend * localDirection.magnitude,
                Vector3.Cross(Vector3.up, localDirection)) : Quaternion.identity;
        Quaternion sway = Quaternion.Euler(Mathf.Sin(hoverPhase + .6f) * idleSwayAngle * .5f,
            0, Mathf.Sin(hoverPhase) * idleSwayAngle * (1 - sprintBlend));
        visualRoot.localRotation = lean * sway * visualRestRotation;
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
            foreach (Collider copiedHitbox in fragment.GetComponentsInChildren<Collider>(true))
            {
                copiedHitbox.enabled = false;
                Destroy(copiedHitbox);
            }
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
