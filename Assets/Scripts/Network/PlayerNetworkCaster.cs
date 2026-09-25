using System.Collections.Generic;
using Mirror;
using UnityEngine;

// принимает нажатия от владельца; сервер выбирает заклинание, проверяет перезарядку и создаёт эффект.
public partial class PlayerNetworkCaster : NetworkBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Transform firePoint;
    [SyncVar] private ElementLoadout loadout;
    [SyncVar] private bool loadoutReady;
    private SpellManager spells;
    private Health health;
    private RelativeMovement movementController;
    private WizardAppearance appearance;
    private PlayerUltimate ultimate;
    public bool UltimateBlocksSpells => ultimate != null && ultimate.BlocksSpells;
    private readonly List<MagicElement> serverInput = new List<MagicElement>(3);
    private readonly Dictionary<Spell, double> serverCooldowns = new Dictionary<Spell, double>();
    private readonly Dictionary<Spell, double> clientCooldowns = new Dictionary<Spell, double>();
    private readonly Dictionary<Spell, uint> cooldownConfirmations = new Dictionary<Spell, uint>();
    private double lastInputTime;
    private uint localInputNumber, serverInputNumber;
    private readonly Dictionary<uint, ulong> submittedCombos = new Dictionary<uint, ulong>();
    private sealed class CastPreview
    {
        public GameObject root, visual;
        public Vector3 velocity;
        public Spell spell;
        public double previousCooldown, predictedCooldown;
        public uint confirmation;
        public float expiresAt;
    }
    private readonly Dictionary<uint, CastPreview> previews = new Dictionary<uint, CastPreview>();
    private readonly List<uint> expiredPreviews = new List<uint>();
    private bool resolvingCommand;
    private Ray commandAimRay;
    private Vector3 commandMoveDirection;
    public ElementLoadout Loadout => loadout;
    public Camera ViewCamera => playerCamera;
    public bool LoadoutReady => loadoutReady;
    // получаем каталог заклинаний и здоровье этого персонажа.
    private void Awake()
    {
        spells = GetComponent<SpellManager>();
        health = GetComponent<Health>();
        movementController = GetComponent<RelativeMovement>();
        appearance = GetComponent<WizardAppearance>();
        ultimate = GetComponent<PlayerUltimate>();
    }
    // отправляем серверу выбранный перед матчем набор стихий либо стандартный набор.
    public override void OnStartLocalPlayer()
    {
        CmdSetLoadout(LocalPlayerSettings.Instance != null ?
            LocalPlayerSettings.Instance.Loadout : ElementLoadout.Default);
    }
    // сервер принимает набор только один раз и заменяет некорректный выбор стандартным.
    [Command]
    private void CmdSetLoadout(ElementLoadout requested)
    {
        // фиксируем набор на всю сетевую жизнь персонажа, включая возрождения.
        if (loadoutReady) return;
        loadout = ShopCatalog.Allows(NetManager.Room?.RoomAuth.ProfileFor(connectionToClient), requested) ? requested : ElementLoadout.Default;
        loadoutReady = true;
    }
    // отправляем номер слота вместе с лучом прицела и направлением движения для рывка.
    public void SubmitElement(int slot)
    {
        if (!isLocalPlayer || !loadoutReady || PlayerGameUI.InputBlocked || UltimateBlocksSpells || (movementController != null && movementController.IsStunned)) return;
        if (playerCamera == null) return;
        CancelLocalAreaAim();
        Ray ray=playerCamera.ViewportPointToRay(new Vector3(.5f,.5f));
        var movement = GetComponent<RelativeMovement>();
        uint inputNumber = ++localInputNumber;
        var tracker = GetComponent<InputComboTracker>();
        if (tracker != null) submittedCombos[inputNumber] = tracker.Revision;
        if (!isServer) PreviewCast(inputNumber, ray);
        CmdSubmitElement(slot, ray.origin, ray.direction, movement != null ? movement.PlanarInputDirection : Vector3.zero);
    }
    // переводим центральный луч камеры в направление от фактической точки выпуска снаряда.
    public Vector3 AimDirection()
    {
        if (playerCamera == null || firePoint == null) return transform.forward;
        return ResolveAimDirection(playerCamera.ViewportPointToRay(new Vector3(.5f,.5f)));
    }
    // выбираем ближайшее препятствие, пропуская собственную модель, снаряды и мёртвых игроков.
    private bool FirstAimHit(Ray ray,float range,out RaycastHit hit)
    {
        var hits=Physics.RaycastAll(ray,range,~(1<<2),QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
        foreach(var candidate in hits)
        {
            var collider=candidate.collider;
            if(collider.transform.IsChildOf(transform.root)||collider.GetComponentInParent<ElementalEffect>()!=null||
                collider.GetComponentInParent<FireballProjectile>()!=null||collider.GetComponentInParent<WindFlowProjectile>()!=null)continue;
            var target=collider.GetComponentInParent<Health>();if(target!=null&&target.IsDead)continue;
            hit=candidate;return true;
        }
        hit=default;return false;
    }
    // проверяем отрезок до точки выпуска, чтобы снаряд не появился по другую сторону стены.
    public Vector3 ShotOrigin()
    {
        if(firePoint==null)return transform.position;
        Vector3 anchor=transform.position+Vector3.up*.6f;
        Vector3 delta=firePoint.position-anchor;
        if(delta.sqrMagnitude>.0001f&&FirstAimHit(new Ray(anchor,delta.normalized),delta.magnitude,out var wall))
            return wall.point-delta.normalized*.02f;
        return firePoint.position;
    }
    // направляем снаряд к точке под прицелом, учитывая смещение камеры относительно мага.
    public Vector3 ResolveAimDirection(Ray ray)
    {
        Vector3 point=FirstAimHit(ray,100,out var hit)?hit.point:ray.GetPoint(100);
        return (point-ShotOrigin()).normalized;
    }
    // разрешаем наземный эффект только на доступной сверху поверхности в пределах восемнадцати метров.
    public bool TryGroundTarget(Ray ray,out Vector3 point)
    {
        point=default;
        if(!FirstAimHit(ray,100,out var hit)||hit.normal.y<.6f||hit.collider.GetComponentInParent<Health>()!=null)return false;
        Vector3 origin=ShotOrigin();Vector3 delta=hit.point-origin;
        if(delta.sqrMagnitude>18*18)return false;
        if(delta.sqrMagnitude>.0001f&&FirstAimHit(new Ray(origin,delta.normalized),delta.magnitude+.01f,out var blocker)&&Vector3.Distance(blocker.point,hit.point)>.05f)return false;
        point=hit.point;return true;
    }
    // сервер проверяет входные данные, собирает три стихии и применяет найденный рецепт.
    // перезарядка начинается только после успешного создания эффекта.
    [Command]
    private void CmdSubmitElement(int slot, Vector3 viewOrigin, Vector3 viewDirection, Vector3 moveDirection)
    {
        uint inputNumber = ++serverInputNumber;
        pendingServerSpell = null;
        bool accepted = false;
        try
        {
            // клиент передаёт намерение, а не готовое заклинание: сервер сам проверяет слот и допустимость прицела.
            if (!NetManager.CombatAllowed || UltimateBlocksSpells || !loadoutReady || spells == null || slot < 0 || slot > 2 ||
                (health != null && health.IsDead) || (movementController != null && movementController.IsStunned) || !ValidDirection(viewDirection) ||
                !Finite(viewOrigin) || !Finite(moveDirection) || moveDirection.sqrMagnitude > 1.1f || (viewOrigin-transform.position).sqrMagnitude>100)
            {
                serverInput.Clear();
                return;
            }
            double now = NetworkTime.time;
            // сервер ведёт собственную историю нажатий; локальный буфер нужен только для интерфейса.
            if (now - lastInputTime >= InputComboTracker.InputTimeout) serverInput.Clear();
            lastInputTime = now;
            if (serverInput.Count == 3) serverInput.RemoveAt(0);
            serverInput.Add(loadout.Get(slot));
            int index = spells.FindSpell(serverInput, loadout);
            if (index < 0) return;
            // совпадение с рецептом ещё не расходует ввод: перезарядка или недоступная цель сохраняют окно.
            Spell spell = spells.GetSpell(index);
            if (ServerRemainingCooldown(spell, now) > 0)
            {
                // при отказе возвращаем действительный срок: карточка не должна оставаться готовой раньше сервера.
                TargetSetCooldown(index, serverCooldowns[spell]);
                return;
            }
            if (RequiresAreaConfirmation(spell))
            {
                pendingServerSpell = spell;
                pendingServerToken = inputNumber;
                pendingServerUntil = now + 5;
                serverInput.Clear();
                TargetArmArea(index, inputNumber, pendingServerUntil);
                return;
            }
            commandAimRay=new Ray(viewOrigin,viewDirection.normalized);
            commandMoveDirection=Vector3.ProjectOnPlane(moveDirection,Vector3.up).normalized;
            resolvingCommand=true;
            bool activated;
            // контекст прицела доступен только во время этого применения и очищается даже при исключении.
            try { activated=spell.ActivateServer(this,ResolveAimDirection(commandAimRay)); }
            finally { resolvingCommand=false; }
            if (!activated) return;
            // только успешно применённое заклинание начинает следующую комбинацию.
            serverInput.Clear();
            // клиент получает абсолютное время окончания, чтобы таймер не зависел от задержки доставки сообщения.
            double readyAt = now + spell.Cooldown;
            serverCooldowns[spell] = readyAt;
            TargetSetCooldown(index, readyAt);
            accepted = true;
        }
        finally
        {
            // сообщение идёт после сетевого создания: удаляем предварительную графику или отменяем отказанный выстрел.
            TargetResolvePreview(inputNumber, accepted);
        }
    }
    // сервер очищает комбинацию после смерти или истечения времени ввода.
    private void Update()
    {
        UpdatePreviews();
        UpdateAreaAim();
        UpdateChannelAim();
        if (isServer && ((health != null && health.IsDead) || NetworkTime.time - lastInputTime >= InputComboTracker.InputTimeout)) serverInput.Clear();
    }
    // локальная копия содержит только графику; у неё нет физики, сетевых действий и расчёта попаданий.
    private void PreviewCast(uint inputNumber, Ray ray)
    {
        var tracker = GetComponent<InputComboTracker>();
        if (tracker == null || spells == null || (health != null && health.IsDead)) return;
        int index = spells.FindSpell(tracker.History, loadout);
        Spell spell = spells.GetSpell(index);
        if (spell == null || RemainingCooldown(spell) > 0 || previews.Count >= 16) return;
        if (RequiresAreaConfirmation(spell)) return;
        GameObject prefab = null;
        float speed = 0;
        Color tint = new Color(.7f,.85f,1f);
        if (spell is FireBall fire) { prefab = fire.PreviewPrefab; speed = fire.ProjectileSpeed; tint = new Color(1,.3f,.04f); }
        else if (spell is WindFlow wind) { prefab = wind.PreviewPrefab; speed = wind.ProjectileSpeed; }
        else if (spell is ElementalSpell elemental)
        {
            tint = elemental.tint;
            if (elemental.mode == ElementalCastMode.Bolt) { prefab = elemental.effectPrefab; speed = elemental.speed; }
        }
        else if (spell is TacticalSpell tactical) tint = tactical.tint;
        Vector3 previewPosition = ShotOrigin(), previewDirection = ResolveAimDirection(ray);
        if (prefab != null && !TryProjectileLaunch(prefab, ray, previewDirection, out previewPosition, out previewDirection)) return;
        var preview = new CastPreview { spell = spell, expiresAt = Time.unscaledTime + 2f };
        clientCooldowns.TryGetValue(spell, out preview.previousCooldown);
        cooldownConfirmations.TryGetValue(spell, out preview.confirmation);
        preview.predictedCooldown = NetworkTime.time + spell.Cooldown;
        clientCooldowns[spell] = preview.predictedCooldown;
        previews.Add(inputNumber, preview);
        if (prefab == null)
        {
            // для областей и усилений подтверждаем нажатие короткой вспышкой у посоха; сам эффект создаёт сервер.
            ElementalVisual.Burst(ShotOrigin(), tint, .35f);
            return;
        }
        preview.root = new GameObject("Local cast preview");
        preview.root.SetActive(false);
        preview.visual = Instantiate(prefab, previewPosition, Quaternion.LookRotation(previewDirection), preview.root.transform);
        foreach (var behaviour in preview.visual.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = behaviour is SpellVfx || behaviour is ElementalVisual;
        foreach (var collider in preview.visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var body in preview.visual.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }
        foreach (var child in preview.visual.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
        preview.velocity = preview.visual.transform.forward * speed;
        preview.root.SetActive(true);
    }
    // ведём только предварительную графику и прекращаем её у стены без фиктивного урона или взрыва.
    private void UpdatePreviews()
    {
        if (previews.Count == 0) return;
        expiredPreviews.Clear();
        foreach (var entry in previews)
        {
            var preview = entry.Value;
            if (!isLocalPlayer || (health != null && health.IsDead))
            {
                expiredPreviews.Add(entry.Key);
                continue;
            }
            // две секунды ограничивают только предварительную графику, а не серверную перезарядку.
            // сохраняем ожидание ответа, чтобы поздний отказ ещё мог вернуть прежний таймер.
            if (Time.unscaledTime >= preview.expiresAt)
            {
                if (preview.root != null) Destroy(preview.root);
                preview.root = preview.visual = null;
                if (NetworkTime.time >= preview.predictedCooldown) expiredPreviews.Add(entry.Key);
                continue;
            }
            if (preview.visual == null || !preview.visual.activeSelf) continue;
            Vector3 delta = preview.velocity * Time.deltaTime;
            if (delta.sqrMagnitude > 0 && FirstAimHit(new Ray(preview.visual.transform.position, delta.normalized), delta.magnitude, out _))
                preview.visual.SetActive(false);
            else preview.visual.transform.position += delta;
        }
        foreach (uint id in expiredPreviews) FinishPreview(id, true);
    }
    // серверное подтверждение заменяет предварительную графику настоящим объектом, отказ возвращает таймер.
    [TargetRpc]
    private void TargetResolvePreview(uint inputNumber, bool accepted)
    {
        // запоздалый ответ не должен стирать нажатия, сделанные после подтверждённого заклинания.
        if (submittedCombos.TryGetValue(inputNumber, out ulong revision))
        {
            if (accepted) GetComponent<InputComboTracker>()?.ClearThrough(revision);
            submittedCombos.Remove(inputNumber);
        }
        FinishPreview(inputNumber, accepted);
    }
    private void FinishPreview(uint inputNumber, bool accepted)
    {
        if (!previews.TryGetValue(inputNumber, out var preview)) return;
        if (preview.root != null) Destroy(preview.root);
        cooldownConfirmations.TryGetValue(preview.spell, out uint confirmation);
        if (!accepted && confirmation == preview.confirmation &&
            clientCooldowns.TryGetValue(preview.spell, out double current) && current == preview.predictedCooldown)
            clientCooldowns[preview.spell] = preview.previousCooldown;
        previews.Remove(inputNumber);
    }
    // удаляем несетевые копии при выходе игрока, включая смену сцены и разрыв соединения.
    public override void OnStopClient()
    {
        CancelLocalAreaAim();
        foreach (var preview in previews.Values) if (preview.root != null) Destroy(preview.root);
        previews.Clear();
        submittedCombos.Clear();
        base.OnStopClient();
    }
    // отбрасываем нечисловые, бесконечные, почти нулевые и чрезмерно большие направления.
    private static bool ValidDirection(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z) &&
            value.sqrMagnitude > 0.001f && value.sqrMagnitude < 100f;
    }
    // проверяем, что все координаты являются конечными числами.
    private static bool Finite(Vector3 value)=>!float.IsNaN(value.x)&&!float.IsNaN(value.y)&&!float.IsNaN(value.z)&&!float.IsInfinity(value.x)&&!float.IsInfinity(value.y)&&!float.IsInfinity(value.z);
    // передаём владельцу серверное время окончания перезарядки для отображения в интерфейсе.
    [TargetRpc]
    private void TargetSetCooldown(int index, double readyAt)
    {
        Spell spell = spells.GetSpell(index);
        if (spell != null) ConfirmCooldown(spell, readyAt);
    }
    // подтверждённый срок нельзя отменить удалением старого предварительного эффекта, даже при равных числах.
    private void ConfirmCooldown(Spell spell, double readyAt)
    {
        clientCooldowns[spell] = readyAt;
        cooldownConfirmations.TryGetValue(spell, out uint version);
        cooldownConfirmations[spell] = version + 1;
    }
    private double ServerRemainingCooldown(Spell spell, double now) =>
        serverCooldowns.TryGetValue(spell, out double readyAt) ? System.Math.Max(0, readyAt - now) : 0;
    // вычисляем оставшееся время по сетевым часам, не допуская отрицательного результата.
    public double RemainingCooldown(Spell spell) => isServer ? ServerRemainingCooldown(spell, NetworkTime.time) :
        clientCooldowns.TryGetValue(spell, out double readyAt) ? System.Math.Max(0, readyAt - NetworkTime.time) : 0;
    // сервер проверяет место применения, выполняет рывок при необходимости и создаёт тактический объект.
    [Server]
    public bool CastTactical(TacticalSpell spell, Vector3 direction)
    {
        if (spell == null || spell.effectPrefab == null || !ValidDirection(direction) || health == null || health.IsDead) return false;
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (flat.sqrMagnitude < .01f) flat = transform.forward;
        var cc = GetComponent<CharacterController>();
        Vector3 feet = cc != null ? transform.TransformPoint(cc.center) - Vector3.up * cc.height * transform.lossyScale.y * .5f : transform.position - Vector3.up;
        Vector3 position = feet + Vector3.up * .05f;
        if (spell.kind == TacticalKind.StoneWall || spell.kind == TacticalKind.FireSeal || spell.kind == TacticalKind.GravityWell)
        {
            if (!TryGroundTarget(resolvingCommand ? commandAimRay : new Ray(ShotOrigin(), direction), out var ground))
            {
                if (spell.kind != TacticalKind.StoneWall) return false;
                // без выбранного пола ищем землю перед персонажем, а не в направлении взгляда вверх.
                flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                if (flat.sqrMagnitude < .01f) flat = Vector3.forward;
                Vector3 ahead = feet + flat * 2.5f;
                if (!TryGroundTarget(new Ray(ahead + Vector3.up * 2, Vector3.down), out ground) ||
                    Mathf.Abs(ground.y - feet.y) > 2) return false;
            }
            position = ground + Vector3.up * .05f;
            if (spell.kind == TacticalKind.StoneWall)
            {
                // не создаём твёрдую стену внутри игрока или существующей геометрии.
                if (Physics.OverlapBox(position + Vector3.up * 2.025f, new Vector3(1.9f,2.025f,.275f), Quaternion.LookRotation(flat), ~(1<<2), QueryTriggerInteraction.Ignore).Length > 0) return false;
            }
        }
        else if (spell.kind == TacticalKind.IceMirror) position = transform.position + flat * 1.5f;
        if (spell.kind == TacticalKind.SteamDash)
        {
            var movement = GetComponent<RelativeMovement>();
            if (movement == null) return false;
            movement.ServerDash(resolvingCommand && commandMoveDirection.sqrMagnitude > .01f ? commandMoveDirection : flat);
        }
        var effect = Instantiate(spell.effectPrefab,position,Quaternion.LookRotation(flat));
        effect.GetComponent<TacticalEffect>().ownerId = netId;
        NetworkServer.Spawn(effect);
        appearance?.ServerPlayCast(WizardCastMotion.ForSpell(spell), position, true);
        return true;
    }
    // сервер применяет щит либо создаёт снаряд, наземную зону или эффект вокруг мага.
    [Server]
    public bool CastElemental(ElementalSpell spell, Vector3 direction)
    {
        if (spell == null || !ValidDirection(direction)) return false;
        if (spell.name == "BoilingJet" && !emittingChannelDrop)
        {
            if (channelRoutine != null || spell.effectPrefab == null || firePoint == null) return false;
            channelRay = resolvingCommand ? commandAimRay : new Ray(ShotOrigin(), direction);
            channelRoutine = StartCoroutine(EmitBoilingDrops(spell));
            TargetChannelStarted();
            return true;
        }
        if (spell.mode == ElementalCastMode.Shield)
        {
            if (health == null) return false;
            health.GrantShield(spell.shieldAmount, spell.duration);
            appearance?.ServerPlayCast(WizardCastMotion.ForSpell(spell), direction, false);
            return true;
        }
        if (spell.effectPrefab == null || firePoint == null) return false;
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (flat.sqrMagnitude < 0.01f) flat = transform.forward;
        Vector3 position = ShotOrigin();
        if (spell.mode == ElementalCastMode.Bolt &&
            !TryProjectileLaunch(spell.effectPrefab, ProjectileAimRay(direction), direction, out position, out direction)) return false;
        if (spell.mode == ElementalCastMode.GroundZone && spell.name != "SmokeCloud")
        {
            Ray ray=resolvingCommand?commandAimRay:new Ray(ShotOrigin(),direction.normalized);
            if(!TryGroundTarget(ray,out var ground))return false;
            position=ground+Vector3.up*.25f;
        }
        if (spell.mode == ElementalCastMode.SelfBurst) position = transform.position;
        var effect = Instantiate(spell.effectPrefab, position, Quaternion.LookRotation(spell.mode==ElementalCastMode.Bolt?direction.normalized:flat));
        effect.GetComponent<ElementalEffect>().ownerId = netId;
        var body = effect.GetComponent<Rigidbody>();
        body.useGravity=false;body.linearDamping=0;
        body.linearVelocity = spell.mode == ElementalCastMode.Tornado ? flat * spell.speed :
            spell.mode == ElementalCastMode.Bolt ? direction.normalized * spell.speed : Vector3.zero;
        if (spell.name == "SmokeCloud")
            effect.GetComponent<ArcSmokeProjectile>().Launch(ResolveThrowVelocity(resolvingCommand ? commandAimRay : new Ray(ShotOrigin(), direction)));
        foreach (Collider source in GetComponentsInChildren<Collider>())
            foreach (Collider target in effect.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(source, target);
        NetworkServer.Spawn(effect);
        bool atPoint = spell.mode == ElementalCastMode.GroundZone;
        if (spell.mode == ElementalCastMode.Bolt && spell.name == "IceShard") ultimate?.ServerRecordShot(spell.effectPrefab, spell.speed);
        appearance?.ServerPlayCast(WizardCastMotion.ForSpell(spell), atPoint ? position : direction, atPoint);
        return true;
    }
    // проверяем цель на сервере; воздушный мост требует свободного объёма, но не опоры снизу.
    [Server] public bool CastAdvanced(AdvancedSpell spell, Vector3 direction)
    {
        if (spell == null || spell.effectPrefab == null || health == null || health.IsDead || !ValidDirection(direction)) return false;
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (flat.sqrMagnitude < .01f) flat = transform.forward;
        Vector3 position = ShotOrigin();
        Quaternion rotation = Quaternion.LookRotation(flat);
        if (spell.kind == AdvancedSpellKind.SteamLens)
        {
            position = ShotOrigin() + direction.normalized * 2;
            rotation = Quaternion.LookRotation(direction);
            if (FirstAimHit(new Ray(ShotOrigin(), direction.normalized), 2.1f, out _)) return false;
            foreach (var obstacle in Physics.OverlapBox(position, new Vector3(1.1f,1.1f,.1f), rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!obstacle.transform.IsChildOf(transform)) return false;
        }
        else if (spell.kind == AdvancedSpellKind.IceBridge)
        {
            var controller = GetComponent<CharacterController>();
            Vector3 feet = transform.TransformPoint(controller.center) - Vector3.up * controller.height * transform.lossyScale.y * .5f;
            position = feet + flat * (spell.bridgeSize.z * .5f + .5f) + Vector3.up * (.01f + spell.bridgeSize.y * .5f);
            foreach (var obstacle in Physics.OverlapBox(position, spell.bridgeSize * .5f, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!obstacle.transform.IsChildOf(transform)) return false;
        }
        else if (!spell.IsProjectile)
        {
            if (!TryGroundTarget(resolvingCommand ? commandAimRay : new Ray(ShotOrigin(), direction), out var ground)) return false;
            position = ground + Vector3.up * .05f;
        }
        var root = Instantiate(spell.effectPrefab, position, rotation);
        var effect = root.GetComponent<AdvancedSpellEffect>();
        effect.SetOwner(health);
        effect.velocity = direction.normalized * spell.projectileSpeed;
        if (spell.kind == AdvancedSpellKind.Meteor) movementController?.ApplySlow(.5f, spell.delay);
        NetworkServer.Spawn(root);
        var gesture = WizardCastMotion.ForSpell(spell);
        appearance?.ServerPlayCast(gesture, spell.IsProjectile ? direction : position, !spell.IsProjectile,
            spell.HasWarning ? Mathf.Max(WizardCastMotion.Duration(gesture), spell.delay) : 0);
        return true;
    }

    // создаём обычный сетевой снаряд, назначаем владельца и скорость до отправки клиентам.
    [Server]
    public bool SpawnProjectile(GameObject prefab, float speed, Vector3 direction)
    {
        if (prefab == null || firePoint == null || !ValidDirection(direction) ||
            prefab.GetComponent<Rigidbody>() == null || prefab.GetComponent<NetworkIdentity>() == null) return false;
        direction.Normalize();
        if (!TryProjectileLaunch(prefab, ProjectileAimRay(direction), direction, out var position, out direction)) return false;
        GameObject projectile = Instantiate(prefab, position, Quaternion.LookRotation(direction));
        if (projectile.TryGetComponent<FireballProjectile>(out var fire)) fire.ownerId = netId;
        if (projectile.TryGetComponent<WindFlowProjectile>(out var wind)) wind.ownerId = netId;
        // исключаем столкновения со всеми коллайдерами заклинателя, включая дочерние.
        foreach (Collider source in GetComponentsInChildren<Collider>())
            foreach (Collider target in projectile.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(source, target);
        var body=projectile.GetComponent<Rigidbody>();body.useGravity=false;body.linearDamping=0;body.linearVelocity = direction * speed;
        NetworkServer.Spawn(projectile);
        if (fire != null) ultimate?.ServerRecordShot(prefab, speed);
        appearance?.ServerPlayCast(WizardCastGesture.Shot, direction, false);
        return true;
    }
}

