using System.Collections.Generic;
using Mirror;
using UnityEngine;

// принимает нажатия от владельца; сервер выбирает заклинание, проверяет перезарядку и создаёт эффект.
public class PlayerNetworkCaster : NetworkBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Transform firePoint;
    [SyncVar] private ElementLoadout loadout;
    [SyncVar] private bool loadoutReady;
    private SpellManager spells;
    private Health health;
    private readonly List<MagicElement> serverInput = new List<MagicElement>(3);
    private readonly Dictionary<Spell, double> serverCooldowns = new Dictionary<Spell, double>();
    private readonly Dictionary<Spell, double> clientCooldowns = new Dictionary<Spell, double>();
    private double lastInputTime;
    private bool resolvingCommand;
    private Ray commandAimRay;
    private Vector3 commandMoveDirection;
    public ElementLoadout Loadout => loadout;
    public bool LoadoutReady => loadoutReady;
    // получаем каталог заклинаний и здоровье этого персонажа.
    private void Awake()
    {
        spells = GetComponent<SpellManager>();
        health = GetComponent<Health>();
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
        loadout = requested.IsValid ? requested : ElementLoadout.Default;
        loadoutReady = true;
    }
    // отправляем номер слота вместе с лучом прицела и направлением движения для рывка.
    public void SubmitElement(int slot)
    {
        if (!isLocalPlayer || !loadoutReady || PlayerGameUI.InputBlocked) return;
        if (playerCamera == null) return;
        Ray ray=playerCamera.ViewportPointToRay(new Vector3(.5f,.5f));
        var movement = GetComponent<RelativeMovement>();
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
        // клиент передаёт намерение, а не готовое заклинание: сервер сам проверяет слот и допустимость прицела.
        if (!loadoutReady || spells == null || slot < 0 || slot > 2 ||
            (health != null && health.IsDead) || !ValidDirection(viewDirection) ||
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
        // распознанный рецепт расходует комбинацию даже при ещё действующей перезарядке.
        serverInput.Clear();
        Spell spell = spells.GetSpell(index);
        if (serverCooldowns.TryGetValue(spell, out double readyAt) && now < readyAt) return;
        commandAimRay=new Ray(viewOrigin,viewDirection.normalized);
        commandMoveDirection=Vector3.ProjectOnPlane(moveDirection,Vector3.up).normalized;
        resolvingCommand=true;
        bool activated;
        // контекст прицела доступен только во время этого применения и очищается даже при исключении.
        try { activated=spell.ActivateServer(this,ResolveAimDirection(commandAimRay)); }
        finally { resolvingCommand=false; }
        if (!activated) return;
        // клиент получает абсолютное время окончания, чтобы таймер не зависел от задержки доставки сообщения.
        readyAt = now + spell.Cooldown;
        serverCooldowns[spell] = readyAt;
        TargetSetCooldown(index, readyAt);
    }
    // сервер очищает комбинацию после смерти или истечения времени ввода.
    private void Update()
    {
        if (isServer && ((health != null && health.IsDead) || NetworkTime.time - lastInputTime >= InputComboTracker.InputTimeout)) serverInput.Clear();
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
        if (spell != null) clientCooldowns[spell] = readyAt;
    }
    // вычисляем оставшееся время по сетевым часам, не допуская отрицательного результата.
    public double RemainingCooldown(Spell spell) => clientCooldowns.TryGetValue(spell, out double readyAt) ?
        System.Math.Max(0, readyAt - NetworkTime.time) : 0;
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
                if (Physics.OverlapBox(position + Vector3.up * 1.35f, new Vector3(1.9f,1.35f,.275f), Quaternion.LookRotation(flat), ~(1<<2), QueryTriggerInteraction.Ignore).Length > 0) return false;
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
        return true;
    }
    // сервер применяет щит либо создаёт снаряд, наземную зону или эффект вокруг мага.
    [Server]
    public bool CastElemental(ElementalSpell spell, Vector3 direction)
    {
        if (spell == null || !ValidDirection(direction)) return false;
        if (spell.mode == ElementalCastMode.Shield)
        {
            if (health == null) return false;
            health.GrantShield(spell.shieldAmount, spell.duration);
            return true;
        }
        if (spell.effectPrefab == null || firePoint == null) return false;
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (flat.sqrMagnitude < 0.01f) flat = transform.forward;
        Vector3 position = ShotOrigin();
        if (spell.mode == ElementalCastMode.GroundZone)
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
        body.linearVelocity = spell.mode == ElementalCastMode.Bolt || spell.mode == ElementalCastMode.Tornado ? direction.normalized * spell.speed : Vector3.zero;
        foreach (Collider source in GetComponentsInChildren<Collider>())
            foreach (Collider target in effect.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(source, target);
        NetworkServer.Spawn(effect);
        return true;
    }
    // создаём обычный сетевой снаряд, назначаем владельца и скорость до отправки клиентам.
    [Server]
    public bool SpawnProjectile(GameObject prefab, float speed, Vector3 direction)
    {
        if (prefab == null || firePoint == null || !ValidDirection(direction) ||
            prefab.GetComponent<Rigidbody>() == null || prefab.GetComponent<NetworkIdentity>() == null) return false;
        direction.Normalize();
        GameObject projectile = Instantiate(prefab, ShotOrigin(), Quaternion.LookRotation(direction));
        if (projectile.TryGetComponent<FireballProjectile>(out var fire)) fire.ownerId = netId;
        if (projectile.TryGetComponent<WindFlowProjectile>(out var wind)) wind.ownerId = netId;
        // исключаем столкновения со всеми коллайдерами заклинателя, включая дочерние.
        foreach (Collider source in GetComponentsInChildren<Collider>())
            foreach (Collider target in projectile.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(source, target);
        var body=projectile.GetComponent<Rigidbody>();body.useGravity=false;body.linearDamping=0;body.linearVelocity = direction * speed;
        NetworkServer.Spawn(projectile);
        return true;
    }
}

